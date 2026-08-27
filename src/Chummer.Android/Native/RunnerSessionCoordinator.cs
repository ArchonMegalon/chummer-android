using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Platform;
using Chummer.Application.Characters;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;

namespace Chummer.Android.Native;

public sealed record NativePlaySnapshot(
    int PhysicalDamage,
    int StunDamage,
    int LastPool,
    IReadOnlyList<int> LastRoll,
    int Hits,
    bool Glitch,
    string Notes)
{
    public static NativePlaySnapshot Empty { get; } = new(0, 0, 6, [], 0, false, string.Empty);
}

public sealed record NativeWorkspaceAuthoritySnapshot(
    string WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string PayloadSha256,
    string DocumentSha256)
{
    public bool Matches(CharacterOverviewState state)
        => state.WorkspaceId is { } workspaceId
           && string.Equals(WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
           && ContentRevision == state.ContentRevision
           && SavedRevision == state.SavedRevision;
}

public sealed record NativeDurableSaveNotice(
    CharacterWorkspaceId WorkspaceId,
    long SavedRevision)
{
    public bool Matches(CharacterOverviewState state)
        => state.Error is null
           && state.WorkspaceId is { } activeWorkspaceId
           && string.Equals(WorkspaceId.Value, activeWorkspaceId.Value, StringComparison.Ordinal)
           && SavedRevision > 0
           && state.ContentRevision == SavedRevision
           && state.SavedRevision == SavedRevision;
}

public static class AndroidE2EAuthority
{
    private static int _enabled;
    private static long _generation;

    public static bool Enabled => Volatile.Read(ref _enabled) != 0;
    public static long Generation => Volatile.Read(ref _generation);
    public static event EventHandler? Changed;

    internal static void ConfigureForCurrentProcess(bool enabled)
    {
        int requested = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _enabled, requested) != requested)
        {
            Interlocked.Increment(ref _generation);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}

public sealed record NativeAccountErasureResult(
    AndroidAccountErasureReceipt Receipt,
    bool LocalRunnersRemoved);

public enum NativeWorkspaceActivationKind
{
    LocalFile,
    OnlineCharacter,
    WorkspaceSwitch
}

public sealed record NativeWorkspaceActivationReceipt(
    NativeWorkspaceActivationKind Kind,
    CharacterWorkspaceId WorkspaceId)
{
    public bool Matches(
        CharacterOverviewState state,
        NativeWorkspaceActivationKind expectedKind)
        => Kind == expectedKind
           && state.WorkspaceId is { } activeWorkspaceId
           && string.Equals(
               WorkspaceId.Value,
               activeWorkspaceId.Value,
               StringComparison.Ordinal);
}

public sealed record CharacterNotesEditRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string CharacterNotes,
    string GameNotes,
    string GroupNotes);

public sealed record CreationPrerequisitePhoneConfirmResult(
    string Outcome,
    CharacterCreationPrerequisiteReceipt? Receipt,
    CharacterCreationPrerequisiteState? RefreshedState,
    IReadOnlyList<string> Blockers);

public sealed record CreationContactPhoneConfirmResult(
    string Outcome,
    CharacterCreationContactPreparedPreview PreparedPreview,
    CharacterCreationContactReceipt? Receipt,
    CharacterCreationContactsInteractionState? RefreshedState,
    bool RecoveredByReceiptLookup,
    IReadOnlyList<string> Blockers);

public sealed record CreationLifestylePhoneConfirmResult(
    string Outcome,
    CharacterCreationLifestylePreparedPreview PreparedPreview,
    CharacterCreationLifestyleReceipt? Receipt,
    CharacterCreationLifestylesInteractionState? RefreshedState,
    bool RecoveredByReceiptLookup,
    IReadOnlyList<string> Blockers);

public sealed record CreationAttributesPhoneConfirmResult(
    string Outcome,
    CharacterCreationAttributesReceipt? Receipt,
    CharacterCreationAttributesState? RefreshedState,
    IReadOnlyList<string> Blockers);

public sealed record CreationSkillsPhoneConfirmResult(
    string Outcome,
    CharacterCreationSkillsReceipt? Receipt,
    CharacterCreationSkillsState? RefreshedState,
    IReadOnlyList<string> Blockers);

public sealed record CreationQualitiesPhoneConfirmResult(
    string Outcome,
    CharacterCreationQualitiesDraftReceipt? Receipt,
    CharacterCreationQualitiesState? RefreshedState,
    IReadOnlyList<string> Blockers,
    bool MutationOutcomeKnown);

public static class CreationQualitiesPhoneOutcomes
{
    public const string Applied = "applied";
    public const string RejectedBeforeMutation = "rejected-before-mutation";
    public const string OutcomeUnknown = "outcome-unknown";
}

public static class CreationQualitiesPhoneBlockers
{
    public const string PostCommitRefreshRequired =
        "creation-qualities-post-commit-refresh-required";
    public const string OutcomeUnknown =
        "creation-qualities-commit-outcome-unknown";
}

public sealed record CreationMagicResonancePhoneConfirmResult(
    string Outcome,
    CharacterCreationMagicResonanceConfirmation? Confirmation,
    IReadOnlyList<string> Blockers,
    bool MutationOutcomeKnown);

public static class CreationMagicResonancePhoneOutcomes
{
    public const string Applied = "applied";
    public const string RejectedBeforeMutation = "rejected-before-mutation";
    public const string OutcomeUnknown = "outcome-unknown";
}

public static class CreationMagicResonancePhoneBlockers
{
    public const string PostCommitRefreshRequired =
        "creation-magic-resonance-post-commit-refresh-required";
    public const string OutcomeUnknown =
        "creation-magic-resonance-commit-outcome-unknown";
}

public sealed class RunnerSessionCoordinator : IDisposable
{
    private const string WorkspaceAuthorityDigestSchema =
        "chummer.android.workspace-document-authority/v1";
    private const string WorkspaceVerificationUnavailableNotice =
        "Workspace verification unavailable. Recent-file attribution was not recorded.";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private const string SelectedGroupPreferenceKey = "chummer.android.selected-group.v1";
    private const string SelectedWorkspacePreferenceKey = "chummer.android.selected-workspace.v1";
    private const string CharacterSettingsCatalogPreferenceKey = "chummer.android.character-settings-catalog.v1";
    private const string RosterLocatorPreferencePrefix = "chummer.android.roster-locator.v1.";
    private readonly ICharacterOverviewPresenter _presenter;
    private readonly IChummerClient _client;
    private readonly IWorkspaceOperationCoordinator _workspaceOperationCoordinator;
    private readonly ICharacterCreationFoundationInteractionPresenter _foundationInteractionPresenter;
    private readonly OriginDossierLifeModulePhoneRuntime? _originLifeModuleRuntime;
    private readonly ICharacterCreationContactsInteractionPresenter _creationContactsPresenter;
    private readonly ICharacterCreationLifestylesInteractionPresenter _creationLifestylesPresenter;
    private readonly ICharacterCreationPrerequisiteService _creationPrerequisiteService;
    private readonly ICharacterCreationAttributesService? _creationAttributesService;
    private readonly ICharacterCreationSkillsService? _creationSkillsService;
    private readonly ICharacterCreationQualitiesService? _creationQualitiesService;
    private readonly ICharacterCreationMagicResonanceService? _creationMagicResonanceService;
    private readonly ICharacterCreationFinalizationService? _creationFinalizationService;
    private readonly Sr5CustomDrugLabService? _customDrugLabService;
    private readonly ICharacterCareerSkillGroupAdvanceService? _careerSkillGroupService;
    private readonly ICharacterAfterRunSettlementService? _afterRunSettlementService;
    private readonly IAndroidAfterRunProposalCatalog? _afterRunProposalCatalog;
    private readonly CareerQualityInteractionPresenter? _careerQualityPresenter;
    private readonly IShellPresenter _shellPresenter;
    private readonly IShellSurfaceResolver _surfaceResolver;
    private readonly ICommandAvailabilityEvaluator _availability;
    private readonly IAndroidDocumentService _documents;
    private readonly IAndroidLinkedCharacterFileService _linkedCharacters;
    private readonly IAndroidSystemService _system;
    private readonly IAndroidAccountLinkService _account;
    private readonly CharacterRosterFavoritePresenter _rosterFavoritePresenter;
    private readonly ApplicationDeleteConfirmationPresenter _applicationSettingsPresenter;
    private readonly RookConversationStore _rookConversations = new();
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly SemaphoreSlim _workspaceActivationGate = new(1, 1);
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private readonly SemaphoreSlim _shellSyncGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _workspaceAuthoritySync = new();
    private bool _initialized;
    private bool _disposed;
    private long _handledDownloadVersion;
    private long _handledExportVersion;
    private long _handledPrintVersion;
    private string? _notice;
    private NativeDurableSaveNotice? _durableSaveNotice;
    private string _persistedCharacterSettingsCatalogJson = string.Empty;
    private IReadOnlyList<AndroidOnlineCharacter> _onlineCharacters = [];
    private IReadOnlyList<AndroidLinkedGroup> _groups = [];
    private IReadOnlyList<AndroidChronicleProject> _chronicles = [];
    private NativePlaySnapshot _play = NativePlaySnapshot.Empty;
    private NativeWorkspaceAuthoritySnapshot? _workspaceAuthority;
    private long _workspaceAuthorityOptInGeneration;
    private long _workspaceAuthorityEpoch;
    private ShellSurfaceState _surface = ShellSurfaceState.Empty;
    private CharacterWorkspaceId? _characterNotesWorkspaceId;
    private long _characterNotesRevision;
    private string _characterNotes = string.Empty;
    private string _gameNotes = string.Empty;
    private string _groupNotes = string.Empty;
    private CharacterRosterFavoriteState _rosterFavorites = CharacterRosterFavoriteState.Empty;
    private ApplicationDeleteConfirmationState _applicationSettings = ApplicationDeleteConfirmationState.Default;

    public RunnerSessionCoordinator(
        ICharacterOverviewPresenter presenter,
        IChummerClient client,
        IWorkspaceOperationCoordinator workspaceOperationCoordinator,
        ICharacterCreationFoundationInteractionPresenter foundationInteractionPresenter,
        ICharacterCreationContactsInteractionPresenter creationContactsPresenter,
        ICharacterCreationLifestylesInteractionPresenter creationLifestylesPresenter,
        ICharacterCreationPrerequisiteService creationPrerequisiteService,
        IShellPresenter shellPresenter,
        IShellSurfaceResolver surfaceResolver,
        ICommandAvailabilityEvaluator availability,
        IAndroidDocumentService documents,
        IAndroidLinkedCharacterFileService linkedCharacters,
        IAndroidSystemService system,
        IAndroidAccountLinkService account,
        CharacterRosterFavoritePresenter rosterFavoritePresenter,
        ApplicationDeleteConfirmationPresenter applicationSettingsPresenter,
        ICharacterCreationAttributesService? creationAttributesService = null,
        ICharacterCreationSkillsService? creationSkillsService = null,
        ICharacterCreationQualitiesService? creationQualitiesService = null,
        ICareerQualityAtomicWorkspace? careerQualityWorkspace = null,
        ICharacterCareerSkillGroupAdvanceService? careerSkillGroupService = null,
        ICharacterAfterRunSettlementService? afterRunSettlementService = null,
        IAndroidAfterRunProposalCatalog? afterRunProposalCatalog = null,
        ICharacterCreationMagicResonanceService? creationMagicResonanceService = null,
        OriginDossierLifeModulePhoneRuntime? originLifeModuleRuntime = null,
        ICharacterCreationFinalizationService? creationFinalizationService = null,
        Sr5CustomDrugLabService? customDrugLabService = null)
    {
        _presenter = presenter;
        _client = client;
        _workspaceOperationCoordinator = workspaceOperationCoordinator;
        _foundationInteractionPresenter = foundationInteractionPresenter;
        _originLifeModuleRuntime = originLifeModuleRuntime;
        _creationContactsPresenter = creationContactsPresenter;
        _creationLifestylesPresenter = creationLifestylesPresenter;
        _creationPrerequisiteService = creationPrerequisiteService;
        _creationAttributesService = creationAttributesService;
        _creationSkillsService = creationSkillsService;
        _creationQualitiesService = creationQualitiesService;
        _creationMagicResonanceService = creationMagicResonanceService;
        _creationFinalizationService = creationFinalizationService;
        _customDrugLabService = customDrugLabService;
        _careerSkillGroupService = careerSkillGroupService;
        _afterRunSettlementService = afterRunSettlementService;
        _afterRunProposalCatalog = afterRunProposalCatalog;
        _careerQualityPresenter = careerQualityWorkspace is null
            ? null
            : new CareerQualityInteractionPresenter(careerQualityWorkspace);
        _shellPresenter = shellPresenter;
        _surfaceResolver = surfaceResolver;
        _availability = availability;
        _documents = documents;
        _linkedCharacters = linkedCharacters;
        _system = system;
        _account = account;
        _rosterFavoritePresenter = rosterFavoritePresenter;
        _applicationSettingsPresenter = applicationSettingsPresenter;
        _presenter.StateChanged += OnPresenterStateChanged;
        _shellPresenter.StateChanged += OnShellStateChanged;
        _account.Changed += OnAccountChanged;
        AndroidE2EAuthority.Changed += OnE2EAuthorityChanged;
    }

    public event EventHandler? Changed;

    public CharacterOverviewState State => _presenter.State;

    /// <summary>
    /// Captures the exact current document bytes and document metadata for the read-only SR5
    /// Career action chooser. This does not enable the debug authority cache and grants no
    /// mutation capability; callers must still double-read around their typed projections.
    /// </summary>
    internal Task<NativeWorkspaceAuthoritySnapshot?> CaptureSr5CareerWizardWorkspaceAuthorityAsync(
        CancellationToken cancellationToken = default)
        => TryRefreshWorkspaceAuthorityAsync(
            State.WorkspaceId,
            expectedPayloadSha256: null,
            cancellationToken,
            allowReadOnlyProductCapture: true);

    public ShellSurfaceState Surface => _surface;

    public AndroidAccountLinkSnapshot Account => _account.Snapshot;

    public IReadOnlyList<AndroidOnlineCharacter> OnlineCharacters => _onlineCharacters;

    public IReadOnlyList<AndroidLinkedGroup> Groups => _groups;

    public IReadOnlyList<AndroidChronicleProject> Chronicles => _chronicles;

    public AndroidLinkedGroup? SelectedGroup
    {
        get
        {
            string selectedId = Preferences.Default.Get(SelectedGroupPreferenceKey, string.Empty);
            return _groups.FirstOrDefault(group => string.Equals(group.GroupId, selectedId, StringComparison.Ordinal))
                ?? _groups.FirstOrDefault();
        }
    }

    public NativePlaySnapshot Play => _play;

#if DEBUG
    public NativeWorkspaceAuthoritySnapshot? DebugWorkspaceAuthority
    {
        get
        {
            if (!AndroidE2EAuthority.Enabled)
            {
                return null;
            }

            lock (_workspaceAuthoritySync)
            {
                return _workspaceAuthority is { } authority && authority.Matches(State)
                    && _workspaceAuthorityOptInGeneration == AndroidE2EAuthority.Generation
                    ? authority
                    : null;
            }
        }
    }
#endif

    public CharacterRosterFavoriteState RosterFavorites => _rosterFavorites;

    public ApplicationDeleteConfirmationState ApplicationSettings => _applicationSettings;

    public RookConversationThreadState RookConversation
        => State.WorkspaceId is { } workspaceId
            ? _rookConversations.Read(workspaceId.Value)
            : RookConversationThreadState.Empty;

    public string CharacterNotes
        => State.WorkspaceId == _characterNotesWorkspaceId
            && State.ContentRevision == _characterNotesRevision
                ? _characterNotes
                : State.Profile?.CharacterNotes ?? State.Preferences.CharacterNotes;

    public string GameNotes
        => State.WorkspaceId == _characterNotesWorkspaceId
            && State.ContentRevision == _characterNotesRevision
                ? _gameNotes
                : State.Profile?.GameNotes ?? string.Empty;

    public string GroupNotes
        => State.WorkspaceId == _characterNotesWorkspaceId
            && State.ContentRevision == _characterNotesRevision
                ? _groupNotes
                : State.Profile?.GroupNotes ?? string.Empty;

    public string? Notice => _notice ?? State.Notice ?? Surface.Notice;

    public bool HasDurableSaveNotice
        => string.Equals(_notice, "Saved.", StringComparison.Ordinal)
           && _durableSaveNotice?.Matches(State) == true;

    public bool IsBusy => State.IsBusy || Surface.IsBusy;

    public Sr5CustomDrugLabSnapshot LoadCustomDrugLab(CharacterCustomDrugContext context)
    {
        if (_customDrugLabService is null
            || State.WorkspaceId is not { } workspaceId
            || State.IsDirty
            || State.ContentRevision != State.SavedRevision
            || !string.IsNullOrWhiteSpace(State.Error)
            || State.Profile?.Created != (context == CharacterCustomDrugContext.Career)
            || !string.Equals(State.Rules?.GameEdition, "SR5", StringComparison.OrdinalIgnoreCase))
        {
            return Sr5CustomDrugLabSnapshot.Blocked(
                State.WorkspaceId ?? default,
                context,
                CharacterCustomDrugBlockers.AuthorityUnavailable);
        }
        Sr5CustomDrugLabSnapshot result = _customDrugLabService.Load(workspaceId, context);
        return result.Preparation is { } preparation
               && preparation.ContentRevision == State.ContentRevision
            ? result
            : Sr5CustomDrugLabSnapshot.Blocked(
                workspaceId,
                context,
                CharacterCustomDrugBlockers.StaleRevision);
    }

    public Sr5CustomDrugLabSnapshot UpdateCustomDrugSelection(
        CharacterCustomDrugContext context,
        CharacterCustomDrugSelection selection)
    {
        CharacterWorkspaceId workspaceId = RequireCustomDrugWorkspace(context);
        return _customDrugLabService!.UpdateSelection(workspaceId, context, selection);
    }

    public Sr5CustomDrugLabSnapshot StartEditingCustomDrug(CharacterCustomDrugContext context)
    {
        CharacterWorkspaceId workspaceId = RequireCustomDrugWorkspace(context);
        return _customDrugLabService!.StartEditing(workspaceId, context);
    }

    public Sr5CustomDrugLabSnapshot ReviewCustomDrug(CharacterCustomDrugContext context)
    {
        CharacterWorkspaceId workspaceId = RequireCustomDrugWorkspace(context);
        return _customDrugLabService!.Review(workspaceId, context);
    }

    public Sr5CustomDrugLabSnapshot ConfirmCreationCustomDrug()
    {
        CharacterWorkspaceId workspaceId = RequireCustomDrugWorkspace(
            CharacterCustomDrugContext.Creation);
        return _customDrugLabService!.ConfirmCreation(workspaceId);
    }

    public Sr5CreationCustomDrugFinalizationContribution? ReadCreationCustomDrugContribution()
        => State.WorkspaceId is { } workspaceId && _customDrugLabService is not null
            ? _customDrugLabService.ReadCreationContribution(workspaceId)
            : null;

    public async Task<Sr5CustomDrugLabSnapshot> ConfirmCareerCustomDrugAsync(
        CancellationToken cancellationToken = default)
    {
        CharacterWorkspaceId workspaceId = RequireCustomDrugWorkspace(
            CharacterCustomDrugContext.Career);
        Sr5CustomDrugLabSnapshot result = _customDrugLabService!.ConfirmCareer(workspaceId);
        if (result.HasAppliedReceipt
            || result.Checkpoint?.Phase == Sr5CustomDrugCheckpointPhase.RecoveryUnknown)
        {
            await _presenter.LoadAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            await SyncShellAsync(cancellationToken).ConfigureAwait(false);
            NotifyChanged();
        }
        return result;
    }

    public async Task<Sr5CustomDrugLabSnapshot> UndoCareerCustomDrugAsync(
        CancellationToken cancellationToken = default)
    {
        CharacterWorkspaceId workspaceId = RequireCustomDrugWorkspace(
            CharacterCustomDrugContext.Career);
        Sr5CustomDrugLabSnapshot result = _customDrugLabService!.UndoCareer(workspaceId);
        if (string.Equals(result.Notice, Sr5CustomDrugLabNotices.UndoApplied, StringComparison.Ordinal)
            || result.Checkpoint?.Phase == Sr5CustomDrugCheckpointPhase.RecoveryUnknown)
        {
            await _presenter.LoadAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            await SyncShellAsync(cancellationToken).ConfigureAwait(false);
            NotifyChanged();
        }
        return result;
    }

    private CharacterWorkspaceId RequireCustomDrugWorkspace(CharacterCustomDrugContext context)
    {
        Sr5CustomDrugLabSnapshot live = LoadCustomDrugLab(context);
        if (!live.IsReady || State.WorkspaceId is not { } workspaceId)
        {
            throw new InvalidOperationException(
                live.Blockers.FirstOrDefault() ?? CharacterCustomDrugBlockers.AuthorityUnavailable);
        }
        return workspaceId;
    }

    public CharacterCreationContactsInteractionLoadResult LoadCreationContacts()
    {
        if (State.Profile?.Created != false || State.WorkspaceId is null)
        {
            return new CharacterCreationContactsInteractionLoadResult(
                CharacterCreationContactOutcomes.Blocked,
                null,
                [CharacterCreationContactsBlockers.WorkspaceUnavailable]);
        }

        CharacterCreationContactsInteractionLoadResult result =
            _creationContactsPresenter.Load(State);
        if (string.Equals(
                result.Outcome,
                CharacterCreationContactOutcomes.Available,
                StringComparison.Ordinal)
            && (result.State is null
                || !CreationContactsPhoneAuthority.IsBound(result.State, State)))
        {
            return new CharacterCreationContactsInteractionLoadResult(
                CharacterCreationContactOutcomes.Conflict,
                null,
                [CharacterCreationContactsBlockers.StaleWorkspaceRevision]);
        }
        return result;
    }

    public CharacterCreationContactsInteractionPrepareResult PrepareCreationContact(
        CharacterCreationContactEditInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        CharacterCreationContactsInteractionLoadResult load = LoadCreationContacts();
        if (load.State is not { } state
            || !CreationContactsPhoneAuthority.IsBound(state, State))
        {
            return new CharacterCreationContactsInteractionPrepareResult(
                CharacterCreationContactOutcomes.Conflict,
                load.State,
                null,
                load.Blockers.Count > 0
                    ? load.Blockers
                    : [CharacterCreationContactsBlockers.StaleWorkspaceRevision]);
        }
        if (!CreationContactsPhoneAuthority.IsReady(state, State))
        {
            return new CharacterCreationContactsInteractionPrepareResult(
                CharacterCreationContactOutcomes.Blocked,
                state,
                null,
                load.Blockers
                    .Concat(state.Blockers)
                    .DefaultIfEmpty(CharacterCreationContactsBlockers.AuthorityUnavailable)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }
        if (CreationContactsPhoneAuthority.ResolveUniqueContact(state, input.ContactId) is null)
        {
            return new CharacterCreationContactsInteractionPrepareResult(
                CharacterCreationContactOutcomes.Invalid,
                state,
                null,
                [CharacterCreationContactsBlockers.ContactNotFound]);
        }

        CharacterCreationContactsInteractionPrepareResult result =
            _creationContactsPresenter.Prepare(State, input);
        if (string.Equals(
                result.Outcome,
                CharacterCreationContactOutcomes.Available,
                StringComparison.Ordinal)
            && (result.State is null
                || result.PreparedPreview is null
                || !CreationContactsPhoneAuthority.PreparedMatches(
                    result.PreparedPreview,
                    result.State,
                    State)))
        {
            return new CharacterCreationContactsInteractionPrepareResult(
                CharacterCreationContactOutcomes.Conflict,
                result.State,
                null,
                result.Blockers
                    .Append(CharacterCreationContactsBlockers.PreviewDigestMismatch)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }
        return result;
    }

    public async Task<CreationContactPhoneConfirmResult> ConfirmCreationContactAsync(
        CharacterCreationContactPreparedPreview prepared,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationContactCoreAsync(prepared, cancellationToken),
            cancellationToken);

    private async Task<CreationContactPhoneConfirmResult> ConfirmCreationContactCoreAsync(
        CharacterCreationContactPreparedPreview prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        CharacterCreationContactsInteractionLoadResult before = LoadCreationContacts();
        if (before.State is not { } state
            || !CreationContactsPhoneAuthority.PreparedMatches(prepared, state, State))
        {
            return new CreationContactPhoneConfirmResult(
                CharacterCreationContactOutcomes.Conflict,
                prepared,
                null,
                null,
                false,
                before.Blockers.Count > 0
                    ? before.Blockers
                    : [CharacterCreationContactsBlockers.StaleWorkspaceRevision]);
        }

        var confirmation = new CharacterCreationContactConfirmation(
            prepared,
            prepared.PreviewDigest,
            prepared.IdempotencyKey,
            ExplicitlyConfirmed: true);
        CharacterCreationContactsInteractionConfirmResult? result = null;
        Exception? ambiguousFailure = null;
        try
        {
            result = _creationContactsPresenter.Confirm(State, confirmation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Core may have durably checkpointed before a transport/presentation failure became
            // observable. Never issue a fresh key: reload and recover the exact retained key.
            ambiguousFailure = exception;
        }

        CharacterCreationContactReceipt? receipt = result?.Receipt;
        bool recoveredByReceiptLookup = false;
        bool directlyValid = result is
        {
            Outcome: CharacterCreationContactOutcomes.Applied
                or CharacterCreationContactOutcomes.Replayed,
            Receipt: not null
        } && CreationContactsPhoneAuthority.ReceiptMatches(prepared, receipt!);
        if (!directlyValid)
        {
            await _presenter.LoadAsync(prepared.Binding.WorkspaceId, cancellationToken);
            await SyncShellAsync(cancellationToken);
            CharacterCreationContactsInteractionReceiptLookupResult lookup =
                _creationContactsPresenter.LookupReceipt(State, prepared.IdempotencyKey);
            if (string.Equals(
                    lookup.Outcome,
                    CharacterCreationContactOutcomes.Available,
                    StringComparison.Ordinal)
                && lookup.Receipt is { } recovered
                && CreationContactsPhoneAuthority.ReceiptMatches(prepared, recovered))
            {
                receipt = recovered;
                recoveredByReceiptLookup = true;
            }
            else
            {
                return new CreationContactPhoneConfirmResult(
                    result?.Outcome ?? CharacterCreationContactOutcomes.Unavailable,
                    prepared,
                    result?.Receipt,
                    null,
                    false,
                    (result?.Blockers ?? [])
                        .Concat(lookup.Blockers)
                        .Append(ambiguousFailure is null
                            ? CharacterCreationContactsBlockers.PersistenceAuthorityRequired
                            : CharacterCreationContactsBlockers.AuthorityUnavailable)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                        .ToArray());
            }
        }

        await _presenter.LoadAsync(receipt!.WorkspaceId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        CharacterCreationContactsInteractionLoadResult refreshed = LoadCreationContacts();
        if (refreshed.State is not { } refreshedState
            || !CreationContactsPhoneAuthority.ReceiptMatches(prepared, receipt)
            || !CreationContactsPhoneAuthority.RefreshedStateMatches(
                prepared,
                receipt,
                refreshedState,
                State))
        {
            _notice = null;
            NotifyChanged();
            return new CreationContactPhoneConfirmResult(
                CharacterCreationContactOutcomes.Conflict,
                prepared,
                receipt,
                null,
                recoveredByReceiptLookup,
                refreshed.Blockers
                    .Append(CharacterCreationContactsBlockers.StaleWorkspaceRevision)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }

        _ = await TryRefreshWorkspaceAuthorityAsync(
            expectedWorkspaceId: receipt.WorkspaceId,
            expectedPayloadSha256: receipt.ContentDigestAfter["sha256:".Length..],
            cancellationToken);
        _notice = recoveredByReceiptLookup
            ? "Creation Contact saved; the exact receipt recovered an ambiguous confirmation."
            : "Creation Contact saved and atomically checkpointed.";
        NotifyChanged();
        return new CreationContactPhoneConfirmResult(
            recoveredByReceiptLookup
                ? CharacterCreationContactOutcomes.Replayed
                : result!.Outcome,
            prepared,
            receipt,
            refreshedState,
            recoveredByReceiptLookup,
            []);
    }

    public CharacterCreationLifestylesInteractionLoadResult LoadCreationLifestyles()
    {
        if (State.Profile?.Created != false || State.WorkspaceId is null)
        {
            return new CharacterCreationLifestylesInteractionLoadResult(
                CharacterCreationLifestyleOutcomes.Blocked,
                null,
                [CharacterCreationLifestylesBlockers.WorkspaceUnavailable]);
        }

        CharacterCreationLifestylesInteractionLoadResult result =
            _creationLifestylesPresenter.Load(State);
        if (string.Equals(
                result.Outcome,
                CharacterCreationLifestyleOutcomes.Available,
                StringComparison.Ordinal)
            && (result.State is null
                || !CreationLifestylesPhoneAuthority.IsBound(result.State, State)))
        {
            return new CharacterCreationLifestylesInteractionLoadResult(
                CharacterCreationLifestyleOutcomes.Conflict,
                null,
                [CharacterCreationLifestylesBlockers.StaleWorkspaceRevision]);
        }
        return result;
    }

    public CharacterCreationLifestylesInteractionPrepareResult PrepareCreationLifestyle(
        CharacterCreationLifestyleMutationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        CharacterCreationLifestylesInteractionLoadResult load = LoadCreationLifestyles();
        if (load.State is not { } state
            || !CreationLifestylesPhoneAuthority.IsReady(state, State))
        {
            return new CharacterCreationLifestylesInteractionPrepareResult(
                CharacterCreationLifestyleOutcomes.Conflict,
                load.State,
                null,
                load.Blockers.Count > 0
                    ? load.Blockers
                    : [CharacterCreationLifestylesBlockers.StaleWorkspaceRevision]);
        }

        CharacterCreationLifestylesInteractionPrepareResult result =
            _creationLifestylesPresenter.Prepare(State, input);
        if (string.Equals(
                result.Outcome,
                CharacterCreationLifestyleOutcomes.Available,
                StringComparison.Ordinal)
            && (result.State is null
                || result.PreparedPreview is null
                || !CreationLifestylesPhoneAuthority.PreparedMatches(
                    result.PreparedPreview,
                    result.State,
                    State)))
        {
            return new CharacterCreationLifestylesInteractionPrepareResult(
                CharacterCreationLifestyleOutcomes.Conflict,
                result.State,
                null,
                result.Blockers
                    .Append(CharacterCreationLifestylesBlockers.PreviewDigestMismatch)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }
        return result;
    }

    public async Task<CreationLifestylePhoneConfirmResult> ConfirmCreationLifestyleAsync(
        CharacterCreationLifestylePreparedPreview prepared,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationLifestyleCoreAsync(prepared, cancellationToken),
            cancellationToken);

    private async Task<CreationLifestylePhoneConfirmResult> ConfirmCreationLifestyleCoreAsync(
        CharacterCreationLifestylePreparedPreview prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        CharacterCreationLifestylesInteractionLoadResult before = LoadCreationLifestyles();
        if (before.State is not { } state
            || !CreationLifestylesPhoneAuthority.PreparedMatches(prepared, state, State))
        {
            return new CreationLifestylePhoneConfirmResult(
                CharacterCreationLifestyleOutcomes.Conflict,
                prepared,
                null,
                null,
                false,
                before.Blockers.Count > 0
                    ? before.Blockers
                    : [CharacterCreationLifestylesBlockers.StaleWorkspaceRevision]);
        }

        var confirmation = new CharacterCreationLifestyleConfirmation(
            prepared,
            prepared.PreviewDigest,
            prepared.IdempotencyKey,
            ExplicitlyConfirmed: true);
        CharacterCreationLifestylesInteractionConfirmResult? result = null;
        Exception? ambiguousFailure = null;
        try
        {
            result = _creationLifestylesPresenter.Confirm(State, confirmation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The atomic Core commit may have completed before the caller observed a transport
            // failure. Retain the same key and recover only the matching durable receipt.
            ambiguousFailure = exception;
        }

        CharacterCreationLifestyleReceipt? receipt = result?.Receipt;
        bool recoveredByReceiptLookup = false;
        bool directlyValid = result is
        {
            Outcome: CharacterCreationLifestyleOutcomes.Applied
                or CharacterCreationLifestyleOutcomes.Replayed,
            Receipt: not null
        } && CreationLifestylesPhoneAuthority.ReceiptMatches(prepared, receipt!);
        if (!directlyValid)
        {
            await _presenter.LoadAsync(prepared.Binding.WorkspaceId, cancellationToken);
            await SyncShellAsync(cancellationToken);
            CharacterCreationLifestylesInteractionReceiptLookupResult lookup =
                _creationLifestylesPresenter.LookupReceipt(State, prepared.IdempotencyKey);
            if (string.Equals(
                    lookup.Outcome,
                    CharacterCreationLifestyleOutcomes.Available,
                    StringComparison.Ordinal)
                && lookup.Receipt is { } recovered
                && CreationLifestylesPhoneAuthority.ReceiptMatches(prepared, recovered))
            {
                receipt = recovered;
                recoveredByReceiptLookup = true;
            }
            else
            {
                return new CreationLifestylePhoneConfirmResult(
                    result?.Outcome ?? CharacterCreationLifestyleOutcomes.Unavailable,
                    prepared,
                    result?.Receipt,
                    null,
                    false,
                    (result?.Blockers ?? [])
                        .Concat(lookup.Blockers)
                        .Append(ambiguousFailure is null
                            ? CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired
                            : CharacterCreationLifestylesBlockers.AuthorityUnavailable)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                        .ToArray());
            }
        }

        await _presenter.LoadAsync(receipt!.WorkspaceId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        CharacterCreationLifestylesInteractionLoadResult refreshed = LoadCreationLifestyles();
        if (refreshed.State is not { } refreshedState
            || !CreationLifestylesPhoneAuthority.ReceiptMatches(prepared, receipt)
            || !CreationLifestylesPhoneAuthority.RefreshedStateMatches(
                prepared,
                receipt,
                refreshedState,
                State))
        {
            _notice = null;
            NotifyChanged();
            return new CreationLifestylePhoneConfirmResult(
                CharacterCreationLifestyleOutcomes.Conflict,
                prepared,
                receipt,
                null,
                recoveredByReceiptLookup,
                refreshed.Blockers
                    .Append(CharacterCreationLifestylesBlockers.StaleWorkspaceRevision)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }

        _ = await TryRefreshWorkspaceAuthorityAsync(
            expectedWorkspaceId: receipt.WorkspaceId,
            expectedPayloadSha256: receipt.ContentDigestAfter["sha256:".Length..],
            cancellationToken);
        _notice = recoveredByReceiptLookup
            ? "Creation Lifestyle saved; the exact receipt recovered an ambiguous confirmation."
            : "Creation Lifestyle saved and atomically checkpointed.";
        NotifyChanged();
        return new CreationLifestylePhoneConfirmResult(
            recoveredByReceiptLookup
                ? CharacterCreationLifestyleOutcomes.Replayed
                : result!.Outcome,
            prepared,
            receipt,
            refreshedState,
            recoveredByReceiptLookup,
            []);
    }

    public CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>
        LoadCreationPrerequisite()
    {
        if (State.Profile?.Created != false || State.WorkspaceId is not { } workspaceId)
        {
            return new CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable]);
        }

        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> result =
            _creationPrerequisiteService.Load(
                new CharacterCreationPrerequisiteLoadRequest(workspaceId));
        if (result.Value is { } state
            && !CreationPrerequisitePhoneAuthority.MatchesOverview(state, State))
        {
            return new CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                [CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);
        }
        return result;
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationState>
        LoadCreationFinalization()
    {
        if (_creationFinalizationService is null
            || State.Profile?.Created != false
            || State.WorkspaceId is not { } workspaceId)
        {
            return new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
                CharacterCreationFinalizationOutcomes.Unavailable,
                null,
                [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);
        }

        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> result =
            _creationFinalizationService.Load(new(workspaceId));
        if (result.Value is { } authority
            && (authority.Binding.WorkspaceId != workspaceId
                || authority.Binding.ContentRevision != State.ContentRevision
                || authority.Binding.SavedRevision != State.SavedRevision))
        {
            return new CharacterCreationFinalizationResult<CharacterCreationFinalizationState>(
                CharacterCreationFinalizationOutcomes.Conflict,
                null,
                [CharacterCreationFinalizationBlockers.StaleWorkspaceRevision]);
        }
        return result;
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview>
        ReviewCreationFinalization(CharacterCreationFinalizationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        CharacterCreationFinalizationResult<CharacterCreationFinalizationState> load =
            LoadCreationFinalization();
        if (_creationFinalizationService is null
            || load.Value is not { CanReview: true } state
            || state.Binding != binding)
        {
            return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReview>(
                CharacterCreationFinalizationOutcomes.Conflict,
                null,
                load.Blockers.Count > 0
                    ? load.Blockers
                    : [CharacterCreationFinalizationBlockers.StaleWorkspaceRevision]);
        }
        return _creationFinalizationService.Review(new(binding));
    }

    public Task<CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>>
        ConfirmCreationFinalizationAsync(
            CharacterCreationFinalizationReview review,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
        WithWorkspaceActivationGateAsync(
            () => ConfirmCreationFinalizationCoreAsync(review, idempotencyKey, cancellationToken),
            cancellationToken);

    private async Task<CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>>
        ConfirmCreationFinalizationCoreAsync(
            CharacterCreationFinalizationReview review,
            string idempotencyKey,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (_creationFinalizationService is null
            || review is not { CanConfirm: true, Plan: not null }
            || State.Profile?.Created != false
            || State.WorkspaceId != review.Binding.WorkspaceId
            || State.ContentRevision != review.Binding.ContentRevision
            || State.SavedRevision != review.Binding.SavedRevision)
        {
            return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Conflict,
                null,
                [CharacterCreationFinalizationBlockers.StaleWorkspaceRevision]);
        }

        CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>? result = null;
        try
        {
            result = _creationFinalizationService.Confirm(new(
                review.Binding,
                review.PreviewDigest,
                review.Plan.PlanDigest,
                idempotencyKey,
                ExplicitlyConfirmed: true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A durable atomic commit may have completed before its result reached this
            // boundary. Recover only the exact retained idempotency key; never retry with
            // a new command identity.
        }

        CharacterCreationFinalizationReceipt? receipt = result?.Value;
        if (receipt is null
            || result!.Outcome is not (CharacterCreationFinalizationOutcomes.Applied
                or CharacterCreationFinalizationOutcomes.Replayed))
        {
            CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> lookup =
                _creationFinalizationService.LookupReceipt(new(
                    review.Binding.WorkspaceId,
                    idempotencyKey));
            if (lookup.Outcome != CharacterCreationFinalizationOutcomes.Replayed
                || lookup.Value is null)
                return result ?? lookup;
            result = lookup;
            receipt = lookup.Value;
        }

        await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        if (State.Profile?.Created != true
            || State.WorkspaceId != receipt.WorkspaceId
            || State.ContentRevision != receipt.ContentRevision
            || State.SavedRevision != receipt.SavedRevision)
        {
            return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
                CharacterCreationFinalizationOutcomes.Applied,
                receipt,
                [CharacterCreationFinalizationBlockers.PostCommitReopenRequired]);
        }

        _notice = "Character creation finalized. Career mode reopened from the durable receipt.";
        NotifyChanged();
        return new CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>(
            result.Outcome,
            receipt,
            []);
    }

    internal CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>
        PreviewCreationPrerequisite(
            CharacterCreationPrerequisiteBinding binding,
            IReadOnlyDictionary<string, string> assignments,
            CreationPrerequisitePhoneSelections selections)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(selections);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> load =
            LoadCreationPrerequisite();
        if (load.Value is not { } state
            || !CreationPrerequisitePhoneAuthority.BindingEquals(binding, state.Binding))
        {
            return new CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                [CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);
        }
        if (!CreationPrerequisitePhoneAuthority.IsReady(state, State))
        {
            return new CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                state.Blockers.Count > 0
                    ? state.Blockers
                    : [CharacterCreationPrerequisiteBlockers.AuthorityUnavailable]);
        }

        return _creationPrerequisiteService.Preview(
            new CharacterCreationPrerequisitePreviewRequest(
                binding,
                new Dictionary<string, string>(assignments, StringComparer.Ordinal))
            {
                HeritageSelectionId = selections.HeritageSelectionId,
                TalentSelectionId = selections.TalentSelectionId,
                TalentActiveSkillSelectionIds = selections.TalentActiveSkillSelectionIds.ToArray(),
                TalentSkillGroupSelectionIds = selections.TalentSkillGroupSelectionIds.ToArray()
            });
    }

    internal async Task<CreationPrerequisitePhoneConfirmResult>
        ConfirmCreationPrerequisiteAsync(
            CharacterCreationPrerequisitePreview preview,
            IReadOnlyDictionary<string, string> assignments,
            CreationPrerequisitePhoneSelections selections,
            CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationPrerequisiteCoreAsync(
                preview,
                assignments,
                selections,
                cancellationToken),
            cancellationToken);

    private async Task<CreationPrerequisitePhoneConfirmResult>
        ConfirmCreationPrerequisiteCoreAsync(
            CharacterCreationPrerequisitePreview preview,
            IReadOnlyDictionary<string, string> assignments,
            CreationPrerequisitePhoneSelections selections,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(selections);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> before =
            LoadCreationPrerequisite();
        if (before.Value is not { } state
            || !CreationPrerequisitePhoneAuthority.BindingEquals(preview.Binding, state.Binding))
        {
            return new CreationPrerequisitePhoneConfirmResult(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                null,
                [CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision]);
        }
        if (!CreationPrerequisitePhoneAuthority.IsReady(state, State)
            || !PreviewMatchesSelections(preview, assignments, selections, state)
            || !preview.RequiresExplicitConfirmation
            || !preview.CanConfirm
            || preview.Blockers.Count != 0
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(preview.PreviewDigest))
        {
            return new CreationPrerequisitePhoneConfirmResult(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                null,
                [CharacterCreationPrerequisiteBlockers.PreviewDigestMismatch]);
        }

        CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> result =
            _creationPrerequisiteService.Confirm(
                new CharacterCreationPrerequisiteConfirmRequest(
                    preview.Binding,
                    new Dictionary<string, string>(assignments, StringComparer.Ordinal),
                    preview.PreviewDigest,
                    ExplicitlyConfirmed: true)
                {
                    HeritageSelectionId = selections.HeritageSelectionId,
                    TalentSelectionId = selections.TalentSelectionId,
                    TalentActiveSkillSelectionIds = selections.TalentActiveSkillSelectionIds.ToArray(),
                    TalentSkillGroupSelectionIds = selections.TalentSkillGroupSelectionIds.ToArray()
                });
        if (!string.Equals(
                result.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || result.Value is not { } receipt)
        {
            return new CreationPrerequisitePhoneConfirmResult(
                result.Outcome,
                result.Value,
                null,
                result.Blockers);
        }

        await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> refreshed =
            LoadCreationPrerequisite();
        if (refreshed.Value is not { } refreshedState
            || !CreationPrerequisitePhoneAuthority.ReceiptMatches(
                receipt,
                refreshedState,
                State))
        {
            _notice = null;
            NotifyChanged();
            return new CreationPrerequisitePhoneConfirmResult(
                CharacterCreationFoundationOutcomes.Conflict,
                receipt,
                null,
                refreshed.Blockers
                    .Append(CharacterCreationPrerequisiteBlockers.DraftConflict)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }

        _notice = "Creation-method draft saved. Core has opened the Attributes prerequisite.";
        NotifyChanged();
        return new CreationPrerequisitePhoneConfirmResult(
            CharacterCreationFoundationOutcomes.Success,
            receipt,
            refreshedState,
            []);
    }

    public CharacterCreationFoundationResult<CharacterCreationAttributesState>
        LoadCreationAttributes()
    {
        if (State.Profile?.Created != false || State.WorkspaceId is not { } workspaceId)
        {
            return new CharacterCreationFoundationResult<CharacterCreationAttributesState>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationAttributesBlockers.WorkspaceUnavailable]);
        }
        if (_creationAttributesService is null)
        {
            return new CharacterCreationFoundationResult<CharacterCreationAttributesState>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationAttributesBlockers.AuthorityUnavailable]);
        }

        CharacterCreationFoundationResult<CharacterCreationAttributesState> result =
            _creationAttributesService.Load(new CharacterCreationAttributesLoadRequest(workspaceId));
        if (result.Value is { } state
            && !CreationAttributesPhoneAuthority.MatchesOverview(state, State))
        {
            return new CharacterCreationFoundationResult<CharacterCreationAttributesState>(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                [CharacterCreationAttributesBlockers.StaleWorkspaceRevision]);
        }
        return result;
    }

    internal CharacterCreationFoundationResult<CharacterCreationAttributesPreview>
        PreviewCreationAttributes(
            CharacterCreationAttributesBinding binding,
            IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(allocations);
        CharacterCreationFoundationResult<CharacterCreationAttributesState> live =
            LoadCreationAttributes();
        if (live.Value is not { } state
            || !CreationAttributesPhoneAuthority.IsReady(state, State)
            || !CreationAttributesPhoneAuthority.BindingEquals(binding, state.Binding))
        {
            return new CharacterCreationFoundationResult<CharacterCreationAttributesPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                [CharacterCreationAttributesBlockers.StaleWorkspaceRevision]);
        }
        if (_creationAttributesService is null)
        {
            return new CharacterCreationFoundationResult<CharacterCreationAttributesPreview>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationAttributesBlockers.AuthorityUnavailable]);
        }

        return _creationAttributesService.Preview(
            new CharacterCreationAttributesPreviewRequest(binding, allocations.ToArray()));
    }

    internal async Task<CreationAttributesPhoneConfirmResult>
        ConfirmCreationAttributesAsync(
            CharacterCreationAttributesPreview preview,
            IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
            CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationAttributesCoreAsync(
                preview,
                allocations,
                cancellationToken),
            cancellationToken);

    private async Task<CreationAttributesPhoneConfirmResult>
        ConfirmCreationAttributesCoreAsync(
            CharacterCreationAttributesPreview preview,
            IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(allocations);
        CharacterOverviewState beforeActivation = State;
        CharacterCreationFoundationResult<CharacterCreationAttributesState> live =
            LoadCreationAttributes();
        if (_creationAttributesService is null
            || live.Value is not { } state
            || !CreationAttributesPhoneAuthority.CanConfirmPreview(
                state,
                State,
                preview,
                allocations))
        {
            return new CreationAttributesPhoneConfirmResult(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                null,
                [CharacterCreationAttributesBlockers.PreviewDigestMismatch]);
        }

        CharacterCreationFoundationResult<CharacterCreationAttributesPreview> authoritativePreview =
            _creationAttributesService.Preview(
                new CharacterCreationAttributesPreviewRequest(
                    preview.Binding,
                    allocations.ToArray()));
        if (!CreationAttributesPhoneAuthority.CanAdoptPreview(
                state,
                beforeActivation,
                authoritativePreview,
                allocations)
            || authoritativePreview.Value is not { } canonicalPreview
            || !CreationAttributesPhoneAuthority.CanonicallyEquals(
                preview,
                canonicalPreview))
        {
            return new CreationAttributesPhoneConfirmResult(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                null,
                [CharacterCreationAttributesBlockers.PreviewDigestMismatch]);
        }

        CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> result =
            _creationAttributesService.Confirm(
                new CharacterCreationAttributesConfirmRequest(
                    canonicalPreview.Binding,
                    allocations.ToArray(),
                    canonicalPreview.PreviewDigest,
                    ExplicitlyConfirmed: true));
        if (!string.Equals(
                result.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || result.Value is not { } receipt)
        {
            return new CreationAttributesPhoneConfirmResult(
                result.Outcome,
                result.Value,
                null,
                result.Blockers);
        }

        CharacterCreationFoundationResult<CharacterCreationAttributesState> committed =
            _creationAttributesService.Load(
                new CharacterCreationAttributesLoadRequest(receipt.WorkspaceId));
        if (!string.Equals(
                committed.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || committed.Value is not { } committedState
            || !CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation(
                receipt,
                canonicalPreview,
                allocations,
                committedState,
                beforeActivation))
        {
            _notice = null;
            NotifyChanged();
            return new CreationAttributesPhoneConfirmResult(
                CharacterCreationFoundationOutcomes.Conflict,
                receipt,
                null,
                committed.Blockers
                    .Append(CharacterCreationAttributesBlockers.DraftConflict)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }

        await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        CharacterCreationFoundationResult<CharacterCreationAttributesState> refreshed =
            LoadCreationAttributes();
        if (refreshed.Value is not { } refreshedState
            || !CreationAttributesPhoneAuthority.ReceiptMatches(
                receipt,
                canonicalPreview,
                allocations,
                refreshedState,
                State))
        {
            _notice = null;
            NotifyChanged();
            return new CreationAttributesPhoneConfirmResult(
                CharacterCreationFoundationOutcomes.Conflict,
                receipt,
                null,
                refreshed.Blockers
                    .Append(CharacterCreationAttributesBlockers.DraftConflict)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(blocker => blocker, StringComparer.Ordinal)
                    .ToArray());
        }

        _notice = "Attributes draft saved. Character effects remain pending finalization.";
        NotifyChanged();
        return new CreationAttributesPhoneConfirmResult(
            CharacterCreationFoundationOutcomes.Success,
            receipt,
            refreshedState,
            []);
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsState> LoadCreationSkills()
    {
        if (State.Profile?.Created != false || State.WorkspaceId is not { } workspaceId)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationSkillsBlockers.WorkspaceUnavailable]);
        if (_creationSkillsService is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationSkillsBlockers.AuthorityUnavailable]);
        CharacterCreationFoundationResult<CharacterCreationSkillsState> result =
            _creationSkillsService.Load(new(workspaceId));
        return result.Value is { } state && !CreationSkillsPhoneAuthority.MatchesOverview(state, State)
            ? new(CharacterCreationFoundationOutcomes.Conflict, null,
                [CharacterCreationSkillsBlockers.StaleWorkspaceRevision])
            : result;
    }

    internal CharacterCreationFoundationResult<CharacterCreationSkillsPreview> PreviewCreationSkills(
        CharacterCreationSkillsBinding binding,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        CharacterCreationFoundationResult<CharacterCreationSkillsState> live = LoadCreationSkills();
        if (_creationSkillsService is null || live.Value is not { } state
            || !CreationSkillsPhoneAuthority.IsReady(state, State)
            || !CreationSkillsPhoneAuthority.BindingEquals(binding, state.Binding))
            return new(CharacterCreationFoundationOutcomes.Conflict, null,
                [CharacterCreationSkillsBlockers.StaleWorkspaceRevision]);
        return _creationSkillsService.Preview(new(binding, allocations.ToArray(), groups.ToArray()));
    }

    internal async Task<CreationSkillsPhoneConfirmResult> ConfirmCreationSkillsAsync(
        CharacterCreationSkillsPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationSkillsCoreAsync(preview, allocations, groups, idempotencyKey, cancellationToken),
            cancellationToken);

    private async Task<CreationSkillsPhoneConfirmResult> ConfirmCreationSkillsCoreAsync(
        CharacterCreationSkillsPreview preview,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        CharacterOverviewState beforeActivation = State;
        CharacterCreationFoundationResult<CharacterCreationSkillsState> live = LoadCreationSkills();
        if (_creationSkillsService is null || live.Value is not { } state
            || !CreationSkillsPhoneAuthority.CanConfirmPreview(state, beforeActivation, preview, allocations, groups))
            return new(CharacterCreationFoundationOutcomes.Conflict, null, null,
                [CharacterCreationSkillsBlockers.PreviewDigestMismatch]);
        CharacterCreationFoundationResult<CharacterCreationSkillsPreview> reprojection =
            _creationSkillsService.Preview(new(preview.Binding, allocations.ToArray(), groups.ToArray()));
        if (!CreationSkillsPhoneAuthority.CanAdoptPreview(state, beforeActivation, reprojection, allocations, groups)
            || reprojection.Value is not { } canonical
            || !CreationSkillsPhoneAuthority.CanonicallyEquals(preview, canonical))
            return new(CharacterCreationFoundationOutcomes.Conflict, null, null,
                [CharacterCreationSkillsBlockers.PreviewDigestMismatch]);
        CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> confirmed =
            _creationSkillsService.Confirm(new(canonical.Binding, allocations.ToArray(), groups.ToArray(),
                canonical.PreviewDigest, idempotencyKey, true));
        if (confirmed.Value is not { } receipt
            || !string.Equals(confirmed.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal))
            return new(confirmed.Outcome, confirmed.Value, null, confirmed.Blockers);
        CharacterCreationFoundationResult<CharacterCreationSkillsState> committed =
            _creationSkillsService.Load(new(receipt.WorkspaceId));
        if (committed.Value is not { } committedState
            || !CreationSkillsPhoneAuthority.ReceiptMatchesBeforeActivation(
                receipt, canonical, committedState, beforeActivation, idempotencyKey))
            return new(CharacterCreationFoundationOutcomes.Conflict, receipt, null,
                committed.Blockers.Append(CharacterCreationSkillsBlockers.DraftConflict).Distinct().ToArray());
        try
        {
            await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken);
            await SyncShellAsync(cancellationToken);
        }
        catch (Exception)
        {
            return CommittedSkillsRefreshRequired(receipt, committedState, []);
        }
        CharacterCreationFoundationResult<CharacterCreationSkillsState> refreshed = LoadCreationSkills();
        if (refreshed.Value is not { } refreshedState
            || !CreationSkillsPhoneAuthority.ReceiptMatches(
                receipt, canonical, refreshedState, State, idempotencyKey))
            return CommittedSkillsRefreshRequired(receipt, committedState, refreshed.Blockers);
        _notice = "Skills draft saved. Character effects remain pending finalization.";
        NotifyChanged();
        return new(CharacterCreationFoundationOutcomes.Success, receipt, refreshedState, []);
    }

    private CreationSkillsPhoneConfirmResult CommittedSkillsRefreshRequired(
        CharacterCreationSkillsReceipt receipt,
        CharacterCreationSkillsState committedState,
        IEnumerable<string> blockers)
    {
        _notice = "Skills draft saved. Reopen the character to refresh the phone view.";
        NotifyChanged();
        return CreationSkillsPhoneAuthority.CommittedRefreshRequired(
            receipt,
            committedState,
            blockers);
    }

    public CharacterCreationFoundationResult<CharacterCreationQualitiesState>
        LoadCreationQualities()
    {
        if (State.Profile?.Created != false || State.WorkspaceId is not { } workspaceId)
        {
            return new(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationQualitiesBlockers.RevisionConflict]);
        }
        if (_creationQualitiesService is null)
        {
            return new(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationQualitiesBlockers.AuthorityUnavailable]);
        }
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> result =
            _creationQualitiesService.Load(new(workspaceId));
        return result.Value is { } state
               && !CreationQualitiesPhoneAuthority.MatchesOverview(state, State)
            ? new(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                [CharacterCreationQualitiesBlockers.RevisionConflict])
            : result;
    }

    internal CharacterCreationFoundationResult<CharacterCreationQualitiesPreview>
        PreviewCreationQualities(
            CharacterCreationQualitiesBinding binding,
            IReadOnlyList<string> selectedOptionIds)
    {
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> live =
            LoadCreationQualities();
        if (_creationQualitiesService is null
            || live.Value is not { } state
            || !CreationQualitiesPhoneAuthority.IsReady(state, State)
            || !CreationQualitiesPhoneAuthority.BindingEquals(binding, state.Binding))
        {
            return new(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                [CharacterCreationQualitiesBlockers.RevisionConflict]);
        }
        return _creationQualitiesService.Preview(new(
            binding,
            selectedOptionIds.OrderBy(static item => item, StringComparer.Ordinal).ToArray()));
    }

    internal async Task<CreationQualitiesPhoneConfirmResult>
        ConfirmCreationQualitiesAsync(
            CharacterCreationQualitiesCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationQualitiesCoreAsync(checkpoint, cancellationToken),
            cancellationToken);

    private async Task<CreationQualitiesPhoneConfirmResult>
        ConfirmCreationQualitiesCoreAsync(
            CharacterCreationQualitiesCheckpoint checkpoint,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        CharacterOverviewState beforeActivation = State;
        if (_creationQualitiesService is null
            || checkpoint.Phase != CharacterCreationQualitiesCheckpointPhase.Applying
            || !checkpoint.IsStructurallyValid()
            || !checkpoint.OwnsRecoveryRevision(beforeActivation))
        {
            return new(
                CreationQualitiesPhoneOutcomes.RejectedBeforeMutation,
                null,
                null,
                [CharacterCreationQualitiesBlockers.RevisionConflict],
                MutationOutcomeKnown: true);
        }

        CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> confirmed;
        try
        {
            confirmed = _creationQualitiesService.Confirm(new(
                checkpoint.Preview.Binding,
                checkpoint.SelectedOptionIds,
                checkpoint.Preview.PreviewDigest,
                checkpoint.IdempotencyKey,
                checkpoint.TransactionId,
                ExplicitlyConfirmed: true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return UnknownQualitiesOutcome();
        }

        if (!string.Equals(
                confirmed.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || confirmed.Value is not { } receipt)
        {
            CharacterCreationFoundationResult<CharacterCreationQualitiesState> observed =
                _creationQualitiesService.Load(new(checkpoint.Preview.Binding.WorkspaceId));
            if (observed.Value is { } unchanged
                && CreationQualitiesPhoneAuthority.BindingEquals(
                    unchanged.Binding,
                    checkpoint.Preview.Binding)
                && beforeActivation.ContentRevision == checkpoint.Preview.Binding.ContentRevision
                && beforeActivation.SavedRevision == checkpoint.Preview.Binding.SavedRevision)
            {
                return new(
                    CreationQualitiesPhoneOutcomes.RejectedBeforeMutation,
                    null,
                    unchanged,
                    confirmed.Blockers,
                    MutationOutcomeKnown: true);
            }
            return UnknownQualitiesOutcome(confirmed.Blockers);
        }

        CharacterCreationFoundationResult<CharacterCreationQualitiesState> committed =
            _creationQualitiesService.Load(new(receipt.WorkspaceId));
        if (committed.Value is not { } committedState
            || !CreationQualitiesPhoneAuthority.ReceiptMatchesPersistedState(
                checkpoint,
                receipt,
                committedState))
        {
            return UnknownQualitiesOutcome(
                committed.Blockers.Append(CharacterCreationQualitiesBlockers.DraftInvalid));
        }

        try
        {
            await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken);
            await SyncShellAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _notice = "Qualities draft saved. Reopen the character to refresh the phone view.";
            NotifyChanged();
            return new(
                CreationQualitiesPhoneOutcomes.Applied,
                receipt,
                committedState,
                [CreationQualitiesPhoneBlockers.PostCommitRefreshRequired],
                MutationOutcomeKnown: true);
        }

        CharacterCreationFoundationResult<CharacterCreationQualitiesState> refreshed =
            LoadCreationQualities();
        if (refreshed.Value is not { } refreshedState
            || !CreationQualitiesPhoneAuthority.ReceiptMatches(
                checkpoint,
                receipt,
                refreshedState,
                State))
        {
            _notice = "Qualities draft saved. Reopen the character to refresh the phone view.";
            NotifyChanged();
            return new(
                CreationQualitiesPhoneOutcomes.Applied,
                receipt,
                committedState,
                refreshed.Blockers
                    .Append(CreationQualitiesPhoneBlockers.PostCommitRefreshRequired)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                MutationOutcomeKnown: true);
        }

        _notice = "Qualities draft saved. Character effects remain pending finalization.";
        NotifyChanged();
        return new(
            CreationQualitiesPhoneOutcomes.Applied,
            receipt,
            refreshedState,
            [],
            MutationOutcomeKnown: true);
    }

    private static CreationQualitiesPhoneConfirmResult UnknownQualitiesOutcome(
        IEnumerable<string>? blockers = null)
        => new(
            CreationQualitiesPhoneOutcomes.OutcomeUnknown,
            null,
            null,
            (blockers ?? [])
                .Append(CreationQualitiesPhoneBlockers.OutcomeUnknown)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray(),
            MutationOutcomeKnown: false);

    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState>
        LoadCreationMagicResonance()
    {
        if (State.Profile?.Created != false || State.WorkspaceId is not { } workspaceId)
        {
            return new(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationMagicResonanceBlockers.WorkspaceUnavailable]);
        }
        if (_creationMagicResonanceService is null)
        {
            return new(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                [CharacterCreationMagicResonanceBlockers.AuthorityUnavailable]);
        }
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> result =
            _creationMagicResonanceService.Load(new(workspaceId));
        return result.Value is { } state
               && !CreationMagicResonancePhoneAuthority.MatchesOverview(state, State)
            ? new(
                CharacterCreationFoundationOutcomes.Conflict,
                null,
                [CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision])
            : result;
    }

    internal CharacterCreationMagicResonanceReview ReviewCreationMagicResonance(
        CharacterCreationMagicResonanceEditorState expectedEditor,
        CharacterCreationMagicResonanceDesktopDraft draft)
    {
        ArgumentNullException.ThrowIfNull(expectedEditor);
        ArgumentNullException.ThrowIfNull(draft);
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> live =
            LoadCreationMagicResonance();
        if (_creationMagicResonanceService is null
            || live.Value is not { } state
            || !CharacterCreationMagicResonanceWorkflow.TryProject(
                state,
                out CharacterCreationMagicResonanceEditorState? editor)
            || editor is null
            || !CreationMagicResonancePhoneAuthority.IsReady(state, editor, State)
            || !CreationMagicResonancePhoneAuthority.EditorEquals(
                expectedEditor,
                editor))
        {
            throw new InvalidOperationException(
                CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
        }
        return CharacterCreationMagicResonanceWorkflow.Review(
            _creationMagicResonanceService,
            editor,
            draft);
    }

    internal async Task<CreationMagicResonancePhoneConfirmResult>
        ConfirmCreationMagicResonanceAsync(
            CharacterCreationMagicResonanceCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationMagicResonanceCoreAsync(
                checkpoint,
                cancellationToken),
            cancellationToken);

    private async Task<CreationMagicResonancePhoneConfirmResult>
        ConfirmCreationMagicResonanceCoreAsync(
            CharacterCreationMagicResonanceCheckpoint checkpoint,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        CharacterOverviewState beforeActivation = State;
        if (_creationMagicResonanceService is null
            || checkpoint.Phase !=
            CharacterCreationMagicResonanceCheckpointPhase.Confirming
            || !checkpoint.IsStructurallyValid()
            || !checkpoint.OwnsRecoveryRevision(beforeActivation))
        {
            return new(
                CreationMagicResonancePhoneOutcomes.RejectedBeforeMutation,
                null,
                [CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision],
                MutationOutcomeKnown: true);
        }

        CharacterCreationMagicResonanceConfirmation confirmation;
        try
        {
            confirmation = CharacterCreationMagicResonanceWorkflow.Confirm(
                _creationMagicResonanceService,
                checkpoint.Review,
                checkpoint.IdempotencyKey,
                explicitlyConfirmed: true);
        }
        catch (Exception exception)
        {
            try
            {
                CharacterCreationFoundationResult<CharacterCreationMagicResonanceState>
                    observed = _creationMagicResonanceService.Load(new(
                        checkpoint.Review.Draft.ExpectedBinding.WorkspaceId));
                if (observed.Value is { } unchanged
                    && CreationMagicResonancePhoneAuthority.BindingEquals(
                        unchanged.Binding,
                        checkpoint.Review.Draft.ExpectedBinding)
                    && beforeActivation.ContentRevision ==
                    checkpoint.Review.Draft.ExpectedBinding.ContentRevision
                    && beforeActivation.SavedRevision ==
                    checkpoint.Review.Draft.ExpectedBinding.SavedRevision)
                {
                    return new(
                        CreationMagicResonancePhoneOutcomes.RejectedBeforeMutation,
                        null,
                        [exception.Message],
                        MutationOutcomeKnown: true);
                }
            }
            catch
            {
                // The exact mutation outcome cannot be proven; retain Confirming for replay.
            }
            return UnknownMagicResonanceOutcome([exception.Message]);
        }

        if (!CreationMagicResonancePhoneAuthority.ConfirmationMatches(
                checkpoint.Review,
                checkpoint.IdempotencyKey,
                confirmation))
        {
            return UnknownMagicResonanceOutcome(
                [CharacterCreationMagicResonanceBlockers.DraftInvalid]);
        }

        try
        {
            await _presenter.LoadAsync(
                confirmation.Receipt.WorkspaceId,
                cancellationToken);
            await SyncShellAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _notice = "Magic/Resonance draft saved. Reopen the character to refresh the phone view.";
            NotifyChanged();
            return new(
                CreationMagicResonancePhoneOutcomes.Applied,
                confirmation,
                [CreationMagicResonancePhoneBlockers.PostCommitRefreshRequired],
                MutationOutcomeKnown: true);
        }

        if (!CreationMagicResonancePhoneAuthority.OverviewMatchesReceipt(
                State,
                confirmation.Receipt))
        {
            _notice = "Magic/Resonance draft saved. Reopen the character to refresh the phone view.";
            NotifyChanged();
            return new(
                CreationMagicResonancePhoneOutcomes.Applied,
                confirmation,
                [CreationMagicResonancePhoneBlockers.PostCommitRefreshRequired],
                MutationOutcomeKnown: true);
        }

        _notice = "Magic/Resonance draft saved. Character effects remain pending finalization.";
        NotifyChanged();
        return new(
            CreationMagicResonancePhoneOutcomes.Applied,
            confirmation,
            [],
            MutationOutcomeKnown: true);
    }

    private static CreationMagicResonancePhoneConfirmResult
        UnknownMagicResonanceOutcome(IEnumerable<string>? blockers = null)
        => new(
            CreationMagicResonancePhoneOutcomes.OutcomeUnknown,
            null,
            (blockers ?? [])
                .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
                .Append(CreationMagicResonancePhoneBlockers.OutcomeUnknown)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                .ToArray(),
            MutationOutcomeKnown: false);

    public CharacterCreationFoundationInteractionLoadResult LoadCreationFoundation()
        => _foundationInteractionPresenter.Load(State);

    internal bool CanOpenSr5LifeModuleOrigin()
        => _originLifeModuleRuntime is not null
           && State.Profile?.Created == false
           && State.WorkspaceId is { } workspaceId
           && State.CreationWizard is { } wizard
           && string.Equals(wizard.BuildMethod, CharacterCreationBuildMethods.LifeModules, StringComparison.Ordinal)
           && State.CreationFoundation is { } foundation
           && string.Equals(foundation.RulesetId, "sr5", StringComparison.Ordinal)
           && string.Equals(foundation.BuildMethod, CharacterCreationBuildMethods.LifeModules, StringComparison.Ordinal)
           && foundation.Binding.WorkspaceId == workspaceId
           && foundation.Binding.ContentRevision == State.ContentRevision
           && foundation.Binding.SavedRevision == State.SavedRevision
           && !foundation.CharacterCreated;

    internal Task<OriginDossierLifeModulePhoneResult> OpenSr5LifeModuleOriginAsync(
        CancellationToken cancellationToken = default)
        => CanOpenSr5LifeModuleOrigin() && State.WorkspaceId is { } workspaceId
            ? _originLifeModuleRuntime!.OpenAsync(workspaceId.Value, cancellationToken)
            : Task.FromResult(new OriginDossierLifeModulePhoneResult(
                "blocked",
                null,
                ["sr5-life-module-origin-authority-unavailable"]));

    internal Task<OriginDossierLifeModulePhoneResult> PrepareSr5LifeModuleOriginAsync(
        string choiceId,
        CancellationToken cancellationToken = default)
        => CanOpenSr5LifeModuleOrigin() && State.WorkspaceId is { } workspaceId
            ? _originLifeModuleRuntime!.PrepareAsync(workspaceId.Value, choiceId, cancellationToken)
            : Task.FromResult(new OriginDossierLifeModulePhoneResult(
                "blocked",
                null,
                ["sr5-life-module-origin-authority-unavailable"]));

    internal async Task<OriginDossierLifeModulePhoneResult> ConfirmSr5LifeModuleOriginAsync(
        string choiceId,
        string previewDigest,
        CancellationToken cancellationToken = default)
    {
        if (!CanOpenSr5LifeModuleOrigin() || State.WorkspaceId is not { } workspaceId)
        {
            return new OriginDossierLifeModulePhoneResult(
                "blocked",
                null,
                ["sr5-life-module-origin-authority-unavailable"]);
        }
        OriginDossierLifeModulePhoneResult result = await _originLifeModuleRuntime!
            .ConfirmAsync(workspaceId.Value, choiceId, previewDigest, cancellationToken);
        if (result.IsSuccess && result.Completed)
        {
            await _presenter.LoadAsync(workspaceId, cancellationToken);
            await SyncShellAsync(cancellationToken);
            _notice = "Life Module decision saved. Continue character creation.";
            NotifyChanged();
        }
        return result;
    }

    public CharacterCreationFoundationInteractionPrepareResult PrepareCreationFoundation(
        CharacterCreationFoundationSelectionInput input)
        => _foundationInteractionPresenter.Prepare(State, input);

    public async Task<CharacterCreationFoundationInteractionConfirmResult> ConfirmCreationFoundationAsync(
        CharacterCreationFoundationConfirmation confirmation,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ConfirmCreationFoundationCoreAsync(confirmation, cancellationToken),
            cancellationToken);

    private async Task<CharacterCreationFoundationInteractionConfirmResult> ConfirmCreationFoundationCoreAsync(
        CharacterCreationFoundationConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        CharacterCreationFoundationInteractionConfirmResult result =
            _foundationInteractionPresenter.Confirm(State, confirmation);
        if (!string.Equals(
                result.Outcome,
                CharacterCreationFoundationOutcomes.Success,
                StringComparison.Ordinal)
            || result.Receipt is not { } receipt)
        {
            return result;
        }

        await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        if (!OverviewMatchesFoundationReceipt(State, result, receipt))
        {
            _notice = null;
            NotifyChanged();
            return result with
            {
                Outcome = CharacterCreationFoundationOutcomes.Conflict,
                RefreshedState = null,
                Blockers = result.Blockers
                    .Append(CharacterCreationFoundationInteractionBlockers.RefreshAuthorityRequired)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static blocker => blocker, StringComparer.Ordinal)
                    .ToArray()
            };
        }

        _notice = "Foundation draft saved. Character effects remain pending compilation.";
        NotifyChanged();
        return result;
    }

    private static bool PreviewMatchesSelections(
        CharacterCreationPrerequisitePreview preview,
        IReadOnlyDictionary<string, string> assignments,
        CreationPrerequisitePhoneSelections selections,
        CharacterCreationPrerequisiteState state)
    {
        if (!string.Equals(
                preview.Schema,
                CharacterCreationPrerequisiteSchemas.PreviewV1,
                StringComparison.Ordinal)
            || !CreationPrerequisitePhoneAuthority.BindingEquals(preview.Binding, state.Binding)
            || assignments.Count != CharacterCreationPriorityCategoryIds.Ordered.Count
            || preview.Assignments.Count != CharacterCreationPriorityCategoryIds.Ordered.Count)
        {
            return false;
        }

        for (int index = 0; index < CharacterCreationPriorityCategoryIds.Ordered.Count; index++)
        {
            string category = CharacterCreationPriorityCategoryIds.Ordered[index];
            CharacterCreationPriorityAssignment assignment = preview.Assignments[index];
            CharacterCreationPriorityOptionProjection? option =
                CreationPrerequisitePhoneAuthority.ResolveUniqueOption(
                    state,
                    category,
                    assignment.Rank);
            if (!assignments.TryGetValue(category, out string? rank)
                || assignment.Order != index
                || !string.Equals(assignment.CategoryId, category, StringComparison.Ordinal)
                || !string.Equals(assignment.Rank, rank, StringComparison.Ordinal)
                || option is null
                || !CreationPrerequisitePhoneAuthority.AssignmentMatchesOption(
                    assignment,
                    option))
            {
                return false;
            }
        }

        CharacterCreationPriorityOptionProjection? heritageRank = preview.Assignments
            .Where(assignment => string.Equals(
                assignment.CategoryId,
                CharacterCreationPriorityCategoryIds.Heritage,
                StringComparison.Ordinal))
            .Select(assignment => CreationPrerequisitePhoneAuthority.ResolveUniqueOption(
                state,
                assignment.CategoryId,
                assignment.Rank))
            .SingleOrDefault();
        CharacterCreationPriorityOptionProjection? talentRank = preview.Assignments
            .Where(assignment => string.Equals(
                assignment.CategoryId,
                CharacterCreationPriorityCategoryIds.Talent,
                StringComparison.Ordinal))
            .Select(assignment => CreationPrerequisitePhoneAuthority.ResolveUniqueOption(
                state,
                assignment.CategoryId,
                assignment.Rank))
            .SingleOrDefault();
        CharacterCreationPriorityOptionProjection? attributeRank = preview.Assignments
            .Where(assignment => string.Equals(
                assignment.CategoryId,
                CharacterCreationPriorityCategoryIds.Attributes,
                StringComparison.Ordinal))
            .Select(assignment => CreationPrerequisitePhoneAuthority.ResolveUniqueOption(
                state,
                assignment.CategoryId,
                assignment.Rank))
            .SingleOrDefault();
        CharacterCreationPriorityHeritageOptionProjection? heritageOption = heritageRank is null
            ? null
            : CreationPrerequisitePhoneAuthority.ResolveUniqueHeritageOption(
                heritageRank,
                selections.HeritageSelectionId);
        CharacterCreationPriorityTalentOptionProjection? talentOption = talentRank is null
            ? null
            : CreationPrerequisitePhoneAuthority.ResolveUniqueTalentOption(
                talentRank,
                selections.TalentSelectionId);

        return heritageRank is not null
               && talentRank is not null
               && attributeRank is not null
               && heritageOption is not null
               && talentOption is not null
               && preview.HeritageSelection is { } heritageSelection
               && preview.TalentSelection is { } talentSelection
               && CreationPrerequisitePhoneAuthority.HeritageSelectionMatchesOption(
                   heritageSelection,
                   heritageOption,
                   heritageRank.SourceId)
               && CreationPrerequisitePhoneAuthority.TalentSelectionMatchesOption(
                   talentSelection,
                   talentOption,
                   talentRank.SourceId)
               && CreationPrerequisitePhoneAuthority.TalentGrantPlanMatchesSelections(
                   talentSelection.GrantPlan,
                   talentOption,
                   selections.TalentActiveSkillSelectionIds,
                   selections.TalentSkillGroupSelectionIds)
               && preview.CreationKarmaBudget.IsExact
               && string.Equals(
                   preview.CreationKarmaBudget.BudgetId,
                   CharacterCreationBudgetIds.Karma,
                   StringComparison.Ordinal)
               && preview.CreationKarmaBudget.Blockers.Count == 0
               && preview.CreationKarmaBudget.Total == state.CreationKarmaBudget.Total
               && preview.CreationKarmaBudget.Used == heritageSelection.KarmaCost
               && preview.CreationKarmaBudget.Used >= 0m
               && preview.CreationKarmaBudget.Used <= preview.CreationKarmaBudget.Total
               && preview.CreationKarmaBudget.Remaining
                  == preview.CreationKarmaBudget.Total - preview.CreationKarmaBudget.Used
               && preview.SumToTenTarget == state.Authority.SumToTenTarget
               && preview.BaseNormalAttributePoints == attributeRank.BaseNormalAttributePoints
               && preview.BaseNormalAttributePoints >= 0
               && preview.EffectiveNormalAttributePoints >= 0
               && preview.TotalSpecialAttributePoints >= 0
               && !preview.RequiresMetatypeAttributeAdjustment;
    }

    public void AskRook(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        CharacterCreationWizardSnapshot snapshot = State.CreationWizard
            ?? throw new InvalidOperationException(
                "Rook needs a current character-creation snapshot. Return to the wizard and try again.");
        if (State.WorkspaceId is not { } workspaceId
            || !string.Equals(snapshot.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
            || snapshot.WorkspaceRevision != State.ContentRevision)
        {
            throw new InvalidOperationException(
                "The runner changed before Rook could answer. Refresh the wizard and ask again.");
        }

        _rookConversations.AddGroundedTurn(snapshot, question);
        NotifyChanged();
    }

    private static bool OverviewMatchesFoundationReceipt(
        CharacterOverviewState overview,
        CharacterCreationFoundationInteractionConfirmResult result,
        CharacterCreationFoundationApplyReceipt receipt)
        => result.RefreshedState is { } refreshed
           && overview.WorkspaceId == receipt.WorkspaceId
           && overview.ContentRevision == receipt.ContentRevision
           && overview.SavedRevision == receipt.SavedRevision
           && overview.Profile?.Created == false
           && overview.CreationWizard is { } wizard
           && wizard.WorkspaceRevision == receipt.ContentRevision
           && string.Equals(wizard.SourceDigest, receipt.SourceDigest, StringComparison.Ordinal)
           && overview.CreationFoundation is { } foundation
           && foundation.Binding.WorkspaceId == receipt.WorkspaceId
           && foundation.Binding.ContentRevision == receipt.ContentRevision
           && foundation.Binding.SavedRevision == receipt.SavedRevision
           && string.Equals(foundation.Binding.SourceDigest, receipt.SourceDigest, StringComparison.Ordinal)
           && string.Equals(foundation.SnapshotDigest, refreshed.FoundationSnapshotDigest, StringComparison.Ordinal)
           && foundation.PendingDraft is { } draft
           && draft.DraftRevision == receipt.DraftRevision
           && string.Equals(draft.DraftDigest, receipt.DraftDigest, StringComparison.Ordinal)
           && !draft.CharacterEffectsApplied
           && !receipt.CharacterEffectsApplied;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            RestoreCharacterSettingsCatalog();
            _rosterFavorites = _rosterFavoritePresenter.Load();
            _applicationSettings = _applicationSettingsPresenter.Load();
            await _shellPresenter.InitializeAsync(cancellationToken);
            await _workspaceActivationGate.WaitAsync(cancellationToken);
            try
            {
                await _presenter.InitializeAsync(cancellationToken);
                await RestoreSelectedWorkspaceAsync(cancellationToken);
                await _account.InitializeAsync(cancellationToken);
                await SyncShellAsync(cancellationToken);
                _ = await TryRefreshWorkspaceAuthorityAsync(
                    expectedWorkspaceId: State.WorkspaceId,
                    expectedPayloadSha256: null,
                    cancellationToken);
            }
            finally
            {
                _workspaceActivationGate.Release();
            }
            _surface = _surfaceResolver.Resolve(State, _shellPresenter.State);
            RestorePlayState();
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }

        NotifyChanged();
    }

    public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync(
        CancellationToken cancellationToken = default)
    {
        await _workspaceActivationGate.WaitAsync(cancellationToken);
        AndroidDocument? document = null;
        NativeWorkspaceActivationReceipt? activation = null;
        CharacterWorkspaceId? activatedWorkspaceId = null;
        NativeWorkspaceAuthoritySnapshot? verifiedAuthority = null;
        try
        {
            document = await _documents.OpenAsync(cancellationToken);
            if (document is null)
            {
                return null;
            }
            CharacterOverviewState previousState = State;
            string expectedPayloadSha256 = ComputeExactImportPayloadSha256(document.Content);
            _notice = null;
            await _presenter.ImportAsync(
                WorkspaceImportDocument.FromUtf8Bytes(document.Content, string.Empty, WorkspaceDocumentFormat.NativeXml),
                cancellationToken);
            if (ActivatedNewWorkspace(previousState, State)
                && State.WorkspaceId is { } importedWorkspaceId)
            {
                NativeWorkspaceAuthoritySnapshot? authority = await TryRefreshWorkspaceAuthorityAsync(
                    importedWorkspaceId,
                    expectedPayloadSha256,
                    cancellationToken);
                if (authority is not null)
                {
                    RememberRosterLocator(importedWorkspaceId, document.ContentUri);
                    _notice = $"Opened {document.DisplayName}.";
                    activatedWorkspaceId = importedWorkspaceId;
                    verifiedAuthority = authority;
                }
                else
                {
                    _notice = WorkspaceVerificationUnavailableNotice;
                }
            }
            await SyncShellAsync(cancellationToken);
            RestorePlayState();
            if (activatedWorkspaceId is { } stableWorkspaceId
                && verifiedAuthority?.Matches(State) == true
                && WorkspaceIsActive(State, stableWorkspaceId))
            {
                activation = new(
                    NativeWorkspaceActivationKind.LocalFile,
                    stableWorkspaceId);
            }
        }
        finally
        {
            if (document is not null)
            {
                CryptographicOperations.ZeroMemory(document.Content);
            }
            _workspaceActivationGate.Release();
        }

        NotifyChanged();
        return activation;
    }

    private static bool ActivatedNewWorkspace(
        CharacterOverviewState previous,
        CharacterOverviewState current)
        => current.WorkspaceId is { } currentWorkspace
           && (previous.WorkspaceId is not { } previousWorkspace
               || !string.Equals(previousWorkspace.Value, currentWorkspace.Value, StringComparison.Ordinal));

    private static bool WorkspaceIsActive(
        CharacterOverviewState state,
        CharacterWorkspaceId expectedWorkspaceId)
        => state.WorkspaceId is { } activeWorkspaceId
           && string.Equals(
               expectedWorkspaceId.Value,
               activeWorkspaceId.Value,
               StringComparison.Ordinal);

    public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync(
        AndroidOnlineCharacter character,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        await _workspaceActivationGate.WaitAsync(cancellationToken);
        byte[]? payload = null;
        NativeWorkspaceActivationReceipt? activation = null;
        CharacterWorkspaceId? activatedWorkspaceId = null;
        NativeWorkspaceAuthoritySnapshot? verifiedAuthority = null;
        try
        {
            payload = StrictUtf8.GetBytes(character.Payload);
            CharacterOverviewState previousState = State;
            string expectedPayloadSha256 = Sha256Hex(payload);
            _notice = null;
            await _presenter.ImportAsync(
                WorkspaceImportDocument.FromUtf8Bytes(payload, character.RulesetId, ParseFormat(character.Format)),
                cancellationToken);
            if (ActivatedNewWorkspace(previousState, State)
                && State.WorkspaceId is { } importedWorkspaceId)
            {
                NativeWorkspaceAuthoritySnapshot? authority = await TryRefreshWorkspaceAuthorityAsync(
                    importedWorkspaceId,
                    expectedPayloadSha256,
                    cancellationToken);
                if (authority is not null)
                {
                    RememberRosterLocator(
                        importedWorkspaceId,
                        $"chummer-run://workspace/{Uri.EscapeDataString(character.WorkspaceId)}");
                    _notice = $"Opened {DisplayName(character.Name, character.Alias)}.";
                    activatedWorkspaceId = importedWorkspaceId;
                    verifiedAuthority = authority;
                }
                else
                {
                    _notice = WorkspaceVerificationUnavailableNotice;
                }
            }
            await SyncShellAsync(cancellationToken);
            RestorePlayState();
            if (activatedWorkspaceId is { } stableWorkspaceId
                && verifiedAuthority?.Matches(State) == true
                && WorkspaceIsActive(State, stableWorkspaceId))
            {
                activation = new(
                    NativeWorkspaceActivationKind.OnlineCharacter,
                    stableWorkspaceId);
            }
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
            _workspaceActivationGate.Release();
        }

        NotifyChanged();
        return activation;
    }

    public async Task CreateRunnerAsync(CancellationToken cancellationToken = default)
        => await ExecuteCommandAsync("new_character", cancellationToken);

    public async Task<NativeWorkspaceActivationReceipt?> SwitchWorkspaceAsync(
        OpenWorkspaceState workspace,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            async () =>
            {
                await _presenter.SwitchWorkspaceAsync(workspace.Id, cancellationToken);
                await SyncShellAsync(cancellationToken);
                NativeWorkspaceAuthoritySnapshot? authority = await TryRefreshWorkspaceAuthorityAsync(
                    expectedWorkspaceId: workspace.Id,
                    expectedPayloadSha256: null,
                    cancellationToken);
                RestorePlayState();
                bool authorityRequired = AndroidE2EAuthority.Enabled;

                return WorkspaceIsActive(State, workspace.Id)
                       && (!authorityRequired || authority?.Matches(State) == true)
                    ? new NativeWorkspaceActivationReceipt(
                        NativeWorkspaceActivationKind.WorkspaceSwitch,
                        workspace.Id)
                    : null;
            },
            cancellationToken);

    public async Task CloseWorkspaceAsync(OpenWorkspaceState workspace, CancellationToken cancellationToken = default)
    {
        await WithWorkspaceActivationGateAsync(
            async () =>
            {
                await _presenter.CloseWorkspaceAsync(workspace.Id, cancellationToken);
                await SyncShellAsync(cancellationToken);
                _ = await TryRefreshWorkspaceAuthorityAsync(
                    expectedWorkspaceId: State.WorkspaceId,
                    expectedPayloadSha256: null,
                    cancellationToken);
                RestorePlayState();
            },
            cancellationToken);
    }

    private async Task WithWorkspaceActivationGateAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await _workspaceActivationGate.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            _workspaceActivationGate.Release();
        }
    }

    private async Task<T> WithWorkspaceActivationGateAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await _workspaceActivationGate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _workspaceActivationGate.Release();
        }
    }

    public bool IsRosterFavorite(OpenWorkspaceState workspace)
    {
        CharacterRosterDocumentIdentity identity = ResolveRosterIdentity(workspace);
        return CharacterRosterFavoriteRules.IsFavorite(_rosterFavorites, identity);
    }

    public Task ToggleRosterFavoriteAsync(
        OpenWorkspaceState workspace,
        bool isFavorite,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CharacterRosterDocumentIdentity identity = ResolveRosterIdentity(workspace);
        _rosterFavorites = _rosterFavoritePresenter.Apply(new CharacterRosterFavoriteMutation(
            identity,
            isFavorite,
            expectedRevision));
        _notice = isFavorite
            ? $"{identity.DisplayName} added to favorites."
            : $"{identity.DisplayName} moved to recent runners.";
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SortRosterAsync(
        CharacterRosterSortTarget target,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rosterFavorites = _rosterFavoritePresenter.ApplySort(new CharacterRosterSortMutation(
            target,
            expectedRevision));
        _notice = target switch
        {
            CharacterRosterSortTarget.Favorites => "Favorite runners sorted by document locator.",
            CharacterRosterSortTarget.Recent => "Recent runners sorted by document locator.",
            _ => throw new ArgumentOutOfRangeException(nameof(target), "A known roster sort target is required.")
        };
        NotifyChanged();
        return Task.CompletedTask;
    }

    public bool IsRosterEntry(OpenWorkspaceState workspace, CharacterRosterRemoveTarget target)
    {
        CharacterRosterDocumentIdentity identity = ResolveRosterIdentity(workspace);
        return CharacterRosterFavoriteRules.Contains(_rosterFavorites, identity, target);
    }

    public Task RemoveRosterEntryAsync(
        OpenWorkspaceState workspace,
        CharacterRosterRemoveTarget target,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CharacterRosterDocumentIdentity identity = ResolveRosterIdentity(workspace);
        _rosterFavorites = _rosterFavoritePresenter.ApplyRemove(new CharacterRosterRemoveMutation(
            identity,
            target,
            expectedRevision));
        _notice = target switch
        {
            CharacterRosterRemoveTarget.Favorites => $"{identity.DisplayName} removed from favorites.",
            CharacterRosterRemoveTarget.Recent => $"{identity.DisplayName} removed from recent runners.",
            _ => throw new ArgumentOutOfRangeException(nameof(target), "A known roster removal target is required.")
        };
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveDeleteConfirmationSettingAsync(
        bool confirmDelete,
        long expectedRevision,
        CancellationToken cancellationToken = default)
        => SaveApplicationConfirmationSettingsAsync(
            confirmDelete,
            _applicationSettings.ConfirmKarmaExpense,
            expectedRevision,
            cancellationToken);

    public Task SaveApplicationConfirmationSettingsAsync(
        bool confirmDelete,
        bool confirmKarmaExpense,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _applicationSettings = _applicationSettingsPresenter.ApplySnapshot(
            new ApplicationConfirmationSettingsMutation(
                confirmDelete,
                confirmKarmaExpense,
                expectedRevision));
        _notice = "Application settings saved.";
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SaveApplicationSettingsAsync(
        bool confirmDelete,
        bool confirmKarmaExpense,
        bool customDateTimeFormats,
        string customDateFormat,
        string customTimeFormat,
        bool datesIncludeTime,
        bool hideMasterIndex,
        bool hideCharacterRoster,
        bool searchInCategoryOnly,
        bool allowEasterEggs,
        bool preferNightlyBuilds,
        bool liveUpdateCleanCharacterFiles,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _applicationSettings = _applicationSettingsPresenter.ApplySettingsSnapshot(
            new ApplicationSettingsSnapshotMutation(
                confirmDelete,
                confirmKarmaExpense,
                new(ApplicationSettingIdentity.CustomDateTimeFormats, customDateTimeFormats),
                new(ApplicationSettingIdentity.CustomDateFormat, customDateFormat),
                new(ApplicationSettingIdentity.CustomTimeFormat, customTimeFormat),
                new(ApplicationSettingIdentity.DatesIncludeTime, datesIncludeTime),
                new(ApplicationSettingIdentity.HideMasterIndex, hideMasterIndex),
                new(ApplicationSettingIdentity.HideCharacterRoster, hideCharacterRoster),
                new(ApplicationSettingIdentity.SearchInCategoryOnly, searchInCategoryOnly),
                new(ApplicationSettingIdentity.AllowEasterEggs, allowEasterEggs),
                new(ApplicationSettingIdentity.PreferNightlyBuilds, preferNightlyBuilds),
                new(
                    ApplicationSettingIdentity.LiveUpdateCleanCharacterFiles,
                    liveUpdateCleanCharacterFiles),
                expectedRevision));
        _notice = "Application settings saved.";
        NotifyChanged();
        return Task.CompletedTask;
    }

    private CharacterRosterDocumentIdentity ResolveRosterIdentity(OpenWorkspaceState workspace)
    {
        string locator = Preferences.Default.Get(
            RosterLocatorPreferencePrefix + workspace.Id.Value,
            string.Empty);
        if (string.IsNullOrWhiteSpace(locator))
        {
            locator = $"chummer-workspace://local/{Uri.EscapeDataString(workspace.Id.Value)}";
        }
        string displayName = !string.IsNullOrWhiteSpace(workspace.Alias)
            ? workspace.Alias
            : !string.IsNullOrWhiteSpace(workspace.Name) ? workspace.Name : "Runner";
        return new CharacterRosterDocumentIdentity(locator, displayName);
    }

    private static void RememberRosterLocator(CharacterWorkspaceId? workspaceId, string locator)
    {
        if (workspaceId is null || string.IsNullOrWhiteSpace(locator))
            return;
        Preferences.Default.Set(RosterLocatorPreferencePrefix + workspaceId.Value.Value, locator.Trim());
    }

    public async Task SelectTabAsync(string tabId, CancellationToken cancellationToken = default)
    {
        await _presenter.SelectTabAsync(tabId, cancellationToken);
        await _shellPresenter.SelectTabAsync(tabId, cancellationToken);
        RefreshSurface();
    }

    public async Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ExecuteCommandCoreAsync(commandId, cancellationToken),
            cancellationToken);

    private async Task ExecuteCommandCoreAsync(string commandId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        _notice = null;
        await _presenter.ExecuteCommandAsync(commandId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        await ProcessPendingOutputsAsync(cancellationToken);
    }

    public async Task HandleUiControlAsync(string controlId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        _notice = null;
        await _presenter.HandleUiControlAsync(controlId, cancellationToken);
        await SyncShellAsync(cancellationToken);
        await ProcessPendingOutputsAsync(cancellationToken);
    }

    public async Task ExecuteWorkspaceActionAsync(
        WorkspaceSurfaceActionDefinition action,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ExecuteWorkspaceActionCoreAsync(action, cancellationToken),
            cancellationToken);

    private async Task ExecuteWorkspaceActionCoreAsync(
        WorkspaceSurfaceActionDefinition action,
        CancellationToken cancellationToken)
    {
        await _presenter.ExecuteWorkspaceActionAsync(action, cancellationToken);
        await SyncShellAsync(cancellationToken);
        await ProcessPendingOutputsAsync(cancellationToken);
    }

    public bool IsCommandEnabled(AppCommandDefinition command)
        => _availability.IsCommandEnabled(command, State);

    public bool IsTabEnabled(NavigationTabDefinition tab)
        => _availability.IsNavigationTabEnabled(tab, State);

    public Task UpdateDialogFieldAsync(string fieldId, string? value, CancellationToken cancellationToken = default)
        => _presenter.UpdateDialogFieldAsync(fieldId, value, cancellationToken);

    public async Task ApplyAttributeEditAsync(
        AttributeEditRequest request,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ApplyAttributeEditCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task ApplyAttributeEditCoreAsync(
        AttributeEditRequest request,
        CancellationToken cancellationToken)
    {
        await _presenter.ApplyAttributeEditAsync(request, cancellationToken);
        _notice = State.Error is null ? "Attribute updated." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyOriginDossierEditAsync(
        OriginDossierEditRequest request,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ApplyOriginDossierEditCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task ApplyOriginDossierEditCoreAsync(
        OriginDossierEditRequest request,
        CancellationToken cancellationToken)
    {
        await _presenter.ApplyOriginDossierEditAsync(request, cancellationToken);
        _notice = State.Error is null ? "Dossier updated." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyCharacterNotesEditAsync(
        CharacterNotesEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while its notes were open. Reopen Notes before saving.");
        }

        CharacterProfileSection profile = State.Profile
            ?? throw new InvalidOperationException("Open a runner before editing Notes.");
        await _presenter.UpdateMetadataAsync(
            new UpdateWorkspaceMetadata(profile.Name, profile.Alias, request.CharacterNotes)
            {
                GameNotes = request.GameNotes,
                GroupNotes = request.GroupNotes
            },
            cancellationToken);
        if (State.Error is not null)
        {
            return;
        }

        await _presenter.SaveAsync(cancellationToken);
        if (State.Error is null)
        {
            _characterNotesWorkspaceId = State.WorkspaceId;
            _characterNotesRevision = State.ContentRevision;
            _characterNotes = request.CharacterNotes;
            _gameNotes = request.GameNotes;
            _groupNotes = request.GroupNotes;
        }
        _notice = State.Error is null ? "Notes saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyCollectionMutationAsync(
        WorkspaceCollectionMutationRequest request,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ApplyCollectionMutationCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task ApplyCollectionMutationCoreAsync(
        WorkspaceCollectionMutationRequest request,
        CancellationToken cancellationToken)
    {
        await _presenter.ApplyCollectionMutationAsync(request, cancellationToken);
        _notice = State.Error is null ? "Runner item updated." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task AttachLinkedCharacterAsync(
        WorkspaceCollectionItemTarget target,
        CancellationToken cancellationToken = default)
    {
        WorkspaceCollectionItemEditorState item = ResolveCollectionItem(target);
        WorkspaceLinkedCharacterState linked = item.LinkedCharacter
            ?? throw new InvalidOperationException("This runner item does not support linked characters.");
        if (!linked.CanAttach)
        {
            throw new InvalidOperationException("This runner's exact Chummer5 link rules are unavailable.");
        }

        AndroidStagedLinkedCharacter? staged = await _linkedCharacters.StageAsync(target, cancellationToken);
        if (staged is null)
        {
            return;
        }

        bool sameAsPrior = PathsEqual(staged.FileName, linked.FileName);
        try
        {
            await ApplyCollectionMutationAsync(
                new WorkspaceSetLinkedCharacterRequest(
                    target,
                    staged.FileName,
                    staged.RelativeFileName,
                    staged.DisplayName,
                    staged.Identity),
                cancellationToken);
            if (State.Error is not null)
            {
                if (!sameAsPrior)
                {
                    await _linkedCharacters.DeleteOwnedAsync(target, staged.FileName, CancellationToken.None);
                }
                return;
            }

            if (!sameAsPrior)
            {
                await _linkedCharacters.DeleteOwnedAsync(target, linked.FileName, CancellationToken.None);
            }
            _notice = $"Linked {staged.Identity.CharacterName}.";
            NotifyChanged();
        }
        catch
        {
            if (!sameAsPrior)
            {
                await _linkedCharacters.DeleteOwnedAsync(target, staged.FileName, CancellationToken.None);
            }
            throw;
        }
    }

    public async Task RemoveLinkedCharacterAsync(
        WorkspaceCollectionItemTarget target,
        CancellationToken cancellationToken = default)
    {
        WorkspaceCollectionItemEditorState item = ResolveCollectionItem(target);
        WorkspaceLinkedCharacterState linked = item.LinkedCharacter
            ?? throw new InvalidOperationException("This runner item does not support linked characters.");
        if (!linked.CanRemove)
        {
            throw new InvalidOperationException("This runner item is not linked to another character.");
        }

        await ApplyCollectionMutationAsync(new WorkspaceRemoveLinkedCharacterRequest(target), cancellationToken);
        if (State.Error is not null)
        {
            return;
        }

        await _linkedCharacters.DeleteOwnedAsync(target, linked.FileName, CancellationToken.None);
        _notice = "Linked runner removed.";
        NotifyChanged();
    }

    private WorkspaceCollectionItemEditorState ResolveCollectionItem(WorkspaceCollectionItemTarget target)
        => State.ActiveCollectionEditor?.Items.FirstOrDefault(item =>
                CollectionItemEditorPage.TargetsMatch(item.Target, target))
            ?? throw new InvalidOperationException(
                "This runner item no longer has a unique stable identity. Reload the section before editing.");

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public async Task ApplyConditionMonitorEditAsync(
        ConditionMonitorEditRequest request,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ApplyConditionMonitorEditCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task ApplyConditionMonitorEditCoreAsync(
        ConditionMonitorEditRequest request,
        CancellationToken cancellationToken)
    {
        await _presenter.ApplyConditionMonitorEditAsync(request, cancellationToken);
        _notice = State.Error is null ? "Damage track updated." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerReputationEditorState?> PrepareCareerReputationEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerReputationEditAsync(cancellationToken);

    public async Task ApplyCareerReputationEditAsync(
        CareerReputationEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while reputation was open. Reopen Reputation before saving.");
        }

        await _presenter.ApplyCareerReputationEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Reputation saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyBurnStreetCredAsync(
        BurnStreetCredRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while reputation was open. Reopen Reputation before burning Street Cred.");
        }

        await _presenter.ApplyBurnStreetCredAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "2 Street Cred burned." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<SituationalModifiersEditorState?> PrepareSituationalModifiersEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareSituationalModifiersEditAsync(cancellationToken);

    public async Task ApplySituationalModifiersEditAsync(
        SituationalModifiersEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while situational modifiers were open. Reopen them before saving.");
        }

        await _presenter.ApplySituationalModifiersEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Situational modifiers saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<PrimaryArmEditorState?> PreparePrimaryArmEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PreparePrimaryArmEditAsync(cancellationToken);

    public async Task ApplyPrimaryArmEditAsync(
        PrimaryArmEditRequest request,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ApplyPrimaryArmEditCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task ApplyPrimaryArmEditCoreAsync(
        PrimaryArmEditRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Primary Arm was open. Reopen it before saving.");
        }

        await _presenter.ApplyPrimaryArmEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Primary arm saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerMugshotEditorState?> PrepareCareerMugshotEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerMugshotEditAsync(cancellationToken);

    public async Task ApplyCareerMugshotMainEditAsync(
        CareerMugshotMainEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Mugshots was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerMugshotMainEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Main Mugshot state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyCareerMugshotDeleteAsync(
        CareerMugshotDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Mugshots was open. Reopen it before deleting.");
        }

        await _presenter.ApplyCareerMugshotDeleteAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Selected mugshot deleted." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CreationMugshotEditorState?> PrepareCreationMugshotEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCreationMugshotEditAsync(cancellationToken);

    public async Task ApplyCreationMugshotMainEditAsync(
        CreationMugshotMainEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This Creation runner changed while Mugshots was open. Reopen it before saving.");
        }

        await _presenter.ApplyCreationMugshotMainEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Creation Main Mugshot state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyCreationMugshotDeleteAsync(
        CreationMugshotDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This Creation runner changed while Mugshots was open. Reopen it before deleting.");
        }

        await _presenter.ApplyCreationMugshotDeleteAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Selected Creation mugshot deleted." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GroupMembershipEditorState?> PrepareGroupMembershipEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGroupMembershipEditAsync(cancellationToken);

    public async Task ApplyGroupMembershipEditAsync(
        GroupMembershipEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Group Membership was open. Reopen it before saving.");
        }

        await _presenter.ApplyGroupMembershipEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Group membership saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GroupNameEditorState?> PrepareGroupNameEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGroupNameEditAsync(cancellationToken);

    public async Task ApplyGroupNameEditAsync(
        GroupNameEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Group Name was open. Reopen it before saving.");
        }

        await _presenter.ApplyGroupNameEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Group name saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<TraditionNameEditorState?> PrepareTraditionNameEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareTraditionNameEditAsync(cancellationToken);

    public async Task ApplyTraditionNameEditAsync(
        TraditionNameEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Tradition Name was open. Reopen it before saving.");
        }

        await _presenter.ApplyTraditionNameEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Tradition name saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<TraditionDrainEditorState?> PrepareTraditionDrainEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareTraditionDrainEditAsync(cancellationToken);

    public async Task ApplyTraditionDrainEditAsync(
        TraditionDrainEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Tradition Drain was open. Reopen it before saving.");
        }

        await _presenter.ApplyTraditionDrainEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Tradition drain attributes saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<TraditionSpiritCategoryEditorState?> PrepareTraditionSpiritCategoryEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareTraditionSpiritCategoryEditAsync(cancellationToken);

    public async Task ApplyTraditionSpiritCategoryEditAsync(
        TraditionSpiritCategoryEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Spirit Categories was open. Reopen it before saving.");
        }

        await _presenter.ApplyTraditionSpiritCategoryEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Tradition spirit categories saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<ArmorTreeFlagEditorState?> PrepareArmorTreeFlagEditAsync(
        Guid armorId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareArmorTreeFlagEditAsync(armorId, cancellationToken);

    public async Task ApplyArmorTreeFlagEditAsync(
        ArmorTreeFlagEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Armor Flags was open. Reopen it before saving.");
        }

        await _presenter.ApplyArmorTreeFlagEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Armor-tree flags saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GearStolenEditorState?> PrepareGearStolenEditAsync(
        Guid rootGearId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGearStolenEditAsync(rootGearId, cancellationToken);

    public async Task ApplyGearStolenEditAsync(
        GearStolenEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Gear Stolen was open. Reopen it before saving.");
        }

        await _presenter.ApplyGearStolenEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear Stolen state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<WeaponStolenEditorState?> PrepareWeaponStolenEditAsync(
        Guid rootWeaponId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareWeaponStolenEditAsync(rootWeaponId, cancellationToken);

    public async Task ApplyWeaponStolenEditAsync(
        WeaponStolenEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Weapon Stolen was open. Reopen it before saving.");
        }

        await _presenter.ApplyWeaponStolenEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Weapon Stolen state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GearEquipmentEditorState?> PrepareGearEquipmentEditAsync(
        Guid rootGearId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGearEquipmentEditAsync(rootGearId, cancellationToken);

    public async Task ApplyGearEquipmentEditAsync(
        GearEquipmentEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Gear Equipped was open. Reopen it before saving.");
        }

        await _presenter.ApplyGearEquipmentEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear Equipped state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<VehicleEquipmentInstalledEditorState?> PrepareVehicleEquipmentInstalledEditAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareVehicleEquipmentInstalledEditAsync(vehicleId, cancellationToken);

    public async Task ApplyVehicleEquipmentInstalledEditAsync(
        VehicleEquipmentInstalledEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Vehicle Installed was open. Reopen it before saving.");
        }

        await _presenter.ApplyVehicleEquipmentInstalledEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Vehicle Installed state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<VehicleDataProcessingFirewallSwapEditorState?> PrepareVehicleDataProcessingFirewallSwapEditAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareVehicleDataProcessingFirewallSwapEditAsync(vehicleId, cancellationToken);

    public async Task ApplyVehicleDataProcessingFirewallSwapEditAsync(
        VehicleDataProcessingFirewallSwapEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
            throw new InvalidOperationException(
                "This runner changed while Vehicle Matrix swapping was open. Reopen it.");
        await _presenter.ApplyVehicleDataProcessingFirewallSwapEditAsync(request, cancellationToken);
        if (State.Error is null) await _presenter.SaveAsync(cancellationToken);
        _notice = State.Error is null ? "Vehicle Matrix values swapped." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CyberwareMatrixSwapEditorState?> PrepareCyberwareMatrixSwapEditAsync(
        Guid cyberwareId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCyberwareMatrixSwapEditAsync(cyberwareId, cancellationToken);

    public async Task ApplyCyberwareMatrixSwapEditAsync(
        CyberwareMatrixSwapEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Cyberware Matrix swapping was open. Reopen it.");
        }

        await _presenter.ApplyCyberwareMatrixSwapEditAsync(request, cancellationToken);
        if (State.Error is null)
            await _presenter.SaveAsync(cancellationToken);
        _notice = State.Error is null ? "Cyberware Matrix values swapped." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<WeaponMatrixSwapEditorState?> PrepareWeaponMatrixSwapEditAsync(
        Guid weaponId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareWeaponMatrixSwapEditAsync(weaponId, cancellationToken);

    public async Task ApplyWeaponMatrixSwapEditAsync(
        WeaponMatrixSwapEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.Profile?.Created != true
            || State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This Career runner changed while Weapon Matrix swapping was open. Reopen it.");
        }

        await _presenter.ApplyWeaponMatrixSwapEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Weapon Matrix values swapped." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerWeaponFireEditorState?> PrepareCareerWeaponFireAsync(
        Guid weaponId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerWeaponFireAsync(weaponId, cancellationToken);

    public Task<CareerWeaponFireCatalogEditorState?> PrepareCareerWeaponFireCatalogAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerWeaponFireCatalogAsync(cancellationToken);

    public async Task ApplyCareerWeaponFireAsync(
        CareerWeaponFireRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.Profile?.Created != true
            || State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This Career runner changed while Weapon firing was open. Reopen it.");
        }

        await _presenter.ApplyCareerWeaponFireAsync(request, cancellationToken)
            .ConfigureAwait(false);
        bool exactMutationApplied = State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && request.ExpectedContentRevision < long.MaxValue
            && State.ContentRevision == request.ExpectedContentRevision + 1
            && State.IsDirty;
        long appliedContentRevision = exactMutationApplied ? State.ContentRevision : 0;
        if (exactMutationApplied)
        {
            await _presenter.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        bool durableState = exactMutationApplied
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && State.ContentRevision == appliedContentRevision
            && State.SavedRevision == appliedContentRevision
            && !State.IsDirty;
        NativeWorkspaceAuthoritySnapshot? authority = durableState
            ? await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: request.WorkspaceId,
                expectedPayloadSha256: null,
                cancellationToken).ConfigureAwait(false)
            : null;
        bool persisted = durableState
            && (!AndroidE2EAuthority.Enabled
                || authority is not null && authority.Matches(State));
        _notice = persisted ? "Weapon ammo updated." : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
    }

    public Task<VehicleWeaponFiringModeEditorState?> PrepareVehicleWeaponFiringModeEditAsync(
        Guid vehicleId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareVehicleWeaponFiringModeEditAsync(vehicleId, cancellationToken);

    public async Task ApplyVehicleWeaponFiringModeEditAsync(
        VehicleWeaponFiringModeEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Vehicle Weapon firing-mode editing was open. Reopen it.");
        }

        await _presenter.ApplyVehicleWeaponFiringModeEditAsync(request, cancellationToken);
        if (State.Error is null)
            await _presenter.SaveAsync(cancellationToken);
        _notice = State.Error is null ? "Vehicle Weapon firing mode saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GearOverclockerEditorState?> PrepareGearOverclockerEditAsync(
        Guid rootGearId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGearOverclockerEditAsync(rootGearId, cancellationToken);

    public async Task ApplyGearOverclockerEditAsync(
        GearOverclockerEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Gear Overclocker was open. Reopen it before saving.");
        }

        await _presenter.ApplyGearOverclockerEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear Overclocker attribute saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GearAttackSwapEditorState?> PrepareGearAttackSwapEditAsync(
        Guid rootGearId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGearAttackSwapEditAsync(rootGearId, cancellationToken);

    public async Task ApplyGearAttackSwapEditAsync(
        GearAttackSwapEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
            throw new InvalidOperationException(
                "This runner changed while Gear Attack was open. Reopen it before saving.");

        await _presenter.ApplyGearAttackSwapEditAsync(request, cancellationToken);
        if (State.Error is null)
            await _presenter.SaveAsync(cancellationToken);
        _notice = State.Error is null ? "Gear Attack value swapped." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GearSleazeSwapEditorState?> PrepareGearSleazeSwapEditAsync(Guid rootGearId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGearSleazeSwapEditAsync(rootGearId, cancellationToken);

    public async Task ApplyGearSleazeSwapEditAsync(GearSleazeSwapEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId || State.ContentRevision != request.ExpectedContentRevision)
            throw new InvalidOperationException("This runner changed while Gear Sleaze was open. Reopen it.");
        await _presenter.ApplyGearSleazeSwapEditAsync(request, cancellationToken);
        if (State.Error is null) await _presenter.SaveAsync(cancellationToken);
        _notice = State.Error is null ? "Gear Sleaze value swapped." : null;
        await SyncShellAsync(cancellationToken); NotifyChanged();
    }

    public Task<GearDataProcessingFirewallSwapEditorState?> PrepareGearDataProcessingFirewallSwapEditAsync(
        Guid rootGearId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGearDataProcessingFirewallSwapEditAsync(rootGearId, cancellationToken);

    public async Task ApplyGearDataProcessingFirewallSwapEditAsync(
        GearDataProcessingFirewallSwapEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Gear Data Processing or Firewall was open. Reopen it.");
        }

        await _presenter.ApplyGearDataProcessingFirewallSwapEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear Data Processing or Firewall value swapped." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<GearWirelessEditorState?> PrepareGearWirelessEditAsync(
        Guid rootGearId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareGearWirelessEditAsync(rootGearId, cancellationToken);

    public async Task ApplyGearWirelessEditAsync(
        GearWirelessEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Gear Wireless was open. Reopen it before saving.");
        }

        await _presenter.ApplyGearWirelessEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear Wireless state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<ImprovementActiveEditorState?> PrepareImprovementActiveEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareImprovementActiveEditAsync(cancellationToken);

    public async Task ApplyImprovementActiveEditAsync(
        ImprovementActiveEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Improvement Active was open. Reopen it before saving.");
        }

        await _presenter.ApplyImprovementActiveEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Improvement active state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<ImprovementNotesEditorState?> PrepareImprovementNotesEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareImprovementNotesEditAsync(cancellationToken);

    public async Task ApplyImprovementNotesEditAsync(
        ImprovementNotesEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Improvement notes were open. Reopen them before saving.");
        }

        await _presenter.ApplyImprovementNotesEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Improvement notes saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<ImprovementGroupActiveEditorState?> PrepareImprovementGroupActiveEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareImprovementGroupActiveEditAsync(cancellationToken);

    public async Task ApplyImprovementGroupActiveEditAsync(
        ImprovementGroupActiveEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Improvement groups was open. Reopen it before saving.");
        }

        await _presenter.ApplyImprovementGroupActiveEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Improvement group states saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<ImprovementGroupAddEditorState?> PrepareImprovementGroupAddAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareImprovementGroupAddAsync(cancellationToken);

    public async Task ApplyImprovementGroupAddAsync(
        ImprovementGroupAddRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Add Improvement Group was open. Reopen it before saving.");
        }

        await _presenter.ApplyImprovementGroupAddAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Improvement group added." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<FreeSpriteConversionEditorState?> PrepareFreeSpriteConversionAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareFreeSpriteConversionAsync(cancellationToken);

    public async Task ApplyFreeSpriteConversionAsync(
        FreeSpriteConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Convert to Free Sprite was open. Reopen it before saving.");
        }

        await _presenter.ApplyFreeSpriteConversionAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Converted to Free Sprite." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<MartialArtNotesEditorState?> PrepareMartialArtNotesEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareMartialArtNotesEditAsync(cancellationToken);

    public async Task ApplyMartialArtNotesEditAsync(
        MartialArtNotesEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Martial Arts notes was open. Reopen it before saving.");
        }
        await _presenter.ApplyMartialArtNotesEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Martial Arts notes saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<MartialArtDeleteEditorState?> PrepareMartialArtDeleteAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareMartialArtDeleteAsync(cancellationToken);

    public async Task ApplyMartialArtDeleteAsync(
        MartialArtDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirmed)
        {
            throw new InvalidOperationException("Martial Art deletion requires explicit confirmation.");
        }
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Martial Art deletion was open. Reopen it before saving.");
        }
        await _presenter.ApplyMartialArtDeleteAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Martial Art or Technique deleted." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CreationLifestyleDeleteEditorState?> PrepareCreationLifestyleDeleteAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCreationLifestyleDeleteAsync(cancellationToken);

    public async Task ApplyCreationLifestyleDeleteAsync(
        CreationLifestyleDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirmed)
        {
            throw new InvalidOperationException("Lifestyle deletion requires explicit confirmation authority.");
        }
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Lifestyle deletion was open. Reopen it before deleting.");
        }
        await _presenter.ApplyCreationLifestyleDeleteAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Lifestyle deleted." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerEdgeUseEditorState?> PrepareCareerEdgeUseEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerEdgeUseEditAsync(cancellationToken);

    public async Task ApplyCareerEdgeUseEditAsync(
        CareerEdgeUseEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Edge use was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerEdgeUseEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Edge use saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerManualKarmaEditorState?> PrepareCareerManualKarmaEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerManualKarmaEditAsync(cancellationToken);

    public async Task ApplyCareerManualKarmaEditAsync(
        CareerManualKarmaEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while manual Karma was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerManualKarmaEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Manual Karma saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerManualNuyenEditorState?> PrepareCareerManualNuyenEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerManualNuyenEditAsync(cancellationToken);

    public async Task ApplyCareerManualNuyenEditAsync(
        CareerManualNuyenEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while manual Nuyen was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerManualNuyenEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Manual Nuyen saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerCreateExpenseEditorState?> PrepareCareerCreateExpenseEditAsync(
        CharacterCareerCreateExpenseOperation operation,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerCreateExpenseEditAsync(operation, cancellationToken);

    public async Task ApplyCareerCreateExpenseEditAsync(
        CareerCreateExpenseEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Create Expense was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerCreateExpenseEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Expense saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerNuyenExpenseEditorState?> PrepareCareerNuyenExpenseEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerNuyenExpenseEditAsync(cancellationToken);

    public async Task ApplyCareerNuyenExpenseEditAsync(
        CareerNuyenExpenseEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while a Nuyen expense was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerNuyenExpenseEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Nuyen expense saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CareerKarmaExpenseEditorState?> PrepareCareerKarmaExpenseEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerKarmaExpenseEditAsync(cancellationToken);

    public async Task<bool> ApplyCareerKarmaExpenseEditAsync(
        CareerKarmaExpenseEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string currentReasonNormalizationLanguage = DesktopLocalizationCatalog.NormalizeOrDefault(
            State.Preferences.Language);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision
            || !string.Equals(
                request.ExpectedReasonNormalizationLanguage,
                currentReasonNormalizationLanguage,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This runner changed while a Karma expense was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerKarmaExpenseEditAsync(request, cancellationToken);
        bool exactMutationApplied = State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && request.ExpectedContentRevision < long.MaxValue
            && State.ContentRevision == request.ExpectedContentRevision + 1
            && State.IsDirty;
        long appliedContentRevision = exactMutationApplied ? State.ContentRevision : 0;
        if (exactMutationApplied)
        {
            await _presenter.SaveAsync(cancellationToken);
        }

        bool durableState = exactMutationApplied
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && State.ContentRevision == appliedContentRevision
            && State.SavedRevision == appliedContentRevision
            && !State.IsDirty;
        NativeWorkspaceAuthoritySnapshot? authority = durableState
            ? await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: request.WorkspaceId,
                expectedPayloadSha256: null,
                cancellationToken)
            : null;
        bool persisted = durableState
            && (!AndroidE2EAuthority.Enabled
                || authority is not null && authority.Matches(State));
        _notice = persisted ? "Karma expense saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
        return persisted;
    }

    public Task<CareerCalendarEditorState?> PrepareCareerCalendarEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerCalendarEditAsync(cancellationToken);

    public async Task<bool> ApplyCareerCalendarAddAsync(
        CareerCalendarAddRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCareerCalendarMutationAsync(
            request.WorkspaceId,
            request.ExpectedContentRevision,
            token => _presenter.ApplyCareerCalendarAddAsync(request, token),
            "Calendar week added.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ApplyCareerCalendarEditAsync(
        CareerCalendarEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCareerCalendarMutationAsync(
            request.WorkspaceId,
            request.ExpectedContentRevision,
            token => _presenter.ApplyCareerCalendarEditAsync(request, token),
            "Calendar week saved.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ApplyCareerCalendarDeleteAsync(
        CareerCalendarDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ApplyCareerCalendarMutationAsync(
            request.WorkspaceId,
            request.ExpectedContentRevision,
            token => _presenter.ApplyCareerCalendarDeleteAsync(request, token),
            "Calendar week deleted.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ApplyCareerCalendarMutationAsync(
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        Func<CancellationToken, Task> apply,
        string successNotice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (State.WorkspaceId != workspaceId
            || State.ContentRevision != expectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while the calendar was open. Reopen it before saving.");
        }

        await apply(cancellationToken).ConfigureAwait(false);
        bool exactMutationApplied = State.Error is null
            && State.WorkspaceId == workspaceId
            && expectedContentRevision < long.MaxValue
            && State.ContentRevision == expectedContentRevision + 1
            && State.IsDirty;
        long appliedContentRevision = exactMutationApplied ? State.ContentRevision : 0;
        if (exactMutationApplied)
        {
            await _presenter.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        bool durableState = exactMutationApplied
            && State.Error is null
            && State.WorkspaceId == workspaceId
            && State.ContentRevision == appliedContentRevision
            && State.SavedRevision == appliedContentRevision
            && !State.IsDirty;
        NativeWorkspaceAuthoritySnapshot? authority = durableState
            ? await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: workspaceId,
                expectedPayloadSha256: null,
                cancellationToken).ConfigureAwait(false)
            : null;
        bool persisted = durableState
            && (!AndroidE2EAuthority.Enabled
                || authority is not null && authority.Matches(State));
        _notice = persisted ? successNotice : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return persisted;
    }

    public Task<CareerActiveSkillAdvanceEditorState?> PrepareCareerActiveSkillAdvanceAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerActiveSkillAdvanceAsync(cancellationToken);

    public async Task<bool> ApplyCareerActiveSkillAdvanceAsync(
        CareerActiveSkillAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while active-skill advancement was open. Reopen it before saving.");
        }

        await _presenter.ApplyCareerActiveSkillAdvanceAsync(request, cancellationToken)
            .ConfigureAwait(false);
        bool exactMutationApplied = State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && request.ExpectedContentRevision < long.MaxValue
            && State.ContentRevision == request.ExpectedContentRevision + 1
            && State.IsDirty;
        long appliedContentRevision = exactMutationApplied ? State.ContentRevision : 0;
        if (exactMutationApplied)
        {
            await _presenter.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        bool durableState = exactMutationApplied
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && State.ContentRevision == appliedContentRevision
            && State.SavedRevision == appliedContentRevision
            && !State.IsDirty;
        NativeWorkspaceAuthoritySnapshot? authority = durableState
            ? await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: request.WorkspaceId,
                expectedPayloadSha256: null,
                cancellationToken).ConfigureAwait(false)
            : null;
        bool persisted = durableState
            && (!AndroidE2EAuthority.Enabled
                || authority is not null && authority.Matches(State));
        _notice = persisted ? "Active skill advanced and Karma expense saved." : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return persisted;
    }

    public Task<CareerAttributeAdvanceEditorState?> PrepareCareerAttributeAdvanceAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerAttributeAdvanceAsync(cancellationToken);

    public Task<CareerKnowledgeSkillAdvanceEditorState?> PrepareCareerKnowledgeSkillAdvanceAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerKnowledgeSkillAdvanceAsync(cancellationToken);

    public Task<CareerSkillGroupAdvanceEditorState?> PrepareCareerSkillGroupAdvanceAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerSkillGroupAdvanceAsync(cancellationToken);

    public Task<CareerSkillSpecializationEditorState?> PrepareCareerSkillSpecializationAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerSkillSpecializationAsync(cancellationToken);

    /// <summary>
    /// Loads only governed run identities and Core quote bindings. Until both
    /// the Run Services catalog and the concrete Core workspace adapter are
    /// composed, the route remains visible but explicitly unavailable.
    /// </summary>
    public async Task<Sr5AfterRunSettlementEditorState> PrepareAfterRunSettlementAsync(
        CancellationToken cancellationToken = default)
    {
        Sr5CareerRunnerBinding before = new(
            State.Profile?.Created == true,
            State.Rules?.GameEdition,
            State.WorkspaceId,
            State.ContentRevision,
            State.SavedRevision,
            State.IsDirty,
            State.Error);
        Sr5CareerRunnerGuard.RequireCreated(before);
        if (before.WorkspaceId is not { } workspaceId
            || before.SavedRevision != before.ContentRevision
            || before.IsDirty
            || !string.IsNullOrWhiteSpace(before.Error))
        {
            throw new InvalidOperationException(
                "After Run settlement requires an exact clean saved SR5 runner revision.");
        }

        ICharacterAfterRunSettlementService? service = _afterRunSettlementService;
        IAndroidAfterRunProposalCatalog? catalog = _afterRunProposalCatalog;
        if (service is null || catalog is null)
        {
            return Sr5AfterRunSettlementEditorState.Unavailable(
                workspaceId,
                before.ContentRevision,
                "The governed After Run proposal catalog and atomic workspace adapter are not composed in this build. No fallback mutation is available.");
        }

        Sr5AfterRunProposalCatalogResult projected = await Task.Run(
            () => catalog.Load(workspaceId),
            cancellationToken).ConfigureAwait(false);
        if (projected is null
            || !Enum.IsDefined(projected.Status)
            || projected.Entries is null
            || projected.Blockers is null
            || projected.OmittedProposalCount < 0
            || projected.OmittedProposalCount
                > Sr5AfterRunProposalCatalogContract.MaximumProposalCount
            || projected.Entries.Count
                > Sr5AfterRunProposalCatalogContract.MaximumProposalCount
            || projected.Blockers.Any(blocker =>
                string.IsNullOrWhiteSpace(blocker)
                || blocker.Length
                    > CharacterAfterRunSettlementRules.MaximumTextLength)
            || projected.Status == Sr5AfterRunCatalogStatus.Available
                && projected.Blockers.Count > 0)
        {
            return new Sr5AfterRunSettlementEditorState(
                workspaceId,
                before.ContentRevision,
                Sr5AfterRunCatalogStatus.Corrupt,
                [],
                0,
                ["The governed After Run proposal catalog returned an invalid projection."]);
        }
        if (projected.Status != Sr5AfterRunCatalogStatus.Available)
        {
            string blocker = projected.Blockers.FirstOrDefault()
                ?? "The governed After Run proposal catalog is unavailable.";
            return new Sr5AfterRunSettlementEditorState(
                workspaceId,
                before.ContentRevision,
                projected.Status,
                [],
                projected.OmittedProposalCount,
                [blocker]);
        }
        if (projected.Entries.Count == 0
            || projected.Entries.Any(entry =>
                entry is null
                || entry.Identity.ProposalId == Guid.Empty
                || entry.Identity.RunId == Guid.Empty
                || entry.Identity.CharacterId == Guid.Empty
                || entry.RewardContext is null
                || !entry.RewardContext.IsExact())
            || projected.Entries.Select(entry => entry.Identity).Distinct().Count()
                != projected.Entries.Count)
        {
            return new Sr5AfterRunSettlementEditorState(
                workspaceId,
                before.ContentRevision,
                Sr5AfterRunCatalogStatus.Corrupt,
                [],
                projected.OmittedProposalCount,
                ["The governed After Run catalog contains an invalid or duplicate typed proposal identity."]);
        }

        var candidates = new List<Sr5AfterRunSettlementCandidate>();
        int omitted = projected.OmittedProposalCount;
        foreach (Sr5AfterRunProposalCatalogEntry entry in projected.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CharacterAfterRunSettlementQuoteResult quoted = await Task.Run(
                () => service.Quote(new CharacterAfterRunSettlementQuoteRequest(
                    workspaceId,
                    entry.Identity)),
                cancellationToken).ConfigureAwait(false);
            if (quoted.Outcome != CharacterAfterRunSettlementServiceOutcome.Available
                || quoted.Binding is not { } binding
                || binding.WorkspaceId != workspaceId
                || binding.WorkspaceRevision != before.ContentRevision
                || binding.Identity != entry.Identity)
            {
                omitted = checked(omitted + 1);
                continue;
            }
            var candidate = new Sr5AfterRunSettlementCandidate(
                entry.RewardContext,
                binding);
            if (!candidate.IsExact(workspaceId, before.ContentRevision))
            {
                omitted = checked(omitted + 1);
                continue;
            }
            candidates.Add(candidate);
        }

        Sr5CareerRunnerBinding after = new(
            State.Profile?.Created == true,
            State.Rules?.GameEdition,
            State.WorkspaceId,
            State.ContentRevision,
            State.SavedRevision,
            State.IsDirty,
            State.Error);
        if (after != before)
        {
            throw new InvalidOperationException(
                "The saved runner changed while After Run proposals were being quoted.");
        }
        if (candidates.Count == 0)
        {
            return Sr5AfterRunSettlementEditorState.Unavailable(
                workspaceId,
                before.ContentRevision,
                "No exact unsettled completed-run proposal is available for this runner revision.") with
            {
                OmittedProposalCount = omitted
            };
        }
        var editor = new Sr5AfterRunSettlementEditorState(
            workspaceId,
            before.ContentRevision,
            Sr5AfterRunCatalogStatus.Available,
            candidates,
            omitted,
            []);
        if (!editor.IsExact())
        {
            throw new InvalidOperationException(
                "The After Run editor projection failed exact identity and digest validation.");
        }
        return editor;
    }

    public bool SupportsManualAfterRunProposalEntry
        => _afterRunProposalCatalog is ISr5AfterRunManualProposalAuthority;

    /// <summary>
    /// Publishes one fully typed manual run result through the Android host
    /// authority. The authority independently re-reads and digest-binds the
    /// exact clean workspace before Core can see the proposal.
    /// </summary>
    public async Task<Sr5AfterRunManualProposalPublishResult>
        PublishManualAfterRunProposalAsync(
            Sr5AfterRunManualProposalSubmission submission,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (_afterRunProposalCatalog is not ISr5AfterRunManualProposalAuthority authority
            || State.WorkspaceId is not { } workspaceId
            || submission.WorkspaceId != workspaceId
            || submission.ExpectedWorkspaceRevision != State.ContentRevision
            || State.SavedRevision != State.ContentRevision
            || State.IsDirty
            || !string.IsNullOrWhiteSpace(State.Error))
        {
            return new(
                Published: false,
                Replayed: false,
                Proposal: null,
                "Manual After Run entry does not own the exact current clean saved runner revision.");
        }
        Sr5AfterRunManualProposalPublishResult result = await Task.Run(
            () => authority.Publish(submission),
            cancellationToken).ConfigureAwait(false);
        if (State.WorkspaceId != workspaceId
            || State.ContentRevision != submission.ExpectedWorkspaceRevision
            || State.SavedRevision != submission.ExpectedWorkspaceRevision
            || State.IsDirty
            || !string.IsNullOrWhiteSpace(State.Error))
        {
            throw new InvalidOperationException(
                "The saved runner changed while the manual After Run proposal was being registered.");
        }
        return result;
    }

    public Task<CharacterCareerSkillSpecializationQuote?> PrepareCareerSkillSpecializationQuoteAsync(
        CareerSkillSpecializationQuoteRequest request,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCareerSkillSpecializationQuoteAsync(request, cancellationToken);

    public async Task<Sr5CareerSpecializationApplyObservation?> ApplyCareerSkillSpecializationAsync(
        CareerSkillSpecializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision
            || State.SavedRevision != request.ExpectedContentRevision
            || State.IsDirty
            || !string.IsNullOrWhiteSpace(State.Error)
            || !CharacterCareerSkillSpecializationRules.IsCoherent(request.ExpectedQuote)
            || !request.ExpectedQuote.CanAdd
            || !string.Equals(request.ExpectedQuote.CharacterRevision, request.ExpectedCharacterRevision, StringComparison.Ordinal)
            || !string.Equals(request.ExpectedQuote.SourceRevision, request.ExpectedSourceRevision, StringComparison.Ordinal)
            || !string.Equals(request.ExpectedQuote.RuleDigest, request.ExpectedRuleDigest, StringComparison.Ordinal)
            || !string.Equals(request.ExpectedQuote.LogicalRevision, request.ExpectedLogicalRevision, StringComparison.Ordinal)
            || !CharacterCareerSkillSpecializationRules.TryPlanAdd(
                request.ExpectedQuote,
                request.ExpectedCharacterRevision,
                request.ExpectedSourceRevision,
                request.ExpectedRuleDigest,
                request.ExpectedLogicalRevision,
                request.Confirmed,
                request.SpecializationId,
                request.ExpenseId,
                request.ExpenseDateLocal,
                out CharacterCareerSkillSpecializationPlan plan))
        {
            throw new InvalidOperationException(
                "Core rejected the specialization identity, selection, confirmation, IDs, date, Karma, or four-revision CAS before mutation.");
        }

        await _presenter.ApplyCareerSkillSpecializationAsync(request, cancellationToken)
            .ConfigureAwait(false);
        bool exactMutationApplied = State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && request.ExpectedContentRevision < long.MaxValue
            && State.ContentRevision == request.ExpectedContentRevision + 1
            && State.IsDirty;
        long appliedRevision = exactMutationApplied ? State.ContentRevision : 0;
        if (exactMutationApplied)
        {
            await _presenter.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        bool durable = exactMutationApplied
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && State.ContentRevision == appliedRevision
            && State.SavedRevision == appliedRevision
            && !State.IsDirty;
        NativeWorkspaceAuthoritySnapshot? authority = durable
            ? await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: request.WorkspaceId,
                expectedPayloadSha256: null,
                cancellationToken).ConfigureAwait(false)
            : null;
        if (!durable
            || AndroidE2EAuthority.Enabled && (authority is null || !authority.Matches(State)))
        {
            await SyncShellAsync(cancellationToken).ConfigureAwait(false);
            NotifyChanged();
            return null;
        }

        CareerSkillSpecializationEditorState? editor =
            await _presenter.PrepareCareerSkillSpecializationAsync(cancellationToken)
                .ConfigureAwait(false);
        CharacterCareerSkillSpecializationQuote? freshQuote = editor is null
            ? null
            : await _presenter.PrepareCareerSkillSpecializationQuoteAsync(
                new CareerSkillSpecializationQuoteRequest(
                    request.WorkspaceId,
                    appliedRevision,
                    request.ExpectedQuote.Identity,
                    request.ExpectedQuote.Selection),
                cancellationToken).ConfigureAwait(false);
        bool exactProjection = editor is not null
            && freshQuote is not null
            && editor.WorkspaceId == request.WorkspaceId
            && editor.ContentRevision == appliedRevision
            && freshQuote.Identity == request.ExpectedQuote.Identity
            && freshQuote.Selection == request.ExpectedQuote.Selection
            && freshQuote.ExistingSpecializationCount
                == request.ExpectedQuote.ExistingSpecializationCount + 1
            && freshQuote.AvailableKarma == plan.SavedCharacterKarma
            && freshQuote.TotalBaseRating == request.ExpectedQuote.TotalBaseRating
            && string.Equals(freshQuote.SourceRevision, request.ExpectedSourceRevision, StringComparison.Ordinal)
            && string.Equals(freshQuote.RuleDigest, request.ExpectedRuleDigest, StringComparison.Ordinal);
        _notice = exactProjection
            ? "Specialization and Karma expense saved; current-process typed projection verified."
            : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return exactProjection
            ? new Sr5CareerSpecializationApplyObservation(editor!, freshQuote!, appliedRevision)
            : null;
    }

    /// <summary>
    /// Executes only the atomic Core service command. The Presentation
    /// skill-group request is deliberately not used as mutation authority
    /// because that compatibility path cannot return a persisted receipt.
    /// </summary>
    public async Task<CharacterCareerSkillGroupAdvanceResult?> AdvanceCareerSkillGroupAsync(
        CharacterCareerSkillGroupAdvanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeCommandDigest(
                command,
                out string commandDigest)
            || State.WorkspaceId != command.WorkspaceId
            || State.IsDirty
            || State.SavedRevision != State.ContentRevision
            || State.ContentRevision < command.ExpectedWorkspaceRevision
            || State.ContentRevision > command.ExpectedWorkspaceRevision + 1)
        {
            throw new InvalidOperationException(
                "The atomic skill-group command does not own this exact clean runner revision.");
        }

        ICharacterCareerSkillGroupAdvanceService? service = _careerSkillGroupService;
        if (service is null)
        {
            return null;
        }

        CharacterCareerSkillGroupAdvanceResult result = await Task.Run(
            () => service.Advance(command),
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(result.ContractName,
                CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
                StringComparison.Ordinal)
            || result.WorkspaceId != command.WorkspaceId
            || result.ExpectedWorkspaceRevision != command.ExpectedWorkspaceRevision
            || result.Identity != command.Identity
            || result.TransactionId != command.TransactionId
            || !string.Equals(result.CommandDigest, commandDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Core returned a skill-group result for another command or runner.");
        }

        if (result.CurrentWorkspaceRevision > 0
            && result.CurrentWorkspaceRevision != State.ContentRevision)
        {
            await _presenter.LoadAsync(command.WorkspaceId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Outcome is CharacterCareerSkillGroupAdvanceServiceOutcome.Applied
                or CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed)
        {
            if (State.WorkspaceId != command.WorkspaceId
                || State.ContentRevision != result.CurrentWorkspaceRevision
                || State.SavedRevision != result.CurrentWorkspaceRevision
                || State.IsDirty
                || !string.IsNullOrWhiteSpace(State.Error))
            {
                throw new InvalidOperationException(
                    "The atomic skill-group receipt was returned without the exact clean saved runner revision.");
            }
            _notice = "Skill group advanced and exact Core receipt saved.";
        }

        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return result;
    }

    /// <summary>
    /// Executes only the atomic Core settlement service. Missing composition
    /// returns null; Android never substitutes manual Karma/Nuyen/reputation or
    /// generic XML writes for the combined governed transaction.
    /// </summary>
    public async Task<CharacterAfterRunSettlementResult?> SettleAfterRunAsync(
        CharacterAfterRunSettlementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!CharacterAfterRunSettlementServiceIntegrity.TryComputeCommandDigest(
                command,
                out string commandDigest)
            || State.WorkspaceId != command.WorkspaceId
            || State.IsDirty
            || State.SavedRevision != State.ContentRevision
            || State.ContentRevision < command.ExpectedWorkspaceRevision
            || State.ContentRevision > command.ExpectedWorkspaceRevision + 1)
        {
            throw new InvalidOperationException(
                "The atomic After Run command does not own this exact clean saved runner revision.");
        }

        ICharacterAfterRunSettlementService? service = _afterRunSettlementService;
        if (service is null)
        {
            return null;
        }

        CharacterAfterRunSettlementResult result = await Task.Run(
            () => service.Settle(command),
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                result.ContractName,
                CharacterAfterRunSettlementServiceSchemas.ResultV1,
                StringComparison.Ordinal)
            || result.WorkspaceId != command.WorkspaceId
            || result.ExpectedWorkspaceRevision != command.ExpectedWorkspaceRevision
            || result.Identity != command.Identity
            || result.TransactionId != command.TransactionId
            || !string.Equals(result.CommandDigest, commandDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Core returned an After Run result for another command or runner.");
        }

        if (result.CurrentWorkspaceRevision > 0
            && result.CurrentWorkspaceRevision != State.ContentRevision)
        {
            await _presenter.LoadAsync(command.WorkspaceId, cancellationToken)
                .ConfigureAwait(false);
        }
        if (result.Outcome is CharacterAfterRunSettlementServiceOutcome.Applied
                or CharacterAfterRunSettlementServiceOutcome.Replayed)
        {
            if (State.WorkspaceId != command.WorkspaceId
                || State.ContentRevision != result.CurrentWorkspaceRevision
                || State.SavedRevision != result.CurrentWorkspaceRevision
                || State.IsDirty
                || !string.IsNullOrWhiteSpace(State.Error))
            {
                throw new InvalidOperationException(
                    "The atomic After Run receipt was returned without the exact clean saved runner revision.");
            }
            _notice = "After Run Heat, reputation and contacts settled; exact Core receipt saved.";
        }

        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return result;
    }

    public async Task<bool> ApplyCareerKnowledgeSkillAdvanceAsync(
        CareerKnowledgeSkillAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Knowledge/Language advancement was open. Reopen it before saving.");
        }
        if (!CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(request.ExpectedSkill)
            || !request.ExpectedSkill.CanAdvance
            || !string.Equals(request.ExpectedSkill.CharacterRevision, request.ExpectedCharacterRevision, StringComparison.Ordinal)
            || !string.Equals(request.ExpectedSkill.LogicalRevision, request.ExpectedLogicalRevision, StringComparison.Ordinal)
            || !string.Equals(request.ExpectedSkill.SourceRevision, request.ExpectedSourceRevision, StringComparison.Ordinal)
            || !string.Equals(request.ExpectedSkill.RuleDigest, request.ExpectedRuleDigest, StringComparison.Ordinal)
            || !CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
                request.ExpectedSkill,
                request.ExpectedCharacterRevision,
                request.ExpectedLogicalRevision,
                request.ExpectedSourceRevision,
                request.ExpectedRuleDigest,
                request.Confirmed,
                request.ExpenseId,
                request.ExpenseDateLocal,
                out CharacterCareerKnowledgeSkillAdvancePlan expectedPlan))
        {
            throw new InvalidOperationException(
                "Core rejected the Knowledge/Language identity, native-language gate, Karma budget, confirmation, expense, or reviewed CAS revisions before mutation.");
        }

        CharacterCareerKnowledgeSkillAdvanceReceipt? preparedReceipt =
            await _presenter.ApplyCareerKnowledgeSkillAdvanceAsync(request, cancellationToken)
                .ConfigureAwait(false);
        bool exactReceipt = preparedReceipt is not null
            && CharacterCareerKnowledgeSkillAdvanceRules.TryCreateReceipt(
                request.ExpenseId,
                request.ExpectedSkill,
                expectedPlan,
                preparedReceipt.SkillKarmaAfter,
                preparedReceipt.CharacterKarmaAfter,
                expenseExistsExactlyOnce: true,
                out CharacterCareerKnowledgeSkillAdvanceReceipt expectedReceipt)
            && preparedReceipt == expectedReceipt;
        bool exactMutationApplied = exactReceipt
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && request.ExpectedContentRevision < long.MaxValue
            && State.ContentRevision == request.ExpectedContentRevision + 1
            && State.IsDirty;
        long appliedContentRevision = exactMutationApplied ? State.ContentRevision : 0;
        if (exactMutationApplied)
        {
            await _presenter.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        bool durableState = exactMutationApplied
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && State.ContentRevision == appliedContentRevision
            && State.SavedRevision == appliedContentRevision
            && !State.IsDirty;
        NativeWorkspaceAuthoritySnapshot? authority = durableState
            ? await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: request.WorkspaceId,
                expectedPayloadSha256: null,
                cancellationToken).ConfigureAwait(false)
            : null;
        bool persisted = durableState
            && (!AndroidE2EAuthority.Enabled
                || authority is not null && authority.Matches(State));
        _notice = persisted
            ? "Knowledge/Language skill advanced and exact Karma expense receipt saved."
            : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return persisted;
    }

    public async Task<bool> ApplyCareerAttributeAdvanceAsync(
        CareerAttributeAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while attribute advancement was open. Reopen it before saving.");
        }
        if (!CharacterCareerAttributeAdvanceRules.IsCoherent(request.ExpectedAttribute)
            || !request.ExpectedAttribute.CanAdvance
            || !string.Equals(
                request.ExpectedAttribute.LogicalRevision,
                request.ExpectedLogicalRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                request.ExpectedAttribute.SourceRevision,
                request.ExpectedSourceRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                request.ExpectedAttribute.RuleDigest,
                request.ExpectedRuleDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Core rejected the attribute identity, legality, Karma budget, confirmation, expense, or reviewed revisions before mutation.");
        }
        if (!CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
                request.ExpectedAttribute,
                request.ExpectedLogicalRevision,
                request.ExpectedSourceRevision,
                request.ExpectedRuleDigest,
                request.Confirmed,
                request.ExpenseId,
                request.ExpenseDateLocal,
                out CharacterCareerAttributeAdvancePlan expectedPlan))
        {
            throw new InvalidOperationException(
                "Core rejected the attribute identity, legality, Karma budget, confirmation, expense, or reviewed revisions before mutation.");
        }

        CharacterCareerAttributeAdvanceReceipt? preparedReceipt =
            await _presenter.ApplyCareerAttributeAdvanceAsync(request, cancellationToken)
                .ConfigureAwait(false);
        bool exactReceipt = preparedReceipt is not null
            && CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
                request.ExpenseId,
                request.ExpectedAttribute,
                expectedPlan,
                preparedReceipt.AttributeKarmaAfter,
                preparedReceipt.CharacterKarmaAfter,
                preparedReceipt.BurnedEdgePointsAfter,
                expenseExistsExactlyOnce: true,
                out CharacterCareerAttributeAdvanceReceipt expectedReceipt)
            && preparedReceipt == expectedReceipt;
        bool exactMutationApplied = exactReceipt
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && request.ExpectedContentRevision < long.MaxValue
            && State.ContentRevision == request.ExpectedContentRevision + 1
            && State.IsDirty;
        long appliedContentRevision = exactMutationApplied ? State.ContentRevision : 0;
        if (exactMutationApplied)
        {
            await _presenter.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        bool durableState = exactMutationApplied
            && State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && State.ContentRevision == appliedContentRevision
            && State.SavedRevision == appliedContentRevision
            && !State.IsDirty;
        NativeWorkspaceAuthoritySnapshot? authority = durableState
            ? await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: request.WorkspaceId,
                expectedPayloadSha256: null,
                cancellationToken).ConfigureAwait(false)
            : null;
        bool persisted = durableState
            && (!AndroidE2EAuthority.Enabled
                || authority is not null && authority.Matches(State));
        _notice = persisted
            ? "Attribute advanced and exact Karma expense receipt saved."
            : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return persisted;
    }

    public async Task<CareerQualityEditorState?> PrepareCareerQualityAsync(
        CancellationToken cancellationToken = default)
    {
        if (_careerQualityPresenter is null || State.WorkspaceId is not { } workspaceId)
        {
            return null;
        }
        return await _careerQualityPresenter.ProjectAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CareerQualityReview> ReviewCareerQualityAsync(
        CareerQualityDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (_careerQualityPresenter is null
            || State.WorkspaceId != draft.WorkspaceId
            || State.ContentRevision != draft.ExpectedWorkspaceRevision
            || State.SavedRevision != draft.ExpectedSavedRevision
            || State.IsDirty
            || !string.IsNullOrWhiteSpace(State.Error))
        {
            throw new InvalidOperationException(
                "Exact atomic SR5 quality authority is unavailable or the runner revision changed.");
        }
        return await _careerQualityPresenter.ReviewAsync(draft, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CareerQualityConfirmation> ConfirmCareerQualityAsync(
        CareerQualityReview review,
        Guid transactionId,
        DateTime expenseDateLocal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        CareerQualityDraft draft = review.Draft;
        if (_careerQualityPresenter is null
            || State.WorkspaceId != draft.WorkspaceId
            || State.ContentRevision != draft.ExpectedWorkspaceRevision
            || State.SavedRevision != draft.ExpectedSavedRevision
            || State.IsDirty
            || !string.IsNullOrWhiteSpace(State.Error))
        {
            throw new InvalidOperationException(
                "The atomic quality transaction no longer owns this clean saved revision.");
        }

        CareerQualityConfirmation confirmation = await _careerQualityPresenter.ConfirmAsync(
                review,
                confirmed: true,
                transactionId,
                expenseDateLocal,
                cancellationToken)
            .ConfigureAwait(false);
        await _presenter.LoadAsync(draft.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        bool exact = State.Error is null
            && State.WorkspaceId == draft.WorkspaceId
            && State.ContentRevision == confirmation.PersistedState.WorkspaceRevision
            && State.SavedRevision == confirmation.PersistedState.SavedRevision
            && !State.IsDirty;
        _notice = exact ? "Quality transaction and exact receipt saved atomically." : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return exact
            ? confirmation
            : throw new InvalidOperationException(
                "The atomically persisted quality receipt did not reopen as the exact saved runner revision.");
    }

    public async Task<CareerQualityCorrectionConfirmation> CorrectCareerQualityAsync(
        CareerQualityCorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_careerQualityPresenter is null
            || State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedWorkspaceRevision
            || State.SavedRevision != request.ExpectedSavedRevision
            || State.IsDirty
            || !string.IsNullOrWhiteSpace(State.Error))
        {
            throw new InvalidOperationException(
                "The atomic quality correction no longer owns this clean saved receipt revision.");
        }

        CareerQualityCorrectionConfirmation confirmation =
            await _careerQualityPresenter.CorrectAsync(request, cancellationToken)
                .ConfigureAwait(false);
        await _presenter.LoadAsync(request.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        bool exact = State.Error is null
            && State.WorkspaceId == request.WorkspaceId
            && State.ContentRevision == confirmation.PersistedState.WorkspaceRevision
            && State.SavedRevision == confirmation.PersistedState.SavedRevision
            && !State.IsDirty;
        _notice = exact ? "Quality transaction corrected atomically." : null;
        await SyncShellAsync(cancellationToken).ConfigureAwait(false);
        NotifyChanged();
        return exact
            ? confirmation
            : throw new InvalidOperationException(
                "The atomically corrected quality transaction did not reopen as the exact saved revision.");
    }

    public Task<SustainedObjectsEditorState?> PrepareSustainedObjectsEditAsync(
        CancellationToken cancellationToken = default)
        => _presenter.PrepareSustainedObjectsEditAsync(cancellationToken);

    public async Task ApplySustainedObjectEditAsync(
        SustainedObjectEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while sustained effects were open. Reopen them before saving.");
        }

        await _presenter.ApplySustainedObjectEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null
            ? request.Action == CharacterSustainedObjectAction.Delete
                ? "Sustained effect removed."
                : "Sustained effect saved."
            : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyPsycheActiveEditAsync(
        PsycheActiveEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Psyche state was open. Reopen sustained effects before saving.");
        }

        await _presenter.ApplyPsycheActiveEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Psyche state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyGearLocationAddAsync(
        GearLocationAddRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Add Gear Location was open. Reopen it before saving.");
        }

        await _presenter.ApplyGearLocationAddAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear location added." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyWeaponLocationAddAsync(
        WeaponLocationAddRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Add Weapon Location was open. Reopen it before saving.");
        }

        await _presenter.ApplyWeaponLocationAddAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Weapon location added." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyVehicleLocationAddAsync(
        VehicleLocationAddRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Add Vehicle Location was open. Reopen it before saving.");
        }

        await _presenter.ApplyVehicleLocationAddAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Vehicle location added." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyVehicleHomeNodeEditAsync(
        VehicleHomeNodeEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Vehicle Home Node was open. Reopen it before saving.");
        }

        await _presenter.ApplyVehicleHomeNodeEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Vehicle Home Node saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyArmorHomeNodeEditAsync(
        ArmorHomeNodeEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Armor Home Node was open. Reopen it before saving.");
        }

        await _presenter.ApplyArmorHomeNodeEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Armor Home Node saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyWeaponHomeNodeEditAsync(
        WeaponHomeNodeEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Weapon Home Node was open. Reopen it before saving.");
        }

        await _presenter.ApplyWeaponHomeNodeEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Weapon Home Node saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyWeaponActiveCommlinkEditAsync(
        WeaponActiveCommlinkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Weapon Active Commlink was open. Reopen it before saving.");
        }

        await _presenter.ApplyWeaponActiveCommlinkEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Weapon Active Commlink saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyArmorActiveCommlinkEditAsync(
        ArmorActiveCommlinkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Armor Active Commlink was open. Reopen it before saving.");
        }

        await _presenter.ApplyArmorActiveCommlinkEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Armor Active Commlink saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyGearActiveCommlinkEditAsync(
        GearActiveCommlinkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Gear Active Commlink was open. Reopen it before saving.");
        }

        await _presenter.ApplyGearActiveCommlinkEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear Active Commlink saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyCyberwareActiveCommlinkEditAsync(
        CyberwareActiveCommlinkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Cyberware Active Commlink was open. Reopen it before saving.");
        }

        await _presenter.ApplyCyberwareActiveCommlinkEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Cyberware Active Commlink saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyVehicleActiveCommlinkEditAsync(
        VehicleActiveCommlinkEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Vehicle Active Commlink was open. Reopen it before saving.");
        }

        await _presenter.ApplyVehicleActiveCommlinkEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Vehicle Active Commlink saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyPrototypeTranshumanEditAsync(
        PrototypeTranshumanEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Prototype Transhuman was open. Reopen it before saving.");
        }

        await _presenter.ApplyPrototypeTranshumanEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Prototype Transhuman state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyArmorDamageAdjustmentAsync(
        ArmorDamageAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Armor Condition was open. Reopen it before adjusting damage.");
        }

        await _presenter.ApplyArmorDamageAdjustmentAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Armor condition saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyArmorEquipmentEditAsync(
        ArmorEquipmentEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Armor Equipment was open. Reopen it before saving.");
        }

        await _presenter.ApplyArmorEquipmentEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Armor equipment saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyWeaponAccessoryIncludedEditAsync(
        WeaponAccessoryIncludedEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Included in Weapon was open. Reopen it before saving.");
        }

        await _presenter.ApplyWeaponAccessoryIncludedEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Included-in-weapon state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyCritterPowerCountEditAsync(
        CritterPowerCountEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Critter Power Count was open. Reopen it before saving.");
        }

        await _presenter.ApplyCritterPowerCountEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Critter Power count state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplySpiritFetteredEditAsync(
        SpiritFetteredEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Fettered/Pet was open. Reopen it before saving.");
        }

        await _presenter.ApplySpiritFetteredEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Fettered/Pet state saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplySpiritNameChoiceEditAsync(
        SpiritNameChoiceEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Spirit/Sprite Metatype was open. Reopen it before saving.");
        }

        await _presenter.ApplySpiritNameChoiceEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Spirit/Sprite metatype saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyGearQuantityEditAsync(
        GearQuantityEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Gear Quantity was open. Reopen it before saving.");
        }

        await _presenter.ApplyGearQuantityEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Gear quantity saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyLifestyleIncrementEditAsync(
        LifestyleIncrementEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Lifestyle intervals were open. Reopen them before saving.");
        }

        await _presenter.ApplyLifestyleIncrementEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Lifestyle intervals saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyQualityLevelEditAsync(
        QualityLevelEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Quality Level was open. Reopen it before saving.");
        }

        await _presenter.ApplyQualityLevelEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Quality level saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public Task<CyberwareCommerceEditorState?> PrepareCyberwareCommerceEditAsync(
        Guid cyberwareId,
        CancellationToken cancellationToken = default)
        => _presenter.PrepareCyberwareCommerceEditAsync(cyberwareId, cancellationToken);

    public async Task ApplyCyberwareCommerceEditAsync(
        CyberwareCommerceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Cyberware Commerce was open. Reopen it before saving.");
        }

        await _presenter.ApplyCyberwareCommerceEditAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Cyberware commerce saved." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyLocationRenameAsync(
        LocationRenameRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State.WorkspaceId != request.WorkspaceId
            || State.ContentRevision != request.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "This runner changed while Rename Location was open. Reopen it before saving.");
        }

        await _presenter.ApplyLocationRenameAsync(request, cancellationToken);
        if (State.Error is null)
        {
            await _presenter.SaveAsync(cancellationToken);
        }
        _notice = State.Error is null ? "Location renamed." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ExecuteDialogActionAsync(string actionId, CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => ExecuteDialogActionCoreAsync(actionId, cancellationToken),
            cancellationToken);

    private async Task ExecuteDialogActionCoreAsync(string actionId, CancellationToken cancellationToken)
    {
        long contentRevision = State.ContentRevision;
        WorkspaceSurfaceActionDefinition? activeSectionAction = _surface.WorkspaceActions.FirstOrDefault(action =>
            action.Kind == WorkspaceSurfaceActionKind.Section
            && (string.Equals(action.Id, State.ActiveActionId, StringComparison.Ordinal)
                || string.Equals(action.TargetId, State.ActiveSectionId, StringComparison.Ordinal)));
        await _presenter.ExecuteDialogActionAsync(actionId, cancellationToken);
        if (activeSectionAction is not null
            && State.ActiveDialog is null
            && State.ContentRevision > contentRevision)
        {
            await _presenter.ExecuteWorkspaceActionAsync(activeSectionAction, cancellationToken);
        }
        await SyncShellAsync(cancellationToken);
        await ProcessPendingOutputsAsync(cancellationToken);
    }

    public Task CloseDialogAsync(CancellationToken cancellationToken = default)
        => _presenter.CloseDialogAsync(cancellationToken);

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _notice = null;
        _durableSaveNotice = null;
        await _presenter.SaveAsync(cancellationToken);
        if (State.Error is null)
        {
            _ = await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: State.WorkspaceId,
                expectedPayloadSha256: null,
                cancellationToken);
        }
        bool durableSaveVerified = State.Error is null
                                   && State.ContentRevision > 0
                                   && State.ContentRevision == State.SavedRevision;
        if (durableSaveVerified && State.WorkspaceId is { } verifiedWorkspaceId)
        {
            _durableSaveNotice = new NativeDurableSaveNotice(
                verifiedWorkspaceId,
                State.SavedRevision);
            _notice = "Saved.";
        }
        NotifyChanged();
    }

    public async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        await _presenter.ExportAsync(cancellationToken);
        await ProcessPendingOutputsAsync(cancellationToken);
    }

    public async Task PrintAsync(CancellationToken cancellationToken = default)
    {
        await _presenter.PrintAsync(cancellationToken);
        await ProcessPendingOutputsAsync(cancellationToken);
    }

    public async Task BeginAccountLinkAsync(CancellationToken cancellationToken = default)
        => await _account.BeginLinkAsync(cancellationToken);

    public async Task UnlinkAccountAsync(CancellationToken cancellationToken = default)
    {
        await _account.UnlinkAsync(cancellationToken);
        _onlineCharacters = [];
        _groups = [];
        _chronicles = [];
        NotifyChanged();
    }

    public Task OpenAccountAsync(CancellationToken cancellationToken = default)
        => _account.OpenAccountAsync(cancellationToken);

    public Task OpenAccountDeletionInfoAsync()
        => _system.OpenUriAsync(ChummerWebRoutes.Resolve(ChummerWebRoutes.AccountDeletion));

    public async Task<NativeAccountErasureResult> EraseAccountAsync(
        bool removeLocalRunners,
        CancellationToken cancellationToken = default)
        => await WithWorkspaceActivationGateAsync(
            () => EraseAccountCoreAsync(removeLocalRunners, cancellationToken),
            cancellationToken);

    private async Task<NativeAccountErasureResult> EraseAccountCoreAsync(
        bool removeLocalRunners,
        CancellationToken cancellationToken)
    {
        OpenWorkspaceState[] openWorkspaces = State.OpenWorkspaces.ToArray();
        AndroidLinkedGroup[] linkedGroups = _groups.ToArray();
        AndroidAccountErasureReceipt receipt = await _account.EraseAccountAsync(
            AndroidAccountErasureConfirmation.RequiredPhrase,
            cancellationToken);

        bool localRunnersRemoved = true;
        if (removeLocalRunners)
        {
            foreach (OpenWorkspaceState workspace in openWorkspaces)
            {
                try
                {
                    await _presenter.DeleteWorkspaceAsync(workspace.Id, confirmed: true, ct: cancellationToken);
                }
                catch
                {
                    localRunnersRemoved = false;
                }
            }

            ClearPlayPreferences(openWorkspaces, linkedGroups);
        }

        Preferences.Default.Remove(SelectedGroupPreferenceKey);
        _onlineCharacters = [];
        _groups = [];
        _chronicles = [];
        _play = NativePlaySnapshot.Empty;
        _notice = localRunnersRemoved
            ? "Account deletion completed."
            : "Account deletion completed. Some runners could not be removed from this device.";
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
        return new NativeAccountErasureResult(receipt, localRunnersRemoved);
    }

    public Task<AndroidUpdateCheckResult> CheckForUpdatesAsync()
        => _system.CheckForUpdatesAsync();

    public Task ShareTextAsync(string text)
        => _system.ShareTextAsync(text);

    public async Task RefreshLinkedDataAsync(CancellationToken cancellationToken = default)
    {
        await _account.InitializeAsync(cancellationToken);
        if (!_account.Snapshot.IsLinked)
        {
            _onlineCharacters = [];
            _groups = [];
            _chronicles = [];
            NotifyChanged();
            return;
        }

        Task<IReadOnlyList<AndroidOnlineCharacter>> charactersTask =
            _account.ListOnlineCharactersAsync(cancellationToken);
        Task<IReadOnlyList<AndroidLinkedGroup>> groupsTask =
            _account.ListGroupsAsync(cancellationToken);
        await Task.WhenAll(charactersTask, groupsTask);
        _onlineCharacters = await charactersTask;
        _groups = await groupsTask;
        EnsureSelectedGroup();
        _chronicles = SelectedGroup is { } selectedGroup
            ? await _account.ListChroniclesAsync(selectedGroup.GroupId, cancellationToken)
            : [];
        NotifyChanged();
    }

    public void SelectGroup(AndroidLinkedGroup? group)
    {
        string? previousGroupId = SelectedGroup?.GroupId;
        if (group is null)
        {
            Preferences.Default.Remove(SelectedGroupPreferenceKey);
        }
        else
        {
            Preferences.Default.Set(SelectedGroupPreferenceKey, group.GroupId);
        }

        if (!string.Equals(previousGroupId, group?.GroupId, StringComparison.Ordinal))
        {
            _chronicles = [];
        }

        RestorePlayState();
        NotifyChanged();
    }

    public async Task<AndroidLinkedGroup> CreateGroupAsync(
        string name,
        string visibility,
        CancellationToken cancellationToken = default)
    {
        AndroidLinkedGroup group = await _account.CreateGroupAsync(name.Trim(), visibility, cancellationToken);
        await RefreshLinkedDataAsync(cancellationToken);
        SelectGroup(_groups.FirstOrDefault(item => string.Equals(item.GroupId, group.GroupId, StringComparison.Ordinal)) ?? group);
        _notice = $"Created {group.Name}.";
        return group;
    }

    public async Task<AndroidLinkedGroup> UpdateGroupAsync(
        AndroidLinkedGroup group,
        string name,
        string visibility,
        CancellationToken cancellationToken = default)
    {
        AndroidLinkedGroup updated = await _account.UpdateGroupAsync(
            group.GroupId,
            name.Trim(),
            visibility,
            cancellationToken);
        await RefreshLinkedDataAsync(cancellationToken);
        SelectGroup(_groups.FirstOrDefault(item => string.Equals(item.GroupId, updated.GroupId, StringComparison.Ordinal)) ?? updated);
        _notice = $"Updated {updated.Name}.";
        return updated;
    }

    public Task<Uri> CreateGroupInviteAsync(AndroidLinkedGroup group, CancellationToken cancellationToken = default)
        => _account.CreateGroupInviteAsync(group.GroupId, cancellationToken);

    public async Task RefreshChroniclesAsync(
        AndroidLinkedGroup group,
        CancellationToken cancellationToken = default)
    {
        _chronicles = await _account.ListChroniclesAsync(group.GroupId, cancellationToken);
        NotifyChanged();
    }

    public async Task<AndroidChronicleProject> CreateChronicleAsync(
        AndroidLinkedGroup group,
        AndroidChronicleDraft draft,
        CancellationToken cancellationToken = default)
    {
        AndroidChronicleProject project = await _account.CreateChronicleAsync(group.GroupId, draft, cancellationToken);
        await RefreshChroniclesAsync(group, cancellationToken);
        _notice = $"Created {project.Title}.";
        return project;
    }

    public async Task<AndroidChronicleProject> ReviseChronicleAsync(
        AndroidLinkedGroup group,
        AndroidChronicleProject project,
        AndroidChronicleDraft draft,
        CancellationToken cancellationToken = default)
    {
        AndroidChronicleProject revised = await _account.ReviseChronicleAsync(
            group.GroupId,
            project.ChronicleProjectId,
            draft,
            cancellationToken);
        await RefreshChroniclesAsync(group, cancellationToken);
        _notice = $"Updated {revised.Title}.";
        return revised;
    }

    public async Task<AndroidChronicleProject> AdvanceChronicleAsync(
        AndroidLinkedGroup group,
        AndroidChronicleProject project,
        string action,
        string? externalProjectRef = null,
        string? artifactUrl = null,
        string? artifactSha256 = null,
        string? exportFormat = null,
        CancellationToken cancellationToken = default)
    {
        AndroidChronicleProject updated = await _account.AdvanceChronicleAsync(
            group.GroupId,
            project.ChronicleProjectId,
            action,
            externalProjectRef,
            artifactUrl,
            artifactSha256,
            exportFormat,
            cancellationToken);
        await RefreshChroniclesAsync(group, cancellationToken);
        _notice = $"{updated.Title}: {HumanizeId(updated.Status)}.";
        return updated;
    }

    public async Task SaveChroniclePacketAsync(
        AndroidLinkedGroup group,
        AndroidChronicleProject project,
        CancellationToken cancellationToken = default)
    {
        AndroidChroniclePacket packet = await _account.DownloadChroniclePacketAsync(
            group.GroupId,
            project.ChronicleProjectId,
            cancellationToken);
        await SaveBase64Async(packet.FileName, packet.MediaType, packet.ContentBase64, cancellationToken);
    }

    public async Task SaveChronicleHandoffAsync(
        AndroidLinkedGroup group,
        AndroidChronicleProject project,
        CancellationToken cancellationToken = default)
    {
        AndroidChroniclePacket handoff = await _account.DownloadChronicleHandoffAsync(
            group.GroupId,
            project.ChronicleProjectId,
            cancellationToken);
        await SaveBase64Async(handoff.FileName, handoff.MediaType, handoff.ContentBase64, cancellationToken);
    }

    public NativePlaySnapshot RollDice(int pool)
    {
        pool = Math.Clamp(pool, 1, 100);
        int[] dice = Enumerable.Range(0, pool)
            .Select(static _ => RandomNumberGenerator.GetInt32(1, 7))
            .OrderByDescending(static value => value)
            .ToArray();
        int hits = dice.Count(static value => value >= 5);
        int ones = dice.Count(static value => value == 1);
        _play = _play with
        {
            LastPool = pool,
            LastRoll = dice,
            Hits = hits,
            Glitch = ones >= (pool + 1) / 2
        };
        SavePlayState();
        NotifyChanged();
        return _play;
    }

    public void SetDamage(int physical, int stun)
    {
        _play = _play with
        {
            PhysicalDamage = Math.Clamp(physical, 0, 18),
            StunDamage = Math.Clamp(stun, 0, 18)
        };
        SavePlayState();
        NotifyChanged();
    }

    public void SetPlayNotes(string? notes)
    {
        _play = _play with { Notes = (notes ?? string.Empty).Trim().Length <= 4000
            ? (notes ?? string.Empty).Trim()
            : (notes ?? string.Empty).Trim()[..4000] };
        SavePlayState();
        NotifyChanged();
    }

    public static string HumanizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Action";
        }

        string value = id.Replace("tab-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
        value = string.Concat(value.Select(static (character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value);
    }

    private async Task<NativeWorkspaceAuthoritySnapshot?> TryRefreshWorkspaceAuthorityAsync(
        CharacterWorkspaceId? expectedWorkspaceId,
        string? expectedPayloadSha256,
        CancellationToken cancellationToken,
        bool allowReadOnlyProductCapture = false)
    {
        if (!allowReadOnlyProductCapture
            && !AndroidE2EAuthority.Enabled
            && expectedPayloadSha256 is null)
        {
            ClearWorkspaceAuthority();
            return null;
        }
        if (expectedWorkspaceId is not { } workspaceId)
        {
            ClearWorkspaceAuthority();
            return null;
        }

        long authorityEpoch;
        long optInGeneration = AndroidE2EAuthority.Generation;
        lock (_workspaceAuthoritySync)
        {
            if (_disposed)
            {
                _workspaceAuthority = null;
                _workspaceAuthorityOptInGeneration = 0;
                return null;
            }
            authorityEpoch = _workspaceAuthorityEpoch;
        }

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            WorkspaceOperationExecution<NativeWorkspaceAuthoritySnapshot> execution =
                await _workspaceOperationCoordinator.RunCurrentAsync(
                    workspaceId,
                    async token =>
                    {
                        NativeWorkspaceAuthoritySnapshot candidate =
                            await ReadWorkspaceAuthorityAsync(workspaceId, token);
                        if (expectedPayloadSha256 is not null)
                        {
                            RequirePayloadDigest(candidate, expectedPayloadSha256);
                        }
                        return candidate;
                    },
                    linked.Token);
            if (!execution.CanPublish
                || !execution.HasValue
                || execution.Value is not { } authority)
            {
                ClearWorkspaceAuthority();
                return null;
            }

            lock (_workspaceAuthoritySync)
            {
                if (_disposed
                    || authorityEpoch != _workspaceAuthorityEpoch
                    || !authority.Matches(State))
                {
                    _workspaceAuthority = null;
                    _workspaceAuthorityOptInGeneration = 0;
                    return null;
                }
                if (AndroidE2EAuthority.Enabled
                    && optInGeneration == AndroidE2EAuthority.Generation)
                {
                    _workspaceAuthority = authority;
                    _workspaceAuthorityOptInGeneration = optInGeneration;
                }
                else
                {
                    _workspaceAuthority = null;
                    _workspaceAuthorityOptInGeneration = 0;
                }
            }
            if (AndroidE2EAuthority.Enabled
                && optInGeneration == AndroidE2EAuthority.Generation)
            {
                NotifyChanged();
            }
            return authority;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ClearWorkspaceAuthority();
            return null;
        }
    }

    private void ClearWorkspaceAuthority()
    {
        lock (_workspaceAuthoritySync)
        {
            _workspaceAuthority = null;
            _workspaceAuthorityOptInGeneration = 0;
        }
    }

    private async Task<NativeWorkspaceAuthoritySnapshot> ReadWorkspaceAuthorityAsync(
        CharacterWorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        WorkspaceDocumentSnapshot first = RequireWorkspaceSnapshot(
            await _client.GetWorkspaceAsync(workspaceId, cancellationToken),
            workspaceId);
        WorkspaceDocumentSnapshot verified = RequireWorkspaceSnapshot(
            await _client.GetWorkspaceAsync(workspaceId, cancellationToken),
            workspaceId);
        if (!AuthoritySnapshotsMatch(first, verified))
        {
            throw new InvalidOperationException(
                $"Dossier '{workspaceId.Value}' changed while Android was capturing its authority proof.");
        }
        return new NativeWorkspaceAuthoritySnapshot(
            workspaceId.Value,
            verified.ContentRevision,
            verified.SavedRevision,
            Sha256Hex(verified.Document.Content),
            ComputeDocumentAuthoritySha256(verified.Document));
    }

    private static WorkspaceDocumentSnapshot RequireWorkspaceSnapshot(
        CommandResult<WorkspaceDocumentSnapshot> result,
        CharacterWorkspaceId expectedWorkspaceId)
    {
        WorkspaceDocumentSnapshot snapshot = result.Success && result.Value is not null
            ? result.Value
            : throw new InvalidOperationException(
                result.Error ?? $"Dossier '{expectedWorkspaceId.Value}' could not be read for Android authority proof.");
        if (!string.Equals(snapshot.Id.Value, expectedWorkspaceId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Android authority read returned '{snapshot.Id.Value}' while '{expectedWorkspaceId.Value}' was requested.");
        }

        return snapshot;
    }

    private static bool AuthoritySnapshotsMatch(
        WorkspaceDocumentSnapshot first,
        WorkspaceDocumentSnapshot verified)
        => string.Equals(first.Id.Value, verified.Id.Value, StringComparison.Ordinal)
           && first.LastUpdatedUtc == verified.LastUpdatedUtc
           && first.ContentRevision == verified.ContentRevision
           && first.SavedRevision == verified.SavedRevision
           && first.Document.Format == verified.Document.Format
           && string.Equals(first.Document.RulesetId, verified.Document.RulesetId, StringComparison.Ordinal)
           && first.Document.SchemaVersion == verified.Document.SchemaVersion
           && string.Equals(first.Document.PayloadKind, verified.Document.PayloadKind, StringComparison.Ordinal)
           && string.Equals(first.Document.Content, verified.Document.Content, StringComparison.Ordinal)
           && string.Equals(
               first.Document.AuxiliaryStateDigest,
               verified.Document.AuxiliaryStateDigest,
               StringComparison.Ordinal);

    private static string ComputeDocumentAuthoritySha256(WorkspaceDocument document)
    {
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendAuthorityField(digest, "schema", WorkspaceAuthorityDigestSchema);
        AppendAuthorityField(
            digest,
            "format",
            ((int)document.Format).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendAuthorityField(digest, "rulesetId", document.RulesetId);
        AppendAuthorityField(
            digest,
            "schemaVersion",
            document.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendAuthorityField(digest, "payloadKind", document.PayloadKind);
        AppendAuthorityField(digest, "content", document.Content);
        AppendAuthorityField(digest, "auxiliaryStateDigest", document.AuxiliaryStateDigest);
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendAuthorityField(
        IncrementalHash digest,
        string tag,
        string? value)
    {
        byte[] tagBytes = StrictUtf8.GetBytes(tag);
        byte[]? valueBytes = value is null ? null : StrictUtf8.GetBytes(value);
        try
        {
            Span<byte> length = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(length, tagBytes.LongLength);
            digest.AppendData(length);
            digest.AppendData(tagBytes);
            Span<byte> nullMarker = stackalloc byte[1];
            nullMarker[0] = value is null ? (byte)0 : (byte)1;
            digest.AppendData(nullMarker);
            if (valueBytes is not null)
            {
                BinaryPrimitives.WriteInt64BigEndian(length, valueBytes.LongLength);
                digest.AppendData(length);
                digest.AppendData(valueBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tagBytes);
            if (valueBytes is not null)
            {
                CryptographicOperations.ZeroMemory(valueBytes);
            }
        }
    }

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Sha256Hex(string value)
    {
        byte[] bytes = StrictUtf8.GetBytes(value);
        try
        {
            return Sha256Hex(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string ComputeExactImportPayloadSha256(byte[] input)
    {
        string decoded = StrictUtf8.GetString(input);
        byte[] canonical = StrictUtf8.GetBytes(decoded);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(input, canonical))
            {
                throw new InvalidDataException(
                    "The selected dossier changed bytes during strict UTF-8 decoding.");
            }
            return Sha256Hex(input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }
    }

    private static void RequirePayloadDigest(
        NativeWorkspaceAuthoritySnapshot authority,
        string expectedPayloadSha256)
    {
        byte[] actual = Convert.FromHexString(authority.PayloadSha256);
        byte[] expected = Convert.FromHexString(expectedPayloadSha256);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                throw new InvalidOperationException(
                    $"Dossier '{authority.WorkspaceId}' did not activate the exact selected input bytes.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private async Task SyncShellAsync(CancellationToken cancellationToken)
    {
        await _shellSyncGate.WaitAsync(cancellationToken);
        try
        {
            CharacterWorkspaceId? active = State.Session.ActiveWorkspaceId ?? State.WorkspaceId;
            if (active is not null)
            {
                Preferences.Default.Set(SelectedWorkspacePreferenceKey, active.Value.Value);
            }
            else if (_initialized && State.OpenWorkspaces.Count == 0)
            {
                Preferences.Default.Remove(SelectedWorkspacePreferenceKey);
            }
            await _shellPresenter.SyncWorkspaceContextAsync(active, cancellationToken);
            RefreshSurface();
        }
        finally
        {
            _shellSyncGate.Release();
        }
    }

    private async Task RestoreSelectedWorkspaceAsync(CancellationToken cancellationToken)
    {
        if (State.Profile is not null && State.WorkspaceId is not null)
        {
            return;
        }

        string selectedId = Preferences.Default.Get(SelectedWorkspacePreferenceKey, string.Empty);
        CharacterWorkspaceId? workspaceId = State.Session.ActiveWorkspaceId;
        workspaceId ??= State.OpenWorkspaces.FirstOrDefault(workspace =>
            string.Equals(workspace.Id.Value, selectedId, StringComparison.Ordinal))?.Id;
        if (workspaceId is null && !string.IsNullOrWhiteSpace(selectedId))
        {
            workspaceId = new CharacterWorkspaceId(selectedId);
        }
        workspaceId ??= State.OpenWorkspaces.Count == 1 ? State.OpenWorkspaces[0].Id : null;
        if (workspaceId is null)
        {
            return;
        }

        await _presenter.LoadAsync(workspaceId.Value, cancellationToken);
        if (State.Profile is null
            && !string.IsNullOrWhiteSpace(selectedId)
            && !string.Equals(workspaceId.Value.Value, selectedId, StringComparison.Ordinal))
        {
            workspaceId = new CharacterWorkspaceId(selectedId);
            await _presenter.LoadAsync(workspaceId.Value, cancellationToken);
        }

        if (State.Profile is not null)
        {
            Preferences.Default.Set(SelectedWorkspacePreferenceKey, workspaceId.Value.Value);
        }
    }

    private void RefreshSurface()
        => _surface = _surfaceResolver.Resolve(State, _shellPresenter.State);

    private async Task ProcessPendingOutputsAsync(CancellationToken cancellationToken = default)
    {
        if (!await _outputGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            CharacterOverviewState state = State;
            if (state.PendingDownload is { } download && state.PendingDownloadVersion > _handledDownloadVersion)
            {
                _handledDownloadVersion = state.PendingDownloadVersion;
                await SaveBase64Async(download.FileName, MimeType(download.Format), download.ContentBase64, cancellationToken);
            }

            state = State;
            if (state.PendingExport is { } export && state.PendingExportVersion > _handledExportVersion)
            {
                _handledExportVersion = state.PendingExportVersion;
                await SaveBase64Async(export.FileName, MimeType(export.Format), export.ContentBase64, cancellationToken);
            }

            state = State;
            if (state.PendingPrint is { } print && state.PendingPrintVersion > _handledPrintVersion)
            {
                _handledPrintVersion = state.PendingPrintVersion;
                bool opened = await _system.PrintPdfAsync(
                    print.FileName,
                    print.ContentBase64,
                    print.Title,
                    cancellationToken);
                _notice = opened ? "Print dialog opened." : "Printing is not available on this device.";
            }
        }
        finally
        {
            _outputGate.Release();
            NotifyChanged();
        }
    }

    private async Task SaveBase64Async(
        string fileName,
        string mediaType,
        string contentBase64,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Convert.FromBase64String(contentBase64);
        try
        {
            await using MemoryStream stream = new(bytes, writable: false);
            bool saved = await _documents.SaveAsAsync(fileName, mediaType, stream, cancellationToken);
            _notice = saved ? $"Saved {Path.GetFileName(fileName)}." : "Save cancelled.";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string MimeType(WorkspaceDocumentFormat format)
        => format == WorkspaceDocumentFormat.Json ? "application/json" : "application/xml";

    private static WorkspaceDocumentFormat ParseFormat(string? value)
        => Enum.TryParse(value, ignoreCase: true, out WorkspaceDocumentFormat format)
            ? format
            : WorkspaceDocumentFormat.NativeXml;

    private void EnsureSelectedGroup()
    {
        string selectedId = Preferences.Default.Get(SelectedGroupPreferenceKey, string.Empty);
        if (_groups.Count == 0)
        {
            Preferences.Default.Remove(SelectedGroupPreferenceKey);
        }
        else if (!_groups.Any(group => string.Equals(group.GroupId, selectedId, StringComparison.Ordinal)))
        {
            Preferences.Default.Set(SelectedGroupPreferenceKey, _groups[0].GroupId);
        }
    }

    private string PlayPreferenceKey(string suffix)
    {
        string workspace = (State.Session.ActiveWorkspaceId ?? State.WorkspaceId)?.Value ?? "none";
        string group = SelectedGroup?.GroupId ?? "solo";
        return $"chummer.android.play.{workspace}.{group}.{suffix}";
    }

    private void RestorePlayState()
    {
        _play = new NativePlaySnapshot(
            Preferences.Default.Get(PlayPreferenceKey("physical"), 0),
            Preferences.Default.Get(PlayPreferenceKey("stun"), 0),
            Preferences.Default.Get(PlayPreferenceKey("pool"), 6),
            [],
            0,
            false,
            Preferences.Default.Get(PlayPreferenceKey("notes"), string.Empty));
    }

    private void SavePlayState()
    {
        Preferences.Default.Set(PlayPreferenceKey("physical"), _play.PhysicalDamage);
        Preferences.Default.Set(PlayPreferenceKey("stun"), _play.StunDamage);
        Preferences.Default.Set(PlayPreferenceKey("pool"), _play.LastPool);
        Preferences.Default.Set(PlayPreferenceKey("notes"), _play.Notes);
    }

    private static void ClearPlayPreferences(
        IReadOnlyList<OpenWorkspaceState> workspaces,
        IReadOnlyList<AndroidLinkedGroup> groups)
    {
        string[] workspaceIds = workspaces
            .Select(static workspace => workspace.Id.Value)
            .Append("none")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] groupIds = groups
            .Select(static group => group.GroupId)
            .Append("solo")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string workspaceId in workspaceIds)
        {
            foreach (string groupId in groupIds)
            {
                foreach (string suffix in new[] { "physical", "stun", "pool", "notes" })
                {
                    Preferences.Default.Remove($"chummer.android.play.{workspaceId}.{groupId}.{suffix}");
                }
            }
        }
    }

    private void OnPresenterStateChanged(object? sender, EventArgs e)
    {
        lock (_workspaceAuthoritySync)
        {
            unchecked
            {
                _workspaceAuthorityEpoch++;
            }
            _workspaceAuthority = null;
            _workspaceAuthorityOptInGeneration = 0;
        }
        PersistCharacterSettingsCatalog();
        RefreshSurface();
        NotifyChanged();
        _ = ProcessPendingOutputsAsync();
    }

    private void RestoreCharacterSettingsCatalog()
    {
        _persistedCharacterSettingsCatalogJson = Preferences.Default.Get(
            CharacterSettingsCatalogPreferenceKey,
            string.Empty);
        if (!string.IsNullOrWhiteSpace(_persistedCharacterSettingsCatalogJson))
        {
            DesktopPreferenceStateRuntime.SetCurrent(
                DesktopPreferenceStateRuntime.Current with
                {
                    CharacterSettingsCatalogJson = _persistedCharacterSettingsCatalogJson
                });
        }
    }

    private void PersistCharacterSettingsCatalog()
    {
        string catalogJson = State.Preferences.CharacterSettingsCatalogJson ?? string.Empty;
        if (string.Equals(catalogJson, _persistedCharacterSettingsCatalogJson, StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(catalogJson))
        {
            Preferences.Default.Remove(CharacterSettingsCatalogPreferenceKey);
        }
        else
        {
            Preferences.Default.Set(CharacterSettingsCatalogPreferenceKey, catalogJson);
        }
        _persistedCharacterSettingsCatalogJson = catalogJson;
        DesktopPreferenceStateRuntime.SetCurrent(State.Preferences);
    }

    private void OnShellStateChanged(object? sender, EventArgs e)
    {
        RefreshSurface();
        NotifyChanged();
    }

    private void OnAccountChanged(object? sender, EventArgs e) => NotifyChanged();

    private void OnE2EAuthorityChanged(object? sender, EventArgs e)
    {
        ClearWorkspaceAuthority();
        NotifyChanged();
        if (AndroidE2EAuthority.Enabled && !_disposed)
        {
            _ = RefreshWorkspaceAuthorityForOptInAsync();
        }
    }

    private async Task RefreshWorkspaceAuthorityForOptInAsync()
    {
        try
        {
            _ = await TryRefreshWorkspaceAuthorityAsync(
                expectedWorkspaceId: State.WorkspaceId,
                expectedPayloadSha256: null,
                _lifetime.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ClearWorkspaceAuthority();
        }
    }

    private void NotifyChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string DisplayName(string name, string alias)
        => !string.IsNullOrWhiteSpace(alias) ? alias : !string.IsNullOrWhiteSpace(name) ? name : "runner";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        lock (_workspaceAuthoritySync)
        {
            unchecked
            {
                _workspaceAuthorityEpoch++;
            }
            _workspaceAuthority = null;
            _workspaceAuthorityOptInGeneration = 0;
        }
        _presenter.StateChanged -= OnPresenterStateChanged;
        _shellPresenter.StateChanged -= OnShellStateChanged;
        _account.Changed -= OnAccountChanged;
        AndroidE2EAuthority.Changed -= OnE2EAuthorityChanged;
        // Await continuations may still release these process-scoped gates or read
        // the canceled lifetime token. Disposing them synchronously here would
        // turn an otherwise safe shutdown race into ObjectDisposedException.
    }
}
