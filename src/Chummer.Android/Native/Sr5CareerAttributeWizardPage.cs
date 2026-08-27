using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

internal sealed record Sr5CareerAttributeWizardDependencies(
    Sr5CareerAttributeCheckpointStore Store,
    ISr5CareerAttributeCheckpointAuthority CheckpointAuthority);

/// <summary>
/// Phone-deep first step. The page only selects an exact Core quote and creates
/// a durable review checkpoint; no mutation is reachable from this surface.
/// </summary>
public sealed class Sr5CareerAttributeWizardPage : NativePageBase
{
    private readonly CareerAttributeAdvanceEditorState _editor;
    private readonly Sr5CareerAttributeCoordinator _authority;
    private readonly Sr5CareerAttributeCheckpointStore _store;
    private readonly ISr5CareerAttributeCheckpointAuthority _checkpointAuthority;
    private readonly Picker _attributes;
    private readonly Label _rating;
    private readonly Label _cost;
    private readonly Label _blocker;
    private readonly Label _recovery;
    private readonly Button _review;
    private readonly Button _resume;
    private readonly Button _resolve;
    private readonly Button _abandon;
    private CharacterCareerAttributeAdvanceQuote? _selected;
    private Sr5CareerAttributeDraft? _recoveryDraft;
    private Sr5CareerAttributeCheckpoint? _checkpoint;
    private int _automaticResolutionStarted;

    public Sr5CareerAttributeWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerAttributeAdvanceEditorState editor)
        : this(coordinator, editor, CreateDependencies(coordinator, editor))
    {
    }

    private Sr5CareerAttributeWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerAttributeAdvanceEditorState editor,
        Sr5CareerAttributeWizardDependencies dependencies)
        : this(
            coordinator,
            editor,
            new Sr5CareerAttributeCoordinator(
                new RunnerSessionSr5CareerAttributePresenter(coordinator),
                dependencies.CheckpointAuthority),
            dependencies.Store,
            dependencies.CheckpointAuthority)
    {
    }

    private static Sr5CareerAttributeWizardDependencies CreateDependencies(
        RunnerSessionCoordinator coordinator,
        CareerAttributeAdvanceEditorState editor)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(editor);
        PreferencesSr5CareerCheckpointOwnerAuthority ownerAuthority = new();
        Sr5CareerAttributeLiveCheckpointAuthority checkpointAuthority = new(
            ownerAuthority,
            editor,
            () => new RunnerSessionSr5CareerAttributePresenter(coordinator).Binding);
        return new(
            Sr5CareerAttributeCheckpointStore.CreateDefault(checkpointAuthority),
            checkpointAuthority);
    }

    internal Sr5CareerAttributeWizardPage(
        RunnerSessionCoordinator coordinator,
        CareerAttributeAdvanceEditorState editor,
        Sr5CareerAttributeCoordinator authority,
        Sr5CareerAttributeCheckpointStore store,
        ISr5CareerAttributeCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        Sr5CareerRunnerGuard.RequireCreated(
            new RunnerSessionSr5CareerAttributePresenter(coordinator).Binding);
        if (coordinator.State.WorkspaceId != editor.WorkspaceId
            || coordinator.State.ContentRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                Text("The SR5 Attribute route requires the current exact runner revision."));
        }

        _selected = editor.Attributes.FirstOrDefault();
        Title = Text("Advance attribute");
        AutomationId = Sr5CareerWizardRoutes.AttributeChoose;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 1 of 3")));
        body.Add(NativeTheme.Title(Text("Choose an attribute")));
        body.Add(NativeTheme.Body(
            Text("Only exact attributes projected from this saved SR5 revision are shown. Core owns identity, natural maximum, special-attribute eligibility, Burned Edge repair, Karma cost and expense semantics."),
            NativeTheme.Muted));

        body.Add(NativeTheme.FieldLabel(Text("Attribute")));
        _attributes = new Picker
        {
            AutomationId = "sr5-career-attribute-picker",
            Title = Text("Saved attribute"),
            ItemsSource = editor.Attributes.Select(AttributeLabel).ToArray(),
            SelectedIndex = editor.Attributes.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _attributes.SelectedIndexChanged += (_, _) => SelectAttribute();
        body.Add(_attributes);

        _rating = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _rating.AutomationId = "sr5-career-attribute-rating";
        body.Add(_rating);
        _cost = NativeTheme.Body(string.Empty, NativeTheme.Text);
        _cost.AutomationId = "sr5-career-attribute-cost";
        body.Add(_cost);
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        _blocker.AutomationId = "sr5-career-attribute-blocker";
        body.Add(_blocker);
        _recovery = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _recovery.AutomationId = "sr5-career-attribute-recovery";
        body.Add(_recovery);

        if (editor.OmittedAttributeCount > 0 || editor.OmittedReceiptCount > 0)
        {
            Label omitted = NativeTheme.Body(
                Format(
                    "{0} attribute quote(s) and {1} receipt(s) were omitted because exact authority could not be reproduced.",
                    editor.OmittedAttributeCount.ToString(CultureInfo.InvariantCulture),
                    editor.OmittedReceiptCount.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Danger);
            omitted.AutomationId = "sr5-career-attribute-omitted";
            body.Add(NativeTheme.Card(omitted));
        }

        _review = NativeTheme.PrimaryButton(Text("Review exact advancement"));
        _review.AutomationId = "sr5-career-attribute-review";
        _review.Clicked += async (_, _) => await RunAsync(OpenReviewAsync);
        body.Add(_review);
        _resume = NativeTheme.SecondaryButton(Text("Resume reviewed advancement"));
        _resume.AutomationId = "sr5-career-attribute-resume";
        _resume.Clicked += async (_, _) => await RunAsync(ResumeReviewAsync);
        body.Add(_resume);
        _resolve = NativeTheme.PrimaryButton(Text("Resolve interrupted apply"));
        _resolve.AutomationId = "sr5-career-attribute-resolve-outcome";
        _resolve.Clicked += async (_, _) => await RunAsync(ResolveCheckpointAsync);
        body.Add(_resolve);
        _abandon = NativeTheme.SecondaryButton(Text("Abandon reviewed draft"));
        _abandon.AutomationId = "sr5-career-attribute-abandon-reviewed";
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
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                Text("Attribute recovery unavailable"),
                exception.Message,
                Text("OK"));
        }
    }

    protected override void Refresh() => RefreshEnabledState();

    private static string AttributeLabel(CharacterCareerAttributeAdvanceQuote attribute)
        => attribute.KarmaCost >= 0
            ? Format(
                "{0} · {1} → {2} · {3} Karma",
                attribute.DisplayName,
                attribute.EffectiveValue.ToString(CultureInfo.InvariantCulture),
                attribute.TargetValue.ToString(CultureInfo.InvariantCulture),
                attribute.KarmaCost.ToString(CultureInfo.InvariantCulture))
            : Format(
                "{0} · {1} → {2} · blocked",
                attribute.DisplayName,
                attribute.EffectiveValue.ToString(CultureInfo.InvariantCulture),
                attribute.TargetValue.ToString(CultureInfo.InvariantCulture));

    private void SelectAttribute()
    {
        _selected = _attributes.SelectedIndex >= 0
            && _attributes.SelectedIndex < _editor.Attributes.Count
            ? _editor.Attributes[_attributes.SelectedIndex]
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
        _attributes.IsEnabled = revisionMatches && _checkpoint is null && _editor.Attributes.Count > 0;
        _rating.Text = _selected is null
            ? Text("No exact attribute quote is available.")
            : Format(
                _selected.RepairsBurnedEdge
                    ? "Current {0} · after {1} · natural maximum {2} · repairs one Burned Edge point"
                    : "Current {0} · after {1} · natural maximum {2}",
                _selected.EffectiveValue.ToString(CultureInfo.InvariantCulture),
                _selected.TargetValue.ToString(CultureInfo.InvariantCulture),
                _selected.NaturalMaximum.ToString(CultureInfo.InvariantCulture));
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
                ? Text("This runner changed. Reopen attribute advancement.")
                : _selected is null
                    ? Text("No exact attribute projection is available.")
                    : Sr5CareerAttributeDraft.BlockerText(_selected.Blocker);
        _review.IsEnabled = revisionMatches
            && _checkpoint is null
            && _selected is { CanAdvance: true }
            && CharacterCareerAttributeAdvanceRules.IsCoherent(_selected);
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
            new RunnerSessionSr5CareerAttributePresenter(Coordinator).Binding);
        if (!Sr5CareerAttributeDraft.TryCreate(
                _editor,
                _selected,
                _checkpointAuthority.CurrentOwnerId,
                Guid.NewGuid(),
                DateTime.Now,
                out Sr5CareerAttributeDraft draft,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Cannot review"), blocker, Text("OK"));
            return;
        }
        if (!draft.Matches(Coordinator.State.WorkspaceId, Coordinator.State.ContentRevision))
        {
            await DisplayAlertAsync(
                Text("Runner changed"),
                Text("Reopen attribute advancement."),
                Text("OK"));
            return;
        }

        Sr5CareerAttributeCheckpoint candidate = Sr5CareerAttributeCheckpoint.FromDraft(draft);
        if (!_store.TryCreate(
                candidate,
                out Sr5CareerAttributeCheckpoint stored,
                out blocker))
        {
            await DisplayAlertAsync(Text("Review not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = stored;
        _recoveryDraft = draft;
        await Navigation.PushAsync(new Sr5CareerAttributeReviewPage(
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
        await Navigation.PushAsync(new Sr5CareerAttributeReviewPage(
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

        Sr5CareerAttributeRecoveryResolution resolution =
            await _authority.ResolveAsync(_checkpoint);
        if (resolution.Status == Sr5CareerAttributeRecoveryStatus.OutcomeUnknown)
        {
            _recovery.Text = resolution.Message;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }

        Sr5CareerAttributeCheckpoint stored = _checkpoint;
        if (_checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            && !_store.TryRecordAuthoritativeResolution(
                Sr5CareerAttributeCheckpointCas.From(_checkpoint),
                resolution,
                out stored,
                out string blocker))
        {
            _recovery.Text = blocker;
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = stored;
        if (resolution.Status == Sr5CareerAttributeRecoveryStatus.AppliedVerified
            && resolution.Receipt is { } receipt)
        {
            _recovery.Text = Text("The interrupted attribute apply was verified from the saved receipt ledger.");
            _recovery.TextColor = NativeTheme.Muted;
            await Navigation.PushAsync(new Sr5CareerAttributeReceiptPage(
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
            Text("Abandon reviewed attribute?"),
            Text("This removes only the durable review checkpoint and does not change the runner."),
            Text("Abandon"),
            Text("Keep"));
        if (!confirmed)
        {
            return;
        }
        if (!_store.TryDeleteReviewed(
                Sr5CareerAttributeCheckpointCas.From(_checkpoint),
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
        if (!_store.TryRead(out Sr5CareerAttributeCheckpoint checkpoint, out string blocker))
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
                    out Sr5CareerAttributeDraft draft,
                    out _)
                || !_checkpointAuthority.OwnsReviewed(checkpoint))
            {
                _recovery.Text = Text("A saved attribute review is not authorized for this owner and runner revision.");
                _recovery.TextColor = NativeTheme.Danger;
                return;
            }
            _checkpoint = checkpoint;
            _recoveryDraft = draft;
            _recovery.Text = Text("A durable reviewed attribute advancement can be resumed.");
            _recovery.TextColor = NativeTheme.Muted;
            int index = _editor.Attributes
                .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                .First(pair => pair.candidate.Identity == draft.Quote.Identity)
                .candidateIndex;
            _attributes.SelectedIndex = index;
            return;
        }
        if (!_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            _recovery.Text = Text("A saved attribute apply lock is not authorized for this owner and SR5 runner.");
            _recovery.TextColor = NativeTheme.Danger;
            return;
        }
        _checkpoint = checkpoint;
        _recovery.Text = checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            ? Text("An interrupted apply is locked and will be resolved without replay.")
            : Text("A verified saved attribute receipt is awaiting acknowledgement.");
        _recovery.TextColor = NativeTheme.Muted;
    }
}

/// <summary>
/// Step two is a pure preview. Apply is exposed only after the durable CAS
/// journal has moved this exact owner/action from Reviewed to Applying.
/// </summary>
public sealed class Sr5CareerAttributeReviewPage : NativePageBase
{
    private readonly Sr5CareerAttributeDraft _draft;
    private Sr5CareerAttributeCheckpoint _checkpoint;
    private readonly Sr5CareerAttributeCoordinator _authority;
    private readonly Sr5CareerAttributeCheckpointStore _store;
    private readonly ISr5CareerAttributeCheckpointAuthority _checkpointAuthority;
    private readonly Button _apply;
    private readonly Label _blocker;
    private int _attempted;

    internal Sr5CareerAttributeReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerAttributeDraft draft,
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeCoordinator authority,
        Sr5CareerAttributeCheckpointStore store,
        ISr5CareerAttributeCheckpointAuthority checkpointAuthority) : base(coordinator)
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
                Text("The attribute preview does not own its durable review checkpoint."));
        }

        Title = Text("Review attribute");
        AutomationId = Sr5CareerWizardRoutes.AttributeReview;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 2 of 3")));
        body.Add(NativeTheme.Title(Text("Review exact diff")));
        VerticalStackLayout diff = new() { Spacing = 8 };
        diff.Add(NativeTheme.Metric(Text("Attribute"), _draft.Quote.DisplayName));
        diff.Add(NativeTheme.Metric(
            Text("Value"),
            $"{_draft.Quote.EffectiveValue} → {_draft.Quote.TargetValue}"));
        diff.Add(NativeTheme.Metric(
            Text("Attribute Karma points"),
            $"{_draft.Quote.KarmaPoints} → {_draft.Plan.SavedAttributeKarmaPoints}"));
        diff.Add(NativeTheme.Metric(
            Text("Runner Karma"),
            $"{_draft.Quote.AvailableKarma} → {_draft.Plan.SavedCharacterKarma}"));
        diff.Add(NativeTheme.Metric(
            Text("Burned Edge"),
            $"{_draft.Quote.BurnedEdgePoints} → {_draft.Plan.SavedBurnedEdgePoints}"));
        diff.Add(NativeTheme.Metric(Text("Natural maximum"), _draft.Quote.NaturalMaximum.ToString(CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(Text("Expense"), _draft.Plan.ExpenseReason));
        diff.Add(NativeTheme.Metric(Text("Expense identity"), _draft.Plan.ExpenseId.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Date"), _draft.Plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture)));
        diff.Add(NativeTheme.Metric(Text("Undo"), $"{_draft.Plan.KarmaUndoType} · {_draft.Plan.UndoObjectId}"));
        body.Add(NativeTheme.Card(diff));
        _blocker = NativeTheme.Body(string.Empty, NativeTheme.Danger);
        body.Add(_blocker);
        _apply = NativeTheme.PrimaryButton(Text("Apply and verify once"));
        _apply.AutomationId = "sr5-career-attribute-apply";
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
                Sr5CareerAttributeCheckpointCas.From(_checkpoint),
                out Sr5CareerAttributeCheckpoint applying,
                out string blocker))
        {
            await DisplayAlertAsync(
                Text("Apply blocked"),
                blocker,
                Text("OK"));
            return;
        }
        _checkpoint = applying;

        Sr5CareerAttributeApplyResult result = await _authority.ApplyAsync(
            _draft,
            applying,
            _store);
        if (result.Status == Sr5CareerAttributeApplyStatus.OutcomeUnknown)
        {
            await DisplayAlertAsync(
                Text("Outcome unresolved"),
                Format("{0} The Applying checkpoint cannot be cleared or replayed.", result.Message),
                Text("OK"));
            return;
        }
        if (!_store.TryRecordAuthoritativeResolution(
                Sr5CareerAttributeCheckpointCas.From(applying),
                result.Resolution,
                out Sr5CareerAttributeCheckpoint resolved,
                out blocker))
        {
            await DisplayAlertAsync(Text("Outcome not checkpointed"), blocker, Text("OK"));
            return;
        }
        _checkpoint = resolved;
        if (result.Status == Sr5CareerAttributeApplyStatus.RejectedBeforeMutation)
        {
            await DisplayAlertAsync(
                Text("Not applied"),
                Text("Fresh typed projections prove no attribute or receipt mutation was saved. Return and resume the review before retrying."),
                Text("OK"));
            return;
        }

        await Navigation.PushAsync(new Sr5CareerAttributeReceiptPage(
            Coordinator,
            result.Receipt!,
            resolved,
            _store,
            _checkpointAuthority));
    }
}

public sealed class Sr5CareerAttributeReceiptPage : NativePageBase
{
    private readonly CharacterCareerAttributeAdvanceReceipt _receipt;
    private readonly Sr5CareerAttributeCheckpoint _checkpoint;
    private readonly Sr5CareerAttributeCheckpointStore _store;
    private readonly ISr5CareerAttributeCheckpointAuthority _checkpointAuthority;
    private readonly Label _durability;

    internal Sr5CareerAttributeReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCareerAttributeAdvanceReceipt receipt,
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeCheckpointStore store,
        ISr5CareerAttributeCheckpointAuthority checkpointAuthority) : base(coordinator)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _checkpointAuthority = checkpointAuthority
            ?? throw new ArgumentNullException(nameof(checkpointAuthority));
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applied
            || !Sr5CareerAttributeCoordinator.ReceiptMatchesDraft(checkpoint.Draft, receipt)
            || !_checkpointAuthority.OwnsCurrentRunner(checkpoint))
        {
            throw new InvalidOperationException(
                Text("The typed attribute receipt does not own the resolved checkpoint."));
        }

        Title = Text("Attribute receipt");
        AutomationId = Sr5CareerWizardRoutes.AttributeReceipt;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(Text("SR5 Career · 3 of 3")));
        body.Add(NativeTheme.Title(Text("Verified saved advancement")));
        VerticalStackLayout details = new() { Spacing = 8 };
        details.Add(NativeTheme.Metric(Text("Attribute"), receipt.Identity.Abbreviation));
        details.Add(NativeTheme.Metric(Text("Attribute Karma"), $"{receipt.AttributeKarmaBefore} → {receipt.AttributeKarmaAfter}"));
        details.Add(NativeTheme.Metric(Text("Runner Karma"), $"{receipt.CharacterKarmaBefore} → {receipt.CharacterKarmaAfter}"));
        details.Add(NativeTheme.Metric(Text("Burned Edge"), $"{receipt.BurnedEdgePointsBefore} → {receipt.BurnedEdgePointsAfter}"));
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
        acknowledge.AutomationId = "sr5-career-attribute-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!_store.TryDeleteApplied(
                    Sr5CareerAttributeCheckpointCas.From(_checkpoint),
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
