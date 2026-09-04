using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
using Chummer.Android.Proof;
#endif

namespace Chummer.Android.Native;

public sealed class Sr5TableWizardPage : NativePageBase
{
    private readonly Sr5TableWizardLane _lane;
    private readonly RunnerSessionSr5TableWizardPhoneAuthority _authority;
    private readonly Sr5TableWizardTransactionStore _transactionStore;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CancellationTokenSource? _loadLifetime;
    private Sr5TableWizardSession? _session;
    private Sr5TableWizardSnapshot? _snapshot;
    private Sr5TableWizardTransactionJournal? _transaction;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
    private Sr5TableWizardCheckpointReadStatus _checkpointReadStatus =
        Sr5TableWizardCheckpointReadStatus.Empty;
#endif
    private string? _notice;
    private bool _loading;
    private long _loadVersion;

    internal Sr5TableWizardLane Lane => _lane;

    public Sr5TableWizardPage(
        RunnerSessionCoordinator coordinator,
        Sr5TableWizardLane lane) : base(coordinator)
    {
        if (!Enum.IsDefined(lane))
            throw new ArgumentOutOfRangeException(nameof(lane));
        _lane = lane;
        _authority = new RunnerSessionSr5TableWizardPhoneAuthority(coordinator);
        _transactionStore = new Sr5TableWizardTransactionStore(
            new PreferencesSr5TableWizardCheckpointBackend(lane));
        Title = lane == Sr5TableWizardLane.BeforeRun
            ? Text("Before the run")
            : Text("Live / playtime");
        AutomationId = lane == Sr5TableWizardLane.BeforeRun
            ? Sr5CareerWizardRoutes.BeforeRun
            : Sr5CareerWizardRoutes.Playtime;
        Content = new ScrollView { Content = _body };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _loadLifetime?.Cancel();
        _loadLifetime?.Dispose();
        _loadLifetime = new CancellationTokenSource();
        await LoadLatestAsync(_loadLifetime.Token);
    }

    protected override void OnDisappearing()
    {
        _loadLifetime?.Cancel();
        _loadLifetime?.Dispose();
        _loadLifetime = null;
        Interlocked.Increment(ref _loadVersion);
        _loading = false;
        base.OnDisappearing();
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("SR5 Career · table-safe actions")));
        _body.Add(NativeTheme.Title(
            _lane == Sr5TableWizardLane.BeforeRun
                ? Text("Before the run")
                : Text("Live / playtime")));
        _body.Add(NativeTheme.Body(
            _lane == Sr5TableWizardLane.BeforeRun
                ? Text("Review one exact point of Edge use before the run. Loadout, preparation, contacts, and commitments remain unavailable until they have typed authority.")
                : Text("Use exact Edge, direct Weapon-fire, and Physical or Stun damage actions without opening unrestricted runner editing."),
            NativeTheme.Muted));
        if (_lane == Sr5TableWizardLane.BeforeRun)
            AddCapabilityScope(Sr5CareerRunCapabilityCatalog.BeforeRun);

        if (_loading)
        {
            AddStatus(Text("Loading exact table authority…"), "sr5-table-wizard-loading", NativeTheme.Muted);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            PublishApi36ProofState("loading", settled: false);
#endif
            return;
        }
        if (_snapshot is null || _session is null)
        {
            bool saveRequired = Coordinator.State.IsDirty
                                || Coordinator.State.ContentRevision
                                != Coordinator.State.SavedRevision;
            AddStatus(
                saveRequired
                    ? Text("Save this runner before opening a table action. No review or resumable transaction was opened.")
                    : Text("Exact SR5 table authority is unavailable. No fallback action was opened."),
                saveRequired
                    ? "sr5-table-wizard-save-required"
                    : "sr5-table-wizard-unavailable",
                NativeTheme.Danger);
            Button retry = NativeTheme.SecondaryButton(Text("Retry"));
            retry.AutomationId = "sr5-table-wizard-retry";
            retry.Clicked += async (_, _) => await ReloadAsync();
            _body.Add(retry);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            PublishApi36ProofState("unavailable", settled: true);
#endif
            return;
        }

        Sr5TableWizardState state = _session.State;
        if (!MatchesCurrent(state.Snapshot))
        {
            _transaction = null;
            AddStatus(
                Text("The saved runner changed. Return to Career and reopen this table wizard."),
                "sr5-table-wizard-stale",
                NativeTheme.Danger);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            PublishApi36ProofState("stale", settled: true);
#endif
            return;
        }

        Label binding = NativeTheme.Body(
            Format(
                "Workspace {0} · revision {1} · snapshot {2}",
                state.Snapshot.WorkspaceId.Value,
                state.Snapshot.WorkspaceRevision,
                ShortDigest(state.Snapshot.SnapshotDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "sr5-table-wizard-binding";
        _body.Add(NativeTheme.Card(binding));

        if (!string.IsNullOrWhiteSpace(_notice))
        {
            AddStatus(
                NoticeMessage(_notice),
                "sr5-table-wizard-checkpoint-notice",
                NativeTheme.Muted);
        }

        if (_transaction is { Phase: Sr5TableWizardTransactionPhase.Applied, Receipt: { } receipt })
        {
            AddReceipt(receipt);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            PublishApi36ProofState("receipt-ready", settled: true);
#endif
            return;
        }
        if (_transaction is { Phase: Sr5TableWizardTransactionPhase.Applying })
        {
            AddStatus(
                Text("The prior confirmation is locked because its exact postcondition could not be classified. Reload the current runner before retrying."),
                "sr5-table-wizard-applying-conflict",
                NativeTheme.Danger);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            PublishApi36ProofState("conflict", settled: true);
#endif
            return;
        }
        if (_transaction is { Phase: Sr5TableWizardTransactionPhase.Reviewed } reviewed
            && state.Resume.Restored
            && state.SelectedAction is not null)
        {
            _body.Add(NativeTheme.NavigationRow(
                Text("Resume reviewed action"),
                ActionDetail(reviewed.Quote),
                () => Navigation.PushAsync(new Sr5TableWizardReviewPage(
                    Coordinator,
                    _session,
                    _transactionStore,
                    reviewed)),
                automationId: "sr5-table-wizard-resume-review"));
        }

        Sr5TableWizardActionState[] edgeActions = state.Snapshot.Actions
            .Where(static action => action.Identity.Kind is
                Sr5TableWizardActionKind.SpendEdge or Sr5TableWizardActionKind.RegainEdge)
            .ToArray();
        if (edgeActions.Length > 0)
        {
            _body.Add(NativeTheme.Eyebrow(Text("Edge")));
            foreach (Sr5TableWizardActionState action in edgeActions)
                AddAction(action);
        }

        Sr5TableWizardActionState[] weaponActions = state.Snapshot.Actions
            .Where(static action => action.Identity.Kind == Sr5TableWizardActionKind.FireWeapon)
            .ToArray();
        if (weaponActions.Length > 0)
        {
            _body.Add(NativeTheme.Eyebrow(Text("Direct Career Weapons")));
            foreach (IGrouping<Guid, Sr5TableWizardActionState> weapon in weaponActions
                         .GroupBy(static action => action.Identity.WeaponId))
            {
                _body.Add(NativeTheme.FieldLabel(weapon.First().DisplayName));
                foreach (Sr5TableWizardActionState action in weapon)
                    AddAction(action);
            }
        }

        bool hasDamageActions = false;
        if (_lane == Sr5TableWizardLane.Playtime
            && Coordinator.State.ActiveConditionMonitor is { CareerEditable: true } conditionMonitor)
        {
            ConditionMonitorTrackState[] damageTracks = conditionMonitor.Tracks
                .Where(track => track is not null
                                && Sr5PlaytimeDamageIntegrity.IsSupportedTrack(track.Track)
                                && !track.ActsAsAlternateTrack
                                && track.EditableMaximum > 0
                                && track.Filled >= 0
                                && track.Filled <= track.EditableMaximum)
                .GroupBy(static track => track.Track)
                .Where(static group => group.Count() == 1)
                .Select(static group => group.Single())
                .OrderBy(static track => track.Track)
                .ToArray();
            if (damageTracks.Length > 0)
            {
                hasDamageActions = true;
                _body.Add(NativeTheme.Eyebrow(Text("Condition tracks")));
                foreach (ConditionMonitorTrackState track in damageTracks)
                {
                    string token = Sr5PlaytimeDamageWizardPage.Token(track.Track);
                    _body.Add(NativeTheme.NavigationRow(
                        Format("Set {0} damage", track.Label),
                        Format("Exact saved boxes {0} / {1} · review and receipt required",
                            track.Filled,
                            track.EditableMaximum),
                        () => Navigation.PushAsync(new Sr5PlaytimeDamageWizardPage(
                            Coordinator,
                            track.Track,
                            state.Snapshot.WorkspaceId)),
                        automationId: $"sr5-table-playtime-damage-{token}"));
                }
            }
        }

        if (state.Snapshot.Actions.Count == 0 && !hasDamageActions)
        {
            AddStatus(
                Text("No exact table-safe action is available for this runner state."),
                "sr5-table-wizard-no-actions",
                NativeTheme.Danger);
        }

        Label boundary = NativeTheme.Body(
            Text("Selection saves a digest-bound review draft. Apply rechecks the exact workspace revision and uses only typed Edge, Weapon, or condition-track requests."),
            NativeTheme.Muted);
        boundary.AutomationId = "sr5-table-wizard-boundary";
        _body.Add(NativeTheme.Card(boundary));
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        PublishApi36ProofState(
            _transaction is { Phase: Sr5TableWizardTransactionPhase.Reviewed }
            && state.Resume.Restored
                ? "resume-ready"
                : "ready",
            settled: true);
#endif
    }

#if CHUMMER_API36_PROOF_INSTRUMENTATION
    private void PublishApi36ProofState(string stage, bool settled)
        => Api36ProofStatePublisher.TryPublishTableWizard(
            this,
            Coordinator,
            PhoneShellRoutes.Runner,
            _lane,
            stage,
            settled,
            _checkpointReadStatus,
            _session,
            _transaction,
            statusCode: null);
#endif

    private void AddAction(Sr5TableWizardActionState action)
    {
        string title = action.Identity.Kind switch
        {
            Sr5TableWizardActionKind.SpendEdge => Text("Spend 1 Edge"),
            Sr5TableWizardActionKind.RegainEdge => Text("Regain 1 Edge"),
            Sr5TableWizardActionKind.FireWeapon when action.Identity.FireMode is { } mode
                => Format("Fire · {0}", ModeLabel(mode)),
            _ => throw new InvalidOperationException("Unknown SR5 table action identity.")
        };
        _body.Add(NativeTheme.NavigationRow(
            title,
            ActionDetail(action),
            () => RunWithConditionalRefreshAsync(() => QuoteAsync(action.Identity)),
            automationId: "sr5-table-action-" + action.Identity.ActionDigest[7..19]));
    }

    private async Task<bool> QuoteAsync(Sr5TableWizardActionIdentity identity)
    {
        if (_session is null
            || !MatchesCurrent(_session.State.Snapshot)
            || !_session.TrySelect(identity))
        {
            await DisplayAlertAsync(
                Text("Table authority changed"),
                Text("Return to Career and load the current runner revision."),
                Text("OK"));
            return true;
        }
        await Navigation.PushAsync(new Sr5TableWizardQuotePage(
            Coordinator,
            _session,
            _transactionStore));
        // The quote page owns the next visual and proof state. Refreshing this
        // disappeared lane page after PushAsync would republish its stale
        // `ready` state over the quote page's `quote-ready` authority.
        return false;
    }

    private async Task ReloadAsync()
    {
        _loadLifetime?.Cancel();
        _loadLifetime?.Dispose();
        _loadLifetime = new CancellationTokenSource();
        await LoadLatestAsync(_loadLifetime.Token);
    }

    private async Task LoadLatestAsync(CancellationToken cancellationToken)
    {
        long version = Interlocked.Increment(ref _loadVersion);
        _loading = true;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        _checkpointReadStatus = Sr5TableWizardCheckpointReadStatus.Empty;
#endif
        _notice = null;
        Refresh();
        try
        {
            await Coordinator.InitializeAsync();
            // This method owns MAUI view state in its finally block. Preserve the UI
            // synchronization context so Refresh never mutates the visual tree from a
            // thread-pool continuation.
            Sr5TableWizardSnapshot? snapshot = await _authority
                .LoadAsync(_lane, cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || version != Volatile.Read(ref _loadVersion))
            {
                return;
            }
            if (snapshot is null)
            {
                _snapshot = null;
                _session = null;
                _transaction = null;
                return;
            }

            Sr5TableWizardCheckpointReadStatus transactionStatus =
                _transactionStore.TryRead(out Sr5TableWizardTransactionJournal? transaction);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            _checkpointReadStatus = transactionStatus;
#endif
            if (transactionStatus == Sr5TableWizardCheckpointReadStatus.Unavailable)
            {
                _snapshot = null;
                _session = null;
                return;
            }
            if (transactionStatus == Sr5TableWizardCheckpointReadStatus.Invalid)
                _notice = Sr5TableWizardCheckpointInvalidationReasons.InvalidCheckpoint;

            if (transaction is { Phase: Sr5TableWizardTransactionPhase.Applying })
            {
                Sr5TableWizardRecoveryObservation observation =
                    Sr5TableWizardTypedTransactionPresenter.Observe(transaction, snapshot, out _);
                if (observation == Sr5TableWizardRecoveryObservation.Original
                    && _transactionStore.TryReturnToReview(transaction, out var recovered))
                {
                    transaction = recovered;
                    _notice = "table-wizard-applying-not-observed";
                }
                else if (observation == Sr5TableWizardRecoveryObservation.Applied
                         && _transactionStore.TryComplete(transaction, snapshot, out var applied))
                {
                    transaction = applied;
                    _notice = "table-wizard-receipt-recovered";
                }
            }

            var session = new Sr5TableWizardSession();
            Sr5TableWizardState state = session.Bind(
                snapshot,
                transaction is { Phase: Sr5TableWizardTransactionPhase.Reviewed }
                    ? transaction.Review
                    : null);
            if (transaction is { Phase: Sr5TableWizardTransactionPhase.Reviewed }
                && !state.Resume.Restored)
            {
                if (_transactionStore.TryDiscardReview(transaction))
                    transaction = null;
                _notice = state.Resume.InvalidationReason
                          ?? Sr5TableWizardCheckpointInvalidationReasons.InvalidCheckpoint;
            }
            else if (state.Resume.Restored)
            {
                _notice = "table-wizard-checkpoint-restored";
            }
            _snapshot = snapshot;
            _session = session;
            _transaction = transaction;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leaving the page cancels read-only authority composition.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                _snapshot = null;
                _session = null;
                _transaction = null;
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                _loading = false;
                Refresh();
            }
        }
    }

    private bool MatchesCurrent(Sr5TableWizardSnapshot snapshot)
        => Coordinator.State.Profile?.Created == true
           && Sr5CareerWizardCatalog.IsSr5CareerRunner(
               true,
               Coordinator.State.Rules?.GameEdition)
           && Coordinator.State.WorkspaceId == snapshot.WorkspaceId
           && Coordinator.State.ContentRevision == snapshot.WorkspaceRevision
           && Coordinator.State.SavedRevision == snapshot.WorkspaceRevision
           && !Coordinator.State.IsDirty;

    private void AddStatus(string text, string automationId, Color color)
    {
        Label label = NativeTheme.Body(text, color);
        label.AutomationId = automationId;
        _body.Add(NativeTheme.Card(label));
    }

    private void AddCapabilityScope(IReadOnlyList<Sr5CareerRunCapability> capabilities)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(Text("Typed mutation scope")));
        foreach (Sr5CareerRunCapability capability in capabilities)
        {
            string status = capability.Status switch
            {
                Sr5CareerRunCapabilityStatus.Available => Text("available"),
                Sr5CareerRunCapabilityStatus.ReadOnly => Text("read-only"),
                Sr5CareerRunCapabilityStatus.Unavailable => Text("unavailable"),
                _ => throw new ArgumentOutOfRangeException()
            };
            Label row = NativeTheme.Body(
                Format("{0} · {1} · {2}", capability.Label, status, capability.Authority),
                capability.Status == Sr5CareerRunCapabilityStatus.Unavailable
                    ? NativeTheme.Danger
                    : NativeTheme.Muted);
            row.AutomationId = "sr5-table-capability-" + capability.Id;
            card.Add(row);
        }
        View border = NativeTheme.Card(card);
        border.AutomationId = "sr5-table-capability-scope";
        _body.Add(border);
    }

    private void AddReceipt(Sr5TableWizardTransactionReceipt receipt)
    {
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow(Text("Verified receipt")));
        card.Add(NativeTheme.Body(Format(
            "{0} · revision {1} → {2}",
            receipt.ActionId,
            receipt.ExpectedWorkspaceRevision,
            receipt.AppliedWorkspaceRevision)));
        Label digest = NativeTheme.Body(
            Format("Receipt {0}", ShortDigest(receipt.ReceiptDigest)),
            NativeTheme.Muted);
        digest.AutomationId = "sr5-table-wizard-receipt-digest";
        card.Add(digest);
        Button acknowledge = NativeTheme.PrimaryButton(Text("Acknowledge receipt"));
        acknowledge.AutomationId = "sr5-table-wizard-receipt-acknowledge";
        acknowledge.Clicked += (_, _) =>
        {
            if (_transaction is { } current
                && _transactionStore.TryClearApplied(current))
            {
                _transaction = null;
                _notice = null;
                Refresh();
            }
        };
        card.Add(acknowledge);
        View border = NativeTheme.Card(card);
        border.AutomationId = "sr5-table-wizard-receipt";
        _body.Add(border);
    }

    private static string ActionDetail(Sr5TableWizardActionState action)
        => action.Identity.Kind switch
        {
            Sr5TableWizardActionKind.SpendEdge or Sr5TableWizardActionKind.RegainEdge =>
                Format(
                    "Edge used {0} → {1}",
                    action.EdgeUsedBefore,
                    action.EdgeUsedAfter),
            Sr5TableWizardActionKind.FireWeapon when action.WeaponPlan is { } plan =>
                Format(
                    "{0} rounds · ammo {1} → {2}{3}",
                    plan.RoundsConsumed,
                    checked(plan.NewAmmoRemaining + plan.RoundsConsumed),
                    plan.NewAmmoRemaining,
                    plan.RequiresPartialConfirmation ? Text(" · shortened burst") : string.Empty),
            _ => throw new InvalidOperationException("Unknown SR5 table action detail.")
        };

    private static string NoticeMessage(string notice)
        => notice switch
        {
            "table-wizard-checkpoint-restored" =>
                Text("The exact reviewed action was restored. Confirm it again before saving."),
            "table-wizard-applying-not-observed" =>
                Text("The previous confirmation did not change the runner. Its exact quote returned to review."),
            "table-wizard-receipt-recovered" =>
                Text("The previous confirmation was observed after restart and its exact receipt was recovered."),
            Sr5TableWizardCheckpointInvalidationReasons.WorkspaceRevisionChanged =>
                Text("The runner revision changed, so the saved table review was discarded."),
            Sr5TableWizardCheckpointInvalidationReasons.SnapshotChanged =>
                Text("Edge, ammunition, modes, or table authority changed; the saved review was discarded."),
            Sr5TableWizardCheckpointInvalidationReasons.ActionUnavailable =>
                Text("The reviewed table action is no longer available and was discarded."),
            Sr5TableWizardCheckpointInvalidationReasons.LaneChanged =>
                Text("The saved review belonged to another table lane and was discarded."),
            Sr5TableWizardCheckpointInvalidationReasons.WorkspaceChanged =>
                Text("The saved review belonged to another runner and was discarded."),
            _ => Text("An invalid table review draft was discarded.")
        };

    internal static string ModeLabel(CharacterWeaponFireMode mode)
        => mode switch
        {
            CharacterWeaponFireMode.SingleShot => Text("Single Shot"),
            CharacterWeaponFireMode.ShortBurst => Text("Short Burst"),
            CharacterWeaponFireMode.LongBurst => Text("Long Burst"),
            CharacterWeaponFireMode.FullBurst => Text("Full Burst"),
            CharacterWeaponFireMode.SuppressiveFire => Text("Suppressive Fire"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

    internal static string ShortDigest(string value)
        => value.Length <= 19 ? value : value[..19] + "…";
}

public sealed class Sr5TableWizardQuotePage : NativePageBase
{
    private readonly Sr5TableWizardSession _session;
    private readonly Sr5TableWizardTransactionStore _transactionStore;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5TableWizardQuotePage(
        RunnerSessionCoordinator coordinator,
        Sr5TableWizardSession session,
        Sr5TableWizardTransactionStore transactionStore) : base(coordinator)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _transactionStore = transactionStore ?? throw new ArgumentNullException(nameof(transactionStore));
        if (_session.State.SelectedAction is null)
            throw new ArgumentException("Quote requires an exact selected table action.", nameof(session));
        Title = Text("Exact table quote");
        AutomationId = "sr5-table-wizard-quote";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        Sr5TableWizardActionState quote = _session.State.SelectedAction
            ?? throw new InvalidOperationException("The exact table quote disappeared.");
        _body.Add(NativeTheme.Eyebrow(Text("Configure → quote")));
        _body.Add(NativeTheme.Title(Sr5TableWizardReviewPage.TitleFor(quote)));
        _body.Add(NativeTheme.Card(NativeTheme.Body(
            Sr5TableWizardReviewPage.ReviewDetail(quote))));

        VerticalStackLayout facts = new() { Spacing = 6 };
        facts.Add(NativeTheme.FieldLabel(Text("Exact authority facts")));
        facts.Add(NativeTheme.Body(quote.Identity.Kind switch
        {
            Sr5TableWizardActionKind.SpendEdge =>
                Text("Resource cost: 1 point of current Edge use. Karma: 0. Nuyen: 0."),
            Sr5TableWizardActionKind.RegainEdge =>
                Text("Resource change: restore 1 used Edge. Karma: 0. Nuyen: 0."),
            Sr5TableWizardActionKind.FireWeapon when quote.WeaponPlan is { } plan =>
                Format("Resource cost: {0} rounds from the bound active clip. Karma: 0. Nuyen: 0.", plan.RoundsConsumed),
            _ => throw new InvalidOperationException("Unknown typed table quote.")
        }));
        facts.Add(NativeTheme.Body(
            Text("Prerequisites: the exact saved runner revision and this listed typed action must remain available."),
            NativeTheme.Muted));
        facts.Add(NativeTheme.Body(
            Text("Elapsed in-game time: not supplied by this typed table leaf; no duration is invented."),
            NativeTheme.Muted));
        View quoteCard = NativeTheme.Card(facts);
        quoteCard.AutomationId = "sr5-table-wizard-quote-facts";
        _body.Add(quoteCard);

        Button review = NativeTheme.PrimaryButton(Text("Review exact diff"));
        review.AutomationId = "sr5-table-wizard-open-review";
        review.Clicked += async (_, _) => await RunWithConditionalRefreshAsync(OpenReviewAsync);
        _body.Add(review);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        Api36ProofStatePublisher.TryPublishTableWizard(
            this,
            Coordinator,
            PhoneShellRoutes.Runner,
            _session.State.Snapshot.Lane,
            "quote-ready",
            settled: true,
            checkpointReadStatus: Sr5TableWizardCheckpointReadStatus.Empty,
            session: _session,
            transaction: null,
            statusCode: null);
#endif
    }

    private async Task<bool> OpenReviewAsync()
    {
        Sr5TableWizardSnapshot snapshot = _session.State.Snapshot;
        if (Coordinator.State.WorkspaceId != snapshot.WorkspaceId
            || Coordinator.State.ContentRevision != snapshot.WorkspaceRevision
            || Coordinator.State.SavedRevision != snapshot.WorkspaceRevision
            || Coordinator.State.IsDirty)
        {
            await DisplayAlertAsync(
                Text("Runner changed"),
                Text("Reopen Playtime and request a current quote."),
                Text("OK"));
            return true;
        }
        if (!_transactionStore.TryWriteReview(
                _session,
                Guid.NewGuid(),
                Guid.NewGuid(),
                out Sr5TableWizardTransactionJournal? review))
        {
            await DisplayAlertAsync(
                Text("Review unavailable"),
                Text("The exact quote was not opened because its durable review could not be saved and verified."),
                Text("OK"));
            return true;
        }
        await Navigation.PushAsync(new Sr5TableWizardReviewPage(
            Coordinator,
            _session,
            _transactionStore,
            review!));
        // The review page owns the next visual and proof state. Rebuilding this
        // disappeared quote page after PushAsync would publish `quote-ready`
        // over the review page's `review-ready` authority.
        return false;
    }
}

public sealed class Sr5TableWizardReviewPage : NativePageBase
{
    private readonly Sr5TableWizardSession _session;
    private readonly Sr5TableWizardTransactionStore _transactionStore;
    private Sr5TableWizardTransactionJournal _transaction;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly Button _confirm;

    public Sr5TableWizardReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5TableWizardSession session,
        Sr5TableWizardTransactionStore transactionStore,
        Sr5TableWizardTransactionJournal transaction) : base(coordinator)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _transactionStore = transactionStore ?? throw new ArgumentNullException(nameof(transactionStore));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        if (_session.State.SelectedAction is null
            || !_transaction.IsExact()
            || _transaction.Phase != Sr5TableWizardTransactionPhase.Reviewed
            || _transaction.Quote != _session.State.SelectedAction)
            throw new ArgumentException("Review requires an exact selected table action.", nameof(session));
        Title = Text("Review table action");
        AutomationId = _session.State.Snapshot.Lane == Sr5TableWizardLane.BeforeRun
            ? Sr5CareerWizardRoutes.BeforeRunReview
            : Sr5CareerWizardRoutes.PlaytimeReview;
        _confirm = NativeTheme.PrimaryButton(Text("Confirm and save exact action"));
        _confirm.AutomationId = "sr5-table-wizard-confirm";
        _confirm.Clicked += async (_, _) => await RunWithConditionalRefreshAsync(ConfirmAsync);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        Sr5TableWizardState state = _session.State;
        Sr5TableWizardActionState selected = state.SelectedAction
            ?? throw new InvalidOperationException("The reviewed table action disappeared.");
        bool current = MatchesCurrent(state.Snapshot);

        _body.Add(NativeTheme.Eyebrow(Text("Digest-bound review")));
        _body.Add(NativeTheme.Title(TitleFor(selected)));
        if (selected.Identity.Kind == Sr5TableWizardActionKind.FireWeapon)
            _body.Add(NativeTheme.FieldLabel(selected.DisplayName));
        _body.Add(NativeTheme.Card(NativeTheme.Body(ReviewDetail(selected))));
        Label identity = NativeTheme.Body(
            Format(
                "Action {0} · target {1} · review {2}",
                selected.Identity.ActionId,
                Sr5TableWizardPage.ShortDigest(selected.Identity.TargetRevision),
                Sr5TableWizardPage.ShortDigest(selected.Identity.ActionDigest)),
            NativeTheme.Muted);
        identity.AutomationId = "sr5-table-wizard-review-identity";
        _body.Add(NativeTheme.Card(identity));

        if (!current)
        {
            Label stale = NativeTheme.Body(
                Text("The runner changed after review. This action cannot be saved."),
                NativeTheme.Danger);
            stale.AutomationId = "sr5-table-wizard-review-stale";
            _body.Add(NativeTheme.Card(stale));
        }
        _confirm.IsEnabled = current && state.CanConfirm;
        _body.Add(_confirm);
        _body.Add(NativeTheme.Body(
            Text("Confirming does not use a generic editor. It sends the exact typed request through the existing revision-checked save boundary."),
            NativeTheme.Muted));
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        PublishApi36ProofState(current ? "review-ready" : "stale", settled: true);
#endif
    }

    private async Task<bool> ConfirmAsync()
    {
        Sr5TableWizardState state = _session.State;
        Sr5TableWizardActionState selected = state.SelectedAction
            ?? throw new InvalidOperationException("Choose an exact table action first.");
        if (!MatchesCurrent(state.Snapshot))
        {
            await DisplayAlertAsync(
                Text("Runner changed"),
                Text("Reopen the table wizard and review the current runner revision."),
                Text("OK"));
            return true;
        }

        if (!_transactionStore.TryBeginApplying(
                _transaction,
                out Sr5TableWizardTransactionJournal? applying))
        {
            await DisplayAlertAsync(
                Text("Confirmation already claimed"),
                Text("This exact review is stale, already applying, or already has a receipt. Reopen Playtime to recover it."),
                Text("OK"));
            return true;
        }
        _transaction = applying!;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        PublishApi36ProofState("applying", settled: false);
#endif

        long expectedRevision = state.Snapshot.WorkspaceRevision;
        if (selected.Identity.Kind == Sr5TableWizardActionKind.FireWeapon)
        {
            await Coordinator.ApplyCareerWeaponFireAsync(
                _session.CreateWeaponRequest(confirmed: true));
        }
        else
        {
            await Coordinator.ApplyCareerEdgeUseEditAsync(
                _session.CreateEdgeRequest(confirmed: true));
        }

        Sr5TableWizardSnapshot? observed = null;
        if (Coordinator.State.Error is null
            && Coordinator.State.WorkspaceId == state.Snapshot.WorkspaceId
            && Coordinator.State.ContentRevision == expectedRevision + 1)
        {
            observed = await new RunnerSessionSr5TableWizardPhoneAuthority(Coordinator)
                .LoadAsync(state.Snapshot.Lane, CancellationToken.None);
        }
        if (observed is null
            || !_transactionStore.TryComplete(_transaction, observed, out var applied))
        {
            await DisplayAlertAsync(
                Text("Save not verified"),
                Text("The exact next-revision postcondition was not observed. The Applying journal remains locked for restart recovery."),
                Text("OK"));
            return true;
        }

        _transaction = applied!;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        PublishApi36ProofState("applied", settled: true);
#endif
        await DisplayAlertAsync(
            Text("Table action saved"),
            Format(
                "Verified runner revision {0}. Receipt {1}.",
                applied!.Receipt!.AppliedWorkspaceRevision,
                Sr5TableWizardPage.ShortDigest(applied.Receipt.ReceiptDigest)),
            Text("OK"));
        await ReturnToOwningLaneAsync();
        // The Playtime lane owns the final receipt and proof state. Refreshing
        // this disappeared review page would publish a stale review over it.
        return false;
    }

    private async Task ReturnToOwningLaneAsync()
    {
        INavigation navigation = Navigation;
        Page[] navigationStack = navigation.NavigationStack.ToArray();
        string expectedLaneRoute = _session.State.Snapshot.Lane == Sr5TableWizardLane.BeforeRun
            ? Sr5CareerWizardRoutes.BeforeRun
            : Sr5CareerWizardRoutes.Playtime;
        bool IsOwningLane(Page page)
            => page is Sr5TableWizardPage lanePage
               && lanePage.Lane == _session.State.Snapshot.Lane
               && string.Equals(
                   lanePage.AutomationId,
                   expectedLaneRoute,
                   StringComparison.Ordinal);

        int quoteCount = navigationStack.Count(static page =>
            page is Sr5TableWizardQuotePage);
        if (navigationStack.Length < 2
            || !ReferenceEquals(navigationStack[^1], this)
            || navigationStack.Count(IsOwningLane) != 1
            || quoteCount > 1)
        {
            throw new InvalidOperationException(
                "The saved table action could not return through an exact unique lane stack.");
        }

        if (navigationStack[^2] is Sr5TableWizardQuotePage quotePage)
        {
            if (quoteCount != 1
                || navigationStack.Length < 3
                || !IsOwningLane(navigationStack[^3]))
            {
                throw new InvalidOperationException(
                    "The saved table action could not return through its exact quote and lane.");
            }
            navigation.RemovePage(quotePage);
        }
        else if (quoteCount != 0 || !IsOwningLane(navigationStack[^2]))
        {
            throw new InvalidOperationException(
                "The saved table action could not return to its exact resumed lane.");
        }

        Page[] preparedStack = navigation.NavigationStack.ToArray();
        if (preparedStack.Length < 2
            || !ReferenceEquals(preparedStack[^1], this)
            || !IsOwningLane(preparedStack[^2])
            || preparedStack.Any(static page => page is Sr5TableWizardQuotePage))
        {
            throw new InvalidOperationException(
                "The saved table action return stack changed before navigation completed.");
        }

        await navigation.PopAsync();
    }

    private bool MatchesCurrent(Sr5TableWizardSnapshot snapshot)
        => Coordinator.State.Profile?.Created == true
           && Sr5CareerWizardCatalog.IsSr5CareerRunner(
               true,
               Coordinator.State.Rules?.GameEdition)
           && Coordinator.State.WorkspaceId == snapshot.WorkspaceId
           && Coordinator.State.ContentRevision == snapshot.WorkspaceRevision
           && Coordinator.State.SavedRevision == snapshot.WorkspaceRevision
           && !Coordinator.State.IsDirty;

#if CHUMMER_API36_PROOF_INSTRUMENTATION
    private void PublishApi36ProofState(string stage, bool settled)
        => Api36ProofStatePublisher.TryPublishTableWizard(
            this,
            Coordinator,
            PhoneShellRoutes.Runner,
            _session.State.Snapshot.Lane,
            stage,
            settled,
            Sr5TableWizardCheckpointReadStatus.Ready,
            _session,
            _transaction,
            statusCode: null);
#endif

    internal static string TitleFor(Sr5TableWizardActionState action)
        => action.Identity.Kind switch
        {
            Sr5TableWizardActionKind.SpendEdge => Text("Spend 1 Edge"),
            Sr5TableWizardActionKind.RegainEdge => Text("Regain 1 Edge"),
            Sr5TableWizardActionKind.FireWeapon when action.Identity.FireMode is { } mode
                => Format("Fire · {0}", Sr5TableWizardPage.ModeLabel(mode)),
            _ => throw new InvalidOperationException("Unknown reviewed table action.")
        };

    internal static string ReviewDetail(Sr5TableWizardActionState action)
        => action.Identity.Kind switch
        {
            Sr5TableWizardActionKind.SpendEdge or Sr5TableWizardActionKind.RegainEdge =>
                Format(
                    "Saved EdgeUsed changes by exactly one: {0} → {1}.",
                    action.EdgeUsedBefore,
                    action.EdgeUsedAfter),
            Sr5TableWizardActionKind.FireWeapon when action.WeaponPlan is { } plan =>
                Format(
                    "Consume {0} rounds from active clip {1}. Ammunition changes {2} → {3}.{4}",
                    plan.RoundsConsumed,
                    action.Identity.AmmoSlot,
                    checked(plan.NewAmmoRemaining + plan.RoundsConsumed),
                    plan.NewAmmoRemaining,
                    plan.RequiresPartialConfirmation
                        ? Text(" This confirms Chummer5's shortened burst using all remaining rounds.")
                        : string.Empty),
            _ => throw new InvalidOperationException("Unknown reviewed table action detail.")
        };
}

internal sealed class PreferencesSr5TableWizardCheckpointBackend :
    ISr5TableWizardCheckpointBackend
{
    private readonly string _storageKey;

    public PreferencesSr5TableWizardCheckpointBackend(Sr5TableWizardLane lane)
    {
        _storageKey = lane switch
        {
            Sr5TableWizardLane.BeforeRun =>
                "chummer.android.sr5-before-run.review.v1",
            Sr5TableWizardLane.Playtime =>
                "chummer.android.sr5-playtime.review.v1",
            _ => throw new ArgumentOutOfRangeException(nameof(lane))
        };
    }

    public string Read() => Preferences.Default.Get(_storageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(_storageKey, payload);
    public void Remove() => Preferences.Default.Remove(_storageKey);
}
