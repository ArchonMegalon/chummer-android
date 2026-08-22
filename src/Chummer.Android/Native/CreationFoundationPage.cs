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
    private string? _observedSelectionKey;
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
        string currentSelectionKey = string.Join(
            "|",
            _phoneDraft.ConfirmedMetatypeOptionId,
            _phoneDraft.ConfirmedNationalityModuleId,
            _phoneDraft.ConfirmedNationalityVersionId);
        if (rebound)
        {
            _followUpValues.Clear();
            foreach ((string promptId, string value) in _phoneDraft.ResolvePendingFollowUpValues(state))
                _followUpValues[promptId] = value;
            _prepareBlockers = [];
        }
        else if (!string.Equals(_observedSelectionKey, currentSelectionKey, StringComparison.Ordinal))
        {
            _followUpValues.Clear();
            _prepareBlockers = [];
        }
        _observedSelectionKey = currentSelectionKey;

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
        AddNationalitySelection(state);
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

    private void AddNationalitySelection(CharacterCreationFoundationInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Nationality Life Module"));
        if (state.NationalityOptions.Count == 0)
        {
            _body.Add(NativeTheme.Body("No Nationality modules were projected.", NativeTheme.Danger));
            return;
        }

        CharacterCreationLegalOption? metatype = SelectedMetatype(state);
        LifeModuleLegalOptionDto? nationality = SelectedNationality(state);
        LifeModuleVersionProjectionDto? version = SelectedNationalityVersion(state);
        bool canOpen = metatype is not null
                       && state.NationalityOptions.Any(candidate =>
                           CreationFoundationPhoneAuthority.CanOpenModule(
                               state,
                               candidate,
                               metatype));
        string detail = nationality is null
            ? $"{state.NationalityOptions.Count.ToString(CultureInfo.InvariantCulture)} authoritative modules"
            : JoinDetails(
                $"Selected · ID {nationality.ModuleId}",
                version is null ? null : $"Version {version.VersionId}",
                nationality.KarmaIsExact
                    ? FormatBudget(nationality.KarmaCost, "karma")
                    : $"Karma not exact ({nationality.KarmaRaw})",
                FormatSource(nationality.Source, nationality.Page, nationality.PageReference),
                nationality.SourceAnchorIds.Count == 0
                    ? null
                    : $"Anchors {string.Join(" · ", nationality.SourceAnchorIds)}");
        _body.Add(NativeTheme.NavigationRow(
            nationality?.Name ?? (metatype is null ? "Choose metatype first" : "Choose Nationality"),
            detail,
            () => Navigation.PushAsync(new CreationNationalityPage(Coordinator, _phoneDraft)),
            canOpen,
            "creation-foundation-open-nationality"));
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
                    SelectedNationalityVersion(state)?.VersionId,
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
        else if (!nationality.IsEnabled
                 && !CreationFoundationPhoneAuthority.IsMetatypeEvaluationCandidate(
                     state,
                     nationality,
                     metatype))
            blockers.Add(FormatBlockers(nationality.AuthorityBlockers));

        if (nationality is { Versions.Count: > 0 })
        {
            LifeModuleVersionProjectionDto? version = SelectedNationalityVersion(state);
            if (version is null)
                blockers.Add("Select an enabled Nationality version.");
            else if (!version.IsEnabled
                     && !CreationFoundationPhoneAuthority.IsMetatypeEvaluationCandidate(
                         state,
                         nationality,
                         version,
                         metatype))
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
        => _phoneDraft.ResolveConfirmedNationality(state);

    private LifeModuleVersionProjectionDto? SelectedNationalityVersion(
        CharacterCreationFoundationInteractionState state)
        => _phoneDraft.ResolveConfirmedNationalityVersion(state);

    private IReadOnlyList<LifeModuleFollowUpPromptDto> SelectedFollowUps(
        CharacterCreationFoundationInteractionState state)
    {
        LifeModuleLegalOptionDto? nationality = SelectedNationality(state);
        if (nationality is null)
            return [];
        LifeModuleVersionProjectionDto? version = SelectedNationalityVersion(state);
        return nationality.FollowUps.Concat(version?.FollowUps ?? []).ToArray();
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
