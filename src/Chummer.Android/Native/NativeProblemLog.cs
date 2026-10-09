using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chummer.Android.Native;

public enum NativeProblemArea { Other, Creation, LifeModules, Book, Account, Settings, Career }
public enum NativeProblemOperation { Appearance, Action, Refresh, StoryReadiness }
public enum NativeProblemOutcome { Started, Completed, Canceled, Failed, Slow, Busy, Departed, DispatchRejected, NotReady, Incomplete }
public enum NativeProblemError { None, Network, Storage, InvalidState, Access, Json, Other }

/// <summary>
/// Local, app-private technical breadcrumbs. No network transport, identifiers,
/// exception text, story content or arbitrary caller strings enter this log.
/// A slow operation is an observation, NOT an ANR or proof of product failure.
/// </summary>
public sealed class NativeProblemLog : IDisposable
{
    internal const int MaximumEntries = 128;
    internal const int MaximumFileBytes = 65536;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _version;
    private readonly TimeProvider _clock;
    private readonly List<NativeProblemEntry> _entries = new();
    private readonly HashSet<Operation> _active = new();
    private readonly Timer _timer;
    private Task _writer = Task.CompletedTask;
    private long _revision;
    private long _sequence;
    private bool _writing;
    private bool _disposed;
    private DateTimeOffset _retryWriteAfter;

    public NativeProblemLog(string directory, string version, TimeProvider? clock = null)
    {
        _path = Path.Combine(directory, "technical-diagnostics.json");
        _version = SafeVersion(version) ? version : "unknown";
        _clock = clock ?? TimeProvider.System;
        _timer = new Timer(_ => CheckSlowOperations(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public static NativeProblemArea AreaFor(Type page) => page.Name switch
    {
        "BuildPage" => NativeProblemArea.Creation,
        "OriginDossierLifeModuleDecisionPage" or "LifeModuleCompletionPage" => NativeProblemArea.LifeModules,
        "RetainedOriginBookPage" or "OriginDossierBookPage" or "StoriesPage" => NativeProblemArea.Book,
        "AccountPage" => NativeProblemArea.Account,
        "ApplicationSettingsPage" => NativeProblemArea.Settings,
        var name when name.StartsWith("Creation", StringComparison.Ordinal) => NativeProblemArea.Creation,
        var name when name.StartsWith("Career", StringComparison.Ordinal) => NativeProblemArea.Career,
        _ => NativeProblemArea.Other
    };

    public Operation Begin(NativeProblemArea area, NativeProblemOperation kind)
    {
        lock (_gate)
        {
            var operation = new Operation(this, area, kind, ++_sequence, _clock.GetTimestamp());
            // Bound the watchdog even if callers lose an operation scope.
            if (!_disposed && _active.Count < 32) _active.Add(operation);
            Record(area, kind, NativeProblemOutcome.Started, operation.Id);
            return operation;
        }
    }

    public void Record(NativeProblemArea area, NativeProblemOperation kind, NativeProblemOutcome outcome,
        long operationId = 0, long elapsedMilliseconds = 0, NativeProblemError error = NativeProblemError.None)
    {
        if (!Enum.IsDefined(area) || !Enum.IsDefined(kind) || !Enum.IsDefined(outcome) || !Enum.IsDefined(error)) return;
        lock (_gate)
        {
            if (_disposed) return;
            _entries.Add(new(_clock.GetUtcNow(), _version, area, kind, outcome,
                Math.Max(0, operationId), Math.Clamp(elapsedMilliseconds, 0, 86400000), error));
            Trim(_entries, _clock.GetUtcNow());
            ++_revision;
            if (_writing || _clock.GetUtcNow() < _retryWriteAfter) return;
            _writing = true;
            _writer = Task.Run(PersistAsync);
        }
    }

    internal void CheckSlowOperations()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (Operation operation in _active)
            {
                if (operation.SlowReported || operation.ElapsedMilliseconds < 30000) continue;
                operation.SlowReported = true;
                Record(operation.Area, operation.Kind, NativeProblemOutcome.Slow, operation.Id,
                    operation.ElapsedMilliseconds);
            }
        }
    }

    // Called only on explicit tester export. The automatic writer never sends.
    public async Task<string> ExportAsync()
    {
        await FlushAsync().ConfigureAwait(false);
        List<NativeProblemEntry> saved = await Task.Run(ReadSaved).ConfigureAwait(false);
        lock (_gate)
        {
            saved.AddRange(_entries);
            saved = saved.Distinct().OrderBy(e => e.AtUtc).ToList();
            Trim(saved, _clock.GetUtcNow());
        }
        return JsonSerializer.Serialize(saved, NativeProblemJsonContext.Default.ListNativeProblemEntry);
    }

    internal Task FlushAsync() { lock (_gate) return _writer; }

    private async Task PersistAsync()
    {
        try
        {
            // Coalesce short refresh bursts. Disk work never owns a UI action gate.
            await Task.Delay(150).ConfigureAwait(false);
            List<NativeProblemEntry> previous = ReadSaved();
            while (true)
            {
                long revision;
                List<NativeProblemEntry> snapshot;
                lock (_gate)
                {
                    revision = _revision;
                    snapshot = previous.Concat(_entries).Distinct().OrderBy(e => e.AtUtc).ToList();
                    Trim(snapshot, _clock.GetUtcNow());
                }
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot,
                    NativeProblemJsonContext.Default.ListNativeProblemEntry);
                if (bytes.Length > MaximumFileBytes) throw new IOException("Diagnostic size limit.");
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await File.WriteAllBytesAsync(_path + ".tmp", bytes).ConfigureAwait(false);
                File.Move(_path + ".tmp", _path, overwrite: true);
                lock (_gate)
                {
                    if (revision == _revision || _disposed) { _writing = false; return; }
                }
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Diagnostics must not break creation or recursively report itself.
            lock (_gate) { _writing = false; _retryWriteAfter = _clock.GetUtcNow().AddMinutes(1); }
        }
    }

    private List<NativeProblemEntry> ReadSaved()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaximumFileBytes) return new();
            var saved = JsonSerializer.Deserialize(stream,
                NativeProblemJsonContext.Default.ListNativeProblemEntry) ?? new();
            saved = saved.Where(e => e is not null && SafeVersion(e.Version)
                && Enum.IsDefined(e.Area) && Enum.IsDefined(e.Operation) && Enum.IsDefined(e.Outcome)
                && Enum.IsDefined(e.Error) && e.OperationId >= 0 && e.ElapsedMilliseconds is >= 0 and <= 86400000
                && e.AtUtc <= _clock.GetUtcNow()).ToList();
            Trim(saved, _clock.GetUtcNow());
            return saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        { return new(); }
    }

    private static bool SafeVersion(string? value) => value is { Length: > 0 and <= 48 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+');

    private static void Trim(List<NativeProblemEntry> entries, DateTimeOffset now)
    {
        entries.RemoveAll(e => e.AtUtc < now.AddDays(-2));
        if (entries.Count > MaximumEntries) entries.RemoveRange(0, entries.Count - MaximumEntries);
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _active.Clear(); }
        _timer.Dispose();
    }

    public sealed class Operation : IDisposable
    {
        private readonly NativeProblemLog _owner;
        private readonly long _started;
        private bool _finished;
        internal long Id { get; }
        internal NativeProblemArea Area { get; }
        internal NativeProblemOperation Kind { get; }
        internal bool SlowReported { get; set; }
        internal long ElapsedMilliseconds => (long)_owner._clock.GetElapsedTime(_started).TotalMilliseconds;
        internal Operation(NativeProblemLog owner, NativeProblemArea area, NativeProblemOperation kind, long id, long started)
        { _owner = owner; Area = area; Kind = kind; Id = id; _started = started; }

        public void Fail(Exception error) => Finish(error is OperationCanceledException
            ? NativeProblemOutcome.Canceled : NativeProblemOutcome.Failed, error switch
            {
                OperationCanceledException => NativeProblemError.None,
                HttpRequestException => NativeProblemError.Network,
                IOException => NativeProblemError.Storage,
                UnauthorizedAccessException => NativeProblemError.Access,
                JsonException => NativeProblemError.Json,
                InvalidOperationException => NativeProblemError.InvalidState,
                _ => NativeProblemError.Other
            });

        public void Finish(NativeProblemOutcome outcome = NativeProblemOutcome.Completed,
            NativeProblemError error = NativeProblemError.None)
        {
            lock (_owner._gate)
            {
                if (_finished) return;
                _finished = true;
                _owner._active.Remove(this);
                _owner.Record(Area, Kind, outcome, Id, ElapsedMilliseconds, error);
            }
        }
        // An unexpected escape must never manufacture a successful result.
        public void Dispose() => Finish(NativeProblemOutcome.Incomplete);
    }
}

internal sealed record NativeProblemEntry(DateTimeOffset AtUtc, string Version, NativeProblemArea Area,
    NativeProblemOperation Operation, NativeProblemOutcome Outcome, long OperationId,
    long ElapsedMilliseconds, NativeProblemError Error);

[JsonSerializable(typeof(List<NativeProblemEntry>))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal partial class NativeProblemJsonContext : JsonSerializerContext { }
