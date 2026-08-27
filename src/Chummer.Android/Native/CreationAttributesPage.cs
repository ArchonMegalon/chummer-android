using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Phone deep-navigation entry for Core's draft-only SR5 Priority Attributes authority.
/// </summary>
public sealed class CreationAttributesPage : NativePageBase
{
    private readonly CreationAttributesPhoneDraft _draft = new();
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _previewBlockers = [];

    public CreationAttributesPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = CreationAllocationStrings.Get("Attributes.PageTitle", "Attributes");
        AutomationId = "creation-attributes-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.CharacterCreation",
            "Character creation")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "Attributes.Heading",
            "Allocate Attributes")));
        _body.Add(NativeTheme.Body(
            CreationAllocationStrings.Get(
                "Attributes.Intro",
                "Every value, limit, and cost below is projected by Core for this exact Priority draft."),
            NativeTheme.Muted));

        CharacterCreationFoundationResult<CharacterCreationAttributesState> load =
            Coordinator.LoadCreationAttributes();
        if (!string.Equals(
                load.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || load.Value is not { } state)
        {
            AddBlockers(
                load.Blockers.Count > 0
                    ? load.Blockers
                    : [CharacterCreationAttributesBlockers.AuthorityUnavailable],
                "creation-attributes-unavailable");
            return;
        }

        _draft.Bind(state, Coordinator.State);
        AddBinding(state);
        AddBudgets(state);
        AddLimits(state);
        AddPendingDraft(state.PendingDraft);
        if (!CreationAttributesPhoneAuthority.IsReady(state, Coordinator.State)
            || !_draft.Matches(state, Coordinator.State))
        {
            AddBlockers(
                state.Blockers.Count > 0
                    ? state.Blockers
                    : [CharacterCreationAttributesBlockers.AuthorityUnavailable],
                "creation-attributes-blockers");
            return;
        }

        AddAttributeGroup(
            state,
            CharacterCreationAttributeCategories.Normal,
            CreationAllocationStrings.Get("Attributes.Normal", "Normal Attributes"));
        AddAttributeGroup(
            state,
            CharacterCreationAttributeCategories.Special,
            CreationAllocationStrings.Get("Attributes.Special", "Special Attributes"));
        if (_previewBlockers.Count > 0)
            AddBlockers(_previewBlockers, "creation-attributes-preview-blockers");
        AddReviewAction(state);
    }

    private void AddBinding(CharacterCreationAttributesState state)
    {
        Label binding = NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Attributes.Binding",
                "Revision {0} · saved {1} · prerequisite draft {2}",
                state.Binding.ContentRevision,
                state.Binding.SavedRevision,
                state.Binding.PrerequisiteDraftRevision),
            NativeTheme.Muted);
        binding.AutomationId = "creation-attributes-binding";
        _body.Add(binding);
        AddDigest("creation-attributes-snapshot-digest", state.SnapshotDigest);
        AddDigest("creation-attributes-raw-character-xml-digest", state.Binding.RawCharacterXmlDigest);
        AddDigest("creation-attributes-auxiliary-state-digest", state.Binding.AuxiliaryStateDigest);
        AddDigest("creation-attributes-prerequisite-draft-digest", state.Binding.PrerequisiteDraftDigest);
        AddDigest("creation-attributes-prerequisite-authority-digest", state.Binding.PrerequisiteAuthorityDigest);
    }

    private void AddBudgets(CharacterCreationAttributesState state)
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Attributes.ExactLedgers",
            "Exact ledgers")));
        AddBudgetCard(
            _draft.NormalBudget(state),
            "creation-attributes-budget-normal");
        AddBudgetCard(
            _draft.SpecialBudget(state),
            "creation-attributes-budget-special");
        AddBudgetCard(
            _draft.KarmaBudget(state),
            "creation-attributes-budget-karma");
    }

    private void AddLimits(CharacterCreationAttributesState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Attributes.CreationLimits",
            "Creation limits")));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get(
                "Attributes.MaxAtMetatypeMaximum",
                "Attributes allowed at metatype maximum"),
            state.MaxNumberMaxAttributesCreate.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get(
                "Attributes.KarmaMultiplier",
                "Karma multiplier per raised level"),
            state.KarmaAttribute.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-attributes-limits";
        _body.Add(border);
    }

    private void AddPendingDraft(CharacterCreationAttributesDraft? pending)
    {
        if (pending is null)
            return;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Attributes.ResumedDraft",
            "Resumed persisted draft")));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.DraftRevision", "Draft revision"),
            pending.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        Label digest = NativeTheme.Body(pending.DraftDigest, NativeTheme.Muted);
        digest.AutomationId = "creation-attributes-pending-draft-digest";
        card.Add(digest);
        card.Add(NativeTheme.Body(
            pending.CharacterEffectsApplied
                ? CreationAllocationStrings.Get(
                    "Attributes.UnexpectedAppliedEffects",
                    "Unexpected applied character effects; editing remains fail-closed.")
                : CreationAllocationStrings.Get(
                    "Attributes.ResumedDraftDetail",
                    "The typed allocation IDs resumed from Core auxiliary state. Character effects are still pending finalization."),
            pending.CharacterEffectsApplied ? NativeTheme.Danger : NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-attributes-pending-draft";
        _body.Add(border);
    }

    private void AddAttributeGroup(
        CharacterCreationAttributesState state,
        string category,
        string label)
    {
        _body.Add(NativeTheme.Eyebrow(label));
        foreach (CharacterCreationAttributeProjection attribute in _draft.Attributes(state)
                     .Where(attribute => string.Equals(
                         attribute.Category,
                         category,
                         StringComparison.Ordinal)))
        {
            string detail = attribute.IsEnabled
                ? CreationAllocationStrings.Format(
                    "Attributes.AttributeDetail",
                    "{0} · range {1}–{2} · Priority {3} · Karma levels {4} ({5} karma)",
                    attribute.Current,
                    attribute.Minimum,
                    attribute.Maximum,
                    attribute.PriorityPointsSpent,
                    attribute.KarmaLevels,
                    attribute.KarmaCost)
                : string.Join(
                    " · ",
                    attribute.DisableReasons.DefaultIfEmpty(CreationAllocationStrings.Get(
                        "Attributes.NotEnabledByTalent",
                        "Not enabled by this Talent")));
            _body.Add(NativeTheme.NavigationRow(
                AttributeLabel(attribute.AttributeId),
                detail,
                () => Navigation.PushAsync(new CreationAttributeAllocationPage(
                    Coordinator,
                    _draft,
                    attribute.AttributeId)),
                enabled: attribute.IsEnabled,
                automationId: $"creation-attributes-open-{Token(attribute.AttributeId)}"));
        }
    }

    private void AddReviewAction(CharacterCreationAttributesState state)
    {
        Button review = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "Attributes.ReviewExact",
            "Review exact allocation"));
        review.AutomationId = "creation-attributes-prepare-preview";
        review.IsEnabled = _draft.Matches(state, Coordinator.State);
        review.Clicked += async (_, _) => await RunAsync(async () =>
        {
            IReadOnlyList<CharacterCreationAttributeAllocation> allocations = _draft.Allocations(state);
            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> result =
                Coordinator.PreviewCreationAttributes(state.Binding, allocations);
            if (result.Value is { } preview
                && CreationAttributesPhoneAuthority.CanConfirmPreview(
                    state,
                    Coordinator.State,
                    preview,
                    allocations))
            {
                _previewBlockers = [];
                await Navigation.PushAsync(new CreationAttributesPreviewPage(
                    Coordinator,
                    preview,
                    allocations));
                return;
            }

            _previewBlockers = result.Value?.Blockers.Count > 0
                ? result.Value.Blockers
                : result.Blockers.Count > 0
                    ? result.Blockers
                    : [CharacterCreationAttributesBlockers.AuthorityUnavailable];
        });
        _body.Add(review);

        Label note = NativeTheme.Body(
            CreationAllocationStrings.Get(
                "Attributes.ReviewBoundary",
                "Review creates no character write. Confirmation stores only a typed auxiliary draft for the final composed creation transaction."),
            NativeTheme.Muted);
        note.AutomationId = "creation-attributes-draft-only-notice";
        _body.Add(note);
    }

    private void AddBudgetCard(CharacterCreationBudgetState budget, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Title(budget.Label, 18));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Total", "Total"),
            FormatBudget(budget.Total, budget.Unit)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Used", "Used"),
            FormatBudget(budget.Used, budget.Unit)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Remaining", "Remaining"),
            FormatBudget(budget.Remaining, budget.Unit)));
        card.Add(NativeTheme.Body(
            budget.IsExact
                ? CreationAllocationStrings.Get("Common.ExactCoreBudget", "Exact Core budget")
                : CreationAllocationStrings.Get("Common.BudgetNotExact", "Budget is not exact"),
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            CreationAllocationStrings.Format(
                "Common.BudgetSemanticDescription",
                "{0}. Total {1}. Used {2}. Remaining {3}.",
                budget.Label,
                FormatBudget(budget.Total, budget.Unit),
                FormatBudget(budget.Used, budget.Unit),
                FormatBudget(budget.Remaining, budget.Unit)));
        _body.Add(border);
    }

    private void AddBlockers(IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get("Common.Blockers", "Blockers")));
        foreach (string blocker in blockers
                     .Where(blocker => !string.IsNullOrWhiteSpace(blocker))
                     .Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _body.Add(label);
    }

    internal static string AttributeLabel(string attributeId)
        => CreationAllocationStrings.AttributeName(attributeId);

    internal static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    internal static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-').ToArray());
}

/// <summary>
/// One-attribute drill-in. Candidate +/- operations are enabled only when Core returns an
/// adoptable immutable preview for the complete allocation ledger.
/// </summary>
public sealed class CreationAttributeAllocationPage : NativePageBase
{
    private readonly CreationAttributesPhoneDraft _draft;
    private readonly string _attributeId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationAttributeAllocationPage(
        RunnerSessionCoordinator coordinator,
        CreationAttributesPhoneDraft draft,
        string attributeId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _attributeId = string.IsNullOrWhiteSpace(attributeId)
            ? throw new ArgumentException("A typed Attribute ID is required.", nameof(attributeId))
            : attributeId;
        Title = CreationAttributesPage.AttributeLabel(attributeId);
        AutomationId = "creation-attribute-allocation-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "AttributeAllocation.Eyebrow",
            "Attribute allocation")));
        _body.Add(NativeTheme.Title(CreationAttributesPage.AttributeLabel(_attributeId)));

        CharacterCreationFoundationResult<CharacterCreationAttributesState> load =
            Coordinator.LoadCreationAttributes();
        if (load.Value is not { } state
            || !_draft.Matches(state, Coordinator.State)
            || _draft.Attribute(state, _attributeId) is not { } attribute)
        {
            Label stale = NativeTheme.Body(
                load.Blockers.FirstOrDefault()
                ?? CharacterCreationAttributesBlockers.StaleWorkspaceRevision,
                NativeTheme.Danger);
            stale.AutomationId = "creation-attribute-allocation-stale";
            _body.Add(NativeTheme.Card(stale));
            return;
        }

        AddProjection(attribute);
        AddBudgetSummary(state);
        AddAdjustment(
            state,
            CreationAllocationStrings.Get("AttributeAllocation.PriorityDecrease", "Priority point −"),
            -1,
            0,
            "priority-decrease");
        AddAdjustment(
            state,
            CreationAllocationStrings.Get("AttributeAllocation.PriorityIncrease", "Priority point +"),
            1,
            0,
            "priority-increase");
        AddAdjustment(
            state,
            CreationAllocationStrings.Get("AttributeAllocation.KarmaDecrease", "Karma level −"),
            0,
            -1,
            "karma-decrease");
        AddAdjustment(
            state,
            CreationAllocationStrings.Get("AttributeAllocation.KarmaIncrease", "Karma level +"),
            0,
            1,
            "karma-increase");
        AddSources(attribute);
    }

    private void AddProjection(CharacterCreationAttributeProjection attribute)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.TypedId", "Typed ID"),
            attribute.AttributeId));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Category", "Category"),
            attribute.Category));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.Current", "Current"),
            attribute.Current.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.NaturalRange", "Natural range"),
            $"{attribute.Minimum.ToString(CultureInfo.InvariantCulture)}–{attribute.Maximum.ToString(CultureInfo.InvariantCulture)}"));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.AugmentedMaximum", "Augmented maximum"),
            attribute.AugmentedMaximum.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.PriorityPoints", "Priority points"),
            attribute.PriorityPointsSpent.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.KarmaLevels", "Karma levels"),
            attribute.KarmaLevels.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.KarmaCost", "Karma cost"),
            attribute.KarmaCost.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-attribute-allocation-projection";
        _body.Add(border);
    }

    private void AddBudgetSummary(CharacterCreationAttributesState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.NormalPointsLeft", "Normal points left"),
            _draft.NormalBudget(state).Remaining.ToString("0.##", CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.SpecialPointsLeft", "Special points left"),
            _draft.SpecialBudget(state).Remaining.ToString("0.##", CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.CreationKarmaLeft", "Creation Karma left"),
            _draft.KarmaBudget(state).Remaining.ToString("0.##", CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-attribute-allocation-budgets";
        _body.Add(border);
    }

    private void AddAdjustment(
        CharacterCreationAttributesState state,
        string label,
        int priorityDelta,
        int karmaDelta,
        string automationToken)
    {
        IReadOnlyList<CharacterCreationAttributeAllocation>? allocations =
            _draft.ChangedAllocations(state, _attributeId, priorityDelta, karmaDelta);
        CharacterCreationFoundationResult<CharacterCreationAttributesPreview>? result =
            allocations is null
                ? null
                : Coordinator.PreviewCreationAttributes(state.Binding, allocations);
        bool enabled = allocations is not null
                       && result is not null
                       && CreationAttributesPhoneAuthority.CanAdoptPreview(
                           state,
                           Coordinator.State,
                           result,
                           allocations);
        Button button = NativeTheme.SecondaryButton(label);
        button.AutomationId = $"creation-attribute-{automationToken}-{CreationAttributesPage.Token(_attributeId)}";
        button.IsEnabled = enabled;
        if (enabled)
        {
            button.Clicked += async (_, _) => await RunAsync(() =>
            {
                _draft.TryAdopt(state, Coordinator.State, result!, allocations!);
                return Task.CompletedTask;
            });
        }
        _body.Add(button);

        if (!enabled && allocations is not null)
        {
            string? blocker = result?.Value?.Blockers.FirstOrDefault()
                              ?? result?.Blockers.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(blocker))
            {
                Label reason = NativeTheme.Body(CreationAllocationStrings.Format(
                    "Common.ActionBlocker",
                    "{0}: {1}",
                    label,
                    blocker), NativeTheme.Muted);
                reason.AutomationId = $"{button.AutomationId}-reason";
                _body.Add(reason);
            }
        }
    }

    private void AddSources(CharacterCreationAttributeProjection attribute)
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.SourceAuthority",
            "Source authority")));
        foreach (string anchor in attribute.SourceAnchorIds)
            _body.Add(NativeTheme.Body(anchor, NativeTheme.Muted));
    }
}

/// <summary>
/// Immutable Core preview plus an explicit, digest-bound auxiliary draft confirmation.
/// </summary>
public sealed class CreationAttributesPreviewPage : NativePageBase
{
    private readonly CharacterCreationAttributesPreview _preview;
    private readonly IReadOnlyList<CharacterCreationAttributeAllocation> _allocations;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CreationAttributesPhoneConfirmResult? _confirmation;

    internal CreationAttributesPreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations) : base(coordinator)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _allocations = allocations?.ToArray()
            ?? throw new ArgumentNullException(nameof(allocations));
        Title = CreationAllocationStrings.Get("AttributesPreview.PageTitle", "Review Attributes");
        AutomationId = "creation-attributes-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.ExplicitReview",
            "Explicit review")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "AttributeAllocation.Eyebrow",
            "Attribute allocation")));
        Label binding = NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Common.PreviewBinding",
                "Revision {0} · saved {1} · preview {2}",
                _preview.Binding.ContentRevision,
                _preview.Binding.SavedRevision,
                CreationPrerequisiteDigestText.CanonicalPrefix(_preview.PreviewDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-attributes-preview-binding";
        _body.Add(binding);
        AddDigest("creation-attributes-preview-digest", _preview.PreviewDigest);
        AddDigest("creation-attributes-preview-raw-character-xml-digest", _preview.Binding.RawCharacterXmlDigest);
        AddDigest("creation-attributes-preview-auxiliary-state-digest", _preview.Binding.AuxiliaryStateDigest);
        AddBudgets();
        AddAttributes();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
    }

    private void AddBudgets()
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "AttributesPreview.FinalDraftLedgers",
            "Final draft ledgers")));
        foreach (CharacterCreationBudgetState budget in new[]
                 {
                     _preview.NormalPointBudget,
                     _preview.SpecialPointBudget,
                     _preview.CreationKarmaBudget
                 })
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(budget.Label, 18));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Total", "Total"),
                CreationAttributesPage.FormatBudget(budget.Total, budget.Unit)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Used", "Used"),
                CreationAttributesPage.FormatBudget(budget.Used, budget.Unit)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Remaining", "Remaining"),
                CreationAttributesPage.FormatBudget(budget.Remaining, budget.Unit)));
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-attributes-preview-budget-{CreationAttributesPage.Token(budget.BudgetId)}";
            _body.Add(border);
        }
    }

    private void AddAttributes()
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "AttributesPreview.TypedAllocations",
            "Typed allocations")));
        foreach (CharacterCreationAttributeProjection attribute in _preview.Attributes)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(CreationAttributesPage.AttributeLabel(attribute.AttributeId), 18));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.TypedId", "Typed ID"),
                attribute.AttributeId));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Value", "Value"),
                attribute.Current.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.PriorityPoints", "Priority points"),
                attribute.PriorityPointsSpent.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.KarmaLevels", "Karma levels"),
                attribute.KarmaLevels.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.KarmaCost", "Karma cost"),
                attribute.KarmaCost.ToString(CultureInfo.InvariantCulture)));
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-attributes-preview-attribute-{CreationAttributesPage.Token(attribute.AttributeId)}";
            _body.Add(border);
        }
    }

    private void AddBlockers()
    {
        string[] blockers = _preview.Blockers
            .Concat(_confirmation?.Blockers ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (blockers.Length == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get("Common.Blockers", "Blockers")));
        foreach (string blocker in blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-attributes-preview-blockers";
        _body.Add(border);
    }

    private void AddConfirmation()
    {
        if (_confirmation is
            {
                Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: not null,
                RefreshedState: not null
            })
        {
            Label complete = NativeTheme.Body(CreationAllocationStrings.Get(
                "AttributesPreview.Confirmed",
                "Attributes draft confirmed and authoritative state reloaded."));
            complete.AutomationId = "creation-attributes-confirmed";
            _body.Add(NativeTheme.Card(complete));
            return;
        }

        CharacterCreationFoundationResult<CharacterCreationAttributesState> live =
            Coordinator.LoadCreationAttributes();
        bool canConfirm = live.Value is { } state
                          && CreationAttributesPhoneAuthority.CanConfirmPreview(
                              state,
                              Coordinator.State,
                              _preview,
                              _allocations);
        Button confirm = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "AttributesPreview.Confirm",
            "Confirm Attributes draft"));
        confirm.AutomationId = "creation-attributes-confirm";
        confirm.IsEnabled = canConfirm;
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            _confirmation = await Coordinator.ConfirmCreationAttributesAsync(
                _preview,
                _allocations);
        });
        _body.Add(confirm);

        Label explicitAction = NativeTheme.Body(
            CreationAllocationStrings.Get(
                "AttributesPreview.ConfirmationBoundary",
                "Confirmation is bound to this exact preview digest and stores no character effects before finalization."),
            NativeTheme.Muted);
        explicitAction.AutomationId = "creation-attributes-explicit-confirmation";
        _body.Add(explicitAction);
    }

    private void AddReceipt()
    {
        if (_confirmation is not
            {
                Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: { } receipt,
                RefreshedState: { } refreshed
            })
        {
            return;
        }

        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.AtomicDraftReceipt",
            "Atomic draft receipt")));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.PreviousRevision", "Previous revision"),
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.ContentRevision", "Content revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.SavedRevision", "Saved revision"),
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.DraftRevision", "Draft revision"),
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributesPreview.NormalPointsRemaining", "Normal points remaining"),
            receipt.NormalPointsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributesPreview.SpecialPointsRemaining", "Special points remaining"),
            receipt.SpecialPointsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributesPreview.CreationKarmaRemaining", "Creation Karma remaining"),
            receipt.CreationKarmaRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.CharacterDocumentChanged", "Character document changed"),
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddReceiptDigest(card, "creation-attributes-receipt-draft-digest", receipt.DraftDigest);
        AddReceiptDigest(card, "creation-attributes-receipt-raw-character-xml-digest", refreshed.Binding.RawCharacterXmlDigest);
        AddReceiptDigest(card, "creation-attributes-receipt-auxiliary-state-digest", refreshed.Binding.AuxiliaryStateDigest);
        card.Add(NativeTheme.Body(
            refreshed.PendingDraft?.CharacterEffectsApplied == false
                ? CreationAllocationStrings.Get(
                    "AttributesPreview.DurablePendingFinalization",
                    "Typed Attributes are durable; character effects remain pending the final composed creation transaction.")
                : CreationAllocationStrings.Get(
                    "Common.CharacterEffectStateUnsafe",
                    "Character-effect state is not safe to continue."),
            refreshed.PendingDraft?.CharacterEffectsApplied == false ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-attributes-confirm-receipt";
        _body.Add(border);

        Button back = NativeTheme.SecondaryButton(CreationAllocationStrings.Get(
            "Common.BackToBuild",
            "Back to Build"));
        back.AutomationId = "creation-attributes-back-to-build";
        back.Clicked += async (_, _) => await BackToBuildAsync();
        _body.Add(back);
    }

    private async Task BackToBuildAsync()
    {
        await Navigation.PopAsync(animated: false);
        if (Navigation.NavigationStack.LastOrDefault() is CreationAttributesPage)
            await Navigation.PopAsync();
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _body.Add(label);
    }

    private static void AddReceiptDigest(
        VerticalStackLayout card,
        string automationId,
        string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        card.Add(label);
    }
}
