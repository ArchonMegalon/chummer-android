using System.Collections.Concurrent;
using Chummer.Android.Platform;

internal static class Program
{
    private static async Task Main()
    {
        await ProviderWorkLeavesAndReturnsToTheUiContextAsync();
        await CancellationReachesProviderWorkAndRunsCleanupAsync();
        await PreCancelledWorkNeverTouchesTheProviderAsync();
        Console.WriteLine("Android document-provider work tests passed: 3");
    }

    private static async Task ProviderWorkLeavesAndReturnsToTheUiContextAsync()
    {
        using var ui = new SingleThreadSynchronizationContext();
        await ui.InvokeAsync(async () =>
        {
            int uiThreadId = Environment.CurrentManagedThreadId;
            SynchronizationContext? uiContext = SynchronizationContext.Current;
            int workerEntryThreadId = -1;
            int workerContinuationThreadId = -1;
            SynchronizationContext? workerEntryContext = uiContext;
            SynchronizationContext? workerContinuationContext = uiContext;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<int> work = DocumentProviderWorkScheduler.RunAsync(
                async cancellationToken =>
                {
                    workerEntryThreadId = Environment.CurrentManagedThreadId;
                    workerEntryContext = SynchronizationContext.Current;
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    workerContinuationThreadId = Environment.CurrentManagedThreadId;
                    workerContinuationContext = SynchronizationContext.Current;
                    return 17;
                },
                CancellationToken.None);

            await entered.Task;
            Require(!work.IsCompleted, "Provider work did not remain asynchronous while its worker was blocked.");
            Require(workerEntryThreadId != uiThreadId, "Provider work entered on the simulated MAUI UI thread.");
            Require(workerEntryContext is null, "Provider work inherited the simulated MAUI SynchronizationContext.");

            release.TrySetResult(true);
            int result = await work;
            Require(result == 17, "Provider work lost its result across the worker boundary.");
            Require(workerContinuationThreadId != uiThreadId, "Provider work resumed on the simulated MAUI UI thread.");
            Require(workerContinuationContext is null, "Provider work recaptured the simulated MAUI SynchronizationContext.");
            Require(
                Environment.CurrentManagedThreadId == uiThreadId
                && ReferenceEquals(SynchronizationContext.Current, uiContext),
                "The caller did not resume on its original UI synchronization context.");
        });
    }

    private static async Task CancellationReachesProviderWorkAndRunsCleanupAsync()
    {
        using var ui = new SingleThreadSynchronizationContext();
        using var cancellation = new CancellationTokenSource();
        await ui.InvokeAsync(async () =>
        {
            int uiThreadId = Environment.CurrentManagedThreadId;
            CancellationToken observedToken = default;
            int cleanupThreadId = -1;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleaned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task work = DocumentProviderWorkScheduler.RunAsync(
                async cancellationToken =>
                {
                    observedToken = cancellationToken;
                    entered.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        cleanupThreadId = Environment.CurrentManagedThreadId;
                        cleaned.TrySetResult(true);
                    }
                },
                cancellation.Token);

            await entered.Task;
            cancellation.Cancel();
            await RequireCancelledAsync(work, cancellation.Token);
            await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(observedToken == cancellation.Token, "Provider work did not receive the caller's cancellation token.");
            Require(cleanupThreadId != uiThreadId, "Cancelled provider cleanup ran on the simulated MAUI UI thread.");
        });
    }

    private static async Task PreCancelledWorkNeverTouchesTheProviderAsync()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int calls = 0;
        Task work = DocumentProviderWorkScheduler.RunAsync(
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.CompletedTask;
            },
            cancellation.Token);

        await RequireCancelledAsync(work, cancellation.Token);
        Require(calls == 0, "Pre-cancelled work still touched the document provider.");
    }

    private static async Task RequireCancelledAsync(Task task, CancellationToken expectedToken)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("Expected document-provider work to be cancelled.");
        }
        catch (OperationCanceledException exception)
        {
            Require(exception.CancellationToken == expectedToken, "Cancellation lost the caller's token identity.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly ManualResetEventSlim _started = new();
        private readonly Thread _thread;

        public SingleThreadSynchronizationContext()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "document-provider-ui-test"
            };
            _thread.Start();
            Require(_started.Wait(TimeSpan.FromSeconds(5)), "The simulated MAUI UI thread did not start.");
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _queue.Add((callback, state));
        }

        public Task InvokeAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(
                async _ =>
                {
                    try
                    {
                        await action();
                        completion.TrySetResult(true);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                },
                null);
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            Require(_thread.Join(TimeSpan.FromSeconds(5)), "The simulated MAUI UI thread did not stop.");
            _started.Dispose();
            _queue.Dispose();
        }

        private void Run()
        {
            SynchronizationContext.SetSynchronizationContext(this);
            _started.Set();
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
