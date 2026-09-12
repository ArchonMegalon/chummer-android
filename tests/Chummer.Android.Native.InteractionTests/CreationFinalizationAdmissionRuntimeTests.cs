using System.Reflection;
using System.Runtime.ExceptionServices;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using FinalizationRead = Chummer.Contracts.Characters.CharacterCreationFinalizationResult<Chummer.Contracts.Characters.CharacterCreationFinalizationState>;
using FinalizationQueue = Chummer.Android.Native.LatestBackgroundProjectionQueue<Chummer.Android.Native.CreationFinalizationProjectionBinding, Chummer.Contracts.Characters.CharacterCreationFinalizationResult<Chummer.Contracts.Characters.CharacterCreationFinalizationState>>;

internal static partial class AfterRunAuthorityHarness
{
    // This is a managed deterministic scheduling regression over real Core/file/
    // Android account services. It does not identify the hosted contender or
    // replace the real seven-journey/device qualification.
    public static async Task RunCreationFinalizationAdmissionCasesAsync(string contentRoot, bool baselineOnly = false)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply the explicit canonical Core content root.", nameof(contentRoot));
        Require(typeof(RunnerSessionCoordinator).GetMethod("RefreshApi36ProofWorkspaceAuthorityAsync",
            BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "The real revision3 fixture requires Debug and ChummerNativeProofCaptureTests=true.");
        PropertyInfo proof = typeof(RunnerSessionCoordinator).GetProperty("DebugWorkspaceAuthority")
            ?? throw new InvalidOperationException("Actual Debug capture is required.");
        if (!baselineOnly) _ = FinalizationAdmissionWorker();
        using var ui = new IssuedPageUiContext();
        var finalizer = new FinalizationAdmissionProbe();
        bool prior = AndroidE2EAuthority.Enabled;
        try
        {
            AndroidE2EAuthority.ConfigureForCurrentProcess(true);
            await ui.RunAsync(async () =>
            {
                await RunActualResourcesPrerequisiteRebindAsync(contentRoot, ui, proof,
                    async (runtime, account, prerequisite, trace, issued) =>
                    {
                        issued = await FinalizationOverlapAsync(runtime, account, prerequisite, trace,
                            finalizer, issued, protectedWorker: false, cancelQueue: false);
                        if (!baselineOnly)
                        {
                            issued = await FinalizationOverlapAsync(runtime, account, prerequisite, trace,
                                finalizer, issued, protectedWorker: true, cancelQueue: false);
                            issued = await FinalizationOverlapAsync(runtime, account, prerequisite, trace,
                                finalizer, issued, protectedWorker: true, cancelQueue: true);
                            issued = await FinalizationCanceledAdmissionAsync(runtime, account, prerequisite,
                                trace, finalizer, issued);
                            await FinalizationThrownLoadCleanupAsync(runtime, account, prerequisite,
                                trace, finalizer, issued);
                        }
                    }, finalizer);
                ui.AssertHealthy();
                Console.WriteLine(baselineOnly
                    ? "PASS actual ungated finalizer/Android owner overlap control: 1"
                    : "PASS actual finalizer admission cases: 5 (ungated, protected, queue cancellation, canceled wait, thrown load)");
            });
        }
        finally { AndroidE2EAuthority.ConfigureForCurrentProcess(prior); }
    }

    private static readonly TimeSpan FinalizationAdmissionTestBound = TimeSpan.FromSeconds(90);

    private static async Task<CharacterCreationPrerequisiteState> FinalizationCanceledAdmissionAsync(
        NativeRewardRuntime runtime, ActualAccountFixture account, ResourcesRebindCoreProbe prerequisite,
        ResourcesRebindTrace trace, FinalizationAdmissionProbe finalizer, CharacterCreationPrerequisiteState issued)
    {
        trace.Phase = "finalization-canceled-wait";
        var original = runtime.Coordinator.State;
        var owner = account.Owner.Capture();
        var store = runtime.Services.GetRequiredService<IWorkspaceStore>();
        var before = store.Get(runtime.Id).Value!;
        int finalizerCalls = finalizer.LoadCalls, prerequisiteCalls = prerequisite.LoadCalls;
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        MethodInfo workerMethod = FinalizationAdmissionWorker();
        using var cancellation = new CancellationTokenSource();
        using var observationDeadline = new CancellationTokenSource(FinalizationAdmissionTestBound);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A dedicated thread makes its blocking state observable without guessing
        // whether a Task.Run delegate has merely not been scheduled. There are no
        // other waits between this entry signal and the real worker's gate.Wait.
        var thread = new Thread(() =>
        {
            entered.TrySetResult();
            try
            {
                workerMethod.Invoke(runtime.Coordinator, new object[] { original, cancellation.Token });
                finished.TrySetResult(null);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { finished.TrySetResult(error.InnerException); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { finished.TrySetResult(error); }
        }) { IsBackground = true, Name = "actual-finalization-admission-wait" };
        Require(await gate.WaitAsync(FinalizationAdmissionTestBound), "Could not hold the actual admission gate.");
        bool started = false;
        try
        {
            thread.Start(); started = true;
            await entered.Task.WaitAsync(FinalizationAdmissionTestBound);
            while ((thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0)
            {
                Require(!finished.Task.IsCompleted, "The protected worker did not wait for held admission.");
                observationDeadline.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            Require(!finished.Task.IsCompleted && gate.CurrentCount == 0
                && finalizer.LoadCalls == finalizerCalls && prerequisite.LoadCalls == prerequisiteCalls,
                "Waiting for admission already entered a Core service.");
            cancellation.Cancel();
            Exception? failure = await finished.Task.WaitAsync(FinalizationAdmissionTestBound);
            Require(failure is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token
                && gate.CurrentCount == 0 && finalizer.LoadCalls == finalizerCalls
                && prerequisite.LoadCalls == prerequisiteCalls && account.Owner.Capture() == owner,
                "Canceled admission must exit without acquiring/releasing someone else's gate or calling Core.");
        }
        finally
        {
            cancellation.Cancel();
            gate.Release();
            if (started)
            {
                await finished.Task.WaitAsync(FinalizationAdmissionTestBound);
                Require(thread.Join(TimeSpan.FromSeconds(5)), "The canceled admission thread did not exit.");
            }
        }
        var recovered = await runtime.Coordinator.RevalidateCreationPrerequisiteAsync(issued);
        Require(recovered is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null }
            && recovered.Value.Binding.ContentRevision == 3 && recovered.Value.Binding.SavedRevision == 3
            && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(recovered.Value)
            && prerequisite.LoadCalls == prerequisiteCalls + 1 && finalizer.LoadCalls == finalizerCalls
            && gate.CurrentCount == 1 && account.Owner.Capture() == owner && account.Requests == 0,
            "Canceled waiting leaked admission or prevented one fresh same-owner child load.");
        var after = store.Get(runtime.Id).Value!;
        Require(after.LastUpdatedUtc == before.LastUpdatedUtc
            && FinalizationDocumentDigest(after) == FinalizationDocumentDigest(before),
            "Canceled admission or its read-only child changed persisted data.");
        Console.WriteLine("PASS actual finalization admission: canceled wait");
        return recovered.Value!;
    }

    private static async Task FinalizationThrownLoadCleanupAsync(
        NativeRewardRuntime runtime, ActualAccountFixture account, ResourcesRebindCoreProbe prerequisite,
        ResourcesRebindTrace trace, FinalizationAdmissionProbe finalizer, CharacterCreationPrerequisiteState issued)
    {
        trace.Phase = "finalization-thrown-load";
        var original = runtime.Coordinator.State;
        var owner = account.Owner.Capture();
        var store = runtime.Services.GetRequiredService<IWorkspaceStore>();
        var before = store.Get(runtime.Id).Value!;
        int finalizerCalls = finalizer.LoadCalls, prerequisiteCalls = prerequisite.LoadCalls;
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        Require(gate.CurrentCount == 1 && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(issued),
            "Thrown-load control needs a current issued child and unoccupied gate.");
        var injected = new InvalidOperationException("test-only fault after the actual finalizer returned");
        finalizer.FailAfterNextActualLoad(injected);
        Exception? observed = null;
        Task<FinalizationRead> invocation = Task.Run(
            () => InvokeFinalizationAdmissionWorker(runtime.Coordinator, original, default));
        try
        {
            await invocation.WaitAsync(FinalizationAdmissionTestBound);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { observed = error; }
        finally
        {
            if (!invocation.IsCompleted)
            {
                try { await invocation.WaitAsync(FinalizationAdmissionTestBound); }
                catch (Exception error) when (error is not OutOfMemoryException && invocation.IsCompleted) { }
            }
        }
        Require(ReferenceEquals(observed, injected) && finalizer.LoadCalls == finalizerCalls + 1
            && finalizer.LastResult is { Value: not null } actual
            && actual.Value.Binding.ContentRevision == 3 && actual.Value.Binding.SavedRevision == 3
            && !actual.Blockers.Contains(CharacterCreationFinalizationBlockers.WorkspaceUnavailable)
            && prerequisite.LoadCalls == prerequisiteCalls && gate.CurrentCount == 1,
            "The worker did not propagate the exact post-Core fault after releasing its gate.");
        var recovered = await runtime.Coordinator.RevalidateCreationPrerequisiteAsync(issued);
        Require(recovered is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null }
            && recovered.Value.Binding.ContentRevision == 3 && recovered.Value.Binding.SavedRevision == 3
            && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(recovered.Value)
            && prerequisite.LoadCalls == prerequisiteCalls + 1 && finalizer.LoadCalls == finalizerCalls + 1
            && gate.CurrentCount == 1 && account.Owner.Capture() == owner && account.Requests == 0,
            "A thrown finalizer stranded its gate/owner lease or changed one fresh child load.");
        var after = store.Get(runtime.Id).Value!;
        Require(after.LastUpdatedUtc == before.LastUpdatedUtc
            && FinalizationDocumentDigest(after) == FinalizationDocumentDigest(before),
            "Post-Core exception cleanup changed the persisted workspace.");
        Console.WriteLine("PASS actual finalization admission: thrown load cleanup");
    }

    private static async Task<CharacterCreationPrerequisiteState> FinalizationOverlapAsync(
        NativeRewardRuntime runtime, ActualAccountFixture account, ResourcesRebindCoreProbe prerequisite,
        ResourcesRebindTrace trace, FinalizationAdmissionProbe finalizer,
        CharacterCreationPrerequisiteState issued, bool protectedWorker, bool cancelQueue)
    {
        string name = !protectedWorker ? "ungated-control" : cancelQueue ? "queue-canceled" : "protected-overlap";
        trace.Phase = "finalization-" + name;
        var original = runtime.Coordinator.State;
        var owner = account.Owner.Capture();
        var store = runtime.Services.GetRequiredService<IWorkspaceStore>();
        var before = store.Get(runtime.Id).Value
            ?? throw new InvalidOperationException("The actual Resources fixture disappeared.");
        Require(before.ContentRevision == 3 && before.SavedRevision == 3
            && original.DisplayOwnerContext == owner && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(issued),
            "Overlap must begin with the actual same-owner revision3 issued state.");
        if (original.CreationWizard is not { } wizard
            || !CreationDashboardProjectionBinding.TryCreate(original, wizard, out var binding) || binding is null)
            throw new InvalidOperationException("The actual display cannot supply the dashboard finalization key.");
        var key = new CreationFinalizationProjectionBinding(binding, original.DisplayOwnerContext,
            original.ActiveTabId, original.ActiveActionId, original.ActiveSectionId);
        using var queue = new FinalizationQueue();
        using var stop = finalizer.Arm();
        var loaderExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completed = 0, failed = 0;
        queue.Completed += _ => Interlocked.Increment(ref completed);
        queue.Failed += _ => Interlocked.Increment(ref failed);
        int actualFinalizationLoads = finalizer.LoadCalls;
        Task<CharacterCreationFoundationResult<CharacterCreationPrerequisiteState>>? child = null;
        var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Coordinator)!;
        Require(gate.CurrentCount == 1, "Another coordinator operation was live before the controlled overlap.");
        Require(queue.TryRequest(key, (_, cancellationToken) =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FinalizationRead result = protectedWorker
                    ? InvokeFinalizationAdmissionWorker(runtime.Coordinator, original, cancellationToken)
                    : runtime.Coordinator.LoadCreationFinalization(original);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            finally { loaderExited.TrySetResult(); }
        }, out var request), "The real background queue did not admit its single request.");
        try
        {
            await stop.Entered.Task.WaitAsync(FinalizationAdmissionTestBound);
            Require(finalizer.LoadCalls == actualFinalizationLoads + 1 && account.Owner.Capture() == owner,
                "The held real finalizer changed owner or entered Core more than once.");
            // The source hook is reached only while the real Android lease is
            // active. Currentness alone remains true during this credential hold.
            Require(runtime.Coordinator.IsCreationPrerequisiteStateCurrent(issued),
                "The negative control lost native currentness before competing for owner admission.");
            bool nestedAcquired = account.Owner.TryAcquire(owner, out var unexpectedLease);
            unexpectedLease?.Dispose();
            Require(!nestedAcquired,
                "A nested acquisition bypassed the actual Android credential lease.");
            Require(gate.CurrentCount == (protectedWorker ? 0 : 1),
                "Dashboard finalization did not have the expected shared-gate ownership.");
            int childLoads = prerequisite.LoadCalls;
            child = runtime.Coordinator.RevalidateCreationPrerequisiteAsync(issued);
            if (protectedWorker)
            {
                // Gate possession is established above: this is not a timed
                // absence assertion or a guess about thread-pool scheduling.
                Require(!child.IsCompleted && prerequisite.LoadCalls == childLoads,
                    "The child entered Core instead of awaiting real finalizer completion.");
                if (cancelQueue)
                {
                    queue.Cancel();
                    Require(!loaderExited.Task.IsCompleted && !child.IsCompleted
                        && gate.CurrentCount == 0 && prerequisite.LoadCalls == childLoads
                        && account.Owner.Capture() == owner,
                        "Cancel released admission before the synchronous finalizer actually returned.");
                    Require(!queue.TryTake(request, out _, out _),
                        "A canceled, still-running finalization was accepted.");
                }
            }
            else
            {
                var rejected = await child.WaitAsync(FinalizationAdmissionTestBound);
                Require(prerequisite.LoadCalls == childLoads + 1 && rejected.Value is null
                    && rejected.Outcome == CharacterCreationFoundationOutcomes.Blocked
                    && rejected.Blockers.SequenceEqual(new[] { CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable })
                    && account.Owner.Capture() == owner && !loaderExited.Task.IsCompleted,
                    "Ungated overlap did not reproduce exact same-stamp Core workspace-unavailable admission.");
            }
            stop.Release();
            await loaderExited.Task.WaitAsync(FinalizationAdmissionTestBound);
            await DrainFinalizationQueueAsync(queue);
            Require(finalizer.LastResult is { Value: not null } actual
                && actual.Value.Binding.ContentRevision == 3 && actual.Value.Binding.SavedRevision == 3
                && !actual.Blockers.Contains(CharacterCreationFinalizationBlockers.WorkspaceUnavailable),
                "The actual finalizer never completed its genuine revision3 read.");
            if (cancelQueue)
                Require(completed == 0 && failed == 0 && !queue.TryTake(request, out _, out _),
                    "The canceled finalization published or admitted a stale queue outcome.");
            else
                Require(completed == 1 && failed == 0
                    && queue.TryTake(request, out var accepted, out var error) && error is null
                    && ReferenceEquals(accepted, finalizer.LastResult),
                    "The queue did not retain the actual finalizer's one unmodified result.");

            CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> recovered;
            if (protectedWorker)
            {
                recovered = await child.WaitAsync(FinalizationAdmissionTestBound);
                Require(prerequisite.LoadCalls == childLoads + 1,
                    "Protected revalidation did not enter Core exactly once after completion.");
            }
            else
            {
                int afterRejected = prerequisite.LoadCalls;
                recovered = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
                Require(prerequisite.LoadCalls == afterRejected + 1,
                    "Same-stamp recovery did not perform one fresh real load after lease release.");
            }
            Require(recovered is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null }
                && recovered.Value.Binding.ContentRevision == 3 && recovered.Value.Binding.SavedRevision == 3
                && runtime.Coordinator.IsCreationPrerequisiteStateCurrent(recovered.Value)
                && account.Owner.Capture() == owner && account.Requests == 0 && gate.CurrentCount == 1,
                "The same actual owner/store did not recover without mutation or provider activity.");
            var after = store.Get(runtime.Id).Value!;
            Require(after.LastUpdatedUtc == before.LastUpdatedUtc
                && FinalizationDocumentDigest(after) == FinalizationDocumentDigest(before),
                "A read-only overlap modified the real Resources/prerequisite workspace.");
            Console.WriteLine("PASS actual finalization admission: " + name);
            return recovered.Value!;
        }
        finally
        {
            stop.Release();
            await loaderExited.Task.WaitAsync(FinalizationAdmissionTestBound);
            await DrainFinalizationQueueAsync(queue);
            if (child is not null) await child.WaitAsync(FinalizationAdmissionTestBound);
        }
    }

    private static MethodInfo FinalizationAdmissionWorker()
        => typeof(RunnerSessionCoordinator).GetMethod("LoadCreationFinalizationInBackground",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(CharacterOverviewState), typeof(CancellationToken) }, null)
            ?? throw new InvalidOperationException("Requested protected lane requires the real dashboard admission worker.");

    private static FinalizationRead InvokeFinalizationAdmissionWorker(RunnerSessionCoordinator coordinator,
        CharacterOverviewState original, CancellationToken cancellationToken)
    {
        try { return (FinalizationRead)FinalizationAdmissionWorker().Invoke(coordinator, new object[] { original, cancellationToken })!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static async Task DrainFinalizationQueueAsync(FinalizationQueue queue)
    {
        // Join the actual queue's execution finally, including canceled work
        // which intentionally emits neither Completed nor Failed.
        var execution = (SemaphoreSlim)typeof(FinalizationQueue).GetField("_executionGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        Require(await execution.WaitAsync(FinalizationAdmissionTestBound),
            "The actual background queue did not finish its execution finally.");
        execution.Release();
    }

    private sealed class FinalizationAdmissionStop : IDisposable
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        internal void Hold()
        {
            Entered.TrySetResult();
            if (!_release.Wait(FinalizationAdmissionTestBound))
                throw new TimeoutException("The explicit test source gate was not released.");
        }
        internal void Release() => _release.Set();
        public void Dispose() => _release.Dispose();
    }

    private sealed class FinalizationAdmissionProbe : IOwnerBoundCharacterCreationFinalizationService
    {
        private IOwnerBoundCharacterCreationFinalizationService? _actual;
        private ResourcesRebindOwnerProbe? _owner;
        private FinalizationAdmissionStop? _stop;
        private Exception? _afterActualLoadFailure;
        private int _loadCalls;
        internal int LoadCalls => Volatile.Read(ref _loadCalls);
        internal FinalizationRead? LastResult { get; private set; }
        internal IOwnerBoundCharacterCreationFinalizationService Bind(IOwnerBoundCharacterCreationFinalizationService actual)
        {
            Require(actual is OwnerBoundCharacterCreationFinalizationService, "Actual Core finalizer is required.");
            _actual = actual; return this;
        }
        internal void Observe(IWorkspaceStore store, ResourcesRebindOwnerProbe owner,
            ICharacterFileQueries queries, ICharacterSourceDataResolver resolver)
        {
            _owner = owner;
            _actual = new OwnerBoundCharacterCreationFinalizationService(store, owner, queries,
                new SourceProbe(resolver, this));
        }
        internal FinalizationAdmissionStop Arm()
        {
            var stop = new FinalizationAdmissionStop();
            Require(Interlocked.CompareExchange(ref _stop, stop, null) is null, "An earlier source gate remains armed.");
            return stop;
        }
        internal void FailAfterNextActualLoad(Exception failure)
            => Require(Interlocked.CompareExchange(ref _afterActualLoadFailure, failure, null) is null,
                "A previous post-Core fault remains armed.");
        public FinalizationRead Load(OwnerContextStamp owner, CharacterCreationFinalizationLoadRequest request)
        {
            Interlocked.Increment(ref _loadCalls);
            var actual = _actual!.Load(owner, request);
            LastResult = actual;
            if (Interlocked.Exchange(ref _afterActualLoadFailure, null) is { } failure)
            {
                Require(_owner!.ActiveLeases == 0, "Post-Core fault must run after the real owner lease is disposed.");
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return actual;
        }
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(OwnerContextStamp owner,
            CharacterCreationFinalizationReviewRequest request) => _actual!.Review(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(OwnerContextStamp owner,
            CharacterCreationFinalizationConfirmRequest request) => _actual!.Confirm(owner, request);
        public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(OwnerContextStamp owner,
            CharacterCreationFinalizationReceiptLookupRequest request) => _actual!.LookupReceipt(owner, request);

        private sealed class SourceProbe : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
        {
            private readonly ICharacterSourceDataResolver _actual;
            private readonly ICharacterSourceDataResolverOperationScopeFactory _factory;
            private readonly FinalizationAdmissionProbe _probe;
            internal SourceProbe(ICharacterSourceDataResolver actual, FinalizationAdmissionProbe probe)
            {
                _actual = actual; _probe = probe;
                _factory = actual as ICharacterSourceDataResolverOperationScopeFactory
                    ?? throw new InvalidOperationException("The real resolver's source operation scope is required.");
            }
            public ICharacterSourceDataContext? TryCreateContext(string xml) => _actual.TryCreateContext(xml);
            public ICharacterSourceDataResolverOperationScope CreateOperationScope() => new Scope(_factory.CreateOperationScope(), _probe);
            private sealed class Scope(ICharacterSourceDataResolverOperationScope actual, FinalizationAdmissionProbe probe)
                : ICharacterSourceDataResolverOperationScope
            {
                public ICharacterSourceDataContext? TryCreateContext(string xml)
                {
                    var context = actual.TryCreateContext(xml);
                    if (Interlocked.Exchange(ref probe._stop, null) is { } stop)
                    {
                        Require(context is not null && probe._owner!.IsLeasedThread,
                            "The gate requires a real source context inside the actual Android owner lease.");
                        stop.Hold();
                    }
                    return context;
                }
                public void Dispose() => actual.Dispose();
            }
        }
    }
}
