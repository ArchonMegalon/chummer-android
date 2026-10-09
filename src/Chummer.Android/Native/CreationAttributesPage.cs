using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Phone deep-navigation entry for Core's draft-only SR5 priority-table Attributes authority.
/// </summary>
public sealed class CreationAttributesPage : NativePageBase
{
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly CreationAttributesPhoneDraft _draft = new();
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _previewBlockers = [];
    private CharacterCreationAttributesState? _authority;
    private CharacterCreationAttributesState? _revalidationAuthority;
    private readonly ScrollView _scroll;
    private Label? _normalAttributesHeading;
    private Label? _specialAttributesHeading;
    private Button? _reviewButton;
    private CancellationTokenSource? _reviewPreparation;

    public CreationAttributesPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationAttributesState? authority = null) : base(coordinator)
    {
        _authority = authority;
        _revalidationAuthority = authority;
        Title = CreationAllocationStrings.Get("Attributes.PageTitle", "Attributes");
        AutomationId = "creation-attributes-page";
        Content = _scroll = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        // Keep the pressed review control visible while its read runs off-thread.
        // The action gate excludes other mutations; leaving cancels this read.
        if (_reviewPreparation is not null) return;
        _reviewButton = null;
        _body.Clear();
        // The previous disclosure still owns its native child after _body.Clear().
        // Never attach that child to a new parent during a refresh.
        _technicalDetails = new() { Spacing = 6 };
        _normalAttributesHeading = null;
        _specialAttributesHeading = null;
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.CharacterCreation",
            "Character creation")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "Attributes.Heading",
            "Allocate Attributes")));
        _body.Add(NativeTheme.Body(
            CreationAllocationStrings.Get(
                "Attributes.Intro",
                "Choose an attribute to spend points. Normal and special attributes use separate pools."),
            NativeTheme.Muted));

        CharacterCreationFoundationResult<CharacterCreationAttributesState>? load = null;
        CharacterCreationAttributesState? state = CreationPageAuthorityCache.Resolve(
            _authority,
            candidate => Coordinator.IsCreationAttributesStateCurrent(candidate)
                         && CreationAttributesPhoneAuthority.IsReady(candidate, Coordinator.State),
            () =>
            {
                load = _revalidationAuthority is { } original
                    ? Coordinator.RevalidateCreationAttributes(original)
                    : Coordinator.LoadCreationAttributes();
                return string.Equals(
                        load.Outcome,
                        CharacterCreationFoundationOutcomes.Success,
                        StringComparison.Ordinal)
                    ? load.Value
                    : null;
            });
        _authority = state;
        _revalidationAuthority ??= state;
        if (state is null)
        {
            AddBlockers(
                load is { Blockers.Count: > 0 }
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
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-attributes-details"));
    }

    protected override void OnDisappearing()
    {
        var pending = _reviewPreparation;
        _reviewPreparation = null;
        _reviewButton = null;
        pending?.Cancel();
        base.OnDisappearing();
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
        _technicalDetails.Add(binding);
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
            "Available points")));
        AddBudgetCard(
            _draft.NormalBudget(state),
            CharacterCreationBudgetIds.NormalAttributes, "creation-attributes-budget-normal", state);
        AddBudgetCard(
            _draft.SpecialBudget(state),
            CharacterCreationBudgetIds.SpecialAttributes, "creation-attributes-budget-special", state, CharacterCreationAttributeCategories.Special);
        AddBudgetCard(
            _draft.KarmaBudget(state),
            CharacterCreationBudgetIds.Karma, "creation-attributes-budget-karma");
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
            "Saved allocation")));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.DraftRevision", "Draft revision"),
            pending.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        Label digest = NativeTheme.Body(pending.DraftDigest, NativeTheme.Muted);
        digest.AutomationId = "creation-attributes-pending-draft-digest";
        _technicalDetails.Add(digest);
        card.Add(NativeTheme.Body(
            pending.CharacterEffectsApplied
                ? CreationAllocationStrings.Get(
                    "Attributes.UnexpectedAppliedEffects",
                    "Unexpected applied character effects; editing remains fail-closed.")
                : CreationAllocationStrings.Get(
                    "Attributes.ResumedDraftDetail",
                    "Your saved allocation is restored. You can still change it before finishing character creation."),
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
        Label heading = NativeTheme.Eyebrow(label);
        if (category == CharacterCreationAttributeCategories.Normal)
        {
            heading.AutomationId = "creation-attributes-normal-heading";
            _normalAttributesHeading = heading;
        }
        else if (category == CharacterCreationAttributeCategories.Special)
        {
            heading.AutomationId = "creation-attributes-special-heading";
            _specialAttributesHeading = heading;
        }
        _body.Add(heading);
        if (category == CharacterCreationAttributeCategories.Special)
            _body.Add(NativeTheme.Body(CreationAllocationStrings.Get(
                "Attributes.SpecialHelp",
                "Choose Edge, Magic or Resonance below to spend special attribute points. Availability and limits depend on your metatype and talent."), NativeTheme.Muted));
        foreach (CharacterCreationAttributeProjection attribute in _draft.Attributes(state)
                     .Where(attribute => string.Equals(
                         attribute.Category,
                         category,
                         StringComparison.Ordinal)))
        {
            string detail = attribute.IsEnabled
                ? string.Format(CultureInfo.CurrentUICulture,
                    attribute.Category == CharacterCreationAttributeCategories.Special
                        ? CreationAllocationStrings.Get("Attributes.SpecialDetail",
                            "{0} · range {1}–{2} · special points {3} · Karma levels {4} ({5} karma)")
                        : CreationAllocationStrings.Get("Attributes.AttributeDetail",
                            "{0} · range {1}–{2} · Priority {3} · Karma levels {4} ({5} karma)"),
                    attribute.Current,
                    attribute.Minimum,
                    attribute.Maximum,
                    attribute.PriorityPointsSpent,
                    attribute.KarmaLevels,
                    attribute.KarmaCost)
                : string.Join(
                    " · ",
                    attribute.DisableReasons.Select(CreationAllocationStrings.AttributeBlocker).DefaultIfEmpty(CreationAllocationStrings.Get(
                        "Attributes.NotEnabledByTalent",
                        "Not enabled by this Talent")));
            foreach (string reason in attribute.DisableReasons)
                _technicalDetails.Add(NativeTheme.Body(attribute.AttributeId + ": " + reason, NativeTheme.Muted));
            _body.Add(NativeTheme.NavigationRow(
                AttributeLabel(attribute.AttributeId),
                detail,
                () => !Coordinator.IsCreationAttributesStateCurrent(state) ? Task.CompletedTask
                    : Navigation.PushAsync(new CreationAttributeAllocationPage(
                    Coordinator,
                    _draft,
                    attribute.AttributeId,
                    state)),
                enabled: attribute.IsEnabled,
                automationId: $"creation-attributes-open-{Token(attribute.AttributeId)}",
                value: attribute.Current.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private void AddReviewAction(CharacterCreationAttributesState state)
    {
        Button review = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "Attributes.ReviewExact",
            "Review allocation"));
        review.AutomationId = "creation-attributes-prepare-preview";
        review.IsEnabled = Coordinator.IsCreationAttributesStateCurrent(state) && _draft.Matches(state, Coordinator.State);
        _reviewButton = review;
        long appearance = CaptureAppearanceGeneration();
        review.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!ReferenceEquals(_reviewButton, review) || !IsCurrentAppearanceGeneration(appearance)
                || !Coordinator.IsCreationAttributesStateCurrent(state) || !_draft.Matches(state, Coordinator.State)) return;
            var original = Coordinator.State;
            var allocations = _draft.Allocations(state).ToArray();
            using var lifetime = new CancellationTokenSource();
            _reviewPreparation = lifetime;
            string label = review.Text;
            review.IsEnabled = false;
            review.Text = CreationAllocationStrings.Get("Attributes.ReviewPreparing", "Checking your choices…");
            try
            {
                var result = await Task.Run(() => Coordinator.ReadCreationAuthority(original,
                    () => Coordinator.PreviewCreationAttributes(state.Binding, allocations), lifetime.Token), lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                if (!IsCurrentAppearanceGeneration(appearance) || !ReferenceEquals(_reviewButton, review)
                    || !Coordinator.IsCreationAttributesStateCurrent(state) || !_draft.Matches(state, Coordinator.State)
                    || !_draft.Allocations(state).SequenceEqual(allocations)) return;
                if (result.Value is { } preview
                    && Coordinator.CanOfferCreationAttributesConfirmation(preview, allocations))
                {
                    _previewBlockers = [];
                    await Navigation.PushAsync(new CreationAttributesPreviewPage(
                        Coordinator, preview, allocations,
                        confirmed =>
                        {
                            if (confirmed is { Outcome: CharacterCreationFoundationOutcomes.Success,
                                    Receipt: { } receipt, RefreshedState: { } fresh, Blockers.Count: 0 }
                                && Coordinator.IsCreationAttributesReceiptCurrent(receipt)
                                && Coordinator.IsCreationAttributesStateCurrent(fresh))
                                _authority = _revalidationAuthority = fresh;
                        }));
                    return;
                }
                _previewBlockers = result.Value?.Blockers.Count > 0
                    ? result.Value.Blockers
                    : result.Blockers.Count > 0 ? result.Blockers
                        : [CharacterCreationAttributesBlockers.AuthorityUnavailable];
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_reviewButton, review))
                    _previewBlockers = [Coordinator.IsCreationAttributesStateCurrent(state)
                        ? CharacterCreationAttributesBlockers.AuthorityUnavailable
                        : CharacterCreationAttributesBlockers.StaleWorkspaceRevision];
            }
            finally
            {
                // Never enable a retained button. A fresh render owns any retry.
                review.Text = label;
                if (ReferenceEquals(_reviewPreparation, lifetime)) _reviewPreparation = null;
            }
        });
        _body.Add(review);

        Label note = NativeTheme.Body(
            CreationAllocationStrings.Get(
                "Attributes.ReviewBoundary",
                "Review your choices, then confirm to save this creation draft."),
            NativeTheme.Muted);
        note.AutomationId = "creation-attributes-draft-only-notice";
        _body.Add(note);
    }

    private void AddBudgetCard(CharacterCreationBudgetState budget, string canonicalBudgetId, string automationId,
        CharacterCreationAttributesState? scrollAuthority = null,
        string category = CharacterCreationAttributeCategories.Normal)
    {
        string label = BuildPageUiProjection.BudgetLabel(budget, canonicalBudgetId);
        VerticalStackLayout card = new() { Spacing = 6 };
        if (scrollAuthority is null)
            card.Add(NativeTheme.Title(label, 18));
        else
        {
            Button jump = NativeTheme.ReadingButton(label + " ↓");
            jump.AutomationId = automationId + "-jump";
            SemanticProperties.SetDescription(jump, label);
            long appearance = CaptureAppearanceGeneration();
            jump.Clicked += async (_, _) =>
            {
                Label? target = category == CharacterCreationAttributeCategories.Special
                    ? _specialAttributesHeading : _normalAttributesHeading;
                if (!IsCurrentAppearanceGeneration(appearance) || !ReferenceEquals(card.Parent?.Parent, _body)
                    || !Coordinator.IsCreationAttributesStateCurrent(scrollAuthority)
                    || target is not { Parent: not null }) return;
                await _scroll.ScrollToAsync(target, ScrollToPosition.Start, animated: true);
            };
            card.Add(jump);
        }
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
                label,
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
        {
            card.Add(NativeTheme.Body(CreationAllocationStrings.AttributeBlocker(blocker), NativeTheme.Danger));
            _technicalDetails.Add(NativeTheme.Body(blocker, NativeTheme.Muted));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _technicalDetails.Add(label);
    }

    internal static string AttributeLabel(string attributeId)
        => CreationAllocationStrings.AttributeName(attributeId);

    internal static string FormatBudget(decimal value, string unit)
    {
        string label = string.IsNullOrWhiteSpace(unit) ? string.Empty
            : PhoneStrings.Get("CreationUnit." + unit, unit);
        return $"{value.ToString("0.##", CultureInfo.CurrentCulture)} {label}".TrimEnd();
    }

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
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly CreationAttributesPhoneDraft _draft;
    private readonly string _attributeId;
    private readonly CharacterCreationAttributesState? _originalAuthority;
    private sealed record Adjustment(string Token,
        IReadOnlyList<CharacterCreationAttributeAllocation>? Allocations,
        CharacterCreationFoundationResult<CharacterCreationAttributesPreview>? Result, bool Enabled);
    private sealed record PreparedAllocation(CharacterCreationAttributesState State,
        IReadOnlyList<CharacterCreationAttributeAllocation> Allocations,
        IReadOnlyList<Adjustment> Adjustments);
    private PreparedAllocation? _prepared;
    private CancellationTokenSource? _preparation;
    private long _preparationGeneration;
    private bool _loading;
    private bool _canRetryPreparation;
    private Button? _retryButton;
    private string? _failure;
    private readonly Label _progress = new()
    {
        Text = CreationAllocationStrings.Get("AttributeAllocation.Checking", "Checking points…"),
        TextColor = NativeTheme.Text, FontSize = 16, IsVisible = false,
        AutomationId = "creation-attribute-allocation-loading"
    };
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationAttributeAllocationPage(
        RunnerSessionCoordinator coordinator,
        CreationAttributesPhoneDraft draft,
        string attributeId,
        CharacterCreationAttributesState? originalAuthority = null) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _originalAuthority = originalAuthority;
        _attributeId = string.IsNullOrWhiteSpace(attributeId)
            ? throw new ArgumentException("A typed Attribute ID is required.", nameof(attributeId))
            : attributeId;
        Title = CreationAttributesPage.AttributeLabel(attributeId);
        AutomationId = "creation-attribute-allocation-page";
        Content = new ScrollView { Content = _body };
    }

    protected override Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
        => PrepareAsync(cancellationToken);

    protected override void OnDisappearing()
    {
        ++_preparationGeneration;
        _preparation?.Cancel();
        _prepared = null;
        _loading = false;
        _canRetryPreparation = false;
        _retryButton = null;
        _progress.IsVisible = false;
        base.OnDisappearing();
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        long generation = ++_preparationGeneration;
        _preparation?.Cancel();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _preparation = lifetime;
        CancellationToken ct = lifetime.Token;
        _prepared = null;
        _canRetryPreparation = false;
        _retryButton = null;
        _failure = null;
        _loading = true;
        if (_originalAuthority is null || !Coordinator.IsCreationAttributesStateCurrent(_originalAuthority))
            _body.Clear();
        Refresh();
        var original = Coordinator.State;
        var draft = _draft.Copy();
        bool settled = false;
        try
        {
            var prepared = await Task.Run(() => Coordinator.ReadCreationAuthority(original, () =>
            {
                ct.ThrowIfCancellationRequested();
                var load = _originalAuthority is { } authority
                    ? Coordinator.RevalidateCreationAttributes(authority)
                    : new CharacterCreationFoundationResult<CharacterCreationAttributesState>(
                        CharacterCreationFoundationOutcomes.Blocked, null,
                        [CharacterCreationAttributesBlockers.WorkspaceUnavailable]);
                if (load.Value is not { } state || !draft.Matches(state, original))
                    return (Value: (PreparedAllocation?)null, Failure: load.Blockers.FirstOrDefault());
                var choices = new List<Adjustment>();
                foreach (var (token, priority, karma) in new[]
                {
                    ("priority-decrease", -1, 0), ("priority-increase", 1, 0),
                    ("karma-decrease", 0, -1), ("karma-increase", 0, 1)
                })
                {
                    ct.ThrowIfCancellationRequested();
                    var allocations = draft.ChangedAllocations(state, _attributeId, priority, karma);
                    var result = allocations is null ? null
                        : Coordinator.PreviewCreationAttributes(state.Binding, allocations);
                    bool enabled = allocations is not null && result is not null
                        && CreationAttributesPhoneAuthority.CanAdoptPreview(state, original, result, allocations);
                    choices.Add(new(token, allocations, result, enabled));
                }
                return (Value: new PreparedAllocation(state, draft.Allocations(state), choices), Failure: (string?)null);
            }, ct), ct);
            ct.ThrowIfCancellationRequested();
            if (generation != _preparationGeneration) return;
            _failure = prepared.Failure;
            // Owner transition, navigation and local draft edits all invalidate a late result.
            if (prepared.Value is { } value && IsCurrent(value)) _prepared = value;
            _canRetryPreparation = prepared.Value is null;
            settled = true;
        }
        catch (Exception error) when (error is not OutOfMemoryException && !ct.IsCancellationRequested)
        {
            if (generation == _preparationGeneration)
            {
                // This operation only reads previews; the already admitted local
                // draft is retained. Never expose an exception message as UI copy.
                _failure = error.GetType().Name;
                _canRetryPreparation = true;
                settled = true;
            }
        }
        finally
        {
            if (generation == _preparationGeneration)
            {
                _loading = false;
                _progress.IsVisible = false;
                if (settled && _prepared is null) Refresh();
            }
            if (ReferenceEquals(_preparation, lifetime)) _preparation = null;
        }
    }

    private bool IsCurrent(PreparedAllocation prepared)
        => Coordinator.IsCreationAttributesStateCurrent(prepared.State)
           && _draft.Matches(prepared.State, Coordinator.State)
           && _draft.Allocations(prepared.State).SequenceEqual(prepared.Allocations);

    protected override void Refresh()
    {
        // A plus/minus preparation must not collapse the scrolled page.
        // Keep the old projection visible but non-actionable until the fresh one is ready.
        if (_loading && _body.Children.Count > 0)
        {
            foreach (Button button in _body.Children.OfType<Button>()) button.IsEnabled = false;
            _progress.IsVisible = true;
            return;
        }
        _body.Clear();
        _retryButton = null;
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "AttributeAllocation.Eyebrow",
            "Attribute allocation")));
        _body.Add(NativeTheme.Title(CreationAttributesPage.AttributeLabel(_attributeId)));
        _progress.IsVisible = _loading;
        _body.Add(_progress);

        if (_loading)
        {
            return;
        }
        if (_prepared is not { } prepared || !IsCurrent(prepared)
            || _draft.Attribute(prepared.State, _attributeId) is not { } attribute)
        {
            AddUnavailable();
            return;
        }

        var state = prepared.State;
        AddProjection(attribute);
        AddBudgetSummary(state);
        AddAdjustment(
            state,
            attribute.Category == CharacterCreationAttributeCategories.Special
                ? CreationAllocationStrings.Get("AttributeAllocation.SpecialDecrease", "Special point −")
                : CreationAllocationStrings.Get("AttributeAllocation.PriorityDecrease", "Priority point −"),
            "priority-decrease");
        AddAdjustment(
            state,
            attribute.Category == CharacterCreationAttributeCategories.Special
                ? CreationAllocationStrings.Get("AttributeAllocation.SpecialIncrease", "Special point +")
                : CreationAllocationStrings.Get("AttributeAllocation.PriorityIncrease", "Priority point +"),
            "priority-increase");
        AddAdjustment(
            state,
            CreationAllocationStrings.Get("AttributeAllocation.KarmaDecrease", "Karma level −"),
            "karma-decrease");
        AddAdjustment(
            state,
            CreationAllocationStrings.Get("AttributeAllocation.KarmaIncrease", "Karma level +"),
            "karma-increase");
        AddSources(attribute);
    }

    private bool CanRetryPreparation()
        => _canRetryPreparation && !_loading && _prepared is null
           && _originalAuthority is { } authority
           && Coordinator.IsCreationAttributesStateCurrent(authority)
           && _draft.Matches(authority, Coordinator.State);

    private void AddUnavailable()
    {
        bool canRetry = CanRetryPreparation();
        Label message = NativeTheme.Body(canRetry
            ? CreationAllocationStrings.Get("AttributeAllocation.CheckFailed",
                "Couldn't check the points. Your choices are still in this draft; nothing was saved. Check again to continue.")
            : CreationAllocationStrings.Get("AttributeAllocation.Reopen",
                "These choices are no longer current. Return to Attributes and reopen this editor."),
            NativeTheme.Danger);
        message.AutomationId = "creation-attribute-allocation-stale";
        _body.Add(NativeTheme.Card(message));
        if (canRetry)
        {
            long generation = _preparationGeneration;
            string label = CreationAllocationStrings.Get("AttributeAllocation.CheckAgain", "Check again");
            Button retry = NativeTheme.SecondaryButton(label);
            retry.AutomationId = "creation-attribute-allocation-retry";
            _retryButton = retry;
            retry.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (!ReferenceEquals(_retryButton, retry) || generation != _preparationGeneration
                    || !CanRetryPreparation()) return;
                retry.Text = CreationAllocationStrings.Get("AttributeAllocation.Checking", "Checking points…");
                // Rebuild action previews for this exact draft; do not replay +/-.
                try { await PrepareAsync(CancellationToken.None); }
                finally { retry.Text = label; }
            });
            _body.Add(retry);
        }
        if (!string.IsNullOrWhiteSpace(_failure))
        {
            _technicalDetails.Add(NativeTheme.Body(_failure, NativeTheme.Muted));
            _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-attribute-allocation-details"));
        }
    }

    private void AddProjection(CharacterCreationAttributeProjection attribute)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.TypedId", "Typed ID"),
            attribute.AttributeId));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Category", "Category"),
            attribute.Category));
        Label current = NativeTheme.Title(attribute.Current.ToString(CultureInfo.InvariantCulture), 32);
        current.AutomationId = "creation-attribute-allocation-current";
        SemanticProperties.SetDescription(current, CreationAllocationStrings.Get("AttributeAllocation.Current", "Current")
            + " " + attribute.Current.ToString(CultureInfo.InvariantCulture));
        card.Add(current);
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.NaturalRange", "Natural range"),
            $"{attribute.Minimum.ToString(CultureInfo.InvariantCulture)}–{attribute.Maximum.ToString(CultureInfo.InvariantCulture)}"));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("AttributeAllocation.AugmentedMaximum", "Augmented maximum"),
            attribute.AugmentedMaximum.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            attribute.Category == CharacterCreationAttributeCategories.Special
                ? CreationAllocationStrings.Get("AttributeAllocation.SpecialSpent", "Special points spent")
                : CreationAllocationStrings.Get("Common.PriorityPoints", "Priority points"),
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
        string automationToken)
    {
        var prepared = _prepared!;
        var choice = prepared.Adjustments.Single(item => item.Token == automationToken);
        var allocations = choice.Allocations;
        var result = choice.Result;
        bool enabled = choice.Enabled;
        Button button = NativeTheme.SecondaryButton(label);
        button.AutomationId = $"creation-attribute-{automationToken}-{CreationAttributesPage.Token(_attributeId)}";
        button.IsEnabled = enabled;
        if (enabled)
        {
            button.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (ReferenceEquals(_prepared, prepared) && IsCurrent(prepared)
                    && _draft.TryAdopt(state, Coordinator.State, result!, allocations!))
                {
                    // Keep feedback on the control the user just pressed: the page's
                    // loading message can be above the current scroll position.
                    button.Text = CreationAllocationStrings.Get(
                        "AttributeAllocation.Checking", "Checking points…");
                    try { await PrepareAsync(CancellationToken.None); }
                    finally
                    {
                        // Never re-enable this retained control. Only a newly rendered,
                        // current Core preview may admit the next adjustment.
                        button.Text = label;
                    }
                }
            });
        }
        _body.Add(button);

        if (!enabled && allocations is not null)
        {
            var blockers = (result?.Value?.Blockers ?? result?.Blockers ?? [])
                .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.Ordinal).ToArray();
            if (blockers.Length > 0)
            {
                var projected = result?.Value?.Attributes.SingleOrDefault(item => item.AttributeId == _attributeId);
                // Explain Core's rejected projection; this never admits an allocation
                // or substitutes a locally calculated cap for the rules authority.
                string explanation = string.Join(" ", blockers.Select(blocker =>
                    blocker == CharacterCreationAttributesBlockers.AllocationInvalid
                    && projected is { IsEnabled: true } && projected.Current > projected.Maximum
                        ? CreationAllocationStrings.Format("Attributes.MaximumReached",
                            "{0} has reached its creation maximum of {1}. Remaining points cannot raise it further.",
                            CreationAttributesPage.AttributeLabel(_attributeId), projected.Maximum)
                        : CreationAllocationStrings.AttributeBlocker(blocker)).Distinct(StringComparer.Ordinal));
                Label reason = NativeTheme.Body(CreationAllocationStrings.Format(
                    "Common.ActionBlocker",
                    "{0}: {1}",
                    label,
                    explanation), NativeTheme.Muted);
                reason.AutomationId = $"{button.AutomationId}-reason";
                _body.Add(reason);
                foreach (string blocker in blockers)
                    _technicalDetails.Add(NativeTheme.Body(blocker, NativeTheme.Muted));
            }
        }
    }

    private void AddSources(CharacterCreationAttributeProjection attribute)
    {
        foreach (string anchor in attribute.SourceAnchorIds)
            _technicalDetails.Add(NativeTheme.Body(anchor, NativeTheme.Muted));
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-attribute-allocation-details"));
    }
}

/// <summary>
/// Immutable Core preview plus an explicit, digest-bound auxiliary draft confirmation.
/// </summary>
public sealed class CreationAttributesPreviewPage : NativePageBase
{
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly CharacterCreationAttributesPreview _preview;
    private readonly IReadOnlyList<CharacterCreationAttributeAllocation> _allocations;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CreationAttributesPhoneConfirmResult? _confirmation;
    private readonly Action<CreationAttributesPhoneConfirmResult>? _onConfirmed;
    private Button? _confirmButton;
    private bool _saving;

    internal CreationAttributesPreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationAttributesPreview preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
        Action<CreationAttributesPhoneConfirmResult>? onConfirmed = null) : base(coordinator)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _allocations = allocations?.ToArray()
            ?? throw new ArgumentNullException(nameof(allocations));
        _onConfirmed = onConfirmed;
        Title = CreationAllocationStrings.Get("AttributesPreview.PageTitle", "Review Attributes");
        AutomationId = "creation-attributes-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        // Keep the pressed control and scroll position while the save finishes.
        // Owner changes must still clear the previous account's projection.
        if (_saving && Coordinator.CanDisplayCreationAttributesPreview(_preview)) return;
        _confirmButton = null;
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.ExplicitReview",
            "Explicit review")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "AttributeAllocation.Eyebrow",
            "Attribute allocation")));
        if (!Coordinator.CanDisplayCreationAttributesPreview(_preview))
        {
            _body.Add(NativeTheme.Body(CreationAllocationStrings.AttributeBlocker(
                CharacterCreationAttributesBlockers.StaleWorkspaceRevision), NativeTheme.Danger));
            return;
        }
        Label binding = NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Common.PreviewBinding",
                "Revision {0} · saved {1} · preview {2}",
                _preview.Binding.ContentRevision,
                _preview.Binding.SavedRevision,
                CreationPrerequisiteDigestText.CanonicalPrefix(_preview.PreviewDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-attributes-preview-binding";
        _technicalDetails.Add(binding);
        AddDigest("creation-attributes-preview-digest", _preview.PreviewDigest);
        AddDigest("creation-attributes-preview-raw-character-xml-digest", _preview.Binding.RawCharacterXmlDigest);
        AddDigest("creation-attributes-preview-auxiliary-state-digest", _preview.Binding.AuxiliaryStateDigest);
        AddBudgets();
        AddAttributes();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-attributes-preview-details"));
    }

    protected override void OnDisappearing()
    {
        _confirmButton = null;
        // Departure does not cancel/replay a mutation whose outcome may already
        // be durable. The coordinator retains its exact owner and receipt guards.
        base.OnDisappearing();
    }

    private void AddBudgets()
    {
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "AttributesPreview.FinalDraftLedgers",
            "Points after saving")));
        foreach (var (budget, canonicalBudgetId) in new[]
                 {
                     (_preview.NormalPointBudget, CharacterCreationBudgetIds.NormalAttributes),
                     (_preview.SpecialPointBudget, CharacterCreationBudgetIds.SpecialAttributes),
                     (_preview.CreationKarmaBudget, CharacterCreationBudgetIds.Karma)
                 })
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(BuildPageUiProjection.BudgetLabel(budget, canonicalBudgetId), 18));
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
            "Your attributes")));
        foreach (CharacterCreationAttributeProjection attribute in _preview.Attributes)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(CreationAttributesPage.AttributeLabel(attribute.AttributeId), 18));
            _technicalDetails.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.TypedId", "Typed ID"),
                attribute.AttributeId));
            Label current = NativeTheme.Title(attribute.Current.ToString(CultureInfo.InvariantCulture), 28);
            current.AutomationId = $"creation-attributes-preview-value-{CreationAttributesPage.Token(attribute.AttributeId)}";
            SemanticProperties.SetDescription(current, CreationAttributesPage.AttributeLabel(attribute.AttributeId)
                + " · " + CreationAllocationStrings.Get("Common.Value", "Value")
                + " " + attribute.Current.ToString(CultureInfo.InvariantCulture));
            card.Add(current);
            card.Add(NativeTheme.Metric(
                attribute.Category == CharacterCreationAttributeCategories.Special
                    ? CreationAllocationStrings.Get("AttributeAllocation.SpecialSpent", "Special points spent")
                    : CreationAllocationStrings.Get("Common.PriorityPoints", "Priority points"),
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
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "AttributesPreview.CheckChoices", "Check your choices")));
        foreach (string guidance in blockers.Select(CreationAllocationStrings.AttributeBlocker).Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(guidance, NativeTheme.Danger));
        foreach (string blocker in blockers)
            _technicalDetails.Add(NativeTheme.Body(blocker, NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-attributes-preview-blockers";
        _body.Add(border);
    }

    private void AddConfirmation()
    {
        if (_confirmation is
            {
                Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: { } savedReceipt,
                RefreshedState: not null
            } && Coordinator.IsCreationAttributesReceiptCurrent(savedReceipt))
        {
            Label complete = NativeTheme.Body(CreationAllocationStrings.Get(
                "AttributesPreview.Confirmed",
                "Your attribute choices are saved."));
            complete.AutomationId = "creation-attributes-confirmed";
            _body.Add(NativeTheme.Card(complete));
            return;
        }

        bool canConfirm = !_saving && Coordinator.CanOfferCreationAttributesConfirmation(_preview, _allocations);
        Button confirm = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "AttributesPreview.Confirm",
            "Save attribute choices"));
        confirm.AutomationId = "creation-attributes-confirm";
        confirm.IsEnabled = canConfirm;
        _confirmButton = confirm;
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (_saving || !ReferenceEquals(_confirmButton, confirm) || !confirm.IsEnabled
                || !Coordinator.CanOfferCreationAttributesConfirmation(_preview, _allocations)) return;
            long saveAppearance = CaptureAppearanceGeneration();
            _saving = true;
            string label = confirm.Text;
            confirm.IsEnabled = false;
            confirm.Text = CreationAllocationStrings.Get("AttributesPreview.Saving", "Saving…");
            try
            {
                _confirmation = await Coordinator.ConfirmCreationAttributesAsync(
                    _preview,
                    _allocations);
                _onConfirmed?.Invoke(_confirmation);
            }
            finally
            {
                _saving = false;
                confirm.Text = label;
                // A fresh render, never this retained button, owns any next action.
                // RunAsync refreshes only its original appearance. If the user
                // returned while saving, show the result on that new appearance
                // too, including uncertain outcomes that emit no Changed event.
                long currentAppearance = CaptureAppearanceGeneration();
                if (currentAppearance != saveAppearance && IsCurrentAppearanceGeneration(currentAppearance))
                    Refresh();
            }
        });
        _body.Add(confirm);

        Label explicitAction = NativeTheme.Body(
            CreationAllocationStrings.Get(
                "AttributesPreview.ConfirmationBoundary",
                "Save the choices shown here. Your runner stays in character creation."),
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
            } || !Coordinator.IsCreationAttributesReceiptCurrent(receipt))
        {
            return;
        }

        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "AttributesPreview.SavedHeading", "Attributes saved")));
        _technicalDetails.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.AtomicDraftReceipt", "Atomic draft receipt")));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.PreviousRevision", "Previous revision"),
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.ContentRevision", "Content revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.SavedRevision", "Saved revision"),
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        _technicalDetails.Add(NativeTheme.Metric(
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
        _technicalDetails.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.CharacterDocumentChanged", "Character document changed"),
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddReceiptDigest(_technicalDetails, "creation-attributes-receipt-draft-digest", receipt.DraftDigest);
        AddReceiptDigest(_technicalDetails, "creation-attributes-receipt-raw-character-xml-digest", refreshed.Binding.RawCharacterXmlDigest);
        AddReceiptDigest(_technicalDetails, "creation-attributes-receipt-auxiliary-state-digest", refreshed.Binding.AuxiliaryStateDigest);
        card.Add(NativeTheme.Body(
            refreshed.PendingDraft?.CharacterEffectsApplied == false
                ? CreationAllocationStrings.Get(
                    "AttributesPreview.DurablePendingFinalization",
                    "These choices are saved in your creation draft. Finish character creation separately to enter Career.")
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
        back.Clicked += async (_, _) => await RunAsync(BackToBuildAsync);
        _body.Add(back);
    }

    private async Task BackToBuildAsync()
    {
        // Child pops detach this page's navigation proxy before the parent pop.
        // Shell must own the return and attach the authored Runner root.
        if (Shell.Current is not MainShell { UsesTabletComposition: false } shell)
            throw new InvalidOperationException("Creation returns through the phone Runner route.");
        await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false);
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _technicalDetails.Add(label);
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
