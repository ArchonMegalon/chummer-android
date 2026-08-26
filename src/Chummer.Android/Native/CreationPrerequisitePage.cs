using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only Priority/Sum-to-Ten prerequisite stage. The page stores typed projected selections;
/// Core evaluates and persists the revision-bound draft.
/// </summary>
public sealed class CreationPrerequisitePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly CreationPrerequisitePhoneDraft _draft = new();
    private IReadOnlyList<string> _prepareBlockers = [];

    public CreationPrerequisitePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Priorities";
        AutomationId = "creation-prerequisite-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Character creation"));
        _body.Add(NativeTheme.Title("Priority / Sum-to-Ten"));
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> load =
            Coordinator.LoadCreationPrerequisite();
        if (!string.Equals(
                load.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || load.Value is not { } state)
        {
            AddBlockers(
                "Creation prerequisite authority unavailable",
                load.Blockers.Count > 0 ? load.Blockers : [load.Outcome],
                "creation-prerequisite-unavailable");
            return;
        }

        _draft.Bind(state, Coordinator.State);
        AddBinding(state);
        AddCreationKarma(state.CreationKarmaBudget);
        AddMethod(state);
        if (state.PendingDraft is { } pending)
            AddPendingDraft(pending);

        if (!CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(
                "Creation prerequisite authority blocked",
                state.Blockers
                    .Concat(state.Authority.Blockers)
                    .Concat(state.CreationKarmaBudget.Blockers)
                    .DefaultIfEmpty(CharacterCreationPrerequisiteBlockers.AuthorityUnavailable)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                "creation-prerequisite-blockers");
            AddAttributesGate(state, _draft.BaseNormalAttributePoints(state, Coordinator.State));
            return;
        }

        AddCategories(state);
        AddRawAttributeGrant(state);
        AddSourceAuthority(state);
        if (_prepareBlockers.Count > 0)
            AddBlockers("Preview blockers", _prepareBlockers, "creation-prerequisite-preview-blockers");
        AddActions(state);
    }

    private void AddBinding(CharacterCreationPrerequisiteState state)
    {
        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {ShortDigest(state.SnapshotDigest)} · authority {ShortDigest(state.Binding.AuthorityDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-prerequisite-binding";
        _body.Add(binding);
        AddDigestBinding(
            "creation-prerequisite-snapshot-digest",
            state.SnapshotDigest);
        AddDigestBinding(
            "creation-prerequisite-raw-character-xml-digest",
            state.Binding.RawCharacterXmlDigest);
        AddDigestBinding(
            "creation-prerequisite-auxiliary-state-digest",
            state.Binding.AuxiliaryStateDigest);
        AddDigestBinding(
            "creation-prerequisite-authority-digest",
            state.Binding.AuthorityDigest);
    }

    private void AddDigestBinding(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        _body.Add(label);
    }

    private void AddCreationKarma(CharacterCreationBudgetState budget)
    {
        string total = FormatBudget(budget.Total, budget.Unit);
        string used = FormatBudget(budget.Used, budget.Unit);
        string remaining = FormatBudget(budget.Remaining, budget.Unit);
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Global Creation Karma"));
        card.Add(NativeTheme.Metric("Budget ID", budget.BudgetId));
        card.Add(NativeTheme.Metric("Total", total));
        card.Add(NativeTheme.Metric("Used", used));
        card.Add(NativeTheme.Metric("Remaining", remaining));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact authoritative budget" : "Budget is not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-karma-budget";
        SemanticProperties.SetDescription(
            border,
            $"Global Creation Karma. Total {total}. Used {used}. Remaining {remaining}.");
        _body.Add(border);
    }

    private void AddMethod(CharacterCreationPrerequisiteState state)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Authoritative build method"));
        card.Add(NativeTheme.Title(
            string.Equals(
                state.BuildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal)
                ? "Sum-to-Ten"
                : state.BuildMethod,
            21));
        card.Add(NativeTheme.Metric("Settings profile", state.Authority.SettingsProfileId));
        card.Add(NativeTheme.Metric("Priority table", state.Authority.PriorityTable));
        if (string.Equals(
                state.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal))
        {
            card.Add(NativeTheme.Metric(
                "Required profile multiset",
                string.Join(" · ", state.Authority.PriorityArray)));
        }
        else
        {
            int used = _draft.SumToTenUsed(state, Coordinator.State) ?? 0;
            card.Add(NativeTheme.Metric(
                "Sum-to-Ten",
                $"{used.ToString(CultureInfo.InvariantCulture)} / "
                + (state.Authority.SumToTenTarget?.ToString(CultureInfo.InvariantCulture) ?? "—")));
            card.Add(NativeTheme.Body(
                "Repeated ranks are allowed only when the authority projects an exact route to the target.",
                NativeTheme.Muted));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-method";
        _body.Add(border);
    }

    private void AddPendingDraft(CharacterCreationPrerequisiteDraft pending)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Resumed persisted draft"));
        card.Add(NativeTheme.Metric(
            "Draft revision",
            pending.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Draft digest", ShortDigest(pending.DraftDigest)));
        Label fullDraftDigest = NativeTheme.Body(pending.DraftDigest, NativeTheme.Muted);
        fullDraftDigest.AutomationId = "creation-prerequisite-pending-draft-digest";
        card.Add(fullDraftDigest);
        card.Add(NativeTheme.Metric(
            "Base revision",
            pending.BaseContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            "The rank, Heritage, and Talent selections were restored from Core auxiliary state after reload.",
            NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-pending-draft";
        _body.Add(border);
    }

    private void AddCategories(CharacterCreationPrerequisiteState state)
    {
        _body.Add(NativeTheme.Eyebrow("Five ordered categories"));
        for (int index = 0; index < CharacterCreationPriorityCategoryIds.Ordered.Count; index++)
        {
            string category = CharacterCreationPriorityCategoryIds.Ordered[index];
            CharacterCreationPriorityOptionProjection? selected =
                _draft.SelectedOption(state, Coordinator.State, category);
            string label = state.Authority.Options
                .Where(option => string.Equals(option.CategoryId, category, StringComparison.Ordinal))
                .Select(option => option.CategoryName)
                .Distinct(StringComparer.Ordinal)
                .Single();
            string detail = selected is null
                ? $"{index + 1}. Select an authority-projected rank"
                : JoinDetails(
                    $"{index + 1}. Rank {selected.Rank}",
                    selected.Label,
                    $"source {selected.SourceId}",
                    string.Equals(
                        category,
                        CharacterCreationPriorityCategoryIds.Attributes,
                        StringComparison.Ordinal)
                        ? $"raw grant {selected.BaseNormalAttributePoints?.ToString(CultureInfo.InvariantCulture)}"
                        : null);
            _body.Add(NativeTheme.NavigationRow(
                label,
                detail,
                () => Navigation.PushAsync(new CreationPriorityCategoryPage(
                    Coordinator,
                    _draft,
                    category)),
                automationId: $"creation-prerequisite-category-{Token(category)}"));

            if (string.Equals(
                    category,
                    CharacterCreationPriorityCategoryIds.Heritage,
                    StringComparison.Ordinal))
            {
                CharacterCreationPriorityHeritageOptionProjection? selectedHeritage =
                    _draft.SelectedHeritage(state, Coordinator.State);
                _body.Add(NativeTheme.NavigationRow(
                    "Heritage choice",
                    selectedHeritage is null
                        ? "Select an exact Core-projected metatype or metavariant"
                        : JoinDetails(
                            selectedHeritage.MetatypeName,
                            selectedHeritage.MetavariantName,
                            $"selection {selectedHeritage.SelectionId}"),
                    () => Navigation.PushAsync(new CreationPriorityDetailPage(
                        Coordinator,
                        _draft,
                        category)),
                    enabled: selected is not null,
                    automationId: "creation-prerequisite-heritage-selection"));
                if (selectedHeritage is not null)
                {
                    Label selectionId = NativeTheme.Body(
                        selectedHeritage.SelectionId,
                        NativeTheme.Muted);
                    selectionId.AutomationId =
                        "creation-prerequisite-heritage-selection-id";
                    _body.Add(selectionId);
                }
            }
            else if (string.Equals(
                         category,
                         CharacterCreationPriorityCategoryIds.Talent,
                         StringComparison.Ordinal))
            {
                CharacterCreationPriorityTalentOptionProjection? selectedTalent =
                    _draft.SelectedTalent(state, Coordinator.State);
                _body.Add(NativeTheme.NavigationRow(
                    "Talent choice",
                    selectedTalent is null
                        ? "Select an exact Core-projected Talent and complete any required grants"
                        : JoinDetails(
                            selectedTalent.Name,
                            selectedTalent.Value,
                            $"selection {selectedTalent.SelectionId}",
                            TalentGrantProgress(selectedTalent, state)),
                    () => Navigation.PushAsync(new CreationPriorityDetailPage(
                        Coordinator,
                        _draft,
                        category)),
                    enabled: selected is not null,
                    automationId: "creation-prerequisite-talent-selection"));
                if (selectedTalent is not null)
                {
                    Label selectionId = NativeTheme.Body(
                        selectedTalent.SelectionId,
                        NativeTheme.Muted);
                    selectionId.AutomationId =
                        "creation-prerequisite-talent-selection-id";
                    _body.Add(selectionId);
                }
            }
        }
    }

    private void AddRawAttributeGrant(CharacterCreationPrerequisiteState state)
    {
        int? rawGrant = _draft.BaseNormalAttributePoints(state, Coordinator.State)
                        ?? state.BaseNormalAttributePoints;
        AddAttributesGate(state, rawGrant);
    }

    private void AddAttributesGate(CharacterCreationPrerequisiteState state, int? rawGrant)
    {
        string adjustmentReason = state.RequiresMetatypeAttributeAdjustment
            ? "Raw grant only: Heritage/metatype halveattributepoints adjustment is still required."
            : state.CanEnterAttributes
                ? "Core resolved Heritage/metatype adjustment; the dedicated phone Attributes page is the next stage."
                : "The prerequisite authority has not enabled Attributes navigation.";
        string detail = JoinDetails(
            $"Raw normal Attribute grant: {rawGrant?.ToString(CultureInfo.InvariantCulture) ?? "not selected"}",
            state.EffectiveNormalAttributePoints is int effective
                ? $"Effective normal Attribute grant: {effective.ToString(CultureInfo.InvariantCulture)}"
                : null,
            state.TotalSpecialAttributePoints is int special
                ? $"Special Attribute points: {special.ToString(CultureInfo.InvariantCulture)}"
                : null,
            adjustmentReason,
            state.CanEnterAttributes ? "Core prerequisite complete" : "Attributes remain disabled");
        Border row = NativeTheme.NavigationRow(
            "Attributes",
            detail,
            () => Task.CompletedTask,
            enabled: false,
            automationId: state.CanEnterAttributes
                ? "creation-prerequisite-attributes-ready"
                : "creation-prerequisite-attributes-disabled");
        _body.Add(row);
    }

    private void AddSourceAuthority(CharacterCreationPrerequisiteState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Source authority"));
        card.Add(SourceAuthorityMetric(
            "Authority digest",
            state.Authority.AuthorityDigest,
            "creation-prerequisite-authority-digest"));
        card.Add(SourceAuthorityMetric(
            "Profile inputs",
            state.Authority.RawProfileInputsDigest,
            "creation-prerequisite-profile-inputs-digest"));
        card.Add(SourceAuthorityMetric(
            "Priorities XML",
            state.Authority.RawPrioritiesXmlDigest,
            "creation-prerequisite-priorities-xml-digest"));
        foreach (string anchor in state.Authority.SourceAnchorIds)
            card.Add(NativeTheme.Body($"Source anchor · {anchor}", NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-source-authority";
        _body.Add(border);
    }

    private static VerticalStackLayout SourceAuthorityMetric(
        string label,
        string value,
        string automationId)
    {
        Label labelView = NativeTheme.Body(label, NativeTheme.Muted);
        labelView.AutomationId = $"{automationId}-label";
        Label valueView = NativeTheme.Body(string.IsNullOrWhiteSpace(value) ? "—" : value);
        valueView.AutomationId = automationId;
        valueView.FontAttributes = FontAttributes.Bold;
        valueView.LineBreakMode = LineBreakMode.CharacterWrap;
        return new VerticalStackLayout
        {
            Spacing = 2,
            Children = { labelView, valueView }
        };
    }

    private void AddActions(CharacterCreationPrerequisiteState state)
    {
        Button preview = NativeTheme.PrimaryButton("Preview assignments draft");
        preview.AutomationId = "creation-prerequisite-prepare-preview";
        preview.IsEnabled = _draft.CanPrepare(state, Coordinator.State);
        preview.Clicked += async (_, _) => await PreparePreviewAsync(state);
        _body.Add(preview);

        Button reset = NativeTheme.SecondaryButton("Reset rank selections");
        reset.AutomationId = "creation-prerequisite-reset";
        reset.IsEnabled = _draft.Assignments(state, Coordinator.State).Count > 0;
        reset.Clicked += (_, _) =>
        {
            _prepareBlockers = [];
            _draft.Reset(state, Coordinator.State);
            Refresh();
        };
        _body.Add(reset);

        Label scope = NativeTheme.Body(
            "Selection is local until the exact Core preview is explicitly confirmed. "
            + "No character XML or legacy editor field is changed by this page.",
            NativeTheme.Muted);
        scope.AutomationId = "creation-prerequisite-draft-scope";
        _body.Add(scope);
    }

    private async Task PreparePreviewAsync(CharacterCreationPrerequisiteState state)
    {
        _prepareBlockers = [];
        if (!_draft.CanPrepare(state, Coordinator.State))
        {
            _prepareBlockers = [CharacterCreationPrerequisiteBlockers.SelectionIncomplete];
            Refresh();
            return;
        }

        IReadOnlyDictionary<string, string> assignments = _draft.Assignments(
            state,
            Coordinator.State);
        CreationPrerequisitePhoneSelections? selections = _draft.Selections(
            state,
            Coordinator.State);
        if (selections is null)
        {
            _prepareBlockers = [CharacterCreationPrerequisiteBlockers.SelectionIncomplete];
            Refresh();
            return;
        }
        CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> result =
            Coordinator.PreviewCreationPrerequisite(state.Binding, assignments, selections);
        if (!string.Equals(
                result.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || result.Value is not { CanConfirm: true } prepared
            || prepared.Blockers.Count > 0)
        {
            _prepareBlockers = result.Blockers.Count > 0
                ? result.Blockers
                : result.Value?.Blockers ?? [result.Outcome];
            Refresh();
            return;
        }

        await Navigation.PushAsync(new CreationPrerequisitePreviewPage(
            Coordinator,
            prepared,
            assignments,
            selections,
            state.BuildMethod));
    }

    private void AddBlockers(
        string title,
        IReadOnlyList<string> blockers,
        string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.Where(static value => !string.IsNullOrWhiteSpace(value)))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    private string? TalentGrantProgress(
        CharacterCreationPriorityTalentOptionProjection talent,
        CharacterCreationPrerequisiteState state)
    {
        if (talent.ActiveSkillGrant is { } active)
        {
            return $"Active-skill grant {_draft.TalentActiveSkillSelectionIds(state, Coordinator.State).Count.ToString(CultureInfo.InvariantCulture)} / {active.Quantity.ToString(CultureInfo.InvariantCulture)}";
        }
        if (talent.SkillGroupGrant is { } group)
        {
            return $"Skill-group grant {_draft.TalentSkillGroupSelectionIds(state, Coordinator.State).Count.ToString(CultureInfo.InvariantCulture)} / {group.Quantity.ToString(CultureInfo.InvariantCulture)}";
        }
        return null;
    }

    private static string ShortDigest(string digest)
        => CreationPrerequisiteDigestText.CanonicalPrefix(digest);

    private static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!));

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
