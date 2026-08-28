using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public enum Sr5TableWizardTransactionPhase
{
    Reviewed,
    Applying,
    Applied
}

public enum Sr5TableWizardRecoveryObservation
{
    Original,
    Applied,
    Conflict
}

public sealed record Sr5TableWizardTransactionReceipt(
    string ContractName,
    Guid TransactionId,
    string IdempotencyKey,
    string WorkspaceId,
    long ExpectedWorkspaceRevision,
    long AppliedWorkspaceRevision,
    string ActionId,
    Sr5TableWizardActionKind ActionKind,
    string ActionDigest,
    string ExpectedPostconditionDigest,
    string ObservedPostconditionDigest,
    string ReceiptDigest)
{
    public const string CurrentContractName =
        "chummer.android.sr5-table-transaction-receipt/v1";

    public bool IsExact()
        => string.Equals(ContractName, CurrentContractName, StringComparison.Ordinal)
           && TransactionId != Guid.Empty
           && Sr5TableWizardTypedTransactionPresenter.IsDigest(IdempotencyKey)
           && !string.IsNullOrWhiteSpace(WorkspaceId)
           && ExpectedWorkspaceRevision > 0
           && ExpectedWorkspaceRevision < long.MaxValue
           && AppliedWorkspaceRevision == ExpectedWorkspaceRevision + 1
           && !string.IsNullOrWhiteSpace(ActionId)
           && Enum.IsDefined(ActionKind)
           && Sr5TableWizardTypedTransactionPresenter.IsDigest(ActionDigest)
           && Sr5TableWizardTypedTransactionPresenter.IsDigest(ExpectedPostconditionDigest)
           && string.Equals(
               ExpectedPostconditionDigest,
               ObservedPostconditionDigest,
               StringComparison.Ordinal)
           && Sr5TableWizardTypedTransactionPresenter.IsDigest(ReceiptDigest)
           && string.Equals(
               ReceiptDigest,
               Sr5TableWizardTypedTransactionPresenter.ComputeReceiptDigest(this),
               StringComparison.Ordinal);
}

/// <summary>
/// Durable orchestration for the renderer-neutral typed table presenter. It stores only the exact
/// reviewed quote and transition identity; rules and mutations remain owned by Core/Presentation.
/// </summary>
public sealed record Sr5TableWizardTransactionJournal(
    int SchemaVersion,
    long Version,
    Sr5TableWizardTransactionPhase Phase,
    Guid OwnerId,
    Guid TransactionId,
    string IdempotencyKey,
    Sr5TableWizardCheckpoint Review,
    Sr5TableWizardActionState Quote,
    string ExpectedPostconditionDigest,
    Sr5TableWizardTransactionReceipt? Receipt,
    string JournalDigest)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsExact()
        => Sr5TableWizardTypedTransactionPresenter.IsExact(this);
}

/// <summary>
/// Shared configure/quote/review transaction presenter for the typed Edge and direct-weapon
/// leaves. It never derives game rules: all quote facts come from <see cref="Sr5TableWizardSession"/>.
/// </summary>
public static class Sr5TableWizardTypedTransactionPresenter
{
    private const string JournalSchema = "chummer.android.sr5-table-transaction-journal/v1";
    private const string IdempotencySchema = "chummer.android.sr5-table-transaction-idempotency/v1";
    private const string PostconditionSchema = "chummer.android.sr5-table-transaction-postcondition/v1";
    private const string ReceiptSchema = "chummer.android.sr5-table-transaction-receipt-digest/v1";

    public static Sr5TableWizardTransactionJournal CreateReview(
        Sr5TableWizardSession session,
        Guid ownerId,
        Guid transactionId,
        long version = 1)
    {
        ArgumentNullException.ThrowIfNull(session);
        Sr5TableWizardState state = session.State;
        Sr5TableWizardActionState quote = state.SelectedAction
            ?? throw new InvalidOperationException("Quote one exact table action before review.");
        if (ownerId == Guid.Empty || transactionId == Guid.Empty || version <= 0)
            throw new InvalidOperationException("The table transaction identity is unavailable.");

        Sr5TableWizardCheckpoint review = session.CreateCheckpoint();
        string expectedPostcondition = ComputeExpectedPostconditionDigest(
            review.WorkspaceId,
            checked(review.WorkspaceRevision + 1),
            quote);
        string idempotencyKey = Hash(
            IdempotencySchema,
            ownerId.ToString("D"),
            transactionId.ToString("D"),
            review.WorkspaceId,
            review.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            review.SnapshotDigest,
            quote.Identity.ActionDigest,
            expectedPostcondition);
        var unsigned = new Sr5TableWizardTransactionJournal(
            Sr5TableWizardTransactionJournal.CurrentSchemaVersion,
            version,
            Sr5TableWizardTransactionPhase.Reviewed,
            ownerId,
            transactionId,
            idempotencyKey,
            review,
            quote,
            expectedPostcondition,
            Receipt: null,
            JournalDigest: string.Empty);
        Sr5TableWizardTransactionJournal signed = Sign(unsigned);
        return signed.IsExact()
            ? signed
            : throw new InvalidOperationException("The typed table review is incoherent.");
    }

    public static Sr5TableWizardTransactionJournal BeginApplying(
        Sr5TableWizardTransactionJournal reviewed)
    {
        if (!IsExact(reviewed) || reviewed.Phase != Sr5TableWizardTransactionPhase.Reviewed)
            throw new InvalidOperationException("Only the exact reviewed table quote can enter Applying.");
        return Sign(reviewed with
        {
            Version = checked(reviewed.Version + 1),
            Phase = Sr5TableWizardTransactionPhase.Applying,
            Receipt = null,
            JournalDigest = string.Empty
        });
    }

    public static Sr5TableWizardTransactionJournal ReturnToReview(
        Sr5TableWizardTransactionJournal applying)
    {
        if (!IsExact(applying) || applying.Phase != Sr5TableWizardTransactionPhase.Applying)
            throw new InvalidOperationException("Only the exact Applying table transaction can return to review.");
        return Sign(applying with
        {
            Version = checked(applying.Version + 1),
            Phase = Sr5TableWizardTransactionPhase.Reviewed,
            Receipt = null,
            JournalDigest = string.Empty
        });
    }

    public static Sr5TableWizardTransactionJournal Complete(
        Sr5TableWizardTransactionJournal applying,
        Sr5TableWizardSnapshot observed)
    {
        if (!IsExact(applying) || applying.Phase != Sr5TableWizardTransactionPhase.Applying)
            throw new InvalidOperationException("Only the exact Applying table transaction can complete.");
        Sr5TableWizardRecoveryObservation observation = Observe(
            applying,
            observed,
            out string observedPostcondition);
        if (observation != Sr5TableWizardRecoveryObservation.Applied)
            throw new InvalidOperationException("The exact reviewed table postcondition was not observed.");

        var unsignedReceipt = new Sr5TableWizardTransactionReceipt(
            Sr5TableWizardTransactionReceipt.CurrentContractName,
            applying.TransactionId,
            applying.IdempotencyKey,
            applying.Review.WorkspaceId,
            applying.Review.WorkspaceRevision,
            observed.WorkspaceRevision,
            applying.Quote.Identity.ActionId,
            applying.Quote.Identity.Kind,
            applying.Quote.Identity.ActionDigest,
            applying.ExpectedPostconditionDigest,
            observedPostcondition,
            ReceiptDigest: string.Empty);
        Sr5TableWizardTransactionReceipt receipt = unsignedReceipt with
        {
            ReceiptDigest = ComputeReceiptDigest(unsignedReceipt)
        };
        if (!receipt.IsExact())
            throw new InvalidOperationException("The table transaction receipt is incoherent.");

        return Sign(applying with
        {
            Version = checked(applying.Version + 1),
            Phase = Sr5TableWizardTransactionPhase.Applied,
            Receipt = receipt,
            JournalDigest = string.Empty
        });
    }

    public static Sr5TableWizardRecoveryObservation Observe(
        Sr5TableWizardTransactionJournal journal,
        Sr5TableWizardSnapshot observed,
        out string observedPostconditionDigest)
    {
        observedPostconditionDigest = string.Empty;
        if (!IsExact(journal))
            return Sr5TableWizardRecoveryObservation.Conflict;
        try
        {
            Sr5TableWizardProjector.ValidateSnapshot(observed);
        }
        catch (InvalidOperationException)
        {
            return Sr5TableWizardRecoveryObservation.Conflict;
        }
        if (!string.Equals(
                observed.WorkspaceId.Value,
                journal.Review.WorkspaceId,
                StringComparison.Ordinal)
            || observed.Lane != journal.Review.Lane)
        {
            return Sr5TableWizardRecoveryObservation.Conflict;
        }
        if (observed.WorkspaceRevision == journal.Review.WorkspaceRevision)
        {
            return string.Equals(
                    observed.SnapshotDigest,
                    journal.Review.SnapshotDigest,
                    StringComparison.Ordinal)
                ? Sr5TableWizardRecoveryObservation.Original
                : Sr5TableWizardRecoveryObservation.Conflict;
        }
        if (journal.Review.WorkspaceRevision == long.MaxValue
            || observed.WorkspaceRevision != journal.Review.WorkspaceRevision + 1
            || !TryProjectObservedQuote(journal.Quote, observed, out Sr5TableWizardActionState projected))
        {
            return Sr5TableWizardRecoveryObservation.Conflict;
        }

        observedPostconditionDigest = ComputeExpectedPostconditionDigest(
            journal.Review.WorkspaceId,
            observed.WorkspaceRevision,
            projected);
        return string.Equals(
                observedPostconditionDigest,
                journal.ExpectedPostconditionDigest,
                StringComparison.Ordinal)
            ? Sr5TableWizardRecoveryObservation.Applied
            : Sr5TableWizardRecoveryObservation.Conflict;
    }

    public static bool IsExact(Sr5TableWizardTransactionJournal? journal)
    {
        if (journal is null
            || journal.SchemaVersion != Sr5TableWizardTransactionJournal.CurrentSchemaVersion
            || journal.Version <= 0
            || !Enum.IsDefined(journal.Phase)
            || journal.OwnerId == Guid.Empty
            || journal.TransactionId == Guid.Empty
            || !IsDigest(journal.IdempotencyKey)
            || journal.Review is null
            || journal.Quote is null
            || journal.Quote.Identity != journal.Review.SelectedAction
            || !IsDigest(journal.ExpectedPostconditionDigest)
            || !IsDigest(journal.JournalDigest))
        {
            return false;
        }
        try
        {
            byte[] checkpoint = Sr5TableWizardSession.SerializeCheckpoint(journal.Review);
            CryptographicOperations.ZeroMemory(checkpoint);
            if (!string.Equals(
                    journal.ExpectedPostconditionDigest,
                    ComputeExpectedPostconditionDigest(
                        journal.Review.WorkspaceId,
                        checked(journal.Review.WorkspaceRevision + 1),
                        journal.Quote),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return false;
        }

        bool receiptMatches = journal.Phase == Sr5TableWizardTransactionPhase.Applied
            ? journal.Receipt is { } receipt
              && receipt.IsExact()
              && receipt.TransactionId == journal.TransactionId
              && string.Equals(receipt.IdempotencyKey, journal.IdempotencyKey, StringComparison.Ordinal)
              && string.Equals(receipt.ActionDigest, journal.Quote.Identity.ActionDigest, StringComparison.Ordinal)
              && string.Equals(
                  receipt.ExpectedPostconditionDigest,
                  journal.ExpectedPostconditionDigest,
                  StringComparison.Ordinal)
            : journal.Receipt is null;
        return receiptMatches
               && string.Equals(journal.JournalDigest, ComputeJournalDigest(journal), StringComparison.Ordinal);
    }

    internal static string ComputeReceiptDigest(Sr5TableWizardTransactionReceipt receipt)
        => Hash(
            ReceiptSchema,
            receipt.ContractName,
            receipt.TransactionId.ToString("D"),
            receipt.IdempotencyKey,
            receipt.WorkspaceId,
            receipt.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            receipt.AppliedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            receipt.ActionId,
            receipt.ActionKind.ToString(),
            receipt.ActionDigest,
            receipt.ExpectedPostconditionDigest,
            receipt.ObservedPostconditionDigest);

    internal static bool IsDigest(string? value)
        => value is { Length: 71 }
           && value.StartsWith("sha256:", StringComparison.Ordinal)
           && value.AsSpan(7).ToString().All(static character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Sr5TableWizardTransactionJournal Sign(Sr5TableWizardTransactionJournal journal)
        => journal with { JournalDigest = ComputeJournalDigest(journal) };

    private static string ComputeJournalDigest(Sr5TableWizardTransactionJournal journal)
    {
        byte[] reviewBytes = Sr5TableWizardSession.SerializeCheckpoint(journal.Review);
        byte[] quoteBytes = JsonSerializer.SerializeToUtf8Bytes(journal.Quote);
        try
        {
            return Hash(
                JournalSchema,
                journal.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                journal.Version.ToString(CultureInfo.InvariantCulture),
                journal.Phase.ToString(),
                journal.OwnerId.ToString("D"),
                journal.TransactionId.ToString("D"),
                journal.IdempotencyKey,
                HashBytes(reviewBytes),
                HashBytes(quoteBytes),
                journal.ExpectedPostconditionDigest,
                journal.Receipt?.ReceiptDigest ?? string.Empty);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(reviewBytes);
            CryptographicOperations.ZeroMemory(quoteBytes);
        }
    }

    private static string ComputeExpectedPostconditionDigest(
        string workspaceId,
        long appliedRevision,
        Sr5TableWizardActionState action)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)
            || appliedRevision <= 0
            || action?.Identity is null
            || !IsDigest(action.Identity.ActionDigest))
        {
            throw new InvalidOperationException("The typed table postcondition is incomplete.");
        }
        return action.Identity.Kind switch
        {
            Sr5TableWizardActionKind.SpendEdge or Sr5TableWizardActionKind.RegainEdge
                when action.WeaponPlan is null
                     && action.EdgeUsedBefore >= 0
                     && action.EdgeUsedAfter >= 0 => Hash(
                PostconditionSchema,
                workspaceId,
                appliedRevision.ToString(CultureInfo.InvariantCulture),
                action.Identity.Kind.ToString(),
                action.EdgeUsedAfter.ToString(CultureInfo.InvariantCulture)),
            Sr5TableWizardActionKind.FireWeapon when action.WeaponPlan is { } plan => Hash(
                PostconditionSchema,
                workspaceId,
                appliedRevision.ToString(CultureInfo.InvariantCulture),
                action.Identity.Kind.ToString(),
                action.Identity.WeaponId.ToString("D"),
                action.Identity.AmmoSlot.ToString(CultureInfo.InvariantCulture),
                (plan.DeleteAmmoGear ? Guid.Empty : action.Identity.AmmoGearId).ToString("D"),
                plan.NewAmmoRemaining.ToString(CultureInfo.InvariantCulture),
                plan.NewAmmoGearQuantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            _ => throw new InvalidOperationException("The typed table quote is incoherent.")
        };
    }

    private static bool TryProjectObservedQuote(
        Sr5TableWizardActionState reviewed,
        Sr5TableWizardSnapshot observed,
        out Sr5TableWizardActionState projected)
    {
        projected = reviewed;
        switch (reviewed.Identity.Kind)
        {
            case Sr5TableWizardActionKind.SpendEdge:
            case Sr5TableWizardActionKind.RegainEdge:
                if (observed.Edge.EdgeUsed != reviewed.EdgeUsedAfter)
                    return false;
                projected = reviewed with
                {
                    EdgeUsedBefore = reviewed.EdgeUsedAfter,
                    EdgeUsedAfter = observed.Edge.EdgeUsed
                };
                return true;
            case Sr5TableWizardActionKind.FireWeapon when reviewed.WeaponPlan is { } plan:
            {
                Guid expectedAmmoGear = plan.DeleteAmmoGear
                    ? Guid.Empty
                    : reviewed.Identity.AmmoGearId;
                CareerWeaponFireEditorState[] matches = observed.Weapons
                    .Where(candidate =>
                        candidate.Weapon.Identity.WeaponId == reviewed.Identity.WeaponId
                        && candidate.Weapon.Identity.AmmoSlot == reviewed.Identity.AmmoSlot
                        && candidate.Weapon.Identity.AmmoGearId == expectedAmmoGear)
                    .Take(2)
                    .ToArray();
                if (matches.Length != 1
                    || matches[0].Weapon.AmmoRemaining != plan.NewAmmoRemaining
                    || matches[0].Weapon.AmmoGearQuantity != plan.NewAmmoGearQuantity)
                {
                    return false;
                }
                return true;
            }
            default:
                return false;
        }
    }

    private static string Hash(params string[] values)
    {
        var canonical = new StringBuilder();
        foreach (string value in values)
            canonical.Append(value.Length).Append(':').Append(value).Append(';');
        return HashBytes(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private static string HashBytes(ReadOnlySpan<byte> value)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(value));
}

public sealed class Sr5TableWizardTransactionStore
{
    private const int MaximumPayloadBytes = 64 * 1024;
    private const int MaximumEncodedCharacters = 96 * 1024;
    private readonly ISr5TableWizardCheckpointBackend _backend;
    private static readonly object ProcessSync = new();

    public Sr5TableWizardTransactionStore(ISr5TableWizardCheckpointBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Sr5TableWizardCheckpointReadStatus TryRead(
        out Sr5TableWizardTransactionJournal? journal)
    {
        lock (ProcessSync)
        {
            journal = null;
            string encoded;
            try
            {
                encoded = _backend.Read();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return Sr5TableWizardCheckpointReadStatus.Unavailable;
            }
            if (string.IsNullOrEmpty(encoded))
                return Sr5TableWizardCheckpointReadStatus.Empty;
            if (encoded.Length > MaximumEncodedCharacters)
                return InvalidAndRemove();
            try
            {
                byte[] payload = Convert.FromBase64String(encoded);
                try
                {
                    if (payload.Length is 0 or > MaximumPayloadBytes
                        || !string.Equals(Convert.ToBase64String(payload), encoded, StringComparison.Ordinal))
                    {
                        return InvalidAndRemove();
                    }
                    journal = JsonSerializer.Deserialize<Sr5TableWizardTransactionJournal>(payload);
                    if (journal is null || !journal.IsExact())
                    {
                        journal = null;
                        return InvalidAndRemove();
                    }
                    return Sr5TableWizardCheckpointReadStatus.Ready;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(payload);
                }
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                journal = null;
                return InvalidAndRemove();
            }
        }
    }

    public bool TryWriteReview(
        Sr5TableWizardSession session,
        Guid ownerId,
        Guid transactionId,
        out Sr5TableWizardTransactionJournal? review)
    {
        lock (ProcessSync)
        {
            long version = 1;
            Sr5TableWizardCheckpointReadStatus status = TryReadLocked(out Sr5TableWizardTransactionJournal? current);
            if (status == Sr5TableWizardCheckpointReadStatus.Unavailable
                || status == Sr5TableWizardCheckpointReadStatus.Invalid
                || current is { Phase: Sr5TableWizardTransactionPhase.Applying or Sr5TableWizardTransactionPhase.Applied })
            {
                review = null;
                return false;
            }
            if (current is not null)
                version = checked(current.Version + 1);
            try
            {
                review = Sr5TableWizardTypedTransactionPresenter.CreateReview(
                    session,
                    ownerId,
                    transactionId,
                    version);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
            {
                review = null;
                return false;
            }
            return TryWriteLocked(review);
        }
    }

    public bool TryBeginApplying(
        Sr5TableWizardTransactionJournal expected,
        out Sr5TableWizardTransactionJournal? applying)
        => TryTransition(
            expected,
            Sr5TableWizardTransactionPhase.Reviewed,
            Sr5TableWizardTypedTransactionPresenter.BeginApplying,
            out applying);

    public bool TryReturnToReview(
        Sr5TableWizardTransactionJournal expected,
        out Sr5TableWizardTransactionJournal? review)
        => TryTransition(
            expected,
            Sr5TableWizardTransactionPhase.Applying,
            Sr5TableWizardTypedTransactionPresenter.ReturnToReview,
            out review);

    public bool TryComplete(
        Sr5TableWizardTransactionJournal expected,
        Sr5TableWizardSnapshot observed,
        out Sr5TableWizardTransactionJournal? applied)
    {
        ArgumentNullException.ThrowIfNull(observed);
        return TryTransition(
            expected,
            Sr5TableWizardTransactionPhase.Applying,
            value => Sr5TableWizardTypedTransactionPresenter.Complete(value, observed),
            out applied);
    }

    public bool TryClearApplied(Sr5TableWizardTransactionJournal expected)
        => TryClear(expected, Sr5TableWizardTransactionPhase.Applied);

    public bool TryDiscardReview(Sr5TableWizardTransactionJournal expected)
        => TryClear(expected, Sr5TableWizardTransactionPhase.Reviewed);

    private bool TryClear(
        Sr5TableWizardTransactionJournal expected,
        Sr5TableWizardTransactionPhase requiredPhase)
    {
        lock (ProcessSync)
        {
            if (!expected.IsExact()
                || expected.Phase != requiredPhase
                || TryReadLocked(out Sr5TableWizardTransactionJournal? current)
                    != Sr5TableWizardCheckpointReadStatus.Ready
                || current != expected)
            {
                return false;
            }
            try
            {
                _backend.Remove();
                return string.IsNullOrEmpty(_backend.Read());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
            }
        }
    }

    private bool TryTransition(
        Sr5TableWizardTransactionJournal expected,
        Sr5TableWizardTransactionPhase phase,
        Func<Sr5TableWizardTransactionJournal, Sr5TableWizardTransactionJournal> transition,
        out Sr5TableWizardTransactionJournal? result)
    {
        lock (ProcessSync)
        {
            result = null;
            if (!expected.IsExact()
                || expected.Phase != phase
                || TryReadLocked(out Sr5TableWizardTransactionJournal? current)
                    != Sr5TableWizardCheckpointReadStatus.Ready
                || current != expected)
            {
                return false;
            }
            try
            {
                result = transition(expected);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
            {
                return false;
            }
            return TryWriteLocked(result);
        }
    }

    private Sr5TableWizardCheckpointReadStatus TryReadLocked(
        out Sr5TableWizardTransactionJournal? journal)
    {
        journal = null;
        string encoded;
        try
        {
            encoded = _backend.Read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Sr5TableWizardCheckpointReadStatus.Unavailable;
        }
        if (string.IsNullOrEmpty(encoded))
            return Sr5TableWizardCheckpointReadStatus.Empty;
        if (encoded.Length > MaximumEncodedCharacters)
            return InvalidAndRemove();
        try
        {
            byte[] payload = Convert.FromBase64String(encoded);
            try
            {
                if (payload.Length is 0 or > MaximumPayloadBytes
                    || !string.Equals(Convert.ToBase64String(payload), encoded, StringComparison.Ordinal))
                {
                    return InvalidAndRemove();
                }
                journal = JsonSerializer.Deserialize<Sr5TableWizardTransactionJournal>(payload);
                if (journal is null || !journal.IsExact())
                {
                    journal = null;
                    return InvalidAndRemove();
                }
                return Sr5TableWizardCheckpointReadStatus.Ready;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return InvalidAndRemove();
        }
    }

    private bool TryWriteLocked(Sr5TableWizardTransactionJournal journal)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(journal);
        try
        {
            if (payload.Length is 0 or > MaximumPayloadBytes)
                return false;
            string encoded = Convert.ToBase64String(payload);
            if (encoded.Length > MaximumEncodedCharacters)
                return false;
            try
            {
                _backend.Write(encoded);
                if (!string.Equals(_backend.Read(), encoded, StringComparison.Ordinal))
                    return false;
                return TryReadLocked(out Sr5TableWizardTransactionJournal? readBack)
                           == Sr5TableWizardCheckpointReadStatus.Ready
                       && readBack == journal;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private Sr5TableWizardCheckpointReadStatus InvalidAndRemove()
    {
        try
        {
            _backend.Remove();
            return Sr5TableWizardCheckpointReadStatus.Invalid;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Sr5TableWizardCheckpointReadStatus.Unavailable;
        }
    }
}
