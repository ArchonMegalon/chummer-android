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
        Console.WriteLine("SR5 Downtime Calendar Android authority tests passed: 3");
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
}
