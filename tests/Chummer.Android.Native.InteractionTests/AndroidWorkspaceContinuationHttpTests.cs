using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunAndroidContinuationTransportCasesAsync()
    {
        await RunContinuationRoundTripAsync();
        await RunContinuationMalformedRowsAsync();
        await RunContinuationConflictAndUnknownAsync();
        await RunContinuationOwnerWaitsAsync();
        await RunContinuationBoundsAsync();
    }

    private static async Task RunContinuationRoundTripAsync()
    {
        using var fixture = new ContinuationAccountFixture();
        await fixture.LinkAsync("subject", "continuation-grant");
        OwnerContextStamp stamp = fixture.Owner.Capture();
        Require(stamp.Owner.NormalizedValue == "install-account-v1:32fc53336d923859934f26f4dfbcddc3cf226ad874aed338112961467fdd8748",
            "The actual Android credential authority disagrees with the fixed Hub interoperability vector.");
        WorkspaceContinuationExport full = ContinuationFixture(stamp);
        var result = await fixture.Transport.UpsertContinuationAsync(stamp, full, 0, null);
        Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Applied
            && result.RemoteRevision == 1 && result.ServerToken == new string('b', 64)
            && !result.UnknownRemoteOutcome && fixture.ContinuationRequests == 1,
            "The signed full-continuation request did not yield the exact CAS receipt.");
        JsonObject written = fixture.LastWrite!;
        Require(written["workspaceContinuation"]!["Snapshot"]!["Workspace"]!["SavedRevision"]!.GetValue<long>() == 5
            && written["workspaceContinuation"]!["Snapshot"]!["DelegatedGmHistorySegmentStarts"]!.AsArray().Count == 1
            && written["workspaceContinuation"]!["Snapshot"]!["DelegatedGmCharacterEdits"]!.AsArray().Count == 2,
            "The signed carrier dropped private receipts, grouping metadata or dirty checkpoint revisions.");
        var row = ContinuationRow(full);
        var legacy = (JsonObject)row.DeepClone();
        legacy["workspaceId"] = "legacy-runner";
        legacy.Remove("workspaceContinuation");
        legacy.Remove("workspaceContinuationDigest");
        fixture.ListRows = new JsonArray(legacy, row);
        IReadOnlyList<AndroidWorkspaceContinuationItem> listed = await fixture.Transport.ListContinuationsAsync(stamp);
        Require(listed.Count == 2 && listed.Single(item => item.Character.WorkspaceId == "legacy-runner") is
            { Continuation: null, RemoteRevision: null, ServerToken: null, UnavailableReason: "complete-continuation-unavailable" },
            "A legacy runner was hidden or silently promoted to a full restore candidate.");
        WorkspaceContinuationExport returned = listed.Single(item => item.Continuation is not null).Continuation!;
        Require(WorkspaceContinuationCodec.Encode(returned, 512 * 1024)
                .SequenceEqual(WorkspaceContinuationCodec.Encode(full, 512 * 1024)),
            "The actual account HTTP roundtrip changed full history, dictionary keys, revisions or timestamp.");
        // The upsert response communicates no-op through the unchanged CAS pair.
        fixture.UpsertResponse = _ => ContinuationJsonResponse(new JsonObject { ["snapshot"] = ContinuationRow(full) });
        result = await fixture.Transport.UpsertContinuationAsync(stamp, full, 1, new string('b', 64));
        Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.AlreadyCurrent && !result.UnknownRemoteOutcome,
            "An exact unchanged server CAS pair was not recognized as a no-op.");
        Console.WriteLine("PASS actual signed continuation roundtrip, full GM/aux history, opaque bytes and explicit legacy-unavailable row");
    }

    private static async Task RunContinuationMalformedRowsAsync()
    {
        using var fixture = new ContinuationAccountFixture();
        await fixture.LinkAsync("subject", "continuation-malformed");
        OwnerContextStamp stamp = fixture.Owner.Capture();
        WorkspaceContinuationExport full = ContinuationFixture(stamp);
        var attacks = new (string Name, Action<JsonObject> Change)[]
        {
            ("outer digest", row => row["workspaceContinuationDigest"] = new string('0', 64)),
            ("inner digest", row => row["workspaceContinuation"]!["SnapshotDigest"] = new string('0', 64)),
            ("payload projection", row => row["payload"] = "<character><name>other</name></character>"),
            ("workspace projection", row => row["workspaceId"] = "other-runner"),
            ("schema projection", row => row["schemaVersion"] = 2),
            ("timestamp offset", row => row["updatedAtUtc"] = full.Snapshot.Workspace.LastUpdatedUtc.ToOffset(TimeSpan.FromHours(1))),
            ("saved revision", row => row["workspaceContinuation"]!["Snapshot"]!["Workspace"]!["SavedRevision"] = 6),
            ("unknown full member", row => row["workspaceContinuation"]!["Snapshot"]!["unrecognized"] = 7),
            ("missing paired envelope", row => row.Remove("workspaceContinuation")),
            ("missing CAS token", row => row.Remove("serverToken")),
            ("invalid CAS revision", row => row["remoteRevision"] = 0),
            ("foreign owner", row =>
            {
                var foreign = full.Snapshot with { OwnerId = "foreign-owner" };
                var export = new WorkspaceContinuationExport(foreign, WorkspaceContinuationSnapshotDigest.Compute(foreign));
                row["workspaceContinuation"] = JsonNode.Parse(WorkspaceContinuationCodec.Encode(export, 512 * 1024));
                row["workspaceContinuationDigest"] = export.SnapshotDigest;
            })
        };
        foreach ((string name, Action<JsonObject> change) in attacks)
        {
            JsonObject row = ContinuationRow(full);
            change(row);
            // A valid neighbour cannot cause corrupt full history to be skipped.
            fixture.ListRows = new JsonArray(ContinuationRow(ContinuationFixture(stamp, "valid-neighbour")), row);
            await RequireContinuationListRejectedAsync(fixture, stamp, name);
        }
        string valid = new JsonObject { ["snapshots"] = new JsonArray(ContinuationRow(full)) }.ToJsonString();
        string duplicate = valid.Replace("\"SnapshotDigest\":", "\"SnapshotDigest\":\"" + full.SnapshotDigest + "\",\"SnapshotDigest\":", StringComparison.Ordinal);
        Require(duplicate != valid, "The duplicate-property adversarial fixture did not change its input.");
        fixture.ListResponse = () => new(HttpStatusCode.OK) { Content = new StringContent(duplicate, Encoding.UTF8, "application/json") };
        await RequireContinuationListRejectedAsync(fixture, stamp, "duplicate full property");
        Require(fixture.Owner.Capture() == stamp && fixture.Account.Snapshot.IsLinked,
            "A malformed continuation response changed authenticated account ownership.");
        Console.WriteLine("PASS actual continuation list rejects 13 malformed full-carrier cases without partial corruption skipping");
    }

    private static async Task RunContinuationConflictAndUnknownAsync()
    {
        using var fixture = new ContinuationAccountFixture();
        await fixture.LinkAsync("subject", "continuation-conflicts");
        OwnerContextStamp stamp = fixture.Owner.Capture();
        WorkspaceContinuationExport full = ContinuationFixture(stamp);
        string credentialBefore = JsonSerializer.Serialize(fixture.Metadata.Inner.Rows.OrderBy(pair => pair.Key));
        foreach (int status in new[] { 409, 428 })
        {
            fixture.UpsertResponse = _ => new((HttpStatusCode)status) { Content = new StringContent("{}") };
            var result = await fixture.Transport.UpsertContinuationAsync(stamp, full, 1, new string('a', 64));
            Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Conflict && !result.UnknownRemoteOutcome
                && fixture.Owner.Capture() == stamp && fixture.Account.Snapshot.IsLinked
                && credentialBefore == JsonSerializer.Serialize(fixture.Metadata.Inner.Rows.OrderBy(pair => pair.Key)),
                "A remote CAS conflict cleared or rotated the actual grant.");
        }
        fixture.UpsertResponse = _ => throw new HttpRequestException("Simulated connection loss after possible commit.");
        int before = fixture.ContinuationRequests;
        var unknown = await fixture.Transport.UpsertContinuationAsync(stamp, full, 1, new string('a', 64));
        Require(unknown.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unavailable && unknown.UnknownRemoteOutcome
            && fixture.ContinuationRequests == before + 1 && fixture.Owner.Capture() == stamp,
            "An unknown remote commit was retried or mistaken for a definite rejection.");
        fixture.UpsertResponse = _ => ContinuationJsonResponse(new JsonObject { ["snapshot"] = new JsonObject() });
        unknown = await fixture.Transport.UpsertContinuationAsync(stamp, full, 1, new string('a', 64));
        Require(unknown.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unavailable && unknown.UnknownRemoteOutcome,
            "A malformed success response incorrectly granted a safe mutation retry.");
        Console.WriteLine("PASS actual continuation 409/428 preserve grants; lost or malformed write outcomes never replay");
    }

    private static async Task RunContinuationOwnerWaitsAsync()
    {
        using (var fixture = new ContinuationAccountFixture())
        {
            await fixture.LinkAsync("subject", "continuation-owner-A");
            OwnerContextStamp original = fixture.Owner.Capture();
            WorkspaceContinuationExport full = ContinuationFixture(original);
            await fixture.LinkAsync("other-subject", "continuation-owner-B");
            await fixture.LinkAsync("subject", "continuation-owner-A2");
            var result = await fixture.Transport.UpsertContinuationAsync(original, full, 0, null);
            Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unauthorized
                && !result.UnknownRemoteOutcome && fixture.ContinuationRequests == 0,
                "A queued original owner stamp survived an actual account A-to-B-to-A transition.");
        }
        using (var fixture = new ContinuationAccountFixture())
        {
            await fixture.LinkAsync("subject", "continuation-signing-A");
            OwnerContextStamp original = fixture.Owner.Capture();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Keys.BeforeRelease = async () => { fixture.Keys.BeforeRelease = null; started.SetResult(); await release.Task; };
            Task<AndroidWorkspaceContinuationWriteResult> pending = fixture.Transport.UpsertContinuationAsync(original,
                ContinuationFixture(original), 0, null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // This must finish while the hardware signer is suspended; retaining
            // the non-reentrant credential lease across await would deadlock.
            await fixture.LinkAsync("other-subject", "continuation-signing-B").WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unauthorized
                && !result.UnknownRemoteOutcome && fixture.ContinuationRequests == 0,
                "A completed old-key signature escaped after the real account changed.");
        }
        using (var fixture = new ContinuationAccountFixture())
        {
            await fixture.LinkAsync("subject", "continuation-storage-A");
            OwnerContextStamp original = fixture.Owner.Capture();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Metadata.BeforeRead = async key =>
            {
                if (key != "chummer.account.installation-grant.v1") return;
                fixture.Metadata.BeforeRead = null;
                started.SetResult();
                await release.Task;
            };
            Task<AndroidWorkspaceContinuationWriteResult> pending = fixture.Transport.UpsertContinuationAsync(original,
                ContinuationFixture(original), 0, null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Clock.Now = fixture.Clock.Now.AddDays(31);
            release.SetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unauthorized
                && !result.UnknownRemoteOutcome && fixture.ContinuationRequests == 0,
                "An owner which expired during the secure-store await dispatched a packet.");
        }
        foreach (bool write in new[] { false, true })
        {
            using var fixture = new ContinuationAccountFixture();
            await fixture.LinkAsync("subject", "continuation-http-A");
            OwnerContextStamp original = fixture.Owner.Capture();
            WorkspaceContinuationExport full = ContinuationFixture(original);
            fixture.ListRows = new JsonArray(ContinuationRow(full));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.BeforeHttpResponse = async () => { started.SetResult(); await release.Task; };
            Task pending = write
                ? fixture.Transport.UpsertContinuationAsync(original, full, 0, null)
                : fixture.Transport.ListContinuationsAsync(original);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.LinkAsync("other-subject", "continuation-http-B").WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            if (write)
            {
                var result = await ((Task<AndroidWorkspaceContinuationWriteResult>)pending).WaitAsync(TimeSpan.FromSeconds(5));
                Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unauthorized && result.UnknownRemoteOutcome,
                    "A possible old-owner commit was reported as authorized or safely retryable under the new owner.");
            }
            else
            {
                bool rejected = false;
                try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (UnauthorizedAccessException) { rejected = true; }
                Require(rejected, "An old-owner list response was returned after the actual account changed.");
            }
        }
        using (var fixture = new ContinuationAccountFixture())
        {
            await fixture.LinkAsync("subject", "continuation-owned-capture");
            OwnerContextStamp original = fixture.Owner.Capture();
            WorkspaceContinuationExport full = ContinuationFixture(original);
            byte[] before = WorkspaceContinuationCodec.Encode(full, 512 * 1024);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Metadata.BeforeRead = async key =>
            {
                if (key != "chummer.account.installation-grant.v1") return;
                fixture.Metadata.BeforeRead = null;
                started.SetResult();
                await release.Task;
            };
            Task<AndroidWorkspaceContinuationWriteResult> pending = fixture.Transport.UpsertContinuationAsync(original, full, 0, null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ((Dictionary<string, string>)full.Snapshot.Workspace.Document.AuxiliaryState
                .CharacterCreationFoundationDraft!.FollowUpValues)["story"] = "Changed after capture";
            release.SetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            byte[] sent = Encoding.UTF8.GetBytes(fixture.LastWrite!["workspaceContinuation"]!.ToJsonString());
            Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Applied
                && WorkspaceContinuationCodec.TryDecodeCandidate(sent, 512 * 1024, out var captured)
                && WorkspaceContinuationCodec.Encode(captured, 512 * 1024).SequenceEqual(before),
                "Caller-owned auxiliary history was reread after secure-store capture or not independently owned.");
        }
        Console.WriteLine("PASS actual continuation owner ABA, secure-store expiry, signer and list/write HTTP transition fences");
    }

    private static async Task RunContinuationBoundsAsync()
    {
        using var fixture = new ContinuationAccountFixture();
        await fixture.LinkAsync("subject", "continuation-bounds");
        OwnerContextStamp stamp = fixture.Owner.Capture();
        WorkspaceContinuationExport full = ContinuationFixture(stamp);
        var oversized = full.Snapshot with { Workspace = full.Snapshot.Workspace with
        {
            Document = full.Snapshot.Workspace.Document with { State = full.Snapshot.Workspace.Document.State with
                { Payload = new string('x', 512 * 1024) } }
        } };
        var result = await fixture.Transport.UpsertContinuationAsync(stamp,
            new(oversized, WorkspaceContinuationSnapshotDigest.Compute(oversized)), 0, null);
        Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unavailable && !result.UnknownRemoteOutcome
            && fixture.ContinuationRequests == 0, "An oversized continuation reached the HTTP writer or omitted its history.");
        var combined = oversized with { Workspace = oversized.Workspace with
        {
            Document = oversized.Workspace.Document with { State = oversized.Workspace.Document.State with
                { Payload = new string('x', 400 * 1024) } }
        } };
        var combinedExport = new WorkspaceContinuationExport(combined, WorkspaceContinuationSnapshotDigest.Compute(combined));
        Require(WorkspaceContinuationCodec.Encode(combinedExport, 512 * 1024).Length < 512 * 1024,
            "The combined-body fixture must fit the independent full-envelope limit.");
        result = await fixture.Transport.UpsertContinuationAsync(stamp, combinedExport, 0, null);
        Require(result.Outcome == AndroidWorkspaceContinuationWriteOutcome.Unavailable && !result.UnknownRemoteOutcome
            && fixture.ContinuationRequests == 0, "The duplicated public payload bypassed the complete request byte cap.");
        fixture.ListRows = new JsonArray(Enumerable.Range(0, 201).Select(_ => (JsonNode)ContinuationRow(full)).ToArray());
        await RequireContinuationListRejectedAsync(fixture, stamp, "row cap");
        fixture.ListResponse = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"snapshots\":[]}") };
            response.Content.Headers.ContentLength = 16L * 1024 * 1024 + 1;
            return response;
        };
        await RequireContinuationListRejectedAsync(fixture, stamp, "aggregate byte cap");
        string deep = "{\"snapshots\":[],\"untrusted\":" + new string('[', 65) + "0" + new string(']', 65) + "}";
        fixture.ListResponse = () => new(HttpStatusCode.OK) { Content = new StringContent(deep) };
        await RequireContinuationListRejectedAsync(fixture, stamp, "HTTP depth cap");
        Console.WriteLine("PASS actual continuation per-envelope, aggregate response, row-count and depth bounds fail closed");
    }

    private static async Task RequireContinuationListRejectedAsync(ContinuationAccountFixture fixture,
        OwnerContextStamp stamp, string reason)
    {
        bool rejected = false;
        try { await fixture.Transport.ListContinuationsAsync(stamp); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "The actual continuation transport accepted " + reason + ".");
    }

    private static WorkspaceContinuationExport ContinuationFixture(OwnerContextStamp owner,
        string workspaceId = "continuation-runner")
    {
        string ownerId = owner.Owner.NormalizedValue;
        var id = new CharacterWorkspaceId(workspaceId);
        var draft = new CharacterCreationFoundationDraftLedger("unverified-transport-fixture/v1", id, 3, 4,
            new string('a', 64), new string('b', 64), "Human", new("street", "v1"), [], [],
            new Dictionary<string, string> { ["story"] = "Straßenkind — live choice", ["Story"] = "distinct key" },
            ["anchor"], "review", false, new string('c', 64));
        var auxiliary = new WorkspaceDocumentAuxiliaryState(CharacterCreationFoundationDraft: draft,
            CharacterCreationFinalizationArchive: new(new(CharacterCreationFoundationDraft: draft)));
        var workspace = new WorkspaceDocumentSnapshot(id, new(new WorkspaceDocumentState("sr5", 1, "workspace",
            "<character><name>Runner</name></character>") { AuxiliaryState = auxiliary }),
            new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567), 7, 5);
        // Synthetic historical claims test transport fidelity only; neither
        // their digest nor successful HTTP delivery is semantic restore admission.
        var receipts = Enumerable.Range(1, 2).Select(index => new DelegatedGmCharacterEditAuditReceipt(
            DelegatedGmCharacterEditContract.Name, "unverified-" + index, "campaign", "delegation", "gm-owner", ownerId,
            "authority", index, "gm", DelegatedGmCharacterEditContract.GameMasterRole, ownerId, id,
            "Unverified history", new string('a', 64), new string('b', 64), index, index + 1,
            workspace.LastUpdatedUtc, [new(DelegatedGmCharacterPatchOperationKind.Replace,
                DelegatedGmCharacterEditContract.ProfileNotesPath, new string('c', 64), 3)])).ToArray();
        var snapshot = new WorkspaceContinuationSnapshot(ownerId, workspace, receipts) { DelegatedGmHistorySegmentStarts = [1] };
        return new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot));
    }

    private static JsonObject ContinuationRow(WorkspaceContinuationExport full)
    {
        WorkspaceDocumentSnapshot workspace = full.Snapshot.Workspace;
        return new()
        {
            ["workspaceId"] = workspace.Id.Value, ["rulesetId"] = workspace.Document.RulesetId,
            ["format"] = "NativeXml", ["schemaVersion"] = workspace.Document.SchemaVersion,
            ["payloadKind"] = workspace.Document.PayloadKind, ["payload"] = workspace.Document.Content,
            ["updatedAtUtc"] = workspace.LastUpdatedUtc,
            ["summary"] = new JsonObject { ["name"] = "Runner", ["alias"] = "R", ["metatype"] = "Human" },
            ["remoteRevision"] = 1, ["serverToken"] = new string('b', 64),
            ["workspaceContinuation"] = JsonNode.Parse(WorkspaceContinuationCodec.Encode(full, 512 * 1024)),
            ["workspaceContinuationDigest"] = full.SnapshotDigest
        };
    }

    private static HttpResponseMessage ContinuationJsonResponse(JsonNode body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private sealed class ContinuationAccountFixture : IDisposable
    {
        private readonly IDisposable _http;
        internal readonly ContinuationMetadata Metadata = new();
        internal readonly ContinuationKeys Keys = new();
        internal readonly AccountClock Clock = new();
        internal AndroidAccountLinkService Account { get; }
        internal AndroidAccountOwnerContextAccessor Owner { get; }
        internal IAndroidWorkspaceContinuationTransport Transport => Account;
        internal JsonArray ListRows = new();
        internal JsonObject? LastWrite;
        internal int ContinuationRequests;
        internal Func<HttpRequestMessage, HttpResponseMessage>? UpsertResponse;
        internal Func<HttpResponseMessage>? ListResponse;
        internal Func<Task>? BeforeHttpResponse;
        private string _subject = "subject";
        private string _grant = "continuation-grant";

        internal ContinuationAccountFixture()
        {
            var keyAuthority = new AndroidAccountLinkKeyAuthority(Keys, Metadata);
            Type httpType = typeof(AndroidAccountLinkService).Assembly.GetType(
                "Chummer.Android.Platform.AndroidAccountLinkHttpTransport", throwOnError: true)!;
            object http = Activator.CreateInstance(httpType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object?[] { new AccountHandler(RespondAsync), TimeSpan.FromSeconds(10) }, null)!;
            _http = (IDisposable)http;
            Account = (AndroidAccountLinkService)Activator.CreateInstance(typeof(AndroidAccountLinkService),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { http, new AccountSystem(),
                    keyAuthority, Metadata, (Func<string>)(() => "continuation-test"),
                    (Func<string>)(() => "test-only"), (Func<string>)(() => "x64"), Clock }, null)!;
            Owner = new(Account);
        }

        internal async Task LinkAsync(string subject, string grant)
        {
            _subject = subject;
            _grant = grant;
            await Account.BeginLinkAsync();
            await Account.ResumePendingLinkAsync();
            Require(Account.Snapshot.IsLinked, "Actual continuation fixture account linking failed.");
        }

        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            byte[] bytes = await request.Content!.ReadAsByteArrayAsync(token);
            JsonObject body = JsonNode.Parse(bytes)!.AsObject();
            if (path.Contains("/continuation/workspaces/", StringComparison.Ordinal))
            {
                ContinuationRequests++;
                Require(request.Headers.Authorization?.Scheme == "Bearer"
                    && request.Headers.Authorization.Parameter == "continuation-test-token"
                    && !Encoding.UTF8.GetString(bytes).Contains("continuation-test-token", StringComparison.Ordinal)
                    && body.All(property => !property.Key.Equals("accessToken", StringComparison.OrdinalIgnoreCase)),
                    "The real continuation packet omitted its header credential or leaked it into the body.");
                string Header(string name) => request.Headers.GetValues(name).Single();
                string canonical = string.Join('\n', "chummer.android.packet.v2", "POST", path,
                    Header("X-Chummer-Installation"), Header("X-Chummer-Grant"), Header("X-Chummer-Packet-Issued"),
                    Header("X-Chummer-Packet-Key"), "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)));
                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Keys.PublicKey!), out _);
                Require(rsa.VerifyData(Encoding.UTF8.GetBytes(canonical),
                    Convert.FromBase64String(Header("X-Chummer-Packet-Signature")), HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1), "The real continuation HTTP body was not covered by the device signature.");
                if (BeforeHttpResponse is not null) await BeforeHttpResponse();
                if (path.EndsWith("/list", StringComparison.Ordinal))
                    return ListResponse?.Invoke() ?? ContinuationJsonResponse(new JsonObject { ["snapshots"] = ListRows.DeepClone() });
                LastWrite = body;
                if (UpsertResponse is not null) return UpsertResponse(request);
                JsonObject row = (JsonObject)body.DeepClone();
                row["summary"] = new JsonObject();
                row["remoteRevision"] = body["expectedRemoteRevision"]!.GetValue<long>() + 1;
                row["serverToken"] = new string('b', 64);
                return ContinuationJsonResponse(new JsonObject { ["snapshot"] = row });
            }
            if (path.EndsWith("/revoke", StringComparison.Ordinal)) return ContinuationJsonResponse(new JsonObject());
            string installation = body["installationId"]!.GetValue<string>();
            string operation = body["operationId"]!.GetValue<string>();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    installation = new { installationId = installation, grantId = _grant, subjectId = _subject, status = "active" },
                    grant = new { installationId = installation, grantId = _grant, status = "active",
                        issuedAtUtc = DateTimeOffset.UtcNow, expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) },
                    alreadyClaimed = false, rotated = true, operationId = operation, grantTransport = "android-linked-v2"
                }), Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("Authorization", "Bearer continuation-test-token");
            response.Headers.TryAddWithoutValidation("X-Chummer-Grant", _grant);
            return response;
        }

        public void Dispose() { _http.Dispose(); Keys.Dispose(); }
    }

    private sealed class ContinuationMetadata : IAndroidAccountLinkKeyMetadataStore
    {
        internal readonly AccountMetadata Inner = new();
        internal Func<string, Task>? BeforeRead;
        public async Task<string?> GetAsync(string key, CancellationToken token = default)
        { if (BeforeRead is not null) await BeforeRead(key); return await Inner.GetAsync(key, token); }
        public Task SetAsync(string key, string value, CancellationToken token = default) => Inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token = default) => Inner.RemoveAsync(key, token);
    }

    private sealed class ContinuationKeys : IAndroidDeviceKeyStore, IDisposable
    {
        private readonly AccountDeviceKeys _inner = new();
        internal string? PublicKey;
        internal Func<Task>? BeforeRelease;
        public async Task<AndroidDevicePublicKey> CreateAsync(string alias, CancellationToken token = default)
        { var key = await _inner.CreateAsync(alias, token); PublicKey = key.PublicKey; return key; }
        public async Task<AndroidDevicePublicKey> GetPublicKeyAsync(string alias, CancellationToken token = default)
        { var key = await _inner.GetPublicKeyAsync(alias, token); PublicKey = key.PublicKey; return key; }
        public async Task<byte[]> SignAsync(string alias, ReadOnlyMemory<byte> payload, CancellationToken token = default)
        { byte[] signature = await _inner.SignAsync(alias, payload, token); if (BeforeRelease is not null) await BeforeRelease(); return signature; }
        public Task DeleteAsync(string alias, CancellationToken token = default) => _inner.DeleteAsync(alias, token);
        public void Dispose() => _inner.Dispose();
    }
}
