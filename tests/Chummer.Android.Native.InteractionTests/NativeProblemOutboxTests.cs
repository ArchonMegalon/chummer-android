using Chummer.Android.Native;

internal static class NativeProblemOutboxTests
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "chummer-outbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clock = new Clock();
            NativeProblemEntry Failure() => new(clock.GetUtcNow(), "0.1.0-preview.165+165", NativeProblemArea.LifeModules,
                NativeProblemOperation.Action, NativeProblemOutcome.Failed, 42, 250, NativeProblemError.Network);
            var queue = new NativeProblemOutbox(root, clock);
            int calls = 0;
            Task<NativeProblemDeliveryResult> Accept(NativeProblemSubmission report, CancellationToken ct)
            {
                calls++;
                Require(Guid.TryParseExact(report.Id, "N", out _), "No stable random report identity.");
                Require(report.Event == Failure(), "Unexpected report content.");
                return Task.FromResult(NativeProblemDeliveryResult.Accepted);
            }
            Require(!await queue.EnqueueAsync(Failure()), "Queued without opt-in.");
            await queue.DeliverOneAsync(Accept);
            Require(calls == 0, "Sent without opt-in.");
            Require(await queue.SetEnabledAsync(true), "Opt-in not saved.");
            Require(!await queue.EnqueueAsync(Failure() with { AtUtc = clock.GetUtcNow().AddSeconds(-1) }),
                "Old event crossed the consent boundary.");
            foreach (var outcome in new[] { NativeProblemOutcome.Slow, NativeProblemOutcome.Busy,
                NativeProblemOutcome.Canceled, NativeProblemOutcome.NotReady, NativeProblemOutcome.Completed })
                Require(!await queue.EnqueueAsync(Failure() with { Outcome = outcome }), "Non-failure submitted as crash.");
            Require(!await queue.EnqueueAsync(Failure() with { Version = "PRIVATE email token" }), "Invalid metadata accepted.");
            Require(!await queue.EnqueueAsync(Failure() with { Area = (NativeProblemArea)999 }), "Invalid category accepted.");
            Require(await queue.EnqueueAsync(Failure()), "Failure not queued.");
            Require(!await queue.EnqueueAsync(Failure() with { OperationId = 43 }), "Repeated category not suppressed.");

            string? attemptedId = null;
            await queue.DeliverOneAsync((report, ct) =>
            {
                calls++;
                attemptedId = report.Id;
                throw new HttpRequestException("PRIVATE unknown remote outcome token=secret");
            });
            queue = new NativeProblemOutbox(root, clock);
            await queue.DeliverOneAsync(Accept);
            Require(calls == 1, "Restart bypassed retry backoff.");
            clock.Advance(TimeSpan.FromMinutes(5));
            await queue.DeliverOneAsync((report, ct) =>
            {
                calls++;
                Require(report.Id == attemptedId, "Uncertain delivery received a fresh ID.");
                return Task.FromResult(NativeProblemDeliveryResult.Accepted);
            });
            queue = new NativeProblemOutbox(root, clock);
            clock.Advance(TimeSpan.FromMinutes(5));
            await queue.DeliverOneAsync(Accept);
            Require(calls == 2, "Acknowledged report replayed after restart.");
            Require(!await queue.EnqueueAsync(Failure()), "Restart lost duplicate suppression.");
            string path = Path.Combine(root, "technical-delivery.json");
            Require(!File.ReadAllText(path).Contains("PRIVATE") && !File.ReadAllText(path).Contains("secret"), "Error text persisted.");

            Require(await queue.SetEnabledAsync(false), "Revocation not saved.");
            clock.Advance(TimeSpan.FromMinutes(10));
            await queue.DeliverOneAsync(Accept);
            Require(calls == 2, "Revoked queue sent a report.");
            Require(!File.ReadAllText(path).Contains(attemptedId!), "Revocation did not clear pending/history data.");
            await queue.SetEnabledAsync(true);
            await queue.EnqueueAsync(Failure());
            clock.Advance(TimeSpan.FromDays(3));
            await queue.DeliverOneAsync(Accept);
            Require(calls == 2, "Expired report submitted.");
            Require(await queue.EnqueueAsync(Failure()), "Expiry unexpectedly revoked consent.");

            // Bound all memory/disk input, including repeated distinct categories.
            foreach (var area in Enum.GetValues<NativeProblemArea>())
                foreach (var operation in Enum.GetValues<NativeProblemOperation>())
                    await queue.EnqueueAsync(Failure() with { Area = area, Operation = operation });
            var state = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllBytes(path),
                NativeProblemDeliveryJsonContext.Default.NativeProblemDeliveryState)!;
            Require(state.Items.Count == NativeProblemOutbox.MaximumItems, "Outbox limit not enforced.");
            Require(new FileInfo(path).Length <= NativeProblemOutbox.MaximumFileBytes, "Outbox file exceeded cap.");

            foreach (string invalid in new[] { "{invalid secret", new string('x', NativeProblemOutbox.MaximumFileBytes + 1) })
            {
                await File.WriteAllTextAsync(path, invalid);
                queue = new NativeProblemOutbox(root, clock);
                await queue.DeliverOneAsync(Accept);
                Require(calls == 2 && !await queue.EnqueueAsync(Failure()), "Corrupt state enabled delivery.");
                Require(await queue.SetEnabledAsync(false), "Cannot revoke corrupt state.");
            }
            string unavailable = Path.Combine(root, "not-a-directory");
            await File.WriteAllTextAsync(unavailable, "fixture");
            queue = new NativeProblemOutbox(unavailable, clock);
            Require(!await queue.SetEnabledAsync(true), "Unavailable disk enabled delivery.");
            await queue.DeliverOneAsync(Accept);
            Require(calls == 2, "Disk failure sent data.");

            var concurrent = new NativeProblemOutbox(Path.Combine(root, "concurrent"), clock);
            await concurrent.SetEnabledAsync(true);
            await concurrent.EnqueueAsync(Failure());
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int activeSends = 0;
            Task first = concurrent.DeliverOneAsync(async (_, ct) =>
            {
                activeSends++;
                entered.SetResult();
                await release.Task.WaitAsync(ct);
                return NativeProblemDeliveryResult.Accepted;
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task second = concurrent.DeliverOneAsync((_, _) =>
            {
                activeSends++;
                return Task.FromResult(NativeProblemDeliveryResult.Accepted);
            });
            release.SetResult();
            await Task.WhenAll(first, second);
            Require(activeSends == 1, "Parallel wake-ups sent duplicates.");
            clock.Advance(TimeSpan.FromMinutes(5));
            await concurrent.EnqueueAsync(Failure() with { Area = NativeProblemArea.Account });
            await concurrent.DeliverOneAsync((_, _) => Task.FromResult(NativeProblemDeliveryResult.Rejected));
            clock.Advance(TimeSpan.FromMinutes(5));
            await concurrent.DeliverOneAsync((_, _) => throw new InvalidOperationException("Permanent rejection replayed."));

            await concurrent.EnqueueAsync(Failure() with { Area = NativeProblemArea.Career });
            using var cancel = new CancellationTokenSource();
            string? canceledId = null;
            await concurrent.DeliverOneAsync((report, ct) =>
            {
                canceledId = report.Id;
                cancel.Cancel();
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Cancellation ignored.");
            }, cancel.Token);
            clock.Advance(TimeSpan.FromMinutes(5));
            await concurrent.DeliverOneAsync((report, _) =>
            {
                Require(report.Id == canceledId, "Uncertain cancellation lost stable identity.");
                return Task.FromResult(NativeProblemDeliveryResult.Accepted);
            });
            Console.WriteLine("PASS diagnostic outbox: opt-in/revoke, bounds, expiry, invalid metadata, durable IDs, uncertainty backoff, restart dedupe, no non-failure/crash conflation");
            Console.WriteLine("PASS diagnostic delivery: serialized wake-ups, permanent rejection, uncertain cancellation identity");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now += interval;
    }
}
