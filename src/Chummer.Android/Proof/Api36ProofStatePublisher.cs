using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Proof;

/// <summary>
/// Publishes a bounded, read-only observation for the disposable API-36 proof APK. The only
/// external reader is an exact app-private run-as/cat command; this type exposes no command or
/// mutation surface and is not compiled into normal Debug, ARM64, Release, or Play artifacts.
/// </summary>
public sealed class Api36ProofStatePublisher
{
    public const string RelativePath = "api36-proof/state.v2.json";
    public const string ImportRelativePath = "api36-proof/import.v1.json";
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly string _path;
    private readonly string _temporaryPath;
    private readonly string _importPath;
    private readonly string _importTemporaryPath;
    private readonly string _processInstanceId = Guid.NewGuid().ToString("D");
    private readonly Api36ProofBuildIdentity _build;
    private long _sequence;
    private long _importSequence;
    private string? _importOperationId;
    private Api36ImportPickerState? _importPicker;
    private Api36ImportStreamState? _importStream;
    private Api36ImportWorkspaceState? _importWorkspace;

    public Api36ProofStatePublisher()
    {
        _directory = Path.Combine(FileSystem.AppDataDirectory, "api36-proof");
        _path = Path.Combine(FileSystem.AppDataDirectory, RelativePath);
        _temporaryPath = _path + ".tmp";
        _importPath = Path.Combine(FileSystem.AppDataDirectory, ImportRelativePath);
        _importTemporaryPath = _importPath + ".tmp";
        _build = ReadBuildIdentity();
        DeleteObservation();
        DeleteImportObservation();
    }

    public static void TryBeginDocumentImport()
        => Current()?.BeginDocumentImport();

    public static void TryRecordDocumentPickerCallback(
        int requestCode,
        bool resultOk,
        global::Android.Net.Uri? uri)
        => Current()?.RecordDocumentPickerCallback(requestCode, resultOk, uri);

    public static void TryRecordDocumentStream(AndroidDocument document)
        => Current()?.RecordDocumentStream(document);

    public static void TryRecordDocumentWorkspace(
        string expectedPayloadSha256,
        NativeWorkspaceAuthoritySnapshot authority,
        bool activationIssued)
        => Current()?.RecordDocumentWorkspace(
            expectedPayloadSha256,
            authority,
            activationIssued);

    public static void TryRecordDocumentImportFailure(string failureCode)
        => Current()?.RecordDocumentImportFailure(failureCode);

    private static Api36ProofStatePublisher? Current()
        => IPlatformApplication.Current?.Services
            .GetService(typeof(Api36ProofStatePublisher)) as Api36ProofStatePublisher;

    public void BeginDocumentImport()
    {
        if (!AndroidE2EAuthority.Enabled)
        {
            DeleteImportObservation();
            return;
        }
        lock (_sync)
        {
            _importOperationId = Guid.NewGuid().ToString("D");
            _importPicker = null;
            _importStream = null;
            _importWorkspace = null;
            PublishImportLocked("picker-launched", activationIssued: false, failureCode: null);
        }
    }

    public void RecordDocumentPickerCallback(
        int requestCode,
        bool resultOk,
        global::Android.Net.Uri? uri)
    {
        if (!AndroidE2EAuthority.Enabled
            || requestCode != DocumentIntentBroker.OpenRequestCode)
        {
            return;
        }
        lock (_sync)
        {
            RequireImportOperation();
            bool accepted = resultOk && uri is not null;
            _importPicker = new Api36ImportPickerState(
                requestCode,
                accepted ? "ok" : "cancelled",
                accepted,
                accepted ? Sha256(uri!.ToString() ?? string.Empty) : null);
            PublishImportLocked(
                accepted ? "picker-callback" : "cancelled",
                activationIssued: false,
                failureCode: null);
        }
    }

    public void RecordDocumentStream(AndroidDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_sync)
        {
            RequireImportOperation();
            if (_importPicker is not { Result: "ok", UriPresent: true })
                throw new InvalidOperationException("Import stream was observed before an accepted picker callback.");
            _importStream = new Api36ImportStreamState(
                document.DisplayName,
                document.MediaType,
                document.Content.LongLength,
                Convert.ToHexString(SHA256.HashData(document.Content)).ToLowerInvariant());
            PublishImportLocked("stream-read", activationIssued: false, failureCode: null);
        }
    }

    public void RecordDocumentWorkspace(
        string expectedPayloadSha256,
        NativeWorkspaceAuthoritySnapshot authority,
        bool activationIssued)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPayloadSha256);
        ArgumentNullException.ThrowIfNull(authority);
        lock (_sync)
        {
            RequireImportOperation();
            if (_importStream is null)
                throw new InvalidOperationException("Import workspace was observed before its document stream.");
            _importWorkspace = new Api36ImportWorkspaceState(
                expectedPayloadSha256,
                new Api36ProofWorkspaceState(
                    authority.WorkspaceId,
                    authority.ContentRevision,
                    authority.SavedRevision,
                    authority.PayloadSha256,
                    authority.DocumentSha256,
                    null));
            PublishImportLocked(
                activationIssued ? "activation-issued" : "workspace-verified",
                activationIssued,
                failureCode: null);
        }
    }

    public void RecordDocumentImportFailure(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        lock (_sync)
        {
            if (_importOperationId is null || !AndroidE2EAuthority.Enabled)
                return;
            PublishImportLocked("failed", activationIssued: false, failureCode);
        }
    }

    private void PublishImportLocked(
        string stage,
        bool activationIssued,
        string? failureCode)
    {
        string operationId = RequireImportOperation();
        Api36ImportProofState state = Api36ImportProofStateContract.Create(
            checked(++_importSequence),
            Environment.ProcessId,
            _processInstanceId,
            AndroidE2EAuthority.Generation,
            _build,
            operationId,
            stage,
            _importPicker,
            _importStream,
            _importWorkspace,
            activationIssued,
            failureCode);
        WriteAtomically(
            _importPath,
            _importTemporaryPath,
            Api36ImportProofStateContract.Serialize(state));
    }

    private string RequireImportOperation()
        => _importOperationId
           ?? throw new InvalidOperationException("No API-36 document import operation is active.");

    public static void TryPublishTableWizard(
        Page page,
        RunnerSessionCoordinator coordinator,
        string shellDestination,
        Sr5TableWizardLane? lane,
        string stage,
        bool settled,
        Sr5TableWizardCheckpointReadStatus checkpointReadStatus,
        Sr5TableWizardSession? session,
        Sr5TableWizardTransactionJournal? transaction,
        string? statusCode)
    {
        ArgumentNullException.ThrowIfNull(page);
        Api36ProofStatePublisher? publisher = IPlatformApplication.Current?.Services
            .GetService(typeof(Api36ProofStatePublisher)) as Api36ProofStatePublisher;
        publisher?.PublishTableWizard(
            coordinator,
            shellDestination,
            string.IsNullOrWhiteSpace(page.AutomationId) ? "unknown-page" : page.AutomationId,
            page.Navigation.NavigationStack.Count,
            lane,
            stage,
            settled,
            checkpointReadStatus,
            session,
            transaction,
            statusCode);
    }

    public void PublishTableWizard(
        RunnerSessionCoordinator coordinator,
        string shellDestination,
        string pageAutomationId,
        int navigationDepth,
        Sr5TableWizardLane? lane,
        string stage,
        bool settled,
        Sr5TableWizardCheckpointReadStatus checkpointReadStatus,
        Sr5TableWizardSession? session,
        Sr5TableWizardTransactionJournal? transaction,
        string? statusCode)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        if (!AndroidE2EAuthority.Enabled)
        {
            DeleteObservation();
            return;
        }

        NativeWorkspaceAuthoritySnapshot? authority = coordinator.DebugWorkspaceAuthority;
        Sr5TableWizardActionState? action = transaction?.Quote ?? session?.State.SelectedAction;
        Sr5TableWizardTransactionReceipt? receipt = transaction?.Receipt;
        Api36ProofWorkspaceState? workspace = authority is null
            ? null
            : new Api36ProofWorkspaceState(
                authority.WorkspaceId,
                authority.ContentRevision,
                authority.SavedRevision,
                authority.PayloadSha256,
                authority.DocumentSha256,
                session?.State.Snapshot.SnapshotDigest);
        Api36ProofTransactionState? transactionState = transaction is null && session is null
            ? null
            : new Api36ProofTransactionState(
                Token(checkpointReadStatus),
                transaction is null ? null : Token(transaction.Phase),
                transaction?.Version,
                transaction?.TransactionId.ToString("D"),
                transaction?.JournalDigest,
                action?.Identity.ActionId,
                action is null ? null : Token(action.Identity.Kind),
                action?.Identity.ActionDigest,
                transaction?.Review.WorkspaceRevision,
                receipt?.AppliedWorkspaceRevision,
                transaction?.ExpectedPostconditionDigest,
                receipt?.ObservedPostconditionDigest,
                receipt?.ReceiptDigest,
                session?.State.Resume.Restored == true,
                session?.State.CanConfirm == true,
                statusCode);
        Api36ProofSurfaceState surface = new(
            shellDestination,
            pageAutomationId,
            navigationDepth,
            lane is null ? null : Token(lane.Value),
            stage,
            settled);

        lock (_sync)
        {
            Api36ProofState state = Api36ProofStateContract.Create(
                checked(++_sequence),
                Environment.ProcessId,
                _processInstanceId,
                AndroidE2EAuthority.Generation,
                _build,
                surface,
                workspace,
                transactionState);
            WriteAtomically(_path, _temporaryPath, Api36ProofStateContract.Serialize(state));
        }
    }

    public static void TryPublishCreationResources(
        Page page,
        RunnerSessionCoordinator coordinator,
        CharacterCreationResourcesBinding binding,
        CharacterCreationResourcesDraft? pendingDraft,
        CharacterCreationResourcesBudget budget,
        string snapshotDigest)
    {
        ArgumentNullException.ThrowIfNull(page);
        Api36ProofStatePublisher? publisher = IPlatformApplication.Current?.Services
            .GetService(typeof(Api36ProofStatePublisher)) as Api36ProofStatePublisher;
        publisher?.PublishCreationResources(
            coordinator,
            string.IsNullOrWhiteSpace(page.AutomationId) ? "unknown-page" : page.AutomationId,
            page.Navigation.NavigationStack.Count,
            binding,
            pendingDraft,
            budget,
            snapshotDigest);
    }

    public void PublishCreationResources(
        RunnerSessionCoordinator coordinator,
        string pageAutomationId,
        int navigationDepth,
        CharacterCreationResourcesBinding binding,
        CharacterCreationResourcesDraft? pendingDraft,
        CharacterCreationResourcesBudget budget,
        string snapshotDigest)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(budget);
        if (!AndroidE2EAuthority.Enabled
            || coordinator.DebugWorkspaceAuthority is not { } authority
            || !string.Equals(binding.WorkspaceId.Value, authority.WorkspaceId,
                StringComparison.Ordinal)
            || binding.ContentRevision != authority.ContentRevision
            || binding.SavedRevision != authority.SavedRevision)
        {
            DeleteObservation();
            return;
        }

        var surface = new Api36ProofSurfaceState(
            PhoneShellRoutes.Runner,
            pageAutomationId,
            navigationDepth,
            "creation-resources",
            "authority-ready",
            Settled: true);
        var workspace = new Api36ProofWorkspaceState(
            authority.WorkspaceId,
            authority.ContentRevision,
            authority.SavedRevision,
            authority.PayloadSha256,
            authority.DocumentSha256,
            snapshotDigest);
        var resources = new Api36ProofCreationResourcesState(
            pageAutomationId,
            binding.WorkspaceId.Value,
            binding.WorkspaceRevision,
            binding.ContentRevision,
            binding.SavedRevision,
            binding.AuthorityDigest,
            binding.SourceDigest,
            binding.RulesDigest,
            binding.RuntimeDigest,
            snapshotDigest,
            binding.RawCharacterXmlDigest,
            binding.AuxiliaryStateDigest,
            binding.PrerequisiteDraftRevision,
            binding.PrerequisiteDraftDigest,
            budget.PriorityNuyen,
            budget.TotalStartingNuyen,
            pendingDraft?.SelectedOptionId,
            pendingDraft?.DraftRevision,
            pendingDraft?.DraftDigest);

        lock (_sync)
        {
            Api36ProofState state = Api36ProofStateContract.Create(
                checked(++_sequence),
                Environment.ProcessId,
                _processInstanceId,
                AndroidE2EAuthority.Generation,
                _build,
                surface,
                workspace,
                transaction: null,
                creationResources: resources);
            WriteAtomically(_path, _temporaryPath, Api36ProofStateContract.Serialize(state));
        }
    }

    private void WriteAtomically(string path, string temporaryPath, byte[] payload)
    {
        Directory.CreateDirectory(_directory);
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void DeleteObservation()
    {
        lock (_sync)
        {
            if (File.Exists(_temporaryPath))
                File.Delete(_temporaryPath);
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    private void DeleteImportObservation()
    {
        lock (_sync)
        {
            if (File.Exists(_importTemporaryPath))
                File.Delete(_importTemporaryPath);
            if (File.Exists(_importPath))
                File.Delete(_importPath);
            _importOperationId = null;
            _importPicker = null;
            _importStream = null;
            _importWorkspace = null;
        }
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Api36ProofBuildIdentity ReadBuildIdentity()
    {
        Dictionary<string, string> metadata = typeof(Api36ProofStatePublisher).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(static value => value.Key, static value => value.Value ?? string.Empty,
                StringComparer.Ordinal);
        return new Api36ProofBuildIdentity(
            Required(metadata, "ChummerApi36ProofSourceCommit"),
            Required(metadata, "ChummerApi36ProofSourceTree"),
            Required(metadata, "ChummerApi36ProofGateContractSha256"),
            Required(metadata, "ChummerApi36ProofBuildId"),
            AppInfo.Current.PackageName,
            AppInfo.Current.VersionString,
            AppInfo.Current.BuildString,
            Required(metadata, "ChummerApi36ProofRuntimeIdentifier"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Required API-36 proof build identity {key} is absent.");

    private static string Token<T>(T value) where T : struct, Enum
        => string.Concat(value.ToString().Select(static (character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"-{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
}
