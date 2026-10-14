using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("calendar-phone-tests");
    private static readonly Guid WeekId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static async Task<int> Main()
    {
        await ReviewApplyReceiptIsExactAsync();
        RestartDropsConfirmationAndStaleCasFails();
        UnexpectedPostconditionFailsClosed();
        ResolvedJournalCannotOrphanSharedMutationOwner();
        ReviewDiscardUsesExactCasAndAllowsFreshReview();
        ReviewDiscardRejectsApplyingAndApplied();
        ReviewDiscardRetainsCorruptAndUnreadableJournals();
        ReviewDiscardRequiresRemovalReadback();
        ReturnedReviewCannotOrphanSharedMutationOwner();
        Console.WriteLine("SR5 Downtime Calendar Android authority tests passed: 9");
        return 0;
    }

    private static async Task ReviewApplyReceiptIsExactAsync()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        Sr5CareerWizardBinding beforeBinding = Binding(before, 41, 'a', 'b');
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(beforeBinding, before);
        Require(session.TryPreviewEdit(WeekId, "healed and trained", "Chocolate", out string blocker), blocker);
        var backend = new MemoryBackend();
        Sr5DowntimeCalendarJournalStore store = Sr5DowntimeCalendarJournalStore.CreateIsolated(backend);
        Guid owner = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Require(store.TryWriteReview(session, owner, out Sr5DowntimeCalendarJournal review, out blocker), blocker);
        Require(!session.State.Confirmed, "journal write must not confirm");
        Require(session.TryConfirm(session.State.Preview!.PreviewDigest), "foreground confirmation");
        Require(store.TryBeginApplying(review, out Sr5DowntimeCalendarJournal applying, out blocker), blocker);
        using (await store.AcquireApplyingLeaseAsync(applying, CancellationToken.None)) { }

        CareerCalendarEditorState after = Editor(42, "healed and trained");
        Sr5CareerWizardBinding afterBinding = Binding(after, 42, 'c', 'd');
        Sr5DowntimeCalendarPersistenceReceipt receipt =
            Sr5DowntimeCalendarPersistenceReceipt.Create(applying, afterBinding, after);
        Require(receipt.IsExact(), "receipt exactness");
        Require(store.TryComplete(applying, receipt, out Sr5DowntimeCalendarJournal applied, out blocker), blocker);
        Require(applied.IsExact() && applied.Phase == Sr5DowntimeCalendarJournalPhase.Applied,
            "Applied journal exactness");
        Require(!store.TryWriteReview(session, owner, out _, out blocker),
            "Applied receipt was overwritten without explicit clear");
        RequireThrows<InvalidOperationException>(() =>
            Sr5DowntimeCalendarPersistenceReceipt.Create(
                applying,
                afterBinding with { SourceDigest = "sha256:" + new string('e', 64) },
                after),
            "foreign observed source binding");
    }

    private static void RestartDropsConfirmationAndStaleCasFails()
    {
        CareerCalendarEditorState editor = Editor(41, "old notes");
        Sr5CareerWizardBinding binding = Binding(editor, 41, 'a', 'b');
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(binding, editor);
        Require(session.TryPreviewDelete(WeekId, out string blocker), blocker);
        var backend = new MemoryBackend();
        Sr5DowntimeCalendarJournalStore store = Sr5DowntimeCalendarJournalStore.CreateIsolated(backend);
        Require(store.TryWriteReview(session, Guid.NewGuid(), out Sr5DowntimeCalendarJournal review, out blocker), blocker);
        var restarted = new Sr5DowntimeCalendarDesktopSession();
        Sr5DowntimeCalendarDesktopState state = restarted.Bind(binding, editor, review.Review);
        Require(state.Resume.Restored && !state.Confirmed && !state.CanApply,
            "restart restored confirmation");
        Sr5DowntimeCalendarJournal forged = review with { Version = review.Version + 1 };
        Require(!store.TryBeginApplying(forged, out _, out blocker), "forged CAS began Applying");
        Sr5DowntimeCalendarJournal changedPreview = review with
        {
            Review = review.Review with
            {
                Preview = review.Review.Preview with { Notes = "tampered" }
            }
        };
        Require(!changedPreview.IsExact(), "unhashed preview fields survived journal validation");
    }

    private static void UnexpectedPostconditionFailsClosed()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(Binding(before, 41, 'a', 'b'), before);
        Require(session.TryPreviewEdit(WeekId, "expected", "Chocolate", out string blocker), blocker);
        var store = Sr5DowntimeCalendarJournalStore.CreateIsolated(new MemoryBackend());
        Require(store.TryWriteReview(session, Guid.NewGuid(), out Sr5DowntimeCalendarJournal review, out blocker), blocker);
        Require(store.TryBeginApplying(review, out Sr5DowntimeCalendarJournal applying, out blocker), blocker);
        CareerCalendarEditorState unexpected = Editor(42, "different");
        RequireThrows<InvalidOperationException>(() => Sr5DowntimeCalendarPersistenceReceipt.Create(
            applying, Binding(unexpected, 42, 'c', 'd'), unexpected), "unexpected postcondition");
    }

    private static void ResolvedJournalCannotOrphanSharedMutationOwner()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(Binding(before, 41, 'a', 'b'), before);
        Require(session.TryPreviewEdit(WeekId, "resolved", "Chocolate", out string blocker), blocker);

        var journalBackend = new MemoryBackend();
        var ownerBackend = new FaultingRemoveBackend();
        var ownerStore = new Sr5CareerMutationOwnerStore(ownerBackend);
        var store = new Sr5DowntimeCalendarJournalStore(journalBackend, ownerStore);
        Require(store.TryWriteReview(
            session,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            out Sr5DowntimeCalendarJournal review,
            out blocker), blocker);
        Require(store.TryBeginApplying(
            review,
            out Sr5DowntimeCalendarJournal applying,
            out blocker), blocker);

        CareerCalendarEditorState after = Editor(42, "resolved");
        Sr5DowntimeCalendarPersistenceReceipt receipt =
            Sr5DowntimeCalendarPersistenceReceipt.Create(
                applying,
                Binding(after, 42, 'c', 'd'),
                after);
        ownerBackend.FailRemove = true;
        Require(!store.TryComplete(
            applying,
            receipt,
            out Sr5DowntimeCalendarJournal applied,
            out blocker), "owner-release failure was hidden");
        Require(applied.IsExact()
            && applied.Phase == Sr5DowntimeCalendarJournalPhase.Applied,
            "resolved journal was not durable before owner-release failure");
        Require(!store.TryClearResolved(applied, out blocker),
            "resolved journal was cleared while its shared owner remained durable");
        Require(!string.IsNullOrWhiteSpace(journalBackend.Payload),
            "failed reconciliation orphaned the shared owner by clearing its domain journal");
        Require(!ownerStore.TryRunWhenUnowned(
            () => (true, string.Empty),
            out _), "a foreign Career mutation entered while the stale owner remained");

        ownerBackend.FailRemove = false;
        Require(store.TryRead(out Sr5DowntimeCalendarJournal? recovered, out blocker), blocker);
        Require(recovered == applied, "resolved journal changed during owner reconciliation");
        Require(ownerStore.TryRunWhenUnowned(
            () => (true, string.Empty),
            out blocker), blocker);
        Require(store.TryClearResolved(applied, out blocker), blocker);
        Require(string.IsNullOrWhiteSpace(journalBackend.Payload),
            "reconciled resolved journal was not cleared");
    }

    private static void ReviewDiscardUsesExactCasAndAllowsFreshReview()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(Binding(before, 41, 'a', 'b'), before);
        Require(session.TryPreviewEdit(WeekId, "first preview", "Chocolate", out string blocker), blocker);
        var backend = new DiscardBackend();
        var store = Sr5DowntimeCalendarJournalStore.CreateIsolated(backend);
        Require(store.TryWriteReview(session, Guid.NewGuid(), out var first, out blocker), blocker);
        Require(session.TryPreviewEdit(WeekId, "replacement preview", "Chocolate", out blocker), blocker);
        Require(store.TryWriteReview(session, Guid.NewGuid(), out var current, out blocker), blocker);
        string payload = backend.Payload;
        Require(!store.TryDiscardUnappliedReview(first, out blocker), "stale review cleared its replacement");
        Require(!store.TryDiscardUnappliedReview(current with { Version = current.Version + 1 }, out blocker),
            "forged review was discarded");
        Require(backend.Payload == payload && backend.RemoveCalls == 0,
            "rejected discard touched the current journal");

        CareerCalendarEditorState changed = Editor(42, "old notes");
        var restarted = new Sr5DowntimeCalendarDesktopSession();
        Require(!restarted.Bind(Binding(changed, 42, 'c', 'd'), changed, current.Review).Resume.Restored,
            "stale runner revision unexpectedly restored review");
        Require(store.TryDiscardUnappliedReview(current, out blocker), blocker);
        Require(backend.Payload.Length == 0 && backend.RemoveCalls == 1, "review discard was not durable");
        Require(!store.TryBeginApplying(current, out _, out blocker), "discarded review entered Applying");
        Require(restarted.TryPreviewEdit(WeekId, "fresh preview", "Chocolate", out blocker), blocker);
        Require(store.TryWriteReview(restarted, Guid.NewGuid(), out var fresh, out blocker), blocker);
        Require(fresh.Review.WorkspaceRevision == 42 && !restarted.State.CanApply,
            "fresh review lost the current revision or inherited confirmation");
    }

    private static void ReviewDiscardRejectsApplyingAndApplied()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(Binding(before, 41, 'a', 'b'), before);
        Require(session.TryPreviewEdit(WeekId, "saved notes", "Chocolate", out string blocker), blocker);
        var backend = new DiscardBackend();
        var store = Sr5DowntimeCalendarJournalStore.CreateIsolated(backend);
        Require(store.TryWriteReview(session, Guid.NewGuid(), out var review, out blocker), blocker);
        Require(store.TryBeginApplying(review, out var applying, out blocker), blocker);
        string applyingPayload = backend.Payload;
        Require(!store.TryDiscardUnappliedReview(applying, out blocker), "Applying journal was discarded");
        Require(!store.TryDiscardUnappliedReview(review, out blocker), "old Review cleared Applying");
        Require(backend.Payload == applyingPayload && backend.RemoveCalls == 0,
            "Applying journal was touched by review discard");

        CareerCalendarEditorState after = Editor(42, "saved notes");
        var receipt = Sr5DowntimeCalendarPersistenceReceipt.Create(applying, Binding(after, 42, 'c', 'd'), after);
        Require(store.TryComplete(applying, receipt, out var applied, out blocker), blocker);
        string appliedPayload = backend.Payload;
        Require(!store.TryDiscardUnappliedReview(applied, out blocker), "Applied receipt was discarded as a review");
        Require(!store.TryDiscardUnappliedReview(review, out blocker), "old Review cleared Applied");
        Require(backend.Payload == appliedPayload && backend.RemoveCalls == 0,
            "Applied receipt was touched by review discard");
    }

    private static void ReviewDiscardRetainsCorruptAndUnreadableJournals()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(Binding(before, 41, 'a', 'b'), before);
        Require(session.TryPreviewDelete(WeekId, out string blocker), blocker);
        var backend = new DiscardBackend();
        var store = Sr5DowntimeCalendarJournalStore.CreateIsolated(backend);
        Require(store.TryWriteReview(session, Guid.NewGuid(), out var review, out blocker), blocker);
        string validPayload = backend.Payload;
        backend.Write("{unreadable journal");
        Require(!store.TryDiscardUnappliedReview(review, out blocker), "corrupt journal was discarded");
        Require(backend.Payload == "{unreadable journal" && backend.RemoveCalls == 0,
            "corrupt journal was not retained");
        backend.Write(validPayload);
        backend.FailRead = true;
        Require(!store.TryDiscardUnappliedReview(review, out blocker), "unreadable journal was discarded");
        Require(backend.Payload == validPayload && backend.RemoveCalls == 0,
            "read failure removed the review");
        backend.FailRead = false;
        Require(store.TryDiscardUnappliedReview(review, out blocker), blocker);
    }

    private static void ReviewDiscardRequiresRemovalReadback()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(Binding(before, 41, 'a', 'b'), before);
        Require(session.TryPreviewDelete(WeekId, out string blocker), blocker);
        var backend = new DiscardBackend();
        var store = Sr5DowntimeCalendarJournalStore.CreateIsolated(backend);
        Require(store.TryWriteReview(session, Guid.NewGuid(), out var review, out blocker), blocker);
        string payload = backend.Payload;
        backend.IgnoreRemove = true;
        Require(!store.TryDiscardUnappliedReview(review, out blocker), "failed removal read-back reported success");
        Require(backend.Payload == payload, "failed removal did not retain review");
        backend.IgnoreRemove = false;
        backend.FailReadAfterRemove = true;
        Require(!store.TryDiscardUnappliedReview(review, out blocker), "unreadable removal result reported success");
        Require(backend.Payload.Length == 0, "simulated successful removal did not run");
    }

    private static void ReturnedReviewCannotOrphanSharedMutationOwner()
    {
        CareerCalendarEditorState before = Editor(41, "old notes");
        var session = new Sr5DowntimeCalendarDesktopSession();
        session.Bind(Binding(before, 41, 'a', 'b'), before);
        Require(session.TryPreviewDelete(WeekId, out string blocker), blocker);
        var journalBackend = new DiscardBackend();
        var ownerBackend = new FaultingRemoveBackend();
        var ownerStore = new Sr5CareerMutationOwnerStore(ownerBackend);
        var store = new Sr5DowntimeCalendarJournalStore(journalBackend, ownerStore);
        Require(store.TryWriteReview(session, Guid.NewGuid(), out var review, out blocker), blocker);
        Require(store.TryBeginApplying(review, out var applying, out blocker), blocker);
        ownerBackend.FailRemove = true;
        Require(!store.TryReturnToReview(applying, out var returned, out blocker), "owner-release failure was hidden");
        Require(returned.IsExact() && returned.Version >= 3
            && returned.Phase == Sr5DowntimeCalendarJournalPhase.Review, "returned Review was not durable");
        string payload = journalBackend.Payload;
        Require(!store.TryDiscardUnappliedReview(returned, out blocker),
            "returned Review was discarded while its shared owner remained");
        Require(journalBackend.Payload == payload && journalBackend.RemoveCalls == 0,
            "failed owner reconciliation removed the returned Review");
        Require(!ownerStore.TryRunWhenUnowned(() => (true, string.Empty), out _),
            "another Career mutation entered while the shared owner remained");

        ownerBackend.FailRemove = false;
        Require(store.TryDiscardUnappliedReview(returned, out blocker), blocker);
        Require(journalBackend.Payload.Length == 0 && journalBackend.RemoveCalls == 1,
            "reconciled returned Review was not discarded");
        Require(ownerStore.TryRunWhenUnowned(() => (true, string.Empty), out blocker), blocker);
        Require(!store.TryBeginApplying(returned, out _, out blocker), "discarded recovery Review was replayed");
    }

    private static CareerCalendarEditorState Editor(long revision, string notes)
    {
        string raw = $"<week><guid>{WeekId:D}</guid><year>2081</year><week>12</week><notes>{notes}</notes><notesColor>Chocolate</notesColor></week>";
        Require(CharacterCareerCalendarRules.TryCreateCalendar(
            isCareer: true,
            CharacterCareerCalendarRules.PinnedSourceAuthority,
            new[] { raw },
            out CharacterCareerCalendarState calendar), "calendar projection");
        return new(WorkspaceId, revision, calendar, false, "unavailable");
    }

    private static Sr5CareerWizardBinding Binding(
        CareerCalendarEditorState editor, long revision, char payload, char document)
        => Sr5DowntimeCalendarPhoneProjection.CreateBinding(
            new Sr5DowntimeCalendarWorkspaceAuthority(
                WorkspaceId.Value, revision, revision, new string(payload, 64), new string(document, 64)),
            editor);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class DiscardBackend : ISr5CareerCheckpointBackend
    {
        public string Payload { get; private set; } = string.Empty;
        public int RemoveCalls { get; private set; }
        public bool FailRead { get; set; }
        public bool IgnoreRemove { get; set; }
        public bool FailReadAfterRemove { get; set; }

        public string Read() => FailRead ? throw new IOException("Simulated read failure.") : Payload;
        public void Write(string payload) => Payload = payload;
        public void Remove()
        {
            RemoveCalls++;
            if (!IgnoreRemove) Payload = string.Empty;
            if (FailReadAfterRemove) FailRead = true;
        }
    }
}
