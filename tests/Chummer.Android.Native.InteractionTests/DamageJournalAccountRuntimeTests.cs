using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

internal static partial class AfterRunAuthorityHarness
{
    // Real native projection/recovery, real leased account accessor and canonical
    // cold store. This is headless managed evidence, not a device/authentication
    // claim, nor proof that a matching successor belongs to one operation ID.
    public static async Task RunDamageJournalAccountCasesAsync(string contentRoot)
    {
        string[] cases = ["same-original", "same-successor", "b-original", "b-successor",
            "old-page-b", "old-page-aba", "fresh-page-aba", "legacy-a", "legacy-b",
            "admission-missing", "admission-value-only", "admission-invalidated",
            "admission-foreign-scope", "admission-false-with-lease", "admission-true-with-null"];
        var failures = new List<string>();
        int executed = 0;
        int setupFailures = 0;
        foreach (string scenario in cases)
        {
            executed++;
            try
            {
                if (scenario.StartsWith("admission-", StringComparison.Ordinal))
                    await RunDamageJournalAdmissionFailureAsync(contentRoot, scenario);
                else
                    await RunDamageJournalAccountCaseAsync(contentRoot, scenario);
                Console.WriteLine("PASS damage journal account: " + scenario);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add(scenario + ": " + error);
                string detail = error.ToString();
                bool setup = detail.Contains("SETUP:", StringComparison.Ordinal)
                    || detail.Contains("unjoined", StringComparison.OrdinalIgnoreCase)
                    || detail.Contains("did not join", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"FAIL {(setup ? "SETUP" : "REGRESSION")} damage journal account: {scenario}: {detail}");
                if (setup)
                {
                    setupFailures++;
                    break; // Do not interpret later unavailable fixtures as product observations.
                }
            }
        }
        Console.WriteLine($"DAMAGE_JOURNAL_ACCOUNT total={cases.Length} executed={executed} passed={executed - failures.Count} failed={failures.Count} setupFailed={setupFailures} notRun={cases.Length - executed}");
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task RunDamageJournalAdmissionFailureAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var probe = new JournalAdmissionProbe(owners, scenario);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            damageJournalAccessorDecorator: actual =>
            {
                Require(ReferenceEquals(actual, owners), "SETUP: journal fixture changed the real Core accessor.");
                return scenario switch
                {
                    "admission-missing" => null,
                    "admission-value-only" => new JournalValueOnlyOwner(owners),
                    _ => probe
                };
            });
        await SeedJournalAccountRuntimeAsync(runtime, owners);
        var state = runtime.Coordinator.State;
        // Pure fixture projection seeds actual journal bytes without granting a
        // replacement lease or successful validator to the page under test.
        Require(Sr5PlaytimeDamageIntegrity.TryProject(ContactsOwnerA, state.Profile?.Created == true,
                state.Rules?.GameEdition, state.WorkspaceId, state.ContentRevision, state.SavedRevision,
                state.IsDirty, state.Error, state.ActiveConditionMonitor, JournalAccountTrack, out var snapshot)
                && snapshot.IsExact(), "SETUP: canonical clean account fixture did not project.");
        Require(Sr5PlaytimeDamageIntegrity.TryQuote(snapshot, 3, Guid.NewGuid(), out var quote),
            "SETUP: fixture quote failed.");
        var store = JournalAccountStore(ContactsOwnerA, runtime.Id);
        Require(store.TryWriteReview(quote, Guid.NewGuid(), out var review, out var blocker)
                && store.TryBeginApplying(review, out _, out blocker), "SETUP: " + blocker);
        var journalBackend = JournalAccountBackend(ContactsOwnerA, runtime.Id);
        var mutationBackend = new PreferencesSr5CareerMutationOwnerBackend();
        string journal = journalBackend.Read(), reservation = mutationBackend.Read();
        var before = ReadJournalAccountRows(runtime);
        var page = new Sr5PlaytimeDamageWizardPage(runtime.Coordinator, JournalAccountTrack, runtime.Id, store);
        typeof(Sr5PlaytimeDamageWizardPage).GetMethod("LoadLatest",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null, types: [typeof(CancellationToken)], modifiers: null)!
            .Invoke(page, [CancellationToken.None]);
        var after = ReadJournalAccountRows(runtime);
        Require(before.All(pair => after[pair.Key].Complete == pair.Value.Complete)
                && ReferenceEquals(state, runtime.Coordinator.State)
                && journalBackend.Read() == journal && mutationBackend.Read() == reservation
                && JournalAccountField<Sr5PlaytimeDamageSnapshot>(page, "_snapshot") is null
                && JournalAccountField<Sr5PlaytimeDamageJournal>(page, "_journal") is null
                && !string.IsNullOrWhiteSpace(JournalAccountField<string>(page, "_notice"))
                && owners.ActiveLeases == 0 && probe.Foreign.ActiveLeases == 0,
            "Failed/missing/mismatched lease admission changed history, rows, display or leaked a lease.");
        Require(probe.Calls == (scenario is "admission-missing" or "admission-value-only" ? 0 : 1),
            "The intended lease-admission boundary was not exercised exactly once.");
        Console.WriteLine($"DAMAGE_JOURNAL_ADMISSION scenario={scenario} calls={probe.Calls} rowsUnchanged=true journalUnchanged=true reservationUnchanged=true leases=0");
    }

    private sealed class JournalValueOnlyOwner(ControlledLinkedOwner actual) : IOwnerContextAccessor
    {
        public OwnerScope Current => actual.Current;
    }

    // Hostile capability-return contracts wrap genuine test issuer leases. No
    // wrapper fabricates a successful mutation/receipt or changes Core's client.
    private sealed class JournalAdmissionProbe(ControlledLinkedOwner actual, string scenario) : IOwnerContextLeaseAccessor
    {
        public readonly ControlledLinkedOwner Foreign = new();
        public int Calls;
        public OwnerScope Current => actual.Current;
        public OwnerContextStamp Capture() => actual.Capture();
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Calls++;
            lease = null;
            if (scenario == "admission-invalidated")
            {
                actual.Set(ContactsOwnerB);
                return actual.TryAcquire(expected, out lease);
            }
            if (scenario == "admission-foreign-scope")
            {
                Foreign.Set(ContactsOwnerB);
                return Foreign.TryAcquire(Foreign.Capture(), out lease);
            }
            if (scenario == "admission-false-with-lease")
            {
                Require(actual.TryAcquire(expected, out lease), "SETUP: genuine lease unavailable.");
                return false;
            }
            if (scenario == "admission-true-with-null")
            {
                lease = null!; // Deliberate hostile contract violation.
                return true;
            }
            throw new InvalidOperationException("Unexpected admission fixture.");
        }
    }

    private static async Task RunDamageJournalAccountCaseAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners);
        await SeedJournalAccountRuntimeAsync(runtime, owners);
        OwnerContextStamp originalStamp = owners.Capture();
        var aBackend = JournalAccountBackend(ContactsOwnerA, runtime.Id);
        var bBackend = JournalAccountBackend(ContactsOwnerB, runtime.Id);
        var ownerBackend = new PreferencesSr5CareerMutationOwnerBackend();
        var aStore = JournalAccountStore(ContactsOwnerA, runtime.Id);
        var original = Sr5PlaytimeDamageWizardPage.ProjectForOriginalOwner(runtime.Coordinator,
            originalStamp, runtime.Id, JournalAccountTrack);
        Require(original is { Filled: 2, WorkspaceRevision: 1, SavedRevision: 1 }
                && original.AccountOwner == ContactsOwnerA && original.IsExact(),
            "SETUP: real leased A projection was unavailable.");
        Require(Sr5PlaytimeDamageIntegrity.TryQuote(original!, 3, Guid.NewGuid(), out var quote),
            "SETUP: real projection could not be quoted.");
        // Seed durable Applying through the actual journal store. No mutation or
        // fabricated canonical Core receipt is supplied by this fixture.
        Require(aStore.TryWriteReview(quote, Guid.NewGuid(), out var review, out var blocker)
                && aStore.TryBeginApplying(review, out _, out blocker), "SETUP: " + blocker);
        Require(aStore.TryRead(out var applying, out blocker)
                && applying is { Phase: Sr5PlaytimeDamageTransactionPhase.Applying }, "SETUP: " + blocker);
        var originalPage = new Sr5PlaytimeDamageWizardPage(runtime.Coordinator,
            JournalAccountTrack, runtime.Id, aStore);

        bool legacy = scenario.StartsWith("legacy-", StringComparison.Ordinal);
        string legacyKey = "sr5.playtime.damage.physical." + JournalAccountHash(runtime.Id.Value) + ".v1";
        const string legacyBytes = "{\"SchemaVersion\":1,\"Phase\":1,\"unbound-history\":true}";
        if (legacy)
        {
            // Model unattributed old storage; no claim that an unbound record
            // can be authenticated or safely assigned to the current account.
            runtime.Settings.Set(legacyKey, legacyBytes);
        }
        bool toB = scenario is "b-original" or "b-successor" or "old-page-b" or "legacy-b";
        bool aba = scenario is "old-page-aba" or "fresh-page-aba";
        if (toB || aba)
        {
            owners.Set(ContactsOwnerB);
            if (aba) owners.Set(ContactsOwnerA);
            await HydrateJournalAccountRuntimeAsync(runtime, owners, 1);
            Require(owners.Capture() != originalStamp, "SETUP: owner transition did not advance its real epoch.");
        }
        bool successor = scenario is "same-successor" or "b-successor";
        if (successor)
        {
            // This independently seeds a saved successor in the selected owner
            // partition; it does NOT attribute that mutation to A's journal.
            await runtime.Coordinator.ApplyConditionMonitorEditAsync(new(JournalAccountTrack, 3), default);
            var save = await runtime.Presenter.SaveAsync(owners.Capture(), runtime.Id, 2, default);
            Require(save.Success && save.Value is { ContentRevision: 2, SavedRevision: 2 } receipt
                    && receipt.Id == runtime.Id, "SETUP: real successor mutation/checkpoint failed.");
            await HydrateJournalAccountRuntimeAsync(runtime, owners, 2);
        }
        var before = ReadJournalAccountRows(runtime);
        string aBytes = aBackend.Read(), bBytes = bBackend.Read(), sharedBytes = ownerBackend.Read();
        Require(aBytes.Length > 0 && bBytes.Length == 0 && sharedBytes.Length > 0,
            "SETUP: A journal/global reservation and empty B namespace were not preserved.");
        OwnerScope liveAccount = owners.Capture().Owner;
        bool oldPage = scenario.StartsWith("old-page-", StringComparison.Ordinal);
        var page = oldPage ? originalPage : new Sr5PlaytimeDamageWizardPage(runtime.Coordinator,
            JournalAccountTrack, runtime.Id, JournalAccountStore(liveAccount, runtime.Id));
        CharacterOverviewState displayed = runtime.Coordinator.State;
        typeof(Sr5PlaytimeDamageWizardPage).GetMethod("LoadLatest",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null, types: [typeof(CancellationToken)], modifiers: null)!
            .Invoke(page, [CancellationToken.None]);
        // LoadLatest is synchronous; all real seeding tasks were awaited before
        // these independent cold reads. No task or lease survives observation.
        var after = ReadJournalAccountRows(runtime);
        string afterA = aBackend.Read(), afterB = bBackend.Read(), afterShared = ownerBackend.Read();
        var pageSnapshot = JournalAccountField<Sr5PlaytimeDamageSnapshot>(page, "_snapshot");
        var pageJournal = JournalAccountField<Sr5PlaytimeDamageJournal>(page, "_journal");
        string? notice = JournalAccountField<string>(page, "_notice");
        Console.WriteLine("DAMAGE_JOURNAL_ACCOUNT_OBSERVATION " + JsonSerializer.Serialize(new
        {
            scenario, originalOwner = originalStamp.Owner.NormalizedValue,
            originalEpoch = originalStamp.TransitionRevision,
            liveOwner = liveAccount.NormalizedValue, liveEpoch = owners.Capture().TransitionRevision,
            before = before.ToDictionary(p => p.Key, p => new { p.Value.Content, p.Value.Saved, p.Value.Filled, p.Value.Digest }),
            after = after.ToDictionary(p => p.Key, p => new { p.Value.Content, p.Value.Saved, p.Value.Filled, p.Value.Digest }),
            journalAChanged = aBytes != afterA, journalBChanged = bBytes != afterB,
            sharedOwnerChanged = sharedBytes != afterShared,
            pageSnapshotOwner = pageSnapshot?.AccountOwner.NormalizedValue,
            pagePhase = pageJournal?.Phase.ToString(), notice, owners.ActiveLeases
        }));
        Require(before.All(pair => after[pair.Key].Complete == pair.Value.Complete)
                && ReferenceEquals(displayed, runtime.Coordinator.State) && owners.ActiveLeases == 0,
            "Recovery changed canonical rows/display or leaked a real owner lease.");
        Require(afterB == bBytes, "Recovery changed another account's journal namespace.");
        if (legacy || toB || oldPage)
        {
            Require(afterA == aBytes && afterShared == sharedBytes,
                "Wrong-account/stale/legacy recovery changed A's journal or released its shared reservation.");
            if (legacy)
                Require(pageSnapshot is null && pageJournal is null
                        && notice == Sr5PlaytimeDamageJournalStore.LegacyUnattributedBlocker
                        && runtime.Settings.Get(legacyKey, string.Empty) == legacyBytes,
                    "Legacy recovery must remain explicitly quarantined, without clearing old bytes.");
            else if (oldPage)
                Require(pageSnapshot is null && pageJournal is null && !string.IsNullOrWhiteSpace(notice),
                    "An old displayed epoch cannot project or read history after B/ABA.");
            else
                Require(pageSnapshot?.AccountOwner == ContactsOwnerB && pageJournal is null,
                    "B may display B's exact runner, but must not borrow A's journal.");
        }
        else
        {
            Require(pageJournal is not null && pageJournal.IsExact()
                    && pageJournal.Quote.Original.AccountOwner == ContactsOwnerA
                    && pageJournal.Phase == (successor ? Sr5PlaytimeDamageTransactionPhase.Applied
                        : Sr5PlaytimeDamageTransactionPhase.Reviewed)
                    && afterShared.Length == 0,
                "Same stable account recovery failed its existing exact resolution semantics.");
            if (successor)
                Require(pageJournal!.Receipt is { } recovered && recovered.IsExact()
                        && recovered.AccountOwner == ContactsOwnerA
                        && recovered.ExpectedWorkspaceRevision == 1 && recovered.AppliedWorkspaceRevision == 2,
                    "Same-account recovered history lost its stable account/revision binding.");
            // Cold store reconstructs the same history without executing Core.
            Require(JournalAccountStore(ContactsOwnerA, runtime.Id).TryRead(out var cold, out blocker)
                    && cold == pageJournal && aBackend.Read() == afterA,
                "Cold same-account history replay changed the durable journal.");
        }
    }

    private const WorkspaceConditionMonitorTrack JournalAccountTrack = WorkspaceConditionMonitorTrack.Physical;
    private static Sr5PlaytimeDamageJournalStore JournalAccountStore(OwnerScope owner, CharacterWorkspaceId id)
        => Sr5PlaytimeDamageJournalStore.CreateDefault(owner, JournalAccountTrack, id);
    private static PreferencesSr5PlaytimeDamageJournalBackend JournalAccountBackend(OwnerScope owner, CharacterWorkspaceId id)
        => new(owner, JournalAccountTrack, id);
    private static T? JournalAccountField<T>(object target, string name) where T : class
        => (T?)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .GetValue(target);
    private static string JournalAccountHash(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record JournalAccountRow(long Content, long Saved, string Filled, string Digest, string DocumentDigest, string Complete);
    private static Dictionary<string, JournalAccountRow> ReadJournalAccountRows(NativeRewardRuntime runtime)
    {
        var cold = new FileWorkspaceStore(runtime.StateDirectory);
        return new[] { (Name: "a", Owner: ContactsOwnerA), (Name: "b", Owner: ContactsOwnerB) }
            .ToDictionary(pair => pair.Name, pair =>
            {
                var read = cold.Get(pair.Owner, runtime.Id);
                Require(read.Success && read.Value is not null, "SETUP: canonical cold row unavailable.");
                var row = read.Value!;
                string exact = JsonSerializer.Serialize(row);
                return new JournalAccountRow(row.ContentRevision, row.SavedRevision,
                    XDocument.Parse(row.Document.Content).Root!.Element("physicalcmfilled")?.Value ?? "",
                    JournalAccountHash(exact), JournalAccountHash(JsonSerializer.Serialize(row.Document)), exact);
            });
    }

    private static async Task SeedJournalAccountRuntimeAsync(NativeRewardRuntime runtime, ControlledLinkedOwner owners)
    {
        await runtime.LoadRunnerAsync("""
            <character><name>Account-bound damage recovery fixture</name><gameedition>SR5</gameedition>
            <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings><metatype>Human</metatype>
            <buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
            <created>True</created><karma>30</karma><nuyen>1000</nuyen><streetcred>10</streetcred>
            <notoriety>4</notoriety><publicawareness>6</publicawareness><burntstreetcred>0</burntstreetcred><improvements/>
            <physicalcm>10</physicalcm><physicalcmoverflow>3</physicalcmoverflow><physicalcmfilled>2</physicalcmfilled>
            <stuncm>10</stuncm><stuncmfilled>4</stuncmfilled><contacts/><expenses/>
            <notes>Preserve journal account data</notes></character>
            """);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var original = store.Get(ContactsOwnerA, runtime.Id);
        Require(original.Success && original.Value is not null, "SETUP: A row unavailable.");
        Require(store.CreateWorkspaceDocument(ContactsOwnerB, runtime.Id, original.Value!.Document).Success
                && store.SaveCheckpoint(ContactsOwnerB, runtime.Id, 1).Success,
            "SETUP: real owner-scoped same-ID B clone/checkpoint failed.");
        await HydrateJournalAccountRuntimeAsync(runtime, owners, 1);
        var rows = ReadJournalAccountRows(runtime);
        Require(rows.Values.All(row => row.Content == 1 && row.Saved == 1 && row.Filled == "2")
                && rows["a"].DocumentDigest == rows["b"].DocumentDigest, "SETUP: original saved partitions differ.");
    }

    private static async Task HydrateJournalAccountRuntimeAsync(NativeRewardRuntime runtime,
        ControlledLinkedOwner owners, long revision)
    {
        await runtime.Shell.InitializeAsync(default);
        await runtime.Presenter.InitializeAsync(default);
        await runtime.Presenter.LoadAsync(runtime.Id, default);
        await runtime.Coordinator.SelectTabAsync("tab-combat", default);
        var surface = runtime.Services.GetRequiredService<IShellSurfaceResolver>()
            .Resolve(runtime.Presenter.State, runtime.Shell.State);
        await runtime.Presenter.ExecuteWorkspaceActionAsync(
            surface.WorkspaceActions.Single(item => item.Id == "tab-combat.conditionmonitor"), default);
        var state = runtime.Coordinator.State;
        Require(state.WorkspaceId == runtime.Id && state.Session.ActiveWorkspaceId == runtime.Id
                && state.DisplayOwnerContext == owners.Capture() && state.Session.OwnerContext == owners.Capture()
                && runtime.Shell.State.OwnerContext == owners.Capture() && state.Error is null
                && state.ConflictState is null && !state.IsBusy && !state.IsDirty
                && state.ContentRevision == revision && state.SavedRevision == revision
                && state.ActiveConditionMonitor is { CareerEditable: true },
            "SETUP: live owner/display/session/shell/clean revision did not hydrate.");
    }
}
