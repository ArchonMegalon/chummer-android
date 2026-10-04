using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleExoticSkillsAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            LifeCompletionNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, lifeModuleInputDrafts: true,
                lifeCompletionDecorator: actual => probe = new(actual));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Two exotic skills", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
            await runtime.Presenter.LoadAsync(id, default);
            var files = new FileWorkspaceStore(runtime.StateDirectory);
            string originalRunner = JsonSerializer.Serialize(files.Get(id).Value!);
            var root = new BuildPage(runtime.Coordinator);
            ((ScrollView)root.Content!).ScrollToRequested += (_, _) => ((ScrollView)root.Content!).SendScrollFinished();
            var navigation = new NavigationPage(root);
            var window = new Window(navigation);
            await Appear();
            await Click("creation-life-module-continue");
            await Click("life-open-qualities");
            foreach (var entry in IssuedElements(Current()).OfType<Entry>().Where(x => x.AutomationId?.StartsWith("life-quality-", StringComparison.Ordinal) == true))
                entry.Text = "Renraku";
            await Click("life-review-qualities"); await Back();
            await Click("life-open-talent"); await Click("life-talent-mundane"); await Back();
            await Click("life-open-attributes"); await Click("life-begin-attributes"); await Back();
            await Click("life-open-skills"); await Click("life-begin-skills");
            var source = Session().Preview!.SkillsCatalog!.ActiveSkills.First(x => x.IsExotic && x.Specializations.Count >= 2
                && Session().Preview!.SkillsQuote!.AllowedActiveSkillSourceIds.Contains(x.SourceSkillId));
            var variants = source.Specializations.Take(2).ToArray();
            Console.WriteLine("Exotic fixture: " + source.Name + " / " + string.Join(" / ", variants.Select(x => x.Name)));
            await AddVariant(0, 2);
            await AddVariant(1, 1);
            AssertAllocations(2, 1);
            var selected = IssuedElements(Current()).OfType<Button>().Where(x => x.AutomationId?.StartsWith("life-selected-skill-", StringComparison.Ordinal) == true).ToArray();
            Require(selected.Length == 2 && selected.Select(x => x.AutomationId).Distinct().Count() == 2
                && selected.All(button => button.LineBreakMode == LineBreakMode.WordWrap && button.HeightRequest == -1
                    && button.MinimumHeightRequest >= 44)
                && variants.All(variant => selected.Any(button => button.Text.Contains(variant.Name, StringComparison.Ordinal))),
                "Exotic rows lack distinct exact identities and readable variant names.");
            await Click(selected.Single(x => x.Text.Contains(variants[1].Name, StringComparison.Ordinal)).AutomationId);
            Require(Element<Stepper>("life-skill-levels").Value == 1, "Second exotic row opened the first allocation.");
            var retiredStepper = Element<Stepper>("life-skill-levels");
            retiredStepper.Value = 3;
            await Click("life-use-skill"); await SkillsPage();
            AssertAllocations(2, 3);
            string afterEdit = JsonSerializer.Serialize(Session().Input);
            retiredStepper.Value = 6;
            Require(JsonSerializer.Serialize(Session().Input) == afterEdit, "Departed skill control changed the retained choices.");
            await OpenSelected(0);
            await Click("life-remove-skill"); await SkillsPage();
            Require(Allocations().Length == 1 && Allocations()[0].SpecializationOptionId == variants[1].OptionId
                && Allocations()[0].KarmaLevels == 3, "Removing one exotic variant changed or deleted its sibling.");
            await AddVariant(0, 2);
            AssertAllocations(2, 3);
            // Catalog navigation for a selected variant edits it; it cannot append a duplicate.
            await OpenVariant(1);
            Require(Element<Stepper>("life-skill-levels").Value == 3, "Catalog route lost the selected variant's rating.");
            await Click("life-use-skill"); await SkillsPage();
            AssertAllocations(2, 3);
            Require(JsonSerializer.Serialize(files.Get(id).Value!) == originalRunner && probe!.ConfirmCalls == 0,
                "Editing input choices mutated the runner before final confirmation.");
            await Back();
            string expectedInputs = JsonSerializer.Serialize(Session().Input);
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await navigation.PushAsync(new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory)), false);
            await Appear();
            Require(JsonSerializer.Serialize(Session().Input) == expectedInputs && Session().Reviewed,
                "A fresh page/session/store lost either exotic variant or its exact rating.");
            await Click("life-open-skills"); await OpenSelected(1);
            Require(Element<Stepper>("life-skill-levels").Value == 3, "Cold draft reopen opened the wrong exotic variant.");
            await Click("life-use-skill"); await SkillsPage(); await Back();
            var native = Session().Preview!.SkillsCatalog!.KnowledgeSkills.First(x => x.CanBeNativeLanguage);
            Session().Change(Session().Input! with
            {
                SkillSelection = Session().Input!.SkillSelection! with
                { Skills = Session().Input!.SkillSelection!.Skills.Append(new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)).ToArray() },
                KarmaResourceInvestment = 0, GearSelection = [], LifestyleSelection = [], ContactSelection = [],
                MagicSelection = new(null, null, [], [], []), StartingNuyenDiceTotal = 6
            });
            await Session().ReviewAsync(default, () => true);
            Require(Session().CanConfirm, "Core rejected the two legal exotic instances: " + string.Join(", ", Session().Blockers));
            var quoted = Session().Preview!.SkillsQuote!.Skills.Where(x => x.Allocation.SourceSkillId == source.SourceSkillId).ToArray();
            Require(quoted.Length == 2 && quoted.All(x => x.Allocation.SpecializationPayment == CharacterCreationKarmaSpecializationPayments.ExoticIdentity),
                "Core did not quote both exact exotic identities independently.");
            // Ordinary active specializations cannot spend the knowledge pool.
            // Exercise the real controls and Core quote, including the valid
            // knowledge-skill alternative and removal of an active specialization.
            var ordinary = Session().Preview!.SkillsCatalog!.ActiveSkills.First(x => !x.IsExotic
                && x.Specializations.Any(s => !s.Name.Contains('[', StringComparison.Ordinal))
                && Session().Preview!.SkillsQuote!.AllowedActiveSkillSourceIds.Contains(x.SourceSkillId)
                && Session().Preview!.SkillsQuote!.Skills.All(s => s.Allocation.SourceSkillId != x.SourceSkillId));
            var ordinarySpec = ordinary.Specializations.First(s => !s.Name.Contains('[', StringComparison.Ordinal));
            await Click("life-open-skills");
            await OpenSkill(ordinary.Name, ordinary.SourceSkillId);
            Element<Stepper>("life-skill-levels").Value = 1;
            await Click("life-specialization-" + ordinarySpec.OptionId);
            Require(Session().CanConfirm, "Core rejected the legal active specialization: " + string.Join(", ", Session().Blockers));
            var legalActiveInput = Session().Input!;
            Session().Change(legalActiveInput with { SkillSelection = legalActiveInput.SkillSelection! with
            { Skills = legalActiveInput.SkillSelection.Skills.Select(x => x.SourceSkillId == ordinary.SourceSkillId
                ? x with { SpecializationPayment = CharacterCreationKarmaSpecializationPayments.KnowledgePoint } : x).ToArray() } });
            await Session().ReviewAsync(default, () => true);
            Require(!Session().CanConfirm && Session().Preview!.SkillsQuote!.Skills.Single(x =>
                x.Allocation.SourceSkillId == ordinary.SourceSkillId).Blockers.Contains(CharacterCreationSkillsBlockers.SpecializationInvalid),
                "Core admitted an active specialization paid with knowledge points.");
            Session().Change(legalActiveInput);
            await Session().ReviewAsync(default, () => true);
            Require(!IssuedElements(Current()).OfType<Switch>().Any(x => x.AutomationId == "life-specialization-knowledge"),
                "An active-skill specialization exposes the invalid knowledge-point payment choice.");
            AssertRemovalCopy();
            await Click("life-remove-specialization");
            Require(Session().Input!.SkillSelection!.Skills.Single(x => x.SourceSkillId == ordinary.SourceSkillId)
                is { SpecializationOptionId: null, SpecializationPayment: null, KarmaLevels: 1 },
                "Removing an active specialization removed its skill or retained its payment.");
            await Click("life-specialization-" + ordinarySpec.OptionId);
            await Click("life-use-skill");
            var knowledge = Session().Preview!.SkillsCatalog!.KnowledgeSkills.First(x => !x.CanBeNativeLanguage
                && x.Specializations.Any(s => !s.Name.Contains('[', StringComparison.Ordinal)));
            var knowledgeSpec = knowledge.Specializations.First(s => !s.Name.Contains('[', StringComparison.Ordinal));
            await OpenSkill(knowledge.Name, knowledge.SourceSkillId);
            Element<Stepper>("life-knowledge-levels").Value = 1;
            await Click("life-specialization-" + knowledgeSpec.OptionId);
            Element<Switch>("life-specialization-knowledge").IsToggled = true;
            await Click("life-use-skill");
            Require(Session().CanConfirm && Session().Preview!.SkillsQuote!.Skills.Single(x =>
                x.Allocation.SourceSkillId == knowledge.SourceSkillId) is { KnowledgePointCost: 2, SpecializationKarmaCost: 0 },
                "A legal knowledge specialization could not use knowledge points.");
            await Back();
            string specializedInputs = JsonSerializer.Serialize(Session().Input);
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await navigation.PushAsync(new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory)), false);
            await Appear();
            Require(Session().CanConfirm && JsonSerializer.Serialize(Session().Input) == specializedInputs,
                "A fresh completion session lost the specialization payments.");
            await Click("life-open-skills");
            await OpenSkill(knowledge.Name, knowledge.SourceSkillId);
            AssertRemovalCopy();
            await Back();
            await OpenSkill(ordinary.Name, ordinary.SourceSkillId);
            var retainedChoices = Session().Input!.SkillSelection!.Skills.Where(x => x.SourceSkillId != ordinary.SourceSkillId).ToArray();
            await Click("life-remove-skill");
            Require(Session().Input!.SkillSelection!.Skills.SequenceEqual(retainedChoices)
                && JsonSerializer.Serialize(files.Get(id).Value!) == originalRunner,
                "Removing this skill's added choices changed another choice or the module-granted runner.");
            await Back();
            string removedInputs = JsonSerializer.Serialize(Session().Input);
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await navigation.PushAsync(new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory)), false);
            await Appear();
            Require(Session().CanConfirm && JsonSerializer.Serialize(Session().Input) == removedInputs,
                "Cold draft reopen restored removed choices or lost a retained skill.");
            await Click("life-open-skills");
            await OpenSkill(ordinary.Name, ordinary.SourceSkillId);
            Require(!Element<Button>("life-remove-skill").IsEnabled,
                "An absent added skill allocation still offered removal of module grants.");
            Element<Stepper>("life-skill-levels").Value = 1;
            await Click("life-specialization-" + ordinarySpec.OptionId);
            await Click("life-use-skill");
            await Back();
            Console.WriteLine("PASS Life Modules removal: distinct English/German wrapping labels; specialization preserves rating; skill removal preserves all other choices/module runner; cold removal reopen and re-add");
            Console.WriteLine("PASS Life Modules specialization payments: Core rejects active knowledge payment; active UI excludes it; removal retained; knowledge payment accepted; fresh draft reopen");
            if (Environment.GetEnvironmentVariable("CHUMMER_LIFE_EXOTIC_SMOKE_DIRECTORY") is { Length: > 0 } export)
            {
                Require(Path.IsPathFullyQualified(export) && !Directory.Exists(export), "Smoke export requires a new explicit synthetic directory.");
                Directory.CreateDirectory(export);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(export, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: false);
                }
                Console.WriteLine("Exotic synthetic smoke fixture: " + id.Value);
            }
            await Click("life-open-review");
            Element<Switch>("life-completion-confirmed").IsToggled = true;
            await Click("life-confirm-completion");
            Require(Session().Receipt is { CharacterCreated: true } && probe!.ConfirmCalls == 1,
                "Explicit final confirmation failed or dispatched more than once.");
            var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var xml = System.Xml.Linq.XDocument.Parse(saved.Document.Content);
            var persisted = xml.Descendants("skill").Where(x => (string?)x.Element("suid") == source.SourceSkillId).ToArray();
            Require(persisted.Length == 2 && persisted.Select(x => (string?)x.Element("guid")).Distinct().Count() == 2
                && variants.All(v => persisted.Any(x => (string?)x.Element("specific") == v.Name)),
                "The saved Career runner lost an exotic variant identity.");
            await runtime.Presenter.LoadAsync(id, default);
            Require(JsonSerializer.Serialize(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!) == JsonSerializer.Serialize(saved),
                "Reopening Career changed the once-applied runner.");
            Console.WriteLine("PASS Life Modules exotic variants: independent add/edit/remove, exact readable rows, no duplicate, retired control, fresh draft reopen, Core review, one finalization and Career reread");
            IssuedPageLifecycle(Current(), "OnDisappearing");

            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
            LifeModuleCompletionSession Session() => (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Current())!;
            T Element<T>(string key) where T : Element => IssuedElements(Current()).OfType<T>().Single(x => x.AutomationId == key);
            CharacterCreationKarmaSkillAllocation[] Allocations() => Session().Input!.SkillSelection!.Skills.Where(x => x.SourceSkillId == source.SourceSkillId).ToArray();
            void AssertRemovalCopy()
            {
                var previousCulture = CultureInfo.CurrentUICulture;
                try
                {
                    foreach (var (culture, specializationText, skillText, help) in new[]
                    {
                        ("en-US", "Remove specialization", "Remove skill choices", "Only your added choices for this skill are removed. Life Module grants stay."),
                        ("de-DE", "Spezialisierung entfernen", "Fertigkeitsauswahl entfernen", "Nur deine zusätzliche Auswahl für diese Fertigkeit wird entfernt. Vorteile aus Lebensmodulen bleiben erhalten.")
                    })
                    {
                        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                        typeof(LifeModuleCompletionPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Current(), null);
                        var specialization = Element<Button>("life-remove-specialization");
                        var skill = Element<Button>("life-remove-skill");
                        Require(specialization.Text == specializationText && skill.Text == skillText
                            && Element<Label>("life-remove-skill-help").Text == help,
                            "Skill and specialization removal do not clearly identify their different scope in " + culture + ".");
                        Require(new[] { specialization, skill }.All(button => button.LineBreakMode == LineBreakMode.WordWrap
                            && button.HeightRequest == -1 && button.MinimumHeightRequest >= 44),
                            "Localized removal controls can clip at larger font sizes.");
                    }
                }
                finally
                {
                    CultureInfo.CurrentUICulture = previousCulture;
                    typeof(LifeModuleCompletionPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Current(), null);
                }
            }
            void AssertAllocations(int first, int second) => Require(Allocations().Length == 2
                && Allocations().Single(x => x.SpecializationOptionId == variants[0].OptionId).KarmaLevels == first
                && Allocations().Single(x => x.SpecializationOptionId == variants[1].OptionId).KarmaLevels == second,
                "Editing one exotic variant replaced another allocation.");
            async Task OpenVariant(int index)
            {
                Element<SearchBar>("life-search").Text = source.Name;
                await Click("life-search-go");
                await Click("life-skill-" + source.SourceSkillId);
                await Click("life-specialization-" + variants[index].OptionId);
            }
            async Task OpenSkill(string name, string skillId)
            {
                Element<SearchBar>("life-search").Text = name;
                await Click("life-search-go");
                await Click("life-skill-" + skillId);
            }
            async Task AddVariant(int index, int levels)
            {
                await OpenVariant(index);
                Require(Element<Stepper>("life-skill-levels").Value == 0,
                    "A new exotic variant inherited an existing variant's purchase instead of starting independently.");
                Element<Stepper>("life-skill-levels").Value = levels;
                await Click("life-use-skill"); await SkillsPage();
            }
            async Task OpenSelected(int index) => await Click(IssuedElements(Current()).OfType<Button>().Single(x =>
                x.AutomationId?.StartsWith("life-selected-skill-", StringComparison.Ordinal) == true
                && x.Text.Contains(variants[index].Name, StringComparison.Ordinal)).AutomationId);
            async Task SkillsPage() { while (Current().AutomationId != "life-completion-skills") await Back(); }
            async Task Appear()
            {
                if (IssuedPageField<int>(Current(), "_subscribed") == 0)
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(Current(), "OnAppearing")).WaitAsync(TimeSpan.FromSeconds(30));
            }
            async Task Click(string key)
            {
                var previous = Current(); var button = Element<Button>(key);
                Require(button.IsEnabled, "Disabled exotic-skill action: " + key);
                using (var alerts = new IssuedPageAlerts(previous, window))
                {
                    await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked()).WaitAsync(TimeSpan.FromSeconds(45));
                    Require(alerts.Titles.Count == 0, "Unexpected exotic-skill alert: " + string.Join("; ", alerts.Messages));
                }
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0)
                    IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
            async Task Back() { IssuedPageLifecycle(Current(), "OnDisappearing"); await navigation.PopAsync(false); await Appear(); }
        });
    }
}
