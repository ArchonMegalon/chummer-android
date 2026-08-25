using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

public interface ISr5CareerCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

internal interface ISr5CareerCheckpointOwnerAuthority
{
    Guid CurrentOwnerId { get; }
}

internal interface ISr5CareerReviewedCheckpointAuthority :
    ISr5CareerCheckpointOwnerAuthority
{
    bool Owns(Sr5CareerDraftCheckpoint checkpoint);
}

internal sealed class PreferencesSr5CareerCheckpointOwnerAuthority :
    ISr5CareerCheckpointOwnerAuthority
{
    private const string OwnerStorageKey = "sr5.career.owner.v1";
    private static readonly object OwnerGate = new();

    public PreferencesSr5CareerCheckpointOwnerAuthority()
    {
        lock (OwnerGate)
        {
            string payload = Preferences.Default.Get(OwnerStorageKey, string.Empty);
            if (Guid.TryParse(payload, out Guid existing) && existing != Guid.Empty)
            {
                CurrentOwnerId = existing;
                return;
            }

            Guid candidate = Guid.NewGuid();
            Preferences.Default.Set(OwnerStorageKey, candidate.ToString("D"));
            string readBack = Preferences.Default.Get(OwnerStorageKey, string.Empty);
            if (!Guid.TryParse(readBack, out Guid stored) || stored != candidate)
            {
                throw new InvalidOperationException(
                    "The local Career checkpoint owner identity was not durable.");
            }
            CurrentOwnerId = stored;
        }
    }

    public Guid CurrentOwnerId { get; }
}

internal sealed class PreferencesSr5CareerCheckpointBackend : ISr5CareerCheckpointBackend
{
    private const string StorageKey = "sr5.career.active-skill.draft.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

public sealed record Sr5CareerCheckpointCas(
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long Version,
    Sr5CareerCheckpointPhase Phase,
    string IdempotencyKey)
{
    public static Sr5CareerCheckpointCas From(Sr5CareerDraftCheckpoint checkpoint)
        => new(
            checkpoint.WorkspaceId,
            checkpoint.OwnerId,
            checkpoint.ActionId,
            checkpoint.Version,
            checkpoint.Phase,
            checkpoint.IdempotencyKey);

    public bool Matches(Sr5CareerDraftCheckpoint checkpoint)
        => string.Equals(WorkspaceId, checkpoint.WorkspaceId, StringComparison.Ordinal)
           && OwnerId == checkpoint.OwnerId
           && ActionId == checkpoint.ActionId
           && Version == checkpoint.Version
           && Phase == checkpoint.Phase
           && string.Equals(IdempotencyKey, checkpoint.IdempotencyKey, StringComparison.Ordinal);
}

public sealed class Sr5CareerDraftCheckpointStore
{
    private static readonly object Gate = new();
    private readonly ISr5CareerCheckpointBackend _backend;
    private readonly ISr5CareerReviewedCheckpointAuthority? _reviewedAuthority;

    public Sr5CareerDraftCheckpointStore(ISr5CareerCheckpointBackend backend)
        : this(backend, reviewedAuthority: null)
    {
    }

    internal Sr5CareerDraftCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5CareerReviewedCheckpointAuthority? reviewedAuthority)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _reviewedAuthority = reviewedAuthority;
    }

    internal static Sr5CareerDraftCheckpointStore CreateDefault(
        ISr5CareerReviewedCheckpointAuthority reviewedAuthority)
        => new(
            new PreferencesSr5CareerCheckpointBackend(),
            reviewedAuthority ?? throw new ArgumentNullException(nameof(reviewedAuthority)));

    public bool TryRead(out Sr5CareerDraftCheckpoint checkpoint, out string blocker)
    {
        lock (Gate)
        {
            return TryReadLocked(out checkpoint, out blocker);
        }
    }

    public bool TryCreate(
        Sr5CareerDraftCheckpoint checkpoint,
        out Sr5CareerDraftCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        stored = null!;
        lock (Gate)
        {
            if (checkpoint.Version != 1
                || checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed)
            {
                blocker = "A new Career checkpoint must start as Reviewed at version 1.";
                return false;
            }
            if (TryReadLocked(out Sr5CareerDraftCheckpoint existing, out string readBlocker))
            {
                blocker = Sr5CareerCheckpointCas.From(existing).Matches(checkpoint)
                    ? "This exact action already owns the Career checkpoint. Resume it instead of overwriting it."
                    : "Another workspace, owner or action already owns the Career checkpoint.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(readBlocker))
            {
                blocker = readBlocker;
                return false;
            }
            return TryWriteAndReadBackLocked(
                checkpoint,
                rollback: null,
                out stored,
                out blocker);
        }
    }

    public bool TryBeginApply(
        Sr5CareerCheckpointCas expected,
        out Sr5CareerDraftCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        stored = null!;
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Reviewed
                || !TryRequireCasLocked(expected, out Sr5CareerDraftCheckpoint current, out blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact Reviewed owner may begin this apply."
                    : blocker;
                return false;
            }
            Sr5CareerDraftCheckpoint next = current with
            {
                Version = checked(current.Version + 1),
                Phase = Sr5CareerCheckpointPhase.Applying
            };
            return TryWriteAndReadBackLocked(next, current, out stored, out blocker);
        }
    }

    public bool TryRecordAuthoritativeResolution(
        Sr5CareerCheckpointCas expected,
        Sr5CareerRecoveryResolution resolution,
        out Sr5CareerDraftCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(resolution);
        stored = null!;
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Applying
                || !TryRequireCasLocked(expected, out Sr5CareerDraftCheckpoint current, out blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact Applying owner may record an outcome."
                    : blocker;
                return false;
            }
            if (!ResolutionMatches(expected, resolution)
                || resolution.Status == Sr5CareerRecoveryStatus.OutcomeUnknown)
            {
                blocker = "An unknown or foreign outcome cannot change the Applying checkpoint.";
                return false;
            }

            Sr5CareerCheckpointPhase nextPhase = resolution.Status switch
            {
                Sr5CareerRecoveryStatus.AppliedVerified when resolution.Receipt is not null =>
                    Sr5CareerCheckpointPhase.Applied,
                Sr5CareerRecoveryStatus.NotAppliedVerified when resolution.Receipt is null =>
                    Sr5CareerCheckpointPhase.Reviewed,
                _ => throw new InvalidOperationException(
                    "The authoritative resolution and receipt shape are inconsistent.")
            };
            Sr5CareerDraftCheckpoint next = current with
            {
                Version = checked(current.Version + 1),
                Phase = nextPhase
            };
            return TryWriteAndReadBackLocked(next, current, out stored, out blocker);
        }
    }

    internal bool TryDeleteReviewed(
        Sr5CareerCheckpointCas expected,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Reviewed
                || !TryRequireCasLocked(
                    expected,
                    out Sr5CareerDraftCheckpoint current,
                    out blocker)
                || _reviewedAuthority is null
                || !_reviewedAuthority.Owns(current))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the current SR5 owner/workspace/revision/action may abandon this Reviewed checkpoint."
                    : blocker;
                return false;
            }
            return TryDeleteAndReadBackLocked(out blocker);
        }
    }

    public bool TryDeleteApplied(
        Sr5CareerCheckpointCas expected,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Applied
                || !TryRequireCasLocked(expected, out _, out blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only an exact Applied receipt checkpoint may be acknowledged."
                    : blocker;
                return false;
            }
            return TryDeleteAndReadBackLocked(out blocker);
        }
    }

    private bool TryDeleteAndReadBackLocked(out string blocker)
    {
        try
        {
            _backend.Remove();
            if (!string.IsNullOrWhiteSpace(_backend.Read()))
            {
                blocker = "The Career checkpoint delete was not durable on read-back.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The Career checkpoint could not be deleted: {exception.Message}";
            return false;
        }
    }

    private bool TryRequireCasLocked(
        Sr5CareerCheckpointCas expected,
        out Sr5CareerDraftCheckpoint current,
        out string blocker)
    {
        if (!TryReadLocked(out current, out blocker))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The expected Career checkpoint no longer exists."
                : blocker;
            return false;
        }
        if (!expected.Matches(current))
        {
            blocker = "Career checkpoint CAS failed: workspace, owner, action, version or phase changed.";
            return false;
        }
        return true;
    }

    private bool TryWriteAndReadBackLocked(
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerDraftCheckpoint? rollback,
        out Sr5CareerDraftCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        try
        {
            _backend.Write(JsonSerializer.Serialize(checkpoint));
            if (!TryReadLocked(out Sr5CareerDraftCheckpoint readBack, out string readBlocker)
                || readBack != checkpoint)
            {
                blocker = string.IsNullOrWhiteSpace(readBlocker)
                    ? "The Career checkpoint write did not survive exact read-back."
                    : readBlocker;
                blocker += TryRestoreAfterFailedWriteLocked(rollback)
                    ? " The previous checkpoint state was restored."
                    : " Rollback could not be verified; treat the checkpoint as unresolved.";
                return false;
            }
            stored = readBack;
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The Career checkpoint could not be durably written: {exception.Message}";
            return false;
        }
    }

    private bool TryRestoreAfterFailedWriteLocked(Sr5CareerDraftCheckpoint? rollback)
    {
        try
        {
            if (rollback is null)
            {
                _backend.Remove();
                return string.IsNullOrWhiteSpace(_backend.Read());
            }

            _backend.Write(JsonSerializer.Serialize(rollback));
            return TryReadLocked(out Sr5CareerDraftCheckpoint restored, out _)
                && restored == rollback;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadLocked(
        out Sr5CareerDraftCheckpoint checkpoint,
        out string blocker)
    {
        checkpoint = null!;
        try
        {
            string payload = _backend.Read();
            if (string.IsNullOrWhiteSpace(payload))
            {
                blocker = string.Empty;
                return false;
            }
            checkpoint = JsonSerializer.Deserialize<Sr5CareerDraftCheckpoint>(payload)!;
            if (checkpoint is null || !checkpoint.IsStructurallyValid())
            {
                checkpoint = null!;
                blocker = "The saved Career checkpoint is unreadable or incomplete.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The saved Career checkpoint is unreadable: {exception.Message}";
            return false;
        }
    }

    private static bool ResolutionMatches(
        Sr5CareerCheckpointCas expected,
        Sr5CareerRecoveryResolution resolution)
        => string.Equals(expected.WorkspaceId, resolution.WorkspaceId, StringComparison.Ordinal)
           && expected.OwnerId == resolution.OwnerId
           && expected.ActionId == resolution.ActionId
           && expected.Version == resolution.CheckpointVersion;
}
