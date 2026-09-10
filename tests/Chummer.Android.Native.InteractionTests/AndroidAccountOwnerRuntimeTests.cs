using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunAndroidAccountOwnerCasesAsync(string contentRoot)
    {
        await RunOpaqueAccountOwnerKeyCasesAsync();
        await RunUnknownAccountStartupCasesAsync(contentRoot);
        using var fixture = new ActualAccountFixture();
        Require(!fixture.Owner.Capture().IsValid, "An uninitialized Android account cannot mint Local owner authority.");
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: fixture.Owner,
            creationBootstrap: true, accountService: fixture.Account);
        await runtime.Coordinator.InitializeAsync();
        Require(fixture.Owner.Capture().Owner == OwnerScope.LocalSingleUser,
            "Native startup did not hydrate the actual device-local authority before Shell.");
        Require(runtime.Shell.State.OwnerContext == fixture.Owner.Capture(),
            "The actual Shell was not projected from the Android account owner.");
        Require(fixture.Requests == 0, "Device-local bootstrap cannot wait for or query Hub.");
        Console.WriteLine("PASS actual Android account → Core → Shell local startup");
        OwnerContextStamp local = fixture.Owner.Capture();
        await fixture.Account.InitializeAsync();
        Require(fixture.Owner.Capture() == local,
            "Background account initialization invalidated an unchanged device-local owner.");
        CharacterWorkspaceId localRunner = await CreateActualAccountRunnerAsync(runtime, "Device-local runner");
        await fixture.LinkAsync("native-account-A", "native-grant-A");
        OwnerContextStamp linked = fixture.Owner.Capture();
        Require(linked.IsValid && linked.Owner.Value ==
            "install-account-v1:2439e7738b045b5b21ad3d31b94018953bad614617b36b09adc7a790240aa752"
            && linked.AuthorityInstanceId == local.AuthorityInstanceId && linked.TransitionRevision > local.TransitionRevision,
            "Authenticated Android link did not become the exact Core owner.");
        Require(!fixture.Owner.TryAcquire(local, out _), "The old local stamp survived an actual account transition.");
        // Explicit shared presentation reinitialization here. Production account-
        // switch UX/Maui composition is a separate closure requirement, not faked.
        await runtime.Shell.InitializeAsync(default);
        await runtime.Presenter.InitializeAsync(default);
        CharacterWorkspaceId linkedRunner = await CreateActualAccountRunnerAsync(runtime, "Account runner");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        // The unscoped overload is the trusted device-local store boundary;
        // passing LocalSingleUser to the remote-owner overload is rejected.
        Require(store.Get(localRunner).Success && !store.Get(linked.Owner, localRunner).Success
            && store.Get(linked.Owner, linkedRunner).Success && !store.Get(linkedRunner).Success,
            "Actual creation adopted or crossed device-local/account partitions.");
        string localBefore = JsonSerializer.Serialize(store.Get(localRunner).Value);
        await runtime.Presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata(
            "Saved account runner", "ACCOUNT", "Actual Android account-owned edit"), default);
        await runtime.Coordinator.SaveAsync();
        var savedAccount = new FileWorkspaceStore(runtime.StateDirectory).Get(linked.Owner, linkedRunner).Value!;
        Require(runtime.Presenter.State.Error is null && savedAccount.SavedRevision == savedAccount.ContentRevision
            && savedAccount.Document.Content.Contains("Saved account runner", StringComparison.Ordinal)
            && JsonSerializer.Serialize(store.Get(localRunner).Value) == localBefore,
            "Actual Android account edit/save was not durably confined to its account partition.");
        string accountBefore = JsonSerializer.Serialize(savedAccount);
        Console.WriteLine("PASS actual Android grant → Core native creation in separate owner partitions");

        Task unlink;
        int originalRequests = fixture.Requests;
        Require(fixture.Owner.TryAcquire(linked, out IOwnerContextLease? held), "Current owner lease unavailable.");
        using (held)
        {
            var started = new ManualResetEventSlim();
            unlink = Task.Run(async () => { started.Set(); await fixture.Account.UnlinkAsync(); });
            Require(started.Wait(TimeSpan.FromSeconds(5)), "Unlink writer did not start.");
            var accountGate = (SemaphoreSlim)typeof(AndroidAccountLinkService).GetField("_gate",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Account)!;
            Require(SpinWait.SpinUntil(() => accountGate.CurrentCount == 0, TimeSpan.FromSeconds(5)),
                "Unlink did not enter the actual account operation gate.");
            Require(!unlink.IsCompleted && fixture.Requests == originalRequests,
                "Unlink crossed the live Core lease before credential/request admission.");
            Require(fixture.Owner.Capture() == linked && held!.Stamp == linked, "Live Core ownership changed during the lease.");
            Require(!fixture.Owner.TryAcquire(linked, out _), "A nested lease bypassed the credential exclusion gate.");
            // No await occurs between acquisition and release, as Core requires.
        }
        await unlink.WaitAsync(TimeSpan.FromSeconds(5));
        Require(fixture.Owner.Current == OwnerScope.LocalSingleUser && !fixture.Owner.TryAcquire(linked, out _),
            "Unlink did not retire the original grant stamp.");
        await runtime.Presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata(
            "Rejected stale account runner", "REJECTED", "After unlink"), default);
        Require(JsonSerializer.Serialize(store.Get(linked.Owner, linkedRunner).Value) == accountBefore
            && JsonSerializer.Serialize(store.Get(localRunner).Value) == localBefore,
            "An old account display mutated a runner after actual unlink.");
        Console.WriteLine("PASS actual credential writer waits for Core lease, then retires original ownership");

        await fixture.LinkAsync("native-account-A", "native-grant-A2");
        OwnerContextStamp returned = fixture.Owner.Capture();
        Require(returned.Owner == linked.Owner && returned != linked && !fixture.Owner.TryAcquire(linked, out _),
            "Actual unlink/relink A-to-Local-to-A restored a stale epoch.");
        await fixture.Owner.InitializeAsync();
        Require(fixture.Owner.Capture() == returned, "A read-only local hydration unnecessarily invalidated an unchanged grant.");
        await runtime.Presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata(
            "Rejected old grant runner", "REJECTED", "After relink to the same account"), default);
        Require(JsonSerializer.Serialize(store.Get(linked.Owner, linkedRunner).Value) == accountBefore
            && JsonSerializer.Serialize(store.Get(localRunner).Value) == localBefore,
            "A stale original display was resurrected by relinking the same account.");
        await runtime.Shell.InitializeAsync(default);
        await runtime.Presenter.InitializeAsync(default);
        await runtime.Presenter.LoadAsync(linkedRunner, default);
        await runtime.Presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata(
            "Reopened account runner", "ACCOUNT", "Explicitly reopened after relink"), default);
        await runtime.Coordinator.SaveAsync();
        var reopened = new FileWorkspaceStore(runtime.StateDirectory).Get(returned.Owner, linkedRunner).Value!;
        Require(runtime.Presenter.State.Error is null && reopened.SavedRevision == reopened.ContentRevision
            && reopened.Document.Content.Contains("Reopened account runner", StringComparison.Ordinal)
            && JsonSerializer.Serialize(store.Get(localRunner).Value) == localBefore,
            "Explicit reopening under the new account grant did not restore correct edit/save authority.");
        Console.WriteLine("PASS actual Android account edit/save and stale unlink/relink display fencing");
        using (var restart = fixture.Restart())
        {
            Require(!restart.Owner.Capture().IsValid, "Restart acquired identity before secure-store hydration.");
            await restart.Owner.InitializeAsync();
            OwnerContextStamp restored = restart.Owner.Capture();
            Require(restored.Owner == returned.Owner && restored.AuthorityInstanceId != returned.AuthorityInstanceId
                && !restart.Owner.TryAcquire(returned, out _) && restart.Requests == 0,
                "Restart reused process authority or changed the persisted account owner.");
        }
        Console.WriteLine("PASS actual account ABA and new-process issuer isolation with zero-network hydration");

        using (var restart = fixture.Restart())
        {
            await restart.Owner.InitializeAsync();
            OwnerContextStamp beforeExpiry = restart.Owner.Capture();
            Require(restart.Owner.TryAcquire(beforeExpiry, out IOwnerContextLease? expiryLease), "Expiry control needs one live lease.");
            using (expiryLease)
            {
                restart.Clock.Now = DateTimeOffset.UtcNow.AddDays(31);
                Require(restart.Owner.Capture() == beforeExpiry && expiryLease!.Stamp == beforeExpiry,
                    "Clock expiry tore an already admitted synchronous Core operation.");
            }
            Require(!restart.Owner.Capture().IsValid && !restart.Owner.TryAcquire(beforeExpiry, out _),
                "Expired credentials renewed Core access after lease disposal.");
            bool denied = false;
            try { _ = restart.Owner.Current; } catch (InvalidOperationException) { denied = true; }
            Require(denied, "Unknown/expired Current silently returned a local owner.");
        }
        Console.WriteLine("PASS actual account expiry rejects new leases without tearing the admitted operation");

        foreach (bool missing in new[] { true, false })
        {
            string original = fixture.Metadata.Rows["chummer.account.grant-owner.v1"];
            if (missing) fixture.Metadata.Rows.Remove("chummer.account.grant-owner.v1");
            else fixture.Metadata.Rows["chummer.account.grant-owner.v1"] = "{corrupt";
            using (var restart = fixture.Restart())
            {
                try { await restart.Owner.InitializeAsync(); } catch (InvalidDataException) { }
                Require(!restart.Owner.Capture().IsValid && restart.Requests == 0,
                    "Legacy/corrupt owner material authorized a local fallback or performed a hidden network lookup.");
            }
            fixture.Metadata.Rows["chummer.account.grant-owner.v1"] = original;
        }
        Console.WriteLine("PASS actual legacy/corrupt account startup stays unknown rather than device-local");

        fixture.Metadata.Rows.Remove("chummer.account.grant-owner.v1");
        using (var restart = fixture.Restart())
        {
            await restart.Owner.InitializeAsync();
            Require(!restart.Owner.Capture().IsValid && restart.Requests == 0,
                "Legacy local hydration invented an owner before authenticated status.");
            await restart.Account.InitializeAsync();
            OwnerContextStamp migrated = restart.Owner.Capture();
            Require(restart.Account.Snapshot.IsLinked && migrated.IsValid && migrated.Owner == returned.Owner
                && migrated.AuthorityInstanceId != returned.AuthorityInstanceId && restart.Requests == 1,
                "Real account status did not hydrate the original stable Core owner on restart.");
            Require(!restart.Owner.TryAcquire(returned, out _)
                && store.Get(migrated.Owner, linkedRunner).Success && store.Get(localRunner).Success,
                "Legacy owner migration revived an old process stamp or moved durable runners.");
            await restart.Account.InitializeAsync();
            Require(restart.Owner.Capture() == migrated,
                "Repeated authenticated status retired an unchanged Core owner epoch.");
        }
        Console.WriteLine("PASS actual legacy grant → authenticated status → same Core partition, fresh process authority");

        fixture.Subject = "native-account-B";
        fixture.NextGrant = "native-grant-B";
        await fixture.Account.BeginLinkAsync();
        bool interrupted = false;
        fixture.Metadata.BeforeWrite = key =>
        {
            if (key != "chummer.account.grant-owner.v1" || interrupted) return;
            interrupted = true;
            throw new IOException("Injected owner metadata write failure.");
        };
        await fixture.Account.ResumePendingLinkAsync();
        Require(interrupted && !fixture.Account.Snapshot.IsLinked && !fixture.Owner.Capture().IsValid,
            "A torn grant commit exposed old or partial Core authority.");
        fixture.Metadata.BeforeWrite = null;
        int calls = fixture.Requests;
        await fixture.Owner.InitializeAsync();
        Require(fixture.Owner.Current.Value ==
            "install-account-v1:52be106c25f3a9ebd622d7426ce4a4d461991f05282f8f929f70f3d094f5ef1b" && fixture.Requests == calls
            && !fixture.Owner.TryAcquire(returned, out _), "Exact staged recovery failed to publish only the new committed account.");
        Require(store.Get(localRunner).Success && store.Get(linked.Owner, linkedRunner).Success,
            "Credential transitions deleted unrelated durable runner data.");
        Console.WriteLine("PASS actual torn credential commit → exact local recovery, no false Core authority or runner adoption");
    }

    private static async Task RunOpaqueAccountOwnerKeyCasesAsync()
    {
        using var fixture = new ActualAccountFixture();
        Require(!fixture.Owner.Capture().IsValid, "Owner-key encoding invented an uninitialized account.");
        await fixture.Owner.InitializeAsync();
        Require(fixture.Owner.Current == OwnerScope.LocalSingleUser,
            "Owner-key encoding changed the explicit device-local identity.");
        var normalizedKeys = new HashSet<string>(StringComparer.Ordinal);
        // Interior whitespace is permitted by the existing wire contract;
        // padding remains rejected by the account-service admission tests.
        foreach (string subject in new[] { "opaque-A", "opaque-a", "opaque A", "opaque  A", "e\u0301", "\u00e9" })
        {
            await fixture.LinkAsync(subject, "owner-key-grant-" + normalizedKeys.Count);
            OwnerContextStamp linked = fixture.Owner.Capture();
            Require(linked.IsValid && !linked.Owner.UsesLocalSingleUserValue
                && linked.Owner.Value.StartsWith("install-account-v1:", StringComparison.Ordinal)
                && linked.Owner.Value == linked.Owner.NormalizedValue
                && normalizedKeys.Add(linked.Owner.NormalizedValue),
                "Distinct opaque subjects aliased after Core owner normalization.");
            Require(fixture.Owner.Capture() == linked, "An unchanged subject minted a different owner stamp.");
            using (var restart = fixture.Restart())
            {
                await restart.Owner.InitializeAsync();
                Require(restart.Owner.Current == linked.Owner && restart.Requests == 0
                    && restart.Owner.Capture().AuthorityInstanceId != linked.AuthorityInstanceId,
                    "Credential rehydration changed the exact subject's durable owner key.");
            }
            await fixture.LinkAsync(subject, "owner-key-replacement-grant-" + normalizedKeys.Count);
            OwnerContextStamp replacement = fixture.Owner.Capture();
            Require(replacement.Owner == linked.Owner && replacement != linked
                && replacement.TransitionRevision > linked.TransitionRevision
                && !fixture.Owner.TryAcquire(linked, out _),
                "A new grant changed the durable owner key or retained stale credential authority.");
        }
        Console.WriteLine("PASS actual opaque subject owner isolation, rehydration and credential-revision stability");
    }

    private static async Task RunUnknownAccountStartupCasesAsync(string contentRoot)
    {
        foreach (string mode in new[] { "online", "offline-retry", "corrupt-repair", "dispose", "cancel-startup" })
            await RunUnknownAccountStartupCaseAsync(contentRoot, mode);
    }

    private static async Task RunUnknownAccountStartupCaseAsync(string contentRoot, string mode)
    {
        using var seed = new ActualAccountFixture();
        await seed.Account.InitializeAsync();
        await seed.LinkAsync("startup-account", "startup-grant");
        seed.Metadata.Rows.Remove("chummer.account.grant-owner.v1");
        if (mode == "corrupt-repair") seed.Metadata.Rows["chummer.account.grant-owner.v1"] = "{corrupt";
        using var restart = seed.Restart();
        var statusStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStatus = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        restart.BeforeStatus = async ct =>
        {
            statusStarted.TrySetResult();
            await releaseStatus.Task.WaitAsync(ct);
            if (mode == "offline-retry") throw new HttpRequestException("Synthetic offline status");
        };
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: restart.Owner,
            creationBootstrap: true, accountService: restart.Account);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Coordinator.Changed += (_, _) =>
        {
            OwnerContextStamp owner = restart.Owner.Capture();
            if (owner.IsValid && runtime.Shell.State.OwnerContext == owner
                && runtime.Presenter.State.Session.OwnerContext == owner
                && !runtime.Coordinator.IsBusy && runtime.Presenter.State.Error is null)
                ready.TrySetResult();
        };
        try
        {
            if (mode == "cancel-startup")
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                bool canceled = false;
                try { await runtime.Coordinator.InitializeAsync(cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Require(canceled && restart.Requests == 0 && !restart.Owner.Capture().IsValid,
                    "Canceled startup performed account recovery or swallowed cancellation.");
            }
            await runtime.Coordinator.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Require(!restart.Owner.Capture().IsValid && runtime.Presenter.State.WorkspaceId is null
                && runtime.Presenter.State.OpenWorkspaces.Count == 0 && !runtime.Coordinator.IsBusy,
                "Unknown startup displayed a runner or stayed blocked on account validation.");
            if (mode == "corrupt-repair")
            {
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                Require(restart.Requests == 0 && !ready.Task.IsCompleted && !restart.Owner.Capture().IsValid,
                    "Corrupt local owner metadata reached Hub or bootstrapped a fallback owner.");
                restart.Metadata.Rows.Remove("chummer.account.grant-owner.v1");
            }
            else await statusStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (mode == "dispose") runtime.Coordinator.Dispose();
        }
        finally { releaseStatus.TrySetResult(); }
        await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
        if (mode == "dispose")
        {
            Require(!ready.Task.IsCompleted && runtime.Presenter.State.WorkspaceId is null
                && runtime.Presenter.State.OpenWorkspaces.Count == 0,
                "Disposed startup published or reactivated a workspace from a late account response.");
            Console.WriteLine("PASS actual unknown account startup: dispose fences delayed authenticated status");
            return;
        }
        if (mode is "offline-retry" or "corrupt-repair")
        {
            Require(!ready.Task.IsCompleted && !restart.Owner.Capture().IsValid && !runtime.Coordinator.IsBusy,
                "Failed account recovery manufactured usable authority or left the app blocked.");
            restart.BeforeStatus = null;
            await restart.Account.InitializeAsync();
        }
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        OwnerContextStamp recovered = restart.Owner.Capture();
        Require(recovered.Owner.Value ==
            "install-account-v1:8c1efec2205519c46b77c7c9a34d2b8f70ba3cc65fb8f7241cb360c2d4807eae"
            && restart.Requests == (mode == "offline-retry" ? 2 : 1),
            "Automatic startup recovery invented an owner or repeated the authenticated status request.");
        CharacterWorkspaceId runner = await CreateActualAccountRunnerAsync(runtime, "Recovered startup runner");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        Require(store.Get(recovered.Owner, runner).Success && !store.Get(runner).Success,
            "Recovered startup created its runner under Local instead of the authenticated account.");
        Console.WriteLine($"PASS actual unknown account startup: {mode} → automatic owner-bound Shell → create");
    }

    private static Task AccountStartupTask(RunnerSessionCoordinator coordinator)
        => (Task?)typeof(RunnerSessionCoordinator).GetField("_accountInitialization",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)
            ?? throw new InvalidOperationException("The actual background account recovery was never started.");

    private static async Task<CharacterWorkspaceId> CreateActualAccountRunnerAsync(NativeRewardRuntime runtime, string name)
    {
        await runtime.Coordinator.CreateRunnerAsync();
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", name, default);
        await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
        Require(runtime.Presenter.State.WorkspaceId is not null && runtime.Presenter.State.Error is null,
            "Actual account-owned native runner creation failed: " + runtime.Presenter.State.Error);
        return runtime.Presenter.State.WorkspaceId!.Value;
    }

    private sealed class ActualAccountFixture : IDisposable
    {
        private readonly IDisposable _transport;
        private readonly AccountDeviceKeys _keys = new();
        private readonly bool _ownsKeys;
        internal readonly AccountMetadata Metadata = new();
        internal readonly AccountClock Clock = new();
        internal AndroidAccountLinkService Account { get; }
        internal AndroidAccountOwnerContextAccessor Owner { get; }
        internal int Requests;
        internal string Subject = "native-account-A";
        internal string NextGrant = "native-grant-A";
        internal Func<CancellationToken, Task>? BeforeStatus;

        internal ActualAccountFixture(AccountMetadata? metadata = null, AccountDeviceKeys? keys = null)
        {
            _ownsKeys = keys is null;
            if (metadata is not null) Metadata = metadata;
            if (keys is not null) _keys = keys;
            var authority = new AndroidAccountLinkKeyAuthority(_keys, Metadata);
            Type transportType = typeof(AndroidAccountLinkService).Assembly.GetType(
                "Chummer.Android.Platform.AndroidAccountLinkHttpTransport", throwOnError: true)!;
            object transport = Activator.CreateInstance(transportType,
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object?[] { new AccountHandler(RespondAsync), TimeSpan.FromSeconds(10) }, null)!;
            _transport = (IDisposable)transport;
            Account = (AndroidAccountLinkService)Activator.CreateInstance(typeof(AndroidAccountLinkService),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object?[] { transport, new AccountSystem(), authority, Metadata,
                    (Func<string>)(() => "proof-owner"), (Func<string>)(() => "test-only"),
                    (Func<string>)(() => "x64"), Clock }, null)!;
            Owner = new(Account);
        }

        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/status", StringComparison.Ordinal))
            {
                if (BeforeStatus is not null) await BeforeStatus(cancellationToken);
                using JsonDocument statusBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        installationId = statusBody.RootElement.GetProperty("installationId").GetString(),
                        grantId = request.Headers.GetValues("X-Chummer-Grant").Single(),
                        status = "active", subjectId = Subject,
                        issuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        expiresAtUtc = DateTimeOffset.Parse(Metadata.Rows["chummer.account.installation-grant-expiry.v1"],
                            System.Globalization.CultureInfo.InvariantCulture),
                        observedAtUtc = DateTimeOffset.UtcNow
                    }), Encoding.UTF8, "application/json")
                };
            }
            if (path.EndsWith("/revoke", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string install = body.RootElement.GetProperty("installationId").GetString()!;
            string operation = body.RootElement.GetProperty("operationId").GetString()!;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    installation = new { installationId = install, grantId = NextGrant, subjectId = Subject, status = "active" },
                    grant = new { installationId = install, grantId = NextGrant, status = "active",
                        issuedAtUtc = DateTimeOffset.UtcNow, expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) },
                    alreadyClaimed = false, rotated = true, operationId = operation, grantTransport = "android-linked-v2"
                }), Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("Authorization", "Bearer native-owner-test-token");
            response.Headers.TryAddWithoutValidation("X-Chummer-Grant", NextGrant);
            return response;
        }

        internal async Task LinkAsync(string subject, string grant)
        {
            Subject = subject;
            NextGrant = grant;
            await Account.BeginLinkAsync();
            await Account.ResumePendingLinkAsync();
            Require(Account.Snapshot.IsLinked, "The real account service did not commit a test grant.");
        }

        internal ActualAccountFixture Restart() => new(Metadata, _keys) { Subject = Subject, NextGrant = NextGrant };
        public void Dispose() { _transport.Dispose(); if (_ownsKeys) _keys.Dispose(); }
    }

    private sealed class AccountMetadata : IAndroidAccountLinkKeyMetadataStore
    {
        internal readonly Dictionary<string, string> Rows = new(StringComparer.Ordinal);
        internal Action<string>? BeforeWrite;
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); lock (Rows) return Task.FromResult(Rows.GetValueOrDefault(key)); }
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); BeforeWrite?.Invoke(key); lock (Rows) Rows[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); BeforeWrite?.Invoke(key); lock (Rows) Rows.Remove(key); return Task.CompletedTask; }
    }

    private sealed class AccountClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AccountDeviceKeys : IAndroidDeviceKeyStore, IDisposable
    {
        private readonly Dictionary<string, RSA> _keys = new(StringComparer.Ordinal);
        public Task<AndroidDevicePublicKey> CreateAsync(string alias, CancellationToken cancellationToken = default)
        { _keys.Add(alias, RSA.Create(2048)); return GetPublicKeyAsync(alias, cancellationToken); }
        public Task<AndroidDevicePublicKey> GetPublicKeyAsync(string alias, CancellationToken cancellationToken = default)
            => Task.FromResult(_keys.TryGetValue(alias, out RSA? key)
                ? new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Available, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))
                : new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Missing));
        public Task<byte[]> SignAsync(string alias, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
            => Task.FromResult(_keys[alias].SignData(payload.Span, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        public Task DeleteAsync(string alias, CancellationToken cancellationToken = default)
        { if (_keys.Remove(alias, out RSA? key)) key.Dispose(); return Task.CompletedTask; }
        public void Dispose() { foreach (RSA key in _keys.Values) key.Dispose(); _keys.Clear(); }
    }

    private sealed class AccountHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class AccountSystem : IAndroidSystemService
    {
        public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(true);
        public Task<AndroidUpdateCheckResult> CheckForUpdatesAsync() => Task.FromResult(AndroidUpdateCheckResult.Unavailable);
        public Task ShareTextAsync(string text) => Task.CompletedTask;
        public Task<bool> PrintPdfAsync(string name, string payload, string title, CancellationToken ct) => Task.FromResult(false);
    }
}
