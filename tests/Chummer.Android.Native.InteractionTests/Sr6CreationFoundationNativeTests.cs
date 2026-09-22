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
                    Require(!IssuedElements(reopened).OfType<Button>().Single(item => item.AutomationId == "sr6-foundation-talent-budget").IsEnabled,
                        "Talent allocation opened before attributes established its limits.");
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

            foreach (string method in new[] { "Priority", "PointBuy" })
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
                    var loaded = await runtime.Coordinator.LoadSr6FoundationAsync();
                    var selection = new Sr6CreationFoundationSelection("human", "mystic-adept", method == "PointBuy" ? [] : [
                        new("heritage", "C"), new("talent", "A"), new("attributes", "B"), new("skills", "D"), new("resources", "E")])
                    {
                        PointBuy = method == "PointBuy" ? new(0, 0, 2, 0) : null,
                        Attributes = new(Sr6CreationAttributeIds.Ordered.Select(attribute =>
                            new Sr6CreationAttributeSpend(attribute, 0, attribute == "Magic" ? 2 : 0)).ToArray())
                    };
                    var seed = await runtime.Coordinator.PreviewSr6FoundationAsync(loaded.Value!, selection);
                    Require(seed.Value is not null, "Talent seed preview missing: " + string.Join(",", seed.Blockers));
                    Require((await runtime.Coordinator.ConfirmSr6FoundationAsync(seed.Value!, true)).Commit is not null,
                        "Talent seed did not persist.");
                    var page = new Sr6CreationFoundationPage(runtime.Coordinator, talentMode: true);
                    var navigation = new NavigationPage(new ContentPage());
                    await navigation.PushAsync(page, false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task Click(string key) => ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                    Require(page.Title == Sr6CreationCopy.Text("TalentTitle") && page.Title != "TalentTitle", "Talent localization missing.");
                    Require(IssuedElements(page).OfType<Label>().Any(item => item.Text == Sr6CreationCopy.Text("TalentHelp")),
                        "Talent budget page omitted the unimplemented catalog boundary.");
                    Element<Picker>("talent-power-points").SelectedIndex = 1;
                    await Click("preview");
                    var staleConfirm = Element<Button>("confirm");
                    Element<Picker>("talent-power-points").SelectedIndex = 2;
                    staleConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleConfirm).SendClicked());
                    Require(probe!.Confirms == 1, "A changed talent selection admitted its old confirmation.");
                    await Click("preview");
                    await Click("confirm");
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var quote = saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(probe.Confirms == 2 && saved.ContentRevision == 3 && saved.SavedRevision == 3,
                        "Talent save was lost or repeated.");
                    Require(quote.TalentAllocation is { PowerPointBudget: 2 }
                        && quote.Attributes!.Values.Single(row => row.AttributeId == "Magic").AdjustmentPoints == 2,
                        "Talent save lost its split or independent attribute allocation.");
                    if (method == "PointBuy")
                        Require(quote.PointBuy is { PointsSpent: 34, PointsRemaining: 66, PowerPointCost: 16 }
                            && quote.TalentAllocation is { SpellOrRitualLimit: 2, FreeSpellOrRitualSlots: 0 },
                            "Point Buy talent costs or limits were incorrect.");
                    else Require(quote.TalentAllocation is { Magic: 6, FreeSpellOrRitualSlots: 4, PowerPointCharacterPointCost: 0 },
                        "Priority split spent adjusted Magic twice.");
                    Require(IssuedElements(page).OfType<Label>().Any(item => item.Text == Sr6CreationCopy.TalentBudget(quote.TalentAllocation!)),
                        "Saved talent budget was not rendered from the Core preview.");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    Element<Picker>("talent-power-points").SelectedIndex = 0;
                    staleConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)staleConfirm).SendClicked());
                    Require(probe.Confirms == 2, "Departed talent controls dispatched a write.");
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator, talentMode: true);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Picker>().Single(item => item.AutomationId == "sr6-foundation-talent-power-points").SelectedIndex == 2,
                        "Talent allocation was not restored.");
                    Require(!IssuedElements(reopened).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Saved talent history became current confirmation authority.");
                    IssuedPageLifecycle(reopened, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 talent phone " + method + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
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
                    var initial = (await runtime.Coordinator.LoadSr6FoundationAsync()).Value!;
                    Require((await runtime.Coordinator.PreviewSr6FoundationAsync(initial, null!)).Value is null,
                        "Null selection must remain a rejected preview, not a crash.");
                    var seedSelection = new Sr6CreationFoundationSelection("human", "technomancer", method == "PointBuy" ? [] : [
                        new("heritage", "C"), new("talent", "A"), new("attributes", "B"), new("skills", "D"), new("resources", "E")])
                    {
                        PointBuy = method == "PointBuy" ? new(0, 0, 2, 0) : null,
                        Attributes = new(Sr6CreationAttributeIds.Ordered.Select(attribute =>
                            new Sr6CreationAttributeSpend(attribute, 0, attribute == "Resonance" ? 2 : 0)).ToArray()),
                        TalentAllocation = new(0)
                    };
                    var seed = (await runtime.Coordinator.PreviewSr6FoundationAsync(initial, seedSelection)).Value!;
                    var committed = await runtime.Coordinator.ConfirmSr6FoundationAsync(seed, true);
                    Require(committed.Commit is not null, "Complex form seed not saved.");
                    var catalog = committed.State!.ComplexFormOptions!.ToArray();
                    var page = new Sr6CreationFoundationPage(runtime.Coordinator, formsMode: true);
                    var navigation = new NavigationPage(new ContentPage());
                    await navigation.PushAsync(page, false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task Click(string key)
                    {
                        if (!key.StartsWith("form-", StringComparison.Ordinal))
                            return ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                        ((IButtonController)Element<Button>(key)).SendClicked();
                        return Task.CompletedTask;
                    }
                    async Task Add(string catalogId)
                    {
                        Element<Picker>("form-catalog").SelectedIndex = Array.FindIndex(catalog, item => item.Id == catalogId);
                        await Click("form-add");
                    }
                    Require(page.Title == Sr6CreationCopy.Text("FormsTitle") && page.Title != "FormsTitle", "Forms title missing.");
                    Require(Element<Picker>("form-catalog").Items.SequenceEqual(catalog.Select(row => row.SourceName)),
                        "Native page invented a non-Core form catalog.");
                    await Add("editor");
                    await Click("preview");
                    Require(!IssuedElements(page).OfType<Label>().Any(item => item.Text == Sr6CreationCopy.Text("TalentHelp")),
                        "Complex-form review must not claim this is the budget-only page.");
                    var oldConfirm = Element<Button>("confirm");
                    await Add("cleaner");
                    oldConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
                    Require(probe!.Confirms == 1, "Old form review admitted a changed selection.");
                    await Add("editor");
                    await Click("preview");
                    Require(!IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Duplicate form gained confirmation.");
                    Require(Element<Label>("status").Text == Sr6CreationCopy.Text("FormsInvalid"), "Invalid form feedback not localized.");
                    await Click("form-remove-2");
                    await Add("diffusion-firewall");
                    await Add("emulate-autosoft-targeting");
                    await Click("preview");
                    Require(!IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                        "Missing weapon identity gained confirmation.");
                    Element<Entry>("form-subject-3").Text = " Ares Alpha ";
                    await Click("preview");
                    Require(IssuedElements(page).OfType<Label>().Any(item => item.Text == Sr6CreationCopy.Text("FormsSubjectReview")),
                        "Free-text weapon model lost its GM review boundary.");
                    await Click("confirm");
                    var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var quote = saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(saved.ContentRevision == 3 && saved.SavedRevision == 3 && probe.Confirms == 2,
                        "Complex form save was duplicated or lost.");
                    Require(quote.ComplexForms is { Forms.Count: 4 } forms
                        && forms.CharacterPointCost == (method == "PointBuy" ? 8 : 0)
                        && forms.FreeSlotsUsed == (method == "PointBuy" ? 0 : 4), "Complex form budget mismatch.");
                    if (method == "PointBuy") Require(quote.PointBuy is { PointsSpent: 26, PointsRemaining: 74, ComplexFormCost: 8 },
                        "Form CP costs did not join the purchased pool budget.");
                    var oldAdd = Element<Button>("form-add");
                    var oldPicker = Element<Picker>("form-catalog");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    oldPicker.SelectedIndex = 0;
                    oldAdd.IsEnabled = true;
                    ((IButtonController)oldAdd).SendClicked();
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator, formsMode: true);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Entry>().Single().Text == "Ares Alpha", "Form subject did not restore.");
                    Require(IssuedElements(reopened).OfType<Button>().Count(item => item.AutomationId?.StartsWith("sr6-foundation-form-remove-", StringComparison.Ordinal) == true) == 4,
                        "Exact form selections did not restore.");
                    Require(!IssuedElements(reopened).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                        && probe.Confirms == 2, "Restored/departed form controls replayed confirmation.");
                    IssuedPageLifecycle(reopened, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 complex forms phone " + method + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
            }

            foreach (var (method, aspect) in new (string, string?)[]
                { ("Priority", null), ("SumtoTen", null), ("PointBuy", null), ("PointBuy", "Enchanting") })
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
                    var initial = (await runtime.Coordinator.LoadSr6FoundationAsync()).Value!;
                    var seedSelection = new Sr6CreationFoundationSelection("human", aspect is null ? "magician" : "aspected-magician",
                        method == "PointBuy" ? [] : [new("heritage", "C"), new("talent", "A"),
                            new("attributes", "B"), new("skills", "D"), new("resources", "E")])
                    {
                        PointBuy = method == "PointBuy" ? new(0, 0, 2, 0) : null,
                        Attributes = new(Sr6CreationAttributeIds.Ordered.Select(attribute =>
                            new Sr6CreationAttributeSpend(attribute, 0, attribute == "Magic" ? 2 : 0)).ToArray()),
                        Skills = aspect is null ? null : new([], aspect), TalentAllocation = new(0)
                    };
                    var seed = (await runtime.Coordinator.PreviewSr6FoundationAsync(initial, seedSelection)).Value!;
                    var committed = await runtime.Coordinator.ConfirmSr6FoundationAsync(seed, true);
                    Require(committed.Commit is not null, "Spell seed not saved.");
                    var catalog = committed.State!.SpellOptions!.ToArray();
                    Require(catalog.Length == (aspect is null ? 81 : 73)
                        && (aspect is null || catalog.All(row => row.Kind == "spell")), "Wrong aspect catalog.");
                    var parent = new Sr6CreationFoundationPage(runtime.Coordinator);
                    var navigation = new NavigationPage(parent);
                    var window = new Window(navigation);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                    var open = IssuedElements(parent).OfType<Button>().Single(item => item.AutomationId == "sr6-foundation-spells");
                    Require(open.IsEnabled, "Saved caster did not gain its spell route.");
                    await ui.BeginAsyncVoid(() => ((IButtonController)open).SendClicked());
                    IssuedPageLifecycle(parent, "OnDisappearing");
                    var page = (Sr6CreationFoundationPage)navigation.Navigation.NavigationStack.Last();
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task Click(string key)
                    {
                        if (!key.StartsWith("spell-", StringComparison.Ordinal))
                            return ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                        ((IButtonController)Element<Button>(key)).SendClicked();
                        return Task.CompletedTask;
                    }
                    async Task Add(string catalogId)
                    {
                        Element<Picker>("spell-catalog").SelectedIndex = Array.FindIndex(catalog, item => item.Id == catalogId);
                        await Click("spell-add");
                    }
                    Require(page.Title == Sr6CreationCopy.Text("SpellsTitle") && page.Title != "SpellsTitle", "Unlocalized spell title.");
                    Require(Element<Picker>("spell-catalog").Items.SequenceEqual(catalog.Select(Sr6CreationCopy.SpellName)),
                        "Native spell catalog differs from Core.");
                    await Add("spell-heal");
                    await Click("preview");
                    var oldConfirm = Element<Button>("confirm");
                    await Add(aspect is null ? "ritual-ward" : "spell-increase-attribute");
                    oldConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
                    Require(probe!.Confirms == 1, "Changed spell selection reused old confirmation.");
                    await Add("spell-heal");
                    await Click("preview");
                    Require(!IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                        && Element<Label>("status").Text == Sr6CreationCopy.Text("SpellsInvalid"), "Duplicate spell not blocked/localized.");
                    await Click("spell-remove-2");
                    await Click("preview");
                    Require(IssuedElements(page).OfType<Label>().Any(item => item.Text == Sr6CreationCopy.Text(
                        "SpellsUse." + (aspect is null ? "sorcery-and-enchanting" : "enchanting"))), "Spell use boundary missing.");
                    Require(!IssuedElements(page).OfType<Label>().Any(item => item.Text == Sr6CreationCopy.Text("TalentHelp")),
                        "Spell selection claimed to be the budget-only page.");
                    await Click("confirm");
                    var stored = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var quote = stored.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(stored.ContentRevision == 3 && stored.SavedRevision == 3 && probe.Confirms == 2,
                        "Spell save duplicated or lost.");
                    Require(quote.Spells is { Spells.Count: 2 } selected && selected.CharacterPointCost == (method == "PointBuy" ? 4 : 0)
                        && selected.FreeSlotsUsed == (method == "PointBuy" ? 0 : 2), "Spell cost/grant mismatch.");
                    if (method == "PointBuy") Require(quote.PointBuy is { PointsSpent: 22, PointsRemaining: 78, SpellCost: 4 }, "Spell CP absent from total.");
                    var oldAdd = Element<Button>("spell-add");
                    var oldPicker = Element<Picker>("spell-catalog");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    oldPicker.SelectedIndex = 0;
                    oldAdd.IsEnabled = true;
                    ((IButtonController)oldAdd).SendClicked();
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator, spellsMode: true);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Button>().Count(item => item.AutomationId?.StartsWith("sr6-foundation-spell-remove-", StringComparison.Ordinal) == true) == 2,
                        "Saved spell choices did not restore.");
                    Require(!IssuedElements(reopened).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                        && probe.Confirms == 2, "Restored/departed spell controls replayed writes.");
                    IssuedPageLifecycle(reopened, "OnDisappearing");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 spells phone " + method + " " + (aspect ?? "full") + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
            }

            foreach (string method in new[] { "Priority", "SumtoTen", "PointBuy" })
            foreach (string talent in new[] { "adept", "mystic-adept" })
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
                    var initial = (await runtime.Coordinator.LoadSr6FoundationAsync()).Value!;
                    var seedSelection = new Sr6CreationFoundationSelection("human", talent,
                        method == "PointBuy" ? [] : [new("heritage", "C"), new("talent", "A"),
                            new("attributes", "B"), new("skills", "D"), new("resources", "E")])
                    {
                        PointBuy = method == "PointBuy" ? new(0, 0, 2, 0) : null,
                        Attributes = new(Sr6CreationAttributeIds.Ordered.Select(attribute =>
                            new Sr6CreationAttributeSpend(attribute, 0, attribute == "Magic" ? 2 : 0)).ToArray()),
                        Skills = new([new("Athletics", 3, [])]),
                        TalentAllocation = new(method == "PointBuy" || talent == "mystic-adept" ? 3 : 0)
                    };
                    var seed = (await runtime.Coordinator.PreviewSr6FoundationAsync(initial, seedSelection)).Value!;
                    var committed = await runtime.Coordinator.ConfirmSr6FoundationAsync(seed, true);
                    Require(committed.Commit is not null, "Power seed not saved.");
                    var catalog = committed.State!.AdeptPowerOptions!.ToArray();
                    Require(catalog.Length == 51, "Wrong Core adept-power catalog.");
                    var parent = new Sr6CreationFoundationPage(runtime.Coordinator);
                    var navigation = new NavigationPage(parent);
                    var window = new Window(navigation);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                    var open = IssuedElements(parent).OfType<Button>().Single(item => item.AutomationId == "sr6-foundation-powers");
                    Require(open.IsEnabled, "Saved adept budget did not enable power page.");
                    await ui.BeginAsyncVoid(() => ((IButtonController)open).SendClicked());
                    IssuedPageLifecycle(parent, "OnDisappearing");
                    var page = (Sr6CreationFoundationPage)navigation.Navigation.NavigationStack.Last();
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task Click(string key)
                    {
                        if (!key.StartsWith("power-", StringComparison.Ordinal))
                            return ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                        ((IButtonController)Element<Button>(key)).SendClicked();
                        return Task.CompletedTask;
                    }
                    async Task Add(string catalogId, int level = 1)
                    {
                        Element<Picker>("power-catalog").SelectedIndex = Array.FindIndex(catalog, item => item.Id == catalogId);
                        Element<Picker>("power-rating").SelectedIndex = level - 1;
                        Require(Element<Button>("power-add").IsEnabled, "Valid power level could not be added.");
                        await Click("power-add");
                    }
                    Require(page.Title == Sr6CreationCopy.Text("PowersTitle") && page.Title != "PowersTitle", "Power title not localized.");
                    Require(Element<Picker>("power-catalog").Items.SequenceEqual(catalog.Select(Sr6CreationCopy.PowerName)), "Native catalog differs from Core.");
                    Element<Picker>("power-catalog").SelectedIndex = Array.FindIndex(catalog, item => item.Id == "improved-ability-firearms-all");
                    Require(!Element<Button>("power-add").IsEnabled && Element<Picker>("power-rating").Items.Count == 0,
                        "Untrained improved skill was selectable.");
                    await Add("astral-perception");
                    await Click("preview");
                    var oldConfirm = Element<Button>("confirm");
                    await Add("mystic-armor");
                    oldConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
                    Require(probe!.Confirms == 1, "Power change reused old confirmation.");
                    await Add("mystic-armor");
                    await Click("preview");
                    Require(!IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                        && Element<Label>("status").Text == Sr6CreationCopy.Text("PowersInvalid"), "Duplicate power not rejected/localized.");
                    await Click("power-remove-2");
                    await Add("improved-ability-athletics-noncombat", 2);
                    await Click("preview");
                    Require(IssuedElements(page).OfType<Label>().Any(item => item.Text?.Contains(Sr6CreationCopy.Text("PowersUse.noncombat"), StringComparison.Ordinal) == true),
                        "Noncombat scope missing from review.");
                    await Click("confirm");
                    var stored = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var quote = stored.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(stored.ContentRevision == 3 && stored.SavedRevision == 3 && probe.Confirms == 2, "Power save duplicated or lost.");
                    Require(quote.AdeptPowers is { Powers.Count: 3, QuarterPointsSpent: 9 }, "Power fractional cost wrong.");
                    Require(quote.PointBuy?.PointsSpent == seed.PointBuy?.PointsSpent, "Power allocation charged CP a second time.");
                    var oldAdd = Element<Button>("power-add");
                    var oldPicker = Element<Picker>("power-catalog");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    oldPicker.SelectedIndex = 0;
                    oldAdd.IsEnabled = true;
                    ((IButtonController)oldAdd).SendClicked();
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator, powersMode: true);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Button>().Count(item => item.AutomationId?.StartsWith("sr6-foundation-power-remove-", StringComparison.Ordinal) == true) == 3,
                        "Saved powers did not restore.");
                    Require(!IssuedElements(reopened).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                        && probe.Confirms == 2, "Restored/departed power controls replayed writes.");
                    IssuedPageLifecycle(reopened, "OnDisappearing");
                    var restored = (await runtime.Coordinator.LoadSr6FoundationAsync()).Value!;
                    Require(restored.SkillOptions!.Single(row => row.SkillId == "Astral").Available, "Saved Astral Perception did not unlock skill.");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 adept powers phone " + method + " " + talent + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
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
                    var initial = (await runtime.Coordinator.LoadSr6FoundationAsync()).Value!;
                    var selection = new Sr6CreationFoundationSelection("human", "mundane",
                        method == "PointBuy" ? [] : [new("heritage", "D"), new("talent", "E"),
                            new("attributes", "A"), new("skills", "B"), new("resources", "C")])
                    {
                        PointBuy = method == "PointBuy" ? new(0, 0, 0, 0) : null,
                        Attributes = new(Sr6CreationAttributeIds.Ordered.Select(attribute => new Sr6CreationAttributeSpend(attribute, 0, 0)).ToArray()),
                        Skills = new([new("Athletics", 1, [])])
                    };
                    var seed = (await runtime.Coordinator.PreviewSr6FoundationAsync(initial, selection)).Value!;
                    var committed = await runtime.Coordinator.ConfirmSr6FoundationAsync(seed, true);
                    Require(committed.Commit is not null && committed.State!.KarmaOptions is not null, "Karma seed not saved.");
                    var options = committed.State!.KarmaOptions!;
                    var catalog = options.Attributes.Concat(options.Skills).ToArray();
                    var parent = new Sr6CreationFoundationPage(runtime.Coordinator);
                    var navigation = new NavigationPage(parent);
                    var window = new Window(navigation);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                    var open = IssuedElements(parent).OfType<Button>().Single(item => item.AutomationId == "sr6-foundation-karma");
                    Require(open.IsEnabled, "Saved allocations did not enable Karma.");
                    await ui.BeginAsyncVoid(() => ((IButtonController)open).SendClicked());
                    IssuedPageLifecycle(parent, "OnDisappearing");
                    var page = (Sr6CreationFoundationPage)navigation.Navigation.NavigationStack.Last();
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task Click(string key)
                    {
                        if (!key.StartsWith("karma-", StringComparison.Ordinal))
                            return ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                        ((IButtonController)Element<Button>(key)).SendClicked(); return Task.CompletedTask;
                    }
                    async Task Add(string target, int levels, string? subject = null)
                    {
                        Element<Picker>("karma-catalog").SelectedIndex = Array.FindIndex(catalog, row => row.Id == target);
                        Element<Picker>("karma-increase").SelectedIndex = levels - 1;
                        if (subject is not null) Element<Entry>("karma-exotic").Text = subject;
                        Require(Element<Button>("karma-add").IsEnabled, "Valid Karma increase not selectable.");
                        await Click("karma-add");
                    }
                    Require(page.Title == Sr6CreationCopy.Text("KarmaTitle") && page.Title != "KarmaTitle", "Karma title not localized.");
                    Element<Picker>("karma-catalog").SelectedIndex = Array.FindIndex(catalog, row => row.Id == "Magic");
                    Require(!Element<Button>("karma-add").IsEnabled && Element<Picker>("karma-increase").Items.Count == 0,
                        "Karma could awaken a mundane character.");
                    await Add("Body", 2);
                    await Click("preview");
                    var oldConfirm = Element<Button>("confirm");
                    Element<Picker>("karma-nuyen").SelectedIndex = 1;
                    oldConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
                    Require(probe!.Confirms == 1, "Cash change reused stale Karma confirmation.");
                    await Add("Body", 1);
                    await Click("preview");
                    Require(Element<Label>("status").Text == Sr6CreationCopy.Text("KarmaInvalid"), "Duplicate Karma target was admitted.");
                    await Click("karma-remove-attribute-1");
                    await Add("Athletics", 1);
                    await Add("ExoticWeapons", 1, "Whip");
                    Element<Picker>("karma-nuyen").SelectedIndex = 11;
                    await Click("preview");
                    Require(!IssuedElements(page).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm")
                        && Element<Label>("status").Text == Sr6CreationCopy.Text("KarmaOverspend"), "Karma overspend was admitted/unlocalized.");
                    Element<Picker>("karma-nuyen").SelectedIndex = 10;
                    await Click("preview");
                    Require(IssuedElements(page).OfType<Label>().Any(item => item.Text?.Contains("10 + 15", StringComparison.Ordinal) == true),
                        "Cumulative Karma cost trace absent.");
                    await Click("confirm");
                    var stored = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var quote = stored.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(stored.ContentRevision == 3 && stored.SavedRevision == 3 && probe.Confirms == 2, "Karma save lost or duplicated.");
                    Require(quote.Karma is { KarmaSpent: 50, KarmaRemaining: 0, AdditionalNuyen: 20000 }
                        && quote.Karma.Attributes.Single().Rating == 3 && quote.Karma.Skills.Single(row => row.Id == "Athletics").Rating == 2,
                        "Saved Karma cost or final rating wrong.");
                    Require(quote.Skills!.PointsSpent == 1 && quote.PointBuy?.PointsSpent == seed.PointBuy?.PointsSpent,
                        "Karma altered pool or CP costs.");
                    var oldAdd = Element<Button>("karma-add");
                    var oldCash = Element<Picker>("karma-nuyen");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    oldCash.SelectedIndex = 0; oldAdd.IsEnabled = true;
                    ((IButtonController)oldAdd).SendClicked();
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator, karmaMode: true);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Button>().Count(item => item.AutomationId?.StartsWith("sr6-foundation-karma-remove-", StringComparison.Ordinal) == true) == 3,
                        "Karma purchases did not restore.");
                    Require(IssuedElements(reopened).OfType<Picker>().Single(item => item.AutomationId == "sr6-foundation-karma-nuyen").SelectedIndex == 10
                        && !IssuedElements(reopened).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm") && probe.Confirms == 2,
                        "Departed or restored Karma controls changed saved state.");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 Karma phone " + method + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
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
                    var initial = (await runtime.Coordinator.LoadSr6FoundationAsync()).Value!;
                    var selection = new Sr6CreationFoundationSelection("human", "mundane",
                        method == "PointBuy" ? [] : [new("heritage", "D"), new("talent", "E"),
                            new("attributes", "A"), new("skills", "B"), new("resources", "C")])
                    {
                        PointBuy = method == "PointBuy" ? new(0, 0, 0, 0) : null,
                        Attributes = new(Sr6CreationAttributeIds.Ordered.Select(attribute => new Sr6CreationAttributeSpend(attribute, 0, 0)).ToArray()),
                        Skills = new([new("Athletics", 1, []), new("Firearms", 1, ["Pistols"]), new("ExoticWeapons", 1, ["Whip"])])
                    };
                    var seed = (await runtime.Coordinator.PreviewSr6FoundationAsync(initial, selection)).Value!;
                    var committed = await runtime.Coordinator.ConfirmSr6FoundationAsync(seed, true);
                    var options = committed.State!.KarmaSpecializationOptions!;
                    Require(!options.ExpertiseAvailable && options.KarmaCost == 5, "Creation specialty options wrong.");
                    var page = new Sr6CreationFoundationPage(runtime.Coordinator, karmaMode: true);
                    var navigation = new NavigationPage(page);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task Click(string key)
                    {
                        if (!key.StartsWith("karma-", StringComparison.Ordinal))
                            return ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                        ((IButtonController)Element<Button>(key)).SendClicked(); return Task.CompletedTask;
                    }
                    async Task Add(string skill, string subject)
                    {
                        Element<Picker>("karma-specialization-skill").SelectedIndex = options.Skills.ToList().FindIndex(row => row.SkillId == skill);
                        Element<Entry>("karma-specialization-subject").Text = subject;
                        Require(Element<Button>("karma-specialization-add").IsEnabled, "Specialty add not enabled.");
                        await Click("karma-specialization-add");
                    }
                    Element<Picker>("karma-specialization-skill").SelectedIndex = options.Skills.ToList().FindIndex(row => row.SkillId == "Firearms");
                    Element<Entry>("karma-specialization-subject").Text = "Rifles";
                    Require(!Element<Button>("karma-specialization-add").IsEnabled
                        && Element<Label>("karma-specialization-details").Text!.Contains(Sr6CreationCopy.Text("KarmaSpecializationLimit"), StringComparison.Ordinal),
                        "Pool specialty was not counted against creation cap.");
                    await Add("Athletics", "Climbing");
                    await Click("preview");
                    var oldConfirm = Element<Button>("confirm");
                    await Add("ExoticWeapons", "Laser");
                    oldConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
                    Require(probe!.Confirms == 1, "Adding a specialty reused stale confirmation.");
                    foreach (var invalid in new[] { ("Athletics", "Swimming", "KarmaSpecializationLimit"),
                        ("ExoticWeapons", "WHIP", "KarmaSpecializationLimit"), ("Perception", "Searching", "KarmaSpecializationRatingRequired") })
                    {
                        await Add(invalid.Item1, invalid.Item2);
                        await Click("preview");
                        Require(Element<Label>("status").Text == Sr6CreationCopy.Text(invalid.Item3), "Specialty restriction not enforced/localized.");
                        await Click("karma-specialization-remove-2");
                    }
                    Element<Picker>("karma-nuyen").SelectedIndex = 41;
                    await Click("preview");
                    Require(Element<Label>("status").Text == Sr6CreationCopy.Text("KarmaOverspend"), "Specialty costs missed shared budget.");
                    Element<Picker>("karma-nuyen").SelectedIndex = 40;
                    await Click("preview");
                    Require(IssuedElements(page).OfType<Label>().Any(row => row.Text == Sr6CreationCopy.KarmaSpecializationValue(new("ExoticWeapons", "Laser", 5, 0, true))),
                        "Exotic specialty review invented a bonus or lost cost.");
                    Require(IssuedElements(page).OfType<Label>().Any(row => row.Text == Sr6CreationCopy.Text("SkillGmReview")), "Free text missing GM-review warning.");
                    await Click("confirm");
                    var stored = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var quote = stored.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(stored.ContentRevision == 3 && stored.SavedRevision == 3 && probe.Confirms == 2
                        && quote.Karma!.KarmaSpent == 50 && quote.Karma.Specializations!.Count == 2, "Specialty save lost or duplicated.");
                    Require(quote.Skills!.PointsSpent == seed.Skills!.PointsSpent && quote.PointBuy?.PointsSpent == seed.PointBuy?.PointsSpent,
                        "Specialties changed base pool costs.");
                    var oldAdd = Element<Button>("karma-specialization-add");
                    var oldSubject = Element<Entry>("karma-specialization-subject");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    oldSubject.Text = "stale"; oldAdd.IsEnabled = true; ((IButtonController)oldAdd).SendClicked();
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator, karmaMode: true);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Button>().Count(row => row.AutomationId?.StartsWith("sr6-foundation-karma-specialization-remove-", StringComparison.Ordinal) == true) == 2,
                        "Specialties did not reopen.");
                    Require(!IssuedElements(reopened).OfType<Button>().Any(row => row.AutomationId == "sr6-foundation-confirm") && probe.Confirms == 2,
                        "Reopened specialties exposed historical confirmation.");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 Karma specialties phone " + method + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
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
                    Guid spanish = Guid.NewGuid();
                    var initial = (await runtime.Coordinator.LoadSr6FoundationAsync()).Value!;
                    var selection = new Sr6CreationFoundationSelection("human", "mundane",
                        method == "PointBuy" ? [] : [new("heritage", "D"), new("talent", "E"),
                            new("attributes", "A"), new("skills", "B"), new("resources", "C")])
                    {
                        PointBuy = method == "PointBuy" ? new(0, 0, 0, 0) : null,
                        Attributes = new(Sr6CreationAttributeIds.Ordered.Select(attribute => new Sr6CreationAttributeSpend(attribute, 0, 0)).ToArray()),
                        Skills = new([]), Knowledge = new("English", [], [new(spanish, "Spanish", "basic")])
                    };
                    var seed = (await runtime.Coordinator.PreviewSr6FoundationAsync(initial, selection)).Value!;
                    await runtime.Coordinator.ConfirmSr6FoundationAsync(seed, true);
                    var page = new Sr6CreationFoundationPage(runtime.Coordinator, karmaMode: true);
                    var navigation = new NavigationPage(page);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(page, window);
                    await alerts.PreflightAsync();
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                    T Element<T>(string key) where T : Element => IssuedElements(page).OfType<T>()
                        .Single(item => item.AutomationId == "sr6-foundation-" + key);
                    Task Click(string key)
                    {
                        if (!key.StartsWith("karma-", StringComparison.Ordinal))
                            return ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(key)).SendClicked());
                        ((IButtonController)Element<Button>(key)).SendClicked(); return Task.CompletedTask;
                    }
                    Require(Element<Picker>("karma-knowledge-level").Items.Count == 3, "Native language was exposed as a Karma purchase.");
                    Element<Entry>("karma-knowledge-topic").Text = "Seattle gangs";
                    await Click("karma-knowledge-add-topic");
                    await Click("preview");
                    var oldConfirm = Element<Button>("confirm");
                    async Task AddLanguage(int index, string name, int level)
                    {
                        Element<Picker>("karma-knowledge-language").SelectedIndex = index;
                        Element<Entry>("karma-knowledge-name").Text = name;
                        Element<Picker>("karma-knowledge-level").SelectedIndex = level;
                        Require(Element<Button>("karma-knowledge-add-language").IsEnabled, "Language purchase not enabled.");
                        await Click("karma-knowledge-add-language");
                    }
                    await AddLanguage(1, "Ignored rename", 2);
                    oldConfirm.IsEnabled = true;
                    await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
                    Require(probe!.Confirms == 1, "Language edit reused an old confirmation.");
                    await AddLanguage(0, "German", 2);
                    await AddLanguage(0, "ENGLISH", 0);
                    await Click("preview");
                    Require(Element<Label>("status").Text == Sr6CreationCopy.Text("KarmaKnowledgeConflict"), "Native-language duplicate not rejected/localized.");
                    var removeDuplicate = IssuedElements(page).OfType<Button>().Last(row =>
                        row.AutomationId?.StartsWith("sr6-foundation-karma-knowledge-remove-language-", StringComparison.Ordinal) == true);
                    ((IButtonController)removeDuplicate).SendClicked();
                    Element<Picker>("karma-nuyen").SelectedIndex = 33;
                    await Click("preview");
                    Require(Element<Label>("status").Text == Sr6CreationCopy.Text("KarmaOverspend"), "Knowledge/language costs omitted from shared budget.");
                    Element<Picker>("karma-nuyen").SelectedIndex = 32;
                    await Click("preview");
                    Require(IssuedElements(page).OfType<Label>().Any(row => row.Text ==
                        Sr6CreationCopy.KarmaLanguageValue(new(spanish, "Spanish", "basic", "expert", 2, 6, 3))),
                        "Existing language was renamed, charged twice or lost its exact rank delta.");
                    Require(IssuedElements(page).OfType<Label>().Any(row => row.Text == Sr6CreationCopy.Text("KnowledgeGmReview")), "Knowledge lacked GM review.");
                    await Click("confirm");
                    var stored = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                    var quote = stored.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Preview;
                    Require(stored.ContentRevision == 3 && stored.SavedRevision == 3 && probe.Confirms == 2
                        && quote.Karma!.KarmaSpent == 50 && quote.Karma.Knowledge!.KarmaCost == 18,
                        "Karma knowledge save lost or duplicated.");
                    Require(quote.Knowledge!.PointsSpent == 1 && quote.Knowledge.Languages.Single().Level == "basic"
                        && quote.PointBuy?.PointsSpent == seed.PointBuy?.PointsSpent, "Karma purchase changed the free pool or CP.");
                    Guid topicId = quote.Karma!.Knowledge!.KnowledgeSkills.Single().Id;
                    var oldAdd = Element<Button>("karma-knowledge-add-topic");
                    var oldTopic = Element<Entry>("karma-knowledge-topic");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    oldTopic.Text = "stale"; oldAdd.IsEnabled = true; ((IButtonController)oldAdd).SendClicked();
                    await runtime.Presenter.LoadAsync(id, default);
                    var reopened = new Sr6CreationFoundationPage(runtime.Coordinator, karmaMode: true);
                    await navigation.PushAsync(reopened, false);
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(reopened, "OnAppearing"));
                    Require(IssuedElements(reopened).OfType<Button>().Any(row =>
                        row.AutomationId == "sr6-foundation-karma-knowledge-remove-topic-" + topicId.ToString("N")), "Knowledge identity did not survive reopen.");
                    Require(IssuedElements(reopened).OfType<Button>().Count(row => row.AutomationId?.StartsWith(
                        "sr6-foundation-karma-knowledge-remove-language-", StringComparison.Ordinal) == true) == 2,
                        "Language purchases did not reopen.");
                    Require(!IssuedElements(reopened).OfType<Button>().Any(row => row.AutomationId == "sr6-foundation-confirm") && probe.Confirms == 2,
                        "Reopened Karma knowledge exposed historical confirmation.");
                    ui.AssertHealthy();
                    Console.WriteLine("PASS SR6 Karma knowledge phone " + method + " " + language);
                }
                finally { CultureInfo.CurrentUICulture = previousCulture; }
            }

            // Reuse the actual parent page, rather than constructing a replacement
            // after each child save. Unsaved input must not be rebased onto new state.
            {
                var owners = new ControlledLinkedOwner();
                Sr6Probe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners, sr6Decorator: actual => probe = new(actual, ui));
                await Bootstrap(runtime, "PointBuy");
                var id = runtime.Coordinator.State.WorkspaceId!.Value;
                var parent = new Sr6CreationFoundationPage(runtime.Coordinator);
                var navigation = new NavigationPage(new ContentPage());
                await navigation.PushAsync(parent, false);
                var window = new Window(navigation);
                using var parentAlerts = new IssuedPageAlerts(parent, window);
                await parentAlerts.PreflightAsync();
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                T Element<T>(ContentPage page, string key) where T : Element => IssuedElements(page).OfType<T>()
                    .Single(item => item.AutomationId == "sr6-foundation-" + key);
                Task Click(ContentPage page, string key) => ui.BeginAsyncVoid(() => ((IButtonController)Element<Button>(page, key)).SendClicked());
                Element<Picker>(parent, "point-buy-adjustment").SelectedIndex = 2;
                Element<Picker>(parent, "metatype").SelectedIndex = 0;
                Element<Picker>(parent, "talent").SelectedIndex = 4;
                await Click(parent, "preview");
                var oldConfirm = Element<Button>(parent, "confirm");
                await Click(parent, "confirm");
                var oldPicker = Element<Picker>(parent, "metatype");
                var oldAttributeOpen = Element<Button>(parent, "attributes");
                await Click(parent, "attributes");
                var attributes = (Sr6CreationFoundationPage)navigation.Navigation.NavigationStack.Last();
                Require(!ReferenceEquals(parent, attributes), "Attribute navigation did not push a child.");
                IssuedPageLifecycle(parent, "OnDisappearing");
                using var attributeAlerts = new IssuedPageAlerts(attributes, window);
                await attributeAlerts.PreflightAsync();
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(attributes, "OnAppearing"));
                Element<Picker>(attributes, "adjustment-Magic").SelectedIndex = 2;
                await Click(attributes, "preview");
                await Click(attributes, "confirm");
                IssuedPageLifecycle(attributes, "OnDisappearing");
                await navigation.PopAsync(false);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                Require(Element<Button>(parent, "talent-budget").IsEnabled
                    && Element<Button>(parent, "knowledge").IsEnabled
                    && Element<Label>(parent, "status").Text == string.Empty,
                    "Returning from a saved child halted the clean parent.");
                int pageCount = navigation.Navigation.NavigationStack.Count;
                oldPicker.SelectedIndex = 1;
                oldAttributeOpen.IsEnabled = true;
                await ui.BeginAsyncVoid(() => ((IButtonController)oldAttributeOpen).SendClicked());
                oldConfirm.IsEnabled = true;
                await ui.BeginAsyncVoid(() => ((IButtonController)oldConfirm).SendClicked());
                Require(probe!.Confirms == 2 && navigation.Navigation.NavigationStack.Count == pageCount
                    && Element<Picker>(parent, "metatype").SelectedIndex == 0,
                    "Returning made old parent controls current again.");
                await Click(parent, "talent-budget");
                var talent = (Sr6CreationFoundationPage)navigation.Navigation.NavigationStack.Last();
                IssuedPageLifecycle(parent, "OnDisappearing");
                using var talentAlerts = new IssuedPageAlerts(talent, window);
                await talentAlerts.PreflightAsync();
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(talent, "OnAppearing"));
                Element<Picker>(talent, "talent-power-points").SelectedIndex = 2;
                await Click(talent, "preview");
                await Click(talent, "confirm");
                IssuedPageLifecycle(talent, "OnDisappearing");
                await navigation.PopAsync(false);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                await Click(parent, "preview");
                Require(IssuedElements(parent).OfType<Label>().Any(item => item.Text?.Contains("Power-point budget 2", StringComparison.Ordinal) == true),
                    "Parent kept the old selection and lost its child's saved talent allocation.");
                Require(probe.Confirms == 3, "Parent refresh replayed a child write.");
                Console.WriteLine("PASS SR6 clean parent child-save return");

                Element<Picker>(parent, "metatype").SelectedIndex = 1;
                await Click(parent, "preview");
                var dirtyConfirm = Element<Button>(parent, "confirm");
                IssuedPageLifecycle(parent, "OnDisappearing");
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                Require(Element<Picker>(parent, "metatype").SelectedIndex == 1
                    && !IssuedElements(parent).OfType<Button>().Any(item => item.AutomationId == "sr6-foundation-confirm"),
                    "Same-revision return discarded edits or retained old confirmation authority.");
                Console.WriteLine("PASS SR6 unsaved same-revision return");

                IssuedPageLifecycle(parent, "OnDisappearing");
                var loaded = await runtime.Coordinator.LoadSr6FoundationAsync();
                var changed = await runtime.Coordinator.PreviewSr6FoundationAsync(loaded.Value!,
                    loaded.Value!.Selection!.Selection with { Skills = new([]) });
                Require(changed.Value is not null
                    && (await runtime.Coordinator.ConfirmSr6FoundationAsync(changed.Value, true)).Commit is not null,
                    "External revision fixture did not save.");
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(parent, "OnAppearing"));
                Require(Element<Label>(parent, "status").Text == Sr6CreationCopy.Text("Stale")
                    && !IssuedElements(parent).OfType<Picker>().Any(), "Unsaved parent was silently rebased onto a newer revision.");
                dirtyConfirm.IsEnabled = true;
                await ui.BeginAsyncVoid(() => ((IButtonController)dirtyConfirm).SendClicked());
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(probe.Confirms == 4 && saved.ContentRevision == 5 && saved.SavedRevision == 5
                    && saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last().Command.Selection.MetatypeId == "human",
                    "Conflicting dirty parent changed the durable runner.");
                IssuedPageLifecycle(parent, "OnDisappearing");
                Console.WriteLine("PASS SR6 dirty parent revision conflict");

                var failedLoad = new Sr6CreationFoundationPage(runtime.Coordinator);
                await navigation.PushAsync(failedLoad, false);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(failedLoad, "OnAppearing"));
                Element<Picker>(failedLoad, "metatype").SelectedIndex = 1;
                IssuedPageLifecycle(failedLoad, "OnDisappearing");
                probe.FailNextLoad = true;
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(failedLoad, "OnAppearing"));
                Require(!probe.FailNextLoad && !IssuedElements(failedLoad).OfType<Picker>().Any(),
                    "A failed reload left dirty controls editable.");
                IssuedPageLifecycle(failedLoad, "OnDisappearing");
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(failedLoad, "OnAppearing"));
                Require(!IssuedElements(failedLoad).OfType<Picker>().Any() && probe.Confirms == 4,
                    "Dirty inputs recovered after losing their editing baseline.");
                IssuedPageLifecycle(failedLoad, "OnDisappearing");
                Console.WriteLine("PASS SR6 dirty parent failed-load fencing");

                var clean = new Sr6CreationFoundationPage(runtime.Coordinator);
                await navigation.PushAsync(clean, false);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(clean, "OnAppearing"));
                IssuedPageLifecycle(clean, "OnDisappearing");
                owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser);
                await runtime.Presenter.LoadAsync(id, default);
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(clean, "OnAppearing"));
                Require(!IssuedElements(clean).OfType<Picker>().Any()
                    && probe.Confirms == 4, "A clean parent adopted an A-to-B-to-A owner transition.");
                IssuedPageLifecycle(clean, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS SR6 clean parent owner-ABA return");
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
        internal bool FailNextLoad;
        internal Action? AfterLoad, AfterPreview, AfterConfirm;
        private void Background() => Require(!ReferenceEquals(SynchronizationContext.Current, ui), "SR6 Core call blocked the UI context.");
        public CharacterCreationFoundationResult<Sr6CreationFoundationState> Load(OwnerContextStamp owner, CharacterWorkspaceId id)
        {
            Background();
            if (FailNextLoad)
            {
                FailNextLoad = false;
                return new(CharacterCreationFoundationOutcomes.Blocked, null, [Sr6CreationFoundationBlockers.StaleBinding]);
            }
            var result = actual.Load(owner, id); AfterLoad?.Invoke(); return result;
        }
        public CharacterCreationFoundationResult<Sr6CreationFoundationPreview> Preview(OwnerContextStamp owner,
            Sr6CreationFoundationBinding binding, Sr6CreationFoundationSelection selection)
        { Background(); var result = actual.Preview(owner, binding, selection); AfterPreview?.Invoke(); return result; }
        public CharacterCreationFoundationResult<Sr6CreationFoundationCommit> Confirm(OwnerContextStamp owner, Sr6CreationFoundationConfirmRequest request)
        { Background(); Confirms++; var result = actual.Confirm(owner, request); AfterConfirm?.Invoke(); return result; }
    }
}
