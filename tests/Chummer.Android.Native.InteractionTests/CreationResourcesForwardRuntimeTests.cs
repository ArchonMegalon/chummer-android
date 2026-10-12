using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunResourcesForwardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var culture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string scenario in new[] { "local", "sum", "gear-fault", "refresh-fault",
                    "owner-b", "owner-aba", "departure", "return", "unknown" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(
                        scenario == "sum" ? "de-AT" : scenario == "gear-fault" ? "es-MX" : "en-GB");
                    var owners = new ControlledLinkedOwner();
                    ResourcesNextGearProbe? gear = null;
                    await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                        creationFinalization: true, productionCreationOverview: true,
                        gearDecorator: presenter => gear = new(presenter));
                    var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeResources: true,
                        buildMethod: scenario == "sum" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority);
                    var coordinator = runtime.Coordinator;
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    await coordinator.InitializeAsync();
                    await HydrateFinalizationOwnerAsync(runtime, owners, before);
                    var resources = new ResourcesForwardProbe(new CharacterCreationResourcesInteractionPresenter(
                        runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                        runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>()));
                    var original = coordinator.State;
                    var state = resources.Load(original).State!;
                    var option = state.Options.Single(x => x.IsEnabled && x.KarmaInvestment == 0);
                    var prepared = resources.Prepare(original, option.OptionId).PreparedPreview!;
                    var editor = new CreationResourcesPage(coordinator, resources, runtime.Presenter);
                    var navigation = new NavigationPage(editor);
                    var review = new CreationResourcesPreviewPage(coordinator, resources, runtime.Presenter,
                        prepared, AndroidSurfaceStrings.Resolve(), original);
                    await navigation.PushAsync(review, animated: false);
                    var window = new Window(navigation);
                    using var alerts = new IssuedPageAlerts(review, window);
                    await alerts.PreflightAsync();
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(review, "OnAppearing")));
                    var save = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-resources-confirm");
                    Require(save.IsEnabled, "SETUP: Resources confirm disabled.");
                    string openingText = CreationFlowStrings.Get("Resources.OpeningNext", "missing");
                    Exception? observationFailure = null;
                    gear!.BeforeLoad = () =>
                    {
                        Require(SynchronizationContext.Current != ui, "Gear preparation blocked the UI.");
                        if (scenario == "gear-fault") throw new IOException("synthetic-gear-read");
                        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            try
                            {
                                Require(!save.IsEnabled && save.Text == openingText,
                                    "Next-step read lacks visible disabled feedback.");
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
                    if (scenario == "unknown") resources.AfterConfirm = () => throw new IOException("synthetic-result-loss");
                    if (scenario == "refresh-fault") resources.AfterConfirm = () => resources.FailLoad = true;
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                    if (observationFailure is not null) throw observationFailure;
                    gear.BeforeLoad = null;
                    resources.FailLoad = false;
                    var store = new FileWorkspaceStore(runtime.StateDirectory);
                    var saved = store.Get(runtime.Id).Value!;
                    Require(resources.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                        && saved.SavedRevision == saved.ContentRevision
                        && saved.Document.AuxiliaryState.CharacterCreationResourcesReceipts?.Count == 1,
                        "Resources did not persist exactly once.");
                    bool advanced = scenario is "local" or "sum";
                    Require(navigation.Navigation.NavigationStack.Last() is CreationGearPage == advanced,
                        "Resources advanced incorrectly: " + scenario);
                    if (scenario is "gear-fault" or "refresh-fault")
                    {
                        var retry = MinimalVisible(review).OfType<Button>().Single(x => x.AutomationId == "creation-resources-continue-next");
                        Require(retry.IsEnabled, "Known saved resources lost its read-only Continue.");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)retry).SendClicked()));
                        advanced = true;
                    }
                    if (advanced)
                    {
                        Require(navigation.Navigation.NavigationStack.Last() is CreationGearPage
                            && !navigation.Navigation.NavigationStack.Contains(review), "Saved review was not replaced by Gear.");
                        var next = (CreationGearPage)navigation.Navigation.NavigationStack.Last();
                        int loads = gear.LoadCalls;
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(gear.LoadCalls == loads && MinimalVisible(next).OfType<Button>().Any(x => x.IsEnabled),
                            "First Gear appearance discarded its read or exposed no actions.");
                        Require(!MinimalVisible(next).Any(x => x.AutomationId == "creation-resources-receipt-digest"),
                            "Receipt identifiers leaked into the normal Gear surface.");
                        var details = MinimalVisible(next).OfType<Button>().Single(x => x.AutomationId == "creation-gear-details-toggle");
                        ((IButtonController)details).SendClicked();
                        var observedReceipt = MinimalVisible(next).OfType<Label>().Single(x => x.AutomationId == "creation-resources-receipt-digest");
                        Require(observedReceipt.Text == resources.LookupReceipt(coordinator.State, prepared.IdempotencyKey).Receipt!.ReceiptDigest,
                            "Forwarded diagnostic receipt did not match the real Core commit.");
                        ((IButtonController)details).SendClicked();
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(next, "OnAppearing")));
                        Require(gear.LoadCalls == loads + 1, "Later Gear appearance reused stale authority.");
                        IssuedPageLifecycle(next, "OnDisappearing");
                        await navigation.PopAsync(animated: false);
                        Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(), editor),
                            "Back returned to consumed Resources review.");
                    }
                    if (scenario == "unknown") Require(gear.LoadCalls == 0, "Unknown commit advanced without a receipt.");
                    ((IButtonController)save).SendClicked();
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(resources.ConfirmCalls == 1 && gear.ConfirmCalls == 0 && alerts.Titles.Count == 0,
                        "Continuation replayed a mutation or showed a raw exception.");
                    RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
                    if (scenario is not ("owner-b" or "owner-aba"))
                    {
                        await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                        var cold = resources.LookupReceipt(coordinator.State, prepared.IdempotencyKey).Receipt;
                        Require(cold is not null && CreationResourcesPhoneAuthority.ReceiptMatches(prepared, cold)
                            && CreationResourcesPhoneAuthority.RefreshedStateMatches(prepared, cold, resources.Load(coordinator.State).State!),
                            "Cold Core receipt/budget differs from the committed resource choice.");
                        RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                    }
                    IssuedPageLifecycle(review, "OnDisappearing");
                    Console.WriteLine("PASS Resources forward: " + scenario);
                }
                await RunSavedResourcesForwardAsync(contentRoot, ui);
            }
            finally { CultureInfo.CurrentUICulture = culture; }
        });
    }

    private static async Task RunSavedResourcesForwardAsync(string contentRoot, IssuedPageUiContext ui)
    {
        foreach (string scenario in new[] { "local", "sum", "read-fault", "stale", "owner-aba", "departure", "return" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(scenario == "sum" ? "de-AT" : "en-GB");
            var owners = new ControlledLinkedOwner();
            ResourcesNextGearProbe? gear = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true,
                gearDecorator: presenter => gear = new(presenter));
            var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeGear: true,
                buildMethod: scenario == "sum" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            if (scenario == "owner-aba")
            {
                CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
                Require(store.Delete(runtime.Id, before.ContentRevision).Success, "SETUP: legacy fixture removal failed.");
                owners.Set(ContactsOwnerA);
            }
            var coordinator = runtime.Coordinator;
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            await coordinator.InitializeAsync();
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var resources = new ResourcesForwardProbe(new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>()));
            var editor = new CreationResourcesPage(coordinator, resources, runtime.Presenter, gear);
            var navigation = new NavigationPage(editor);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(editor, window);
            await alerts.PreflightAsync();
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(editor, "OnAppearing")));
            var next = MinimalVisible(editor).OfType<Button>().Single(x => x.AutomationId == "creation-resources-open-gear");
            var views = MinimalVisible(editor).ToArray();
            Require(next.IsEnabled && !views.Any(x => x.AutomationId == "creation-resources-option-karma-0")
                && Array.IndexOf(views, next) < Array.FindIndex(views, x => x.AutomationId == "creation-resources-option-karma-1"),
                "Saved Resources must offer Continue before changes, without a redundant no-change preview.");
            if (scenario == "read-fault") resources.FailLoad = true;
            if (scenario == "stale") resources.StateOverride = state => state with { SnapshotDigest = "sha256:" + new string('1', 64) };
            // The UI test dispatcher's Post does not flow the caller's culture.
            // Capture the expected translated label in the page's context.
            string openingText = CreationFlowStrings.Get("Resources.OpeningNext", "missing");
            Exception? observationFailure = null;
            gear!.BeforeLoad = () =>
            {
                Require(SynchronizationContext.Current != ui, "Saved Resources continuation blocked the UI.");
                var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ui.Post(_ =>
                {
                    try
                    {
                        Require(!next.IsEnabled && next.Text == openingText,
                            "Saved Resources lacks immediate localized progress: " + scenario + " / " + next.Text);
                        ((IButtonController)next).SendClicked();
                        if (scenario == "owner-aba") { owners.Set(ContactsOwnerB); owners.Set(ContactsOwnerA); }
                        if (scenario is "departure" or "return") IssuedPageLifecycle(editor, "OnDisappearing");
                        if (scenario == "return") IssuedPageLifecycle(editor, "OnAppearing");
                        observed.SetResult();
                    }
                    catch (Exception error) { observationFailure = error; observed.SetException(error); }
                }, null);
                Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Continue blocked progress or cancellation.");
                observed.Task.GetAwaiter().GetResult();
            };
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)next).SendClicked()));
            if (observationFailure is not null) throw observationFailure;
            gear.BeforeLoad = null;
            resources.FailLoad = false;
            resources.StateOverride = null;
            bool advanced = scenario is "local" or "sum";
            Require(navigation.Navigation.NavigationStack.Last() is CreationGearPage == advanced,
                "Saved Resources continuation admitted the wrong state: " + scenario);
            if (scenario is "read-fault" or "stale")
            {
                Require(next.IsEnabled && MinimalVisible(editor).Any(x => x.AutomationId == "creation-resources-saved-next-unavailable")
                    && gear.LoadCalls == 0, "Unavailable saved authority must retain a safe retry without loading Gear.");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)next).SendClicked()));
                advanced = true;
            }
            if (advanced)
            {
                var page = navigation.Navigation.NavigationStack.Last() as CreationGearPage;
                Require(page is not null, "Saved Resources retry failed.");
                int reads = gear.LoadCalls;
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page!, "OnAppearing")));
                Require(gear.LoadCalls == reads && MinimalVisible(page!).Any(x => x.AutomationId == "creation-gear-preview"),
                    "Prepared Gear read was discarded or its actual controls are missing.");
                IssuedPageLifecycle(page!, "OnDisappearing");
                await navigation.PopAsync(animated: false);
            }
            IssuedPageLifecycle(editor, "OnDisappearing");
            ((IButtonController)next).SendClicked();
            await ui.DrainDispatchedAsyncVoidAsync();
            Require(navigation.Navigation.NavigationStack.Last() == editor && resources.PrepareCalls == 0
                && resources.ConfirmCalls == 0 && gear.ConfirmCalls == 0 && alerts.Titles.Count == 0,
                "Saved continuation replayed a mutation, exposed raw errors, or a departed control navigated.");
            RequireSameRewardDocument(before, (scenario == "owner-aba" ? store.Get(ContactsOwnerA, runtime.Id) : store.Get(runtime.Id)).Value!);
            Console.WriteLine("PASS saved Resources continue: " + scenario);
        }
    }

    private sealed class ResourcesForwardProbe(ICharacterCreationResourcesInteractionPresenter inner)
        : ICharacterCreationResourcesInteractionPresenter
    {
        public int PrepareCalls, ConfirmCalls;
        public bool FailLoad;
        public Action? AfterConfirm;
        public Func<CharacterCreationResourcesInteractionState, CharacterCreationResourcesInteractionState>? StateOverride;
        public CharacterCreationResourcesInteractionLoadResult Load(CharacterOverviewState display)
        {
            if (FailLoad) throw new IOException("synthetic-resources-read");
            var result = inner.Load(display);
            return StateOverride is not null && result.State is { } state ? result with { State = StateOverride(state) } : result;
        }
        public CharacterCreationResourcesInteractionPrepareResult Prepare(CharacterOverviewState display, string optionId)
        { PrepareCalls++; return inner.Prepare(display, optionId); }
        public CharacterCreationResourcesInteractionConfirmResult Confirm(CharacterOverviewState display, CharacterCreationResourcesConfirmation confirmation)
        { ConfirmCalls++; var result = inner.Confirm(display, confirmation); AfterConfirm?.Invoke(); return result; }
        public CharacterCreationResourcesInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState display, string idempotencyKey)
            => inner.LookupReceipt(display, idempotencyKey);
    }

    private sealed class ResourcesNextGearProbe(ICharacterCreationGearInteractionPresenter inner)
        : ICharacterCreationGearInteractionPresenter
    {
        public int LoadCalls, ConfirmCalls;
        public Action? BeforeLoad;
        public CharacterCreationGearInteractionLoadResult Load(CharacterOverviewState display)
        { LoadCalls++; BeforeLoad?.Invoke(); return inner.Load(display); }
        public CharacterCreationGearInteractionPrepareResult Prepare(CharacterOverviewState display, IReadOnlyList<CharacterCreationGearSelection> basket)
            => inner.Prepare(display, basket);
        public CharacterCreationGearInteractionConfirmResult Confirm(CharacterOverviewState display, CharacterCreationGearConfirmation confirmation)
        { ConfirmCalls++; return inner.Confirm(display, confirmation); }
        public CharacterCreationGearInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState display, string idempotencyKey)
            => inner.LookupReceipt(display, idempotencyKey);
    }
}
