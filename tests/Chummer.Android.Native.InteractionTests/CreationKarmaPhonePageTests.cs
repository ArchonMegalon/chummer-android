using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private static async Task RunKarmaPhonePagesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            KarmaNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners,
                karmaDecorator: actual => probe = new(actual, ui));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Karma phone pages", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            Require(runtime.Coordinator.CanOpenCreationKarma(), "Native Karma entry is unavailable after actual bootstrap.");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var root = new CreationKarmaPage(runtime.Coordinator);
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(root, false);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(root, window);
            await alerts.PreflightAsync();
            await Appear();
            await Click("karma-open-metatype");
            Button human = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Human");
            await Click(human.AutomationId);
            await Click("karma-open-talent");
            await Click("karma-talent-mundane");
            await Click("karma-open-attributes");
            Stepper oldAgility = Element<Stepper>("karma-attribute-AGI");
            oldAgility.Value = 1;
            Require(Element<Label>("creation-karma-budget").Text == CreationKarmaCopy.Pending,
                "Attribute editing displayed stale budget totals as current.");
            await Back();
            await Click("karma-open-attributes");
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1,
                "Back navigation silently dropped the uncommitted attribute selection.");
            oldAgility.Value = 2;
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1,
                "An obsolete native Stepper overwrote the returned editor.");
            await Back();
            await Click("karma-open-skills");
            await Click("karma-filter-knowledge");
            await Search("English");
            var english = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "English");
            await Click(english.AutomationId);
            Element<Switch>("karma-native-language").IsToggled = true;
            await Click("karma-use-skill");
            await Click("karma-filter-active");
            await Search("Pistols");
            var pistols = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Pistols");
            await Click(pistols.AutomationId);
            Element<Stepper>("karma-skill-levels").Value = 1;
            await Click("karma-use-skill");
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision
                && probe!.ConfirmCalls == 0, "Selecting phone options mutated the workspace without review.");
            await Back();
            await Click("karma-open-review");
            Require(Element<Button>("karma-confirm").IsEnabled, "The exact Human/Mundane/native-language/Pistols review is not confirmable.");
            await Click("karma-confirm");
            Require(Element<Label>("creation-karma-saved").Text == CreationKarmaCopy.Saved
                && !IssuedElements(Current()).OfType<Button>().Any(b => b.AutomationId == "karma-confirm" && b.IsEnabled),
                "Saved review did not expose its known result or offered another write.");
            var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var decision = cold.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions!.Single();
            Require(probe!.ConfirmCalls == 1 && cold.ContentRevision == before.ContentRevision + 1
                && cold.Document.Content == before.Document.Content
                && decision.Quote.Attributes!.Attributes.Single(a => a.AttributeId == "AGI").Current == 2
                && decision.Quote.Skills!.Skills.Single(s => s.Name == "Pistols").Rating == 1
                && decision.Quote.Skills.NativeLanguagesUsed == 1,
                "Native review did not persist exactly the selected source-bound pending foundation.");
            // Recreate all page/session objects and reread the actual durable
            // store. This is managed reopen evidence, not an Android process restart.
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await HydrateFinalizationOwnerAsync(runtime, owners, cold);
            var reopened = new CreationKarmaPage(runtime.Coordinator);
            await navigation.PushAsync(reopened, false);
            await Appear();
            await Click("karma-open-attributes");
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1, "Reopened phone page lost its durable allocation.");
            await Back();
            await Click("karma-open-skills");
            Require(IssuedElements(Current()).OfType<Button>().Count(b => b.AutomationId?.StartsWith("karma-selected-skill-", StringComparison.Ordinal) == true) == 2,
                "Reopened phone page lost the native language or active skill.");
            IssuedPageLifecycle(Current(), "OnDisappearing");
            var prior = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
                Require(CreationKarmaCopy.Title == "Karma-Grunddaten", "German regional resources are missing.");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-MX");
                Require(CreationKarmaCopy.Confirm == "Confirmar y guardar borrador", "Spanish regional resources are missing.");
            }
            finally { CultureInfo.CurrentUICulture = prior; }
            ui.AssertHealthy();
            Console.WriteLine("PASS Karma native phone deep pages: explicit choices, stale controls, draft Back, review/save, cold reopen, DE/ES resources");

            CreationKarmaPage Current() => (CreationKarmaPage)navigation.Navigation.NavigationStack.Last();
            T Element<T>(string automationId) where T : Element
                => IssuedElements(Current()).OfType<T>().Single(e => e.AutomationId == automationId);
            async Task Appear()
            {
                var page = Current();
                if (IssuedPageField<int>(page, "_subscribed") == 0)
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            }
            async Task Click(string automationId)
            {
                var previous = Current();
                var button = Element<Button>(automationId);
                Require(button.IsEnabled, "Button disabled: " + automationId);
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0)
                    IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
            async Task Back()
            {
                IssuedPageLifecycle(Current(), "OnDisappearing");
                await navigation.PopAsync(false);
                await Appear();
            }
            async Task Search(string term)
            { Element<SearchBar>("karma-skill-search").Text = term; await Click("karma-search"); }
        });
    }
}
