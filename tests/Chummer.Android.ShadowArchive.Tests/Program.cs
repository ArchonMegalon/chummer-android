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
        CatalogFiltersUseAuthoritativeLanguageAndEditionArchetypes();
        CatalogFiltersFailClosedOnCrossEditionMetadata();
        await MissingCompositionFailsClosedAsync();
        Console.WriteLine("Stories phone policy tests passed: 8");
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

    private static void CatalogFiltersUseAuthoritativeLanguageAndEditionArchetypes()
    {
        ShadowArchivePublicCatalogViewModel catalog = new(
            new[]
            {
                Story("pub-de-face", "de-at", "Deutsch (Österreich)", "face", "Face"),
                Story("pub-en-face", "en", "English", "face", "Face"),
                Story("pub-es-face", "es-mx", "Español (México)", "face", "Face"),
                Story("pub-de-street", "de-at", "Deutsch (Österreich)", "street-samurai", "Street Samurai")
            },
            new ShadowArchiveViewerContext(false, null, null));
        ShadowArchiveFilteredCatalogViewModel filtered = ShadowArchiveCatalogFilterPolicy.Project(
            catalog,
            new ShadowArchiveCatalogFilterSelection("de-at", "face"));
        Require(filtered.Stories.Count == 1);
        Require(filtered.Stories[0].Binding.PublicationId == "pub-de-face");
        Require(filtered.LanguageEditions.Count == 3);
        Require(filtered.Archetypes.Count == 2);
    }

    private static void CatalogFiltersFailClosedOnCrossEditionMetadata()
    {
        ShadowArchivePublicStoryCardViewModel invalid = Story(
            "pub-invalid",
            "de-at",
            "Deutsch (Österreich)",
            "face",
            "Face") with
        {
            Metadata = new ShadowArchiveCatalogMetadataViewModel(
                new("de-at", "de-AT", "Deutsch (Österreich)", "sr5", "catalog:locale:de-at", Digest('a')),
                new[] { new ShadowArchiveEditionArchetypeViewModel(
                    "face", "Face", "sr6", "catalog:archetype:face", Digest('b')) })
        };
        bool rejected = false;
        try
        {
            ShadowArchiveCatalogFilterPolicy.Project(
                new ShadowArchivePublicCatalogViewModel(
                    new[] { invalid },
                    new ShadowArchiveViewerContext(false, null, null)),
                new ShadowArchiveCatalogFilterSelection(null, null));
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Require(rejected);
    }

    private static ShadowArchivePublicStoryCardViewModel Story(
        string publicationId,
        string languageEditionId,
        string languageDisplay,
        string archetypeId,
        string archetypeDisplay)
        => new(
            "A runner story",
            "Summary",
            new ShadowArchiveStoryIdentityViewModel("Nightshade", null, "Tibor", null, "Chummer.run"),
            12,
            false,
            new ShadowArchiveCatalogMetadataViewModel(
                new(languageEditionId, languageEditionId, languageDisplay, "sr5", $"catalog:locale:{languageEditionId}", Digest('a')),
                new[] { new ShadowArchiveEditionArchetypeViewModel(
                    archetypeId,
                    archetypeDisplay,
                    "sr5",
                    $"catalog:archetype:{archetypeId}",
                    Digest('b')) }),
            new ShadowArchiveBindingViewModel(publicationId, 1, Digest('c')));

    private static string Digest(char value) => new(value, 64);

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
            throw new InvalidOperationException("Stories phone policy assertion failed.");
        }
    }
}
