using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

internal sealed record Sr5CareerKnowledgeSkillWizardDependencies(
    Sr5CareerKnowledgeSkillCheckpointStore Store,
    ISr5CareerKnowledgeSkillCheckpointAuthority CheckpointAuthority);

/// <summary>
/// Phone-deep first step. The page only selects an exact Core quote and creates
/// a durable review checkpoint; no mutation is reachable from this surface.
/// </summary>
public sealed class Sr5CareerKnowledgeSkillWizardPage : NativePageBase
{
    private readonly CareerKnowledgeSkillAdvanceEditorState _editor;
    private readonly Sr5CareerKnowledgeSkillCoordinator _authority;
    private readonly Sr5CareerKnowledgeSkillCheckpointStore _store;
    private readonly ISr5CareerKnowledgeSkillCheckpointAuthority _checkpointAuthority;
    private readonly Picker _skills;
    private readonly Label _rating;
    private readonly Label _cost;
    private readonly Label _blocker;
    private readonly Label _recovery;
    private readonly Button _review;
    private readonly Button _resume;
    private readonly Button _resolve;
    private readonly Button _abandon;
    private CharacterCareerKnowledgeSkillAdvanceQuote? _selected;
    private Sr5CareerKnowledgeSkillDraft? _recoveryDraft;
    private Sr5CareerKnowledgeSkillCheckpoint? _checkpoint;
    private int _automaticResolutionStarted;

    public Sr5CareerKnowledgeSkillWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerKnowledgeSkillAdvanceEditorState editor)
        : this(coordinator, editor, CreateDependencies(coordinator, editor))
    {
    }

    private Sr5CareerKnowledgeSkillWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerKnowledgeSkillAdvanceEditorState editor,
        Sr5CareerKnowledgeSkillWizardDependencies dependencies)
        : this(
            coordinator,
            editor,
            new Sr5CareerKnowledgeSkillCoordinator(
                new RunnerSessionSr5CareerKnowledgeSkillPresenter(coordinator),
                dependencies.CheckpointAuthority),
            dependencies.Store,
            dependencies.CheckpointAuthority)
    {
    }

    private static Sr5CareerKnowledgeSkillWizardDependencies CreateDependencies(
        RunnerSessionCoordinator coordinator,
        CareerKnowledgeSkillAdvanceEditorState editor)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(editor);
        PreferencesSr5CareerCheckpointOwnerAuthority ownerAuthority = new();
        Sr5CareerKnowledgeSkillLiveCheckpointAuthority checkpointAuthority = new(
            ownerAuthority,
            editor,
            () => new RunnerSessionSr5CareerKnowledgeSkillPresenter(coordinator).Binding);
        return new(
            Sr5CareerKnowledgeSkillCheckpointStore.CreateDefault(checkpointAuthority),
            checkpointAuthority);
    }

    internal Sr5CareerKnowledgeSkillWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerKnowledgeSkillAdvanceEditorState editor,
        Sr5CareerKnowledgeSkillCoordinator authority,
        Sr5CareerKnowledgeSkillCheckpointStore store,
        ISr5CareerKnowledgeSkillCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerKnowledgeSkillPresenter(coordinator).Binding);
        if (coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                Text("The SR5 Knowledge/Language route requires the current exact runner revision."));
        }

        _selected = editor.Skills.FirstOrDefault();
        Title = Text("Advance knowledge skill");
        AutomationId = Sr5CareerWizardRoutes.KnowledgeSkillChoose;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 1 of 3")));
        body.Add(NativeTheme.Title(Text("Choose a Knowledge or Language skill")));
        body.Add(NativeTheme.Body(
            Text("Only exact Knowledge and Language skills projected from this saved SR5 revision are shown. Core owns nullable source identity, native-language eligibility, maximum rating, Karma cost and expense semantics."),
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel(Text("Knowledge / Language skill")));
        _skills = new Picker
        {
            AutomationId = "sr5-career-knowledge-skill-picker",
            Title = Text("Saved knowledge skill"),
            ItemsSource = editor.Skills.Select(KnowledgeSkillLabel).ToArray(),
            SelectedIndex = editor.Skills.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _skills.SelectedIndexChanged += (_, _) => SelectKnowledgeSkill();
        body.Add(_skills);

        _rating = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _rating.AutomationId = "sr5-career-knowledge-skill-rating";
        body.Add(_rating);
        _cost = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _cost.AutomationId = "sr5-career-knowledge-skill-cost";
        body.Add(_cost);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "sr5-career-knowledge-skill-blocker";
        body.Add(_blocker);
        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-career-knowledge-skill-recovery";
        body.Add(_recovery);

        if (editor.OmittedSkillCount > 0 || editor.OmittedReceiptCount > 0)
        {
            Label omitted = NativeTheme.Body(
                Format(
                    "{0} knowledge skill quote(s) and {1} receipt(s) were omitted because exact authority could not be reproduced.",
                    editor.OmittedSkillCount.ToString(CultureInfo.InvariantCulture),
                    editor.OmittedReceiptCount.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-knowledge-skill-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton(Text("Review exact advancement"));
        _review.AutomationId = "sr5-career-knowledge-skill-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);
        _resume = NativeTheme.SecondaryButton(Text("Resume reviewed advancement"));
        _resume.AutomationId = "sr5-career-knowledge-skill-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton(Text("Resolve interrupted apply"));
        _resolve.AutomationId = "sr5-career-knowledge-skill-resolve-outcome";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveCheckpointAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed draft"));
        _abandon.AutomationId = "sr5-career-knowledge-skill-abandon-reviewed";
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
        // A child review page may have durably moved Applying back to
        // Reviewed, or forward to Applied, while this chooser was hidden.
        // Re-read the journal before deciding whether recovery may run.
        LoadRecoveryCheckpoint();
        RefreshEnabledState();
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

    private static string KnowledgeSkillLabel(CharacterCareerKnowledgeSkillAdvanceQuote skill)
        => skill.KarmaCost >= 0
            ? Format(
                "{0} · {1} · {2} → {3} · {4} Karma",
                skill.Name,
                skill.SkillType,
                skill.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
                (skill.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture),
                skill.KarmaCost.ToString(CultureInfo.InvariantCulture))
            : Format(
                "{0} · {1} · {2} → {3} · blocked",
                skill.Name,
                skill.SkillType,
                skill.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
                (skill.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture));

    private void SelectKnowledgeSkill()
    {
        _selected = _skills.SelectedIndex >= 0
            && _skills.SelectedIndex < _editor.Skills.Count
            ? _editor.Skills[_skills.SelectedIndex]
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
        _skills.IsEnabled = revisionMatches && _checkpoint is null && _editor.Skills.Count > 0;
        _rating.Text = _selected is null
            ? Text("No exact knowledge skill quote is available.")
            : Format(
                "Current {0} · after {1} · maximum {2}",
                _selected.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
                (_selected.TotalBaseRating + 1).ToString(CultureInfo.InvariantCulture),
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
                ? Text("This runner changed. Reopen knowledge skill advancement.")
                : _selected is null
                    ? Text("No exact knowledge skill projection is available.")
                    : Sr5CareerKnowledgeSkillDraft.BlockerText(_selected.Blocker);
        _review.IsEnabled = revisionMatches
            && _checkpoint is null
            && _selected is { CanAdvance: true }
            && CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(_selected);
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
            new RunnerSessionSr5CareerKnowledgeSkillPresenter(Coordinator).Binding);
        if (!Sr5CareerKnowledgeSkillDraft.TryCreate(
                _editor,
                _selected,
                _checkpointAuthority.CurrentOwnerId,
                Guid.NewGuid(),
                DateTime.Now,
                out Sr5CareerKnowledgeSkillDraft draft,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Cannot review"), blocker, Text("OK"));
            return;
        }
        if (!draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(Text("Runner changed"), Text("Reopen knowledge skill advancement."), Text("OK"));
            return;
        }

        Sr5CareerKnowledgeSkillCheckpoint candidate = Sr5CareerKnowledgeSkillCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(
                candidate,
                out Sr5CareerKnowledgeSkillCheckpoint stored,
                out blocker))
        {
            await DisplayAlertAsync(Text("Review not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = stored;
        _recoveryDraft = draft;
        await Navigation.PushAsync(new Sr5CareerKnowledgeSkillReviewPage(
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
        await Navigation.PushAsync(new Sr5CareerKnowledgeSkillReviewPage(
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

        Sr5CareerKnowledgeSkillRecoveryResolution resolution =
            await _authority.ResolveAsync(_checkpoint);
        if (resolution.Status == Sr5CareerKnowledgeSkillRecoveryStatus.OutcomeUnknown)
        {
            _recovery.Text = resolution.Message;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }

        Sr5CareerKnowledgeSkillCheckpoint stored = _checkpoint;
        if (_checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            && !_store.TryRecordAuthoritativeResolution(
                Sr5CareerKnowledgeSkillCheckpointCas.From(_checkpoint),
                resolution,
                out stored,
                out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;
        if (resolution.Status == Sr5CareerKnowledgeSkillRecoveryStatus.AppliedVerified
            && resolution.Receipt is { } receipt)
        {
            _recovery.Text = Text("The interrupted knowledge skill apply was verified from the saved receipt ledger.");
            _recovery.TextColor = NativeTheme.Muted;
            await Navigation.PushAsync(new Sr5CareerKnowledgeSkillReceiptPage(
                Coordinator,
                receipt,
                stored,
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
            Text("Abandon reviewed knowledge skill?"),
            Text("This removes only the durable review checkpoint and does not change the runner."),
            Text("Abandon"),
            Text("Keep"));
        if (!confirmed)
        {
            return;
        }
        if (!_store.TryDeleteReviewed(
                Sr5CareerKnowledgeSkillCheckpointCas.From(_checkpoint),
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
        if (!_store.TryRead(out Sr5CareerKnowledgeSkillCheckpoint checkpoint, out string blocker))
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
                    out Sr5CareerKnowledgeSkillDraft draft,
                    out _)
                || !_checkpointAuthority.OwnsReviewed(checkpoint))
            {
                _recovery.Text = Text("A saved knowledge skill review is not authorized for this owner and runner revision.");
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            _checkpoint = checkpoint;
            _recoveryDraft = draft;
            _recovery.Text = Text("A durable reviewed knowledge skill advancement can be resumed.");
            _recovery.TextColor = NativeTheme.Muted;
            int index = _editor.Skills
                .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                .First(pair => pair.candidate.Identity == draft.Quote.Identity)
                .candidateIndex;
            _skills.SelectedIndex = index;
            return;
        }
        if (!_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            _recovery.Text = Text("A saved knowledge skill apply lock is not authorized for this owner and SR5 runner.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;
        _recovery.Text = checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            ? Text("An interrupted apply is locked and will be resolved without replay.")
            : Text("A verified saved knowledge skill receipt is awaiting acknowledgement.");
        _recovery.TextColor = NativeTheme.Muted;
    }
}

/// <summary>
/// Step two is a pure preview. Apply is exposed only after the durable CAS
/// journal has moved this exact owner/action from Reviewed to Applying.
/// </summary>
public sealed class Sr5CareerKnowledgeSkillReviewPage : NativePageBase
{
    private readonly Sr5CareerKnowledgeSkillDraft _draft;
    private Sr5CareerKnowledgeSkillCheckpoint _checkpoint;
    private readonly Sr5CareerKnowledgeSkillCoordinator _authority;
    private readonly Sr5CareerKnowledgeSkillCheckpointStore _store;
    private readonly ISr5CareerKnowledgeSkillCheckpointAuthority _checkpointAuthority;
    private readonly Button _apply;
    private readonly Label _blocker;
    private int _attempted;

    internal Sr5CareerKnowledgeSkillReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerKnowledgeSkillDraft draft,
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
        Sr5CareerKnowledgeSkillCoordinator authority,
        Sr5CareerKnowledgeSkillCheckpointStore store,
        ISr5CareerKnowledgeSkillCheckpointAuthority checkpointAuthority) : base(coordinator)
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
                Text("The knowledge skill preview does not own its durable review checkpoint."));
        }

        Title = Text("Review knowledge skill");
        AutomationId = Sr5CareerWizardRoutes.KnowledgeSkillReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 2 of 3")));
        body.Add(NativeTheme.Title(Text("Review exact diff")));
        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric(Text("Knowledge / Language"), $"{_draft.Quote.Name} · {_draft.Quote.SkillType}"));
        diff.Add(NativeTheme.Metric(
            Text("Rating"),
            $"{_draft.Quote.TotalBaseRating} → {_draft.Quote.TotalBaseRating + 1}"));
        diff.Add(NativeTheme.Metric(
            Text("Skill Karma points"),
            $"{_draft.Quote.KarmaPoints} → {_draft.Plan.SavedSkillKarmaPoints}"));
        diff.Add(NativeTheme.Metric(
            Text("Runner Karma"),
            $"{_draft.Quote.AvailableKarma} → {_draft.Plan.SavedCharacterKarma}"));
        diff.Add(NativeTheme.Metric(Text("Maximum"), _draft.Quote.RatingMaximum.ToString(CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(
            Text("Source identity"),
            _draft.Quote.Identity.SourceSkillId?.ToString("D") ?? Text("custom saved skill")));
        diff.Add(NativeTheme.Metric(Text("Expense"), _draft.Plan.ExpenseReason));
        diff.Add(NativeTheme.Metric(Text("Expense identity"), _draft.Plan.ExpenseId.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Date"), _draft.Plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(Text("Undo"), $"{_draft.Plan.KarmaUndoType} · {_draft.Plan.UndoObjectId}"));
        body.Add(NativeTheme.Card(diff));
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        body.Add(_blocker);
        _apply = NativeTheme.PrimaryButton(Text("Apply and verify once"));
        _apply.AutomationId = "sr5-career-knowledge-skill-apply";
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
            Text("The exact checkpoint moves to Applying before mutation. Success is shown only after atomic save and a fresh recoverable receipt projection match the reviewed identity, costs and digests."),
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
                Sr5CareerKnowledgeSkillCheckpointCas.From(_checkpoint),
                out Sr5CareerKnowledgeSkillCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync(
                Text("Apply blocked"),
                blocker,
                Text("OK"));
            return;
        }
        _checkpoint = applying;

        Sr5CareerKnowledgeSkillApplyResult result = await _authority.ApplyAsync(
            _draft,
            applying,
            _store);
        if (result.Status == Sr5CareerKnowledgeSkillApplyStatus.OutcomeUnknown)
        {
            await DisplayAlertAsync(
                Text("Outcome unresolved"),
                Format("{0} The Applying checkpoint cannot be cleared or replayed.", result.Message),
                Text("OK"));
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5CareerKnowledgeSkillCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerKnowledgeSkillCheckpoint resolved,
                out blocker))
        {
            await DisplayAlertAsync(Text("Outcome not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = resolved;
        if (result.Status == Sr5CareerKnowledgeSkillApplyStatus.RejectedBeforeMutation)
        {
            await DisplayAlertAsync(
                Text("Not applied"),
                Text("Fresh typed projections prove no knowledge skill or receipt mutation was saved. Return and resume the review before retrying."),
                Text("OK"));
            return;
        }

        await Navigation.PushAsync(new Sr5CareerKnowledgeSkillReceiptPage(
            Coordinator,
            result.Receipt!,
            resolved,
            _store,
            _checkpointAuthority));
    }
}

public sealed class Sr5CareerKnowledgeSkillReceiptPage : NativePageBase
{
    private readonly CharacterCareerKnowledgeSkillAdvanceReceipt _receipt;
    private readonly Sr5CareerKnowledgeSkillCheckpoint _checkpoint;
    private readonly Sr5CareerKnowledgeSkillCheckpointStore _store;
    private readonly ISr5CareerKnowledgeSkillCheckpointAuthority _checkpointAuthority;
    private readonly Label _durability;

    internal Sr5CareerKnowledgeSkillReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCareerKnowledgeSkillAdvanceReceipt receipt,
        Sr5CareerKnowledgeSkillCheckpoint checkpoint,
        Sr5CareerKnowledgeSkillCheckpointStore store,
        ISr5CareerKnowledgeSkillCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applied
            || !Sr5CareerKnowledgeSkillCoordinator.ReceiptMatchesDraft(checkpoint.Draft, receipt)
            || !_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            throw new InvalidOperationException(
                Text("The typed knowledge skill receipt does not own the resolved checkpoint."));
        }

        Title = Text("Knowledge / Language receipt");
        AutomationId = Sr5CareerWizardRoutes.KnowledgeSkillReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 3 of 3")));
        body.Add(NativeTheme.Title(Text("Verified saved advancement")));
        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric(Text("Knowledge / Language"), $"{receipt.Name} · {receipt.SkillType}"));
        details.Add(NativeTheme.Metric(Text("Skill Karma"), $"{receipt.SkillKarmaBefore} → {receipt.SkillKarmaAfter}"));
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
                "receipt {0} · reviewed source {1} · rule {2} · owner {3}",
                receipt.ReceiptDigest,
                receipt.SourceRevision,
                receipt.RuleDigest,
                checkpoint.Draft.OwnerId.ToString("D")),
            NativeTheme.Muted));
        Button acknowledge = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        acknowledge.AutomationId = "sr5-career-knowledge-skill-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!_store.TryDeleteApplied(
                    Sr5CareerKnowledgeSkillCheckpointCas.From(_checkpoint),
                    _receipt,
                    out string blocker))
            {
                await DisplayAlertAsync(Text("Receipt remains pending"), blocker, Text("OK"));
                return;
            }
            await Navigation.PopToRootAsync();
        });
        body.Add(acknowledge);
        Content = new ScrollView { Content = body };
        Refresh();
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
