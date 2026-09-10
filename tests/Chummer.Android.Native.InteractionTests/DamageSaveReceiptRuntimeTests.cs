using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

// Real local Core writes and cold partition reads. The controlled owner and
// Changed subscriber model interruption timing, not deployed account authority.
internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunDamageSaveReceiptCasesAsync(string contentRoot)
    {
        int passed = 0;
        foreach (string scenario in new[] { "same", "saved-notification-throws", "switch-after-mutation", "switch-after-save" })
        {
            try
            {
                await RunDamageSaveReceiptCaseAsync(contentRoot, scenario);
                passed++;
                Console.WriteLine("PASS actual damage save receipt: " + scenario);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.WriteLine("FAIL actual damage save receipt: " + scenario + ": " + error);
                Console.WriteLine($"DAMAGE_SAVE_RECEIPT_SUMMARY total=4 passed={passed} failed=1 notRun={3 - passed}");
                throw; // A setup failure or unjoined operation cannot become later green coverage.
            }
        }
        Console.WriteLine("DAMAGE_SAVE_RECEIPT_SUMMARY total=4 passed=4 failed=0 notRun=0");
    }

    private static async Task RunDamageSaveReceiptCaseAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
        await runtime.LoadRunnerAsync("""
            <character><name>Damage save receipt runner</name><gameedition>SR5</gameedition>
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
        var initial = store.Get(ContactsOwnerA, runtime.Id);
        Require(initial.Success && initial.Value is { ContentRevision: 1, SavedRevision: 1 },
            "SETUP: canonical original saved row is absent.");
        var initialRow = initial.Value!;
        Require(store.CreateWorkspaceDocument(ContactsOwnerB, runtime.Id, initialRow.Document).Success
            && store.SaveCheckpoint(ContactsOwnerB, runtime.Id, 1).Success,
            "SETUP: actual same-ID saved B partition could not be created.");
        await runtime.Shell.InitializeAsync(default);
        await runtime.Presenter.InitializeAsync(default);
        await runtime.Presenter.LoadAsync(runtime.Id, default);
        await runtime.Coordinator.SelectTabAsync("tab-combat", default);
        Require(runtime.Presenter.State.ActiveTabId == "tab-combat" && runtime.Shell.State.ActiveTabId == "tab-combat",
            "SETUP: native combat tab did not join shell and presenter.");
        var surface = runtime.Services.GetRequiredService<IShellSurfaceResolver>()
            .Resolve(runtime.Presenter.State, runtime.Shell.State);
        await runtime.Presenter.ExecuteWorkspaceActionAsync(
            surface.WorkspaceActions.Single(action => action.Id == "tab-combat.conditionmonitor"), default);
        CharacterOverviewState original = runtime.Coordinator.State;
        var originalOwner = owners.Capture();
        Require(original.WorkspaceId == runtime.Id && original.Session.ActiveWorkspaceId == runtime.Id
            && original.DisplayOwnerContext == originalOwner && original.Session.OwnerContext == originalOwner
            && runtime.Shell.State.OwnerContext == originalOwner && original.Profile?.Created == true
            && original.ContentRevision == 1 && original.SavedRevision == 1 && !original.IsDirty
            && !original.IsBusy && original.Error is null && original.ConflictState is null
            && original.ActiveConditionMonitor is { CareerEditable: true } monitor
            && monitor.Tracks.Count(track => track.Track == WorkspaceConditionMonitorTrack.Physical) == 1
            && monitor.Tracks.Single(track => track.Track == WorkspaceConditionMonitorTrack.Physical)
                is { Filled: 2, EditableMaximum: >= 3 },
            "SETUP: real issued condition frame is not actionable.");
        var before = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        int hookCalls = 0;
        int? leasesAtHook = null;
        string? hookSetupFailure = null;
        object? hookObservation = null;
        EventHandler interruption = (_, _) =>
        {
            if (scenario == "same" || hookCalls != 0) return;
            CharacterOverviewState state = runtime.Coordinator.State;
            long requiredSaved = scenario == "switch-after-mutation" ? 1 : 2;
            if (state.WorkspaceId != runtime.Id || state.DisplayOwnerContext != originalOwner
                || state.ContentRevision != 2 || state.SavedRevision != requiredSaved
                || scenario == "saved-notification-throws" && runtime.Coordinator.Notice != "Damage saved.") return;
            if (Interlocked.CompareExchange(ref hookCalls, 1, 0) != 0) return;
            leasesAtHook = owners.ActiveLeases;
            var cold = new FileWorkspaceStore(runtime.StateDirectory);
            var a = cold.Get(ContactsOwnerA, runtime.Id);
            var b = cold.Get(ContactsOwnerB, runtime.Id);
            hookObservation = new
            {
                state.ContentRevision, state.SavedRevision, leasesAtHook,
                aContent = a.Value?.ContentRevision, aSaved = a.Value?.SavedRevision,
                bContent = b.Value?.ContentRevision, bSaved = b.Value?.SavedRevision
            };
            if (leasesAtHook != 0 || !a.Success || a.Value is not { ContentRevision: 2 } aRow
                || aRow.SavedRevision != requiredSaved || !b.Success
                || b.Value is not { ContentRevision: 1, SavedRevision: 1 })
            {
                hookSetupFailure = "SETUP: Changed hook was not outside the real lease at its claimed canonical commit boundary.";
                throw new InvalidOperationException(hookSetupFailure);
            }
            if (scenario == "saved-notification-throws")
                throw new IOException("Synthetic optional final damage notification failure");
            owners.Set(ContactsOwnerB); // Real accessor invalidates the original stamp; no State assignment.
        };
        WorkspaceSaveReceipt? receipt = null;
        Exception? dispatchFailure = null;
        runtime.Coordinator.Changed += interruption;
        try
        {
            receipt = await runtime.Coordinator.TryApplyAndSaveBoundConditionMonitorEditAsync(
                new(WorkspaceConditionMonitorTrack.Physical, 3), original, () => true, default);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { dispatchFailure = error; }
        finally { runtime.Coordinator.Changed -= interruption; }

        // The complete helper is joined before independently opening either row.
        // Fixture disposal also joins actual presenter/owner/Core queue work.
        var after = PersistencePartitionSnapshots(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
        var actual = new FileWorkspaceStore(runtime.StateDirectory).Get(ContactsOwnerA, runtime.Id).Value!;
        Console.WriteLine("DAMAGE_SAVE_RECEIPT_OBSERVATION " + JsonSerializer.Serialize(new
        {
            scenario, originalOwner, liveOwner = owners.Capture(), hookCalls, hookObservation, hookSetupFailure,
            receipt,
            before = before.Select(pair => new { owner = pair.Key.NormalizedValue, snapshot = pair.Value }).ToArray(),
            after = after.Select(pair => new { owner = pair.Key.NormalizedValue, snapshot = pair.Value }).ToArray(),
            owners.ActiveLeases,
            dispatchFailure = dispatchFailure?.ToString()
        }));
        Require(hookSetupFailure is null, hookSetupFailure ?? "SETUP: unexpected hook failure.");
        Require(hookCalls == (scenario == "same" ? 0 : 1) && (scenario == "same" || leasesAtHook == 0),
            "SETUP: the intended interruption never ran at its actual commit boundary.");
        bool saved = scenario != "switch-after-mutation";
        Require(dispatchFailure is null && (saved
                ? receipt is not null && receipt.Id == runtime.Id && receipt.ContentRevision == 2
                    && receipt.SavedRevision == 2 && !string.IsNullOrWhiteSpace(receipt.ReceiptId)
                : receipt is null),
            "Actual damage helper lost a known receipt or claimed a checkpoint after owner drift.");
        var expectedXml = XDocument.Parse(initialRow.Document.Content);
        expectedXml.Root!.Element("physicalcmfilled")!.Value = "3";
        Require(actual.ContentRevision == 2 && actual.SavedRevision == (saved ? 2 : 1)
            && XDocument.Parse(actual.Document.Content).ToString(SaveOptions.DisableFormatting)
                == expectedXml.ToString(SaveOptions.DisableFormatting)
            && after[ContactsOwnerA] != before[ContactsOwnerA]
            && after.Where(pair => pair.Key != ContactsOwnerA).All(pair => pair.Value == before[pair.Key])
            && owners.ActiveLeases == 0,
            "Damage helper replayed, retargeted, or changed bytes beyond the actual original-owner edit/checkpoint.");
        Require(scenario.StartsWith("switch-", StringComparison.Ordinal)
                ? owners.Capture().Owner == ContactsOwnerB && owners.Capture() != originalOwner
                : owners.Capture() == originalOwner,
            "Interruption fixture did not retain its intended live owner.");
    }
}
