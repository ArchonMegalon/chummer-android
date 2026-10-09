namespace Chummer.Android.Native;

/// <summary>
/// Best-effort foreground-process reporting, not an Android background service.
/// All disk/network work stays off the UI thread. The outbox applies the build's
/// initial preference without overriding a saved opt-out.
/// </summary>
internal sealed class NativeProblemReporter : IDisposable
{
    private readonly NativeProblemLog _log;
    private readonly NativeProblemOutbox _outbox;
    private readonly Func<NativeProblemSubmission, CancellationToken, Task<NativeProblemDeliveryResult>> _send;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _delivery;
    private Task? _loop;
    private bool _initialized;
    private bool _enabled;
    private bool _suspended;
    private long _revision;

    internal NativeProblemReporter(NativeProblemLog log, NativeProblemOutbox outbox,
        Func<NativeProblemSubmission, CancellationToken, Task<NativeProblemDeliveryResult>> send)
    { _log = log; _outbox = outbox; _send = send; }

    internal void Start()
    {
        lock (_stateGate) _loop ??= Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    await RunOnceAsync().ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(30), _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        });
    }

    internal Task<bool> IsEnabledAsync() => Task.Run(async () =>
    {
        await _work.WaitAsync(_stop.Token).ConfigureAwait(false);
        try { await InitializeAsync().ConfigureAwait(false); return _enabled; }
        finally { _work.Release(); }
    });

    internal Task<bool> SetEnabledAsync(bool enabled) => Task.Run(async () =>
    {
        await _changes.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            Task cancellation;
            lock (_stateGate)
            {
                _suspended = true;
                // Cancel without running transport callbacks under this lock.
                cancellation = _delivery?.CancelAsync() ?? Task.CompletedTask;
            }
            await cancellation.ConfigureAwait(false);
            await _work.WaitAsync(_stop.Token).ConfigureAwait(false);
            try
            {
                bool saved = await _outbox.SetEnabledAsync(enabled, _stop.Token).ConfigureAwait(false);
                _enabled = saved && enabled;
                _initialized = true;
                // Exact sequence boundary also excludes older events whose UTC
                // timestamp happens to equal the new consent timestamp.
                _revision = _log.CurrentRevision;
                lock (_stateGate) _suspended = !saved;
                return saved; // Failure leaves this process's sender stopped.
            }
            finally { _work.Release(); }
        }
        finally { _changes.Release(); }
    });

    // One bounded observation/drain pass; also used by focused deterministic tests.
    internal Task RunOnceAsync() => Task.Run(async () =>
    {
        await _work.WaitAsync(_stop.Token).ConfigureAwait(false);
        CancellationTokenSource? delivery = null;
        try
        {
            await InitializeAsync().ConfigureAwait(false);
            lock (_stateGate)
            {
                if (_suspended || !_enabled) return;
                _delivery = delivery = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            }
            foreach (var entry in _log.ReadAfter(ref _revision))
                await _outbox.EnqueueAsync(entry, delivery.Token).ConfigureAwait(false);
            await _outbox.DeliverOneAsync(_send, delivery.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested || delivery?.IsCancellationRequested == true) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or HttpRequestException or System.Text.Json.JsonException or System.Security.SecurityException)
        { /* Diagnostics cannot interrupt creation or recursively report themselves. */ }
        finally
        {
            lock (_stateGate) { _delivery = null; delivery?.Dispose(); }
            _work.Release();
        }
    });

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _enabled = await _outbox.IsEnabledAsync(_stop.Token).ConfigureAwait(false);
        _revision = _log.CurrentRevision;
        _initialized = true;
    }

    public void Dispose() => _stop.Cancel();

    internal async Task StopAsync()
    {
        Dispose();
        Task? loop;
        lock (_stateGate) loop = _loop;
        if (loop is not null) await loop.ConfigureAwait(false);
    }
}
