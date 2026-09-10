using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunInitialPhoneRouteCasesAsync(string contentRoot)
    {
        RunInitialPhoneRoutePolicyCases();
        await RunDelayedInitialPhoneRouteCaseAsync(contentRoot, navigateAway: false);
        await RunDelayedInitialPhoneRouteCaseAsync(contentRoot, navigateAway: true);
        await RunUnknownRecoveringInitialPhoneRouteCaseAsync(contentRoot);
        await RunEmptyAndUnknownInitialPhoneRouteCasesAsync(contentRoot);
    }

    private static void RunInitialPhoneRoutePolicyCases()
    {
        var owner = new OwnerContextStamp(OwnerScope.LocalSingleUser, "initial-phone-route-test", 1);
        var workspace = new CharacterWorkspaceId("1f5c4717d6a642ac80f6767a14a93498");
        var pending = new PhoneInitialRouteReadiness(PhoneInitialRouteReadinessKind.Pending, owner, null, false);
        var ready = new PhoneInitialRouteReadiness(PhoneInitialRouteReadinessKind.Ready, owner, workspace, true);
        var policy = new PhoneInitialRoutePolicy();
        for (int index = 0; index < 20; index++)
        {
            policy.Observe(pending);
            Require(!policy.TryResolve(pending) && !policy.IsRetired,
                "Pending readiness consumed the initial route or admitted a profile.");
        }
        Require(policy.TryResolve(ready) && !policy.TryResolve(ready),
            "Settled startup did not resolve exactly once.");
        Console.WriteLine("PASS initial route pending and duplicate notifications resolve exactly once");

        policy = new();
        policy.Observe(pending);
        policy.ObserveNavigation(null, "//runners", isDefaultRunnersPage: true);
        policy.ObserveNavigation("//implicit-root", "//implicit-root/runners", isDefaultRunnersPage: true);
        Require(!policy.IsRetired && !policy.TryResolve(pending),
            "Initial Shell default-item attachment was mistaken for a user departure.");
        Require(policy.TryResolve(ready), "Default Shell settling lost the pending automatic route.");
        policy = new();
        policy.Observe(pending);
        policy.ObserveNavigation("//runners", "//more", isDefaultRunnersPage: true);
        policy.ObserveNavigation("//more", "//runners", isDefaultRunnersPage: false);
        Require(!policy.TryResolve(ready), "Leaving and returning to Home revived initial routing.");
        Console.WriteLine("PASS initial Shell default navigation is distinct from user destination ABA");

        foreach (string boundary in new[] { "user-navigation-ABA", "unload", "cancel", "navigation-failure" })
        {
            policy = new();
            policy.Observe(pending);
            policy.Retire();
            Require(!policy.TryResolve(ready), "A late callback revived initial navigation after " + boundary);
            Console.WriteLine("PASS initial route superseded by " + boundary);
        }
        foreach (string boundary in new[] { "owner-change", "owner-ABA", "owner-unavailable" })
        {
            policy = new();
            policy.Observe(pending);
            var changed = pending with { Owner = boundary == "owner-unavailable" ? null
                : new OwnerContextStamp(new OwnerScope("route-other-owner"), owner.AuthorityInstanceId,
                    owner.TransitionRevision + 1) };
            policy.Observe(changed);
            var returned = ready with { Owner = new OwnerContextStamp(owner.Owner, owner.AuthorityInstanceId,
                owner.TransitionRevision + 2) };
            Require(!policy.TryResolve(boundary == "owner-ABA" ? returned : ready),
                "Queued initial navigation survived " + boundary);
            Console.WriteLine("PASS initial route rejects " + boundary);
        }
        policy = new();
        var unavailable = new PhoneInitialRouteReadiness(PhoneInitialRouteReadinessKind.Unavailable, null, null, false);
        Require(!policy.TryResolve(unavailable) && policy.IsRetired && !policy.TryResolve(ready),
            "Unavailable startup waited for or adopted a later unrelated account owner.");
        Console.WriteLine("PASS unknown initial owner stays Home without adopting later authority");
    }

    private static async Task RunDelayedInitialPhoneRouteCaseAsync(string contentRoot, bool navigateAway)
    {
        using var fixture = new ActualAccountFixture();
        await fixture.Owner.InitializeAsync();
        var owners = new DeniedStartupOwner(fixture.Owner);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
            accountService: fixture.Account);
        // Seed through the real owner-bound client and store, but do not load or
        // initialize either presenter. The coordinator must restore this row.
        var imported = await runtime.Client.ImportAsync(new WorkspaceImportDocument("""
            <character><name>Initial route saved runner</name><gameedition>SR5</gameedition>
            <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings><metatype>Human</metatype>
            <buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion>
            <appversion>5.225.0</appversion><created>True</created><karma>30</karma><nuyen>1000</nuyen>
            <improvements/><contacts/><expenses/><notes>Initial route must never mutate this.</notes></character>
            """, "sr5"), default);
        Require((await runtime.Client.SaveAsync(imported.Id, default)).Success, "Initial route seed did not save.");
        var before = new FileWorkspaceStore(runtime.StateDirectory).Get(imported.Id).Value!;
        string beforeBytes = JsonSerializer.Serialize(before);
        Require(runtime.Presenter.State.Profile is null, "Seed bypassed the actual startup presenter restoration.");
        var accountGate = (SemaphoreSlim)typeof(Chummer.Android.Platform.AndroidAccountLinkService).GetField("_gate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Account)!;
        int heldAccountGate = 0;
        owners.BeforeFirstDenial = () =>
        {
            // Local hydration has finished before Core requests this lease.
            // Hold the real background account initializer until the test has
            // observed Pending; its first genuine notification will resume it.
            Require(accountGate.Wait(0), "The post-hydration account gate was unexpectedly occupied.");
            Volatile.Write(ref heldAccountGate, 1);
        };
        owners.Deny = true;
        var policy = new PhoneInitialRoutePolicy();
        var callbacks = new ConcurrentQueue<Action>();
        int routeCount = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void QueueObservation(object? sender, EventArgs args)
        {
            PhoneInitialRouteReadiness observation = runtime.Coordinator.CaptureInitialPhoneRouteReadiness();
            policy.Observe(observation);
            // Match MainShell's non-reentrant event boundary: observers enqueue
            // work, and the UI later captures the current state again.
            callbacks.Enqueue(() =>
            {
                var current = runtime.Coordinator.CaptureInitialPhoneRouteReadiness();
                if (policy.TryResolve(current))
                {
                    Require(current.Owner == fixture.Owner.Capture() && current.WorkspaceId == imported.Id
                        && current.HasProfile, "Initial route selected a stale owner or runner.");
                    routeCount++;
                }
            });
            if (observation.Kind == PhoneInitialRouteReadinessKind.Ready) ready.TrySetResult();
        }
        runtime.Coordinator.Changed += QueueObservation;
        try
        {
            await runtime.Coordinator.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Require(Volatile.Read(ref heldAccountGate) == 1 && !AccountStartupTask(runtime.Coordinator).IsCompleted
                && runtime.Coordinator.CaptureInitialPhoneRouteReadiness().Kind == PhoneInitialRouteReadinessKind.Pending
                && NativeAccountInitializationField<bool>(runtime, "_workspaceOwnerInitializationPending"),
                "The denied admission did not exercise real pending owner initialization.");
            while (callbacks.TryDequeue(out Action? early)) early();
            Require(routeCount == 0 && !policy.IsRetired, "Pending actual startup chose the empty Home terminal state.");

            var activationGate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await activationGate.WaitAsync();
            try
            {
                owners.Deny = false;
                if (Interlocked.Exchange(ref heldAccountGate, 0) == 1) accountGate.Release();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                Require(runtime.Coordinator.CaptureInitialPhoneRouteReadiness().Kind == PhoneInitialRouteReadinessKind.Pending
                    && !ready.Task.IsCompleted && routeCount == 0,
                    "Account callback re-entered the gated native initialization or routed inline.");
                if (navigateAway) policy.Retire(); // A leave-and-return cannot revive this intent.
            }
            finally { activationGate.Release(); }

            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await AwaitNativeAccountOwnerInitializationAsync(runtime, fixture, fixture.Owner.Capture());
            Require(routeCount == 0, "An account callback performed UI routing before dispatch.");
            while (callbacks.TryDequeue(out Action? callback)) callback();
            QueueObservation(null, EventArgs.Empty);
            while (callbacks.TryDequeue(out Action? repeated)) repeated();
            Require(routeCount == (navigateAway ? 0 : 1), "Initial route was lost, replayed, or ignored user navigation.");
            RequireNativeAccountDisplay(runtime, fixture, fixture.Owner.Capture(), imported.Id, "initial route readiness");
            var after = new FileWorkspaceStore(runtime.StateDirectory).Get(imported.Id).Value!;
            Require(beforeBytes == JsonSerializer.Serialize(after)
                && after.ContentRevision == before.ContentRevision && after.SavedRevision == before.SavedRevision
                && fixture.Requests == 0,
                "Initial route changed durable bytes/revisions or waited for Hub.");
            Console.WriteLine("PASS actual delayed owner startup restores unchanged runner and "
                + (navigateAway ? "preserves user navigation" : "routes once only after UI dispatch"));
        }
        finally
        {
            owners.Deny = false;
            if (Interlocked.Exchange(ref heldAccountGate, 0) == 1) accountGate.Release();
            runtime.Coordinator.Changed -= QueueObservation;
        }
    }

    private static async Task RunUnknownRecoveringInitialPhoneRouteCaseAsync(string contentRoot)
    {
        using var seed = new ActualAccountFixture();
        await seed.Account.InitializeAsync();
        await seed.LinkAsync("initial-route-linked", "initial-route-grant");
        using var restart = seed.Restart();
        await restart.Owner.InitializeAsync();
        OwnerContextStamp beforeOwner = restart.Owner.Capture();
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: restart.Owner,
            accountService: restart.Account);
        var imported = await runtime.Client.ImportAsync(new WorkspaceImportDocument("""
            <character><name>Legacy owner startup runner</name><gameedition>SR5</gameedition>
            <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings><metatype>Human</metatype>
            <buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion>
            <appversion>5.225.0</appversion><created>True</created><karma>30</karma><nuyen>1000</nuyen>
            <improvements/><contacts/><expenses/><notes>Keep this saved owner-bound row.</notes></character>
            """, "sr5"), default);
        Require((await runtime.Client.SaveAsync(imported.Id, default)).Success, "Legacy startup seed did not save.");
        string before = JsonSerializer.Serialize(new FileWorkspaceStore(runtime.StateDirectory)
            .Get(beforeOwner.Owner, imported.Id).Value!);
        Require(runtime.Presenter.State.Profile is null, "Legacy fixture manually initialized the presenter.");
        restart.Metadata.Rows.Remove("chummer.account.grant-owner.v1");
        await restart.Owner.InitializeAsync();
        Require(!restart.Owner.Capture().IsValid, "Missing local owner binding still exposed owner authority.");
        var statusStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStatus = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        restart.BeforeStatus = async ct =>
        {
            statusStarted.TrySetResult();
            await releaseStatus.Task.WaitAsync(ct);
        };
        var policy = new PhoneInitialRoutePolicy();
        void Observe(object? sender, EventArgs args)
        {
            var observation = runtime.Coordinator.CaptureInitialPhoneRouteReadiness();
            policy.Observe(observation);
            if (observation.Kind == PhoneInitialRouteReadinessKind.Ready) ready.TrySetResult();
        }
        runtime.Coordinator.Changed += Observe;
        try
        {
            await runtime.Coordinator.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await statusStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var pending = runtime.Coordinator.CaptureInitialPhoneRouteReadiness();
            Require(pending.Kind == PhoneInitialRouteReadinessKind.Pending && pending.Owner is null
                && !policy.TryResolve(pending) && !policy.IsRetired && !ready.Task.IsCompleted
                && runtime.Presenter.State.Profile is null && !runtime.Coordinator.IsBusy,
                "In-flight legacy owner recovery consumed initial navigation, exposed data, or blocked Home.");
            var activationGate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await activationGate.WaitAsync();
            try
            {
                releaseStatus.TrySetResult();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                Require(restart.Owner.Capture().IsValid && restart.Owner.Capture().Owner == beforeOwner.Owner
                    && runtime.Coordinator.CaptureInitialPhoneRouteReadiness().Kind == PhoneInitialRouteReadinessKind.Pending
                    && !policy.IsRetired && !ready.Task.IsCompleted,
                    "Authenticated owner recovery bypassed the pending native hydration barrier.");
            }
            finally { activationGate.Release(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await AwaitNativeAccountOwnerInitializationAsync(runtime, restart, restart.Owner.Capture());
            var settled = runtime.Coordinator.CaptureInitialPhoneRouteReadiness();
            Require(settled.Owner == restart.Owner.Capture() && settled.WorkspaceId == imported.Id && settled.HasProfile
                && policy.TryResolve(settled) && !policy.TryResolve(settled) && restart.Requests == 1,
                "The same authenticated grant did not restore and resolve the exact initial runner once.");
            Require(before == JsonSerializer.Serialize(new FileWorkspaceStore(runtime.StateDirectory)
                .Get(beforeOwner.Owner, imported.Id).Value!), "Legacy startup routing changed saved bytes or revisions.");
            Console.WriteLine("PASS actual unknown legacy owner stays pending until authenticated recovery and native hydration settle");
        }
        finally
        {
            releaseStatus.TrySetResult();
            runtime.Coordinator.Changed -= Observe;
        }
    }

    private static async Task RunEmptyAndUnknownInitialPhoneRouteCasesAsync(string contentRoot)
    {
        foreach (bool corrupt in new[] { false, true })
        {
            using var seed = new ActualAccountFixture();
            if (corrupt)
            {
                // Corrupt an actual linked credential bundle, not an orphan
                // owner row in an otherwise empty store. The latter is an
                // explicitly recoverable incomplete grant that becomes Local.
                await seed.Account.InitializeAsync();
                await seed.LinkAsync("initial-route-corrupt", "initial-route-corrupt-grant");
                Require(seed.Owner.Capture().IsValid && seed.Account.Snapshot.IsLinked,
                    "Corrupt startup fixture did not first establish a real linked grant.");
                seed.Metadata.Rows["chummer.account.grant-owner.v1"] = "{corrupt";
            }
            using var fixture = seed.Restart();
            int requestsBeforeStartup = fixture.Requests;
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: fixture.Owner,
                accountService: fixture.Account);
            await runtime.Coordinator.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            if (!corrupt) await AwaitNativeAccountOwnerInitializationAsync(runtime, fixture, fixture.Owner.Capture());
            PhoneInitialRouteReadiness observation = runtime.Coordinator.CaptureInitialPhoneRouteReadiness();
            var policy = new PhoneInitialRoutePolicy();
            Require(observation.WorkspaceId is null && !observation.HasProfile
                && fixture.Requests == requestsBeforeStartup && !runtime.Coordinator.IsBusy,
                "Empty/unknown initial owner invented a runner or required Hub.");
            if (corrupt)
                Require(observation.Kind == PhoneInitialRouteReadinessKind.Unavailable
                    && !fixture.Owner.Capture().IsValid && !policy.TryResolve(observation) && policy.IsRetired
                    && fixture.Metadata.Rows.GetValueOrDefault("chummer.account.grant-owner.v1") == "{corrupt",
                    "Corrupt credentials invented Local or retained an auto-navigation intent.");
            else
                Require(observation.Kind == PhoneInitialRouteReadinessKind.Ready
                    && policy.TryResolve(observation) && !policy.TryResolve(observation),
                    "Known empty owner did not settle exactly once on Home.");
            Console.WriteLine("PASS actual initial route " + (corrupt ? "corrupt owner stays usable Home" : "known empty owner settles on Home"));
        }
    }

    // Denial-only fault injection models a contended non-reentrant lease. It
    // never manufactures a stamp or grants authority the actual account denies.
    private sealed class DeniedStartupOwner(AndroidAccountOwnerContextAccessor actual) : IOwnerContextLeaseAccessor
    {
        internal volatile bool Deny;
        internal Action? BeforeFirstDenial;
        public OwnerScope Current => actual.Current;
        public OwnerContextStamp Capture() => actual.Capture();
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            lease = null;
            if (!Deny) return actual.TryAcquire(expected, out lease);
            Interlocked.Exchange(ref BeforeFirstDenial, null)?.Invoke();
            return false;
        }
    }
}
