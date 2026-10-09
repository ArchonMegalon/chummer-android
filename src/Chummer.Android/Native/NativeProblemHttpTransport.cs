using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Control.Contracts.Support;

namespace Chummer.Android.Native;

// Never use the account client: no account tokens, cookies or identity.
internal sealed class NativeProblemHttpTransport(HttpClient client, TimeProvider? clock = null) : IDisposable
{
    public void Dispose() => client.Dispose();
    internal static readonly Uri Endpoint = new("https://chummer.run/api/v1/support/android-diagnostics");
    private const int MaximumResponseBytes = 2048;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    internal static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        MaxResponseHeadersLength = 8
    }) { Timeout = TimeSpan.FromSeconds(10) };

    internal async Task<NativeProblemDeliveryResult> SendAsync(NativeProblemSubmission submission, CancellationToken ct)
    {
        NativeProblemEntry entry = submission.Event;
        int separator = entry.Version.LastIndexOf('+');
        if (!Guid.TryParseExact(submission.Id, "N", out Guid id) || id == Guid.Empty || separator < 0
            || !int.TryParse(entry.Version.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int version)
            || version is < 1 or > 2100000000
            || !TryMap(entry.Area, out AndroidDiagnosticArea area)
            || !TryMap(entry.Operation, out AndroidDiagnosticOperation operation)
            || !TryMap(entry.Outcome, out AndroidDiagnosticOutcome outcome)
            || !TryMap(entry.Error, out AndroidDiagnosticError error)
            || entry.ElapsedMilliseconds is < 0 or > 86400000)
            return NativeProblemDeliveryResult.Rejected;

        var report = new AndroidDiagnosticReport(id, version, entry.AtUtc, area, operation,
            outcome, (int)entry.ElapsedMilliseconds, error);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(report,
                NativeProblemHttpJsonContext.Default.AndroidDiagnosticReport))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            var retry = response.Headers.RetryAfter;
            TimeSpan delay = retry?.Delta ?? (retry?.Date is { } date ? date - _clock.GetUtcNow() : TimeSpan.FromMinutes(5));
            return NativeProblemDeliveryResult.Defer(delay);
        }
        if ((int)response.StatusCode is >= 300 and < 400
            || response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict
                or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType)
            return NativeProblemDeliveryResult.Rejected;
        if (response.StatusCode != HttpStatusCode.Accepted
            || response.Content.Headers.ContentLength is > MaximumResponseBytes
            || response.Content.Headers.ContentType?.MediaType != "application/json")
            return NativeProblemDeliveryResult.RetryLater;

        // Read at most cap+1 even for chunked/unknown-length responses. Do not
        // materialize an unbounded JSON object or trust a status code alone.
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        byte[] bytes = new byte[MaximumResponseBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(count), ct).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumResponseBytes) return NativeProblemDeliveryResult.RetryLater;
        try
        {
            var receipt = JsonSerializer.Deserialize(bytes.AsSpan(0, count),
                NativeProblemHttpJsonContext.Default.AndroidDiagnosticReceipt);
            return receipt is not null && receipt.ReportId == id && receipt.ReceivedAtUtc != default
                && receipt.ReceivedAtUtc >= entry.AtUtc.AddMinutes(-5)
                && receipt.ReceivedAtUtc <= _clock.GetUtcNow().AddMinutes(5)
                ? NativeProblemDeliveryResult.Accepted : NativeProblemDeliveryResult.RetryLater;
        }
        catch (JsonException) { return NativeProblemDeliveryResult.RetryLater; }
    }

    private static bool TryMap<TSource, TTarget>(TSource value, out TTarget mapped)
        where TSource : struct, Enum where TTarget : struct, Enum
    {
        mapped = default;
        return Enum.IsDefined(value) && Enum.TryParse(value.ToString(), out mapped) && Enum.IsDefined(mapped);
    }
}

[JsonSerializable(typeof(AndroidDiagnosticReport))]
[JsonSerializable(typeof(AndroidDiagnosticReceipt))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal partial class NativeProblemHttpJsonContext : JsonSerializerContext { }
