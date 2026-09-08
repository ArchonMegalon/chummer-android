using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public enum Sr5CareerReputationPhase { Confirmed, Applying, Applied, Superseded }

public sealed record Sr5CareerReputationCheckpoint(int SchemaVersion, long Version, Guid OwnerId,
    Sr5CareerReputationPhase Phase, CharacterCareerReputationCommand Command, string CommandDigest,
    CharacterCareerReputationReceipt? Receipt = null, long? SupersededAtRevision = null)
{
    public bool IsExact()
        => SchemaVersion == 1 && OwnerId != Guid.Empty
            && CharacterCareerReputationTransaction.IsConfirmedCommand(Command)
            && CommandDigest == CharacterCareerReputationTransaction.CommandDigest(Command)
            && Phase switch
            {
                Sr5CareerReputationPhase.Confirmed => Version == 1 && Receipt is null && SupersededAtRevision is null,
                Sr5CareerReputationPhase.Applying => Version == 2 && Receipt is null && SupersededAtRevision is null,
                Sr5CareerReputationPhase.Applied => Version == 3 && Matches(Receipt) && SupersededAtRevision is null,
                Sr5CareerReputationPhase.Superseded => Version == 3 && Receipt is null
                    && SupersededAtRevision > Command.Binding.WorkspaceRevision,
                _ => false
            };

    public bool Matches(CharacterCareerReputationReceipt? receipt)
        => CharacterCareerReputationTransaction.IsCoherent(receipt)
            && receipt!.Command == Command && receipt.CommandDigest == CommandDigest;

    public bool IsTerminal => Phase is Sr5CareerReputationPhase.Applied or Sr5CareerReputationPhase.Superseded;

    internal Sr5CareerMutationOwner MutationOwner()
        => new(1, "career-reputation", Command.Request.WorkspaceId.Value, OwnerId,
            Command.Request.OperationId, 2, Command.Binding.WorkspaceRevision, CommandDigest);
}

public sealed record Sr5CareerReputationEntryState(IReadOnlyList<Sr5CareerReputationCheckpoint> History,
    Sr5CareerReputationCheckpoint? RecoveryRequired,
    IReadOnlyList<Sr5CareerReputationCheckpoint> SupersededHistory);

/// <summary>Typed reputation history over the existing cross-Career owner and execution gate.</summary>
public sealed class Sr5CareerReputationJournal
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
    private readonly ISr5CareerCommandJournalBackend _backend;
    private readonly Sr5CareerMutationOwnerStore _owners;
    private readonly ICharacterCareerReputationService _core;

    internal Sr5CareerReputationJournal(ISr5CareerCommandJournalBackend backend,
        Sr5CareerMutationOwnerStore owners, ICharacterCareerReputationService core)
        => (_backend, _owners, _core) = (backend ?? throw new ArgumentNullException(nameof(backend)),
            owners ?? throw new ArgumentNullException(nameof(owners)), core ?? throw new ArgumentNullException(nameof(core)));

    public static Sr5CareerReputationJournal CreateDefault(string directory, ICharacterCareerReputationService core)
        => new(new FileSr5CareerCommandJournalBackend(directory, Sr5CareerCommandJournalDomain.Reputation),
            Sr5CareerMutationOwnerStore.CreateDefault(), core);

    internal bool TryInspect(Guid owner, CharacterWorkspaceId workspace, out Sr5CareerReputationEntryState? state, out string error)
    {
        Sr5CareerReputationEntryState? observed = null;
        bool success = _owners.TryInspectCurrent(active =>
        {
            lock (Gate)
            {
                if (!TryRead(out var ledger, out string readError)) return (false, readError);
                bool Selected(Sr5CareerReputationCheckpoint entry) => entry.OwnerId == owner && entry.Command.Request.WorkspaceId == workspace;
                var pending = ledger.Entries.SingleOrDefault(entry => !entry.IsTerminal);
                if (pending is not null && !Selected(pending)) return (false, "Another runner has an unresolved reputation change.");
                if (active is not null)
                {
                    var owning = ledger.Entries.SingleOrDefault(entry => entry.MutationOwner() == active);
                    if (owning is null || !Selected(owning) || pending is not null && pending != owning)
                        return (false, "Another Career operation owns recovery. Resolve its original journal first.");
                    pending = owning;
                }
                observed = new(Array.AsReadOnly(ledger.Entries.Where(entry => Selected(entry)
                    && entry.Phase == Sr5CareerReputationPhase.Applied).ToArray()), pending,
                    Array.AsReadOnly(ledger.Entries.Where(entry => Selected(entry)
                        && entry.Phase == Sr5CareerReputationPhase.Superseded).ToArray()));
                return (true, string.Empty);
            }
        }, out error);
        state = observed;
        return success;
    }

    internal bool TryGet(Guid owner, CharacterWorkspaceId workspace, Guid operation,
        out Sr5CareerReputationCheckpoint? entry, out string error)
    {
        entry = null;
        lock (Gate)
        {
            if (!TryRead(out var ledger, out error)) return false;
            var found = ledger.Entries.SingleOrDefault(row => row.Command.Request.OperationId == operation);
            if (found is not null && (found.OwnerId != owner || found.Command.Request.WorkspaceId != workspace))
            { error = "The retained reputation operation belongs to another runner or owner."; return false; }
            entry = found;
            return true;
        }
    }

    internal bool TryPrepare(Sr5CareerReputationCheckpoint entry, Func<bool> owns, out string error, out bool unknown)
    {
        bool attempted = false;
        bool result = _owners.TryRunWhenUnowned(() =>
        {
            lock (Gate)
            {
                if (!owns() || !entry.IsExact() || entry.Phase != Sr5CareerReputationPhase.Confirmed)
                    return (false, "The exact reviewed runner changed before confirmation.");
                if (!TryRead(out var ledger, out string blocker)) return (false, blocker);
                if (ledger.Entries.Count >= CharacterCareerReputationTransaction.MaximumReceipts
                    || ledger.Entries.Any(row => !row.IsTerminal
                        || row.Command.Request.OperationId == entry.Command.Request.OperationId))
                    return (false, "Resolve the original reputation intent or retain the full history before creating another.");
                attempted = true;
                bool written = TryWrite(Seal(checked(ledger.Version + 1), [.. ledger.Entries, entry]), out blocker);
                return (written, blocker);
            }
        }, out error);
        unknown = !result && attempted;
        return result;
    }

    internal bool TryBegin(Sr5CareerReputationCheckpoint entry, Func<bool> owns,
        out Sr5CareerReputationCheckpoint? applying, out string error)
    {
        applying = null;
        lock (Gate)
            if (!owns() || entry.Phase is not (Sr5CareerReputationPhase.Confirmed or Sr5CareerReputationPhase.Applying)
                || !TryRequire(entry, out _))
            { error = "The original reputation intent is no longer pending."; return false; }
        Sr5CareerReputationCheckpoint? observed = null;
        bool result = _owners.TryBegin(entry.MutationOwner(), () =>
        {
            lock (Gate)
            {
                if (!owns() || entry.Phase is not (Sr5CareerReputationPhase.Confirmed or Sr5CareerReputationPhase.Applying)
                    || !TryRequire(entry, out var ledger))
                    return new(false, false, "The original reputation journal changed before Applying.");
                if (entry.Phase == Sr5CareerReputationPhase.Applying) { observed = entry; return new(true, false, ""); }
                var next = entry with { Version = 2, Phase = Sr5CareerReputationPhase.Applying };
                bool written = TryWrite(Replace(ledger, next), out string blocker);
                if (written) observed = next;
                return new(written, !written && TryRequire(entry, out _), blocker);
            }
        }, out error);
        applying = observed;
        return result && observed is not null;
    }

    internal async Task<IDisposable> AcquireAsync(Sr5CareerReputationCheckpoint entry, Func<bool> owns,
        bool mayCommit, CancellationToken token)
    {
        var lease = await _owners.AcquireExecutionLeaseAsync(entry.MutationOwner(), token).ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (!owns() || entry.Phase != Sr5CareerReputationPhase.Applying || !TryRequire(entry, out var ledger))
                    throw new InvalidOperationException("The exact Applying reputation no longer owns execution.");
                if (mayCommit && !TryWrite(ledger, out _))
                    throw new IOException("The original reputation command durability could not be re-established.");
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal bool TryResolve(Sr5CareerReputationCheckpoint entry, Func<bool> owns,
        out Sr5CareerReputationCheckpoint? applied, out long? revision, out string error)
    {
        applied = null;
        revision = null;
        // This service is a constructor-owned Core dependency, not a caller's
        // receipt or a rehashed JSON assertion of success.
        var observed = _core.Lookup(entry.Command.Request.WorkspaceId, entry.Command.Request.OperationId, entry.CommandDigest);
        if (!owns() || observed.Outcome != CharacterCareerReputationOutcome.Replayed
            || !entry.Matches(observed.Receipt) || observed.CurrentWorkspaceRevision is not { } current
            || current < observed.Receipt!.CommittedWorkspaceRevision)
        { error = observed.Error ?? "Core cannot verify the original durable reputation receipt."; return false; }
        revision = current;
        if (entry.Phase == Sr5CareerReputationPhase.Applied)
        {
            lock (Gate)
                if (!TryRequire(entry, out _) || entry.Receipt != observed.Receipt)
                { error = "Historical reputation journal changed."; return false; }
            bool reconciled = _owners.TryReconcileResolved(entry.MutationOwner(), () =>
            { lock (Gate) return owns() && TryRequire(entry, out _); }, out error);
            if (reconciled && owns()) applied = entry;
            return applied is not null;
        }
        Sr5CareerReputationCheckpoint? durable = null;
        bool success = _owners.TryComplete(entry.MutationOwner(), () =>
        {
            lock (Gate)
            {
                if (!owns() || entry.Phase != Sr5CareerReputationPhase.Applying || !TryRequire(entry, out var ledger))
                    return (false, "The exact reputation journal changed before receipt persistence.");
                var next = entry with { Version = 3, Phase = Sr5CareerReputationPhase.Applied, Receipt = observed.Receipt };
                bool written = TryWrite(Replace(ledger, next), out string blocker);
                if (written) durable = next;
                return (written, blocker);
            }
        }, out error);
        if (success && owns()) applied = durable;
        return applied is not null;
    }

    /// <summary>
    /// An explicit close may retire only a command that Core proves did not
    /// commit and can no longer pass its revision CAS. Missing/unavailable data
    /// or NotFound at the original revision cannot release an unknown outcome.
    /// The shared transition gate excludes in-flight native execution while
    /// Core is queried and the original command is durably retained as superseded.
    /// </summary>
    internal bool TrySupersede(Sr5CareerReputationCheckpoint entry, Func<bool> owns,
        out Sr5CareerReputationCheckpoint? closed, out string error)
    {
        Sr5CareerReputationCheckpoint? retained = null;
        bool ProvesNotApplied(out long revision)
        {
            revision = 0;
            var observed = _core.Lookup(entry.Command.Request.WorkspaceId, entry.Command.Request.OperationId, entry.CommandDigest);
            if (observed.Outcome != CharacterCareerReputationOutcome.NotFound || observed.Receipt is not null
                || observed.CurrentWorkspaceRevision is not { } current || current <= entry.Command.Binding.WorkspaceRevision
                || entry.SupersededAtRevision is { } prior && current < prior) return false;
            revision = current;
            return true;
        }

        bool success;
        if (entry.Phase == Sr5CareerReputationPhase.Superseded)
        {
            // TryReconcileResolved deliberately skips its predicate when no owner
            // exists, so history must be independently validated first.
            lock (Gate)
                if (!owns() || !TryRequire(entry, out _) || !ProvesNotApplied(out _))
                { closed = null; error = "Core cannot revalidate the retained not-applied outcome."; return false; }
            success = _owners.TryReconcileResolved(entry.MutationOwner(), () =>
            { lock (Gate) return owns() && TryRequire(entry, out _) && ProvesNotApplied(out _); }, out error);
            if (success && owns()) retained = entry;
        }
        else
        {
            success = _owners.TryComplete(entry.MutationOwner(), () =>
            {
                lock (Gate)
                {
                    if (!owns() || entry.Phase != Sr5CareerReputationPhase.Applying || !TryRequire(entry, out var ledger)
                        || !ProvesNotApplied(out long revision))
                        return (false, "Core has not proven this original command permanently inapplicable. Keep recovery open.");
                    var next = entry with { Version = 3, Phase = Sr5CareerReputationPhase.Superseded, SupersededAtRevision = revision };
                    bool written = TryWrite(Replace(ledger, next), out string blocker);
                    if (written) retained = next;
                    return (written, blocker);
                }
            }, out error);
        }
        closed = success && owns() ? retained : null;
        return closed is not null;
    }

    private bool TryRequire(Sr5CareerReputationCheckpoint entry, out Journal ledger)
    {
        ledger = null!;
        return entry.IsExact() && TryRead(out ledger, out _) && ledger.Entries.Contains(entry);
    }

    private bool TryRead(out Journal ledger, out string error)
    {
        ledger = null!;
        error = "Reputation journal is unavailable or corrupt. Preserve the original command.";
        try
        {
            string payload = _backend.Read();
            if (payload.Length == 0) { ledger = Seal(0, []); error = ""; return true; }
            if (Encoding.UTF8.GetByteCount(payload) > FileSr5CareerCommandJournalBackend.MaximumBytes) return false;
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            RequireUniqueProperties(document.RootElement);
            ledger = JsonSerializer.Deserialize<Journal>(payload, JsonOptions)!;
            if (ledger is null || ledger.SchemaVersion != 1 || ledger.Version <= 0
                || ledger.Entries is null || ledger.Entries.Count is 0 or > CharacterCareerReputationTransaction.MaximumReceipts
                || ledger.Entries.Any(entry => entry is null || !entry.IsExact())
                || ledger.Entries.Select(entry => entry.Command.Request.OperationId).Distinct().Count() != ledger.Entries.Count
                || ledger.Version != ledger.Entries.Sum(entry => entry.Version)
                || ledger.Entries.Count(entry => !entry.IsTerminal) > 1
                || ledger.Digest != Seal(ledger.Version, ledger.Entries).Digest) return false;
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    private bool TryWrite(Journal next, out string error)
    {
        error = "Reputation journal write acknowledgement is unavailable. Retain the same operation.";
        try
        {
            string payload = JsonSerializer.Serialize(next, JsonOptions);
            if (Encoding.UTF8.GetByteCount(payload) > FileSr5CareerCommandJournalBackend.MaximumBytes) return false;
            _backend.Write(payload);
            if (!TryRead(out var readBack, out _) || JsonSerializer.Serialize(readBack, JsonOptions) != payload) return false;
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return false; }
    }

    private static Journal Replace(Journal ledger, Sr5CareerReputationCheckpoint next)
        => Seal(checked(ledger.Version + 1), ledger.Entries.Select(entry =>
            entry.Command.Request.OperationId == next.Command.Request.OperationId ? next : entry).ToArray());
    private static Journal Seal(long version, IReadOnlyList<Sr5CareerReputationCheckpoint> entries)
    {
        var empty = new Journal(1, version, entries, "");
        return empty with { Digest = CharacterCareerReputationTransaction.Hash(JsonSerializer.Serialize(empty, JsonOptions)) };
    }
    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!keys.Add(property.Name)) throw new JsonException("Duplicate journal property."); RequireUniqueProperties(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUniqueProperties(child);
    }
    private sealed record Journal(int SchemaVersion, long Version, IReadOnlyList<Sr5CareerReputationCheckpoint> Entries, string Digest);
}
