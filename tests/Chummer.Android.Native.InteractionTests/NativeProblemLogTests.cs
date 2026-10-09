using Chummer.Android.Native;
using System.Text.Json;

internal static class NativeProblemLogTests
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "chummer-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clock = new DiagnosticClock();
            using (var log = new NativeProblemLog(root, "0.1.0-preview.165+165", clock))
            {
                using (var operation = log.Begin(NativeProblemArea.LifeModules, NativeProblemOperation.StoryReadiness))
                {
                    clock.Advance(TimeSpan.FromSeconds(29));
                    log.CheckSlowOperations();
                    Require(!Rows(await log.ExportAsync()).Any(r => r.Outcome == NativeProblemOutcome.Slow), "Slow too early.");
                    clock.Advance(TimeSpan.FromSeconds(2));
                    log.CheckSlowOperations();
                    log.CheckSlowOperations();
                    operation.Fail(new IOException("PRIVATE runner email@example.org token=secret chapter words"));
                    operation.Dispose();
                }
                var rows = Rows(await log.ExportAsync());
                Require(rows.Count(r => r.Outcome == NativeProblemOutcome.Slow) == 1, "Slow observation repeated.");
                Require(rows.Count(r => r.Outcome == NativeProblemOutcome.Failed && r.Error == NativeProblemError.Storage) == 1,
                    "Caught storage failure missing.");
                Require(!rows.Any(r => r.Outcome == NativeProblemOutcome.Completed), "Failed scope also reported success.");
                Require(!File.ReadAllText(Path.Combine(root, "technical-diagnostics.json")).Contains("PRIVATE"), "Exception text leaked.");
                using (var canceled = log.Begin(NativeProblemArea.Creation, NativeProblemOperation.Action))
                    canceled.Fail(new OperationCanceledException("private cancellation"));
                using (log.Begin(NativeProblemArea.Other, NativeProblemOperation.Refresh)) { }
                Require(Rows(await log.ExportAsync()).Any(r => r.Outcome == NativeProblemOutcome.Incomplete),
                    "An unclassified scope escape was mislabeled completed.");
                log.Record(NativeProblemArea.Creation, NativeProblemOperation.Action, NativeProblemOutcome.Busy);
                await log.FlushAsync();
            }
            using (var reopened = new NativeProblemLog(root, "0.1.0-preview.165+165", clock))
            {
                var retained = Rows(await reopened.ExportAsync());
                Require(retained.Any(r => r.Outcome == NativeProblemOutcome.Failed)
                    && retained.Any(r => r.Outcome == NativeProblemOutcome.Canceled)
                    && retained.Any(r => r.Outcome == NativeProblemOutcome.Busy), "Reopen lost useful events.");
                clock.Advance(TimeSpan.FromDays(3));
                Require(Rows(await reopened.ExportAsync()).Count == 0, "Expired events exported.");
                await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < 300; ++i)
                        reopened.Record(NativeProblemArea.Creation, NativeProblemOperation.Refresh, NativeProblemOutcome.Completed);
                })));
                Require(Rows(await reopened.ExportAsync()).Count <= NativeProblemLog.MaximumEntries, "Entry cap exceeded.");
                Require(new FileInfo(Path.Combine(root, "technical-diagnostics.json")).Length <= NativeProblemLog.MaximumFileBytes,
                    "File cap exceeded.");
            }
            string reportPath = Path.Combine(root, "technical-diagnostics.json");
            foreach (string invalid in new[] { "not JSON secret", new string('x', NativeProblemLog.MaximumFileBytes + 1) })
            {
                await File.WriteAllTextAsync(reportPath, invalid);
                using var bad = new NativeProblemLog(root, "bad version\nsecret", clock);
                bad.Record((NativeProblemArea)999, NativeProblemOperation.Action, NativeProblemOutcome.Started);
                Require(Rows(await bad.ExportAsync()).Count == 0, "Invalid history or enum exported.");
                bad.Record(NativeProblemArea.Other, NativeProblemOperation.Action, NativeProblemOutcome.Busy);
                string exported = await bad.ExportAsync();
                Require(Rows(exported).Single().Version == "unknown" && !exported.Contains("secret"), "Invalid version leaked.");
            }
            string blocked = Path.Combine(root, "not-a-directory");
            await File.WriteAllTextAsync(blocked, "fixture");
            using (var noDisk = new NativeProblemLog(blocked, "165", clock))
            {
                noDisk.Record(NativeProblemArea.Creation, NativeProblemOperation.Refresh, NativeProblemOutcome.DispatchRejected);
                Require(Rows(await noDisk.ExportAsync()).Single().Outcome == NativeProblemOutcome.DispatchRejected,
                    "Disk failure blocked in-memory diagnostics.");
            }
            Console.WriteLine("PASS diagnostics: bounded events/file, no exception text, slow once, cancellation, reopen, expiry, invalid input, disk failure");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static List<NativeProblemEntry> Rows(string json) => JsonSerializer.Deserialize(json,
        NativeProblemJsonContext.Default.ListNativeProblemEntry)!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class DiagnosticClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
        public void Advance(TimeSpan time) => Interlocked.Add(ref _ticks, time.Ticks);
    }
}
