using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

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
        RunnerSessionSr5CareerQualityPresenter ownerAuthority = new(coordinator);
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
                Text("The SR5 quality route requires the exact clean saved runner revision."));
        }

        _selected = editor.Quotes.FirstOrDefault();
        Title = Text("Change quality");
        AutomationId = Sr5CareerWizardRoutes.QualityChoose;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 1 of 3")));
        body.Add(NativeTheme.Title(Text("Change a quality")));
        body.Add(NativeTheme.Body(
            Text("Choose a quality, then check its Karma cost before applying the change."),
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel(Text("Quality and operation")));
        _qualities = new Picker
        {
            AutomationId = "sr5-career-quality-picker",
            Title = Text("Quality and operation"),
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
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "sr5-career-quality-blocker";
        body.Add(_blocker);
        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-career-quality-recovery";
        body.Add(_recovery);

        if (editor.OmittedCandidateCount != 0 || editor.OmittedReceiptCount != 0)
        {
            Label omitted = NativeTheme.Body(
                Format(
                    "{0} candidate(s) and {1} receipt(s) were omitted. The whole quality lane remains fail-closed.",
                    editor.OmittedCandidateCount.ToString(CultureInfo.InvariantCulture),
                    editor.OmittedReceiptCount.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-quality-omitted-authority";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton(Text("Review quality"));
        _review.AutomationId = "sr5-career-quality-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);
        _resume = NativeTheme.SecondaryButton(Text("Resume reviewed quality change"));
        _resume.AutomationId = "sr5-career-quality-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton(Text("Resolve interrupted atomic commit"));
        _resolve.AutomationId = "sr5-career-quality-resolve-outcome";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveCheckpointAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed draft"));
        _abandon.AutomationId = "sr5-career-quality-abandon-reviewed";
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

    private static string QualityLabel(CharacterCareerQualityQuote quote)
        => Format(
            "{0} · {1} · level {2} → {3} · {4}",
            OperationLabel(quote.Operation),
            QualityCatalogStrings.Name(quote.Definition.SourceId, quote.Definition.Name),
            quote.LevelBefore,
            quote.LevelAfter,
            quote.CanApply ? KarmaChangeLabel(quote.CharacterKarmaDelta) : Sr5CareerQualityDraft.BlockerText(quote.Blocker));

    // A quality's rule value is not necessarily the amount booked in Career
    // (for example, acquiring a negative quality may grant no Karma).
    internal static string KarmaChangeLabel(int karmaDelta)
        => karmaDelta switch
        {
            < 0 => Format("Spend {0} Karma", -(long)karmaDelta),
            > 0 => Format("Gain {0} Karma", karmaDelta),
            _ => Text("No Karma change")
        };

    internal static string OperationLabel(CharacterCareerQualityOperation operation)
        => operation switch
        {
            CharacterCareerQualityOperation.AcquireLevel => Text("Acquire level"),
            CharacterCareerQualityOperation.RemoveLevel => Text("Remove level"),
            CharacterCareerQualityOperation.RemoveAllLevels => Text("Remove all levels"),
            _ => Text("Unsupported operation")
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
            && _checkpointAuthority.CurrentOwnerId != Guid.Empty
            && _checkpointAuthority.CurrentOwnerId == Sr5CareerQualityDraft.CheckpointOwnerId(_editor.OwnerId)
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
            ? Text("No exact quality operation is available.")
            : Format(
                "{0} · Karma {1} → {2}",
                KarmaChangeLabel(_selected.CharacterKarmaDelta),
                _selected.AvailableKarma,
                _selected.AvailableKarma + _selected.CharacterKarmaDelta);
        _blocker.Text = !revisionMatches
            ? Text("The saved runner revision changed. Reopen quality advancement.")
            : !completeAuthority
                ? Text("Ambiguous candidate or receipt authority was omitted. No quality operation may continue.")
                : _selected is null
                    ? Text("No exact quality projection is available.")
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
            await DisplayAlertAsync(Text("Cannot review"), Text("Choose an exact quality operation."), Text("OK"));
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
            await DisplayAlertAsync(Text("Review not checkpointed"), blocker, Text("OK"));
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
            await DisplayAlertAsync(
                Text("Draft cannot resume"),
                Text("The exact quality review no longer owns this runner."),
                Text("OK"));
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
            _recovery.Text = Text("This quality recovery lock belongs to another owner or runner revision.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        if (_checkpoint.Phase == Sr5CareerCheckpointPhase.Applied
            && await _authority.ResolveCorrectionAsync(_checkpoint) is { } corrected)
        {
            if (!_store.TryDeleteCorrected(Sr5CareerQualityCheckpointCas.From(_checkpoint),
                corrected.Receipt, corrected.Correction, out string correctionBlocker))
            {
                _recovery.Text = correctionBlocker;
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            await DisplayAlertAsync(Text("Correction saved"),
                Text("Your previous qualities and Karma have been restored."), Text("OK"));
            await Navigation.PopToRootAsync();
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
        _recovery.Text = Text("Fresh authority proves the atomic transaction was not saved. Resume the reviewed draft.");
        _recovery.TextColor = NativeTheme.Muted;
    }

    private async Task AbandonReviewedAsync()
    {
        if (_checkpoint is null || !_checkpointAuthority.OwnsReviewed(_checkpoint))
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            Text("Abandon reviewed quality change?"),
            Text("This removes only the durable review checkpoint. It never changes the runner."),
            Text("Abandon"),
            Text("Keep"));
        if (!confirmed)
        {
            return;
        }
        if (!_store.TryDeleteReviewed(Sr5CareerQualityCheckpointCas.From(_checkpoint), out string blocker))
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
                    ? Text("A saved quality review is not authorized for this exact runner.")
                    : blocker;
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            _checkpoint = checkpoint;
            _recoveryDraft = draft;
            _recovery.Text = Text("A durable reviewed quality operation can be resumed.");
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
            _recovery.Text = Text("A saved quality commit lock is not authorized for this owner and runner.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;
        _recovery.Text = checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            ? Text("An interrupted atomic commit is locked and will be resolved without replay.")
            : Text("A verified saved quality receipt is awaiting acknowledgement or correction.");
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
            throw new InvalidOperationException(Text("The quality preview does not own its exact Reviewed checkpoint."));
        }

        Title = Text("Review quality");
        AutomationId = Sr5CareerWizardRoutes.QualityReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 2 of 3")));
        body.Add(NativeTheme.Title(Text("Review quality")));
        CharacterCareerQualityQuote quote = draft.Review.Quote;
        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric(Text("Quality"), QualityCatalogStrings.Name(quote.Definition.SourceId, quote.Definition.Name)));
        diff.Add(NativeTheme.Metric(Text("Operation"), Sr5CareerQualityWizardPage.OperationLabel(quote.Operation)));
        diff.Add(NativeTheme.Metric(Text("Level"), $"{quote.LevelBefore} → {quote.LevelAfter}"));
        diff.Add(NativeTheme.Metric(Text("Runner Karma"), $"{quote.AvailableKarma} → {quote.AvailableKarma + quote.CharacterKarmaDelta}"));
        body.Add(NativeTheme.Card(diff));
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        body.Add(_blocker);
        _apply = NativeTheme.PrimaryButton(Text("Apply quality change"));
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
            ? Text("Owner, revision, source, rule, effects, runtime, content or durable checkpoint changed.")
            : attempted
                ? Text("Saving your quality change…")
                : string.Empty;
    }

    private async Task ApplyAsync()
    {
        if (!_store.TryBeginApply(
                Sr5CareerQualityCheckpointCas.From(_checkpoint),
                out Sr5CareerQualityCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Commit blocked"), blocker, Text("OK"));
            return;
        }
        _checkpoint = applying;
        Sr5CareerQualityApplyResult result = await _authority.ApplyAsync(_draft, applying, _store);
        if (result.Status == Sr5CareerQualityApplyStatus.OutcomeUnknown)
        {
            await DisplayAlertAsync(
                Text("Outcome unresolved"),
                Format("{0} The Applying lock cannot be cleared or replayed.", result.Message),
                Text("OK"));
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5CareerQualityCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerQualityCheckpoint resolved,
                out blocker))
        {
            await DisplayAlertAsync(Text("Outcome not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = resolved;
        if (result.Status == Sr5CareerQualityApplyStatus.RejectedBeforeMutation)
        {
            await DisplayAlertAsync(
                Text("Not applied"),
                Text("Fresh atomic authority proves no quality/effect/expense/receipt transaction was saved."),
                Text("OK"));
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
            throw new InvalidOperationException(Text("The quality receipt does not own this resolved checkpoint."));
        }

        Title = Text("Quality saved");
        AutomationId = Sr5CareerWizardRoutes.QualityReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 3 of 3")));
        body.Add(NativeTheme.Title(Text("Quality saved")));
        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric(Text("Quality"), QualityCatalogStrings.Name(receipt.Definition.SourceId, receipt.Definition.Name)));
        details.Add(NativeTheme.Metric(Text("Operation"), Sr5CareerQualityWizardPage.OperationLabel(receipt.Operation)));
        details.Add(NativeTheme.Metric(Text("Level"), $"{receipt.InstancesBefore.Count} → {receipt.InstancesAfter.Count}"));
        details.Add(NativeTheme.Metric(Text("Runner Karma"), $"{receipt.CharacterKarmaBefore} → {receipt.CharacterKarmaAfter}"));
        body.Add(NativeTheme.Card(details));
        _durability = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        body.Add(_durability);
        Button acknowledge = NativeTheme.PrimaryButton(Text("Back to Career"));
        acknowledge.AutomationId = "sr5-career-quality-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!_store.TryDeleteApplied(
                    Sr5CareerQualityCheckpointCas.From(_checkpoint),
                    _receipt,
                    out string blocker))
            {
                await DisplayAlertAsync(Text("Receipt remains pending"), blocker, Text("OK"));
                return;
            }
            await Navigation.PopToRootAsync();
        });
        body.Add(acknowledge);
        Button correct = NativeTheme.SecondaryButton(Text("Undo this quality change"));
        correct.AutomationId = "sr5-career-quality-receipt-correct";
        correct.Clicked += async (_, _) => await RunAsync(CorrectAsync);
        body.Add(correct);
        body.Add(NativeTheme.Body(
            Text("Undo restores your previous qualities and Karma."),
            NativeTheme.Muted));
        Content = new ScrollView { Content = body };
        Refresh();
    }

    private async Task CorrectAsync()
    {
        string? reason = await DisplayPromptAsync(
            Text("Correct quality transaction"),
            Text("Why do you want to undo this change?"),
            accept: Text("Review correction"),
            cancel: Text("Keep transaction"),
            initialValue: Text("User-requested correction"),
            maxLength: CharacterCareerQualityRules.MaximumReasonLength);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }
        bool confirmed = await DisplayAlertAsync(
            Text("Undo this quality change?"),
            Text("Your previous qualities and Karma will be restored."),
            Text("Correct"),
            Text("Cancel"));
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
            await DisplayAlertAsync(Text("Correction saved; checkpoint remains locked"), blocker, Text("OK"));
            return;
        }
        await DisplayAlertAsync(
            Text("Correction saved"),
            Text("Your previous qualities and Karma have been restored."),
            Text("OK"));
        await Navigation.PopToRootAsync();
    }

    protected override void Refresh()
    {
        bool exact = Coordinator.State.WorkspaceId == _checkpoint.Draft.WorkspaceId
            && Coordinator.State.ContentRevision == _receipt.WorkspaceRevisionAfter
            && Coordinator.State.SavedRevision == _receipt.SavedRevisionAfter
            && !Coordinator.State.IsDirty;
        _durability.Text = exact
            ? Text("Your change is saved.")
            : Text("This runner has changed since this quality was saved.");
        _durability.TextColor = exact ? NativeTheme.Muted : NativeTheme.Danger;
    }
}
