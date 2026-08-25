using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal interface ISr5CareerAttributeCheckpointAuthority :
    ISr5CareerCheckpointOwnerAuthority
{
    bool OwnsReviewed(Sr5CareerAttributeCheckpoint checkpoint);
    bool OwnsCurrentRunner(Sr5CareerAttributeCheckpoint checkpoint);
    bool OwnsResolution(
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeRecoveryStatus status);
}

internal sealed class Sr5CareerAttributeLiveCheckpointAuthority(
    ISr5CareerCheckpointOwnerAuthority ownerAuthority,
    CareerAttributeAdvanceEditorState editor,
    Func<Sr5CareerRunnerBinding> currentBinding) :
    ISr5CareerAttributeCheckpointAuthority
{
    private readonly ISr5CareerCheckpointOwnerAuthority _ownerAuthority =
        ownerAuthority ?? throw new ArgumentNullException(nameof(ownerAuthority));
    private readonly CareerAttributeAdvanceEditorState _editor =
        editor ?? throw new ArgumentNullException(nameof(editor));
    private readonly Func<Sr5CareerRunnerBinding> _currentBinding =
        currentBinding ?? throw new ArgumentNullException(nameof(currentBinding));

    public Guid CurrentOwnerId => _ownerAuthority.CurrentOwnerId;

    public bool OwnsReviewed(Sr5CareerAttributeCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = _currentBinding();
        return checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            && checkpoint.TryResume(_editor, out Sr5CareerAttributeDraft draft, out _)
            && checkpoint.MatchesActionDraft(draft)
            && CurrentOwnerId != Guid.Empty
            && CurrentOwnerId == draft.OwnerId
            && Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
            && binding.WorkspaceId == draft.WorkspaceId
            && binding.ContentRevision == draft.ExpectedContentRevision
            && binding.SavedRevision == draft.ExpectedContentRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);
    }

    public bool OwnsCurrentRunner(Sr5CareerAttributeCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = _currentBinding();
        if (!checkpoint.IsStructurallyValid()
            || CurrentOwnerId == Guid.Empty
            || checkpoint.Draft.OwnerId != CurrentOwnerId
            || !Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
            || binding.WorkspaceId != checkpoint.Draft.WorkspaceId
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            return false;
        }

        long expected = checkpoint.Draft.ExpectedContentRevision;
        return checkpoint.Phase switch
        {
            Sr5CareerCheckpointPhase.Reviewed =>
                binding.ContentRevision == expected
                && binding.SavedRevision == expected,
            Sr5CareerCheckpointPhase.Applying =>
                binding.ContentRevision == expected
                    && binding.SavedRevision == expected
                || expected < long.MaxValue
                    && binding.ContentRevision == expected + 1
                    && binding.SavedRevision == binding.ContentRevision,
            Sr5CareerCheckpointPhase.Applied =>
                expected < long.MaxValue
                && binding.ContentRevision == expected + 1
                && binding.SavedRevision == binding.ContentRevision,
            _ => false
        };
    }

    public bool OwnsResolution(
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeRecoveryStatus status)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = _currentBinding();
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying
            || !checkpoint.IsStructurallyValid()
            || CurrentOwnerId == Guid.Empty
            || checkpoint.Draft.OwnerId != CurrentOwnerId
            || !Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
            || binding.WorkspaceId != checkpoint.Draft.WorkspaceId
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            return false;
        }

        long expected = checkpoint.Draft.ExpectedContentRevision;
        return status switch
        {
            Sr5CareerAttributeRecoveryStatus.AppliedVerified =>
                expected < long.MaxValue
                && binding.ContentRevision == expected + 1
                && binding.SavedRevision == expected + 1,
            Sr5CareerAttributeRecoveryStatus.NotAppliedVerified =>
                binding.ContentRevision == expected
                && binding.SavedRevision == expected,
            _ => false
        };
    }
}

internal sealed class PreferencesSr5CareerAttributeCheckpointBackend :
    ISr5CareerCheckpointBackend
{
    private const string StorageKey = "sr5.career.attribute.draft.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

public sealed record Sr5CareerAttributeCheckpointCas(
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long Version,
    Sr5CareerCheckpointPhase Phase,
    string IdempotencyKey)
{
    public static Sr5CareerAttributeCheckpointCas From(
        Sr5CareerAttributeCheckpoint checkpoint)
        => new(
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.ExpenseId,
            checkpoint.Version,
            checkpoint.Phase,
            checkpoint.IdempotencyKey);

    public bool Matches(Sr5CareerAttributeCheckpoint checkpoint)
        => string.Equals(WorkspaceId, checkpoint.Draft.WorkspaceId.Value, StringComparison.Ordinal)
            && OwnerId == checkpoint.Draft.OwnerId
            && ActionId == checkpoint.Draft.Plan.ExpenseId
            && Version == checkpoint.Version
            && Phase == checkpoint.Phase
            && string.Equals(IdempotencyKey, checkpoint.IdempotencyKey, StringComparison.Ordinal);
}

/// <summary>
/// Durable single-action CAS journal. A malformed or prior-schema payload is a
/// replay-blocking lock and is never silently removed.
/// </summary>
public sealed class Sr5CareerAttributeCheckpointStore
{
    private static readonly object Gate = new();
    private static readonly SemaphoreSlim ApplyingMutationGate = new(1, 1);
    private readonly ISr5CareerCheckpointBackend _backend;
    private readonly ISr5CareerAttributeCheckpointAuthority? _authority;

    internal Sr5CareerAttributeCheckpointStore(ISr5CareerCheckpointBackend backend)
        : this(backend, authority: null)
    {
    }

    internal Sr5CareerAttributeCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5CareerAttributeCheckpointAuthority? authority)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _authority = authority;
    }

    internal static Sr5CareerAttributeCheckpointStore CreateDefault(
        ISr5CareerAttributeCheckpointAuthority authority)
        => new(
            new PreferencesSr5CareerAttributeCheckpointBackend(),
            authority ?? throw new ArgumentNullException(nameof(authority)));

    public bool TryRead(
        out Sr5CareerAttributeCheckpoint checkpoint,
        out string blocker)
    {
        lock (Gate)
        {
            return TryReadLocked(out checkpoint, out blocker);
        }
    }

    internal async Task<IDisposable> AcquireDurableApplyingLeaseAsync(
        Sr5CareerAttributeCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await ApplyingMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying
                    || !checkpoint.IsStructurallyValid()
                    || !TryReadLocked(out Sr5CareerAttributeCheckpoint current, out _)
                    || current != checkpoint
                    || _authority is null
                    || !_authority.OwnsCurrentRunner(current))
                {
                    throw new InvalidOperationException(
                        "The exact durable Applying checkpoint no longer owns this runner.");
                }
            }
            return new ApplyingMutationLease();
        }
        catch
        {
            ApplyingMutationGate.Release();
            throw;
        }
    }

    public bool TryCreate(
        Sr5CareerAttributeCheckpoint checkpoint,
        out Sr5CareerAttributeCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        stored = null!;
        lock (Gate)
        {
            if (!checkpoint.IsStructurallyValid()
                || checkpoint.Version != 1
                || checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed
                || _authority is null
                || !_authority.OwnsReviewed(checkpoint))
            {
                blocker = "A new attribute checkpoint must be an authenticated exact Reviewed action at version 1.";
                return false;
            }
            if (TryReadLocked(out Sr5CareerAttributeCheckpoint existing, out string readBlocker))
            {
                blocker = Sr5CareerAttributeCheckpointCas.From(existing).Matches(checkpoint)
                    ? "This exact attribute action already owns the checkpoint. Resume it."
                    : "Another owner, workspace, revision, or attribute action owns the checkpoint.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(readBlocker))
            {
                blocker = readBlocker;
                return false;
            }
            return TryWriteAndReadBackLocked(checkpoint, rollback: null, out stored, out blocker);
        }
    }

    public bool TryBeginApply(
        Sr5CareerAttributeCheckpointCas expected,
        out Sr5CareerAttributeCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        stored = null!;
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Reviewed
                || !TryRequireCasLocked(expected, out Sr5CareerAttributeCheckpoint current, out blocker)
                || _authority is null
                || !_authority.OwnsReviewed(current))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact authenticated Reviewed attribute action may begin apply."
                    : blocker;
                return false;
            }
            Sr5CareerAttributeCheckpoint next = current with
            {
                Version = checked(current.Version + 1),
                Phase = Sr5CareerCheckpointPhase.Applying
            };
            return TryWriteAndReadBackLocked(next, current, out stored, out blocker);
        }
    }

    public bool TryRecordAuthoritativeResolution(
        Sr5CareerAttributeCheckpointCas expected,
        Sr5CareerAttributeRecoveryResolution resolution,
        out Sr5CareerAttributeCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(resolution);
        stored = null!;
        blocker = string.Empty;
        if (!ApplyingMutationGate.Wait(0))
        {
            blocker = "The attribute mutation is still running; its Applying checkpoint remains locked.";
            return false;
        }
        try
        {
            lock (Gate)
            {
                if (expected.Phase != Sr5CareerCheckpointPhase.Applying
                    || !TryRequireCasLocked(expected, out Sr5CareerAttributeCheckpoint current, out blocker)
                    || _authority is null
                    || !_authority.OwnsResolution(current, resolution.Status)
                    || !ResolutionMatches(current, resolution))
                {
                    blocker = string.IsNullOrWhiteSpace(blocker)
                        ? "Only a fresh signed exact outcome may advance this Applying attribute checkpoint."
                        : blocker;
                    return false;
                }

                Sr5CareerCheckpointPhase nextPhase = resolution.Status switch
                {
                    Sr5CareerAttributeRecoveryStatus.AppliedVerified when resolution.Receipt is not null =>
                        Sr5CareerCheckpointPhase.Applied,
                    Sr5CareerAttributeRecoveryStatus.NotAppliedVerified when resolution.Receipt is null =>
                        Sr5CareerCheckpointPhase.Reviewed,
                    _ => throw new InvalidOperationException(
                        "Unknown or inconsistent attribute outcomes cannot change the checkpoint.")
                };
                Sr5CareerAttributeCheckpoint next = current with
                {
                    Version = checked(current.Version + 1),
                    Phase = nextPhase
                };
                return TryWriteAndReadBackLocked(next, current, out stored, out blocker);
            }
        }
        finally
        {
            ApplyingMutationGate.Release();
        }
    }

    internal bool TryDeleteReviewed(
        Sr5CareerAttributeCheckpointCas expected,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Reviewed
                || !TryRequireCasLocked(expected, out Sr5CareerAttributeCheckpoint current, out blocker)
                || _authority is null
                || !_authority.OwnsReviewed(current))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact current owner may abandon this Reviewed attribute checkpoint."
                    : blocker;
                return false;
            }
            return TryDeleteAndReadBackLocked(current, out blocker);
        }
    }

    internal bool TryDeleteApplied(
        Sr5CareerAttributeCheckpointCas expected,
        CharacterCareerAttributeAdvanceReceipt receipt,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(receipt);
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Applied
                || !TryRequireCasLocked(expected, out Sr5CareerAttributeCheckpoint current, out blocker)
                || _authority is null
                || !_authority.OwnsCurrentRunner(current)
                || !Sr5CareerAttributeCoordinator.ReceiptMatchesDraft(current.Draft, receipt))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact current owner and saved receipt may acknowledge this Applied attribute checkpoint."
                    : blocker;
                return false;
            }
            return TryDeleteAndReadBackLocked(current, out blocker);
        }
    }

    private bool TryRequireCasLocked(
        Sr5CareerAttributeCheckpointCas expected,
        out Sr5CareerAttributeCheckpoint current,
        out string blocker)
    {
        if (!TryReadLocked(out current, out blocker))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The expected attribute checkpoint no longer exists."
                : blocker;
            return false;
        }
        if (!expected.Matches(current))
        {
            blocker = "Attribute checkpoint CAS failed: owner, workspace, action, version, or phase changed.";
            return false;
        }
        return true;
    }

    private bool TryReadLocked(
        out Sr5CareerAttributeCheckpoint checkpoint,
        out string blocker)
    {
        checkpoint = null!;
        string payload;
        try
        {
            payload = _backend.Read();
        }
        catch (Exception exception)
        {
            blocker = $"The attribute checkpoint could not be read: {exception.Message}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(payload))
        {
            blocker = string.Empty;
            return false;
        }
        try
        {
            Sr5CareerAttributeCheckpoint? parsed =
                JsonSerializer.Deserialize<Sr5CareerAttributeCheckpoint>(payload);
            if (parsed is null || !parsed.IsStructurallyValid())
            {
                blocker = "The durable attribute checkpoint is unreadable and remains a replay-blocking lock.";
                return false;
            }
            checkpoint = parsed;
            blocker = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            blocker = "The durable attribute checkpoint is unreadable and remains a replay-blocking lock.";
            return false;
        }
    }

    private bool TryWriteAndReadBackLocked(
        Sr5CareerAttributeCheckpoint value,
        Sr5CareerAttributeCheckpoint? rollback,
        out Sr5CareerAttributeCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        string payload = JsonSerializer.Serialize(value);
        string rollbackPayload = rollback is null ? string.Empty : JsonSerializer.Serialize(rollback);
        try
        {
            _backend.Write(payload);
            string readBack = _backend.Read();
            Sr5CareerAttributeCheckpoint? parsed =
                JsonSerializer.Deserialize<Sr5CareerAttributeCheckpoint>(readBack);
            if (!string.Equals(readBack, payload, StringComparison.Ordinal)
                || parsed is null
                || !parsed.IsStructurallyValid()
                || !Sr5CareerAttributeCheckpointCas.From(value).Matches(parsed))
            {
                RestoreLocked(rollbackPayload);
                blocker = "The attribute checkpoint write was not durable on exact read-back.";
                return false;
            }
            stored = parsed;
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            RestoreLocked(rollbackPayload);
            blocker = $"The attribute checkpoint could not be written: {exception.Message}";
            return false;
        }
    }

    private void RestoreLocked(string rollbackPayload)
    {
        try
        {
            if (string.IsNullOrEmpty(rollbackPayload))
            {
                _backend.Remove();
            }
            else
            {
                _backend.Write(rollbackPayload);
            }
        }
        catch
        {
            // The caller receives a fail-closed result; a surviving payload remains a lock.
        }
    }

    private bool TryDeleteAndReadBackLocked(
        Sr5CareerAttributeCheckpoint rollback,
        out string blocker)
    {
        string rollbackPayload = JsonSerializer.Serialize(rollback);
        try
        {
            _backend.Remove();
            string readBack = _backend.Read();
            if (!string.IsNullOrWhiteSpace(readBack))
            {
                if (!string.Equals(readBack, rollbackPayload, StringComparison.Ordinal))
                {
                    RestoreExactLocked(rollbackPayload);
                }
                blocker = "The attribute checkpoint delete was not durable on read-back.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            bool restored = RestoreExactLocked(rollbackPayload);
            blocker = restored
                ? $"The attribute checkpoint could not be deleted and was restored: {exception.Message}"
                : $"The attribute checkpoint delete outcome is unknown and remains replay-blocked: {exception.Message}";
            return false;
        }
    }

    private bool RestoreExactLocked(string payload)
    {
        try
        {
            _backend.Write(payload);
            return string.Equals(_backend.Read(), payload, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool ResolutionMatches(
        Sr5CareerAttributeCheckpoint checkpoint,
        Sr5CareerAttributeRecoveryResolution resolution)
        => resolution.Status != Sr5CareerAttributeRecoveryStatus.OutcomeUnknown
            && string.Equals(
                resolution.WorkspaceId,
                checkpoint.Draft.WorkspaceId.Value,
                StringComparison.Ordinal)
            && resolution.OwnerId == checkpoint.Draft.OwnerId
            && resolution.ActionId == checkpoint.Draft.Plan.ExpenseId
            && resolution.CheckpointVersion == checkpoint.Version
            && Sr5CareerAttributeRecoveryProof.Verifies(checkpoint, resolution)
            && (resolution.Status == Sr5CareerAttributeRecoveryStatus.AppliedVerified
                ? resolution.Receipt is not null
                    && Sr5CareerAttributeCoordinator.ReceiptMatchesDraft(
                        checkpoint.Draft,
                        resolution.Receipt)
                : resolution.Receipt is null);

    private sealed class ApplyingMutationLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ApplyingMutationGate.Release();
            }
        }
    }
}
