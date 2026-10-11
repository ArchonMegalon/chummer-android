using System.Globalization;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunCreationFinalizationForwardAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in new[] { "priority", "sum", "linked", "shell-fault", "departure",
                         "return", "stack-change", "owner-b", "owner-aba", "lost-result" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(scenario == "sum" ? "de-AT" : "en-GB");
                var owners = new ControlledLinkedOwner();
                bool linked = scenario is "linked" or "owner-b" or "owner-aba";
                bool committed = false;
                int confirms = 0;
                CreationFinalizationPage? page = null;
                NavigationPage? navigation = null;
                Page unrelated = new ContentPage();
                Button? save = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    creationFinalization: true, finalizationDecorator: actual => new FinalizationPostCommitProbe(actual, () =>
                    {
                        confirms++;
                        committed = true;
                        Require(SynchronizationContext.Current != ui, "Finalization blocked the UI context.");
                        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(async _ =>
                        {
                            try
                            {
                                ((IButtonController)save!).SendClicked();
                                if (scenario is "departure" or "return")
                                {
                                    IssuedPageLifecycle(page!, "OnDisappearing");
                                    if (scenario == "return") IssuedPageLifecycle(page!, "OnAppearing");
                                }
                                if (scenario == "stack-change") await navigation!.PushAsync(unrelated, false);
                                observed.SetResult();
                            }
                            catch (Exception error) { observed.SetException(error); }
                        }, null);
                        Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Finalization held the UI thread.");
                        observed.Task.GetAwaiter().GetResult();
                        if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                        if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                        if (scenario == "lost-result") throw new IOException("Synthetic result loss after durable commit.");
                    }), beforeShellWorkspaceList: () =>
                    {
                        if (committed && scenario == "shell-fault") throw new IOException("Synthetic postcommit shell failure.");
                    });
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                var before = PrepareActualFinalizationReadyContext(runtime,
                    buildMethod: scenario == "sum" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority);
                if (linked)
                {
                    CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
                    CloneFinalizationRecordFixture(runtime, ContactsOwnerB, before);
                    owners.Set(ContactsOwnerA);
                }
                await HydrateFinalizationOwnerAsync(runtime, owners, before);
                var coordinator = runtime.Coordinator;
                var state = coordinator.LoadCreationFinalization().Value!;
                var reviewed = await coordinator.ReviewCreationFinalizationAsync(state.Binding, FinalizationFixtureCash(state));
                Require(reviewed.Value is { CanConfirm: true, Plan: not null }, "SETUP: actual Core review unavailable.");
                var root = new BuildPage(coordinator);
                var rootScroll = (IScrollViewController)(ScrollView)root.Content!;
                rootScroll.ScrollToRequested += (_, _) => rootScroll.SendScrollFinished();
                navigation = new NavigationPage(root);
                await navigation.PushAsync(new CreationStartingCashPage(coordinator, state), false);
                page = new CreationFinalizationPage(coordinator, reviewed.Value!);
                await navigation.PushAsync(page, false);
                var window = new Window(navigation);
                using var alerts = new IssuedPageAlerts(page, window);
                await alerts.PreflightAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                save = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == "creation-finalization-confirm");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)save).SendClicked()));
                committed = false;
                var cold = new FileWorkspaceStore(runtime.StateDirectory);
                var saved = (linked ? cold.Get(ContactsOwnerA, runtime.Id) : cold.Get(runtime.Id)).Value!;
                Require(confirms == 1 && saved.ContentRevision == before.ContentRevision + 1
                    && saved.SavedRevision == saved.ContentRevision
                    && saved.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Count == 1,
                    "Forward navigation lost the durable commit or repeated finalization.");
                bool forward = scenario is "priority" or "sum" or "linked" or "lost-result";
                if (forward)
                {
                    Require(navigation.Navigation.NavigationStack.Count == 1
                        && ReferenceEquals(navigation.Navigation.NavigationStack[0], root)
                        && coordinator.State.Profile?.Created == true
                        && coordinator.State.ContentRevision == saved.ContentRevision,
                        "Finalization did not lead straight to restored Career: " + scenario);
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
                    Require(MinimalVisible(root).Any(x => x.AutomationId == "build-sr5-career-wizard"),
                        "Returned root did not render the usable Career route.");
                    IssuedPageLifecycle(root, "OnDisappearing");
                }
                else if (scenario == "shell-fault")
                    Require(navigation.Navigation.NavigationStack.Last() is CreationFinalizationReceiptPage,
                        "Postcommit restoration failure lost the receipt fallback or claimed Career readiness.");
                else Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(),
                    scenario == "stack-change" ? unrelated : page), "Retired finalization stole navigation: " + scenario);
                ((IButtonController)save).SendClicked();
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(confirms == 1 && alerts.Titles.Count == 0, "Retained confirm replayed or displayed an error: " + scenario);
                if (scenario is "departure" or "return")
                {
                    if (scenario == "departure")
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                    var continued = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == "creation-finalization-continue-saved");
                    Require(continued.IsEnabled && !MinimalVisible(page).OfType<Button>().Any(x => x.AutomationId == "creation-finalization-confirm"),
                        "Returning to a committed finalization has no read-only continuation.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)continued).SendClicked()));
                    Require(navigation.Navigation.NavigationStack.Count == 1 && confirms == 1,
                        "Read-only continuation did not enter Career or re-submitted finalization.");
                }
                RequireSameRewardDocument(saved, (linked ? cold.Get(ContactsOwnerA, runtime.Id) : cold.Get(runtime.Id)).Value!);
                if (linked)
                {
                    RequireSameRewardDocument(before, cold.Get(runtime.Id).Value!);
                    RequireSameRewardDocument(before, cold.Get(ContactsOwnerB, runtime.Id).Value!);
                }
                Require(owners.ActiveLeases == 0, "Finalization leaked an owner lease.");
                IssuedPageLifecycle(page, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS actual Core/MAUI finalization forward: " + scenario);
            }
        });
    }
}
