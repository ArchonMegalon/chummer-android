using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Android.Platform;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record AndroidLinkedCharacterIntent(
    Guid OperationId, string OwnerScope, bool TrustedLocalOwner, string WorkspaceId,
    long ContentRevision, long SavedRevision, string DocumentAuthoritySha256, string ExpectedDocumentAuthoritySha256,
    string EditorAuthoritySha256, string SectionId, WorkspaceCollectionItemTarget Target,
    bool Attach, string RequestSha256, string? StagedFileName, string? StagedFileSha256,
    WorkspaceSetLinkedCharacterRequest? Attachment = null);

internal sealed record AndroidLinkedCharacterIntentObservation(
    Guid OperationId, string OwnerScope, bool TrustedLocalOwner, string WorkspaceId,
    string DocumentAuthoritySha256, long ContentRevision, long SavedRevision,
    string IntentSha256, string Outcome);

internal sealed record AndroidLinkedCharacterIntentRecord(
    AndroidLinkedCharacterIntent Intent, AndroidLinkedCharacterIntentObservation? Observation,
    bool ObservationAcknowledged = true)
{
    public bool EffectObserved => ObservationAcknowledged && Observation?.Outcome == "current-effect-observed";
    public bool NotDispatched => ObservationAcknowledged && Observation?.Outcome == "not-dispatched";
}

/// <summary>
/// App-private, append-only custody of uncertain link operations in Android's
/// single process. This is not a Core mutation receipt, historical commit lookup,
/// checkpoint, cross-process lock, or permission to replay an operation. The caller
/// must independently establish the current typed effect before calling Observe.
/// All disk work is synchronous; callers must join it off the UI thread.
/// </summary>
public sealed class AndroidLinkedCharacterIntentJournal
{
    internal const int MaximumRecordBytes = 64 * 1024;
    internal const int MaximumRecords = 512;
    private const string IntentSchema = "chummer.android.link-intent/v1";
    private const string ObservationSchema = "chummer.android.link-observation/v1";
    private const string DirectoryName = "linked-character-intents-v1";
    private static readonly object ProcessGate = new();
    // A failed directory acknowledgement must not become terminal merely because
    // the same process can read the renamed file back. Across process recreation,
    // surviving valid records still assert only their narrow host observation.
    private static readonly HashSet<string> UncertainObservations = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };
    private readonly string _directory;
    private readonly Action<string> _syncDirectory;
    private readonly Action<FileStream> _flushFile;

    public AndroidLinkedCharacterIntentJournal(string stateDirectory)
        : this(stateDirectory, AndroidPrivateFileDurability.SyncDirectory) { }

    internal AndroidLinkedCharacterIntentJournal(string stateDirectory,
        Action<string> syncDirectory, Action<FileStream>? flushFile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        if (!Path.IsPathFullyQualified(stateDirectory))
            throw new ArgumentException("Link journal requires an absolute private state directory.", nameof(stateDirectory));
        _directory = Path.Combine(Path.GetFullPath(stateDirectory), DirectoryName);
        _syncDirectory = syncDirectory ?? throw new ArgumentNullException(nameof(syncDirectory));
        _flushFile = flushFile ?? (stream => stream.Flush(flushToDisk: true));
    }

    internal void Begin(AndroidLinkedCharacterIntent intent)
    {
        Validate(intent);
        lock (ProcessGate)
        {
            EnsureDirectory(_directory);
            IReadOnlyList<AndroidLinkedCharacterIntentRecord> records = ReadAll();
            if (records.Count >= MaximumRecords)
                throw new IOException("Link journal record limit reached; no records were evicted.");
            if (records.Any(record => record.Intent.OperationId == intent.OperationId))
                throw new IOException("Link operation identity already exists.");
            if (records.Any(record => SameScope(record.Intent, intent.OwnerScope,
                    intent.TrustedLocalOwner, intent.WorkspaceId) && !record.EffectObserved && !record.NotDispatched))
                throw new IOException("An unresolved link intent already exists for this owner and workspace.");
            Publish(intent.OperationId, "intent", IntentSchema, intent);
        }
    }

    internal void Observe(Guid id, string owner, bool trusted, string workspace,
        string documentSha, long content, long saved)
    {
        ValidateScope(owner, workspace);
        lock (ProcessGate)
        {
            AndroidLinkedCharacterIntentRecord? record = ReadAll().SingleOrDefault(record =>
                record.Intent.OperationId == id && SameScope(record.Intent, owner, trusted, workspace));
            if (record is null || record.EffectObserved || record.NotDispatched
                || UncertainObservations.Contains(ObservationPath(id)))
                throw new IOException("An unresolved owned link intent is required.");
            var observation = new AndroidLinkedCharacterIntentObservation(
                id, owner, trusted, workspace, documentSha, content, saved,
                Integrity(IntentSchema, record.Intent), "current-effect-observed");
            ValidateObservation(record.Intent, observation);
            Publish(id, "observation", ObservationSchema, observation);
        }
    }

    // Only the activation-gate owner that has not crossed dispatch may call this.
    // This is not a user/recovery discard API and never establishes non-commit
    // after dispatch. No staged-file deletion occurs in this journal.
    internal void AbandonBeforeDispatch(Guid id, string owner, bool trusted, string workspace)
    {
        ValidateScope(owner, workspace);
        lock (ProcessGate)
        {
            AndroidLinkedCharacterIntentRecord? record = ReadAll().SingleOrDefault(record =>
                record.Intent.OperationId == id && SameScope(record.Intent, owner, trusted, workspace));
            if (record is null || record.EffectObserved || record.NotDispatched
                || UncertainObservations.Contains(ObservationPath(id)))
                throw new IOException("An unresolved owned link intent is required.");
            AndroidLinkedCharacterIntent intent = record.Intent;
            var observation = new AndroidLinkedCharacterIntentObservation(id, owner, trusted, workspace,
                intent.DocumentAuthoritySha256, intent.ContentRevision, intent.SavedRevision,
                Integrity(IntentSchema, intent), "not-dispatched");
            ValidateObservation(intent, observation);
            Publish(id, "observation", ObservationSchema, observation);
        }
    }

    internal IReadOnlyList<AndroidLinkedCharacterIntentRecord> Read(string owner, bool trusted, string workspace)
    {
        ValidateScope(owner, workspace);
        lock (ProcessGate)
        {
            // Never return another owner's/workspace's intent or its observation.
            // Malformed shared storage fails generically, without exposing payloads.
            return ReadAll().Where(record => SameScope(record.Intent, owner, trusted, workspace)).ToArray();
        }
    }

    internal IReadOnlyList<AndroidLinkedCharacterIntentRecord> ReadAll(string owner, bool trusted)
    {
        if (!Text(owner, 256)) throw new IOException("Link journal owner is invalid.");
        lock (ProcessGate)
            return ReadAll().Where(record => record.Intent.OwnerScope == owner
                && record.Intent.TrustedLocalOwner == trusted).ToArray();
    }

    private IReadOnlyList<AndroidLinkedCharacterIntentRecord> ReadAll()
    {
        RejectLinks(_directory);
        if (!Directory.Exists(_directory))
        {
            if (File.Exists(_directory)) throw new IOException("Link journal directory is not a directory.");
            return [];
        }
        string[] paths = Directory.EnumerateFileSystemEntries(_directory)
            .Take(MaximumRecords * 2 + 1).ToArray();
        if (paths.Length > MaximumRecords * 2
            || paths.Count(path => path.EndsWith(".intent.json", StringComparison.Ordinal)) > MaximumRecords)
            throw new IOException("Link journal record limit exceeded.");
        var intents = new Dictionary<Guid, AndroidLinkedCharacterIntent>();
        var observations = new Dictionary<Guid, AndroidLinkedCharacterIntentObservation>();
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            RejectLinks(path);
            string[] name = Path.GetFileName(path).Split('.');
            if (Directory.Exists(path) || name.Length != 3 || name[2] != "json"
                || !Guid.TryParseExact(name[0], "N", out Guid id) || id == Guid.Empty
                || id.ToString("N") != name[0] || name[1] is not ("intent" or "observation"))
                throw new IOException("Link journal contains an unknown entry; no entry was removed.");
            if (name[1] == "intent")
            {
                AndroidLinkedCharacterIntent intent = ReadEnvelope<AndroidLinkedCharacterIntent>(path, IntentSchema);
                Validate(intent);
                if (intent.OperationId != id || !intents.TryAdd(id, intent))
                    throw new IOException("Link journal intent identity differs.");
            }
            else
            {
                var observation = ReadEnvelope<AndroidLinkedCharacterIntentObservation>(path, ObservationSchema);
                if (observation.OperationId != id || !observations.TryAdd(id, observation))
                    throw new IOException("Link journal observation identity differs.");
            }
        }
        if (intents.Count > MaximumRecords)
            throw new IOException("Link journal record limit exceeded.");
        if (observations.Keys.Any(id => !intents.ContainsKey(id)))
            throw new IOException("Link journal observation has no intent.");
        var result = new List<AndroidLinkedCharacterIntentRecord>();
        foreach ((Guid id, AndroidLinkedCharacterIntent intent) in intents)
        {
            observations.TryGetValue(id, out AndroidLinkedCharacterIntentObservation? observation);
            if (observation is not null) ValidateObservation(intent, observation);
            result.Add(new(intent, observation, !UncertainObservations.Contains(ObservationPath(id))));
        }
        if (result.Where(record => !record.EffectObserved && !record.NotDispatched)
            .GroupBy(record => (record.Intent.OwnerScope, record.Intent.TrustedLocalOwner, record.Intent.WorkspaceId))
            .Any(group => group.Count() > 1))
            throw new IOException("Link journal has ambiguous unresolved ownership.");
        return result;
    }

    private T ReadEnvelope<T>(string path, string schema)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            long length = stream.Length;
            if (length is <= 0 or > MaximumRecordBytes)
                throw new IOException("Link journal record size limit exceeded.");
            bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 || stream.Length != length)
                throw new IOException("Link journal record changed while reading.");
        }
        RejectLinks(path);
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            RejectDuplicateProperties(document.RootElement);
            Envelope<T>? envelope = JsonSerializer.Deserialize<Envelope<T>>(bytes, Json);
            if (envelope is null || envelope.Payload is null || envelope.Schema != schema
                || !Hash(envelope.IntegritySha256) || envelope.IntegritySha256 != Integrity(schema, envelope.Payload)
                || !ExactJson(document.RootElement, JsonSerializer.SerializeToElement(envelope, Json)))
                throw new IOException("Link journal integrity differs.");
            return envelope.Payload;
        }
        catch (JsonException error) { throw new IOException("Link journal JSON is malformed.", error); }
    }

    private void Publish<T>(Guid id, string kind, string schema, T payload)
    {
        RejectLinks(_directory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(schema, Integrity(schema, payload), payload), Json);
        if (bytes.Length > MaximumRecordBytes) throw new IOException("Link journal record size limit exceeded.");
        string final = Path.Combine(_directory, $"{id:N}.{kind}.json");
        string temporary = Path.Combine(_directory, $".{id:N}.{kind}.{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            _flushFile(stream);
        }
        RejectLinks(_directory);
        File.Move(temporary, final, overwrite: false);
        try { _syncDirectory(_directory); }
        catch
        {
            if (kind == "observation") UncertainObservations.Add(final);
            throw;
        }
        // Deliberately no catch/finally deletion. A failed durability acknowledgement
        // retains final bytes; interrupted temporary/unknown entries fail closed.
    }

    private string ObservationPath(Guid id) => Path.Combine(_directory, $"{id:N}.observation.json");

    private void EnsureDirectory(string directory)
    {
        RejectLinks(directory);
        string? parent = Path.GetDirectoryName(directory);
        if (!Directory.Exists(directory))
        {
            if (parent is null || File.Exists(directory)) throw new IOException("Link journal directory is unavailable.");
            EnsureDirectory(parent);
            Directory.CreateDirectory(directory);
        }
        // Re-establish custody even if an earlier creation acknowledgement failed.
        if (parent is not null) _syncDirectory(parent);
        _syncDirectory(directory);
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var info = new FileInfo(current);
            if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Link journal paths must not contain symbolic links.");
        }
    }

    private static void Validate(AndroidLinkedCharacterIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ValidateScope(intent.OwnerScope, intent.WorkspaceId);
        if (intent.OperationId == Guid.Empty || intent.ContentRevision < 0 || intent.ContentRevision == long.MaxValue
            || intent.SavedRevision < 0 || intent.SavedRevision > intent.ContentRevision
            || !Hash(intent.DocumentAuthoritySha256) || !Hash(intent.ExpectedDocumentAuthoritySha256)
            || intent.ExpectedDocumentAuthoritySha256 == intent.DocumentAuthoritySha256
            || !Hash(intent.EditorAuthoritySha256) || !Hash(intent.RequestSha256)
            || intent.Target is null || intent.Target.Kind is not (WorkspaceCollectionKind.Contact or WorkspaceCollectionKind.Pet)
            || !Guid.TryParseExact(intent.Target.ItemId, "D", out Guid item) || item == Guid.Empty
            || intent.Target.NestedKind is not null || intent.Target.NestedItemId is not null
            || intent.SectionId != (intent.Target.Kind == WorkspaceCollectionKind.Contact ? "contacts" : "pets"))
            throw new IOException("Link journal intent authority is invalid.");
        if (intent.Attach)
        {
            if (!Text(intent.StagedFileName, 4096) || !Path.IsPathFullyQualified(intent.StagedFileName!)
                || Path.GetFullPath(intent.StagedFileName!) != intent.StagedFileName || !Hash(intent.StagedFileSha256)
                || intent.Attachment is not { Identity: not null } attachment
                || attachment.Target != intent.Target || attachment.FileName != intent.StagedFileName
                || !Text(attachment.RelativeFileName, 4096) || Path.IsPathFullyQualified(attachment.RelativeFileName)
                || !Text(attachment.DisplayName, 4096))
                throw new IOException("Link journal staged file binding is invalid.");
        }
        else if (intent.StagedFileName is not null || intent.StagedFileSha256 is not null || intent.Attachment is not null)
            throw new IOException("Link removal must not introduce staged file authority.");
        WorkspaceCollectionMutationRequest request = intent.Attach
            ? intent.Attachment! : new WorkspaceRemoveLinkedCharacterRequest(intent.Target);
        string requestSha = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(request, request.GetType())));
        if (intent.RequestSha256 != requestSha)
            throw new IOException("Link journal concrete typed request integrity differs.");
    }

    private static void ValidateObservation(AndroidLinkedCharacterIntent intent, AndroidLinkedCharacterIntentObservation observed)
    {
        if (observed.OperationId != intent.OperationId
            || !SameScope(intent, observed.OwnerScope, observed.TrustedLocalOwner, observed.WorkspaceId)
            || !Hash(observed.DocumentAuthoritySha256)
            || observed.IntentSha256 != Integrity(IntentSchema, intent))
            throw new IOException("Link current-effect observation is not the exact owned successor.");
        bool valid = observed.Outcome switch
        {
            "current-effect-observed" => observed.DocumentAuthoritySha256 == intent.ExpectedDocumentAuthoritySha256
                && observed.ContentRevision == intent.ContentRevision + 1 && observed.SavedRevision == intent.SavedRevision,
            "not-dispatched" => observed.DocumentAuthoritySha256 == intent.DocumentAuthoritySha256
                && observed.ContentRevision == intent.ContentRevision && observed.SavedRevision == intent.SavedRevision,
            _ => false
        };
        if (!valid) throw new IOException("Link observation outcome or revision boundary differs.");
    }

    private static bool SameScope(AndroidLinkedCharacterIntent intent, string owner, bool trusted, string workspace)
        => intent.OwnerScope == owner && intent.TrustedLocalOwner == trusted && intent.WorkspaceId == workspace;
    private static void ValidateScope(string owner, string workspace)
    {
        if (!Text(owner, 256) || !Text(workspace, 256)) throw new IOException("Link journal owner/workspace is invalid.");
    }
    private static bool Text(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value.Trim() == value && !value.Any(char.IsControl);
    private static bool Hash(string? value)
        => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Integrity<T>(string schema, T payload)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new IntegrityInput<T>(schema, payload), Json)));
    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new IOException("Link journal JSON contains duplicate fields.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }
    private static bool ExactJson(JsonElement actual, JsonElement expected)
    {
        // Required constructor parameters alone do not protect optional fields
        // or serialized read-only projections (e.g. Identity.DisplayMetatype).
        // Accept harmless property ordering/escaping, never lost or changed data.
        if (actual.ValueKind != expected.ValueKind) return false;
        if (actual.ValueKind == JsonValueKind.Object)
        {
            JsonProperty[] fields = actual.EnumerateObject().ToArray();
            return fields.Length == expected.EnumerateObject().Count()
                && fields.All(field => expected.TryGetProperty(field.Name, out JsonElement other)
                    && ExactJson(field.Value, other));
        }
        if (actual.ValueKind == JsonValueKind.Array)
            return actual.GetArrayLength() == expected.GetArrayLength()
                && actual.EnumerateArray().Zip(expected.EnumerateArray())
                    .All(pair => ExactJson(pair.First, pair.Second));
        return actual.ValueKind == JsonValueKind.String
            ? actual.GetString() == expected.GetString()
            : actual.GetRawText() == expected.GetRawText();
    }
    private sealed record IntegrityInput<T>(string Schema, T Payload);
    private sealed record Envelope<T>(string Schema, string IntegritySha256, T Payload);
}
