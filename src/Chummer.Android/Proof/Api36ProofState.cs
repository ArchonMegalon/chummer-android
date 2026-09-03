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
    string StateDigest);

public static class Api36ProofStateContract
{
    public const string CurrentSchema = "chummer.android.api36-proof-state/v1";
    private const string DigestSchema = "chummer.android.api36-proof-state-digest/v1";
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
        Api36ProofTransactionState? transaction)
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
        return string.Equals(state.StateDigest, ComputeDigest(state), StringComparison.Ordinal);
    }

    public static string ComputeDigest(Api36ProofState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Api36ProofBuildIdentity build = state.Build;
        Api36ProofSurfaceState surface = state.Surface;
        Api36ProofWorkspaceState? workspace = state.Workspace;
        Api36ProofTransactionState? transaction = state.Transaction;
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
            transaction?.StatusCode);
    }

    private static bool IsBuild(Api36ProofBuildIdentity? build)
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

    private static bool IsWorkspace(Api36ProofWorkspaceState? workspace)
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

    private static string Hash(params string?[] values)
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
