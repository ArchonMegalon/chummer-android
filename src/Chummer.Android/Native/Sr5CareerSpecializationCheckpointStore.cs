using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

internal interface ISr5CareerSpecializationCheckpointAuthority :
    ISr5CareerCheckpointOwnerAuthority
{
    bool OwnsReviewed(Sr5CareerSpecializationCheckpoint checkpoint);
    bool OwnsCurrentRunner(Sr5CareerSpecializationCheckpoint checkpoint);
    bool OwnsNotAppliedResolution(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationResolution resolution);
    bool OwnsImmediateAppliedReceipt(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationReceipt receipt);
}

internal sealed class Sr5CareerSpecializationLiveCheckpointAuthority(
    ISr5CareerCheckpointOwnerAuthority ownerAuthority,
    Chummer.Presentation.Overview.CareerSkillSpecializationEditorState editor,
    Func<Sr5CareerRunnerBinding> currentBinding) :
    ISr5CareerSpecializationCheckpointAuthority
{
    public Guid CurrentOwnerId => ownerAuthority.CurrentOwnerId;

    public bool OwnsReviewed(Sr5CareerSpecializationCheckpoint checkpoint)
    {
        Sr5CareerRunnerBinding binding = currentBinding();
        Chummer.Presentation.Overview.CareerSkillSpecializationCandidate[] matches = editor.Skills
            .Where(candidate => candidate.Identity == checkpoint.Draft.Quote.Identity)
            .Take(2)
            .ToArray();
        return checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed
            && checkpoint.IsStructurallyValid()
            && checkpoint.Draft.OwnerId == CurrentOwnerId
            && CurrentOwnerId != Guid.Empty
            && checkpoint.Draft.WorkspaceId == editor.WorkspaceId
            && checkpoint.Draft.ExpectedContentRevision == editor.ContentRevision
            && matches.Length == 1
            && Sr5CareerSpecializationDraft.CandidateMatchesQuote(matches[0], checkpoint.Draft.Quote)
            && OwnsBinding(checkpoint, binding, applied: false);
    }

    public bool OwnsCurrentRunner(Sr5CareerSpecializationCheckpoint checkpoint)
    {
        Sr5CareerRunnerBinding binding = currentBinding();
        if (!checkpoint.IsStructurallyValid()
            || checkpoint.Draft.OwnerId != CurrentOwnerId
            || CurrentOwnerId == Guid.Empty)
        {
            return false;
        }
        return checkpoint.Phase switch
        {
            Sr5CareerCheckpointPhase.Reviewed => OwnsBinding(checkpoint, binding, applied: false),
            Sr5CareerCheckpointPhase.Applying =>
                OwnsBinding(checkpoint, binding, applied: false)
                || OwnsBinding(checkpoint, binding, applied: true),
            Sr5CareerCheckpointPhase.Applied => OwnsBinding(checkpoint, binding, applied: true),
            _ => false
        };
    }

    public bool OwnsNotAppliedResolution(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationResolution resolution)
        => checkpoint.Phase == Sr5CareerCheckpointPhase.Applying
            && resolution.Status == Sr5CareerSpecializationRecoveryStatus.NotAppliedVerified
            && resolution.Receipt is null
            && Sr5CareerSpecializationCoordinator.VerifiesResolution(checkpoint, resolution)
            && OwnsBinding(checkpoint, currentBinding(), applied: false);

    public bool OwnsImmediateAppliedReceipt(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationReceipt receipt)
        => checkpoint.Phase is Sr5CareerCheckpointPhase.Applying or Sr5CareerCheckpointPhase.Applied
            && Sr5CareerSpecializationCoordinator.VerifiesReceipt(checkpoint.Draft, receipt)
            && OwnsBinding(checkpoint, currentBinding(), applied: true);

    private bool OwnsBinding(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        bool applied)
    {
        long expectedRevision = checkpoint.Draft.ExpectedContentRevision;
        long requiredRevision = applied && expectedRevision < long.MaxValue
            ? expectedRevision + 1
            : expectedRevision;
        return Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition)
            && binding.WorkspaceId == checkpoint.Draft.WorkspaceId
            && binding.ContentRevision == requiredRevision
            && binding.SavedRevision == requiredRevision
            && !binding.IsDirty
            && string.IsNullOrWhiteSpace(binding.Error);
    }
}

internal sealed class PreferencesSr5CareerSpecializationCheckpointBackend :
    ISr5CareerCheckpointBackend
{
    private const string StorageKey = "sr5.career.specialization.draft.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

public sealed record Sr5CareerSpecializationCheckpointCas(
    string WorkspaceId,
    Guid OwnerId,
    Guid ActionId,
    long Version,
    Sr5CareerCheckpointPhase Phase,
    string IdempotencyKey)
{
    public static Sr5CareerSpecializationCheckpointCas From(
        Sr5CareerSpecializationCheckpoint checkpoint)
        => new(
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.ExpenseId,
            checkpoint.Version,
            checkpoint.Phase,
            checkpoint.IdempotencyKey);

    public bool Matches(Sr5CareerSpecializationCheckpoint checkpoint)
        => string.Equals(WorkspaceId, checkpoint.Draft.WorkspaceId.Value, StringComparison.Ordinal)
            && OwnerId == checkpoint.Draft.OwnerId
            && ActionId == checkpoint.Draft.Plan.ExpenseId
            && Version == checkpoint.Version
            && Phase == checkpoint.Phase
            && string.Equals(IdempotencyKey, checkpoint.IdempotencyKey, StringComparison.Ordinal);
}

/// <summary>
/// Durable CAS journal for the specialization lane. Unknown outcomes retain
/// both this domain checkpoint and the shared mutation-owner lock.
/// </summary>
public sealed class Sr5CareerSpecializationCheckpointStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly ISr5CareerCheckpointBackend _backend;
    private readonly ISr5CareerSpecializationCheckpointAuthority? _authority;
    private readonly Sr5CareerMutationOwnerStore _mutationOwners;

    internal Sr5CareerSpecializationCheckpointStore(ISr5CareerCheckpointBackend backend)
        : this(backend, authority: null, Sr5CareerMutationOwnerStore.CreateIsolated())
    {
    }

    internal Sr5CareerSpecializationCheckpointStore(
        ISr5CareerCheckpointBackend backend,
        ISr5CareerSpecializationCheckpointAuthority? authority,
        Sr5CareerMutationOwnerStore mutationOwners)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _authority = authority;
        _mutationOwners = mutationOwners ?? throw new ArgumentNullException(nameof(mutationOwners));
    }

    internal static Sr5CareerSpecializationCheckpointStore CreateDefault(
        ISr5CareerSpecializationCheckpointAuthority authority)
        => new(
            new PreferencesSr5CareerSpecializationCheckpointBackend(),
            authority,
            Sr5CareerMutationOwnerStore.CreateDefault());

    public bool TryRead(out Sr5CareerSpecializationCheckpoint checkpoint, out string blocker)
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
        Sr5CareerSpecializationCheckpoint checkpoint,
        out Sr5CareerSpecializationCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        lock (Gate)
        {
            if (checkpoint.Version != 1
                || checkpoint.Phase != Sr5CareerCheckpointPhase.Reviewed
                || !checkpoint.IsStructurallyValid()
                || _authority is null
                || !_authority.OwnsReviewed(checkpoint))
            {
                blocker = "A specialization checkpoint must begin as an authenticated exact Reviewed action.";
                return false;
            }
            if (TryReadLocked(out _, out blocker) || !string.IsNullOrWhiteSpace(blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Another specialization action owns the durable checkpoint."
                    : blocker;
                return false;
            }
            return TryWriteAndReadBack(checkpoint, rollback: null, out stored, out blocker);
        }
    }

    public bool TryBeginApply(
        Sr5CareerSpecializationCheckpointCas expected,
        out Sr5CareerSpecializationCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        Sr5CareerMutationOwner owner;
        lock (Gate)
        {
            if (!TryRequireCas(expected, out Sr5CareerSpecializationCheckpoint current, out blocker)
                || current.Phase != Sr5CareerCheckpointPhase.Reviewed
                || _authority is null
                || !_authority.OwnsReviewed(current))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact authenticated Reviewed specialization action may begin apply."
                    : blocker;
                return false;
            }
            owner = OwnerForApplying(current with
            {
                Version = checked(current.Version + 1),
                Phase = Sr5CareerCheckpointPhase.Applying
            });
        }

        Sr5CareerSpecializationCheckpoint? durable = null;
        bool began = _mutationOwners.TryBegin(
            owner,
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireCas(expected, out Sr5CareerSpecializationCheckpoint current, out string casBlocker)
                        || _authority is null
                        || !_authority.OwnsReviewed(current))
                    {
                        return new Sr5CareerMutationBeginResult(false, false, casBlocker);
                    }
                    Sr5CareerSpecializationCheckpoint next = current with
                    {
                        Version = checked(current.Version + 1),
                        Phase = Sr5CareerCheckpointPhase.Applying
                    };
                    bool wrote = TryWriteAndReadBack(next, current, out Sr5CareerSpecializationCheckpoint written, out string writeBlocker);
                    if (wrote)
                    {
                        durable = written;
                        return new Sr5CareerMutationBeginResult(true, false, string.Empty);
                    }
                    bool restored = TryReadLocked(out Sr5CareerSpecializationCheckpoint readBack, out _)
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
        return false;
    }

    internal async Task<IDisposable> AcquireDurableApplyingLeaseAsync(
        Sr5CareerSpecializationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        IDisposable lease = await _mutationOwners.AcquireExecutionLeaseAsync(
            OwnerForApplying(checkpoint),
            cancellationToken).ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (checkpoint.Phase != Sr5CareerCheckpointPhase.Applying
                    || !TryReadLocked(out Sr5CareerSpecializationCheckpoint current, out _)
                    || current != checkpoint
                    || _authority is null
                    || !_authority.OwnsCurrentRunner(current))
                {
                    throw new InvalidOperationException(
                        "The exact durable Applying specialization checkpoint no longer owns this runner.");
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

    public bool TryRecordImmediateApplied(
        Sr5CareerSpecializationCheckpointCas expected,
        Sr5CareerSpecializationReceipt receipt,
        out Sr5CareerSpecializationCheckpoint stored,
        out string blocker)
        => TryComplete(
            expected,
            current => _authority?.OwnsImmediateAppliedReceipt(current, receipt) == true,
            Sr5CareerCheckpointPhase.Applied,
            out stored,
            out blocker);

    public bool TryRecordNotApplied(
        Sr5CareerSpecializationCheckpointCas expected,
        Sr5CareerSpecializationResolution resolution,
        out Sr5CareerSpecializationCheckpoint stored,
        out string blocker)
        => TryComplete(
            expected,
            current => _authority?.OwnsNotAppliedResolution(current, resolution) == true,
            Sr5CareerCheckpointPhase.Reviewed,
            out stored,
            out blocker);

    public bool TryDeleteReviewed(
        Sr5CareerSpecializationCheckpointCas expected,
        out string blocker)
        => TryDelete(
            expected,
            current => current.Phase == Sr5CareerCheckpointPhase.Reviewed
                && _authority?.OwnsReviewed(current) == true,
            out blocker);

    public bool TryDeleteApplied(
        Sr5CareerSpecializationCheckpointCas expected,
        Sr5CareerSpecializationReceipt receipt,
        out string blocker)
        => TryDelete(
            expected,
            current => current.Phase == Sr5CareerCheckpointPhase.Applied
                && _authority?.OwnsImmediateAppliedReceipt(current, receipt) == true,
            out blocker);

    private bool TryComplete(
        Sr5CareerSpecializationCheckpointCas expected,
        Func<Sr5CareerSpecializationCheckpoint, bool> ownsResolution,
        Sr5CareerCheckpointPhase nextPhase,
        out Sr5CareerSpecializationCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        Sr5CareerMutationOwner owner;
        lock (Gate)
        {
            if (!TryRequireCas(expected, out Sr5CareerSpecializationCheckpoint current, out blocker)
                || current.Phase != Sr5CareerCheckpointPhase.Applying)
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact Applying specialization checkpoint may record an outcome."
                    : blocker;
                return false;
            }
            owner = OwnerForApplying(current);
        }
        Sr5CareerSpecializationCheckpoint? durable = null;
        bool completed = _mutationOwners.TryComplete(
            owner,
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireCas(expected, out Sr5CareerSpecializationCheckpoint current, out string casBlocker)
                        || !ownsResolution(current))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "The outcome is not an exact authoritative specialization resolution."
                            : casBlocker);
                    }
                    Sr5CareerSpecializationCheckpoint next = current with
                    {
                        Version = checked(current.Version + 1),
                        Phase = nextPhase
                    };
                    bool wrote = TryWriteAndReadBack(next, current, out Sr5CareerSpecializationCheckpoint written, out string writeBlocker);
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

    private bool TryDelete(
        Sr5CareerSpecializationCheckpointCas expected,
        Func<Sr5CareerSpecializationCheckpoint, bool> authorized,
        out string blocker)
        => _mutationOwners.TryRunWhenUnowned(
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireCas(expected, out Sr5CareerSpecializationCheckpoint current, out string casBlocker)
                        || !authorized(current))
                    {
                        return (false, string.IsNullOrWhiteSpace(casBlocker)
                            ? "Only the exact current specialization owner may delete this checkpoint."
                            : casBlocker);
                    }
                    _backend.Remove();
                    bool deleted = string.IsNullOrWhiteSpace(_backend.Read());
                    return (deleted, deleted ? string.Empty : "The specialization checkpoint remained after deletion.");
                }
            },
            out blocker);

    private bool TryRequireCas(
        Sr5CareerSpecializationCheckpointCas expected,
        out Sr5CareerSpecializationCheckpoint current,
        out string blocker)
    {
        if (!TryReadLocked(out current, out blocker) || !expected.Matches(current))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The specialization checkpoint CAS no longer matches."
                : blocker;
            return false;
        }
        return true;
    }

    private bool TryReadLocked(
        out Sr5CareerSpecializationCheckpoint checkpoint,
        out string blocker)
    {
        checkpoint = null!;
        string payload = _backend.Read();
        if (string.IsNullOrWhiteSpace(payload))
        {
            blocker = string.Empty;
            return false;
        }
        try
        {
            checkpoint = JsonSerializer.Deserialize<Sr5CareerSpecializationCheckpoint>(payload, JsonOptions)!;
            if (checkpoint is null || !checkpoint.IsStructurallyValid())
            {
                blocker = "The durable specialization checkpoint is malformed or uses an unsupported schema; it remains locked.";
                checkpoint = null!;
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            blocker = "The durable specialization checkpoint cannot be parsed; it remains locked.";
            return false;
        }
    }

    private bool TryWriteAndReadBack(
        Sr5CareerSpecializationCheckpoint checkpoint,
        Sr5CareerSpecializationCheckpoint? rollback,
        out Sr5CareerSpecializationCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        try
        {
            _backend.Write(JsonSerializer.Serialize(checkpoint, JsonOptions));
            if (TryReadLocked(out Sr5CareerSpecializationCheckpoint readBack, out blocker)
                && readBack == checkpoint)
            {
                stored = readBack;
                return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            blocker = $"The specialization checkpoint could not be serialized: {exception.Message}";
        }
        if (rollback is null)
        {
            _backend.Remove();
        }
        else
        {
            _backend.Write(JsonSerializer.Serialize(rollback, JsonOptions));
        }
        blocker = string.IsNullOrWhiteSpace(blocker)
            ? "The specialization checkpoint failed exact write/read-back verification."
            : blocker;
        return false;
    }

    private static Sr5CareerMutationOwner OwnerForApplying(
        Sr5CareerSpecializationCheckpoint checkpoint)
        => new(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.SkillSpecializationAdd,
            checkpoint.Draft.WorkspaceId.Value,
            checkpoint.Draft.OwnerId,
            checkpoint.Draft.Plan.ExpenseId,
            checkpoint.Version,
            checkpoint.Draft.ExpectedContentRevision,
            checkpoint.IdempotencyKey);

    private void TryReconcileResolvedOwner(
        Sr5CareerSpecializationCheckpoint checkpoint)
    {
        if (checkpoint.Version < 3
            || checkpoint.Phase is not (Sr5CareerCheckpointPhase.Reviewed
                or Sr5CareerCheckpointPhase.Applied))
        {
            return;
        }
        Sr5CareerSpecializationCheckpoint applying = checkpoint with
        {
            Version = checkpoint.Version - 1,
            Phase = Sr5CareerCheckpointPhase.Applying
        };
        Sr5CareerMutationOwner owner = OwnerForApplying(applying);
        _ = _mutationOwners.TryReconcileResolved(
            owner,
            () =>
            {
                lock (Gate)
                {
                    return TryReadLocked(out Sr5CareerSpecializationCheckpoint current, out _)
                        && current == checkpoint;
                }
            },
            out _);
    }
}
