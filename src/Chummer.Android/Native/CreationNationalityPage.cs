using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only deep navigation over exact Foundation-owned Nationality module identities. A row
/// opens either its authoritative versions or an explicit non-writing selection preview.
/// </summary>
public sealed class CreationNationalityPage : NativePageBase
{
    private readonly CreationFoundationPhoneDraft _draft;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationNationalityPage(
        RunnerSessionCoordinator coordinator,
        CreationFoundationPhoneDraft draft) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Title = "Nationality";
        AutomationId = "creation-nationality-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Foundation"));
        _body.Add(NativeTheme.Title("Choose a Nationality"));

        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(
                "Nationality authority unavailable",
                load.Blockers.Count > 0 ? load.Blockers : [load.Outcome]);
            return;
        }

        if (!_draft.Matches(state))
        {
            AddBlockers(
                "Nationality selection is stale",
                ["The Foundation workspace, revision, or digest changed. Return and reload before selecting."]);
            return;
        }

        CharacterCreationLegalOption? metatype = _draft.ResolveConfirmedMetatype(state);
        if (metatype is null)
        {
            AddBlockers(
                "Metatype required",
                ["Confirm one exact metatype before reviewing Nationality candidates."]);
            return;
        }

        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {CreationNationalityViewText.ShortDigest(state.FoundationSnapshotDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-nationality-binding";
        _body.Add(binding);
        AddBudget(state.LifeModuleBudget);
        if (state.AuthorityBlockers.Count > 0)
            AddBlockers("Authority blockers", state.AuthorityBlockers);

        if (state.NationalityOptions.Count == 0)
        {
            AddBlockers("No candidates", ["No Nationality modules were projected by the authority."]);
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Authoritative modules"));
        foreach (LifeModuleLegalOptionDto module in state.NationalityOptions)
        {
            bool selected = string.Equals(
                _draft.ConfirmedNationalityModuleId,
                module.ModuleId,
                StringComparison.Ordinal);
            LifeModuleVersionProjectionDto? selectedVersion = selected
                ? _draft.ResolveConfirmedNationalityVersion(state)
                : null;
            bool evaluationCandidate = CreationFoundationPhoneAuthority
                .IsMetatypeEvaluationCandidate(state, module, metatype);
            bool canOpen = CreationFoundationPhoneAuthority.CanOpenModule(
                state,
                module,
                metatype);
            string[] candidateBlockers = module.AuthorityBlockers
                .Concat(module.Versions.SelectMany(static version => version.AuthorityBlockers))
                .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string detail = CreationNationalityViewText.JoinDetails(
                selected ? "Current draft selection" : null,
                $"ID {module.ModuleId}",
                module.Versions.Count == 0
                    ? null
                    : $"{module.Versions.Count.ToString(CultureInfo.InvariantCulture)} authoritative versions",
                selectedVersion is null ? null : $"Version {selectedVersion.VersionId}",
                module.KarmaIsExact
                    ? CreationNationalityViewText.FormatKarma(module.KarmaCost)
                    : $"Karma not exact ({module.KarmaRaw})",
                CreationNationalityViewText.FormatSource(
                    module.Source,
                    module.Page,
                    module.PageReference),
                CreationNationalityViewText.FormatAnchors(module.SourceAnchorIds),
                module.IsEnabled
                    ? null
                    : evaluationCandidate
                        ? "Catalog disabled · requires selected-metatype evaluation in Core Preview"
                        : CreationNationalityViewText.FormatBlockers(module.AuthorityBlockers),
                candidateBlockers.Length == 0
                    ? null
                    : CreationNationalityViewText.FormatBlockers(candidateBlockers),
                canOpen ? null : "Exact review unavailable");
            _body.Add(NativeTheme.NavigationRow(
                module.Name,
                detail,
                () => OpenModuleAsync(module),
                canOpen,
                $"creation-nationality-option-{CreationNationalityViewText.Token(module.ModuleId)}"));
        }
    }

    private Task OpenModuleAsync(LifeModuleLegalOptionDto module)
        => module.Versions.Count == 0
            ? Navigation.PushAsync(new CreationNationalityPreviewPage(
                Coordinator,
                _draft,
                module.ModuleId,
                null))
            : Navigation.PushAsync(new CreationNationalityVersionPage(
                Coordinator,
                _draft,
                module.ModuleId));

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
        border.AutomationId = "creation-nationality-budget";
        _body.Add(border);
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.Where(static value => !string.IsNullOrWhiteSpace(value)))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-blockers";
        _body.Add(border);
    }
}

/// <summary>Exact version-ID navigation for one authoritative Nationality module.</summary>
public sealed class CreationNationalityVersionPage : NativePageBase
{
    private readonly CreationFoundationPhoneDraft _draft;
    private readonly string _moduleId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationNationalityVersionPage(
        RunnerSessionCoordinator coordinator,
        CreationFoundationPhoneDraft draft,
        string moduleId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _moduleId = string.IsNullOrWhiteSpace(moduleId)
            ? throw new ArgumentException("A typed Nationality module ID is required.", nameof(moduleId))
            : moduleId;
        Title = "Nationality Version";
        AutomationId = "creation-nationality-version-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Foundation"));
        _body.Add(NativeTheme.Title("Choose a version"));

        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state
            || !_draft.Matches(state))
        {
            AddBlocker("The Foundation authority changed. Return and reload the Nationality list.");
            return;
        }

        CharacterCreationLegalOption? metatype = _draft.ResolveConfirmedMetatype(state);
        LifeModuleLegalOptionDto? module = CreationFoundationPhoneAuthority.ResolveUniqueModule(
            state,
            _moduleId);
        if (metatype is null || module is null || module.Versions.Count == 0)
        {
            AddBlocker("The exact metatype, module, or version catalog is unavailable.");
            return;
        }

        VerticalStackLayout moduleCard = new() { Spacing = 6 };
        moduleCard.Add(NativeTheme.Eyebrow("Selected module"));
        moduleCard.Add(NativeTheme.Title(module.Name, 21));
        moduleCard.Add(NativeTheme.Metric("Module ID", module.ModuleId));
        moduleCard.Add(NativeTheme.Metric("Module cost", CreationNationalityViewText.FormatKarma(module.KarmaCost)));
        moduleCard.Add(NativeTheme.Body(
            CreationNationalityViewText.FormatSource(module.Source, module.Page, module.PageReference)
            ?? "Source unavailable",
            NativeTheme.Muted));
        _body.Add(NativeTheme.Card(moduleCard));
        AddBudget(state.LifeModuleBudget);

        foreach (LifeModuleVersionProjectionDto version in module.Versions)
        {
            bool selected = string.Equals(
                _draft.ConfirmedNationalityModuleId,
                module.ModuleId,
                StringComparison.Ordinal)
                && string.Equals(
                    _draft.ConfirmedNationalityVersionId,
                    version.VersionId,
                    StringComparison.Ordinal);
            bool evaluationCandidate = CreationFoundationPhoneAuthority
                .IsMetatypeEvaluationCandidate(state, module, version, metatype);
            bool canReview = CreationFoundationPhoneAuthority.CanReviewSelection(
                state,
                module,
                version,
                metatype);
            string[] candidateBlockers = module.AuthorityBlockers
                .Concat(version.AuthorityBlockers)
                .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string detail = CreationNationalityViewText.JoinDetails(
                selected ? "Current draft selection" : null,
                $"ID {version.VersionId}",
                version.KarmaIsExact
                    ? CreationNationalityViewText.FormatKarma(version.KarmaCost)
                    : $"Karma not exact ({version.KarmaRaw})",
                CreationNationalityViewText.FormatSource(
                    version.Source,
                    version.Page,
                    version.PageReference),
                CreationNationalityViewText.FormatAnchors(version.SourceAnchorIds),
                version.IsEnabled
                    ? null
                    : evaluationCandidate
                        ? "Catalog disabled · requires selected-metatype evaluation in Core Preview"
                        : CreationNationalityViewText.FormatBlockers(version.AuthorityBlockers),
                candidateBlockers.Length == 0
                    ? null
                    : CreationNationalityViewText.FormatBlockers(candidateBlockers),
                canReview ? null : "Exact review unavailable");
            _body.Add(NativeTheme.NavigationRow(
                version.Label,
                detail,
                () => Navigation.PushAsync(new CreationNationalityPreviewPage(
                    Coordinator,
                    _draft,
                    module.ModuleId,
                    version.VersionId)),
                canReview,
                $"creation-nationality-version-option-{CreationNationalityViewText.Token(version.VersionId)}"));
        }
    }

    private void AddBudget(CharacterCreationBudgetState budget)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Current authoritative budget"));
        card.Add(NativeTheme.Metric("Budget ID", budget.BudgetId));
        card.Add(NativeTheme.Metric(
            "Remaining",
            CreationNationalityViewText.FormatBudget(budget.Remaining, budget.Unit)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact" : "Not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-nationality-version-budget";
        _body.Add(border);
    }

    private void AddBlocker(string blocker)
    {
        Label label = NativeTheme.Body(blocker, NativeTheme.Danger);
        label.AutomationId = "creation-nationality-version-blockers";
        _body.Add(label);
    }
}

internal static class CreationNationalityViewText
{
    public static string FormatKarma(decimal value)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} karma";

    public static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    public static string? FormatSource(string? source, int? page, string? pageReference)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        string rawPage = !string.IsNullOrWhiteSpace(pageReference)
            ? pageReference
            : page?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return string.IsNullOrWhiteSpace(rawPage) ? source : $"{source} p. {rawPage}";
    }

    public static string? FormatAnchors(IReadOnlyList<string> anchors)
        => anchors.Count == 0 ? null : $"Anchors {string.Join(" · ", anchors)}";

    public static string FormatBlockers(IReadOnlyList<string> blockers)
        => blockers.Count == 0 ? "disable-reason-not-projected" : string.Join(", ", blockers);

    public static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!));

    public static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest) ? "unavailable" : digest[..Math.Min(12, digest.Length)];

    public static string Token(string value)
        => new(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
}
