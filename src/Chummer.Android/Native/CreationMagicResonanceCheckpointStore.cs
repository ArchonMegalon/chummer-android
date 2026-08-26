using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

public enum CharacterCreationMagicResonanceCheckpointPhase
{
    Reviewed,
    Confirming,
    Confirmed
}

/// <summary>
/// Durable journal for one exact typed Presentation review. Confirming is written before Core's
/// atomic auxiliary-state mutation so process death can replay only the identical idempotent
/// command. A malformed journal is a lock, never an empty store.
/// </summary>
public sealed record CharacterCreationMagicResonanceCheckpoint(
    string Schema,
    long Version,
    CharacterCreationMagicResonanceCheckpointPhase Phase,
    CharacterCreationMagicResonanceReview Review,
    string IdempotencyKey,
    CharacterCreationMagicResonanceConfirmation? Confirmation,
    string CheckpointDigest)
{
    public const string CurrentSchema =
        "chummer.android.sr5-priority-magic-resonance-checkpoint.v1";

    public static CharacterCreationMagicResonanceCheckpoint CreateReviewed(
        CharacterCreationMagicResonanceReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!review.Preview.CanConfirm || review.Preview.Blockers.Count != 0)
            throw new InvalidOperationException(
                "Only a blocker-free Core Magic/Resonance review can be checkpointed.");
        string idempotencyKey =
            CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
        var candidate = new CharacterCreationMagicResonanceCheckpoint(
            CurrentSchema,
            Version: 1,
            CharacterCreationMagicResonanceCheckpointPhase.Reviewed,
            review,
            idempotencyKey,
            Confirmation: null,
            CheckpointDigest: string.Empty);
        return WithDigest(candidate);
    }

    public bool IsStructurallyValid()
    {
        if (!string.Equals(Schema, CurrentSchema, StringComparison.Ordinal)
            || Version <= 0
            || Review is null
            || string.IsNullOrWhiteSpace(IdempotencyKey)
            || IdempotencyKey.Length > 200
            || !string.Equals(IdempotencyKey, IdempotencyKey.Trim(), StringComparison.Ordinal)
            || !string.Equals(
                IdempotencyKey,
                CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(Review),
                StringComparison.Ordinal)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(CheckpointDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                CheckpointDigest,
                ComputeDigest(this))
            || !ReviewShapeIsValid(Review))
        {
            return false;
        }

        return Phase switch
        {
            CharacterCreationMagicResonanceCheckpointPhase.Reviewed =>
                Confirmation is null,
            CharacterCreationMagicResonanceCheckpointPhase.Confirming =>
                Confirmation is null,
            CharacterCreationMagicResonanceCheckpointPhase.Confirmed =>
                Confirmation is not null
                && CreationMagicResonancePhoneAuthority.ConfirmationMatches(
                    Review,
                    IdempotencyKey,
                    Confirmation),
            _ => false
        };
    }

    public bool OwnsExactReview(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterOverviewState overview)
        => Phase == CharacterCreationMagicResonanceCheckpointPhase.Reviewed
           && IsStructurallyValid()
           && CreationMagicResonancePhoneAuthority.IsReady(
               overview.CreationMagicResonance,
               editor,
               overview)
           && CreationMagicResonancePhoneAuthority.BindingEquals(
               Review.Draft.ExpectedBinding,
               editor.Binding)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
               Review.Draft.ExpectedCoreSnapshotDigest,
               editor.CoreSnapshotDigest)
           && CreationMagicResonancePhoneAuthority.ReviewMatches(
               editor,
               Review,
               requireConfirmable: true);

    public bool OwnsRecoveryRevision(CharacterOverviewState overview)
    {
        if (Phase is not (CharacterCreationMagicResonanceCheckpointPhase.Confirming
                or CharacterCreationMagicResonanceCheckpointPhase.Confirmed)
            || !IsStructurallyValid()
            || overview.Profile?.Created != false
            || overview.WorkspaceId != Review.Draft.ExpectedBinding.WorkspaceId
            || overview.IsDirty
            || !string.IsNullOrWhiteSpace(overview.Error))
        {
            return false;
        }

        long expected = Review.Draft.ExpectedBinding.ContentRevision;
        if (Phase == CharacterCreationMagicResonanceCheckpointPhase.Confirming)
        {
            return overview.ContentRevision == expected
                       && overview.SavedRevision == expected
                   || expected < long.MaxValue
                   && overview.ContentRevision == expected + 1
                   && overview.SavedRevision == expected + 1;
        }
        return Confirmation is { } confirmation
               && CreationMagicResonancePhoneAuthority.OverviewMatchesReceipt(
                   overview,
                   confirmation.Receipt);
    }

    public static string ComputeDigest(
        CharacterCreationMagicResonanceCheckpoint checkpoint)
        => CharacterCreationMagicResonanceDigest.Compute(
            checkpoint with { CheckpointDigest = string.Empty });

    internal static CharacterCreationMagicResonanceCheckpoint WithDigest(
        CharacterCreationMagicResonanceCheckpoint checkpoint)
    {
        CharacterCreationMagicResonanceCheckpoint blank = checkpoint with
        {
            CheckpointDigest = string.Empty
        };
        return blank with { CheckpointDigest = ComputeDigest(blank) };
    }

    private static bool ReviewShapeIsValid(
        CharacterCreationMagicResonanceReview review)
    {
        CharacterCreationMagicResonancePreview preview = review.Preview;
        CharacterCreationMagicResonanceDesktopDraft draft = review.Draft;
        return draft is not null
               && preview is not null
               && CreationMagicResonancePhoneAuthority.BindingEquals(
                   draft.ExpectedBinding,
                   preview.Binding)
               && CharacterCreationMagicResonanceDigest.IsCanonical(
                   draft.ExpectedCoreSnapshotDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   CharacterCreationMagicResonanceDigest.Compute(
                       draft.Selections),
                   CharacterCreationMagicResonanceDigest.Compute(
                       preview.Selections))
               && string.Equals(
                   preview.Schema,
                   CharacterCreationMagicResonanceSchemas.PreviewV1,
                   StringComparison.Ordinal)
               && preview.RequiresExplicitConfirmation
               && preview.CanConfirm
               && preview.Blockers.Count == 0
               && CharacterCreationMagicResonanceDigest.IsCanonical(
                   preview.PreviewDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   preview.PreviewDigest,
                   CharacterCreationMagicResonanceDigest.Compute(
                       preview with { PreviewDigest = string.Empty }))
               && new[]
               {
                   preview.TraditionBudget,
                   preview.StreamBudget,
                   preview.AdeptPowerPointBudget,
                   preview.SpellBudget,
                   preview.ComplexFormBudget
               }.All(static budget =>
                   budget.Total >= 0m
                   && budget.Used >= 0m
                   && budget.Used <= budget.Total
                   && budget.Remaining == budget.Total - budget.Used
                   && budget.Remaining == 0m
                   && budget.Blockers.Count == 0)
               && preview.SourceAnchorIds.Count > 0
               && preview.SourceAnchorIds.All(static anchor =>
                   !string.IsNullOrWhiteSpace(anchor));
    }
}

public sealed record CharacterCreationMagicResonanceCheckpointCas(
    long Version,
    CharacterCreationMagicResonanceCheckpointPhase Phase,
    string CheckpointDigest,
    string WorkspaceId,
    string IdempotencyKey)
{
    public static CharacterCreationMagicResonanceCheckpointCas From(
        CharacterCreationMagicResonanceCheckpoint checkpoint)
        => new(
            checkpoint.Version,
            checkpoint.Phase,
            checkpoint.CheckpointDigest,
            checkpoint.Review.Draft.ExpectedBinding.WorkspaceId.Value,
            checkpoint.IdempotencyKey);

    public bool Matches(CharacterCreationMagicResonanceCheckpoint checkpoint)
        => Version == checkpoint.Version
           && Phase == checkpoint.Phase
           && string.Equals(
               CheckpointDigest,
               checkpoint.CheckpointDigest,
               StringComparison.Ordinal)
           && string.Equals(
               WorkspaceId,
               checkpoint.Review.Draft.ExpectedBinding.WorkspaceId.Value,
               StringComparison.Ordinal)
           && string.Equals(
               IdempotencyKey,
               checkpoint.IdempotencyKey,
               StringComparison.Ordinal);
}

internal interface ICharacterCreationMagicResonanceCheckpointBackend
{
    string Read();
    void Write(string payload);
    void Remove();
}

internal sealed class PreferencesCharacterCreationMagicResonanceCheckpointBackend :
    ICharacterCreationMagicResonanceCheckpointBackend
{
    private const string StorageKey =
        "sr5.priority.creation.magic-resonance.checkpoint.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

/// <summary>
/// Single-lane compare-and-swap store. Writes must survive exact read-back; malformed existing
/// data blocks every create/replay path until support recovery rather than being overwritten.
/// </summary>
public sealed class CharacterCreationMagicResonanceCheckpointStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private readonly ICharacterCreationMagicResonanceCheckpointBackend _backend;

    internal CharacterCreationMagicResonanceCheckpointStore(
        ICharacterCreationMagicResonanceCheckpointBackend backend)
        => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    internal static CharacterCreationMagicResonanceCheckpointStore CreateDefault()
        => new(new PreferencesCharacterCreationMagicResonanceCheckpointBackend());

    public bool TryRead(
        out CharacterCreationMagicResonanceCheckpoint checkpoint,
        out string blocker)
    {
        lock (Gate)
            return TryReadLocked(out checkpoint, out blocker);
    }

    public bool TryCreate(
        CharacterCreationMagicResonanceCheckpoint checkpoint,
        out CharacterCreationMagicResonanceCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        lock (Gate)
        {
            stored = null!;
            if (!checkpoint.IsStructurallyValid()
                || checkpoint.Version != 1
                || checkpoint.Phase !=
                CharacterCreationMagicResonanceCheckpointPhase.Reviewed)
            {
                blocker = "Only one exact version-1 Reviewed Magic/Resonance checkpoint may be created.";
                return false;
            }
            if (TryReadLocked(
                    out CharacterCreationMagicResonanceCheckpoint existing,
                    out string readBlocker))
            {
                blocker = CharacterCreationMagicResonanceCheckpointCas.From(existing)
                    .Matches(checkpoint)
                    ? "This exact Magic/Resonance review is already durable. Resume it."
                    : "Another workspace, revision or Magic/Resonance review owns the durable checkpoint.";
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

    public bool TryBeginConfirm(
        CharacterCreationMagicResonanceCheckpointCas expected,
        out CharacterCreationMagicResonanceCheckpoint stored,
        out string blocker)
        => TryTransition(
            expected,
            CharacterCreationMagicResonanceCheckpointPhase.Reviewed,
            CharacterCreationMagicResonanceCheckpointPhase.Confirming,
            confirmation: null,
            out stored,
            out blocker);

    public bool TryReturnToReviewed(
        CharacterCreationMagicResonanceCheckpointCas expected,
        out CharacterCreationMagicResonanceCheckpoint stored,
        out string blocker)
        => TryTransition(
            expected,
            CharacterCreationMagicResonanceCheckpointPhase.Confirming,
            CharacterCreationMagicResonanceCheckpointPhase.Reviewed,
            confirmation: null,
            out stored,
            out blocker);

    public bool TryRecordConfirmed(
        CharacterCreationMagicResonanceCheckpointCas expected,
        CharacterCreationMagicResonanceConfirmation confirmation,
        out CharacterCreationMagicResonanceCheckpoint stored,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        lock (Gate)
        {
            stored = null!;
            blocker = string.Empty;
            if (expected.Phase !=
                    CharacterCreationMagicResonanceCheckpointPhase.Confirming
                || !TryRequireCasLocked(
                    expected,
                    out CharacterCreationMagicResonanceCheckpoint current,
                    out blocker)
                || current.Version == long.MaxValue
                || !CreationMagicResonancePhoneAuthority.ConfirmationMatches(
                    current.Review,
                    current.IdempotencyKey,
                    confirmation))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only the exact Core/Presentation confirmation may close this checkpoint."
                    : blocker;
                return false;
            }
            CharacterCreationMagicResonanceCheckpoint next =
                CharacterCreationMagicResonanceCheckpoint.WithDigest(current with
                {
                    Version = checked(current.Version + 1),
                    Phase = CharacterCreationMagicResonanceCheckpointPhase.Confirmed,
                    Confirmation = confirmation
                });
            return TryWriteAndReadBackLocked(
                next,
                current,
                out stored,
                out blocker);
        }
    }

    public bool TryDeleteReviewed(
        CharacterCreationMagicResonanceCheckpointCas expected,
        out string blocker)
        => TryRemove(
            expected,
            CharacterCreationMagicResonanceCheckpointPhase.Reviewed,
            out blocker);

    public bool TryAcknowledgeConfirmed(
        CharacterCreationMagicResonanceCheckpointCas expected,
        out string blocker)
        => TryRemove(
            expected,
            CharacterCreationMagicResonanceCheckpointPhase.Confirmed,
            out blocker);

    private bool TryTransition(
        CharacterCreationMagicResonanceCheckpointCas expected,
        CharacterCreationMagicResonanceCheckpointPhase required,
        CharacterCreationMagicResonanceCheckpointPhase nextPhase,
        CharacterCreationMagicResonanceConfirmation? confirmation,
        out CharacterCreationMagicResonanceCheckpoint stored,
        out string blocker)
    {
        lock (Gate)
        {
            stored = null!;
            blocker = string.Empty;
            if (expected.Phase != required
                || !TryRequireCasLocked(
                    expected,
                    out CharacterCreationMagicResonanceCheckpoint current,
                    out blocker)
                || current.Version == long.MaxValue)
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "The durable Magic/Resonance checkpoint phase cannot advance."
                    : blocker;
                return false;
            }
            CharacterCreationMagicResonanceCheckpoint next =
                CharacterCreationMagicResonanceCheckpoint.WithDigest(current with
                {
                    Version = checked(current.Version + 1),
                    Phase = nextPhase,
                    Confirmation = confirmation
                });
            return TryWriteAndReadBackLocked(
                next,
                current,
                out stored,
                out blocker);
        }
    }

    private bool TryRemove(
        CharacterCreationMagicResonanceCheckpointCas expected,
        CharacterCreationMagicResonanceCheckpointPhase required,
        out string blocker)
    {
        lock (Gate)
        {
            blocker = string.Empty;
            if (expected.Phase != required
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
                    return true;
                blocker = "The durable Magic/Resonance checkpoint was not removed on read-back.";
                return false;
            }
            catch (Exception exception)
            {
                blocker = $"The durable Magic/Resonance checkpoint could not be removed: {exception.Message}";
                return false;
            }
        }
    }

    private bool TryRequireCasLocked(
        CharacterCreationMagicResonanceCheckpointCas expected,
        out CharacterCreationMagicResonanceCheckpoint current,
        out string blocker)
    {
        if (!TryReadLocked(out current, out blocker))
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "No durable Magic/Resonance checkpoint exists."
                : blocker;
            return false;
        }
        if (!expected.Matches(current))
        {
            blocker = "The durable Magic/Resonance checkpoint changed; the stale action was rejected.";
            return false;
        }
        return true;
    }

    private bool TryReadLocked(
        out CharacterCreationMagicResonanceCheckpoint checkpoint,
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
            checkpoint = JsonSerializer.Deserialize<
                CharacterCreationMagicResonanceCheckpoint>(payload, JsonOptions)!;
            if (checkpoint is null || !checkpoint.IsStructurallyValid())
            {
                checkpoint = null!;
                blocker = "A malformed Magic/Resonance checkpoint blocks replay; support recovery is required.";
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            blocker = $"The durable Magic/Resonance checkpoint is unreadable and blocks replay: {exception.Message}";
            return false;
        }
    }

    private bool TryWriteAndReadBackLocked(
        CharacterCreationMagicResonanceCheckpoint candidate,
        CharacterCreationMagicResonanceCheckpoint? rollback,
        out CharacterCreationMagicResonanceCheckpoint stored,
        out string blocker)
    {
        stored = null!;
        try
        {
            _backend.Write(JsonSerializer.Serialize(candidate, JsonOptions));
            if (TryReadLocked(
                    out CharacterCreationMagicResonanceCheckpoint readBack,
                    out string readBlocker)
                && CharacterCreationMagicResonanceCheckpointCas.From(readBack)
                    .Matches(candidate))
            {
                stored = readBack;
                blocker = string.Empty;
                return true;
            }
            TryRollbackLocked(rollback);
            blocker = string.IsNullOrWhiteSpace(readBlocker)
                ? "The durable Magic/Resonance checkpoint failed exact write/read-back validation."
                : readBlocker;
            return false;
        }
        catch (Exception exception)
        {
            TryRollbackLocked(rollback);
            blocker = $"The durable Magic/Resonance checkpoint write failed: {exception.Message}";
            return false;
        }
    }

    private void TryRollbackLocked(
        CharacterCreationMagicResonanceCheckpoint? rollback)
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
}
