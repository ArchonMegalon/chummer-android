using System.Globalization;
using System.Reflection;
using System.Text.Json;
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
    internal static async Task RunResourcesSaveFeedbackAsync(string contentRoot)
    {
        foreach (string scenario in new[] { "local", "sum-to-ten", "linked", "owner-aba", "lost-reply", "failed-refresh" })
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true);
            var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeResources: true,
                buildMethod: scenario == "sum-to-ten" ? CharacterCreationBuildMethods.SumToTen : CharacterCreationBuildMethods.Priority);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            bool linked = scenario is not ("local" or "sum-to-ten");
            if (linked)
            {
                CloneFinalizationRecordFixture(runtime, ContactsOwnerA, before);
                Require(store.Delete(runtime.Id, before.ContentRevision).Success, "SETUP: could not remove the temporary legacy fixture.");
                owners.Set(ContactsOwnerA);
            }
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            WorkspaceStoredDocument Read() => (linked ? store.Get(ContactsOwnerA, runtime.Id) : store.Get(runtime.Id)).Value!;
            var actual = new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
            var original = runtime.Coordinator.State;
            var option = actual.Load(original).State!.Options.Single(row => row.IsEnabled && row.KarmaInvestment == 0);
            var prepared = actual.Prepare(original, option.OptionId).PreparedPreview
                ?? throw new InvalidOperationException("SETUP: actual Resources preview unavailable.");
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var probe = new ResourcesSaveFeedbackProbe(actual, scenario, () =>
            {
                entered.TrySetResult();
                Require(release.Wait(TimeSpan.FromSeconds(10)), "Test did not release Resources confirmation.");
            });
            using var ui = new IssuedPageUiContext();
            await ui.RunAsync(async () =>
            {
                probe.UiThread = Environment.CurrentManagedThreadId;
                var page = new CreationResourcesPreviewPage(runtime.Coordinator, probe, runtime.Presenter,
                    prepared, AndroidSurfaceStrings.Resolve(), original);
                var type = typeof(CreationResourcesPreviewPage);
                void Render() => type.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                await (Task)type.GetMethod("PrepareForAppearanceRefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, [CancellationToken.None])!;
                Render();
                var button = body.Children.OfType<Button>().Single(row => row.AutomationId == "creation-resources-confirm");
                var authority = body.Children.OfType<Label>().Single(row => row.AutomationId == "creation-resources-confirm-authority");
                Require(button.IsEnabled, "Actual issued Resources preview cannot confirm.");
                var controls = body.Children.ToArray();
                Task pending = ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                ActivityIndicator? progress = null;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(!button.IsEnabled && body.Children.SequenceEqual(controls),
                        "Pending Resources save replaced its issued controls or left Confirm enabled.");
                    Require(button.Text == CreationFlowStrings.Get("Resources.Saving", "Saving budget…")
                        && authority.Text == CreationFlowStrings.Get("Resources.SavingDetail", "Saving your resource budget. Please wait…")
                        && authority.TextColor == NativeTheme.Muted,
                        "Pending Resources save is falsely presented as stale or failed.");
                    progress = body.Children.OfType<ActivityIndicator>().Single(row => row.AutomationId == "creation-resources-save-progress");
                    Require(progress.IsVisible && progress.IsRunning, "Pending Resources save has no visible activity indicator.");
                    var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ui.Post(_ => heartbeat.SetResult(), null);
                    await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Render();
                    Require(body.Children.SequenceEqual(controls), "Coordinator refresh rebuilt a valid pending Resources preview.");
                    ((IButtonController)button).SendClicked();
                    Require(probe.ConfirmCalls == 1, "Duplicate tap dispatched a second Resources save.");
                    if (scenario == "owner-aba")
                    {
                        owners.Set(ContactsOwnerB);
                        Render();
                        Require(!body.Children.OfType<Border>().Any() && !body.Children.OfType<Button>().Any(),
                            "Pending Resources display exposed the original account's preview after owner change.");
                        owners.Set(ContactsOwnerA);
                        Render();
                        Require(!body.Children.OfType<Border>().Any(), "Owner A→B→A revived the original Resources preview.");
                    }
                }
                finally
                {
                    release.Set();
                    await pending.WaitAsync(TimeSpan.FromSeconds(20));
                }
                Render();
                Require(progress is { IsRunning: false, IsVisible: false }, "Completed Resources save left its spinner active.");
                ((IButtonController)button).SendClicked();
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(probe.ConfirmCalls == 1, "Retained button replayed a terminal Resources save.");
                var after = Read();
                if (scenario == "owner-aba")
                {
                    Require(FinalizationDocumentDigest(after) == FinalizationDocumentDigest(before)
                        && !body.Children.OfType<Border>().Any(), "Stale owner saved or exposed a Resources result.");
                }
                else
                {
                    Require(after.ContentRevision == before.ContentRevision + 1 && after.SavedRevision == after.ContentRevision
                        && after.Document.Content == before.Document.Content && owners.ActiveLeases == 0
                        && after.Document.AuxiliaryState.CharacterCreationResourcesReceipts?.Count == 1
                        && JsonSerializer.Serialize(after.Document.AuxiliaryState with
                            { CharacterCreationResourcesDraft = null, CharacterCreationResourcesReceipts = null })
                            == JsonSerializer.Serialize(before.Document.AuxiliaryState),
                        "Resources did not persist exactly one checkpoint or changed unrelated state.");
                    bool hasReceipt = body.Children.OfType<Border>().Any(row => row.AutomationId == "creation-resources-confirm-receipt");
                    Require(hasReceipt == (scenario != "lost-reply"), "Known/uncertain Resources receipt display is incorrect.");
                    MinimalRequireNoMachineValues(page);
                    if (scenario == "failed-refresh")
                        Require(((VerticalStackLayout)body.Children.OfType<Border>().Single(row => row.AutomationId == "creation-resources-confirm-receipt").Content!)
                            .Children.OfType<Label>().Any(row => row.Text == CreationFlowStrings.Get("Purchases.SavedReopen", "Saved. Reopen the character to refresh this view.")),
                            "Known Resources commit with failed refresh lost its reopen guidance.");
                    if (scenario == "lost-reply")
                        Require(body.Children.OfType<Button>().All(row => !row.IsEnabled)
                            && body.Children.OfType<Border>().Any(row => row.Content is Label label
                                && label.AutomationId == "creation-resources-confirm-failed"),
                            "Uncertain Resources reply permitted retry or hid its warning.");
                    await HydrateFinalizationOwnerAsync(runtime, owners, after);
                    var cold = actual.LookupReceipt(runtime.Coordinator.State, prepared.IdempotencyKey);
                    Require(cold.Receipt is { } receipt && CreationResourcesPhoneAuthority.ReceiptMatches(prepared, receipt)
                        && CreationResourcesPhoneAuthority.RefreshedStateMatches(prepared, receipt, actual.Load(runtime.Coordinator.State).State!)
                        && FinalizationDocumentDigest(Read()) == FinalizationDocumentDigest(after),
                        "Cold reopen failed to recover exact saved Resources without another mutation.");
                }
                Require(!linked || !store.Get(runtime.Id).Success, "Scoped Resources save created a hidden legacy workspace.");
            });
            Console.WriteLine("PASS Resources save feedback " + scenario + ": real Core, UI heartbeat, stable controls, one submission and cold recovery");
        }
        foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            foreach (string key in new[] { "Resources.Saving", "Resources.SavingDetail" })
                Require(CreationFlowStrings.Get(key, "missing", CultureInfo.GetCultureInfo(locale)) != "missing",
                    "Missing Resources save-feedback resource: " + locale + "/" + key);
    }

    private sealed class ResourcesSaveFeedbackProbe(ICharacterCreationResourcesInteractionPresenter inner, string scenario,
        Action wait) : ICharacterCreationResourcesInteractionPresenter
    {
        public int UiThread { get; set; }
        public int ConfirmCalls { get; private set; }
        public CharacterCreationResourcesInteractionLoadResult Load(CharacterOverviewState overview)
        {
            if (ConfirmCalls > 0 && scenario == "failed-refresh") throw new IOException("Synthetic post-commit read failure.");
            return inner.Load(overview);
        }
        public CharacterCreationResourcesInteractionPrepareResult Prepare(CharacterOverviewState overview, string optionId)
            => inner.Prepare(overview, optionId);
        public CharacterCreationResourcesInteractionConfirmResult Confirm(CharacterOverviewState overview,
            CharacterCreationResourcesConfirmation confirmation)
        {
            Require(Environment.CurrentManagedThreadId != UiThread, "Resources persistence ran on the UI thread.");
            ConfirmCalls++;
            wait();
            var result = inner.Confirm(overview, confirmation);
            if (scenario == "lost-reply") throw new IOException("Synthetic lost reply after actual commit.");
            return result;
        }
        public CharacterCreationResourcesInteractionReceiptLookupResult LookupReceipt(CharacterOverviewState overview,
            string idempotencyKey) => inner.LookupReceipt(overview, idempotencyKey);
    }
}
