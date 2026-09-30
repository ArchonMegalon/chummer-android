using System.Globalization;
using System.Reflection;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunGearSaveFeedbackAsync(string contentRoot)
    {
        foreach (string scenario in new[] { "local", "linked", "owner-aba", "lost-reply", "failed-refresh" })
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true);
            var before = PrepareActualFinalizationReadyContext(runtime);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            bool linked = scenario != "local";
            if (linked)
            {
                CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
                Require(store.Delete(runtime.Id, before.ContentRevision).Success,
                    "SETUP: temporary legacy fixture could not be removed.");
                owners.Set(ContactsOwnerA);
            }
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            WorkspaceStoredDocument Read() => (linked ? store.Get(ContactsOwnerA, runtime.Id) : store.Get(runtime.Id)).Value!;
            var actual = new CharacterCreationGearInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
            var original = runtime.Coordinator.State;
            var prepared = actual.Prepare(original, [new("gear:c49a893a-d445-4aac-bec0-c8501cba4c2c", 1)]).PreparedPreview
                ?? throw new InvalidOperationException("SETUP: actual Flashlight preview unavailable.");
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var probe = new GearSaveFeedbackProbe(actual, scenario, () =>
            {
                entered.TrySetResult();
                Require(release.Wait(TimeSpan.FromSeconds(10)), "Test did not release Gear confirmation.");
            });
            using var ui = new IssuedPageUiContext();
            await ui.RunAsync(async () =>
            {
                probe.UiThread = Environment.CurrentManagedThreadId;
                var page = new CreationGearPreviewPage(runtime.Coordinator, probe, runtime.Presenter,
                    prepared, AndroidSurfaceStrings.Resolve(), original);
                var type = typeof(CreationGearPreviewPage);
                void Render() => type.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                await (Task)type.GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, [CancellationToken.None])!;
                Render();
                var button = body.Children.OfType<Button>().Single(row => row.AutomationId == "creation-gear-confirm");
                var authority = body.Children.OfType<Label>().Single(row => row.AutomationId == "creation-gear-confirm-authority");
                Require(button.IsEnabled, "Actual issued Gear preview cannot confirm.");
                var controls = body.Children.ToArray();
                Task pending = ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                ActivityIndicator? progress = null;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(!button.IsEnabled && body.Children.SequenceEqual(controls),
                        "Pending Gear save replaced its issued controls or left Confirm enabled.");
                    Require(button.Text == CreationFlowStrings.Get("Gear.Saving", "Saving gear…")
                        && authority.Text == CreationFlowStrings.Get("Gear.SavingDetail", "Saving your equipment choices. Please wait…")
                        && authority.TextColor == NativeTheme.Muted,
                        "Pending Gear save is falsely presented as stale or failed.");
                    progress = body.Children.OfType<ActivityIndicator>().Single(row => row.AutomationId == "creation-gear-save-progress");
                    Require(progress.IsVisible && progress.IsRunning, "Pending Gear save has no visible activity indicator.");
                    var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ui.Post(_ => heartbeat.SetResult(), null);
                    await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Render();
                    Require(body.Children.SequenceEqual(controls), "Coordinator refresh rebuilt a valid pending preview.");
                    ((IButtonController)button).SendClicked();
                    Require(probe.ConfirmCalls == 1, "Duplicate tap dispatched a second save.");
                    if (scenario == "owner-aba")
                    {
                        owners.Set(ContactsOwnerB);
                        Render();
                        Require(!body.Children.OfType<Border>().Any() && !body.Children.OfType<Button>().Any(),
                            "Pending display exposed the original account's preview after owner change.");
                        owners.Set(ContactsOwnerA);
                        Render();
                        Require(!body.Children.OfType<Border>().Any(), "Owner A→B→A revived the original preview.");
                    }
                }
                finally
                {
                    release.Set();
                    await pending.WaitAsync(TimeSpan.FromSeconds(20));
                }
                Render();
                Require(progress is { IsRunning: false, IsVisible: false }, "Completed save left its spinner active.");
                ((IButtonController)button).SendClicked();
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(probe.ConfirmCalls == 1, "Retained button replayed a terminal save.");
                var after = Read();
                if (scenario == "owner-aba")
                {
                    Require(FinalizationDocumentDigest(after) == FinalizationDocumentDigest(before)
                        && !body.Children.OfType<Border>().Any(), "Stale owner saved or exposed a result.");
                }
                else
                {
                    Require(after.ContentRevision == before.ContentRevision + 1 && after.SavedRevision == after.ContentRevision
                        && after.Document.Content == before.Document.Content && owners.ActiveLeases == 0,
                        "Gear did not persist exactly one auxiliary checkpoint.");
                    bool hasReceipt = body.Children.OfType<Border>().Any(row => row.AutomationId == "creation-gear-confirm-receipt");
                    Require(hasReceipt == (scenario != "lost-reply"), "Known/uncertain receipt display is incorrect.");
                    MinimalRequireNoMachineValues(page);
                    if (scenario == "failed-refresh")
                        Require(((VerticalStackLayout)body.Children.OfType<Border>().Single(row => row.AutomationId == "creation-gear-confirm-receipt").Content!)
                            .Children.OfType<Label>().Any(row => row.Text == CreationFlowStrings.Get("Purchases.SavedReopen", "Saved. Reopen the character to refresh this view.")),
                            "Known commit with failed refresh lost its reopen guidance.");
                    if (scenario == "lost-reply")
                        Require(body.Children.OfType<Button>().All(row => !row.IsEnabled)
                            && body.Children.OfType<Border>().Any(row => row.Content is Label label
                                && label.AutomationId == "creation-gear-confirm-failed"),
                            "Uncertain reply permitted retry or hid its warning.");
                    await HydrateFinalizationOwnerAsync(runtime, owners, after);
                    var cold = actual.LookupReceipt(runtime.Coordinator.State, prepared.IdempotencyKey);
                    Require(cold.Receipt is { } receipt && CreationGearPhoneAuthority.ReceiptMatches(prepared, receipt)
                        && CreationGearPhoneAuthority.RefreshedStateMatches(prepared, receipt, actual.Load(runtime.Coordinator.State).State!)
                        && FinalizationDocumentDigest(Read()) == FinalizationDocumentDigest(after),
                        "Cold reopen failed to recover exact saved Gear without another mutation.");
                }
                Require(!linked || !store.Get(runtime.Id).Success, "Scoped save created a hidden legacy workspace.");
            });
            Console.WriteLine("PASS Gear save feedback " + scenario + ": real Core, UI heartbeat, stable controls, one submission and cold recovery");
        }
        foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            foreach (string key in new[] { "Gear.Saving", "Gear.SavingDetail" })
                Require(CreationFlowStrings.Get(key, "missing", CultureInfo.GetCultureInfo(locale)) != "missing",
                    "Missing save-feedback resource: " + locale + "/" + key);
    }

    private sealed class GearSaveFeedbackProbe(ICharacterCreationGearInteractionPresenter inner, string scenario,
        Action wait) : ICharacterCreationGearInteractionPresenter
    {
        public int UiThread { get; set; }
        public int ConfirmCalls { get; private set; }
        public CharacterCreationGearInteractionLoadResult Load(CharacterOverviewState overview)
        {
            if (ConfirmCalls > 0 && scenario == "failed-refresh") throw new IOException("Synthetic post-commit read failure.");
            return inner.Load(overview);
        }
        public CharacterCreationGearInteractionPrepareResult Prepare(CharacterOverviewState overview,
            IReadOnlyList<CharacterCreationGearSelection> basket) => inner.Prepare(overview, basket);
        public CharacterCreationGearInteractionConfirmResult Confirm(CharacterOverviewState overview,
            CharacterCreationGearConfirmation confirmation)
        {
            Require(Environment.CurrentManagedThreadId != UiThread, "Gear persistence ran on the UI thread.");
            ConfirmCalls++;
            wait();
            var result = inner.Confirm(overview, confirmation);
            if (scenario == "lost-reply") throw new IOException("Synthetic lost reply after actual commit.");
            return result;
        }
        public CharacterCreationGearInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState overview,
            string idempotencyKey) => inner.LookupReceipt(overview, idempotencyKey);
    }
}
