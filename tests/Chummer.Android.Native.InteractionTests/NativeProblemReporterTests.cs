using Chummer.Android.Native;
using System.Text.Json;

internal static class NativeProblemReporterTests
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "chummer-reporter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var clock = new Clock();
        using var log = new NativeProblemLog(Path.Combine(root, "log"), "0.1.0-preview.165+165", clock);
        try
        {
            void Fail(NativeProblemArea area) => log.Record(area, NativeProblemOperation.Refresh,
                NativeProblemOutcome.Failed, error: NativeProblemError.Network);
            int calls = 0;
            bool offline = false;
            var ids = new List<string>();
            Task<NativeProblemDeliveryResult> Send(NativeProblemSubmission report, CancellationToken _)
            {
                calls++;
                ids.Add(report.Id);
                if (offline) throw new HttpRequestException("Synthetic offline");
                return Task.FromResult(NativeProblemDeliveryResult.Accepted);
            }
            var outbox = new NativeProblemOutbox(Path.Combine(root, "queue"), clock);
            using var reporter = new NativeProblemReporter(log, outbox, Send);
            Fail(NativeProblemArea.Creation);
            await reporter.RunOnceAsync();
            Require(!await reporter.IsEnabledAsync() && calls == 0, "Default started sending.");
            Fail(NativeProblemArea.Book);
            Require(await reporter.SetEnabledAsync(true), "Cannot enable.");
            await reporter.RunOnceAsync();
            Require(calls == 0, "Pre-consent events with identical timestamps were uploaded.");
            Fail(NativeProblemArea.LifeModules);
            await Task.WhenAll(reporter.RunOnceAsync(), reporter.RunOnceAsync());
            Require(calls == 1, "New event not sent once.");
            Require(await reporter.SetEnabledAsync(false), "Cannot revoke.");
            Fail(NativeProblemArea.Account);
            Require(await reporter.SetEnabledAsync(true), "Cannot re-enable.");
            await reporter.RunOnceAsync();
            Require(calls == 1, "Disabled-session event uploaded after re-enabling.");
            offline = true;
            Fail(NativeProblemArea.Career);
            await reporter.RunOnceAsync();
            Require(calls == 2, "Offline fixture did not attempt delivery.");
            await reporter.StopAsync();
            using (var restartedLog = new NativeProblemLog(Path.Combine(root, "log"), "0.1.0-preview.165+165", clock))
            using (var restarted = new NativeProblemReporter(restartedLog,
                new NativeProblemOutbox(Path.Combine(root, "queue"), clock), Send))
            {
                Require(await restarted.IsEnabledAsync(), "Restart lost explicit consent.");
                await restarted.RunOnceAsync();
                Require(calls == 2, "Restart bypassed backoff.");
                clock.Advance(TimeSpan.FromMinutes(5));
                offline = false;
                await restarted.RunOnceAsync();
                Require(calls == 3 && ids[1] == ids[2], "Restart changed pending report identity.");
                Require(await restarted.SetEnabledAsync(false), "Restart revocation failed.");
                Require(!await outbox.IsEnabledAsync(), "Revocation not durable.");
                await restartedLog.FlushAsync();
            }

            // A real background pass is blocked in its transport. Withdrawal
            // must cancel it, not wait for the network deadline or page gate.
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelOutbox = new NativeProblemOutbox(Path.Combine(root, "cancel"), clock);
            using (var cancelReporter = new NativeProblemReporter(log, cancelOutbox, async (_, ct) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("Cancellation ignored.");
            }))
            {
                Require(await cancelReporter.SetEnabledAsync(true), "Cancel fixture not enabled.");
                Fail(NativeProblemArea.Settings);
                cancelReporter.Start();
                cancelReporter.Start(); // idempotent scheduler registration
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Require(await cancelReporter.SetEnabledAsync(false).WaitAsync(TimeSpan.FromSeconds(3)), "Withdrawal blocked.");
                var saved = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(root, "cancel", "technical-delivery.json")),
                    NativeProblemDeliveryJsonContext.Default.NativeProblemDeliveryState)!;
                Require(!saved.Enabled && saved.Items.Count == 0, "Withdrawal retained pending data.");
                await cancelReporter.StopAsync();
            }

            string blocked = Path.Combine(root, "blocked");
            await File.WriteAllTextAsync(blocked, "fixture");
            using (var noDisk = new NativeProblemReporter(log, new NativeProblemOutbox(blocked, clock), Send))
            {
                Require(!await noDisk.SetEnabledAsync(true) && !await noDisk.IsEnabledAsync(), "Disk error enabled reporting.");
                Fail(NativeProblemArea.Other);
                await noDisk.RunOnceAsync();
                Require(calls == 3, "Failed consent write allowed sending.");
            }

            // Legacy preparatory states lacking the consent epoch fail closed.
            string damaged = Path.Combine(root, "damaged");
            using (var withdrawal = new NativeProblemReporter(log, new NativeProblemOutbox(damaged, clock), Send))
            {
                Require(await withdrawal.SetEnabledAsync(true), "Withdrawal fixture not enabled.");
                Directory.Move(damaged, damaged + "-preserved");
                await File.WriteAllTextAsync(damaged, "fixture");
                Require(!await withdrawal.SetEnabledAsync(false), "Unavailable disk falsely acknowledged withdrawal.");
                Require(!await withdrawal.IsEnabledAsync(), "Failed withdrawal left this session enabled.");
                Fail(NativeProblemArea.Account);
                await withdrawal.RunOnceAsync();
                Require(calls == 3, "Failed withdrawal kept sending.");
                File.Delete(damaged);
                Directory.Move(damaged + "-preserved", damaged);
                Require(await withdrawal.SetEnabledAsync(false), "Retry could not persist withdrawal.");
                Require(!await new NativeProblemOutbox(damaged, clock).IsEnabledAsync(), "Retried withdrawal lost on restart.");
            }

            // Legacy preparatory states lacking the consent epoch fail closed.
            string legacy = Path.Combine(root, "legacy");
            Directory.CreateDirectory(legacy);
            await File.WriteAllTextAsync(Path.Combine(legacy, "technical-delivery.json"),
                JsonSerializer.Serialize(new NativeProblemDeliveryState(true, default, new()),
                    NativeProblemDeliveryJsonContext.Default.NativeProblemDeliveryState));
            Require(!await new NativeProblemOutbox(legacy, clock).IsEnabledAsync(), "Unversioned consent accepted.");

            int internalCalls = 0;
            string internalPath = Path.Combine(root, "internal-default");
            using (var internalReporter = new NativeProblemReporter(log,
                new NativeProblemOutbox(internalPath, clock, internalTestBuild: true), (_, _) =>
                { internalCalls++; return Task.FromResult(NativeProblemDeliveryResult.Accepted); }))
            {
                Fail(NativeProblemArea.Book);
                await internalReporter.RunOnceAsync();
                Require(await internalReporter.IsEnabledAsync() && internalCalls == 0, "Internal default replayed old journal.");
                Fail(NativeProblemArea.LifeModules);
                await internalReporter.RunOnceAsync();
                Require(internalCalls == 1, "Internal default did not send a new observation.");
                Require(await internalReporter.SetEnabledAsync(false), "Cannot disable Internal default.");
            }
            using (var updated = new NativeProblemReporter(log, new NativeProblemOutbox(internalPath, clock, true), (_, _) =>
                { internalCalls++; return Task.FromResult(NativeProblemDeliveryResult.Accepted); }))
            {
                Require(!await updated.IsEnabledAsync(), "Update/restart overrode Internal opt-out.");
                Fail(NativeProblemArea.Career);
                await updated.RunOnceAsync();
                Require(internalCalls == 1, "Disabled Internal reporter sent a new event.");
            }
            Console.WriteLine("PASS Internal reporter: first-run on without history replay, new-event delivery, durable opt-out across update/restart");
            Console.WriteLine("PASS diagnostic reporter: opt-in boundary (same timestamp), off/re-enable, restart, offline stable ID, cancel in-flight, one scheduler, storage failure, old consent rejected");
        }
        finally
        {
            await log.FlushAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now += interval;
    }
}
