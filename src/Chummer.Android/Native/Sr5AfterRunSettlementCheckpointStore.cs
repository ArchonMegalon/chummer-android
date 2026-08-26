using System.Text.Json;
using Chummer.Contracts.Characters;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal interface ISr5AfterRunSettlementCheckpointAuthority :
    ISr5CareerCheckpointOwnerAuthority
{
    bool OwnsReviewed(Sr5AfterRunSettlementCheckpoint checkpoint);
    bool OwnsCurrentRunner(Sr5AfterRunSettlementCheckpoint checkpoint);
    bool OwnsResolution(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementRecoveryStatus status);
}

internal sealed class Sr5AfterRunSettlementLiveCheckpointAuthority(
    ISr5CareerCheckpointOwnerAuthority ownerAuthority,
    Sr5AfterRunSettlementEditorState editor,
    Func<Sr5CareerRunnerBinding> currentBinding) :
    ISr5AfterRunSettlementCheckpointAuthority
{
    private readonly ISr5CareerCheckpointOwnerAuthority _ownerAuthority =
        ownerAuthority ?? throw new ArgumentNullException(nameof(ownerAuthority));
    private readonly Sr5AfterRunSettlementEditorState _editor =
        editor ?? throw new ArgumentNullException(nameof(editor));
    private readonly Func<Sr5CareerRunnerBinding> _currentBinding =
        currentBinding ?? throw new ArgumentNullException(nameof(currentBinding));

    public Guid CurrentOwnerId => _ownerAuthority.CurrentOwnerId;

    public bool OwnsReviewed(Sr5AfterRunSettlementCheckpoint checkpoint)
    {
        Sr5CareerRunnerBinding binding = _currentBinding();
        return checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            && checkpoint.TryResume(_editor, out Sr5AfterRunSettlementDraft draft, out _)
            && checkpoint.MatchesActionDraft(draft)
            && CurrentOwnerId != Guid.Empty
            && CurrentOwnerId == draft.OwnerId
            && OwnsCleanRunner(binding, draft.WorkspaceId.Value)
            && binding.ContentRevision == draft.ExpectedWorkspaceRevision;
    }

    public bool OwnsCurrentRunner(Sr5AfterRunSettlementCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = _currentBinding();
        if (!checkpoint.IsStructurallyValid()
            || CurrentOwnerId == Guid.Empty
            || checkpoint.Draft.OwnerId != CurrentOwnerId
            || !OwnsCleanRunner(binding, checkpoint.Draft.WorkspaceId.Value))
        {
            return false;
        }
        long expected = checkpoint.Draft.ExpectedWorkspaceRevision;
        return checkpoint.Phase switch
        {
            Sr5CareerCheckpointPhase.Reviewed =>
                binding.ContentRevision == expected,
            Sr5CareerCheckpointPhase.Applying =>
                binding.ContentRevision == expected
                || expected < long.MaxValue
                    && binding.ContentRevision == expected + 1,
            Sr5CareerCheckpointPhase.Applied =>
                expected < long.MaxValue
                && binding.ContentRevision == expected + 1,
            _ => false
        };
    }

    public bool OwnsResolution(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementRecoveryStatus status)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = _currentBinding();
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying
            || !checkpoint.IsStructurallyValid()
            || CurrentOwnerId == Guid.Empty
            || checkpoint.Draft.OwnerId != CurrentOwnerId
            || !OwnsCleanRunner(binding, checkpoint.Draft.WorkspaceId.Value))
        {
            return false;
        }
        long expected = checkpoint.Draft.ExpectedWorkspaceRevision;
        return status switch
        {
            Sr5AfterRunSettlementRecoveryStatus.AppliedVerified =>
                expected < long.MaxValue
                && binding.ContentRevision == expected + 1,
            Sr5AfterRunSettlementRecoveryStatus.NotAppliedVerified =>
                binding.ContentRevision == expected,
            _ => false
        };
    }

    private static bool OwnsCleanRunner(
        Sr5CareerRunnerBinding binding,
        string expectedWorkspaceId)
        => Sr5CareerWizardCatalog.IsSr5CareerRunner(
                binding.Created,
                binding.GameEdition)
            && binding.WorkspaceId is { } workspaceId
            && string.Equals(
                workspaceId.Value,
                expectedWorkspaceId,
                StringComparison.Ordinal)
            && binding.SavedRevision == binding.ContentRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);
}

internal sealed class PreferencesSr5AfterRunSettlementCheckpointBackend :
    ISr5CareerCheckpointBackend
{
    private const string StorageKey = "sr5.after-run.settlement.checkpoint.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

public sealed record Sr5AfterRunSettlementCheckpointCas(
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long Version,
    Sr5CareerCheckpointPhase Phase,
    string IdempotencyKey)
{
    public static Sr5AfterRunSettlementCheckpointCas From(
        Sr5AfterRunSettlementCheckpoint checkpoint)
        => new(
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.TransactionId,
            checkpoint.Version,
            checkpoint.Phase,
            checkpoint.IdempotencyKey);

    public bool Matches(Sr5AfterRunSettlementCheckpoint checkpoint)
        => string.Equals(
                WorkspaceId,
                checkpoint.Draft.WorkspaceId.Value,
                StringComparison.Ordinal)
            && OwnerId == checkpoint.Draft.OwnerId
            && ActionId == checkpoint.Draft.Plan.TransactionId
            && Version == checkpoint.Version
            && Phase == checkpoint.Phase
            && string.Equals(
                IdempotencyKey,
                checkpoint.IdempotencyKey,
                StringComparison.Ordinal);
}

/// <summary>
/// Durable CAS journal for a single After Run transaction. A malformed payload
/// is a replay-blocking lock. The shared cross-lane owner is released only
/// after an exact authoritative resolution survives write/read-back.
/// </summary>
public sealed class Sr5AfterRunSettlementCheckpointStore
{
    private static readonly object Gate = new();
    private readonly ISr5CareerCheckpointBackend _backend;
    private readonly ISr5AfterRunSettlementCheckpointAuthority? _authority;
    private readonly Sr5CareerMutationOwnerStore _mutationOwners;

    internal Sr5AfterRunSettlementCheckpointStore(
        ISr5CareerCheckpointBackend backend)
        : this(
            backend,
            authority: null,
            Sr5CareerMutationOwnerStore.CreateIsolated())
    {
    }

    internal Sr5AfterRunSettlementCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5AfterRunSettlementCheckpointAuthority? authority,
        Sr5CareerMutationOwnerStore mutationOwners)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _authority = authority;
        _mutationOwners = mutationOwners
            ?? throw new ArgumentNullException(nameof(mutationOwners));
    }

    internal static Sr5AfterRunSettlementCheckpointStore CreateDefault(
        ISr5AfterRunSettlementCheckpointAuthority authority)
        => new(
            new PreferencesSr5AfterRunSettlementCheckpointBackend(),
            authority ?? throw new ArgumentNullException(nameof(authority)),
            Sr5CareerMutationOwnerStore.CreateDefault());

    public bool TryRead(
        out Sr5AfterRunSettlementCheckpoint checkpoint,
        out string blocker)
    {
        bool found;
        lock (Gate)
        {
            found = TryReadLocked(out checkpoint, out blocker);
        }
        if (found)
        {
            TryReconcileResolvedOwner(checkpoint);
        }
        return found;
    }

    public bool TryCreate(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        out Sr5AfterRunSettlementCheckpoint stored,
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
                blocker = "A new After Run checkpoint must be an authenticated exact Reviewed action at version 1.";
                return false;
            }
            if (TryReadLocked(
                    out Sr5AfterRunSettlementCheckpoint existing,
                    out string readBlocker))
            {
                blocker = Sr5AfterRunSettlementCheckpointCas.From(existing)
                    .Matches(checkpoint)
                        ? "This exact After Run action already owns the checkpoint. Resume it."
                        : "Another owner, workspace, revision, or After Run action owns the checkpoint.";
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
        Sr5AfterRunSettlementCheckpointCas expected,
        out Sr5AfterRunSettlementCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        stored = null!;
        blocker = string.Empty;
        Sr5CareerMutationOwner owner;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Reviewed
                || !TryRequireCasLocked(
                    expected,
                    out Sr5AfterRunSettlementCheckpoint current,
                    out blocker)
                || _authority is null
                || !_authority.OwnsReviewed(current))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact authenticated Reviewed After Run action may begin apply."
                    : blocker;
                return false;
            }
            owner = MutationOwnerForNextApplying(current);
        }

        Sr5AfterRunSettlementCheckpoint? durable = null;
        bool began = _mutationOwners.TryBegin(
            owner,
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireCasLocked(
                            expected,
                            out Sr5AfterRunSettlementCheckpoint current,
                            out string casBlocker)
                        || _authority is null
                        || !_authority.OwnsReviewed(current))
                    {
                        return new Sr5CareerMutationBeginResult(
                            Success: false,
                            ExactReviewedStateWasRestored: false,
                            Blocker: string.IsNullOrWhiteSpace(casBlocker)
                                ? "The reviewed After Run checkpoint changed before apply."
                                : casBlocker);
                    }
                    Sr5AfterRunSettlementCheckpoint next = current with
                    {
                        Version = checked(current.Version + 1),
                        Phase = Sr5CareerCheckpointPhase.Applying
                    };
                    bool wrote = TryWriteAndReadBackLocked(
                        next,
                        current,
                        out Sr5AfterRunSettlementCheckpoint written,
                        out string writeBlocker);
                    if (wrote)
                    {
                        durable = written;
                    }
                    bool restored = !wrote
                        && TryReadLocked(out Sr5AfterRunSettlementCheckpoint restoredCheckpoint, out _)
                        && DurablyEquivalent(restoredCheckpoint, current);
                    return new Sr5CareerMutationBeginResult(
                        wrote,
                        restored,
                        writeBlocker);
                }
            },
            out blocker);
        if (!began || durable is null)
        {
            return false;
        }
        stored = durable;
        return true;
    }

    internal async Task<IDisposable> AcquireDurableApplyingLeaseAsync(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerMutationOwner owner = MutationOwnerFromApplying(checkpoint);
        IDisposable lease = await _mutationOwners.AcquireExecutionLeaseAsync(
            owner,
            cancellationToken).ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying
                    || !checkpoint.IsStructurallyValid()
                    || !TryReadLocked(
                        out Sr5AfterRunSettlementCheckpoint current,
                        out _)
                    || !DurablyEquivalent(current, checkpoint)
                    || _authority is null
                    || !_authority.OwnsCurrentRunner(current))
                {
                    throw new InvalidOperationException(
                        "The exact durable After Run Applying checkpoint no longer owns this runner.");
                }
            }
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public bool TryRecordAuthoritativeResolution(
        Sr5AfterRunSettlementCheckpointCas expected,
        Sr5AfterRunSettlementRecoveryResolution resolution,
        out Sr5AfterRunSettlementCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(resolution);
        stored = null!;
        Sr5CareerMutationOwner owner;
        lock (Gate)
        {
            if (!TryRequireCasLocked(
                    expected,
                    out Sr5AfterRunSettlementCheckpoint current,
                    out blocker)
                || current.Phase != Sr5CareerCheckpointPhase.Applying
                || !ResolutionMatches(current, resolution)
                || _authority is null
                || !_authority.OwnsResolution(current, resolution.Status))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only an exact authoritative After Run resolution may advance Applying."
                    : blocker;
                return false;
            }
            owner = MutationOwnerFromApplying(current);
        }

        Sr5AfterRunSettlementCheckpoint? durable = null;
        bool completed = _mutationOwners.TryComplete(
            owner,
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireCasLocked(
                            expected,
                            out Sr5AfterRunSettlementCheckpoint current,
                            out string casBlocker)
                        || !ResolutionMatches(current, resolution)
                        || _authority is null
                        || !_authority.OwnsResolution(current, resolution.Status))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "The Applying After Run checkpoint changed before resolution."
                            : casBlocker);
                    }
                    Sr5AfterRunSettlementCheckpoint next = resolution.Status switch
                    {
                        Sr5AfterRunSettlementRecoveryStatus.AppliedVerified =>
                            current with
                            {
                                Version = checked(current.Version + 1),
                                Phase = Sr5CareerCheckpointPhase.Applied,
                                Receipt = resolution.Receipt
                            },
                        Sr5AfterRunSettlementRecoveryStatus.NotAppliedVerified =>
                            current with
                            {
                                Version = checked(current.Version + 1),
                                Phase = Sr5CareerCheckpointPhase.Reviewed,
                                Receipt = null
                            },
                        _ => throw new InvalidOperationException(
                            "OutcomeUnknown must remain Applying and keep the shared owner.")
                    };
                    bool wrote = TryWriteAndReadBackLocked(
                        next,
                        current,
                        out Sr5AfterRunSettlementCheckpoint written,
                        out string writeBlocker);
                    if (wrote)
                    {
                        durable = written;
                    }
                    return (wrote, writeBlocker);
                }
            },
            out blocker);
        if (!completed || durable is null)
        {
            return false;
        }
        stored = durable;
        return true;
    }

    public bool TryDeleteReviewed(
        Sr5AfterRunSettlementCheckpointCas expected,
        out string blocker)
        => TryDelete(expected, Sr5CareerCheckpointPhase.Reviewed, out blocker);

    public bool TryDeleteApplied(
        Sr5AfterRunSettlementCheckpointCas expected,
        out string blocker)
        => TryDelete(expected, Sr5CareerCheckpointPhase.Applied, out blocker);

    private bool TryDelete(
        Sr5AfterRunSettlementCheckpointCas expected,
        Sr5CareerCheckpointPhase requiredPhase,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != requiredPhase
                || !TryRequireCasLocked(
                    expected,
                    out Sr5AfterRunSettlementCheckpoint current,
                    out blocker)
                || _authority is null
                || !_authority.OwnsCurrentRunner(current))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? $"Only the exact owned {requiredPhase} After Run checkpoint may be removed."
                    : blocker;
                return false;
            }
            return TryDeleteAndReadBackLocked(current, out blocker);
        }
    }

    private bool TryRequireCasLocked(
        Sr5AfterRunSettlementCheckpointCas expected,
        out Sr5AfterRunSettlementCheckpoint checkpoint,
        out string blocker)
    {
        if (!TryReadLocked(out checkpoint, out blocker))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "No durable After Run checkpoint exists."
                : blocker;
            return false;
        }
        if (!expected.Matches(checkpoint))
        {
            blocker = "The After Run checkpoint changed; reopen the exact durable action.";
            return false;
        }
        return true;
    }

    private bool TryReadLocked(
        out Sr5AfterRunSettlementCheckpoint checkpoint,
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
            checkpoint = JsonSerializer.Deserialize<Sr5AfterRunSettlementCheckpoint>(
                payload)!;
            if (checkpoint is null || !checkpoint.IsStructurallyValid())
            {
                checkpoint = null!;
                blocker = "The durable After Run checkpoint is malformed or prior-schema and remains replay-blocking.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The durable After Run checkpoint is unreadable and replay-blocking: {exception.Message}";
            return false;
        }
    }

    private bool TryWriteAndReadBackLocked(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementCheckpoint? rollback,
        out Sr5AfterRunSettlementCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        string? rollbackPayload = rollback is null
            ? null
            : JsonSerializer.Serialize(rollback);
        try
        {
            _backend.Write(JsonSerializer.Serialize(checkpoint));
            if (TryReadLocked(out Sr5AfterRunSettlementCheckpoint readBack, out blocker)
                && DurablyEquivalent(readBack, checkpoint))
            {
                stored = readBack;
                return true;
            }
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The After Run checkpoint write did not survive exact read-back."
                : blocker;
        }
        catch (Exception exception)
        {
            blocker = $"The After Run checkpoint could not be written: {exception.Message}";
        }

        if (rollbackPayload is not null)
        {
            try
            {
                _backend.Write(rollbackPayload);
            }
            catch
            {
                blocker += " The prior checkpoint could not be restored; treat the journal as unknown.";
            }
        }
        return false;
    }

    private bool TryDeleteAndReadBackLocked(
        Sr5AfterRunSettlementCheckpoint rollback,
        out string blocker)
    {
        try
        {
            _backend.Remove();
            if (string.IsNullOrWhiteSpace(_backend.Read()))
            {
                blocker = string.Empty;
                return true;
            }
            _backend.Write(JsonSerializer.Serialize(rollback));
            blocker = "The After Run checkpoint delete did not survive read-back; the exact checkpoint was restored.";
            return false;
        }
        catch (Exception exception)
        {
            blocker = $"The After Run checkpoint could not be deleted safely: {exception.Message}";
            return false;
        }
    }

    private static bool ResolutionMatches(
        Sr5AfterRunSettlementCheckpoint checkpoint,
        Sr5AfterRunSettlementRecoveryResolution resolution)
        => checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            && resolution.Status is Sr5AfterRunSettlementRecoveryStatus.AppliedVerified
                or Sr5AfterRunSettlementRecoveryStatus.NotAppliedVerified
            && string.Equals(
                resolution.WorkspaceId,
                checkpoint.Draft.WorkspaceId.Value,
                StringComparison.Ordinal)
            && resolution.OwnerId == checkpoint.Draft.OwnerId
            && resolution.ActionId == checkpoint.Draft.Plan.TransactionId
            && resolution.CheckpointVersion == checkpoint.Version
            && (resolution.Status == Sr5AfterRunSettlementRecoveryStatus.AppliedVerified
                ? resolution.Receipt is not null
                    && Sr5AfterRunSettlementCoordinator.ReceiptMatchesDraft(
                        checkpoint.Draft,
                        resolution.Receipt)
                : resolution.Receipt is null)
            && Sr5AfterRunSettlementRecoveryProof.Verifies(checkpoint, resolution);

    private static Sr5CareerMutationOwner MutationOwnerForNextApplying(
        Sr5AfterRunSettlementCheckpoint checkpoint)
        => new(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.AfterRunSettlement,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.TransactionId,
            checked(checkpoint.Version + 1),
            checkpoint.Draft.ExpectedWorkspaceRevision,
            checkpoint.IdempotencyKey);

    private static Sr5CareerMutationOwner MutationOwnerFromApplying(
        Sr5AfterRunSettlementCheckpoint checkpoint)
    {
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying)
        {
            throw new InvalidOperationException(
                "Only an Applying After Run checkpoint owns the shared mutation lock.");
        }
        return new Sr5CareerMutationOwner(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.AfterRunSettlement,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.TransactionId,
            checkpoint.Version,
            checkpoint.Draft.ExpectedWorkspaceRevision,
            checkpoint.IdempotencyKey);
    }

    private void TryReconcileResolvedOwner(
        Sr5AfterRunSettlementCheckpoint checkpoint)
    {
        if (checkpoint.Phase == Sr5CareerCheckpointPhase.Applying)
        {
            return;
        }
        var owner = new Sr5CareerMutationOwner(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.AfterRunSettlement,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.TransactionId,
            checked(checkpoint.Version - 1),
            checkpoint.Draft.ExpectedWorkspaceRevision,
            checkpoint.IdempotencyKey);
        _mutationOwners.TryReconcileResolved(
            owner,
            () =>
            {
                lock (Gate)
                {
                    return TryReadLocked(
                            out Sr5AfterRunSettlementCheckpoint current,
                            out _)
                        && DurablyEquivalent(current, checkpoint)
                        && current.Phase != Sr5CareerCheckpointPhase.Applying;
                }
            },
            out _);
    }

    private static bool DurablyEquivalent(
        Sr5AfterRunSettlementCheckpoint left,
        Sr5AfterRunSettlementCheckpoint right)
        => left.SchemaVersion == right.SchemaVersion
            && left.Version == right.Version
            && string.Equals(left.RouteId, right.RouteId, StringComparison.Ordinal)
            && left.Phase == right.Phase
            && left.Draft.SemanticallyEquals(right.Draft)
            && string.Equals(
                left.IdempotencyKey,
                right.IdempotencyKey,
                StringComparison.Ordinal)
            && (left.Receipt is null && right.Receipt is null
                || left.Receipt is { } leftReceipt
                    && right.Receipt is { } rightReceipt
                    && CharacterAfterRunSettlementRules.IsCoherent(leftReceipt)
                    && CharacterAfterRunSettlementRules.IsCoherent(rightReceipt)
                    && string.Equals(
                        leftReceipt.ReceiptDigest,
                        rightReceipt.ReceiptDigest,
                        StringComparison.Ordinal));
}
