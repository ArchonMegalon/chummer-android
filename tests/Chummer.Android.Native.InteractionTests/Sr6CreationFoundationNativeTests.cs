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

            foreach (string method in new[] { "Priority", "SumtoTen" })
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
                    Pick("rank-heritage", 3); Pick("rank-talent", 4); Pick("rank-attributes", method == "Priority" ? 0 : 1);
                    Pick("rank-skills", 1); Pick("rank-resources", method == "Priority" ? 2 : 1);
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
