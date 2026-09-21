using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunSr6FoundationAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "commit", "owner-aba-load", "owner-aba-preview", "owner-aba-confirm",
                         "departed-preview", "cancel-after-commit", "lost-return", "forged-review" })
            {
                var owners = new ControlledLinkedOwner();
                Sr6Probe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners, sr6Decorator: actual => probe = new(actual, ui));
                await Bootstrap(runtime, "Priority");
                var id = runtime.Coordinator.State.WorkspaceId!.Value;
                void Aba() { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); }
                if (scenario == "owner-aba-load") probe!.AfterLoad = Aba;
                var loaded = await runtime.Coordinator.LoadSr6FoundationAsync();
                if (scenario == "owner-aba-load")
                {
                    Require(loaded.Value is null && probe!.Confirms == 0, "An expired load issued an SR6 editor.");
                    Console.WriteLine("PASS SR6 " + scenario); continue;
                }
                Require(loaded.Value is not null, "SR6 foundation did not load: " + string.Join(",", loaded.Blockers));
                var state = loaded.Value!;
                bool currentPage = true;
                if (scenario == "owner-aba-preview") probe!.AfterPreview = Aba;
                if (scenario == "departed-preview") probe!.AfterPreview = () => currentPage = false;
                var selection = new Sr6CreationFoundationSelection("human", "mundane", [
                    new("heritage", "D"), new("talent", "E"), new("attributes", "A"), new("skills", "B"), new("resources", "C")]);
                var preview = await runtime.Coordinator.PreviewSr6FoundationAsync(state, selection, isCurrentPage: () => currentPage);
                if (scenario is "owner-aba-preview" or "departed-preview")
                {
                    Require(preview.Value is null && probe!.Confirms == 0, "Departed/foreign preview was admitted.");
                    Console.WriteLine("PASS SR6 " + scenario); continue;
                }
                Require(preview.Value is not null, "SR6 valid preview missing.");
                var quote = preview.Value!;
                if (scenario == "owner-aba-confirm") Aba();
                if (scenario == "forged-review") quote = quote with { };
                using var cancellation = new CancellationTokenSource();
                if (scenario == "cancel-after-commit") probe!.AfterConfirm = cancellation.Cancel;
                if (scenario == "lost-return") probe!.AfterConfirm = () => throw new IOException("Lost committed result");
                var result = await runtime.Coordinator.ConfirmSr6FoundationAsync(quote, true, cancellation.Token);
                var stored = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                if (scenario is "owner-aba-confirm" or "forged-review")
                    Require(result.Commit is null && probe!.Confirms == 0 && stored.ContentRevision == 1, "Obsolete/unissued quote wrote SR6 state.");
                else
                {
                    Require(probe!.Confirms == 1 && stored.ContentRevision == 2 && stored.SavedRevision == 2
                        && stored.Document.AuxiliaryState.Sr6CreationFoundationDecisions?.Count == 1, "SR6 save was lost or repeated.");
                    if (scenario == "commit") Require(result.Commit is not null && result.State is not null, "Saved SR6 state was not refreshed.");
                    if (scenario == "cancel-after-commit") Require(result.Commit is not null && result.State is null, "Post-commit cancellation lost the acknowledged save.");
                    if (scenario == "lost-return") Require(result.Commit is null && result.Blockers.Contains(RunnerSessionCoordinator.Sr6OutcomeUnknown), "Unknown return was represented as success.");
                    await runtime.Coordinator.ConfirmSr6FoundationAsync(preview.Value!, true);
                    Require(probe.Confirms == 1, "A repeated tap redispatched the mutation.");
                }
                ui.AssertHealthy();
                Console.WriteLine("PASS SR6 " + scenario);
            }

            foreach (string method in new[] { "Priority", "SumtoTen", "PointBuy" })
            foreach (string language in new[] { "en", "de", "es" })
            {
                var previousCulture = CultureInfo.CurrentUICulture;
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                try
                {
                    Sr6Probe? probe = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                        productionCreationOverview: true, sr6Decorator: actual => probe = new(actual, ui));
                    await Bootstrap(runtime, method);
                    var id = runtime.Coordinator.State.WorkspaceId!.Value;
                    var page = new Sr6CreationFoundationPage(runtime.Coordinator);
                    var navigation = new NavigationPage(new ContentPage());
                    await navigation.PushAsync(page, false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    Require(page.Title == Sr6CreationCopy.Text("Title") && page.Title != "Title", "Localized SR6 title missing.");
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>().Single(item => item.AutomationId == "sr6-foundation-" + key);
                    void Pick(string key, int index) => Element<Picker>(key).SelectedIndex = index;
                    Task Click(string key) => ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                    if (method == "PointBuy")
                    {
                        Require(!IssuedElements(page).OfType<Picker>().Any(item => item.AutomationId?.StartsWith("sr6-foundation-rank-", StringComparison.Ordinal) == true),
                            "Point Buy was routed into priority inputs.");
                        await Click("preview");
                        Require(!IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                            && Element<Label>("status").Text == Sr6CreationCopy.Text("PointBuyInvalid"),
                            "Incomplete Point Buy choices requested nonexistent priority ranks.");
                        Pick("point-buy-attributes", 20); Pick("point-buy-skills", 20);
                        Pick("point-buy-adjustment", 12); Pick("point-buy-resources", 30);
                        Pick("metatype", 0); Pick("talent", 0);
                        await Click("preview");
                        Require(!IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                            && Element<Label>("status").Text == Sr6CreationCopy.Text("PointBuyOverspend"), "Point Buy CP overspend admitted.");
                        Pick("point-buy-skills", 16); Pick("point-buy-adjustment", 6); Pick("point-buy-resources", 12);
                        Pick("point-buy-attributes", 16); // Editing first field last must retain all other pools.
                    }
                    else
                    {
                        Pick("rank-heritage", 3); Pick("rank-talent", 4); Pick("rank-attributes", method == "Priority" ? 0 : 1);
                        Pick("rank-skills", 1); Pick("rank-resources", method == "Priority" ? 2 : 1);
                    }
                    Pick("metatype", 0); Pick("talent", 0);
                    await Click("preview");
                    var staleConfirm = Element<Button>("confirm");
                    var oldPicker = Element<Picker>("metatype");
                    Pick("metatype", 1);
                    staleConfirm.IsEnabled = true; // Simulate an already queued stale native callback.
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleConfirm).SendClicked());
                    Require(probe!.Confirms == 0, "Changed SR6 selection retained an old Confirm callback.");
                    await Click("preview");
                    await Click("confirm");
                    Require(probe.Confirms == 1, "Native SR6 page did not save once.");
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    Require(saved.ContentRevision == 2 && saved.SavedRevision == 2
                        && saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Single().Command.Selection.MetatypeId == "elf",
                        "Native SR6 page saved the wrong choice.");
                    if (method == "PointBuy")
                    {
                        var purchased = saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Single().Preview;
                        Require(purchased.PointBuy is { PointsSpent: 100, PointsRemaining: 0 }
                            && purchased.Budget == new Sr6CreationPriorityBudget(20, 28, 180000, 7, null),
                            "Point Buy lost independently selected pools or invented a priority rank.");
                    }
                    IssuedPageLifecycle(page, "OnDisappearing");
                    oldPicker.SelectedIndex = 4;
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleConfirm).SendClicked());
                    Require(probe.Confirms == 1, "Departed controls dispatched an SR6 mutation.");
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Picker>().Single(item => item.AutomationId == "sr6-foundation-metatype").SelectedIndex == 1,
                        "Reopened native SR6 page lost its saved metatype.");
                    Require(!IssuedElements(reopened).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Historical saved preview became current confirmation authority.");
                    Require(!IssuedElements(reopened).OfType<Button>().Single(item => item.AutomationId == "sr6-foundation-knowledge").IsEnabled,
                        "Knowledge allocation opened before attributes established its pool.");
                    IssuedPageLifecycle(reopened, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 phone " + method + " " + language);

                    var attributesPage = new Sr6CreationFoundationPage(runtime.Coordinator, attributesMode: true);
                    await navigation.PushAsync(attributesPage, false);
                    using var attributeAlerts = new IssuedPageAlerts(attributesPage, window);
                    await attributeAlerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(attributesPage, "OnAppearing"));
                    T AttributeElement<T>(string key) where T : Element => IssuedElements(attributesPage).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task AttributeClick(string key) => ui.BeginAsyncVoid(() => ((IButtonController)AttributeElement<Button>(key)).SendClicked());
                    Require(attributesPage.Title == Sr6CreationCopy.Text("AttributeTitle"), "Attribute title localization missing.");
                    Require(!IssuedElements(attributesPage).OfType<Picker>().Any(item => item.AutomationId == "sr6-foundation-normal-Edge"
                        || item.AutomationId == "sr6-foundation-adjustment-Body"), "Unavailable point kind appeared.");
                    AttributeElement<Picker>("normal-Body").SelectedIndex = 2;
                    AttributeElement<Picker>("normal-Logic").SelectedIndex = 2;
                    AttributeElement<Picker>("adjustment-Charisma").SelectedIndex = 1;
                    AttributeElement<Picker>("adjustment-Edge").SelectedIndex = 3;
                    await AttributeClick("preview");
                    var staleAttributeConfirm = AttributeElement<Button>("confirm");
                    AttributeElement<Picker>("normal-Body").SelectedIndex = 3;
                    staleAttributeConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleAttributeConfirm).SendClicked());
                    Require(probe.Confirms == 1, "Changed attribute input admitted its stale review.");
                    await AttributeClick("preview");
                    await AttributeClick("confirm");
                    Require(probe.Confirms == 2, "Attribute allocation did not save exactly once.");
                    var allocated = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    Require(allocated.ContentRevision == 3 && allocated.SavedRevision == 3
                        && allocated.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview.Attributes!.Values
                            .Single(item => item.AttributeId == "Body").Value == 4, "Wrong persisted attribute allocation.");
                    IssuedPageLifecycle(attributesPage, "OnDisappearing");
                    await runtime.Presenter.LoadAsync(id, default);
                    var attributeReopen = new Sr6CreationFoundationPage(runtime.Coordinator, attributesMode: true);
                    await navigation.PushAsync(attributeReopen, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(attributeReopen, "OnAppearing"));
                    Require(IssuedElements(attributeReopen).OfType<Picker>().Single(item => item.AutomationId == "sr6-foundation-normal-Body").SelectedIndex == 3,
                        "Saved attribute selection was not restored.");
                    Require(!IssuedElements(attributeReopen).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Saved attribute preview became current confirmation authority.");
                    IssuedPageLifecycle(attributeReopen, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 attribute phone " + method + " " + language);

                    var skillsPage = new Sr6CreationFoundationPage(runtime.Coordinator, skillsMode: true);
                    await navigation.PushAsync(skillsPage, false);
                    using var skillAlerts = new IssuedPageAlerts(skillsPage, window);
                    await skillAlerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(skillsPage, "OnAppearing"));
                    T SkillElement<T>(string key) where T : Element => IssuedElements(skillsPage).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task SkillClick(string key) => ui.BeginAsyncVoid(() => ((IButtonController)SkillElement<Button>(key)).SendClicked());
                    Require(skillsPage.Title == Sr6CreationCopy.Text("SkillTitle") && skillsPage.Title != "SkillTitle", "Skill title localization missing.");
                    Require(!IssuedElements(skillsPage).OfType<Picker>().Any(item => item.AutomationId is
                        "sr6-foundation-skill-Sorcery" or "sr6-foundation-skill-Tasking" or "sr6-foundation-skill-Astral"),
                        "Mundane draft exposed unavailable skill controls.");
                    SkillElement<Picker>("skill-Firearms").SelectedIndex = 4;
                    SkillElement<Entry>("specialization-Firearms").Text = "Pistols";
                    SkillElement<Picker>("skill-ExoticWeapons").SelectedIndex = 2;
                    SkillElement<Entry>("specialization-ExoticWeapons").Text = "Whip; Net";
                    await SkillClick("preview");
                    var staleSkillConfirm = SkillElement<Button>("confirm");
                    SkillElement<Picker>("skill-Firearms").SelectedIndex = 5;
                    staleSkillConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleSkillConfirm).SendClicked());
                    Require(probe.Confirms == 2, "Changed skill selection admitted stale confirmation.");
                    await SkillClick("preview");
                    await SkillClick("confirm");
                    Require(probe.Confirms == 3, "Skills did not save exactly once.");
                    var skilled = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var last = skilled.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(skilled.ContentRevision == 4 && skilled.SavedRevision == 4 && last.Skills!.PointsSpent == 9,
                        "Wrong persisted skill allocation or specialization costs.");
                    Require(last.Attributes!.Values.Single(row => row.AttributeId == "Body").Value == 4, "Skill save lost attributes.");
                    IssuedPageLifecycle(skillsPage, "OnDisappearing");
                    SkillElement<Entry>("specialization-Firearms").Text = "Stale";
                    await runtime.Presenter.LoadAsync(id, default);
                    var skillReopen = new Sr6CreationFoundationPage(runtime.Coordinator, skillsMode: true);
                    await navigation.PushAsync(skillReopen, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(skillReopen, "OnAppearing"));
                    Require(IssuedElements(skillReopen).OfType<Picker>().Single(item => item.AutomationId == "sr6-foundation-skill-Firearms").SelectedIndex == 5,
                        "Reopened skill page lost rating.");
                    Require(IssuedElements(skillReopen).OfType<Entry>().Single(item => item.AutomationId == "sr6-foundation-specialization-Firearms").Text == "Pistols",
                        "Departed skill callback changed saved specialization.");
                    Require(!IssuedElements(skillReopen).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Reopened skill page reused an old confirmation.");
                    IssuedPageLifecycle(skillReopen, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 skill phone " + method + " " + language);

                    var knowledgePage = new Sr6CreationFoundationPage(runtime.Coordinator, knowledgeMode: true);
                    await navigation.PushAsync(knowledgePage, false);
                    using var knowledgeAlerts = new IssuedPageAlerts(knowledgePage, window);
                    await knowledgeAlerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(knowledgePage, "OnAppearing"));
                    T KnowledgeElement<T>(string key) where T : Element => IssuedElements(knowledgePage).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    T KnowledgeRow<T>(string prefix) where T : Element => IssuedElements(knowledgePage).OfType<T>()
                        .Single(item => item.AutomationId?.StartsWith("sr6-foundation-knowledge-" + prefix, StringComparison.Ordinal) == true);
                    Task KnowledgeClick(string key)
                    {
                        if (key is "preview" or "confirm")
                            return ui.BeginAsyncVoid(() => ((IButtonController)KnowledgeElement<Button>(key)).SendClicked());
                        ((IButtonController)KnowledgeElement<Button>(key)).SendClicked(); // Local row edit is synchronous, no Core call.
                        return Task.CompletedTask;
                    }
                    Require(knowledgePage.Title == Sr6CreationCopy.Text("KnowledgeTitle") && knowledgePage.Title != "KnowledgeTitle",
                        "Knowledge title localization missing.");
                    KnowledgeElement<Entry>("knowledge-native").Text = "German";
                    await KnowledgeClick("knowledge-add-topic");
                    var discardedEntry = KnowledgeRow<Entry>("topic-");
                    string discardedId = discardedEntry.AutomationId!["sr6-foundation-knowledge-topic-".Length..];
                    await KnowledgeClick("preview");
                    Require(!IssuedElements(knowledgePage).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Blank topic was admitted for confirmation.");
                    await KnowledgeClick("knowledge-remove-topic-" + discardedId);
                    discardedEntry.Text = "Old row";
                    await KnowledgeClick("knowledge-add-topic");
                    var topicEntry = KnowledgeRow<Entry>("topic-");
                    string topicId = topicEntry.AutomationId!["sr6-foundation-knowledge-topic-".Length..];
                    topicEntry.Text = "Seattle gangs";
                    await KnowledgeClick("knowledge-add-language");
                    var languageEntry = KnowledgeRow<Entry>("language-");
                    string languageId = languageEntry.AutomationId!["sr6-foundation-knowledge-language-".Length..];
                    languageEntry.Text = "Sperethiel";
                    Require(KnowledgeRow<Picker>("level-").Items.Count == 3, "Additional native language level was exposed.");
                    await KnowledgeClick("preview");
                    var staleKnowledgeConfirm = KnowledgeElement<Button>("confirm");
                    KnowledgeElement<Entry>("knowledge-native").Text = "Deutsch";
                    staleKnowledgeConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleKnowledgeConfirm).SendClicked());
                    Require(probe.Confirms == 3, "Changed knowledge selection admitted stale confirmation.");
                    KnowledgeRow<Picker>("level-").SelectedIndex = 2; // Expert plus topic costs four, Logic is three.
                    await KnowledgeClick("preview");
                    Require(!IssuedElements(knowledgePage).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                        && KnowledgeElement<Label>("status").Text == Sr6CreationCopy.Text("KnowledgeOverspend"),
                        "Knowledge overspend was not blocked with localized explanation.");
                    Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == 4,
                        "An invalid knowledge draft wrote the workspace.");
                    KnowledgeRow<Picker>("level-").SelectedIndex = 1;
                    await KnowledgeClick("preview");
                    await KnowledgeClick("confirm");
                    Require(probe.Confirms == 4, "Knowledge allocation did not save exactly once.");
                    var knowledgeable = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var knowledgePreview = knowledgeable.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(knowledgeable.ContentRevision == 5 && knowledgeable.SavedRevision == 5
                        && knowledgePreview.Knowledge is { Logic: 3, PointsSpent: 3, PointsRemaining: 0, NativeLanguage: "Deutsch" },
                        "Knowledge allocation or its free native language was not persisted.");
                    Require(knowledgePreview.Skills!.PointsSpent == 9
                        && knowledgePreview.Attributes!.Values.Single(row => row.AttributeId == "Body").Value == 4,
                        "Knowledge allocation changed independent skill or attribute allocations.");
                    Require(knowledgePreview.Knowledge!.KnowledgeSkills.Single().Id.ToString("N") == topicId
                        && knowledgePreview.Knowledge.Languages.Single().Id.ToString("N") == languageId,
                        "Knowledge entry identities changed on save.");
                    var departedNative = KnowledgeElement<Entry>("knowledge-native");
                    IssuedPageLifecycle(knowledgePage, "OnDisappearing");
                    departedNative.Text = "Stale";
                    staleKnowledgeConfirm.IsEnabled = true; // Simulate queued callback after later edits disabled the detached control again.
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleKnowledgeConfirm).SendClicked());
                    Require(probe.Confirms == 4, "Departed knowledge controls dispatched a write.");
                    await runtime.Presenter.LoadAsync(id, default);
                    var knowledgeReopen = new Sr6CreationFoundationPage(runtime.Coordinator, knowledgeMode: true);
                    await navigation.PushAsync(knowledgeReopen, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(knowledgeReopen, "OnAppearing"));
                    Require(IssuedElements(knowledgeReopen).OfType<Entry>().Single(item => item.AutomationId == "sr6-foundation-knowledge-native").Text == "Deutsch"
                        && IssuedElements(knowledgeReopen).OfType<Entry>().Single(item => item.AutomationId == "sr6-foundation-knowledge-topic-" + topicId).Text == "Seattle gangs"
                        && IssuedElements(knowledgeReopen).OfType<Picker>().Single(item => item.AutomationId == "sr6-foundation-knowledge-level-" + languageId).SelectedIndex == 1,
                        "Reopened knowledge page lost names, levels or stable IDs.");
                    Require(!IssuedElements(knowledgeReopen).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Reopened knowledge page reused an old confirmation.");
                    IssuedPageLifecycle(knowledgeReopen, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 knowledge phone " + method + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
            }
        });

        static async Task Bootstrap(NativeRewardRuntime runtime, string method)
        {
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterRulesetId", "sr6", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "SR6 phone", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", method, default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            Require(runtime.Coordinator.CanOpenSr6Foundation(), "SR6 native bootstrap/route unavailable: "
                + runtime.Coordinator.State.Error + " / " + runtime.Coordinator.State.Profile?.BuildMethod + " / " + runtime.Coordinator.State.Rules?.GameEdition);
        }
    }

    private sealed class Sr6Probe(ISr6CreationFoundationService actual, SynchronizationContext ui) : ISr6CreationFoundationService
    {
        internal int Confirms;
        internal Action? AfterLoad, AfterPreview, AfterConfirm;
        private void Background() => Require(!ReferenceEquals(SynchronizationContext.Current, ui), "SR6 Core call blocked the UI context.");
        public CharacterCreationFoundationResult<Sr6CreationFoundationState> Load(OwnerContextStamp owner, CharacterWorkspaceId id)
        { Background(); var result = actual.Load(owner, id); AfterLoad?.Invoke(); return result; }
        public CharacterCreationFoundationResult<Sr6CreationFoundationPreview> Preview(OwnerContextStamp owner,
            Sr6CreationFoundationBinding binding, Sr6CreationFoundationSelection selection)
        { Background(); var result = actual.Preview(owner, binding, selection); AfterPreview?.Invoke(); return result; }
        public CharacterCreationFoundationResult<Sr6CreationFoundationCommit> Confirm(OwnerContextStamp owner, Sr6CreationFoundationConfirmRequest request)
        { Background(); Confirms++; var result = actual.Confirm(owner, request); AfterConfirm?.Invoke(); return result; }
    }
}
