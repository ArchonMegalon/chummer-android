using System.Collections.Concurrent;
using Chummer.Android.Native;

internal static class Program
{
    private static async Task Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            (nameof(AppearanceChangeAfterLoadingRenderGetsOneTrailingRefreshAsync), AppearanceChangeAfterLoadingRenderGetsOneTrailingRefreshAsync),
            (nameof(ActionChangeAfterLoadingRenderGetsOneTrailingRefreshAsync), ActionChangeAfterLoadingRenderGetsOneTrailingRefreshAsync),
            (nameof(NoEventAddsNoAppearanceOrActionRefreshAsync), NoEventAddsNoAppearanceOrActionRefreshAsync),
            (nameof(PreRenderBurstIsAbsorbedByExplicitAppearanceRefreshAsync), PreRenderBurstIsAbsorbedByExplicitAppearanceRefreshAsync),
            (nameof(HideAndReappearRejectsStaleGenerationCallbackAsync), HideAndReappearRejectsStaleGenerationCallbackAsync),
            (nameof(StaleActionReleaseSchedulesOnlyCurrentAppearanceAsync), StaleActionReleaseSchedulesOnlyCurrentAppearanceAsync),
            (nameof(RejectedDrainReleaseCannotLoseOrClearAnotherGeneration), RejectedDrainReleaseCannotLoseOrClearAnotherGeneration)
        ];

        foreach ((string name, Func<Task> run) in tests)
        {
            await run();
            Console.WriteLine($"PASS {name}");
        }

        Console.WriteLine($"Native page refresh tests passed: {tests.Length}");
    }

    private static async Task AppearanceChangeAfterLoadingRenderGetsOneTrailingRefreshAsync()
    {
        using var ui = new TestUiThread();
        var refreshDispatcher = new ControlledRefreshDispatcher();
        var account = new FakeAccountLinkService();
        var coordinator = new RunnerSessionCoordinator(account);
        using var renderRead = new ManualResetEventSlim();
        using var releaseRender = new ManualResetEventSlim();
        var page = new TestPage(coordinator, refreshDispatcher, ui.ThreadId)
        {
            RenderRead = renderRead,
            ReleaseRender = releaseRender
        };

        Task appearance = ui.InvokeAsync(page.Appear);
        Require(renderRead.Wait(TimeSpan.FromSeconds(5)), "Appearance did not reach its loading render.");
        await Task.Run(() => account.Publish(isLoading: false, burst: 64));
        Require(!page.LinkEnabled, "The blocked loading render changed before its trailing pass.");
        releaseRender.Set();
        await appearance;

        Require(
            refreshDispatcher.PendingCount == 1,
            "A Changed burst during appearance did not schedule exactly one trailing dispatcher pass.");
        await ui.InvokeAsync(refreshDispatcher.DrainAll);
        Require(page.RenderCount == 2, "Appearance did not render exactly one trailing pass.");
        Require(page.LinkEnabled, "Appearance left the account link disabled after recovery completed.");
        Require(page.AllRendersOnUiThread, "Appearance trailing refresh left the UI thread.");
        await ui.InvokeAsync(page.Disappear);
    }

    private static async Task ActionChangeAfterLoadingRenderGetsOneTrailingRefreshAsync()
    {
        using var ui = new TestUiThread();
        var refreshDispatcher = new ControlledRefreshDispatcher();
        var account = new FakeAccountLinkService();
        var coordinator = new RunnerSessionCoordinator(account);
        var page = new TestPage(coordinator, refreshDispatcher, ui.ThreadId);

        await ui.InvokeAsync(page.Appear);
        Require(page.RenderCount == 1, "Initial appearance did not render exactly once.");
        using var renderRead = new ManualResetEventSlim();
        using var releaseRender = new ManualResetEventSlim();
        page.RenderRead = renderRead;
        page.ReleaseRender = releaseRender;

        Task action = ui.InvokeAsync(() => page.ExecuteActionAsync(() => Task.CompletedTask));
        Require(renderRead.Wait(TimeSpan.FromSeconds(5)), "Action did not reach its loading render.");
        await Task.Run(() => account.Publish(isLoading: false, burst: 64));
        Require(!page.LinkEnabled, "The blocked action render changed before its trailing pass.");
        releaseRender.Set();
        await action;

        Require(
            refreshDispatcher.PendingCount == 1,
            "A Changed burst during the action gate did not schedule exactly one trailing dispatcher pass.");
        await ui.InvokeAsync(refreshDispatcher.DrainAll);
        Require(page.RenderCount == 3, "Action did not render exactly one trailing pass.");
        Require(page.LinkEnabled, "Action left the account link disabled after recovery completed.");
        Require(page.AllRendersOnUiThread, "Action trailing refresh left the UI thread.");
        await ui.InvokeAsync(page.Disappear);
    }

    private static async Task NoEventAddsNoAppearanceOrActionRefreshAsync()
    {
        using var ui = new TestUiThread();
        var refreshDispatcher = new ControlledRefreshDispatcher();
        var account = new FakeAccountLinkService();
        var page = new TestPage(
            new RunnerSessionCoordinator(account),
            refreshDispatcher,
            ui.ThreadId);

        await ui.InvokeAsync(page.Appear);
        Require(page.RenderCount == 1, "An event-free appearance rendered more than once.");
        Require(refreshDispatcher.PendingCount == 0, "An event-free appearance queued a trailing pass.");

        await ui.InvokeAsync(() => page.ExecuteActionAsync(() => Task.CompletedTask));
        Require(page.RenderCount == 2, "An event-free action rendered more than once.");
        Require(refreshDispatcher.PendingCount == 0, "An event-free action queued a trailing pass.");
        await ui.InvokeAsync(page.Disappear);
    }

    private static async Task PreRenderBurstIsAbsorbedByExplicitAppearanceRefreshAsync()
    {
        using var ui = new TestUiThread();
        var refreshDispatcher = new ControlledRefreshDispatcher();
        var account = new FakeAccountLinkService();
        var releaseInitialize = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new RunnerSessionCoordinator(
            account,
            () => releaseInitialize.Task);
        using var renderObserved = new ManualResetEventSlim();
        var page = new TestPage(coordinator, refreshDispatcher, ui.ThreadId)
        {
            RenderObserved = renderObserved
        };

        await ui.InvokeAsync(page.Appear);
        await Task.Run(() => account.Publish(isLoading: false, burst: 64));
        Require(
            refreshDispatcher.PendingCount == 0,
            "A pre-render Changed burst bypassed appearance suppression.");
        releaseInitialize.SetResult();
        Require(
            renderObserved.Wait(TimeSpan.FromSeconds(5)),
            "Appearance did not render after initialization was released.");
        await ui.InvokeAsync(() => { });

        Require(page.RenderCount == 1, "Pre-render events caused an extra appearance refresh.");
        Require(page.LinkEnabled, "The explicit appearance refresh missed pre-render account state.");
        Require(refreshDispatcher.PendingCount == 0, "Pre-render events survived the explicit render.");
        await ui.InvokeAsync(page.Disappear);
    }

    private static async Task HideAndReappearRejectsStaleGenerationCallbackAsync()
    {
        using var ui = new TestUiThread();
        var refreshDispatcher = new ControlledRefreshDispatcher();
        var account = new FakeAccountLinkService();
        var coordinator = new RunnerSessionCoordinator(account);
        using var renderRead = new ManualResetEventSlim();
        using var releaseRender = new ManualResetEventSlim();
        var page = new TestPage(coordinator, refreshDispatcher, ui.ThreadId)
        {
            RenderRead = renderRead,
            ReleaseRender = releaseRender
        };

        Task firstAppearance = ui.InvokeAsync(page.Appear);
        Require(renderRead.Wait(TimeSpan.FromSeconds(5)), "First appearance did not reach its render.");
        await Task.Run(() => account.Publish(isLoading: false));
        releaseRender.Set();
        await firstAppearance;
        Require(refreshDispatcher.PendingCount == 1, "First appearance did not queue its trailing pass.");

        await ui.InvokeAsync(page.Disappear);
        await ui.InvokeAsync(page.Appear);
        Require(page.RenderCount == 2, "Reappearance did not perform exactly one explicit render.");

        await Task.Run(() => account.Publish(isLoading: true));
        Require(
            refreshDispatcher.PendingCount == 2,
            "A stale dispatcher owner prevented the new appearance from scheduling its own pass.");
        await ui.InvokeAsync(refreshDispatcher.DrainAll);

        Require(page.RenderCount == 3, "A stale generation rendered or suppressed the current generation.");
        Require(!page.LinkEnabled, "The current generation did not render the latest loading state.");
        Require(page.AllRendersOnUiThread, "A hide/reappear refresh left the UI thread.");
        await ui.InvokeAsync(page.Disappear);
    }

    private static async Task StaleActionReleaseSchedulesOnlyCurrentAppearanceAsync()
    {
        using var ui = new TestUiThread();
        var refreshDispatcher = new ControlledRefreshDispatcher();
        var account = new FakeAccountLinkService();
        var page = new TestPage(
            new RunnerSessionCoordinator(account),
            refreshDispatcher,
            ui.ThreadId);
        var actionEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAction = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await ui.InvokeAsync(page.Appear);
        Task staleAction = ui.InvokeAsync(() => page.ExecuteActionAsync(async () =>
        {
            actionEntered.SetResult();
            await releaseAction.Task;
        }));
        await actionEntered.Task;

        await ui.InvokeAsync(page.Disappear);
        await ui.InvokeAsync(page.Appear);
        Require(page.RenderCount == 2, "The replacement appearance did not render exactly once.");
        await Task.Run(() => account.Publish(isLoading: false));
        Require(
            refreshDispatcher.PendingCount == 0,
            "A current event bypassed the still-claimed stale action gate.");

        releaseAction.SetResult();
        await staleAction;
        Require(
            page.RenderCount == 2,
            "The stale action rendered over the replacement appearance.");
        Require(
            refreshDispatcher.PendingCount == 1,
            "The stale action release did not schedule retained current-generation state.");
        await ui.InvokeAsync(refreshDispatcher.DrainAll);

        Require(page.RenderCount == 3, "The replacement appearance did not receive one trailing render.");
        Require(page.LinkEnabled, "The replacement appearance did not render the recovered account state.");
        Require(page.AllRendersOnUiThread, "A stale-action recovery refresh left the UI thread.");
        await ui.InvokeAsync(page.Disappear);
    }

    private static Task RejectedDrainReleaseCannotLoseOrClearAnotherGeneration()
    {
        var coalescer = new NativeRefreshCoalescer();
        coalescer.MarkPending(7);
        Require(coalescer.TrySchedulePending(7), "Generation 7 did not acquire dispatcher ownership.");

        // Model a suppression endpoint checking while a rejected drain still owns scheduling.
        Require(
            !coalescer.TrySchedulePending(7),
            "A suppression endpoint overlapped the rejected drain owner.");
        coalescer.ReleaseSchedule(7);
        Require(
            coalescer.TrySchedulePending(7),
            "The rejected drain lost dirty state when it released scheduling ownership.");
        Require(coalescer.TryTakePending(7), "Generation 7 could not consume its retained state.");
        coalescer.ReleaseSchedule(7);

        coalescer.MarkPending(8);
        Require(coalescer.TrySchedulePending(8), "Generation 8 did not acquire dispatcher ownership.");
        coalescer.AbandonThrough(8);
        coalescer.MarkPending(9);
        Require(coalescer.TrySchedulePending(9), "Generation 9 did not replace an abandoned owner.");
        coalescer.ReleaseSchedule(8);
        Require(
            !coalescer.TrySchedulePending(9),
            "A stale generation callback cleared the current dispatcher owner.");
        Require(coalescer.TryTakePending(9), "Generation 9 lost its pending refresh.");
        coalescer.ReleaseSchedule(9);
        return Task.CompletedTask;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestPage : NativePageBase
    {
        private readonly ControlledRefreshDispatcher _refreshDispatcher;
        private readonly int _uiThreadId;

        public TestPage(
            RunnerSessionCoordinator coordinator,
            ControlledRefreshDispatcher refreshDispatcher,
            int uiThreadId)
            : base(coordinator)
        {
            _refreshDispatcher = refreshDispatcher;
            _uiThreadId = uiThreadId;
        }

        public ManualResetEventSlim? RenderRead;

        public ManualResetEventSlim? ReleaseRender;

        public ManualResetEventSlim? RenderObserved;

        public int RenderCount { get; private set; }

        public bool LinkEnabled { get; private set; }

        public bool AllRendersOnUiThread { get; private set; } = true;

        public void Appear() => OnAppearing();

        public void Disappear() => OnDisappearing();

        public Task ExecuteActionAsync(Func<Task> action) => RunAsync(action);

        protected override void Refresh()
        {
            RenderCount++;
            AllRendersOnUiThread &= Environment.CurrentManagedThreadId == _uiThreadId;
            LinkEnabled = !Coordinator.Account.Snapshot.IsLoading;
            RenderObserved?.Set();
            ManualResetEventSlim? renderRead = Interlocked.Exchange(ref RenderRead, null);
            ManualResetEventSlim? releaseRender = Interlocked.Exchange(ref ReleaseRender, null);
            renderRead?.Set();
            if (releaseRender is not null
                && !releaseRender.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The deterministic render barrier was not released.");
            }
        }

        protected override bool TryDispatchCoordinatorRefresh(Action action)
            => _refreshDispatcher.Dispatch(action);
    }

    private sealed class ControlledRefreshDispatcher
    {
        private readonly ConcurrentQueue<Action> _callbacks = new();

        public int PendingCount => _callbacks.Count;

        public bool Dispatch(Action action)
        {
            _callbacks.Enqueue(action);
            return true;
        }

        public void DrainAll()
        {
            while (_callbacks.TryDequeue(out Action? callback))
            {
                callback();
            }
        }
    }

    private sealed class TestUiThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _started = new();

        public TestUiThread()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
            if (!_started.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The test UI thread did not start.");
            }
        }

        public int ThreadId { get; private set; }

        public Task InvokeAsync(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }

        public Task InvokeAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
            _started.Dispose();
        }

        private void Run()
        {
            ThreadId = Environment.CurrentManagedThreadId;
            SynchronizationContext.SetSynchronizationContext(new QueueSynchronizationContext(_queue));
            _started.Set();
            foreach (Action action in _queue.GetConsumingEnumerable())
            {
                action();
            }
        }
    }

    private sealed class QueueSynchronizationContext(BlockingCollection<Action> queue)
        : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
            => queue.Add(() => callback(state));
    }
}
