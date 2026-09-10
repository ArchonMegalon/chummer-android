using System.Text.Json;
using System.Reflection;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Presentation;
using Chummer.Presentation.Shell;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunPersistenceOwnerCasesAsync(string contentRoot)
    {
        var failures = new List<string>();
        foreach (string operation in new[] { "save", "metadata", "delete" })
        foreach (string scenario in new[] { "local", "linked", "owner-b", "owner-aba" })
        {
            var owners = new ControlledLinkedOwner();
            OwnerScope originalOwner = scenario == "local" ? OwnerScope.LocalSingleUser : ContactsOwnerA;
            owners.Set(originalOwner);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var imported = await runtime.Client.ImportAsync(new WorkspaceImportDocument(ContactsCreationXml, "sr5"), default);
            runtime.Id = imported.Id;
            var original = ReadPersistencePartition(store, originalOwner, runtime.Id).Value!;
            foreach (OwnerScope owner in new[] { OwnerScope.LocalSingleUser, ContactsOwnerA, ContactsOwnerB })
                if (owner != originalOwner)
                    Require((owner.IsLocalSingleUser
                        ? store.CreateWorkspaceDocument(runtime.Id, original.Document)
                        : store.CreateWorkspaceDocument(owner, runtime.Id, original.Document)).Success,
                        "Actual same-ID persistence fixture could not create the separate owner partition.");
            if (operation == "delete")
                foreach (OwnerScope owner in new[] { OwnerScope.LocalSingleUser, ContactsOwnerA, ContactsOwnerB })
                    Require((owner.IsLocalSingleUser
                        ? store.SaveCheckpoint(runtime.Id, original.ContentRevision)
                        : store.SaveCheckpoint(owner, runtime.Id, original.ContentRevision)).Success,
                        "Delete fixture must be clean and saved before exercising the actual deletion path.");
            await runtime.Presenter.LoadAsync(runtime.Id, default);
            Require(runtime.Presenter.State.DisplayOwnerContext == owners.Capture()
                && runtime.Presenter.State.WorkspaceId == runtime.Id && runtime.Presenter.State.Error is null,
                "Persistence fixture did not load an actual owner-bound display.");
            var before = PersistencePartitionSnapshots(store, runtime.Id);
            if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
            if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
            Exception? error = null;
            try
            {
                if (operation == "save") await runtime.Presenter.SaveAsync(default);
                else if (operation == "metadata")
                    await runtime.Presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Persistence owner probe", "PROBE", "Exact original account"), default);
                else await runtime.Presenter.DeleteWorkspaceAsync(runtime.Id, confirmed: true, default);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { error = ex; }
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            var after = PersistencePartitionSnapshots(cold, runtime.Id);
            bool shouldApply = scenario is "local" or "linked";
            bool changedOnlyOriginal = before.All(pair => pair.Key == originalOwner
                ? after[pair.Key] != pair.Value : after[pair.Key] == pair.Value);
            bool unchanged = before.All(pair => after[pair.Key] == pair.Value);
            var saved = ReadPersistencePartition(cold, originalOwner, runtime.Id);
            bool exactEffect = operation switch
            {
                "save" => saved.Success && saved.Value!.ContentRevision == original.ContentRevision
                    && saved.Value.SavedRevision == original.ContentRevision,
                "metadata" => saved.Success && saved.Value!.ContentRevision == original.ContentRevision + 1
                    && saved.Value.Document.Content.Contains("Persistence owner probe", StringComparison.Ordinal),
                _ => !saved.Success && saved.Value is null
            };
            bool passed = shouldApply ? changedOnlyOriginal && exactEffect && error is null
                && runtime.Presenter.State.Error is null : unchanged;
            Console.WriteLine("PERSISTENCE_OWNER_OBSERVATION " + JsonSerializer.Serialize(new
            {
                operation, scenario, passed, changedOnlyOriginal, unchanged, exactEffect,
                exception = error?.GetType().Name, runtime.Presenter.State.Error
            }));
            Require(owners.ActiveLeases == 0, "Persistence left a canonical owner lease held.");
            if (!passed) failures.Add(operation + ":" + scenario);
        }
        Require(failures.Count == 0, "Actual original-display persistence failures: " + string.Join(", ", failures));
        Console.WriteLine("PASS 12 actual original-display persistence cases");
        foreach (string operation in new[] { "save", "metadata", "delete" })
        foreach (string scenario in new[] { "owner-b", "owner-aba", "cancel" })
            await RunQueuedPersistenceAsync(contentRoot, operation, scenario);
        foreach (string operation in new[] { "save", "metadata" })
        foreach (string scenario in new[] { "roaming-fault", "cancel", "owner-b", "owner-aba" })
            await RunCommittedPersistenceAsync(contentRoot, operation, scenario, throughPresenter: false);
        foreach (string operation in new[] { "save", "metadata" })
        foreach (string scenario in new[] { "cancel", "owner-b", "owner-aba" })
            await RunCommittedPersistenceAsync(contentRoot, operation, scenario, throughPresenter: true);
        foreach (string phase in new[] { "prepare", "lease", "complete", "ack" })
        foreach (string scenario in new[] { "same", "b", "aba" })
            await RunRecoveryExportOwnerBoundaryAsync(contentRoot, phase, scenario);
        foreach (string action in new[] { "notes-dialog", "queued-close" })
        foreach (string scenario in new[] { "same", "owner-b", "owner-aba" })
        {
            try { await RunNativePersistenceGestureAsync(contentRoot, action, scenario); }
            catch (Exception ex) { failures.Add(action + "/" + scenario + ": " + ex.Message); }
        }
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        foreach (string action in new[] { "save", "metadata" })
        foreach (string scenario in new[] { "same", "pre-b", "pre-aba", "pre-revision", "post-cancel", "post-b", "post-aba", "post-fault" })
            await RunPresenterPersistenceReceiptAsync(contentRoot, action, scenario);
        foreach (string scenario in new[] { "same", "post-b", "post-aba", "cancel-metadata", "cancel-save" })
            await RunNativeNotesCommitAsync(contentRoot, scenario);
        foreach (string scenario in new[] { "same", "post-b", "post-aba", "post-cancel" })
            await RunNativeSaveCommitAsync(contentRoot, scenario);
        Console.WriteLine("PASS 78 actual owner persistence/display/queue/commit/recovery/gesture cases");
        await RunReputationGestureOwnerCasesAsync(contentRoot);
    }

    private static async Task RunPresenterPersistenceReceiptAsync(string contentRoot, string action, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializePersistenceBoundaryAsync(runtime, owners, clean: false);
        var original = owners.Capture();
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        using var canceled = new CancellationTokenSource();
        roaming.ExpectedOwner = original;
        roaming.AfterCommit = () =>
        {
            if (scenario == "post-cancel") canceled.Cancel();
            if (scenario is "post-b" or "post-aba") owners.Set(ContactsOwnerB);
            if (scenario == "post-aba") owners.Set(ContactsOwnerA);
            if (scenario == "post-fault") throw new IOException("Synthetic postcommit failure");
        };
        if (scenario is "pre-b" or "pre-aba")
        {
            owners.Set(ContactsOwnerB);
            if (scenario == "pre-aba") owners.Set(ContactsOwnerA);
            await runtime.Presenter.LoadAsync(runtime.Id, default);
        }
        var currentView = runtime.Presenter.State;
        long revision = scenario == "pre-revision" ? 2 : 1;
        var bound = (IOwnerBoundWorkspacePersistencePresenter)runtime.Presenter;
        object actual = action == "save"
            ? await bound.SaveAsync(original, runtime.Id, revision, canceled.Token)
            : await bound.UpdateMetadataAsync(original, runtime.Id, revision, PersistenceMetadata(), canceled.Token);
        bool shouldCommit = !scenario.StartsWith("pre-", StringComparison.Ordinal);
        bool valid = actual switch
        {
            CommandResult<WorkspaceSaveReceipt> saved => shouldCommit
                ? saved.Success && saved.Value is { ContentRevision: 1, SavedRevision: 1 }
                    && saved.Value.Id == runtime.Id && !string.IsNullOrWhiteSpace(saved.Value.ReceiptId)
                : !saved.Success && saved.Value is null && saved.Outcome == WorkspaceOperationOutcome.Conflict,
            CommandResult<WorkspaceMetadataResult> updated => shouldCommit
                ? updated.Success && updated.Value is { ContentRevision: 2, SavedRevision: 0 }
                    && updated.Value.Profile.Name == "Persistence owner probe"
                : !updated.Success && updated.Value is null && updated.Outcome == WorkspaceOperationOutcome.Conflict,
            _ => false
        };
        var after = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        Require(valid && (shouldCommit ? after[ContactsOwnerA] != before[ContactsOwnerA]
                : after[ContactsOwnerA] == before[ContactsOwnerA] && ReferenceEquals(runtime.Presenter.State, currentView))
            && after[ContactsOwnerB] == before[ContactsOwnerB]
            && after[OwnerScope.LocalSingleUser] == before[OwnerScope.LocalSingleUser]
            && roaming.BoundCalls == (shouldCommit ? 1 : 0) && roaming.UnboundCalls == 0
            && owners.ActiveLeases == 0, "Original-gesture presenter lost a canonical result or modified a superseding view.");
        Console.WriteLine("PASS actual presenter persistence receipt: " + action + "/" + scenario);
    }

    private static async Task RunNativeNotesCommitAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializePersistenceBoundaryAsync(runtime, owners, clean: false);
        var original = owners.Capture();
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        using var canceled = new CancellationTokenSource();
        roaming.ExpectedOwner = original;
        roaming.AfterCommit = () =>
        {
            if (scenario == "cancel-metadata" || scenario == "cancel-save" && roaming.BoundCalls == 2) canceled.Cancel();
            if (scenario is "post-b" or "post-aba") owners.Set(ContactsOwnerB);
            if (scenario == "post-aba") owners.Set(ContactsOwnerA);
        };
        bool saved = false;
        Exception? failure = null;
        try { saved = await runtime.Coordinator.ApplyCharacterNotesEditAsync(new CharacterNotesEditRequest(
            runtime.Id, 1, "Original gesture notes", "Original game notes", "Original group notes")
            { OriginalOwner = original }, canceled.Token); }
        catch (Exception error) { failure = error; }
        bool checkpointed = scenario is "same" or "cancel-save";
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var actual = store.Get(ContactsOwnerA, runtime.Id).Value!;
        var after = PersistencePartitionSnapshots(store, runtime.Id);
        Require(saved == checkpointed && (failure is null || !checkpointed && failure is OperationCanceledException)
            && actual.ContentRevision == 2 && actual.SavedRevision == (checkpointed ? 2 : 0)
            && after[ContactsOwnerB] == before[ContactsOwnerB]
            && after[OwnerScope.LocalSingleUser] == before[OwnerScope.LocalSingleUser]
            && roaming.BoundCalls == (checkpointed ? 2 : 1) && roaming.UnboundCalls == 0 && owners.ActiveLeases == 0,
            "Native Notes misreported its commit or retargeted/replayed the metadata-to-save continuation: " + scenario);
        Console.WriteLine("PASS actual native Notes commit: " + scenario);
    }

    private static async Task RunNativeSaveCommitAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializePersistenceBoundaryAsync(runtime, owners, clean: false);
        using var canceled = new CancellationTokenSource();
        roaming.ExpectedOwner = owners.Capture();
        roaming.AfterCommit = () =>
        {
            if (scenario == "post-cancel") canceled.Cancel();
            if (scenario is "post-b" or "post-aba") owners.Set(ContactsOwnerB);
            if (scenario == "post-aba") owners.Set(ContactsOwnerA);
        };
        await runtime.Coordinator.SaveAsync(canceled.Token);
        Require(runtime.Coordinator.HasDurableSaveNotice == (scenario is "same" or "post-cancel")
            && new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, runtime.Id).Value!.SavedRevision == 1
            && roaming.BoundCalls == 1 && owners.ActiveLeases == 0,
            "Native Save reported an unrelated current screen as its durable result.");
        Console.WriteLine("PASS actual native Save commit: " + scenario);
    }

    private static async Task RunNativePersistenceGestureAsync(string contentRoot, string action, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
        await InitializePersistenceBoundaryAsync(runtime, owners, clean: action == "queued-close");
        var original = runtime.Presenter.State;
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        var page = action == "notes-dialog" ? new CharacterNotesPage(runtime.Coordinator) : null;
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime.Coordinator)!;
        Task? pending = null;
        Exception? observed = null;
        if (action == "queued-close")
        {
            await gate.WaitAsync();
            pending = runtime.Coordinator.CloseWorkspaceAsync(original.OpenWorkspaces.Single(item => item.Id == runtime.Id));
            Require(!pending.IsCompleted, "Native Close did not queue behind actual workspace activation.");
        }
        try
        {
            if (scenario != "same")
            {
                owners.Set(ContactsOwnerB);
                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                await runtime.Presenter.LoadAsync(runtime.Id, default);
                Require(runtime.Presenter.State.DisplayOwnerContext == owners.Capture()
                    && runtime.Presenter.State.ContentRevision == original.ContentRevision
                    && runtime.Presenter.State.Error is null,
                    "Actual gesture fixture did not activate the same-ID/revision runner under the new epoch.");
            }
            if (page is not null)
                pending = (Task)typeof(CharacterNotesPage).GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, null)!;
        }
        finally
        {
            if (action == "queued-close") gate.Release();
        }
        try { await pending!.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { observed = ex; }
        var after = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        bool current = scenario == "same";
        bool unchanged = before.All(pair => after[pair.Key] == pair.Value);
        if (action == "queued-close")
            Require(observed is null && unchanged
                && (current ? runtime.Presenter.State.WorkspaceId is null
                    : runtime.Presenter.State.WorkspaceId == runtime.Id
                        && runtime.Presenter.State.DisplayOwnerContext == owners.Capture()
                        && runtime.Presenter.State.OpenWorkspaces.Any(item => item.Id == runtime.Id)),
                "A queued native Close lost its original owner and closed/replaced the current account view.");
        else if (!current)
            Require(unchanged && runtime.Presenter.State.DisplayOwnerContext == owners.Capture(),
                "An old Notes dialog wrote the current account's identical-ID runner.");
        else
        {
            var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, runtime.Id).Value!;
            Require(saved.ContentRevision == 2 && saved.SavedRevision == 2
                && after[ContactsOwnerB] == before[ContactsOwnerB]
                && after[OwnerScope.LocalSingleUser] == before[OwnerScope.LocalSingleUser],
                "Current original-owner Notes no longer saves exactly once.");
        }
        Require(owners.ActiveLeases == 0, "Native gesture leaked its owner lease.");
        Console.WriteLine("PASS actual native persistence gesture: " + action + "/" + scenario);
    }

    private static async Task RunRecoveryExportOwnerBoundaryAsync(string contentRoot, string phase, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
        await InitializePersistenceBoundaryAsync(runtime, owners, clean: false);
        await runtime.Presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Original recovery runner", "RECOVERY", "Only the original account"), default);
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        long revision = runtime.Presenter.State.ContentRevision;
        var availability = runtime.Presenter.GetRecoveryCopyAvailability(runtime.Id, revision);
        Require(availability.Available, "Actual committed metadata did not retain owner-bound recovery.");
        void Switch()
        {
            if (scenario != "same") owners.Set(ContactsOwnerB);
            if (scenario == "aba") owners.Set(ContactsOwnerA);
        }
        bool expected = scenario == "same";
        if (phase == "prepare") Switch();
        var prepared = await runtime.Presenter.PrepareRecoveryCopyAsync(runtime.Id, revision, availability.LocalGeneration, default);
        if (phase == "prepare")
            Require(prepared.Success == expected, "Recovery prepare used stale account authority.");
        else
        {
            Require(prepared.Success, "Original owner could not prepare recovery.");
            var request = runtime.Presenter.State.PendingRecoveryExport
                ?? throw new InvalidOperationException("No pending actual recovery request.");
            if (phase == "lease") Switch();
            bool acquired = runtime.Presenter.TryAcquireRecoveryCopyExportLease(request, out var lease);
            if (phase == "lease")
                Require(acquired == expected, "A stale original-owner export token exposed recovery bytes.");
            else
            {
                Require(acquired && lease is not null, "Original-owner recovery bytes were unavailable.");
                using (var reader = new StreamReader(lease!.Stream, leaveOpen: true))
                    Require((await reader.ReadToEndAsync()).Contains("Original recovery runner", StringComparison.Ordinal),
                        "The recovery lease was not the exact original runner.");
                if (phase == "complete") Switch();
                var outcome = new Chummer.Presentation.Overview.WorkspaceRecoveryBrowserExportOutcome(
                    phase == "ack" ? Chummer.Presentation.Overview.WorkspaceRecoveryBrowserExportOutcome.DispatchedRequiresExplicitUserAck
                        : Chummer.Presentation.Overview.WorkspaceRecoveryBrowserExportOutcome.DurableSaved);
                bool completed = runtime.Presenter.CompleteRecoveryCopyExport(request, outcome);
                if (phase == "complete")
                    Require(completed == expected, "A stale account acknowledged a recovery export.");
                else
                {
                    Require(completed, "Original-owner recovery dispatch was rejected.");
                    Switch();
                    Require(runtime.Presenter.AcknowledgeRecoveryCopySaved(runtime.Id, revision, availability.LocalGeneration) == expected,
                        "A stale account acknowledged another generation's recovery file.");
                }
            }
            lease?.Dispose();
        }
        var after = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        Require(before.All(pair => after[pair.Key] == pair.Value) && owners.ActiveLeases == 0,
            "Recovery-only export changed canonical storage or leaked its owner lease.");
        Console.WriteLine("PASS actual recovery export owner: " + phase + "/" + scenario);
    }

    private static async Task InitializePersistenceBoundaryAsync(NativeRewardRuntime runtime, ControlledLinkedOwner owners, bool clean)
    {
        runtime.Id = (await runtime.Client.ImportAsync(new WorkspaceImportDocument(ContactsCreationXml, "sr5"), default)).Id;
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var source = store.Get(ContactsOwnerA, runtime.Id).Value!;
        Require(store.CreateWorkspaceDocument(runtime.Id, source.Document).Success
            && store.CreateWorkspaceDocument(ContactsOwnerB, runtime.Id, source.Document).Success,
            "Actual persistence boundary fixture could not create identical-ID partitions.");
        if (clean)
            Require(store.SaveCheckpoint(runtime.Id, 1).Success
                && store.SaveCheckpoint(ContactsOwnerA, runtime.Id, 1).Success
                && store.SaveCheckpoint(ContactsOwnerB, runtime.Id, 1).Success, "Delete fixture was not saved.");
        await runtime.Presenter.LoadAsync(runtime.Id, default);
        Require(runtime.Presenter.State.Error is null && runtime.Presenter.State.DisplayOwnerContext == owners.Capture(),
            "Boundary fixture lost actual display authority.");
    }

    private static async Task RunQueuedPersistenceAsync(string contentRoot, string operation, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializePersistenceBoundaryAsync(runtime, owners, clean: operation == "delete");
        var original = owners.Capture();
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        using var canceled = new CancellationTokenSource();
        roaming.BlockInbound = true;
        Task predecessor = ((IOwnerBoundShellStateClient)runtime.Client).ListWorkspacesAsync(original, default);
        Task<object>? queued = null;
        bool rejected = false;
        try
        {
            await roaming.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            queued = InvokeBoundPersistenceAsync(runtime, operation, original, canceled.Token);
            Require(!queued.IsCompleted, "Persistence did not wait in the actual client executor.");
            if (scenario == "cancel") canceled.Cancel();
            else owners.Set(ContactsOwnerB);
            if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
            roaming.Release.TrySetResult();
            try { await queued.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (InvalidOperationException) { rejected = true; }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { rejected = true; }
        }
        finally
        {
            roaming.Release.TrySetResult();
            try { await predecessor.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (InvalidOperationException) when (scenario != "cancel") { }
            if (queued is not null)
                try { await queued; }
                catch (InvalidOperationException) { }
                catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        }
        var after = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        Require(rejected && before.All(pair => after[pair.Key] == pair.Value)
            && roaming.BoundCalls == 0 && owners.ActiveLeases == 0,
            "A queued original-owner operation wrote data, roamed, or leaked its lease after losing admission.");
        Console.WriteLine("PASS actual queued persistence: " + operation + "/" + scenario);
    }

    private static async Task RunCommittedPersistenceAsync(string contentRoot, string operation, string scenario, bool throughPresenter)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializePersistenceBoundaryAsync(runtime, owners, clean: false);
        var original = owners.Capture();
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        using var canceled = new CancellationTokenSource();
        roaming.ExpectedOwner = original;
        roaming.AfterCommit = () =>
        {
            if (scenario == "cancel") canceled.Cancel();
            if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
            if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
            if (scenario == "roaming-fault") throw new IOException("Synthetic postcommit roaming failure");
        };
        if (throughPresenter)
        {
            if (operation == "save") await runtime.Presenter.SaveAsync(canceled.Token);
            else await runtime.Presenter.UpdateMetadataAsync(PersistenceMetadata(), canceled.Token);
            if (scenario is "owner-b" or "owner-aba")
                Require(runtime.Presenter.State.WorkspaceId is null && runtime.Presenter.State.Notice?.Contains("committed", StringComparison.Ordinal) == true,
                    "Postcommit account change hid known success or retained the old private display.");
            else Require(runtime.Presenter.State.Error is null && runtime.Presenter.State.WorkspaceId == runtime.Id,
                "Postcommit cancellation falsely reported the committed persistence operation as failed.");
        }
        else
        {
            object raw = await InvokeBoundPersistenceAsync(runtime, operation, original, canceled.Token);
            bool receipt = raw switch
            {
                CommandResult<WorkspaceSaveReceipt> saved => saved.Success && saved.Value is { ContentRevision: 1, SavedRevision: 1 }
                    && saved.Value.Id == runtime.Id && !string.IsNullOrWhiteSpace(saved.Value.ReceiptId),
                CommandResult<WorkspaceMetadataResult> metadata => metadata.Success && metadata.Value is { ContentRevision: 2, SavedRevision: 0 }
                    && metadata.Value.Profile.Name == "Persistence owner probe",
                _ => false
            };
            Require(receipt, "The actual canonical commit result was lost after its roaming/cancellation boundary.");
        }
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var after = PersistencePartitionSnapshots(store, runtime.Id);
        Require(after[ContactsOwnerA] != before[ContactsOwnerA]
            && after[ContactsOwnerB] == before[ContactsOwnerB]
            && after[OwnerScope.LocalSingleUser] == before[OwnerScope.LocalSingleUser]
            && roaming.BoundCalls == 1 && roaming.UnboundCalls == 0 && owners.ActiveLeases == 0,
            "Committed persistence changed another partition or bypassed bound postcommit observation.");
        var actual = store.Get(ContactsOwnerA, runtime.Id).Value!;
        Require(actual.ContentRevision == (operation == "save" ? 1 : 2)
            && actual.SavedRevision == (operation == "save" ? 1 : 0), "Persistence replayed a commit.");
        Console.WriteLine("PASS actual committed persistence: " + operation + "/" + scenario + "/" + (throughPresenter ? "presenter" : "runtime"));
    }

    private static UpdateWorkspaceMetadata PersistenceMetadata()
        => new("Persistence owner probe", "PROBE", "Exact original account");

    private static async Task<object> InvokeBoundPersistenceAsync(NativeRewardRuntime runtime, string operation,
        OwnerContextStamp original, CancellationToken ct)
    {
        Require(runtime.Client is IOwnerBoundWorkspacePersistenceClient, "Actual runtime lacks original-owner persistence capability.");
        var client = (IOwnerBoundWorkspacePersistenceClient)runtime.Client;
        if (operation == "save") return await client.SaveAsync(original, runtime.Id, 1, ct);
        if (operation == "metadata") return await client.UpdateMetadataAsync(original, runtime.Id, 1, PersistenceMetadata(), ct);
        return await client.CloseWorkspaceAsync(original, runtime.Id, 1, ct);
    }

    private sealed class PersistenceRoamingProbe(ControlledLinkedOwner owners)
        : IDesktopWorkspaceRoamingSync, IOwnerBoundDesktopWorkspaceRoamingSync
    {
        public bool BlockInbound { get; set; }
        public OwnerContextStamp? ExpectedOwner { get; set; }
        public Action? AfterCommit { get; set; }
        public int BoundCalls { get; private set; }
        public int UnboundCalls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DesktopWorkspaceRoamingResult> SynchronizeInboundAsync(OwnerScope owner, CancellationToken ct)
        {
            if (BlockInbound)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return DesktopWorkspaceRoamingResult.AlreadyCurrent();
        }
        public Task<DesktopWorkspaceRoamingResult> SynchronizeOutboundAsync(OwnerScope owner, CharacterWorkspaceId id, CancellationToken ct)
        {
            if (AfterCommit is not null) { UnboundCalls++; throw new InvalidOperationException("Unbound postcommit roaming"); }
            return Task.FromResult(DesktopWorkspaceRoamingResult.AlreadyCurrent(id));
        }
        public Task<DesktopWorkspaceRoamingResult> SynchronizeOutboundAsync(OwnerContextStamp original, CharacterWorkspaceId id, CancellationToken ct)
        {
            BoundCalls++;
            Require(owners.ActiveLeases == 0 && original == ExpectedOwner,
                "Postcommit observer crossed a lease boundary or changed the original owner.");
            AfterCommit?.Invoke();
            Require(!ct.IsCancellationRequested, "Postcommit observer reused the caller's canceled token.");
            return Task.FromResult(DesktopWorkspaceRoamingResult.AlreadyCurrent(id));
        }
    }

    private static Dictionary<OwnerScope, string> PersistencePartitionSnapshots(FileWorkspaceStore store, CharacterWorkspaceId id)
        => new[] { OwnerScope.LocalSingleUser, ContactsOwnerA, ContactsOwnerB }
            .ToDictionary(owner => owner, owner => JsonSerializer.Serialize(ReadPersistencePartition(store, owner, id)));

    private static Chummer.Application.Workspaces.WorkspaceStoreReadResult ReadPersistencePartition(
        FileWorkspaceStore store, OwnerScope owner, CharacterWorkspaceId id)
        => owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner, id);
}
