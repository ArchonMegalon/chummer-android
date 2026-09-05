using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chummer.Android.Platform;

public enum AndroidDeviceKeyAvailability
{
    Available,
    Missing,
    Invalidated
}

public sealed record AndroidDevicePublicKey(
    AndroidDeviceKeyAvailability Availability,
    string? PublicKey = null);

public interface IAndroidDeviceKeyStore
{
    Task<AndroidDevicePublicKey> CreateAsync(string alias, CancellationToken cancellationToken = default);

    Task<AndroidDevicePublicKey> GetPublicKeyAsync(string alias, CancellationToken cancellationToken = default);

    Task<byte[]> SignAsync(
        string alias,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string alias, CancellationToken cancellationToken = default);
}

public interface IAndroidAccountLinkKeyMetadataStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record AndroidAccountLinkKeyIdentity(
    string InstallationId,
    string Alias,
    string PublicKey,
    string? GrantId = null);

public sealed class AndroidDeviceRelinkRequiredException : CryptographicException
{
    public AndroidDeviceRelinkRequiredException(
        AndroidDeviceKeyAvailability availability,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Availability = availability;
    }

    public AndroidDeviceKeyAvailability Availability { get; }
}

/// <summary>
/// Owns the persisted public binding for the non-exportable account-link key. The private key
/// remains behind <see cref="IAndroidDeviceKeyStore"/> for every operation.
/// </summary>
public sealed class AndroidAccountLinkKeyAuthority
{
    internal const string InstallationIdStorageKey = "chummer.account.installation-id.v1";
    internal const string LegacyPrivateKeyStorageKey = "chummer.account.installation-private-key.v1";
    internal const string BindingStorageKey = "chummer.account.installation-key-binding.v2";
    internal const string CleanupTombstoneStorageKey = "chummer.account.installation-key-cleanup.v2";
    internal const string AliasPrefix = "com.myexternalbrain.chummer.account-link.v2.";
    private const int BindingVersion = 2;
    private const int CleanupTombstoneVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IAndroidDeviceKeyStore _keyStore;
    private readonly IAndroidAccountLinkKeyMetadataStore _metadataStore;

    public AndroidAccountLinkKeyAuthority(
        IAndroidDeviceKeyStore keyStore,
        IAndroidAccountLinkKeyMetadataStore metadataStore)
    {
        _keyStore = keyStore;
        _metadataStore = metadataStore;
    }

    public async Task RemoveLegacyPrivateKeyAsync(CancellationToken cancellationToken = default)
    {
        // Removal is deliberately unconditional: detecting the old format must not deserialize
        // or otherwise bring the exported PKCS#8 value back into application memory.
        await _metadataStore.RemoveAsync(LegacyPrivateKeyStorageKey, cancellationToken);
    }

    public async Task<AndroidAccountLinkKeyIdentity> StartOrResumeExplicitLinkAsync(
        CancellationToken cancellationToken = default)
    {
        await RemoveLegacyPrivateKeyAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(
                await _metadataStore.GetAsync(CleanupTombstoneStorageKey, cancellationToken)))
        {
            // A prior Keystore deletion failure is retried before any replacement authority can
            // be created. The tombstone is the only durable selector for that orphaned alias.
            await RemoveAsync(cancellationToken);
        }
        AndroidAccountLinkKeyIdentity? identity = null;
        try
        {
            identity = await ReadAndValidateAsync(
                expectedInstallationId: null,
                requireGrantBinding: false,
                cancellationToken);
        }
        catch (AndroidDeviceRelinkRequiredException)
        {
            await RemoveAsync(cancellationToken);
        }

        if (identity is null)
        {
            return await CreateAsync(cancellationToken);
        }

        if (identity.GrantId is null)
        {
            return identity;
        }

        AndroidAccountLinkKeyIdentity unbound = identity with { GrantId = null };
        await PersistBindingAsync(unbound, cancellationToken);
        return unbound;
    }

    public async Task<AndroidAccountLinkKeyIdentity> RequirePendingIdentityAsync(
        string installationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        await RemoveLegacyPrivateKeyAsync(cancellationToken);
        AndroidAccountLinkKeyIdentity identity = await ReadAndValidateAsync(
            expectedInstallationId: installationId,
            requireGrantBinding: false,
            cancellationToken)
            ?? throw RelinkRequired(AndroidDeviceKeyAvailability.Missing);
        if (identity.GrantId is not null)
        {
            throw new AndroidDeviceRelinkRequiredException(
                AndroidDeviceKeyAvailability.Invalidated,
                "The pending account link is bound to a completed grant.");
        }

        return identity;
    }

    public async Task<AndroidAccountLinkKeyIdentity> RequireLinkedIdentityAsync(
        string installationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        await RemoveLegacyPrivateKeyAsync(cancellationToken);
        return await ReadAndValidateAsync(
            installationId,
            requireGrantBinding: true,
            cancellationToken)
            ?? throw RelinkRequired(AndroidDeviceKeyAvailability.Missing);
    }

    public async Task BindGrantAsync(
        string installationId,
        string grantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
        AndroidAccountLinkKeyIdentity identity = await ReadAndValidateAsync(
            installationId,
            requireGrantBinding: false,
            cancellationToken)
            ?? throw RelinkRequired(AndroidDeviceKeyAvailability.Missing);
        await PersistBindingAsync(identity with { GrantId = grantId }, cancellationToken);
    }

    public async Task<byte[]> SignAsync(
        AndroidAccountLinkKeyIdentity identity,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        AndroidAccountLinkKeyIdentity current = await ReadAndValidateAsync(
            identity.InstallationId,
            requireGrantBinding: false,
            cancellationToken)
            ?? throw RelinkRequired(AndroidDeviceKeyAvailability.Missing);
        if (!string.Equals(current.Alias, identity.Alias, StringComparison.Ordinal)
            || !string.Equals(current.PublicKey, identity.PublicKey, StringComparison.Ordinal)
            || !string.Equals(current.GrantId, identity.GrantId, StringComparison.Ordinal))
        {
            throw new AndroidDeviceRelinkRequiredException(
                AndroidDeviceKeyAvailability.Invalidated,
                "The account-link key binding changed before signing.");
        }

        try
        {
            byte[]? signature = await _keyStore.SignAsync(current.Alias, payload, cancellationToken);
            if (signature is null
                || !VerifyProtocolSignature(current.PublicKey, payload.Span, signature))
            {
                if (signature is not null)
                {
                    CryptographicOperations.ZeroMemory(signature);
                }
                throw new AndroidDeviceRelinkRequiredException(
                    AndroidDeviceKeyAvailability.Invalidated,
                    "The Android account-link key returned an invalid signature.");
            }

            return signature;
        }
        catch (AndroidDeviceRelinkRequiredException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            throw new AndroidDeviceRelinkRequiredException(
                AndroidDeviceKeyAvailability.Invalidated,
                "The Android account-link key can no longer sign.",
                exception);
        }
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? installationId = null;
        string? serialized = null;
        string? serializedTombstone = null;
        Exception? firstFailure = null;

        async Task<bool> AttemptAsync(Func<Task> action)
        {
            try
            {
                await action();
                return true;
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
                return false;
            }
        }

        await AttemptAsync(async () =>
            installationId = await _metadataStore.GetAsync(
                InstallationIdStorageKey,
                cancellationToken));
        await AttemptAsync(async () =>
            serialized = await _metadataStore.GetAsync(
                BindingStorageKey,
                cancellationToken));
        await AttemptAsync(async () =>
            serializedTombstone = await _metadataStore.GetAsync(
                CleanupTombstoneStorageKey,
                cancellationToken));

        Dictionary<string, string> cleanupEntries = new(StringComparer.Ordinal);
        if (IsExpectedInstallationId(installationId))
        {
            cleanupEntries[installationId!] = AliasForInstallation(installationId!);
        }

        AndroidAccountLinkKeyIdentity? identity = TryDeserialize(serialized);
        if (identity is not null && IsExpectedInstallationId(identity.InstallationId))
        {
            cleanupEntries[identity.InstallationId] = AliasForInstallation(identity.InstallationId);
        }
        foreach (CleanupEntry entry in ReadCleanupTombstone(serializedTombstone))
        {
            cleanupEntries[entry.InstallationId] = entry.Alias;
        }

        // Invalidate a well-formed linked grant before its key is touched. Should later cleanup
        // fail, the retained binding is explicitly unbound and cannot authorize another packet.
        if (identity is not null
            && IsExpectedAlias(identity.InstallationId, identity.Alias)
            && IsValidProtocolPublicKey(identity.PublicKey))
        {
            await AttemptAsync(() => PersistBindingAsync(
                identity with { GrantId = null },
                CancellationToken.None));
        }

        CleanupTombstone tombstone = new(
            CleanupTombstoneVersion,
            cleanupEntries.Select(static pair => new CleanupEntry(pair.Key, pair.Value)).ToArray());
        bool tombstoneReady = cleanupEntries.Count == 0
            || await AttemptAsync(() => _metadataStore.SetAsync(
                CleanupTombstoneStorageKey,
                JsonSerializer.Serialize(tombstone, JsonOptions),
                CancellationToken.None));

        if (tombstoneReady)
        {
            bool bindingRemoved = await AttemptAsync(() => _metadataStore.RemoveAsync(
                BindingStorageKey,
                CancellationToken.None));
            bool installationRemoved = await AttemptAsync(() => _metadataStore.RemoveAsync(
                InstallationIdStorageKey,
                CancellationToken.None));
            bool legacyRemoved = await AttemptAsync(() => _metadataStore.RemoveAsync(
                LegacyPrivateKeyStorageKey,
                CancellationToken.None));
            bool aliasesDeleted = true;
            foreach (string alias in cleanupEntries.Values)
            {
                aliasesDeleted = await AttemptAsync(
                    () => _keyStore.DeleteAsync(alias, CancellationToken.None))
                    && aliasesDeleted;
            }

            if (bindingRemoved && installationRemoved && legacyRemoved && aliasesDeleted)
            {
                await AttemptAsync(() => _metadataStore.RemoveAsync(
                    CleanupTombstoneStorageKey,
                    CancellationToken.None));
            }
        }

        if (firstFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    public static string AliasForInstallation(string installationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        byte[] scope = Encoding.UTF8.GetBytes($"chummer.account-link-key.v2\n{installationId}");
        try
        {
            return AliasPrefix + Convert.ToHexString(SHA256.HashData(scope)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scope);
        }
    }

    private async Task<AndroidAccountLinkKeyIdentity> CreateAsync(CancellationToken cancellationToken)
    {
        string installationId = $"android-{NewBase64UrlToken(24)}";
        string alias = AliasForInstallation(installationId);
        AndroidDevicePublicKey created;
        try
        {
            created = await _keyStore.CreateAsync(alias, cancellationToken);
        }
        catch
        {
            // A hostile or buggy backend can observe cancellation after it has already generated
            // a persistent key. Cleanup uses a non-cancellable token so that key cannot orphan.
            await CleanupFailedCreationAsync(installationId, alias);
            throw;
        }
        if (created.Availability != AndroidDeviceKeyAvailability.Available
            || !IsValidProtocolPublicKey(created.PublicKey))
        {
            await _keyStore.DeleteAsync(alias, CancellationToken.None);
            throw RelinkRequired(created.Availability == AndroidDeviceKeyAvailability.Available
                ? AndroidDeviceKeyAvailability.Invalidated
                : created.Availability);
        }

        AndroidAccountLinkKeyIdentity identity = new(
            installationId,
            alias,
            created.PublicKey!,
            GrantId: null);
        try
        {
            await PersistBindingAsync(identity, cancellationToken);
            await _metadataStore.SetAsync(InstallationIdStorageKey, installationId, cancellationToken);
            return identity;
        }
        catch
        {
            await CleanupFailedCreationAsync(installationId, alias);
            throw;
        }
    }

    private async Task<AndroidAccountLinkKeyIdentity?> ReadAndValidateAsync(
        string? expectedInstallationId,
        bool requireGrantBinding,
        CancellationToken cancellationToken)
    {
        string? installationId = await _metadataStore.GetAsync(InstallationIdStorageKey, cancellationToken);
        string? serialized = await _metadataStore.GetAsync(BindingStorageKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(installationId) && string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        AndroidAccountLinkKeyIdentity? identity = TryDeserialize(serialized);
        if (identity is null
            || !string.Equals(installationId, identity.InstallationId, StringComparison.Ordinal)
            || (expectedInstallationId is not null
                && !string.Equals(expectedInstallationId, identity.InstallationId, StringComparison.Ordinal))
            || !IsExpectedInstallationId(identity.InstallationId)
            || !IsExpectedAlias(identity.InstallationId, identity.Alias)
            || !IsValidProtocolPublicKey(identity.PublicKey)
            || (requireGrantBinding && string.IsNullOrWhiteSpace(identity.GrantId)))
        {
            throw new AndroidDeviceRelinkRequiredException(
                AndroidDeviceKeyAvailability.Invalidated,
                "The stored account-link key binding is invalid.");
        }

        AndroidDevicePublicKey current = await _keyStore.GetPublicKeyAsync(identity.Alias, cancellationToken);
        if (current.Availability != AndroidDeviceKeyAvailability.Available
            || !string.Equals(current.PublicKey, identity.PublicKey, StringComparison.Ordinal))
        {
            throw RelinkRequired(current.Availability == AndroidDeviceKeyAvailability.Available
                ? AndroidDeviceKeyAvailability.Invalidated
                : current.Availability);
        }

        return identity;
    }

    private async Task PersistBindingAsync(
        AndroidAccountLinkKeyIdentity identity,
        CancellationToken cancellationToken)
    {
        StoredBinding binding = new(
            BindingVersion,
            identity.InstallationId,
            identity.Alias,
            identity.PublicKey,
            identity.GrantId);
        await _metadataStore.SetAsync(
            BindingStorageKey,
            JsonSerializer.Serialize(binding, JsonOptions),
            cancellationToken);
    }

    private static AndroidAccountLinkKeyIdentity? TryDeserialize(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        try
        {
            StoredBinding? binding = JsonSerializer.Deserialize<StoredBinding>(serialized, JsonOptions);
            if (binding is not { Version: BindingVersion }
                || string.IsNullOrWhiteSpace(binding.InstallationId)
                || string.IsNullOrWhiteSpace(binding.Alias)
                || string.IsNullOrWhiteSpace(binding.PublicKey))
            {
                return null;
            }

            return new(binding.InstallationId, binding.Alias, binding.PublicKey, binding.GrantId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsExpectedAlias(string installationId, string alias)
        => IsExpectedAlias(alias)
            && string.Equals(alias, AliasForInstallation(installationId), StringComparison.Ordinal);

    internal static bool IsExpectedAlias(string? alias)
    {
        if (alias is null
            || alias.Length != AliasPrefix.Length + 64
            || !alias.StartsWith(AliasPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return alias.AsSpan(AliasPrefix.Length).IndexOfAnyExcept("0123456789abcdef".AsSpan()) < 0;
    }

    private static IReadOnlyList<CleanupEntry> ReadCleanupTombstone(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return [];
        }

        try
        {
            CleanupTombstone? tombstone = JsonSerializer.Deserialize<CleanupTombstone>(
                serialized,
                JsonOptions);
            if (tombstone is not { Version: CleanupTombstoneVersion }
                || tombstone.Entries.Count is < 1 or > 8
                || tombstone.Entries.Any(static entry =>
                    !IsExpectedInstallationId(entry.InstallationId)
                    || !IsExpectedAlias(entry.InstallationId, entry.Alias)))
            {
                return [];
            }

            return tombstone.Entries;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task CleanupFailedCreationAsync(string installationId, string alias)
    {
        Exception? firstFailure = null;
        async Task<bool> AttemptAsync(Func<Task> action)
        {
            try
            {
                await action();
                return true;
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
                return false;
            }
        }

        bool aliasDeleted = await AttemptAsync(
            () => _keyStore.DeleteAsync(alias, CancellationToken.None));
        bool cleanupRecoverable = aliasDeleted;
        if (!aliasDeleted)
        {
            CleanupTombstone tombstone = new(
                CleanupTombstoneVersion,
                [new CleanupEntry(installationId, alias)]);
            cleanupRecoverable = await AttemptAsync(() => _metadataStore.SetAsync(
                CleanupTombstoneStorageKey,
                JsonSerializer.Serialize(tombstone, JsonOptions),
                CancellationToken.None));
        }

        // Remove partial link authority only after either the key is gone or its exact cleanup
        // selector is durable. A failed tombstone write must not erase the last recoverable alias.
        if (cleanupRecoverable)
        {
            await AttemptAsync(() => _metadataStore.RemoveAsync(BindingStorageKey, CancellationToken.None));
            await AttemptAsync(() => _metadataStore.RemoveAsync(InstallationIdStorageKey, CancellationToken.None));
        }
        if (firstFailure is not null)
        {
            throw new CryptographicException(
                "The failed Android account-link key could not be completely removed.",
                firstFailure);
        }
    }

    private static bool IsExpectedInstallationId(string? installationId)
    {
        const string prefix = "android-";
        if (installationId is null
            || installationId.Length != prefix.Length + 32
            || !installationId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return installationId.AsSpan(prefix.Length).IndexOfAnyExcept(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".AsSpan()) < 0;
    }

    private static bool IsValidProtocolPublicKey(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey.Length > 4096)
        {
            return false;
        }

        byte[]? encoded = null;
        try
        {
            encoded = Convert.FromBase64String(publicKey);
            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(encoded, out int bytesRead);
            return bytesRead == encoded.Length && rsa.KeySize >= 2048;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
        finally
        {
            if (encoded is not null)
            {
                CryptographicOperations.ZeroMemory(encoded);
            }
        }
    }

    private static bool VerifyProtocolSignature(
        string publicKey,
        ReadOnlySpan<byte> payload,
        byte[]? signature)
    {
        if (signature is null || signature.Length == 0)
        {
            return false;
        }

        byte[]? encoded = null;
        try
        {
            encoded = Convert.FromBase64String(publicKey);
            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(encoded, out int bytesRead);
            return bytesRead == encoded.Length
                && rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
        finally
        {
            if (encoded is not null)
            {
                CryptographicOperations.ZeroMemory(encoded);
            }
        }
    }

    private static AndroidDeviceRelinkRequiredException RelinkRequired(AndroidDeviceKeyAvailability availability)
        => new(availability, "The Android account-link key is unavailable. Start a fresh account link.");

    private static string NewBase64UrlToken(int bytes)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record StoredBinding(
        int Version,
        string InstallationId,
        string Alias,
        string PublicKey,
        string? GrantId);

    private sealed record CleanupTombstone(int Version, IReadOnlyList<CleanupEntry> Entries);

    private sealed record CleanupEntry(string InstallationId, string Alias);
}

public sealed class UnavailableAndroidDeviceKeyStore : IAndroidDeviceKeyStore
{
    public Task<AndroidDevicePublicKey> CreateAsync(
        string alias,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));

    public Task<AndroidDevicePublicKey> GetPublicKeyAsync(
        string alias,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));

    public Task<byte[]> SignAsync(
        string alias,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
        => Task.FromException<byte[]>(new AndroidDeviceRelinkRequiredException(
            AndroidDeviceKeyAvailability.Invalidated,
            "Android Keystore is unavailable outside the Android application."));

    public Task DeleteAsync(string alias, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
