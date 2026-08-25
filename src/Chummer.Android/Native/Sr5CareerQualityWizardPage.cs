using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record Sr5CareerQualityWizardDependencies(
    Sr5CareerQualityCheckpointStore Store,
    ISr5CareerQualityCheckpointAuthority CheckpointAuthority);

/// <summary>
/// Phone-deep choose step. Labels are display-only; selection and every later
/// transition use Operation + InternalId + SourceId + all Core digests.
/// </summary>
public sealed class Sr5CareerQualityWizardPage : NativePageBase
{
    private readonly CareerQualityEditorState _editor;
    private readonly Sr5CareerQualityCoordinator _authority;
    private readonly Sr5CareerQualityCheckpointStore _store;
    private readonly ISr5CareerQualityCheckpointAuthority _checkpointAuthority;
    private readonly Picker _qualities;
    private readonly Label _summary;
    private readonly Label _authorityDetail;
    private readonly Label _blocker;
    private readonly Label _recovery;
    private readonly Button _review;
    private readonly Button _resume;
    private readonly Button _resolve;
    private readonly Button _abandon;
    private CharacterCareerQualityQuote? _selected;
    private Sr5CareerQualityDraft? _recoveryDraft;
    private Sr5CareerQualityCheckpoint? _checkpoint;
    private int _automaticResolutionStarted;

    public Sr5CareerQualityWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerQualityEditorState editor)
        : this(coordinator, editor, CreateDependencies(coordinator, editor))
    {
    }

    private Sr5CareerQualityWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerQualityEditorState editor,
        Sr5CareerQualityWizardDependencies dependencies)
        : this(
            coordinator,
            editor,
            new Sr5CareerQualityCoordinator(
                new RunnerSessionSr5CareerQualityPresenter(coordinator),
                dependencies.CheckpointAuthority),
            dependencies.Store,
            dependencies.CheckpointAuthority)
    {
    }

    private static Sr5CareerQualityWizardDependencies CreateDependencies(
        RunnerSessionCoordinator coordinator,
        CareerQualityEditorState editor)
    {
        PreferencesSr5CareerCheckpointOwnerAuthority ownerAuthority = new();
        Sr5CareerQualityLiveCheckpointAuthority checkpointAuthority = new(
            ownerAuthority,
            editor,
            () => new RunnerSessionSr5CareerQualityPresenter(coordinator).Binding);
        return new(
            Sr5CareerQualityCheckpointStore.CreateDefault(checkpointAuthority),
            checkpointAuthority);
    }

    internal Sr5CareerQualityWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerQualityEditorState editor,
        Sr5CareerQualityCoordinator authority,
        Sr5CareerQualityCheckpointStore store,
        ISr5CareerQualityCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        if (coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.WorkspaceRevision
            || coordinator.State.SavedRevision != editor.SavedRevision)
        {
            throw new InvalidOperationException(
                "The SR5 quality route requires the exact clean saved runner revision.");
        }

        _selected = editor.Quotes.FirstOrDefault();
        Title = "Change quality";
        AutomationId = Sr5CareerWizardRoutes.QualityChoose;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 1 of 3"));
        body.Add(NativeTheme.Title("Choose a quality operation"));
        body.Add(NativeTheme.Body(
            "Only exact Core candidates from enabled sources, custom data and the active GM policy are shown. Android never identifies a quality by its label and never applies a partial effect family.",
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel("Quality and operation"));
        _qualities = new Picker
        {
            AutomationId = "sr5-career-quality-picker",
            Title = "Exact quality operation",
            ItemsSource = editor.Quotes.Select(QualityLabel).ToArray(),
            SelectedIndex = editor.Quotes.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _qualities.SelectedIndexChanged += (_, _) => SelectQuality();
        body.Add(_qualities);
        _summary = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _summary.AutomationId = "sr5-career-quality-summary";
        body.Add(_summary);
        _authorityDetail = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _authorityDetail.AutomationId = "sr5-career-quality-authority";
        body.Add(_authorityDetail);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "sr5-career-quality-blocker";
        body.Add(_blocker);
        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-career-quality-recovery";
        body.Add(_recovery);

        if (editor.OmittedCandidateCount != 0 || editor.OmittedReceiptCount != 0)
        {
            Label omitted = NativeTheme.Body(
                $"{editor.OmittedCandidateCount.ToString(CultureInfo.InvariantCulture)} candidate(s) and "
                + $"{editor.OmittedReceiptCount.ToString(CultureInfo.InvariantCulture)} receipt(s) were omitted. The whole quality lane remains fail-closed.",
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-quality-omitted-authority";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton("Review exact quality diff");
        _review.AutomationId = "sr5-career-quality-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);
        _resume = NativeTheme.SecondaryButton("Resume reviewed quality change");
        _resume.AutomationId = "sr5-career-quality-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton("Resolve interrupted atomic commit");
        _resolve.AutomationId = "sr5-career-quality-resolve-outcome";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveCheckpointAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton("Abandon reviewed draft");
        _abandon.AutomationId = "sr5-career-quality-abandon-reviewed";
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
            await DisplayAlertAsync("Quality recovery unavailable", exception.Message, "OK");
        }
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string QualityLabel(CharacterCareerQualityQuote quote)
        => $"{OperationLabel(quote.Operation)} · {quote.Definition.Name} · level {quote.LevelBefore} → {quote.LevelAfter} · "
            + (quote.CanApply ? $"{quote.RuleKarmaCost} Karma" : quote.Blocker.ToString());

    private static string OperationLabel(CharacterCareerQualityOperation operation)
        => operation switch
        {
            CharacterCareerQualityOperation.AcquireLevel => "Acquire level",
            CharacterCareerQualityOperation.RemoveLevel => "Remove level",
            CharacterCareerQualityOperation.RemoveAllLevels => "Remove all levels",
            _ => "Unsupported operation"
        };

    private void SelectQuality()
    {
        _selected = _qualities.SelectedIndex >= 0
            && _qualities.SelectedIndex < _editor.Quotes.Count
            ? _editor.Quotes[_qualities.SelectedIndex]
            : null;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.WorkspaceRevision
            && Coordinator.State.SavedRevision == _editor.SavedRevision
            && !Coordinator.State.IsDirty
            && string.IsNullOrWhiteSpace(Coordinator.State.Error);
        bool completeAuthority = _editor.OmittedCandidateCount == 0
            && _editor.OmittedReceiptCount == 0;
        bool reviewedOwned = _checkpoint is not null
            && _recoveryDraft is not null
            && _checkpoint.MatchesActionDraft(_recoveryDraft)
            && _checkpointAuthority.OwnsReviewed(_checkpoint);
        _qualities.IsEnabled = revisionMatches && completeAuthority && _checkpoint is null;
        _summary.Text = _selected is null
            ? "No exact quality operation is available."
            : $"{_selected.Definition.Type} · {OperationLabel(_selected.Operation)} · level {_selected.LevelBefore} → {_selected.LevelAfter} · "
                + $"runner Karma {_selected.AvailableKarma} → {_selected.AvailableKarma + _selected.CharacterKarmaDelta}";
        _authorityDetail.Text = _selected is null
            ? string.Empty
            : $"source {_selected.SourceName} · source id {_selected.Identity.SourceId:D} · internal id {_selected.Identity.InternalId:D} · "
                + $"effects {_selected.Authority.Effects.MutationCount} · GM {(_selected.Authority.GmAllows ? "allowed" : "blocked")}";
        _blocker.Text = !revisionMatches
            ? "The saved runner revision changed. Reopen quality advancement."
            : !completeAuthority
                ? "Ambiguous candidate or receipt authority was omitted. No quality operation may continue."
                : _selected is null
                    ? "No exact quality projection is available."
                    : Sr5CareerQualityDraft.BlockerText(_selected.Blocker);
        _review.IsEnabled = revisionMatches
            && completeAuthority
            && _checkpoint is null
            && _selected is { CanApply: true }
            && CharacterCareerQualityRules.IsCoherent(_selected)
            && _selected.Authority.Effects.IsExact
            && _selected.Authority.Effects.UnsupportedFamilies.Count == 0;
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
        if (_selected is null)
        {
            await DisplayAlertAsync("Cannot review", "Choose an exact quality operation.", "OK");
            return;
        }
        Sr5CareerQualityDraft draft = await _authority.ReviewAsync(
            _editor,
            _selected,
            Guid.NewGuid(),
            DateTime.Now);
        Sr5CareerQualityCheckpoint candidate = Sr5CareerQualityCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(candidate, out Sr5CareerQualityCheckpoint stored, out string blocker))
        {
            await DisplayAlertAsync("Review not checkpointed", blocker, "OK");
            return;
        }
        _checkpoint = stored;
        _recoveryDraft = draft;
        await Navigation.PushAsync(new Sr5CareerQualityReviewPage(
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
            || !_checkpoint.MatchesActionDraft(_recoveryDraft))
        {
            await DisplayAlertAsync("Draft cannot resume", "The exact quality review no longer owns this runner.", "OK");
            return;
        }
        await Navigation.PushAsync(new Sr5CareerQualityReviewPage(
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
            _recovery.Text = "This quality recovery lock belongs to another owner or runner revision.";
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        Sr5CareerQualityRecoveryResolution resolution = await _authority.ResolveAsync(_checkpoint);
        if (resolution.Status == Sr5CareerQualityRecoveryStatus.OutcomeUnknown)
        {
            _recovery.Text = resolution.Message;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        Sr5CareerQualityCheckpoint stored = _checkpoint;
        if (_checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            && !_store.TryRecordAuthoritativeResolution(
                Sr5CareerQualityCheckpointCas.From(_checkpoint),
                resolution,
                out stored,
                out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;
        if (resolution.Status == Sr5CareerQualityRecoveryStatus.AppliedVerified
            && resolution.Receipt is { } receipt)
        {
            await Navigation.PushAsync(new Sr5CareerQualityReceiptPage(
                Coordinator,
                receipt,
                stored,
                _authority,
                _store,
                _checkpointAuthority));
            return;
        }
        LoadRecoveryCheckpoint();
        _recovery.Text = "Fresh authority proves the atomic transaction was not saved. Resume the reviewed draft.";
        _recovery.TextColor = NativeTheme.Muted;
    }

    private async Task AbandonReviewedAsync()
    {
        if (_checkpoint is null || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            "Abandon reviewed quality change?",
            "This removes only the durable review checkpoint. It never changes the runner.",
            "Abandon",
            "Keep");
        if (!confirmed)
        {
            return;
        }
        if (!_store.TryDeleteReviewed(Sr5CareerQualityCheckpointCas.From(_checkpoint), out string blocker))
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
        if (!_store.TryRead(out Sr5CareerQualityCheckpoint checkpoint, out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = string.IsNullOrWhiteSpace(blocker) ? NativeTheme.Muted : NativeTheme.Danger;
            return;
        }
        if (checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed)
        {
            if (!checkpoint.TryResume(_editor, out Sr5CareerQualityDraft draft, out blocker)
                || !_checkpointAuthority.OwnsReviewed(checkpoint))
            {
                _recovery.Text = string.IsNullOrWhiteSpace(blocker)
                    ? "A saved quality review is not authorized for this exact runner."
                    : blocker;
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            _checkpoint = checkpoint;
            _recoveryDraft = draft;
            _recovery.Text = "A durable reviewed quality operation can be resumed.";
            _recovery.TextColor = NativeTheme.Muted;
            int index = _editor.Quotes
                .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                .First(pair => pair.candidate.Operation == draft.Review.Quote.Operation
                    && pair.candidate.Identity == draft.Review.Quote.Identity)
                .candidateIndex;
            _qualities.SelectedIndex = index;
            return;
        }
        if (!_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            _recovery.Text = "A saved quality commit lock is not authorized for this owner and runner.";
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;
        _recovery.Text = checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            ? "An interrupted atomic commit is locked and will be resolved without replay."
            : "A verified saved quality receipt is awaiting acknowledgement or correction.";
        _recovery.TextColor = NativeTheme.Muted;
    }
}

public sealed class Sr5CareerQualityReviewPage : NativePageBase
{
    private readonly Sr5CareerQualityDraft _draft;
    private Sr5CareerQualityCheckpoint _checkpoint;
    private readonly Sr5CareerQualityCoordinator _authority;
    private readonly Sr5CareerQualityCheckpointStore _store;
    private readonly ISr5CareerQualityCheckpointAuthority _checkpointAuthority;
    private readonly Button _apply;
    private readonly Label _blocker;
    private int _attempted;

    internal Sr5CareerQualityReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerQualityDraft draft,
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityCoordinator authority,
        Sr5CareerQualityCheckpointStore store,
        ISr5CareerQualityCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _draft = draft;
        _checkpoint = checkpoint;
        _authority = authority;
        _store = store;
        _checkpointAuthority = checkpointAuthority;
        if (!_checkpoint.MatchesActionDraft(_draft)
            || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            throw new InvalidOperationException("The quality preview does not own its exact Reviewed checkpoint.");
        }

        Title = "Review quality";
        AutomationId = Sr5CareerWizardRoutes.QualityReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 2 of 3"));
        body.Add(NativeTheme.Title("Review exact atomic diff"));
        CharacterCareerQualityQuote quote = draft.Review.Quote;
        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric("Quality", quote.Definition.Name));
        diff.Add(NativeTheme.Metric("Operation", quote.Operation.ToString()));
        diff.Add(NativeTheme.Metric("Typed identity", $"{quote.Identity.InternalId:D} · source {quote.Identity.SourceId:D}"));
        diff.Add(NativeTheme.Metric("Level", $"{quote.LevelBefore} → {quote.LevelAfter}"));
        diff.Add(NativeTheme.Metric("Runner Karma", $"{quote.AvailableKarma} → {quote.AvailableKarma + quote.CharacterKarmaDelta}"));
        diff.Add(NativeTheme.Metric("Rule cost / delta", $"{quote.RuleKarmaCost} / {quote.CharacterKarmaDelta}"));
        diff.Add(NativeTheme.Metric("Source", $"{quote.SourceName} · enabled {quote.Definition.SourceEnabled}"));
        diff.Add(NativeTheme.Metric("GM policy", $"allowed {quote.Authority.GmAllows} · free approved {quote.Authority.GmFreeCostApproved}"));
        diff.Add(NativeTheme.Metric("Effects", $"exact {quote.Authority.Effects.IsExact} · mutations {quote.Authority.Effects.MutationCount}"));
        diff.Add(NativeTheme.Metric("Applied effect families", string.Join(", ", quote.Authority.Effects.AppliedFamilies)));
        diff.Add(NativeTheme.Metric("Unsupported effect families", string.Join(", ", quote.Authority.Effects.UnsupportedFamilies)));
        foreach (CharacterCareerQualityPrerequisiteResult prerequisite in quote.Prerequisites)
        {
            diff.Add(NativeTheme.Metric(
                $"Prerequisite · {prerequisite.Prerequisite}",
                $"{(prerequisite.Satisfied ? "satisfied" : "blocked")} · {prerequisite.Authority}"));
        }
        diff.Add(NativeTheme.Metric("Transaction", draft.TransactionId.ToString("D")));
        diff.Add(NativeTheme.Metric("Workspace / saved revisions", $"{draft.ExpectedWorkspaceRevision} / {draft.ExpectedSavedRevision}"));
        diff.Add(NativeTheme.Metric("Logical revision", quote.LogicalRevision));
        diff.Add(NativeTheme.Metric("Source revision", quote.SourceRevision));
        diff.Add(NativeTheme.Metric("Rule digest", quote.RuleDigest));
        diff.Add(NativeTheme.Metric("Content digest", draft.RuntimeAuthority.ContentDigest));
        diff.Add(NativeTheme.Metric("Runtime digest", draft.RuntimeAuthority.RuntimeDigest));
        body.Add(NativeTheme.Card(diff));
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        body.Add(_blocker);
        _apply = NativeTheme.PrimaryButton("Confirm atomic quality transaction");
        _apply.AutomationId = "sr5-career-quality-apply";
        _apply.Clicked += async (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _attempted, 1, 0) == 0)
            {
                Refresh();
                await RunAsync(ApplyAsync);
            }
        };
        body.Add(_apply);
        body.Add(NativeTheme.Body(
            "The checkpoint moves to Applying first. Presentation commits the full quality/effect delta, expense and receipt atomically; Android never retries an unknown outcome.",
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        Refresh();
    }

    protected override void Refresh()
    {
        bool current = _draft.Matches(
                Coordinator.State.WorkspaceId,
                Coordinator.State.ContentRevision,
                Coordinator.State.SavedRevision)
            && _checkpoint.MatchesActionDraft(_draft)
            && _checkpointAuthority.OwnsReviewed(_checkpoint);
        bool attempted = Volatile.Read(ref _attempted) != 0;
        _apply.IsEnabled = current && !attempted;
        _blocker.Text = !current
            ? "Owner, revision, source, rule, effects, runtime, content or durable checkpoint changed."
            : attempted
                ? "The one-shot atomic commit is running or awaiting authoritative recovery."
                : string.Empty;
    }

    private async Task ApplyAsync()
    {
        if (!_store.TryBeginApply(
                Sr5CareerQualityCheckpointCas.From(_checkpoint),
                out Sr5CareerQualityCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync("Commit blocked", blocker, "OK");
            return;
        }
        _checkpoint = applying;
        Sr5CareerQualityApplyResult result = await _authority.ApplyAsync(_draft, applying, _store);
        if (result.Status == Sr5CareerQualityApplyStatus.OutcomeUnknown)
        {
            await DisplayAlertAsync(
                "Outcome unresolved",
                $"{result.Message} The Applying lock cannot be cleared or replayed.",
                "OK");
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5CareerQualityCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerQualityCheckpoint resolved,
                out blocker))
        {
            await DisplayAlertAsync("Outcome not checkpointed", blocker, "OK");
            return;
        }
        _checkpoint = resolved;
        if (result.Status == Sr5CareerQualityApplyStatus.RejectedBeforeMutation)
        {
            await DisplayAlertAsync(
                "Not applied",
                "Fresh atomic authority proves no quality/effect/expense/receipt transaction was saved.",
                "OK");
            return;
        }
        await Navigation.PushAsync(new Sr5CareerQualityReceiptPage(
            Coordinator,
            result.Receipt!,
            resolved,
            _authority,
            _store,
            _checkpointAuthority));
    }
}

public sealed class Sr5CareerQualityReceiptPage : NativePageBase
{
    private readonly CharacterCareerQualityReceipt _receipt;
    private readonly Sr5CareerQualityCheckpoint _checkpoint;
    private readonly Sr5CareerQualityCoordinator _authority;
    private readonly Sr5CareerQualityCheckpointStore _store;
    private readonly ISr5CareerQualityCheckpointAuthority _checkpointAuthority;
    private readonly Label _durability;

    internal Sr5CareerQualityReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCareerQualityReceipt receipt,
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityCoordinator authority,
        Sr5CareerQualityCheckpointStore store,
        ISr5CareerQualityCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _receipt = receipt;
        _checkpoint = checkpoint;
        _authority = authority;
        _store = store;
        _checkpointAuthority = checkpointAuthority;
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applied
            || !Sr5CareerQualityCoordinator.ReceiptMatchesDraft(checkpoint.Draft, receipt)
            || !_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            throw new InvalidOperationException("The quality receipt does not own this resolved checkpoint.");
        }

        Title = "Quality receipt";
        AutomationId = Sr5CareerWizardRoutes.QualityReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("SR5 Career · 3 of 3"));
        body.Add(NativeTheme.Title("Verified atomic quality receipt"));
        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric("Quality", receipt.Definition.Name));
        details.Add(NativeTheme.Metric("Operation", receipt.Operation.ToString()));
        details.Add(NativeTheme.Metric("Typed identity", $"{receipt.Identity.InternalId:D} · {receipt.Identity.SourceId:D}"));
        details.Add(NativeTheme.Metric("Instances", $"{receipt.InstancesBefore.Count} → {receipt.InstancesAfter.Count}"));
        details.Add(NativeTheme.Metric("Runner Karma", $"{receipt.CharacterKarmaBefore} → {receipt.CharacterKarmaAfter}"));
        details.Add(NativeTheme.Metric("Expense", $"{receipt.ExpenseAmount} · {receipt.ExpenseReason}"));
        details.Add(NativeTheme.Metric("Transaction", receipt.TransactionId.ToString("D")));
        details.Add(NativeTheme.Metric("Saved revision", receipt.WorkspaceRevisionAfter.ToString(CultureInfo.InvariantCulture)));
        body.Add(NativeTheme.Card(details));
        _durability = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        body.Add(_durability);
        body.Add(NativeTheme.Body(
            $"receipt {receipt.ReceiptDigest} · source {receipt.SourceRevisionBefore} → {receipt.SourceRevisionAfter} · rule {receipt.RuleDigestBefore} → {receipt.RuleDigestAfter} · state {receipt.StateDigestAfter}",
            NativeTheme.Muted));
        Button acknowledge = NativeTheme.PrimaryButton("Acknowledge receipt");
        acknowledge.AutomationId = "sr5-career-quality-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!_store.TryDeleteApplied(
                    Sr5CareerQualityCheckpointCas.From(_checkpoint),
                    _receipt,
                    out string blocker))
            {
                await DisplayAlertAsync("Receipt remains pending", blocker, "OK");
                return;
            }
            await Navigation.PopToRootAsync();
        });
        body.Add(acknowledge);
        Button correct = NativeTheme.SecondaryButton("Correct this quality transaction");
        correct.AutomationId = "sr5-career-quality-receipt-correct";
        correct.Clicked += async (_, _) => await RunAsync(CorrectAsync);
        body.Add(correct);
        body.Add(NativeTheme.Body(
            "Correction is a separate typed compensating transaction. It restores exact instances and Karma and removes only the original expense; the receipt is never edited in place.",
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        Refresh();
    }

    private async Task CorrectAsync()
    {
        string? reason = await DisplayPromptAsync(
            "Correct quality transaction",
            "Enter the reason recorded with the compensating transaction.",
            accept: "Review correction",
            cancel: "Keep transaction",
            initialValue: "User-requested correction",
            maxLength: CharacterCareerQualityRules.MaximumReasonLength);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            "Apply compensating correction?",
            "This atomically restores the exact pre-transaction quality instances and Karma and removes the bound expense.",
            "Correct",
            "Cancel");
        if (!confirmed)
        {
            return;
        }
        CharacterCareerQualityCorrectionPlan correction = await _authority.CorrectAsync(
            _checkpoint,
            _receipt,
            Guid.NewGuid(),
            reason);
        if (!_store.TryDeleteCorrected(
                Sr5CareerQualityCheckpointCas.From(_checkpoint),
                _receipt,
                correction,
                out string blocker))
        {
            await DisplayAlertAsync("Correction saved; checkpoint remains locked", blocker, "OK");
            return;
        }
        await DisplayAlertAsync(
            "Correction saved",
            $"Compensating transaction {correction.CorrectionId:D} restored the exact prior state.",
            "OK");
        await Navigation.PopToRootAsync();
    }

    protected override void Refresh()
    {
        bool exact = Coordinator.State.WorkspaceId == _checkpoint.Draft.WorkspaceId
            && Coordinator.State.ContentRevision == _receipt.WorkspaceRevisionAfter
            && Coordinator.State.SavedRevision == _receipt.SavedRevisionAfter
            && !Coordinator.State.IsDirty;
        _durability.Text = exact
            ? "The receipt was recovered from the exact clean atomic saved revision."
            : "The runner moved past this receipt; it remains bound to the earlier saved revision.";
        _durability.TextColor = exact ? NativeTheme.Muted : NativeTheme.Danger;
    }
}
