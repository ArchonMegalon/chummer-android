using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunAndroidContinuationNativeCasesAsync(string contentRoot)
    {
        await RunNativeContinuationCanceledAsync(contentRoot);
        await RunNativeContinuationRestoreAsync(contentRoot);
        await RunNativeContinuationLegacyAsync(contentRoot);
        foreach (bool returnToOwner in new[] { false, true })
            await RunNativeContinuationDialogOwnerAsync(contentRoot, returnToOwner);
        foreach (bool cancel in new[] { false, true })
            await RunNativeContinuationPostcommitAsync(contentRoot, cancel);
        await RunNativeContinuationActivationOwnerAsync(contentRoot);
        Console.WriteLine("PASS 8 actual native/Core continuation review, restore, receipt and owner cases");
    }

    private static async Task RunNativeContinuationCanceledAsync(string contentRoot)
    {
        await using var fixture = await NativeContinuationFixture.CreateAsync(contentRoot);
        using var review = await fixture.ReviewAsync();
        fixture.AssertNoTarget();
        var canceled = await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, explicitlyConfirmed: false);
        Require(canceled.Restore.Outcome == WorkspaceContinuationRestoreOutcome.Canceled
            && canceled.Restore.Receipt is null && canceled.Activation is null && !review.CanConfirm,
            "An unconfirmed native continuation review mutated state or retained a reusable admission.");
        fixture.AssertNoTarget();
        var reused = await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, explicitlyConfirmed: true);
        Require(reused.Restore.Outcome == WorkspaceContinuationRestoreOutcome.ReviewConsumed,
            "A canceled native review could be reused to restore the workspace.");
        fixture.AssertNoTarget();
        Console.WriteLine("PASS actual native continuation review/cancel remains read-only and consumes its opaque handle");
    }

    private static async Task RunNativeContinuationRestoreAsync(string contentRoot)
    {
        await using var fixture = await NativeContinuationFixture.CreateAsync(contentRoot);
        using var review = await fixture.ReviewAsync();
        var restored = await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, explicitlyConfirmed: true);
        fixture.AssertRestored(restored.Restore);
        Require(restored.Activation is not null
            && fixture.Runtime.Coordinator.IsWorkspaceActivationCurrent(restored.Activation, NativeWorkspaceActivationKind.OnlineCharacter)
            && fixture.Runtime.Coordinator.State.ContentRevision == 2
            && fixture.Runtime.Coordinator.State.SavedRevision == 1,
            "The actual native/Core restore did not activate the exact owner-scoped dirty Bootstrap workspace.");
        string after = fixture.TargetBytes();
        var reused = await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, explicitlyConfirmed: true);
        Require(reused.Restore.Outcome == WorkspaceContinuationRestoreOutcome.ReviewConsumed
            && fixture.TargetBytes() == after, "A consumed native handle repeated the canonical restore.");
        Console.WriteLine("PASS actual native owner-scoped restore preserves full Bootstrap auxiliary state, dirty revisions and exact snapshot identity");
    }

    private static async Task RunNativeContinuationLegacyAsync(string contentRoot)
    {
        await using var fixture = await NativeContinuationFixture.CreateAsync(contentRoot);
        var coordinator = fixture.Runtime.Coordinator;
        AndroidOnlineCharacter legacy = coordinator.OnlineCharacters.Single(item => item.WorkspaceId == "native-legacy-runner");
        Require(coordinator.OnlineCharacters.Count == 2
            && !coordinator.HasCompleteOnlineContinuation(legacy)
            && coordinator.HasCompleteOnlineContinuation(fixture.Character),
            "The actual catalog hid legacy rows or enabled their XML as a complete restore source.");
        Require(await coordinator.ReviewOnlineAsync(legacy) is null
            && await coordinator.OpenOnlineAsync(legacy) is null,
            "A legacy XML-only row reached either review admission or the retired automatic importer.");
        fixture.AssertNoTarget();
        Require(new FileWorkspaceStore(fixture.Runtime.StateDirectory).List(fixture.Owner.Owner).Count == 0,
            "The legacy compatibility entry point imported a different workspace identity.");
        Console.WriteLine("PASS actual signed catalog shows but disables XML-only rows; old online import is non-mutating");
    }

    private static async Task RunNativeContinuationDialogOwnerAsync(string contentRoot, bool returnToOwner)
    {
        await using var fixture = await NativeContinuationFixture.CreateAsync(contentRoot);
        using var review = await fixture.ReviewAsync();
        await fixture.Account.LinkAsync("native-continuation-B", "native-continuation-B");
        if (returnToOwner) await fixture.Account.LinkAsync("subject", "native-continuation-A2");
        var result = await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, explicitlyConfirmed: true);
        Require(result.Restore.Outcome == WorkspaceContinuationRestoreOutcome.Conflict && result.Restore.Receipt is null
            && result.Activation is null && !fixture.Runtime.Coordinator.HasCompleteOnlineContinuation(fixture.Character),
            "A real account change during the confirmation dialog retained an old native restore admission.");
        fixture.AssertNoTarget();
        var current = fixture.Account.Owner.Capture();
        Require(!new FileWorkspaceStore(fixture.Runtime.StateDirectory).Get(current.Owner, fixture.Id).Success,
            "The old confirmation was rebound to the replacement account's workspace partition.");
        Console.WriteLine("PASS actual native continuation dialog owner " + (returnToOwner ? "A-to-B-to-A" : "A-to-B") + " refuses restore");
    }

    private static async Task RunNativeContinuationPostcommitAsync(string contentRoot, bool cancel)
    {
        await using var fixture = await NativeContinuationFixture.CreateAsync(contentRoot);
        using var review = await fixture.ReviewAsync();
        using var cancellation = new CancellationTokenSource();
        int afterCommitCallbacks = 0;
        EventHandler observer = (_, _) =>
        {
            var observed = new FileWorkspaceStore(fixture.Runtime.StateDirectory).Get(fixture.Owner.Owner, fixture.Id);
            if (observed.Value?.LocalHistory?.LastRestore is null) return;
            Interlocked.Increment(ref afterCommitCallbacks);
            if (cancel) cancellation.Cancel();
            else throw new InvalidOperationException("Synthetic native view failure after actual Core restore commit.");
        };
        fixture.Runtime.Coordinator.Changed += observer;
        NativeWorkspaceContinuationOpenResult restored;
        try
        {
            restored = await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, explicitlyConfirmed: true, cancellation.Token);
        }
        finally { fixture.Runtime.Coordinator.Changed -= observer; }
        Require(afterCommitCallbacks > 0,
            "The postcommit native failure injection never observed an actual persisted Core restore receipt.");
        fixture.AssertRestored(restored.Restore);
        Require(restored.Restore.Receipt is not null,
            "Display failure or cancellation replaced the successful native restore result with a retryable failure.");
        string bytes = fixture.TargetBytes();
        Require((await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, true)).Restore.Outcome
                == WorkspaceContinuationRestoreOutcome.ReviewConsumed && fixture.TargetBytes() == bytes,
            "A view failure permitted a second native restore attempt.");
        Console.WriteLine("PASS actual native committed receipt survives " + (cancel ? "postcommit cancellation" : "throwing Changed subscriber"));
    }

    private static async Task RunNativeContinuationActivationOwnerAsync(string contentRoot)
    {
        await using var fixture = await NativeContinuationFixture.CreateAsync(contentRoot);
        using var review = await fixture.ReviewAsync();
        var restored = await fixture.Runtime.Coordinator.ConfirmOnlineAsync(review, true);
        fixture.AssertRestored(restored.Restore);
        var activation = restored.Activation
            ?? throw new InvalidOperationException("The successful native restore did not return an activation.");
        Require(fixture.Runtime.Coordinator.IsWorkspaceActivationCurrent(activation, NativeWorkspaceActivationKind.OnlineCharacter),
            "The stale-owner activation fixture never established a genuine successful current activation.");
        var activationGate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Runtime.Coordinator)!;
        await activationGate.WaitAsync();
        try
        {
            // Pause only the native selection/rebind queue, not the credential
            // lease. The real account transition must still complete normally.
            await fixture.Account.LinkAsync("native-activation-B", "native-activation-B");
            Require(activation.Matches(fixture.Runtime.Coordinator.State, NativeWorkspaceActivationKind.OnlineCharacter)
                && !fixture.Runtime.Coordinator.IsWorkspaceActivationCurrent(activation, NativeWorkspaceActivationKind.OnlineCharacter),
                "Home's activation check accepted retired owner A merely because queued presenter state still showed A.");
        }
        finally { activationGate.Release(); }
        Require(restored.Restore.Receipt is not null, "A later owner change discarded the completed restore receipt.");
        Console.WriteLine("PASS native routing rejects live-owner mismatch while genuine old-owner presenter state awaits rebind");
    }

    private sealed class NativeContinuationFixture : IAsyncDisposable
    {
        private readonly DirectoryInfo _sourceDirectory = Directory.CreateTempSubdirectory("android-native-continuation-source-");
        internal readonly NativeContinuationAccount Account = new();
        internal NativeRewardRuntime Runtime { get; private set; } = null!;
        internal OwnerContextStamp Owner { get; private set; }
        internal WorkspaceContinuationExport Exported { get; private set; } = null!;
        internal CharacterWorkspaceId Id => Exported.Snapshot.Workspace.Id;
        internal AndroidOnlineCharacter Character { get; private set; } = null!;

        internal static async Task<NativeContinuationFixture> CreateAsync(string contentRoot)
        {
            var fixture = new NativeContinuationFixture();
            try
            {
                await fixture.Account.LinkAsync("subject", "native-continuation-A");
                fixture.Owner = fixture.Account.Owner.Capture();
                fixture.Runtime = new NativeRewardRuntime(contentRoot, linkedOwners: fixture.Account.Owner,
                    creationBootstrap: true, accountService: fixture.Account.Service);
                await fixture.Runtime.Coordinator.InitializeAsync();
                // Finish the genuine deferred account hydration before the
                // synchronous Core Bootstrap lease competes with credential IO.
                if (typeof(RunnerSessionCoordinator).GetField("_accountInitialization",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Runtime.Coordinator) is Task startup)
                    await startup.WaitAsync(TimeSpan.FromSeconds(10));
                var source = new FileWorkspaceStore(fixture._sourceDirectory.FullName);
                IServiceProvider services = fixture.Runtime.Services;
                var bootstrap = new OwnerBoundCharacterCreationBootstrapService(new CharacterCreationBootstrapService(source,
                    services.GetRequiredService<IRulesetWorkspaceCodecResolver>(),
                    services.GetRequiredService<ICharacterFileQueries>(),
                    services.GetRequiredService<ICharacterSourceDataResolver>()), fixture.Account.Owner);
                Require(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
                    CharacterCreationBuildMethods.Priority, out string profile), "The canonical Priority Bootstrap profile is missing.");
                var created = bootstrap.Create(fixture.Owner, new(CharacterCreationBootstrapSchemas.RequestV1,
                    CharacterCreationBootstrapStages.AwaitingFoundationSelection, "sr5", "Native full continuation", "Restore",
                    CharacterCreationBuildMethods.Priority, profile));
                Require(created.Outcome == CharacterCreationBootstrapOutcomes.Success && created.Value is not null,
                    "Actual owner-scoped source Bootstrap failed: " + string.Join(",", created.Blockers));
                CharacterWorkspaceId id = created.Value!.WorkspaceId;
                var initial = source.Get(fixture.Owner.Owner, id).Value!;
                Require(source.SaveCheckpoint(fixture.Owner.Owner, id, initial.ContentRevision).Success
                    && source.ReplaceWorkspaceDocument(fixture.Owner.Owner, id, initial.ContentRevision, initial.Document).Success,
                    "The actual Bootstrap source could not establish dirty content revision 2 / checkpoint 1.");
                var exported = new WorkspaceContinuationExportService(source, fixture.Account.Owner).Export(fixture.Owner, id);
                Require(exported.Success && exported.Value is not null, "Actual owner-leased source export failed.");
                fixture.Exported = exported.Value!;
                Require(fixture.Exported.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationBootstrapBinding is not null,
                    "Native full-history fixture must carry real Core-issued auxiliary Bootstrap state.");
                JsonObject full = ContinuationRow(fixture.Exported);
                JsonObject legacy = (JsonObject)full.DeepClone();
                legacy["workspaceId"] = "native-legacy-runner";
                legacy.Remove("workspaceContinuation");
                legacy.Remove("workspaceContinuationDigest");
                fixture.Account.Rows = new JsonArray(full, legacy);
                await fixture.Runtime.Coordinator.RefreshLinkedDataAsync();
                fixture.Character = fixture.Runtime.Coordinator.OnlineCharacters.Single(item => item.WorkspaceId == id.Value);
                Require(fixture.Runtime.Coordinator.HasCompleteOnlineContinuation(fixture.Character)
                    && fixture.Runtime.Coordinator.State.Session.OwnerContext == fixture.Owner
                    && !fixture.Runtime.Coordinator.State.IsDirty && fixture.Account.SignedLists > 0,
                    "The real signed catalog did not publish into the actual original-owner native session.");
                fixture.AssertNoTarget();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal async Task<NativeWorkspaceContinuationReview> ReviewAsync()
        {
            var review = await Runtime.Coordinator.ReviewOnlineAsync(Character);
            Require(review is { CanConfirm: true } && review.Result.Outcome == WorkspaceContinuationRestoreOutcome.Available,
                "The actual Core candidate evaluator rejected the real Bootstrap continuation: "
                + JsonSerializer.Serialize(review?.Result));
            return review!;
        }

        internal void AssertNoTarget()
            => Require(!new FileWorkspaceStore(Runtime.StateDirectory).Get(Owner.Owner, Id).Success,
                "The native review-only or rejected path unexpectedly persisted the original owner's target.");

        internal void AssertRestored(WorkspaceContinuationRestoreResult result)
        {
            Require(result.Outcome is WorkspaceContinuationRestoreOutcome.Applied or WorkspaceContinuationRestoreOutcome.Recovered
                && result.Receipt is not null, "Native Core restore did not return a durable receipt: " + JsonSerializer.Serialize(result));
            var coldStore = new FileWorkspaceStore(Runtime.StateDirectory);
            var cold = coldStore.Get(Owner.Owner, Id).Value;
            Require(cold is { ContentRevision: 2, SavedRevision: 1 }
                && cold.LocalHistory is { ImportedThroughRevision: 2 } history && history.LastRestore == result.Receipt
                && cold.Document.AuxiliaryState.CharacterCreationBootstrapBinding is not null,
                "Cold native restore lost dirty revisions, imported-history boundary, Bootstrap state or its actual receipt.");
            var snapshot = coldStore.ReadContinuation(Owner.Owner, Id);
            Require(snapshot.Success && snapshot.Value is not null
                && WorkspaceContinuationCodec.Encode(new(snapshot.Value, WorkspaceContinuationSnapshotDigest.Compute(snapshot.Value)), 512 * 1024)
                    .SequenceEqual(WorkspaceContinuationCodec.Encode(Exported, 512 * 1024)),
                "Cold owner-scoped restore changed the complete snapshot, exact timestamp or auxiliary/history bytes.");
            Require(!coldStore.Get(Id).Success, "The owner-scoped restore also wrote a trusted-local copy.");
        }

        internal string TargetBytes() => JsonSerializer.Serialize(new FileWorkspaceStore(Runtime.StateDirectory).Get(Owner.Owner, Id).Value);
        public async ValueTask DisposeAsync()
        {
            if (Runtime is not null) await Runtime.DisposeAsync();
            Account.Dispose();
            _sourceDirectory.Delete(recursive: true);
        }
    }

    // Real service/key authority/HTTP transport, with only the remote endpoint
    // replaced. Source evaluation, restore admission and FileStore commits are real.
    private sealed class NativeContinuationAccount : IDisposable
    {
        private readonly IDisposable _http;
        private readonly AccountMetadata _metadata = new();
        private readonly AccountDeviceKeys _keys = new();
        private readonly AccountClock _clock = new();
        private string _subject = "subject";
        private string _grant = "native-continuation-A";
        internal AndroidAccountLinkService Service { get; }
        internal AndroidAccountOwnerContextAccessor Owner { get; }
        internal JsonArray Rows = new();
        internal int SignedLists;

        internal NativeContinuationAccount()
        {
            Type httpType = typeof(AndroidAccountLinkService).Assembly.GetType(
                "Chummer.Android.Platform.AndroidAccountLinkHttpTransport", throwOnError: true)!;
            object http = Activator.CreateInstance(httpType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object?[] { new AccountHandler(RespondAsync), TimeSpan.FromSeconds(10) }, null)!;
            _http = (IDisposable)http;
            Service = (AndroidAccountLinkService)Activator.CreateInstance(typeof(AndroidAccountLinkService),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { http, new AccountSystem(),
                    new AndroidAccountLinkKeyAuthority(_keys, _metadata), _metadata,
                    (Func<string>)(() => "native-continuation"), (Func<string>)(() => "test-only"),
                    (Func<string>)(() => "x64"), _clock }, null)!;
            Owner = new(Service);
        }

        internal async Task LinkAsync(string subject, string grant)
        {
            _subject = subject; _grant = grant;
            await Service.BeginLinkAsync();
            await Service.ResumePendingLinkAsync();
            Require(Service.Snapshot.IsLinked, "The actual native account fixture failed to commit its grant.");
        }

        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(ct));
            string installation = document.RootElement.GetProperty("installationId").GetString()!;
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/status", StringComparison.Ordinal))
                return ContinuationJsonResponse(JsonSerializer.SerializeToNode(new
                {
                    installationId = installation, grantId = request.Headers.GetValues("X-Chummer-Grant").Single(),
                    status = "active", subjectId = _subject, issuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    expiresAtUtc = DateTimeOffset.Parse(_metadata.Rows["chummer.account.installation-grant-expiry.v1"],
                        System.Globalization.CultureInfo.InvariantCulture), observedAtUtc = DateTimeOffset.UtcNow
                })!);
            if (path.EndsWith("/continuation/workspaces/list", StringComparison.Ordinal))
            {
                Require(request.Headers.Authorization?.Scheme == "Bearer"
                    && request.Headers.Contains("X-Chummer-Packet-Signature")
                    && !document.RootElement.TryGetProperty("accessToken", out _),
                    "The native catalog bypassed the actual signed credential-free-body transport.");
                Interlocked.Increment(ref SignedLists);
                return ContinuationJsonResponse(new JsonObject { ["snapshots"] = Rows.DeepClone() });
            }
            if (path.EndsWith("/linked/groups", StringComparison.Ordinal))
                return ContinuationJsonResponse(new JsonObject { ["groups"] = new JsonArray() });
            if (path.EndsWith("/revoke", StringComparison.Ordinal)) return ContinuationJsonResponse(new JsonObject());
            string operation = document.RootElement.GetProperty("operationId").GetString()!;
            var response = ContinuationJsonResponse(JsonSerializer.SerializeToNode(new
            {
                installation = new { installationId = installation, grantId = _grant, subjectId = _subject, status = "active" },
                grant = new { installationId = installation, grantId = _grant, status = "active",
                    issuedAtUtc = DateTimeOffset.UtcNow, expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) },
                alreadyClaimed = false, rotated = true, operationId = operation, grantTransport = "android-linked-v2"
            })!);
            response.Headers.TryAddWithoutValidation("Authorization", "Bearer native-continuation-test-token");
            response.Headers.TryAddWithoutValidation("X-Chummer-Grant", _grant);
            return response;
        }

        public void Dispose() { _http.Dispose(); _keys.Dispose(); }
    }
}
