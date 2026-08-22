using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Retains and renders the exact prepared preview supplied by Presentation. Confirmation always
/// sends that same object and digest back through the interaction presenter on a separate tap.
/// </summary>
public sealed class CreationFoundationPreviewPage : NativePageBase
{
    private readonly CharacterCreationFoundationPreparedPreview _prepared;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CharacterCreationFoundationInteractionConfirmResult? _confirmation;
    private bool _savedAfterConfirmation;

    public CreationFoundationPreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationFoundationPreparedPreview prepared) : base(coordinator)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        Title = "Review Foundation";
        AutomationId = "creation-foundation-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Explicit review"));
        _body.Add(NativeTheme.Title("Foundation changes"));

        Label binding = NativeTheme.Body(
            $"Revision {_prepared.Binding.ContentRevision} · saved {_prepared.Binding.SavedRevision} · "
            + $"preview {ShortDigest(_prepared.PreviewDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-foundation-preview-binding";
        _body.Add(binding);

        AddSelection();
        AddBudget();
        AddRequirements();
        AddFollowUps();
        AddDiff();
        AddEffectAndCompilationStatus();
        AddBlockers();
        AddConfirmationAction();
        AddPostConfirmationActions();
    }

    private void AddSelection()
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Exact selection"));
        card.Add(NativeTheme.Metric("Metatype", _prepared.RequestedMetatype));
        card.Add(NativeTheme.Metric("Nationality", _prepared.Nationality?.Name ?? _prepared.Selection.ModuleId));
        if (_prepared.Nationality is { } nationality)
        {
            card.Add(NativeTheme.Metric(
                "Nationality source",
                FormatSource(nationality.Source, nationality.Page, nationality.PageReference)));
            if (!string.IsNullOrWhiteSpace(nationality.StoryTemplate))
                card.Add(NativeTheme.Body(nationality.StoryTemplate, NativeTheme.Muted));
        }
        if (_prepared.NationalityVersion is { } version)
        {
            card.Add(NativeTheme.Metric("Version", version.Label));
            card.Add(NativeTheme.Metric(
                "Version source",
                FormatSource(version.Source, version.Page, version.PageReference)));
            if (!string.IsNullOrWhiteSpace(version.StoryTemplate))
                card.Add(NativeTheme.Body(version.StoryTemplate, NativeTheme.Muted));
        }
        card.Add(NativeTheme.Metric(
            "Selection cost",
            FormatBudget(_prepared.SelectionCost.Delta, _prepared.SelectionCost.Unit)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-foundation-preview-selection";
        _body.Add(border);
    }

    private void AddBudget()
    {
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow("Exact budget change"));
        AddBudgetState(card, "Before", _prepared.LifeModuleBudgetBefore);
        AddBudgetState(card, "After", _prepared.LifeModuleBudgetAfter);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-foundation-preview-budget";
        _body.Add(border);
    }

    private static void AddBudgetState(
        VerticalStackLayout card,
        string label,
        CharacterCreationBudgetState budget)
    {
        card.Add(NativeTheme.FieldLabel(label));
        card.Add(NativeTheme.Metric("Total", FormatBudget(budget.Total, budget.Unit)));
        card.Add(NativeTheme.Metric("Used", FormatBudget(budget.Used, budget.Unit)));
        card.Add(NativeTheme.Metric("Remaining", FormatBudget(budget.Remaining, budget.Unit)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact" : "Not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
    }

    private void AddRequirements()
    {
        if (_prepared.RequirementEvaluations.Count == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Requirements"));
        foreach (LifeModuleRequirementProjectionDto requirement in _prepared.RequirementEvaluations)
        {
            card.Add(NativeTheme.Body(
                $"{requirement.Label} · {(requirement.IsMet ? "met" : requirement.DisableReasonKey ?? "not met")}",
                requirement.IsMet ? NativeTheme.Muted : NativeTheme.Danger));
            if (requirement.SourceAnchorIds.Count > 0)
                card.Add(NativeTheme.Body(string.Join(" · ", requirement.SourceAnchorIds), NativeTheme.Muted));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-foundation-preview-requirements";
        _body.Add(border);
    }

    private void AddFollowUps()
    {
        if (_prepared.FollowUpValues.Count == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Follow-ups"));
        foreach (KeyValuePair<string, string> value in _prepared.FollowUpValues.OrderBy(
                     item => item.Key,
                     StringComparer.Ordinal))
        {
            card.Add(NativeTheme.Metric(value.Key, value.Value));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-foundation-preview-follow-ups";
        _body.Add(border);
    }

    private void AddDiff()
    {
        _body.Add(NativeTheme.Eyebrow("Typed diff"));
        if (_prepared.Diff.Count == 0)
        {
            Label empty = NativeTheme.Body("The authority projected no diff entries.", NativeTheme.Danger);
            empty.AutomationId = "creation-foundation-preview-diff-empty";
            _body.Add(empty);
            return;
        }

        foreach (CharacterCreationFoundationDiffEntry diff in _prepared.Diff)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Eyebrow(diff.Domain));
            card.Add(NativeTheme.Title(diff.TargetId, 18));
            card.Add(NativeTheme.Metric("Before", diff.BeforeValue ?? "—"));
            card.Add(NativeTheme.Metric("After", diff.AfterValue ?? "—"));
            card.Add(NativeTheme.Metric("Phase", diff.Phase));
            card.Add(NativeTheme.Metric(
                "Character document",
                diff.AppliesToCharacterDocument ? "applies" : "does not apply"));
            card.Add(NativeTheme.Metric("Authoritative", diff.IsAuthoritative.ToString()));
            card.Add(NativeTheme.Metric("Can apply", diff.CanApply.ToString()));
            foreach (string blocker in diff.Blockers)
                card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
            if (diff.SourceAnchorIds.Count > 0)
                card.Add(NativeTheme.Body(string.Join(" · ", diff.SourceAnchorIds), NativeTheme.Muted));
            Border border = NativeTheme.Card(card, new Thickness(14));
            border.AutomationId = $"creation-foundation-preview-diff-{Token(diff.DiffId)}";
            _body.Add(border);
        }
    }

    private void AddEffectAndCompilationStatus()
    {
        bool effectsApplied = _confirmation?.Receipt?.CharacterEffectsApplied
                              ?? _prepared.CharacterEffectsApplied;
        Label effects = NativeTheme.Body(
            $"CharacterEffectsApplied = {effectsApplied.ToString().ToLowerInvariant()}",
            effectsApplied ? NativeTheme.Danger : NativeTheme.Muted);
        effects.AutomationId = "creation-foundation-character-effects-applied";
        _body.Add(effects);

        string? confirmedCompilationStatus =
            _confirmation?.RefreshedState?.PendingDraft?.CompilationStatus;
        Label compilation = NativeTheme.Body(
            confirmedCompilationStatus is null
                ? $"Compilation after confirmation: {CharacterCreationFoundationDraftStatuses.PendingFinalization}"
                : $"Compilation status: {confirmedCompilationStatus}",
            NativeTheme.Muted);
        compilation.AutomationId = "creation-foundation-compilation-status";
        _body.Add(compilation);
    }

    private void AddBlockers()
    {
        string[] blockers = _prepared.AuthorityBlockers
            .Concat(_confirmation?.Blockers ?? [])
            .Concat(_prepared.LifeModuleBudgetBefore.Blockers)
            .Concat(_prepared.LifeModuleBudgetAfter.Blockers)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (blockers.Length == 0)
            return;

        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Blockers"));
        foreach (string blocker in blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-foundation-preview-blockers";
        _body.Add(border);
    }

    private void AddConfirmationAction()
    {
        bool alreadyConfirmed = string.Equals(
            _confirmation?.Outcome,
            CharacterCreationFoundationOutcomes.Success,
            StringComparison.Ordinal);
        if (alreadyConfirmed)
        {
            Label confirmed = NativeTheme.Body("Foundation draft confirmed and authoritative state reloaded.");
            confirmed.AutomationId = "creation-foundation-confirmed";
            _body.Add(NativeTheme.Card(confirmed));
            return;
        }

        Button confirm = NativeTheme.PrimaryButton("Confirm Foundation draft");
        confirm.AutomationId = "creation-foundation-confirm";
        confirm.IsEnabled = _prepared.RequiresExplicitConfirmation
                            && _prepared.CanConfirm
                            && _prepared.CanApply
                            && _prepared.AuthorityBlockers.Count == 0
                            && _prepared.LifeModuleBudgetBefore.IsExact
                            && _prepared.LifeModuleBudgetAfter.IsExact;
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            _confirmation = await Coordinator.ConfirmCreationFoundationAsync(
                new CharacterCreationFoundationConfirmation(
                    _prepared,
                    _prepared.PreviewDigest,
                    ExplicitlyConfirmed: true));
        });
        _body.Add(confirm);

        Label explicitConfirmation = NativeTheme.Body(
            _prepared.RequiresExplicitConfirmation
                ? "Confirmation is a separate explicit action."
                : "The authority did not request explicit confirmation.",
            NativeTheme.Muted);
        explicitConfirmation.AutomationId = "creation-foundation-explicit-confirmation";
        _body.Add(explicitConfirmation);
    }

    private void AddPostConfirmationActions()
    {
        CharacterCreationFoundationInteractionConfirmResult? confirmation = _confirmation;
        if (confirmation is null
            || !string.Equals(
                confirmation.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || confirmation.Receipt is not { } receipt)
        {
            return;
        }

        VerticalStackLayout receiptCard = new() { Spacing = 6 };
        receiptCard.Add(NativeTheme.Eyebrow("Saved draft receipt"));
        receiptCard.Add(NativeTheme.Metric("Previous revision", receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        receiptCard.Add(NativeTheme.Metric("Content revision", receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        receiptCard.Add(NativeTheme.Metric("Saved revision", receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        receiptCard.Add(NativeTheme.Metric("Draft revision", receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        receiptCard.Add(NativeTheme.Metric("Draft digest", ShortDigest(receipt.DraftDigest)));
        Border border = NativeTheme.Card(receiptCard);
        border.AutomationId = "creation-foundation-confirm-receipt";
        _body.Add(border);

        Button save = NativeTheme.SecondaryButton(_savedAfterConfirmation ? "Runner saved" : "Save runner");
        save.AutomationId = "creation-foundation-save";
        save.IsEnabled = !_savedAfterConfirmation;
        save.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Coordinator.SaveAsync();
            _savedAfterConfirmation = Coordinator.State.Error is null;
        });
        _body.Add(save);

        Button back = NativeTheme.SecondaryButton("Back to Build");
        back.AutomationId = "creation-foundation-back-to-build";
        back.Clicked += async (_, _) => await BackToBuildAsync();
        _body.Add(back);
    }

    private async Task BackToBuildAsync()
    {
        await Navigation.PopAsync(animated: false);
        if (Navigation.NavigationStack.LastOrDefault() is CreationFoundationPage)
            await Navigation.PopAsync();
    }

    private static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    private static string FormatSource(string source, int? page, string pageReference)
    {
        string rawPage = !string.IsNullOrWhiteSpace(pageReference)
            ? pageReference
            : page?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
            return string.IsNullOrWhiteSpace(rawPage) ? "—" : rawPage;
        return string.IsNullOrWhiteSpace(rawPage) ? source : $"{source} p. {rawPage}";
    }

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest) ? "unavailable" : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
