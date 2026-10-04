using System.Reflection;
using Chummer.Android.Native;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunRunnerDeletionAsync(string contentRoot, string? exportState = null)
    {
        foreach (string scenario in new[] { "local", "linked", "dirty", "inactive", "cancel", "revision",
                     "owner-b", "owner-aba", "queued-aba", "queued-cancel" })
        {
            var owners = new ControlledLinkedOwner();
            OwnerScope originalOwner = scenario == "local" ? OwnerScope.LocalSingleUser : ContactsOwnerA;
            owners.Set(originalOwner);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
            await runtime.LoadRunnerAsync();
            var target = runtime.Id;
            if (scenario == "dirty") await runtime.Presenter.UpdateMetadataAsync(PersistenceMetadata(), default);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var original = ReadPersistencePartition(store, originalOwner, target).Value!;
            foreach (OwnerScope owner in new[] { OwnerScope.LocalSingleUser, ContactsOwnerA, ContactsOwnerB })
                if (owner != originalOwner)
                    Require((owner.IsLocalSingleUser ? store.CreateWorkspaceDocument(target, original.Document)
                        : store.CreateWorkspaceDocument(owner, target, original.Document)).Success,
                        "Could not construct isolated same-ID runner partitions.");
            var second = await runtime.Client.ImportAsync(new WorkspaceImportDocument(
                original.Document.Content.Replace("Native reward runner", "Keep this runner", StringComparison.Ordinal), "sr5"), default);
            if (scenario == "inactive") await runtime.Presenter.LoadAsync(second.Id, default);
            var workspace = runtime.Coordinator.State.Session.FindWorkspace(target)!;
            NativeRunnerDeletionRequest request = runtime.Coordinator.CaptureRunnerDeletionRequest(workspace)
                ?? throw new InvalidOperationException("No deletion confirmation for the displayed runner.");
            Require(runtime.Coordinator.CaptureRunnerDeletionRequest(workspace with { }) is null,
                "A reconstructed roster item was treated as the rendered runner.");
            if (scenario == "revision") await runtime.Presenter.UpdateMetadataAsync(PersistenceMetadata(), default);
            var before = PersistencePartitionSnapshots(store, target);
            using var canceled = new CancellationTokenSource();
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            bool queued = scenario.StartsWith("queued-", StringComparison.Ordinal);
            if (queued) await gate.WaitAsync();
            Task<bool>? pending = null;
            bool deleted = false;
            try
            {
                if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                pending = runtime.Coordinator.DeleteRunnerAsync(request, scenario != "cancel", canceled.Token);
                if (queued)
                {
                    Require(!pending.IsCompleted, "Deletion bypassed the workspace action queue.");
                    if (scenario == "queued-cancel") canceled.Cancel();
                    else { owners.Set(ContactsOwnerB); owners.Set(ContactsOwnerA); }
                }
            }
            finally { if (queued) gate.Release(); }
            try { deleted = await pending!.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (InvalidOperationException) { }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            var after = PersistencePartitionSnapshots(cold, target);
            bool shouldDelete = scenario is "local" or "linked" or "dirty" or "inactive";
            Require(deleted == shouldDelete, "Wrong deletion outcome: " + scenario);
            Require(before.All(pair => pair.Key == originalOwner && shouldDelete
                    ? pair.Value != after[pair.Key] : pair.Value == after[pair.Key]),
                "Deletion changed the wrong runner/account or a canceled/stale target: " + scenario);
            Require(ReadPersistencePartition(cold, originalOwner, target).Success == !shouldDelete
                && ReadPersistencePartition(cold, originalOwner, second.Id).Success,
                "Cold local storage does not match deletion or lost the unrelated runner: " + scenario);
            if (shouldDelete)
            {
                Require(runtime.Coordinator.State.OpenWorkspaces.All(row => row.Id != target)
                    && runtime.Coordinator.State.WorkspaceId != target,
                    "Deleted runner remains in the active/session projection.");
                if (scenario == "inactive") Require(runtime.Coordinator.State.WorkspaceId == second.Id,
                    "Deleting an inactive runner closed the active runner.");
                await runtime.Presenter.LoadAsync(target, default);
                Require(runtime.Presenter.State.WorkspaceId != target
                    && !ReadPersistencePartition(new FileWorkspaceStore(runtime.StateDirectory), originalOwner, target).Success,
                    "Deleted runner reopened from retained view bytes.");
            }
            Require(!await runtime.Coordinator.DeleteRunnerAsync(request, true),
                "A used or canceled confirmation was replayed.");
            Require(owners.ActiveLeases == 0, "Deletion leaked an account lease.");
            Console.WriteLine("PASS runner deletion: " + scenario);
        }

        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string decision in new[] { "cancel", "confirm", "departure" })
            {
                using var account = new ActualAccountFixture();
                await account.Account.InitializeAsync();
                await using var runtime = new NativeRewardRuntime(contentRoot,
                    linkedOwners: account.Owner, accountService: account.Account);
                await runtime.LoadRunnerAsync();
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                var target = runtime.Id;
                var page = new RunnersPage(runtime.Coordinator);
                var window = new Window(page);
                int confirmations = 0;
                using var alerts = new IssuedPageAlerts(page, window, args =>
                {
                    Require(args.Title == "Delete runner" && args.Accept == "Delete from device"
                        && args.Cancel == "Cancel" && args.Message!.Contains("Native reward runner", StringComparison.Ordinal)
                        && args.Message.Contains("Online copies", StringComparison.Ordinal),
                        "Deletion confirmation does not identify the runner and exact local-only scope.");
                    confirmations++;
                    if (decision == "departure") IssuedPageLifecycle(page, "OnDisappearing");
                    args.SetResult(decision != "cancel");
                });
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                var button = IssuedElements(page).OfType<Button>()
                    .Single(item => item.AutomationId == "home-delete-current-runner");
                Require(button.IsEnabled && button.Text == "Delete runner", "Home has no usable deletion action.");
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                Require(confirmations == 1 && new FileWorkspaceStore(runtime.StateDirectory).Get(target).Success
                    == (decision != "confirm"), "Actual Home deletion gesture ignored cancellation/confirmation/departure.");
                IssuedPageLifecycle(page, "OnDisappearing");
                Console.WriteLine("PASS actual MAUI Home deletion confirmation: " + decision);
            }
        });

        if (exportState is not null)
        {
            Require(!Directory.Exists(exportState), "Native smoke state destination already exists.");
            await using var runtime = new NativeRewardRuntime(contentRoot);
            for (int index = 1; index <= 7; index++)
                await runtime.LoadRunnerAsync(ContactsCreationXml
                    .Replace("<created>False</created>", "<created>True</created>", StringComparison.Ordinal)
                    .Replace("Contacts owner fixture", $"Delete smoke {index}", StringComparison.Ordinal));
            // Fixture state only, not real user data. A separate diagnostic app owns it.
            foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(exportState, Path.GetRelativePath(runtime.StateDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            Console.WriteLine("Exported seven synthetic saved runners for isolated API36 smoke.");
        }
    }
}
