using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

public sealed record Sr5DowntimeCalendarWorkspaceAuthority(
    string WorkspaceId,
    long WorkspaceRevision,
    long SavedRevision,
    string PayloadSha256,
    string DocumentSha256);

public static class Sr5DowntimeCalendarPhoneProjection
{
    private const string ContentSchema = "chummer.android.sr5-downtime-calendar.content.v1";

    public static Sr5CareerWizardBinding CreateBinding(
        Sr5DowntimeCalendarWorkspaceAuthority workspace,
        CareerCalendarEditorState editor)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(editor);
        if (string.IsNullOrWhiteSpace(workspace.WorkspaceId)
            || workspace.WorkspaceRevision <= 0
            || workspace.SavedRevision != workspace.WorkspaceRevision
            || !IsRawDigest(workspace.PayloadSha256)
            || !IsRawDigest(workspace.DocumentSha256)
            || !string.Equals(
                workspace.WorkspaceId,
                editor.WorkspaceId.Value,
                StringComparison.Ordinal)
            || workspace.WorkspaceRevision != editor.ContentRevision)
        {
            throw new InvalidOperationException(
                "Downtime Calendar requires exact matching saved workspace and typed editor authority.");
        }

        var content = new StringBuilder(ContentSchema);
        Append(content, workspace.PayloadSha256);
        Append(content, workspace.DocumentSha256);
        return new Sr5CareerWizardBinding(
            workspace.WorkspaceId,
            workspace.WorkspaceRevision,
            workspace.SavedRevision,
            "sr5",
            Sr5DowntimeCalendarDesktopSession.RuntimeFingerprint,
            "sha256:" + editor.SourceAuthorityDigest,
            Hash(content.ToString()));
    }

    internal static string Hash(string value)
        => "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append(';');

    internal static bool IsDigest(string? value)
        => value is { Length: 71 }
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && IsRawDigest(value["sha256:".Length..]);

    internal static bool IsRawDigest(string? value)
        => value is { Length: 64 }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public enum Sr5DowntimeCalendarJournalPhase
{
    Review,
    Applying,
    Applied
}

public sealed record Sr5DowntimeCalendarPersistenceReceipt(
    string ContractName,
    string WorkspaceId,
    long ExpectedWorkspaceRevision,
    long AppliedWorkspaceRevision,
    Guid ActionId,
    Sr5DowntimeCalendarOperation Operation,
    string PreviewDigest,
    string ExpectedPostconditionDigest,
    string ObservedPostconditionDigest,
    string CalendarRevisionAfter,
    string SourceDigestAfter,
    string ContentDigestAfter,
    string ReceiptDigest)
{
    public const string CurrentContractName =
        "chummer.android.sr5-downtime-calendar.persistence-receipt/v1";

    public bool IsExact()
        => string.Equals(ContractName, CurrentContractName, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(WorkspaceId)
            && ExpectedWorkspaceRevision > 0
            && ExpectedWorkspaceRevision < long.MaxValue
            && AppliedWorkspaceRevision == ExpectedWorkspaceRevision + 1
            && ActionId != Guid.Empty
            && Enum.IsDefined(Operation)
            && Sr5DowntimeCalendarPhoneProjection.IsDigest(PreviewDigest)
            && Sr5DowntimeCalendarPhoneProjection.IsDigest(ExpectedPostconditionDigest)
            && string.Equals(
                ExpectedPostconditionDigest,
                ObservedPostconditionDigest,
                StringComparison.Ordinal)
            && Sr5DowntimeCalendarPhoneProjection.IsRawDigest(CalendarRevisionAfter)
            && Sr5DowntimeCalendarPhoneProjection.IsDigest(SourceDigestAfter)
            && Sr5DowntimeCalendarPhoneProjection.IsDigest(ContentDigestAfter)
            && Sr5DowntimeCalendarPhoneProjection.IsDigest(ReceiptDigest)
            && string.Equals(ReceiptDigest, ComputeDigest(this), StringComparison.Ordinal);

    public static Sr5DowntimeCalendarPersistenceReceipt Create(
        Sr5DowntimeCalendarJournal applying,
        Sr5CareerWizardBinding observedBinding,
        CareerCalendarEditorState observedEditor)
    {
        ArgumentNullException.ThrowIfNull(applying);
        ArgumentNullException.ThrowIfNull(observedBinding);
        ArgumentNullException.ThrowIfNull(observedEditor);
        try
        {
            _ = new Sr5DowntimeCalendarDesktopSession().Bind(
                observedBinding,
                observedEditor);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "The observed Downtime Calendar binding is not exact.",
                exception);
        }
        if (!applying.IsExact()
            || applying.Phase != Sr5DowntimeCalendarJournalPhase.Applying
            || applying.Review.Preview.WeekId != applying.ActionId
            || !string.Equals(
                observedBinding.WorkspaceId,
                applying.Review.WorkspaceId,
                StringComparison.Ordinal)
            || observedBinding.WorkspaceRevision != applying.Review.WorkspaceRevision + 1
            || observedBinding.SavedRevision != observedBinding.WorkspaceRevision
            || observedEditor.ContentRevision != observedBinding.WorkspaceRevision)
        {
            throw new InvalidOperationException(
                "The observed Downtime Calendar state does not match the Applying journal.");
        }
        string observed = Sr5DowntimeCalendarDesktopSession.ComputeObservedCalendarDigest(
            observedEditor);
        if (!string.Equals(
            applying.ExpectedPostconditionDigest,
            observed,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The saved Downtime Calendar does not satisfy the exact reviewed postcondition.");
        }

        var unsigned = new Sr5DowntimeCalendarPersistenceReceipt(
            CurrentContractName,
            applying.Review.WorkspaceId,
            applying.Review.WorkspaceRevision,
            observedBinding.WorkspaceRevision,
            applying.ActionId,
            applying.Review.Preview.Operation,
            applying.Review.Preview.PreviewDigest,
            applying.ExpectedPostconditionDigest,
            observed,
            observedEditor.CalendarRevision,
            observedBinding.SourceDigest,
            observedBinding.ContentDigest,
            string.Empty);
        return unsigned with { ReceiptDigest = ComputeDigest(unsigned) };
    }

    private static string ComputeDigest(Sr5DowntimeCalendarPersistenceReceipt receipt)
    {
        var material = new StringBuilder(receipt.ContractName);
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.WorkspaceId);
        Sr5DowntimeCalendarPhoneProjection.Append(
            material,
            receipt.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture));
        Sr5DowntimeCalendarPhoneProjection.Append(
            material,
            receipt.AppliedWorkspaceRevision.ToString(CultureInfo.InvariantCulture));
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.ActionId.ToString("D"));
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.Operation.ToString());
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.PreviewDigest);
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.ExpectedPostconditionDigest);
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.ObservedPostconditionDigest);
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.CalendarRevisionAfter);
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.SourceDigestAfter);
        Sr5DowntimeCalendarPhoneProjection.Append(material, receipt.ContentDigestAfter);
        return Sr5DowntimeCalendarPhoneProjection.Hash(material.ToString());
    }
}

public sealed record Sr5DowntimeCalendarJournal(
    int SchemaVersion,
    long Version,
    Sr5DowntimeCalendarJournalPhase Phase,
    Guid OwnerId,
    Guid ActionId,
    Sr5DowntimeCalendarCheckpoint Review,
    string ExpectedPostconditionDigest,
    Sr5DowntimeCalendarPersistenceReceipt? Receipt,
    string JournalDigest)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsExact()
    {
        if (SchemaVersion != CurrentSchemaVersion
            || Version <= 0
            || !Enum.IsDefined(Phase)
            || OwnerId == Guid.Empty
            || ActionId == Guid.Empty
            || Review is null
            || Review.Preview is null
            || ActionId != Review.Preview.WeekId
            || !Sr5DowntimeCalendarPhoneProjection.IsDigest(ExpectedPostconditionDigest)
            || !Sr5DowntimeCalendarPhoneProjection.IsDigest(JournalDigest))
        {
            return false;
        }
        try
        {
            byte[] payload = Sr5DowntimeCalendarDesktopSession.SerializeCheckpoint(Review);
            CryptographicOperations.ZeroMemory(payload);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        return (Phase == Sr5DowntimeCalendarJournalPhase.Applied
                ? Receipt is not null
                    && Receipt.IsExact()
                    && Receipt.ActionId == ActionId
                    && Receipt.Operation == Review.Preview.Operation
                    && string.Equals(Receipt.WorkspaceId, Review.WorkspaceId, StringComparison.Ordinal)
                    && Receipt.ExpectedWorkspaceRevision == Review.WorkspaceRevision
                    && Receipt.AppliedWorkspaceRevision == Review.WorkspaceRevision + 1
                    && string.Equals(
                        Receipt.PreviewDigest,
                        Review.Preview.PreviewDigest,
                        StringComparison.Ordinal)
                    && string.Equals(
                        Receipt.ExpectedPostconditionDigest,
                        ExpectedPostconditionDigest,
                        StringComparison.Ordinal)
                : Receipt is null)
            && (Phase == Sr5DowntimeCalendarJournalPhase.Review || Version >= 2)
            && string.Equals(JournalDigest, ComputeDigest(this), StringComparison.Ordinal);
    }

    public static Sr5DowntimeCalendarJournal CreateReview(
        long version,
        Guid ownerId,
        Sr5DowntimeCalendarCheckpoint review,
        string expectedPostconditionDigest)
        => Sign(new Sr5DowntimeCalendarJournal(
            CurrentSchemaVersion,
            version,
            Sr5DowntimeCalendarJournalPhase.Review,
            ownerId,
            review.Preview.WeekId,
            review,
            expectedPostconditionDigest,
            Receipt: null,
            JournalDigest: string.Empty));

    internal static Sr5DowntimeCalendarJournal Sign(Sr5DowntimeCalendarJournal journal)
        => journal with { JournalDigest = ComputeDigest(journal) };

    private static string ComputeDigest(Sr5DowntimeCalendarJournal journal)
    {
        var material = new StringBuilder("chummer.android.sr5-downtime-calendar.journal.v1");
        Sr5DowntimeCalendarPhoneProjection.Append(
            material,
            journal.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        Sr5DowntimeCalendarPhoneProjection.Append(
            material,
            journal.Version.ToString(CultureInfo.InvariantCulture));
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Phase.ToString());
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.OwnerId.ToString("D"));
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.ActionId.ToString("D"));
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.WorkspaceId);
        Sr5DowntimeCalendarPhoneProjection.Append(
            material,
            journal.Review.WorkspaceRevision.ToString(CultureInfo.InvariantCulture));
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.SnapshotDigest);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Schema);
        Sr5DowntimeCalendarPhoneProjection.Append(
            material,
            journal.Review.Preview.PreviewDigest);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.Schema);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.Operation.ToString());
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.WeekId.ToString("D"));
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.Year.ToString(CultureInfo.InvariantCulture));
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.Week.ToString(CultureInfo.InvariantCulture));
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.Notes);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.NotesColor);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.ExpectedCalendarRevision);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.ExpectedLogicalRevision);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.ExpectedSourceRevision);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.Review.Preview.Summary);
        Sr5DowntimeCalendarPhoneProjection.Append(material, journal.ExpectedPostconditionDigest);
        Sr5DowntimeCalendarPhoneProjection.Append(
            material,
            journal.Receipt?.ReceiptDigest ?? string.Empty);
        return Sr5DowntimeCalendarPhoneProjection.Hash(material.ToString());
    }
}

internal sealed class PreferencesSr5DowntimeCalendarJournalBackend : ISr5CareerCheckpointBackend
{
    private const string StorageKey = "sr5.career.downtime-calendar.journal.v1";

    public string Read() => Preferences.Default.Get(StorageKey, string.Empty);
    public void Write(string payload) => Preferences.Default.Set(StorageKey, payload);
    public void Remove() => Preferences.Default.Remove(StorageKey);
}

/// <summary>
/// Durable CAS journal for one Downtime Calendar mutation. Applying also owns the shared Career
/// mutation owner, so no other typed Career lane can race an unresolved calendar save.
/// </summary>
internal sealed class Sr5DowntimeCalendarJournalStore
{
    private const int MaximumPayloadCharacters = 64 * 1024;
    private static readonly object Gate = new();
    private readonly ISr5CareerCheckpointBackend _backend;
    private readonly Sr5CareerMutationOwnerStore _mutationOwners;

    internal Sr5DowntimeCalendarJournalStore(
        ISr5CareerCheckpointBackend backend,
        Sr5CareerMutationOwnerStore mutationOwners)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _mutationOwners = mutationOwners ?? throw new ArgumentNullException(nameof(mutationOwners));
    }

    public static Sr5DowntimeCalendarJournalStore CreateDefault()
        => new(
            new PreferencesSr5DowntimeCalendarJournalBackend(),
            Sr5CareerMutationOwnerStore.CreateDefault());

    internal static Sr5DowntimeCalendarJournalStore CreateIsolated(
        ISr5CareerCheckpointBackend backend)
        => new(backend, Sr5CareerMutationOwnerStore.CreateIsolated());

    public bool TryRead(out Sr5DowntimeCalendarJournal? journal, out string blocker)
    {
        lock (Gate)
            return TryReadLocked(out journal, out blocker);
    }

    public bool TryWriteReview(
        Sr5DowntimeCalendarDesktopSession session,
        Guid ownerId,
        out Sr5DowntimeCalendarJournal review,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(session);
        review = null!;
        if (ownerId == Guid.Empty || session.State.Preview is null)
        {
            blocker = "An exact owner and preview are required before checkpointing.";
            return false;
        }
        Sr5DowntimeCalendarCheckpoint checkpoint = session.CreateCheckpoint();
        string expected = Sr5DowntimeCalendarDesktopSession.ComputeExpectedPostconditionDigest(
            checkpoint.Preview,
            session.State.Editor);
        lock (Gate)
        {
            long version = 1;
            if (TryReadLocked(out Sr5DowntimeCalendarJournal? current, out string readBlocker))
            {
                if (current!.Phase == Sr5DowntimeCalendarJournalPhase.Applying)
                {
                    blocker = "An unresolved Downtime Calendar mutation is still Applying.";
                    return false;
                }
                if (current.Phase == Sr5DowntimeCalendarJournalPhase.Applied)
                {
                    blocker = "Clear the verified Applied Calendar receipt before starting another review.";
                    return false;
                }
                version = checked(current.Version + 1);
            }
            else if (!string.IsNullOrWhiteSpace(readBlocker))
            {
                blocker = readBlocker;
                return false;
            }
            review = Sr5DowntimeCalendarJournal.CreateReview(
                version,
                ownerId,
                checkpoint,
                expected);
            return TryWriteLocked(review, out blocker);
        }
    }

    public bool TryBeginApplying(
        Sr5DowntimeCalendarJournal expected,
        out Sr5DowntimeCalendarJournal applying,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!expected.IsExact()
            || expected.Phase != Sr5DowntimeCalendarJournalPhase.Review)
        {
            applying = null!;
            blocker = "Only an exact Review journal may enter Applying.";
            return false;
        }
        Sr5DowntimeCalendarJournal applyingCandidate = Sr5DowntimeCalendarJournal.Sign(expected with
        {
            Version = checked(expected.Version + 1),
            Phase = Sr5DowntimeCalendarJournalPhase.Applying,
            Receipt = null
        });
        applying = applyingCandidate;
        Sr5CareerMutationOwner owner = Owner(applying);
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
                            ExactReviewedStateWasRestored: true,
                            casBlocker);
                    }
                    bool wrote = TryWriteLocked(applyingCandidate, out string writeBlocker);
                    return new Sr5CareerMutationBeginResult(
                        wrote,
                        ExactReviewedStateWasRestored: !wrote && TryRequireLocked(expected, out _),
                        writeBlocker);
                }
            },
            out blocker);
        return began;
    }

    public Task<IDisposable> AcquireApplyingLeaseAsync(
        Sr5DowntimeCalendarJournal applying,
        CancellationToken cancellationToken)
        => _mutationOwners.AcquireExecutionLeaseAsync(Owner(applying), cancellationToken);

    public bool TryReturnToReview(
        Sr5DowntimeCalendarJournal applying,
        out Sr5DowntimeCalendarJournal review,
        out string blocker)
    {
        review = Sr5DowntimeCalendarJournal.Sign(applying with
        {
            Version = checked(applying.Version + 1),
            Phase = Sr5DowntimeCalendarJournalPhase.Review,
            Receipt = null
        });
        return CompleteOwner(applying, review, out blocker);
    }

    public bool TryComplete(
        Sr5DowntimeCalendarJournal applying,
        Sr5DowntimeCalendarPersistenceReceipt receipt,
        out Sr5DowntimeCalendarJournal applied,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        applied = Sr5DowntimeCalendarJournal.Sign(applying with
        {
            Version = checked(applying.Version + 1),
            Phase = Sr5DowntimeCalendarJournalPhase.Applied,
            Receipt = receipt
        });
        return CompleteOwner(applying, applied, out blocker);
    }

    public bool TryClearResolved(Sr5DowntimeCalendarJournal expected, out string blocker)
    {
        blocker = string.Empty;
        lock (Gate)
        {
            if (expected.Phase != Sr5DowntimeCalendarJournalPhase.Applied
                || !TryRequireLocked(expected, out blocker))
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "Only an exact Applied Downtime Calendar receipt can be cleared."
                    : blocker;
                return false;
            }
            try
            {
                _backend.Remove();
                if (!string.IsNullOrEmpty(_backend.Read()))
                {
                    blocker = "The Downtime Calendar journal clear failed read-back.";
                    return false;
                }
                blocker = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                blocker = $"The Downtime Calendar journal could not be cleared: {exception.Message}";
                return false;
            }
        }
    }

    private bool CompleteOwner(
        Sr5DowntimeCalendarJournal applying,
        Sr5DowntimeCalendarJournal resolution,
        out string blocker)
        => _mutationOwners.TryComplete(
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

    private static Sr5CareerMutationOwner Owner(Sr5DowntimeCalendarJournal applying)
    {
        if (!applying.IsExact() || applying.Phase != Sr5DowntimeCalendarJournalPhase.Applying)
            throw new InvalidOperationException("Only an exact Applying calendar journal owns mutation.");
        return new Sr5CareerMutationOwner(
            Sr5CareerMutationOwner.CurrentSchemaVersion,
            Sr5CareerMutationDomains.DowntimeCalendar,
            applying.Review.WorkspaceId,
            applying.OwnerId,
            applying.ActionId,
            applying.Version,
            applying.Review.WorkspaceRevision,
            applying.Review.Preview.PreviewDigest["sha256:".Length..]);
    }

    private bool TryRequireLocked(Sr5DowntimeCalendarJournal expected, out string blocker)
    {
        if (!TryReadLocked(out Sr5DowntimeCalendarJournal? current, out blocker)
            || current != expected)
        {
            blocker = string.IsNullOrWhiteSpace(blocker)
                ? "The Downtime Calendar journal changed before CAS."
                : blocker;
            return false;
        }
        return true;
    }

    private bool TryReadLocked(
        out Sr5DowntimeCalendarJournal? journal,
        out string blocker)
    {
        journal = null;
        try
        {
            string payload = _backend.Read();
            if (string.IsNullOrEmpty(payload))
            {
                blocker = string.Empty;
                return false;
            }
            if (payload.Length > MaximumPayloadCharacters)
            {
                blocker = "The Downtime Calendar journal exceeds its bound and remains replay-blocking.";
                return false;
            }
            Sr5DowntimeCalendarJournal? parsed =
                JsonSerializer.Deserialize<Sr5DowntimeCalendarJournal>(payload);
            if (parsed is null || !parsed.IsExact())
            {
                blocker = "The Downtime Calendar journal is invalid and remains replay-blocking.";
                return false;
            }
            journal = parsed;
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            blocker = $"The Downtime Calendar journal is unreadable and replay-blocking: {exception.Message}";
            return false;
        }
    }

    private bool TryWriteLocked(Sr5DowntimeCalendarJournal journal, out string blocker)
    {
        if (!journal.IsExact())
        {
            blocker = "The Downtime Calendar journal is structurally invalid.";
            return false;
        }
        try
        {
            string payload = JsonSerializer.Serialize(journal);
            if (payload.Length > MaximumPayloadCharacters)
            {
                blocker = "The Downtime Calendar journal exceeds its durable bound.";
                return false;
            }
            _backend.Write(payload);
            if (!TryReadLocked(out Sr5DowntimeCalendarJournal? readBack, out blocker)
                || readBack != journal)
            {
                blocker = string.IsNullOrWhiteSpace(blocker)
                    ? "The Downtime Calendar journal did not survive exact read-back."
                    : blocker;
                return false;
            }
            blocker = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            blocker = $"The Downtime Calendar journal could not be written: {exception.Message}";
            return false;
        }
    }
}
