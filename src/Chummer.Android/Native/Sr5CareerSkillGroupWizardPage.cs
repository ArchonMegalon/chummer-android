using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

internal sealed record Sr5CareerSkillGroupWizardDependencies(
    Sr5CareerSkillGroupCheckpointStore Store,
    ISr5CareerSkillGroupCheckpointAuthority CheckpointAuthority);

/// <summary>
/// Phone-deep first step. The page only selects an exact Core quote and creates
/// a durable review checkpoint; no mutation is reachable from this surface.
/// </summary>
public sealed class Sr5CareerSkillGroupWizardPage : NativePageBase
{
    private readonly CareerSkillGroupAdvanceEditorState _editor;
    private readonly Sr5CareerSkillGroupCoordinator _authority;
    private readonly Sr5CareerSkillGroupCheckpointStore _store;
    private readonly ISr5CareerSkillGroupCheckpointAuthority _checkpointAuthority;
    private readonly Picker _groups;
    private readonly Label _rating;
    private readonly Label _cost;
    private readonly Label _blocker;
    private readonly Label _recovery;
    private readonly Button _review;
    private readonly Button _resume;
    private readonly Button _resolve;
    private readonly Button _abandon;
    private CharacterCareerSkillGroupAdvanceQuote? _selected;
    private Sr5CareerSkillGroupDraft? _recoveryDraft;
    private Sr5CareerSkillGroupCheckpoint? _checkpoint;
    private int _automaticResolutionStarted;

    public Sr5CareerSkillGroupWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerSkillGroupAdvanceEditorState editor)
        : this(coordinator, editor, CreateDependencies(coordinator, editor))
    {
    }

    private Sr5CareerSkillGroupWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerSkillGroupAdvanceEditorState editor,
        Sr5CareerSkillGroupWizardDependencies dependencies)
        : this(
            coordinator,
            editor,
            new Sr5CareerSkillGroupCoordinator(
                new RunnerSessionSr5CareerSkillGroupPresenter(coordinator),
                dependencies.CheckpointAuthority),
            dependencies.Store,
            dependencies.CheckpointAuthority)
    {
    }

    private static Sr5CareerSkillGroupWizardDependencies CreateDependencies(
        RunnerSessionCoordinator coordinator,
        CareerSkillGroupAdvanceEditorState editor)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(editor);
        PreferencesSr5CareerCheckpointOwnerAuthority ownerAuthority = new();
        Sr5CareerSkillGroupLiveCheckpointAuthority checkpointAuthority = new(
            ownerAuthority,
            editor,
            () => new RunnerSessionSr5CareerSkillGroupPresenter(coordinator).Binding);
        return new(
            Sr5CareerSkillGroupCheckpointStore.CreateDefault(checkpointAuthority),
            checkpointAuthority);
    }

    internal Sr5CareerSkillGroupWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerSkillGroupAdvanceEditorState editor,
        Sr5CareerSkillGroupCoordinator authority,
        Sr5CareerSkillGroupCheckpointStore store,
        ISr5CareerSkillGroupCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerSkillGroupPresenter(coordinator).Binding);
        if (coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                Text("The SR5 SkillGroup route requires the current exact runner revision."));
        }

        _selected = editor.SkillGroups.FirstOrDefault();
        Title = Text("Advance skill group");
        AutomationId = Sr5CareerWizardRoutes.SkillGroupChoose;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 1 of 3")));
        body.Add(NativeTheme.Title(Text("Choose a skill group")));
        body.Add(NativeTheme.Body(
            Text("Only exact skill groups projected from this saved SR5 revision are shown. Core owns the internal identity, exact member projection, group integrity, rating maximum, modifiers, Karma cost and expense semantics."),
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel(Text("Skill group")));
        _groups = new Picker
        {
            AutomationId = "sr5-career-skill-group-picker",
            Title = Text("Saved skill group"),
            ItemsSource = editor.SkillGroups.Select(SkillGroupLabel).ToArray(),
            SelectedIndex = editor.SkillGroups.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _groups.SelectedIndexChanged += (_, _) => SelectSkillGroup();
        body.Add(_groups);

        _rating = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _rating.AutomationId = "sr5-career-skill-group-rating";
        body.Add(_rating);
        _cost = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _cost.AutomationId = "sr5-career-skill-group-cost";
        body.Add(_cost);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "sr5-career-skill-group-blocker";
        body.Add(_blocker);
        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-career-skill-group-recovery";
        body.Add(_recovery);

        if (editor.OmittedSkillGroupCount > 0)
        {
            Label omitted = NativeTheme.Body(
                Format(
                    "{0} skill-group quote(s) were omitted because exact authority could not be reproduced.",
                    editor.OmittedSkillGroupCount.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-skill-group-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton(Text("Review exact advancement"));
        _review.AutomationId = "sr5-career-skill-group-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);
        _resume = NativeTheme.SecondaryButton(Text("Resume reviewed advancement"));
        _resume.AutomationId = "sr5-career-skill-group-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton(Text("Resolve interrupted apply"));
        _resolve.AutomationId = "sr5-career-skill-group-resolve-outcome";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveCheckpointAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed draft"));
        _abandon.AutomationId = "sr5-career-skill-group-abandon-reviewed";
        _abandon.Clicked += async (_, _) => await RunAsync(AbandonReviewedAsync);
        body.Add(_abandon);

        Content = new ScrollView { Content = body };
        LoadRecoveryCheckpoint();
        RefreshEnabledState();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_checkpoint?.Phase is (Sr5CareerCheckpointPhase.Applying
            or Sr5CareerCheckpointPhase.Applied)
            && _checkpointAuthority.OwnsCurrentRunner(_checkpoint)
            && Interlocked.CompareExchange(ref _automaticResolutionStarted, 1, 0) == 0)
        {
            await RunAsync(ResolveCheckpointAsync);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string SkillGroupLabel(CharacterCareerSkillGroupAdvanceQuote group)
        => group.KarmaCost >= 0
            ? Format(
                "{0} · {1} → {2} · {3} Karma",
                group.Name,
                group.GroupRating.ToString(CultureInfo.InvariantCulture),
                group.TargetGroupRating.ToString(CultureInfo.InvariantCulture),
                group.KarmaCost.ToString(CultureInfo.InvariantCulture))
            : Format(
                "{0} · {1} → {2} · blocked",
                group.Name,
                group.GroupRating.ToString(CultureInfo.InvariantCulture),
                group.TargetGroupRating.ToString(CultureInfo.InvariantCulture));

    private void SelectSkillGroup()
    {
        _selected = _groups.SelectedIndex >= 0
            && _groups.SelectedIndex < _editor.SkillGroups.Count
            ? _editor.SkillGroups[_groups.SelectedIndex]
            : null;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool sr5 = Sr5CareerWizardCatalog.IsSr5CareerRunner(
            Coordinator.State.Profile?.Created == true,
            Coordinator.State.Rules?.GameEdition);
        bool revisionMatches = sr5
            && Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        bool reviewedOwned = _checkpoint is not null
            && _recoveryDraft is not null
            && _checkpoint.MatchesActionDraft(_recoveryDraft)
            && _checkpointAuthority.OwnsReviewed(_checkpoint);
        _groups.IsEnabled = revisionMatches && _checkpoint is null && _editor.SkillGroups.Count > 0;
        _rating.Text = _selected is null
            ? Text("No exact skill-group quote is available.")
            : Format(
                "Group {0} → {1} · cost rating {2} → {3} · {4} enabled member(s) · maximum {5}",
                _selected.GroupRating.ToString(CultureInfo.InvariantCulture),
                _selected.TargetGroupRating.ToString(CultureInfo.InvariantCulture),
                _selected.CostRating.ToString(CultureInfo.InvariantCulture),
                _selected.TargetCostRating.ToString(CultureInfo.InvariantCulture),
                _selected.EnabledMemberCount.ToString(CultureInfo.InvariantCulture),
                _selected.RatingMaximum.ToString(CultureInfo.InvariantCulture));
        _cost.Text = _selected is null
            ? string.Empty
            : Format(
                "Cost {0} Karma · available {1} · after {2}",
                _selected.KarmaCost.ToString(CultureInfo.InvariantCulture),
                _selected.AvailableKarma.ToString(CultureInfo.InvariantCulture),
                (_selected.AvailableKarma - Math.Max(0, _selected.KarmaCost)).ToString(CultureInfo.InvariantCulture));
        _blocker.Text = !sr5
            ? Text("This action is available only to a created SR5 runner.")
            : !revisionMatches
                ? Text("This runner changed. Reopen skill-group advancement.")
                : _selected is null
                    ? Text("No exact skill-group projection is available.")
                    : Sr5CareerSkillGroupDraft.BlockerText(_selected.Blocker);
        _review.IsEnabled = revisionMatches
            && _checkpoint is null
            && _selected is { CanAdvance: true }
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(_selected);
        _resume.IsVisible = reviewedOwned;
        _resume.IsEnabled = revisionMatches && reviewedOwned;
        _resolve.IsVisible = _checkpoint?.Phase is (Sr5CareerCheckpointPhase.Applying
            or Sr5CareerCheckpointPhase.Applied);
        _resolve.IsEnabled = _resolve.IsVisible
            && _checkpoint is not null
            && _checkpointAuthority.OwnsCurrentRunner(_checkpoint);
        _abandon.IsVisible = reviewedOwned;
        _abandon.IsEnabled = revisionMatches && reviewedOwned;
    }

    private async Task OpenReviewAsync()
    {
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerSkillGroupPresenter(Coordinator).Binding);
        if (!Sr5CareerSkillGroupDraft.TryCreate(
                _editor,
                _selected,
                _checkpointAuthority.CurrentOwnerId,
                Guid.NewGuid(),
                DateTime.Now,
                out Sr5CareerSkillGroupDraft draft,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Cannot review"), blocker, Text("OK"));
            return;
        }
        if (!draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(Text("Runner changed"), Text("Reopen skill-group advancement."), Text("OK"));
            return;
        }

        Sr5CareerSkillGroupCheckpoint candidate = Sr5CareerSkillGroupCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(
                candidate,
                out Sr5CareerSkillGroupCheckpoint stored,
                out blocker))
        {
            await DisplayAlertAsync(Text("Review not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = stored;
        _recoveryDraft = draft;
        await Navigation.PushAsync(new Sr5CareerSkillGroupReviewPage(
            Coordinator,
            draft,
            stored,
            _authority,
            _store,
            _checkpointAuthority));
    }

    private async Task ResumeReviewAsync()
    {
        if (_recoveryDraft is null
            || _checkpoint is null
            || !_checkpointAuthority.OwnsReviewed(_checkpoint)
            || !_checkpoint.MatchesActionDraft(_recoveryDraft)
            || !_recoveryDraft.Matches(
                Coordinator.State.WorkspaceId,
                Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(
                Text("Draft cannot resume"),
                Text("The saved review no longer owns this exact runner revision."),
                Text("OK"));
            return;
        }
        await Navigation.PushAsync(new Sr5CareerSkillGroupReviewPage(
            Coordinator,
            _recoveryDraft,
            _checkpoint,
            _authority,
            _store,
            _checkpointAuthority));
    }

    private async Task ResolveCheckpointAsync()
    {
        if (_checkpoint is null
            || _checkpoint.Phase is not (Sr5CareerCheckpointPhase.Applying
                or Sr5CareerCheckpointPhase.Applied)
            || !_checkpointAuthority.OwnsCurrentRunner(_checkpoint))
        {
            _recovery.Text = Text("This recovery lock belongs to another owner or SR5 runner context.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }

        Sr5CareerSkillGroupRecoveryResolution resolution =
            await _authority.ResolveAsync(_checkpoint, _store);
        if (resolution.Status == Sr5CareerSkillGroupRecoveryStatus.OutcomeUnknown)
        {
            _recovery.Text = resolution.Message;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }

        Sr5CareerSkillGroupCheckpoint stored = _checkpoint;
        if (_checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            && !_store.TryRecordAuthoritativeResolution(
                Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                resolution,
                out stored,
                out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;
        if (resolution.Status == Sr5CareerSkillGroupRecoveryStatus.AppliedVerified
            && resolution.Receipt is { } receipt)
        {
            _recovery.Text = Text("The interrupted skill-group apply was verified from the saved receipt ledger.");
            _recovery.TextColor = NativeTheme.Muted;
            await Navigation.PushAsync(new Sr5CareerSkillGroupReceiptPage(
                Coordinator,
                receipt,
                stored,
                _authority,
                _store,
                _checkpointAuthority));
            return;
        }

        LoadRecoveryCheckpoint();
        _recovery.Text = Text("Fresh typed projections prove the action was not saved. The reviewed draft may be resumed.");
        _recovery.TextColor = NativeTheme.Muted;
    }

    private async Task AbandonReviewedAsync()
    {
        if (_checkpoint is null || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            await DisplayAlertAsync(
                Text("Cannot abandon"),
                Text("Only the current owner and exact runner revision may abandon this review."),
                Text("OK"));
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            Text("Abandon reviewed skill group?"),
            Text("This removes only the durable review checkpoint and does not change the runner."),
            Text("Abandon"),
            Text("Keep"));
        if (!confirmed)
        {
            return;
        }
        if (!_store.TryDeleteReviewed(
                Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(Text("Checkpoint not deleted"), blocker, Text("OK"));
            return;
        }
        _checkpoint = null;
        _recoveryDraft = null;
        _recovery.Text = string.Empty;
        RefreshEnabledState();
    }

    private void LoadRecoveryCheckpoint()
    {
        _checkpoint = null;
        _recoveryDraft = null;
        if (!_store.TryRead(out Sr5CareerSkillGroupCheckpoint checkpoint, out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = string.IsNullOrWhiteSpace(blocker)
                ? NativeTheme.Muted
                : NativeTheme.Danger;
            return;
        }
        if (checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed)
        {
            if (!checkpoint.TryResume(
                    _editor,
                    out Sr5CareerSkillGroupDraft draft,
                    out _)
                || !_checkpointAuthority.OwnsReviewed(checkpoint))
            {
                _recovery.Text = Text("A saved skill-group review is not authorized for this owner and runner revision.");
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            _checkpoint = checkpoint;
            _recoveryDraft = draft;
            _recovery.Text = Text("A durable reviewed skill-group advancement can be resumed.");
            _recovery.TextColor = NativeTheme.Muted;
            int index = _editor.SkillGroups
                .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                .First(pair => pair.candidate.Identity == draft.Quote.Identity)
                .candidateIndex;
            _groups.SelectedIndex = index;
            return;
        }
        if (!_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            _recovery.Text = Text("A saved skill-group apply lock is not authorized for this owner and SR5 runner.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;
        _recovery.Text = checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            ? Text("An interrupted apply is locked and will be resolved only by the exact idempotent Core command.")
            : Text("A verified saved skill-group receipt is awaiting acknowledgement.");
        _recovery.TextColor = NativeTheme.Muted;
    }
}

/// <summary>
/// Step two is a pure preview. Apply is exposed only after the durable CAS
/// journal has moved this exact owner/action from Reviewed to Applying.
/// </summary>
public sealed class Sr5CareerSkillGroupReviewPage : NativePageBase
{
    private readonly Sr5CareerSkillGroupDraft _draft;
    private Sr5CareerSkillGroupCheckpoint _checkpoint;
    private readonly Sr5CareerSkillGroupCoordinator _authority;
    private readonly Sr5CareerSkillGroupCheckpointStore _store;
    private readonly ISr5CareerSkillGroupCheckpointAuthority _checkpointAuthority;
    private readonly Button _apply;
    private readonly Label _blocker;
    private int _attempted;

    internal Sr5CareerSkillGroupReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerSkillGroupDraft draft,
        Sr5CareerSkillGroupCheckpoint checkpoint,
        Sr5CareerSkillGroupCoordinator authority,
        Sr5CareerSkillGroupCheckpointStore store,
        ISr5CareerSkillGroupCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        if (!_checkpoint.MatchesActionDraft(_draft)
            || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            throw new InvalidOperationException(
                Text("The skill-group preview does not own its durable review checkpoint."));
        }

        Title = Text("Review skill group");
        AutomationId = Sr5CareerWizardRoutes.SkillGroupReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 2 of 3")));
        body.Add(NativeTheme.Title(Text("Review exact diff")));
        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric(Text("Skill group"), _draft.Quote.Name));
        diff.Add(NativeTheme.Metric(
            Text("Group rating"),
            $"{_draft.Quote.GroupRating} → {_draft.Quote.TargetGroupRating}"));
        diff.Add(NativeTheme.Metric(
            Text("Cost rating"),
            $"{_draft.Quote.CostRating} → {_draft.Quote.TargetCostRating}"));
        diff.Add(NativeTheme.Metric(
            Text("Group Karma points"),
            $"{_draft.Quote.KarmaPoints} → {_draft.Plan.SavedGroupKarmaPoints}"));
        diff.Add(NativeTheme.Metric(
            Text("Runner Karma"),
            $"{_draft.Quote.AvailableKarma} → {_draft.Plan.SavedCharacterKarma}"));
        diff.Add(NativeTheme.Metric(Text("Enabled members"), _draft.Quote.EnabledMemberCount.ToString(CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(Text("Rating maximum"), _draft.Quote.RatingMaximum.ToString(CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(
            Text("Application time"),
            $"{_draft.Quote.ApplicationDuration} · {_draft.Quote.TimeAuthority}"));
        foreach (CharacterCareerSkillGroupPrerequisiteResult prerequisite in _draft.Quote.Prerequisites)
        {
            diff.Add(NativeTheme.Metric(
                Format("Prerequisite · {0}", prerequisite.Prerequisite),
                Format(
                    prerequisite.Satisfied ? "satisfied · {0}" : "blocked · {0}",
                    prerequisite.Authority)));
        }
        diff.Add(NativeTheme.Metric(Text("Expense"), _draft.Plan.ExpenseReason));
        diff.Add(NativeTheme.Metric(Text("Transaction"), _draft.Plan.TransactionId.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Expense identity"), _draft.Plan.ExpenseId.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Content authority"), _draft.RuntimeAuthority.ContentDigest));
        diff.Add(NativeTheme.Metric(Text("Runtime authority"), _draft.RuntimeAuthority.RuntimeDigest));
        diff.Add(NativeTheme.Metric(Text("Date"), _draft.Plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(Text("Undo"), $"{_draft.Plan.KarmaUndoType} · {_draft.Plan.UndoObjectId}"));
        body.Add(NativeTheme.Card(diff));
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        body.Add(_blocker);
        _apply = NativeTheme.PrimaryButton(Text("Apply and verify once"));
        _apply.AutomationId = "sr5-career-skill-group-apply";
        _apply.Clicked += async (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _attempted, 1, 0) == 0)
            {
                RefreshEnabledState();
                await RunAsync(ApplyAsync);
            }
        };
        body.Add(_apply);
        body.Add(NativeTheme.Body(
            Text("The exact checkpoint moves to Applying before mutation. Success is shown only after the atomic Core service returns a digest-verified persisted receipt for the reviewed identity, costs and revisions."),
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool current = _draft.Matches(
                Coordinator.State.WorkspaceId,
                Coordinator.State.ContentRevision)
            && _checkpoint.MatchesActionDraft(_draft)
            && _checkpointAuthority.OwnsReviewed(_checkpoint);
        bool attempted = Volatile.Read(ref _attempted) != 0;
        _apply.IsEnabled = current && !attempted;
        _blocker.Text = !current
            ? Text("The runner revision, owner, quote, or durable checkpoint changed. This preview cannot apply.")
            : attempted
                ? Text("The one-shot apply is running or awaiting authoritative recovery.")
                : string.Empty;
    }

    private async Task ApplyAsync()
    {
        if (!_checkpoint.MatchesActionDraft(_draft)
            || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            await DisplayAlertAsync(
                Text("Apply blocked"),
                Text("The exact reviewed action no longer owns this runner."),
                Text("OK"));
            return;
        }
        if (!_store.TryBeginApply(
                Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                out Sr5CareerSkillGroupCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync(
                Text("Apply blocked"),
                blocker,
                Text("OK"));
            return;
        }
        _checkpoint = applying;

        Sr5CareerSkillGroupApplyResult result = await _authority.ApplyAsync(
            _draft,
            applying,
            _store);
        if (result.Status == Sr5CareerSkillGroupApplyStatus.OutcomeUnknown)
        {
            await DisplayAlertAsync(
                Text("Outcome unresolved"),
                Format("{0} The Applying checkpoint cannot be cleared or replaced by a compatibility mutation.", result.Message),
                Text("OK"));
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5CareerSkillGroupCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerSkillGroupCheckpoint resolved,
                out blocker))
        {
            await DisplayAlertAsync(Text("Outcome not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = resolved;
        if (result.Status == Sr5CareerSkillGroupApplyStatus.RejectedBeforeMutation)
        {
            await DisplayAlertAsync(
                Text("Not applied"),
                Text("Core rejected the exact command and the runner stayed at the reviewed revision. Return and resume the review before retrying."),
                Text("OK"));
            return;
        }

        await Navigation.PushAsync(new Sr5CareerSkillGroupReceiptPage(
            Coordinator,
            result.Receipt!,
            resolved,
            _authority,
            _store,
            _checkpointAuthority));
    }
}

public sealed class Sr5CareerSkillGroupReceiptPage : NativePageBase
{
    private readonly CharacterCareerSkillGroupAdvanceReceipt _receipt;
    private readonly Sr5CareerSkillGroupCheckpoint _checkpoint;
    private readonly Sr5CareerSkillGroupCoordinator _authority;
    private readonly Sr5CareerSkillGroupCheckpointStore _store;
    private readonly ISr5CareerSkillGroupCheckpointAuthority _checkpointAuthority;
    private readonly Label _durability;

    internal Sr5CareerSkillGroupReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCareerSkillGroupAdvanceReceipt receipt,
        Sr5CareerSkillGroupCheckpoint checkpoint,
        Sr5CareerSkillGroupCoordinator authority,
        Sr5CareerSkillGroupCheckpointStore store,
        ISr5CareerSkillGroupCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applied
            || !Sr5CareerSkillGroupCoordinator.ReceiptMatchesDraft(checkpoint.Draft, receipt)
            || !_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            throw new InvalidOperationException(
                Text("The typed skill-group receipt does not own the resolved checkpoint."));
        }

        Title = Text("Skill-group receipt");
        AutomationId = Sr5CareerWizardRoutes.SkillGroupReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 3 of 3")));
        body.Add(NativeTheme.Title(Text("Verified saved advancement")));
        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric(Text("Skill group"), checkpoint.Draft.Quote.Name));
        details.Add(NativeTheme.Metric(Text("Group rating"), $"{receipt.GroupRatingBefore} → {receipt.GroupRatingAfter}"));
        details.Add(NativeTheme.Metric(Text("Cost rating"), $"{receipt.CostRatingBefore} → {receipt.CostRatingAfter}"));
        details.Add(NativeTheme.Metric(Text("Group Karma"), $"{receipt.GroupKarmaBefore} → {receipt.GroupKarmaAfter}"));
        details.Add(NativeTheme.Metric(Text("Runner Karma"), $"{receipt.CharacterKarmaBefore} → {receipt.CharacterKarmaAfter}"));
        details.Add(NativeTheme.Metric(Text("Expense"), receipt.ExpenseAmount.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(Text("Transaction"), receipt.TransactionId.ToString("D")));
        details.Add(NativeTheme.Metric(
            Text("Saved revision"),
            checked(checkpoint.Draft.ExpectedContentRevision + 1).ToString(CultureInfo.InvariantCulture)));
        body.Add(NativeTheme.Card(details));
        _durability = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        body.Add(_durability);
        body.Add(NativeTheme.Body(
            Format(
                "receipt {0} · reviewed source {1} · rule {2} · content {3} · runtime {4} · owner {5}",
                receipt.ReceiptDigest,
                receipt.SourceRevisionBefore,
                receipt.RuleDigestBefore,
                checkpoint.Draft.RuntimeAuthority.ContentDigest,
                checkpoint.Draft.RuntimeAuthority.RuntimeDigest,
                checkpoint.Draft.OwnerId.ToString("D")),
            NativeTheme.Muted));
        Button acknowledge = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        acknowledge.AutomationId = "sr5-career-skill-group-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!_store.TryDeleteApplied(
                    Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                    _receipt,
                    out string blocker))
            {
                await DisplayAlertAsync(Text("Receipt remains pending"), blocker, Text("OK"));
                return;
            }
            await Navigation.PopToRootAsync();
        });
        body.Add(acknowledge);
        Button correct = NativeTheme.SecondaryButton(Text("Correct this advancement"));
        correct.AutomationId = "sr5-career-skill-group-receipt-correct";
        correct.IsEnabled = false;
        body.Add(correct);
        body.Add(NativeTheme.Body(
            Text("Correction stays unavailable until Core exposes an atomic compensating service with persisted receipt authority. The original receipt remains immutable."),
            NativeTheme.Danger));
        Content = new ScrollView { Content = body };
        Refresh();
    }

    private async Task CorrectAsync()
    {
        string? reason = await DisplayPromptAsync(
            Text("Correct skill-group advancement"),
            Text("Enter the reason recorded with this compensating transaction."),
            accept: Text("Review correction"),
            cancel: Text("Keep advancement"),
            initialValue: Text("User-requested correction"),
            maxLength: CharacterCareerSkillGroupAdvanceRules.MaximumNameLength);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            Text("Apply compensating correction?"),
            Text("This atomically restores the pre-advance group and Karma values and removes the exact expense."),
            Text("Correct"),
            Text("Cancel"));
        if (!confirmed)
        {
            return;
        }

        CharacterCareerSkillGroupCorrectionPlan correction = await _authority.CorrectAsync(
            _checkpoint,
            _receipt,
            Guid.NewGuid(),
            reason.Trim());
        if (!_store.TryDeleteCorrected(
                Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                _receipt,
                correction,
                out string blocker))
        {
            await DisplayAlertAsync(
                Text("Correction saved; checkpoint remains locked"),
                blocker,
                Text("OK"));
            return;
        }

        await DisplayAlertAsync(
            Text("Correction saved"),
            Format("Compensating transaction {0} restored the reviewed values.", correction.CorrectionId.ToString("D")),
            Text("OK"));
        await Navigation.PopToRootAsync();
    }

    protected override void Refresh()
    {
        long savedRevision = checked(_checkpoint.Draft.ExpectedContentRevision + 1);
        bool stillExact = Coordinator.State.WorkspaceId == _checkpoint.Draft.WorkspaceId
            && Coordinator.State.ContentRevision == savedRevision
            && Coordinator.State.SavedRevision == savedRevision
            && !Coordinator.State.IsDirty;
        _durability.Text = stillExact
            ? Text("The receipt was recovered from the exact clean saved revision.")
            : Text("The runner moved past this receipt; it remains bound to its earlier saved revision.");
        _durability.TextColor = stillExact ? NativeTheme.Muted : NativeTheme.Danger;
    }
}
