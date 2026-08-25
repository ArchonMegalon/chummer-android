namespace Chummer.Android.Native;

/// <summary>
/// Identifies one background projection attempt.  The monotonically increasing
/// generation is part of the acceptance boundary so a late result for an older
/// key can never replace a newer request.
/// </summary>
public sealed record BackgroundProjectionRequest<TKey>(
    long Generation,
    TKey Key)
    where TKey : notnull;

public sealed record BackgroundProjectionCompletion<TKey, TResult>(
    BackgroundProjectionRequest<TKey> Request,
    TResult Result)
    where TKey : notnull;

public sealed record BackgroundProjectionFailure<TKey>(
    BackgroundProjectionRequest<TKey> Request,
    Exception Error)
    where TKey : notnull;

/// <summary>
/// Runs synchronous, I/O-heavy projection work away from the caller and admits
/// only the latest generation.  Consumers must still marshal UI mutations to
/// their dispatcher and call <see cref="TryAccept"/> at that final boundary.
/// </summary>
public sealed class LatestBackgroundProjectionQueue<TKey, TResult> : IDisposable
    where TKey : notnull
{
    private sealed class Work
    {
        private int _executionCompleted;
        private int _disposalRequested;
        private int _outcomeKind;
        private TResult? _result;
        private Exception? _error;

        public Work(
            BackgroundProjectionRequest<TKey> request,
            CancellationTokenSource cancellation)
        {
            Request = request;
            Cancellation = cancellation;
        }

        public BackgroundProjectionRequest<TKey> Request { get; }

        public CancellationTokenSource Cancellation { get; }

        public bool IsResultReady => Volatile.Read(ref _outcomeKind) != 0;

        public void MarkResultReady(TResult result)
        {
            _result = result;
            Volatile.Write(ref _outcomeKind, 1);
        }

        public void MarkFailureReady(Exception error)
        {
            _error = error;
            Volatile.Write(ref _outcomeKind, 2);
        }

        public bool TryReadOutcome(out TResult result, out Exception? error)
        {
            int outcomeKind = Volatile.Read(ref _outcomeKind);
            if (outcomeKind == 1)
            {
                result = _result!;
                error = null;
                return true;
            }
            if (outcomeKind == 2)
            {
                result = default!;
                error = _error
                    ?? new InvalidOperationException("The background projection failed without an error.");
                return true;
            }

            result = default!;
            error = null;
            return false;
        }

        public void CancelAndDisposeWhenSafe()
        {
            Cancellation.Cancel();
            RequestDisposal();
        }

        public void RequestDisposal()
        {
            if (Interlocked.Exchange(ref _disposalRequested, 1) != 0)
                return;
            if (Volatile.Read(ref _executionCompleted) != 0)
                Cancellation.Dispose();
        }

        public void MarkExecutionCompleted()
        {
            if (Interlocked.Exchange(ref _executionCompleted, 1) != 0)
                return;
            if (Volatile.Read(ref _disposalRequested) != 0)
                Cancellation.Dispose();
        }
    }

    private readonly object _sync = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private Work? _current;
    private long _generation;
    private bool _disposed;

    public event Action<BackgroundProjectionCompletion<TKey, TResult>>? Completed;

    public event Action<BackgroundProjectionFailure<TKey>>? Failed;

    /// <summary>
    /// Queues a projection and returns immediately.  Repeated requests for the
    /// current key share the same generation instead of duplicating I/O.
    /// </summary>
    public bool TryRequest(
        TKey key,
        Func<BackgroundProjectionRequest<TKey>, CancellationToken, TResult> loader,
        out BackgroundProjectionRequest<TKey> request)
    {
        ArgumentNullException.ThrowIfNull(loader);

        Work work;
        Work? superseded;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is { } existing
                && EqualityComparer<TKey>.Default.Equals(existing.Request.Key, key))
            {
                request = existing.Request;
                return false;
            }

            superseded = _current;
            long generation = unchecked(++_generation);
            if (generation == 0)
                generation = unchecked(++_generation);
            request = new BackgroundProjectionRequest<TKey>(generation, key);
            work = new Work(request, new CancellationTokenSource());
            _current = work;
        }

        superseded?.CancelAndDisposeWhenSafe();
        _ = ExecuteAsync(work, loader);
        return true;
    }

    /// <summary>
    /// Atomically consumes a completion only while its exact generation remains
    /// current.  A result queued to a dispatcher before a newer request arrived
    /// is rejected here.
    /// </summary>
    public bool TryAccept(BackgroundProjectionRequest<TKey> request)
        => TryTake(request, out _, out _);

    /// <summary>
    /// Atomically consumes the stored terminal outcome for the exact current
    /// generation.  The outcome remains recoverable even when a best-effort UI
    /// notification was posted before a page dispatcher became available.
    /// </summary>
    public bool TryTake(
        BackgroundProjectionRequest<TKey> request,
        out TResult result,
        out Exception? error)
    {
        result = default!;
        error = null;
        lock (_sync)
        {
            if (_disposed
                || _current is not { } current
                || current.Cancellation.IsCancellationRequested
                || !current.IsResultReady
                || current.Request.Generation != request.Generation
                || !EqualityComparer<TKey>.Default.Equals(current.Request.Key, request.Key)
                || !current.TryReadOutcome(out result, out error))
            {
                return false;
            }

            _current = null;
            current.RequestDisposal();
            return true;
        }
    }

    public void Cancel()
    {
        Work? cancelled;
        lock (_sync)
        {
            cancelled = _current;
            _current = null;
            unchecked
            {
                _generation++;
            }
        }
        cancelled?.CancelAndDisposeWhenSafe();
    }

    private async Task ExecuteAsync(
        Work work,
        Func<BackgroundProjectionRequest<TKey>, CancellationToken, TResult> loader)
    {
        CancellationToken cancellationToken = work.Cancellation.Token;
        bool gateEntered = false;
        try
        {
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;
            cancellationToken.ThrowIfCancellationRequested();
            TResult result = await Task.Run(
                    () => loader(work.Request, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            work.MarkResultReady(result);

            Action<BackgroundProjectionCompletion<TKey, TResult>>? completed;
            lock (_sync)
            {
                if (_disposed
                    || !ReferenceEquals(_current, work)
                    || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                completed = Completed;
            }

            try
            {
                completed?.Invoke(
                    new BackgroundProjectionCompletion<TKey, TResult>(work.Request, result));
            }
            catch
            {
                // A projection observer cannot fault the owned background task.
                // UI consumers perform their own dispatcher-bound error handling.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Superseded and page-lifetime cancellations are expected.  They
            // deliberately publish no partial or stale projection.
        }
        catch (Exception exception)
        {
            Action<BackgroundProjectionFailure<TKey>>? failed;
            lock (_sync)
            {
                if (_disposed
                    || !ReferenceEquals(_current, work)
                    || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                work.MarkFailureReady(exception);
                failed = Failed;
            }

            try
            {
                failed?.Invoke(new BackgroundProjectionFailure<TKey>(work.Request, exception));
            }
            catch
            {
                // Keep the worker task observed even if a diagnostic subscriber fails.
            }
        }
        finally
        {
            if (gateEntered)
                _executionGate.Release();
            work.MarkExecutionCompleted();
        }
    }

    public void Dispose()
    {
        Work? cancelled;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            cancelled = _current;
            _current = null;
        }
        cancelled?.CancelAndDisposeWhenSafe();
        Completed = null;
        Failed = null;
    }
}
