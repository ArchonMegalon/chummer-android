using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

public sealed class Sr5TableWizardPage : NativePageBase
{
    private readonly Sr5TableWizardLane _lane;
    private readonly RunnerSessionSr5TableWizardPhoneAuthority _authority;
    private readonly Sr5TableWizardCheckpointStore _checkpointStore;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CancellationTokenSource? _loadLifetime;
    private Sr5TableWizardSession? _session;
    private Sr5TableWizardSnapshot? _snapshot;
    private string? _notice;
    private bool _loading;
    private long _loadVersion;

    public Sr5TableWizardPage(
        RunnerSessionCoordinator coordinator,
        Sr5TableWizardLane lane) : base(coordinator)
    {
        if (!Enum.IsDefined(lane))
            throw new ArgumentOutOfRangeException(nameof(lane));
        _lane = lane;
        _authority = new RunnerSessionSr5TableWizardPhoneAuthority(coordinator);
        _checkpointStore = new Sr5TableWizardCheckpointStore(
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
                ? Text("Review one exact point of Edge use before the run. Loadout, healing, contacts, and acquisitions remain unavailable until they have typed authority.")
                : Text("Use exact Edge and direct Weapon-fire actions without opening unrestricted runner editing."),
            NativeTheme.Muted));

        if (_loading)
        {
            AddStatus(Text("Loading exact table authority…"), "sr5-table-wizard-loading", NativeTheme.Muted);
            return;
        }
        if (_snapshot is null || _session is null)
        {
            AddStatus(
                Text("Exact SR5 table authority is unavailable. No fallback action was opened."),
                "sr5-table-wizard-unavailable",
                NativeTheme.Danger);
            Button retry = NativeTheme.SecondaryButton(Text("Retry"));
            retry.AutomationId = "sr5-table-wizard-retry";
            retry.Clicked += async (_, _) => await ReloadAsync();
            _body.Add(retry);
            return;
        }

        Sr5TableWizardState state = _session.State;
        if (!MatchesCurrent(state.Snapshot))
        {
            _checkpointStore.Clear();
            AddStatus(
                Text("The saved runner changed. Return to Career and reopen this table wizard."),
                "sr5-table-wizard-stale",
                NativeTheme.Danger);
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

        if (state.Resume.Restored && state.SelectedAction is not null)
        {
            Sr5TableWizardActionState restored = state.SelectedAction;
            _body.Add(NativeTheme.NavigationRow(
                Text("Resume reviewed action"),
                ActionDetail(restored),
                () => Navigation.PushAsync(new Sr5TableWizardReviewPage(
                    Coordinator,
                    _session,
                    _checkpointStore)),
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

        if (state.Snapshot.Actions.Count == 0)
        {
            AddStatus(
                Text("No exact table-safe action is available for this runner state."),
                "sr5-table-wizard-no-actions",
                NativeTheme.Danger);
        }

        Label boundary = NativeTheme.Body(
            Text("Selection saves a digest-bound review draft. Apply still rechecks workspace revision and uses only the existing typed Edge or Weapon request."),
            NativeTheme.Muted);
        boundary.AutomationId = "sr5-table-wizard-boundary";
        _body.Add(NativeTheme.Card(boundary));
    }

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
            () => RunAsync(() => ReviewAsync(action.Identity)),
            automationId: "sr5-table-action-" + action.Identity.ActionDigest[7..19]));
    }

    private async Task ReviewAsync(Sr5TableWizardActionIdentity identity)
    {
        if (_session is null
            || !MatchesCurrent(_session.State.Snapshot)
            || !_session.TrySelect(identity))
        {
            _checkpointStore.Clear();
            await DisplayAlertAsync(
                Text("Table authority changed"),
                Text("Return to Career and load the current runner revision."),
                Text("OK"));
            return;
        }
        if (!_checkpointStore.TryWrite(_session))
        {
            await DisplayAlertAsync(
                Text("Review draft unavailable"),
                Text("The action was not opened because its durable review draft could not be saved and verified."),
                Text("OK"));
            return;
        }
        await Navigation.PushAsync(new Sr5TableWizardReviewPage(
            Coordinator,
            _session,
            _checkpointStore));
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
        _notice = null;
        Refresh();
        try
        {
            await Coordinator.InitializeAsync();
            Sr5TableWizardSnapshot? snapshot = await _authority
                .LoadAsync(_lane, cancellationToken)
                .ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested
                || version != Volatile.Read(ref _loadVersion))
            {
                return;
            }
            if (snapshot is null)
            {
                _snapshot = null;
                _session = null;
                return;
            }

            Sr5TableWizardCheckpointRead checkpoint = _checkpointStore.Read();
            if (checkpoint.Status == Sr5TableWizardCheckpointReadStatus.Unavailable)
            {
                _snapshot = null;
                _session = null;
                return;
            }
            if (checkpoint.Status == Sr5TableWizardCheckpointReadStatus.Invalid)
                _notice = Sr5TableWizardCheckpointInvalidationReasons.InvalidCheckpoint;

            var session = new Sr5TableWizardSession();
            Sr5TableWizardState state = session.Bind(
                snapshot,
                checkpoint.Status == Sr5TableWizardCheckpointReadStatus.Ready
                    ? checkpoint.Checkpoint
                    : null);
            if (!state.Resume.Restored && state.Resume.InvalidationReason is not null)
            {
                _checkpointStore.Clear();
                _notice = state.Resume.InvalidationReason;
            }
            else if (state.Resume.Restored)
            {
                _notice = "table-wizard-checkpoint-restored";
            }
            _snapshot = snapshot;
            _session = session;
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
           && Coordinator.State.ContentRevision == snapshot.WorkspaceRevision;

    private void AddStatus(string text, string automationId, Color color)
    {
        Label label = NativeTheme.Body(text, color);
        label.AutomationId = automationId;
        _body.Add(NativeTheme.Card(label));
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

public sealed class Sr5TableWizardReviewPage : NativePageBase
{
    private readonly Sr5TableWizardSession _session;
    private readonly Sr5TableWizardCheckpointStore _checkpointStore;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly Button _confirm;

    public Sr5TableWizardReviewPage(
        RunnerSessionCoordinator coordinator,
        Sr5TableWizardSession session,
        Sr5TableWizardCheckpointStore checkpointStore) : base(coordinator)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        if (_session.State.SelectedAction is null)
            throw new ArgumentException("Review requires an exact selected table action.", nameof(session));
        Title = Text("Review table action");
        AutomationId = _session.State.Snapshot.Lane == Sr5TableWizardLane.BeforeRun
            ? Sr5CareerWizardRoutes.BeforeRunReview
            : Sr5CareerWizardRoutes.PlaytimeReview;
        _confirm = NativeTheme.PrimaryButton(Text("Confirm and save exact action"));
        _confirm.AutomationId = "sr5-table-wizard-confirm";
        _confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
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
    }

    private async Task ConfirmAsync()
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
            return;
        }

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

        bool applied = Coordinator.State.Error is null
                       && Coordinator.State.WorkspaceId == state.Snapshot.WorkspaceId
                       && Coordinator.State.ContentRevision > expectedRevision;
        if (!applied)
        {
            await DisplayAlertAsync(
                Text("Save not verified"),
                Text("The exact post-save revision was not observed. The review remains locked for recovery."),
                Text("OK"));
            return;
        }

        _checkpointStore.Clear();
        await DisplayAlertAsync(
            Text("Table action saved"),
            Format(
                "Verified runner revision {0} after {1}.",
                Coordinator.State.ContentRevision,
                TitleFor(selected)),
            Text("OK"));
        await Navigation.PopAsync();
    }

    private bool MatchesCurrent(Sr5TableWizardSnapshot snapshot)
        => Coordinator.State.Profile?.Created == true
           && Sr5CareerWizardCatalog.IsSr5CareerRunner(
               true,
               Coordinator.State.Rules?.GameEdition)
           && Coordinator.State.WorkspaceId == snapshot.WorkspaceId
           && Coordinator.State.ContentRevision == snapshot.WorkspaceRevision;

    private static string TitleFor(Sr5TableWizardActionState action)
        => action.Identity.Kind switch
        {
            Sr5TableWizardActionKind.SpendEdge => Text("Spend 1 Edge"),
            Sr5TableWizardActionKind.RegainEdge => Text("Regain 1 Edge"),
            Sr5TableWizardActionKind.FireWeapon when action.Identity.FireMode is { } mode
                => Format("Fire · {0}", Sr5TableWizardPage.ModeLabel(mode)),
            _ => throw new InvalidOperationException("Unknown reviewed table action.")
        };

    private static string ReviewDetail(Sr5TableWizardActionState action)
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
