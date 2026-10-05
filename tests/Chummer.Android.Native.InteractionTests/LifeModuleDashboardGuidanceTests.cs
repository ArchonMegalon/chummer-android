using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Presentation.OriginBooks;
using Chummer.Run.Contracts.Community;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleDashboardGuidanceAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(OwnerScope.LocalSingleUser);
            LifeBookReadProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners,
                lifeBookDecorator: actual => probe = new(actual));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await CreateRunner();
            var page = new BuildPage(runtime.Coordinator);
            // Headless MAUI has no native scroll handler. Acknowledge geometry
            // only; all story state still comes from the real coordinator.
            var scroll = (ScrollView)page.Content!;
            scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
            var window = new Window(new NavigationPage(page));
            using var alerts = new IssuedPageAlerts(page, window);
            await alerts.PreflightAsync();
            var copy = AndroidSurfaceStrings.Resolve();
            await Observe(copy["Origin.OpeningSetupRequired"], false, "fresh runner");
            probe!.Transform = _ => new(LifeModuleOriginDossierOutcomes.Missing, null,
                ["life-module-origin-history-unreadable"]);
            await Observe(copy["Origin.BookUnavailable"], false, "unreadable ledger");
            probe.Transform = _ => throw new IOException("Synthetic reading failure.");
            await Observe(copy["Origin.BookUnavailable"], false, "read failure");
            probe.Transform = null;
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 1));
            await runtime.Presenter.LoadAsync(id, default);
            await Observe(copy["Origin.OpeningSetupRequired"], false, "birth chosen, childhood pending");

            await CreateRunner();
            id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeStory(runtime, id, stopAfterDecisions: 2));
            await runtime.Presenter.LoadAsync(id, default);
            await Observe(copy["Origin.ReaderFullTextPending"], false, "opening complete, no prose");
            var book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            var chapter = book.Chapters.Last();
            var draft = OriginBookProseDraft.Create(chapter, book.Locale,
                OriginChapterSourceIdentity.RequestId(book.AuthoringSource(chapter)), new string('f', 64),
                "Synthetic full chapter for the dashboard-state regression, not provider evidence.");
            book = (await runtime.Coordinator.StageOriginBookProseDraftAsync(book, draft, () => true, default))!;
            await Observe(copy["Origin.MechanicsAfterReading"], false, "returned prose, not read");
            book = (await runtime.Coordinator.LoadRetainedOriginBookAsync(default, () => true))!;
            book = (await runtime.Coordinator.ReviewOriginBookProseDraftAsync(book, draft, true, true, () => true, default))!;
            Require(book.HasReadCurrentStory, "The fixture failed to acknowledge the chapter.");
            await Observe(CreationAllocationStrings.Get("LifeDashboard.StoryHelp", ""), true, "read chapter reopened from disk");
            Require(alerts.Titles.Count == 0, "Dashboard guidance raised an unexpected blocking alert.");
            ui.AssertHealthy();

            async Task CreateRunner()
            {
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Opening guidance fixture", default);
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                await runtime.Coordinator.SaveAsync();
            }

            async Task Observe(string expected, bool showMechanics, string scenario)
            {
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"))
                    .WaitAsync(TimeSpan.FromSeconds(30));
                var elements = IssuedElements(page).ToArray();
                Require(elements.OfType<Label>().Single(e => e.AutomationId == "creation-life-module-scope").Text == expected,
                    "Wrong Life Modules dashboard guidance: " + scenario);
                Require(elements.Any(e => e.AutomationId == "creation-life-module-budget") == showMechanics,
                    "Dashboard mechanics were revealed before reading: " + scenario);
                var next = elements.Single(e => e.AutomationId == "creation-life-module-continue");
                Require(SemanticProperties.GetDescription(next).Contains(expected, StringComparison.Ordinal),
                    "The next-decision card contradicts the dashboard: " + scenario);
                IssuedPageLifecycle(page, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS Life Modules dashboard guidance: " + scenario);
            }
        });
    }
}
