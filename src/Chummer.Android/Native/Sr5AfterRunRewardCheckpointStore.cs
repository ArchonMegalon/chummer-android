using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

public interface ISr5AfterRunRewardJournalBackend
{
    string Read();
    void Write(string payload);
}

/// <summary>
/// Android/Linux durable host journal, outside the Core workspace. The supplied
/// state directory must already exist. Failed rename/fsync acknowledgements do
/// not remove the possibly published journal.
/// </summary>
public sealed class FileSr5AfterRunRewardJournalBackend : ISr5AfterRunRewardJournalBackend
{
    private readonly string _directory;
    private readonly string _path;

    public FileSr5AfterRunRewardJournalBackend(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _directory = Path.GetFullPath(stateDirectory);
        if (!Directory.Exists(_directory))
            throw new DirectoryNotFoundException("The reward journal state directory must already exist.");
        _path = Path.Combine(_directory, "sr5-after-run-rewards.v1.json");
    }

    public string Read()
    {
        FileStream opened;
        try { opened = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return string.Empty; }
        using var stream = opened;
        if (stream.Length == 0)
            throw new InvalidDataException("An existing empty reward journal is corrupt, not absent.");
        if (stream.Length > Sr5AfterRunRewardCheckpointStore.MaximumJournalBytes)
            throw new InvalidDataException("Reward journal capacity exceeded; history must be retained.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        string payload = reader.ReadToEnd();
        return payload.Length > 0 ? payload
            : throw new InvalidDataException("An existing empty reward journal is corrupt, not absent.");
    }

    public void Write(string payload)
    {
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Durable reward journal directory sync requires Android/Linux.");
        if (Encoding.UTF8.GetByteCount(payload) > Sr5AfterRunRewardCheckpointStore.MaximumJournalBytes)
            throw new InvalidDataException("Reward journal capacity exceeded; history must be retained.");
        string temporary = Path.Combine(_directory, $".sr5-after-run-rewards.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                writer.Write(payload);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
            SyncDirectory(_directory);
        }
        finally
        {
            // Only an unpublished temporary file is eligible for cleanup.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void SyncDirectory(string directory)
    {
        int descriptor = Open(directory, 0x10000 | 0x80000); // O_RDONLY | O_DIRECTORY | O_CLOEXEC
        if (descriptor < 0) throw new IOException("Cannot open reward journal directory for sync.",
            new Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (Fsync(descriptor) != 0)
                throw new IOException("Reward journal directory sync acknowledgement is unavailable.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally { Close(descriptor); }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int descriptor);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}

/// <summary>
/// Append-retaining host ledger. Reads never release ownership. Every transition
/// observes the existing shared process gate before the local journal gate.
/// </summary>
public sealed class Sr5AfterRunRewardCheckpointStore
{
    public const int MaximumJournalBytes = 16 * 1024 * 1024;
    private const int MaximumEntries = 4096;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };
    private readonly ISr5AfterRunRewardJournalBackend _backend;
    private readonly Sr5CareerMutationOwnerStore _owners;

    internal Sr5AfterRunRewardCheckpointStore(ISr5AfterRunRewardJournalBackend backend,
        Sr5CareerMutationOwnerStore owners)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _owners = owners ?? throw new ArgumentNullException(nameof(owners));
    }

    public static Sr5AfterRunRewardCheckpointStore CreateDefault(string stateDirectory)
        => new(new FileSr5AfterRunRewardJournalBackend(stateDirectory),
            Sr5CareerMutationOwnerStore.CreateDefault());

    public bool TryReadOwned(Guid ownerId, CharacterWorkspaceId workspaceId,
        out IReadOnlyList<Sr5AfterRunRewardCheckpoint> checkpoints, out string blocker)
    {
        checkpoints = [];
        if (ownerId == Guid.Empty || !CharacterAfterRunRewardProjector.IsValidWorkspaceId(workspaceId))
        {
            blocker = "An exact local owner and workspace are required.";
            return false;
        }
        lock (Gate)
        {
            if (!TryReadLedger(out var ledger, out blocker)) return false;
            checkpoints = Array.AsReadOnly(ledger.Entries
                .Where(entry => entry.OwnerId == ownerId && entry.Command.WorkspaceId == workspaceId)
                .ToArray());
            return true;
        }
    }

    internal bool TryGet(Guid ownerId, CharacterWorkspaceId workspaceId, Guid operationId,
        out Sr5AfterRunRewardCheckpoint? checkpoint, out string blocker)
    {
        checkpoint = null;
        lock (Gate)
        {
            if (!TryReadLedger(out var ledger, out blocker)) return false;
            checkpoint = ledger.Entries.SingleOrDefault(entry => entry.Command.OperationId == operationId);
            if (checkpoint is not null
                && (checkpoint.OwnerId != ownerId || checkpoint.Command.WorkspaceId != workspaceId))
            {
                checkpoint = null;
                blocker = "The reward operation belongs to another owner or workspace.";
                return false;
            }
            return true;
        }
    }

    internal bool TryPrepare(Sr5AfterRunRewardCheckpoint checkpoint, Func<bool> owns,
        out string blocker)
        => TryPrepare(checkpoint, owns, out blocker, out _);

    internal bool TryPrepare(Sr5AfterRunRewardCheckpoint checkpoint, Func<bool> owns,
        out string blocker, out bool writeOutcomeUnknown)
    {
        bool attemptedWrite = false;
        bool prepared = _owners.TryRunWhenUnowned(() =>
        {
            lock (Gate)
            {
                if (!owns() || !checkpoint.IsExact()
                    || checkpoint.Phase != Sr5AfterRunRewardCheckpointPhase.Confirmed)
                    return (false, "The confirmed reward no longer owns this exact saved runner.");
                if (!TryReadLedger(out var ledger, out string readBlocker)) return (false, readBlocker);
                if (ledger.Entries.Any(entry => entry.Phase != Sr5AfterRunRewardCheckpointPhase.Applied))
                    return (false, "An unresolved reward already owns the journal. Recover its original command.");
                if (ledger.Entries.Count >= MaximumEntries
                    || ledger.Entries.Any(entry => entry.Command.OperationId == checkpoint.Command.OperationId
                        || entry.Command.WorkspaceId == checkpoint.Command.WorkspaceId
                            && entry.Command.RewardId == checkpoint.Command.RewardId))
                    return (false, "Reward identity already exists or journal capacity is exhausted; retain history.");
                var next = Seal(checked(ledger.Version + 1), [.. ledger.Entries, checkpoint]);
                attemptedWrite = true;
                bool written = TryWriteLedger(next, out string writeBlocker);
                return (written, writeBlocker);
            }
        }, out blocker);
        writeOutcomeUnknown = !prepared && attemptedWrite;
        return prepared;
    }

    internal bool TryBegin(Sr5AfterRunRewardCheckpoint confirmed, Func<bool> owns,
        out Sr5AfterRunRewardCheckpoint? applying, out string blocker)
    {
        applying = null;
        lock (Gate)
        {
            if (!owns() || confirmed.Phase != Sr5AfterRunRewardCheckpointPhase.Confirmed
                || !TryRequire(confirmed, out _, out _))
            {
                blocker = "The exact confirmed reward no longer owns the current runner.";
                return false;
            }
        }
        Sr5AfterRunRewardCheckpoint? durable = null;
        bool began = _owners.TryBegin(confirmed.MutationOwner(), () =>
        {
            lock (Gate)
            {
                if (!owns() || confirmed.Phase != Sr5AfterRunRewardCheckpointPhase.Confirmed
                    || !TryRequire(confirmed, out var ledger, out string readBlocker))
                    return new(false, TryRequire(confirmed, out _, out _),
                        "The exact confirmed reward journal or owner changed.");
                var next = confirmed with { Version = 2, Phase = Sr5AfterRunRewardCheckpointPhase.Applying };
                bool written = TryWriteLedger(Replace(ledger, next), out string writeBlocker);
                if (written) durable = next;
                // Never overwrite an uncertain write with earlier bytes.
                bool unchanged = !written && TryRequire(confirmed, out _, out _);
                return new(written, unchanged, writeBlocker);
            }
        }, out blocker);
        applying = durable;
        return began && durable is not null;
    }

    internal bool TryOwnApplying(Sr5AfterRunRewardCheckpoint checkpoint, Func<bool> owns,
        out string blocker)
    {
        lock (Gate)
        {
            if (!owns() || checkpoint.Phase != Sr5AfterRunRewardCheckpointPhase.Applying
                || !TryRequire(checkpoint, out _, out _))
            {
                blocker = "The original Applying reward has changed; resolve its exact current journal.";
                return false;
            }
        }
        return _owners.TryBegin(checkpoint.MutationOwner(), () =>
        {
            lock (Gate)
            {
                bool exact = owns() && checkpoint.Phase == Sr5AfterRunRewardCheckpointPhase.Applying
                    && TryRequire(checkpoint, out _, out _);
                return new(exact, false, exact ? string.Empty : "The original Applying reward cannot be re-owned.");
            }
        }, out blocker);
    }

    internal async Task<IDisposable> AcquireApplyingLeaseAsync(Sr5AfterRunRewardCheckpoint checkpoint,
        Func<bool> owns, bool mayCommit, CancellationToken cancellationToken)
    {
        IDisposable lease = await _owners.AcquireExecutionLeaseAsync(checkpoint.MutationOwner(),
            cancellationToken).ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (!owns() || checkpoint.Phase != Sr5AfterRunRewardCheckpointPhase.Applying
                    || !TryRequire(checkpoint, out var ledger, out _))
                    throw new InvalidOperationException("The exact reward Applying journal no longer owns this execution.");
                // A previous write may have published bytes but lost its fsync
                // acknowledgement. Repair durability before any new Commit call.
                if (mayCommit && !TryWriteLedger(ledger, out _))
                    throw new IOException("The original reward command could not be durably re-established.");
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal bool TryRecordReceipt(Sr5AfterRunRewardCheckpoint checkpoint,
        Sr5AfterRunRewardReceiptObservation observation, Func<bool> owns,
        out Sr5AfterRunRewardCheckpoint? applied, out string blocker)
    {
        applied = null;
        if (!owns() || !observation.Verifies(checkpoint))
        {
            blocker = "Only an exact current service-observed reward receipt can resolve this journal.";
            return false;
        }
        if (checkpoint.Phase == Sr5AfterRunRewardCheckpointPhase.Applied)
        {
            // Validate independently: TryReconcileResolved can succeed without
            // calling its predicate when the shared owner is already absent.
            lock (Gate)
            {
                if (!TryRequire(checkpoint, out _, out blocker)
                    || checkpoint.Receipt!.ReceiptDigest != observation.Receipt.ReceiptDigest)
                    return false;
            }
            bool reconciled = _owners.TryReconcileResolved(checkpoint.MutationOwner(), () =>
            {
                lock (Gate) return owns() && TryRequire(checkpoint, out _, out _);
            }, out blocker);
            if (reconciled && owns()) applied = checkpoint;
            return applied is not null;
        }

        Sr5AfterRunRewardCheckpoint? durable = null;
        bool resolved = _owners.TryComplete(checkpoint.MutationOwner(), () =>
        {
            lock (Gate)
            {
                if (!owns() || checkpoint.Phase != Sr5AfterRunRewardCheckpointPhase.Applying
                    || !observation.Verifies(checkpoint)
                    || !TryRequire(checkpoint, out var ledger, out _))
                    return (false, "The reward Applying journal changed before receipt persistence.");
                var next = checkpoint with
                {
                    Version = 3, Phase = Sr5AfterRunRewardCheckpointPhase.Applied,
                    Receipt = observation.Receipt
                };
                bool written = TryWriteLedger(Replace(ledger, next), out string writeBlocker);
                if (written) durable = next;
                return (written, writeBlocker);
            }
        }, out blocker);
        if (resolved) applied = durable;
        return resolved && applied is not null;
    }

    private bool TryRequire(Sr5AfterRunRewardCheckpoint expected, out RewardLedger ledger, out string blocker)
    {
        if (!expected.IsExact() || !TryReadLedger(out ledger, out blocker))
        {
            ledger = null!;
            blocker = "The exact reward journal is unreadable or invalid.";
            return false;
        }
        if (!ledger.Entries.Any(entry => Equivalent(entry, expected)))
        {
            blocker = "The reward journal version or exact command changed.";
            return false;
        }
        return true;
    }

    private bool TryReadLedger(out RewardLedger ledger, out string blocker)
    {
        ledger = null!;
        blocker = string.Empty;
        try
        {
            string payload = _backend.Read();
            if (payload.Length == 0)
            {
                ledger = Seal(0, []);
                return true;
            }
            if (Encoding.UTF8.GetByteCount(payload) > MaximumJournalBytes)
                throw new InvalidDataException("Reward history capacity exceeded.");
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            RequireUniqueProperties(document.RootElement);
            ledger = JsonSerializer.Deserialize<RewardLedger>(payload, JsonOptions)!;
            if (ledger is null || ledger.SchemaVersion != 1 || ledger.Version <= 0
                || ledger.Entries is null || ledger.Entries.Count is 0 or > MaximumEntries
                || ledger.Entries.Any(entry => entry is null || !entry.IsExact())
                || ledger.Entries.Select(entry => entry.Command.OperationId).Distinct().Count() != ledger.Entries.Count
                || ledger.Entries.Select(entry => (entry.Command.WorkspaceId, entry.Command.RewardId))
                    .Distinct().Count() != ledger.Entries.Count
                || ledger.Entries.Count(entry => entry.Phase != Sr5AfterRunRewardCheckpointPhase.Applied) > 1
                || !CharacterAfterRunRewardProjector.IsDigest(ledger.Digest)
                || ledger.Digest != Seal(ledger.Version, ledger.Entries).Digest)
                throw new InvalidDataException("Reward journal identity, history or digest is invalid.");
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {
            ledger = null!;
            blocker = "Reward journal is unavailable or corrupt. Preserve it and resolve before another reward.";
            return false;
        }
    }

    private bool TryWriteLedger(RewardLedger next, out string blocker)
    {
        blocker = string.Empty;
        try
        {
            string payload = JsonSerializer.Serialize(next, JsonOptions);
            if (Encoding.UTF8.GetByteCount(payload) > MaximumJournalBytes)
                throw new InvalidDataException("Reward journal is full; history cannot be discarded.");
            _backend.Write(payload);
            if (TryReadLedger(out var readBack, out blocker) && Equivalent(readBack, next)) return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            blocker = "Reward journal write acknowledgement is unavailable. Preserve the original operation.";
        }
        if (string.IsNullOrWhiteSpace(blocker)) blocker = "Reward journal did not survive exact read-back.";
        return false;
    }

    private static RewardLedger Replace(RewardLedger ledger, Sr5AfterRunRewardCheckpoint next)
        => Seal(checked(ledger.Version + 1), ledger.Entries.Select(entry =>
            entry.Command.OperationId == next.Command.OperationId ? next : entry).ToArray());

    private static RewardLedger Seal(long version, IReadOnlyList<Sr5AfterRunRewardCheckpoint> entries)
    {
        var unsigned = new RewardLedger(1, version, entries, string.Empty);
        string digest = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(unsigned, JsonOptions))));
        return unsigned with { Digest = digest };
    }

    private static bool Equivalent<T>(T left, T right)
        => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate journal property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in element.EnumerateArray()) RequireUniqueProperties(child);
    }

    private sealed record RewardLedger(int SchemaVersion, long Version,
        IReadOnlyList<Sr5AfterRunRewardCheckpoint> Entries, string Digest);
}
