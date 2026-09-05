using Android.App;
using Android.Content;
using Android.Database;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using System.Buffers;
using System.Security.Cryptography;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
using Chummer.Android.Proof;
#endif

namespace Chummer.Android.Platform;

public sealed class AndroidDocumentService : IAndroidDocumentService
{
    private const int MaxDocumentBytes = 8 * 1024 * 1024;

    public async Task<AndroidDocument?> OpenAsync(CancellationToken cancellationToken)
    {
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        Api36ProofStatePublisher.TryBeginDocumentImport();
#endif
        try
        {
            Activity activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? throw new InvalidOperationException("No active Android activity is available.");
            Intent intent = new(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
            intent.PutExtra(Intent.ExtraMimeTypes, new[] { "application/xml", "text/xml", "application/json", "application/octet-stream" });

            global::Android.Net.Uri? uri = await DocumentIntentBroker.LaunchAsync(
                activity,
                intent,
                DocumentIntentBroker.OpenRequestCode,
                cancellationToken);
            if (uri is null)
            {
                return null;
            }

            ContentResolver resolver = activity.ContentResolver
                ?? throw new IOException("Android did not provide a content resolver.");
            AndroidDocument document = await DocumentProviderWorkScheduler.RunAsync(
                token => OpenDocumentAsync(resolver, uri, token),
                cancellationToken);
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            Api36ProofStatePublisher.TryRecordDocumentStream(document);
#endif
            return document;
        }
        catch (Exception error)
        {
#if CHUMMER_API36_PROOF_INSTRUMENTATION
            Api36ProofStatePublisher.TryRecordDocumentImportFailure(
                "document-open-" + error.GetType().Name);
#endif
            throw;
        }
    }

    public async Task<bool> SaveAsAsync(
        string suggestedName,
        string mediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(content);

        Activity activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("No active Android activity is available.");
        Intent intent = new(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mediaType);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);
        intent.AddFlags(
            ActivityFlags.GrantReadUriPermission
            | ActivityFlags.GrantWriteUriPermission
            | ActivityFlags.GrantPersistableUriPermission);

        global::Android.Net.Uri? uri = await DocumentIntentBroker.LaunchAsync(
            activity,
            intent,
            DocumentIntentBroker.CreateRequestCode,
            cancellationToken);
        if (uri is null)
        {
            return false;
        }

        ContentResolver resolver = activity.ContentResolver
            ?? throw new IOException("Android did not provide a content resolver.");
        await DocumentProviderWorkScheduler.RunAsync(
            token => WriteDocumentAsync(resolver, uri, content, token),
            cancellationToken);
        return true;
    }

    private static async Task<AndroidDocument> OpenDocumentAsync(
        ContentResolver resolver,
        global::Android.Net.Uri uri,
        CancellationToken cancellationToken)
    {
        TryPersistDocumentGrant(resolver, uri, ActivityFlags.GrantReadUriPermission);
        await using Stream source = resolver.OpenInputStream(uri)
            ?? throw new IOException("Android did not provide a readable document stream.");
        byte[] content = await ReadBoundedAsync(source, cancellationToken).ConfigureAwait(false);
        return new AndroidDocument(
            ResolveDisplayName(resolver, uri) ?? "Chummer document",
            uri.ToString() ?? string.Empty,
            resolver.GetType(uri),
            content);
    }

    private static async Task WriteDocumentAsync(
        ContentResolver resolver,
        global::Android.Net.Uri uri,
        Stream content,
        CancellationToken cancellationToken)
    {
        TryPersistDocumentGrant(
            resolver,
            uri,
            ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        await using Stream destination = resolver.OpenOutputStream(uri, "wt")
            ?? throw new IOException("Android did not provide a writable document stream.");
        await content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? ResolveDisplayName(
        ContentResolver resolver,
        global::Android.Net.Uri uri)
    {
        using ICursor? cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        int index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
        return index >= 0 ? cursor.GetString(index) : null;
    }

    private static void TryPersistDocumentGrant(
        ContentResolver resolver,
        global::Android.Net.Uri uri,
        ActivityFlags flags)
    {
        try
        {
            resolver.TakePersistableUriPermission(uri, flags);
        }
        catch (Java.Lang.SecurityException)
        {
            // Some conforming document providers grant access only for the active intent.
            // Import/save still succeeds through that temporary grant; persistence is optional.
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        using MemoryStream destination = new();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (destination.Length + read > MaxDocumentBytes)
                {
                    throw new IOException("The selected document is larger than Chummer's 8 MB import limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return destination.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            if (destination.TryGetBuffer(out ArraySegment<byte> importedBytes))
            {
                CryptographicOperations.ZeroMemory(importedBytes.AsSpan(0, checked((int)destination.Length)));
            }
        }
    }
}
