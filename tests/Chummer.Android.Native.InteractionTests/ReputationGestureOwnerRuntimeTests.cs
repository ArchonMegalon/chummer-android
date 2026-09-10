using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Shell;
using Chummer.Presentation.Overview;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunReputationGestureOwnerCasesAsync(string contentRoot)
    {
        VerifyTransientGestureOwnerSerialization();
        var failures = new List<string>();
        foreach (string action in new[] { "reputation-page", "burn-confirmation" })
        foreach (string scenario in new[] { "same", "owner-b", "owner-aba" })
        {
            try { await RunReputationGestureOwnerAsync(contentRoot, action, scenario); }
            catch (Exception error) { failures.Add(action + "/" + scenario + ": " + error.Message); }
        }
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS 6 actual reputation original-gesture cases");
        foreach (string scenario in new[] { "same", "stale-b", "stale-aba", "queued-b", "queued-aba" })
            await RunReputationReadOwnerAsync(contentRoot, scenario);
        foreach (string action in new[] { "reputation", "burn" })
        foreach (string scenario in new[] { "cancel-mutation", "owner-b", "owner-aba", "roaming-fault" })
            await RunReputationPostCommitOwnerAsync(contentRoot, action, scenario, native: false);
        foreach (string action in new[] { "reputation", "burn" })
        foreach (string scenario in new[] { "same", "cancel-mutation", "cancel-save", "owner-b", "owner-aba", "roaming-fault" })
            await RunReputationPostCommitOwnerAsync(contentRoot, action, scenario, native: true);
        Console.WriteLine("PASS 31 actual reputation owner/read/canonical-commit cases");
        await RunOutputOwnerCasesAsync(contentRoot);
    }

    private static void VerifyTransientGestureOwnerSerialization()
    {
        var id = new CharacterWorkspaceId("transient-gesture-workspace");
        var owner = new OwnerContextStamp(ContactsOwnerA, "memory-only-test-authority", 17);
        Verify(new CareerReputationEditorState(id, 1, 10, 4, 6, false, 0, false, 0)
            { OriginalOwner = owner });
        Verify(new CareerReputationEditRequest(id, 1, 11, 5, 7, null, null)
            { OriginalOwner = owner });
        Verify(new BurnStreetCredRequest(id, 1) { OriginalOwner = owner });
        Verify(new CharacterNotesEditRequest(id, 1, "notes", "game", "group")
            { OriginalOwner = owner });
        Verify(new NativeDurableSaveNotice(id, 1) { OriginalOwner = owner });
        Console.WriteLine("PASS 5 transient gesture-owner JSON boundaries");

        void Verify<T>(T value) where T : class
        {
            PropertyInfo property = typeof(T).GetProperty("OriginalOwner")!;
            Require(Equals(property.GetValue(value), owner), "In-memory gesture provenance was lost.");
            string json = JsonSerializer.Serialize(value);
            using JsonDocument parsed = JsonDocument.Parse(json);
            Require(!parsed.RootElement.TryGetProperty("OriginalOwner", out _)
                && !json.Contains(owner.AuthorityInstanceId, StringComparison.Ordinal),
                typeof(T).Name + " serialized memory-only authority.");
            T restored = JsonSerializer.Deserialize<T>(json)!;
            Require(property.GetValue(restored) is null, "Roundtrip recreated a privileged gesture stamp.");
            string injected = json[..^1] + ",\"OriginalOwner\":" + JsonSerializer.Serialize(owner) + "}";
            Require(property.GetValue(JsonSerializer.Deserialize<T>(injected)!) is null,
                "Untrusted JSON supplied privileged gesture provenance.");
        }
    }

    private static async Task InitializeReputationOwnerAsync(NativeRewardRuntime runtime)
    {
        await runtime.LoadRunnerAsync();
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var original = store.Get(ContactsOwnerA, runtime.Id).Value!;
        Require(store.CreateWorkspaceDocument(runtime.Id, original.Document).Success
            && store.CreateWorkspaceDocument(ContactsOwnerB, runtime.Id, original.Document).Success
            && store.SaveCheckpoint(runtime.Id, 1).Success
            && store.SaveCheckpoint(ContactsOwnerB, runtime.Id, 1).Success,
            "Could not create exact same-ID/revision reputation partitions.");
    }

    private static async Task RunReputationGestureOwnerAsync(string contentRoot, string action, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
        await InitializeReputationOwnerAsync(runtime);
        var originalOwner = runtime.Presenter.State.DisplayOwnerContext;
        var editor = await runtime.Coordinator.PrepareCareerReputationEditAsync()
            ?? throw new InvalidOperationException("Actual reputation projection is unavailable.");
        var page = new CareerReputationPage(runtime.Coordinator, editor);
        var burn = new BurnStreetCredRequest(editor.WorkspaceId, editor.ContentRevision)
            { OriginalOwner = originalOwner };
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        if (scenario != "same")
        {
            owners.Set(ContactsOwnerB);
            if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
            await runtime.Presenter.LoadAsync(runtime.Id, default);
        }
        var beforeView = runtime.Presenter.State;
        Exception? failure = null;
        try
        {
            if (action == "reputation-page")
                await ((Task)typeof(CareerReputationPage).GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, null)!).WaitAsync(TimeSpan.FromSeconds(20));
            else await runtime.Coordinator.ApplyBurnStreetCredAsync(burn);
        }
        catch (Exception error) { failure = error; }
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var after = PersistencePartitionSnapshots(store, runtime.Id);
        bool current = scenario == "same";
        if (!current)
            Require(before.All(pair => after[pair.Key] == pair.Value)
                && ReferenceEquals(beforeView, runtime.Presenter.State),
                "A stale native reputation action changed the replacement account or its view.");
        else
        {
            var saved = store.Get(ContactsOwnerA, runtime.Id).Value!;
            Require(saved.ContentRevision == 2 && saved.SavedRevision == 2
                && after[ContactsOwnerB] == before[ContactsOwnerB]
                && after[OwnerScope.LocalSingleUser] == before[OwnerScope.LocalSingleUser],
                "The unchanged-account reputation action did not save exactly once: " + failure?.Message);
            if (action == "burn-confirmation")
                Require(XDocument.Parse(saved.Document.Content).Root!.Element("burntstreetcred")?.Value == "2",
                    "Actual Street Cred burn did not retain canonical semantics.");
        }
        Require(owners.ActiveLeases == 0, "Reputation gesture leaked its owner lease.");
        Console.WriteLine("PASS actual reputation gesture: " + action + "/" + scenario);
    }

    private static async Task RunReputationReadOwnerAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializeReputationOwnerAsync(runtime);
        var original = runtime.Presenter.State;
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        bool queued = scenario.StartsWith("queued-", StringComparison.Ordinal);
        Task? predecessor = null;
        Task<CareerReputationEditorState?>? pending = null;
        try
        {
            if (queued)
            {
                roaming.BlockInbound = true;
                predecessor = ((IOwnerBoundShellStateClient)runtime.Client).ListWorkspacesAsync(owners.Capture(), default);
                await roaming.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                pending = runtime.Coordinator.PrepareCareerReputationEditAsync();
                Require(!pending.IsCompleted, "Reputation read did not enter the actual executor queue.");
            }
            if (scenario != "same") owners.Set(ContactsOwnerB);
            if (scenario.EndsWith("aba", StringComparison.Ordinal)) owners.Set(ContactsOwnerA);
            pending ??= runtime.Coordinator.PrepareCareerReputationEditAsync();
        }
        finally { roaming.Release.TrySetResult(); }
        CareerReputationEditorState? projected = await pending!.WaitAsync(TimeSpan.FromSeconds(10));
        if (predecessor is not null)
            try { await predecessor.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (InvalidOperationException) { }
        var after = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        Require((scenario == "same"
                ? projected is not null && projected.OriginalOwner == original.DisplayOwnerContext && projected.StreetCred == 10
                : projected is null && ReferenceEquals(runtime.Presenter.State, original))
            && before.All(pair => after[pair.Key] == pair.Value) && owners.ActiveLeases == 0,
            "Reputation preparation minted facts from a changed account or overwrote its view.");
        Console.WriteLine("PASS actual reputation original read: " + scenario);
    }

    private static async Task RunReputationPostCommitOwnerAsync(string contentRoot, string action, string scenario, bool native)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var roaming = new PersistenceRoamingProbe(owners);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, persistenceRoaming: roaming);
        await InitializeReputationOwnerAsync(runtime);
        var editor = await runtime.Coordinator.PrepareCareerReputationEditAsync()
            ?? throw new InvalidOperationException("Original reputation editor missing.");
        var original = owners.Capture();
        Require(editor.OriginalOwner == original, "Actual reputation read did not retain its original owner.");
        var edit = new CareerReputationEditRequest(runtime.Id, 1, 11, 5, 7, null, null) { OriginalOwner = editor.OriginalOwner };
        var burn = new BurnStreetCredRequest(runtime.Id, 1) { OriginalOwner = editor.OriginalOwner };
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        using var canceled = new CancellationTokenSource();
        roaming.ExpectedOwner = original;
        roaming.AfterCommit = () =>
        {
            if (scenario == "cancel-mutation" || scenario == "cancel-save" && roaming.BoundCalls == 2) canceled.Cancel();
            if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
            if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
            if (scenario == "roaming-fault") throw new IOException("Synthetic XML postcommit roaming failure");
        };
        bool checkpointExpected = native && scenario is "same" or "cancel-save" or "roaming-fault";
        bool saved = false;
        CommandResult<WorkspaceRevisionReceipt>? result = null;
        Exception? failure = null;
        try
        {
            if (native)
                saved = action == "reputation" ? await runtime.Coordinator.ApplyCareerReputationEditAsync(edit, canceled.Token)
                    : await runtime.Coordinator.ApplyBurnStreetCredAsync(burn, canceled.Token);
            else
            {
                var bound = (IOwnerBoundWorkspaceMutationPresenter)runtime.Presenter;
                result = action == "reputation" ? await bound.ApplyCareerReputationEditAsync(edit, original, canceled.Token)
                    : await bound.ApplyBurnStreetCredAsync(burn, original, canceled.Token);
            }
        }
        catch (Exception error) { failure = error; }
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var current = store.Get(ContactsOwnerA, runtime.Id).Value!;
        var after = PersistencePartitionSnapshots(store, runtime.Id);
        Require((native ? saved == checkpointExpected && (failure is null || scenario == "cancel-mutation" && failure is OperationCanceledException)
                : failure is null && result is { Success: true, Value: { ContentRevision: 2, SavedRevision: 1 } } && result.Value.Id == runtime.Id)
            && current.ContentRevision == 2 && current.SavedRevision == (checkpointExpected ? 2 : 1)
            && after[ContactsOwnerB] == before[ContactsOwnerB]
            && after[OwnerScope.LocalSingleUser] == before[OwnerScope.LocalSingleUser]
            && roaming.BoundCalls == (checkpointExpected ? 2 : 1) && roaming.UnboundCalls == 0 && owners.ActiveLeases == 0,
            "Reputation lost/replayed its commit or retargeted the save: " + action + "/" + scenario + "/" + native + "/" + failure?.Message);
        Console.WriteLine("PASS actual reputation postcommit: " + action + "/" + scenario + "/" + (native ? "native" : "canonical"));
    }
}
