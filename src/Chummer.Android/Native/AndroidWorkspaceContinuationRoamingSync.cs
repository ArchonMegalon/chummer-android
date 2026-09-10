using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Android.Native;

/// <summary>
/// Android's actual signed account transport. Inbound data is restored only by
/// an explicit Core review/confirm gesture, never while enumerating the roster.
/// Outbound updates use a known exact remote base, not timestamp arbitration.
/// </summary>
public sealed class AndroidWorkspaceContinuationRoamingSync(
    IOwnerContextLeaseAccessor owners,
    WorkspaceContinuationExportService exporter,
    IAndroidWorkspaceContinuationTransport transport,
    string statePath)
    : IDesktopWorkspaceRoamingSync, IOwnerBoundDesktopWorkspaceRoamingSync
{
    private readonly object _sync = new();
    private readonly Dictionary<(OwnerContextStamp Owner, string Workspace), RemoteBase> _bases = new();
    private readonly string _writeMarkers = MarkerRoot(statePath);

    public Task<DesktopWorkspaceRoamingResult> SynchronizeInboundAsync(OwnerScope owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // This is only the local-roster startup hook. It must neither claim a
        // remote synchronization nor silently replace locally edited runners.
        return Task.FromResult(new DesktopWorkspaceRoamingResult(DesktopWorkspaceRoamingOutcome.Unavailable));
    }

    public Task<DesktopWorkspaceRoamingResult> SynchronizeOutboundAsync(
        OwnerScope owner, CharacterWorkspaceId workspaceId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A value-only owner cannot authorize a delayed gesture after A→B→A.
        // Callers without the original transition stamp remain local-only.
        return Task.FromResult(new DesktopWorkspaceRoamingResult(DesktopWorkspaceRoamingOutcome.Unavailable, workspaceId));
    }

    public async Task<DesktopWorkspaceRoamingResult> SynchronizeOutboundAsync(
        OwnerContextStamp originalOwner, CharacterWorkspaceId workspaceId, CancellationToken ct)
    {
        if (!IsCurrent(originalOwner)) return Result(DesktopWorkspaceRoamingOutcome.Unauthorized);
        if (originalOwner.Owner == OwnerScope.LocalSingleUser)
            return Result(DesktopWorkspaceRoamingOutcome.Unavailable);
        try
        {
            // Core owns its synchronous credential lease. Do not reenter the
            // InProcess client queue which may be waiting for this status.
            var exported = await Task.Run(() => exporter.Export(originalOwner, workspaceId), ct).ConfigureAwait(false);
            if (!exported.Success || exported.Value is null) return Result(DesktopWorkspaceRoamingOutcome.Unavailable);
            IReadOnlyList<AndroidWorkspaceContinuationItem> candidates =
                await transport.ListContinuationsAsync(originalOwner, ct).ConfigureAwait(false);
            if (!IsCurrent(originalOwner)) return Result(DesktopWorkspaceRoamingOutcome.Unauthorized);
            var matches = candidates.Where(item => string.Equals(item.Character.WorkspaceId, workspaceId.Value,
                StringComparison.Ordinal)).Take(2).ToArray();
            if (matches.Length > 1) return Result(DesktopWorkspaceRoamingOutcome.Conflict);
            AndroidWorkspaceContinuationItem? remote = matches.SingleOrDefault();
            long expectedRevision = 0;
            string? expectedToken = null;
            if (remote is not null)
            {
                await Task.Run(() => MarkRemoteSeen(originalOwner, workspaceId, requireNew: false), ct).ConfigureAwait(false);
                if (!IsCurrent(originalOwner)) return Result(DesktopWorkspaceRoamingOutcome.Unauthorized);
                if (remote.Continuation is null || remote.RemoteRevision is not >= 0 || string.IsNullOrEmpty(remote.ServerToken))
                    return Result(DesktopWorkspaceRoamingOutcome.Conflict);
                if (remote.Continuation.SnapshotDigest == exported.Value.SnapshotDigest)
                {
                    Remember(originalOwner, workspaceId, remote.Continuation.SnapshotDigest,
                        remote.RemoteRevision.Value, remote.ServerToken);
                    return new(DesktopWorkspaceRoamingOutcome.AlreadyCurrent, workspaceId,
                        remote.RemoteRevision, remote.ServerToken);
                }
                RemoteBase? known;
                lock (_sync) _bases.TryGetValue((originalOwner, workspaceId.Value), out known);
                if (known is null || known.Digest != remote.Continuation.SnapshotDigest
                    || known.Revision != remote.RemoteRevision || known.Token != remote.ServerToken)
                    return Result(DesktopWorkspaceRoamingOutcome.Conflict);
                expectedRevision = known.Revision;
                expectedToken = known.Token;
            }
            if (!IsCurrent(originalOwner)) return Result(DesktopWorkspaceRoamingOutcome.Unauthorized);
            if (remote is null)
            {
                // Persist BEFORE dispatch. A vanished row or lost upload
                // response must not become another initial create after restart.
                bool admitted = await Task.Run(() => MarkRemoteSeen(originalOwner, workspaceId, requireNew: true), ct).ConfigureAwait(false);
                if (!admitted) return Result(DesktopWorkspaceRoamingOutcome.Conflict);
            }
            if (!IsCurrent(originalOwner)) return Result(DesktopWorkspaceRoamingOutcome.Unauthorized);
            var written = await transport.UpsertContinuationAsync(originalOwner, exported.Value,
                expectedRevision, expectedToken, ct).ConfigureAwait(false);
            // A known remote commit is not erased by a later owner transition;
            // it cannot grant the new owner an in-memory synchronization base.
            if (!written.UnknownRemoteOutcome
                && written.Outcome is AndroidWorkspaceContinuationWriteOutcome.Applied or AndroidWorkspaceContinuationWriteOutcome.AlreadyCurrent
                && written.RemoteRevision is >= 0 && !string.IsNullOrEmpty(written.ServerToken)
                && IsCurrent(originalOwner))
                Remember(originalOwner, workspaceId, exported.Value.SnapshotDigest, written.RemoteRevision.Value, written.ServerToken);
            else
                Forget(originalOwner, workspaceId);
            return new(written.UnknownRemoteOutcome ? DesktopWorkspaceRoamingOutcome.Unavailable : written.Outcome switch
            {
                AndroidWorkspaceContinuationWriteOutcome.Applied => DesktopWorkspaceRoamingOutcome.Applied,
                AndroidWorkspaceContinuationWriteOutcome.AlreadyCurrent => DesktopWorkspaceRoamingOutcome.AlreadyCurrent,
                AndroidWorkspaceContinuationWriteOutcome.Conflict => DesktopWorkspaceRoamingOutcome.Conflict,
                AndroidWorkspaceContinuationWriteOutcome.Unauthorized => DesktopWorkspaceRoamingOutcome.Unauthorized,
                _ => DesktopWorkspaceRoamingOutcome.Unavailable
            }, workspaceId, written.RemoteRevision, written.ServerToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A dropped response is not permission to replay a write. A later
            // read can reconcile an identical digest, otherwise review is needed.
            Forget(originalOwner, workspaceId);
            return Result(IsCurrent(originalOwner)
                ? DesktopWorkspaceRoamingOutcome.Unavailable : DesktopWorkspaceRoamingOutcome.Unauthorized);
        }

        DesktopWorkspaceRoamingResult Result(DesktopWorkspaceRoamingOutcome outcome) => new(outcome, workspaceId);
    }

    private bool IsCurrent(OwnerContextStamp original) => original.IsValid && owners.Capture() == original;

    private void Remember(OwnerContextStamp owner, CharacterWorkspaceId workspace, string digest, long revision, string token)
    {
        lock (_sync)
        {
            // Bases deliberately expire on process/account-epoch changes. After
            // restart, a divergent remote copy requires explicit reconciliation.
            foreach (var stale in _bases.Keys.Where(key => key.Owner != owner).ToArray()) _bases.Remove(stale);
            if (_bases.Count >= 256) _bases.Remove(_bases.Keys.First());
            _bases[(owner, workspace.Value)] = new(digest, revision, token);
        }
    }

    private void Forget(OwnerContextStamp owner, CharacterWorkspaceId workspace)
    {
        lock (_sync) _bases.Remove((owner, workspace.Value));
    }

    private sealed record RemoteBase(string Digest, long Revision, string Token);

    private static string MarkerRoot(string stateDirectory)
    {
        if (string.IsNullOrWhiteSpace(stateDirectory) || !Path.IsPathFullyQualified(stateDirectory))
            throw new ArgumentException("Continuation roaming requires an absolute private state directory.", nameof(stateDirectory));
        return Path.Combine(Path.GetFullPath(stateDirectory), "continuation-remote-seen-v1");
    }

    private bool MarkRemoteSeen(OwnerContextStamp owner, CharacterWorkspaceId workspace, bool requireNew)
    {
        if (!owners.TryAcquire(owner, out var lease))
            throw new InvalidOperationException("The original workspace owner is unavailable.");
        using var admittedOwner = lease;
        lock (_sync)
        {
            string parent = Path.GetDirectoryName(_writeMarkers)!;
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Private state directory cannot be a link.");
            Directory.CreateDirectory(_writeMarkers);
            if ((File.GetAttributes(_writeMarkers) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Continuation markers cannot be linked.");
            string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                "chummer.android.remote-seen/v1\0" + owner.Owner.NormalizedValue + "\0" + workspace.Value)));
            string path = Path.Combine(_writeMarkers, key + ".seen");
            if (File.Exists(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Continuation marker cannot be linked.");
                return !requireNew;
            }
            if (Directory.EnumerateFileSystemEntries(_writeMarkers).Take(513).Count() >= 512)
                throw new IOException("Continuation marker capacity reached; existing markers are retained.");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128, FileOptions.WriteThrough))
            {
                stream.Write("chummer.android.remote-seen/v1"u8);
                stream.Flush(flushToDisk: true);
            }
            AndroidPrivateFileDurability.SyncDirectory(_writeMarkers);
            AndroidPrivateFileDurability.SyncDirectory(parent);
            return true;
        }
    }
}
