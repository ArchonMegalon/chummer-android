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
            int loadsBeforeBack = probe!.LoadCalls;
            int previewsBeforeBack = probe.PreviewCalls;
            await Back();
            Require(probe.LoadCalls == loadsBeforeBack && probe.PreviewCalls == previewsBeforeBack + 1,
                "Returning to the overview must freshly preview, without a redundant preceding Load.");
            await Click("karma-open-attributes");
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1,
                "Back navigation silently dropped the uncommitted attribute selection.");
            oldAgility.Value = 2;
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1,
                "An obsolete native Stepper overwrote the returned editor.");
            await Back();
            await Click("karma-open-skills");
            await Click("karma-filter-knowledge");
            Require(Element<Button>("karma-filter-knowledge").Text == CreationKarmaCopy.KnowledgeSkills,
                "The catalog filter must not use the knowledge-point payment caption.");
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
            await Click("karma-open-qualities");
            await QualitySearch("Code of Honor");
            Require(IssuedElements(Current()).OfType<Button>().Any(b => b.AutomationId?.StartsWith("karma-add-quality-", StringComparison.Ordinal) == true)
                && IssuedElements(Current()).OfType<Button>().Where(b => b.AutomationId?.StartsWith("karma-add-quality-", StringComparison.Ordinal) == true).All(b => !b.IsEnabled),
                "An unresolved source prompt must not be offered as an ordinary purchase.");
            await QualitySearch("Unsteady Hands");
            var addNegative = IssuedElements(Current()).OfType<Button>().Single(b => b.AutomationId?.StartsWith("karma-add-quality-", StringComparison.Ordinal) == true);
            string negativeId = addNegative.AutomationId["karma-add-quality-".Length..];
            await Click(addNegative.AutomationId);
            Require(Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(0, 7, -7),
                "Quality credit was not projected from the current Core quote.");
            await Click("karma-remove-quality-" + negativeId);
            await ui.BeginAsyncVoid(() => ((IButtonController)addNegative).SendClicked());
            Require(Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(0, 0, 0),
                "An obsolete quality button restored a removed choice.");
            await Click("karma-add-quality-" + negativeId);
            await QualitySearch("Overclocker");
            var addPositive = IssuedElements(Current()).OfType<Button>().Single(b => b.AutomationId?.StartsWith("karma-add-quality-", StringComparison.Ordinal) == true);
            await Click(addPositive.AutomationId);
            Require(Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(5, 7, -2)
                && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision,
                "Quality selection must show Core net costs without persisting before review.");
            await Back();
            await Click("karma-open-resources");
            var resourceInput = Element<Entry>("karma-resource-investment");
            var numberCulture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string language in new[] { "de-AT", "en-US", "es-ES" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                    resourceInput.Text = "not-a-number";
                    Require(!Element<Button>("karma-use-resources").IsEnabled
                        && Element<Label>("karma-resource-invalid").IsVisible,
                        "Invalid resource text must not reuse an earlier amount.");
                    resourceInput.Text = 10.5m.ToString(CultureInfo.CurrentCulture);
                    Require(Element<Button>("karma-use-resources").IsEnabled, "Localized decimal amount was rejected.");
                }
            }
            finally { CultureInfo.CurrentCulture = numberCulture; }
            resourceInput.Text = 10.5m.ToString(CultureInfo.CurrentCulture);
            await Click("karma-use-resources");
            resourceInput.Text = "99";
            Require(Element<Label>("karma-resource-funding").Text == CreationKarmaCopy.ResourceFunding(10.5m, 21000m),
                "An obsolete resource entry altered the selected draft: " + Element<Label>("karma-resource-funding").Text);
            await Click("karma-open-gear");
            Element<SearchBar>("karma-gear-search").Text = "Flashlight";
            await Click("karma-gear-search-go");
            var addGear = IssuedElements(Current()).OfType<Button>().First(b => b.IsEnabled
                && b.AutomationId?.StartsWith("karma-add-gear-", StringComparison.Ordinal) == true);
            string gearId = addGear.AutomationId["karma-add-gear-".Length..];
            await Click(addGear.AutomationId);
            var oldQuantity = Element<Stepper>("karma-gear-quantity-" + gearId);
            oldQuantity.Value = 2;
            await Click("karma-preview-gear");
            Require(Element<Label>("karma-gear-totals").Text == CreationKarmaCopy.GearTotals(21000m, 50m, 20950m, 0m),
                "Gear quantity was not priced from the current Core resource quote.");
            oldQuantity.Value = 3;
            Require(Element<Stepper>("karma-gear-quantity-" + gearId).Value == 2,
                "An obsolete quantity control changed the current basket.");
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision
                && probe!.ConfirmCalls == 0, "Editing gear persisted without review.");
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
                && decision.Quote.Skills.NativeLanguagesUsed == 1
                && decision.Command.ResourceKarmaInvestment == 10.5m
                && decision.Quote.Resources!.NuyenFromKarma == 21000m
                && decision.Command.QualityOptionIds!.Count == 2
                && decision.Quote.Qualities!.Costs.NetKarmaSpent == -2
                && decision.Command.GearSelections!.Single().Quantity == 2
                && decision.Quote.Gear!.Budget.BasketCost == 50m,
                "Native review did not persist exactly the selected source-bound pending foundation.");
            // Recreate all page/session objects and reread the actual durable
            // store. This is managed reopen evidence, not an Android process restart.
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await HydrateFinalizationOwnerAsync(runtime, owners, cold);
            foreach (var corrupt in new Func<CharacterCreationKarmaMetatypeOpen, CharacterCreationKarmaMetatypeOpen>[]
            {
                opened => opened with { Quote = opened.State.Selection!.Quote },
                opened => opened with { Quote = null },
                opened => opened with { Quote = opened.Quote! with { SnapshotDigest = "sha256:" + new string('a', 64) } }
            })
            {
                probe.TransformOpen = corrupt;
                Require((await runtime.Coordinator.OpenCreationKarmaAsync()).Value is null,
                    "Open admitted a historical, missing or mismatched saved review.");
            }
            probe.TransformOpen = null;
            int opensBeforeReopen = probe.OpenCalls;
            int loadsBeforeReopen = probe.LoadCalls;
            int previewsBeforeReopen = probe.PreviewCalls;
            var reopened = new CreationKarmaPage(runtime.Coordinator);
            await navigation.PushAsync(reopened, false);
            await Appear();
            Require(probe.OpenCalls == opensBeforeReopen + 1 && probe.LoadCalls == loadsBeforeReopen
                && probe.PreviewCalls == previewsBeforeReopen && probe.ConfirmCalls == 1,
                "Reopening a saved wizard must use one Core Open, without a second Load/Preview or a write.");
            await Click("karma-open-attributes");
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1, "Reopened phone page lost its durable allocation.");
            await Back();
            await Click("karma-open-resources");
            Require(Element<Entry>("karma-resource-investment").Text == 10.5m.ToString(CultureInfo.CurrentCulture),
                "Cold reopen lost the resource investment.");
            await Back();
            await Click("karma-open-gear");
            Require(Element<Stepper>("karma-gear-quantity-" + gearId).Value == 2
                && Element<Label>("karma-gear-totals").Text == CreationKarmaCopy.GearTotals(21000m, 50m, 20950m, 0m),
                "Cold reopen lost equipment identity, quantity or funding binding.");
            await Back();
            await Click("karma-open-qualities");
            Require(IssuedElements(Current()).OfType<Button>().Count(b => b.AutomationId?.StartsWith("karma-remove-quality-", StringComparison.Ordinal) == true) == 2
                && Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(5, 7, -2),
                "Cold reopen lost the selected quality identities or recalculated profile costs.");
            await Back();
            await Click("karma-open-skills");
            Require(IssuedElements(Current()).OfType<Button>().Count(b => b.AutomationId?.StartsWith("karma-selected-skill-", StringComparison.Ordinal) == true) == 2,
                "Reopened phone page lost the native language or active skill.");
            await Back();
            await Click("karma-open-completion");
            Require(Element<Entry>("karma-completion-roll").Text == string.Empty
                && !Element<Button>("karma-completion-preview").IsEnabled
                && !Element<Button>("karma-completion-confirm").IsEnabled,
                "Completion guessed a dice result or authorized confirmation before review.");
            Element<Entry>("karma-completion-roll").Text = "999";
            await Click("karma-completion-preview");
            Require(!Element<Button>("karma-completion-confirm").IsEnabled && probe.FinalConfirmCalls == 0,
                "An out-of-range starting-cash roll authorized completion.");
            Element<Entry>("karma-completion-roll").Text = "4";
            await Click("karma-completion-preview");
            var oldFinalConfirm = Element<Button>("karma-completion-confirm");
            Require(oldFinalConfirm.IsEnabled, "Core did not admit the saved mundane foundation for completion.");
            Element<Entry>("karma-completion-roll").Text = "5";
            Require(!oldFinalConfirm.IsEnabled, "Editing a roll did not invalidate its exact review.");
            ((IButtonController)oldFinalConfirm).SendClicked(); // MAUI suppresses disabled Clicked events.
            Require(probe.FinalConfirmCalls == 0, "An obsolete review confirmed a different roll.");
            Element<Entry>("karma-completion-roll").Text = "4";
            await Click("karma-completion-preview");
            var review = (CharacterCreationFinalizationReview)typeof(CreationKarmaCompletionPage)
                .GetField("_review", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Current())!;
            Require((await runtime.Coordinator.ConfirmKarmaCompletionAsync(review, false, default, () => true)).Value is null
                && (await runtime.Coordinator.ConfirmKarmaCompletionAsync(review with { }, true, default, () => true)).Value is null
                && probe.FinalConfirmCalls == 0, "Completion accepted implicit consent or a forged review identity.");
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == cold.ContentRevision,
                "Starting-cash preview mutated the pending workspace.");
            var finalButton = Element<Button>("karma-completion-confirm");
            await Click("karma-completion-confirm");
            Require(Element<Label>("karma-completion-receipt").Text == CreationKarmaCopy.CareerReady
                && Element<Button>("karma-completion-open-career").IsEnabled && probe.FinalConfirmCalls == 1,
                "Atomic completion did not reopen the saved Career runner.");
            var completed = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            Require(completed.ContentRevision == cold.ContentRevision + 1 && completed.SavedRevision == completed.ContentRevision
                && completed.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.KarmaAuthority is not null
                && completed.Document.AuxiliaryState.CharacterCreationFinalizationArchive.State.CharacterCreationKarmaMetatypeDecisions!.Single().DecisionDigest == decision.DecisionDigest
                && runtime.Coordinator.State.Profile?.Created == true
                && !runtime.Coordinator.CanOpenCreationKarma(), "Completion lost history or retained Creation mode.");
            await ui.BeginAsyncVoid(() => ((IButtonController)finalButton).SendClicked());
            Require(probe.FinalConfirmCalls == 1 && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == completed.ContentRevision,
                "An obsolete finalization button replayed the mutation.");
            await runtime.Shell.InitializeAsync(default);
            await runtime.Presenter.InitializeAsync(default);
            await runtime.Presenter.LoadAsync(completed.Id, default);
            Require(runtime.Coordinator.State.Profile?.Created == true
                && runtime.Coordinator.State.ContentRevision == completed.ContentRevision
                && runtime.Coordinator.State.SavedRevision == completed.SavedRevision
                && runtime.Coordinator.State.DisplayOwnerContext == owners.Capture()
                && runtime.Coordinator.State.Session.OwnerContext == owners.Capture(),
                "Cold Career reopen lost the finalization transition.");
            IssuedPageLifecycle(Current(), "OnDisappearing");
            var prior = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
                Require(CreationKarmaCopy.Title == "Karma-Grunddaten", "German regional resources are missing.");
                Require(CreationKarmaCopy.Qualities == "Vor- und Nachteile", "German quality resources are missing.");
                Require(CreationKarmaCopy.Gear == "Ausrüstung", "German equipment resources are missing.");
                Require(CreationKarmaCopy.Finish == "Karma-Erstellung abschließen", "German completion resources are missing.");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-MX");
                Require(CreationKarmaCopy.Confirm == "Confirmar y guardar borrador", "Spanish regional resources are missing.");
                Require(CreationKarmaCopy.Qualities == "Cualidades", "Spanish quality resources are missing.");
                Require(CreationKarmaCopy.Gear == "Equipo", "Spanish equipment resources are missing.");
                Require(CreationKarmaCopy.Finish == "Finalizar creación con Karma", "Spanish completion resources are missing.");
            }
            finally { CultureInfo.CurrentUICulture = prior; }
            ui.AssertHealthy();
            Console.WriteLine($"Karma phone source work: opens={probe!.OpenCalls}, loads={probe.LoadCalls}, previews={probe.PreviewCalls}, open-ms={probe.OpenTime.TotalMilliseconds:F0}, load-ms={probe.LoadTime.TotalMilliseconds:F0}, preview-ms={probe.PreviewTime.TotalMilliseconds:F0}");
            Console.WriteLine("PASS Karma native phone deep pages: explicit choices, stale controls, draft Back, review/save, cold reopen, DE/ES resources");

            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
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
            async Task QualitySearch(string term)
            { Element<SearchBar>("karma-quality-search").Text = term; await Click("karma-quality-search-go"); }
        });
    }

    private static async Task RunKarmaPhoneRevalidationAsync(string contentRoot)
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
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var session = new CreationKarmaPhoneSession(runtime.Coordinator);
            await session.ReloadAsync(false, default, () => true);
            var state = session.Authority!;
            var draft = new CreationKarmaPhoneSelection(state.Options.Single(o => o.Label == "Human").OptionId,
                "mundane", [], ResourceKarmaInvestment: 10.5m);
            session.Change(draft);
            await session.PreviewAsync(default, () => true);
            var firstQuote = session.Quote;
            int loads = probe!.LoadCalls, previews = probe.PreviewCalls;
            await session.ReloadAsync(false, default, () => true);
            Require(session.QuoteCurrent && !ReferenceEquals(firstQuote, session.Quote)
                && ReferenceEquals(state, session.Authority)
                && probe.LoadCalls == loads && probe.PreviewCalls == previews + 1,
                "Revisit reused an old quote or redundantly loaded before fresh Core revalidation.");

            session.Change(draft with { Attributes = [new("AGI", 99)] });
            await session.ReloadAsync(false, default, () => true);
            Require(session.Ready && session.QuoteCurrent && session.Quote is { CanSelect: false }
                && probe.LoadCalls == loads,
                "An invalid allocation must remain editable with a freshly validated blocked quote.");
            session.Change(draft);
            await session.ReloadAsync(false, default, () => true);
            Require(session.QuoteCurrent && session.Quote!.CanSelect, "Valid edit failed to recover the draft.");

            probe.FailReads = true;
            await session.ReloadAsync(false, default, () => true);
            await session.ConfirmAsync(default, () => true);
            Require(!session.Ready && !session.QuoteCurrent && probe.ConfirmCalls == 0
                && probe.LoadCalls == loads + 1 && session.Blockers.Contains(CharacterCreationKarmaMetatypeBlockers.StaleBinding),
                "A failed fresh read fell back to cached source or permitted confirmation.");
            probe.FailReads = false;
            await session.ReloadAsync(false, default, () => true);
            await session.PreviewAsync(default, () => true);
            Require(session.QuoteCurrent && session.Selection is { TalentOptionId: "mundane", Attributes.Count: 0 }
                && session.Selection.MetatypeOptionId == draft.MetatypeOptionId,
                "Successful reread lost the unsaved selection or failed to reissue the review.");

            bool current = true;
            probe.AfterPreview = () => current = false;
            await session.ReloadAsync(false, default, () => current);
            Require(!session.QuoteCurrent, "A departed page admitted a completed review.");
            current = true;
            probe.AfterPreview = null;
            await session.ReloadAsync(false, default, () => current);
            Require(session.QuoteCurrent, "Returning to the original frame did not freshly revalidate.");

            probe.AfterPreview = () => { owners.Set(ContactsOwnerB); owners.Set(Chummer.Contracts.Owners.OwnerScope.LocalSingleUser); };
            await session.ReloadAsync(false, default, () => current);
            await session.ConfirmAsync(default, () => current);
            var after = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            Require(!session.Ready && !session.QuoteCurrent && probe.ConfirmCalls == 0
                && after.ContentRevision == before.ContentRevision
                && after.Document.AuxiliaryStateDigest == before.Document.AuxiliaryStateDigest
                && owners.ActiveLeases == 0,
                "Owner A→B→A admitted cached authority, changed state or leaked a lease.");
            ui.AssertHealthy();
            Console.WriteLine("PASS Karma phone revalidation: one fresh Preview, editable invalid draft, failed source, departed page, owner ABA, no writes");
        });
    }
}
