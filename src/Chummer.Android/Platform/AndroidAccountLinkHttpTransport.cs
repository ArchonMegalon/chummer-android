using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Android.Platform;

internal sealed class AndroidAccountLinkHttpTransport : IDisposable
{
    internal const long MaxResponseBodyBytes = 16L * 1024 * 1024;
    internal static readonly Uri TrustedOrigin = new("https://chummer.run/");
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _responseReadTimeout;

    internal AndroidAccountLinkHttpTransport(
        HttpMessageHandler terminalHandler,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(terminalHandler);
        TimeSpan timeout = requestTimeout ?? RequestTimeout;
        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        _httpClient = new HttpClient(
            new AndroidAccountLinkAuthorizationHandler(terminalHandler),
            disposeHandler: true)
        {
            BaseAddress = TrustedOrigin,
            Timeout = timeout
        };
        _responseReadTimeout = timeout;
    }

    internal static AndroidAccountLinkHttpTransport CreateDefault()
        => new(new HttpClientHandler
        {
            // The transport evaluates every redirect itself. A bearer credential must never be
            // entrusted to framework redirect behavior or replayed outside the exact origin.
            AllowAutoRedirect = false,
            UseCookies = false
        });

    internal async Task<HttpResponseMessage> PostJsonAsync(
        string path,
        object payload,
        AndroidAccountLinkRequestAuthority? authority,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(payload);

        Uri requestUri = ResolveTrustedRequestUri(path);
        EnsureAllowedRoute(requestUri.AbsolutePath, authority is not null);
        byte[] json = SerializeCredentialFreePayload(payload, authority?.AccessToken);
        using HttpRequestMessage request = new RedactedAccountLinkRequestMessage(
            HttpMethod.Post,
            requestUri)
        {
            Content = new ByteArrayContent(json)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = Encoding.UTF8.WebName
        };
        if (authority is not null)
        {
            await authority.ApplyAsync(request, json, cancellationToken);
            AndroidAccountLinkAuthorizationHandler.SetBearerToken(request, authority.AccessToken);
        }

        HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!IsRedirect(response.StatusCode))
        {
            return response;
        }

        bool crossOrigin = response.Headers.Location is Uri location
            && !IsTrustedRedirect(requestUri, location);
        response.Dispose();
        throw new HttpRequestException(
            crossOrigin
                ? "A cross-origin Chummer account redirect was rejected."
                : "A Chummer account redirect was rejected.");
    }

    internal async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaxResponseBodyBytes)
        {
            throw OversizedResponse();
        }

        try
        {
            using CancellationTokenSource? timeoutSource = CreateResponseReadTimeoutSource(cancellationToken);
            CancellationToken readToken = timeoutSource?.Token ?? cancellationToken;
            await using Stream responseStream = await response.Content.ReadAsStreamAsync(readToken);
            await using var cappedStream = new CappedReadStream(
                responseStream,
                MaxResponseBodyBytes,
                leaveOpen: true);
            T? payload = await JsonSerializer.DeserializeAsync<T>(
                cappedStream,
                JsonOptions,
                readToken);
            return payload
                ?? throw new InvalidDataException("Chummer returned an empty account response.");
        }
        catch (ResponseBodyTooLargeException)
        {
            throw OversizedResponse();
        }
        catch (JsonException)
        {
            // Do not retain parser details: hostile response fragments can contain credentials.
            throw new InvalidDataException("Chummer returned an invalid account response.");
        }
        catch (NotSupportedException)
        {
            throw new InvalidDataException("Chummer returned an invalid account response.");
        }
    }

    public void Dispose()
        => _httpClient.Dispose();

    internal static string FormatRequestForDiagnostics(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string authorization = request.Headers.Authorization is null
            ? "none"
            : $"{request.Headers.Authorization.Scheme} [REDACTED]";
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Method={request.Method}; Uri={request.RequestUri}; Authorization={authorization}");
    }

    internal static AndroidAccountLinkResponseGrantAuthority ReadResponseGrantAuthority(
        HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            string authorization = ReadSingleResponseHeader(
                response,
                "Authorization",
                1024,
                allowSpaces: true);
            string grantId = ReadSingleResponseHeader(
                response,
                AndroidAccountLinkRequestAuthority.GrantHeader,
                128);
            if (!AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? parsed)
                || !string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(parsed.Parameter)
                || parsed.Parameter.Length > 256)
            {
                throw InvalidGrantHeaders();
            }
            return new AndroidAccountLinkResponseGrantAuthority(grantId, parsed.Parameter);
        }
        finally
        {
            // A response object is frequently included wholesale in diagnostics. Once the rotated
            // credential has crossed the explicit parsing boundary, keep it out of that surface.
            response.Headers.Remove("Authorization");
        }
    }

    private Uri ResolveTrustedRequestUri(string path)
    {
        if (!path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains("\\", StringComparison.Ordinal)
            || path.Contains("?", StringComparison.Ordinal)
            || path.Contains("#", StringComparison.Ordinal)
            || !Uri.TryCreate(path, UriKind.Relative, out Uri? relative))
        {
            throw new InvalidOperationException("The account-link request path is invalid.");
        }

        Uri requestUri = new(_httpClient.BaseAddress!, relative);
        return IsTrustedOrigin(requestUri)
            && string.Equals(path, requestUri.AbsolutePath, StringComparison.Ordinal)
            ? requestUri
            : throw new InvalidOperationException("The account-link request origin is not trusted.");
    }

    private static void EnsureAllowedRoute(string exactPath, bool authenticated)
    {
        bool allowed = authenticated
            ? exactPath is "/api/v2/install-linking/grants/status"
                or "/api/v2/install-linking/grants/refresh"
                or "/api/v2/install-linking/grants/revoke"
                or "/api/v2/install-linking/continuation/workspaces/list"
                or "/api/v2/install-linking/continuation/workspaces/upsert"
                || exactPath.StartsWith("/api/v2/android/linked/", StringComparison.Ordinal)
            : string.Equals(
                exactPath,
                AndroidAccountLinkBootstrapProof.PollPath,
                StringComparison.Ordinal);
        if (!allowed)
        {
            throw new InvalidOperationException("The account-link request route is not allowed.");
        }
    }

    private static bool IsTrustedRedirect(Uri requestUri, Uri location)
    {
        Uri resolved = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        return IsTrustedOrigin(resolved);
    }

    internal static bool IsTrustedOrigin(Uri uri)
        => uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, TrustedOrigin.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == TrustedOrigin.Port
            && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsRedirect(HttpStatusCode statusCode)
        => (int)statusCode is >= 300 and <= 399;

    private static string ReadSingleResponseHeader(
        HttpResponseMessage response,
        string name,
        int maxLength,
        bool allowSpaces = false)
    {
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            throw InvalidGrantHeaders();
        }
        string[] materialized = values.ToArray();
        if (materialized.Length != 1)
        {
            throw InvalidGrantHeaders();
        }
        string value = materialized[0];
        return value.Length is > 0
            && value.Length <= maxLength
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && !value.Contains(',', StringComparison.Ordinal)
            && !value.Any(character => char.IsControl(character)
                || (!allowSpaces && char.IsWhiteSpace(character)))
            ? value
            : throw InvalidGrantHeaders();
    }

    private static InvalidDataException InvalidGrantHeaders()
        => new("Chummer returned invalid account grant headers.");

    private static byte[] SerializeCredentialFreePayload(object payload, string? accessToken)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            payload.GetType(),
            JsonOptions);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (ContainsAccessTokenProperty(document.RootElement)
                || (!string.IsNullOrEmpty(accessToken)
                    && ContainsStringValue(document.RootElement, accessToken)))
            {
                throw new InvalidOperationException(
                    "An account-link bearer credential cannot be serialized into a request body.");
            }
            return json;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(json);
            throw;
        }
    }

    private static bool ContainsAccessTokenProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "accessToken", StringComparison.OrdinalIgnoreCase)
                    || ContainsAccessTokenProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (ContainsAccessTokenProperty(item))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool ContainsStringValue(JsonElement element, string accessToken)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?.Contains(accessToken, StringComparison.Ordinal) == true;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (ContainsStringValue(property.Value, accessToken))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (ContainsStringValue(item, accessToken))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private CancellationTokenSource? CreateResponseReadTimeoutSource(
        CancellationToken cancellationToken)
    {
        if (_responseReadTimeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_responseReadTimeout);
        return timeoutSource;
    }

    private static InvalidDataException OversizedResponse()
        => new("The Chummer account response is too large to open safely.");

    private sealed class RedactedAccountLinkRequestMessage : HttpRequestMessage
    {
        internal RedactedAccountLinkRequestMessage(HttpMethod method, Uri requestUri)
            : base(method, requestUri)
        {
        }

        public override string ToString()
            => FormatRequestForDiagnostics(this);
    }

    private sealed class CappedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private readonly bool _leaveOpen;
        private long _bytesRead;

        internal CappedReadStream(Stream inner, long limit, bool leaveOpen)
        {
            _inner = inner;
            _limit = limit;
            _leaveOpen = leaveOpen;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, AllowedReadSize(count));
            RecordRead(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _inner.Read(buffer[..AllowedReadSize(buffer.Length)]);
            RecordRead(read);
            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int read = await _inner.ReadAsync(
                buffer,
                offset,
                AllowedReadSize(count),
                cancellationToken);
            RecordRead(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(
                buffer[..AllowedReadSize(buffer.Length)],
                cancellationToken);
            RecordRead(read);
            return read;
        }

        public override void Flush()
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_leaveOpen)
            {
                await _inner.DisposeAsync();
            }
            GC.SuppressFinalize(this);
        }

        private int AllowedReadSize(int requested)
        {
            if (requested <= 0)
            {
                return 0;
            }

            long remainingWithProbe = _limit - _bytesRead + 1;
            return (int)Math.Min(requested, Math.Max(1, remainingWithProbe));
        }

        private void RecordRead(int read)
        {
            if (_bytesRead + read > _limit)
            {
                throw new ResponseBodyTooLargeException();
            }
            _bytesRead += read;
        }
    }

    private sealed class ResponseBodyTooLargeException : IOException;
}

internal sealed class AndroidAccountLinkResponseGrantAuthority
{
    internal AndroidAccountLinkResponseGrantAuthority(string grantId, string accessToken)
    {
        GrantId = grantId;
        AccessToken = accessToken;
    }

    internal string GrantId { get; }
    internal string AccessToken { get; }

    public override string ToString()
        => $"AndroidAccountLinkResponseGrantAuthority {{ GrantId = {GrantId}, AccessToken = [REDACTED] }}";
}

internal static class AndroidAccountLinkBootstrapProof
{
    internal const string Scheme = "chummer.install-link.remote-callback.v2";
    internal const string PollPath = "/api/v2/install-linking/callbacks/poll";

    internal static byte[] CreateCanonicalPayload(
        string installationId,
        string headId,
        string applicationVersion,
        string channelId,
        string platform,
        string architecture,
        long issuedAtUnixSeconds,
        string nonce,
        string hostLabel)
        => Encoding.UTF8.GetBytes(string.Join(
            '\n',
            Scheme,
            "POST",
            PollPath,
            installationId,
            headId,
            applicationVersion,
            channelId,
            platform,
            architecture,
            issuedAtUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            nonce,
            hostLabel));
}

internal sealed class AndroidAccountLinkRequestAuthority
{
    internal const string Scheme = "chummer.android.packet.v2";
    internal const string SchemeHeader = "X-Chummer-App-Proof";
    internal const string InstallationHeader = "X-Chummer-Installation";
    internal const string GrantHeader = "X-Chummer-Grant";
    internal const string PacketKeyHeader = "X-Chummer-Packet-Key";
    internal const string IssuedHeader = "X-Chummer-Packet-Issued";
    internal const string SignatureHeader = "X-Chummer-Packet-Signature";
    internal const int PacketKeyBytes = 32;

    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>> _signCanonicalPayload;

    internal AndroidAccountLinkRequestAuthority(
        string installationId,
        string grantId,
        string accessToken,
        long issuedAtUnixSeconds,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>> signCanonicalPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(signCanonicalPayload);
        if (installationId.Length > 128 || grantId.Length > 128 || accessToken.Length > 256)
        {
            throw new ArgumentException("Android account request authority is invalid.");
        }
        try
        {
            _ = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Android account bearer credential is invalid.");
        }
        if (issuedAtUnixSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(issuedAtUnixSeconds));
        }

        InstallationId = installationId;
        GrantId = grantId;
        AccessToken = accessToken;
        IssuedAtUnixSeconds = issuedAtUnixSeconds;
        _signCanonicalPayload = signCanonicalPayload;
    }

    internal string InstallationId { get; }
    internal string GrantId { get; }
    internal string AccessToken { get; }
    internal long IssuedAtUnixSeconds { get; }

    internal async Task ApplyAsync(
        HttpRequestMessage request,
        ReadOnlyMemory<byte> exactBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is null
            || !AndroidAccountLinkHttpTransport.IsTrustedOrigin(request.RequestUri)
            || !string.IsNullOrEmpty(request.RequestUri.Query))
        {
            throw new InvalidOperationException(
                "Android account request proof requires one trusted query-free URI.");
        }

        byte[] packetBytes = RandomNumberGenerator.GetBytes(PacketKeyBytes);
        string packetKey;
        try
        {
            packetKey = Convert.ToBase64String(packetBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(packetBytes);
        }

        byte[] canonical = CreateCanonicalPayload(
            request.Method,
            request.RequestUri.AbsolutePath,
            InstallationId,
            GrantId,
            IssuedAtUnixSeconds,
            packetKey,
            exactBody.Span);
        byte[] signatureBytes;
        try
        {
            signatureBytes = await _signCanonicalPayload(canonical, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }
        if (signatureBytes is null || signatureBytes.Length == 0)
        {
            throw new CryptographicException("Android account request proof signing failed.");
        }
        string signature;
        try
        {
            signature = Convert.ToBase64String(signatureBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signatureBytes);
        }

        request.Headers.Add(SchemeHeader, Scheme);
        request.Headers.Add(InstallationHeader, InstallationId);
        request.Headers.Add(GrantHeader, GrantId);
        request.Headers.Add(
            IssuedHeader,
            IssuedAtUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(PacketKeyHeader, packetKey);
        request.Headers.Add(SignatureHeader, signature);
    }

    internal static byte[] CreateCanonicalPayload(
        HttpMethod method,
        string exactPath,
        string installationId,
        string grantId,
        long issuedAtUnixSeconds,
        string packetKey,
        ReadOnlySpan<byte> exactBody)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(exactPath);
        string bodyDigest = Convert.ToHexString(SHA256.HashData(exactBody)).ToLowerInvariant();
        return Encoding.UTF8.GetBytes(string.Join(
            '\n',
            Scheme,
            method.Method.ToUpperInvariant(),
            exactPath,
            installationId,
            grantId,
            issuedAtUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            packetKey,
            $"sha256:{bodyDigest}"));
    }

    public override string ToString()
        => $"AndroidAccountLinkRequestAuthority {{ InstallationId = {InstallationId}, GrantId = {GrantId}, AccessToken = [REDACTED], IssuedAtUnixSeconds = {IssuedAtUnixSeconds} }}";
}

internal sealed class AndroidAccountLinkAuthorizationHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<SensitiveBearerToken> BearerTokenKey =
        new("Chummer.Android.AccountLink.BearerToken");

    internal AndroidAccountLinkAuthorizationHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    internal static void SetBearerToken(HttpRequestMessage request, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        request.Options.Set(BearerTokenKey, new SensitiveBearerToken(accessToken));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(BearerTokenKey, out SensitiveBearerToken? bearerToken))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (request.RequestUri is null
            || !AndroidAccountLinkHttpTransport.IsTrustedOrigin(request.RequestUri))
        {
            throw new HttpRequestException(
                "The Chummer account credential was not sent to an untrusted origin.");
        }

        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Value);
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            request.Headers.Authorization = null;
            request.Options.Set(BearerTokenKey, SensitiveBearerToken.Redacted);
        }
    }

    private sealed class SensitiveBearerToken
    {
        internal static SensitiveBearerToken Redacted { get; } = new(string.Empty);

        internal SensitiveBearerToken(string value)
        {
            Value = value;
        }

        internal string Value { get; }

        public override string ToString() => "[REDACTED]";
    }
}
