using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Platform;

public sealed class AndroidAccountLinkService : IAndroidAccountLinkService
{
    private const string InstallationIdKey = "chummer.account.installation-id.v1";
    private const string PrivateKeyKey = "chummer.account.installation-private-key.v1";
    private const string AccessTokenKey = "chummer.account.installation-grant.v1";
    private const string GrantExpiryKey = "chummer.account.installation-grant-expiry.v1";
    private const string PendingStateKey = "chummer.account.pending-state.v1";
    private const string PendingStartedKey = "chummer.account.pending-started.v1";
    private const string LastProofTimestampKey = "chummer.account.last-proof-timestamp.v1";
    private const string LinkPath = "/account/access/install-link";
    private const string CallbackPath = "/app/install-link";
    private const string HeadId = "android";
    private const string PlatformId = "android";
    private const string ReleaseChannel = "preview";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(18);
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IAndroidSystemService _systemService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AndroidAccountLinkSnapshot _snapshot = new(AndroidAccountLinkStatus.Loading, "Checking");

    public AndroidAccountLinkService(HttpClient httpClient, IAndroidSystemService systemService)
    {
        _httpClient = httpClient;
        _systemService = systemService;
    }

    public event EventHandler? Changed;

    public AndroidAccountLinkSnapshot Snapshot => _snapshot;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? installationId = await SecureStorage.Default.GetAsync(InstallationIdKey);
            string? accessToken = await SecureStorage.Default.GetAsync(AccessTokenKey);
            if (string.IsNullOrWhiteSpace(installationId) || string.IsNullOrWhiteSpace(accessToken))
            {
                string? pendingState = await SecureStorage.Default.GetAsync(PendingStateKey);
                SetSnapshot(string.IsNullOrWhiteSpace(pendingState)
                    ? new(AndroidAccountLinkStatus.Unlinked, "Not linked")
                    : new(AndroidAccountLinkStatus.Pending, "Finish linking", "Approve in your browser, then return."));
                return;
            }

            DateTimeOffset? expiresAtUtc = await ReadGrantExpiryAsync();
            if (expiresAtUtc <= DateTimeOffset.UtcNow)
            {
                ClearGrant();
                SetSnapshot(new(AndroidAccountLinkStatus.Unlinked, "Link expired", "Link again to restore account access."));
                return;
            }

            GrantValidationResult validation = await ValidateGrantAsync(installationId, accessToken, cancellationToken);
            if (validation == GrantValidationResult.Invalid)
            {
                ClearGrant();
                SetSnapshot(new(AndroidAccountLinkStatus.Unlinked, "Link expired", "Link again to restore account access."));
                return;
            }

            if (validation == GrantValidationResult.Offline)
            {
                SetSnapshot(new(AndroidAccountLinkStatus.Linked, "Linked", "Available offline.", expiresAtUtc));
                return;
            }

            if (expiresAtUtc is not null && expiresAtUtc <= DateTimeOffset.UtcNow.Add(RefreshWindow))
            {
                GrantContract? refreshed = await RefreshGrantAsync(installationId, accessToken, cancellationToken);
                if (refreshed is not null)
                {
                    await SaveGrantAsync(refreshed);
                    expiresAtUtc = refreshed.ExpiresAtUtc;
                }
            }

            SetSnapshot(new(AndroidAccountLinkStatus.Linked, "Linked", null, expiresAtUtc));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetSnapshot(new(AndroidAccountLinkStatus.Error, "Link unavailable", "Android secure storage could not be opened."));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BeginLinkAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            InstallationIdentity identity = await GetOrCreateInstallationIdentityAsync();
            string state = NewBase64UrlToken(24);
            await SecureStorage.Default.SetAsync(PendingStateKey, state);
            await SecureStorage.Default.SetAsync(
                PendingStartedKey,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            string callback = $"https://chummer.run{CallbackPath}?state={Uri.EscapeDataString(state)}";
            Dictionary<string, string> query = new(StringComparer.Ordinal)
            {
                ["installationId"] = identity.InstallationId,
                ["headId"] = HeadId,
                ["applicationVersion"] = AppInfo.Current.VersionString,
                ["releaseChannel"] = ReleaseChannel,
                ["platform"] = PlatformId,
                ["arch"] = ResolveArchitecture(),
                ["installLinkCallbackUri"] = callback,
                ["installLinkTransport"] = "proof_poll",
                ["publicKey"] = identity.PublicKey
            };
            string href = $"{LinkPath}?{string.Join('&', query.Select(static item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"))}";
            SetSnapshot(new(AndroidAccountLinkStatus.Pending, "Finish linking", "Approve in your browser, then return."));
            if (!await _systemService.OpenUriAsync(ChummerWebRoutes.Resolve(href)))
            {
                SetSnapshot(new(AndroidAccountLinkStatus.Error, "Browser unavailable", "Open account linking again."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetSnapshot(new(AndroidAccountLinkStatus.Error, "Link unavailable", "Android could not prepare a protected device key."));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumePendingLinkAsync(Uri? callbackUri = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? pendingState = await SecureStorage.Default.GetAsync(PendingStateKey);
            if (string.IsNullOrWhiteSpace(pendingState))
            {
                return;
            }

            if (callbackUri is not null && !IsExpectedCallback(callbackUri, pendingState))
            {
                SetSnapshot(new(AndroidAccountLinkStatus.Error, "Return rejected", "Start linking again from this app."));
                return;
            }

            DateTimeOffset? pendingStarted = await ReadTimestampAsync(PendingStartedKey);
            if (pendingStarted is null || pendingStarted < DateTimeOffset.UtcNow.Subtract(PendingLifetime))
            {
                ClearPending();
                SetSnapshot(new(AndroidAccountLinkStatus.Error, "Approval expired", "Start a fresh account link."));
                return;
            }

            InstallationIdentity identity = await GetOrCreateInstallationIdentityAsync();
            using RSA signingKey = RSA.Create();
            byte[] privateKey = Convert.FromBase64String(identity.PrivateKey);
            try
            {
                signingKey.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
                if (bytesRead != privateKey.Length)
                {
                    throw new CryptographicException("The installation key is invalid.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }

            long issuedAt = await NextProofTimestampAsync();
            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            string architecture = ResolveArchitecture();
            string version = AppInfo.Current.VersionString;
            byte[] proof = Encoding.UTF8.GetBytes(string.Join(
                '\n',
                "chummer.install-link.remote-callback.v1",
                identity.InstallationId,
                HeadId,
                version,
                ReleaseChannel,
                PlatformId,
                architecture,
                issuedAt.ToString(CultureInfo.InvariantCulture),
                nonce));
            string signature;
            try
            {
                signature = Convert.ToBase64String(signingKey.SignData(
                    proof,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(proof);
            }

            PollRequest request = new(
                identity.InstallationId,
                HeadId,
                version,
                ReleaseChannel,
                PlatformId,
                architecture,
                identity.PublicKey,
                issuedAt,
                nonce,
                signature,
                ResolveHostLabel());
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                "/api/v1/install-linking/callbacks/poll",
                request,
                JsonOptions,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                SetSnapshot(new(AndroidAccountLinkStatus.Pending, "Finish linking", "Approve in your browser, then return."));
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    ClearAllCredentials();
                    SetSnapshot(new(
                        AndroidAccountLinkStatus.Error,
                        "Fresh link required",
                        "Choose Link account to create a new protected device identity."));
                }
                else
                {
                    SetSnapshot(response.StatusCode == HttpStatusCode.NotFound
                        ? new(AndroidAccountLinkStatus.Error, "Approval expired", "Start a fresh account link.")
                        : new(AndroidAccountLinkStatus.Error, "Could not link", "Check your connection and try again."));
                }
                return;
            }

            ExchangeResponse? exchange = await response.Content.ReadFromJsonAsync<ExchangeResponse>(JsonOptions, cancellationToken);
            if (exchange?.Grant is null || string.IsNullOrWhiteSpace(exchange.Grant.AccessToken))
            {
                SetSnapshot(new(AndroidAccountLinkStatus.Error, "Could not link", "The server returned an incomplete grant."));
                return;
            }

            await SaveGrantAsync(exchange.Grant);
            ClearPending();
            SetSnapshot(new(AndroidAccountLinkStatus.Linked, "Linked", null, exchange.Grant.ExpiresAtUtc));
        }
        catch (HttpRequestException)
        {
            SetSnapshot(new(AndroidAccountLinkStatus.Pending, "Finish linking", "Connect to the internet and try again."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetSnapshot(new(AndroidAccountLinkStatus.Error, "Could not link", "Start a fresh protected account link."));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string? installationId = await SecureStorage.Default.GetAsync(InstallationIdKey);
            string? accessToken = await SecureStorage.Default.GetAsync(AccessTokenKey);
            if (!string.IsNullOrWhiteSpace(installationId) && !string.IsNullOrWhiteSpace(accessToken))
            {
                using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                    "/api/v1/install-linking/grants/revoke",
                    new GrantBearerRequest(installationId, accessToken),
                    JsonOptions,
                    cancellationToken);
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    SetSnapshot(new(AndroidAccountLinkStatus.Error, "Still linked", "Could not reach Chummer. Try unlinking again."));
                    return;
                }
            }

            ClearAllCredentials();
            SetSnapshot(new(AndroidAccountLinkStatus.Unlinked, "Not linked"));
        }
        catch (HttpRequestException)
        {
            SetSnapshot(new(AndroidAccountLinkStatus.Error, "Still linked", "Connect to the internet and try again."));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task OpenAccountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _systemService.OpenUriAsync(ChummerWebRoutes.Resolve(ChummerWebRoutes.AccountAccess));
    }

    private async Task<GrantValidationResult> ValidateGrantAsync(
        string installationId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                "/api/v1/install-linking/grants/status",
                new GrantBearerRequest(installationId, accessToken),
                JsonOptions,
                cancellationToken);
            return response.IsSuccessStatusCode
                ? GrantValidationResult.Valid
                : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                    ? GrantValidationResult.Invalid
                    : GrantValidationResult.Offline;
        }
        catch (HttpRequestException)
        {
            return GrantValidationResult.Offline;
        }
    }

    private async Task<GrantContract?> RefreshGrantAsync(
        string installationId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            InstallationIdentity identity = await GetOrCreateInstallationIdentityAsync();
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                "/api/v1/install-linking/grants/refresh",
                new RefreshRequest(
                    installationId,
                    accessToken,
                    HeadId,
                    AppInfo.Current.VersionString,
                    ReleaseChannel,
                    PlatformId,
                    ResolveArchitecture(),
                    identity.PublicKey,
                    ResolveHostLabel()),
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            RefreshResponse? refreshed = await response.Content.ReadFromJsonAsync<RefreshResponse>(JsonOptions, cancellationToken);
            return refreshed?.Grant;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static bool IsExpectedCallback(Uri callbackUri, string expectedState)
    {
        if (!string.Equals(callbackUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(callbackUri.Host, "chummer.run", StringComparison.OrdinalIgnoreCase)
            || !callbackUri.IsDefaultPort
            || !string.Equals(callbackUri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            return false;
        }

        string? state = ParseQueryValue(callbackUri.Query, "state");
        if (state is null)
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(expectedState);
        byte[] actual = Encoding.UTF8.GetBytes(state);
        try
        {
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private async Task<InstallationIdentity> GetOrCreateInstallationIdentityAsync()
    {
        string? installationId = await SecureStorage.Default.GetAsync(InstallationIdKey);
        string? privateKey = await SecureStorage.Default.GetAsync(PrivateKeyKey);
        if (!string.IsNullOrWhiteSpace(installationId) && !string.IsNullOrWhiteSpace(privateKey))
        {
            try
            {
                using RSA existingKey = RSA.Create();
                byte[] existingBytes = Convert.FromBase64String(privateKey);
                try
                {
                    existingKey.ImportPkcs8PrivateKey(existingBytes, out int bytesRead);
                    if (bytesRead == existingBytes.Length && existingKey.KeySize >= 2048)
                    {
                        return new(installationId, privateKey, Convert.ToBase64String(existingKey.ExportRSAPublicKey()));
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(existingBytes);
                }
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                ClearAllCredentials();
                throw new CryptographicException("The protected installation identity is invalid.", ex);
            }

            ClearAllCredentials();
            throw new CryptographicException("The protected installation identity is invalid.");
        }

        SecureStorage.Default.Remove(InstallationIdKey);
        SecureStorage.Default.Remove(PrivateKeyKey);
        SecureStorage.Default.Remove(LastProofTimestampKey);
        ClearGrant();

        using RSA newKey = RSA.Create(2048);
        byte[] exportedPrivateKey = newKey.ExportPkcs8PrivateKey();
        try
        {
            installationId = $"android-{NewBase64UrlToken(24)}";
            privateKey = Convert.ToBase64String(exportedPrivateKey);
            await SecureStorage.Default.SetAsync(InstallationIdKey, installationId);
            await SecureStorage.Default.SetAsync(PrivateKeyKey, privateKey);
            return new(installationId, privateKey, Convert.ToBase64String(newKey.ExportRSAPublicKey()));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exportedPrivateKey);
        }
    }

    private static async Task SaveGrantAsync(GrantContract grant)
    {
        await SecureStorage.Default.SetAsync(AccessTokenKey, grant.AccessToken);
        await SecureStorage.Default.SetAsync(
            GrantExpiryKey,
            grant.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static async Task<DateTimeOffset?> ReadGrantExpiryAsync()
        => await ReadTimestampAsync(GrantExpiryKey);

    private static async Task<DateTimeOffset?> ReadTimestampAsync(string key)
    {
        string? value = await SecureStorage.Default.GetAsync(key);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static async Task<long> NextProofTimestampAsync()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string? saved = await SecureStorage.Default.GetAsync(LastProofTimestampKey);
        if (long.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out long last) && now <= last)
        {
            now = last + 1;
        }

        await SecureStorage.Default.SetAsync(LastProofTimestampKey, now.ToString(CultureInfo.InvariantCulture));
        return now;
    }

    private static string ResolveArchitecture()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => "unknown"
        };

    private static string ResolveHostLabel()
    {
        string candidate = DeviceInfo.Current.Name;
        string cleaned = new(candidate.Where(static value => !char.IsControl(value)).Take(120).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Android device" : cleaned.Trim();
    }

    private static string NewBase64UrlToken(int bytes)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? ParseQueryValue(string query, string key)
    {
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))
            {
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            }
        }

        return null;
    }

    private void SetSnapshot(AndroidAccountLinkSnapshot snapshot)
    {
        if (_snapshot == snapshot)
        {
            return;
        }

        _snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void ClearGrant()
    {
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(GrantExpiryKey);
    }

    private static void ClearPending()
    {
        SecureStorage.Default.Remove(PendingStateKey);
        SecureStorage.Default.Remove(PendingStartedKey);
    }

    private static void ClearAllCredentials()
    {
        ClearGrant();
        ClearPending();
        SecureStorage.Default.Remove(InstallationIdKey);
        SecureStorage.Default.Remove(PrivateKeyKey);
        SecureStorage.Default.Remove(LastProofTimestampKey);
    }

    private enum GrantValidationResult
    {
        Valid,
        Invalid,
        Offline
    }

    private sealed record InstallationIdentity(string InstallationId, string PrivateKey, string PublicKey);
    private sealed record GrantBearerRequest(string InstallationId, string AccessToken);
    private sealed record PollRequest(
        string InstallationId,
        string HeadId,
        string ApplicationVersion,
        string ChannelId,
        string Platform,
        string Arch,
        string PublicKey,
        long IssuedAtUnixSeconds,
        string Nonce,
        string Signature,
        string HostLabel);
    private sealed record RefreshRequest(
        string InstallationId,
        string AccessToken,
        string HeadId,
        string ApplicationVersion,
        string ChannelId,
        string Platform,
        string Arch,
        string PublicKey,
        string HostLabel);
    private sealed record GrantContract(
        string GrantId,
        string InstallationId,
        string Status,
        string AccessToken,
        DateTimeOffset IssuedAtUtc,
        DateTimeOffset ExpiresAtUtc);
    private sealed record ExchangeResponse(GrantContract Grant, bool AlreadyClaimed);
    private sealed record RefreshResponse(GrantContract Grant, bool Rotated);
}
