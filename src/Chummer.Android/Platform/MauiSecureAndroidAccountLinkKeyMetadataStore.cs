using Microsoft.Maui.Storage;

namespace Chummer.Android.Platform;

public sealed class MauiSecureAndroidAccountLinkKeyMetadataStore : IAndroidAccountLinkKeyMetadataStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? value = await SecureStorage.Default.GetAsync(key);
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    public async Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(key, value);
        // SecureStorage has no cancellable write API. Once the write starts, report the completed
        // commit instead of turning a durable metadata update into an ambiguous cancellation.
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}
