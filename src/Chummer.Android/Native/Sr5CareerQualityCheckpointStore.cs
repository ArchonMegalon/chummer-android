using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal interface ISr5CareerQualityCheckpointAuthority :
    ISr5CareerCheckpointOwnerAuthority
{
    bool OwnsReviewed(Sr5CareerQualityCheckpoint checkpoint);
    bool OwnsCurrentRunner(Sr5CareerQualityCheckpoint checkpoint);
    bool OwnsResolution(
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityRecoveryStatus status);
    bool OwnsCorrected(
        Sr5CareerQualityCheckpoint checkpoint,
        CharacterCareerQualityReceipt receipt,
        CharacterCareerQualityCorrectionPlan correction);
}

internal sealed class Sr5CareerQualityLiveCheckpointAuthority(
    ISr5CareerCheckpointOwnerAuthority ownerAuthority,
    CareerQualityEditorState editor,
    Func<Sr5CareerRunnerBinding> currentBinding) :
    ISr5CareerQualityCheckpointAuthority
{
    private readonly ISr5CareerCheckpointOwnerAuthority _ownerAuthority =
        ownerAuthority ?? throw new ArgumentNullException(nameof(ownerAuthority));
    private readonly CareerQualityEditorState _editor =
        editor ?? throw new ArgumentNullException(nameof(editor));
    private readonly Func<Sr5CareerRunnerBinding> _currentBinding =
        currentBinding ?? throw new ArgumentNullException(nameof(currentBinding));

    public Guid CurrentOwnerId => _ownerAuthority.CurrentOwnerId;

    public bool OwnsReviewed(Sr5CareerQualityCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding binding = _currentBinding();
        return checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            && checkpoint.TryResume(_editor, out Sr5CareerQualityDraft draft, out _)
            && checkpoint.MatchesActionDraft(draft)
            && CurrentOwnerId != Guid.Empty
            && CurrentOwnerId == draft.OwnerId
            && Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
            && binding.WorkspaceId == draft.WorkspaceId
            && binding.ContentRevision == draft.ExpectedWorkspaceRevision
            && binding.SavedRevision == draft.ExpectedSavedRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);
    }

    public bool OwnsCurrentRunner(Sr5CareerQualityCheckpoint checkpoint)
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

        long expected = checkpoint.Draft.ExpectedWorkspaceRevision;
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
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityRecoveryStatus status)
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

        long expected = checkpoint.Draft.ExpectedWorkspaceRevision;
        return status switch
        {
            Sr5CareerQualityRecoveryStatus.AppliedVerified =>
                expected < long.MaxValue
                && binding.ContentRevision == expected + 1
                && binding.SavedRevision == expected + 1,
            Sr5CareerQualityRecoveryStatus.NotAppliedVerified =>
                binding.ContentRevision == expected
                && binding.SavedRevision == expected,
            _ => false
        };
    }

    public bool OwnsCorrected(
        Sr5CareerQualityCheckpoint checkpoint,
        CharacterCareerQualityReceipt receipt,
        CharacterCareerQualityCorrectionPlan correction)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(correction);
        Sr5CareerRunnerBinding binding = _currentBinding();
        if (checkpoint.Draft.ExpectedWorkspaceRevision >= long.MaxValue - 1
            || checkpoint.Draft.ExpectedSavedRevision >= long.MaxValue - 1)
        {
            return false;
        }
        long expectedAppliedRevision = checkpoint.Draft.ExpectedWorkspaceRevision + 1;
        return checkpoint.Phase == Sr5CareerCheckpointPhase.Applied
            && checkpoint.IsStructurallyValid()
            && CurrentOwnerId != Guid.Empty
            && checkpoint.Draft.OwnerId == CurrentOwnerId
            && Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
            && binding.WorkspaceId == checkpoint.Draft.WorkspaceId
            && binding.ContentRevision == expectedAppliedRevision + 1
            && binding.SavedRevision == binding.ContentRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error)
            && Sr5CareerQualityCoordinator.ReceiptMatchesDraft(checkpoint.Draft, receipt)
            && CharacterCareerQualityRules.IsCoherent(correction)
            && correction.OriginalTransactionId == receipt.TransactionId
            && correction.Identity == receipt.Identity
            && string.Equals(
                correction.OriginalReceiptDigest,
                receipt.ReceiptDigest,
                StringComparison.Ordinal);
    }
}

internal sealed class PreferencesSr5CareerQualityCheckpointBackend :
    ISr5CareerCheckpointBackend
{
    private const string StorageKey = "sr5.career.quality.draft.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

public sealed record Sr5CareerQualityCheckpointCas(
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long Version,
    Sr5CareerCheckpointPhase Phase,
    string IdempotencyKey)
{
    public static Sr5CareerQualityCheckpointCas From(
        Sr5CareerQualityCheckpoint checkpoint)
        => new(
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.TransactionId,
            checkpoint.Version,
            checkpoint.Phase,
            checkpoint.IdempotencyKey);

    public bool Matches(Sr5CareerQualityCheckpoint checkpoint)
        => string.Equals(WorkspaceId, checkpoint.Draft.WorkspaceId.Value, StringComparison.Ordinal)
            && OwnerId == checkpoint.Draft.OwnerId
            && ActionId == checkpoint.Draft.TransactionId
            && Version == checkpoint.Version
            && Phase == checkpoint.Phase
            && string.Equals(IdempotencyKey, checkpoint.IdempotencyKey, StringComparison.Ordinal);
}

/// <summary>
/// Persistent single-action CAS journal with exact write/read-back validation.
/// A malformed or prior-schema payload is a
/// replay-blocking lock and is never silently removed.
/// </summary>
public sealed class Sr5CareerQualityCheckpointStore
{
    private static readonly object Gate = new();
    private readonly ISr5CareerCheckpointBackend _backend;
    private readonly ISr5CareerQualityCheckpointAuthority? _authority;
    private readonly Sr5CareerMutationOwnerStore _mutationOwners;

    internal Sr5CareerQualityCheckpointStore(ISr5CareerCheckpointBackend backend)
        : this(
            backend,
            authority: null,
            Sr5CareerMutationOwnerStore.CreateIsolated())
    {
    }

    internal Sr5CareerQualityCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5CareerQualityCheckpointAuthority? authority)
        : this(
            backend,
            authority,
            Sr5CareerMutationOwnerStore.CreateIsolated())
    {
    }

    internal Sr5CareerQualityCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5CareerQualityCheckpointAuthority? authority,
        Sr5CareerMutationOwnerStore mutationOwners)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _authority = authority;
        _mutationOwners = mutationOwners ?? throw new ArgumentNullException(nameof(mutationOwners));
    }

    internal static Sr5CareerQualityCheckpointStore CreateDefault(
        ISr5CareerQualityCheckpointAuthority authority)
        => new(
            new PreferencesSr5CareerQualityCheckpointBackend(),
            authority ?? throw new ArgumentNullException(nameof(authority)),
            Sr5CareerMutationOwnerStore.CreateDefault());

    public bool TryRead(
        out Sr5CareerQualityCheckpoint checkpoint,
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

    internal async Task<IDisposable> AcquireDurableApplyingLeaseAsync(
        Sr5CareerQualityCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerMutationOwner owner = MutationOwnerFromApplying(checkpoint);
        IDisposable mutationLease =
            await _mutationOwners.AcquireExecutionLeaseAsync(owner, cancellationToken)
                .ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying
                    || !checkpoint.IsStructurallyValid()
                    || !TryReadLocked(out Sr5CareerQualityCheckpoint current, out _)
                    || !DurablyEquivalent(current, checkpoint)
                    || _authority is null
                    || !_authority.OwnsCurrentRunner(current))
                {
                    throw new InvalidOperationException(
                        "The exact durable Applying checkpoint no longer owns this runner.");
                }
            }
            return mutationLease;
        }
        catch
        {
            mutationLease.Dispose();
            throw;
        }
    }

    public bool TryCreate(
        Sr5CareerQualityCheckpoint checkpoint,
        out Sr5CareerQualityCheckpoint stored,
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
                blocker = "A new quality checkpoint must be an authenticated exact Reviewed action at version 1.";
                return false;
            }
            if (TryReadLocked(out Sr5CareerQualityCheckpoint existing, out string readBlocker))
            {
                blocker = Sr5CareerQualityCheckpointCas.From(existing).Matches(checkpoint)
                    ? "This exact quality action already owns the checkpoint. Resume it."
                    : "Another owner, workspace, revision, or quality action owns the checkpoint.";
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
        Sr5CareerQualityCheckpointCas expected,
        out Sr5CareerQualityCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        stored = null!;
        blocker = string.Empty;
        Sr5CareerMutationOwner owner;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Reviewed
                || !TryRequireCasLocked(expected, out Sr5CareerQualityCheckpoint current, out blocker)
                || _authority is null
                || !_authority.OwnsReviewed(current))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact authenticated Reviewed quality action may begin apply."
                    : blocker;
                return false;
            }
            owner = MutationOwnerForNextApplying(current);
        }

        Sr5CareerQualityCheckpoint? durable = null;
        bool began = _mutationOwners.TryBegin(
            owner,
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireCasLocked(
                            expected,
                            out Sr5CareerQualityCheckpoint current,
                            out string casBlocker)
                        || _authority is null
                        || !_authority.OwnsReviewed(current))
                    {
                        return new Sr5CareerMutationBeginResult(
                            false,
                            false,
                            string.IsNullOrWhiteSpace(casBlocker)
                                ? "Only the exact authenticated Reviewed quality action may begin apply."
                                : casBlocker);
                    }
                    Sr5CareerQualityCheckpoint next = current with
                    {
                        Version = checked(current.Version + 1),
                        Phase = Sr5CareerCheckpointPhase.Applying
                    };
                    bool wrote = TryWriteAndReadBackLocked(
                        next,
                        current,
                        out Sr5CareerQualityCheckpoint written,
                        out string writeBlocker);
                    if (wrote)
                    {
                        durable = written;
                        return new Sr5CareerMutationBeginResult(true, false, string.Empty);
                    }
                    bool restored = TryReadLocked(out Sr5CareerQualityCheckpoint readBack, out _)
                        && expected.Matches(readBack);
                    return new Sr5CareerMutationBeginResult(false, restored, writeBlocker);
                }
            },
            out blocker);
        if (began && durable is not null)
        {
            stored = durable;
            return true;
        }
        if (began)
        {
            blocker = "The shared owner was acquired without an exact durable Applying quality checkpoint.";
        }
        return false;
    }

    public bool TryRecordAuthoritativeResolution(
        Sr5CareerQualityCheckpointCas expected,
        Sr5CareerQualityRecoveryResolution resolution,
        out Sr5CareerQualityCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(resolution);
        stored = null!;
        blocker = string.Empty;
        Sr5CareerMutationOwner owner;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Applying
                || !TryRequireCasLocked(
                    expected,
                    out Sr5CareerQualityCheckpoint current,
                    out blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact Applying quality checkpoint may record an outcome."
                    : blocker;
                return false;
            }
            owner = MutationOwnerFromApplying(current);
        }
        Sr5CareerQualityCheckpoint? durable = null;
        bool completed = _mutationOwners.TryComplete(
            owner,
            () =>
            {
                lock (Gate)
                {
                    string casBlocker = string.Empty;
                    if (expected.Phase != Sr5CareerCheckpointPhase.Applying
                        || !TryRequireCasLocked(
                            expected,
                            out Sr5CareerQualityCheckpoint current,
                            out casBlocker)
                        || _authority is null
                        || !_authority.OwnsResolution(current, resolution.Status)
                        || !ResolutionMatches(current, resolution))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only a fresh signed exact outcome may advance this Applying quality checkpoint."
                            : casBlocker);
                    }

                    Sr5CareerCheckpointPhase nextPhase = resolution.Status switch
                    {
                        Sr5CareerQualityRecoveryStatus.AppliedVerified when resolution.Receipt is not null =>
                            Sr5CareerCheckpointPhase.Applied,
                        Sr5CareerQualityRecoveryStatus.NotAppliedVerified when resolution.Receipt is null =>
                            Sr5CareerCheckpointPhase.Reviewed,
                        _ => throw new InvalidOperationException(
                            "Unknown or inconsistent quality outcomes cannot change the checkpoint.")
                    };
                    Sr5CareerQualityCheckpoint next = current with
                    {
                        Version = checked(current.Version + 1),
                        Phase = nextPhase
                    };
                    bool wrote = TryWriteAndReadBackLocked(
                        next,
                        current,
                        out Sr5CareerQualityCheckpoint written,
                        out string writeBlocker);
                    if (wrote)
                    {
                        durable = written;
                    }
                    return (wrote, writeBlocker);
                }
            },
            out blocker);
        if (completed && durable is not null)
        {
            stored = durable;
            return true;
        }
        return false;
    }

    internal bool TryDeleteReviewed(
        Sr5CareerQualityCheckpointCas expected,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return _mutationOwners.TryRunWhenUnowned(
            () =>
            {
                lock (Gate)
                {
                    string casBlocker = string.Empty;
                    if (expected.Phase != Sr5CareerCheckpointPhase.Reviewed
                        || !TryRequireCasLocked(
                            expected,
                            out Sr5CareerQualityCheckpoint current,
                            out casBlocker)
                        || _authority is null
                        || !_authority.OwnsReviewed(current))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only the exact current owner may abandon this Reviewed quality checkpoint."
                            : casBlocker);
                    }
                    bool deleted = TryDeleteAndReadBackLocked(current, out string deleteBlocker);
                    return (deleted, deleteBlocker);
                }
            },
            out blocker);
    }

    internal bool TryDeleteApplied(
        Sr5CareerQualityCheckpointCas expected,
        CharacterCareerQualityReceipt receipt,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(receipt);
        return _mutationOwners.TryRunWhenUnowned(
            () =>
            {
                lock (Gate)
                {
                    string casBlocker = string.Empty;
                    if (expected.Phase != Sr5CareerCheckpointPhase.Applied
                        || !TryRequireCasLocked(
                            expected,
                            out Sr5CareerQualityCheckpoint current,
                            out casBlocker)
                        || _authority is null
                        || !_authority.OwnsCurrentRunner(current)
                        || !Sr5CareerQualityCoordinator.ReceiptMatchesDraft(current.Draft, receipt))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only the exact current owner and saved receipt may acknowledge this Applied quality checkpoint."
                            : casBlocker);
                    }
                    bool deleted = TryDeleteAndReadBackLocked(current, out string deleteBlocker);
                    return (deleted, deleteBlocker);
                }
            },
            out blocker);
    }

    internal bool TryDeleteCorrected(
        Sr5CareerQualityCheckpointCas expected,
        CharacterCareerQualityReceipt receipt,
        CharacterCareerQualityCorrectionPlan correction,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(correction);
        return _mutationOwners.TryRunWhenUnowned(
            () =>
            {
                lock (Gate)
                {
                    string casBlocker = string.Empty;
                    if (expected.Phase != Sr5CareerCheckpointPhase.Applied
                        || !TryRequireCasLocked(
                            expected,
                            out Sr5CareerQualityCheckpoint current,
                            out casBlocker)
                        || _authority is null
                        || !_authority.OwnsCorrected(current, receipt, correction))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only the exact saved compensating correction may retire this Applied quality checkpoint."
                            : casBlocker);
                    }
                    bool deleted = TryDeleteAndReadBackLocked(current, out string deleteBlocker);
                    return (deleted, deleteBlocker);
                }
            },
            out blocker);
    }

    private bool TryRequireCasLocked(
        Sr5CareerQualityCheckpointCas expected,
        out Sr5CareerQualityCheckpoint current,
        out string blocker)
    {
        if (!TryReadLocked(out current, out blocker))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The expected quality checkpoint no longer exists."
                : blocker;
            return false;
        }
        if (!expected.Matches(current))
        {
            blocker = "Quality checkpoint CAS failed: owner, workspace, action, version, or phase changed.";
            return false;
        }
        return true;
    }

    private static bool DurablyEquivalent(
        Sr5CareerQualityCheckpoint left,
        Sr5CareerQualityCheckpoint right)
    {
        try
        {
            return left.IsStructurallyValid()
                && right.IsStructurallyValid()
                && string.Equals(
                    JsonSerializer.Serialize(left),
                    JsonSerializer.Serialize(right),
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException)
        {
            return false;
        }
    }

    private bool TryReadLocked(
        out Sr5CareerQualityCheckpoint checkpoint,
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
            blocker = $"The quality checkpoint could not be read: {exception.Message}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(payload))
        {
            blocker = string.Empty;
            return false;
        }
        try
        {
            Sr5CareerQualityCheckpoint? parsed =
                JsonSerializer.Deserialize<Sr5CareerQualityCheckpoint>(payload);
            if (parsed is null || !parsed.IsStructurallyValid())
            {
                blocker = "The durable quality checkpoint is unreadable and remains a replay-blocking lock.";
                return false;
            }
            checkpoint = parsed;
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            blocker = "The durable quality checkpoint is unreadable and remains a replay-blocking lock.";
            return false;
        }
    }

    private bool TryWriteAndReadBackLocked(
        Sr5CareerQualityCheckpoint value,
        Sr5CareerQualityCheckpoint? rollback,
        out Sr5CareerQualityCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        string payload = JsonSerializer.Serialize(value);
        string rollbackPayload = rollback is null ? string.Empty : JsonSerializer.Serialize(rollback);
        try
        {
            _backend.Write(payload);
            string readBack = _backend.Read();
            Sr5CareerQualityCheckpoint? parsed =
                JsonSerializer.Deserialize<Sr5CareerQualityCheckpoint>(readBack);
            if (!string.Equals(readBack, payload, StringComparison.Ordinal)
                || parsed is null
                || !parsed.IsStructurallyValid()
                || !Sr5CareerQualityCheckpointCas.From(value).Matches(parsed))
            {
                RestoreLocked(rollbackPayload);
                blocker = "The quality checkpoint write was not durable on exact read-back.";
                return false;
            }
            stored = parsed;
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            RestoreLocked(rollbackPayload);
            blocker = $"The quality checkpoint could not be written: {exception.Message}";
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
        Sr5CareerQualityCheckpoint rollback,
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
                blocker = "The quality checkpoint delete was not durable on read-back.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            bool restored = RestoreExactLocked(rollbackPayload);
            blocker = restored
                ? $"The quality checkpoint could not be deleted and was restored: {exception.Message}"
                : $"The quality checkpoint delete outcome is unknown and remains replay-blocked: {exception.Message}";
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
        Sr5CareerQualityCheckpoint checkpoint,
        Sr5CareerQualityRecoveryResolution resolution)
        => resolution.Status != Sr5CareerQualityRecoveryStatus.OutcomeUnknown
            && string.Equals(
                resolution.WorkspaceId,
                checkpoint.Draft.WorkspaceId.Value,
                StringComparison.Ordinal)
            && resolution.OwnerId == checkpoint.Draft.OwnerId
            && resolution.ActionId == checkpoint.Draft.TransactionId
            && resolution.CheckpointVersion == checkpoint.Version
            && Sr5CareerQualityRecoveryProof.Verifies(checkpoint, resolution)
            && (resolution.Status == Sr5CareerQualityRecoveryStatus.AppliedVerified
                ? resolution.Receipt is not null
                    && Sr5CareerQualityCoordinator.ReceiptMatchesDraft(
                        checkpoint.Draft,
                        resolution.Receipt)
                : resolution.Receipt is null);

    private static Sr5CareerMutationOwner MutationOwnerForNextApplying(
        Sr5CareerQualityCheckpoint checkpoint)
        => new(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.QualityChange,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.TransactionId,
            checked(checkpoint.Version + 1),
            checkpoint.Draft.ExpectedWorkspaceRevision,
            checkpoint.IdempotencyKey);

    private static Sr5CareerMutationOwner MutationOwnerFromApplying(
        Sr5CareerQualityCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying)
        {
            throw new InvalidOperationException(
                "Only an Applying quality checkpoint has a shared mutation owner.");
        }
        return new Sr5CareerMutationOwner(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.QualityChange,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.TransactionId,
            checkpoint.Version,
            checkpoint.Draft.ExpectedWorkspaceRevision,
            checkpoint.IdempotencyKey);
    }

    private void TryReconcileResolvedOwner(Sr5CareerQualityCheckpoint checkpoint)
    {
        if (checkpoint.Version < 3
            || checkpoint.Phase is not (Sr5CareerCheckpointPhase.Reviewed
                or Sr5CareerCheckpointPhase.Applied))
        {
            return;
        }
        Sr5CareerMutationOwner owner = new(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.QualityChange,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.TransactionId,
            checkpoint.Version - 1,
            checkpoint.Draft.ExpectedWorkspaceRevision,
            checkpoint.IdempotencyKey);
        _ = _mutationOwners.TryReconcileResolved(
            owner,
            () =>
            {
                lock (Gate)
                {
                    return TryReadLocked(out Sr5CareerQualityCheckpoint current, out _)
                        && DurablyEquivalent(current, checkpoint);
                }
            },
            out _);
    }
}
