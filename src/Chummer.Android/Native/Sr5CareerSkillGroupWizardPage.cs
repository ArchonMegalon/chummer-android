using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

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
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(
            new RunnerSessionSr5CareerSkillGroupPresenter(coordinator).Binding);
        if (coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                "The SR5 SkillGroup route requires the current exact runner revision.");
        }

        _selected = editor.SkillGroups.FirstOrDefault();
        Title = "Advance skill group";
        AutomationId = Sr5CareerWizardRoutes.SkillGroupChoose;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 1 of 3"));
        body.Add(NativeTheme.Title("Choose a skill group"));
        body.Add(NativeTheme.Body(
            "Only exact skill groups projected from this saved SR5 revision are shown. Core owns the internal identity, exact member projection, group integrity, rating maximum, modifiers, Karma cost and expense semantics.",
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel("Skill group"));
        _groups = new Picker
        {
            AutomationId = "sr5-career-skill-group-picker",
            Title = "Saved skill group",
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

        if (editor.OmittedSkillGroupCount > 0 || editor.OmittedReceiptCount > 0)
        {
            Label omitted = NativeTheme.Body(
                $"{editor.OmittedSkillGroupCount.ToString(CultureInfo.InvariantCulture)} skill-group quote(s) and "
                + $"{editor.OmittedReceiptCount.ToString(CultureInfo.InvariantCulture)} receipt(s) were omitted because exact authority could not be reproduced.",
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-skill-group-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton("Review exact advancement");
        _review.AutomationId = "sr5-career-skill-group-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);
        _resume = NativeTheme.SecondaryButton("Resume reviewed advancement");
        _resume.AutomationId = "sr5-career-skill-group-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton("Resolve interrupted apply");
        _resolve.AutomationId = "sr5-career-skill-group-resolve-outcome";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveCheckpointAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton("Abandon reviewed draft");
        _abandon.AutomationId = "sr5-career-skill-group-abandon-reviewed";
        _abandon.Clicked += async (_, _) => await RunAsync(AbandonReviewedAsync);
        body.Add(_abandon);

        Content = new ScrollView { Content = body };
        LoadRecoveryCheckpoint();
        RefreshEnabledState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await Coordinator.InitializeAsync();
            if (_checkpoint?.Phase is (Sr5CareerCheckpointPhase.Applying
                or Sr5CareerCheckpointPhase.Applied)
                && _checkpointAuthority.OwnsCurrentRunner(_checkpoint)
                && Interlocked.CompareExchange(ref _automaticResolutionStarted, 1, 0) == 0)
            {
                await RunAsync(ResolveCheckpointAsync);
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("SkillGroup recovery unavailable", exception.Message, "OK");
        }
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string SkillGroupLabel(CharacterCareerSkillGroupAdvanceQuote group)
        => $"{group.Name} · {group.GroupRating.ToString(CultureInfo.InvariantCulture)} → "
            + $"{group.TargetGroupRating.ToString(CultureInfo.InvariantCulture)} · "
            + (group.KarmaCost >= 0
                ? $"{group.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma"
                : "blocked");

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
            ? "No exact skill-group quote is available."
            : $"Group {_selected.GroupRating.ToString(CultureInfo.InvariantCulture)} → {_selected.TargetGroupRating.ToString(CultureInfo.InvariantCulture)} · "
                + $"cost rating {_selected.CostRating.ToString(CultureInfo.InvariantCulture)} → {_selected.TargetCostRating.ToString(CultureInfo.InvariantCulture)} · "
                + $"{_selected.EnabledMemberCount.ToString(CultureInfo.InvariantCulture)} enabled member(s) · maximum {_selected.RatingMaximum.ToString(CultureInfo.InvariantCulture)}";
        _cost.Text = _selected is null
            ? string.Empty
            : $"Cost {_selected.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma · "
                + $"available {_selected.AvailableKarma.ToString(CultureInfo.InvariantCulture)} · "
                + $"after {(_selected.AvailableKarma - Math.Max(0, _selected.KarmaCost)).ToString(CultureInfo.InvariantCulture)}";
        _blocker.Text = !sr5
            ? "This action is available only to a created SR5 runner."
            : !revisionMatches
                ? "This runner changed. Reopen skill-group advancement."
                : _selected is null
                    ? "No exact skill-group projection is available."
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
        Sr5CareerActiveSkillCoordinator.RequireCreatedSr5(
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
            await DisplayAlertAsync("Cannot review", blocker, "OK");
            return;
        }
        if (!draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync("Runner changed", "Reopen skill-group advancement.", "OK");
            return;
        }

        Sr5CareerSkillGroupCheckpoint candidate = Sr5CareerSkillGroupCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(
                candidate,
                out Sr5CareerSkillGroupCheckpoint stored,
                out blocker))
        {
            await DisplayAlertAsync("Review not checkpointed", blocker, "OK");
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
                "Draft cannot resume",
                "The saved review no longer owns this exact runner revision.",
                "OK");
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
            _recovery.Text = "This recovery lock belongs to another owner or SR5 runner context.";
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }

        Sr5CareerSkillGroupRecoveryResolution resolution =
            await _authority.ResolveAsync(_checkpoint);
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
            _recovery.Text = "The interrupted skill-group apply was verified from the saved receipt ledger.";
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
        _recovery.Text = "Fresh typed projections prove the action was not saved. The reviewed draft may be resumed.";
        _recovery.TextColor = NativeTheme.Muted;
    }

    private async Task AbandonReviewedAsync()
    {
        if (_checkpoint is null || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            await DisplayAlertAsync(
                "Cannot abandon",
                "Only the current owner and exact runner revision may abandon this review.",
                "OK");
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            "Abandon reviewed skill group?",
            "This removes only the durable review checkpoint and does not change the runner.",
            "Abandon",
            "Keep");
        if (!confirmed)
        {
            return;
        }
        if (!_store.TryDeleteReviewed(
                Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync("Checkpoint not deleted", blocker, "OK");
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
                _recovery.Text = "A saved skill-group review is not authorized for this owner and runner revision.";
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            _checkpoint = checkpoint;
            _recoveryDraft = draft;
            _recovery.Text = "A durable reviewed skill-group advancement can be resumed.";
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
            _recovery.Text = "A saved skill-group apply lock is not authorized for this owner and SR5 runner.";
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;
        _recovery.Text = checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            ? "An interrupted apply is locked and will be resolved without replay."
            : "A verified saved skill-group receipt is awaiting acknowledgement.";
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
                "The skill-group preview does not own its durable review checkpoint.");
        }

        Title = "Review skill group";
        AutomationId = Sr5CareerWizardRoutes.SkillGroupReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 2 of 3"));
        body.Add(NativeTheme.Title("Review exact diff"));
        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric("Skill group", _draft.Quote.Name));
        diff.Add(NativeTheme.Metric(
            "Group rating",
            $"{_draft.Quote.GroupRating} → {_draft.Quote.TargetGroupRating}"));
        diff.Add(NativeTheme.Metric(
            "Cost rating",
            $"{_draft.Quote.CostRating} → {_draft.Quote.TargetCostRating}"));
        diff.Add(NativeTheme.Metric(
            "Group Karma points",
            $"{_draft.Quote.KarmaPoints} → {_draft.Plan.SavedGroupKarmaPoints}"));
        diff.Add(NativeTheme.Metric(
            "Runner Karma",
            $"{_draft.Quote.AvailableKarma} → {_draft.Plan.SavedCharacterKarma}"));
        diff.Add(NativeTheme.Metric("Enabled members", _draft.Quote.EnabledMemberCount.ToString(CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric("Rating maximum", _draft.Quote.RatingMaximum.ToString(CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(
            "Application time",
            $"{_draft.Quote.ApplicationDuration} · {_draft.Quote.TimeAuthority}"));
        foreach (CharacterCareerSkillGroupPrerequisiteResult prerequisite in _draft.Quote.Prerequisites)
        {
            diff.Add(NativeTheme.Metric(
                $"Prerequisite · {prerequisite.Prerequisite}",
                $"{(prerequisite.Satisfied ? "satisfied" : "blocked")} · {prerequisite.Authority}"));
        }
        diff.Add(NativeTheme.Metric("Expense", _draft.Plan.ExpenseReason));
        diff.Add(NativeTheme.Metric("Transaction", _draft.Plan.TransactionId.ToString("D")));
        diff.Add(NativeTheme.Metric("Expense identity", _draft.Plan.ExpenseId.ToString("D")));
        diff.Add(NativeTheme.Metric("Date", _draft.Plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric("Undo", $"{_draft.Plan.KarmaUndoType} · {_draft.Plan.UndoObjectId}"));
        body.Add(NativeTheme.Card(diff));
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        body.Add(_blocker);
        _apply = NativeTheme.PrimaryButton("Apply and verify once");
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
            "The exact checkpoint moves to Applying before mutation. Success is shown only after atomic save and a fresh recoverable receipt projection match the reviewed identity, costs and digests.",
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
            ? "The runner revision, owner, quote, or durable checkpoint changed. This preview cannot apply."
            : attempted
                ? "The one-shot apply is running or awaiting authoritative recovery."
                : string.Empty;
    }

    private async Task ApplyAsync()
    {
        if (!_checkpoint.MatchesActionDraft(_draft)
            || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            await DisplayAlertAsync(
                "Apply blocked",
                "The exact reviewed action no longer owns this runner.",
                "OK");
            return;
        }
        if (!_store.TryBeginApply(
                Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                out Sr5CareerSkillGroupCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync(
                "Apply blocked",
                blocker,
                "OK");
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
                "Outcome unresolved",
                $"{result.Message} The Applying checkpoint cannot be cleared or replayed.",
                "OK");
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5CareerSkillGroupCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerSkillGroupCheckpoint resolved,
                out blocker))
        {
            await DisplayAlertAsync("Outcome not checkpointed", blocker, "OK");
            return;
        }
        _checkpoint = resolved;
        if (result.Status == Sr5CareerSkillGroupApplyStatus.RejectedBeforeMutation)
        {
            await DisplayAlertAsync(
                "Not applied",
                "Fresh typed projections prove no skill-group or receipt mutation was saved. Return and resume the review before retrying.",
                "OK");
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
                "The typed skill-group receipt does not own the resolved checkpoint.");
        }

        Title = "Skill-group receipt";
        AutomationId = Sr5CareerWizardRoutes.SkillGroupReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 3 of 3"));
        body.Add(NativeTheme.Title("Verified saved advancement"));
        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric("Skill group", checkpoint.Draft.Quote.Name));
        details.Add(NativeTheme.Metric("Group rating", $"{receipt.GroupRatingBefore} → {receipt.GroupRatingAfter}"));
        details.Add(NativeTheme.Metric("Cost rating", $"{receipt.CostRatingBefore} → {receipt.CostRatingAfter}"));
        details.Add(NativeTheme.Metric("Group Karma", $"{receipt.GroupKarmaBefore} → {receipt.GroupKarmaAfter}"));
        details.Add(NativeTheme.Metric("Runner Karma", $"{receipt.CharacterKarmaBefore} → {receipt.CharacterKarmaAfter}"));
        details.Add(NativeTheme.Metric("Expense", receipt.ExpenseAmount.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric("Transaction", receipt.TransactionId.ToString("D")));
        details.Add(NativeTheme.Metric(
            "Saved revision",
            checked(checkpoint.Draft.ExpectedContentRevision + 1).ToString(CultureInfo.InvariantCulture)));
        body.Add(NativeTheme.Card(details));
        _durability = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        body.Add(_durability);
        body.Add(NativeTheme.Body(
            $"receipt {receipt.ReceiptDigest} · reviewed source {receipt.SourceRevisionBefore} · rule {receipt.RuleDigestBefore} · owner {checkpoint.Draft.OwnerId:D}",
            NativeTheme.Muted));
        Button acknowledge = NativeTheme.PrimaryButton("Acknowledge receipt");
        acknowledge.AutomationId = "sr5-career-skill-group-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!_store.TryDeleteApplied(
                    Sr5CareerSkillGroupCheckpointCas.From(_checkpoint),
                    _receipt,
                    out string blocker))
            {
                await DisplayAlertAsync("Receipt remains pending", blocker, "OK");
                return;
            }
            await Navigation.PopToRootAsync();
        });
        body.Add(acknowledge);
        Button correct = NativeTheme.SecondaryButton("Correct this advancement");
        correct.AutomationId = "sr5-career-skill-group-receipt-correct";
        correct.Clicked += async (_, _) => await RunAsync(CorrectAsync);
        body.Add(correct);
        body.Add(NativeTheme.Body(
            "Correction is a separate typed compensating transaction. It restores the reviewed group and Karma values and removes the exact original expense; it never edits the receipt in place.",
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        Refresh();
    }

    private async Task CorrectAsync()
    {
        string? reason = await DisplayPromptAsync(
            "Correct skill-group advancement",
            "Enter the reason recorded with this compensating transaction.",
            accept: "Review correction",
            cancel: "Keep advancement",
            initialValue: "User-requested correction",
            maxLength: CharacterCareerSkillGroupAdvanceRules.MaximumNameLength);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            "Apply compensating correction?",
            "This atomically restores the pre-advance group and Karma values and removes the exact expense.",
            "Correct",
            "Cancel");
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
                "Correction saved; checkpoint remains locked",
                blocker,
                "OK");
            return;
        }

        await DisplayAlertAsync(
            "Correction saved",
            $"Compensating transaction {correction.CorrectionId:D} restored the reviewed values.",
            "OK");
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
            ? "The receipt was recovered from the exact clean saved revision."
            : "The runner moved past this receipt; it remains bound to its earlier saved revision.";
        _durability.TextColor = stillExact ? NativeTheme.Muted : NativeTheme.Danger;
    }
}
