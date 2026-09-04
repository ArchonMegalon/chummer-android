using System.Globalization;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

public sealed class Sr5PlaytimeDamageWizardPage : NativePageBase
{
    private readonly WorkspaceConditionMonitorTrack _track;
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly Sr5PlaytimeDamageJournalStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private Sr5PlaytimeDamageSnapshot? _snapshot;
    private Sr5PlaytimeDamageJournal? _journal;
    private string? _notice;
    private bool _loading;

    public Sr5PlaytimeDamageWizardPage(
        RunnerSessionCoordinator coordinator,
        WorkspaceConditionMonitorTrack track,
        CharacterWorkspaceId workspaceId)
        : this(
            coordinator,
            track,
            workspaceId,
            Sr5PlaytimeDamageJournalStore.CreateDefault(track, workspaceId))
    {
    }

    internal Sr5PlaytimeDamageWizardPage(
        RunnerSessionCoordinator coordinator,
        WorkspaceConditionMonitorTrack track,
        CharacterWorkspaceId workspaceId,
        Sr5PlaytimeDamageJournalStore store) : base(coordinator)
    {
        if (!Sr5PlaytimeDamageIntegrity.IsSupportedTrack(track))
            throw new ArgumentOutOfRangeException(nameof(track));
        if (string.IsNullOrWhiteSpace(workspaceId.Value))
            throw new ArgumentException("A loaded runner workspace is required.", nameof(workspaceId));
        _track = track;
        _workspaceId = workspaceId;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        string token = Token(track);
        Title = Format("Playtime · {0} damage", TrackLabel(track));
        AutomationId = $"sr5-career/playtime/damage/{token}";
        Content = new ScrollView { Content = _body };
    }

    protected override Task PrepareForAppearanceRefreshAsync(
        CancellationToken cancellationToken)
    {
        LoadLatest(cancellationToken);
        return Task.CompletedTask;
    }

    protected override void Refresh()
    {
        _body.Clear();
        string token = Token(_track);
        _body.Add(NativeTheme.Eyebrow(Text("SR5 Playtime · typed damage")));
        _body.Add(NativeTheme.Title(Format("{0} condition track", TrackLabel(_track))));
        _body.Add(NativeTheme.Body(
            Text("Choose one exact saved box count. Review and confirmation are bound to this runner revision and recover through one durable receipt."),
            NativeTheme.Muted));

        if (_loading)
        {
            AddStatus(Text("Loading the exact saved damage track…"),
                $"sr5-playtime-damage-{token}-loading", NativeTheme.Muted);
            return;
        }
        if (_snapshot is null)
        {
            AddStatus(
                _notice ?? Text("The exact clean saved SR5 damage track is unavailable. No mutation fallback was opened."),
                $"sr5-playtime-damage-{token}-unavailable",
                NativeTheme.Danger);
            return;
        }

        _body.Add(NativeTheme.Card(NativeTheme.Body(
            Format(
                "Workspace {0} · revision {1} · {2} / {3} boxes",
                _snapshot.WorkspaceId.Value,
                _snapshot.WorkspaceRevision,
                _snapshot.Filled,
                _snapshot.EditableMaximum),
            NativeTheme.Muted)));
        if (!string.IsNullOrWhiteSpace(_notice))
            AddStatus(_notice, $"sr5-playtime-damage-{token}-notice", NativeTheme.Muted);

        if (_journal is { Phase: Sr5PlaytimeDamageTransactionPhase.Applied, Receipt: { } receipt })
        {
            AddReceipt(receipt);
            return;
        }
        if (_journal is { Phase: Sr5PlaytimeDamageTransactionPhase.Applying })
        {
            AddStatus(
                Text("The previous confirmation is Applying but its exact result could not be classified. It remains mutation-owning and cannot be retried."),
                $"sr5-playtime-damage-{token}-applying-conflict",
                NativeTheme.Danger);
            return;
        }
        if (_journal is { Phase: Sr5PlaytimeDamageTransactionPhase.Reviewed } review)
        {
            _body.Add(NativeTheme.NavigationRow(
                Text("Resume exact damage review"),
                Detail(review.Quote),
                () => Navigation.PushAsync(new Sr5PlaytimeDamageReviewPage(Coordinator, _store, review)),
                automationId: $"sr5-playtime-damage-{token}-resume"));
        }

        _body.Add(NativeTheme.FieldLabel(Text("Filled boxes after this action")));
        Picker filled = NumberPicker(_snapshot);
        _body.Add(filled);
        Button reviewButton = NativeTheme.PrimaryButton(Text("Review exact damage change"));
        reviewButton.AutomationId = $"sr5-playtime-damage-{token}-open-review";
        reviewButton.Clicked += async (_, _) => await RunAsync(() => OpenReviewAsync(filled));
        _body.Add(reviewButton);
        Label boundary = NativeTheme.Body(
            Text("This Playtime leaf changes only the selected runner Physical or Stun box count. It does not calculate healing time, temporary modifiers, initiative, or vehicle damage."),
            NativeTheme.Muted);
        boundary.AutomationId = $"sr5-playtime-damage-{token}-boundary";
        _body.Add(NativeTheme.Card(boundary));
    }

    private void LoadLatest(CancellationToken cancellationToken)
    {
        _loading = true;
        _notice = null;
        Refresh();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshot = ProjectCurrent();
            if (_snapshot is null)
            {
                _journal = null;
                return;
            }
            if (!_store.TryRead(out Sr5PlaytimeDamageJournal? journal, out string blocker))
            {
                _journal = null;
                if (!string.IsNullOrWhiteSpace(blocker))
                {
                    _snapshot = null;
                    _notice = blocker;
                }
                return;
            }
            if (journal!.Quote.Original.Track != _track
                || journal.Quote.Original.WorkspaceId != _snapshot.WorkspaceId)
            {
                _snapshot = null;
                _journal = journal;
                _notice = Text("A damage transaction for another runner or track occupies this journal.");
                return;
            }
            if (journal.Phase == Sr5PlaytimeDamageTransactionPhase.Applying)
            {
                Sr5PlaytimeDamageRecoveryObservation observation =
                    Sr5PlaytimeDamageIntegrity.Observe(journal, _snapshot, out _);
                if (observation == Sr5PlaytimeDamageRecoveryObservation.Original
                    && _store.TryReturnToReview(journal, out Sr5PlaytimeDamageJournal review, out blocker))
                {
                    journal = review;
                    _notice = Text("Core proved no mutation after restart. Confirm the exact review again.");
                }
                else if (observation == Sr5PlaytimeDamageRecoveryObservation.Applied
                         && _store.TryComplete(journal, _snapshot, out Sr5PlaytimeDamageJournal applied, out blocker))
                {
                    journal = applied;
                    _notice = Text("The exact next-revision damage receipt was recovered after restart.");
                }
                else if (!string.IsNullOrWhiteSpace(blocker))
                {
                    _notice = blocker;
                }
            }
            else if (journal.Phase == Sr5PlaytimeDamageTransactionPhase.Reviewed
                     && !journal.Quote.MatchesOriginal(_snapshot))
            {
                if (_store.TryDiscardReview(journal, out blocker))
                {
                    journal = null;
                    _notice = Text("The saved damage review was stale and was discarded before mutation.");
                }
                else
                {
                    _snapshot = null;
                    _notice = blocker;
                }
            }
            _journal = journal;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _snapshot = null;
            _notice = exception.Message;
        }
        finally
        {
            _loading = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                Refresh();
            }
        }
    }

    private async Task OpenReviewAsync(Picker picker)
    {
        Sr5PlaytimeDamageSnapshot? current = ProjectCurrent();
        if (_snapshot is null || current != _snapshot)
        {
            await DisplayAlertAsync(
                Text("Runner changed"),
                Text("Reopen Playtime and quote the current saved damage track."),
                Text("OK"));
            return;
        }
        int filledAfter = SelectedNumber(picker, current.Filled);
        if (!Sr5PlaytimeDamageIntegrity.TryQuote(
                current,
                filledAfter,
                Guid.NewGuid(),
                out Sr5PlaytimeDamageQuote quote))
        {
            await DisplayAlertAsync(
                Text("No exact change"),
                Text("Choose a different valid box count before review."),
                Text("OK"));
            return;
        }
        if (!_store.TryWriteReview(quote, Guid.NewGuid(), out Sr5PlaytimeDamageJournal review, out string blocker))
        {
            await DisplayAlertAsync(Text("Review unavailable"), blocker, Text("OK"));
            return;
        }
        _journal = review;
        await Navigation.PushAsync(new Sr5PlaytimeDamageReviewPage(Coordinator, _store, review));
    }

    private void AddReceipt(Sr5PlaytimeDamageReceipt receipt)
    {
        string token = Token(receipt.Track);
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow(Text("Verified Playtime damage receipt")));
        card.Add(NativeTheme.Body(Format(
            "{0}: {1} → {2} boxes · revision {3} → {4}",
            receipt.Label,
            receipt.FilledBefore,
            receipt.FilledAfter,
            receipt.ExpectedWorkspaceRevision,
            receipt.AppliedWorkspaceRevision)));
        Label digest = NativeTheme.Body(
            Format("Receipt {0}", Sr5TableWizardPage.ShortDigest(receipt.ReceiptDigest)),
            NativeTheme.Muted);
        digest.AutomationId = $"sr5-playtime-damage-{token}-receipt-digest";
        card.Add(digest);
        Button acknowledge = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        acknowledge.AutomationId = $"sr5-playtime-damage-{token}-receipt-acknowledge";
        acknowledge.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (_journal is not { Phase: Sr5PlaytimeDamageTransactionPhase.Applied } applied)
            {
                throw new InvalidOperationException(
                    Text("Only the exact verified Playtime damage receipt may be acknowledged."));
            }
            if (!_store.TryClearApplied(applied, out string blocker))
            {
                throw new InvalidOperationException(blocker);
            }
            _journal = null;
            _notice = null;
            await Task.CompletedTask;
        });
        card.Add(acknowledge);
        View border = NativeTheme.Card(card);
        border.AutomationId = $"sr5-playtime-damage-{token}-receipt";
        _body.Add(border);
    }

    private Sr5PlaytimeDamageSnapshot? ProjectCurrent()
        => Sr5PlaytimeDamageIntegrity.TryProject(
            Coordinator.State.Profile?.Created == true,
            Coordinator.State.Rules?.GameEdition,
            Coordinator.State.WorkspaceId,
            Coordinator.State.ContentRevision,
            Coordinator.State.SavedRevision,
            Coordinator.State.IsDirty,
            Coordinator.State.Error,
            Coordinator.State.ActiveConditionMonitor,
            _track,
            out Sr5PlaytimeDamageSnapshot snapshot)
            && snapshot.WorkspaceId == _workspaceId
            ? snapshot
            : null;

    private void AddStatus(string text, string automationId, Color color)
    {
        Label label = NativeTheme.Body(text, color);
        label.AutomationId = automationId;
        _body.Add(NativeTheme.Card(label));
    }

    private static Picker NumberPicker(Sr5PlaytimeDamageSnapshot snapshot)
    {
        string[] values = Enumerable.Range(0, snapshot.EditableMaximum + 1)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return new Picker
        {
            AutomationId = $"sr5-playtime-damage-{Token(snapshot.Track)}-filled",
            Title = Text("Filled boxes"),
            ItemsSource = values,
            SelectedIndex = snapshot.Filled
        };
    }

    private static int SelectedNumber(Picker picker, int fallback)
        => picker.SelectedItem is string selected
           && int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    internal static string Token(WorkspaceConditionMonitorTrack track)
        => track switch
        {
            WorkspaceConditionMonitorTrack.Physical => "physical",
            WorkspaceConditionMonitorTrack.Stun => "stun",
            _ => throw new ArgumentOutOfRangeException(nameof(track))
        };

    internal static string TrackLabel(WorkspaceConditionMonitorTrack track)
        => track switch
        {
            WorkspaceConditionMonitorTrack.Physical => Text("Physical"),
            WorkspaceConditionMonitorTrack.Stun => Text("Stun"),
            _ => throw new ArgumentOutOfRangeException(nameof(track))
        };

    internal static string Detail(Sr5PlaytimeDamageQuote quote)
        => Format(
            "{0} boxes {1} → {2} · maximum {3}",
            quote.Original.Label,
            quote.Original.Filled,
            quote.FilledAfter,
            quote.Original.EditableMaximum);
}

public sealed class Sr5PlaytimeDamageReviewPage : NativePageBase
{
    private readonly Sr5PlaytimeDamageJournalStore _store;
    private Sr5PlaytimeDamageJournal _journal;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly Button _confirm;

    public Sr5PlaytimeDamageReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5PlaytimeDamageJournalStore store,
        Sr5PlaytimeDamageJournal journal) : base(coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        if (!_journal.IsExact() || _journal.Phase != Sr5PlaytimeDamageTransactionPhase.Reviewed)
            throw new ArgumentException("Review requires an exact Playtime damage journal.", nameof(journal));
        string token = Sr5PlaytimeDamageWizardPage.Token(journal.Quote.Original.Track);
        Title = Text("Review Playtime damage");
        AutomationId = $"sr5-career/playtime/damage/{token}/review";
        _confirm = NativeTheme.PrimaryButton(Text("Confirm and save exact damage"));
        _confirm.AutomationId = $"sr5-playtime-damage-{token}-confirm";
        _confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        Sr5PlaytimeDamageQuote quote = _journal.Quote;
        bool current = ProjectCurrent() == quote.Original;
        _body.Add(NativeTheme.Eyebrow(Text("Digest-bound review")));
        _body.Add(NativeTheme.Title(Format(
            "{0} condition track",
            quote.Original.Label)));
        _body.Add(NativeTheme.Card(NativeTheme.Body(
            Sr5PlaytimeDamageWizardPage.Detail(quote))));
        Label identity = NativeTheme.Body(
            Format(
                "Action {0} · revision {1} · quote {2}",
                quote.ActionId,
                quote.Original.WorkspaceRevision,
                Sr5TableWizardPage.ShortDigest(quote.QuoteDigest)),
            NativeTheme.Muted);
        identity.AutomationId = "sr5-playtime-damage-review-identity";
        _body.Add(NativeTheme.Card(identity));
        if (!current)
        {
            Label stale = NativeTheme.Body(
                Text("The saved runner changed after review. This damage action cannot be applied."),
                NativeTheme.Danger);
            stale.AutomationId = "sr5-playtime-damage-review-stale";
            _body.Add(NativeTheme.Card(stale));
        }
        _confirm.IsEnabled = current;
        _body.Add(_confirm);
        _body.Add(NativeTheme.Body(
            Text("Applying is durable before the mutation. Only the exact next revision and selected box count can release its mutation owner."),
            NativeTheme.Muted));
    }

    private async Task ConfirmAsync()
    {
        Sr5PlaytimeDamageSnapshot? before = ProjectCurrent();
        if (before is null || !_journal.Quote.MatchesOriginal(before))
        {
            await DisplayAlertAsync(
                Text("Runner changed"),
                Text("Reopen the Playtime damage wizard and review the current track."),
                Text("OK"));
            return;
        }
        if (!_store.TryBeginApplying(
                _journal,
                out Sr5PlaytimeDamageJournal applying,
                out string blocker))
        {
            await DisplayAlertAsync(Text("Confirmation unavailable"), blocker, Text("OK"));
            return;
        }
        _journal = applying;

        try
        {
            using (await _store.AcquireApplyingLeaseAsync(applying, CancellationToken.None))
            {
                Sr5PlaytimeDamageSnapshot? leased = ProjectCurrent();
                if (leased is null || !applying.Quote.MatchesOriginal(leased))
                    throw new InvalidOperationException(
                        "The exact reviewed damage snapshot was lost before the mutation lease.");
                await Coordinator.ApplyConditionMonitorEditAsync(
                    new ConditionMonitorEditRequest(
                        applying.Quote.Original.Track,
                        applying.Quote.FilledAfter),
                    CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await DisplayAlertAsync(
                Text("Damage outcome unknown"),
                Format("{0} The durable Applying journal remains locked for recovery.", exception.Message),
                Text("OK"));
            return;
        }

        Sr5PlaytimeDamageSnapshot? observed = ProjectCurrent();
        if (observed is null)
        {
            await DisplayAlertAsync(
                Text("Damage outcome unknown"),
                Text("The saved runner could not be projected after apply. The Applying journal remains locked."),
                Text("OK"));
            return;
        }
        Sr5PlaytimeDamageRecoveryObservation observation =
            Sr5PlaytimeDamageIntegrity.Observe(applying, observed, out _);
        if (observation == Sr5PlaytimeDamageRecoveryObservation.Original
            && _store.TryReturnToReview(applying, out Sr5PlaytimeDamageJournal review, out blocker))
        {
            _journal = review;
            await DisplayAlertAsync(
                Text("No mutation observed"),
                Text("Core proved the runner stayed at the reviewed revision. Confirm again if desired."),
                Text("OK"));
            return;
        }
        if (observation != Sr5PlaytimeDamageRecoveryObservation.Applied
            || !_store.TryComplete(applying, observed, out Sr5PlaytimeDamageJournal applied, out blocker))
        {
            await DisplayAlertAsync(
                Text("Damage outcome unknown"),
                string.IsNullOrWhiteSpace(blocker)
                    ? Text("The exact next-revision postcondition was not observed. The Applying journal remains locked.")
                    : blocker,
                Text("OK"));
            return;
        }
        _journal = applied;
        await DisplayAlertAsync(
            Text("Damage saved"),
            Format(
                "Verified runner revision {0}. Receipt {1}.",
                applied.Receipt!.AppliedWorkspaceRevision,
                Sr5TableWizardPage.ShortDigest(applied.Receipt.ReceiptDigest)),
            Text("OK"));
        await Navigation.PopAsync();
    }

    private Sr5PlaytimeDamageSnapshot? ProjectCurrent()
        => Sr5PlaytimeDamageIntegrity.TryProject(
            Coordinator.State.Profile?.Created == true,
            Coordinator.State.Rules?.GameEdition,
            Coordinator.State.WorkspaceId,
            Coordinator.State.ContentRevision,
            Coordinator.State.SavedRevision,
            Coordinator.State.IsDirty,
            Coordinator.State.Error,
            Coordinator.State.ActiveConditionMonitor,
            _journal.Quote.Original.Track,
            out Sr5PlaytimeDamageSnapshot snapshot)
            ? snapshot
            : null;
}
