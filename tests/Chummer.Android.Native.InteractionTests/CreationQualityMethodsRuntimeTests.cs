using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationQualityMethodsAsync(string contentRoot)
    {
        AssertCreationQualityCatalogRatingIdentity();
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
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Quality methods native fixture", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            Require(runtime.Coordinator.CanOpenCreationKarma(), "SETUP: actual Karma bootstrap did not open.");
            var workspaceId = runtime.Coordinator.State.WorkspaceId!.Value;
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(workspaceId).Value!;
            var root = new CreationKarmaPage(runtime.Coordinator);
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(root, false);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(root, window);
            await alerts.PreflightAsync();
            await Appear();
            await Click("karma-open-metatype");
            await Click(IssuedElements(Current()).OfType<Button>().Single(button => button.Text == "Human").AutomationId);
            await Click("karma-open-talent");
            await Click("karma-talent-mundane");
            await Click("karma-open-attributes");
            await Back();
            await Click("karma-open-skills");
            await Click("karma-filter-knowledge");
            Element<SearchBar>("karma-skill-search").Text = "English";
            await Click("karma-search");
            await Click(IssuedElements(Current()).OfType<Button>().Single(button => button.Text == "English").AutomationId);
            Element<Switch>("karma-native-language").IsToggled = true;
            await Click("karma-use-skill");
            await Back();
            await Click("karma-open-qualities");

            var session = Session();
            var state = session.Authority!;
            var selection = session.Selection!;
            var catalog = state.QualitiesCatalog!;
            var spike = catalog.Options.Where(option => option.SourceId == Guid.Parse("60972204-7dc5-4882-be7d-781f533dabef"))
                .OrderBy(option => option.Rating).ToArray();
            Require(spike.Select(option => option.Rating).SequenceEqual([1, 2, 3])
                && spike.Select(option => option.KarmaCost).SequenceEqual([10, 20, 30]),
                "SETUP: the real Spike Resistance fixture no longer has the expected exact source choices.");
            var admitted = new List<CharacterCreationQualityCatalogOption>();
            foreach (var option in spike)
            {
                var result = await runtime.Coordinator.PreviewCreationKarmaAsync(state,
                    selection with { QualityOptionIds = [option.OptionId] });
                if (result is { Outcome: CharacterCreationFoundationOutcomes.Success,
                        Value: { Qualities: not null, KarmaBudget.IsExact: true } quote }
                    && quote.Blockers.All(reason => reason == CharacterCreationQualitiesBlockers.MetagenicImbalanced))
                    admitted.Add(option);
            }
            Require(admitted.Any(option => option.Rating == 1) && admitted.Any(option => option.Rating == 2),
                "SETUP: real Karma must admit both directions of the Spike Resistance rating change.");

            // Reissue the actual draft after the independent candidate quotes.
            await Back();
            await Click("karma-open-qualities");
            await SearchQualities("Spike Resistance");
            AssertSingleSpikeOffer();
            var firstPicker = RatingPicker();
            firstPicker.SelectedIndex = admitted.FindIndex(option => option.Rating == 1);
            await Click("karma-add-quality-" + spike[0].OptionId);
            AssertSelected(1, 10);

            var obsoletePicker = RatingPicker();
            obsoletePicker.SelectedIndex = admitted.FindIndex(option => option.Rating == 2);
            AssertOffer(2);
            var obsoleteAdd = Element<Button>("karma-add-quality-" + spike[1].OptionId);
            Require(session.Selection!.QualityOptionIds!.Single() == spike[0].OptionId,
                "Choosing an offered rating must not silently change the selected draft before its action.");
            await Click(obsoleteAdd.AutomationId);
            AssertSelected(2, 20);
            var changedSelection = session.Selection;
            int callsAfterReplacement = probe!.QualityBatchCalls;
            obsoletePicker.SelectedIndex = admitted.FindIndex(option => option.Rating == 1);
            await ui.BeginAsyncVoid(() => ((IButtonController)obsoleteAdd).SendClicked());
            Require(ReferenceEquals(changedSelection, session.Selection) && probe.QualityBatchCalls == callsAfterReplacement,
                "An obsolete rating picker or action changed the newer draft or issued another preview.");

            RatingPicker().SelectedIndex = admitted.FindIndex(option => option.Rating == 1);
            AssertOffer(1);
            await Click("karma-add-quality-" + spike[0].OptionId);
            AssertSelected(1, 10);
            await SearchQualities("Will to Live");
            Require(!IssuedElements(Current()).OfType<Button>().Any(button =>
                    button.AutomationId?.StartsWith("karma-add-quality-quality:18d2a522-6460-4654-916b-a96631f11323:",
                        StringComparison.Ordinal) == true),
                "An unsupported source-effect quality was offered without exact Core projection.");
            await SearchQualities("Spike Resistance");
            AssertSingleSpikeOffer();

            // The full preview, not a positive-quality cap or phone arithmetic,
            // decides whether this family fits the shared Karma budget.
            var skills = state.SkillsCatalog!;
            var english = skills.KnowledgeSkills.Single(skill => skill.Name == "English");
            var pistols = skills.ActiveSkills.Single(skill => skill.Name == "Pistols");
            var athletics = skills.SkillGroups.Single(group => group.Name == "Athletics");
            var fullBudgetSelection = selection with
            {
                Attributes = new[] { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL" }
                    .Select(id => new CharacterCreationKarmaAttributeAllocation(id, 4)).Append(new("EDG", 5)).ToArray(),
                Skills = new([
                    new(english.SourceSkillId, english.Kind, 0, IsNativeLanguage: true),
                    new(pistols.SourceSkillId, pistols.Kind, 6)], [new(athletics.GroupId, 4)]),
                QualityOptionIds = [], ResourceKarmaInvestment = null
            };
            var priced = await runtime.Coordinator.PreviewCreationKarmaAsync(state, fullBudgetSelection);
            Require(priced.Value is { CanSelect: true, KarmaBudget.Remaining: >= 0 },
                "SETUP: nearly-full real Karma draft rejected: " + string.Join(",", priced.Blockers));
            fullBudgetSelection = fullBudgetSelection with { ResourceKarmaInvestment = priced.Value!.KarmaBudget.Remaining };
            var exhausted = await runtime.Coordinator.PreviewCreationKarmaAsync(state, fullBudgetSelection);
            Require(exhausted.Value is { CanSelect: true, KarmaBudget.Remaining: 0 },
                "SETUP: exact exhausted budget rejected: " + string.Join(",", exhausted.Blockers));
            var noFunds = await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(state,
                fullBudgetSelection, "Spike Resistance", 0, 1, default, () => true);
            Require(noFunds is { Options.Count: 0 }
                && runtime.Coordinator.IsCreationKarmaPreviewCurrent(exhausted.Value!),
                "Unaffordable rating members remained offered, or read-only browsing replaced the issued review.");

            Require(fullBudgetSelection.ResourceKarmaInvestment >= 10,
                "SETUP: the real budget fixture must permit releasing exactly ten Karma.");
            var reducedBudget = fullBudgetSelection with
            {
                ResourceKarmaInvestment = fullBudgetSelection.ResourceKarmaInvestment - 10,
                QualityOptionIds = [spike[1].OptionId]
            };
            session.Change(reducedBudget);
            await Back();
            await Click("karma-open-qualities");
            await SearchQualities("Spike Resistance");
            Require(Element<Button>("karma-remove-quality-" + spike[1].OptionId).IsEnabled
                && RatingPicker().Items.Count == 1 && RatingPicker().SelectedIndex == -1,
                "A now-unaffordable selected rating must remain removable, not appear among admitted alternatives.");
            await Click("karma-remove-quality-" + spike[1].OptionId);
            Require(session.Selection!.QualityOptionIds!.Count == 0
                && !IssuedElements(Current()).OfType<Button>().Any(button =>
                    button.AutomationId == "karma-add-quality-" + spike[1].OptionId),
                "Removing an invalid selected rating exposed it again as an admitted purchase.");
            session.Change(reducedBudget);
            await Back();
            await Click("karma-open-qualities");
            await SearchQualities("Spike Resistance");
            RatingPicker().SelectedIndex = 0;
            await Click("karma-add-quality-" + spike[0].OptionId);
            Require(session.Selection!.QualityOptionIds!.SequenceEqual([spike[0].OptionId])
                && session.QuoteCurrent && session.Quote!.KarmaBudget.Remaining == 0
                && session.Quote.Qualities!.Costs.PositiveKarmaSpent == 10,
                "Downrating an invalid choice did not release the old cost before admitting its exact replacement.");
            session.Change(selection with { QualityOptionIds = [spike[0].OptionId] });

            await Back();
            await Click("karma-open-qualities");
            await SearchQualities("Spike Resistance");
            AssertSelected(1, 10);
            var departedPicker = RatingPicker();
            departedPicker.SelectedIndex = admitted.FindIndex(option => option.Rating == 2);
            var departedAction = Element<Button>("karma-add-quality-" + spike[1].OptionId);
            await Back();
            var departedSelection = session.Selection;
            int departedReads = probe.QualityBatchCalls;
            departedPicker.SelectedIndex = admitted.FindIndex(option => option.Rating == 1);
            ((IButtonController)departedAction).SendClicked();
            Require(ReferenceEquals(departedSelection, session.Selection) && probe.QualityBatchCalls == departedReads,
                "A departed rating picker or action changed the active draft.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(workspaceId).Value!);
            Require(probe.ConfirmCalls == 0 && probe.FinalConfirmCalls == 0,
                "Quality browsing or rating changes wrote the runner without confirmation.");

            if (Environment.GetEnvironmentVariable("CHUMMER_QUALITY_METHODS_SMOKE_DIRECTORY") is { Length: > 0 } smokeDirectory)
            {
                Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                    "Quality methods smoke requires a new explicit synthetic directory.");
                await session.PreviewAsync(default, () => true);
                Require(session.QuoteCurrent && session.Quote is { CanSelect: true },
                    "The exact pending quality draft needs a fresh admitted review before synthetic export.");
                await session.ConfirmAsync(default, () => true);
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(workspaceId).Value!;
                Require(probe.ConfirmCalls == 1 && probe.FinalConfirmCalls == 0 && session.Saved
                    && saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                    && saved.Document.Content == before.Document.Content
                    && saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions!.Single()
                        .Command.QualityOptionIds!.Single() == spike[0].OptionId,
                    "Synthetic export did not save exactly the selected pending quality once, before finalization.");
                Directory.CreateDirectory(smokeDirectory);
                foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: false);
                }
                Console.WriteLine("Quality methods synthetic Karma fixture: " + workspaceId.Value);
            }

            await Click("karma-open-qualities");
            await SearchQualities("Spike Resistance");
            var ownerPicker = RatingPicker();
            ownerPicker.SelectedIndex = admitted.FindIndex(option => option.Rating == 2);
            var ownerAction = Element<Button>("karma-add-quality-" + spike[1].OptionId);
            var ownerSelection = session.Selection;
            var beforeOwnerChange = new FileWorkspaceStore(runtime.StateDirectory).Get(workspaceId).Value!;
            int ownerReads = probe.QualityBatchCalls, confirmations = probe.ConfirmCalls;
            owners.Set(ContactsOwnerB);
            owners.Set(OwnerScope.LocalSingleUser);
            ownerPicker.SelectedIndex = admitted.FindIndex(option => option.Rating == 1);
            ((IButtonController)ownerAction).SendClicked();
            Require(ReferenceEquals(ownerSelection, session.Selection) && probe.QualityBatchCalls == ownerReads
                && probe.ConfirmCalls == confirmations && owners.ActiveLeases == 0,
                "Owner A-to-B-to-A reactivated a stale rating offer or leaked an owner lease.");
            RequireSameRewardDocument(beforeOwnerChange, new FileWorkspaceStore(runtime.StateDirectory).Get(workspaceId).Value!);
            ui.AssertHealthy();
            Console.WriteLine("PASS Karma quality methods: exact typed families, Core-admitted members, replacement 1-to-2-to-1, shared budget, stale/departed/owner callbacks, no implicit writes");

            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
            CreationKarmaPhoneSession Session() => (CreationKarmaPhoneSession)typeof(CreationKarmaPage)
                .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(Current())!;
            T Element<T>(string id) where T : Element => IssuedElements(Current()).OfType<T>().Single(element => element.AutomationId == id);
            Picker RatingPicker() => IssuedElements(Current()).OfType<Picker>().Single(picker =>
                picker.AutomationId?.StartsWith("karma-quality-rating-", StringComparison.Ordinal) == true);
            void AssertSingleSpikeOffer()
            {
                var picker = RatingPicker();
                Require(picker.Items.Count == admitted.Count,
                    "The family picker omitted an admitted rating or offered a member rejected by the real full Core preview.");
                Require(IssuedElements(Current()).OfType<Button>().Count(button =>
                        button.AutomationId?.StartsWith("karma-add-quality-", StringComparison.Ordinal) == true) <= 1,
                    "One exact typed quality family produced duplicate catalog purchase rows.");
            }
            void AssertSelected(int rating, int karma)
            {
                Require(session.Selection!.QualityOptionIds!.SequenceEqual([spike.Single(option => option.Rating == rating).OptionId])
                    && session.QuoteCurrent && session.Quote!.Qualities is { } qualities
                    && qualities.Selections.Count == 1 && qualities.Selections.Single().Rating == rating
                    && qualities.Costs.PositiveKarmaSpent == karma,
                    "Rating replacement retained a sibling, double-charged Karma, or displayed an older Core quote.");
                AssertOffer(rating);
                AssertSingleSpikeOffer();
            }
            void AssertOffer(int rating)
            {
                var option = spike.Single(item => item.Rating == rating);
                string cost = CreationKarmaCopy.QualitySourceCost(option.Name, option.Rating, option.KarmaCost);
                string summary = CreationQualityInfo.Effects(option.SourceNodeXml, option.Rating).FirstOrDefault() ?? string.Empty;
                Require(IssuedElements(Current()).OfType<Label>().Any(label => label.Text?.EndsWith(cost, StringComparison.Ordinal) == true)
                    && IssuedElements(Current()).OfType<Label>().Single(label =>
                        label.AutomationId?.StartsWith("karma-quality-summary-", StringComparison.Ordinal) == true).Text == summary
                    && IssuedElements(Current()).OfType<Button>().Any(button => button.AutomationId == "karma-quality-info-" + option.OptionId),
                    "The offered rating, exact cost, effect summary and information action did not update together.");
            }
            async Task Appear()
            {
                var page = Current();
                if (IssuedPageField<int>(page, "_subscribed") == 0)
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            }
            async Task Click(string id)
            {
                var previous = Current();
                var button = Element<Button>(id);
                Require(button.IsEnabled, "Button disabled: " + id);
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
            async Task SearchQualities(string term)
            {
                Element<SearchBar>("karma-quality-search").Text = term;
                await Click("karma-quality-search-go");
            }
        });
    }

    private static void AssertCreationQualityCatalogRatingIdentity()
    {
        Guid source = Guid.Parse("d58ea4d1-4ce7-44f0-a9bc-50ce52921a21");
        CharacterCreationQualityCatalogOption Option(int rating) => new(
            $"quality:{source:D}:rating:{rating}", source, source.ToString("D"), "Same display name",
            CharacterCreationQualityType.Positive, rating, rating * 2, 1, false, true, true, false, true, true,
            null, null, null, ["quality-fixture"], "<quality />", new string('a', 64), new string('b', 64));
        var one = Option(1);
        var three = Option(3);
        var seven = Option(7);
        var sparse = CreationQualityCatalogRatings.Group([seven, one, three]);
        Require(sparse.Count() == 1 && sparse.Single().Select(option => option.Rating).Order().SequenceEqual([1, 3, 7]),
            "Sparse typed ratings were split or synthesized into a continuous numeric range.");
        var otherSource = Guid.Parse("23f2f19e-8037-4c56-b2b6-fdf2be5b9e68");
        var variants = new[]
        {
            one with { SourceId = otherSource, SelectionKey = otherSource.ToString("D"), OptionId = "other-source" },
            one with { SelectionKey = "different-instance", OptionId = "other-selection" },
            one with { FollowUpChoiceId = "other-follow-up", OptionId = "other-follow-up" },
            one with { Type = CharacterCreationQualityType.Negative, OptionId = "other-type" },
            one with { IsMetagenic = true, OptionId = "other-metagenic" },
            one with { IsFreeOrGranted = true, OptionId = "other-grant" },
            one with { CountsAgainstKarma = false, OptionId = "other-karma-policy" },
            one with { CountsAgainstQualityLimit = false, OptionId = "other-limit-policy" },
            one with { MaximumSelections = 2, OptionId = "other-maximum" },
            one with { SourceNodeDigest = new string('c', 64), OptionId = "other-source-node" },
            one with { ResolvedArmCount = 4, OptionId = "other-anatomy" }
        };
        foreach (var variant in variants)
        {
            Require(CreationQualityCatalogRatings.Group([one, three, variant]).Count() == 2,
                "A similar label with distinct typed identity or source authority was merged: " + variant.OptionId);
            var replaced = CreationQualityCatalogRatings.Replace([one.OptionId, variant.OptionId], [one, three, variant], three);
            Require(replaced.Order(StringComparer.Ordinal).SequenceEqual(new[] { three.OptionId, variant.OptionId }.Order(StringComparer.Ordinal)),
                "Replacing an exact rating removed a different source/follow-up/policy choice: " + variant.OptionId);
        }
        Require(CreationQualityCatalogRatings.Group([one, one with { OptionId = "ambiguous-rating" }, three]).All(group => group.Count() == 1),
            "Repeated ratings with ambiguous option identity were guessed into one family.");
        Require(CreationQualityCatalogRatings.Group([one with { SourceId = Guid.Empty }, three with { SourceId = Guid.Empty }])
                .All(group => group.Count() == 1)
            && CreationQualityCatalogRatings.Group([one with { SelectionKey = "" }, three with { SelectionKey = "" }])
                .All(group => group.Count() == 1),
            "Missing source identity was treated as a valid shared quality family.");
        Console.WriteLine("PASS quality family identity: sparse exact ratings, distinct source/follow-up/type/policy/anatomy, ambiguity, sibling-only replacement");
    }
}
