using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Workspaces;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunAndroidContinuationRoamingCasesAsync()
    {
        await RunRoamingOwnerAdmissionAsync();
        await RunRoamingConfirmedCasAsync();
        await RunRoamingChangedRemoteAsync(raceAfterList: false);
        await RunRoamingChangedRemoteAsync(raceAfterList: true);
        await RunRoamingRestartBaseLossAsync();
        foreach (bool existing in new[] { false, true })
        foreach (bool committed in new[] { false, true })
            await RunRoamingUnknownOutcomeAsync(existing, committed);
        await RunRoamingUnknownRestartAsync();
        await RunRoamingRemoteDeletionAsync();
        Console.WriteLine("PASS 11 actual Core/account continuation roaming cases");
    }

    private static async Task RunRoamingOwnerAdmissionAsync()
    {
        using var fixture = await RoamingFixture.CreateAsync();
        OwnerContextStamp original = fixture.Stamp;
        string before = fixture.LocalBytes();
        Require((await fixture.Sync.SynchronizeOutboundAsync(original.Owner, fixture.Id, default)).Outcome
                == DesktopWorkspaceRoamingOutcome.Unavailable
            && (await fixture.Sync.SynchronizeInboundAsync(original.Owner, default)).Outcome
                == DesktopWorkspaceRoamingOutcome.Unavailable
            && fixture.Account.ContinuationRequests == 0 && fixture.Owners.Attempts == 0,
            "A value-only owner or startup roster hook authorized continuation transport.");
        Require((await fixture.Sync.SynchronizeOutboundAsync(original with { AuthorityInstanceId = "foreign-issuer" },
                fixture.Id, default)).Outcome == DesktopWorkspaceRoamingOutcome.Unauthorized,
            "An unissued owner stamp reached the Core export boundary.");
        await fixture.Account.LinkAsync("roaming-B", "roaming-grant-B");
        await fixture.Account.LinkAsync("subject", "roaming-grant-A2");
        Require((await fixture.Sync.SynchronizeOutboundAsync(original, fixture.Id, default)).Outcome
                == DesktopWorkspaceRoamingOutcome.Unauthorized
            && fixture.Account.ContinuationRequests == 0 && fixture.LocalBytes() == before,
            "A real account A-to-B-to-A transition revived a queued roaming operation.");

        OwnerContextStamp current = fixture.Account.Owner.Capture();
        fixture.Account.BeforeHttpResponse = async () =>
        {
            fixture.Account.BeforeHttpResponse = null;
            await fixture.Account.LinkAsync("roaming-C", "roaming-grant-C");
        };
        var result = await fixture.Sync.SynchronizeOutboundAsync(current, fixture.Id, default)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Require(result.Outcome == DesktopWorkspaceRoamingOutcome.Unauthorized && fixture.Upserts == 0
            && fixture.Owners.Active == 0 && fixture.LocalBytes() == before,
            "A list wait retained a credential lease or uploaded under a superseding actual account.");
        await fixture.Account.Account.UnlinkAsync();
        OwnerContextStamp local = fixture.Account.Owner.Capture();
        int requests = fixture.Account.ContinuationRequests;
        Require(local.Owner == OwnerScope.LocalSingleUser
            && (await fixture.Sync.SynchronizeOutboundAsync(local, fixture.Id, default)).Outcome
                == DesktopWorkspaceRoamingOutcome.Unavailable
            && fixture.Account.ContinuationRequests == requests,
            "The genuine device-local owner was incorrectly used for linked continuation transport.");
        Console.WriteLine("PASS roaming rejects unbound/foreign/stale owners and account change during the actual list wait");
    }

    private static async Task RunRoamingConfirmedCasAsync()
    {
        using var fixture = await RoamingFixture.CreateAsync();
        int leaseChecks = 0;
        fixture.Account.BeforeHttpResponse = () =>
        {
            bool acquired = fixture.Account.Owner.TryAcquire(fixture.Stamp, out var lease);
            Require(fixture.Owners.Active == 0 && acquired,
                "Roaming retained or reentered the actual non-reentrant owner lease across HTTP.");
            using (lease) leaseChecks++;
            return Task.CompletedTask;
        };
        var first = await fixture.PublishAsync();
        Require(first.Outcome == DesktopWorkspaceRoamingOutcome.Applied && fixture.Upserts == 1
            && fixture.Preconditions[0] == (0L, null) && fixture.Owners.Attempts >= 1,
            "Initial publish did not use missing-only CAS and actual Core owner-lease admission.");
        fixture.AssertRemoteEqualsActualExport();
        long firstRevision = fixture.RemoteRevision;
        string firstToken = fixture.RemoteToken!;
        fixture.ChangeLocal("Confirmed local edit two");
        var second = await fixture.PublishAsync();
        Require(second.Outcome == DesktopWorkspaceRoamingOutcome.Applied && fixture.Upserts == 2
            && fixture.Preconditions[1] == (firstRevision, firstToken),
            "A legitimate later local edit did not bind the exact previously confirmed remote CAS base.");
        fixture.AssertRemoteEqualsActualExport();
        Require(fixture.Export().Snapshot.Workspace is { ContentRevision: 2, SavedRevision: 1 },
            "The real Core fixture failed to retain dirty content and its earlier saved checkpoint.");
        long secondRevision = fixture.RemoteRevision;
        string secondToken = fixture.RemoteToken!;
        fixture.ChangeLocal("Confirmed local edit three");
        Require((await fixture.PublishAsync()).Outcome == DesktopWorkspaceRoamingOutcome.Applied
            && fixture.Preconditions[2] == (secondRevision, secondToken),
            "The second confirmed remote base was not advanced for the next legitimate edit.");
        int writes = fixture.Upserts;
        Require((await fixture.PublishAsync()).Outcome == DesktopWorkspaceRoamingOutcome.AlreadyCurrent
            && fixture.Upserts == writes && leaseChecks == 7 && fixture.Owners.Active == 0,
            "An exact-digest read unnecessarily wrote again or failed to release the owner lease before transport.");
        fixture.AssertRemoteEqualsActualExport();
        Console.WriteLine("PASS actual Core export → signed initial publish → repeated confirmed CAS → exact no-op, without nested owner leases");
    }

    private static async Task RunRoamingChangedRemoteAsync(bool raceAfterList)
    {
        using var fixture = await RoamingFixture.CreateAsync();
        Require((await fixture.PublishAsync()).Outcome == DesktopWorkspaceRoamingOutcome.Applied,
            "Remote conflict fixture failed its genuine initial publication.");
        fixture.ChangeLocal("Other device's real exported document");
        WorkspaceContinuationExport competing = fixture.Export();
        fixture.ChangeLocal("Local document kept after conflict");
        string localBefore = fixture.LocalBytes();
        if (raceAfterList) fixture.AfterListCapture = () => fixture.SetRemote(competing);
        else fixture.SetRemote(competing);
        var result = await fixture.PublishAsync();
        Require(result.Outcome == DesktopWorkspaceRoamingOutcome.Conflict
            && fixture.Upserts == (raceAfterList ? 2 : 1)
            && fixture.RemoteDigest == competing.SnapshotDigest && fixture.LocalBytes() == localBefore,
            "A changed remote copy bypassed read-base validation or the actual signed transport's final CAS conflict.");
        Console.WriteLine("PASS actual roaming remote conflict " + (raceAfterList ? "after signed list, before CAS" : "before list"));
    }

    private static async Task RunRoamingRestartBaseLossAsync()
    {
        using var fixture = await RoamingFixture.CreateAsync();
        Require((await fixture.PublishAsync()).Outcome == DesktopWorkspaceRoamingOutcome.Applied,
            "Base-loss fixture did not publish its initial remote version.");
        fixture.ChangeLocal("Local changes after losing transient remote-base memory");
        string before = fixture.LocalBytes();
        // Reconstruct the adapter and the actual FileStore/export service, not a
        // fake exporter. This simulates loss of transient bases, not an Android
        // process/device restart or a new account grant.
        var restarted = new AndroidWorkspaceContinuationRoamingSync(fixture.Owners,
            new WorkspaceContinuationExportService(new FileWorkspaceStore(fixture.Directory), fixture.Owners),
            fixture.Account.Transport, fixture.Directory);
        Require((await restarted.SynchronizeOutboundAsync(fixture.Stamp, fixture.Id, default)).Outcome
                == DesktopWorkspaceRoamingOutcome.Conflict
            && fixture.Upserts == 1 && fixture.LocalBytes() == before,
            "A reconstructed adapter guessed a remote base for divergent local content.");
        // An exact read may establish a base for this fresh adapter; it is not a
        // mutation or timestamp-based overwrite of an unknown remote revision.
        fixture.SetRemote(fixture.Export());
        Require((await restarted.SynchronizeOutboundAsync(fixture.Stamp, fixture.Id, default)).Outcome
                == DesktopWorkspaceRoamingOutcome.AlreadyCurrent && fixture.Upserts == 1,
            "An identical full digest could not reconcile a lost transient base without writing.");
        Console.WriteLine("PASS reconstructed roaming adapter with real cold FileStore rejects divergent base loss and reconciles exact identity");
    }

    private static async Task RunRoamingUnknownOutcomeAsync(bool existing, bool committed)
    {
        using var fixture = await RoamingFixture.CreateAsync();
        if (existing)
        {
            Require((await fixture.PublishAsync()).Outcome == DesktopWorkspaceRoamingOutcome.Applied,
                "Unknown-update fixture failed initial publish.");
            fixture.ChangeLocal("Local edit with unknown remote outcome");
        }
        fixture.DropNextWriteResponse = true;
        fixture.CommitDroppedWrite = committed;
        var unknown = await fixture.PublishAsync();
        int writes = fixture.Upserts;
        string before = fixture.LocalBytes();
        Require(unknown.Outcome == DesktopWorkspaceRoamingOutcome.Unavailable,
            "A connection loss was reported as a confirmed roaming commit.");
        var observed = await fixture.PublishAsync();
        Require(fixture.Upserts == writes && fixture.LocalBytes() == before
            && (committed ? observed.Outcome == DesktopWorkspaceRoamingOutcome.AlreadyCurrent
                : observed.Outcome is DesktopWorkspaceRoamingOutcome.Conflict or DesktopWorkspaceRoamingOutcome.Unavailable),
            "Roaming automatically replayed an unknown " + (existing ? "update" : "initial publish")
                + " instead of read-only exact reconciliation or an explicit conflict.");
        Console.WriteLine("PASS roaming unknown " + (existing ? "update" : "initial publish")
            + "/" + (committed ? "committed" : "uncommitted") + " never issues an automatic mutation retry");
    }

    private static async Task RunRoamingRemoteDeletionAsync()
    {
        using var fixture = await RoamingFixture.CreateAsync();
        Require((await fixture.PublishAsync()).Outcome == DesktopWorkspaceRoamingOutcome.Applied,
            "Remote deletion fixture did not first establish a confirmed remote base.");
        fixture.Remote = null;
        fixture.ChangeLocal("Local edits after remote deletion");
        string before = fixture.LocalBytes();
        var result = await fixture.PublishAsync();
        Require(result.Outcome is DesktopWorkspaceRoamingOutcome.Conflict or DesktopWorkspaceRoamingOutcome.Unavailable
            && fixture.Upserts == 1 && fixture.Remote is null && fixture.LocalBytes() == before,
            "A disappeared confirmed remote row was silently resurrected by missing-only creation.");
        Console.WriteLine("PASS roaming refuses to recreate a disappeared confirmed remote row");
    }

    private static async Task RunRoamingUnknownRestartAsync()
    {
        using var fixture = await RoamingFixture.CreateAsync();
        fixture.DropNextWriteResponse = true;
        fixture.CommitDroppedWrite = false;
        Require((await fixture.PublishAsync()).Outcome == DesktopWorkspaceRoamingOutcome.Unavailable
            && fixture.Upserts == 1 && fixture.Remote is null,
            "Durable uncertainty fixture did not actually lose an initial HTTP write outcome.");
        foreach (bool replaceAccountEpoch in new[] { false, true })
        {
            if (replaceAccountEpoch)
            {
                await fixture.Account.LinkAsync("roaming-restart-B", "roaming-restart-B");
                await fixture.Account.LinkAsync("subject", "roaming-restart-A2");
            }
            OwnerContextStamp current = fixture.Account.Owner.Capture();
            var recreated = new AndroidWorkspaceContinuationRoamingSync(fixture.Owners,
                new WorkspaceContinuationExportService(new FileWorkspaceStore(fixture.Directory), fixture.Owners),
                fixture.Account.Transport, fixture.Directory);
            var result = await recreated.SynchronizeOutboundAsync(current, fixture.Id, default);
            Require(result.Outcome is DesktopWorkspaceRoamingOutcome.Conflict or DesktopWorkspaceRoamingOutcome.Unavailable
                && fixture.Upserts == 1 && fixture.Remote is null,
                "Losing transient bases or replacing the account epoch erased the durable uncertain-publish marker.");
        }
        Console.WriteLine("PASS durable unknown-publish protection survives adapter reconstruction and real account epoch replacement");
    }

    private sealed class RoamingFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = System.IO.Directory.CreateTempSubdirectory("android-continuation-roaming-");
        internal readonly ContinuationAccountFixture Account = new();
        internal readonly CharacterWorkspaceId Id = new("actual-roaming-runner");
        internal string Directory => _directory.FullName;
        internal FileWorkspaceStore Store { get; }
        internal RoamingOwnerObserver Owners { get; }
        internal OwnerContextStamp Stamp { get; private set; }
        internal WorkspaceContinuationExportService Exporter { get; }
        internal AndroidWorkspaceContinuationRoamingSync Sync { get; }
        internal JsonObject? Remote;
        internal int Upserts;
        internal readonly List<(long Revision, string? Token)> Preconditions = new();
        internal Action? AfterListCapture;
        internal bool DropNextWriteResponse;
        internal bool CommitDroppedWrite;
        internal long RemoteRevision => Remote?["remoteRevision"]!.GetValue<long>() ?? 0;
        internal string? RemoteToken => Remote?["serverToken"]!.GetValue<string>();
        internal string? RemoteDigest => Remote?["workspaceContinuationDigest"]!.GetValue<string>();

        private RoamingFixture()
        {
            Store = new FileWorkspaceStore(Directory);
            Owners = new(Account.Owner);
            Exporter = new(Store, Owners);
            Sync = new(Owners, Exporter, Account.Transport, Directory);
            Account.ListResponse = () =>
            {
                var rows = new JsonArray();
                if (Remote is not null) rows.Add(Remote.DeepClone());
                HttpResponseMessage captured = ContinuationJsonResponse(new JsonObject { ["snapshots"] = rows });
                Action? race = AfterListCapture;
                AfterListCapture = null;
                race?.Invoke();
                return captured;
            };
            Account.UpsertResponse = _ =>
            {
                Upserts++;
                JsonObject body = Account.LastWrite!;
                long expected = body["expectedRemoteRevision"]!.GetValue<long>();
                string? token = body["expectedServerToken"]?.GetValue<string>();
                Preconditions.Add((expected, token));
                if (expected != RemoteRevision || token != RemoteToken)
                    return new(HttpStatusCode.Conflict) { Content = new StringContent("{}") };
                bool drop = DropNextWriteResponse;
                DropNextWriteResponse = false;
                if (!drop || CommitDroppedWrite)
                {
                    JsonObject row = (JsonObject)body.DeepClone();
                    row["summary"] = new JsonObject();
                    row["remoteRevision"] = RemoteRevision + 1;
                    row["serverToken"] = RoamingServerToken(RemoteRevision + 1, Upserts);
                    Remote = row;
                }
                if (drop) throw new HttpRequestException("Simulated lost roaming response after possible server commit.");
                return ContinuationJsonResponse(new JsonObject { ["snapshot"] = Remote!.DeepClone() });
            };
        }

        internal static async Task<RoamingFixture> CreateAsync()
        {
            var fixture = new RoamingFixture();
            try
            {
                await fixture.Account.LinkAsync("subject", "roaming-grant-A");
                fixture.Stamp = fixture.Account.Owner.Capture();
                Require(fixture.Store.CreateWorkspaceDocument(fixture.Stamp.Owner, fixture.Id,
                        new WorkspaceDocument("<character><name>Actual file-backed roaming runner</name></character>", "sr5")).Success
                    && fixture.Store.SaveCheckpoint(fixture.Stamp.Owner, fixture.Id, 1).Success,
                    "The real owner-scoped FileWorkspaceStore could not create the roaming fixture.");
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        internal Task<DesktopWorkspaceRoamingResult> PublishAsync()
            => Sync.SynchronizeOutboundAsync(Stamp, Id, default).WaitAsync(TimeSpan.FromSeconds(10));

        internal WorkspaceContinuationExport Export()
        {
            var result = Exporter.Export(Stamp, Id);
            Require(result.Success && result.Value is not null, "The actual owner-leased Core continuation export failed.");
            return result.Value!;
        }

        internal void ChangeLocal(string name)
        {
            var read = Store.Get(Stamp.Owner, Id);
            Require(read.Success && Store.ReplaceWorkspaceDocument(Stamp.Owner, Id, read.Value!.ContentRevision,
                read.Value.Document with { State = read.Value.Document.State with
                    { Payload = "<character><name>" + name + "</name></character>" } }).Success,
                "The real FileWorkspaceStore did not commit the local revision used by the roaming test.");
        }

        internal void SetRemote(WorkspaceContinuationExport full)
        {
            long next = RemoteRevision + 1;
            JsonObject row = ContinuationRow(full);
            row["remoteRevision"] = next;
            row["serverToken"] = RoamingServerToken(next, Upserts + 100);
            Remote = row;
        }

        internal void AssertRemoteEqualsActualExport()
        {
            Require(Remote is not null && WorkspaceContinuationCodec.TryDecodeCandidate(
                    Encoding.UTF8.GetBytes(Remote["workspaceContinuation"]!.ToJsonString()), 512 * 1024, out var transported)
                && WorkspaceContinuationCodec.Encode(transported, 512 * 1024)
                    .SequenceEqual(WorkspaceContinuationCodec.Encode(Export(), 512 * 1024)),
                "The roaming adapter did not transmit the exact current real Core export.");
        }

        internal string LocalBytes() => JsonSerializer.Serialize(new FileWorkspaceStore(Directory).Get(Stamp.Owner, Id).Value);
        public void Dispose() { Account.Dispose(); _directory.Delete(recursive: true); }
        private static string RoamingServerToken(long revision, int sequence)
            => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"roaming-test:{revision}:{sequence}")));
    }

    // Observation only: admission and thread-affine exclusion still come from
    // the real Android account credential authority used by the HTTP transport.
    private sealed class RoamingOwnerObserver(IOwnerContextLeaseAccessor inner) : IOwnerContextLeaseAccessor
    {
        internal int Attempts;
        internal int Active;
        public OwnerScope Current => inner.Current;
        public OwnerContextStamp Capture() => inner.Capture();
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Attempts++;
            lease = null;
            if (!inner.TryAcquire(expected, out var actual)) return false;
            Interlocked.Increment(ref Active);
            lease = new ObservedLease(actual, this);
            return true;
        }
        private sealed class ObservedLease(IOwnerContextLease actual, RoamingOwnerObserver owner) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => actual.Stamp;
            public void Dispose()
            {
                if (_disposed) return;
                actual.Dispose();
                _disposed = true;
                Interlocked.Decrement(ref owner.Active);
            }
        }
    }
}
