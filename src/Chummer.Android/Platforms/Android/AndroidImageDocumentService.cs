using System.Buffers;
using System.Security.Cryptography;
using Android.App;
using Android.Content;
using Android.Database;
using Android.Graphics;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;

namespace Chummer.Android.Platform;

public sealed class AndroidImageDocumentService : IAndroidImageDocumentService
{
    public async Task<AndroidImageDocumentCandidate?> OpenValidatedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Activity activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("No active Android activity is available.");
        Intent intent = new(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("image/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);

        global::Android.Net.Uri? uri = await DocumentIntentBroker.LaunchAsync(
            activity,
            intent,
            DocumentIntentBroker.ImageOpenRequestCode,
            cancellationToken);
        if (uri is null)
        {
            return null;
        }
        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Android image selection requires a content:// document URI.");
        }

        ContentResolver resolver = activity.ContentResolver
            ?? throw new IOException("Android did not provide a content resolver.");
        string? declaredMediaType = resolver.GetType(uri);
        if (!AndroidImageDocumentValidation.IsImageMediaType(declaredMediaType))
        {
            throw new InvalidDataException("The selected document does not declare a concrete image MIME type.");
        }
        long? declaredSize = ResolveDeclaredSize(resolver, uri);
        if (declaredSize is < 1 or > AndroidImageDocumentValidation.MaximumEncodedBytes)
        {
            throw new InvalidDataException(
                $"The selected image must contain between 1 and {AndroidImageDocumentValidation.MaximumEncodedBytes} encoded bytes.");
        }

        TryPersistDocumentGrant(resolver, uri);
        byte[] encodedBytes;
        await using (Stream source = resolver.OpenInputStream(uri)
            ?? throw new IOException("Android did not provide a readable image-document stream."))
        {
            encodedBytes = await ReadBoundedAsync(source, cancellationToken);
        }

        try
        {
            (int width, int height, string decodedMediaType) = DecodeAndValidate(encodedBytes);
            if (!AndroidImageDocumentValidation.TryCreateCandidate(
                    ResolveDisplayName(resolver, uri) ?? "Selected image",
                    uri.ToString(),
                    declaredMediaType,
                    decodedMediaType,
                    width,
                    height,
                    encodedBytes,
                    out AndroidImageDocumentCandidate candidate))
            {
                throw new InvalidDataException(
                    "The selected image failed content URI, MIME, byte, pixel, or identity validation.");
            }
            return candidate;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encodedBytes);
        }
    }

    private static (int Width, int Height, string DecodedMediaType) DecodeAndValidate(
        byte[] encodedBytes)
    {
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        _ = BitmapFactory.DecodeByteArray(encodedBytes, 0, encodedBytes.Length, bounds);
        string decodedMediaType = bounds.OutMimeType ?? string.Empty;
        if (!AndroidImageDocumentValidation.IsImageMediaType(decodedMediaType)
            || !AndroidImageDocumentValidation.IsAllowedPixelSize(bounds.OutWidth, bounds.OutHeight))
        {
            throw new InvalidDataException(
                "The selected document is malformed, unsupported, or exceeds the decoded image bounds.");
        }

        using var options = new BitmapFactory.Options
        {
            InPreferredConfig = Bitmap.Config.Argb8888,
            InPremultiplied = true
        };
        using Bitmap? decoded = BitmapFactory.DecodeByteArray(
            encodedBytes,
            0,
            encodedBytes.Length,
            options);
        if (decoded is null
            || decoded.Width != bounds.OutWidth
            || decoded.Height != bounds.OutHeight
            || decoded.Config != Bitmap.Config.Argb8888
            || !decoded.IsPremultiplied
            || !AndroidImageDocumentValidation.IsAllowedPixelSize(decoded.Width, decoded.Height))
        {
            throw new InvalidDataException("Android could not fully decode the selected image as ARGB pixels.");
        }

        return (decoded.Width, decoded.Height, decodedMediaType);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using MemoryStream destination = new();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (destination.Length + read > AndroidImageDocumentValidation.MaximumEncodedBytes)
                {
                    throw new InvalidDataException(
                        $"The selected image exceeds the {AndroidImageDocumentValidation.MaximumEncodedBytes}-byte limit.");
                }
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (destination.Length == 0)
            {
                throw new InvalidDataException("The selected image document is empty.");
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

    private static long? ResolveDeclaredSize(
        ContentResolver resolver,
        global::Android.Net.Uri uri)
    {
        using ICursor? cursor = resolver.Query(uri, [IOpenableColumns.Size], null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }
        int index = cursor.GetColumnIndex(IOpenableColumns.Size);
        return index >= 0 && !cursor.IsNull(index) ? cursor.GetLong(index) : null;
    }

    private static void TryPersistDocumentGrant(
        ContentResolver resolver,
        global::Android.Net.Uri uri)
    {
        try
        {
            resolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        }
        catch (Java.Lang.SecurityException)
        {
            // Temporary provider grants are enough to validate the selected bytes.
        }
    }
}
