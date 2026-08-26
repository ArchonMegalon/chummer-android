using Chummer.Android.Native;
using Chummer.Presentation.OriginBooks;

internal static class Program
{
    private static async Task Main()
    {
        SignalIsHiddenBeforeTheFinalChapter();
        FinalChapterRequiresOnlyVoteAuthentication();
        OwnerCanNeverVoteForOwnStory();
        VoteAndRetractFollowPresentationCapability();
        PublicSurfaceStatesStayHonest();
        await MissingCompositionFailsClosedAsync();
        Console.WriteLine("Shadow Archive phone policy tests passed: 6");
    }

    private static void SignalIsHiddenBeforeTheFinalChapter()
    {
        ShadowArchivePhoneSignalProjection projected = ShadowArchivePhonePolicy.ProjectSignal(
            chapterIndex: 0,
            chapterCount: 2,
            viewerIsOwner: false,
            Signal(canVote: true));
        Require(projected.Kind == ShadowArchivePhoneSignalKind.Hidden);
    }

    private static void FinalChapterRequiresOnlyVoteAuthentication()
    {
        ShadowArchivePhoneSignalProjection projected = ShadowArchivePhonePolicy.ProjectSignal(
            chapterIndex: 1,
            chapterCount: 2,
            viewerIsOwner: false,
            Signal(requiresSignIn: true));
        Require(projected.Kind == ShadowArchivePhoneSignalKind.SignInRequired);
        Require(projected.Detail == "An account is required only to vote.");
    }

    private static void OwnerCanNeverVoteForOwnStory()
    {
        ShadowArchivePhoneSignalProjection projected = ShadowArchivePhonePolicy.ProjectSignal(
            chapterIndex: 1,
            chapterCount: 2,
            viewerIsOwner: true,
            Signal(canVote: true));
        Require(projected.Kind == ShadowArchivePhoneSignalKind.OwnerBlocked);
        Require(projected.Label is null);
    }

    private static void VoteAndRetractFollowPresentationCapability()
    {
        ShadowArchivePhoneSignalProjection vote = ShadowArchivePhonePolicy.ProjectSignal(
            0,
            1,
            false,
            Signal(canVote: true));
        ShadowArchivePhoneSignalProjection retract = ShadowArchivePhonePolicy.ProjectSignal(
            0,
            1,
            false,
            Signal(viewerHasVoted: true, canRetract: true));
        Require(vote.Kind == ShadowArchivePhoneSignalKind.Vote);
        Require(retract.Kind == ShadowArchivePhoneSignalKind.Retract);
    }

    private static void PublicSurfaceStatesStayHonest()
    {
        ShadowArchivePhoneStateCopy auth = ShadowArchivePhonePolicy.StateCopy(
            ShadowArchivePresentationState.AuthenticationRequired,
            ShadowArchivePhoneSurface.Reader,
            null);
        ShadowArchivePhoneStateCopy moderation = ShadowArchivePhonePolicy.StateCopy(
            ShadowArchivePresentationState.ModerationHeld,
            ShadowArchivePhoneSurface.Reader,
            null);
        ShadowArchivePhoneStateCopy limited = ShadowArchivePhonePolicy.StateCopy(
            ShadowArchivePresentationState.RateLimited,
            ShadowArchivePhoneSurface.Catalog,
            new ShadowArchiveErrorViewModel(
                "rate_limited",
                "Try later.",
                null,
                null,
                null,
                null,
                TimeSpan.FromSeconds(9)));
        Require(auth.Title == "Public access unavailable");
        Require(moderation.Title == "Story under review");
        Require(limited.Detail.Contains("9 seconds", StringComparison.Ordinal));
    }

    private static async Task MissingCompositionFailsClosedAsync()
    {
        var port = new UnavailableShadowArchivePublicCatalogPort();
        ShadowArchivePresentationResult<ShadowArchivePublicCatalogViewModel> result =
            await port.LoadPublicStoriesAsync(CancellationToken.None);
        Require(result.State == ShadowArchivePresentationState.Unavailable);
        Require(result.Value is null);
        Require(result.Error?.Code == "shadow_archive_android_composition_unavailable");
    }

    private static ShadowArchiveSignalViewModel Signal(
        bool viewerHasVoted = false,
        bool canVote = false,
        bool canRetract = false,
        bool requiresSignIn = false)
        => new(
            VoteCount: 12,
            ViewerHasVoted: viewerHasVoted,
            CanVote: canVote,
            CanRetract: canRetract,
            RequiresSignIn: requiresSignIn,
            BlockedReason: null);

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Shadow Archive phone policy assertion failed.");
        }
    }
}
