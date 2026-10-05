using System.Reflection;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunPrivateStoriesNavigationAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(OwnerScope.LocalSingleUser);
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            var page = new PhoneStoriesPage(runtime.Coordinator);
            var window = new Window(new NavigationPage(page));
            using var alerts = new IssuedPageAlerts(page, window);
            await alerts.PreflightAsync();
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            Require(IssuedElements(page).Any(e => e.AutomationId == "phone-stories-choose-runner")
                && !IssuedElements(page).Any(e => e.AutomationId == "phone-stories-read-book"),
                "Empty Stories must offer runner selection, not an unavailable archive or a fictitious book.");
            IssuedPageLifecycle(page, "OnDisappearing");

            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Private Origin runner", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.Priority, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            await runtime.Coordinator.SaveAsync();
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            Require(!IssuedElements(page).Any(e => e.AutomationId == "phone-stories-read-book"),
                "Priority was advertised as a Life Modules Origin book.");
            IssuedPageLifecycle(page, "OnDisappearing");

            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Private Life Modules", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            Require(runtime.Coordinator.CanReadRetainedOriginBook(),
                $"Life Modules fixture has no admitted book: method={runtime.Coordinator.State.Profile?.BuildMethod}, "
                + $"edition={runtime.Coordinator.State.Rules?.GameEdition}, revision={runtime.Coordinator.State.ContentRevision}/{runtime.Coordinator.State.SavedRevision}, "
                + $"error={runtime.Coordinator.State.Error}, notice={runtime.Coordinator.Notice}");
            var read = IssuedElements(page).OfType<Button>().Single(b => b.AutomationId == "phone-stories-read-book");
            await ui.BeginAsyncVoid(() => ((IButtonController)read).SendClicked());
            Require(page.Navigation.NavigationStack.Last() is RetainedOriginBookPage,
                "Stories did not navigate to the real native retained-book reader.");
            await page.Navigation.PopAsync();

            int runnerReturns = 0;
            var reader = new RetainedOriginBookPage(runtime.Coordinator, () =>
            {
                runnerReturns++;
                return Task.CompletedTask;
            });
            await page.Navigation.PushAsync(reader);
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reader, "OnAppearing"));
            var back = IssuedElements(reader).OfType<Button>().Single(b => b.AutomationId == "origin-book-return-to-runner");
            await ui.BeginAsyncVoid(() => ((IButtonController)back).SendClicked());
            Require(runnerReturns == 1, "The reader ignored its explicit runner return destination.");
            IssuedPageLifecycle(reader, "OnDisappearing");
            await ui.BeginAsyncVoid(() => ((IButtonController)back).SendClicked());
            Require(runnerReturns == 1, "A departed reader used its stale runner return action.");
            await page.Navigation.PopAsync();

            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            read = IssuedElements(page).OfType<Button>().Single(b => b.AutomationId == "phone-stories-read-book");
            owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser);
            int depth = page.Navigation.NavigationStack.Count;
            await ui.BeginAsyncVoid(() => ((IButtonController)read).SendClicked());
            Require(page.Navigation.NavigationStack.Count == depth,
                "An owner A-B-A transition revived a stale Stories button.");
            typeof(PhoneStoriesPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            Require(!IssuedElements(page).Any(e => e.AutomationId == "phone-stories-read-book"),
                "A stale owner projection still exposes its private book.");
            IssuedPageLifecycle(page, "OnDisappearing");
            Require(!IssuedElements(page).OfType<Label>().Any(), "The cached hidden tab retained private runner text.");
            await ui.BeginAsyncVoid(() => ((IButtonController)read).SendClicked());
            Require(page.Navigation.NavigationStack.Count == depth, "A departed tab opened an old runner's book.");
            Require(alerts.Titles.Count == 0, "Private Stories navigation raised an unexpected error.");
            ui.AssertHealthy();
            Console.WriteLine("PASS private Stories: empty/unsupported selection, real retained-reader navigation, explicit runner return, stale owner ABA/departure rejection, private text cleanup");
        });
    }
}
