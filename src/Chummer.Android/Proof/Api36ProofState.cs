using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chummer.Android.Proof;

public sealed record Api36ProofBuildIdentity(
    string SourceCommit,
    string SourceTree,
    string GateContractSha256,
    string ProofBuildId,
    string PackageName,
    string VersionName,
    string VersionCode,
    string RuntimeIdentifier);

public sealed record Api36ProofSurfaceState(
    string ShellDestination,
    string PageAutomationId,
    int NavigationDepth,
    string? WizardLane,
    string Stage,
    bool Settled);

public sealed record Api36ProofWorkspaceState(
    string WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string PayloadSha256,
    string DocumentSha256,
    string? SnapshotDigest);

public sealed record Api36ProofCreationResourcesState(
    string PageIdentity,
    string WorkspaceId,
    long WorkspaceRevision,
    long ContentRevision,
    long SavedRevision,
    string AuthorityDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    string SnapshotDigest,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    decimal PriorityNuyen,
    decimal TotalStartingNuyen,
    string? PendingOptionId,
    long? PendingDraftRevision,
    string? PendingDraftDigest);

public sealed record Api36ProofTransactionState(
    string CheckpointReadStatus,
    string? Phase,
    long? JournalVersion,
    string? TransactionId,
    string? JournalDigest,
    string? ActionId,
    string? ActionKind,
    string? ActionDigest,
    long? ExpectedWorkspaceRevision,
    long? AppliedWorkspaceRevision,
    string? ExpectedPostconditionDigest,
    string? ObservedPostconditionDigest,
    string? ReceiptDigest,
    bool ResumeRestored,
    bool CanConfirm,
    string? StatusCode);

public sealed record Api36ProofState(
    string Schema,
    long Sequence,
    int ProcessId,
    string ProcessInstanceId,
    [property: JsonPropertyName("e2eAuthorityGeneration")]
    long E2eAuthorityGeneration,
    Api36ProofBuildIdentity Build,
    Api36ProofSurfaceState Surface,
    Api36ProofWorkspaceState? Workspace,
    Api36ProofTransactionState? Transaction,
    Api36ProofCreationResourcesState? CreationResources,
    string StateDigest);

public sealed record Api36ImportPickerState(
    int RequestCode,
    string Result,
    bool UriPresent,
    string? UriSha256);

public sealed record Api36ImportStreamState(
    string DisplayName,
    string? MediaType,
    long ByteLength,
    string ContentSha256);

public sealed record Api36ImportWorkspaceState(
    string ExpectedPayloadSha256,
    Api36ProofWorkspaceState Authority);

public sealed record Api36ImportProofState(
    string Schema,
    long Sequence,
    int ProcessId,
    string ProcessInstanceId,
    [property: JsonPropertyName("e2eAuthorityGeneration")]
    long E2eAuthorityGeneration,
    Api36ProofBuildIdentity Build,
    string OperationId,
    string Stage,
    Api36ImportPickerState? Picker,
    Api36ImportStreamState? Stream,
    Api36ImportWorkspaceState? Workspace,
    bool ActivationIssued,
    string? FailureCode,
    string StateDigest);

public static class Api36ImportProofStateContract
{
    public const string CurrentSchema = "chummer.android.api36-import-proof-state/v1";
    private const string DigestSchema = "chummer.android.api36-import-proof-state-digest/v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static Api36ImportProofState Create(
        long sequence,
        int processId,
        string processInstanceId,
        long e2eAuthorityGeneration,
        Api36ProofBuildIdentity build,
        string operationId,
        string stage,
        Api36ImportPickerState? picker,
        Api36ImportStreamState? stream,
        Api36ImportWorkspaceState? workspace,
        bool activationIssued,
        string? failureCode)
    {
        Api36ImportProofState unsigned = new(
            CurrentSchema,
            sequence,
            processId,
            processInstanceId,
            e2eAuthorityGeneration,
            build,
            operationId,
            stage,
            picker,
            stream,
            workspace,
            activationIssued,
            failureCode,
            string.Empty);
        Api36ImportProofState state = unsigned with
        {
            StateDigest = ComputeDigest(unsigned)
        };
        if (!IsExact(state))
            throw new InvalidOperationException("The API-36 import proof observation is incomplete.");
        return state;
    }

    public static byte[] Serialize(Api36ImportProofState state)
    {
        if (!IsExact(state))
            throw new InvalidOperationException("Only an exact API-36 import proof observation can be serialized.");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        if (payload.Length is 0 or > 16 * 1024)
            throw new InvalidOperationException("The API-36 import proof observation exceeds its bounded payload.");
        return payload;
    }

    public static bool IsExact(Api36ImportProofState? state)
    {
        if (state is null
            || !string.Equals(state.Schema, CurrentSchema, StringComparison.Ordinal)
            || state.Sequence <= 0
            || state.ProcessId <= 0
            || !Guid.TryParseExact(state.ProcessInstanceId, "D", out Guid processInstance)
            || processInstance == Guid.Empty
            || state.E2eAuthorityGeneration < 0
            || !Api36ProofStateContract.IsBuild(state.Build)
            || !Guid.TryParseExact(state.OperationId, "D", out Guid operationId)
            || operationId == Guid.Empty
            || !IsToken(state.Stage, 64)
            || !IsDigest(state.StateDigest)
            || !IsPicker(state.Picker)
            || !IsStream(state.Stream)
            || !IsWorkspace(state.Workspace)
            || state.FailureCode is not null && !IsToken(state.FailureCode, 128))
        {
            return false;
        }

        bool exactStage = state.Stage switch
        {
            "picker-launched" => state.Picker is null
                && state.Stream is null
                && state.Workspace is null
                && !state.ActivationIssued
                && state.FailureCode is null,
            "picker-callback" => state.Picker is not null
                && state.Stream is null
                && state.Workspace is null
                && !state.ActivationIssued
                && state.FailureCode is null,
            "stream-read" => IsSuccessfulPicker(state.Picker)
                && state.Stream is not null
                && state.Workspace is null
                && !state.ActivationIssued
                && state.FailureCode is null,
            "workspace-verified" => IsSuccessfulPicker(state.Picker)
                && state.Stream is not null
                && state.Workspace is not null
                && IsBound(state.Stream, state.Workspace)
                && !state.ActivationIssued
                && state.FailureCode is null,
            "activation-issued" => IsSuccessfulPicker(state.Picker)
                && state.Stream is not null
                && state.Workspace is not null
                && IsBound(state.Stream, state.Workspace)
                && state.ActivationIssued
                && state.FailureCode is null,
            "cancelled" => state.Picker is { Result: "cancelled", UriPresent: false }
                && state.Stream is null
                && state.Workspace is null
                && !state.ActivationIssued
                && state.FailureCode is null,
            "failed" => !state.ActivationIssued && state.FailureCode is not null,
            _ => false
        };
        return exactStage
            && string.Equals(state.StateDigest, ComputeDigest(state), StringComparison.Ordinal);
    }

    public static string ComputeDigest(Api36ImportProofState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Api36ImportPickerState? picker = state.Picker;
        Api36ImportStreamState? stream = state.Stream;
        Api36ImportWorkspaceState? workspace = state.Workspace;
        Api36ProofWorkspaceState? authority = workspace?.Authority;
        return Api36ProofStateContract.Hash(
            DigestSchema,
            state.Schema,
            state.Sequence.ToString(CultureInfo.InvariantCulture),
            state.ProcessId.ToString(CultureInfo.InvariantCulture),
            state.ProcessInstanceId,
            state.E2eAuthorityGeneration.ToString(CultureInfo.InvariantCulture),
            state.Build.SourceCommit,
            state.Build.SourceTree,
            state.Build.GateContractSha256,
            state.Build.ProofBuildId,
            state.Build.PackageName,
            state.Build.VersionName,
            state.Build.VersionCode,
            state.Build.RuntimeIdentifier,
            state.OperationId,
            state.Stage,
            picker?.RequestCode.ToString(CultureInfo.InvariantCulture),
            picker?.Result,
            picker?.UriPresent is true ? "true" : "false",
            picker?.UriSha256,
            stream?.DisplayName,
            stream?.MediaType,
            stream?.ByteLength.ToString(CultureInfo.InvariantCulture),
            stream?.ContentSha256,
            workspace?.ExpectedPayloadSha256,
            authority?.WorkspaceId,
            authority?.ContentRevision.ToString(CultureInfo.InvariantCulture),
            authority?.SavedRevision.ToString(CultureInfo.InvariantCulture),
            authority?.PayloadSha256,
            authority?.DocumentSha256,
            authority?.SnapshotDigest,
            state.ActivationIssued ? "true" : "false",
            state.FailureCode);
    }

    private static bool IsSuccessfulPicker(Api36ImportPickerState? picker)
        => picker is { Result: "ok", UriPresent: true };

    private static bool IsBound(
        Api36ImportStreamState stream,
        Api36ImportWorkspaceState workspace)
        => string.Equals(
            stream.ContentSha256,
            workspace.ExpectedPayloadSha256,
            StringComparison.Ordinal);

    private static bool IsPicker(Api36ImportPickerState? picker)
        => picker is null
           || picker.RequestCode == 6411
              && picker.Result is "ok" or "cancelled"
              && picker.UriPresent == (picker.Result == "ok")
              && (picker.UriPresent ? IsLowerHex(picker.UriSha256, 64) : picker.UriSha256 is null);

    private static bool IsStream(Api36ImportStreamState? stream)
        => stream is null
           || stream.DisplayName is { Length: > 0 and <= 256 }
              && (stream.MediaType is null || stream.MediaType.Length <= 256)
              && stream.ByteLength is > 0 and <= 8 * 1024 * 1024
              && IsLowerHex(stream.ContentSha256, 64);

    private static bool IsWorkspace(Api36ImportWorkspaceState? workspace)
        => workspace is null
           || IsLowerHex(workspace.ExpectedPayloadSha256, 64)
              && workspace.Authority is { } authority
              && Api36ProofStateContract.IsWorkspace(authority)
              && string.Equals(
                  workspace.ExpectedPayloadSha256,
                  authority.PayloadSha256,
                  StringComparison.Ordinal);

    private static bool IsDigest(string? value)
        => value is { Length: 71 }
           && value.StartsWith("sha256:", StringComparison.Ordinal)
           && IsLowerHex(value[7..], 64);

    private static bool IsLowerHex(string? value, int length)
        => value is not null
           && value.Length == length
           && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsToken(string? value, int maximumLength)
        => value is { Length: > 0 }
           && value.Length <= maximumLength
           && value.All(static character => char.IsAsciiLetterOrDigit(character)
               || character is '.' or '_' or '-' or '/' or ':');
}

public static class Api36ProofStateContract
{
    public const string CurrentSchema = "chummer.android.api36-proof-state/v2";
    private const string DigestSchema = "chummer.android.api36-proof-state-digest/v2";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static Api36ProofState Create(
        long sequence,
        int processId,
        string processInstanceId,
        long e2eAuthorityGeneration,
        Api36ProofBuildIdentity build,
        Api36ProofSurfaceState surface,
        Api36ProofWorkspaceState? workspace,
        Api36ProofTransactionState? transaction,
        Api36ProofCreationResourcesState? creationResources = null)
    {
        var unsigned = new Api36ProofState(
            CurrentSchema,
            sequence,
            processId,
            processInstanceId,
            e2eAuthorityGeneration,
            build,
            surface,
            workspace,
            transaction,
            creationResources,
            string.Empty);
        Api36ProofState state = unsigned with { StateDigest = ComputeDigest(unsigned) };
        if (!IsExact(state))
            throw new InvalidOperationException("The API-36 proof observation is incomplete.");
        return state;
    }

    public static byte[] Serialize(Api36ProofState state)
    {
        if (!IsExact(state))
            throw new InvalidOperationException("Only an exact API-36 proof observation can be serialized.");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        if (payload.Length is 0 or > 32 * 1024)
            throw new InvalidOperationException("The API-36 proof observation exceeds its bounded payload.");
        return payload;
    }

    public static bool IsExact(Api36ProofState? state)
    {
        if (state is null
            || !string.Equals(state.Schema, CurrentSchema, StringComparison.Ordinal)
            || state.Sequence <= 0
            || state.ProcessId <= 0
            || !Guid.TryParseExact(state.ProcessInstanceId, "D", out Guid instance)
            || instance == Guid.Empty
            || state.E2eAuthorityGeneration < 0
            || !IsBuild(state.Build)
            || !IsSurface(state.Surface)
            || !IsWorkspace(state.Workspace)
            || !IsTransaction(state.Transaction)
            || !IsCreationResources(state.CreationResources)
            || !IsDigest(state.StateDigest))
        {
            return false;
        }
        if (state.Transaction is { } transaction
            && state.Workspace is { } workspace
            && transaction.ExpectedWorkspaceRevision is { } expected
            && expected != workspace.ContentRevision
            && transaction.AppliedWorkspaceRevision is null)
        {
            return false;
        }
        bool creationResourcesSurface =
            string.Equals(state.Surface.PageAutomationId, "creation-resources-page",
                StringComparison.Ordinal)
            || string.Equals(state.Surface.WizardLane, "creation-resources",
                StringComparison.Ordinal);
        if (creationResourcesSurface != (state.CreationResources is not null))
            return false;
        if (state.CreationResources is { } resources
            && (state.Workspace is not { } resourceWorkspace
                || !string.Equals(resources.PageIdentity, state.Surface.PageAutomationId,
                    StringComparison.Ordinal)
                || !string.Equals(resources.PageIdentity, "creation-resources-page",
                    StringComparison.Ordinal)
                || !string.Equals(state.Surface.WizardLane, "creation-resources",
                    StringComparison.Ordinal)
                || !string.Equals(state.Surface.Stage, "authority-ready", StringComparison.Ordinal)
                || !state.Surface.Settled
                || !string.Equals(resources.WorkspaceId, resourceWorkspace.WorkspaceId,
                    StringComparison.Ordinal)
                || resources.WorkspaceRevision != resources.ContentRevision
                || resources.ContentRevision != resourceWorkspace.ContentRevision
                || resources.SavedRevision != resourceWorkspace.SavedRevision
                || !string.Equals(resources.SnapshotDigest, resourceWorkspace.SnapshotDigest,
                    StringComparison.Ordinal)))
        {
            return false;
        }
        return string.Equals(state.StateDigest, ComputeDigest(state), StringComparison.Ordinal);
    }

    public static string ComputeDigest(Api36ProofState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Api36ProofBuildIdentity build = state.Build;
        Api36ProofSurfaceState surface = state.Surface;
        Api36ProofWorkspaceState? workspace = state.Workspace;
        Api36ProofTransactionState? transaction = state.Transaction;
        Api36ProofCreationResourcesState? resources = state.CreationResources;
        return Hash(
            DigestSchema,
            state.Schema,
            state.Sequence.ToString(CultureInfo.InvariantCulture),
            state.ProcessId.ToString(CultureInfo.InvariantCulture),
            state.ProcessInstanceId,
            state.E2eAuthorityGeneration.ToString(CultureInfo.InvariantCulture),
            build.SourceCommit,
            build.SourceTree,
            build.GateContractSha256,
            build.ProofBuildId,
            build.PackageName,
            build.VersionName,
            build.VersionCode,
            build.RuntimeIdentifier,
            surface.ShellDestination,
            surface.PageAutomationId,
            surface.NavigationDepth.ToString(CultureInfo.InvariantCulture),
            surface.WizardLane,
            surface.Stage,
            surface.Settled ? "true" : "false",
            workspace?.WorkspaceId,
            workspace?.ContentRevision.ToString(CultureInfo.InvariantCulture),
            workspace?.SavedRevision.ToString(CultureInfo.InvariantCulture),
            workspace?.PayloadSha256,
            workspace?.DocumentSha256,
            workspace?.SnapshotDigest,
            transaction?.CheckpointReadStatus,
            transaction?.Phase,
            transaction?.JournalVersion?.ToString(CultureInfo.InvariantCulture),
            transaction?.TransactionId,
            transaction?.JournalDigest,
            transaction?.ActionId,
            transaction?.ActionKind,
            transaction?.ActionDigest,
            transaction?.ExpectedWorkspaceRevision?.ToString(CultureInfo.InvariantCulture),
            transaction?.AppliedWorkspaceRevision?.ToString(CultureInfo.InvariantCulture),
            transaction?.ExpectedPostconditionDigest,
            transaction?.ObservedPostconditionDigest,
            transaction?.ReceiptDigest,
            transaction?.ResumeRestored is true ? "true" : "false",
            transaction?.CanConfirm is true ? "true" : "false",
            transaction?.StatusCode,
            resources?.PageIdentity,
            resources?.WorkspaceId,
            resources?.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            resources?.ContentRevision.ToString(CultureInfo.InvariantCulture),
            resources?.SavedRevision.ToString(CultureInfo.InvariantCulture),
            resources?.AuthorityDigest,
            resources?.SourceDigest,
            resources?.RulesDigest,
            resources?.RuntimeDigest,
            resources?.SnapshotDigest,
            resources?.RawCharacterXmlDigest,
            resources?.AuxiliaryStateDigest,
            resources?.PrerequisiteDraftRevision.ToString(CultureInfo.InvariantCulture),
            resources?.PrerequisiteDraftDigest,
            resources?.PriorityNuyen.ToString(CultureInfo.InvariantCulture),
            resources?.TotalStartingNuyen.ToString(CultureInfo.InvariantCulture),
            resources?.PendingOptionId,
            resources?.PendingDraftRevision?.ToString(CultureInfo.InvariantCulture),
            resources?.PendingDraftDigest);
    }

    internal static bool IsBuild(Api36ProofBuildIdentity? build)
        => build is not null
           && IsLowerHex(build.SourceCommit, 40)
           && IsLowerHex(build.SourceTree, 40)
           && IsLowerHex(build.GateContractSha256, 64)
           && IsToken(build.ProofBuildId, 128)
           && string.Equals(build.PackageName, "com.myexternalbrain.chummer", StringComparison.Ordinal)
           && IsToken(build.VersionName, 64)
           && build.VersionCode.All(static character => character is >= '0' and <= '9')
           && build.VersionCode.Length is > 0 and <= 20
           && string.Equals(build.RuntimeIdentifier, "android-x64", StringComparison.Ordinal);

    private static bool IsSurface(Api36ProofSurfaceState? surface)
        => surface is not null
           && IsToken(surface.ShellDestination, 64)
           && IsToken(surface.PageAutomationId, 128)
           && surface.NavigationDepth is >= 0 and <= 64
           && (surface.WizardLane is null || IsToken(surface.WizardLane, 64))
           && IsToken(surface.Stage, 64);

    internal static bool IsWorkspace(Api36ProofWorkspaceState? workspace)
        => workspace is null
           || !string.IsNullOrWhiteSpace(workspace.WorkspaceId)
              && workspace.WorkspaceId.Length <= 256
              && workspace.ContentRevision > 0
              && workspace.SavedRevision >= 0
              && IsLowerHex(workspace.PayloadSha256, 64)
              && IsLowerHex(workspace.DocumentSha256, 64)
              && (workspace.SnapshotDigest is null || IsDigest(workspace.SnapshotDigest));

    private static bool IsTransaction(Api36ProofTransactionState? transaction)
        => transaction is null
           || IsToken(transaction.CheckpointReadStatus, 32)
              && (transaction.Phase is null || IsToken(transaction.Phase, 32))
              && (transaction.JournalVersion is null or > 0)
              && (transaction.TransactionId is null
                  || Guid.TryParseExact(transaction.TransactionId, "D", out Guid transactionId)
                  && transactionId != Guid.Empty)
              && IsOptionalDigest(transaction.JournalDigest)
              && (transaction.ActionId is null || IsToken(transaction.ActionId, 256))
              && (transaction.ActionKind is null || IsToken(transaction.ActionKind, 64))
              && IsOptionalDigest(transaction.ActionDigest)
              && (transaction.ExpectedWorkspaceRevision is null or > 0)
              && (transaction.AppliedWorkspaceRevision is null or > 0)
              && IsOptionalDigest(transaction.ExpectedPostconditionDigest)
              && IsOptionalDigest(transaction.ObservedPostconditionDigest)
              && IsOptionalDigest(transaction.ReceiptDigest)
              && (transaction.StatusCode is null || IsToken(transaction.StatusCode, 128));

    private static bool IsCreationResources(Api36ProofCreationResourcesState? resources)
    {
        if (resources is null)
            return true;
        bool pendingAbsent = resources.PendingOptionId is null
                             && resources.PendingDraftRevision is null
                             && resources.PendingDraftDigest is null;
        bool pendingExact = resources.PendingOptionId is not null
                            && IsToken(resources.PendingOptionId, 128)
                            && resources.PendingDraftRevision is > 0
                            && IsDigest(resources.PendingDraftDigest);
        return IsToken(resources.PageIdentity, 128)
               && !string.IsNullOrWhiteSpace(resources.WorkspaceId)
               && resources.WorkspaceId.Length <= 256
               && resources.WorkspaceRevision > 0
               && resources.ContentRevision > 0
               && resources.SavedRevision >= 0
               && IsDigest(resources.AuthorityDigest)
               && IsDigest(resources.SourceDigest)
               && IsDigest(resources.RulesDigest)
               && IsDigest(resources.RuntimeDigest)
               && IsDigest(resources.SnapshotDigest)
               && IsDigest(resources.RawCharacterXmlDigest)
               && IsLowerHex(resources.AuxiliaryStateDigest, 64)
               && resources.PrerequisiteDraftRevision > 0
               && IsDigest(resources.PrerequisiteDraftDigest)
               && resources.PriorityNuyen >= 0
               && resources.TotalStartingNuyen >= 0
               && (pendingAbsent || pendingExact);
    }

    private static bool IsOptionalDigest(string? value) => value is null || IsDigest(value);

    private static bool IsDigest(string? value)
        => value is { Length: 71 }
           && value.StartsWith("sha256:", StringComparison.Ordinal)
           && IsLowerHex(value[7..], 64);

    private static bool IsLowerHex(string? value, int length)
        => value is not null
           && value.Length == length
           && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsToken(string? value, int maximumLength)
        => value is { Length: > 0 }
           && value.Length <= maximumLength
           && value.All(static character => char.IsAsciiLetterOrDigit(character)
               || character is '.' or '_' or '-' or '/' or ':');

    internal static string Hash(params string?[] values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (string? value in values)
        {
            if (value is null)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, -1);
                hash.AppendData(length);
                continue;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
            CryptographicOperations.ZeroMemory(bytes);
        }
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
