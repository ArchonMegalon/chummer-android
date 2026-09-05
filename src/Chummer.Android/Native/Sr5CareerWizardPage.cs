using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Native phone renderer for the renderer-neutral Presentation Career chooser. This page owns
/// only action-family navigation; each destination keeps its existing typed authority boundary.
/// </summary>
public sealed class Sr5CareerWizardPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly RunnerSessionSr5CareerWizardPhoneAuthority _authority;
    private readonly Sr5CareerWizardPhoneCheckpointStore _checkpointStore;
    private CancellationTokenSource? _loadLifetime;
    private Sr5CareerWizardDesktopSession? _session;
    private Sr5CareerWizardSnapshot? _snapshot;
    private string? _loadBlocker;
    private string? _checkpointNotice;
    private string? _afterRunEntryBlocker;
    private long _loadVersion;
    private bool _loading;

    public Sr5CareerWizardPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = WizardStrings.Get("Career.PageTitle", "SR5 Career");
        AutomationId = Sr5CareerWizardRoutes.Hub;
        _authority = new RunnerSessionSr5CareerWizardPhoneAuthority(coordinator);
        _checkpointStore = new Sr5CareerWizardPhoneCheckpointStore(
            new PreferencesSr5CareerWizardPhoneCheckpointBackend());
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(
        CancellationToken cancellationToken)
    {
        _loadLifetime?.Cancel();
        _loadLifetime?.Dispose();
        _loadLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await LoadLatestAsync(_loadLifetime.Token);
        cancellationToken.ThrowIfCancellationRequested();
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
        if (_snapshot is not null && !MatchesCurrentRunner(_snapshot.Binding, Coordinator.State))
        {
            _snapshot = null;
            _session = null;
            _checkpointStore.Clear();
            _checkpointNotice = Sr5CareerWizardCheckpointInvalidationReasons.WorkspaceRevisionChanged;
            ScheduleReload();
        }

        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Career.Eyebrow", "Shadowrun Fifth Edition")));
        _body.Add(NativeTheme.Title(WizardStrings.Get("Career.Heading", "Career wizard")));

        if (!Sr5CareerWizardCatalog.IsSr5CareerRunner(
                Coordinator.State.Profile?.Created == true,
                Coordinator.State.Rules?.GameEdition))
        {
            AddStatus(
                WizardStrings.Get(
                    "Career.RequiresRunner",
                    "This wizard requires a created SR5 runner. It does not fall through to generic editing."),
                "sr5-career-wizard-edition-blocker",
                NativeTheme.Danger);
            return;
        }

        if (_loading)
        {
            AddStatus(
                WizardStrings.Get(
                    "Career.Loading",
                    "Checking the exact workspace and typed Career authorities…"),
                "sr5-career-wizard-loading",
                NativeTheme.Muted);
            return;
        }

        if (_snapshot is null || _session is null)
        {
            AddStatus(
                SafeBlockerMessage(_loadBlocker),
                "sr5-career-wizard-unavailable",
                NativeTheme.Danger);
            Button retry = NativeTheme.SecondaryButton(WizardStrings.Get("Common.Retry", "Retry"));
            retry.AutomationId = "sr5-career-wizard-retry";
            retry.Clicked += async (_, _) => await ReloadAsync();
            _body.Add(retry);
            return;
        }

        Sr5CareerWizardDesktopSession session = _session;
        Sr5CareerWizardDesktopState state = session.State;
        Label binding = NativeTheme.Body(
            WizardStrings.Format(
                "Career.Binding",
                "Workspace {0} · revision {1}/{2} · runtime {3} · source {4} · content {5}",
                state.Snapshot.Binding.WorkspaceId,
                state.Snapshot.Binding.WorkspaceRevision,
                state.Snapshot.Binding.SavedRevision,
                ShortDigest(state.Snapshot.Binding.RuntimeFingerprint),
                ShortDigest(state.Snapshot.Binding.SourceDigest),
                ShortDigest(state.Snapshot.Binding.ContentDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "sr5-career-wizard-binding";
        _body.Add(NativeTheme.Card(binding));

        if (!string.IsNullOrWhiteSpace(_checkpointNotice))
        {
            AddStatus(
                CheckpointMessage(_checkpointNotice),
                "sr5-career-wizard-checkpoint-notice",
                NativeTheme.Muted);
        }

        Sr5CareerCyberwarePurchaseSnapshot commerce =
            Coordinator.LoadCareerCyberwarePurchase();
        Sr5CareerCustomDrugRecipeSnapshot customDrug =
            Coordinator.LoadCareerCustomDrugRecipe();
        bool canOpenCommerce = commerce.IsReady || customDrug.IsReady;
        if (!state.Snapshot.CanOpenAnyAction && !canOpenCommerce)
        {
            AddStatus(
                WizardStrings.Get(
                    "Career.NoActions",
                    "No typed SR5 Career action is available for this exact runner state."),
                "sr5-career-wizard-no-actions",
                NativeTheme.Danger);
            return;
        }

        _body.Add(NativeTheme.Body(
            WizardStrings.Get(
                "Career.ChooseFamily",
                "Choose an action family. Only routes backed by a current typed authority are shown."),
            NativeTheme.Muted));
        foreach (Sr5CareerWizardFamilyState family in state.Snapshot.Families
                     .Where(static family => family.HasAvailableAction))
        {
            Sr5CareerWizardPhoneFamilyDefinition definition =
                Sr5CareerWizardPhoneCatalog.RequireFamily(family.FamilyId);
            int available = family.Actions.Count(static action => action.CanOpen);
            string selected = family.Actions.Any(action =>
                string.Equals(action.ActionId, state.SelectedActionId, StringComparison.Ordinal))
                ? WizardStrings.Get("Career.LastSelection", " · last selection")
                : string.Empty;
            string familyDetail = WizardStrings.Format(
                "Career.AvailableFamily",
                "{0} available{1} · {2}",
                available,
                selected,
                WizardStrings.CareerFamilyDetail(definition.FamilyId, definition.Detail));
            if (family.Actions.Any(action =>
                    action.CanOpen && !Preview11WizardScope.CoversCareerAction(action.ActionId)))
            {
                familyDetail = Preview11WizardScope.ContainsExperimentalRoutes(familyDetail);
            }
            _body.Add(NativeTheme.NavigationRow(
                WizardStrings.CareerFamilyTitle(definition.FamilyId, definition.Title),
                familyDetail,
                () => Navigation.PushAsync(new Sr5CareerActionFamilyPage(
                    Coordinator,
                    session,
                    _checkpointStore,
                    family.FamilyId)),
                automationId: definition.RouteId));
        }

        _body.Add(NativeTheme.Eyebrow(
            Sr5CareerFlowStrings.Text("After the run")));
        RunnerSessionSr5AfterRunSettlementPresenter afterRunPresenter = new(Coordinator);
        bool canOpenAfterRun = Sr5AfterRunSettlementEntryGuard.TryValidate(
            afterRunPresenter.Binding,
            out string afterRunBlocker);
        _afterRunEntryBlocker = canOpenAfterRun ? null : afterRunBlocker;
        _body.Add(NativeTheme.NavigationRow(
            Sr5CareerFlowStrings.Text("After the run"),
            Sr5CareerFlowStrings.Text(
                "Only governed proposal, run, and character IDs are selectable. This page never invents a run from the current character file."),
            () => RunAsync(OpenAfterRunSettlementAsync),
            enabled: canOpenAfterRun,
            automationId: "sr5-career-action-after-run"));
        if (!string.IsNullOrWhiteSpace(_afterRunEntryBlocker))
        {
            Label blocker = NativeTheme.Body(
                _afterRunEntryBlocker,
                NativeTheme.Danger);
            blocker.AutomationId = "sr5-career-after-run-unavailable";
            _body.Add(NativeTheme.Card(blocker));
        }

        _body.Add(NativeTheme.Eyebrow(
            WizardStrings.Get("Career.Commerce", "Commerce")));
        View commerceRoute = NativeTheme.NavigationRow(
            Sr5CareerFlowStrings.Text("Gear and implants"),
            Preview11WizardScope.MarkExperimental(
                Sr5CareerFlowStrings.Text(
                    "Source-bound Cyberware and custom-drug recipes → Core quote → durable receipt")),
            () => Navigation.PushAsync(new Sr5CareerCommerceHubPage(Coordinator)),
            enabled: canOpenCommerce,
            automationId: Sr5CareerRunCapabilityCatalog.CyberwareCommerceRoute);
        _body.Add(commerceRoute);
        if (!canOpenCommerce)
        {
            Label commerceBlocker = NativeTheme.Body(
                commerce.Blockers.FirstOrDefault()
                ?? customDrug.Blockers.FirstOrDefault()
                ?? Sr5CareerFlowStrings.Text(
                    "No typed Career commerce authority is available for this exact runner revision."),
                NativeTheme.Danger);
            commerceBlocker.AutomationId = "sr5-career-commerce-blocker";
            _body.Add(NativeTheme.Card(commerceBlocker));
        }

        Label boundary = NativeTheme.Body(
            WizardStrings.Get(
                "Career.NavigationBoundary",
                "This chooser can select and checkpoint navigation only. Review, confirmation, "
                + "persistence, recovery, and receipts remain owned by the selected typed flow."),
            NativeTheme.Muted);
        boundary.AutomationId = "sr5-career-wizard-navigation-boundary";
        _body.Add(NativeTheme.Card(boundary));
    }

    private async Task OpenAfterRunSettlementAsync()
    {
        RunnerSessionSr5AfterRunSettlementPresenter presenter = new(Coordinator);
        if (!Sr5AfterRunSettlementEntryGuard.TryValidate(
                presenter.Binding,
                out string blocker))
        {
            _afterRunEntryBlocker = blocker;
            return;
        }

        Sr5AfterRunSettlementCoordinator authority = new(
            presenter,
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        Sr5AfterRunSettlementEditorState editor;
        try
        {
            editor = await authority.PrepareAsync();
        }
        catch (InvalidOperationException)
        {
            if (Sr5AfterRunSettlementEntryGuard.TryValidate(
                    presenter.Binding,
                    out blocker))
            {
                throw;
            }

            _afterRunEntryBlocker = blocker;
            return;
        }

        _afterRunEntryBlocker = null;
        Page destination = Sr5AfterRunSettlementWizardPage
            .CreateEntryDestination(Coordinator, editor);
        await Navigation.PushAsync(destination);
    }

    internal static string LaneToken(Sr5CareerWizardLane lane)
        => lane switch
        {
            Sr5CareerWizardLane.Advancement => "advancement",
            Sr5CareerWizardLane.BeforeRun => "before-run",
            Sr5CareerWizardLane.LiveRun => "live-run",
            Sr5CareerWizardLane.AfterRun => "after-run",
            Sr5CareerWizardLane.Downtime => "downtime",
            Sr5CareerWizardLane.Corrections => "corrections",
            _ => throw new ArgumentOutOfRangeException(nameof(lane))
        };

    private void ScheduleReload()
    {
        if (_loading || _loadLifetime is null || _loadLifetime.IsCancellationRequested)
            return;
        _loading = true;
        CancellationToken token = _loadLifetime.Token;
        Dispatcher.Dispatch(async () => await LoadLatestAsync(token));
    }

    private async Task ReloadAsync()
    {
        _loadLifetime?.Cancel();
        _loadLifetime?.Dispose();
        _loadLifetime = new CancellationTokenSource();
        await Coordinator.InitializeAsync();
        await LoadLatestAsync(_loadLifetime.Token);
    }

    private async Task LoadLatestAsync(CancellationToken cancellationToken)
    {
        long version = Interlocked.Increment(ref _loadVersion);
        _loading = true;
        _loadBlocker = null;
        Refresh();
        try
        {
            Sr5CareerWizardPhoneLoadResult loaded =
                await _authority.LoadAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || version != Volatile.Read(ref _loadVersion))
            {
                return;
            }
            Sr5CareerWizardSnapshot? snapshot = loaded.Snapshot;
            if (!loaded.IsReady || snapshot is null)
            {
                _snapshot = null;
                _session = null;
                _loadBlocker = loaded.Blocker;
                return;
            }

            Sr5CareerWizardPhoneCheckpointRead checkpoint = _checkpointStore.Read();
            if (checkpoint.Status == Sr5CareerWizardPhoneCheckpointReadStatus.Unavailable)
            {
                _snapshot = null;
                _session = null;
                _loadBlocker = "career-wizard-checkpoint-store-unavailable";
                return;
            }
            _checkpointNotice = checkpoint.Status == Sr5CareerWizardPhoneCheckpointReadStatus.Invalid
                ? Sr5CareerWizardCheckpointInvalidationReasons.InvalidCheckpoint
                : null;

            var session = new Sr5CareerWizardDesktopSession();
            Sr5CareerWizardDesktopState state = session.Bind(
                snapshot,
                checkpoint.Status == Sr5CareerWizardPhoneCheckpointReadStatus.Ready
                    ? checkpoint.Checkpoint
                    : null);
            if (!state.Resume.Restored && state.Resume.InvalidationReason is not null)
            {
                _checkpointStore.Clear();
                _checkpointNotice = state.Resume.InvalidationReason;
            }
            else if (state.Resume.Restored)
            {
                _checkpointNotice = "career-wizard-checkpoint-restored";
            }
            _snapshot = snapshot;
            _session = session;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leaving the page cancels an in-flight read-only projection.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                _snapshot = null;
                _session = null;
                _loadBlocker = Sr5CareerWizardPhoneBlockers.WorkspaceAuthorityUnavailable;
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

    private void AddStatus(string text, string automationId, Color color)
    {
        Label label = NativeTheme.Body(text, color);
        label.AutomationId = automationId;
        _body.Add(NativeTheme.Card(label));
    }

    private static bool MatchesCurrentRunner(
        Sr5CareerWizardBinding binding,
        CharacterOverviewState state)
        => state.Profile?.Created == true
           && Sr5CareerWizardCatalog.IsSr5CareerRunner(true, state.Rules?.GameEdition)
           && state.WorkspaceId is { } workspaceId
           && string.Equals(binding.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
           && binding.WorkspaceRevision == state.ContentRevision
           && binding.SavedRevision == state.SavedRevision;

    private static string SafeBlockerMessage(string? blocker)
        => blocker switch
        {
            Sr5CareerWizardPhoneBlockers.WorkspaceChangedDuringProjection =>
                WizardStrings.Get(
                    "Career.Blocker.WorkspaceChanged",
                    "The runner changed while Career authorities were loading. Retry from the current revision."),
            "career-wizard-checkpoint-store-unavailable" =>
                WizardStrings.Get(
                    "Career.Blocker.CheckpointUnavailable",
                    "Durable navigation checkpoint storage is unavailable. No Career route can be opened safely."),
            _ => WizardStrings.Get(
                "Career.Blocker.AuthorityUnavailable",
                "Exact typed SR5 Career authority is unavailable for this runner.")
        };

    private static string CheckpointMessage(string notice)
        => notice switch
        {
            "career-wizard-checkpoint-restored" =>
                WizardStrings.Get(
                    "Career.Checkpoint.Restored",
                    "The last action selection was restored for this exact workspace snapshot."),
            Sr5CareerWizardCheckpointInvalidationReasons.WorkspaceChanged =>
                WizardStrings.Get(
                    "Career.Checkpoint.WorkspaceChanged",
                    "The saved Career selection belonged to another workspace and was discarded."),
            Sr5CareerWizardCheckpointInvalidationReasons.WorkspaceRevisionChanged =>
                WizardStrings.Get(
                    "Career.Checkpoint.RevisionChanged",
                    "The runner revision changed, so the saved Career selection was discarded."),
            Sr5CareerWizardCheckpointInvalidationReasons.SnapshotChanged =>
                WizardStrings.Get(
                    "Career.Checkpoint.SnapshotChanged",
                    "Runtime, sources, content, or action availability changed; the saved selection was discarded."),
            Sr5CareerWizardCheckpointInvalidationReasons.ActionUnavailable =>
                WizardStrings.Get(
                    "Career.Checkpoint.ActionUnavailable",
                    "The previously selected typed action is no longer available and was discarded."),
            _ => WizardStrings.Get(
                "Career.Checkpoint.Invalid",
                "An invalid Career navigation checkpoint was discarded.")
        };

    private static string ShortDigest(string value)
        => value.Length <= 19 ? value : value[..19] + "…";
}

public sealed class Sr5CareerActionFamilyPage : NativePageBase
{
    private readonly Sr5CareerWizardDesktopSession _session;
    private readonly Sr5CareerWizardPhoneCheckpointStore _checkpointStore;
    private readonly string _familyId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5CareerActionFamilyPage(
        RunnerSessionCoordinator coordinator,
        Sr5CareerWizardDesktopSession session,
        Sr5CareerWizardPhoneCheckpointStore checkpointStore,
        string familyId) : base(coordinator)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _familyId = Sr5CareerWizardPhoneCatalog.RequireFamily(familyId).FamilyId;
        Sr5CareerWizardPhoneFamilyDefinition definition =
            Sr5CareerWizardPhoneCatalog.RequireFamily(_familyId);
        Title = WizardStrings.CareerFamilyTitle(definition.FamilyId, definition.Title);
        AutomationId = definition.RouteId;
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        Sr5CareerWizardPhoneFamilyDefinition definition =
            Sr5CareerWizardPhoneCatalog.RequireFamily(_familyId);
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Career.PageTitle", "SR5 Career")));
        _body.Add(NativeTheme.Title(
            WizardStrings.CareerFamilyTitle(definition.FamilyId, definition.Title)));
        _body.Add(NativeTheme.Body(
            WizardStrings.CareerFamilyDetail(definition.FamilyId, definition.Detail),
            NativeTheme.Muted));

        Sr5CareerWizardDesktopState state = _session.State;
        if (!MatchesCurrentRunner(state.Snapshot.Binding))
        {
            _checkpointStore.Clear();
            Label blocker = NativeTheme.Body(
                WizardStrings.Get(
                    "Career.FamilyStale",
                    "The runner changed after this action family was projected. Return and reload Career authority."),
                NativeTheme.Danger);
            blocker.AutomationId = "sr5-career-family-stale";
            _body.Add(NativeTheme.Card(blocker));
            return;
        }

        Sr5CareerWizardFamilyState family = state.Snapshot.Families.Single(value =>
            string.Equals(value.FamilyId, _familyId, StringComparison.Ordinal));
        foreach (Sr5CareerWizardActionState action in family.Actions
                     .Where(static action => action.CanOpen))
        {
            Sr5CareerWizardPhoneActionDefinition route =
                Sr5CareerWizardPhoneCatalog.RequireAction(action.ActionId);
            string selected = string.Equals(
                state.SelectedActionId,
                action.ActionId,
                StringComparison.Ordinal)
                ? WizardStrings.Get("Career.Selected", "Selected · ")
                : string.Empty;
            string detail = selected + WizardStrings.CareerActionDetail(route.ActionId, route.Detail);
            if (!Preview11WizardScope.CoversCareerAction(action.ActionId))
                detail = Preview11WizardScope.MarkExperimental(detail);
            _body.Add(NativeTheme.NavigationRow(
                WizardStrings.CareerActionTitle(route.ActionId, route.Title),
                detail,
                () => RunAsync(() => OpenActionAsync(action.ActionId)),
                automationId: route.AutomationId));
        }

        Label boundary = NativeTheme.Body(
            WizardStrings.Get(
                "Career.FamilyBoundary",
                "Selecting a row writes only a digest-bound navigation checkpoint. The destination "
                + "must load and validate its own typed authority again."),
            NativeTheme.Muted);
        boundary.AutomationId = "sr5-career-family-navigation-boundary";
        _body.Add(NativeTheme.Card(boundary));
    }

    private async Task OpenActionAsync(string actionId)
    {
        if (!MatchesCurrentRunner(_session.State.Snapshot.Binding)
            || !_session.TrySelectAction(actionId))
        {
            _checkpointStore.Clear();
            await DisplayAlertAsync(
                WizardStrings.Get("Career.Alert.AuthorityChangedTitle", "Career authority changed"),
                WizardStrings.Get(
                    "Career.Alert.AuthorityChangedMessage",
                    "Return to the Career wizard and load the current runner revision."),
                WizardStrings.Get("Common.OK", "OK"));
            return;
        }
        if (!_checkpointStore.TryWrite(_session))
        {
            await DisplayAlertAsync(
                WizardStrings.Get("Career.Alert.CheckpointTitle", "Navigation checkpoint unavailable"),
                WizardStrings.Get(
                    "Career.Alert.CheckpointMessage",
                    "The typed Career route was not opened because its navigation checkpoint could not be saved and verified."),
                WizardStrings.Get("Common.OK", "OK"));
            return;
        }

        await OpenTypedDestinationAsync(actionId);
    }

    private Task OpenTypedDestinationAsync(string actionId)
        => actionId switch
        {
            Sr5CareerWizardActionIds.AdjustKarma => OpenManualKarmaAsync(),
            Sr5CareerWizardActionIds.AdjustNuyen => OpenManualNuyenAsync(),
            Sr5CareerWizardActionIds.EditKarmaExpense => OpenKarmaExpensesAsync(),
            Sr5CareerWizardActionIds.EditNuyenExpense => OpenNuyenExpensesAsync(),
            Sr5CareerWizardActionIds.AdvanceAttribute => OpenAttributeWizardAsync(),
            Sr5CareerWizardActionIds.AdvanceActiveSkill => OpenActiveSkillWizardAsync(),
            Sr5CareerWizardActionIds.AdvanceKnowledgeSkill => OpenKnowledgeSkillWizardAsync(),
            Sr5CareerWizardActionIds.AdvanceSkillGroup => OpenSkillGroupWizardAsync(),
            Sr5CareerWizardActionIds.LearnSpecialization => OpenSpecializationWizardAsync(),
            Sr5CareerWizardActionIds.ChangeQuality => OpenQualityWizardAsync(),
            Sr5CareerWizardActionIds.BeforeRun => Navigation.PushAsync(
                new Sr5TableWizardPage(Coordinator, Sr5TableWizardLane.BeforeRun)),
            Sr5CareerWizardActionIds.Playtime => Navigation.PushAsync(
                new Sr5TableWizardPage(Coordinator, Sr5TableWizardLane.Playtime)),
            Sr5CareerWizardActionIds.ManageCalendarEntry => OpenCalendarAsync(),
            _ => throw new InvalidOperationException("Unknown SR5 Career destination.")
        };

    private bool MatchesCurrentRunner(Sr5CareerWizardBinding binding)
        => Coordinator.State.Profile?.Created == true
           && Sr5CareerWizardCatalog.IsSr5CareerRunner(true, Coordinator.State.Rules?.GameEdition)
           && Coordinator.State.WorkspaceId is { } workspaceId
           && string.Equals(binding.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
           && binding.WorkspaceRevision == Coordinator.State.ContentRevision
           && binding.SavedRevision == Coordinator.State.SavedRevision;

    private async Task OpenActiveSkillWizardAsync()
    {
        Sr5CareerActiveSkillCoordinator authority = new(
            new RunnerSessionSr5CareerActiveSkillPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerActiveSkillAdvanceEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
            await Navigation.PushAsync(new Sr5CareerActiveSkillWizardPage(Coordinator, editor));
    }

    private async Task OpenAttributeWizardAsync()
    {
        Sr5CareerAttributeCoordinator authority = new(
            new RunnerSessionSr5CareerAttributePresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerAttributeAdvanceEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
            await Navigation.PushAsync(new Sr5CareerAttributeWizardPage(Coordinator, editor));
    }

    private async Task OpenQualityWizardAsync()
    {
        Sr5CareerQualityCoordinator authority = new(
            new RunnerSessionSr5CareerQualityPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerQualityEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new Sr5CareerQualityWizardPage(Coordinator, editor));
            return;
        }
        await DisplayAlertAsync(
            WizardStrings.Get("Career.Alert.QualityTitle", "Quality authority unavailable"),
            WizardStrings.Get(
                "Career.Alert.QualityMessage",
                "Exact atomic SR5 quality authority is no longer connected. No fallback mutation is available."),
            WizardStrings.Get("Common.OK", "OK"));
    }

    private async Task OpenKnowledgeSkillWizardAsync()
    {
        Sr5CareerKnowledgeSkillCoordinator authority = new(
            new RunnerSessionSr5CareerKnowledgeSkillPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerKnowledgeSkillAdvanceEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
            await Navigation.PushAsync(new Sr5CareerKnowledgeSkillWizardPage(Coordinator, editor));
    }

    private async Task OpenSkillGroupWizardAsync()
    {
        Sr5CareerSkillGroupCoordinator authority = new(
            new RunnerSessionSr5CareerSkillGroupPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerSkillGroupAdvanceEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
            await Navigation.PushAsync(new Sr5CareerSkillGroupWizardPage(Coordinator, editor));
    }

    private async Task OpenSpecializationWizardAsync()
    {
        Sr5CareerSpecializationCoordinator authority = new(
            new RunnerSessionSr5CareerSpecializationPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerSkillSpecializationEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
            await Navigation.PushAsync(new Sr5CareerSpecializationWizardPage(Coordinator, editor));
    }

    private async Task OpenManualKarmaAsync()
    {
        CareerManualKarmaEditorState? editor = await Coordinator.PrepareCareerManualKarmaEditAsync();
        if (editor is not null)
            await Navigation.PushAsync(new CareerManualKarmaPage(Coordinator, editor));
    }

    private async Task OpenManualNuyenAsync()
    {
        CareerManualNuyenEditorState? editor = await Coordinator.PrepareCareerManualNuyenEditAsync();
        if (editor is not null)
            await Navigation.PushAsync(new CareerManualNuyenPage(Coordinator, editor));
    }

    private async Task OpenCalendarAsync()
    {
        await Navigation.PushAsync(new Sr5DowntimeCalendarWizardPage(Coordinator));
    }

    private async Task OpenKarmaExpensesAsync()
    {
        CareerKarmaExpenseEditorState? editor = await Coordinator.PrepareCareerKarmaExpenseEditAsync();
        if (editor is not null)
            await Navigation.PushAsync(new CareerKarmaExpensePage(Coordinator, editor));
    }

    private async Task OpenNuyenExpensesAsync()
    {
        CareerNuyenExpenseEditorState? editor = await Coordinator.PrepareCareerNuyenExpenseEditAsync();
        if (editor is not null)
            await Navigation.PushAsync(new CareerNuyenExpensePage(Coordinator, editor));
    }
}
