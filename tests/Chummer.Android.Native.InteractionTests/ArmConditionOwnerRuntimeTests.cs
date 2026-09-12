using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

// Original-account regression through actual native/Presentation/Core mutations.
// These temporary test partitions model owner transitions, not account credentials.
internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunArmConditionOwnerDiagnosticAsync(string contentRoot)
    {
        var failures = new List<string>();
        int passed = 0;
        foreach (string entry in new[] { "primary-arm", "condition-monitor" })
        foreach (string scenario in new[] { "same", "owner-b", "owner-aba" })
        {
            try
            {
                await RunArmConditionCaseAsync(contentRoot, entry, scenario);
                passed++;
                Console.WriteLine($"PASS actual queued owner: {entry}/{scenario}");
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add($"{entry}/{scenario}: {error.Message}");
                Console.WriteLine($"FAIL actual queued owner: {entry}/{scenario}: {error.Message}");
            }
        }
        Console.WriteLine($"ARM_CONDITION_SUMMARY total=6 passed={passed} failed={failures.Count} skipped=0");
        Require(failures.Count == 0, "Queued original-owner regression: " + string.Join("; ", failures));
    }

    private static async Task RunArmConditionCaseAsync(string contentRoot, string entry, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
        await runtime.LoadRunnerAsync("""
            <character><name>Queued owner diagnostic</name><gameedition>SR5</gameedition>
            <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings>
            <metatype>Human</metatype><buildmethod>Priority</buildmethod>
            <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
            <created>True</created><karma>30</karma><nuyen>1000</nuyen>
            <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
            <burntstreetcred>0</burntstreetcred><improvements/><primaryarm>Right</primaryarm>
            <physicalcm>10</physicalcm><physicalcmoverflow>3</physicalcmoverflow><physicalcmfilled>2</physicalcmfilled>
            <stuncm>10</stuncm><stuncmfilled>4</stuncmfilled>
            <contacts/><expenses/><notes>Preserve unrelated data</notes></character>
            """);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var a = store.Get(ContactsOwnerA, runtime.Id);
        Require(a.Success && a.Value is not null && a.Value.ContentRevision == 1 && a.Value.SavedRevision == 1,
            "SETUP: original canonical saved row is unavailable.");
        // Test storage construction through real owner-scoped APIs, not an authenticated restore.
        Require(store.CreateWorkspaceDocument(ContactsOwnerB, runtime.Id, a.Value!.Document).Success
            && store.SaveCheckpoint(ContactsOwnerB, runtime.Id, 1).Success,
            "SETUP: same-ID saved B partition could not be created.");
        Require(JsonSerializer.Serialize(store.Get(ContactsOwnerB, runtime.Id).Value!.Document)
            == JsonSerializer.Serialize(a.Value.Document), "SETUP: shadow document differs.");
        await HydrateAsync();
        CharacterOverviewState original = runtime.Coordinator.State;
        var stamp = owners.Capture();
        PrimaryArmEditorState? arm = entry == "primary-arm"
            ? await runtime.Coordinator.PreparePrimaryArmEditAsync() : null;
        if (entry == "primary-arm")
            Require(arm is { Value: "Right", Ambidextrous: false } && arm.WorkspaceId == runtime.Id
                && arm.ContentRevision == 1, "SETUP: actual primary-arm editor is not actionable.");
        else
            Require(original.ActiveConditionMonitor is { CareerEditable: true } monitor
                && monitor.Tracks.Count(track => track.Track == WorkspaceConditionMonitorTrack.Physical) == 1
                && monitor.Tracks.Single(track => track.Track == WorkspaceConditionMonitorTrack.Physical)
                    is { Filled: 2, EditableMaximum: >= 3 },
                "SETUP: actual career condition projection is not actionable.");
        var before = ReadPartitions();
        var beforeViews = DescribePartitions();
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime.Coordinator)!;
        Task? pending = null;
        Exception? setupFailure = null;
        Exception? dispatchFailure = null;
        using var cancellation = new CancellationTokenSource();
        await gate.WaitAsync();
        try
        {
            pending = entry == "primary-arm"
                ? runtime.Coordinator.ApplyPrimaryArmEditAsync(new(arm!.WorkspaceId, arm.ContentRevision, "Left"), cancellation.Token)
                : runtime.Coordinator.ApplyConditionMonitorEditAsync(new(WorkspaceConditionMonitorTrack.Physical, 3), cancellation.Token);
            Require(!pending.IsCompleted, "SETUP: actual activation barrier did not hold the mutation.");
            if (scenario != "same")
            {
                owners.Set(ContactsOwnerB);
                if (scenario == "owner-aba") owners.Set(ContactsOwnerA);
                await HydrateAsync();
                Require(owners.Capture() != stamp && runtime.Coordinator.State.DisplayOwnerContext == owners.Capture(),
                    "SETUP: owner transition did not publish a genuinely fresh bound display.");
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            setupFailure = error;
            cancellation.Cancel();
        }
        finally { gate.Release(); }
        if (pending is not null)
            try { await pending.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception error) when (error is not OutOfMemoryException) { dispatchFailure = error; }
        // Never let a UI/shell exception hide a completed canonical write.
        var after = ReadPartitions();
        var afterViews = DescribePartitions();
        string[] changed = after.Keys.Where(key => after[key] != before[key]).ToArray();
        Console.WriteLine("ARM_CONDITION_OBSERVATION " + JsonSerializer.Serialize(new
        {
            entry, scenario, originalOwner = stamp, liveOwner = owners.Capture(),
            displayOwner = runtime.Coordinator.State.DisplayOwnerContext,
            sessionOwner = runtime.Coordinator.State.Session.OwnerContext,
            runtime.Coordinator.State.WorkspaceId, runtime.Coordinator.State.ContentRevision,
            runtime.Coordinator.State.SavedRevision, runtime.Coordinator.State.Error,
            setupFailure = setupFailure?.Message, dispatchFailure = dispatchFailure?.GetType().Name,
            changed, before = beforeViews, after = afterViews, owners.ActiveLeases
        }));
        Require(setupFailure is null, "SETUP: " + setupFailure?.Message);
        Require(owners.ActiveLeases == 0, "An owner lease survived the joined operation.");
        if (scenario == "same")
        {
            var current = new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, runtime.Id).Value!;
            var xml = XDocument.Parse(current.Document.Content).Root!;
            Require(dispatchFailure is null && changed.SequenceEqual(new[] { "a" })
                && current.ContentRevision == 2 && current.SavedRevision == (entry == "primary-arm" ? 2 : 1)
                && xml.Element(entry == "primary-arm" ? "primaryarm" : "physicalcmfilled")?.Value
                    == (entry == "primary-arm" ? "Left" : "3")
                && xml.Element("notes")?.Value == "Preserve unrelated data"
                && runtime.Coordinator.State.DisplayOwnerContext == stamp,
                "POSITIVE: actual same-owner mutation failed or violated its existing checkpoint semantics.");
        }
        else
            Require(changed.Length == 0, "DEFECT: stale queued gesture mutated canonical partition(s): " + string.Join(",", changed));

        async Task HydrateAsync()
        {
            // Native owner-initialization component order; never assign presenter State.
            await runtime.Shell.InitializeAsync(default);
            await runtime.Presenter.InitializeAsync(default);
            await runtime.Presenter.LoadAsync(runtime.Id, default);
            if (entry == "condition-monitor")
            {
                await runtime.Coordinator.SelectTabAsync("tab-combat", default);
                Require(runtime.Presenter.State.ActiveTabId == "tab-combat"
                    && runtime.Shell.State.ActiveTabId == "tab-combat",
                    "SETUP: actual native tab selection did not synchronize presenter and shell.");
                var surface = runtime.Services.GetRequiredService<IShellSurfaceResolver>()
                    .Resolve(runtime.Presenter.State, runtime.Shell.State);
                var action = surface.WorkspaceActions.Single(item => item.Id == "tab-combat.conditionmonitor");
                await runtime.Presenter.ExecuteWorkspaceActionAsync(action, default);
            }
            var state = runtime.Coordinator.State;
            Require(state.WorkspaceId == runtime.Id && state.Session.ActiveWorkspaceId == runtime.Id
                && state.DisplayOwnerContext == owners.Capture() && state.Session.OwnerContext == owners.Capture()
                && runtime.Shell.State.OwnerContext == owners.Capture() && state.Profile?.Created == true
                && state.ContentRevision == 1 && state.SavedRevision == 1 && state.Error is null,
                "SETUP: actual hydrated display/session/owner differs: " + JsonSerializer.Serialize(new
                { state.WorkspaceId, state.ContentRevision, state.SavedRevision, state.Error,
                    display = state.DisplayOwnerContext, session = state.Session.OwnerContext,
                    shell = runtime.Shell.State.OwnerContext, live = owners.Capture() }));
        }

        Dictionary<string, string> ReadPartitions()
        {
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            return new()
            {
                ["a"] = JsonSerializer.Serialize(cold.Get(ContactsOwnerA, runtime.Id).Value),
                ["b"] = JsonSerializer.Serialize(cold.Get(ContactsOwnerB, runtime.Id).Value)
            };
        }

        Dictionary<string, object> DescribePartitions()
        {
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            return new[] { (Name: "a", Owner: ContactsOwnerA), (Name: "b", Owner: ContactsOwnerB) }.ToDictionary(
                pair => pair.Name, pair =>
                {
                    var result = cold.Get(pair.Owner, runtime.Id);
                    Require(result.Success && result.Value is not null, "Canonical cold row disappeared.");
                    var row = result.Value!;
                    return (object)new { row.ContentRevision, row.SavedRevision,
                        documentSha256 = Hash(JsonSerializer.Serialize(row.Document)),
                        completeRowSha256 = Hash(JsonSerializer.Serialize(row)) };
                });
        }
        static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
