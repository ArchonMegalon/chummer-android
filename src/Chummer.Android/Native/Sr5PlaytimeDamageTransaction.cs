using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

public enum Sr5PlaytimeDamageTransactionPhase
{
    Reviewed,
    Applying,
    Applied
}

public enum Sr5PlaytimeDamageRecoveryObservation
{
    Original,
    Applied,
    Conflict
}

public sealed record Sr5PlaytimeDamageSnapshot(
    string ContractName,
    [property: JsonConverter(typeof(Sr5PlaytimeDamageOwnerJsonConverter))] OwnerScope AccountOwner,
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    long SavedRevision,
    string RulesetId,
    WorkspaceConditionMonitorTrack Track,
    string Label,
    int Filled,
    int EditableMaximum,
    string SnapshotDigest)
{
    public const string CurrentContractName = "chummer.android.sr5-playtime-damage-snapshot/v2";

    public bool IsExact()
        => string.Equals(ContractName, CurrentContractName, StringComparison.Ordinal)
           && Sr5PlaytimeDamageIntegrity.IsCanonicalAccount(AccountOwner)
           && !string.IsNullOrWhiteSpace(WorkspaceId.Value)
           && WorkspaceRevision is > 0 and < long.MaxValue
           && SavedRevision == WorkspaceRevision
           && string.Equals(RulesetId, "sr5", StringComparison.Ordinal)
           && Sr5PlaytimeDamageIntegrity.IsSupportedTrack(Track)
           && !string.IsNullOrWhiteSpace(Label)
           && Filled >= 0
           && EditableMaximum > 0
           && Filled <= EditableMaximum
           && Sr5PlaytimeDamageIntegrity.IsDigest(SnapshotDigest)
           && Sr5PlaytimeDamageIntegrity.FixedEquals(
               SnapshotDigest,
               Sr5PlaytimeDamageIntegrity.ComputeSnapshotDigest(this));
}

public sealed record Sr5PlaytimeDamageQuote(
    string ContractName,
    Guid ActionId,
    Sr5PlaytimeDamageSnapshot Original,
    int FilledAfter,
    string QuoteDigest)
{
    public const string CurrentContractName = "chummer.android.sr5-playtime-damage-quote/v2";

    public bool IsExact()
        => string.Equals(ContractName, CurrentContractName, StringComparison.Ordinal)
           && ActionId != Guid.Empty
           && Original is not null
           && Original.IsExact()
           && FilledAfter >= 0
           && FilledAfter <= Original.EditableMaximum
           && FilledAfter != Original.Filled
           && Sr5PlaytimeDamageIntegrity.IsDigest(QuoteDigest)
           && Sr5PlaytimeDamageIntegrity.FixedEquals(
               QuoteDigest,
               Sr5PlaytimeDamageIntegrity.ComputeQuoteDigest(this));

    public bool MatchesOriginal(Sr5PlaytimeDamageSnapshot snapshot)
        => IsExact()
           && snapshot is not null
           && snapshot.IsExact()
           && snapshot == Original;
}

public sealed record Sr5PlaytimeDamageReceipt(
    string ContractName,
    [property: JsonConverter(typeof(Sr5PlaytimeDamageOwnerJsonConverter))] OwnerScope AccountOwner,
    Guid ActionId,
    string IdempotencyKey,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long AppliedWorkspaceRevision,
    WorkspaceConditionMonitorTrack Track,
    string Label,
    int FilledBefore,
    int FilledAfter,
    int EditableMaximum,
    string ExpectedPostconditionDigest,
    string ObservedPostconditionDigest,
    string ReceiptDigest)
{
    public const string CurrentContractName = "chummer.android.sr5-playtime-damage-receipt/v2";

    public bool IsExact()
        => string.Equals(ContractName, CurrentContractName, StringComparison.Ordinal)
           && ActionId != Guid.Empty
           && Sr5PlaytimeDamageIntegrity.IsRawDigest(IdempotencyKey)
           && Sr5PlaytimeDamageIntegrity.IsCanonicalAccount(AccountOwner)
           && !string.IsNullOrWhiteSpace(WorkspaceId.Value)
           && ExpectedWorkspaceRevision is > 0 and < long.MaxValue
           && AppliedWorkspaceRevision == ExpectedWorkspaceRevision + 1
           && Sr5PlaytimeDamageIntegrity.IsSupportedTrack(Track)
           && !string.IsNullOrWhiteSpace(Label)
           && FilledBefore >= 0
           && FilledAfter >= 0
           && EditableMaximum > 0
           && FilledBefore <= EditableMaximum
           && FilledAfter <= EditableMaximum
           && FilledBefore != FilledAfter
           && Sr5PlaytimeDamageIntegrity.IsDigest(ExpectedPostconditionDigest)
           && string.Equals(
               ExpectedPostconditionDigest,
               ObservedPostconditionDigest,
               StringComparison.Ordinal)
           && Sr5PlaytimeDamageIntegrity.IsDigest(ReceiptDigest)
           && Sr5PlaytimeDamageIntegrity.FixedEquals(
               ReceiptDigest,
               Sr5PlaytimeDamageIntegrity.ComputeReceiptDigest(this));
}

public sealed record Sr5PlaytimeDamageJournal(
    int SchemaVersion,
    long Version,
    Sr5PlaytimeDamageTransactionPhase Phase,
    Guid OwnerId,
    string IdempotencyKey,
    Sr5PlaytimeDamageQuote Quote,
    string ExpectedPostconditionDigest,
    Sr5PlaytimeDamageReceipt? Receipt,
    string JournalDigest)
{
    public const string CurrentContractName = "chummer.android.sr5-playtime-damage-journal/v2";
    public const int CurrentSchemaVersion = 2;

    public bool IsExact()
        => Sr5PlaytimeDamageIntegrity.IsExact(this);
}

public static class Sr5PlaytimeDamageIntegrity
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const string SnapshotSchema = "chummer.android.sr5-playtime-damage-snapshot-digest/v2";
    private const string QuoteSchema = "chummer.android.sr5-playtime-damage-quote-digest/v2";
    private const string IdempotencySchema = "chummer.android.sr5-playtime-damage-idempotency/v2";
    private const string PostconditionSchema = "chummer.android.sr5-playtime-damage-postcondition/v2";
    private const string ReceiptSchema = "chummer.android.sr5-playtime-damage-receipt-digest/v2";
    private const string JournalSchema = Sr5PlaytimeDamageJournal.CurrentContractName;

    // Persisted identity is not a live lease or credential. Reject aliases rather
    // than silently merging preference namespaces. Core defines canonicalization.
    public static bool IsCanonicalAccount(OwnerScope owner)
    {
        if (string.IsNullOrWhiteSpace(owner.Value)
            || !string.Equals(owner.Value, owner.NormalizedValue, StringComparison.Ordinal)
            || (owner.UsesLocalSingleUserValue && !owner.IsLocalSingleUser)) return false;
        // UTF-8 replacement characters must not alias two different account keys.
        try { _ = StrictUtf8.GetByteCount(owner.Value); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    public static bool IsSupportedTrack(WorkspaceConditionMonitorTrack track)
        => track is WorkspaceConditionMonitorTrack.Physical
            or WorkspaceConditionMonitorTrack.Stun;

    public static bool TryProject(
        OwnerScope accountOwner,
        bool characterCreated,
        string? gameEdition,
        CharacterWorkspaceId? workspaceId,
        long workspaceRevision,
        long savedRevision,
        bool isDirty,
        string? error,
        ConditionMonitorEditorState? editor,
        WorkspaceConditionMonitorTrack track,
        out Sr5PlaytimeDamageSnapshot snapshot)
    {
        snapshot = null!;
        if (!IsCanonicalAccount(accountOwner)
            || !characterCreated
            || !string.Equals(gameEdition?.Trim(), "SR5", StringComparison.OrdinalIgnoreCase)
            || workspaceId is null
            || string.IsNullOrWhiteSpace(workspaceId.Value.Value)
            || workspaceRevision is <= 0 or >= long.MaxValue
            || savedRevision != workspaceRevision
            || isDirty
            || !string.IsNullOrWhiteSpace(error)
            || editor is null
            || !editor.CareerEditable
            || editor.Tracks is null
            || !IsSupportedTrack(track))
        {
            return false;
        }

        ConditionMonitorTrackState[] matches = editor.Tracks
            .Where(candidate => candidate is not null && candidate.Track == track)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            return false;
        ConditionMonitorTrackState selected = matches[0];
        if (string.IsNullOrWhiteSpace(selected.Label)
            || selected.ActsAsAlternateTrack
            || selected.Filled < 0
            || selected.EditableMaximum <= 0
            || selected.Filled > selected.EditableMaximum)
        {
            return false;
        }

        var unsigned = new Sr5PlaytimeDamageSnapshot(
            Sr5PlaytimeDamageSnapshot.CurrentContractName,
            accountOwner,
            workspaceId.Value,
            workspaceRevision,
            savedRevision,
            "sr5",
            track,
            selected.Label.Trim(),
            selected.Filled,
            selected.EditableMaximum,
            SnapshotDigest: string.Empty);
        snapshot = unsigned with { SnapshotDigest = ComputeSnapshotDigest(unsigned) };
        return snapshot.IsExact();
    }

    public static bool TryQuote(
        Sr5PlaytimeDamageSnapshot snapshot,
        int filledAfter,
        Guid actionId,
        out Sr5PlaytimeDamageQuote quote)
    {
        quote = null!;
        if (snapshot is null
            || !snapshot.IsExact()
            || actionId == Guid.Empty
            || filledAfter < 0
            || filledAfter > snapshot.EditableMaximum
            || filledAfter == snapshot.Filled)
        {
            return false;
        }
        var unsigned = new Sr5PlaytimeDamageQuote(
            Sr5PlaytimeDamageQuote.CurrentContractName,
            actionId,
            snapshot,
            filledAfter,
            QuoteDigest: string.Empty);
        quote = unsigned with { QuoteDigest = ComputeQuoteDigest(unsigned) };
        return quote.IsExact();
    }

    public static Sr5PlaytimeDamageJournal CreateReview(
        Sr5PlaytimeDamageQuote quote,
        Guid ownerId,
        long version)
    {
        ArgumentNullException.ThrowIfNull(quote);
        if (!quote.IsExact() || ownerId == Guid.Empty || version <= 0)
            throw new InvalidOperationException("An exact Playtime damage quote and owner are required.");
        string idempotencyKey = RawHash(
            IdempotencySchema,
            quote.Original.AccountOwner.NormalizedValue,
            ownerId.ToString("D"),
            quote.ActionId.ToString("D"),
            quote.QuoteDigest);
        string postcondition = ComputePostconditionDigest(
            quote.Original.AccountOwner,
            quote.Original.WorkspaceId,
            checked(quote.Original.WorkspaceRevision + 1),
            quote.Original.Track,
            quote.FilledAfter,
            quote.Original.EditableMaximum);
        return Sign(new Sr5PlaytimeDamageJournal(
            Sr5PlaytimeDamageJournal.CurrentSchemaVersion,
            version,
            Sr5PlaytimeDamageTransactionPhase.Reviewed,
            ownerId,
            idempotencyKey,
            quote,
            postcondition,
            Receipt: null,
            JournalDigest: string.Empty));
    }

    public static Sr5PlaytimeDamageJournal BeginApplying(Sr5PlaytimeDamageJournal reviewed)
    {
        if (!IsExact(reviewed) || reviewed.Phase != Sr5PlaytimeDamageTransactionPhase.Reviewed)
            throw new InvalidOperationException("Only the exact reviewed damage quote may enter Applying.");
        return Sign(reviewed with
        {
            Version = checked(reviewed.Version + 1),
            Phase = Sr5PlaytimeDamageTransactionPhase.Applying,
            Receipt = null,
            JournalDigest = string.Empty
        });
    }

    public static Sr5PlaytimeDamageJournal ReturnToReview(Sr5PlaytimeDamageJournal applying)
    {
        if (!IsExact(applying) || applying.Phase != Sr5PlaytimeDamageTransactionPhase.Applying)
            throw new InvalidOperationException("Only the exact Applying damage quote may return to review.");
        return Sign(applying with
        {
            Version = checked(applying.Version + 1),
            Phase = Sr5PlaytimeDamageTransactionPhase.Reviewed,
            Receipt = null,
            JournalDigest = string.Empty
        });
    }

    public static Sr5PlaytimeDamageRecoveryObservation Observe(
        Sr5PlaytimeDamageJournal journal,
        Sr5PlaytimeDamageSnapshot observed,
        out string observedPostconditionDigest)
    {
        observedPostconditionDigest = string.Empty;
        if (!IsExact(journal)
            || journal.Phase == Sr5PlaytimeDamageTransactionPhase.Applied
            || observed is null
            || !observed.IsExact()
            || observed.AccountOwner != journal.Quote.Original.AccountOwner
            || observed.WorkspaceId != journal.Quote.Original.WorkspaceId
            || observed.Track != journal.Quote.Original.Track)
        {
            return Sr5PlaytimeDamageRecoveryObservation.Conflict;
        }
        if (observed.WorkspaceRevision == journal.Quote.Original.WorkspaceRevision)
        {
            return journal.Quote.MatchesOriginal(observed)
                ? Sr5PlaytimeDamageRecoveryObservation.Original
                : Sr5PlaytimeDamageRecoveryObservation.Conflict;
        }
        if (observed.WorkspaceRevision != journal.Quote.Original.WorkspaceRevision + 1
            || observed.SavedRevision != observed.WorkspaceRevision
            || observed.EditableMaximum != journal.Quote.Original.EditableMaximum
            || observed.Filled != journal.Quote.FilledAfter)
        {
            return Sr5PlaytimeDamageRecoveryObservation.Conflict;
        }
        observedPostconditionDigest = ComputePostconditionDigest(
            observed.AccountOwner,
            observed.WorkspaceId,
            observed.WorkspaceRevision,
            observed.Track,
            observed.Filled,
            observed.EditableMaximum);
        return string.Equals(
                observedPostconditionDigest,
                journal.ExpectedPostconditionDigest,
                StringComparison.Ordinal)
            ? Sr5PlaytimeDamageRecoveryObservation.Applied
            : Sr5PlaytimeDamageRecoveryObservation.Conflict;
    }

    public static Sr5PlaytimeDamageJournal Complete(
        Sr5PlaytimeDamageJournal applying,
        Sr5PlaytimeDamageSnapshot observed)
    {
        if (!IsExact(applying) || applying.Phase != Sr5PlaytimeDamageTransactionPhase.Applying)
            throw new InvalidOperationException("Only the exact Applying damage quote can complete.");
        if (Observe(applying, observed, out string observedPostcondition)
            != Sr5PlaytimeDamageRecoveryObservation.Applied)
        {
            throw new InvalidOperationException("The exact next-revision damage postcondition was not observed.");
        }
        var unsignedReceipt = new Sr5PlaytimeDamageReceipt(
            Sr5PlaytimeDamageReceipt.CurrentContractName,
            applying.Quote.Original.AccountOwner,
            applying.Quote.ActionId,
            applying.IdempotencyKey,
            applying.Quote.Original.WorkspaceId,
            applying.Quote.Original.WorkspaceRevision,
            observed.WorkspaceRevision,
            applying.Quote.Original.Track,
            applying.Quote.Original.Label,
            applying.Quote.Original.Filled,
            applying.Quote.FilledAfter,
            applying.Quote.Original.EditableMaximum,
            applying.ExpectedPostconditionDigest,
            observedPostcondition,
            ReceiptDigest: string.Empty);
        Sr5PlaytimeDamageReceipt receipt = unsignedReceipt with
        {
            ReceiptDigest = ComputeReceiptDigest(unsignedReceipt)
        };
        if (!receipt.IsExact())
            throw new InvalidOperationException("The Playtime damage receipt is incoherent.");
        return Sign(applying with
        {
            Version = checked(applying.Version + 1),
            Phase = Sr5PlaytimeDamageTransactionPhase.Applied,
            Receipt = receipt,
            JournalDigest = string.Empty
        });
    }

    public static bool IsExact(Sr5PlaytimeDamageJournal? journal)
    {
        if (journal is null
            || journal.SchemaVersion != Sr5PlaytimeDamageJournal.CurrentSchemaVersion
            || journal.Version <= 0
            || !Enum.IsDefined(journal.Phase)
            || journal.OwnerId == Guid.Empty
            || !IsRawDigest(journal.IdempotencyKey)
            || journal.Quote is null
            || !journal.Quote.IsExact()
            || !IsDigest(journal.ExpectedPostconditionDigest)
            || !IsDigest(journal.JournalDigest))
        {
            return false;
        }
        string expectedPostcondition;
        try
        {
            expectedPostcondition = ComputePostconditionDigest(
                journal.Quote.Original.AccountOwner,
                journal.Quote.Original.WorkspaceId,
                checked(journal.Quote.Original.WorkspaceRevision + 1),
                journal.Quote.Original.Track,
                journal.Quote.FilledAfter,
                journal.Quote.Original.EditableMaximum);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (!string.Equals(
                expectedPostcondition,
                journal.ExpectedPostconditionDigest,
                StringComparison.Ordinal))
        {
            return false;
        }
        bool receiptMatches = journal.Phase == Sr5PlaytimeDamageTransactionPhase.Applied
            ? journal.Receipt is { } receipt
              && receipt.IsExact()
              && receipt.ActionId == journal.Quote.ActionId
              && string.Equals(receipt.IdempotencyKey, journal.IdempotencyKey, StringComparison.Ordinal)
              && receipt.AccountOwner == journal.Quote.Original.AccountOwner
              && receipt.WorkspaceId == journal.Quote.Original.WorkspaceId
              && receipt.ExpectedWorkspaceRevision == journal.Quote.Original.WorkspaceRevision
              && receipt.Track == journal.Quote.Original.Track
              && string.Equals(receipt.Label, journal.Quote.Original.Label, StringComparison.Ordinal)
              && receipt.FilledBefore == journal.Quote.Original.Filled
              && receipt.FilledAfter == journal.Quote.FilledAfter
              && receipt.EditableMaximum == journal.Quote.Original.EditableMaximum
              && string.Equals(
                  receipt.ExpectedPostconditionDigest,
                  journal.ExpectedPostconditionDigest,
                  StringComparison.Ordinal)
            : journal.Receipt is null;
        return receiptMatches
               && FixedEquals(journal.JournalDigest, ComputeJournalDigest(journal));
    }

    internal static string ComputeSnapshotDigest(Sr5PlaytimeDamageSnapshot snapshot)
        => Hash(
            SnapshotSchema,
            snapshot.ContractName,
            snapshot.AccountOwner.NormalizedValue,
            snapshot.WorkspaceId.Value,
            snapshot.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            snapshot.SavedRevision.ToString(CultureInfo.InvariantCulture),
            snapshot.RulesetId,
            snapshot.Track.ToString(),
            snapshot.Label,
            snapshot.Filled.ToString(CultureInfo.InvariantCulture),
            snapshot.EditableMaximum.ToString(CultureInfo.InvariantCulture));

    internal static string ComputeQuoteDigest(Sr5PlaytimeDamageQuote quote)
        => Hash(
            QuoteSchema,
            quote.ContractName,
            quote.Original.AccountOwner.NormalizedValue,
            quote.ActionId.ToString("D"),
            quote.Original.SnapshotDigest,
            quote.FilledAfter.ToString(CultureInfo.InvariantCulture));

    internal static string ComputeReceiptDigest(Sr5PlaytimeDamageReceipt receipt)
        => Hash(
            ReceiptSchema,
            receipt.ContractName,
            receipt.AccountOwner.NormalizedValue,
            receipt.ActionId.ToString("D"),
            receipt.IdempotencyKey,
            receipt.WorkspaceId.Value,
            receipt.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            receipt.AppliedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            receipt.Track.ToString(),
            receipt.Label,
            receipt.FilledBefore.ToString(CultureInfo.InvariantCulture),
            receipt.FilledAfter.ToString(CultureInfo.InvariantCulture),
            receipt.EditableMaximum.ToString(CultureInfo.InvariantCulture),
            receipt.ExpectedPostconditionDigest,
            receipt.ObservedPostconditionDigest);

    internal static string ComputePostconditionDigest(
        OwnerScope accountOwner,
        CharacterWorkspaceId workspaceId,
        long appliedRevision,
        WorkspaceConditionMonitorTrack track,
        int filled,
        int editableMaximum)
        => Hash(
            PostconditionSchema,
            accountOwner.NormalizedValue,
            workspaceId.Value,
            appliedRevision.ToString(CultureInfo.InvariantCulture),
            track.ToString(),
            filled.ToString(CultureInfo.InvariantCulture),
            editableMaximum.ToString(CultureInfo.InvariantCulture));

    public static bool IsDigest(string? value)
        => value is { Length: 71 }
           && value.StartsWith("sha256:", StringComparison.Ordinal)
           && IsHex(value.AsSpan(7));

    public static bool IsRawDigest(string? value)
        => value is { Length: 64 }
           && IsHex(value.AsSpan());

    internal static bool FixedEquals(string left, string right)
        => left.Length == right.Length
           && CryptographicOperations.FixedTimeEquals(
               Encoding.ASCII.GetBytes(left),
               Encoding.ASCII.GetBytes(right));

    private static Sr5PlaytimeDamageJournal Sign(Sr5PlaytimeDamageJournal journal)
        => journal with { JournalDigest = ComputeJournalDigest(journal) };

    private static string ComputeJournalDigest(Sr5PlaytimeDamageJournal journal)
        => Hash(
            JournalSchema,
            journal.Quote.Original.AccountOwner.NormalizedValue,
            journal.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            journal.Version.ToString(CultureInfo.InvariantCulture),
            journal.Phase.ToString(),
            journal.OwnerId.ToString("D"),
            journal.IdempotencyKey,
            journal.Quote.QuoteDigest,
            journal.ExpectedPostconditionDigest,
            journal.Receipt?.ReceiptDigest ?? string.Empty);

    private static string RawHash(params string[] values)
        => Hash(values)["sha256:".Length..];

    private static string Hash(params string[] values)
    {
        var canonical = new StringBuilder();
        foreach (string value in values)
            canonical.Append(value.Length).Append(':').Append(value).Append(';');
        return "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool IsHex(ReadOnlySpan<char> value)
        => value.IndexOfAnyExcept("0123456789abcdef") < 0;
}

// Only a stable Core account identity is serialized; no issuer/epoch is persisted.
// Reconstructing the Local identity here attributes history, never admits a lease.
public sealed class Sr5PlaytimeDamageOwnerJsonConverter : JsonConverter<OwnerScope>
{
    public override OwnerScope Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A canonical account identity is required.");
        string raw = reader.GetString()!;
        OwnerScope owner = new(raw);
        if (string.IsNullOrWhiteSpace(raw) || raw != owner.NormalizedValue)
            throw new JsonException("Account identity aliases are not accepted.");
        return owner.UsesLocalSingleUserValue ? OwnerScope.LocalSingleUser : owner;
    }
    public override void Write(Utf8JsonWriter writer, OwnerScope owner, JsonSerializerOptions options)
    {
        if (!Sr5PlaytimeDamageIntegrity.IsCanonicalAccount(owner)) throw new JsonException("Invalid account identity.");
        writer.WriteStringValue(owner.NormalizedValue);
    }
}

internal sealed class PreferencesSr5PlaytimeDamageJournalBackend(
    OwnerScope accountOwner,
    WorkspaceConditionMonitorTrack track,
    CharacterWorkspaceId workspaceId) : ISr5CareerCheckpointBackend
{
    private static string WorkspaceToken(CharacterWorkspaceId id)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id.Value)));

    private readonly string _legacyStorageKey = track switch
    {
        WorkspaceConditionMonitorTrack.Physical =>
            $"sr5.playtime.damage.physical.{WorkspaceToken(workspaceId)}.v1",
        WorkspaceConditionMonitorTrack.Stun =>
            $"sr5.playtime.damage.stun.{WorkspaceToken(workspaceId)}.v1",
        _ => throw new ArgumentOutOfRangeException(nameof(track))
    };

    private readonly string _storageKey = $"sr5.playtime.damage.{track.ToString().ToLowerInvariant()}."
        + $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accountOwner.NormalizedValue)))}."
        + $"{WorkspaceToken(workspaceId)}.v2";

    internal bool HasUnattributedLegacy => !string.IsNullOrEmpty(Preferences.Default.Get(_legacyStorageKey, string.Empty));
    public string Read() => Preferences.Default.Get(_storageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(_storageKey, payload);
    public void Remove() => Preferences.Default.Remove(_storageKey);
}

public sealed class Sr5PlaytimeDamageJournalStore
{
    private const int MaximumPayloadCharacters = 64 * 1024;
    private static readonly object Gate = new();
    private readonly ISr5CareerCheckpointBackend _backend;
    private readonly Sr5CareerMutationOwnerStore _mutationOwners;
    public OwnerScope AccountOwner { get; }
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly WorkspaceConditionMonitorTrack _track;
    public const string LegacyUnattributedBlocker = "Legacy unattributed Playtime damage history is quarantined. No account was assigned and its mutation owner remains unresolved.";

    internal Sr5PlaytimeDamageJournalStore(
        ISr5CareerCheckpointBackend backend,
        Sr5CareerMutationOwnerStore mutationOwners,
        OwnerScope accountOwner,
        CharacterWorkspaceId workspaceId,
        WorkspaceConditionMonitorTrack track)
    {
        if (!Sr5PlaytimeDamageIntegrity.IsCanonicalAccount(accountOwner)
            || string.IsNullOrWhiteSpace(workspaceId.Value) || !Sr5PlaytimeDamageIntegrity.IsSupportedTrack(track))
            throw new ArgumentException("An exact account, workspace and track are required for damage history.");
        AccountOwner = accountOwner;
        _workspaceId = workspaceId;
        _track = track;
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _mutationOwners = mutationOwners ?? throw new ArgumentNullException(nameof(mutationOwners));
    }

    public static Sr5PlaytimeDamageJournalStore CreateDefault(
        OwnerScope accountOwner,
        WorkspaceConditionMonitorTrack track,
        CharacterWorkspaceId? workspaceId)
        => new(
            new PreferencesSr5PlaytimeDamageJournalBackend(
                accountOwner,
                track,
                workspaceId.HasValue
                && !string.IsNullOrWhiteSpace(workspaceId.Value.Value)
                    ? workspaceId.Value
                    : throw new InvalidOperationException(
                        "A loaded runner workspace is required for Playtime damage recovery.")),
            Sr5CareerMutationOwnerStore.CreateDefault(), accountOwner, workspaceId!.Value, track);

    internal static Sr5PlaytimeDamageJournalStore CreateIsolated(
        ISr5CareerCheckpointBackend backend, OwnerScope accountOwner,
        CharacterWorkspaceId workspaceId, WorkspaceConditionMonitorTrack track)
        => new(backend, Sr5CareerMutationOwnerStore.CreateIsolated(), accountOwner, workspaceId, track);

    private bool HasBinding(Sr5PlaytimeDamageSnapshot? snapshot)
        => snapshot is not null && IsBoundTo(snapshot.AccountOwner, snapshot.WorkspaceId, snapshot.Track);

    internal bool IsBoundTo(OwnerScope accountOwner, CharacterWorkspaceId workspaceId,
        WorkspaceConditionMonitorTrack track)
        => accountOwner == AccountOwner && workspaceId == _workspaceId && track == _track;

    public bool TryRead(out Sr5PlaytimeDamageJournal? journal, out string blocker)
    {
        bool found;
        lock (Gate)
            found = TryReadLocked(out journal, out blocker);
        if (found && !TryReconcileResolvedOwner(journal!, out blocker))
        {
            journal = null;
            return false;
        }
        return found;
    }

    public bool TryWriteReview(
        Sr5PlaytimeDamageQuote quote,
        Guid ownerId,
        out Sr5PlaytimeDamageJournal review,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(quote);
        review = null!;
        if (!quote.IsExact() || !HasBinding(quote.Original))
        {
            blocker = "The damage quote belongs to another account, runner or track.";
            return false;
        }
        lock (Gate)
        {
            long version = 1;
            if (TryReadLocked(out Sr5PlaytimeDamageJournal? current, out string readBlocker))
            {
                if (current!.Phase != Sr5PlaytimeDamageTransactionPhase.Reviewed)
                {
                    blocker = "Resolve the existing Playtime damage transaction first.";
                    return false;
                }
                version = checked(current.Version + 1);
            }
            else if (!string.IsNullOrWhiteSpace(readBlocker))
            {
                blocker = readBlocker;
                return false;
            }
            try
            {
                review = Sr5PlaytimeDamageIntegrity.CreateReview(quote, ownerId, version);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
            {
                blocker = exception.Message;
                return false;
            }
            return TryWriteLocked(review, out blocker);
        }
    }

    public bool TryBeginApplying(
        Sr5PlaytimeDamageJournal expected,
        out Sr5PlaytimeDamageJournal applying,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        applying = null!;
        if (!expected.IsExact() || !HasBinding(expected.Quote.Original)
            || expected.Phase != Sr5PlaytimeDamageTransactionPhase.Reviewed)
        {
            blocker = "Only the exact reviewed Playtime damage quote may enter Applying.";
            return false;
        }
        Sr5PlaytimeDamageJournal candidate;
        try
        {
            candidate = Sr5PlaytimeDamageIntegrity.BeginApplying(expected);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            blocker = exception.Message;
            return false;
        }
        Sr5CareerMutationOwner owner = Owner(candidate);
        bool began = _mutationOwners.TryBegin(
            owner,
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireLocked(expected, out string casBlocker))
                    {
                        return new Sr5CareerMutationBeginResult(
                            false,
                            ExactReviewedStateWasRestored: false,
                            casBlocker);
                    }
                    bool wrote = TryWriteLocked(candidate, out string writeBlocker);
                    return new Sr5CareerMutationBeginResult(
                        wrote,
                        ExactReviewedStateWasRestored: !wrote && TryRequireLocked(expected, out _),
                        writeBlocker);
                }
            },
            out blocker);
        if (began)
            applying = candidate;
        return began;
    }

    public async Task<IDisposable> AcquireApplyingLeaseAsync(
        Sr5PlaytimeDamageJournal applying,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applying);
        if (!applying.IsExact() || !HasBinding(applying.Quote.Original))
            throw new InvalidOperationException("The damage journal belongs to another account, runner or track.");
        IDisposable lease = await _mutationOwners.AcquireExecutionLeaseAsync(
            Owner(applying),
            cancellationToken).ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (!TryRequireLocked(applying, out _))
                    throw new InvalidOperationException(
                        "The exact durable Playtime damage journal no longer owns mutation.");
            }
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public bool TryReturnToReview(
        Sr5PlaytimeDamageJournal applying,
        out Sr5PlaytimeDamageJournal review,
        out string blocker)
    {
        review = null!;
        try
        {
            review = Sr5PlaytimeDamageIntegrity.ReturnToReview(applying);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            blocker = exception.Message;
            return false;
        }
        return CompleteOwner(applying, review, out blocker);
    }

    public bool TryComplete(
        Sr5PlaytimeDamageJournal applying,
        Sr5PlaytimeDamageSnapshot observed,
        out Sr5PlaytimeDamageJournal applied,
        out string blocker)
    {
        applied = null!;
        try
        {
            applied = Sr5PlaytimeDamageIntegrity.Complete(applying, observed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            blocker = exception.Message;
            return false;
        }
        return CompleteOwner(applying, applied, out blocker);
    }

    public bool TryClearApplied(Sr5PlaytimeDamageJournal expected, out string blocker)
        => TryClear(expected, Sr5PlaytimeDamageTransactionPhase.Applied, out blocker);

    public bool TryDiscardReview(Sr5PlaytimeDamageJournal expected, out string blocker)
        => TryClear(expected, Sr5PlaytimeDamageTransactionPhase.Reviewed, out blocker);

    private bool CompleteOwner(
        Sr5PlaytimeDamageJournal applying,
        Sr5PlaytimeDamageJournal resolution,
        out string blocker)
    {
        if (!HasBinding(applying.Quote.Original) || !HasBinding(resolution.Quote.Original))
        {
            blocker = "The damage resolution belongs to another account, runner or track.";
            return false;
        }
        return _mutationOwners.TryComplete(
            Owner(applying),
            () =>
            {
                lock (Gate)
                {
                    if (!TryRequireLocked(applying, out string casBlocker))
                        return (false, casBlocker);
                    return TryWriteLocked(resolution, out string writeBlocker)
                        ? (true, string.Empty)
                        : (false, writeBlocker);
                }
            },
            out blocker);
    }

    private bool TryClear(
        Sr5PlaytimeDamageJournal expected,
        Sr5PlaytimeDamageTransactionPhase requiredPhase,
        out string blocker)
    {
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != requiredPhase)
            {
                blocker = "Only the exact resolved Playtime damage journal may be cleared.";
                return false;
            }
            if (!TryRequireLocked(expected, out blocker))
                return false;
            try
            {
                _backend.Remove();
                if (!string.IsNullOrEmpty(_backend.Read()))
                {
                    blocker = "The Playtime damage journal clear failed read-back.";
                    return false;
                }
                blocker = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                blocker = $"The Playtime damage journal could not be cleared: {exception.Message}";
                return false;
            }
        }
    }

    private static Sr5CareerMutationOwner Owner(Sr5PlaytimeDamageJournal applying)
    {
        if (!applying.IsExact() || applying.Phase != Sr5PlaytimeDamageTransactionPhase.Applying)
            throw new InvalidOperationException("Only an exact Applying Playtime damage journal owns mutation.");
        return new Sr5CareerMutationOwner(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.PlaytimeDamage,
            applying.Quote.Original.WorkspaceId.Value,
            applying.OwnerId,
            applying.Quote.ActionId,
            applying.Version,
            applying.Quote.Original.WorkspaceRevision,
            applying.IdempotencyKey);
    }

    private bool TryReconcileResolvedOwner(
        Sr5PlaytimeDamageJournal journal,
        out string blocker)
    {
        blocker = string.Empty;
        if (!journal.IsExact()
            || journal.Phase == Sr5PlaytimeDamageTransactionPhase.Applying
            || journal.Version < 3)
        {
            return true;
        }
        var owner = new Sr5CareerMutationOwner(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.PlaytimeDamage,
            journal.Quote.Original.WorkspaceId.Value,
            journal.OwnerId,
            journal.Quote.ActionId,
            checked(journal.Version - 1),
            journal.Quote.Original.WorkspaceRevision,
            journal.IdempotencyKey);
        return _mutationOwners.TryReconcileResolved(
            owner,
            () =>
            {
                lock (Gate)
                {
                    return TryReadLocked(out Sr5PlaytimeDamageJournal? current, out _)
                           && current == journal
                           && current.Phase != Sr5PlaytimeDamageTransactionPhase.Applying;
                }
            },
            out blocker);
    }

    private bool TryRequireLocked(Sr5PlaytimeDamageJournal expected, out string blocker)
    {
        if (!TryReadLocked(out Sr5PlaytimeDamageJournal? current, out blocker)
            || current != expected)
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The Playtime damage journal changed before CAS."
                : blocker;
            return false;
        }
        return true;
    }

    private bool TryReadLocked(
        out Sr5PlaytimeDamageJournal? journal,
        out string blocker)
    {
        journal = null;
        try
        {
            if (_backend is PreferencesSr5PlaytimeDamageJournalBackend preferences && preferences.HasUnattributedLegacy)
            {
                blocker = LegacyUnattributedBlocker;
                return false;
            }
            string payload = _backend.Read();
            if (string.IsNullOrEmpty(payload))
            {
                blocker = string.Empty;
                return false;
            }
            if (payload.Length > MaximumPayloadCharacters)
            {
                blocker = "The Playtime damage journal is oversized and replay-blocking.";
                return false;
            }
            journal = JsonSerializer.Deserialize<Sr5PlaytimeDamageJournal>(payload);
            if (journal?.SchemaVersion == 1)
            {
                journal = null;
                blocker = LegacyUnattributedBlocker;
                return false;
            }
            if (journal is null || !journal.IsExact() || !HasBinding(journal.Quote.Original))
            {
                journal = null;
                blocker = "The Playtime damage journal is invalid and replay-blocking.";
                return false;
            }
            if (!string.Equals(JsonSerializer.Serialize(journal), payload, StringComparison.Ordinal))
            {
                journal = null;
                blocker = "The Playtime damage journal is not canonical and replay-blocking.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            journal = null;
            blocker = $"The Playtime damage journal is unreadable and replay-blocking: {exception.Message}";
            return false;
        }
    }

    private bool TryWriteLocked(Sr5PlaytimeDamageJournal journal, out string blocker)
    {
        if (!journal.IsExact() || !HasBinding(journal.Quote.Original))
        {
            blocker = "The Playtime damage journal is not exact for this account, runner and track.";
            return false;
        }
        try
        {
            string payload = JsonSerializer.Serialize(journal);
            if (payload.Length is 0 or > MaximumPayloadCharacters)
            {
                blocker = "The Playtime damage journal exceeds its bounded payload.";
                return false;
            }
            _backend.Write(payload);
            if (!TryReadLocked(out Sr5PlaytimeDamageJournal? readBack, out blocker)
                || readBack != journal)
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "The Playtime damage journal failed exact read-back."
                    : blocker;
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            blocker = $"The Playtime damage journal could not be written: {exception.Message}";
            return false;
        }
    }
}
