using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// SR5 Standard Priority phone chooser. The page renders Core/Presentation projections and
/// delegates every proposed OptionId set back to Core before retaining it.
/// </summary>
public sealed class CreationQualitiesPage : NativePageBase
{
    private readonly CreationQualitiesPhoneDraft _draft = new();
    private readonly CharacterCreationQualitiesCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _localBlockers = [];

    public CreationQualitiesPage(RunnerSessionCoordinator coordinator)
        : this(coordinator, CharacterCreationQualitiesCheckpointStore.CreateDefault())
    {
    }

    internal CreationQualitiesPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesCheckpointStore store) : base(coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Title = CreationFlowStrings.Get("Qualities.PageTitle", "Qualities");
        AutomationId = "creation-qualities-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step1", "SR5 Priority · 1 of 4")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Qualities.Choose", "Choose qualities")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Get(
                "Qualities.Intro",
                "Every row is a fixed-cost, source-anchored Core option. Unsupported requirements, variable costs and unresolved custom overlays stay disabled."),
            NativeTheme.Muted));

        CharacterCreationFoundationResult<CharacterCreationQualitiesState> load =
            Coordinator.LoadCreationQualities();
        if (load.Value is not { } state
            || !CreationQualitiesPhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(load.Blockers.Count == 0
                ? [CharacterCreationQualitiesBlockers.AuthorityUnavailable]
                : load.Blockers);
            return;
        }
        _draft.Bind(state, Coordinator.State);
        if (!_draft.Matches(state, Coordinator.State))
        {
            AddBlockers([CharacterCreationQualitiesBlockers.RevisionConflict]);
            return;
        }

        CharacterCreationQualitiesEditorState editor;
        try
        {
            editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, Coordinator.State);
        }
        catch (InvalidOperationException exception)
        {
            AddBlockers([exception.Message]);
            return;
        }

        AddBinding(state);
        AddBudgets(_draft.Preview ?? state.Preview);
        CharacterCreationQualitiesCheckpoint? checkpoint = AddRecovery(state);
        bool checkpointOwnsLane = checkpoint is not null || HasMalformedCheckpoint();
        AddGranted(state);
        AddOptions(state, editor, checkpointOwnsLane);
        AddReview(state, checkpointOwnsLane);
        AddBlockers(_localBlockers);
    }

    private void AddBinding(CharacterCreationQualitiesState state)
    {
        Label binding = NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.Binding",
                "Revision {0} · prerequisite {1} · attributes {2}",
                state.Binding.ContentRevision.ToString(CultureInfo.InvariantCulture),
                state.Binding.PrerequisiteDraftRevision.ToString(CultureInfo.InvariantCulture),
                state.Binding.AttributesDraftRevision.ToString(CultureInfo.InvariantCulture)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-qualities-binding";
        _body.Add(binding);
        AddDigest("creation-qualities-authority-digest", state.Binding.AuthorityDigest);
        AddDigest("creation-qualities-runtime-digest", state.Binding.RuntimeDigest);
    }

    private void AddBudgets(CharacterCreationQualitiesPreview preview)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.CoreLedgers", "Core ledgers")));
        FlexLayout ribbon = new()
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row
        };
        ribbon.Add(BudgetCard(
            CreationFlowStrings.Get("Qualities.Positive", "Positive qualities"),
            preview.PositiveQualityBudget,
            "creation-qualities-budget-positive"));
        ribbon.Add(BudgetCard(
            CreationFlowStrings.Get("Qualities.Negative", "Negative qualities"),
            preview.NegativeQualityBudget,
            "creation-qualities-budget-negative"));
        VerticalStackLayout karma = new() { Spacing = 5, MinimumWidthRequest = 155 };
        karma.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.CreationKarma", "Creation Karma")));
        karma.Add(NativeTheme.Title(
            CreationFlowStrings.Format(
                "Qualities.KarmaRemainingInline",
                "{0} remaining",
                Signed(preview.KarmaRemaining)),
            20));
        Border karmaCard = NativeTheme.Card(karma, new Thickness(12));
        karmaCard.Margin = new Thickness(0, 0, 8, 8);
        karmaCard.AutomationId = "creation-qualities-budget-karma";
        ribbon.Add(karmaCard);
        _body.Add(ribbon);
        if (preview.MetagenicPositiveKarma != 0 || preview.MetagenicNegativeKarma != 0)
        {
            Label metagenic = NativeTheme.Body(
                CreationFlowStrings.Format(
                    "Qualities.Metagenic",
                    "Metagenic: +{0} / -{1}",
                    preview.MetagenicPositiveKarma.ToString(CultureInfo.InvariantCulture),
                    preview.MetagenicNegativeKarma.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Muted);
            metagenic.AutomationId = "creation-qualities-budget-metagenic";
            _body.Add(metagenic);
        }
    }

    private static Border BudgetCard(
        string label,
        CharacterCreationQualitiesBudget budget,
        string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 5, MinimumWidthRequest = 155 };
        card.Add(NativeTheme.Eyebrow(label));
        card.Add(NativeTheme.Title(
            CreationFlowStrings.Format(
                "Qualities.Left",
                "{0} left",
                budget.Remaining.ToString(CultureInfo.InvariantCulture)),
            20));
        card.Add(NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.BudgetKarma",
                "{0} / {1} Karma",
                budget.Used.ToString(CultureInfo.InvariantCulture),
                budget.Total.ToString(CultureInfo.InvariantCulture)),
            budget.Blockers.Count == 0 ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card, new Thickness(12));
        border.Margin = new Thickness(0, 0, 8, 8);
        border.AutomationId = automationId;
        return border;
    }

    private CharacterCreationQualitiesCheckpoint? AddRecovery(
        CharacterCreationQualitiesState state)
    {
        if (!_store.TryRead(
                out CharacterCreationQualitiesCheckpoint checkpoint,
                out string blocker))
        {
            if (!string.IsNullOrWhiteSpace(blocker))
            {
                Label malformed = NativeTheme.Body(blocker, NativeTheme.Danger);
                malformed.AutomationId = "creation-qualities-checkpoint-blocker";
                _body.Add(NativeTheme.Card(malformed));
            }
            return null;
        }

        VerticalStackLayout recovery = new() { Spacing = 8 };
        recovery.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.DurableRecovery", "Durable review recovery")));
        recovery.Add(NativeTheme.Body(
            checkpoint.Phase switch
            {
                CharacterCreationQualitiesCheckpointPhase.Reviewed =>
                    CreationFlowStrings.Get(
                        "Qualities.Recovery.Reviewed",
                        "A reviewed quality draft can be resumed without reconstructing rules data."),
                CharacterCreationQualitiesCheckpointPhase.Applying =>
                    CreationFlowStrings.Get(
                        "Common.Recovery.Applying",
                        "An interrupted atomic commit is locked. Resolve its exact idempotent command before continuing."),
                CharacterCreationQualitiesCheckpointPhase.Applied =>
                    CreationFlowStrings.Get(
                        "Qualities.Recovery.Applied",
                        "A verified quality-draft receipt is waiting for acknowledgement."),
                _ => CreationFlowStrings.Get("Qualities.Recovery.Locked", "The quality lane is locked.")
            },
            NativeTheme.Muted));

        if (checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Reviewed
            && checkpoint.OwnsExactReview(state, Coordinator.State))
        {
            Button resume = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Qualities.Recovery.Resume",
                "Resume reviewed qualities"));
            resume.AutomationId = "creation-qualities-resume-reviewed";
            resume.Clicked += async (_, _) => await RunAsync(() => ResumeReviewAsync(state, checkpoint));
            recovery.Add(resume);
            Button abandon = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
                "Common.AbandonReviewedDraft",
                "Abandon reviewed draft"));
            abandon.AutomationId = "creation-qualities-abandon-reviewed";
            abandon.Clicked += async (_, _) => await RunAsync(() => AbandonReviewedAsync(checkpoint));
            recovery.Add(abandon);
        }
        else if (checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Applying
                 && checkpoint.OwnsRecoveryRevision(Coordinator.State))
        {
            Button resolve = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.ResolveInterruptedCommit",
                "Resolve interrupted commit"));
            resolve.AutomationId = "creation-qualities-resolve-applying";
            resolve.Clicked += async (_, _) => await RunAsync(() => ResolveApplyingAsync(checkpoint));
            recovery.Add(resolve);
        }
        else if (checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Applied
                 && checkpoint.OwnsRecoveryRevision(Coordinator.State)
                 && checkpoint.Receipt is { } receipt)
        {
            Button receiptButton = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.OpenSavedReceipt",
                "Open saved receipt"));
            receiptButton.AutomationId = "creation-qualities-open-receipt";
            receiptButton.Clicked += async (_, _) => await Navigation.PushAsync(
                new CreationQualitiesReceiptPage(Coordinator, checkpoint, receipt, _store));
            recovery.Add(receiptButton);
        }
        else
        {
            Label stale = NativeTheme.Body(
                CreationFlowStrings.Get(
                    "Qualities.Recovery.Stale",
                    "The checkpoint belongs to another revision or its authority changed. The lane remains fail-closed; reopen the exact runner or use support recovery."),
                NativeTheme.Danger);
            stale.AutomationId = "creation-qualities-stale-checkpoint";
            recovery.Add(stale);
        }

        Border card = NativeTheme.Card(recovery);
        card.AutomationId = "creation-qualities-recovery-card";
        _body.Add(card);
        return checkpoint;
    }

    private bool HasMalformedCheckpoint()
        => !_store.TryRead(out _, out string blocker) && !string.IsNullOrWhiteSpace(blocker);

    private void AddGranted(CharacterCreationQualitiesState state)
    {
        if (state.Authority.GrantedQualities.Count == 0)
            return;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Qualities.Granted",
            "Granted by earlier choices")));
        foreach (CharacterCreationGrantedQuality grant in state.Authority.GrantedQualities)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(grant.Name, 18));
            card.Add(NativeTheme.Body(
                CreationFlowStrings.Format(
                    "Qualities.GrantedDetail",
                    "{0} · rating {1} · Karma {2}",
                    grant.Origin,
                    grant.Rating.ToString(CultureInfo.InvariantCulture),
                    Signed(grant.KarmaCost)),
                NativeTheme.Muted));
            card.Add(NativeTheme.Body(
                string.Join(" · ", grant.SourceAnchorIds),
                NativeTheme.Muted));
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-quality-granted-{Token(grant.GrantId)}";
            _body.Add(border);
        }
    }

    private void AddOptions(
        CharacterCreationQualitiesState state,
        CharacterCreationQualitiesEditorState editor,
        bool checkpointOwnsLane)
    {
        foreach (CharacterCreationQualityType type in Enum.GetValues<CharacterCreationQualityType>())
        {
            _body.Add(NativeTheme.Eyebrow(type == CharacterCreationQualityType.Positive
                ? CreationFlowStrings.Get("Qualities.Positive", "Positive qualities")
                : CreationFlowStrings.Get("Qualities.Negative", "Negative qualities")));
            foreach (CharacterCreationQualitiesDesktopOption option in editor.Options
                         .Where(candidate => candidate.Type == type))
            {
                bool selected = _draft.IsSelected(option.OptionId);
                bool exact = CreationQualitiesPhoneAuthority.IsOptionConfigurable(option);
                string followUp = string.IsNullOrWhiteSpace(option.FollowUpChoiceLabel)
                    ? string.Empty
                    : CreationFlowStrings.Format(
                        "Common.DotValue",
                        " · {0}",
                        option.FollowUpChoiceLabel);
                string detail = CreationFlowStrings.Format(
                    "Qualities.OptionDetail",
                    "{0}rating {1} · Karma {2}{3}",
                    selected ? CreationFlowStrings.Get("Common.SelectedPrefix", "Selected · ") : string.Empty,
                    option.Rating.ToString(CultureInfo.InvariantCulture),
                    Signed(option.KarmaCost),
                    followUp);
                if (!exact)
                    detail += $" · {option.DisableReasonKey ?? CharacterCreationQualitiesBlockers.EligibilityUnresolved}";
                Border row = NativeTheme.NavigationRow(
                    option.Name,
                    detail,
                    () => Navigation.PushAsync(new CreationQualityConfigurePage(
                        Coordinator,
                        state,
                        editor,
                        option,
                        _draft)),
                    enabled: !checkpointOwnsLane,
                    automationId: $"creation-quality-option-{Token(option.OptionId)}");
                _body.Add(row);
            }
        }
    }

    private void AddReview(
        CharacterCreationQualitiesState state,
        bool checkpointOwnsLane)
    {
        CharacterCreationQualitiesPreview preview = _draft.Preview ?? state.Preview;
        Button review = NativeTheme.PrimaryButton(
            CreationFlowStrings.Format(
                "Qualities.ReviewSelected",
                "Review {0} selected qualities",
                _draft.SelectedOptionIds.Count.ToString(CultureInfo.InvariantCulture)));
        review.AutomationId = "creation-qualities-open-review";
        review.IsEnabled = !checkpointOwnsLane
                           && CreationQualitiesPhoneAuthority.CanConfirmPreview(
                               state,
                               Coordinator.State,
                               preview,
                               _draft.SelectedOptionIds);
        review.Clicked += async (_, _) => await RunAsync(() => OpenReviewAsync(state));
        _body.Add(review);
        Label finalization = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Qualities.FinalizationBoundary",
                "Confirmation stores only a typed Creation draft. Quality effects are applied later by the all-steps finalization authority."),
            NativeTheme.Muted);
        finalization.AutomationId = "creation-qualities-finalization-boundary";
        _body.Add(finalization);
    }

    private async Task OpenReviewAsync(CharacterCreationQualitiesState state)
    {
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result =
            Coordinator.PreviewCreationQualities(state.Binding, _draft.SelectedOptionIds);
        if (!_draft.TryAdopt(state, Coordinator.State, result, _draft.SelectedOptionIds)
            || result.Value is not { } preview
            || !CreationQualitiesPhoneAuthority.CanConfirmPreview(
                state,
                Coordinator.State,
                preview,
                _draft.SelectedOptionIds))
        {
            _localBlockers = result.Blockers;
            Refresh();
            return;
        }

        CharacterCreationQualitiesCheckpoint candidate =
            CharacterCreationQualitiesCheckpoint.CreateReviewed(
                preview,
                _draft.SelectedOptionIds,
                Guid.NewGuid());
        if (!_store.TryCreate(candidate, out CharacterCreationQualitiesCheckpoint stored, out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReviewNotCheckpointed", "Review not checkpointed"),
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            Refresh();
            return;
        }
        await Navigation.PushAsync(new CreationQualitiesReviewPage(Coordinator, stored, _store));
    }

    private async Task ResumeReviewAsync(
        CharacterCreationQualitiesState state,
        CharacterCreationQualitiesCheckpoint checkpoint)
    {
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result =
            Coordinator.PreviewCreationQualities(
                checkpoint.Preview.Binding,
                checkpoint.SelectedOptionIds);
        if (!checkpoint.OwnsExactReview(state, Coordinator.State)
            || !CreationQualitiesPhoneAuthority.CanDisplayPreview(
                state,
                Coordinator.State,
                result,
                checkpoint.SelectedOptionIds)
            || result.Value is not { } refreshed
            || !CreationQualitiesPhoneAuthority.CanonicallyEquals(
                checkpoint.Preview,
                refreshed))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReviewCannotResume", "Review cannot resume"),
                CreationFlowStrings.Get(
                    "Qualities.ReviewChanged",
                    "The Core preview, source authority or workspace revision changed."),
                CreationFlowStrings.Get("Common.OK", "OK"));
            return;
        }
        await Navigation.PushAsync(new CreationQualitiesReviewPage(Coordinator, checkpoint, _store));
    }

    private async Task AbandonReviewedAsync(CharacterCreationQualitiesCheckpoint checkpoint)
    {
        bool confirmed = await DisplayAlertAsync(
            CreationFlowStrings.Get("Qualities.Abandon.Title", "Abandon reviewed qualities?"),
            CreationFlowStrings.Get(
                "Qualities.Abandon.Message",
                "This removes only the durable phone review. It does not change the Creation draft or character."),
            CreationFlowStrings.Get("Common.Abandon", "Abandon"),
            CreationFlowStrings.Get("Common.Keep", "Keep"));
        if (!confirmed)
            return;
        if (!_store.TryDeleteReviewed(
                CharacterCreationQualitiesCheckpointCas.From(checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.CheckpointNotRemoved", "Checkpoint not removed"),
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
        }
        Refresh();
    }

    private async Task ResolveApplyingAsync(
        CharacterCreationQualitiesCheckpoint checkpoint)
    {
        CreationQualitiesPhoneConfirmResult result =
            await Coordinator.ConfirmCreationQualitiesAsync(checkpoint);
        if (result.MutationOutcomeKnown
            && string.Equals(result.Outcome, CreationQualitiesPhoneOutcomes.Applied, StringComparison.Ordinal)
            && result.Receipt is { } receipt)
        {
            if (_store.TryRecordApplied(
                    CharacterCreationQualitiesCheckpointCas.From(checkpoint),
                    receipt,
                    out CharacterCreationQualitiesCheckpoint applied,
                    out string appliedBlocker))
            {
                await Navigation.PushAsync(new CreationQualitiesReceiptPage(
                    Coordinator,
                    applied,
                    receipt,
                    _store));
                return;
            }
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReceiptLocked", "Receipt remains locked"),
                appliedBlocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            Refresh();
            return;
        }
        if (result.MutationOutcomeKnown
            && string.Equals(
                result.Outcome,
                CreationQualitiesPhoneOutcomes.RejectedBeforeMutation,
                StringComparison.Ordinal))
        {
            if (_store.TryReturnToReviewed(
                    CharacterCreationQualitiesCheckpointCas.From(checkpoint),
                    out _,
                    out string returnBlocker))
            {
                await DisplayAlertAsync(
                    CreationFlowStrings.Get("Common.CommitNotSaved", "Commit was not saved"),
                    result.Blockers.Count == 0
                        ? CreationFlowStrings.Get(
                            "Qualities.NoMutation",
                            "Fresh authority proved that no mutation occurred; the reviewed draft can be resumed.")
                        : string.Join("\n", result.Blockers),
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
            else
            {
                await DisplayAlertAsync(
                    CreationFlowStrings.Get("Common.RecoveryLocked", "Recovery remains locked"),
                    returnBlocker,
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
            Refresh();
            return;
        }
        await DisplayAlertAsync(
            CreationFlowStrings.Get("Common.CommitLocked", "Commit remains locked"),
            string.Join("\n", result.Blockers),
            CreationFlowStrings.Get("Common.OK", "OK"));
        Refresh();
    }

    private void AddBlockers(IEnumerable<string> blockers)
    {
        string[] normalized = blockers
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static blocker => blocker, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.CoreBlockers", "Core blockers")));
        foreach (string blocker in normalized)
            card.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _body.Add(label);
    }

    internal static string Signed(int value)
        => value > 0
            ? $"+{value.ToString(CultureInfo.InvariantCulture)}"
            : value.ToString(CultureInfo.InvariantCulture);

    internal static string Token(string value)
        => new(value.Trim().ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
}

/// <summary>Phone-deep details for one immutable Core option; no label-based identity.</summary>
public sealed class CreationQualityConfigurePage : NativePageBase
{
    private readonly CharacterCreationQualitiesState _state;
    private readonly CharacterCreationQualitiesEditorState _editor;
    private readonly CharacterCreationQualitiesDesktopOption _option;
    private readonly CreationQualitiesPhoneDraft _draft;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];

    internal CreationQualityConfigurePage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesState state,
        CharacterCreationQualitiesEditorState editor,
        CharacterCreationQualitiesDesktopOption option,
        CreationQualitiesPhoneDraft draft) : base(coordinator)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _option = option ?? throw new ArgumentNullException(nameof(option));
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Title = CreationFlowStrings.Get("Qualities.Configure.PageTitle", "Configure quality");
        AutomationId = "creation-quality-configure-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step2", "SR5 Priority · 2 of 4")));
        _body.Add(NativeTheme.Title(_option.Name));
        VerticalStackLayout details = new() { Spacing = 6 };
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.StableOption", "Stable option"), _option.OptionId));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceId", "Source id"), _option.SourceId.ToString("D")));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Type", "Type"), _option.Type.ToString()));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Rating", "Rating"), _option.Rating.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SignedKarma", "Signed Karma"), CreationQualitiesPage.Signed(_option.KarmaCost)));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.MetagenicLabel", "Metagenic"), _option.IsMetagenic.ToString().ToLowerInvariant()));
        if (!string.IsNullOrWhiteSpace(_option.FollowUpChoiceLabel))
        {
            details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.ExactFollowUp", "Exact follow-up"), _option.FollowUpChoiceLabel));
            details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.FollowUpId", "Follow-up id"), _option.FollowUpChoiceId!));
        }
        details.Add(NativeTheme.Body(
            string.Join("\n", _option.SourceAnchorIds),
            NativeTheme.Muted));
        Border detailCard = NativeTheme.Card(details);
        detailCard.AutomationId = "creation-quality-configure-authority";
        _body.Add(detailCard);

        CharacterCreationQualitiesPreview preview = _draft.Preview ?? _state.Preview;
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.Configure.Preview",
                "Core preview: +{0} positive · -{1} negative · {2} Karma remaining",
                preview.PositiveQualityBudget.Used.ToString(CultureInfo.InvariantCulture),
                preview.NegativeQualityBudget.Used.ToString(CultureInfo.InvariantCulture),
                CreationQualitiesPage.Signed(preview.KarmaRemaining)),
            preview.Blockers.Count == 0 ? NativeTheme.Muted : NativeTheme.Danger));

        foreach (string blocker in preview.Blockers.Concat(_blockers).Distinct(StringComparer.Ordinal))
            _body.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));

        bool exact = CreationQualitiesPhoneAuthority.IsOptionConfigurable(_option)
                     && _draft.Matches(_state, Coordinator.State)
                     && _editor.Options.Count(candidate => string.Equals(
                         candidate.OptionId,
                         _option.OptionId,
                         StringComparison.Ordinal)) == 1;
        Button toggle = NativeTheme.PrimaryButton(
            _draft.IsSelected(_option.OptionId)
                ? CreationFlowStrings.Get("Qualities.Configure.Remove", "Remove from draft")
                : CreationFlowStrings.Get("Qualities.Configure.Add", "Add to draft"));
        toggle.AutomationId = "creation-quality-configure-toggle";
        toggle.IsEnabled = exact;
        toggle.Clicked += async (_, _) => await RunAsync(ToggleAsync);
        _body.Add(toggle);
        if (!exact)
        {
            Label disabled = NativeTheme.Body(
                _option.DisableReasonKey
                ?? CharacterCreationQualitiesBlockers.EligibilityUnresolved,
                NativeTheme.Danger);
            disabled.AutomationId = "creation-quality-configure-disabled-reason";
            _body.Add(disabled);
        }
        Button done = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
            "Qualities.Configure.Back",
            "Back to qualities"));
        done.AutomationId = "creation-quality-configure-done";
        done.Clicked += async (_, _) => await Navigation.PopAsync();
        _body.Add(done);
    }

    private Task ToggleAsync()
    {
        IReadOnlyList<string> proposed = _draft.WithToggle(_option);
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result =
            Coordinator.PreviewCreationQualities(_state.Binding, proposed);
        _blockers = result.Blockers;
        _draft.TryAdopt(_state, Coordinator.State, result, proposed);
        Refresh();
        return Task.CompletedTask;
    }
}

/// <summary>Immutable Core preview followed by one explicit durable apply transition.</summary>
public sealed class CreationQualitiesReviewPage : NativePageBase
{
    private CharacterCreationQualitiesCheckpoint _checkpoint;
    private readonly CharacterCreationQualitiesCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];
    private int _applyStarted;

    internal CreationQualitiesReviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesCheckpointStore store) : base(coordinator)
    {
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (!_checkpoint.IsStructurallyValid()
            || _checkpoint.Phase != CharacterCreationQualitiesCheckpointPhase.Reviewed)
            throw new InvalidOperationException("The review page requires one exact Reviewed checkpoint.");
        Title = CreationFlowStrings.Get("Qualities.Review.PageTitle", "Review qualities");
        AutomationId = "creation-qualities-review-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        CharacterCreationQualitiesPreview preview = _checkpoint.Preview;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step3", "SR5 Priority · 3 of 4")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get(
            "Qualities.Review.Heading",
            "Review exact quality draft")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Format(
                "Qualities.Review.Binding",
                "Revision {0} · transaction {1}",
                preview.Binding.ContentRevision.ToString(CultureInfo.InvariantCulture),
                _checkpoint.TransactionId.ToString("D")),
            NativeTheme.Muted));
        AddDigest("creation-qualities-review-preview-digest", preview.PreviewDigest);
        AddDigest("creation-qualities-review-authority-digest", preview.AuthorityDigest);
        AddDigest("creation-qualities-review-raw-digest", preview.Binding.RawCharacterXmlDigest);
        AddDigest("creation-qualities-review-auxiliary-digest", preview.Binding.AuxiliaryStateDigest);

        VerticalStackLayout budgets = new() { Spacing = 6 };
        budgets.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.FinalLedgers", "Final Core ledgers")));
        budgets.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Qualities.PositiveKarma", "Positive quality Karma"),
            $"{preview.PositiveQualityBudget.Used.ToString(CultureInfo.InvariantCulture)} / {preview.PositiveQualityBudget.Total.ToString(CultureInfo.InvariantCulture)}"));
        budgets.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Qualities.NegativeKarma", "Negative quality Karma"),
            $"-{preview.NegativeQualityBudget.Used.ToString(CultureInfo.InvariantCulture)} / -{preview.NegativeQualityBudget.Total.ToString(CultureInfo.InvariantCulture)}"));
        budgets.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.CreationKarmaRemaining", "Creation Karma remaining"),
            CreationQualitiesPage.Signed(preview.KarmaRemaining)));
        _body.Add(NativeTheme.Card(budgets));

        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.TypedSelections", "Typed selections")));
        foreach (CharacterCreationQualitySelection selection in preview.Selections)
        {
            VerticalStackLayout card = new() { Spacing = 5 };
            card.Add(NativeTheme.Title(selection.Name, 18));
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.OptionId", "Option id"), selection.OptionId));
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceId", "Source id"), selection.SourceId.ToString("D")));
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Rating", "Rating"), selection.Rating.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SignedKarma", "Signed Karma"), CreationQualitiesPage.Signed(selection.KarmaCost)));
            if (!string.IsNullOrWhiteSpace(selection.FollowUpChoiceLabel))
                card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.FollowUp", "Follow-up"), selection.FollowUpChoiceLabel));
            card.Add(NativeTheme.Body(string.Join("\n", selection.SourceAnchorIds), NativeTheme.Muted));
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"creation-qualities-review-selection-{CreationQualitiesPage.Token(selection.OptionId)}";
            _body.Add(border);
        }

        foreach (string blocker in preview.Blockers.Concat(_blockers).Distinct(StringComparer.Ordinal))
            _body.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));

        Button apply = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
            "Qualities.Review.Confirm",
            "Confirm Creation qualities draft"));
        apply.AutomationId = "creation-qualities-confirm-draft";
        apply.IsEnabled = _checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Reviewed
                          && preview.CanConfirm
                          && preview.RequiresExplicitConfirmation
                          && preview.Blockers.Count == 0
                          && Volatile.Read(ref _applyStarted) == 0;
        apply.Clicked += async (_, _) => await RunAsync(ApplyAsync);
        _body.Add(apply);
        Label boundary = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Qualities.Review.Boundary",
                "This commits only auxiliary Creation state and its receipt. CharacterDocumentChanged must remain false until finalization."),
            NativeTheme.Muted);
        boundary.AutomationId = "creation-qualities-review-preview-only";
        _body.Add(boundary);
    }

    private async Task ApplyAsync()
    {
        if (Interlocked.CompareExchange(ref _applyStarted, 1, 0) != 0)
            return;
        try
        {
            CharacterCreationFoundationResult<CharacterCreationQualitiesState> live =
                Coordinator.LoadCreationQualities();
            CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> reprojection =
                Coordinator.PreviewCreationQualities(
                    _checkpoint.Preview.Binding,
                    _checkpoint.SelectedOptionIds);
            if (live.Value is not { } state
                || !_checkpoint.OwnsExactReview(state, Coordinator.State)
                || !CreationQualitiesPhoneAuthority.CanDisplayPreview(
                    state,
                    Coordinator.State,
                    reprojection,
                    _checkpoint.SelectedOptionIds)
                || reprojection.Value is not { } canonical
                || !CreationQualitiesPhoneAuthority.CanonicallyEquals(
                    _checkpoint.Preview,
                    canonical))
            {
                _blockers = reprojection.Blockers
                    .Append(CharacterCreationQualitiesBlockers.PreviewChanged)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return;
            }
            if (!_store.TryBeginApply(
                    CharacterCreationQualitiesCheckpointCas.From(_checkpoint),
                    out CharacterCreationQualitiesCheckpoint applying,
                    out string beginBlocker))
            {
                _blockers = [beginBlocker];
                return;
            }
            _checkpoint = applying;
            CreationQualitiesPhoneConfirmResult result =
                await Coordinator.ConfirmCreationQualitiesAsync(applying);
            _blockers = result.Blockers;
            if (result.MutationOutcomeKnown
                && string.Equals(result.Outcome, CreationQualitiesPhoneOutcomes.Applied, StringComparison.Ordinal)
                && result.Receipt is { } receipt)
            {
                if (!_store.TryRecordApplied(
                        CharacterCreationQualitiesCheckpointCas.From(applying),
                        receipt,
                        out CharacterCreationQualitiesCheckpoint applied,
                        out string appliedBlocker))
                {
                    _blockers = [appliedBlocker, CreationQualitiesPhoneBlockers.OutcomeUnknown];
                    return;
                }
                _checkpoint = applied;
                await Navigation.PushAsync(new CreationQualitiesReceiptPage(
                    Coordinator,
                    applied,
                    receipt,
                    _store));
                return;
            }
            if (result.MutationOutcomeKnown
                && string.Equals(
                    result.Outcome,
                    CreationQualitiesPhoneOutcomes.RejectedBeforeMutation,
                    StringComparison.Ordinal)
                && _store.TryReturnToReviewed(
                    CharacterCreationQualitiesCheckpointCas.From(applying),
                    out CharacterCreationQualitiesCheckpoint reviewed,
                    out string returnBlocker))
            {
                _checkpoint = reviewed;
                _blockers = result.Blockers;
                return;
            }
            _blockers = result.Blockers.Count == 0
                ? [CreationQualitiesPhoneBlockers.OutcomeUnknown]
                : result.Blockers;
        }
        finally
        {
            Interlocked.Exchange(ref _applyStarted, 0);
            Refresh();
        }
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _body.Add(label);
    }
}

/// <summary>Receipt acknowledgement; no direct character-apply action exists on this page.</summary>
public sealed class CreationQualitiesReceiptPage : NativePageBase
{
    private readonly CharacterCreationQualitiesCheckpoint _checkpoint;
    private readonly CharacterCreationQualitiesDraftReceipt _receipt;
    private readonly CharacterCreationQualitiesCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationQualitiesReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesDraftReceipt receipt,
        CharacterCreationQualitiesCheckpointStore store) : base(coordinator)
    {
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (!_checkpoint.IsStructurallyValid()
            || _checkpoint.Phase != CharacterCreationQualitiesCheckpointPhase.Applied
            || _checkpoint.Receipt != _receipt)
            throw new InvalidOperationException("The receipt page requires one exact durable Applied checkpoint.");
        Title = CreationFlowStrings.Get("Qualities.Receipt.PageTitle", "Qualities receipt");
        AutomationId = "creation-qualities-receipt-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Qualities.Step4", "SR5 Priority · 4 of 4")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Common.DraftSaved", "Creation draft saved")));
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Transaction", "Transaction"), _receipt.TransactionId.ToString("D")));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.PreviousRevision", "Previous revision"), _receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.ContentRevision", "Content revision"), _receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SavedRevision", "Saved revision"), _receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.PositiveKarmaUsed", "Positive Karma used"), _checkpoint.Preview.PositiveQualityBudget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Qualities.NegativeKarmaUsed", "Negative Karma used"), $"-{_checkpoint.Preview.NegativeQualityBudget.Used.ToString(CultureInfo.InvariantCulture)}"));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.KarmaRemaining", "Karma remaining"), CreationQualitiesPage.Signed(_checkpoint.Preview.KarmaRemaining)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.DocumentChanged", "Character document changed"), _receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddDigest(card, "creation-qualities-receipt-digest", _receipt.ReceiptDigest);
        AddDigest(card, "creation-qualities-receipt-draft-digest", _receipt.DraftDigest);
        AddDigest(card, "creation-qualities-receipt-plan-digest", _receipt.PlanDigest);
        AddDigest(card, "creation-qualities-receipt-command-digest", _receipt.CommandDigest);
        Border receiptCard = NativeTheme.Card(card);
        receiptCard.AutomationId = "creation-qualities-confirm-receipt";
        _body.Add(receiptCard);
        Label boundary = NativeTheme.Body(
            !_receipt.CharacterDocumentChanged
                ? CreationFlowStrings.Get(
                    "Qualities.Receipt.Safe",
                    "Typed selections are durable. Character effects remain pending whole-build finalization.")
                : CreationFlowStrings.Get(
                    "Qualities.Receipt.Unsafe",
                    "Unsafe receipt: the character document changed before finalization."),
            !_receipt.CharacterDocumentChanged ? NativeTheme.Muted : NativeTheme.Danger);
        boundary.AutomationId = "creation-qualities-receipt-finalization-state";
        _body.Add(boundary);
        Button acknowledge = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
            "Common.AcknowledgeReceipt",
            "Acknowledge receipt"));
        acknowledge.AutomationId = "creation-qualities-receipt-acknowledge";
        acknowledge.IsEnabled = !_receipt.CharacterDocumentChanged
                                && _checkpoint.OwnsRecoveryRevision(Coordinator.State);
        acknowledge.Clicked += async (_, _) => await RunAsync(AcknowledgeAsync);
        _body.Add(acknowledge);
    }

    private async Task AcknowledgeAsync()
    {
        if (!_store.TryAcknowledgeApplied(
                CharacterCreationQualitiesCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReceiptNotAcknowledged", "Receipt not acknowledged"),
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            return;
        }
        await Navigation.PopAsync(animated: false);
        while (Navigation.NavigationStack.LastOrDefault() is
               (CreationQualitiesReviewPage or CreationQualitiesPage))
        {
            await Navigation.PopAsync(animated: false);
        }
    }

    private static void AddDigest(
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
