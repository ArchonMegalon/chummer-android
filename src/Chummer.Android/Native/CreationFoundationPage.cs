using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only renderer for the revision-bound Foundation and Nationality authority. It collects
/// explicit selections but delegates all legality, cost, diff, and persistence decisions to the
/// Presentation interaction boundary.
/// </summary>
public sealed class CreationFoundationPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly CreationFoundationPhoneDraft _phoneDraft = new();
    private string? _observedMetatypeId;
    private string? _selectedNationalityId;
    private string? _selectedVersionId;
    private IReadOnlyList<string> _prepareBlockers = [];
    private readonly Dictionary<string, string> _followUpValues = new(StringComparer.Ordinal);

    public CreationFoundationPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Foundation";
        AutomationId = "creation-foundation-page";
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Rook",
            AutomationId = "creation-foundation-rook",
            Command = new Command(async () => await Navigation.PushAsync(new RookConversationPage(Coordinator)))
        });
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Character creation"));
        _body.Add(NativeTheme.Title("Foundation & Nationality"));

        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (!string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockerCard(
                "Foundation unavailable",
                load.Blockers.Count > 0
                    ? load.Blockers
                    : [load.Outcome],
                "creation-foundation-load-blockers");
            return;
        }

        bool rebound = _phoneDraft.Bind(state);
        string? currentMetatypeId = _phoneDraft.ConfirmedMetatypeOptionId;
        if (rebound
            || !string.Equals(_observedMetatypeId, currentMetatypeId, StringComparison.Ordinal))
        {
            _selectedNationalityId = null;
            _selectedVersionId = null;
            _followUpValues.Clear();
            _prepareBlockers = [];
            _observedMetatypeId = currentMetatypeId;
        }

        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {ShortDigest(state.FoundationSnapshotDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-foundation-binding";
        _body.Add(binding);

        AddBudget(state.LifeModuleBudget);
        AddPendingDraft(state.PendingDraft);
        if (state.AuthorityBlockers.Count > 0)
        {
            AddBlockerCard(
                "Authority blockers",
                state.AuthorityBlockers,
                "creation-foundation-authority-blockers");
        }

        AddMetatypeOptions(state);
        AddNationalityOptions(state);
        AddSelectedVersionOptions(state);
        AddFollowUps(state);
        AddPrepareAction(state);

        _body.Add(NativeTheme.NavigationRow(
            "Ask Rook",
            "Current revision, budgets, blockers, and exact legal options",
            () => Navigation.PushAsync(new RookConversationPage(Coordinator)),
            automationId: "creation-foundation-rook-entry"));
    }

    private void AddBudget(CharacterCreationBudgetState budget)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(budget.Label));
        card.Add(NativeTheme.Metric("Total", FormatBudget(budget.Total, budget.Unit)));
        card.Add(NativeTheme.Metric("Used", FormatBudget(budget.Used, budget.Unit)));
        card.Add(NativeTheme.Metric("Remaining", FormatBudget(budget.Remaining, budget.Unit)));
        Label exact = NativeTheme.Body(
            budget.IsExact ? "Exact authoritative budget" : "Budget is not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger);
        exact.AutomationId = "creation-foundation-budget-exactness";
        card.Add(exact);
        foreach (string blocker in budget.Blockers)
        {
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-foundation-budget";
        _body.Add(border);
    }

    private void AddPendingDraft(CharacterCreationFoundationDraftLedger? draft)
    {
        if (draft is null)
            return;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Pending Foundation draft"));
        card.Add(NativeTheme.Metric("Draft revision", draft.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Metatype", draft.RequestedMetatype));
        card.Add(NativeTheme.Metric("Nationality", draft.Selection.ModuleId));
        if (!string.IsNullOrWhiteSpace(draft.Selection.VersionId))
            card.Add(NativeTheme.Metric("Version", draft.Selection.VersionId));
        Label compilation = NativeTheme.Body($"Compilation status: {draft.CompilationStatus}", NativeTheme.Muted);
        compilation.AutomationId = "creation-foundation-pending-compilation-status";
        card.Add(compilation);
        Label effects = NativeTheme.Body(
            $"CharacterEffectsApplied = {draft.CharacterEffectsApplied.ToString().ToLowerInvariant()}",
            draft.CharacterEffectsApplied ? NativeTheme.Danger : NativeTheme.Muted);
        effects.AutomationId = "creation-foundation-pending-character-effects-applied";
        card.Add(effects);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-foundation-pending-draft";
        _body.Add(border);
    }

    private void AddMetatypeOptions(CharacterCreationFoundationInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Metatype"));
        if (state.MetatypeOptions.Count == 0)
        {
            _body.Add(NativeTheme.Body("No metatype options were projected.", NativeTheme.Danger));
            return;
        }

        CharacterCreationLegalOption? selected = _phoneDraft.ResolveConfirmedMetatype(state);
        string detail = selected is null
            ? $"{state.MetatypeOptions.Count} authoritative options"
            : JoinDetails(
                $"Selected · ID {selected.OptionId}",
                FormatCosts(selected.Costs),
                FormatSource(selected.SourceId, selected.SourcePage, null),
                selected.SourceAnchorIds.Count == 0
                    ? null
                    : $"Anchors {string.Join(" · ", selected.SourceAnchorIds)}");
        _body.Add(NativeTheme.NavigationRow(
            selected?.Label ?? "Choose metatype",
            detail,
            () => Navigation.PushAsync(new CreationMetatypePage(Coordinator, _phoneDraft)),
            automationId: "creation-foundation-open-metatype"));
    }

    private void AddNationalityOptions(CharacterCreationFoundationInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Nationality Life Module"));
        if (state.NationalityOptions.Count == 0)
        {
            _body.Add(NativeTheme.Body("No Nationality modules were projected.", NativeTheme.Danger));
            return;
        }

        foreach (LifeModuleLegalOptionDto option in state.NationalityOptions)
        {
            bool selected = string.Equals(_selectedNationalityId, option.ModuleId, StringComparison.Ordinal);
            bool evaluationCandidate = IsMetatypeEvaluationCandidate(state, option);
            bool selectable = option.IsEnabled || evaluationCandidate;
            string detail = JoinDetails(
                selected ? "Selected" : null,
                option.KarmaIsExact
                    ? FormatBudget(option.KarmaCost, "karma")
                    : $"Karma not exact ({option.KarmaRaw})",
                FormatSource(option.Source, option.Page, option.PageReference),
                option.IsEnabled
                    ? null
                    : evaluationCandidate
                        ? "Requires authoritative metatype evaluation in Preview"
                        : FormatBlockers(option.AuthorityBlockers));
            _body.Add(NativeTheme.NavigationRow(
                option.Name,
                detail,
                () => SelectNationalityAsync(option),
                selectable,
                $"creation-foundation-nationality-{Token(option.ModuleId)}"));
            if (selected)
            {
                AddStoryAndRequirements(
                    option.StoryTemplate,
                    option.Requirements,
                    option.SourceAnchorIds,
                    "creation-foundation-nationality-details");
            }
        }
    }

    private Task SelectNationalityAsync(LifeModuleLegalOptionDto option)
    {
        _selectedNationalityId = option.ModuleId;
        _selectedVersionId = null;
        _followUpValues.Clear();
        _prepareBlockers = [];
        Refresh();
        return Task.CompletedTask;
    }

    private void AddSelectedVersionOptions(CharacterCreationFoundationInteractionState state)
    {
        LifeModuleLegalOptionDto? nationality = SelectedNationality(state);
        if (nationality is null || nationality.Versions.Count == 0)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Nationality version"));
        foreach (LifeModuleVersionProjectionDto version in nationality.Versions)
        {
            bool selected = string.Equals(_selectedVersionId, version.VersionId, StringComparison.Ordinal);
            bool evaluationCandidate = IsMetatypeEvaluationCandidate(state, nationality, version);
            bool selectable = version.IsEnabled || evaluationCandidate;
            string detail = JoinDetails(
                selected ? "Selected" : null,
                version.KarmaIsExact
                    ? FormatBudget(version.KarmaCost, "karma")
                    : $"Karma not exact ({version.KarmaRaw})",
                FormatSource(version.Source, version.Page, version.PageReference),
                version.IsEnabled
                    ? null
                    : evaluationCandidate
                        ? "Requires authoritative metatype evaluation in Preview"
                        : FormatBlockers(version.AuthorityBlockers));
            _body.Add(NativeTheme.NavigationRow(
                version.Label,
                detail,
                () => SelectVersionAsync(version),
                selectable,
                $"creation-foundation-version-{Token(version.VersionId)}"));
            if (selected)
            {
                AddStoryAndRequirements(
                    version.StoryTemplate,
                    version.Requirements,
                    version.SourceAnchorIds,
                    "creation-foundation-version-details");
            }
        }
    }

    private Task SelectVersionAsync(LifeModuleVersionProjectionDto version)
    {
        _selectedVersionId = version.VersionId;
        _followUpValues.Clear();
        _prepareBlockers = [];
        Refresh();
        return Task.CompletedTask;
    }

    private void AddFollowUps(CharacterCreationFoundationInteractionState state)
    {
        IReadOnlyList<LifeModuleFollowUpPromptDto> prompts = SelectedFollowUps(state);
        if (prompts.Count == 0)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Follow-ups"));
        foreach (LifeModuleFollowUpPromptDto prompt in prompts)
        {
            Label promptLabel = NativeTheme.Body(
                $"{prompt.Label} · {(prompt.IsRequired ? "required" : "optional")} · {prompt.InputKind}",
                NativeTheme.Text);
            promptLabel.FontAttributes = FontAttributes.Bold;
            promptLabel.AutomationId = $"creation-foundation-follow-up-{Token(prompt.PromptId)}";
            _body.Add(promptLabel);

            if (!IsSelectPrompt(prompt))
            {
                _body.Add(NativeTheme.Body(
                    $"Unsupported follow-up kind: {prompt.InputKind}. Preview stays disabled.",
                    NativeTheme.Danger));
                continue;
            }

            foreach (LifeModuleFollowUpOptionDto option in prompt.Options)
            {
                bool selected = _followUpValues.TryGetValue(prompt.PromptId, out string? value)
                                && string.Equals(value, option.SourceValue, StringComparison.Ordinal);
                _body.Add(NativeTheme.NavigationRow(
                    option.Label,
                    JoinDetails(
                        selected ? "Selected" : null,
                        option.IsEnabled ? null : FormatDisableReason(
                            option.DisableReasonKey,
                            option.DisableReasonArguments)),
                    () => SelectFollowUpAsync(prompt, option),
                    option.IsEnabled,
                    $"creation-foundation-follow-up-{Token(prompt.PromptId)}-option-{Token(option.OptionId)}"));
            }
        }
    }

    private Task SelectFollowUpAsync(
        LifeModuleFollowUpPromptDto prompt,
        LifeModuleFollowUpOptionDto option)
    {
        _followUpValues[prompt.PromptId] = option.SourceValue;
        _prepareBlockers = [];
        Refresh();
        return Task.CompletedTask;
    }

    private void AddPrepareAction(CharacterCreationFoundationInteractionState state)
    {
        IReadOnlyList<string> uiBlockers = GetSelectionBlockers(state);
        if (_prepareBlockers.Count > 0)
        {
            AddBlockerCard("Preview blockers", _prepareBlockers, "creation-foundation-prepare-blockers");
        }
        if (uiBlockers.Count > 0)
        {
            AddBlockerCard("Selection required", uiBlockers, "creation-foundation-selection-blockers");
        }

        Button preview = NativeTheme.PrimaryButton("Review exact changes");
        preview.AutomationId = "creation-foundation-prepare-preview";
        preview.IsEnabled = uiBlockers.Count == 0;
        preview.Clicked += async (_, _) => await RunAsync(async () =>
        {
            CharacterCreationLegalOption metatype = SelectedMetatype(state)!;
            LifeModuleLegalOptionDto nationality = SelectedNationality(state)!;
            CharacterCreationFoundationInteractionPrepareResult result =
                Coordinator.PrepareCreationFoundation(new CharacterCreationFoundationSelectionInput(
                    metatype.Label,
                    nationality.ModuleId,
                    _selectedVersionId,
                    new Dictionary<string, string>(_followUpValues, StringComparer.Ordinal)));
            _prepareBlockers = result.Blockers;
            if (result.PreparedPreview is { } prepared)
            {
                await Navigation.PushAsync(new CreationFoundationPreviewPage(Coordinator, prepared));
            }
        });
        _body.Add(preview);
    }

    private IReadOnlyList<string> GetSelectionBlockers(
        CharacterCreationFoundationInteractionState state)
    {
        var blockers = new List<string>();
        CharacterCreationLegalOption? metatype = SelectedMetatype(state);
        LifeModuleLegalOptionDto? nationality = SelectedNationality(state);
        if (metatype is null)
            blockers.Add("Select an enabled metatype.");
        else if (!metatype.IsEnabled)
            blockers.Add(FormatDisableReason(metatype.DisableReasonKey, metatype.DisableReasonArguments));
        if (nationality is null)
            blockers.Add("Select an enabled Nationality module.");
        else if (!nationality.IsEnabled && !IsMetatypeEvaluationCandidate(state, nationality))
            blockers.Add(FormatBlockers(nationality.AuthorityBlockers));

        if (nationality is { Versions.Count: > 0 })
        {
            LifeModuleVersionProjectionDto? version = nationality.Versions.FirstOrDefault(candidate =>
                string.Equals(candidate.VersionId, _selectedVersionId, StringComparison.Ordinal));
            if (version is null)
                blockers.Add("Select an enabled Nationality version.");
            else if (!version.IsEnabled
                     && !IsMetatypeEvaluationCandidate(state, nationality, version))
                blockers.Add(FormatBlockers(version.AuthorityBlockers));
        }

        IReadOnlyList<LifeModuleFollowUpPromptDto> prompts = SelectedFollowUps(state);
        if (prompts.GroupBy(prompt => prompt.PromptId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            blockers.Add("Duplicate follow-up prompt identities are unsupported.");
        foreach (LifeModuleFollowUpPromptDto prompt in prompts)
        {
            if (!IsSelectPrompt(prompt))
            {
                blockers.Add($"Unsupported follow-up kind: {prompt.InputKind}.");
                continue;
            }
            if (prompt.IsRequired
                && (!_followUpValues.TryGetValue(prompt.PromptId, out string? value)
                    || string.IsNullOrWhiteSpace(value)))
            {
                blockers.Add($"Required follow-up not selected: {prompt.Label}.");
            }
        }

        blockers.AddRange(state.AuthorityBlockers);
        blockers.AddRange(state.LifeModuleBudget.Blockers);
        if (!state.LifeModuleBudget.IsExact)
            blockers.Add("The Life Modules budget is not exact.");
        return blockers
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private CharacterCreationLegalOption? SelectedMetatype(CharacterCreationFoundationInteractionState state)
        => _phoneDraft.ResolveConfirmedMetatype(state);

    private LifeModuleLegalOptionDto? SelectedNationality(CharacterCreationFoundationInteractionState state)
        => state.NationalityOptions.FirstOrDefault(option =>
            string.Equals(option.ModuleId, _selectedNationalityId, StringComparison.Ordinal));

    private IReadOnlyList<LifeModuleFollowUpPromptDto> SelectedFollowUps(
        CharacterCreationFoundationInteractionState state)
    {
        LifeModuleLegalOptionDto? nationality = SelectedNationality(state);
        if (nationality is null)
            return [];
        LifeModuleVersionProjectionDto? version = nationality.Versions.FirstOrDefault(candidate =>
            string.Equals(candidate.VersionId, _selectedVersionId, StringComparison.Ordinal));
        return nationality.FollowUps.Concat(version?.FollowUps ?? []).ToArray();
    }

    private void AddStoryAndRequirements(
        string story,
        IReadOnlyList<LifeModuleRequirementProjectionDto> requirements,
        IReadOnlyList<string> sourceAnchors,
        string automationId)
    {
        VerticalStackLayout details = new() { Spacing = 6 };
        if (!string.IsNullOrWhiteSpace(story))
            details.Add(NativeTheme.Body(story, NativeTheme.Muted));
        foreach (LifeModuleRequirementProjectionDto requirement in requirements)
        {
            details.Add(NativeTheme.Body(
                $"{requirement.Label} · {(requirement.IsMet ? "met" : requirement.DisableReasonKey ?? "not met")}",
                requirement.IsMet ? NativeTheme.Muted : NativeTheme.Danger));
        }
        if (sourceAnchors.Count > 0)
            details.Add(NativeTheme.Body(string.Join(" · ", sourceAnchors), NativeTheme.Muted));
        Border card = NativeTheme.Card(details, new Thickness(14));
        card.AutomationId = automationId;
        _body.Add(card);
    }

    private void AddBlockerCard(string title, IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout content = new() { Spacing = 6 };
        content.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers)
            content.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border card = NativeTheme.Card(content);
        card.AutomationId = automationId;
        _body.Add(card);
    }

    private static bool IsSelectPrompt(LifeModuleFollowUpPromptDto prompt)
        => string.Equals(prompt.InputKind, "select", StringComparison.OrdinalIgnoreCase)
           || string.Equals(prompt.InputKind, "single-select", StringComparison.OrdinalIgnoreCase);

    private static bool IsMetatypeEvaluationCandidate(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module)
    {
        if (module.IsEnabled)
            return false;
        return module.Versions.Count == 0
            ? CanEvaluateWithSelectedMetatype(state, module, null)
            : module.Versions.Any(version =>
                IsMetatypeEvaluationCandidate(state, module, version));
    }

    private static bool IsMetatypeEvaluationCandidate(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto version)
        => !version.IsEnabled && CanEvaluateWithSelectedMetatype(state, module, version);

    private static bool CanEvaluateWithSelectedMetatype(
        CharacterCreationFoundationInteractionState state,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version)
    {
        if (!HasExactCandidateIdentity(state.NationalityOptions, module, version)
            || module.StageOrder != LifeModuleJourneyStageOrders.Nationality
            || !string.Equals(
                module.StageId,
                CharacterCreationLifeModuleStageIds.Nationality,
                StringComparison.OrdinalIgnoreCase)
            || module.CanRepeat
            || !HasExactCandidateIdentityCostAndSource(
                module.ModuleId,
                module.Name,
                module.KarmaCost,
                module.KarmaRaw,
                module.KarmaIsExact,
                module.Source,
                module.SourceAnchorIds)
            || version is not null
            && !HasExactCandidateIdentityCostAndSource(
                version.VersionId,
                version.Label,
                version.KarmaCost,
                version.KarmaRaw,
                version.KarmaIsExact,
                version.Source,
                version.SourceAnchorIds))
        {
            return false;
        }

        LifeModuleRequirementProjectionDto[] requirements = module.Requirements
            .Concat(version?.Requirements ?? [])
            .ToArray();
        LifeModuleRequirementProjectionDto[] unresolved = requirements
            .Where(static requirement =>
                !requirement.IsMet || requirement.RequiresCharacterAuthority)
            .ToArray();
        if (unresolved.Length == 0
            || requirements.Any(requirement =>
                !string.IsNullOrWhiteSpace(requirement.DisableReasonKey)
                && !string.Equals(
                    requirement.DisableReasonKey,
                    CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired,
                    StringComparison.Ordinal))
            || !HasOnlyTypedMetatypeRequirements(unresolved))
        {
            return false;
        }

        string[] blockers = module.AuthorityBlockers
            .Concat(version?.AuthorityBlockers ?? [])
            .Concat(unresolved.Select(static requirement =>
                requirement.DisableReasonKey ?? string.Empty))
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return HasOnlyEligibilityAuthorityBlocker(blockers)
               && state.MetatypeOptions
                   .Where(static option =>
                       option.IsEnabled
                       && string.IsNullOrWhiteSpace(option.DisableReasonKey)
                       && !string.IsNullOrWhiteSpace(option.OptionId)
                       && !string.IsNullOrWhiteSpace(option.Label))
                   .Select(static option => option.Label)
                   .Any(label => unresolved.All(requirement =>
                       requirement.AcceptedValues.Contains(
                           label,
                           StringComparer.OrdinalIgnoreCase)));
    }

    private static bool HasExactCandidateIdentity(
        IReadOnlyList<LifeModuleLegalOptionDto> modules,
        LifeModuleLegalOptionDto module,
        LifeModuleVersionProjectionDto? version)
    {
        if (string.IsNullOrWhiteSpace(module.ModuleId)
            || modules.Count(candidate => string.Equals(
                candidate.ModuleId,
                module.ModuleId,
                StringComparison.Ordinal)) != 1)
        {
            return false;
        }

        if (module.Versions.Count == 0)
            return version is null;
        return version is not null
               && !string.IsNullOrWhiteSpace(version.VersionId)
               && module.Versions.Count(candidate => string.Equals(
                   candidate.VersionId,
                   version.VersionId,
                   StringComparison.Ordinal)) == 1;
    }

    private static bool HasExactCandidateIdentityCostAndSource(
        string id,
        string label,
        decimal karmaCost,
        string karmaRaw,
        bool karmaIsExact,
        string source,
        IReadOnlyList<string> sourceAnchorIds)
        => !string.IsNullOrWhiteSpace(id)
           && string.Equals(id, id.Trim(), StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(label)
           && string.Equals(label, label.Trim(), StringComparison.Ordinal)
           && karmaIsExact
           && karmaCost >= 0
           && !string.IsNullOrWhiteSpace(karmaRaw)
           && string.Equals(karmaRaw, karmaRaw.Trim(), StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(source)
           && string.Equals(source, source.Trim(), StringComparison.Ordinal)
           && sourceAnchorIds.Count > 0
           && sourceAnchorIds.All(static anchor => !string.IsNullOrWhiteSpace(anchor));

    private static bool HasOnlyEligibilityAuthorityBlocker(IReadOnlyList<string> blockers)
        => blockers.Count == 1
           && string.Equals(
               blockers[0],
               CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired,
               StringComparison.Ordinal);

    private static bool HasOnlyTypedMetatypeRequirements(
        IReadOnlyList<LifeModuleRequirementProjectionDto> requirements)
        => requirements.Count > 0
           && requirements.All(requirement =>
               requirement.RequiresCharacterAuthority
               && !requirement.IsMet
               && !string.IsNullOrWhiteSpace(requirement.RequirementId)
               && string.Equals(requirement.Operator, "oneof", StringComparison.OrdinalIgnoreCase)
               && string.Equals(requirement.SubjectKind, "metatype", StringComparison.OrdinalIgnoreCase)
               && requirement.AcceptedValues.Count > 0
               && requirement.AcceptedValues.All(static value =>
                   !string.IsNullOrWhiteSpace(value)));

    private static string FormatCosts(IReadOnlyList<CharacterCreationChoiceCost> costs)
        => string.Join(
            " · ",
            costs.Select(cost => $"{cost.Delta.ToString("0.##", CultureInfo.InvariantCulture)} {cost.Unit}"));

    private static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    private static string? FormatSource(string? source, int? page, string? pageReference)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        string rawPage = !string.IsNullOrWhiteSpace(pageReference)
            ? pageReference
            : page?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return string.IsNullOrWhiteSpace(rawPage) ? source : $"{source} p. {rawPage}";
    }

    private static string FormatDisableReason(
        string? key,
        IReadOnlyDictionary<string, string> arguments)
    {
        string reason = string.IsNullOrWhiteSpace(key) ? "disabled" : key;
        return arguments.Count == 0
            ? reason
            : $"{reason} ({string.Join(", ", arguments.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"))})";
    }

    private static string FormatBlockers(IReadOnlyList<string> blockers)
        => blockers.Count == 0 ? "disable-reason-not-projected" : string.Join(", ", blockers);

    private static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!));

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest) ? "unavailable" : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
