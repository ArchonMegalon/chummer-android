using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Platform;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Workspaces;
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

public sealed record NativeAccountErasureResult(
    AndroidAccountErasureReceipt Receipt,
    bool LocalRunnersRemoved);

public sealed record CharacterNotesEditRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string CharacterNotes,
    string GameNotes,
    string GroupNotes);

public sealed class RunnerSessionCoordinator : IDisposable
{
    private const string SelectedGroupPreferenceKey = "chummer.android.selected-group.v1";
    private const string SelectedWorkspacePreferenceKey = "chummer.android.selected-workspace.v1";
    private const string CharacterSettingsCatalogPreferenceKey = "chummer.android.character-settings-catalog.v1";
    private readonly ICharacterOverviewPresenter _presenter;
    private readonly IShellPresenter _shellPresenter;
    private readonly IShellSurfaceResolver _surfaceResolver;
    private readonly ICommandAvailabilityEvaluator _availability;
    private readonly IAndroidDocumentService _documents;
    private readonly IAndroidLinkedCharacterFileService _linkedCharacters;
    private readonly IAndroidSystemService _system;
    private readonly IAndroidAccountLinkService _account;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private readonly SemaphoreSlim _shellSyncGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;
    private long _handledDownloadVersion;
    private long _handledExportVersion;
    private long _handledPrintVersion;
    private string? _notice;
    private string _persistedCharacterSettingsCatalogJson = string.Empty;
    private IReadOnlyList<AndroidOnlineCharacter> _onlineCharacters = [];
    private IReadOnlyList<AndroidLinkedGroup> _groups = [];
    private IReadOnlyList<AndroidChronicleProject> _chronicles = [];
    private NativePlaySnapshot _play = NativePlaySnapshot.Empty;
    private ShellSurfaceState _surface = ShellSurfaceState.Empty;
    private CharacterWorkspaceId? _characterNotesWorkspaceId;
    private long _characterNotesRevision;
    private string _characterNotes = string.Empty;
    private string _gameNotes = string.Empty;
    private string _groupNotes = string.Empty;

    public RunnerSessionCoordinator(
        ICharacterOverviewPresenter presenter,
        IShellPresenter shellPresenter,
        IShellSurfaceResolver surfaceResolver,
        ICommandAvailabilityEvaluator availability,
        IAndroidDocumentService documents,
        IAndroidLinkedCharacterFileService linkedCharacters,
        IAndroidSystemService system,
        IAndroidAccountLinkService account)
    {
        _presenter = presenter;
        _shellPresenter = shellPresenter;
        _surfaceResolver = surfaceResolver;
        _availability = availability;
        _documents = documents;
        _linkedCharacters = linkedCharacters;
        _system = system;
        _account = account;
        _presenter.StateChanged += OnPresenterStateChanged;
        _shellPresenter.StateChanged += OnShellStateChanged;
        _account.Changed += OnAccountChanged;
    }

    public event EventHandler? Changed;

    public CharacterOverviewState State => _presenter.State;

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

    public bool IsBusy => State.IsBusy || Surface.IsBusy;

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
            await _shellPresenter.InitializeAsync(cancellationToken);
            await _presenter.InitializeAsync(cancellationToken);
            await RestoreSelectedWorkspaceAsync(cancellationToken);
            await _account.InitializeAsync(cancellationToken);
            await SyncShellAsync(cancellationToken);
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

    public async Task OpenLocalAsync(CancellationToken cancellationToken = default)
    {
        AndroidDocument? document = await _documents.OpenAsync(cancellationToken);
        if (document is null)
        {
            return;
        }

        try
        {
            await _presenter.ImportAsync(
                WorkspaceImportDocument.FromUtf8Bytes(document.Content, string.Empty, WorkspaceDocumentFormat.NativeXml),
                cancellationToken);
            _notice = $"Opened {document.DisplayName}.";
            await SyncShellAsync(cancellationToken);
            RestorePlayState();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(document.Content);
        }

        NotifyChanged();
    }

    public async Task OpenOnlineAsync(AndroidOnlineCharacter character, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        byte[] payload = Encoding.UTF8.GetBytes(character.Payload);
        try
        {
            await _presenter.ImportAsync(
                WorkspaceImportDocument.FromUtf8Bytes(payload, character.RulesetId, ParseFormat(character.Format)),
                cancellationToken);
            _notice = $"Opened {DisplayName(character.Name, character.Alias)}.";
            await SyncShellAsync(cancellationToken);
            RestorePlayState();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        NotifyChanged();
    }

    public async Task CreateRunnerAsync(CancellationToken cancellationToken = default)
        => await ExecuteCommandAsync("new_character", cancellationToken);

    public async Task SwitchWorkspaceAsync(OpenWorkspaceState workspace, CancellationToken cancellationToken = default)
    {
        await _presenter.SwitchWorkspaceAsync(workspace.Id, cancellationToken);
        await SyncShellAsync(cancellationToken);
        RestorePlayState();
    }

    public async Task CloseWorkspaceAsync(OpenWorkspaceState workspace, CancellationToken cancellationToken = default)
    {
        await _presenter.CloseWorkspaceAsync(workspace.Id, cancellationToken);
        await SyncShellAsync(cancellationToken);
        RestorePlayState();
    }

    public async Task SelectTabAsync(string tabId, CancellationToken cancellationToken = default)
    {
        await _presenter.SelectTabAsync(tabId, cancellationToken);
        await _shellPresenter.SelectTabAsync(tabId, cancellationToken);
        RefreshSurface();
    }

    public async Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default)
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
    {
        await _presenter.ApplyAttributeEditAsync(request, cancellationToken);
        _notice = State.Error is null ? "Attribute updated." : null;
        await SyncShellAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ApplyOriginDossierEditAsync(
        OriginDossierEditRequest request,
        CancellationToken cancellationToken = default)
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
        await _presenter.SaveAsync(cancellationToken);
        _notice = State.Error is null ? "Saved." : null;
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
        _presenter.StateChanged -= OnPresenterStateChanged;
        _shellPresenter.StateChanged -= OnShellStateChanged;
        _account.Changed -= OnAccountChanged;
        _initializeGate.Dispose();
        _outputGate.Dispose();
        _shellSyncGate.Dispose();
    }
}
