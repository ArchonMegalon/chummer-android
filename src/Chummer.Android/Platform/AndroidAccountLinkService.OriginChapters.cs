using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Owners;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Platform;

public sealed partial class AndroidAccountLinkService : IAndroidOriginChapterTransport
{
    private static readonly JsonSerializerOptions ChapterWireJson = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16, PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
    };

    public Task<AndroidOriginChapterResult> RequestChapterAsync(OwnerContextStamp owner,
        OriginChapterSource approvedSource, bool externalProcessingConsent, CancellationToken ct = default)
        => externalProcessingConsent ? ChapterRequestAsync(owner, approvedSource, create: true, ct)
            : Task.FromResult(new AndroidOriginChapterResult(AndroidOriginChapterOutcome.Unavailable));

    public Task<AndroidOriginChapterResult> ReadChapterAsync(OwnerContextStamp owner,
        OriginChapterSource originalSource, CancellationToken ct = default)
        => ChapterRequestAsync(owner, originalSource, create: false, ct);

    private sealed record ReaderAcceptance(string ProviderReceiptDigest, string TextDigest);
    private static string ChapterTextDigest(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool ChapterHex(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public Task<AndroidOriginChapterResult> AcceptChapterAsync(OwnerContextStamp owner, OriginChapterSource originalSource,
        string providerReceiptDigest, string draftText, bool explicitlyConfirmed, CancellationToken ct = default)
        => explicitlyConfirmed && ChapterHex(providerReceiptDigest) && !string.IsNullOrWhiteSpace(draftText)
            && Encoding.UTF8.GetByteCount(draftText) <= 64 * 1024
            ? ChapterRequestAsync(owner, originalSource, create: false, ct,
                new(providerReceiptDigest, ChapterTextDigest(draftText)))
            : Task.FromResult(new AndroidOriginChapterResult(AndroidOriginChapterOutcome.Unavailable));

    private async Task<AndroidOriginChapterResult> ChapterRequestAsync(OwnerContextStamp owner,
        OriginChapterSource source, bool create, CancellationToken ct, ReaderAcceptance? acceptance = null)
    {
        bool proofReleased = false, knownRejected = false;
        bool changesRemote = create || acceptance is not null;
        AndroidAccountOwnerState? expected = null;
        try
        {
            expected = RequireContinuationOwner(owner);
            // Snapshot before awaiting credentials, including the caller's fact
            // collection. The same source always addresses the same private job.
            var captured = OriginChapterSourceIdentity.Capture(source);
            string digest = OriginChapterSourceIdentity.Digest(captured);
            string requestId = OriginChapterSourceIdentity.RequestId(captured);
            StoredGrant grant = await ReadContinuationGrantAsync(expected, ct);
            object payload = create
                ? new { installationId = grant.InstallationId,
                    authoring = new OriginChapterAuthoringRequest(requestId, captured, true) }
                : acceptance is not null
                    ? new { installationId = grant.InstallationId, requestId, sourceDigest = digest,
                        providerReceiptDigest = acceptance.ProviderReceiptDigest, textDigest = acceptance.TextDigest,
                        explicitlyConfirmed = true }
                    : new { installationId = grant.InstallationId, requestId };
            var authority = CreateContinuationAuthority(expected, grant, () => proofReleased = true);
            using var response = await _httpTransport.PostJsonAsync(
                "/api/v2/android/linked/origin/chapters/" + (create ? "request" : acceptance is not null ? "accept" : "read"), payload, authority, ct);
            knownRejected = (int)response.StatusCode is >= 400 and < 500;
            RequireContinuationOwnerCurrent(expected);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(AndroidOriginChapterOutcome.Unauthorized);
            if (response.StatusCode == HttpStatusCode.Conflict) return new(AndroidOriginChapterOutcome.Conflict);
            if (!create && response.StatusCode == HttpStatusCode.NotFound) return new(AndroidOriginChapterOutcome.NotFound);
            if (!response.IsSuccessStatusCode)
                return new(AndroidOriginChapterOutcome.Unavailable, UnknownRemoteOutcome: changesRemote && !knownRejected);
            JsonElement body = await _httpTransport.ReadJsonAsync<JsonElement>(response, ct, 512 * 1024);
            RequireContinuationOwnerCurrent(expected);
            RejectContinuationWireDuplicates(body);
            // Read the wire flags explicitly: read-only DTO getters must not hide
            // a server response which attempted to grant mechanics/publication.
            if (!body.GetProperty("requiresReaderReview").GetBoolean()
                || body.GetProperty("affectsMechanics").GetBoolean()
                || body.GetProperty("publicationAuthorized").GetBoolean()) throw new JsonException();
            var job = body.Deserialize<OriginChapterAuthoringJob>(ChapterWireJson) ?? throw new JsonException();
            if (job.RequestId != requestId || job.SourceDigest != digest
                || OriginChapterSourceIdentity.Digest(job.Source) != digest || job.Provider != "first_book_ai"
                || job.State is not (OriginChapterAuthoringStates.AwaitingAuthoring
                    or OriginChapterAuthoringStates.ReconciliationRequired or OriginChapterAuthoringStates.ReviewRequired))
                throw new JsonException();
            if (job.State == OriginChapterAuthoringStates.ReviewRequired)
            {
                if (string.IsNullOrWhiteSpace(job.DraftText) || Encoding.UTF8.GetByteCount(job.DraftText) > 64 * 1024
                    || job.ProviderReceiptDigest is not { Length: 64 }
                    || !job.ProviderReceiptDigest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                    throw new JsonException();
            }
            else if (job.DraftText is not null || job.ProviderReceiptDigest is not null) throw new JsonException();
            if (job.ReaderAcceptedTextDigest is not null && (job.DraftText is null
                || job.ReaderAcceptedTextDigest != ChapterTextDigest(job.DraftText))) throw new JsonException();
            if (acceptance is not null && (job.ReaderAcceptedTextDigest != acceptance.TextDigest
                || job.ProviderReceiptDigest != acceptance.ProviderReceiptDigest)) throw new JsonException();
            RequireContinuationOwnerCurrent(expected);
            return new(AndroidOriginChapterOutcome.Available, job with { Source = captured });
        }
        catch (UnauthorizedAccessException)
        { return new(AndroidOriginChapterOutcome.Unauthorized, UnknownRemoteOutcome: changesRemote && proofReleased && !knownRejected); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException
            or JsonException or InvalidDataException or IOException or HttpRequestException or OperationCanceledException or CryptographicException)
        {
            // No automatic retry, token rotation, draft adoption, or provider
            // action. Read the same stable job after an uncertain write outcome.
            return new(expected is not null && !ReferenceEquals(OwnerAuthority.Capture(), expected)
                    ? AndroidOriginChapterOutcome.Unauthorized : AndroidOriginChapterOutcome.Unavailable,
                UnknownRemoteOutcome: changesRemote && proofReleased && !knownRejected);
        }
    }
}
