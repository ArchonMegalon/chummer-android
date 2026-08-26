using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal static class Sr5CareerRecoveryProof
{
    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

    public static Sr5CareerRecoveryResolution Create(
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerRecoveryStatus status,
        Sr5CareerActiveSkillReceipt? receipt,
        string message)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        string proof = Sign(checkpoint, status, receipt, message);
        return new(
            status,
            checkpoint.WorkspaceId,
            checkpoint.OwnerId,
            checkpoint.ActionId,
            checkpoint.Version,
            receipt,
            message,
            proof);
    }

    public static bool Verifies(
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerRecoveryResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.AuthorityProof is not { Length: 64 })
        {
            return false;
        }
        try
        {
            byte[] actual = Convert.FromHexString(resolution.AuthorityProof);
            byte[] expected = Convert.FromHexString(Sign(
                checkpoint,
                resolution.Status,
                resolution.Receipt,
                resolution.Message));
            return actual.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Sign(
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerRecoveryStatus status,
        Sr5CareerActiveSkillReceipt? receipt,
        string message)
    {
        string payload = string.Join(
            "\n",
            JsonSerializer.Serialize(checkpoint),
            status.ToString(),
            JsonSerializer.Serialize(receipt),
            message);
        return Convert.ToHexString(
            HMACSHA256.HashData(ProcessKey, Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}

public interface ISr5CareerCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

public interface ISr5CareerCheckpointOwnerAuthority
{
    Guid CurrentOwnerId { get; }
}

internal interface ISr5CareerReviewedCheckpointAuthority :
    ISr5CareerCheckpointOwnerAuthority
{
    bool Owns(Sr5CareerDraftCheckpoint checkpoint);
    bool OwnsCurrentRunner(Sr5CareerDraftCheckpoint checkpoint);
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
    private readonly Sr5CareerMutationOwnerStore _mutationOwners;

    public Sr5CareerDraftCheckpointStore(ISr5CareerCheckpointBackend backend)
        : this(
            backend,
            reviewedAuthority: null,
            Sr5CareerMutationOwnerStore.CreateIsolated())
    {
    }

    internal Sr5CareerDraftCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5CareerReviewedCheckpointAuthority? reviewedAuthority)
        : this(
            backend,
            reviewedAuthority,
            Sr5CareerMutationOwnerStore.CreateIsolated())
    {
    }

    internal Sr5CareerDraftCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5CareerReviewedCheckpointAuthority? reviewedAuthority,
        Sr5CareerMutationOwnerStore mutationOwners)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _reviewedAuthority = reviewedAuthority;
        _mutationOwners = mutationOwners ?? throw new ArgumentNullException(nameof(mutationOwners));
    }

    internal static Sr5CareerDraftCheckpointStore CreateDefault(
        ISr5CareerReviewedCheckpointAuthority reviewedAuthority)
        => new(
            new PreferencesSr5CareerCheckpointBackend(),
            reviewedAuthority ?? throw new ArgumentNullException(nameof(reviewedAuthority)),
            Sr5CareerMutationOwnerStore.CreateDefault());

    public bool TryRead(out Sr5CareerDraftCheckpoint checkpoint, out string blocker)
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
        Sr5CareerDraftCheckpoint checkpoint,
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
                    || !TryReadLocked(out Sr5CareerDraftCheckpoint current, out _)
                    || current != checkpoint
                    || _reviewedAuthority is null
                    || !_reviewedAuthority.OwnsCurrentRunner(current))
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
        Sr5CareerDraftCheckpoint checkpoint,
        out Sr5CareerDraftCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        stored = null!;
        lock (Gate)
        {
            if (!checkpoint.IsStructurallyValid()
                || checkpoint.Version != 1
                || checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed
                || _reviewedAuthority is null
                || !_reviewedAuthority.Owns(checkpoint))
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
        Sr5CareerMutationOwner owner;
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
            owner = new Sr5CareerMutationOwner(
                Sr5CareerMutationOwner.CurrentSchemaVersion,
                Sr5CareerMutationDomains.ActiveSkillAdvance,
                current.WorkspaceId,
                current.OwnerId,
                current.ActionId,
                checked(current.Version + 1),
                current.ExpectedContentRevision,
                current.IdempotencyKey);
        }

        Sr5CareerDraftCheckpoint? durable = null;
        bool began = _mutationOwners.TryBegin(
            owner,
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireCasLocked(
                            expected,
                            out Sr5CareerDraftCheckpoint current,
                            out string casBlocker))
                    {
                        return new Sr5CareerMutationBeginResult(
                            Success: false,
                            ExactReviewedStateWasRestored: false,
                            casBlocker);
                    }
                    Sr5CareerDraftCheckpoint next = current with
                    {
                        Version = checked(current.Version + 1),
                        Phase = Sr5CareerCheckpointPhase.Applying
                    };
                    bool wrote = TryWriteAndReadBackLocked(
                        next,
                        current,
                        out Sr5CareerDraftCheckpoint written,
                        out string writeBlocker);
                    if (wrote)
                    {
                        durable = written;
                        return new Sr5CareerMutationBeginResult(true, false, string.Empty);
                    }
                    bool restored = TryReadLocked(out Sr5CareerDraftCheckpoint readBack, out _)
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
            blocker = "The shared owner was acquired without an exact durable Applying checkpoint.";
        }
        return false;
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
        Sr5CareerMutationOwner owner;
        lock (Gate)
        {
            if (expected.Phase != Sr5CareerCheckpointPhase.Applying
                || !TryRequireCasLocked(
                    expected,
                    out Sr5CareerDraftCheckpoint current,
                    out blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact Applying checkpoint may record an outcome."
                    : blocker;
                return false;
            }
            owner = MutationOwnerFromApplying(current);
        }
        Sr5CareerDraftCheckpoint? durable = null;
        bool completed = _mutationOwners.TryComplete(
            owner,
            () =>
            {
                lock (Gate)
                {
                    string casBlocker = string.Empty;
                    if (expected.Phase != Sr5CareerCheckpointPhase.Applying
                        || !TryRequireCasLocked(expected, out Sr5CareerDraftCheckpoint current, out casBlocker)
                        || _reviewedAuthority is null
                        || !_reviewedAuthority.OwnsCurrentRunner(current))
                    {
                        string ownedBlocker = string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only the live authenticated SR5 owner and runner may record this exact Applying outcome."
                            : casBlocker;
                        return (false, ownedBlocker);
                    }
                    if (!ResolutionMatches(current, expected, resolution)
                        || resolution.Status == Sr5CareerRecoveryStatus.OutcomeUnknown)
                    {
                        return (false, "An unknown or foreign outcome cannot change the Applying checkpoint.");
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
                    bool wrote = TryWriteAndReadBackLocked(
                        next,
                        current,
                        out Sr5CareerDraftCheckpoint written,
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
        Sr5CareerCheckpointCas expected,
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
                            out Sr5CareerDraftCheckpoint current,
                            out casBlocker)
                        || _reviewedAuthority is null
                        || !_reviewedAuthority.Owns(current))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only the current SR5 owner/workspace/revision/action may abandon this Reviewed checkpoint."
                            : casBlocker);
                    }
                    bool deleted = TryDeleteAndReadBackLocked(out string deleteBlocker);
                    return (deleted, deleteBlocker);
                }
            },
            out blocker);
    }

    internal bool TryDeleteApplied(
        Sr5CareerCheckpointCas expected,
        Sr5CareerActiveSkillReceipt receipt,
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
                            out Sr5CareerDraftCheckpoint current,
                            out casBlocker)
                        || _reviewedAuthority is null
                        || !_reviewedAuthority.OwnsCurrentRunner(current)
                        || !ReceiptMatchesCheckpoint(current, receipt))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only the current authenticated SR5 owner may acknowledge this exact Applied receipt checkpoint."
                            : casBlocker);
                    }
                    bool deleted = TryDeleteAndReadBackLocked(out string deleteBlocker);
                    return (deleted, deleteBlocker);
                }
            },
            out blocker);
    }

    internal static bool ReceiptMatchesCheckpoint(
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerActiveSkillReceipt receipt)
        => checkpoint.Phase is (Sr5CareerCheckpointPhase.Applying
               or Sr5CareerCheckpointPhase.Applied)
           && checkpoint.IsStructurallyValid()
           && receipt.OwnerId == checkpoint.OwnerId
           && receipt.ActionId == checkpoint.ActionId
           && string.Equals(receipt.IdempotencyKey, checkpoint.IdempotencyKey, StringComparison.Ordinal)
           && string.Equals(receipt.RouteId, Sr5CareerWizardRoutes.ActiveSkillReceipt, StringComparison.Ordinal)
           && string.Equals(receipt.WorkspaceId.Value, checkpoint.WorkspaceId, StringComparison.Ordinal)
           && receipt.PreviousContentRevision == checkpoint.ExpectedContentRevision
           && checkpoint.ExpectedContentRevision < long.MaxValue
           && receipt.SavedContentRevision == checkpoint.ExpectedContentRevision + 1
           && receipt.SkillId == checkpoint.SkillId
           && receipt.SourceSkillId == checkpoint.SourceSkillId
           && string.Equals(receipt.SkillName, checkpoint.SkillName, StringComparison.Ordinal)
           && string.Equals(receipt.SkillCategory, checkpoint.SkillCategory, StringComparison.Ordinal)
           && receipt.BasePoints == checkpoint.BasePoints
           && checkpoint.PreviousKarmaPoints < int.MaxValue
           && receipt.SavedSkillKarmaPoints == checkpoint.PreviousKarmaPoints + 1
           && receipt.RatingMaximum == checkpoint.RatingMaximum
           && receipt.PreviousRating == checkpoint.PreviousRating
           && receipt.SavedRating == checkpoint.TargetRating
           && receipt.KarmaCost == -checkpoint.ExpenseAmount
           && receipt.SavedKarma == checkpoint.SavedKarma
           && receipt.ExpenseId == checkpoint.ActionId
           && receipt.ExpenseDateLocal == checkpoint.ExpenseDateLocal
           && string.Equals(receipt.ExpenseReason, checkpoint.ExpenseReason, StringComparison.Ordinal)
           && string.Equals(receipt.ExpenseType, checkpoint.ExpenseType, StringComparison.Ordinal)
           && receipt.ExpenseRefund == checkpoint.ExpenseRefund
           && receipt.ExpenseForceCareerVisible == checkpoint.ExpenseForceCareerVisible
           && string.Equals(receipt.KarmaUndoType, checkpoint.KarmaUndoType, StringComparison.Ordinal)
           && string.Equals(receipt.NuyenUndoType, checkpoint.NuyenUndoType, StringComparison.Ordinal)
           && string.Equals(receipt.UndoObjectId, checkpoint.UndoObjectId, StringComparison.Ordinal)
           && receipt.UndoQuantity == checkpoint.UndoQuantity
           && string.Equals(receipt.UndoExtra, checkpoint.UndoExtra, StringComparison.Ordinal)
           && string.Equals(receipt.ReviewedRuleDigest, checkpoint.RuleDigest, StringComparison.Ordinal)
           && string.Equals(receipt.RuleDigest, checkpoint.RuleDigest, StringComparison.Ordinal)
           && string.Equals(receipt.SourceRevision, checkpoint.SourceRevision, StringComparison.Ordinal)
           && ExactLoadedQuoteIsCoherent(receipt);

    private static bool ExactLoadedQuoteIsCoherent(Sr5CareerActiveSkillReceipt receipt)
        => CharacterCareerActiveSkillAdvanceRules.IsCoherent(
            new CharacterCareerActiveSkillAdvanceQuote(
                new CharacterCareerActiveSkillIdentity(
                    receipt.SkillId,
                    receipt.SourceSkillId),
                receipt.SkillName,
                receipt.SkillCategory,
                receipt.BasePoints,
                receipt.SavedSkillKarmaPoints,
                receipt.SavedRating,
                receipt.RatingMaximum,
                receipt.SavedKarma,
                receipt.NextKarmaCost,
                receipt.CanAdvanceAgain,
                receipt.NextAdvanceBlocker,
                receipt.LogicalRevision,
                receipt.SourceRevision,
                receipt.RuleDigest));

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
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerCheckpointCas expected,
        Sr5CareerRecoveryResolution resolution)
        => string.Equals(expected.WorkspaceId, resolution.WorkspaceId, StringComparison.Ordinal)
           && expected.OwnerId == resolution.OwnerId
           && expected.ActionId == resolution.ActionId
           && expected.Version == resolution.CheckpointVersion
           && Sr5CareerRecoveryProof.Verifies(checkpoint, resolution)
           && (resolution.Status switch
           {
               Sr5CareerRecoveryStatus.AppliedVerified when resolution.Receipt is { } receipt =>
                   ReceiptMatchesCheckpoint(checkpoint, receipt),
               Sr5CareerRecoveryStatus.NotAppliedVerified => resolution.Receipt is null,
               _ => false
           });

    private static Sr5CareerMutationOwner MutationOwnerFromApplying(
        Sr5CareerDraftCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying)
        {
            throw new InvalidOperationException(
                "Only an Applying active-skill checkpoint has a shared mutation owner.");
        }
        return new Sr5CareerMutationOwner(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.ActiveSkillAdvance,
            checkpoint.WorkspaceId,
            checkpoint.OwnerId,
            checkpoint.ActionId,
            checkpoint.Version,
            checkpoint.ExpectedContentRevision,
            checkpoint.IdempotencyKey);
    }

    private void TryReconcileResolvedOwner(Sr5CareerDraftCheckpoint checkpoint)
    {
        if (checkpoint.Version < 3
            || checkpoint.Phase is not (Sr5CareerCheckpointPhase.Reviewed
                or Sr5CareerCheckpointPhase.Applied))
        {
            return;
        }
        Sr5CareerMutationOwner owner = new(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.ActiveSkillAdvance,
            checkpoint.WorkspaceId,
            checkpoint.OwnerId,
            checkpoint.ActionId,
            checkpoint.Version - 1,
            checkpoint.ExpectedContentRevision,
            checkpoint.IdempotencyKey);
        _ = _mutationOwners.TryReconcileResolved(
            owner,
            () =>
            {
                lock (Gate)
                {
                    return TryReadLocked(out Sr5CareerDraftCheckpoint current, out _)
                        && current == checkpoint;
                }
            },
            out _);
    }
}
