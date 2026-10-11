using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCreationContactsForwardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "wizard", "local", "linked", "add", "remove", "refresh-fault",
                "unknown", "recovered", "owner-b", "owner-aba", "departure", "return" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(
                    scenario == "local" ? "de-AT" : scenario == "refresh-fault" ? "es-MX" : "en-GB");
                var owners = new ControlledLinkedOwner();
                bool linked = scenario is "linked" or "owner-b" or "owner-aba";
                bool adding = scenario is "add" or "wizard";
                owners.Set(linked ? ContactsOwnerA : OwnerScope.LocalSingleUser);
                ContactsBoundaryProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationContacts: true, contactsDecorator: service => probe = new(service), creationLifestyles: true,
                    creationFinalization: scenario == "wizard", productionCreationOverview: scenario == "wizard");
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                if (scenario == "wizard")
                {
                    var ready = PrepareActualFinalizationReadyContext(runtime);
                    await HydrateFinalizationOwnerAsync(runtime, owners, ready);
                }
                else
                {
                    var imported = await runtime.Client.ImportAsync(new WorkspaceImportDocument(ContactsCreationXml, "sr5"), default);
                    runtime.Id = imported.Id;
                    await runtime.Presenter.LoadAsync(runtime.Id, default);
                }
                var coordinator = runtime.Coordinator;
                var original = coordinator.State;
                var state = (await coordinator.LoadCreationContactsForDisplayAsync(original, default)).State
                    ?? throw new InvalidOperationException("SETUP: Contacts authority missing.");
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var before = (linked ? store.Get(ContactsOwnerA, runtime.Id) : store.Get(runtime.Id)).Value!;
                var contact = adding ? CreationContactsPhoneAuthority.NewContact(state,
                    Guid.Parse("84120ed6-05d6-4c38-9af3-a67111fd820d"))! : state.Contacts.Single();
                var draft = new CreationContactsPhoneDraft();
                draft.Bind(state, contact);
                Require(draft.TrySetInteger(state, contact, CharacterCreationContactFieldIds.Loyalty,
                    scenario == "wizard" ? 2 : 3), "SETUP: legal loyalty rejected.");
                if (adding) Require(draft.TrySetText(state, contact, CharacterCreationContactFieldIds.Name,
                    "New Street Doc"), "SETUP: legal name rejected.");
                var input = scenario == "remove" ? new CharacterCreationContactEditInput(contact.ContactId)
                    { ChangeKind = CharacterCreationContactChangeKind.Remove, DisplayOwnerContext = state.DisplayOwnerContext }
                    : draft.ToInput(state, contact, adding)!;
                var prepared = (await coordinator.PrepareCreationContactForDisplayAsync(input, original, default)).PreparedPreview
                    ?? throw new InvalidOperationException("SETUP: preview missing.");
                Require(prepared.CanConfirm, "SETUP: contact exceeds Core authority: " + string.Join(",", prepared.Blockers));
                var root = new ContentPage();
                var navigation = new NavigationPage(root);
                var list = new CreationContactsPage(coordinator, state);
                var editor = new CreationContactEditPage(coordinator, contact.ContactId, adding: adding);
                var review = new CreationContactPreviewPage(coordinator, prepared);
                await navigation.PushAsync(list, false);
                await navigation.PushAsync(editor, false);
                await navigation.PushAsync(review, false);
                var window = new Window(navigation);
                using var alerts = new IssuedPageAlerts(review, window);
                await alerts.PreflightAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing")));
                MinimalVisible(review).OfType<CheckBox>().Single(x => x.AutomationId == "creation-contact-explicit-confirm").IsChecked = true;
                var save = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-contact-confirm");
                Require(save.IsEnabled, "SETUP: confirm disabled.");
                string saving = CreationFlowStrings.Get("Contacts.Saving", "missing");
                probe!.BeforeConfirm = () =>
                {
                    Require(SynchronizationContext.Current != ui, "Contact save blocked UI context.");
                    var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ui.Post(_ =>
                    {
                        try
                        {
                            Require(!save.IsEnabled && save.Text == saving, "Save lacks visible localized feedback.");
                            ((IButtonController)save).SendClicked();
                            observed.SetResult();
                        }
                        catch (Exception error) { observed.SetException(error); }
                    }, null);
                    Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Save prevented UI feedback.");
                    observed.Task.GetAwaiter().GetResult();
                };
                probe.AfterConfirm = () =>
                {
                    if (scenario == "refresh-fault") probe.ThrowLoads = true;
                    if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                    if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                    if (scenario is "departure" or "return")
                    {
                        var departed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            IssuedPageLifecycle(review, "OnDisappearing");
                            if (scenario == "return") IssuedPageLifecycle(review, "OnAppearing");
                            departed.SetResult();
                        }, null);
                        Require(departed.Task.Wait(TimeSpan.FromSeconds(5)), "Departure blocked UI.");
                    }
                    if (scenario == "unknown") { probe.ThrowLookup = true; throw new IOException("synthetic-unknown-commit"); }
                    if (scenario == "recovered") throw new IOException("synthetic-result-loss");
                };
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                probe.BeforeConfirm = null;
                probe.AfterConfirm = null;
                probe.ThrowLoads = false;
                probe.ThrowLookup = false;
                var saved = (linked ? store.Get(ContactsOwnerA, runtime.Id) : store.Get(runtime.Id)).Value!;
                Require(probe.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                    && saved.SavedRevision == saved.ContentRevision, "Contact did not persist exactly once.");
                bool advanced = scenario is "wizard" or "local" or "linked" or "add" or "remove" or "recovered";
                Require(navigation.Navigation.NavigationStack.Last() is CreationContactsPage == advanced,
                    "Wrong automatic Contacts navigation: " + scenario);
                if (scenario == "refresh-fault")
                {
                    var retry = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-contact-continue-contacts");
                    Require(retry.IsEnabled, "Known receipt lost read-only continuation.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                    advanced = true;
                }
                if (advanced)
                {
                    Require(navigation.Navigation.NavigationStack.Count == 2
                        && navigation.Navigation.NavigationStack.Last() is CreationContactsPage,
                        "Saved contact retained consumed edit/review/list detours: " + string.Join(",",
                            navigation.Navigation.NavigationStack.Select(x => x?.GetType().Name ?? "null")));
                    var next = (CreationContactsPage)navigation.Navigation.NavigationStack.Last();
                    int loads = probe.Stamps.Count;
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                    Require(probe.Stamps.Count == loads, "Prepared Contacts needlessly reloaded Core on appearance.");
                    Require(MinimalVisible(next).OfType<Button>().Any(x => x.AutomationId == "creation-contacts-add" && x.IsEnabled),
                        "Save skipped the ability to add another contact.");
                    var forward = MinimalVisible(next).OfType<Button>().Single(x => x.AutomationId == "creation-contacts-open-lifestyles");
                    Require(forward.IsEnabled && forward.Text == CreationFlowStrings.Get("Contacts.ContinueLifestyles", "missing"),
                        "Contacts lacks clear localized wizard continuation.");
                    Require(!MinimalVisible(next).Any(x => x.AutomationId == "creation-contact-receipt-digest"),
                        "Machine receipt leaked into ordinary list.");
                    if (scenario == "wizard")
                    {
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)forward).SendClicked()));
                        Require(navigation.Navigation.NavigationStack.Last() is CreationLifestylesPage, "Continue did not open Lifestyles.");
                        var lifestyles = (CreationLifestylesPage)navigation.Navigation.NavigationStack.Last();
                        using var lifestyleAlerts = new IssuedPageAlerts(lifestyles, window);
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(lifestyles, "OnAppearing")));
                        Require(MinimalVisible(lifestyles).Any(x => x.AutomationId == "creation-lifestyles-budget"),
                            "Lifestyles failed to prepare its admitted state off-thread: " + string.Join(";", lifestyleAlerts.Messages));
                        IssuedPageLifecycle(lifestyles, "OnDisappearing");
                        await navigation.PopAsync(false);
                    }
                    IssuedPageLifecycle(next, "OnDisappearing");
                    await navigation.PopAsync(false);
                    Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), root), "Back entered consumed Contact review.");
                    ((IButtonController)forward).SendClicked();
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), root), "Detached Continue navigated.");
                }
                ((IButtonController)save).SendClicked();
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(probe.ConfirmCalls == 1 && alerts.Titles.Count == 0, "Contact replayed or showed a raw error.");
                var cold = new FileWorkspaceStore(runtime.StateDirectory);
                RequireSameRewardDocument(saved, (linked ? cold.Get(ContactsOwnerA, runtime.Id) : cold.Get(runtime.Id)).Value!);
                Require(!linked || !cold.Get(runtime.Id).Success, "Linked contact leaked into local workspace.");
                Require(owners.ActiveLeases == 0, "Contact owner lease leaked.");
                IssuedPageLifecycle(review, "OnDisappearing");
                Console.WriteLine("PASS actual Core/MAUI Contacts forward: " + scenario);
            }
        });
    }
}
