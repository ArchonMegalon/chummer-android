using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleApprenticePagesAsync(string contentRoot)
    {
        const string talentId = "c1d4d7ec-9ebb-4e85-ae72-8155b0f27478";
        const string airId = "380a4860-e5b7-4d07-9b8f-24951c1d656a";
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            LifeCompletionNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners, lifeCompletionDecorator: actual => probe = new(actual),
                lifeModuleInputDrafts: true);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Apprentice category choices", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
            await runtime.Presenter.LoadAsync(id, default);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            string before = JsonSerializer.Serialize(store.Get(id).Value!);
            var drafts = new LifeModuleCompletionDraftStore(runtime.StateDirectory);
            var dashboard = new BuildPage(runtime.Coordinator);
            // Headless MAUI supplies geometry, never rules or route admission.
            ((ScrollView)dashboard.Content!).ScrollToRequested += (_, _) => ((ScrollView)dashboard.Content!).SendScrollFinished();
            var navigation = new NavigationPage(dashboard);
            var window = new Window(navigation);
            await Appear();
            Require(runtime.Coordinator.CanOpenLifeModuleCompletion(), "The Core-finished module sequence cannot open final review.");
            await Click("creation-life-module-continue");
            Require(Current() is LifeModuleCompletionPage,
                "Completed modules without an Origin timeline did not open their Core-governed completion wizard.");
            Require(JsonSerializer.Serialize(store.Get(id).Value!) == before,
                "Opening the finished module sequence mutated the runner.");
            await Click("life-open-qualities");
            foreach (var entry in IssuedElements(Current()).OfType<Entry>().Where(row => row.AutomationId?.StartsWith("life-quality-", StringComparison.Ordinal) == true))
                entry.Text = "Renraku";
            await Click("life-review-qualities");
            await Back();
            await Click("life-open-talent");
            await Click("life-talent-" + talentId);
            var catalog = Session().Preview!.TalentCatalog!.Options.Single(row => row.OptionId == talentId).Restrictions!;
            var spell = Element<Picker>("life-talent-spell-category");
            var spirit = Element<Picker>("life-talent-spirit-category");
            Require(spell.SelectedIndex == -1 && spirit.SelectedIndex == -1 && Session().Input!.TalentSelection!.Restrictions is null,
                "Apprentice silently selected a spell or spirit category.");
            Require(spell.TextColor == NativeTheme.Text && spirit.TextColor == NativeTheme.Text
                && spell.BackgroundColor == NativeTheme.Surface && spirit.BackgroundColor == NativeTheme.Surface,
                "Apprentice choices lost the readable phone theme.");
            Require(!spirit.Items.Any(value => Guid.TryParse(value, out _)), "The spirit picker exposes source GUIDs.");
            foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
            {
                var previousCulture = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentUICulture = new(locale);
                    Require(CreationKarmaCopy.Blocker(CharacterCreationLifeModuleTalentCatalog.RestrictionsRequired)
                        != CharacterCreationLifeModuleTalentCatalog.RestrictionsRequired, "Missing readable category guidance: " + locale);
                    Require(CreationKarmaCopy.Blocker(CharacterCreationLifeModuleResourcesQuote.SelectionRequired)
                        != CharacterCreationLifeModuleResourcesQuote.SelectionRequired, "Missing readable Resources guidance: " + locale);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
            }
            spell.SelectedIndex = Array.FindIndex(catalog.SpellCategories.ToArray(), row => row.Value == "Combat");
            await Click("life-review-talent");
            Require(Session().Input!.TalentSelection!.Restrictions is { SpellCategory: "Combat", SpiritSourceId: "" }
                && Session().Preview!.TalentWriteSummary is null && !Session().CanConfirm,
                "One category defaulted the other or issued a partial talent grant.");
            Element<Picker>("life-talent-spirit-category").SelectedIndex = Array.FindIndex(catalog.SpiritCategories.ToArray(), row => row.SourceId == airId);
            await Click("life-review-talent");
            var expected = new CharacterCreationTalentRestrictionSelection("Combat", airId);
            Require(Session().Input!.TalentSelection!.Restrictions == expected && Session().Preview!.TalentWriteSummary?.KarmaCost == 15
                && Session().Saved, "The two live controls overwrote each other or did not reach the Core review.");
            string inputs = JsonSerializer.Serialize(Session().Input);
            spell.SelectedIndex = Array.FindIndex(catalog.SpellCategories.ToArray(), row => row.Value == "Health");
            spirit.SelectedIndex = 0;
            Require(JsonSerializer.Serialize(Session().Input) == inputs, "Retired controls modified the newly reviewed choices.");
            await Back();
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await navigation.PushAsync(new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory)), false);
            await Appear();
            Require(JsonSerializer.Serialize(Session().Input) == inputs && Session().Reviewed,
                "A fresh page/session/store lost the explicit Apprentice choices.");
            await Click("life-open-talent");
            Require(Element<Picker>("life-talent-spell-category").SelectedItem?.ToString()
                    == catalog.SpellCategories.Single(row => row.Value == "Combat").Label
                && Element<Picker>("life-talent-spirit-category").SelectedItem?.ToString()
                    == catalog.SpiritCategories.Single(row => row.SourceId == airId).Label,
                "Reopened controls show different choices than the saved typed values.");
            if (Environment.GetEnvironmentVariable("CHUMMER_APPRENTICE_SMOKE_DIRECTORY") is { Length: > 0 } smokeDirectory)
            {
                Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                    "Apprentice smoke export requires a new explicit synthetic directory.");
                Directory.CreateDirectory(smokeDirectory);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: false);
                }
                Console.WriteLine("Apprentice synthetic smoke fixture: " + id.Value);
            }
            var oldSpell = Element<Picker>("life-talent-spell-category");
            await Click("life-talent-mundane");
            string mundane = JsonSerializer.Serialize(Session().Input);
            oldSpell.SelectedIndex = 0;
            Require(Session().Input!.TalentSelection is { OptionId: "mundane", Restrictions: null }
                && !IssuedElements(Current()).Any(row => row.AutomationId == "life-talent-spell-category")
                && JsonSerializer.Serialize(Session().Input) == mundane,
                "Changing talent retained restrictions or accepted a departed picker event.");
            await Click("life-talent-" + talentId);
            var ownerSpell = Element<Picker>("life-talent-spell-category");
            string ownerInput = JsonSerializer.Serialize(Session().Input);
            owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser);
            ownerSpell.SelectedIndex = 0;
            Require(!Session().Ready && JsonSerializer.Serialize(Session().Input) == ownerInput,
                "Owner A→B→A accepted an old category picker.");
            Require(JsonSerializer.Serialize(store.Get(id).Value!) == before && probe!.ConfirmCalls == 0 && owners.ActiveLeases == 0,
                "Draft category choices changed runner data or dispatched finalization.");
            IssuedPageLifecycle(Current(), "OnDisappearing");
            Console.WriteLine("PASS Apprentice actual-MAUI pickers: explicit categories, readable labels, source-backed Core review, private draft cold reopen, retired-control and owner-ABA guards; no runner mutation");

            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
            LifeModuleCompletionSession Session() => (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Current())!;
            T Element<T>(string key) where T : Element => IssuedElements(Current()).OfType<T>().Single(row => row.AutomationId == key);
            async Task Appear()
            {
                if (IssuedPageField<int>(Current(), "_subscribed") == 0)
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(Current(), "OnAppearing")).WaitAsync(TimeSpan.FromSeconds(30));
            }
            async Task Click(string key)
            {
                var previous = Current(); var button = Element<Button>(key);
                Require(button.IsEnabled, "Disabled Apprentice action: " + key);
                using (var alerts = new IssuedPageAlerts(previous, window))
                {
                    await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked()).WaitAsync(TimeSpan.FromSeconds(45));
                    Require(alerts.Titles.Count == 0, "Unexpected Apprentice alert: " + string.Join("; ", alerts.Messages));
                }
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0)
                    IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
            async Task Back()
            { IssuedPageLifecycle(Current(), "OnDisappearing"); await navigation.PopAsync(false); await Appear(); }
        });
    }
}
