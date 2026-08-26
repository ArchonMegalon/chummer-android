using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

public enum CharacterCreationQualitiesCheckpointPhase
{
    Reviewed,
    Applying,
    Applied
}

/// <summary>
/// Durable phone journal for exactly one reviewed quality-draft command. The complete Core
/// preview is retained so recovery never reconstructs costs, sources or legal selections.
/// </summary>
public sealed record CharacterCreationQualitiesCheckpoint(
    string Schema,
    long Version,
    CharacterCreationQualitiesCheckpointPhase Phase,
    CharacterCreationQualitiesPreview Preview,
    IReadOnlyList<string> SelectedOptionIds,
    string IdempotencyKey,
    Guid TransactionId,
    CharacterCreationQualitiesDraftReceipt? Receipt,
    string CheckpointDigest)
{
    public const string CurrentSchema =
        "chummer.android.sr5-priority-creation-qualities-checkpoint.v1";

    public static CharacterCreationQualitiesCheckpoint CreateReviewed(
        CharacterCreationQualitiesPreview preview,
        IReadOnlyList<string> selectedOptionIds,
        Guid transactionId)
    {
        ArgumentNullException.ThrowIfNull(preview);
        selectedOptionIds ??= [];
        string[] selected = selectedOptionIds
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        string idempotencyKey = CreationQualitiesPhoneAuthority.ComputeIdempotencyKey(
            preview,
            selected);
        var candidate = new CharacterCreationQualitiesCheckpoint(
            CurrentSchema,
            Version: 1,
            CharacterCreationQualitiesCheckpointPhase.Reviewed,
            preview,
            selected,
            idempotencyKey,
            transactionId,
            Receipt: null,
            CheckpointDigest: string.Empty);
        return candidate with { CheckpointDigest = ComputeDigest(candidate) };
    }

    public bool IsStructurallyValid()
    {
        if (!string.Equals(Schema, CurrentSchema, StringComparison.Ordinal)
            || Version <= 0
            || Preview is null
            || !string.Equals(Preview.Schema, CharacterCreationQualitiesSchemas.PreviewV1, StringComparison.Ordinal)
            || !Preview.RequiresExplicitConfirmation
            || !Preview.CanConfirm
            || Preview.Blockers.Count != 0
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(Preview.PreviewDigest)
            || Preview.Binding.WorkspaceId.Value is not { Length: > 0 }
            || Preview.Binding.ContentRevision <= 0
            || Preview.Binding.SavedRevision != Preview.Binding.ContentRevision
            || SelectedOptionIds.Count > 65_536
            || SelectedOptionIds.Any(string.IsNullOrWhiteSpace)
            || SelectedOptionIds.Distinct(StringComparer.Ordinal).Count() != SelectedOptionIds.Count
            || !SelectedOptionIds.SequenceEqual(
                SelectedOptionIds.OrderBy(static item => item, StringComparer.Ordinal),
                StringComparer.Ordinal)
            || !SelectedOptionIds.SequenceEqual(
                Preview.Selections.Select(static item => item.OptionId)
                    .OrderBy(static item => item, StringComparer.Ordinal),
                StringComparer.Ordinal)
            || TransactionId == Guid.Empty
            || !string.Equals(
                IdempotencyKey,
                CreationQualitiesPhoneAuthority.ComputeIdempotencyKey(Preview, SelectedOptionIds),
                StringComparison.Ordinal)
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(IdempotencyKey)
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(CheckpointDigest)
            || !string.Equals(CheckpointDigest, ComputeDigest(this), StringComparison.Ordinal))
        {
            return false;
        }

        if (!CreationQualitiesPhoneAuthority.TryCreatePlan(
                Preview,
                SelectedOptionIds,
                IdempotencyKey,
                TransactionId,
                out CharacterCreationQualitiesDraftPlan plan))
        {
            return false;
        }

        return Phase switch
        {
            CharacterCreationQualitiesCheckpointPhase.Reviewed => Receipt is null,
            CharacterCreationQualitiesCheckpointPhase.Applying => Receipt is null,
            CharacterCreationQualitiesCheckpointPhase.Applied =>
                Receipt is not null
                && CharacterCreationQualitiesRules.IsValidReceipt(Receipt, plan, Receipt.DraftDigest),
            _ => false
        };
    }

    public bool OwnsExactReview(
        CharacterCreationQualitiesState state,
        Chummer.Presentation.Overview.CharacterOverviewState overview)
        => Phase == CharacterCreationQualitiesCheckpointPhase.Reviewed
           && IsStructurallyValid()
           && CreationQualitiesPhoneAuthority.IsReady(state, overview)
           && CreationQualitiesPhoneAuthority.BindingEquals(Preview.Binding, state.Binding)
           && CharacterCreationQualitiesRules.DigestsEqual(
               Preview.PreviewDigest,
               CharacterCreationQualitiesRules.Evaluate(new(
                   state.Binding,
                   state.Authority,
                   SelectedOptionIds)).PreviewDigest);

    public bool OwnsRecoveryRevision(
        Chummer.Presentation.Overview.CharacterOverviewState overview)
    {
        if (Phase is not (CharacterCreationQualitiesCheckpointPhase.Applying
                or CharacterCreationQualitiesCheckpointPhase.Applied)
            || !IsStructurallyValid()
            || overview.Profile?.Created != false
            || overview.WorkspaceId != Preview.Binding.WorkspaceId
            || overview.IsDirty
            || !string.IsNullOrWhiteSpace(overview.Error))
        {
            return false;
        }

        long expected = Preview.Binding.ContentRevision;
        return Phase == CharacterCreationQualitiesCheckpointPhase.Applying
            ? overview.ContentRevision == expected && overview.SavedRevision == expected
              || expected < long.MaxValue
              && overview.ContentRevision == expected + 1
              && overview.SavedRevision == expected + 1
            : expected < long.MaxValue
              && overview.ContentRevision == expected + 1
              && overview.SavedRevision == expected + 1;
    }

    public static string ComputeDigest(CharacterCreationQualitiesCheckpoint checkpoint)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            checkpoint with { CheckpointDigest = string.Empty });
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload));
    }
}

public sealed record CharacterCreationQualitiesCheckpointCas(
    long Version,
    CharacterCreationQualitiesCheckpointPhase Phase,
    string CheckpointDigest,
    string WorkspaceId,
    Guid TransactionId)
{
    public static CharacterCreationQualitiesCheckpointCas From(
        CharacterCreationQualitiesCheckpoint checkpoint)
        => new(
            checkpoint.Version,
            checkpoint.Phase,
            checkpoint.CheckpointDigest,
            checkpoint.Preview.Binding.WorkspaceId.Value,
            checkpoint.TransactionId);

    public bool Matches(CharacterCreationQualitiesCheckpoint checkpoint)
        => Version == checkpoint.Version
           && Phase == checkpoint.Phase
           && TransactionId == checkpoint.TransactionId
           && string.Equals(CheckpointDigest, checkpoint.CheckpointDigest, StringComparison.Ordinal)
           && string.Equals(
               WorkspaceId,
               checkpoint.Preview.Binding.WorkspaceId.Value,
               StringComparison.Ordinal);
}

internal interface ICharacterCreationQualitiesCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

internal sealed class PreferencesCharacterCreationQualitiesCheckpointBackend :
    ICharacterCreationQualitiesCheckpointBackend
{
    private const string StorageKey = "sr5.priority.creation.qualities.checkpoint.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

/// <summary>
/// Single-action durable CAS store. Malformed state is a replay-blocking lock; it is never
/// treated as empty and is never silently deleted.
/// </summary>
public sealed class CharacterCreationQualitiesCheckpointStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private readonly ICharacterCreationQualitiesCheckpointBackend _backend;

    internal CharacterCreationQualitiesCheckpointStore(
        ICharacterCreationQualitiesCheckpointBackend backend)
        => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    internal static CharacterCreationQualitiesCheckpointStore CreateDefault()
        => new(new PreferencesCharacterCreationQualitiesCheckpointBackend());

    public bool TryRead(
        out CharacterCreationQualitiesCheckpoint checkpoint,
        out string blocker)
    {
        lock (Gate)
            return TryReadLocked(out checkpoint, out blocker);
    }

    public bool TryCreate(
        CharacterCreationQualitiesCheckpoint checkpoint,
        out CharacterCreationQualitiesCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        lock (Gate)
        {
            stored = null!;
            if (!checkpoint.IsStructurallyValid()
                || checkpoint.Version != 1
                || checkpoint.Phase != CharacterCreationQualitiesCheckpointPhase.Reviewed)
            {
                blocker = "Only one exact version-1 Reviewed quality checkpoint may be created.";
                return false;
            }
            if (TryReadLocked(out CharacterCreationQualitiesCheckpoint existing, out string readBlocker))
            {
                blocker = CharacterCreationQualitiesCheckpointCas.From(existing).Matches(checkpoint)
                    ? "This exact quality review is already durable. Resume it."
                    : "Another workspace, revision or quality review owns the durable checkpoint.";
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
        CharacterCreationQualitiesCheckpointCas expected,
        out CharacterCreationQualitiesCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (Gate)
        {
            stored = null!;
            blocker = string.Empty;
            if (expected.Phase != CharacterCreationQualitiesCheckpointPhase.Reviewed
                || !TryRequireCasLocked(expected, out CharacterCreationQualitiesCheckpoint current, out blocker)
                || current.Version == long.MaxValue)
            {
                if (string.IsNullOrWhiteSpace(blocker))
                    blocker = "The durable quality checkpoint version cannot advance.";
                return false;
            }
            CharacterCreationQualitiesCheckpoint next = WithDigest(current with
            {
                Version = checked(current.Version + 1),
                Phase = CharacterCreationQualitiesCheckpointPhase.Applying,
                Receipt = null
            });
            return TryWriteAndReadBackLocked(next, current, out stored, out blocker);
        }
    }

    public bool TryReturnToReviewed(
        CharacterCreationQualitiesCheckpointCas expected,
        out CharacterCreationQualitiesCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (Gate)
        {
            stored = null!;
            blocker = string.Empty;
            if (expected.Phase != CharacterCreationQualitiesCheckpointPhase.Applying
                || !TryRequireCasLocked(expected, out CharacterCreationQualitiesCheckpoint current, out blocker)
                || current.Version == long.MaxValue)
            {
                if (string.IsNullOrWhiteSpace(blocker))
                    blocker = "The durable quality checkpoint version cannot advance.";
                return false;
            }
            CharacterCreationQualitiesCheckpoint next = WithDigest(current with
            {
                Version = checked(current.Version + 1),
                Phase = CharacterCreationQualitiesCheckpointPhase.Reviewed,
                Receipt = null
            });
            return TryWriteAndReadBackLocked(next, current, out stored, out blocker);
        }
    }

    public bool TryRecordApplied(
        CharacterCreationQualitiesCheckpointCas expected,
        CharacterCreationQualitiesDraftReceipt receipt,
        out CharacterCreationQualitiesCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(receipt);
        lock (Gate)
        {
            stored = null!;
            blocker = string.Empty;
            if (expected.Phase != CharacterCreationQualitiesCheckpointPhase.Applying
                || !TryRequireCasLocked(expected, out CharacterCreationQualitiesCheckpoint current, out blocker)
                || current.Version == long.MaxValue
                || !CreationQualitiesPhoneAuthority.TryCreatePlan(
                    current.Preview,
                    current.SelectedOptionIds,
                    current.IdempotencyKey,
                    current.TransactionId,
                    out CharacterCreationQualitiesDraftPlan plan)
                || !CharacterCreationQualitiesRules.IsValidReceipt(receipt, plan, receipt.DraftDigest))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact authority-validated receipt may complete this Applying checkpoint."
                    : blocker;
                return false;
            }
            CharacterCreationQualitiesCheckpoint next = WithDigest(current with
            {
                Version = checked(current.Version + 1),
                Phase = CharacterCreationQualitiesCheckpointPhase.Applied,
                Receipt = receipt
            });
            return TryWriteAndReadBackLocked(next, current, out stored, out blocker);
        }
    }

    public bool TryDeleteReviewed(
        CharacterCreationQualitiesCheckpointCas expected,
        out string blocker)
        => TryRemove(expected, CharacterCreationQualitiesCheckpointPhase.Reviewed, out blocker);

    public bool TryAcknowledgeApplied(
        CharacterCreationQualitiesCheckpointCas expected,
        out string blocker)
        => TryRemove(expected, CharacterCreationQualitiesCheckpointPhase.Applied, out blocker);

    private bool TryRemove(
        CharacterCreationQualitiesCheckpointCas expected,
        CharacterCreationQualitiesCheckpointPhase requiredPhase,
        out string blocker)
    {
        lock (Gate)
        {
            blocker = string.Empty;
            if (expected.Phase != requiredPhase
                || !TryRequireCasLocked(expected, out _, out blocker))
            {
                if (string.IsNullOrWhiteSpace(blocker))
                    blocker = "The checkpoint phase cannot be removed by this action.";
                return false;
            }
            try
            {
                _backend.Remove();
                if (string.IsNullOrEmpty(_backend.Read()))
                {
                    blocker = string.Empty;
                    return true;
                }
                blocker = "The durable quality checkpoint was not removed on read-back.";
                return false;
            }
            catch (Exception exception)
            {
                blocker = $"The durable quality checkpoint could not be removed: {exception.Message}";
                return false;
            }
        }
    }

    private bool TryRequireCasLocked(
        CharacterCreationQualitiesCheckpointCas expected,
        out CharacterCreationQualitiesCheckpoint current,
        out string blocker)
    {
        if (!TryReadLocked(out current, out blocker))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "No durable quality checkpoint exists."
                : blocker;
            return false;
        }
        if (!expected.Matches(current))
        {
            blocker = "The durable quality checkpoint changed; the stale action was rejected.";
            return false;
        }
        return true;
    }

    private bool TryReadLocked(
        out CharacterCreationQualitiesCheckpoint checkpoint,
        out string blocker)
    {
        checkpoint = null!;
        try
        {
            string payload = _backend.Read();
            if (string.IsNullOrEmpty(payload))
            {
                blocker = string.Empty;
                return false;
            }
            checkpoint = JsonSerializer.Deserialize<CharacterCreationQualitiesCheckpoint>(
                payload,
                JsonOptions)!;
            if (checkpoint is null || !checkpoint.IsStructurallyValid())
            {
                checkpoint = null!;
                blocker = "A malformed quality checkpoint blocks replay; support recovery is required.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The durable quality checkpoint is unreadable and blocks replay: {exception.Message}";
            return false;
        }
    }

    private bool TryWriteAndReadBackLocked(
        CharacterCreationQualitiesCheckpoint candidate,
        CharacterCreationQualitiesCheckpoint? rollback,
        out CharacterCreationQualitiesCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        try
        {
            string payload = JsonSerializer.Serialize(candidate, JsonOptions);
            _backend.Write(payload);
            if (TryReadLocked(out CharacterCreationQualitiesCheckpoint readBack, out string readBlocker)
                && string.Equals(readBack.CheckpointDigest, candidate.CheckpointDigest, StringComparison.Ordinal)
                && CharacterCreationQualitiesCheckpointCas.From(readBack).Matches(candidate))
            {
                stored = readBack;
                blocker = string.Empty;
                return true;
            }
            TryRollbackLocked(rollback);
            blocker = string.IsNullOrWhiteSpace(readBlocker)
                ? "The durable quality checkpoint failed exact write/read-back validation."
                : readBlocker;
            return false;
        }
        catch (Exception exception)
        {
            TryRollbackLocked(rollback);
            blocker = $"The durable quality checkpoint write failed: {exception.Message}";
            return false;
        }
    }

    private void TryRollbackLocked(CharacterCreationQualitiesCheckpoint? rollback)
    {
        try
        {
            if (rollback is null)
                _backend.Remove();
            else
                _backend.Write(JsonSerializer.Serialize(rollback, JsonOptions));
        }
        catch
        {
            // Fail closed: the next read exposes the malformed or unexpected lock.
        }
    }

    private static CharacterCreationQualitiesCheckpoint WithDigest(
        CharacterCreationQualitiesCheckpoint checkpoint)
    {
        CharacterCreationQualitiesCheckpoint blank = checkpoint with { CheckpointDigest = string.Empty };
        return blank with
        {
            CheckpointDigest = CharacterCreationQualitiesCheckpoint.ComputeDigest(blank)
        };
    }
}
