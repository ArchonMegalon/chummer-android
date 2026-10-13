using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleWizardFlowAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
          foreach (string scenario in new[] { "current", "departure-return", "stack-change", "owner-aba" })
          {
            var owners = new ControlledLinkedOwner();
            LifeCompletionNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, lifeModuleInputDrafts: true, linkedOwners: owners,
                lifeCompletionDecorator: actual => probe = new(actual));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life guided completion", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
            await runtime.Presenter.LoadAsync(id, default);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var original = store.Get(id).Value!;
            string originalBytes = JsonSerializer.Serialize(original);
            var root = new ContentPage();
            var navigation = new NavigationPage(root);
            var overview = new LifeModuleCompletionPage(runtime.Coordinator);
            await navigation.PushAsync(overview, false);
            var window = new Window(navigation);
            await Appear();
            var session = (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overview)!;
            if (scenario == "current" && Environment.GetEnvironmentVariable("CHUMMER_LIFE_WIZARD_SMOKE_DIRECTORY") is { Length: > 0 } smokeDirectory)
            {
                Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                    "Synthetic wizard export requires a new explicit directory.");
                Directory.CreateDirectory(smokeDirectory);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: false);
                }
                Console.WriteLine("Life Modules guided-completion synthetic fixture: " + id.Value);
            }

            await Click("life-open-qualities");
            await Click("life-review-qualities");
            Require(Has("life-review-qualities") && session.Preview?.EffectWriteSummary is null,
                "Unanswered quality details were skipped.");
            foreach (var entry in IssuedElements(Current()).OfType<Entry>()
                         .Where(e => e.AutomationId?.StartsWith("life-quality-", StringComparison.Ordinal) == true)) entry.Text = "Renraku";
            var qualityPage = (NativePageBase)Current();
            var continueButton = Element<Button>("life-review-qualities");
            var unrelated = new ContentPage();
            probe!.AfterPreview = () =>
            {
                probe.AfterPreview = null;
                var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ui.Post(async _ =>
                {
                    try
                    {
                        ((IButtonController)continueButton).SendClicked();
                        if (scenario == "departure-return")
                        {
                            IssuedPageLifecycle(qualityPage, "OnDisappearing");
                            IssuedPageLifecycle(qualityPage, "OnAppearing");
                        }
                        if (scenario == "stack-change") await navigation.PushAsync(unrelated, false);
                        observed.TrySetResult();
                    }
                    catch (Exception error) { observed.TrySetException(error); }
                }, null);
                Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Wizard review blocked the UI thread.");
                observed.Task.GetAwaiter().GetResult();
                if (scenario == "owner-aba") { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); }
            };
            int beforeContinue = probe.PreviewCalls;
            await Click("life-review-qualities");
            // Returning to the page needs one new-generation read; a duplicate
            // tap must not add a third review or any navigation/write.
            Require(probe.PreviewCalls == beforeContinue + (scenario == "departure-return" ? 2 : 1),
                "Unexpected wizard review count: " + scenario + " / " + (probe.PreviewCalls - beforeContinue));
            if (scenario != "current")
            {
                Require(ReferenceEquals(Current(), scenario == "stack-change" ? unrelated : qualityPage),
                    "Late wizard review stole navigation: " + scenario);
                Require(JsonSerializer.Serialize(store.Get(id).Value!) == originalBytes && probe.ConfirmCalls == 0
                    && owners.ActiveLeases == 0, "Canceled wizard continuation mutated the runner or leaked an owner lease.");
                IssuedPageLifecycle(qualityPage, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS actual Core/MAUI Life Modules wizard continuation guard: " + scenario);
                continue;
            }
            Require(Has("life-talent-mundane"), "Accepted qualities still require Back and manual Talent navigation: "
                + $"ready={session.Ready}, reviewed={session.Reviewed}, saved={session.Saved}, effect={session.Preview?.EffectWriteSummary is not null}, "
                + "sequence=" + string.Join(",", session.Preview?.ModuleSequence?.Blockers ?? [])
                + "; blockers=" + string.Join(",", session.Blockers));
            await Click("life-continue-talent");
            Require(Has("life-talent-mundane") && session.Input!.TalentSelection is null,
                "Continue silently chose a talent.");
            await Click("life-talent-mundane");
            int talentReviews = probe!.PreviewCalls;
            await Click("life-continue-talent");
            Require(Has("life-begin-attributes") && probe.PreviewCalls == talentReviews,
                "Talent continuation repeated an unchanged Core quote or missed Attributes.");
            await Click("life-begin-attributes");
            await Click("life-review-attributes");
            Require(Has("life-begin-skills"), "Attributes did not continue to Skills.");
            await Click("life-begin-skills");
            await Click("life-continue-skills");
            Require(Has("life-continue-skills") && session.Preview!.SkillsQuote!.Blockers
                .Contains(CharacterCreationSkillsBlockers.NativeLanguageRequired), "Skills skipped the native-language choice.");
            Element<SearchBar>("life-search").Text = "English";
            await Click("life-search-go");
            await Click(IssuedElements(Current()).OfType<Button>().Single(x => x.Text == "English").AutomationId);
            Element<Switch>("life-native-language").IsToggled = true;
            await Click("life-use-skill");
            await Click("life-continue-skills");
            Require(Has("life-resource-investment"), "Skills did not continue to Resources.");
            decimal excessive = session.Preview!.ResourcesPolicy!.MaximumKarmaInvestment + 1;
            Element<Entry>("life-resource-investment").Text = excessive.ToString(CultureInfo.CurrentCulture);
            await Click("life-use-resources");
            Require(Has("life-resource-investment") && session.Preview!.ResourcesQuote!.Blockers.Count > 0,
                "Invalid Resources left its editor.");
            Element<Entry>("life-resource-investment").Text = "0";
            await Click("life-use-resources");
            Require(Has("life-begin-gear"), "Resources did not continue to Gear.");
            await Click("life-begin-gear");
            await Click("life-review-gear");
            Require(Has("life-begin-lifestyles"), "Gear did not continue to Lifestyles.");
            await Click("life-begin-lifestyles");
            await Click("life-review-lifestyles");
            Require(Has("life-begin-contacts"), "Lifestyles did not continue to Contacts.");
            await Click("life-begin-contacts");
            await Click("life-add-contact");
            Element<Entry>("life-contact-name").Text = "Mira";
            Element<Entry>("life-contact-role").Text = "Fixer";
            await Click("life-use-contact");
            await Click("life-continue-contacts");
            Require(Has("life-begin-magic"), "Contacts did not continue to Magic.");
            await Click("life-begin-magic");
            await Click("life-review-magic");
            Require(Has("life-starting-dice"), "Magic did not continue to final review.");
            Require(probe.ConfirmCalls == 0 && JsonSerializer.Serialize(store.Get(id).Value!) == originalBytes,
                "Guided draft navigation changed the canonical runner.");

            string inputBytes = JsonSerializer.Serialize(session.Input);
            var cold = new LifeModuleCompletionSession(runtime.Coordinator, new(runtime.StateDirectory));
            await cold.OpenAsync(default, () => true);
            Require(cold.Reviewed && cold.Saved && JsonSerializer.Serialize(cold.Input) == inputBytes,
                "A fresh session lost the wizard selections.");
            // The cold session supersedes the previous preview; reopen the actual
            // final page before explicit confirmation instead of reusing its grant.
            IssuedPageLifecycle((NativePageBase)Current(), "OnDisappearing");
            var reopened = new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory));
            await navigation.PushAsync(reopened, false);
            await Appear();
            await Click("life-open-review");
            Element<Entry>("life-starting-dice").Text = "6";
            await Click("life-review-completion");
            var finalSession = (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Current())!;
            Require(finalSession.CanConfirm, "Guided inputs could not finalize: " + string.Join(", ", finalSession.Blockers));
            var budget = finalSession.Preview!.FinalizationBudget!;
            Element<Switch>("life-completion-confirmed").IsToggled = true;
            await Click("life-confirm-completion");
            var saved = store.Get(id).Value!;
            Require(ReferenceEquals(Current(), root) && probe.ConfirmCalls == 1
                && saved.ContentRevision == original.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                && finalSession.Receipt is { CharacterCreated: true }, "Wizard did not finalize exactly once and open Career.");
            var xml = System.Xml.Linq.XDocument.Parse(saved.Document.Content).Root!;
            Require(decimal.Parse(xml.Element("karma")!.Value, CultureInfo.InvariantCulture) == budget.KarmaCarried
                && decimal.Parse(xml.Element("nuyen")!.Value, CultureInfo.InvariantCulture) == budget.CareerNuyen,
                "Saved runner differs from the exact reviewed Karma/nuyen budget.");
            RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
            Require(owners.ActiveLeases == 0, "Wizard leaked an owner lease.");
            ui.AssertHealthy();
            Console.WriteLine("PASS actual Core/MAUI guided Life Modules completion: all sections forward, invalid inputs stay, fresh draft reopen, one Career commit");

            ContentPage Current() => (ContentPage)navigation.Navigation.NavigationStack.Last();
            bool Has(string key) => IssuedElements(Current()).Any(x => x.AutomationId == key);
            T Element<T>(string key) where T : Element => IssuedElements(Current()).OfType<T>().Single(x => x.AutomationId == key);
            async Task Appear()
            {
                if (Current() is NativePageBase page && IssuedPageField<int>(page, "_subscribed") == 0)
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
            }
            async Task Click(string key)
            {
                var previous = Current();
                var button = Element<Button>(key);
                Require(button.IsEnabled, "Disabled wizard action: " + key);
                using var alerts = new IssuedPageAlerts(previous, window);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked()));
                Require(alerts.Titles.Count == 0, "Unexpected wizard alert: " + string.Join("; ", alerts.Messages));
                if (!ReferenceEquals(previous, Current()) && previous is NativePageBase previousPage
                    && IssuedPageField<int>(previousPage, "_subscribed") != 0) IssuedPageLifecycle(previousPage, "OnDisappearing");
                await Appear();
            }
          }
        });
    }
}
