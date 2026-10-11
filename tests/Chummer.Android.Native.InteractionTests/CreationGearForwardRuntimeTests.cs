using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunGearForwardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string scenario in new[] { "missing-skills", "local", "sum", "nonempty", "linked",
                    "contacts-fault", "refresh-fault", "owner-b", "owner-aba", "departure", "return", "unknown",
                    "missing-skills-no-editor" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(
                        scenario == "sum" ? "de-AT" : scenario == "contacts-fault" ? "es-MX" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    GearNextContactsProbe? contacts = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationFinalization: true, productionCreationOverview: true, creationContacts: true,
                        creationSkills: scenario == "missing-skills",
                        contactsDecorator: service => contacts = new(service));
                    var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeGear: true,
                        buildMethod: scenario == "sum" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority,
                        omitSkillsBeforeGear: scenario.StartsWith("missing-skills", StringComparison.Ordinal));
                    var store = new FileWorkspaceStore(runtime.StateDirectory);
                    bool linked = scenario is "linked" or "owner-b" or "owner-aba";
                    if (linked)
                    {
                        CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
                        Require(store.Delete(runtime.Id, before.ContentRevision).Success, "SETUP: legacy clone removal failed.");
                        owners.Set(ContactsOwnerA);
                    }
                    var coordinator = runtime.Coordinator;
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    await coordinator.InitializeAsync();
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var gear = new GearForwardProbe(new CharacterCreationGearInteractionPresenter(
                        runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                        runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>()));
                    var original = coordinator.State;
                    CharacterCreationGearSelection[] basket = scenario == "nonempty"
                        ? [new("gear:c49a893a-d445-4aac-bec0-c8501cba4c2c", 1)] : [];
                    var prepared = gear.Prepare(original, basket).PreparedPreview
                        ?? throw new InvalidOperationException("SETUP: Gear preview unavailable.");
                    var editor = new CreationGearPage(coordinator, gear, runtime.Presenter);
                    var navigation = new NavigationPage(editor);
                    var review = new CreationGearPreviewPage(coordinator, gear, runtime.Presenter,
                        prepared, AndroidSurfaceStrings.Resolve(), original);
                    await navigation.PushAsync(review, animated: false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(review, window);
                    await alerts.PreflightAsync();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing")));
                    var save = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-gear-confirm");
                    Require(save.IsEnabled, "SETUP: Gear confirm disabled.");
                    string openingText = CreationFlowStrings.Get("Gear.OpeningNext", "missing");
                    Exception? observationFailure = null;
                    contacts!.BeforeLoad = () =>
                    {
                        Require(SynchronizationContext.Current != ui, "Contacts preparation blocked the UI.");
                        if (scenario == "contacts-fault") throw new IOException("synthetic-contacts-read");
                        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            try
                            {
                                Require(!save.IsEnabled && save.Text == openingText,
                                    "Next-step read lacks disabled, localized feedback.");
                                ((IButtonController)save).SendClicked();
                                if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                                if (scenario is "departure" or "return") IssuedPageLifecycle(review, "OnDisappearing");
                                if (scenario == "return") IssuedPageLifecycle(review, "OnAppearing");
                                observed.SetResult();
                            }
                            catch (Exception error) { observationFailure = error; observed.SetException(error); }
                        }, null);
                        Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Next read blocked UI feedback.");
                        observed.Task.GetAwaiter().GetResult();
                    };
                    if (scenario == "unknown") gear.AfterConfirm = () => throw new IOException("synthetic-result-loss");
                    if (scenario == "refresh-fault") gear.AfterConfirm = () => gear.FailLoad = true;
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                    if (observationFailure is not null) throw observationFailure;
                    contacts.BeforeLoad = null;
                    gear.FailLoad = false;
                    var saved = (linked ? store.Get(ContactsOwnerA, runtime.Id) : store.Get(runtime.Id)).Value!;
                    Require(gear.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                        && saved.SavedRevision == saved.ContentRevision
                        && saved.Document.AuxiliaryState.CharacterCreationGearReceipts?.Count == 1
                        && saved.Document.Content == before.Document.Content,
                        "Gear did not persist exactly one auxiliary checkpoint.");
                    bool advanced = scenario is "local" or "sum" or "nonempty" or "linked";
                    Require(navigation.Navigation.NavigationStack.Last() is CreationContactsPage == advanced,
                        "Gear advanced incorrectly: " + scenario);
                    if (scenario == "missing-skills")
                    {
                        var denied = coordinator.LoadCreationContacts();
                        Require(denied.State is { } unavailable
                            && CreationContactsPhoneAuthority.IsBound(unavailable, coordinator.State)
                            && unavailable.Blockers.Contains(CharacterCreationContactsBlockers.BudgetAuthorityRequired)
                            && coordinator.State.CreationWizard!.CompletionBlockers.Contains(
                                CharacterCreationFinalizationBlockers.SkillsDraftRequired),
                            "SETUP: Core did not identify missing Skills as the Contacts prerequisite.");
                        Require(navigation.Navigation.NavigationStack.Last() is CreationSkillsPage
                            && !navigation.Navigation.NavigationStack.Contains(review),
                            "Saved Gear must lead to Core's missing Skills step, not an endless Contacts retry.");
                        var next = (CreationSkillsPage)navigation.Navigation.NavigationStack.Last();
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(MinimalVisible(next).OfType<Picker>().Any(x =>
                            x.AutomationId == "creation-skills-native-language-picker" && x.IsEnabled)
                            && MinimalVisible(next).OfType<Label>().Any(x => x.AutomationId == "creation-gear-skills-required"
                                && x.Text == CreationFlowStrings.Get("Gear.SkillsRequired", "missing")),
                            "Missing Skills route did not open the actual admitted editor.");
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await navigation.PopAsync(animated: false);
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor),
                            "Back from missing Skills returned to the consumed Gear review.");
                    }
                    if (scenario is "contacts-fault" or "refresh-fault")
                    {
                        var retry = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-gear-continue-next");
                        Require(retry.IsEnabled, "Known saved Gear lost its read-only Continue.");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                        advanced = true;
                    }
                    if (advanced)
                    {
                        Require(navigation.Navigation.NavigationStack.Last() is CreationContactsPage
                            && !navigation.Navigation.NavigationStack.Contains(review), "Saved Gear review was not replaced by Contacts.");
                        var next = (CreationContactsPage)navigation.Navigation.NavigationStack.Last();
                        int loads = contacts.LoadCalls;
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(contacts.LoadCalls == loads
                            && MinimalVisible(next).OfType<Button>().Any(x => x.AutomationId == "creation-contacts-add" && x.IsEnabled)
                            && MinimalVisible(next).Any(x => x.AutomationId == "creation-contacts-open-lifestyles"),
                            "First Contacts appearance discarded its prepared authority or skipped contact/lifestyle choices.");
                        Require(!MinimalVisible(next).Any(x => x.AutomationId == "creation-gear-receipt-digest"),
                            "Receipt identifiers leaked into normal Contacts.");
                        var details = MinimalVisible(next).OfType<Button>().Single(x => x.AutomationId == "creation-contacts-details-toggle");
                        ((IButtonController)details).SendClicked();
                        var receipt = gear.LookupReceipt(coordinator.State, prepared.IdempotencyKey).Receipt!;
                        Require(MinimalVisible(next).OfType<Label>().Single(x => x.AutomationId == "creation-gear-receipt-digest").Text
                            == receipt.ReceiptDigest && receipt.BasketCost == prepared.Preview.BudgetAfter.BasketCost
                            && receipt.RemainingNuyen == prepared.Preview.BudgetAfter.RemainingNuyen,
                            "Forwarded receipt or remaining budget differs from Core.");
                        ((IButtonController)details).SendClicked();
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await navigation.PopAsync(animated: false);
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor),
                            "Back returned to the consumed Gear review.");
                    }
                    if (scenario is "local" or "missing-skills")
                    {
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(editor, "OnAppearing")));
                        var resume = MinimalVisible(editor).OfType<Button>().Single(x => x.AutomationId == "creation-gear-saved-continue");
                        Require(!MinimalVisible(editor).Any(x => x.AutomationId == "creation-gear-preview"),
                            "An unchanged saved basket still offers an unnecessary review.");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)resume).SendClicked()));
                        Require(scenario == "local" ? navigation.Navigation.NavigationStack.Last() is CreationContactsPage
                            : navigation.Navigation.NavigationStack.Last() is CreationSkillsPage,
                            "Reopened saved Gear cannot continue without another purchase.");
                        await navigation.PopAsync(animated: false);
                        IssuedPageLifecycle(editor, "OnDisappearing");
                        ((IButtonController)resume).SendClicked();
                        await ui.DrainDispatchedAsyncVoidAsync();
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor),
                            "A departed saved-basket control remained usable.");
                    }
                    if (scenario == "unknown") Require(contacts.LoadCalls == 0, "Unknown commit advanced without a receipt.");
                    if (scenario == "missing-skills-no-editor")
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), review),
                            "Missing Skills service must not open an unbound editor.");
                    ((IButtonController)save).SendClicked();
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(gear.ConfirmCalls == 1 && contacts.ConfirmCalls == 0 && alerts.Titles.Count == 0,
                        "Continuation replayed a mutation, changed contacts, or showed a raw exception.");
                    RequireSameRewardDocument(saved, (linked ? store.Get(ContactsOwnerA, runtime.Id) : store.Get(runtime.Id)).Value!);
                    if (scenario is not ("owner-b" or "owner-aba"))
                    {
                        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                        var cold = gear.LookupReceipt(coordinator.State, prepared.IdempotencyKey).Receipt;
                        Require(cold is not null && CreationGearPhoneAuthority.ReceiptMatches(prepared, cold)
                            && CreationGearPhoneAuthority.RefreshedStateMatches(prepared, cold, gear.Load(coordinator.State).State!),
                            "Cold Core receipt/budget differs from the saved basket.");
                        var reopened = new FileWorkspaceStore(runtime.StateDirectory);
                        RequireSameRewardDocument(saved, (linked ? reopened.Get(ContactsOwnerA, runtime.Id) : reopened.Get(runtime.Id)).Value!);
                    }
                    Require(!linked || !store.Get(runtime.Id).Success, "Linked flow created a hidden legacy runner.");
                    IssuedPageLifecycle(review, "OnDisappearing");
                    Console.WriteLine("PASS Gear forward: " + scenario);
                }
            }
            finally { CultureInfo.CurrentUICulture = culture; }
        });
    }

    private sealed class GearForwardProbe(ICharacterCreationGearInteractionPresenter inner)
        : ICharacterCreationGearInteractionPresenter
    {
        public int ConfirmCalls;
        public bool FailLoad;
        public Action? AfterConfirm;
        public CharacterCreationGearInteractionLoadResult Load(CharacterOverviewState display)
        { if (FailLoad) throw new IOException("synthetic-gear-read"); return inner.Load(display); }
        public CharacterCreationGearInteractionPrepareResult Prepare(CharacterOverviewState display, IReadOnlyList<CharacterCreationGearSelection> basket)
            => inner.Prepare(display, basket);
        public CharacterCreationGearInteractionConfirmResult Confirm(CharacterOverviewState display, CharacterCreationGearConfirmation confirmation)
        { ConfirmCalls++; var result = inner.Confirm(display, confirmation); AfterConfirm?.Invoke(); return result; }
        public CharacterCreationGearInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState display, string idempotencyKey)
            => inner.LookupReceipt(display, idempotencyKey);
    }

    private sealed class GearNextContactsProbe(IOwnerBoundCharacterCreationContactsService inner)
        : IOwnerBoundCharacterCreationContactsService
    {
        public int LoadCalls, ConfirmCalls;
        public Action? BeforeLoad;
        public CharacterCreationContactResult<CharacterCreationContactsState> Load(
            OwnerContextStamp owner, CharacterCreationContactsLoadRequest request)
        { LoadCalls++; BeforeLoad?.Invoke(); return inner.Load(owner, request); }
        public CharacterCreationContactResult<CharacterCreationContactPreview> Preview(
            OwnerContextStamp owner, CharacterCreationContactPreviewRequest request) => inner.Preview(owner, request);
        public CharacterCreationContactResult<CharacterCreationContactReceipt> Confirm(
            OwnerContextStamp owner, CharacterCreationContactConfirmRequest request)
        { ConfirmCalls++; return inner.Confirm(owner, request); }
        public CharacterCreationContactResult<CharacterCreationContactReceipt> LookupReceipt(
            OwnerContextStamp owner, CharacterCreationContactReceiptLookupRequest request) => inner.LookupReceipt(owner, request);
    }
}
