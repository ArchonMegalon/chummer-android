using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunHomeStartupFeedbackAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            await using var runtime = new NativeRewardRuntime(contentRoot);
            var page = new RunnersPage(runtime.Coordinator);
            var window = new Window(page);
            using var alerts = new IssuedPageAlerts(page, window);
            await alerts.PreflightAsync();
            ActivityIndicator Progress() => IssuedElements(page).OfType<ActivityIndicator>()
                .Single(indicator => indicator.AutomationId == "home-startup-progress");
            void AssertLoading()
            {
                Require(IssuedElements(page).OfType<Label>().Any(label => label.IsVisible
                        && label.AutomationId == "home-startup-status" && !string.IsNullOrWhiteSpace(label.Text)),
                    "Cold Home is blank before owner/workspace initialization completes.");
                Require(Progress().IsRunning && Progress().IsVisible
                    && !IssuedElements(page).OfType<Button>().Any(),
                    "Home must show progress without exposing actions before initialization.");
            }
            AssertLoading(); // Also covers the first frame before OnAppearing.
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_initializeGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await gate.WaitAsync();
            Task appearance = ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            ActivityIndicator progress = Progress();
            try
            {
                AssertLoading();
                var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ui.Post(_ => heartbeat.SetResult(), null);
                await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(2));
                IssuedPageLifecycle(page, "OnDisappearing");
                Require(!progress.IsRunning, "Departed Home left its loading animation running.");
            }
            finally { gate.Release(); }
            await appearance.WaitAsync(TimeSpan.FromSeconds(20));
            Require(!IssuedElements(page).OfType<Button>().Any() && !progress.IsRunning,
                "Late initialization rendered actions or restarted progress on a departed Home.");
            await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"))
                .WaitAsync(TimeSpan.FromSeconds(20));
            Require(IssuedElements(page).OfType<Button>().Any(button => button.AutomationId == "home-open-file")
                && !IssuedElements(page).Any(element => element.AutomationId == "home-startup-status")
                && !progress.IsRunning && alerts.Titles.Count == 0,
                "Returning Home did not replace startup progress with settled actions.");
            IssuedPageLifecycle(page, "OnDisappearing");
            Console.WriteLine("PASS Home first frame, held initialization, UI heartbeat, departure and settled reappearance");
        });
    }

    public static async Task RunInitialPhoneRouteCasesAsync(string contentRoot)
    {
        RunInitialPhoneRoutePolicyCases();
        await RunStartupShellReuseAsync(contentRoot);
        await RunDelayedInitialPhoneRouteCaseAsync(contentRoot, navigateAway: false);
        await RunDelayedInitialPhoneRouteCaseAsync(contentRoot, navigateAway: true);
        await RunUnknownRecoveringInitialPhoneRouteCaseAsync(contentRoot);
        await RunEmptyAndUnknownInitialPhoneRouteCasesAsync(contentRoot);
    }

    public static async Task RunStartupShellReuseAsync(string contentRoot)
    {
        foreach (var (method, created) in new[]
        {
            (CharacterCreationBuildMethods.Priority, false),
            (CharacterCreationBuildMethods.SumToTen, false),
            (CharacterCreationBuildMethods.Karma, false),
            (CharacterCreationBuildMethods.LifeModules, false),
            (CharacterCreationBuildMethods.Priority, true)
        })
        {
            int rosterReads = 0;
            var owners = new ControlledLinkedOwner();
            owners.Set(OwnerScope.LocalSingleUser);
            var metrics = new List<BootstrapProductionStage>();
            ProductionFinalizationLoadProbe? finalization = null;
            await using var runtime = new NativeRewardRuntime(contentRoot,
                // Exercise the same owner-bound Creation projections as MauiProgram.
                // The default reduced factory cannot establish real restore behavior.
                productionCreationOverview: true,
                linkedOwners: owners,
                finalizationDecorator: actual => finalization = new(actual, owners.Capture(), metrics),
                beforeShellWorkspaceList: () => Interlocked.Increment(ref rosterReads));
            var imported = await runtime.Client.ImportAsync(new WorkspaceImportDocument($"""
                <character><name>Cold restore fixture</name><gameedition>SR5</gameedition>
                <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings><metatype>Human</metatype>
                <buildmethod>{method}</buildmethod><createdversion>5.225.0</createdversion>
                <appversion>5.225.0</appversion><created>{created}</created><karma>30</karma><nuyen>1000</nuyen>
                <improvements/><contacts/><expenses/><notes>Restoration must not edit this.</notes></character>
                """, "sr5"), default);
            Require((await runtime.Client.SaveAsync(imported.Id, default)).Success, "Cold fixture did not save.");
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            string before = JsonSerializer.Serialize(store.Get(imported.Id).Value!);
            Require(runtime.Presenter.State.Profile is null, "Cold fixture accidentally initialized its presenter.");
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            await runtime.Coordinator.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            var state = runtime.Coordinator.State;
            Console.WriteLine($"Cold restore method={method} created={created}: shell roster reads={rosterReads}, elapsedMs={elapsed.ElapsedMilliseconds}");
            foreach (var metric in metrics)
                Console.WriteLine("STARTUP_PRODUCTION_STAGE " + JsonSerializer.Serialize(metric));
            Require(finalization is not null && finalization.LoadCalls == (created ? 0 : 1)
                    && finalization.ReviewCalls == 0 && finalization.ConfirmCalls == 0 && finalization.LookupCalls == 0,
                "Cold restore must load the actual Creation finalization projection once, without executing an action.");
            Require(state is { IsBusy: false, Error: null, Profile: not null }
                && state.Profile.Created == created && state.WorkspaceId == imported.Id
                && state.Profile.BuildMethod == method
                && runtime.Shell.State.ActiveWorkspaceId == imported.Id
                && runtime.Shell.State.OwnerContext == state.DisplayOwnerContext
                && state.Session.OwnerContext == state.DisplayOwnerContext
                && runtime.Coordinator.CaptureInitialPhoneRouteReadiness().Kind == PhoneInitialRouteReadinessKind.Ready,
                "Cold restore lost its exact owner, runner, method or ready route.");
            Require((state.CreationFoundation is not null)
                    == (!created && method == CharacterCreationBuildMethods.LifeModules),
                "Production restore must retain Life Modules foundation without loading it for other methods.");
            Require(JsonSerializer.Serialize(store.Get(imported.Id).Value!) == before,
                "Startup shell reuse changed the persisted runner.");
            Require(rosterReads == 1,
                $"Cold restore repeated the presenter's completed shell roster read: {rosterReads} reads.");
            await runtime.Coordinator.InitializeAsync();
            Require(rosterReads == 1, "Already initialized startup repeated its roster read.");
            var owner = state.DisplayOwnerContext!.Value;
            var shell = runtime.Shell.State;
            Require(RunnerSessionCoordinator.CanReuseRestoredShellContext(owner, imported.Id, state, shell),
                "Completed exact startup did not admit reuse.");
            foreach (var invalid in new[]
            {
                state with { IsBusy = true }, state with { Error = "Incomplete load" },
                state with { Profile = null }, state with { WorkspaceId = null },
                state with { DisplayOwnerContext = null },
                state with { DisplayOwnerContext = new OwnerContextStamp(owner.Owner,
                    owner.AuthorityInstanceId, owner.TransitionRevision + 2) }
            })
                Require(!RunnerSessionCoordinator.CanReuseRestoredShellContext(owner, imported.Id, invalid, shell),
                    "Incomplete or owner-ABA display incorrectly skipped Shell synchronization.");
            foreach (var invalid in new[]
            {
                shell with { IsBusy = true }, shell with { Error = "Incomplete Shell" },
                shell with { ActiveWorkspaceId = null }, shell with { OpenWorkspaces = [] },
                shell with { OwnerContext = null },
                shell with { OwnerContext = new OwnerContextStamp(owner.Owner,
                    owner.AuthorityInstanceId, owner.TransitionRevision + 2) }
            })
                Require(!RunnerSessionCoordinator.CanReuseRestoredShellContext(owner, imported.Id, state, invalid),
                    "Incomplete or owner-ABA Shell incorrectly skipped synchronization.");
            Require(!RunnerSessionCoordinator.CanReuseRestoredShellContext(null, imported.Id, state, shell),
                "Unbound startup invented completed owner authority.");
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_shellSyncGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await gate.WaitAsync();
            Task retained;
            const string preferenceKey = "chummer.android.selected-workspace.v1";
            try
            {
                retained = (Task)typeof(RunnerSessionCoordinator).GetMethod("FinalizeShellAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime.Coordinator,
                    [false, CancellationToken.None, state])!;
                Require(!retained.IsCompleted, "Retained startup refresh did not wait for Shell admission.");
                await runtime.Presenter.LoadAsync(imported.Id, default);
                Require(!ReferenceEquals(runtime.Coordinator.State, state), "Reload did not replace the display.");
                runtime.Settings.Set(preferenceKey, "newer-selection");
            }
            finally { gate.Release(); }
            await retained;
            Require(runtime.Settings.Get(preferenceKey, string.Empty) == "newer-selection",
                "Queued retained refresh overwrote a newer selection after display replacement.");
            Require(JsonSerializer.Serialize(store.Get(imported.Id).Value!) == before,
                "Retained refresh/reload changed the saved runner.");
            Console.WriteLine("PASS cold runner restore reuses completed owner-bound shell synchronization without mutation");
        }
        await RunStartupShellFailureFallbackAsync(contentRoot);
    }

    private static async Task RunStartupShellFailureFallbackAsync(string contentRoot)
    {
        int rosterReads = 0;
        await using var runtime = new NativeRewardRuntime(contentRoot, beforeShellWorkspaceList: () =>
        {
            Interlocked.Increment(ref rosterReads);
            throw new IOException("Deliberate startup Shell read failure.");
        });
        var imported = await runtime.Client.ImportAsync(new WorkspaceImportDocument("""
            <character><name>Failed startup fixture</name><gameedition>SR5</gameedition>
            <settings>223a11ff-80e0-428b-89a9-6ef1c243b8b6</settings><metatype>Human</metatype>
            <buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion>
            <appversion>5.225.0</appversion><created>False</created>
            <karma>30</karma><nuyen>1000</nuyen><improvements/><contacts/><expenses/></character>
            """, "sr5"), default);
        Require((await runtime.Client.SaveAsync(imported.Id, default)).Success, "Failed-startup fixture did not save.");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        string before = JsonSerializer.Serialize(store.Get(imported.Id).Value!);
        bool failedClosed = false;
        try { await runtime.Coordinator.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (IOException error) when (error.Message == "Deliberate startup Shell read failure.")
        {
            failedClosed = true;
        }
        Require(failedClosed && rosterReads == 2
            && runtime.Coordinator.CaptureInitialPhoneRouteReadiness().Kind != PhoneInitialRouteReadinessKind.Ready,
            "Failed presenter synchronization was reused or exposed a ready runner.");
        Require(JsonSerializer.Serialize(store.Get(imported.Id).Value!) == before,
            "Failed startup synchronization changed saved bytes.");
        Console.WriteLine("PASS real failed presenter Shell read retains full sync and fail-closed startup without mutation");
    }

    private static HomePage RenderAccountHome(NativeRewardRuntime runtime)
    {
        var page = new HomePage(runtime.Coordinator);
        typeof(HomePage).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
        return page;
    }

    private static async Task AssertAccountLinkWaitingAsync(NativeRewardRuntime runtime)
    {
        Require(runtime.Coordinator.Account.IsLoading, "Recovery test missed the actual Loading state.");
        var page = RenderAccountHome(runtime);
        var link = IssuedElements(page).OfType<Button>().Single(button => button.Text ==
            PhoneStrings.Get("LinkAccount", "Link account"));
        Require(!link.IsEnabled, "Home offers account linking while actual startup recovery is still active.");
        Require(IssuedElements(page).OfType<ActivityIndicator>().Any(indicator => indicator.IsRunning),
            "Home gives no visible account-recovery progress while linking is disabled.");
        // A retained control can still deliver an old event. It must not begin
        // another link or overwrite credentials while the real gate is held.
        await runtime.Coordinator.BeginAccountLinkAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Require(runtime.Coordinator.Notice == "Account recovery is still finishing.",
            "The stale link event did not report the actual pending recovery.");
    }

    private static void AssertAccountLinkSettled(NativeRewardRuntime runtime, bool linked)
    {
        Require(!runtime.Coordinator.Account.IsLoading && runtime.Coordinator.Account.IsLinked == linked
            && runtime.Coordinator.Notice != "Account recovery is still finishing.",
            "Finished account recovery left a permanent waiting notice.");
        var notice = typeof(RunnerSessionCoordinator).GetField("_notice", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object? previousNotice = notice.GetValue(runtime.Coordinator);
        try
        {
            // Simulate completion between the old handler's Loading check and
            // its notice assignment. No account transition or relink is needed.
            notice.SetValue(runtime.Coordinator, "Account recovery is still finishing.");
            Require(runtime.Coordinator.Notice != "Account recovery is still finishing.",
                "A late stale-control notice resurrected completed account recovery.");
            notice.SetValue(runtime.Coordinator, "Unrelated saved action.");
            Require(runtime.Coordinator.Notice == "Unrelated saved action.",
                "Account completion hid an unrelated action notice.");
        }
        finally { notice.SetValue(runtime.Coordinator, previousNotice); }
        var page = RenderAccountHome(runtime);
        Require(!IssuedElements(page).OfType<ActivityIndicator>().Any(indicator => indicator.IsRunning),
            "Home retained account recovery progress after the real operation finished.");
        var links = IssuedElements(page).OfType<Button>().Where(button => button.Text ==
            PhoneStrings.Get("LinkAccount", "Link account")).ToArray();
        Require(linked ? links.Length == 0 : links is [{ IsEnabled: true }],
            "Home did not replace pending recovery with the actual linked/unlinked action.");
        Console.WriteLine("PASS account recovery UI settles without relinking, lost readiness or stale waiting notice");
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
            await AssertAccountLinkWaitingAsync(runtime);

            var activationGate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await activationGate.WaitAsync();
            try
            {
                owners.Deny = false;
                if (Interlocked.Exchange(ref heldAccountGate, 0) == 1) accountGate.Release();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                AssertAccountLinkSettled(runtime, linked: false);
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
            await AssertAccountLinkWaitingAsync(runtime);
            var activationGate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
            await activationGate.WaitAsync();
            try
            {
                releaseStatus.TrySetResult();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                AssertAccountLinkSettled(runtime, linked: true);
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
