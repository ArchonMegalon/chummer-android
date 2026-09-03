using System.Globalization;
using Chummer.Contracts.Characters;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
using Chummer.Android.Proof;
#endif

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
#if CHUMMER_API36_PROOF_INSTRUMENTATION
    private CharacterCreationPrerequisiteState? _latestApi36ProofReadyState;
    private bool _api36ProofRouteAppeared;
    private bool _api36ProofPageLoaded;
    private bool _api36ProofAttachmentPublicationAttempted;
#endif

    public CreationPrerequisitePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = WizardStrings.Get("Priority.PageTitle", "Priorities");
        AutomationId = "creation-prerequisite-page";
        Content = new ScrollView { Content = _body };
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        Loaded += OnApi36ProofLoaded;
#endif
    }

#if CHUMMER_API36_PROOF_INSTRUMENTATION
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // NativePageBase may refresh synchronously before its first incomplete await, or later
        // after initialization. Retain the route, Loaded, and ready-state signals so every
        // lifecycle order reaches the same exact attachment gate.
        _api36ProofRouteAppeared = true;
        TryPublishApi36AttachmentProof();
    }

    protected override void OnDisappearing()
    {
        _api36ProofRouteAppeared = false;
        _latestApi36ProofReadyState = null;
        _api36ProofAttachmentPublicationAttempted = false;
        base.OnDisappearing();
    }
#endif

    protected override void Refresh()
    {
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        _api36ProofAttachmentPublicationAttempted = false;
        _latestApi36ProofReadyState = null;
#endif
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Eyebrow", "Character creation")));
        _body.Add(NativeTheme.Title(WizardStrings.Get("Priority.Heading", "Priority / Sum-to-Ten")));
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> load =
            Coordinator.LoadCreationPrerequisite();
        if (!string.Equals(
                load.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || load.Value is not { } state)
        {
            AddBlockers(
                WizardStrings.Get("Priority.AuthorityUnavailable", "Creation prerequisite authority unavailable"),
                load.Blockers.Count > 0 ? load.Blockers : [load.Outcome],
                "creation-prerequisite-unavailable");
            return;
        }

        _draft.Bind(state, Coordinator.State);
        // Keep the build-method authority in the first native viewport.  The
        // following digest and Karma cards are deliberately tall; rendering the
        // short method card between them can move it through Android's
        // accessibility viewport between two otherwise overlapping swipes.
        // This is presentation order only.  Every value still comes from the
        // same revision-bound prerequisite state.
        AddMethod(state);
        AddBinding(state);
        AddCreationKarma(state.CreationKarmaBudget);
        if (state.PendingDraft is { } pending)
            AddPendingDraft(pending);

        if (!CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(
                WizardStrings.Get("Priority.AuthorityBlocked", "Creation prerequisite authority blocked"),
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
            AddBlockers(
                WizardStrings.Get("Common.PreviewBlockers", "Preview blockers"),
                _prepareBlockers,
                "creation-prerequisite-preview-blockers");
        AddActions(state);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        _latestApi36ProofReadyState = state;
        TryPublishApi36AttachmentProof();
#endif
    }

#if CHUMMER_API36_PROOF_INSTRUMENTATION
    private void OnApi36ProofLoaded(object? sender, EventArgs args)
    {
        _api36ProofPageLoaded = true;
        TryPublishApi36AttachmentProof();
    }

    private void TryPublishApi36AttachmentProof()
    {
        IReadOnlyList<Page> navigationStack = Navigation.NavigationStack;
        if (_api36ProofAttachmentPublicationAttempted
            || !_api36ProofRouteAppeared
            || !_api36ProofPageLoaded
            || !IsLoaded
            || Handler is null
            || Window is null
            || navigationStack.Count < 2
            || !ReferenceEquals(navigationStack[^1], this)
            || navigationStack.Count(candidate => ReferenceEquals(candidate, this)) != 1
            || _latestApi36ProofReadyState is not { } state
            || !CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State))
        {
            return;
        }

        _api36ProofAttachmentPublicationAttempted = true;
        Api36ProofStatePublisher.TryPublishCreationPrerequisiteAttachment(
            this,
            Coordinator,
            state.Binding.WorkspaceId.Value,
            state.Binding.ContentRevision,
            state.Binding.SavedRevision,
            state.Binding.RawCharacterXmlDigest,
            state.SnapshotDigest,
            prerequisiteAuthorityReady: true);
    }
#endif

    private void AddBinding(CharacterCreationPrerequisiteState state)
    {
        Label binding = NativeTheme.Body(
            WizardStrings.Format(
                "Priority.Binding",
                "Revision {0} · saved {1} · snapshot {2} · authority {3}",
                state.Binding.ContentRevision,
                state.Binding.SavedRevision,
                ShortDigest(state.SnapshotDigest),
                ShortDigest(state.Binding.AuthorityDigest)),
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
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Karma.Heading", "Global Creation Karma")));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Priority.Karma.BudgetId", "Budget ID"), budget.BudgetId));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Common.Total", "Total"), total));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Common.Used", "Used"), used));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Common.Remaining", "Remaining"), remaining));
        card.Add(NativeTheme.Body(
            budget.IsExact
                ? WizardStrings.Get("Priority.Karma.Exact", "Exact authoritative budget")
                : WizardStrings.Get("Priority.Karma.Inexact", "Budget is not exact"),
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-karma-budget";
        SemanticProperties.SetDescription(
            border,
            WizardStrings.Format(
                "Priority.Karma.Semantic",
                "Global Creation Karma. Total {0}. Used {1}. Remaining {2}.",
                total,
                used,
                remaining));
        _body.Add(border);
    }

    private void AddMethod(CharacterCreationPrerequisiteState state)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Method.Heading", "Authoritative build method")));
        card.Add(NativeTheme.Title(
            string.Equals(
                state.BuildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal)
                ? WizardStrings.Get("Priority.SumToTen", "Sum-to-Ten")
                : string.Equals(
                    state.BuildMethod,
                    CharacterCreationBuildMethods.Priority,
                    StringComparison.Ordinal)
                    ? WizardStrings.Get("Priority.Priority", "Priority")
                    : state.BuildMethod,
            21));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Priority.Method.SettingsProfile", "Settings profile"), state.Authority.SettingsProfileId));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Priority.Method.PriorityTable", "Priority table"), state.Authority.PriorityTable));
        if (string.Equals(
                state.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal))
        {
            card.Add(NativeTheme.Metric(
                WizardStrings.Get("Priority.Method.RequiredProfile", "Required profile multiset"),
                string.Join(" · ", state.Authority.PriorityArray)));
        }
        else
        {
            int used = _draft.SumToTenUsed(state, Coordinator.State) ?? 0;
            card.Add(NativeTheme.Metric(
                WizardStrings.Get("Priority.SumToTen", "Sum-to-Ten"),
                $"{used.ToString(CultureInfo.InvariantCulture)} / "
                + (state.Authority.SumToTenTarget?.ToString(CultureInfo.InvariantCulture) ?? "—")));
            card.Add(NativeTheme.Body(
                WizardStrings.Get(
                    "Priority.Method.RepeatedRanks",
                    "Repeated ranks are allowed only when the authority projects an exact route to the target."),
                NativeTheme.Muted));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-method";
        SemanticProperties.SetDescription(border, state.BuildMethod);
        _body.Add(NativeAuthoritySemantics.Overlay(
            border,
            NativeAuthoritySemantics.Identifier(
                "creation-prerequisite-build-method-id",
                state.BuildMethod)));
    }

    private void AddPendingDraft(CharacterCreationPrerequisiteDraft pending)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Draft.Resumed", "Resumed persisted draft")));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Draft.Revision", "Draft revision"),
            pending.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Priority.Draft.Digest", "Draft digest"), ShortDigest(pending.DraftDigest)));
        Label fullDraftDigest = NativeTheme.Body(pending.DraftDigest, NativeTheme.Muted);
        fullDraftDigest.AutomationId = "creation-prerequisite-pending-draft-digest";
        card.Add(fullDraftDigest);
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Draft.BaseRevision", "Base revision"),
            pending.BaseContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            WizardStrings.Get(
                "Priority.Draft.Restored",
                "The rank, Heritage, and Talent selections were restored from Core auxiliary state after reload."),
            NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-pending-draft";
        _body.Add(border);
    }

    private void AddCategories(CharacterCreationPrerequisiteState state)
    {
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Categories.Heading", "Five ordered categories")));
        for (int index = 0; index < CharacterCreationPriorityCategoryIds.Ordered.Count; index++)
        {
            string category = CharacterCreationPriorityCategoryIds.Ordered[index];
            CharacterCreationPriorityOptionProjection? selected =
                _draft.SelectedOption(state, Coordinator.State, category);
            string projectedLabel = state.Authority.Options
                .Where(option => string.Equals(option.CategoryId, category, StringComparison.Ordinal))
                .Select(option => option.CategoryName)
                .Distinct(StringComparer.Ordinal)
                .Single();
            string label = WizardStrings.PriorityCategory(category, projectedLabel);
            string detail = selected is null
                ? WizardStrings.Format(
                    "Priority.Categories.SelectRank",
                    "{0}. Select an authority-projected rank",
                    index + 1)
                : JoinDetails(
                    WizardStrings.Format("Priority.Categories.Rank", "{0}. Rank {1}", index + 1, selected.Rank),
                    selected.Label,
                    WizardStrings.Format("Common.SourceInline", "source {0}", selected.SourceId),
                    string.Equals(
                        category,
                        CharacterCreationPriorityCategoryIds.Attributes,
                        StringComparison.Ordinal)
                        ? WizardStrings.Format(
                            "Priority.Categories.RawGrantInline",
                            "raw grant {0}",
                            selected.BaseNormalAttributePoints?.ToString(CultureInfo.InvariantCulture))
                        : null);
            _body.Add(NativeTheme.NavigationRow(
                label,
                detail,
                () => Navigation.PushAsync(new CreationPriorityCategoryPage(
                    Coordinator,
                    _draft,
                    state,
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
                    WizardStrings.Get("Priority.Heritage.Choice", "Heritage choice"),
                    selectedHeritage is null
                        ? WizardStrings.Get(
                            "Priority.Heritage.Prompt",
                            "Select an exact Core-projected metatype or metavariant")
                        : JoinDetails(
                            selectedHeritage.MetatypeName,
                            selectedHeritage.MetavariantName,
                            WizardStrings.Format(
                                "Common.SelectionInline",
                                "selection {0}",
                                selectedHeritage.SelectionId)),
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
                    WizardStrings.Get("Priority.Talent.Choice", "Talent choice"),
                    selectedTalent is null
                        ? WizardStrings.Get(
                            "Priority.Talent.Prompt",
                            "Select an exact Core-projected Talent and complete any required grants")
                        : JoinDetails(
                            selectedTalent.Name,
                            selectedTalent.Value,
                            WizardStrings.Format(
                                "Common.SelectionInline",
                                "selection {0}",
                                selectedTalent.SelectionId),
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
            ? WizardStrings.Get(
                "Priority.Attributes.AdjustmentRequired",
                "Raw grant only: Heritage/metatype halveattributepoints adjustment is still required.")
            : state.CanEnterAttributes
                ? WizardStrings.Get(
                    "Priority.Attributes.NextStage",
                    "Core resolved Heritage/metatype adjustment; the dedicated phone Attributes page is the next stage.")
                : WizardStrings.Get(
                    "Priority.Attributes.NotEnabled",
                    "The prerequisite authority has not enabled Attributes navigation.");
        string detail = JoinDetails(
            WizardStrings.Format(
                "Priority.Attributes.RawGrant",
                "Raw normal Attribute grant: {0}",
                rawGrant?.ToString(CultureInfo.InvariantCulture)
                ?? WizardStrings.Get("Common.NotSelected", "not selected")),
            state.EffectiveNormalAttributePoints is int effective
                ? WizardStrings.Format(
                    "Priority.Attributes.EffectiveGrant",
                    "Effective normal Attribute grant: {0}",
                    effective.ToString(CultureInfo.InvariantCulture))
                : null,
            state.TotalSpecialAttributePoints is int special
                ? WizardStrings.Format(
                    "Priority.Attributes.SpecialPoints",
                    "Special Attribute points: {0}",
                    special.ToString(CultureInfo.InvariantCulture))
                : null,
            adjustmentReason,
            state.CanEnterAttributes
                ? WizardStrings.Get("Priority.Attributes.Complete", "Core prerequisite complete")
                : WizardStrings.Get("Priority.Attributes.Disabled", "Attributes remain disabled"));
        Border row = NativeTheme.NavigationRow(
            WizardStrings.PriorityCategory(
                CharacterCreationPriorityCategoryIds.Attributes,
                "Attributes"),
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
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Source.Heading", "Source authority")));
        card.Add(SourceAuthorityMetric(
            WizardStrings.Get("Priority.Source.AuthorityDigest", "Authority digest"),
            state.Authority.AuthorityDigest,
            "creation-prerequisite-authority-digest"));
        card.Add(SourceAuthorityMetric(
            WizardStrings.Get("Priority.Source.ProfileInputs", "Profile inputs"),
            state.Authority.RawProfileInputsDigest,
            "creation-prerequisite-profile-inputs-digest"));
        card.Add(SourceAuthorityMetric(
            WizardStrings.Get("Priority.Source.PrioritiesXml", "Priorities XML"),
            state.Authority.RawPrioritiesXmlDigest,
            "creation-prerequisite-priorities-xml-digest"));
        foreach (string anchor in state.Authority.SourceAnchorIds)
            card.Add(NativeTheme.Body(
                WizardStrings.Format("Common.SourceAnchor", "Source anchor · {0}", anchor),
                NativeTheme.Muted));
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
        Button preview = NativeTheme.PrimaryButton(WizardStrings.Get("Priority.Actions.Preview", "Preview assignments draft"));
        preview.AutomationId = "creation-prerequisite-prepare-preview";
        preview.IsEnabled = _draft.CanPrepare(state, Coordinator.State);
        preview.Clicked += async (_, _) => await PreparePreviewAsync(state);
        _body.Add(preview);

        Button reset = NativeTheme.SecondaryButton(WizardStrings.Get("Priority.Actions.Reset", "Reset rank selections"));
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
            WizardStrings.Get(
                "Priority.Actions.Scope",
                "Selection is local until the exact Core preview is explicitly confirmed. "
                + "No character XML or legacy editor field is changed by this page."),
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
            return WizardStrings.Format(
                "Priority.Talent.ActiveGrantProgress",
                "Active-skill grant {0} / {1}",
                _draft.TalentActiveSkillSelectionIds(state, Coordinator.State).Count.ToString(CultureInfo.InvariantCulture),
                active.Quantity.ToString(CultureInfo.InvariantCulture));
        }
        if (talent.SkillGroupGrant is { } group)
        {
            return WizardStrings.Format(
                "Priority.Talent.GroupGrantProgress",
                "Skill-group grant {0} / {1}",
                _draft.TalentSkillGroupSelectionIds(state, Coordinator.State).Count.ToString(CultureInfo.InvariantCulture),
                group.Quantity.ToString(CultureInfo.InvariantCulture));
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
