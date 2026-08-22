using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Explicit, non-writing review of exact Nationality module/version IDs. Confirmation only updates
/// the revision-bound phone draft; Core Preview remains the sole legality, budget-after, diff, and
/// persistence authority.
/// </summary>
public sealed class CreationNationalityPreviewPage : NativePageBase
{
    private readonly CreationFoundationPhoneDraft _draft;
    private readonly string _moduleId;
    private readonly string? _versionId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private string? _localBlocker;

    internal CreationNationalityPreviewPage(
        RunnerSessionCoordinator coordinator,
        CreationFoundationPhoneDraft draft,
        string moduleId,
        string? versionId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _moduleId = string.IsNullOrWhiteSpace(moduleId)
            ? throw new ArgumentException("A typed Nationality module ID is required.", nameof(moduleId))
            : moduleId;
        _versionId = versionId;
        Title = "Review Nationality";
        AutomationId = "creation-nationality-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Explicit selection review"));
        _body.Add(NativeTheme.Title("Review Nationality"));

        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state
            || !_draft.Matches(state))
        {
            AddBlockers(load.Blockers.Count > 0
                ? load.Blockers
                : ["The Foundation authority changed. Return and reload before confirming."]);
            return;
        }

        CharacterCreationLegalOption? metatype = _draft.ResolveConfirmedMetatype(state);
        LifeModuleLegalOptionDto? module = CreationFoundationPhoneAuthority.ResolveUniqueModule(
            state,
            _moduleId);
        LifeModuleVersionProjectionDto? version = module is null
            ? null
            : CreationFoundationPhoneAuthority.ResolveUniqueVersion(module, _versionId);
        if (module is null
            || module.Versions.Count == 0 && !string.IsNullOrWhiteSpace(_versionId)
            || module.Versions.Count > 0 && version is null)
        {
            AddBlockers(["The exact module/version identity is no longer unique or available."]);
            return;
        }

        bool canReview = CreationFoundationPhoneAuthority.CanReviewSelection(
            state,
            module,
            version,
            metatype);
        bool evaluationCandidate = !module.IsEnabled || version is { IsEnabled: false };

        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {CreationNationalityViewText.ShortDigest(state.FoundationSnapshotDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-nationality-preview-binding";
        _body.Add(binding);
        AddBudget(state.LifeModuleBudget);
        AddSelection(module, version, evaluationCandidate, canReview);
        AddRequirements(module.Requirements.Concat(version?.Requirements ?? []).ToArray());
        AddEffects(module.Effects.Concat(version?.Effects ?? []).ToArray());
        AddFollowUps(module.FollowUps.Concat(version?.FollowUps ?? []).ToArray());

        if (!string.IsNullOrWhiteSpace(_localBlocker))
            AddBlockers([_localBlocker]);
        if (!canReview)
        {
            AddBlockers(module.AuthorityBlockers
                .Concat(version?.AuthorityBlockers ?? [])
                .Append("The exact candidate is not reviewable under the current authority.")
                .ToArray());
        }

        Button confirm = NativeTheme.PrimaryButton("Use this Nationality");
        confirm.AutomationId = "creation-nationality-confirm";
        confirm.IsEnabled = canReview;
        confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
        _body.Add(confirm);

        Label noWrite = NativeTheme.Body(
            "No character data is written here. This only confirms typed module/version IDs in "
            + "the phone draft. Core Preview must still evaluate legality, follow-ups, the exact "
            + "remaining budget, and every resulting effect before any Foundation confirmation.",
            NativeTheme.Muted);
        noWrite.AutomationId = "creation-nationality-no-write";
        _body.Add(noWrite);
    }

    private void AddBudget(CharacterCreationBudgetState budget)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Current authoritative budget"));
        card.Add(NativeTheme.Metric("Budget ID", budget.BudgetId));
        card.Add(NativeTheme.Metric("Total", CreationNationalityViewText.FormatBudget(budget.Total, budget.Unit)));
        card.Add(NativeTheme.Metric("Used", CreationNationalityViewText.FormatBudget(budget.Used, budget.Unit)));
        card.Add(NativeTheme.Metric("Remaining", CreationNationalityViewText.FormatBudget(budget.Remaining, budget.Unit)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact" : "Not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-preview-budget";
        _body.Add(border);
    }

    private void AddSelection(
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version,
        bool evaluationCandidate,
        bool canReview)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Exact typed selection"));
        card.Add(NativeTheme.Title(module.Name, 22));
        card.Add(NativeTheme.Metric("Module ID", module.ModuleId));
        card.Add(NativeTheme.Metric("Module cost", module.KarmaIsExact
            ? CreationNationalityViewText.FormatKarma(module.KarmaCost)
            : $"Not exact · {module.KarmaRaw}"));
        card.Add(NativeTheme.Metric("Catalog enabled", module.IsEnabled.ToString().ToLowerInvariant()));
        card.Add(NativeTheme.Body(
            CreationNationalityViewText.FormatSource(module.Source, module.Page, module.PageReference)
            ?? "Source unavailable",
            NativeTheme.Muted));
        if (module.SourceAnchorIds.Count > 0)
            card.Add(NativeTheme.Body(string.Join(" · ", module.SourceAnchorIds), NativeTheme.Muted));
        if (!string.IsNullOrWhiteSpace(module.StoryTemplate))
            card.Add(NativeTheme.Body(module.StoryTemplate, NativeTheme.Muted));

        if (version is not null)
        {
            card.Add(NativeTheme.Eyebrow("Version"));
            card.Add(NativeTheme.Title(version.Label, 20));
            card.Add(NativeTheme.Metric("Version ID", version.VersionId));
            card.Add(NativeTheme.Metric("Version cost", version.KarmaIsExact
                ? CreationNationalityViewText.FormatKarma(version.KarmaCost)
                : $"Not exact · {version.KarmaRaw}"));
            card.Add(NativeTheme.Metric("Catalog enabled", version.IsEnabled.ToString().ToLowerInvariant()));
            card.Add(NativeTheme.Body(
                CreationNationalityViewText.FormatSource(version.Source, version.Page, version.PageReference)
                ?? "Source unavailable",
                NativeTheme.Muted));
            if (version.SourceAnchorIds.Count > 0)
                card.Add(NativeTheme.Body(string.Join(" · ", version.SourceAnchorIds), NativeTheme.Muted));
            if (!string.IsNullOrWhiteSpace(version.StoryTemplate))
                card.Add(NativeTheme.Body(version.StoryTemplate, NativeTheme.Muted));
        }

        if (evaluationCandidate && canReview)
        {
            card.Add(NativeTheme.Body(
                "Original catalog status remains disabled; only Core Preview may evaluate the "
                + "typed metatype requirement.",
                NativeTheme.Danger));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-preview-selection";
        _body.Add(border);
    }

    private void AddRequirements(IReadOnlyList<LifeModuleRequirementProjectionDto> requirements)
    {
        if (requirements.Count == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Requirements"));
        foreach (LifeModuleRequirementProjectionDto requirement in requirements)
        {
            card.Add(NativeTheme.Title(requirement.Label, 17));
            card.Add(NativeTheme.Metric("Requirement ID", requirement.RequirementId));
            card.Add(NativeTheme.Metric("Operator", requirement.Operator));
            card.Add(NativeTheme.Metric("Subject", requirement.SubjectKind));
            card.Add(NativeTheme.Metric("Accepted", string.Join(" · ", requirement.AcceptedValues)));
            card.Add(NativeTheme.Metric("Met", requirement.IsMet.ToString().ToLowerInvariant()));
            card.Add(NativeTheme.Metric(
                "Character authority",
                requirement.RequiresCharacterAuthority.ToString().ToLowerInvariant()));
            if (!string.IsNullOrWhiteSpace(requirement.DisableReasonKey))
            {
                string disableReason = requirement.DisableReasonArguments.Count == 0
                    ? requirement.DisableReasonKey
                    : $"{requirement.DisableReasonKey} ("
                      + string.Join(
                          ", ",
                          requirement.DisableReasonArguments
                              .OrderBy(item => item.Key, StringComparer.Ordinal)
                              .Select(item => $"{item.Key}={item.Value}"))
                      + ")";
                card.Add(NativeTheme.Body(disableReason, NativeTheme.Danger));
            }
            if (requirement.SourceAnchorIds.Count > 0)
                card.Add(NativeTheme.Body(string.Join(" · ", requirement.SourceAnchorIds), NativeTheme.Muted));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-preview-requirements";
        _body.Add(border);
    }

    private void AddEffects(IReadOnlyList<LifeModuleEffectProjectionDto> effects)
    {
        if (effects.Count == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Projected source effects"));
        foreach (LifeModuleEffectProjectionDto effect in effects)
        {
            card.Add(NativeTheme.Title(effect.Domain, 17));
            card.Add(NativeTheme.Metric("Effect ID", effect.EffectId));
            card.Add(NativeTheme.Metric("Target ID", effect.TargetId));
            if (!string.IsNullOrWhiteSpace(effect.BudgetId))
            {
                card.Add(NativeTheme.Metric(
                    $"Budget delta · {effect.BudgetId}",
                    effect.BudgetDelta.ToString("0.##", CultureInfo.InvariantCulture)));
            }
            card.Add(NativeTheme.Metric("Fully typed", effect.IsFullyTyped.ToString().ToLowerInvariant()));
            if (!string.IsNullOrWhiteSpace(effect.AuthorityBlocker))
                card.Add(NativeTheme.Body(effect.AuthorityBlocker, NativeTheme.Danger));
            if (effect.SourceAnchorIds.Count > 0)
                card.Add(NativeTheme.Body(string.Join(" · ", effect.SourceAnchorIds), NativeTheme.Muted));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-preview-effects";
        _body.Add(border);
    }

    private void AddFollowUps(IReadOnlyList<LifeModuleFollowUpPromptDto> prompts)
    {
        if (prompts.Count == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Follow-ups after selection"));
        foreach (LifeModuleFollowUpPromptDto prompt in prompts)
        {
            card.Add(NativeTheme.Title(prompt.Label, 17));
            card.Add(NativeTheme.Metric("Prompt ID", prompt.PromptId));
            card.Add(NativeTheme.Metric("Input", prompt.InputKind));
            card.Add(NativeTheme.Metric("Required", prompt.IsRequired.ToString().ToLowerInvariant()));
            card.Add(NativeTheme.Metric(
                "Enabled options",
                prompt.Options.Count(option => option.IsEnabled).ToString(CultureInfo.InvariantCulture)));
            if (prompt.SourceAnchorIds.Count > 0)
                card.Add(NativeTheme.Body(string.Join(" · ", prompt.SourceAnchorIds), NativeTheme.Muted));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-preview-follow-ups";
        _body.Add(border);
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Selection blockers"));
        foreach (string blocker in blockers
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal))
        {
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-preview-blockers";
        _body.Add(border);
    }

    private async Task ConfirmAsync()
    {
        _localBlocker = null;
        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state
            || !_draft.Matches(state)
            || !_draft.TryConfirmNationality(state, _moduleId, _versionId))
        {
            _localBlocker = "The exact Nationality selection could not be confirmed.";
            Refresh();
            return;
        }

        await Navigation.PopAsync(animated: false);
        while (Navigation.NavigationStack.LastOrDefault() is CreationNationalityPage
               or CreationNationalityVersionPage)
        {
            await Navigation.PopAsync(animated: false);
        }
    }
}
