using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

/// <summary>
/// Phone catalog projection. The binding and story identity come from the reviewed Presentation
/// contracts; the Android host adds only the list-card signal count and the already-resolved
/// viewer/owner relationship needed to prevent a self-vote.
/// </summary>
public sealed record ShadowArchivePublicStoryCardViewModel(
    string Title,
    string Summary,
    ShadowArchiveStoryIdentityViewModel Identity,
    int SignalCount,
    bool ViewerIsOwner,
    ShadowArchiveBindingViewModel Binding);

public sealed record ShadowArchivePublicCatalogViewModel(
    IReadOnlyList<ShadowArchivePublicStoryCardViewModel> Stories,
    ShadowArchiveViewerContext Viewer);

/// <summary>
/// Narrow Android host seam for the one missing Presentation capability: public story discovery.
/// It is not a second reader/community/signal client. Those flows use
/// <see cref="ShadowArchivePresenter"/> and <see cref="IShadowArchivePresentationClient"/> exactly.
/// Presentation 6ee4a7f5f has no public-list query contract yet.
/// </summary>
public interface IShadowArchivePublicCatalogPort
{
    Task<ShadowArchivePresentationResult<ShadowArchivePublicCatalogViewModel>> LoadPublicStoriesAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Honest default until the Hub catalog and viewer composition are supplied. It never fabricates
/// a story, count, reader payload, login, vote, leaderboard, reward, or provider response.
/// </summary>
public sealed class UnavailableShadowArchivePublicCatalogPort : IShadowArchivePublicCatalogPort
{
    public Task<ShadowArchivePresentationResult<ShadowArchivePublicCatalogViewModel>> LoadPublicStoriesAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(Unavailable<ShadowArchivePublicCatalogViewModel>());

    private static ShadowArchivePresentationResult<T> Unavailable<T>()
        => new(ShadowArchivePresentationState.Unavailable, default, Error());

    private static ShadowArchiveErrorViewModel Error()
        => new(
            "shadow_archive_android_composition_unavailable",
            "Shadow Archive is unavailable in this build. No public story or Signal response was assumed.",
            null,
            null,
            null,
            null,
            null);
}

/// <summary>
/// Exact Presentation transport contract, failed closed until Android receives a reviewed Hub
/// adapter. Returning Unavailable is transport truth; no story, vote, or community payload is
/// synthesized.
/// </summary>
public sealed class UnavailableShadowArchivePresentationClient : IShadowArchivePresentationClient
{
    public Task<ShadowArchiveClientResult<ShadowArchivePublicationPreviewContract>> GetPublicationPreviewAsync(
        ShadowArchivePublicationPreviewQuery query,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchivePublicationPreviewContract>());

    public Task<ShadowArchiveClientResult<ShadowArchivePublicReaderContract>> GetPublicReaderAsync(
        ShadowArchivePublicReaderQuery query,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchivePublicReaderContract>());

    public Task<ShadowArchiveClientResult<ShadowArchiveCommunityStatusContract>> GetCommunityStatusAsync(
        ShadowArchiveCommunityQuery query,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchiveCommunityStatusContract>());

    public Task<ShadowArchiveClientResult<ShadowArchiveCommunityStatusContract>> MutateSignalAsync(
        ShadowArchiveSignalMutation mutation,
        CancellationToken ct)
        => Task.FromResult(Unavailable<ShadowArchiveCommunityStatusContract>());

    private static ShadowArchiveClientResult<T> Unavailable<T>()
        => new(
            ShadowArchiveClientResultKind.Unavailable,
            ErrorCode: "shadow_archive_android_transport_unavailable",
            SafeMessage: "Shadow Archive is unavailable in this build. No public response or Signal change was assumed.");
}
