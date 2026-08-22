using System.Security.Cryptography;

namespace Chummer.Android.Platform;

/// <summary>
/// Immutable validation result for an Android image document. EncodedBase64 and
/// EncodedSha256 describe the exact selected bytes; they are not a Chummer5
/// storage encoding until AndroidMugshotStorageCodecParity is resolved.
/// </summary>
public sealed record AndroidImageDocumentCandidate(
    string DisplayName,
    string ContentUri,
    string DeclaredMediaType,
    string DecodedMediaType,
    int PixelWidth,
    int PixelHeight,
    int EncodedLength,
    string EncodedBase64,
    string EncodedSha256);

public interface IAndroidImageDocumentService
{
    Task<AndroidImageDocumentCandidate?> OpenValidatedAsync(
        CancellationToken cancellationToken = default);
}

public static class AndroidImageDocumentValidation
{
    public const int MaximumEncodedBytes = 16 * 1024 * 1024;
    public const int MaximumPixelDimension = 10_000;
    public const long MaximumPixelCount = 32_000_000;
    public const int Sha256HexLength = 64;

    public static bool IsImageMediaType(string? mediaType)
    {
        string normalized = NormalizeMediaType(mediaType);
        return normalized.StartsWith("image/", StringComparison.Ordinal)
            && normalized.Length > "image/".Length
            && !string.Equals(normalized, "image/*", StringComparison.Ordinal);
    }

    public static bool IsAllowedPixelSize(int width, int height)
        => width is > 0 and <= MaximumPixelDimension
            && height is > 0 and <= MaximumPixelDimension
            && (long)width * height <= MaximumPixelCount;

    public static bool TryCreateCandidate(
        string? displayName,
        string? contentUri,
        string? declaredMediaType,
        string? decodedMediaType,
        int pixelWidth,
        int pixelHeight,
        ReadOnlySpan<byte> encodedBytes,
        out AndroidImageDocumentCandidate candidate)
    {
        candidate = new AndroidImageDocumentCandidate(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            string.Empty,
            string.Empty);
        if (string.IsNullOrWhiteSpace(displayName)
            || !Uri.TryCreate(contentUri, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase)
            || !IsImageMediaType(declaredMediaType)
            || !IsImageMediaType(decodedMediaType)
            || encodedBytes.IsEmpty
            || encodedBytes.Length > MaximumEncodedBytes
            || !IsAllowedPixelSize(pixelWidth, pixelHeight))
        {
            return false;
        }

        string declared = NormalizeMediaType(declaredMediaType);
        string decoded = NormalizeMediaType(decodedMediaType);
        candidate = new AndroidImageDocumentCandidate(
            displayName.Trim(),
            uri.AbsoluteUri,
            declared,
            decoded,
            pixelWidth,
            pixelHeight,
            encodedBytes.Length,
            Convert.ToBase64String(encodedBytes),
            Convert.ToHexString(SHA256.HashData(encodedBytes)).ToLowerInvariant());
        return candidate.EncodedSha256.Length == Sha256HexLength;
    }

    private static string NormalizeMediaType(string? mediaType)
        => mediaType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;
}

/// <summary>
/// Explicit fail-closed gate between validated input bytes and a persisted
/// Chummer mugshot. Chummer5 first converts through GDI+ Format32bppPArgb and
/// then encodes PNG or GDI+ JPEG according to mutable SavedImageQuality.
/// Android currently has neither that setting authority nor a byte-equivalent
/// GDI+ encoder, so callers must not persist a candidate as a mugshot.
/// </summary>
public static class AndroidMugshotStorageCodecParity
{
    public const bool IsExactChummer5StorageEncodingAvailable = false;

    public const string Blocker =
        "Chummer5 mugshot storage depends on mutable SavedImageQuality and GDI+ PNG/JPEG encoding; "
        + "Android has no byte-equivalent codec/settings authority, so validated picker bytes cannot be persisted yet.";
}
