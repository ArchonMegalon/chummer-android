using System.Reflection;
using Chummer.Android.Native;
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
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly string _path;
    private readonly string _temporaryPath;
    private readonly string _processInstanceId = Guid.NewGuid().ToString("D");
    private readonly Api36ProofBuildIdentity _build;
    private long _sequence;

    public Api36ProofStatePublisher()
    {
        _directory = Path.Combine(FileSystem.AppDataDirectory, "api36-proof");
        _path = Path.Combine(FileSystem.AppDataDirectory, RelativePath);
        _temporaryPath = _path + ".tmp";
        _build = ReadBuildIdentity();
        DeleteObservation();
    }

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
            WriteAtomically(Api36ProofStateContract.Serialize(state));
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
            WriteAtomically(Api36ProofStateContract.Serialize(state));
        }
    }

    private void WriteAtomically(byte[] payload)
    {
        Directory.CreateDirectory(_directory);
        try
        {
            using (var stream = new FileStream(
                       _temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }
            File.Move(_temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(_temporaryPath))
                File.Delete(_temporaryPath);
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
