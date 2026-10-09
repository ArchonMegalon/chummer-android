using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Control.Contracts.Support;

internal static class NativeProblemHttpTransportTests
{
    public static async Task RunAsync()
    {
        var clock = new Clock();
        var entry = new NativeProblemEntry(clock.Now, "0.1.0-preview.165+165", NativeProblemArea.LifeModules,
            NativeProblemOperation.Refresh, NativeProblemOutcome.Failed, 42, 230,
            NativeProblemError.InvalidState);
        var report = new NativeProblemSubmission(Guid.NewGuid().ToString("N"), entry);
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var transport = new NativeProblemHttpTransport(client, clock);
        handler.Reply = async request =>
        {
            Require(request.Method == HttpMethod.Post && request.RequestUri == NativeProblemHttpTransport.Endpoint, "Wrong destination.");
            Require(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "Identity leaked.");
            string body = await request.Content!.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Require(json.RootElement.EnumerateObject().Count() == 8, "Unexpected free-form fields.");
            var wire = JsonSerializer.Deserialize(body, NativeProblemHttpJsonContext.Default.AndroidDiagnosticReport)!;
            Require(wire.ReportId == Guid.ParseExact(report.Id, "N") && wire.VersionCode == 165, "Wrong artifact/report identity.");
            Require(wire.Area == AndroidDiagnosticArea.LifeModules && wire.Operation == AndroidDiagnosticOperation.Refresh
                && wire.Outcome == AndroidDiagnosticOutcome.Failed && wire.Error == AndroidDiagnosticError.InvalidState, "Incorrect enum mapping.");
            Require(!body.Contains("operationId", StringComparison.OrdinalIgnoreCase)
                && !body.Contains("preview", StringComparison.Ordinal), "Local-only fields were transmitted.");
            return Receipt(report.Id, clock.Now);
        };
        Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.Accepted, "Exact receipt not accepted.");

        var slow = new NativeProblemSubmission(Guid.NewGuid().ToString("N"), entry with
        { Outcome = NativeProblemOutcome.Slow, ElapsedMilliseconds = 31000, Error = NativeProblemError.None });
        handler.Reply = async request =>
        {
            var wire = JsonSerializer.Deserialize(await request.Content!.ReadAsStringAsync(),
                NativeProblemHttpJsonContext.Default.AndroidDiagnosticReport)!;
            Require(wire.Outcome == AndroidDiagnosticOutcome.Slow && wire.ElapsedMilliseconds == 31000
                && wire.Error == AndroidDiagnosticError.None, "Slow operation became a failure/crash claim.");
            return Receipt(slow.Id, clock.Now);
        };
        Require(await transport.SendAsync(slow, default) == NativeProblemDeliveryResult.Accepted, "Slow observation was not accepted.");

        handler.Reply = _ => Task.FromResult(Receipt(Guid.NewGuid().ToString("N"), clock.Now));
        Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.RetryLater, "Foreign receipt accepted.");
        handler.Reply = _ => Task.FromResult(Receipt(report.Id, default));
        Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.RetryLater, "Missing receipt time accepted.");
        handler.Reply = _ => Task.FromResult(Receipt(report.Id, clock.Now.AddHours(1)));
        Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.RetryLater, "Future receipt accepted.");
        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
        { Content = new StringContent("not-json", Encoding.UTF8, "application/json") });
        Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.RetryLater, "Malformed receipt accepted.");

        var oversized = new EndlessStream();
        handler.Reply = _ =>
        {
            var content = new StreamContent(oversized);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = content });
        };
        Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.RetryLater, "Oversized chunked body accepted.");
        Require(oversized.BytesRead == 2049, "Read past the response cap before JSON materialization.");

        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        { Headers = { Location = new Uri("https://unrelated.invalid/") } });
        int before = handler.Calls;
        Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.Rejected, "Redirect was not rejected.");
        Require(handler.Calls == before + 1, "Redirect caused another request.");
        foreach (HttpStatusCode status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.RequestEntityTooLarge })
        {
            handler.Reply = _ => Task.FromResult(new HttpResponseMessage(status));
            Require(await transport.SendAsync(report, default) == NativeProblemDeliveryResult.Rejected, "Permanent rejection was not terminal.");
        }
        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(2)) } });
        Require((await transport.SendAsync(report, default)).RetryAfter == TimeSpan.FromHours(2), "Ignored Retry-After duration.");
        handler.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Headers = { RetryAfter = new RetryConditionHeaderValue(clock.Now.AddHours(3)) } });
        Require((await transport.SendAsync(report, default)).RetryAfter == TimeSpan.FromHours(3), "Ignored Retry-After date.");

        before = handler.Calls;
        Require(await transport.SendAsync(report with { Event = entry with { Version = "unknown" } }, default)
            == NativeProblemDeliveryResult.Rejected, "Unknown build was transmitted.");
        Require(await transport.SendAsync(report with { Event = entry with { Outcome = NativeProblemOutcome.Busy } }, default)
            == NativeProblemDeliveryResult.Rejected, "Busy was transmitted as a failure.");
        Require(handler.Calls == before, "Invalid metadata made a network request.");

        string root = Path.Combine(Path.GetTempPath(), "chummer-diagnostic-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            var outbox = new NativeProblemOutbox(root, clock);
            await outbox.SetEnabledAsync(true);
            Require(!await outbox.EnqueueAsync(slow.Event with { ElapsedMilliseconds = 250 }), "Sub-threshold slow observation queued.");
            Require(await outbox.EnqueueAsync(slow.Event), "Thirty-second observation not queued.");
            await outbox.DeliverOneAsync((_, _) => Task.FromResult(NativeProblemDeliveryResult.Defer(TimeSpan.FromHours(2))));
            outbox = new NativeProblemOutbox(root, clock);
            int sends = 0;
            Task<NativeProblemDeliveryResult> Accept(NativeProblemSubmission _, CancellationToken __)
            { sends++; return Task.FromResult(NativeProblemDeliveryResult.Accepted); }
            clock.Now += TimeSpan.FromMinutes(6);
            await outbox.DeliverOneAsync(Accept);
            Require(sends == 0, "Restart ignored server backoff.");
            clock.Now += TimeSpan.FromHours(2);
            await outbox.DeliverOneAsync(Accept);
            Require(sends == 1, "Valid wake-up did not deliver.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine("Native diagnostic HTTP privacy, receipt, bounded-stream and retry checks passed.");
    }

    private static HttpResponseMessage Receipt(string id, DateTimeOffset time) => new(HttpStatusCode.Accepted)
    {
        Content = new StringContent(JsonSerializer.Serialize(new AndroidDiagnosticReceipt(Guid.ParseExact(id, "N"), time),
            NativeProblemHttpJsonContext.Default.AndroidDiagnosticReceipt), Encoding.UTF8, "application/json")
    };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Reply = null!;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Reply(request); }
    }
    private sealed class EndlessStream : Stream
    {
        public int BytesRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { Array.Fill(buffer, (byte)' ', offset, count); BytesRead += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); buffer.Span.Fill((byte)' '); BytesRead += buffer.Length; return ValueTask.FromResult(buffer.Length); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
