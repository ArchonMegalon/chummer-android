using System.Globalization;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Layouts;

namespace Chummer.Android.Native;

public sealed record BuildPageRouteMarker(string AutomationId, string Label);

public sealed record CreationIdentityRouteState(bool IsEnabled, string Blocker);

public sealed record CreationDashboardRouteReadyMarker(
    string Schema,
    string RouteAutomationId,
    string DashboardAutomationId,
    string WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string ContentDigest,
    string SourceDigest,
    string RuntimeFingerprint,
    string BuildMethod,
    string SnapshotDigest,
    bool CharacterCreated,
    bool AuthorityReady);

public sealed record CreationDashboardProjectionBinding(
    string WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string ContentDigest,
    string SourceDigest,
    string RuntimeFingerprint,
    string BuildMethod,
    string SnapshotDigest)
{
    public static bool TryCreate(
        CharacterOverviewState state,
        CharacterCreationWizardSnapshot snapshot,
        out CreationDashboardProjectionBinding? binding)
    {
        binding = null;
        if (state.Profile?.Created != false
            || state.WorkspaceId is not { } workspaceId
            || state.ContentRevision <= 0
            || snapshot.WorkspaceRevision != state.ContentRevision
            || !string.Equals(snapshot.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(snapshot.ContentDigest)
            || !IsBootstrapAuthorityBindingValue(snapshot.SourceDigest)
            || !IsBootstrapAuthorityBindingValue(snapshot.RuntimeFingerprint)
            || string.IsNullOrWhiteSpace(snapshot.BuildMethod)
            || string.IsNullOrWhiteSpace(snapshot.SnapshotDigest))
        {
            return false;
        }

        // Source/runtime authority is intentionally absent from the initial presentation snapshot.
        // This binding only schedules the Core prerequisite load that obtains and validates that
        // authority; downstream acceptance remains revision-, content-, and digest-bound.
        binding = new CreationDashboardProjectionBinding(
            workspaceId.Value,
            state.ContentRevision,
            state.SavedRevision,
            snapshot.ContentDigest,
            snapshot.SourceDigest,
            snapshot.RuntimeFingerprint,
            snapshot.BuildMethod,
            snapshot.SnapshotDigest);
        return true;
    }

    private static bool IsBootstrapAuthorityBindingValue(string? value)
        => value is not null
           && (value.Length == 0 || !string.IsNullOrWhiteSpace(value));

    public bool Matches(
        CharacterOverviewState state,
        CharacterCreationWizardSnapshot snapshot)
        => TryCreate(state, snapshot, out CreationDashboardProjectionBinding? current)
           && Equals(current);
}

public enum CreationDashboardAuthorityPhaseState
{
    NotApplicable,
    Loading,
    Ready,
    Failed
}

public enum CreationDashboardAuthorityPhase
{
    Prerequisite,
    Attributes,
    Skills,
    Contacts,
    Resources
}

public sealed record CreationDashboardAuthorityPhaseProgress(
    CreationDashboardAuthorityPhaseState Prerequisite,
    CreationDashboardAuthorityPhaseState Attributes,
    CreationDashboardAuthorityPhaseState Skills,
    CreationDashboardAuthorityPhaseState Contacts,
    CreationDashboardAuthorityPhaseState Resources)
{
    public bool IsTerminalReady
        => IsReadyOrNotApplicable(Prerequisite)
           && IsReadyOrNotApplicable(Attributes)
           && IsReadyOrNotApplicable(Skills)
           && IsReadyOrNotApplicable(Contacts)
           && IsReadyOrNotApplicable(Resources);

    public static CreationDashboardAuthorityPhaseProgress ForBuildMethod(string buildMethod)
    {
        bool priorityOrSumToTen = buildMethod is (CharacterCreationBuildMethods.Priority
            or CharacterCreationBuildMethods.SumToTen);
        return new(
            priorityOrSumToTen
                ? CreationDashboardAuthorityPhaseState.Loading
                : CreationDashboardAuthorityPhaseState.NotApplicable,
            priorityOrSumToTen
                ? CreationDashboardAuthorityPhaseState.Loading
                : CreationDashboardAuthorityPhaseState.NotApplicable,
            string.Equals(buildMethod, CharacterCreationBuildMethods.Priority, StringComparison.Ordinal)
                ? CreationDashboardAuthorityPhaseState.Loading
                : CreationDashboardAuthorityPhaseState.NotApplicable,
            CreationDashboardAuthorityPhaseState.Loading,
            priorityOrSumToTen
                ? CreationDashboardAuthorityPhaseState.Loading
                : CreationDashboardAuthorityPhaseState.NotApplicable);
    }

    public CreationDashboardAuthorityPhaseProgress WithTerminal(
        CreationDashboardAuthorityPhase phase,
        bool failed)
    {
        CreationDashboardAuthorityPhaseState terminal = failed
            ? CreationDashboardAuthorityPhaseState.Failed
            : CreationDashboardAuthorityPhaseState.Ready;
        return phase switch
        {
            CreationDashboardAuthorityPhase.Prerequisite => this with { Prerequisite = terminal },
            CreationDashboardAuthorityPhase.Attributes => this with { Attributes = terminal },
            CreationDashboardAuthorityPhase.Skills => this with { Skills = terminal },
            CreationDashboardAuthorityPhase.Contacts => this with { Contacts = terminal },
            CreationDashboardAuthorityPhase.Resources => this with { Resources = terminal },
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
        };
    }

    private static bool IsReadyOrNotApplicable(CreationDashboardAuthorityPhaseState state)
        => state is CreationDashboardAuthorityPhaseState.Ready
            or CreationDashboardAuthorityPhaseState.NotApplicable;
}

public sealed record CreationDashboardAuthorityProjection(
    CreationDashboardProjectionBinding Binding,
    CreationDashboardAuthorityPhaseProgress Progress,
    CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? Prerequisite,
    CharacterCreationFoundationResult<CharacterCreationAttributesState>? Attributes,
    CharacterCreationFoundationResult<CharacterCreationSkillsState>? Skills,
    CharacterCreationContactsInteractionLoadResult? Contacts,
    CharacterCreationResourcesInteractionLoadResult? Resources,
    string? PrerequisiteFailureReason = null,
    string? AttributesFailureReason = null,
    string? SkillsFailureReason = null,
    string? ContactsFailureReason = null,
    string? ResourcesFailureReason = null)
{
    public static CreationDashboardAuthorityProjection Loading(
        CreationDashboardProjectionBinding binding)
        => new(
            binding,
            CreationDashboardAuthorityPhaseProgress.ForBuildMethod(binding.BuildMethod),
            Prerequisite: null,
            Attributes: null,
            Skills: null,
            Contacts: null,
            Resources: null);

    public bool HasFailure
        => Progress.Prerequisite == CreationDashboardAuthorityPhaseState.Failed
           || Progress.Attributes == CreationDashboardAuthorityPhaseState.Failed
           || Progress.Skills == CreationDashboardAuthorityPhaseState.Failed
           || Progress.Contacts == CreationDashboardAuthorityPhaseState.Failed
           || Progress.Resources == CreationDashboardAuthorityPhaseState.Failed;
}

public static class BuildPageUiProjection
{
    public const string CreationIdentityDraftContractUnavailable =
        "creation-identity-draft-contract-unavailable";
    public const string CreationKarmaAuthorityRequired =
        "creation-karma-authority-required";

    public static BuildPageRouteMarker RouteMarker(CharacterProfileSection? profile)
        => profile switch
        {
            null => new("phone-runner-empty", "No runner loaded"),
            { Created: false } => new("phone-runner-create", "Creation runner"),
            _ => new("phone-runner-sheet", "Career runner")
        };

    public static string SaveToolbarText(bool hasDurableSaveNotice)
        => hasDurableSaveNotice ? "Saved." : "Save";

    /// <summary>
    /// Lets an exact, revision-bound typed domain projection rehydrate a Creation route whose
    /// generic wizard snapshot still carries the conservative legal-options placeholder.  The
    /// placeholder is not a competing domain authority: Attributes, Skills, Contacts, and
    /// Resources are opened only by their dedicated typed projections.  No stage becomes
    /// available from the generic snapshot alone.
    /// </summary>
    public static bool CanOpenExactTypedCreationStage(
        CharacterCreationWizardStageState stage,
        bool exactTypedAuthorityReady)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (!exactTypedAuthorityReady
            || stage.StepId is not (CharacterCreationWizardStepIds.Attributes
                or CharacterCreationWizardStepIds.Skills
                or CharacterCreationWizardStepIds.ContactsLifestyles
                or CharacterCreationWizardStepIds.Resources))
        {
            return false;
        }

        return stage.IsAvailable
            ? stage.Blockers.Count == 0
            : stage.Blockers.Count == 1
              && string.Equals(
                  stage.Blockers[0],
                  "creation-wizard-legal-options-authority-unavailable",
                  StringComparison.Ordinal);
    }

    public static bool HasExactTypedResourcesAuthority(
        CharacterCreationResourcesInteractionLoadResult? result,
        CharacterOverviewState overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        return result is
               {
                   Outcome: CharacterCreationResourcesOutcomes.Available,
                   State: { } state
               }
               && CreationResourcesPhoneAuthority.IsReady(state, overview);
    }

    public static string CreationResourcesStageBlocker(
        CreationDashboardAuthorityProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return projection.Progress.Resources switch
        {
            CreationDashboardAuthorityPhaseState.Loading => "creation-authority-loading",
            CreationDashboardAuthorityPhaseState.Failed => projection.ResourcesFailureReason
                ?? "creation-resources-authority-load-failed",
            _ => FirstBlocker(
                     projection.Resources?.Blockers,
                     projection.Resources?.State?.Blockers,
                     projection.Resources?.State?.Budget.Blockers)
                 ?? "creation-resources-authority-unavailable"
        };
    }

    private static string? FirstBlocker(params IEnumerable<string>?[] candidates)
        => candidates
            .Where(static candidate => candidate is not null)
            .SelectMany(static candidate => candidate!)
            .FirstOrDefault(static blocker => !string.IsNullOrWhiteSpace(blocker));

    public static CreationDashboardRouteReadyMarker? CreationDashboardRouteReady(
        CharacterOverviewState state,
        CharacterCreationWizardSnapshot snapshot,
        CreationDashboardAuthorityProjection? projection)
    {
        if (state.Profile?.Created != false
            || projection is null
            || projection.HasFailure
            || !projection.Progress.IsTerminalReady
            || !projection.Binding.Matches(state, snapshot))
        {
            return null;
        }

        CreationDashboardProjectionBinding binding = projection.Binding;
        return new CreationDashboardRouteReadyMarker(
            "chummer.android.creation-dashboard-route-ready/v1",
            "phone-runner-create",
            "creation-wizard-dashboard",
            binding.WorkspaceId,
            binding.ContentRevision,
            binding.SavedRevision,
            binding.ContentDigest,
            binding.SourceDigest,
            binding.RuntimeFingerprint,
            binding.BuildMethod,
            binding.SnapshotDigest,
            CharacterCreated: false,
            AuthorityReady: true);
    }

    public static CreationIdentityRouteState CreationIdentityRoute(
        IReadOnlyList<string> coreBlockers)
        => new(
            IsEnabled: false,
            Blocker: coreBlockers.FirstOrDefault(static blocker => !string.IsNullOrWhiteSpace(blocker))
                     ?? CreationIdentityDraftContractUnavailable);

    /// <summary>
    /// Consumes a terminal outcome that can no longer be applied to the page's
    /// current binding.  Returning true tells the UI boundary to refresh so it
    /// can cancel the stale projection and request authority for the current
    /// workspace revision.  Without that refresh, a completed stale outcome
    /// can leave the page displaying its fail-closed loading state forever.
    /// </summary>
    public static bool ConsumeRejectedCreationPhaseForRefresh<TResult>(
        LatestBackgroundProjectionQueue<CreationDashboardProjectionBinding, TResult> queue,
        BackgroundProjectionRequest<CreationDashboardProjectionBinding> request)
        => queue.TryAccept(request);
}

public sealed class BuildPage : NativePageBase
{
    private const string CreationDashboardRouteReadyLogTag = "ChummerRoute";
    private const string CreationDashboardRouteReadyLogPrefix =
        "CHUMMER_CREATION_DASHBOARD_READY ";
    private static readonly JsonSerializerOptions CreationDashboardRouteReadyJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly TimeSpan CreationDashboardRouteReadySettleDelay =
        TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan CreationDashboardRouteReadyPollDelay =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CreationDashboardRouteReadyMaximumWait =
        TimeSpan.FromSeconds(25);
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 16, 20, 40),
        Spacing = 16
    };
    private readonly ContentView _routeMarkerHost = new()
    {
        Padding = new Thickness(20, 18, 20, 0)
    };
    private readonly ToolbarItem _save;
    private readonly LatestBackgroundProjectionQueue<
        CreationDashboardProjectionBinding,
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>> _creationPrerequisiteQueue = new();
    private readonly LatestBackgroundProjectionQueue<
        CreationDashboardProjectionBinding,
        CharacterCreationFoundationResult<CharacterCreationAttributesState>> _creationAttributesQueue = new();
    private readonly LatestBackgroundProjectionQueue<
        CreationDashboardProjectionBinding,
        CharacterCreationFoundationResult<CharacterCreationSkillsState>> _creationSkillsQueue = new();
    private readonly LatestBackgroundProjectionQueue<
        CreationDashboardProjectionBinding,
        CharacterCreationContactsInteractionLoadResult> _creationContactsQueue = new();
    private readonly LatestBackgroundProjectionQueue<
        CreationDashboardProjectionBinding,
        CharacterCreationResourcesInteractionLoadResult> _creationResourcesQueue = new();
    private readonly LatestBackgroundProjectionQueue<
        CreationDashboardProjectionBinding,
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState>> _creationFinalizationQueue = new();
    private readonly ICharacterCreationResourcesInteractionPresenter? _resourcesPresenter;
    private readonly ICharacterCreationGearInteractionPresenter? _gearPresenter;
    private readonly ICharacterOverviewPresenter? _overviewPresenter;
    private CreationDashboardAuthorityProjection? _creationProjection;
    private CreationDashboardProjectionBinding? _creationFinalizationBinding;
    private CharacterCreationFinalizationResult<CharacterCreationFinalizationState>?
        _creationFinalizationAuthority;
    private string? _creationFinalizationFailureReason;
    private CancellationTokenSource? _creationDashboardRouteReadyLifetime;
    private long _creationDashboardAppearanceGeneration;
    private long _creationDashboardRouteReadyEmittedGeneration = -1;

    public BuildPage(
        RunnerSessionCoordinator coordinator,
        ICharacterCreationResourcesInteractionPresenter? resourcesPresenter = null,
        ICharacterOverviewPresenter? overviewPresenter = null,
        ICharacterCreationGearInteractionPresenter? gearPresenter = null) : base(coordinator)
    {
        _resourcesPresenter = resourcesPresenter;
        _overviewPresenter = overviewPresenter;
        _gearPresenter = gearPresenter;
        Title = "Runner";
        AutomationId = "phone-runner-page";
        _save = new ToolbarItem
        {
            Text = "Save",
            AutomationId = "build-save-runner",
            Command = new Command(async () => await RunAsync(() => Coordinator.SaveAsync()))
        };
        ToolbarItems.Add(_save);
        _creationPrerequisiteQueue.Completed += completion => ScheduleCreationPhaseAcceptance(
            _creationPrerequisiteQueue,
            completion.Request,
            CreationDashboardAuthorityPhase.Prerequisite,
            AcceptCreationPrerequisite);
        _creationPrerequisiteQueue.Failed += failure => ScheduleCreationPhaseAcceptance(
            _creationPrerequisiteQueue,
            failure.Request,
            CreationDashboardAuthorityPhase.Prerequisite,
            AcceptCreationPrerequisite);
        _creationAttributesQueue.Completed += completion => ScheduleCreationPhaseAcceptance(
            _creationAttributesQueue,
            completion.Request,
            CreationDashboardAuthorityPhase.Attributes,
            AcceptCreationAttributes);
        _creationAttributesQueue.Failed += failure => ScheduleCreationPhaseAcceptance(
            _creationAttributesQueue,
            failure.Request,
            CreationDashboardAuthorityPhase.Attributes,
            AcceptCreationAttributes);
        _creationSkillsQueue.Completed += completion => ScheduleCreationPhaseAcceptance(
            _creationSkillsQueue,
            completion.Request,
            CreationDashboardAuthorityPhase.Skills,
            AcceptCreationSkills);
        _creationSkillsQueue.Failed += failure => ScheduleCreationPhaseAcceptance(
            _creationSkillsQueue,
            failure.Request,
            CreationDashboardAuthorityPhase.Skills,
            AcceptCreationSkills);
        _creationContactsQueue.Completed += completion => ScheduleCreationPhaseAcceptance(
            _creationContactsQueue,
            completion.Request,
            CreationDashboardAuthorityPhase.Contacts,
            AcceptCreationContacts);
        _creationContactsQueue.Failed += failure => ScheduleCreationPhaseAcceptance(
            _creationContactsQueue,
            failure.Request,
            CreationDashboardAuthorityPhase.Contacts,
            AcceptCreationContacts);
        _creationResourcesQueue.Completed += completion => ScheduleCreationPhaseAcceptance(
            _creationResourcesQueue,
            completion.Request,
            CreationDashboardAuthorityPhase.Resources,
            AcceptCreationResources);
        _creationResourcesQueue.Failed += failure => ScheduleCreationPhaseAcceptance(
            _creationResourcesQueue,
            failure.Request,
            CreationDashboardAuthorityPhase.Resources,
            AcceptCreationResources);
        _creationFinalizationQueue.Completed += completion =>
            ScheduleCreationFinalizationAcceptance(completion.Request);
        _creationFinalizationQueue.Failed += failure =>
            ScheduleCreationFinalizationAcceptance(failure.Request);
        ScrollView bodyScroll = new() { Content = _body };
        Grid page = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        page.Add(_routeMarkerHost);
        page.Add(bodyScroll);
        Grid.SetRow(bodyScroll, 1);
        Content = page;
    }

    protected override void OnAppearing()
    {
        _creationDashboardRouteReadyLifetime?.Cancel();
        _creationDashboardRouteReadyLifetime?.Dispose();
        _creationDashboardRouteReadyLifetime = new CancellationTokenSource();
        _creationDashboardAppearanceGeneration++;
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _creationDashboardRouteReadyLifetime?.Cancel();
        _creationDashboardRouteReadyLifetime?.Dispose();
        _creationDashboardRouteReadyLifetime = null;
        _creationDashboardAppearanceGeneration++;
        CancelCreationProjectionQueues();
        // Child creation pages persist auxiliary drafts without necessarily changing the
        // character XML revision represented by CharacterOverviewState.  A completed typed
        // projection can therefore be revision-current yet auxiliary-state-stale when this
        // page reappears.  Never retain those terminal results across a route boundary: the
        // next appearance must reload every typed authority from Core's current auxiliary
        // state.  The individual presenters keep the strict revision and digest checks.
        _creationProjection = null;
        base.OnDisappearing();
    }

    protected override void Refresh()
    {
        _body.Clear();
        _routeMarkerHost.Content = null;
        _save.Text = BuildPageUiProjection.SaveToolbarText(Coordinator.HasDurableSaveNotice);
        _save.IsEnabled = Coordinator.State.Profile is not null;
        BuildPageRouteMarker routeMarker = BuildPageUiProjection.RouteMarker(Coordinator.State.Profile);
        AddRouteMarker(routeMarker.AutomationId, routeMarker.Label);
        if (Coordinator.State.Profile is null)
        {
            Title = "Runner";
            _body.Add(NativeTheme.Title("Open a runner first"));
            _body.Add(NativeTheme.Body("Your file stays on this device unless you choose to link it.", NativeTheme.Muted));
            Button open = NativeTheme.PrimaryButton("Open file");
            open.Clicked += async (_, _) => await RunAsync(() => Coordinator.OpenLocalAsync());
            _body.Add(open);
            return;
        }

        if (Coordinator.State.Profile.Created == false)
        {
            Title = "Create";
            AddWorkspacePicker();
            AddCreationWizardDashboard();
            AddFeedback();
            return;
        }

        Title = "Sheet";
        AddWorkspacePicker();
        if (Sr5CareerWizardCatalog.IsSr5CareerRunner(
                Coordinator.State.Profile.Created,
                Coordinator.State.Rules?.GameEdition))
        {
            AddSr5CareerWizardRoute();
        }
        AddSummary();
        AddDossier();
        AddBuildAreas();

        AddFeedback();
    }

    private void AddRouteMarker(string automationId, string label)
    {
        Label marker = NativeTheme.Eyebrow(label);
        marker.AutomationId = automationId;
        CharacterCreationFinalizationReceipt? persistedReceipt =
            Coordinator.LoadPersistedPriorityCreationReceipt();
        _routeMarkerHost.Content = persistedReceipt is null
            ? marker
            : NativeAuthoritySemantics.Overlay(
                marker,
                NativeAuthoritySemantics.Digest(
                    "phone-workspace-creation-receipt-digest",
                    persistedReceipt.ReceiptDigest));
    }

    private void AddSr5CareerWizardRoute()
    {
        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow("Shadowrun Fifth Edition"));
        card.Add(NativeTheme.Title("Career", 23));
        card.Add(NativeTheme.Body(
            "Use player-intent journeys and exact review/apply receipts for this created SR5 runner.",
            NativeTheme.Muted));
        card.Add(NativeTheme.NavigationRow(
            "Open Career wizard",
            "Advance · Before Run · Live · After Run · Downtime · Corrections",
            () => Navigation.PushAsync(new Sr5CareerWizardPage(Coordinator)),
            automationId: "build-sr5-career-wizard"));
        card.Add(NativeTheme.NavigationRow(
            "Change a quality",
            "Direct deep link · exact InternalId/SourceId → atomic review → receipt/correction",
            OpenSr5CareerQualityWizardAsync,
            automationId: "build-career-quality"));
        card.Add(NativeTheme.NavigationRow(
            "Advance a skill group",
            "Direct deep link · exact InternalId → Core-bound review → atomic receipt/recovery",
            OpenSr5CareerSkillGroupWizardAsync,
            automationId: "build-career-skill-group"));
        card.Add(NativeTheme.NavigationRow(
            "Add a specialization",
            "Direct deep link · typed skill identity → governed/custom choice → four-revision review",
            OpenSr5CareerSpecializationWizardAsync,
            automationId: "build-career-specialization"));
        card.Add(NativeTheme.NavigationRow(
            Sr5CareerFlowStrings.Text("Cyberware purchase"),
            Sr5CareerFlowStrings.Text("Source-bound catalog → configuration → Core quote → durable receipt"),
            () => Navigation.PushAsync(new Sr5CareerCommerceHubPage(Coordinator)),
            automationId: "build-career-commerce"));
        Border route = NativeTheme.Card(card);
        route.AutomationId = Sr5CareerWizardRoutes.Hub;
        _body.Add(route);
    }

    private async Task OpenSr5CareerQualityWizardAsync()
    {
        Sr5CareerQualityCoordinator authority = new(
            new RunnerSessionSr5CareerQualityPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerQualityEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new Sr5CareerQualityWizardPage(Coordinator, editor));
        }
        else
        {
            await DisplayAlertAsync(
                "Quality authority unavailable",
                "This build does not have a complete atomic SR5 quality workspace. No fallback mutation is available.",
                "OK");
        }
    }

    private async Task OpenSr5CareerSkillGroupWizardAsync()
    {
        Sr5CareerSkillGroupCoordinator authority = new(
            new RunnerSessionSr5CareerSkillGroupPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerSkillGroupAdvanceEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new Sr5CareerSkillGroupWizardPage(Coordinator, editor));
        }
    }

    private async Task OpenSr5CareerSpecializationWizardAsync()
    {
        Sr5CareerSpecializationCoordinator authority = new(
            new RunnerSessionSr5CareerSpecializationPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        CareerSkillSpecializationEditorState? editor = await authority.PrepareAsync();
        if (editor is not null)
        {
            await Navigation.PushAsync(new Sr5CareerSpecializationWizardPage(Coordinator, editor));
        }
    }

    private void AddFeedback()
    {
        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error ?? Coordinator.Surface.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error ?? Coordinator.Surface.Error!, NativeTheme.Danger));
        }
        else if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice!, NativeTheme.Muted));
        }
    }

    private void AddCreationWizardDashboard()
    {
        CharacterCreationWizardSnapshot? snapshot = Coordinator.State.CreationWizard;
        VerticalStackLayout header = new()
        {
            AutomationId = "creation-wizard-dashboard",
            Spacing = 8
        };
        header.Add(NativeTheme.Eyebrow("Character creation"));
        header.Add(NativeTheme.Title(
            Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "New runner"));
        header.Add(NativeTheme.Body(
            "Build this runner step by step. The full character editor unlocks after creation is complete.",
            NativeTheme.Muted));
        long appearanceGeneration = _creationDashboardAppearanceGeneration;
        if (snapshot is not null)
        {
            header.Loaded += (_, _) => ScheduleCreationDashboardRouteReady(
                header,
                snapshot,
                appearanceGeneration);
        }
        _body.Add(header);
        if (snapshot is not null)
        {
            ScheduleCreationDashboardRouteReady(
                header,
                snapshot,
                appearanceGeneration);
        }

        if (snapshot is null)
        {
            Label unavailable = NativeTheme.Body(
                "The authoritative creation projection is unavailable. The wizard is fail-closed and will not "
                + "expose the unrestricted editor or invent rules data.",
                NativeTheme.Danger);
            unavailable.AutomationId = "creation-wizard-snapshot-unavailable";
            _body.Add(NativeTheme.Card(unavailable));
            return;
        }

        Label binding = NativeTheme.Body(
            $"Revision {snapshot.WorkspaceRevision} · snapshot {ShortDigest(snapshot.SnapshotDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-wizard-binding";
        _body.Add(binding);

        CharacterCreationWizardStageState? lifeModuleStage = snapshot.Steps.FirstOrDefault(candidate =>
            string.Equals(
                candidate.StepId,
                CharacterCreationWizardStepIds.LifeModules,
                StringComparison.Ordinal));
        if (string.Equals(snapshot.BuildMethod, CharacterCreationBuildMethods.LifeModules, StringComparison.Ordinal)
            && (lifeModuleStage is null
                || !lifeModuleStage.IsAvailable
                || lifeModuleStage.Blockers.Count > 0))
        {
            Label blocked = NativeTheme.Body(
                "Life Modules are blocked by the current authoritative projection. Chummer will not substitute or claim "
                + "the Karma workflow. "
                + (lifeModuleStage?.Blockers.FirstOrDefault() ?? "No typed Life Module authority is available."),
                NativeTheme.Danger);
            blocked.AutomationId = "creation-wizard-life-modules-blocked";
            _body.Add(NativeTheme.Card(blocked));
        }

        CreationDashboardAuthorityProjection? projection = ResolveCreationProjection(snapshot);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? prerequisite =
            projection?.Prerequisite;
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributes =
            projection?.Attributes;
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skills =
            projection?.Skills;
        CharacterCreationContactsInteractionLoadResult? creationContacts =
            projection?.Contacts;
        CharacterCreationResourcesInteractionLoadResult? creationResources =
            projection?.Resources;
        AddCreationMethodRoute(snapshot, projection, prerequisite);
        if (projection is null
            || projection.Progress.Prerequisite == CreationDashboardAuthorityPhaseState.Loading)
        {
            Label loading = NativeTheme.Body(
                "Loading the revision- and source-bound creation authority. Editing remains fail-closed until it is ready.",
                NativeTheme.Muted);
            loading.AutomationId = "creation-dashboard-authority-loading";
            _body.Add(NativeTheme.Card(loading));
        }
        else if (projection.HasFailure)
        {
            VerticalStackLayout failure = new() { Spacing = 8 };
            Label failed = NativeTheme.Body(
                projection.Progress.Prerequisite == CreationDashboardAuthorityPhaseState.Failed
                    ? "The prerequisite creation authority could not be loaded. No rules data was inferred; use Retry authority or reopen this runner."
                    : "A later creation authority phase could not be loaded. Ready phases remain usable; the affected stage stays fail-closed until Retry authority succeeds.",
                NativeTheme.Danger);
            failed.AutomationId = projection.Progress.Prerequisite == CreationDashboardAuthorityPhaseState.Failed
                ? "creation-dashboard-authority-failed"
                : "creation-dashboard-authority-partial-failed";
            failure.Add(failed);
            Button retry = NativeTheme.SecondaryButton("Retry authority");
            retry.AutomationId = "creation-dashboard-authority-retry";
            retry.Clicked += (_, _) => RetryCreationProjection();
            failure.Add(retry);
            _body.Add(NativeTheme.Card(failure));
        }
        AddBudgetRibbon(snapshot, attributes, skills);
        AddWizardStages(
            snapshot,
            projection,
            prerequisite,
            attributes,
            skills,
            creationContacts,
            creationResources);
        AddCompletionBlockers(snapshot);
        AddLegalNextSteps(
            snapshot,
            projection,
            prerequisite,
            attributes,
            skills,
            creationContacts,
            creationResources);
        AddFinalizationReviewAction(snapshot);
    }

    private void ScheduleCreationDashboardRouteReady(
        VisualElement header,
        CharacterCreationWizardSnapshot capturedSnapshot,
        long appearanceGeneration)
    {
        CancellationTokenSource? lifetime = _creationDashboardRouteReadyLifetime;
        if (lifetime is null)
            return;

        _ = EmitCreationDashboardRouteReadyAsync(
            header,
            capturedSnapshot,
            appearanceGeneration,
            lifetime.Token);
    }

    private async Task EmitCreationDashboardRouteReadyAsync(
        VisualElement header,
        CharacterCreationWizardSnapshot capturedSnapshot,
        long appearanceGeneration,
        CancellationToken cancellationToken)
    {
        try
        {
            long waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            await Task.Delay(CreationDashboardRouteReadySettleDelay, cancellationToken);
            while (System.Diagnostics.Stopwatch.GetElapsedTime(waitStarted)
                   < CreationDashboardRouteReadyMaximumWait)
            {
                bool terminal = false;
                bool emitted = false;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested
                        || appearanceGeneration != _creationDashboardAppearanceGeneration
                        || _creationDashboardRouteReadyEmittedGeneration == appearanceGeneration
                        || !_body.Children.Contains(header)
                        || Coordinator.State.CreationWizard is not { } currentSnapshot
                        || !string.Equals(
                            currentSnapshot.SnapshotDigest,
                            capturedSnapshot.SnapshotDigest,
                            StringComparison.Ordinal))
                    {
                        terminal = true;
                        return;
                    }

                    if (header.Handler is null
                        || !header.IsVisible
                        || header.Width <= 0
                        || header.Height <= 0
                        || !ReferenceEquals(Shell.Current?.CurrentPage, this))
                    {
                        return;
                    }

                    CreationDashboardRouteReadyMarker? marker =
                        BuildPageUiProjection.CreationDashboardRouteReady(
                            Coordinator.State,
                            currentSnapshot,
                            _creationProjection);
                    if (marker is null)
                        return;

                    _creationDashboardRouteReadyEmittedGeneration = appearanceGeneration;
                    string payload = JsonSerializer.Serialize(
                        marker,
                        CreationDashboardRouteReadyJson);
#if ANDROID
                    global::Android.Util.Log.Info(
                        CreationDashboardRouteReadyLogTag,
                        CreationDashboardRouteReadyLogPrefix + payload);
#else
                    Console.WriteLine(CreationDashboardRouteReadyLogPrefix + payload);
#endif
                    emitted = true;
                });
                if (terminal || emitted)
                    return;

                TimeSpan remaining = CreationDashboardRouteReadyMaximumWait
                                     - System.Diagnostics.Stopwatch.GetElapsedTime(waitStarted);
                if (remaining <= TimeSpan.Zero)
                    return;
                await Task.Delay(
                    remaining < CreationDashboardRouteReadyPollDelay
                        ? remaining
                        : CreationDashboardRouteReadyPollDelay,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page left or a newer appearance superseded this layout marker.
        }
        catch (Exception error)
        {
            // This proof-only marker must never destabilize the runner UI. Its
            // absence keeps the hosted journey fail-closed.
#if ANDROID
            global::Android.Util.Log.Warn(
                CreationDashboardRouteReadyLogTag,
                $"Creation dashboard route-ready marker suppressed: {error.GetType().Name}");
#else
            Console.Error.WriteLine(
                $"Creation dashboard route-ready marker suppressed: {error.GetType().Name}");
#endif
        }
    }

    private void AddCreationMethodRoute(
        CharacterCreationWizardSnapshot snapshot,
        CreationDashboardAuthorityProjection? projection,
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? prerequisite)
    {
        bool prerequisiteMethod = snapshot.BuildMethod is (CharacterCreationBuildMethods.Priority
            or CharacterCreationBuildMethods.SumToTen);
        bool lifeModuleMethod = string.Equals(
            snapshot.BuildMethod,
            CharacterCreationBuildMethods.LifeModules,
            StringComparison.Ordinal);
        bool canOpenPrerequisite = prerequisiteMethod
                                   && HasAuthoritativePrerequisiteOptions(prerequisite);
        bool canOpenLifeModule = lifeModuleMethod && Coordinator.CanOpenSr5LifeModuleOrigin();
        bool canOpen = canOpenPrerequisite || canOpenLifeModule;
        Func<Task> selected = canOpenPrerequisite
            ? OpenCreationPrerequisiteAsync
            : canOpenLifeModule
                ? OpenSr5LifeModuleOriginAsync
                : static () => Task.CompletedTask;

        string authorityDetail = canOpenPrerequisite
            ? PrerequisiteStageDetail(prerequisite!.Value!)
            : canOpenLifeModule
                ? "Read the source-bound Origin scene, preview exact effects, then confirm"
                : prerequisiteMethod
                    ? BuildPageUiProjection.CreationKarmaAuthorityRequired
                      + " · "
                      + (ProjectionStageBlocker(
                             projection,
                             prerequisiteStage: true,
                             attributeStage: false,
                             skillStage: false,
                             contactsStage: false,
                             resourcesStage: false)
                         ?? prerequisite?.Blockers.FirstOrDefault()
                         ?? prerequisite?.Value?.Blockers.FirstOrDefault()
                         ?? "creation-prerequisite-authority-unavailable")
                    : lifeModuleMethod
                        ? snapshot.Steps.FirstOrDefault(candidate => string.Equals(
                                candidate.StepId,
                                CharacterCreationWizardStepIds.LifeModules,
                                StringComparison.Ordinal))?.Blockers.FirstOrDefault()
                          ?? "sr5-life-module-origin-authority-unavailable"
                        : "creation-build-method-editor-unavailable";
        string method = RunnerSessionCoordinator.HumanizeId(snapshot.BuildMethod);
        string activeStage = StageLabel(snapshot, snapshot.ActiveStepId);
        _body.Add(NativeTheme.NavigationRow(
            $"Build method · {method}",
            $"Active stage: {activeStage} · {authorityDetail}",
            selected,
            enabled: canOpen,
            automationId: "creation-stage-method"));
    }

    private void AddFinalizationReviewAction(CharacterCreationWizardSnapshot snapshot)
    {
        if (!CreationDashboardProjectionBinding.TryCreate(
                Coordinator.State,
                snapshot,
                out CreationDashboardProjectionBinding? binding)
            || binding is null)
            return;
        if (_creationFinalizationBinding?.Equals(binding) != true)
        {
            _creationFinalizationQueue.Cancel();
            _creationFinalizationBinding = binding;
            _creationFinalizationAuthority = null;
            _creationFinalizationFailureReason = null;
        }
        if (_creationFinalizationAuthority is null)
        {
            _creationFinalizationQueue.TryRequest(
                binding,
                (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CharacterCreationFinalizationResult<CharacterCreationFinalizationState> result =
                        Coordinator.LoadCreationFinalization();
                    cancellationToken.ThrowIfCancellationRequested();
                    return result;
                },
                out BackgroundProjectionRequest<CreationDashboardProjectionBinding> request);
            if (_creationFinalizationQueue.TryTake(
                    request,
                    out CharacterCreationFinalizationResult<CharacterCreationFinalizationState> completed,
                    out Exception? error))
            {
                _creationFinalizationAuthority = error is null ? completed : null;
                _creationFinalizationFailureReason = error is null
                    ? null
                    : "creation-finalization-authority-load-failed";
            }
        }

        CharacterCreationFinalizationResult<CharacterCreationFinalizationState>? authority =
            _creationFinalizationAuthority;
        CreationPriorityLegalPathProjection legalPath =
            CreationPriorityLegalPathProjection.From(authority);
        AddCreationFinalizationStatus(legalPath);
        if (!legalPath.CanOpenReview
            || authority is not
               {
                   Outcome: CharacterCreationFinalizationOutcomes.Available,
                   Value.CanReview: true
               })
        {
            return;
        }

        Button review = NativeTheme.PrimaryButton("Review and finish creation");
        review.AutomationId = "creation-finalization-open-review";
        review.Clicked += async (_, _) => await RunAsync(async () =>
        {
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> result =
                Coordinator.ReviewCreationFinalization(authority.Value.Binding);
            if (result is not
                {
                    Outcome: CharacterCreationFinalizationOutcomes.Available,
                    Value.CanConfirm: true
                })
            {
                throw new InvalidOperationException(
                    result.Blockers.FirstOrDefault()
                    ?? "The final creation authority changed. Reload the runner and review again.");
            }
            await Navigation.PushAsync(new CreationFinalizationPage(Coordinator, result.Value));
        });
        _body.Add(review);
    }

    private void AddCreationFinalizationStatus(CreationPriorityLegalPathProjection projection)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Legal build readiness"));
        if (projection.Outcome == "loading")
        {
            Label loading = NativeTheme.Body(
                string.IsNullOrWhiteSpace(_creationFinalizationFailureReason)
                    ? "Loading Core's sealed whole-build readiness. Finalization remains disabled."
                    : _creationFinalizationFailureReason,
                string.IsNullOrWhiteSpace(_creationFinalizationFailureReason)
                    ? NativeTheme.Muted
                    : NativeTheme.Danger);
            loading.AutomationId = string.IsNullOrWhiteSpace(_creationFinalizationFailureReason)
                ? "creation-finalization-authority-loading"
                : "creation-finalization-authority-failed";
            card.Add(loading);
        }
        else
        {
            card.Add(NativeTheme.Metric("Authority", projection.Outcome));
            if (projection.ContentRevision is { } contentRevision)
                card.Add(NativeTheme.Metric("Revision", contentRevision.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrWhiteSpace(projection.SnapshotDigest))
                card.Add(NativeTheme.Metric("Snapshot", ShortDigest(projection.SnapshotDigest)));

            foreach (CreationPriorityLegalPathStep step in projection.Steps)
            {
                string status = step.IsComplete
                    ? "complete"
                    : step.IsRequired ? "required" : "optional";
                string detail = step.Blockers.FirstOrDefault()
                                ?? (step.SourceAnchorIds.Count > 0
                                    ? $"{step.SourceAnchorIds.Count.ToString(CultureInfo.InvariantCulture)} source anchor(s)"
                                    : "No source anchor in the current Core step");
                Label row = NativeTheme.Body($"{RunnerSessionCoordinator.HumanizeId(step.StepId)} · {status} · {detail}",
                    step.IsRequired && !step.IsComplete ? NativeTheme.Danger : NativeTheme.Muted);
                row.AutomationId = $"creation-finalization-step-{Token(step.StepId)}";
                card.Add(row);
            }

            IReadOnlyList<string> blockers = projection.Blockers.Count > 0
                ? projection.Blockers
                : string.IsNullOrWhiteSpace(_creationFinalizationFailureReason)
                    ? []
                    : [_creationFinalizationFailureReason];
            foreach (string blocker in blockers)
                card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
            Label readiness = NativeTheme.Body(
                projection.CanOpenReview
                    ? "Core has sealed a reviewable whole-build plan."
                    : "Review stays disabled until every Core-required typed draft is complete.",
                projection.CanOpenReview ? NativeTheme.Success : NativeTheme.Danger);
            readiness.AutomationId = projection.CanOpenReview
                ? "creation-finalization-authority-ready"
                : "creation-finalization-authority-blocked";
            card.Add(readiness);
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-finalization-readiness";
        _body.Add(border);
    }

    private void ScheduleCreationFinalizationAcceptance(
        BackgroundProjectionRequest<CreationDashboardProjectionBinding> request)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_creationFinalizationBinding?.Equals(request.Key) != true
                || Coordinator.State.CreationWizard is not { } snapshot
                || !request.Key.Matches(Coordinator.State, snapshot))
            {
                _creationFinalizationQueue.TryAccept(request);
                return;
            }
            if (!_creationFinalizationQueue.TryTake(
                    request,
                    out CharacterCreationFinalizationResult<CharacterCreationFinalizationState> completed,
                    out Exception? error))
                return;
            _creationFinalizationAuthority = error is null ? completed : null;
            _creationFinalizationFailureReason = error is null
                ? null
                : "creation-finalization-authority-load-failed";
            Refresh();
        });
    }

    private CreationDashboardAuthorityProjection? ResolveCreationProjection(
        CharacterCreationWizardSnapshot snapshot)
    {
        if (!CreationDashboardProjectionBinding.TryCreate(
                Coordinator.State,
                snapshot,
                out CreationDashboardProjectionBinding? binding)
            || binding is null)
        {
            _creationProjection = null;
            CancelCreationProjectionQueues();
            return null;
        }

        if (_creationProjection is not { } current || !current.Binding.Equals(binding))
        {
            CancelCreationProjectionQueues();
            _creationProjection = CreationDashboardAuthorityProjection.Loading(binding);
        }

        ResolveCreationPhase(
            binding,
            _creationProjection?.Progress.Prerequisite,
            CreationDashboardAuthorityPhase.Prerequisite,
            _creationPrerequisiteQueue,
            Coordinator.LoadCreationPrerequisite,
            AcceptCreationPrerequisite);
        ResolveCreationPhase(
            binding,
            _creationProjection?.Progress.Contacts,
            CreationDashboardAuthorityPhase.Contacts,
            _creationContactsQueue,
            Coordinator.LoadCreationContacts,
            AcceptCreationContacts);
        if (_resourcesPresenter is not null)
        {
            CharacterOverviewState resourcesOverview = Coordinator.State;
            ResolveCreationPhase(
                binding,
                _creationProjection?.Progress.Resources,
                CreationDashboardAuthorityPhase.Resources,
                _creationResourcesQueue,
                () => _resourcesPresenter.Load(resourcesOverview),
                AcceptCreationResources);
        }
        else if (_creationProjection is { Progress.Resources: CreationDashboardAuthorityPhaseState.Loading }
                 projectionWithoutResources)
        {
            _creationProjection = projectionWithoutResources with
            {
                Progress = projectionWithoutResources.Progress.WithTerminal(
                    CreationDashboardAuthorityPhase.Resources,
                    failed: true),
                ResourcesFailureReason = "creation-resources-presenter-unavailable"
            };
        }
        if (_creationProjection is { Progress.Prerequisite: CreationDashboardAuthorityPhaseState.Ready })
        {
            ResolveCreationPhase(
                binding,
                _creationProjection.Progress.Attributes,
                CreationDashboardAuthorityPhase.Attributes,
                _creationAttributesQueue,
                Coordinator.LoadCreationAttributes,
                AcceptCreationAttributes);
            ResolveCreationPhase(
                binding,
                _creationProjection.Progress.Skills,
                CreationDashboardAuthorityPhase.Skills,
                _creationSkillsQueue,
                Coordinator.LoadCreationSkills,
                AcceptCreationSkills);
        }
        return _creationProjection;
    }

    private static void ResolveCreationPhase<TResult>(
        CreationDashboardProjectionBinding binding,
        CreationDashboardAuthorityPhaseState? state,
        CreationDashboardAuthorityPhase phase,
        LatestBackgroundProjectionQueue<CreationDashboardProjectionBinding, TResult> queue,
        Func<TResult> loader,
        Action<CreationDashboardProjectionBinding, TResult, Exception?> accept)
    {
        if (state != CreationDashboardAuthorityPhaseState.Loading)
            return;

        bool requested = queue.TryRequest(
            binding,
            (request, cancellationToken) =>
            {
                TraceCreationPhase(phase, "loader-enter", request);
                cancellationToken.ThrowIfCancellationRequested();
                TResult result = loader();
                cancellationToken.ThrowIfCancellationRequested();
                TraceCreationPhase(phase, "loader-terminal", request);
                return result;
            },
            out BackgroundProjectionRequest<CreationDashboardProjectionBinding> request);
        TraceCreationPhase(phase, requested ? "request-new" : "request-shared", request);
        if (queue.TryTake(request, out TResult completed, out Exception? error))
        {
            TraceCreationPhase(phase, "take-synchronous", request);
            accept(binding, completed, error);
        }
    }

    private void ScheduleCreationPhaseAcceptance<TResult>(
        LatestBackgroundProjectionQueue<CreationDashboardProjectionBinding, TResult> queue,
        BackgroundProjectionRequest<CreationDashboardProjectionBinding> request,
        CreationDashboardAuthorityPhase phase,
        Action<CreationDashboardProjectionBinding, TResult, Exception?> accept)
    {
        TraceCreationPhase(phase, "dispatch-post", request);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TraceCreationPhase(phase, "dispatch-enter", request);
            if (!CanAcceptCreationPhase(request))
            {
                if (BuildPageUiProjection.ConsumeRejectedCreationPhaseForRefresh(queue, request))
                {
                    TraceCreationPhase(phase, "take-rejected-refresh", request);
                    Refresh();
                }
                return;
            }

            if (!queue.TryTake(request, out TResult completed, out Exception? error))
            {
                TraceCreationPhase(phase, "take-missed", request);
                return;
            }

            TraceCreationPhase(phase, "take-accepted", request);
            accept(request.Key, completed, error);
            Refresh();
        });
    }

    private static void TraceCreationPhase(
        CreationDashboardAuthorityPhase phase,
        string activity,
        BackgroundProjectionRequest<CreationDashboardProjectionBinding> request)
    {
        string message = $"phase={phase} activity={activity} "
                         + $"generation={request.Generation.ToString(CultureInfo.InvariantCulture)} "
                         + $"revision={request.Key.ContentRevision.ToString(CultureInfo.InvariantCulture)}";
#if ANDROID
        global::Android.Util.Log.Info("ChummerCreation", message);
#endif
        Console.WriteLine($"CHUMMER_CREATION_AUTHORITY {message}");
    }

    private bool CanAcceptCreationPhase(
        BackgroundProjectionRequest<CreationDashboardProjectionBinding> request)
        => Coordinator.State.CreationWizard is { } snapshot
           && request.Key.Matches(Coordinator.State, snapshot)
           && _creationProjection?.Binding.Equals(request.Key) == true;

    private void AcceptCreationPrerequisite(
        CreationDashboardProjectionBinding binding,
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> result,
        Exception? error)
    {
        if (_creationProjection is not { } projection || !projection.Binding.Equals(binding))
            return;
        _creationProjection = projection with
        {
            Progress = projection.Progress.WithTerminal(
                CreationDashboardAuthorityPhase.Prerequisite,
                failed: error is not null),
            Prerequisite = error is null ? result : null,
            PrerequisiteFailureReason = error is null
                ? null
                : "creation-prerequisite-authority-load-failed"
        };
    }

    private void AcceptCreationAttributes(
        CreationDashboardProjectionBinding binding,
        CharacterCreationFoundationResult<CharacterCreationAttributesState> result,
        Exception? error)
    {
        if (_creationProjection is not { } projection || !projection.Binding.Equals(binding))
            return;
        _creationProjection = projection with
        {
            Progress = projection.Progress.WithTerminal(
                CreationDashboardAuthorityPhase.Attributes,
                failed: error is not null),
            Attributes = error is null ? result : null,
            AttributesFailureReason = error is null
                ? null
                : "creation-attributes-authority-load-failed"
        };
    }

    private void AcceptCreationSkills(
        CreationDashboardProjectionBinding binding,
        CharacterCreationFoundationResult<CharacterCreationSkillsState> result,
        Exception? error)
    {
        if (_creationProjection is not { } projection || !projection.Binding.Equals(binding))
            return;
        _creationProjection = projection with
        {
            Progress = projection.Progress.WithTerminal(
                CreationDashboardAuthorityPhase.Skills,
                failed: error is not null),
            Skills = error is null ? result : null,
            SkillsFailureReason = error is null
                ? null
                : "creation-skills-authority-load-failed"
        };
    }

    private void AcceptCreationContacts(
        CreationDashboardProjectionBinding binding,
        CharacterCreationContactsInteractionLoadResult result,
        Exception? error)
    {
        if (_creationProjection is not { } projection || !projection.Binding.Equals(binding))
            return;
        _creationProjection = projection with
        {
            Progress = projection.Progress.WithTerminal(
                CreationDashboardAuthorityPhase.Contacts,
                failed: error is not null),
            Contacts = error is null ? result : null,
            ContactsFailureReason = error is null
                ? null
                : "creation-contacts-authority-load-failed"
        };
    }

    private void AcceptCreationResources(
        CreationDashboardProjectionBinding binding,
        CharacterCreationResourcesInteractionLoadResult result,
        Exception? error)
    {
        if (_creationProjection is not { } projection || !projection.Binding.Equals(binding))
            return;
        _creationProjection = projection with
        {
            Progress = projection.Progress.WithTerminal(
                CreationDashboardAuthorityPhase.Resources,
                failed: error is not null),
            Resources = error is null ? result : null,
            ResourcesFailureReason = error is null
                ? null
                : "creation-resources-authority-load-failed"
        };
    }

    private void CancelCreationProjectionQueues()
    {
        _creationPrerequisiteQueue.Cancel();
        _creationAttributesQueue.Cancel();
        _creationSkillsQueue.Cancel();
        _creationContactsQueue.Cancel();
        _creationResourcesQueue.Cancel();
        _creationFinalizationQueue.Cancel();
        _creationFinalizationBinding = null;
        _creationFinalizationAuthority = null;
        _creationFinalizationFailureReason = null;
    }

    private void RetryCreationProjection()
    {
        CancelCreationProjectionQueues();
        _creationProjection = null;
        Refresh();
    }

    private void AddBudgetRibbon(
        CharacterCreationWizardSnapshot snapshot,
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributes,
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skills)
    {
        _body.Add(NativeTheme.Eyebrow("Budgets"));
        if (snapshot.Budgets.Count == 0)
        {
            _body.Add(NativeTheme.Body(
                "No authoritative budgets are available. Chummer will not invent a remainder.",
                NativeTheme.Danger));
            return;
        }

        FlexLayout ribbon = new()
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.SpaceBetween,
            AlignItems = FlexAlignItems.Stretch
        };
        foreach (CharacterCreationBudgetState projectedBudget in snapshot.Budgets)
        {
            CharacterCreationBudgetState budget = HasAuthoritativeSkills(skills)
                                                     && skills!.Value is { } skillState
                ? projectedBudget.BudgetId switch
                {
                    CharacterCreationBudgetIds.ActiveSkills => skillState.ActiveSkillPointBudget,
                    CharacterCreationBudgetIds.SkillGroups => skillState.SkillGroupPointBudget,
                    CharacterCreationBudgetIds.KnowledgeSkills => skillState.KnowledgeSkillPointBudget,
                    _ => projectedBudget
                }
                : HasAuthoritativeAttributes(attributes) && attributes!.Value is { } attributeState
                    ? projectedBudget.BudgetId switch
                    {
                        CharacterCreationBudgetIds.NormalAttributes => attributeState.NormalPointBudget,
                        CharacterCreationBudgetIds.SpecialAttributes => attributeState.SpecialPointBudget,
                        CharacterCreationBudgetIds.Karma => attributeState.CreationKarmaBudget,
                        _ => projectedBudget
                    }
                    : projectedBudget;
            string unit = string.IsNullOrWhiteSpace(budget.Unit) ? "points" : budget.Unit;
            VerticalStackLayout card = new()
            {
                MinimumWidthRequest = 164,
                Spacing = 5
            };
            card.Add(NativeTheme.Eyebrow(budget.Label));
            card.Add(NativeTheme.Title(
                budget.IsExact
                    ? $"{budget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} left"
                    : "Not exact",
                20));
            card.Add(NativeTheme.Body(
                budget.IsExact
                    ? $"{budget.Used.ToString("0.##", CultureInfo.InvariantCulture)} / "
                        + $"{budget.Total.ToString("0.##", CultureInfo.InvariantCulture)} {unit}"
                    : budget.Blockers.FirstOrDefault() ?? "Rules authority unavailable",
                budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
            Border budgetCard = NativeTheme.Card(card, new Thickness(14));
            budgetCard.Margin = new Thickness(0, 0, 8, 10);
            budgetCard.AutomationId = $"creation-budget-{Token(budget.BudgetId)}";
            ribbon.Add(budgetCard);
        }
        _body.Add(ribbon);
    }

    private void AddWizardStages(
        CharacterCreationWizardSnapshot snapshot,
        CreationDashboardAuthorityProjection? projection,
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? prerequisite,
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributes,
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skills,
        CharacterCreationContactsInteractionLoadResult? creationContacts,
        CharacterCreationResourcesInteractionLoadResult? creationResources)
    {
        _body.Add(NativeTheme.Eyebrow("Generation steps"));
        foreach (CharacterCreationWizardStageState stage in snapshot.Steps)
        {
            // The build method has one canonical route above the generated stage list.  Core
            // snapshots may include or omit a method step, but the phone surface must never
            // duplicate its automation identity or present two competing method editors.
            if (string.Equals(
                    stage.StepId,
                    CharacterCreationWizardStepIds.Method,
                    StringComparison.Ordinal))
            {
                continue;
            }

            bool foundation = IsFoundationStage(stage.StepId);
            bool basicsStage = string.Equals(
                stage.StepId,
                CharacterCreationWizardStepIds.Basics,
                StringComparison.Ordinal);
            bool canOpenBasics = basicsStage
                                 && stage.IsAvailable
                                 && Coordinator.State.Profile?.Created == false;
            bool lifeModuleStep = string.Equals(
                stage.StepId,
                CharacterCreationWizardStepIds.LifeModules,
                StringComparison.Ordinal);
            bool lifeModuleOrigin = lifeModuleStep && stage.IsAvailable
                && Coordinator.CanOpenSr5LifeModuleOrigin();
            bool priorityPrerequisite = IsPrerequisiteStage(stage.StepId, snapshot.BuildMethod);
            bool canOpenFoundation = foundation
                                     && !lifeModuleStep
                                     && stage.IsAvailable
                                     && HasAuthoritativeFoundationOptions();
            bool canOpenPrerequisite = priorityPrerequisite
                                       && stage.IsAvailable
                                       && HasAuthoritativePrerequisiteOptions(prerequisite);
            bool attributeStage = string.Equals(
                stage.StepId,
                CharacterCreationWizardStepIds.Attributes,
                StringComparison.Ordinal);
            bool canOpenAttributes = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeAttributes(attributes));
            bool skillStage = string.Equals(stage.StepId, CharacterCreationWizardStepIds.Skills, StringComparison.Ordinal);
            bool canOpenSkills = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeSkills(skills));
            bool qualitiesStage = string.Equals(
                stage.StepId,
                CharacterCreationWizardStepIds.Qualities,
                StringComparison.Ordinal);
            bool canOpenQualities = qualitiesStage
                                    && stage.IsAvailable
                                    && HasAuthoritativeQualities();
            bool magicResonanceStage = string.Equals(
                stage.StepId,
                CharacterCreationWizardStepIds.MagicResonance,
                StringComparison.Ordinal);
            bool canOpenMagicResonance = magicResonanceStage
                                         && stage.IsAvailable
                                         && HasAuthoritativeMagicResonance();
            bool contactsStage = IsContactsStage(stage.StepId);
            bool canOpenContacts = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeCreationContacts(creationContacts));
            bool resourcesStage = IsResourcesStage(stage.StepId);
            bool canOpenResources = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeResources(creationResources));
            bool identityStage = string.Equals(
                stage.StepId,
                CharacterCreationWizardStepIds.IdentityStory,
                StringComparison.Ordinal);
            CreationIdentityRouteState? identityRoute = identityStage
                ? BuildPageUiProjection.CreationIdentityRoute(stage.Blockers)
                : null;
            bool canOpen = canOpenBasics || lifeModuleOrigin || canOpenFoundation || canOpenPrerequisite || canOpenAttributes
                           || canOpenSkills || canOpenQualities || canOpenMagicResonance
                           || canOpenContacts || canOpenResources || identityRoute?.IsEnabled == true;
            bool projectionBoundStage =
                priorityPrerequisite || attributeStage || skillStage || contactsStage || resourcesStage;
            string? projectionBlocker = ProjectionStageBlocker(
                projection,
                priorityPrerequisite,
                attributeStage,
                skillStage,
                contactsStage,
                resourcesStage);
            Func<Task> selected = canOpenBasics
                ? OpenCreationBasicsAsync
                : lifeModuleOrigin
                ? OpenSr5LifeModuleOriginAsync
                : canOpenResources
                ? OpenCreationResourcesAsync
                : canOpenPrerequisite
                ? OpenCreationPrerequisiteAsync
                : canOpenAttributes
                    ? OpenCreationAttributesAsync
                : canOpenSkills
                    ? OpenCreationSkillsAsync
                : canOpenQualities
                    ? OpenCreationQualitiesAsync
                : canOpenMagicResonance
                    ? OpenCreationMagicResonanceAsync
                : canOpenContacts
                    ? OpenCreationContactsAsync
                : canOpenFoundation
                    ? OpenCreationFoundationAsync
                    : () => Task.CompletedTask;
            string detail = canOpenBasics
                ? "Inspect the frozen SR5 settings profile; sourcebook changes stay fail-closed without a typed contract"
                : identityStage
                ? identityRoute!.Blocker
                : lifeModuleOrigin
                ? "Read the source-bound Origin scene, preview exact effects, then confirm"
                : canOpenResources
                ? CreationResourcesStageDetail(creationResources!.State!)
                : canOpenPrerequisite
                ? PrerequisiteStageDetail(prerequisite!.Value!)
                : canOpenAttributes
                    ? AttributeStageDetail(attributes!.Value!)
                : canOpenSkills
                    ? SkillsStageDetail(skills!.Value!)
                : canOpenQualities
                    ? QualitiesStageDetail(Coordinator.State.CreationQualities!)
                : canOpenMagicResonance
                    ? MagicResonanceStageDetail(
                        Coordinator.State.CreationMagicResonanceEditor!)
                : canOpenContacts
                    ? CreationContactsStageDetail(creationContacts!.State!)
                : canOpenFoundation
                    ? "Choose an exact metatype and Nationality Life Module"
                    : projectionBoundStage && !string.IsNullOrWhiteSpace(projectionBlocker)
                        ? projectionBlocker
                    : priorityPrerequisite && prerequisite is not null
                        ? prerequisite.Blockers.FirstOrDefault()
                          ?? prerequisite.Value?.Blockers.FirstOrDefault()
                          ?? HumanizeStatus(stage.Status)
                        : HumanizeStatus(stage.Status);
            if (stage.Blockers.Count > 0
                && !canOpenAttributes
                && !canOpenSkills
                && !canOpenQualities
                && !canOpenMagicResonance
                && !canOpenContacts
                && !canOpenResources)
            {
                detail += $" · {stage.Blockers[0]}";
            }
            Border row = NativeTheme.NavigationRow(
                stage.Label,
                detail,
                selected,
                enabled: canOpen,
                automationId: $"creation-stage-{Token(stage.StepId)}");
            _body.Add(row);
        }
    }

    private void AddCompletionBlockers(CharacterCreationWizardSnapshot snapshot)
    {
        if (snapshot.CompletionBlockers.Count == 0)
        {
            return;
        }

        VerticalStackLayout blockers = new() { Spacing = 6 };
        blockers.Add(NativeTheme.Eyebrow("Before you can finish"));
        foreach (string blocker in snapshot.CompletionBlockers)
        {
            blockers.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        }
        Border card = NativeTheme.Card(blockers);
        card.AutomationId = "creation-wizard-blockers";
        _body.Add(card);
    }

    private void AddLegalNextSteps(
        CharacterCreationWizardSnapshot snapshot,
        CreationDashboardAuthorityProjection? projection,
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? prerequisite,
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? attributeResult,
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? skillsResult,
        CharacterCreationContactsInteractionLoadResult? creationContacts,
        CharacterCreationResourcesInteractionLoadResult? creationResources)
    {
        CharacterCreationWizardStageState? active = snapshot.Steps.FirstOrDefault(stage =>
            string.Equals(stage.StepId, snapshot.ActiveStepId, StringComparison.Ordinal));
        string[] candidateIds = new[] { snapshot.ActiveStepId }
            .Concat(active?.LegalNextStepIds ?? [])
            .Concat(HasAuthoritativeAttributes(attributeResult)
                ? [CharacterCreationWizardStepIds.Attributes]
                : [])
            .Concat(HasAuthoritativeSkills(skillsResult)
                ? [CharacterCreationWizardStepIds.Skills]
                : [])
            .Concat(HasAuthoritativeQualities()
                ? [CharacterCreationWizardStepIds.Qualities]
                : [])
            .Concat(HasAuthoritativeMagicResonance()
                ? [CharacterCreationWizardStepIds.MagicResonance]
                : [])
            .Concat(HasAuthoritativeCreationContacts(creationContacts)
                ? [CharacterCreationWizardStepIds.ContactsLifestyles]
                : [])
            .Concat(HasAuthoritativeResources(creationResources)
                ? [CharacterCreationWizardStepIds.Resources]
                : [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidateIds.Length == 0)
        {
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Continue"));
        foreach (string stepId in candidateIds)
        {
            CharacterCreationWizardStageState? stage = snapshot.Steps.FirstOrDefault(candidate =>
                string.Equals(candidate.StepId, stepId, StringComparison.Ordinal));
            if (stage is null)
            {
                continue;
            }

            bool attributeStep = string.Equals(
                stepId,
                CharacterCreationWizardStepIds.Attributes,
                StringComparison.Ordinal);
            bool foundation = IsFoundationStage(stepId);
            bool basicsStep = string.Equals(
                stepId,
                CharacterCreationWizardStepIds.Basics,
                StringComparison.Ordinal);
            bool canOpenBasics = basicsStep
                                 && stage.IsAvailable
                                 && Coordinator.State.Profile?.Created == false;
            bool lifeModuleStep = string.Equals(
                stepId,
                CharacterCreationWizardStepIds.LifeModules,
                StringComparison.Ordinal);
            bool lifeModuleOrigin = lifeModuleStep && stage.IsAvailable
                && Coordinator.CanOpenSr5LifeModuleOrigin();
            bool canOpenFoundation = foundation
                                     && !lifeModuleStep
                                     && stage.IsAvailable
                                     && HasAuthoritativeFoundationOptions();
            bool canOpenAttributes = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeAttributes(attributeResult));
            bool skillStep = string.Equals(stepId, CharacterCreationWizardStepIds.Skills, StringComparison.Ordinal);
            bool canOpenSkills = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeSkills(skillsResult));
            bool qualitiesStep = string.Equals(stepId, CharacterCreationWizardStepIds.Qualities, StringComparison.Ordinal);
            bool canOpenQualities = qualitiesStep
                                    && stage.IsAvailable
                                    && HasAuthoritativeQualities();
            bool magicResonanceStep = string.Equals(
                stepId,
                CharacterCreationWizardStepIds.MagicResonance,
                StringComparison.Ordinal);
            bool canOpenMagicResonance = magicResonanceStep
                                         && stage.IsAvailable
                                         && HasAuthoritativeMagicResonance();
            bool contactsStep = IsContactsStage(stepId);
            bool canOpenContacts = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeCreationContacts(creationContacts));
            bool resourcesStep = IsResourcesStage(stepId);
            bool canOpenResources = BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage,
                HasAuthoritativeResources(creationResources));
            bool identityStep = string.Equals(
                stepId,
                CharacterCreationWizardStepIds.IdentityStory,
                StringComparison.Ordinal);
            CreationIdentityRouteState? identityRoute = identityStep
                ? BuildPageUiProjection.CreationIdentityRoute(stage.Blockers)
                : null;
            // The post-create AttributeEditRequest path must never serve as a wizard fallback.
            // Core's dedicated creation authority is the only Attributes route here.
            bool canOpen = canOpenBasics || lifeModuleOrigin || canOpenFoundation || canOpenAttributes || canOpenSkills
                           || canOpenQualities || canOpenMagicResonance || canOpenContacts
                           || canOpenResources || identityRoute?.IsEnabled == true;
            Func<Task> selected = canOpenBasics
                ? OpenCreationBasicsAsync
                : lifeModuleOrigin
                ? OpenSr5LifeModuleOriginAsync
                : canOpenResources
                ? OpenCreationResourcesAsync
                : canOpenFoundation
                ? OpenCreationFoundationAsync
                : canOpenAttributes
                    ? OpenCreationAttributesAsync
                : canOpenSkills
                    ? OpenCreationSkillsAsync
                : canOpenQualities
                    ? OpenCreationQualitiesAsync
                : canOpenMagicResonance
                    ? OpenCreationMagicResonanceAsync
                : canOpenContacts
                    ? OpenCreationContactsAsync
                : () => Task.CompletedTask;
            string detail = canOpenBasics
                ? "Inspect the frozen SR5 settings profile; sourcebook changes stay fail-closed without a typed contract"
                : identityStep
                ? identityRoute!.Blocker
                : lifeModuleOrigin
                ? "Read the source-bound Origin scene, preview exact effects, then confirm"
                : canOpenResources
                ? CreationResourcesStageDetail(creationResources!.State!)
                : canOpenFoundation
                ? "Choose an exact metatype and Nationality Life Module"
                : canOpenAttributes
                    ? AttributeStageDetail(attributeResult!.Value!)
                : canOpenSkills
                    ? SkillsStageDetail(skillsResult!.Value!)
                : canOpenQualities
                    ? QualitiesStageDetail(Coordinator.State.CreationQualities!)
                : canOpenMagicResonance
                    ? MagicResonanceStageDetail(
                        Coordinator.State.CreationMagicResonanceEditor!)
                : canOpenContacts
                    ? CreationContactsStageDetail(creationContacts!.State!)
                : attributeStep
                  && projection?.Progress.Attributes == CreationDashboardAuthorityPhaseState.Loading
                    ? "creation-authority-loading"
                : skillStep
                  && projection?.Progress.Skills == CreationDashboardAuthorityPhaseState.Loading
                    ? "creation-authority-loading"
                : attributeStep
                  && projection?.Progress.Attributes == CreationDashboardAuthorityPhaseState.Failed
                    ? projection.AttributesFailureReason ?? "creation-attributes-authority-load-failed"
                : skillStep
                  && projection?.Progress.Skills == CreationDashboardAuthorityPhaseState.Failed
                    ? projection.SkillsFailureReason ?? "creation-skills-authority-load-failed"
                : contactsStep
                  && projection?.Progress.Contacts == CreationDashboardAuthorityPhaseState.Loading
                    ? "creation-authority-loading"
                : contactsStep
                  && projection?.Progress.Contacts == CreationDashboardAuthorityPhaseState.Failed
                    ? projection.ContactsFailureReason ?? "creation-contacts-authority-load-failed"
                : contactsStep && creationContacts is not null
                    ? creationContacts.Blockers.FirstOrDefault()
                      ?? creationContacts.State?.Blockers.FirstOrDefault()
                      ?? "creation-contacts-authority-unavailable"
                : resourcesStep
                  && projection?.Progress.Resources == CreationDashboardAuthorityPhaseState.Loading
                    ? "creation-authority-loading"
                : resourcesStep
                  && projection?.Progress.Resources == CreationDashboardAuthorityPhaseState.Failed
                    ? projection.ResourcesFailureReason ?? "creation-resources-authority-load-failed"
                : resourcesStep && creationResources is not null
                    ? creationResources.Blockers.FirstOrDefault()
                      ?? creationResources.State?.Blockers.FirstOrDefault()
                      ?? "creation-resources-authority-unavailable"
                : attributeStep && prerequisite?.Value is { } prerequisiteState
                    ? AttributeGateDetail(prerequisiteState)
                : attributeStep && stage.IsAvailable
                    ? "Rules-authoritative Attribute increments and metatype adjustment are not available yet"
                : stage.IsAvailable
                    ? "Legal in the projection · dedicated phone step is not wired yet"
                    : stage.Blockers.FirstOrDefault() ?? "Blocked by the current projection";
            _body.Add(NativeTheme.NavigationRow(
                stage.Label,
                detail,
                selected,
                canOpen,
                $"creation-next-{Token(stepId)}"));
        }
    }

    private static string? ProjectionStageBlocker(
        CreationDashboardAuthorityProjection? projection,
        bool prerequisiteStage,
        bool attributeStage,
        bool skillStage,
        bool contactsStage,
        bool resourcesStage)
    {
        if (projection is null)
            return "creation-authority-loading";
        if (prerequisiteStage)
        {
            return projection.Progress.Prerequisite switch
            {
                CreationDashboardAuthorityPhaseState.Loading => "creation-authority-loading",
                CreationDashboardAuthorityPhaseState.Failed => projection.PrerequisiteFailureReason
                    ?? "creation-prerequisite-authority-load-failed",
                _ => null
            };
        }
        if (attributeStage)
        {
            return projection.Progress.Attributes switch
            {
                CreationDashboardAuthorityPhaseState.Loading => "creation-authority-loading",
                CreationDashboardAuthorityPhaseState.Failed => projection.AttributesFailureReason
                    ?? "creation-attributes-authority-load-failed",
                _ => null
            };
        }
        if (skillStage)
        {
            return projection.Progress.Skills switch
            {
                CreationDashboardAuthorityPhaseState.Loading => "creation-authority-loading",
                CreationDashboardAuthorityPhaseState.Failed => projection.SkillsFailureReason
                    ?? "creation-skills-authority-load-failed",
                _ => null
            };
        }
        if (contactsStage)
        {
            return projection.Progress.Contacts switch
            {
                CreationDashboardAuthorityPhaseState.Loading => "creation-authority-loading",
                CreationDashboardAuthorityPhaseState.Failed => projection.ContactsFailureReason
                    ?? "creation-contacts-authority-load-failed",
                _ => null
            };
        }
        if (resourcesStage)
        {
            return BuildPageUiProjection.CreationResourcesStageBlocker(projection);
        }
        return null;
    }

    private bool HasAuthoritativeFoundationOptions()
        => Coordinator.State.Profile?.Created == false
           && Coordinator.State.WorkspaceId is { } workspaceId
           && Coordinator.State.CreationFoundation is { } foundation
           && !foundation.CharacterCreated
           && foundation.Binding.WorkspaceId == workspaceId
           && foundation.Binding.ContentRevision == Coordinator.State.ContentRevision
           && foundation.Binding.SavedRevision == Coordinator.State.SavedRevision
           && foundation.MetatypeOptions.Count > 0
           && foundation.NationalityOptions.Count > 0;

    private bool HasAuthoritativePrerequisiteOptions(
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>? result)
        => result is
           {
               Outcome: CharacterCreationFoundationOutcomes.Success,
               Value: { } state
           }
           && CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State);

    private bool HasAuthoritativeAttributes(
        CharacterCreationFoundationResult<CharacterCreationAttributesState>? result)
        => result is
           {
               Outcome: CharacterCreationFoundationOutcomes.Success,
               Value: { } state
           }
           && CreationAttributesPhoneAuthority.IsReady(state, Coordinator.State);

    private bool HasAuthoritativeSkills(
        CharacterCreationFoundationResult<CharacterCreationSkillsState>? result)
        => result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state }
           && CreationSkillsPhoneAuthority.IsReady(state, Coordinator.State);

    private bool HasAuthoritativeQualities()
        => Coordinator.State.CreationQualities is { } state
           && CreationQualitiesPhoneAuthority.IsReady(state, Coordinator.State);

    private bool HasAuthoritativeMagicResonance()
        => Coordinator.State.CreationMagicResonance is { } core
           && Coordinator.State.CreationMagicResonanceEditor is { } editor
           && CreationMagicResonancePhoneAuthority.IsReady(
               core,
               editor,
               Coordinator.State);

    private bool HasAuthoritativeCreationContacts(
        CharacterCreationContactsInteractionLoadResult? result)
        => result is
           {
               Outcome: CharacterCreationContactOutcomes.Available,
               State: { } state
           }
           && CreationContactsPhoneAuthority.IsReady(state, Coordinator.State);

    private bool HasAuthoritativeResources(
        CharacterCreationResourcesInteractionLoadResult? result)
        => BuildPageUiProjection.HasExactTypedResourcesAuthority(result, Coordinator.State);

    private static bool IsPrerequisiteStage(string stepId, string buildMethod)
        => string.Equals(stepId, CharacterCreationWizardStepIds.Method, StringComparison.Ordinal)
           && buildMethod is (CharacterCreationBuildMethods.Priority
               or CharacterCreationBuildMethods.SumToTen);

    private static bool IsFoundationStage(string stepId)
        => string.Equals(stepId, CharacterCreationWizardStepIds.Foundation, StringComparison.Ordinal)
           || string.Equals(stepId, CharacterCreationWizardStepIds.LifeModules, StringComparison.Ordinal);

    private static bool IsContactsStage(string stepId)
        => string.Equals(
            stepId,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            StringComparison.Ordinal);

    private static bool IsResourcesStage(string stepId)
        => string.Equals(stepId, CharacterCreationWizardStepIds.Resources, StringComparison.Ordinal);

    private Task OpenCreationFoundationAsync()
        => Navigation.PushAsync(new CreationFoundationPage(Coordinator));

    private Task OpenCreationBasicsAsync()
        => Navigation.PushAsync(new CreationBasicsPage(Coordinator));

    private async Task OpenSr5LifeModuleOriginAsync()
    {
        AndroidSurfaceCopy copy = AndroidSurfaceStrings.Resolve();
        OriginDossierLifeModulePhoneResult opened =
            await Coordinator.OpenSr5LifeModuleOriginAsync();
        if (!opened.IsSuccess || opened.State is null)
        {
            await DisplayAlertAsync(
                copy["Origin.UnavailableTitle"],
                opened.Blockers.FirstOrDefault()
                ?? copy["Origin.UnavailableDetail"],
                copy["Common.Ok"]);
            return;
        }

        var page = new OriginDossierLifeModuleDecisionPage(
            opened.State,
            CultureInfo.CurrentUICulture.Name,
            async choiceId =>
            {
                OriginDossierLifeModulePhoneResult prepared =
                    await Coordinator.PrepareSr5LifeModuleOriginAsync(choiceId);
                if (prepared.IsSuccess)
                    return prepared.State;
                await DisplayAlertAsync(
                    copy["Origin.PreviewUnavailableTitle"],
                    prepared.Blockers.FirstOrDefault() ?? copy["Origin.PreviewUnavailableDetail"],
                    copy["Common.Ok"]);
                return null;
            },
            async (choiceId, previewDigest) =>
            {
                OriginDossierLifeModulePhoneResult confirmed =
                    await Coordinator.ConfirmSr5LifeModuleOriginAsync(choiceId, previewDigest);
                if (confirmed.IsSuccess && confirmed.Completed)
                    return true;
                await DisplayAlertAsync(
                    copy["Origin.DecisionNotSavedTitle"],
                    confirmed.Blockers.FirstOrDefault() ?? copy["Origin.DecisionNotSavedDetail"],
                    copy["Common.Ok"]);
                return false;
            });
        await Navigation.PushAsync(page);
    }

    private Task OpenCreationPrerequisiteAsync()
        => Navigation.PushAsync(new CreationPrerequisitePage(Coordinator));

    private Task OpenCreationAttributesAsync()
        => Navigation.PushAsync(new CreationAttributesPage(Coordinator));

    private Task OpenCreationSkillsAsync()
        => Navigation.PushAsync(new CreationSkillsPage(Coordinator));

    private Task OpenCreationQualitiesAsync()
        => Navigation.PushAsync(new CreationQualitiesPage(Coordinator));

    private Task OpenCreationMagicResonanceAsync()
        => Navigation.PushAsync(new CreationMagicResonancePage(Coordinator));

    private Task OpenCreationContactsAsync()
        => Navigation.PushAsync(new CreationContactsPage(Coordinator));

    private async Task OpenCreationResourcesAsync()
    {
        if (_resourcesPresenter is null || _overviewPresenter is null)
        {
            await DisplayAlertAsync(
                "Resources authority unavailable",
                "The typed Resources/overview presenters are unavailable. No fallback budget or purchase mutation is allowed.",
                "OK");
            return;
        }
        await Navigation.PushAsync(new CreationResourcesPage(
            Coordinator,
            _resourcesPresenter,
            _overviewPresenter,
            _gearPresenter));
    }

    private static string CreationContactsStageDetail(
        CharacterCreationContactsInteractionState state)
        => $"Edit {state.Contacts.Count.ToString(CultureInfo.InvariantCulture)} existing Contacts · "
           + $"{state.ContactBudget.Remaining.ToString(CultureInfo.InvariantCulture)} exact points remain";

    private static string CreationResourcesStageDetail(
        CharacterCreationResourcesInteractionState state)
        => $"Choose 0–{state.Authority.MaximumKarmaInvestment.ToString(CultureInfo.InvariantCulture)} Karma · "
           + $"{state.Budget.TotalStartingNuyen.ToString("N0", CultureInfo.InvariantCulture)} exact starting nuyen";

    private static string PrerequisiteStageDetail(CharacterCreationPrerequisiteState state)
    {
        string method = string.Equals(
            state.BuildMethod,
            CharacterCreationBuildMethods.SumToTen,
            StringComparison.Ordinal)
            ? "Sum-to-Ten"
            : state.BuildMethod;
        return state.PendingDraft is null
            ? $"Choose five ordered {method} ranks from exact Core authority"
            : $"Resume saved {method} draft · raw Attribute grant "
              + (state.BaseNormalAttributePoints?.ToString(CultureInfo.InvariantCulture) ?? "unavailable");
    }

    private static string AttributeGateDetail(CharacterCreationPrerequisiteState state)
        => state.CanEnterAttributes && !state.RequiresMetatypeAttributeAdjustment
            ? $"Core prerequisite complete · effective normal Attribute grant "
              + (state.EffectiveNormalAttributePoints?.ToString(CultureInfo.InvariantCulture)
                 ?? "unavailable")
              + " · Creation Attributes phone authority is ready"
            : $"Raw normal Attribute grant "
              + (state.BaseNormalAttributePoints?.ToString(CultureInfo.InvariantCulture) ?? "not selected")
              + " · Heritage/metatype halveattributepoints adjustment required · Attributes remain disabled";

    private static string AttributeStageDetail(CharacterCreationAttributesState state)
        => state.PendingDraft is null
            ? $"Allocate exact Core ledgers · {state.NormalPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} "
              + "normal points left"
            : $"Resume saved Attributes draft {state.PendingDraft.DraftRevision.ToString(CultureInfo.InvariantCulture)} · "
              + $"{state.NormalPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} normal points left";

    private static string SkillsStageDetail(CharacterCreationSkillsState state)
        => state.PendingDraft is null
            ? $"Allocate exact Core ledgers · {state.ActiveSkillPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} active points left"
            : $"Resume saved Skills draft {state.PendingDraft.DraftRevision.ToString(CultureInfo.InvariantCulture)} · "
              + $"{state.ActiveSkillPointBudget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} active points left";

    private static string QualitiesStageDetail(CharacterCreationQualitiesState state)
        => state.PendingDraft is null
            ? $"Choose exact Core options · +{state.Preview.PositiveQualityBudget.Remaining.ToString(CultureInfo.InvariantCulture)} positive / -{state.Preview.NegativeQualityBudget.Remaining.ToString(CultureInfo.InvariantCulture)} negative Karma available"
            : $"Resume saved Qualities draft {state.PendingDraft.DraftRevision.ToString(CultureInfo.InvariantCulture)} · {state.Preview.KarmaRemaining.ToString(CultureInfo.InvariantCulture)} Creation Karma left";

    private static string MagicResonanceStageDetail(
        CharacterCreationMagicResonanceEditorState state)
    {
        decimal remaining = state.Budgets.Sum(static budget => budget.Remaining);
        return state.HasPendingDraft
            ? $"Resume saved {CreationMagicResonancePage.KindLabel(state.Talent.Kind)} draft · {remaining.ToString("0.##", CultureInfo.InvariantCulture)} exact budget remaining"
            : $"Choose typed {CreationMagicResonancePage.KindLabel(state.Talent.Kind)} follow-ups · {remaining.ToString("0.##", CultureInfo.InvariantCulture)} exact budget remaining";
    }

    private static string StageLabel(CharacterCreationWizardSnapshot snapshot, string stepId)
        => snapshot.Steps.FirstOrDefault(stage => string.Equals(stage.StepId, stepId, StringComparison.Ordinal))?.Label
            ?? RunnerSessionCoordinator.HumanizeId(stepId);

    private static string HumanizeStatus(string status)
        => RunnerSessionCoordinator.HumanizeId(status);

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest)
            ? "unavailable"
            : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

    private void AddDossier()
    {
        _body.Add(NativeTheme.Eyebrow("Runner"));
        _body.Add(NativeTheme.NavigationRow(
            "Origin dossier",
            "Identity, appearance and story",
            () => Navigation.PushAsync(new OriginDossierPage(Coordinator)),
            automationId: "build-origin-dossier"));
        _body.Add(NativeTheme.NavigationRow(
            "Notes",
            "Private notes stored in this runner",
            () => Navigation.PushAsync(new CharacterNotesPage(Coordinator)),
            automationId: "build-character-notes"));
        _body.Add(NativeTheme.NavigationRow(
            "Situational modifiers",
            "Counterspelling dice and active lift/carry hits",
            async () =>
            {
                SituationalModifiersEditorState? editor = await Coordinator.PrepareSituationalModifiersEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new SituationalModifiersPage(Coordinator, editor));
                }
            },
            automationId: "build-situational-modifiers"));
        _body.Add(NativeTheme.NavigationRow(
            "Primary arm",
            "Preferred arm or Ambidextrous read-only state",
            async () =>
            {
                PrimaryArmEditorState? editor = await Coordinator.PreparePrimaryArmEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new PrimaryArmPage(Coordinator, editor));
                }
            },
            automationId: "build-primary-arm"));
        _body.Add(NativeTheme.NavigationRow(
            "Sustained effects",
            "Edit Psyche, Force, Net Hits, Self-Sustained state, or stop sustaining",
            async () =>
            {
                SustainedObjectsEditorState? editor = await Coordinator.PrepareSustainedObjectsEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new SustainedObjectsPage(Coordinator, editor));
                }
            },
            automationId: "build-sustained-effects"));
        _body.Add(NativeTheme.NavigationRow(
            "Group membership",
            "Join or leave a magical group or Resonance network",
            async () =>
            {
                GroupMembershipEditorState? editor = await Coordinator.PrepareGroupMembershipEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new GroupMembershipPage(Coordinator, editor));
                }
            },
            automationId: "build-group-membership"));
        _body.Add(NativeTheme.NavigationRow(
            "Group name",
            "Edit the saved initiation group name",
            async () =>
            {
                GroupNameEditorState? editor = await Coordinator.PrepareGroupNameEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new GroupNamePage(Coordinator, editor));
                }
            },
            automationId: "build-group-name"));
        _body.Add(NativeTheme.NavigationRow(
            "Tradition name",
            "Edit the saved name of a Custom magical tradition",
            async () =>
            {
                TraditionNameEditorState? editor = await Coordinator.PrepareTraditionNameEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new TraditionNamePage(Coordinator, editor));
                }
            },
            automationId: "build-tradition-name"));
        _body.Add(NativeTheme.NavigationRow(
            "Tradition drain",
            "Choose exact drain attributes for an eligible magical tradition",
            async () =>
            {
                TraditionDrainEditorState? editor = await Coordinator.PrepareTraditionDrainEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new TraditionDrainPage(Coordinator, editor));
                }
            },
            automationId: "build-tradition-drain"));
        _body.Add(NativeTheme.NavigationRow(
            "Tradition spirits",
            "Edit the five Spirit categories of an exact Custom magical tradition",
            async () =>
            {
                TraditionSpiritCategoryEditorState? editor =
                    await Coordinator.PrepareTraditionSpiritCategoryEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new TraditionSpiritCategoryPage(Coordinator, editor));
                }
            },
            automationId: "build-tradition-spirit-categories"));
        _body.Add(NativeTheme.NavigationRow(
            "Convert to Free Sprite",
            "Add Denial and convert an eligible non-Free Sprite",
            async () =>
            {
                FreeSpriteConversionEditorState? editor =
                    await Coordinator.PrepareFreeSpriteConversionAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new FreeSpriteConversionPage(Coordinator, editor));
                }
            },
            automationId: "build-free-sprite-conversion"));
        _body.Add(NativeTheme.NavigationRow(
            "Martial Arts Notes",
            "Edit notes and color for a saved Martial Art or parent-scoped Technique",
            async () =>
            {
                MartialArtNotesEditorState? editor =
                    await Coordinator.PrepareMartialArtNotesEditAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new MartialArtNotesPage(Coordinator, editor));
                }
            },
            automationId: "build-martial-art-notes"));
        _body.Add(NativeTheme.NavigationRow(
            "Delete Martial Art",
            "Delete a saved Martial Art or parent-scoped Technique after explicit confirmation",
            async () =>
            {
                MartialArtDeleteEditorState? editor =
                    await Coordinator.PrepareMartialArtDeleteAsync();
                if (editor is not null)
                {
                    await Navigation.PushAsync(new MartialArtDeletePage(Coordinator, editor));
                }
            },
            automationId: "build-martial-art-delete"));
        if (Coordinator.State.Profile?.Created == false)
        {
            _body.Add(NativeTheme.NavigationRow(
                "Creation Mugshots",
                "Browse existing portraits and choose or clear the exact Main Mugshot",
                async () =>
                {
                    CreationMugshotEditorState? editor = await Coordinator.PrepareCreationMugshotEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CreationMugshotPage(Coordinator, editor));
                    }
                },
                automationId: "build-creation-mugshots"));
        }
        if (Coordinator.State.Profile?.Created == true)
        {
            _body.Add(NativeTheme.NavigationRow(
                "Mugshots",
                "Browse existing portraits, choose or clear the exact Main Mugshot, or delete the selected portrait",
                async () =>
                {
                    CareerMugshotEditorState? editor = await Coordinator.PrepareCareerMugshotEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerMugshotPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-mugshots"));
            _body.Add(NativeTheme.NavigationRow(
                "Improvement groups",
                "Enable or disable every custom Improvement in one saved group",
                async () =>
                {
                    ImprovementGroupActiveEditorState? editor =
                        await Coordinator.PrepareImprovementGroupActiveEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementGroupActivePage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-group-active"));
            _body.Add(NativeTheme.NavigationRow(
                "Add Improvement Group",
                "Append one exact saved custom Improvement group name",
                async () =>
                {
                    ImprovementGroupAddEditorState? editor =
                        await Coordinator.PrepareImprovementGroupAddAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementGroupAddPage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-group-add"));
            _body.Add(NativeTheme.NavigationRow(
                "Improvements",
                "Enable or disable one directly selected saved Improvement",
                async () =>
                {
                    ImprovementActiveEditorState? editor =
                        await Coordinator.PrepareImprovementActiveEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementActivePage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-active"));
            _body.Add(NativeTheme.NavigationRow(
                "Improvement Notes",
                "Edit notes and note color for one directly selected saved Improvement",
                async () =>
                {
                    ImprovementNotesEditorState? editor =
                        await Coordinator.PrepareImprovementNotesEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new ImprovementNotesPage(Coordinator, editor));
                    }
                },
                automationId: "build-improvement-notes"));
            _body.Add(NativeTheme.NavigationRow(
                "Edge use",
                "Spend or regain one point of current Edge",
                async () =>
                {
                    CareerEdgeUseEditorState? editor = await Coordinator.PrepareCareerEdgeUseEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerEdgeUsePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-edge-use"));
            _body.Add(NativeTheme.NavigationRow(
                "Manual Karma",
                "Record dated Karma gained or spent, with optional Nuyen exchange",
                async () =>
                {
                    CareerManualKarmaEditorState? editor = await Coordinator.PrepareCareerManualKarmaEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerManualKarmaPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-manual-karma"));
            _body.Add(NativeTheme.NavigationRow(
                "Create expense",
                "Choose a source-exact Karma/Nuyen gained or spent operation",
                async () =>
                {
                    await Navigation.PushAsync(new CareerCreateExpenseMenuPage(Coordinator));
                },
                automationId: "build-career-create-expense"));
            _body.Add(NativeTheme.NavigationRow(
                "Manual Nuyen",
                "Record dated Nuyen gained or spent, with percentage and optional Karma exchange",
                async () =>
                {
                    CareerManualNuyenEditorState? editor = await Coordinator.PrepareCareerManualNuyenEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerManualNuyenPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-manual-nuyen"));
            _body.Add(NativeTheme.NavigationRow(
                "Nuyen expenses",
                "Select a saved Nuyen expense; edit date and reason, and manual-entry amounts",
                async () =>
                {
                    CareerNuyenExpenseEditorState? editor = await Coordinator.PrepareCareerNuyenExpenseEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerNuyenExpensePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-nuyen-expenses"));
            _body.Add(NativeTheme.NavigationRow(
                "Karma expenses",
                "Select a saved Karma expense; edit date and reason, and source-authorized amounts",
                async () =>
                {
                    CareerKarmaExpenseEditorState? editor = await Coordinator.PrepareCareerKarmaExpenseEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerKarmaExpensePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-karma-expenses"));
            _body.Add(NativeTheme.NavigationRow(
                "Advance active skill",
                "Review exact Chummer5 rating, maximum and Karma cost, then confirm one atomic advancement",
                async () =>
                {
                    CareerActiveSkillAdvanceEditorState? editor = await Coordinator.PrepareCareerActiveSkillAdvanceAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerActiveSkillAdvancePage(Coordinator, editor));
                    }
                },
                automationId: "build-career-active-skill"));
            _body.Add(NativeTheme.NavigationRow(
                "Advance attribute",
                "Choose an exact SR5 attribute quote, review Karma and legality, then apply through a restart-safe receipt checkpoint",
                async () =>
                {
                    Sr5CareerAttributeCoordinator authority = new(
                        new RunnerSessionSr5CareerAttributePresenter(Coordinator),
                        new PreferencesSr5CareerCheckpointOwnerAuthority());
                    CareerAttributeAdvanceEditorState? editor = await authority.PrepareAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new Sr5CareerAttributeWizardPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-attribute"));
            _body.Add(NativeTheme.NavigationRow(
                "Advance Knowledge / Language",
                "Choose an exact saved Knowledge or Language identity, review native-language and Karma authority, then apply through a restart-safe receipt checkpoint",
                async () =>
                {
                    Sr5CareerKnowledgeSkillCoordinator authority = new(
                        new RunnerSessionSr5CareerKnowledgeSkillPresenter(Coordinator),
                        new PreferencesSr5CareerCheckpointOwnerAuthority());
                    CareerKnowledgeSkillAdvanceEditorState? editor = await authority.PrepareAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new Sr5CareerKnowledgeSkillWizardPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-knowledge-language"));
            _body.Add(NativeTheme.NavigationRow(
                "Advance skill group",
                "Choose an exact saved group identity, review Core-owned membership, Karma and revisions, then enter the atomic receipt boundary",
                async () =>
                {
                    Sr5CareerSkillGroupCoordinator authority = new(
                        new RunnerSessionSr5CareerSkillGroupPresenter(Coordinator),
                        new PreferencesSr5CareerCheckpointOwnerAuthority());
                    CareerSkillGroupAdvanceEditorState? editor = await authority.PrepareAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new Sr5CareerSkillGroupWizardPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-skill-group-editor"));
            _body.Add(NativeTheme.NavigationRow(
                "Add specialization",
                "Choose an exact active or knowledge skill identity, configure a governed or custom option, then review the four-revision quote",
                async () =>
                {
                    Sr5CareerSpecializationCoordinator authority = new(
                        new RunnerSessionSr5CareerSpecializationPresenter(Coordinator),
                        new PreferencesSr5CareerCheckpointOwnerAuthority());
                    CareerSkillSpecializationEditorState? editor = await authority.PrepareAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new Sr5CareerSpecializationWizardPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-specialization-editor"));
            _body.Add(NativeTheme.NavigationRow(
                "Settle completed run",
                "Review governed rewards, Heat/reputation, contacts and both approvals before one atomic Core receipt",
                async () =>
                {
                    Sr5AfterRunSettlementCoordinator authority = new(
                        new RunnerSessionSr5AfterRunSettlementPresenter(Coordinator),
                        new PreferencesSr5CareerCheckpointOwnerAuthority());
                    Sr5AfterRunSettlementEditorState editor = await authority.PrepareAsync();
                    Page destination = editor.Status == Sr5AfterRunCatalogStatus.Missing
                        && Coordinator.SupportsManualAfterRunProposalEntry
                            ? new Sr5AfterRunManualProposalPage(
                                Coordinator,
                                editor.WorkspaceId,
                                editor.WorkspaceRevision)
                            : new Sr5AfterRunSettlementWizardPage(
                                Coordinator,
                                editor);
                    await Navigation.PushAsync(destination);
                },
                automationId: "build-career-after-run-settlement"));
            _body.Add(NativeTheme.NavigationRow(
                "Calendar",
                "Add the next ISO week, edit its notes and color, or delete it by stable identity",
                async () =>
                {
                    await Navigation.PushAsync(new Sr5DowntimeCalendarWizardPage(Coordinator));
                },
                automationId: "build-career-calendar"));
            _body.Add(NativeTheme.NavigationRow(
                "Reputation",
                "Street Cred, notoriety and source-aware reputation",
                async () =>
                {
                    CareerReputationEditorState? editor = await Coordinator.PrepareCareerReputationEditAsync();
                    if (editor is not null)
                    {
                        await Navigation.PushAsync(new CareerReputationPage(Coordinator, editor));
                    }
                },
                automationId: "build-career-reputation"));
        }
    }

    private void AddWorkspacePicker()
    {
        IReadOnlyList<OpenWorkspaceState> workspaces = Coordinator.State.OpenWorkspaces;
        if (workspaces.Count <= 1)
        {
            return;
        }

        string[] labels = workspaces.Select(static workspace =>
            !string.IsNullOrWhiteSpace(workspace.Alias) ? workspace.Alias : workspace.Name).ToArray();
        Picker picker = new()
        {
            Title = "Runner",
            ItemsSource = labels,
            SelectedIndex = Math.Max(0, workspaces.ToList().FindIndex(workspace =>
                workspace.Id == Coordinator.State.WorkspaceId)),
            BackgroundColor = NativeTheme.Surface
        };
        picker.SelectedIndexChanged += async (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
            {
                await RunAsync(() => Coordinator.SwitchWorkspaceAsync(workspaces[picker.SelectedIndex]));
            }
        };
        _body.Add(picker);
    }

    private void AddSummary()
    {
        string name = Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? "Runner";
        VerticalStackLayout summary = new() { Spacing = 10 };
        summary.Add(NativeTheme.Eyebrow(Coordinator.State.IsDirty ? "Unsaved changes" : "Runner"));
        summary.Add(NativeTheme.Title(name, 24));
        summary.Add(NativeTheme.Metric("Metatype", Coordinator.State.Profile?.Metatype ?? string.Empty));
        summary.Add(NativeTheme.Metric("Metavariant", Coordinator.State.Profile?.Metavariant ?? string.Empty));
        summary.Add(NativeTheme.Metric("Rules", Coordinator.State.Rules?.GameEdition ?? string.Empty));
        summary.Add(NativeTheme.Metric("Character Setting", Coordinator.State.Rules?.Settings ?? string.Empty));
        summary.Add(NativeTheme.Metric("Karma", Coordinator.State.Progress?.Karma.ToString() ?? string.Empty));
        summary.Add(NativeTheme.Metric("Nuyen", Coordinator.State.Progress?.Nuyen.ToString() ?? string.Empty));
        _body.Add(NativeTheme.Card(summary));
    }

    private void AddBuildAreas()
    {
        _body.Add(NativeTheme.Eyebrow("Build areas"));
        foreach (NavigationTabDefinition tab in Coordinator.Surface.NavigationTabs)
        {
            string title = RunnerSessionCoordinator.HumanizeId(tab.Id);
            bool enabled = Coordinator.IsTabEnabled(tab);
            bool active = string.Equals(tab.Id, Coordinator.Surface.ActiveTabId, StringComparison.Ordinal);
            string detail = active && Coordinator.State.ActiveSectionRows.Count > 0
                ? $"{Coordinator.State.ActiveSectionRows.Count} details"
                : "Open section";
            _body.Add(NativeTheme.NavigationRow(
                title,
                detail,
                () => RunAsync(async () =>
                {
                    await Coordinator.SelectTabAsync(tab.Id);
                    await Navigation.PushAsync(new BuildSectionPage(Coordinator, tab.Id, title));
                }),
                enabled,
                $"build-section-{tab.Id}"));
        }
    }

}
