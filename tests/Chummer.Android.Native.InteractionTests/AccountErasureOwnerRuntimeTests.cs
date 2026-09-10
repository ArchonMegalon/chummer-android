using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunAccountErasureOwnerCasesAsync(string contentRoot)
    {
        await RunStrictInventoryCasesAsync(contentRoot);
        foreach (string admission in new[] { "same", "not-confirmed", "revision", "b", "aba",
                     "queued-b", "queued-aba", "queued-cancel" })
            await RunStoredCleanupAdmissionAsync(contentRoot, admission);
        var failures = new List<string>();
        foreach (string scenario in new[] { "same", "keep", "queued-b", "queued-aba",
            "during-b", "during-aba", "post-cancel", "invalid-receipt", "delete-conflict",
            "dialog-b", "dialog-aba", "new-snapshot", "replay", "closed-runner", "inactive-runner", "cached-runner",
            "closed-conflict", "no-open-tabs", "created-during-erasure", "corrupt-runner",
            "unbound-owner", "other-linked-owner", "serialized-owner", "local-owner" })
        {
            try { await RunNativeAccountErasureOwnerAsync(contentRoot, scenario); }
            catch (Exception error) { failures.Add(scenario + ": " + error.Message); }
        }
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS 24 actual native account-erasure owner/receipt boundaries");
    }

    private static async Task RunStrictInventoryCasesAsync(string contentRoot)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializeReputationOwnerAsync(runtime);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        int cases = 0;
        void Check(bool value, string label)
        {
            Require(value, "Strict inventory: " + label);
            cases++;
            Console.WriteLine("PASS actual strict inventory: " + label);
        }
        Check(store.Inspect() is { Success: true, Value.Count: 1 }, "trusted Local partition");
        Check(store.Inspect(ContactsOwnerA) is { Success: true, Value.Count: 1 }, "linked A partition");
        Check(store.Inspect(ContactsOwnerB) is { Success: true, Value.Count: 1 }, "linked B partition");
        Check(!store.Inspect(default).Success && !store.Inspect(new OwnerScope("local-single-user")).Success,
            "invalid and forged Local scopes cannot inspect storage");
        OwnerScope emptyOwner = new("inventory-empty-owner");
        Check(store.Inspect(emptyOwner) is { Success: true, Value.Count: 0 }, "confirmed missing owner directory");

        CharacterWorkspaceId id = new("strict-inventory-target");
        Require(store.CreateWorkspaceDocument(ContactsOwnerA, id, store.Get(ContactsOwnerA, runtime.Id).Value!.Document).Success,
            "Strict inventory target creation failed.");
        string path = Directory.EnumerateFiles(runtime.StateDirectory, id.Value + ".json", SearchOption.AllDirectories).Single();
        string bytes = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, "{");
        Check(store.List(ContactsOwnerA).Count == 1 && store.Inspect(ContactsOwnerA) is
            { Success: false, Outcome: WorkspaceOperationOutcome.Corrupt, Value.Count: 1 },
            "unreadable member is not a complete roster");
        await File.WriteAllTextAsync(path, bytes);

        string invalid = Path.Combine(Path.GetDirectoryName(path)!, "invalid.name.json");
        await File.WriteAllTextAsync(invalid, "{}");
        Check(!store.Inspect(ContactsOwnerA).Success, "invalid filename is not silently omitted");
        File.Delete(invalid);
        string upper = Path.Combine(Path.GetDirectoryName(path)!, "unexpected-extension.JSON");
        await File.WriteAllTextAsync(upper, bytes);
        Check(!store.Inspect(ContactsOwnerA).Success, "unexpected JSON casing is not an empty member");
        File.Delete(upper);

        string retained = path + ".retained";
        File.Move(path, retained);
        Directory.CreateDirectory(path);
        Check(!store.Inspect(ContactsOwnerA).Success, "directory at runner-file path");
        Directory.Delete(path);
        File.CreateSymbolicLink(path, retained);
        Check(!store.Inspect(ContactsOwnerA).Success, "linked runner file");
        File.Delete(path);
        File.Move(retained, path);

        string missingPath = (string)typeof(FileWorkspaceStore).GetMethod("GetWorkspaceDirectory",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [emptyOwner])!;
        Require(missingPath.StartsWith(runtime.StateDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Scratch inventory path escaped the test directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(missingPath)!);
        await File.WriteAllTextAsync(missingPath, "not a directory");
        Check(!store.Inspect(emptyOwner).Success, "file blocking a missing workspace directory");
        File.Delete(missingPath);
        Check(store.Inspect(emptyOwner) is { Success: true, Value.Count: 0 }, "restored missing-directory state");

        var original = owners.Capture();
        var bound = (IOwnerBoundWorkspacePersistenceClient)runtime.Client;
        roaming.BlockInbound = true;
        try
        {
            var result = await bound.InspectLocalWorkspacesAsync(original, default).WaitAsync(TimeSpan.FromSeconds(5));
            Check(result is { Success: true, Value.Count: 2 } && !roaming.Entered.Task.IsCompleted,
                "runtime inventory is complete and strictly local, without roaming");
        }
        finally { roaming.Release.TrySetResult(); roaming.BlockInbound = false; }

        var missingCapability = new InProcessChummerClient(
            StrictPageProxy.Create<IWorkspaceService>(), StrictPageProxy.Create<IRulesetShellCatalogResolver>(),
            ownerContextAccessor: owners);
        Check(!(await missingCapability.InspectLocalWorkspacesAsync(original, default)).Success,
            "missing store inventory does not fall back to the display roster");
        foreach (bool aba in new[] { false, true })
        {
            owners.Set(ContactsOwnerB);
            if (aba) owners.Set(ContactsOwnerA);
            bool rejected = false;
            try { await bound.InspectLocalWorkspacesAsync(original, default); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, aba ? "old A epoch rejected" : "replacement B rejected");
        }
        Check(owners.ActiveLeases == 0 && store.Inspect(ContactsOwnerA) is { Success: true, Value.Count: 2 }
            && store.Inspect(ContactsOwnerB) is { Success: true, Value.Count: 1 }
            && store.Inspect() is { Success: true, Value.Count: 1 }, "read-only partitions and released leases");
        Console.WriteLine($"PASS {cases} actual Core/store/runtime strict-inventory boundaries");
    }

    private static async Task RunStoredCleanupAdmissionAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializeReputationOwnerAsync(runtime);
        var original = owners.Capture();
        await runtime.Presenter.CloseWorkspaceAsync(runtime.Id, default);
        Require(runtime.Presenter.State.WorkspaceId is null, "Cleanup admission fixture still has an active runner.");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var before = PersistencePartitionSnapshots(store, runtime.Id);
        using var cancellation = new CancellationTokenSource();
        bool queued = scenario.StartsWith("queued-", StringComparison.Ordinal);
        Task? predecessor = null;
        Task<WorkspaceStoredDeletionResult>? pending = null;
        WorkspaceStoredDeletionResult? result = null;
        bool rejected = false;
        try
        {
            if (queued)
            {
                roaming.BlockInbound = true;
                predecessor = ((IOwnerBoundShellStateClient)runtime.Client).ListWorkspacesAsync(original, default);
                await roaming.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            else if (scenario is "b" or "aba")
            {
                owners.Set(ContactsOwnerB);
                if (scenario == "aba") owners.Set(ContactsOwnerA);
            }
            pending = ((IOwnerBoundWorkspaceCleanupPresenter)runtime.Presenter).DeleteStoredWorkspaceAsync(
                original, runtime.Id, scenario == "revision" ? 2 : 1, scenario != "not-confirmed", cancellation.Token);
            if (queued)
            {
                Require(!pending.IsCompleted, "Stored cleanup bypassed the actual serialized runtime executor.");
                if (scenario == "queued-cancel") cancellation.Cancel();
                else owners.Set(ContactsOwnerB);
                if (scenario == "queued-aba") owners.Set(ContactsOwnerA);
                roaming.Release.TrySetResult();
            }
            try { result = await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (InvalidOperationException) { rejected = true; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { rejected = true; }
        }
        finally
        {
            roaming.Release.TrySetResult();
            if (predecessor is not null)
                try { await predecessor.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (InvalidOperationException) when (scenario != "queued-cancel") { }
            if (pending is not null)
                try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (InvalidOperationException) { }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        var after = PersistencePartitionSnapshots(store, runtime.Id);
        Require(scenario == "same"
                ? result is { Deletion.Success: true, LocalProjectionRetired: true }
                    && result.Deletion.Value is { ContentRevision: 1, SavedRevision: 1 }
                    && !store.Get(ContactsOwnerA, runtime.Id).Success
                : (rejected || result is { Deletion.Success: false, LocalProjectionRetired: false })
                    && before.All(pair => after[pair.Key] == pair.Value),
            "Stored cleanup violated original confirmation/revision/queue admission: " + scenario);
        Require(after[ContactsOwnerB] == before[ContactsOwnerB]
            && after[OwnerScope.LocalSingleUser] == before[OwnerScope.LocalSingleUser]
            && owners.ActiveLeases == 0 && roaming.BoundCalls == 0,
            "Stored cleanup crossed owner storage, roamed, or retained its lease.");
        Console.WriteLine("PASS actual closed-runner cleanup admission: " + scenario);
    }

    private static async Task RunNativeAccountErasureOwnerAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var account = DispatchProxy.Create<IAndroidAccountLinkService, ErasureAccountProbe>();
        var probe = (ErasureAccountProbe)account;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, accountService: account);
        await InitializeReputationOwnerAsync(runtime);
        if (scenario == "local-owner")
        {
            owners.Set(OwnerScope.LocalSingleUser);
            await runtime.Presenter.LoadAsync(runtime.Id, default);
        }
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var closedId = new Chummer.Contracts.Workspaces.CharacterWorkspaceId("erasure-closed-runner");
        bool hasClosed = scenario is "closed-runner" or "inactive-runner" or "cached-runner" or "closed-conflict" or "corrupt-runner";
        if (hasClosed)
        {
            Require(store.CreateWorkspaceDocument(ContactsOwnerA, closedId, store.Get(ContactsOwnerA, runtime.Id).Value!.Document).Success,
                "Closed runner control could not be created.");
            Require(store.CreateWorkspaceDocument(ContactsOwnerB, closedId, store.Get(ContactsOwnerB, runtime.Id).Value!.Document).Success
                && store.CreateWorkspaceDocument(closedId, store.Get(runtime.Id).Value!.Document).Success,
                "Foreign closed runner controls could not be created.");
        }
        if (scenario is "inactive-runner" or "cached-runner")
        {
            Require((await runtime.Client.SaveAsync(closedId, 1, default)).Success,
                "Closed runner save control failed.");
            await runtime.Presenter.LoadAsync(closedId, default);
            if (scenario == "cached-runner")
            {
                await runtime.Presenter.CloseWorkspaceAsync(closedId, default);
                Require(!runtime.Presenter.State.OpenWorkspaces.Any(item => item.Id == closedId),
                    "The cached-runner fixture did not really close the runner.");
            }
            await runtime.Presenter.LoadAsync(runtime.Id, default);
        }
        if (scenario == "no-open-tabs")
        {
            await runtime.Presenter.CloseWorkspaceAsync(runtime.Id, default);
            Require(runtime.Presenter.State.WorkspaceId is null && runtime.Presenter.State.OpenWorkspaces.Count == 0,
                "The no-open-tabs fixture retained an active/open runner.");
        }
        string? corruptPath = null;
        if (scenario == "corrupt-runner")
        {
            string directory = (string)typeof(FileWorkspaceStore).GetMethod("GetWorkspaceDirectory",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [ContactsOwnerA])!;
            Require(directory.StartsWith(runtime.StateDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "Corrupt fixture escaped its private scratch directory.");
            corruptPath = Path.Combine(directory, closedId.Value + ".json");
            await File.WriteAllTextAsync(corruptPath, "{");
        }
        var before = PersistencePartitionSnapshots(store, runtime.Id);
        using var cancellation = new CancellationTokenSource();
        async Task ChangeOwner()
        {
            owners.Set(ContactsOwnerB);
            if (scenario.EndsWith("aba", StringComparison.Ordinal)) owners.Set(ContactsOwnerA);
            probe.Snapshot = new(AndroidAccountLinkStatus.Linked, "Replacement account");
            await runtime.Presenter.LoadAsync(runtime.Id, default);
        }
        probe.OnErase = async () =>
        {
            if (scenario == "created-during-erasure")
                Require(store.CreateWorkspaceDocument(ContactsOwnerA, closedId, store.Get(ContactsOwnerA, runtime.Id).Value!.Document).Success,
                    "Concurrent local runner control could not be created.");
            if (scenario.StartsWith("during-", StringComparison.Ordinal)) await ChangeOwner();
            else probe.Snapshot = new(AndroidAccountLinkStatus.Unlinked, "Account deleted");
            if (scenario == "post-cancel") cancellation.Cancel();
            if (scenario == "delete-conflict")
                Require((await runtime.Client.UpdateMetadataAsync(runtime.Id, 1, PersistenceMetadata(), default)).Success,
                    "Revision conflict control failed.");
            if (scenario == "closed-conflict")
                Require((await runtime.Client.UpdateMetadataAsync(closedId, 1, PersistenceMetadata(), default)).Success,
                    "Closed revision conflict control failed.");
            var receipt = new AndroidAccountErasureReceipt(scenario != "invalid-receipt", new string('a', 64),
                null, [], DateTimeOffset.UtcNow, new string('b', 64));
            // Controlled service boundary only. Real grant/subject correlation
            // is separately exercised by AccountLinkHttp.Tests, not this probe.
            if (scenario != "unbound-owner")
                typeof(AndroidAccountErasureReceipt).GetProperty("LocalWorkspaceOwnerKey",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(receipt,
                    scenario == "other-linked-owner" ? ContactsOwnerB.Value
                        : scenario == "local-owner" ? OwnerScope.LocalSingleUser.Value : ContactsOwnerA.Value);
            return scenario == "serialized-owner"
                ? JsonSerializer.Deserialize<AndroidAccountErasureReceipt>(JsonSerializer.Serialize(receipt))!
                : receipt;
        };
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        bool queued = scenario.StartsWith("queued-", StringComparison.Ordinal);
        if (queued) await gate.WaitAsync();
        NativeAccountErasureRequest request = runtime.Coordinator.CaptureAccountErasureRequest();
        Require(JsonSerializer.Serialize(request) == "{}", "An erasure intent exposed its in-memory authority.");
        if (scenario.StartsWith("dialog-", StringComparison.Ordinal)) await ChangeOwner();
        if (scenario == "new-snapshot") probe.Snapshot = probe.Snapshot with { };
        Task<NativeAccountErasureResult> pending = runtime.Coordinator.EraseAccountAsync(request, scenario != "keep", cancellation.Token);
        if (queued)
        {
            try { await ChangeOwner(); }
            finally { gate.Release(); }
        }
        NativeAccountErasureResult? result = null;
        Exception? observed = null;
        try { result = await pending.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (Exception error) when (error is not OutOfMemoryException) { observed = error; }
        if (scenario is "same" or "post-cancel" or "replay" or "no-open-tabs")
            Require(observed is null && result is { LocalRunnersRemoved: true, Receipt.Erased: true }
                && !store.Get(ContactsOwnerA, runtime.Id).Success,
                "A confirmed original deletion lost its canonical result or local cleanup.");
        else if (scenario == "corrupt-runner")
            Require(result is { Receipt.Erased: true, LocalRunnersRemoved: false }
                && File.Exists(corruptPath) && await File.ReadAllTextAsync(corruptPath!) == "{",
                "An unreadable local runner was silently omitted from cleanup truth.");
        else if (scenario is "closed-runner" or "inactive-runner" or "cached-runner")
            Require(observed is null && result is { Receipt.Erased: true, LocalRunnersRemoved: true }
                && !store.Get(ContactsOwnerA, closedId).Success && !store.Get(ContactsOwnerA, runtime.Id).Success
                && runtime.Presenter.State.OpenWorkspaces.Count == 0 && runtime.Presenter.State.WorkspaceId is null,
                "Confirmed account cleanup did not remove the closed original-owner runner.");
        else if (scenario == "closed-conflict")
            Require(result is { Receipt.Erased: true, LocalRunnersRemoved: false }
                && store.Get(ContactsOwnerA, closedId).Value?.ContentRevision == 2,
                "A changed closed runner was deleted using a renewed revision.");
        else if (scenario == "created-during-erasure")
            Require(result is { Receipt.Erased: true, LocalRunnersRemoved: false } && store.Get(ContactsOwnerA, closedId).Success,
                "Open-tab deletion was mislabeled as complete device cleanup.");
        else
        {
            var after = PersistencePartitionSnapshots(store, runtime.Id);
            if (scenario == "delete-conflict")
                Require(after[ContactsOwnerA] != before[ContactsOwnerA] && result?.LocalRunnersRemoved == false,
                    "A failed deletion was reported as completed local cleanup.");
            else
                Require(before.All(pair => after[pair.Key] == pair.Value), "Account erasure deleted an unconfirmed owner partition.");
            if (queued || scenario.StartsWith("dialog-", StringComparison.Ordinal) || scenario == "new-snapshot")
                Require(probe.Calls == 0, "An old confirmation erased the replacement account.");
            if (scenario == "keep") Require(result?.LocalRunnersRemoved == false, "Kept local runners were reported as removed.");
            if (scenario is "unbound-owner" or "other-linked-owner" or "serialized-owner" or "local-owner")
                Require(result is { LocalRunnersRemoved: false, Receipt.Erased: true },
                    "Unmatched online/Core owner was reported as complete local cleanup.");
            if (scenario.StartsWith("during-", StringComparison.Ordinal))
                Require(result is { LocalRunnersRemoved: false, Receipt.Erased: true }
                    && runtime.Presenter.State.DisplayOwnerContext == owners.Capture(),
                    "Online receipt or replacement view was lost after an account transition.");
        }
        if (scenario == "replay")
        {
            bool rejected = false;
            try { await runtime.Coordinator.EraseAccountAsync(request, true); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && probe.Calls == 1, "A consumed erasure confirmation was replayed.");
        }
        Require(ReadPersistencePartition(store, ContactsOwnerB, runtime.Id).Success
            && ReadPersistencePartition(store, OwnerScope.LocalSingleUser, runtime.Id).Success,
            "Erasure changed a foreign/local partition.");
        Require(owners.ActiveLeases == 0, "Erasure leaked a live owner lease.");
        if (hasClosed)
            Require(store.Get(ContactsOwnerB, closedId).Success && store.Get(closedId).Success,
                "Closed-runner cleanup crossed owner partitions.");
        if (result is not null)
        {
            bool mayPresent = !scenario.StartsWith("during-", StringComparison.Ordinal);
            Require(runtime.Coordinator.IsAccountErasureResultCurrent(result) == mayPresent,
                "A completed erasure receipt could be presented to a replacement account.");
            string json = JsonSerializer.Serialize(result);
            Require(!json.Contains("OriginalOwner", StringComparison.Ordinal) && !json.Contains("PostAccount", StringComparison.Ordinal),
                "An erasure result serialized display authority.");
            Require(!runtime.Coordinator.IsAccountErasureResultCurrent(JsonSerializer.Deserialize<NativeAccountErasureResult>(json)!),
                "A deserialized receipt acquired in-memory display authority.");
        }
        Console.WriteLine("PASS actual native account erasure: " + scenario);
    }

    public class ErasureAccountProbe : DispatchProxy
    {
        public AndroidAccountLinkSnapshot Snapshot { get; set; } = new(AndroidAccountLinkStatus.Linked, "Original account");
        public int Calls { get; private set; }
        public Func<Task<AndroidAccountErasureReceipt>>? OnErase { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Snapshot") return Snapshot;
            if (targetMethod?.Name is "add_Changed" or "remove_Changed") return null;
            if (targetMethod?.Name == "EraseAccountAsync")
            {
                if (args is { Length: 3 } && args[1] is Func<bool> current && !current())
                    throw new InvalidOperationException("Original erasure context changed.");
                Calls++;
                return OnErase!();
            }
            throw new InvalidOperationException("Unexpected account boundary: " + targetMethod?.Name);
        }
    }
}
