using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCalendarStaleReviewAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        await runtime.LoadRunnerAsync();
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var original = store.Get(runtime.Id).Value!;
        var calendar = new Sr5DowntimeCalendarWizardPage(runtime.Coordinator);
        await InvokeTokenPageActionAsync(calendar, "PrepareForAppearanceRefreshAsync", default);
        RefreshCalendar(calendar);
        Require(PageButton(calendar, "sr5-downtime-calendar-review").IsEnabled,
            "Real Calendar authority is unavailable for this runner.");
        CalendarField<Entry>(calendar, "_year").Text = "2078";
        CalendarField<Entry>(calendar, "_week").Text = "12";
        await InvokePageActionAsync(calendar, "ReviewAsync");
        RefreshCalendar(calendar);
        var oldReview = CalendarField<Sr5DowntimeCalendarJournal>(calendar, "_journal");
        Require(oldReview.Phase == Sr5DowntimeCalendarJournalPhase.Review,
            "Creating a preview dispatched a Calendar mutation.");
        RequireSameRewardDocument(original, store.Get(runtime.Id).Value!);

        var unchanged = new Sr5DowntimeCalendarWizardPage(runtime.Coordinator);
        await InvokeTokenPageActionAsync(unchanged, "PrepareForAppearanceRefreshAsync", default);
        RefreshCalendar(unchanged);
        Require(CalendarField<Sr5DowntimeCalendarDesktopSession>(unchanged, "_session").State.Resume.Restored
                && !PageButton(unchanged, "sr5-downtime-calendar-apply").IsEnabled,
            "An unchanged review lost its resume or restored confirmation.");

        // Actual unrelated Core-owned save, not a fabricated revision in the view.
        var reward = await runtime.PrepareRewardAsync();
        await reward.ConfirmAsync();
        var rewarded = store.Get(runtime.Id).Value!;
        Require(rewarded.ContentRevision == original.ContentRevision + 1,
            "The unrelated saved reward did not advance the runner.");
        for (int reopen = 0; reopen < 2; reopen++)
        {
            calendar = new Sr5DowntimeCalendarWizardPage(runtime.Coordinator);
            await InvokeTokenPageActionAsync(calendar, "PrepareForAppearanceRefreshAsync", default);
            RefreshCalendar(calendar);
            Require(!CalendarField<bool>(calendar, "_outcomeUnknown"),
                "An uncommitted stale review permanently locks Calendar as an unknown save.");
            Require(PageButton(calendar, "sr5-downtime-calendar-discard-review") is
                    { IsVisible: true, IsEnabled: true },
                "A stale uncommitted preview has no explicit recovery action.");
            Require(!PageButton(calendar, "sr5-downtime-calendar-apply").IsEnabled
                    && CalendarField<Sr5DowntimeCalendarJournal>(calendar, "_journal") == oldReview,
                "Reopening cleared the preview or granted implicit save permission.");
            RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
        }
        await InvokePageActionAsync(calendar, "DiscardStaleReviewAsync");
        RefreshCalendar(calendar);
        Require(!Sr5DowntimeCalendarJournalStore.CreateDefault().TryRead(out _, out var blocker)
                && string.IsNullOrEmpty(blocker)
                && PageButton(calendar, "sr5-downtime-calendar-review").IsEnabled
                && !PageButton(calendar, "sr5-downtime-calendar-apply").IsEnabled,
            "Discard did not restore fresh review without restoring confirmation.");
        RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
        Require(!Sr5DowntimeCalendarJournalStore.CreateDefault().TryBeginApplying(oldReview, out _, out _),
            "The retained discarded review entered Applying.");

        CalendarField<Entry>(calendar, "_year").Text = "2078";
        CalendarField<Entry>(calendar, "_week").Text = "14";
        await InvokePageActionAsync(calendar, "ReviewAsync");
        var fresh = CalendarField<Sr5DowntimeCalendarJournal>(calendar, "_journal");
        var session = CalendarField<Sr5DowntimeCalendarDesktopSession>(calendar, "_session");
        Require(fresh.ActionId != oldReview.ActionId && fresh.Review.WorkspaceRevision == rewarded.SavedRevision
                && !session.State.CanApply, "New review reused stale identity or confirmation.");
        // Explicit managed decision; native dialog interaction is separately smoke-tested.
        Require(session.TryConfirm(session.State.Preview!.PreviewDigest), "Fresh preview confirmation failed.");
        await InvokePageActionAsync(calendar, "ApplyAsync");
        RefreshCalendar(calendar);
        var saved = store.Get(runtime.Id).Value!;
        var current = await runtime.Coordinator.PrepareCareerCalendarEditAsync();
        Require(saved.ContentRevision == rewarded.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                && current?.Weeks.Count == 1 && current.Weeks[0].Week == 14 && current.Weeks[0].Year == 2078,
            "Recovery failed to save the new chosen week exactly once.");
        var beforeXml = XElement.Parse(rewarded.Document.Content);
        var afterXml = XElement.Parse(saved.Document.Content);
        foreach (string element in new[] { "karma", "nuyen", "expenses", "improvements", "contacts" })
            Require(XNode.DeepEquals(beforeXml.Element(element), afterXml.Element(element)),
                "Calendar recovery changed unrelated saved state: " + element);
        var reopened = new Sr5DowntimeCalendarWizardPage(runtime.Coordinator);
        await InvokeTokenPageActionAsync(reopened, "PrepareForAppearanceRefreshAsync", default);
        RefreshCalendar(reopened);
        Require(PageButton(reopened, "sr5-downtime-calendar-clear-applied").IsVisible
                && !PageButton(reopened, "sr5-downtime-calendar-discard-review").IsVisible,
            "Saved receipt became discardable as an unapplied preview.");
        RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
        Console.WriteLine("PASS actual Core/native Calendar preview -> unrelated save -> repeated reopen -> explicit discard -> fresh confirm/save -> receipt reopen; no duplicate reward or old-preview replay.");
    }
}
