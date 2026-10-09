using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chummer.Android.Native;

// App-private delivery state, NOT a Hub wire contract. No production transport
// is registered yet. The eventual adapter must use Chummer.Control.Contracts.
internal sealed record NativeProblemSubmission(string Id, NativeProblemEntry Event);
internal sealed record NativeProblemDeliveryItem(NativeProblemSubmission Submission, bool Terminal);
internal sealed record NativeProblemDeliveryState(bool Enabled, DateTimeOffset RetryAfterUtc,
    List<NativeProblemDeliveryItem> Items);

internal enum NativeProblemDeliveryResult { Accepted, RetryLater, Rejected }

/// <summary>
/// Bounded, opt-in, durable queue for technical failures only. All I/O is called
/// from background work, never while holding a page action or mutation lease.
/// A callback may return Accepted only after validating the exact receipt ID.
/// Unknown outcomes retain the same ID; the Hub adapter must be idempotent.
/// </summary>
internal sealed class NativeProblemOutbox
{
    internal const int MaximumItems = 8;
    internal const int MaximumFileBytes = 16384;
    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NativeProblemOutbox(string directory, TimeProvider? clock = null)
    {
        _path = Path.Combine(directory, "technical-delivery.json");
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<bool> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Changing consent never uploads old local history. Revocation
            // clears the queue; a later opt-in starts with new observations.
            var state = Load();
            if (enabled && state.Enabled) return true;
            Save(new(enabled, DateTimeOffset.MinValue, new()));
            return true;
        }
        catch (Exception e) when (IsStorageFailure(e)) { return false; }
        finally { _gate.Release(); }
    }

    public async Task<bool> EnqueueAsync(NativeProblemEntry entry, CancellationToken ct = default)
    {
        if (!IsValid(entry) || entry.AtUtc < _clock.GetUtcNow().AddDays(-2)
            || entry.Outcome is not (NativeProblemOutcome.Failed or NativeProblemOutcome.DispatchRejected))
            return false; // Slow, not-ready, busy and cancellation are not crashes.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = Load();
            if (!state.Enabled || state.Items.Count >= MaximumItems) return false;
            // One matching category per retention window. Completed entries
            // remain as bounded suppression markers across process restarts.
            if (state.Items.Any(item => SameCategory(item.Submission.Event, entry))) return false;
            state.Items.Add(new(new(Guid.NewGuid().ToString("N"), entry), false));
            Save(state); // Durable ID exists before any possible send.
            return true;
        }
        catch (Exception e) when (IsStorageFailure(e)) { return false; }
        finally { _gate.Release(); }
    }

    public async Task DeliverOneAsync(
        Func<NativeProblemSubmission, CancellationToken, Task<NativeProblemDeliveryResult>> send,
        CancellationToken ct = default)
    {
        // One delivery per wake-up, globally spaced. No while/retry loop.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = Load();
            Save(state); // Persist expiry pruning even when there is nothing left to send.
            int index = state.Items.FindIndex(item => !item.Terminal);
            if (!state.Enabled || index < 0 || _clock.GetUtcNow() < state.RetryAfterUtc) return;
            state = state with { RetryAfterUtc = _clock.GetUtcNow().AddMinutes(5) };
            Save(state); // Restart during an uncertain POST must not cause a burst.
            NativeProblemDeliveryResult result;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                result = await send(state.Items[index].Submission, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
            { return; }
            if (result is NativeProblemDeliveryResult.Accepted or NativeProblemDeliveryResult.Rejected)
            {
                // A permanent rejection must not be replayed either. This flag
                // means terminal, not "success"; it is not shown as delivery truth.
                state.Items[index] = state.Items[index] with { Terminal = true };
                Save(state);
            }
        }
        catch (Exception e) when (IsStorageFailure(e)) { }
        finally { _gate.Release(); }
    }

    private NativeProblemDeliveryState Load()
    {
        if (!File.Exists(_path)) return new(false, DateTimeOffset.MinValue, new());
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumFileBytes) return new(false, DateTimeOffset.MinValue, new());
        NativeProblemDeliveryState? state;
        try { state = JsonSerializer.Deserialize(stream, NativeProblemDeliveryJsonContext.Default.NativeProblemDeliveryState); }
        catch (JsonException) { return new(false, DateTimeOffset.MinValue, new()); }
        if (state?.Items is null || state.Items.Count > MaximumItems
            || state.Items.Any(item => item?.Submission is not { Id.Length: 32, Event: not null } submission
                || !Guid.TryParseExact(submission.Id, "N", out _) || !IsValid(submission.Event))
            || state.Items.Any(item => item.Submission.Event.Outcome is not
                (NativeProblemOutcome.Failed or NativeProblemOutcome.DispatchRejected))
            || state.Items.Select(item => item.Submission.Id).Distinct(StringComparer.Ordinal).Count() != state.Items.Count)
            return new(false, DateTimeOffset.MinValue, new());
        state.Items.RemoveAll(item => item.Submission.Event.AtUtc < _clock.GetUtcNow().AddDays(-2));
        return state;
    }

    private void Save(NativeProblemDeliveryState state)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, NativeProblemDeliveryJsonContext.Default.NativeProblemDeliveryState);
        if (bytes.Length > MaximumFileBytes) throw new IOException("Diagnostic delivery size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using (var stream = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(_path + ".tmp", _path, overwrite: true);
    }

    private bool IsValid(NativeProblemEntry? entry) => entry is not null
        && entry.AtUtc != default && entry.AtUtc <= _clock.GetUtcNow()
        && entry.Version is { Length: > 0 and <= 48 }
        && entry.Version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')
        && Enum.IsDefined(entry.Area) && Enum.IsDefined(entry.Operation)
        && Enum.IsDefined(entry.Outcome) && Enum.IsDefined(entry.Error)
        && entry.OperationId >= 0 && entry.ElapsedMilliseconds is >= 0 and <= 86400000;

    private static bool SameCategory(NativeProblemEntry first, NativeProblemEntry second)
        => first.Version == second.Version && first.Area == second.Area && first.Operation == second.Operation
            && first.Outcome == second.Outcome && first.Error == second.Error;

    private static bool IsStorageFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException;
}

[JsonSerializable(typeof(NativeProblemDeliveryState))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal partial class NativeProblemDeliveryJsonContext : JsonSerializerContext { }
