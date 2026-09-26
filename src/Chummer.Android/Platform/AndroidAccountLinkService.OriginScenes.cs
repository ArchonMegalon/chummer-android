using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Platform;

public sealed partial class AndroidAccountLinkService : IAndroidOriginSceneTransport
{
    private sealed record SceneManifest(string Schema, string AssetId, string OwnerDigest, string WorkspaceId,
        string ChapterId, string ChapterDigest, string TextDigest, string AltText, string ContentType,
        int ContentLengthBytes, string ContentHash, int Width, int Height, string Provider,
        string ProviderReceiptDigest, string AdmissionDigest, bool PublicationAuthorized);
    private sealed record SceneWire(string AssetId, string State, bool PublicationAuthorized,
        SceneManifest? Manifest = null, string? ImageBase64 = null);
    private sealed record SceneConsent(string Excerpt, string AltText);
    private sealed record SceneDecision(string ImageHash, bool Approve);

    public Task<AndroidOriginSceneResult> ReadSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, CancellationToken ct = default) => SceneRequestAsync(owner, source, acceptedText, ct);

    public Task<AndroidOriginSceneResult> RequestSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, string sceneExcerpt, string altText, bool externalProcessingConsent, CancellationToken ct = default)
        => externalProcessingConsent && SceneText(sceneExcerpt, 3072) && SceneText(altText, 1024)
            && acceptedText is not null && acceptedText.Contains(sceneExcerpt, StringComparison.Ordinal)
            ? SceneRequestAsync(owner, source, acceptedText, ct, new(sceneExcerpt, altText))
            : Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Unavailable));

    public Task<AndroidOriginSceneResult> DecideSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, string expectedImageHash, bool approve, bool explicitlyConfirmed, CancellationToken ct = default)
        => explicitlyConfirmed && ChapterHex(expectedImageHash)
            ? SceneRequestAsync(owner, source, acceptedText, ct, decision: new(expectedImageHash, approve))
            : Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Unavailable));

    private static bool SceneText(string? value, int maximumBytes) => !string.IsNullOrWhiteSpace(value)
        && !value.Contains('\0') && Encoding.UTF8.GetByteCount(value) <= maximumBytes;

    private Task<AndroidOriginSceneResult> SceneRequestAsync(OwnerContextStamp owner, OriginChapterSource source,
        string text, CancellationToken ct, SceneConsent? consent = null, SceneDecision? decision = null)
    {
        try
        {
            if (!SceneText(text, 64 * 1024)) throw new ArgumentException();
            var captured = OriginChapterSourceIdentity.Capture(source);
            // As for chapter HTTP: signing, reading and Android stream disposal
            // must all stay off the UI thread. No mutation is automatically retried.
            return Task.Run(() => SceneRequestCoreAsync(owner, captured, text, ct, consent, decision));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Unavailable)); }
    }

    private async Task<AndroidOriginSceneResult> SceneRequestCoreAsync(OwnerContextStamp owner,
        OriginChapterSource source, string text, CancellationToken ct, SceneConsent? consent, SceneDecision? decision)
    {
        bool mutation = consent is not null || decision is not null, proofReleased = false, knownRejected = false;
        AndroidAccountOwnerState? expected = null;
        byte[]? image = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            expected = RequireContinuationOwner(owner);
            string textDigest = ChapterTextDigest(text), ownerDigest = ChapterTextDigest(expected.SubjectId!);
            string assetId = ChapterTextDigest(string.Join('\0', ownerDigest, source.WorkspaceId,
                source.ChapterId, source.ChapterDigest, textDigest));
            string chapterRequestId = OriginChapterSourceIdentity.RequestId(source);
            StoredGrant grant = await ReadContinuationGrantAsync(expected, ct);
            object body = consent is not null
                ? new { installationId = grant.InstallationId, chapterRequestId, textDigest,
                    sceneExcerpt = consent.Excerpt, altText = consent.AltText, externalProcessingConsent = true }
                : decision is not null
                    ? new { installationId = grant.InstallationId, chapterRequestId, textDigest,
                        expectedImageHash = decision.ImageHash, approve = decision.Approve, explicitlyConfirmed = true }
                    : new { installationId = grant.InstallationId, chapterRequestId, textDigest };
            string route = consent is not null ? "request" : decision is not null ? "decide" : "read";
            using var response = await _httpTransport.PostJsonAsync("/api/v2/android/linked/origin/scenes/" + route,
                body, CreateContinuationAuthority(expected, grant, () => proofReleased = true), ct);
            knownRejected = (int)response.StatusCode is >= 400 and < 500;
            RequireContinuationOwnerCurrent(expected);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(AndroidOriginSceneOutcome.Unauthorized);
            if (response.StatusCode == HttpStatusCode.Conflict) return new(AndroidOriginSceneOutcome.Conflict);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(AndroidOriginSceneOutcome.NotFound);
            if (!response.IsSuccessStatusCode)
                return new(AndroidOriginSceneOutcome.Unavailable, UnknownRemoteOutcome: mutation && !knownRejected);
            // Cap chunked and Content-Length responses before base64/JSON allocation.
            JsonElement json = await _httpTransport.ReadJsonAsync<JsonElement>(response, ct, 6 * 1024 * 1024);
            RequireContinuationOwnerCurrent(expected);
            RejectContinuationWireDuplicates(json);
            var wire = json.Deserialize<SceneWire>(ChapterWireJson) ?? throw new JsonException();
            if (wire.AssetId != assetId || wire.PublicationAuthorized
                || wire.State is not ("dispatching" or "uncertain" or "review" or "persisted" or "rejected" or "expired"))
                throw new JsonException();
            bool ready = wire.State is "review" or "persisted";
            if (decision is not null && wire.State != (decision.Approve ? "persisted" : "rejected")) throw new JsonException();
            if ((!ready || decision is not null) && (wire.Manifest is not null || wire.ImageBase64 is not null)) throw new JsonException();
            if (ready && decision is null)
            {
                var m = wire.Manifest ?? throw new JsonException();
                if (m.Schema != "chummer.media.origin-scene/v1" || m.AssetId != assetId || m.OwnerDigest != ownerDigest
                    || m.WorkspaceId != source.WorkspaceId || m.ChapterId != source.ChapterId
                    || m.ChapterDigest != source.ChapterDigest || m.TextDigest != textDigest || m.PublicationAuthorized
                    || !SceneText(m.AltText, 1024) || m.ContentType != "image/png"
                    || m.ContentLengthBytes is < 45 or > 4 * 1024 * 1024 || m.Width is < 1 or > 4096 || m.Height is < 1 or > 4096
                    || !ChapterHex(m.ContentHash) || !ChapterHex(m.ProviderReceiptDigest) || !ChapterHex(m.AdmissionDigest)
                    || m.Provider is not ("onemin" or "phygital")) throw new JsonException();
                if (!mutation)
                {
                    if (wire.ImageBase64 is null || wire.ImageBase64.Length > 4 * ((4 * 1024 * 1024 + 2) / 3)) throw new JsonException();
                    image = Convert.FromBase64String(wire.ImageBase64);
                    if (image.Length != m.ContentLengthBytes || Convert.ToHexStringLower(SHA256.HashData(image)) != m.ContentHash
                        || !image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                        || BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(8)) != 13
                        || !image.AsSpan(12, 4).SequenceEqual("IHDR"u8)
                        || BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(16)) != m.Width
                        || BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(20)) != m.Height
                        || !image.AsSpan(image.Length - 8, 4).SequenceEqual("IEND"u8)) throw new JsonException();
                    ct.ThrowIfCancellationRequested();
                    RequireContinuationOwnerCurrent(expected);
                    var result = new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Available, wire.State,
                        new(m.AltText, m.ContentHash, m.Provider, m.ProviderReceiptDigest, image));
                    image = null; // caller takes custody; coordinator clears after copying into its retained preview
                    return result;
                }
                if (wire.ImageBase64 is not null) throw new JsonException();
            }
            ct.ThrowIfCancellationRequested();
            RequireContinuationOwnerCurrent(expected);
            return new(AndroidOriginSceneOutcome.Available, wire.State);
        }
        catch (UnauthorizedAccessException)
        { return new(AndroidOriginSceneOutcome.Unauthorized, UnknownRemoteOutcome: mutation && proofReleased && !knownRejected); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException
            or JsonException or InvalidDataException or IOException or HttpRequestException or OperationCanceledException or CryptographicException or FormatException)
        {
            bool changed = expected is not null && !ReferenceEquals(OwnerAuthority.Capture(), expected);
            return new(changed ? AndroidOriginSceneOutcome.Unauthorized : AndroidOriginSceneOutcome.Unavailable,
                UnknownRemoteOutcome: mutation && proofReleased && !knownRejected);
        }
        finally { if (image is not null) CryptographicOperations.ZeroMemory(image); }
    }
}
