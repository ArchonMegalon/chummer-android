using Chummer.Application.Owners;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Platform;

public enum AndroidOriginSceneOutcome { Available, NotFound, Unauthorized, Conflict, Unavailable }

// Transport projection only. Hub owns consent/admission; Media owns rendering.
// No provider URL or credential is accepted by the phone.
public sealed record AndroidOriginSceneImage(string AltText, string ImageHash, string Provider,
    string ProviderReceiptDigest, byte[] Bytes, string? InsertionPolicy = null);
public sealed record AndroidOriginSceneResult(AndroidOriginSceneOutcome Outcome, string? State = null,
    AndroidOriginSceneImage? Image = null, bool UnknownRemoteOutcome = false, bool RetryableReadFailure = false);

public interface IAndroidOriginSceneTransport
{
    Task<AndroidOriginSceneResult> ReadSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, CancellationToken ct = default);
    Task<AndroidOriginSceneResult> RequestSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, string sceneExcerpt, string altText, bool externalProcessingConsent, CancellationToken ct = default);
    Task<AndroidOriginSceneResult> RequestAutomaticSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, bool externalProcessingConsent, CancellationToken ct = default)
        => Task.FromResult(new AndroidOriginSceneResult(AndroidOriginSceneOutcome.Unavailable));
    Task<AndroidOriginSceneResult> DecideSceneAsync(OwnerContextStamp owner, OriginChapterSource source,
        string acceptedText, string expectedImageHash, bool approve, bool explicitlyConfirmed, CancellationToken ct = default);
}
