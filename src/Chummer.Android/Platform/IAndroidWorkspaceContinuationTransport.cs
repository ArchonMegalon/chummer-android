using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Platform;

/// <summary>Account-bound transport of untrusted Core candidates, never restore admission.</summary>
public interface IAndroidWorkspaceContinuationTransport
{
    // The complete response is bounded (16 MiB, 200 rows); there is no pagination
    // contract. An oversized response is unavailable, never a truncated list.
    Task<IReadOnlyList<AndroidWorkspaceContinuationItem>> ListContinuationsAsync(
        OwnerContextStamp originalOwner, CancellationToken cancellationToken = default);

    Task<AndroidWorkspaceContinuationWriteResult> UpsertContinuationAsync(
        OwnerContextStamp originalOwner, WorkspaceContinuationExport continuation,
        long expectedRemoteRevision, string? expectedServerToken,
        CancellationToken cancellationToken = default);
}

public sealed record AndroidWorkspaceContinuationItem(
    AndroidOnlineCharacter Character,
    WorkspaceContinuationExport? Continuation,
    long? RemoteRevision,
    string? ServerToken,
    string? UnavailableReason = null);

public enum AndroidWorkspaceContinuationWriteOutcome
{
    Applied,
    AlreadyCurrent,
    Conflict,
    Unavailable,
    Unauthorized
}

public sealed record AndroidWorkspaceContinuationWriteResult(
    AndroidWorkspaceContinuationWriteOutcome Outcome,
    long? RemoteRevision = null,
    string? ServerToken = null,
    bool UnknownRemoteOutcome = false);
