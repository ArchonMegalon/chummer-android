using Android.App;
using Android.Content;
using Android.Database;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;

namespace Chummer.Android.Platform;

public sealed class AndroidDocumentService : IAndroidDocumentService
{
    private const int MaxDocumentBytes = 8 * 1024 * 1024;

    public async Task<AndroidDocument?> OpenAsync(CancellationToken cancellationToken)
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

        TryPersistDocumentGrant(activity, uri, ActivityFlags.GrantReadUriPermission);
        await using Stream source = activity.ContentResolver?.OpenInputStream(uri)
            ?? throw new IOException("Android did not provide a readable document stream.");
        byte[] content = await ReadBoundedAsync(source, cancellationToken);
        return new AndroidDocument(
            ResolveDisplayName(activity, uri) ?? "Chummer document",
            uri.ToString() ?? string.Empty,
            activity.ContentResolver?.GetType(uri),
            content);
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

        TryPersistDocumentGrant(
            activity,
            uri,
            ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        await using Stream destination = activity.ContentResolver?.OpenOutputStream(uri, "wt")
            ?? throw new IOException("Android did not provide a writable document stream.");
        await content.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        return true;
    }

    private static string? ResolveDisplayName(Activity activity, global::Android.Net.Uri uri)
    {
        using ICursor? cursor = activity.ContentResolver?.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        int index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
        return index >= 0 ? cursor.GetString(index) : null;
    }

    private static void TryPersistDocumentGrant(
        Activity activity,
        global::Android.Net.Uri uri,
        ActivityFlags flags)
    {
        try
        {
            activity.ContentResolver?.TakePersistableUriPermission(uri, flags);
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
        byte[] buffer = new byte[32 * 1024];
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
}
