using System.Globalization;
using Chummer.Android.Native;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCalendarWizardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var previousApp = Application.Current;
            var previousCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            var app = new ReviewRouteApplication();
            Application.Current = app;
            var window = (Window)((Microsoft.Maui.IApplication)app).CreateWindow(null!);
            await using var runtime = new NativeRewardRuntime(contentRoot);
            await runtime.LoadRunnerAsync();
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var original = store.Get(runtime.Id).Value!;
            Sr5DowntimeCalendarWizardPage? page = null;
            IssuedPageAlerts? alerts = null;
            bool accept = false, departDuringConfirmation = false;

            async Task OpenAsync()
            {
                alerts?.Dispose();
                if (page is not null) IssuedPageLifecycle(page, "OnDisappearing");
                page = new Sr5DowntimeCalendarWizardPage(runtime.Coordinator);
                window.Page = page;
                await ui.DrainDispatchedAsyncVoidAsync();
                IssuedPageLifecycle(page, "OnDisappearing");
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                await ui.DrainDispatchedAsyncVoidAsync();
                alerts = new IssuedPageAlerts(page, window, args =>
                {
                    Require(args.Title == "Confirm Calendar change" && args.Accept == "Confirm",
                        "Unexpected dialog before Calendar save.");
                    Require(!args.Message.Contains("sha256:") && !args.Message.Contains(runtime.Id.Value),
                        "Confirmation exposes technical authority instead of the chosen change.");
                    if (departDuringConfirmation) IssuedPageLifecycle(page, "OnDisappearing");
                    args.SetResult(accept);
                });
                RefreshCalendar(page);
            }

            async Task ClickAsync(string id)
            {
                var button = MinimalVisible(page!).OfType<Button>().Single(x => x.AutomationId == id);
                Require(button.IsEnabled, "Visible Calendar action is disabled: " + id);
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                await ui.DrainDispatchedAsyncVoidAsync();
            }

            void RequireReview()
            {
                Require(!MinimalVisible(page!).Any(x => x is Entry or Microsoft.Maui.Controls.Editor or Picker),
                    "Review still exposes editable inputs that are not the confirmed preview.");
                Require(!CalendarField<Entry>(page!, "_week").IsEnabled
                    && !CalendarField<Picker>(page!, "_operation").IsEnabled,
                    "Reviewed form remains interactive.");
                Require(MinimalVisible(page!).OfType<Button>().Select(x => x.AutomationId).Order()
                        .SequenceEqual(new[] { "sr5-downtime-calendar-apply", "sr5-downtime-calendar-edit-review" }),
                    "Review should expose only Save change and Change preview.");
                MinimalRequireNoMachineValues(page!);
            }

            try
            {
                await OpenAsync();
                MinimalRequireNoMachineValues(page!);
                Require(!MinimalVisible(page!).OfType<Label>().Any(x => string.IsNullOrWhiteSpace(x.Text)),
                    "Empty status cards are still displayed.");
                Require(!MinimalVisible(page!).OfType<Label>().Any(x => x.Text is "Saved week" or "Notes color"),
                    "Labels for unavailable fields remain visible.");
                CalendarField<Entry>(page!, "_year").Text = "2078";
                CalendarField<Entry>(page!, "_week").Text = "12";
                await ClickAsync("sr5-downtime-calendar-review");
                RequireReview();
                var old = CalendarField<Sr5DowntimeCalendarJournal>(page!, "_journal");
                RequireSameRewardDocument(original, store.Get(runtime.Id).Value!);
                await ClickAsync("sr5-downtime-calendar-apply"); // Cancel the actual MAUI confirmation.
                Require(!CalendarField<Sr5DowntimeCalendarDesktopSession>(page!, "_session").State.CanApply
                    && alerts!.Titles.Count == 1, "Cancel retained confirmation or did not show a dialog.");
                RequireSameRewardDocument(original, store.Get(runtime.Id).Value!);

                await ClickAsync("sr5-downtime-calendar-edit-review");
                Require(CalendarField<Entry>(page!, "_week") is { IsEnabled: true, Text: "12" }
                    && !Sr5DowntimeCalendarJournalStore.CreateDefault().TryRead(out _, out var blocker)
                    && string.IsNullOrEmpty(blocker), "Change preview failed to restore input and discard only its review.");
                CalendarField<Entry>(page!, "_week").Text = "14";
                await ClickAsync("sr5-downtime-calendar-review");
                var fresh = CalendarField<Sr5DowntimeCalendarJournal>(page!, "_journal");
                Require(fresh.ActionId != old.ActionId && fresh.Review.Preview.Week == 14,
                    "Changing inputs reused the old reviewed operation.");
                await OpenAsync();
                RequireReview();
                Require(!CalendarField<Sr5DowntimeCalendarDesktopSession>(page!, "_session").State.CanApply
                    && CalendarField<Entry>(page!, "_week").Text == "14",
                    "Reopen restored confirmation or forgot the reviewed form values.");
                accept = true;
                await ClickAsync("sr5-downtime-calendar-apply");
                var added = store.Get(runtime.Id).Value!;
                var editor = await runtime.Coordinator.PrepareCareerCalendarEditAsync();
                Require(added.ContentRevision == original.ContentRevision + 1 && editor?.Weeks.Count == 1
                    && editor.Weeks[0].Year == 2078 && editor.Weeks[0].Week == 14,
                    "One confirmed button did not save the newly reviewed week exactly once.");
                MinimalRequireNoMachineValues(page!);
                Require(MinimalVisible(page!).OfType<Label>().Count(x => x.Text == "Calendar change saved.") == 1
                    && !MinimalVisible(page!).Any(x => x is Entry or Microsoft.Maui.Controls.Editor or Picker),
                    "Success duplicates receipts or leaves the old form interactive.");

                await ClickAsync("sr5-downtime-calendar-clear-applied");
                CalendarField<Picker>(page!, "_operation").SelectedIndex = 1;
                Require(CalendarField<Picker>(page!, "_weekPicker").Items[0] == "2078 · Week 14",
                    "Week choice exposes GUIDs instead of a readable date.");
                CalendarField<Editor>(page!, "_notes").Text = "A quiet week.";
                await ClickAsync("sr5-downtime-calendar-review");
                RequireReview();
                Require(MinimalVisibleText(page!).Contains("A quiet week."), "Reviewed notes are not shown before saving.");
                await ClickAsync("sr5-downtime-calendar-apply");
                var edited = store.Get(runtime.Id).Value!;
                editor = await runtime.Coordinator.PrepareCareerCalendarEditAsync();
                Require(edited.ContentRevision == added.ContentRevision + 1 && editor!.Weeks.Single().Notes == "A quiet week.",
                    "Typed week selection saved the wrong notes or duplicated the change.");

                await ClickAsync("sr5-downtime-calendar-clear-applied");
                CalendarField<Picker>(page!, "_operation").SelectedIndex = 2;
                await ClickAsync("sr5-downtime-calendar-review");
                RequireReview();
                departDuringConfirmation = true;
                await ClickAsync("sr5-downtime-calendar-apply");
                RequireSameRewardDocument(edited, store.Get(runtime.Id).Value!);
                Require(!CalendarField<Sr5DowntimeCalendarDesktopSession>(page!, "_session").State.CanApply,
                    "A departed confirmation granted save permission.");
                departDuringConfirmation = false;
                await OpenAsync();
                await ClickAsync("sr5-downtime-calendar-apply");
                var deleted = store.Get(runtime.Id).Value!;
                editor = await runtime.Coordinator.PrepareCareerCalendarEditAsync();
                Require(deleted.ContentRevision == edited.ContentRevision + 1 && editor!.Weeks.Count == 0,
                    "Reconfirmed delete did not remove exactly the reviewed week.");
                await OpenAsync();
                MinimalRequireNoMachineValues(page!);
                Require(MinimalVisible(page!).OfType<Button>().Single().AutomationId == "sr5-downtime-calendar-clear-applied",
                    "Applied receipt reopen offers another save or an editable form.");
                RequireSameRewardDocument(deleted, store.Get(runtime.Id).Value!);
                ui.AssertHealthy();
                Console.WriteLine("PASS actual Calendar buttons: staged form/review, cancel, edit/review replacement, reopen/reconfirm, exact add/edit/delete, departed-dialog rejection, single saved receipt and no machine identifiers.");
            }
            finally
            {
                alerts?.Dispose();
                if (page is not null) IssuedPageLifecycle(page, "OnDisappearing");
                app.CloseWindow(window);
                Application.Current = previousApp!;
                CultureInfo.CurrentUICulture = previousCulture;
            }
        });
    }
}
