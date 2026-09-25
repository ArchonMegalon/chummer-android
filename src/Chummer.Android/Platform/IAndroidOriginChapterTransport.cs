using Chummer.Application.Owners;
using Chummer.Run.Contracts.Community;

namespace Chummer.Android.Platform;

public enum AndroidOriginChapterOutcome { Available, NotFound, Unauthorized, Conflict, Unavailable }
public sealed record AndroidOriginChapterResult(AndroidOriginChapterOutcome Outcome,
    OriginChapterAuthoringJob? Job = null, bool UnknownRemoteOutcome = false,
    bool RetryableReadFailure = false);

public interface IAndroidOriginChapterTransport
{
    Task<AndroidOriginChapterResult> RequestChapterAsync(OwnerContextStamp owner, OriginChapterSource approvedSource,
        bool externalProcessingConsent, CancellationToken ct = default, OriginChapterPredecessor? previous = null);
    Task<AndroidOriginChapterResult> ReadChapterAsync(OwnerContextStamp owner, OriginChapterSource originalSource,
        CancellationToken ct = default);
    Task<AndroidOriginChapterResult> AcceptChapterAsync(OwnerContextStamp owner, OriginChapterSource originalSource,
        string providerReceiptDigest, string draftText, bool explicitlyConfirmed, CancellationToken ct = default);
}
