using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

public enum ShadowArchivePhoneSurface
{
    Catalog,
    Reader,
    Signal
}

public enum ShadowArchivePhoneSignalKind
{
    Hidden,
    SignInRequired,
    OwnerBlocked,
    Vote,
    Retract,
    Unavailable
}

public sealed record ShadowArchivePhoneSignalProjection(
    ShadowArchivePhoneSignalKind Kind,
    string? Label,
    string? Detail);

public sealed record ShadowArchivePhoneStateCopy(
    string Title,
    string Detail,
    bool CanRetry);

/// <summary>
/// Pure phone policy shared by the page and managed tests. Signal affordances are hidden until
/// the final chapter, and owner/auth checks are applied before Presentation's vote capability.
/// </summary>
public static class ShadowArchivePhonePolicy
{
    public static ShadowArchivePhoneSignalProjection ProjectSignal(
        int chapterIndex,
        int chapterCount,
        bool viewerIsOwner,
        ShadowArchiveSignalViewModel? signal)
    {
        if (chapterCount <= 0 || chapterIndex < 0 || chapterIndex != chapterCount - 1)
        {
            return new(ShadowArchivePhoneSignalKind.Hidden, null, null);
        }

        if (viewerIsOwner)
        {
            return new(
                ShadowArchivePhoneSignalKind.OwnerBlocked,
                null,
                "Story owners cannot Signal their own story.");
        }

        if (signal is null)
        {
            return new(
                ShadowArchivePhoneSignalKind.Unavailable,
                null,
                "Signal status is unavailable. Reading and downloads remain public.");
        }

        if (signal.RequiresSignIn)
        {
            return new(
                ShadowArchivePhoneSignalKind.SignInRequired,
                "Sign in to Signal",
                "An account is required only to vote.");
        }

        if (signal.ViewerHasVoted && signal.CanRetract)
        {
            return new(
                ShadowArchivePhoneSignalKind.Retract,
                "Retract Signal",
                "Remove your Signal from this exact story revision.");
        }

        if (!signal.ViewerHasVoted && signal.CanVote)
        {
            return new(
                ShadowArchivePhoneSignalKind.Vote,
                "Signal this story",
                "Cast one Signal for this exact story revision.");
        }

        return new(
            ShadowArchivePhoneSignalKind.Unavailable,
            null,
            string.IsNullOrWhiteSpace(signal.BlockedReason)
                ? "Signal voting is unavailable for this story."
                : signal.BlockedReason);
    }

    public static ShadowArchivePhoneStateCopy StateCopy(
        ShadowArchivePresentationState state,
        ShadowArchivePhoneSurface surface,
        ShadowArchiveErrorViewModel? error)
    {
        if (state == ShadowArchivePresentationState.AuthenticationRequired
            && surface == ShadowArchivePhoneSurface.Signal)
        {
            return new(
                "Sign in to Signal",
                "An account is required only to vote. Reading and downloads stay public.",
                false);
        }

        string? safeDetail = string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
        return state switch
        {
            ShadowArchivePresentationState.Offline => new(
                "You're offline",
                safeDetail ?? "Reconnect to load the public Archive. No cached response was assumed.",
                true),
            ShadowArchivePresentationState.Stale or ShadowArchivePresentationState.RevisionConflict => new(
                "Story changed",
                safeDetail ?? "Reload the current immutable story revision before continuing.",
                true),
            ShadowArchivePresentationState.ModerationHeld => new(
                "Story under review",
                safeDetail ?? "This story is not publicly available while moderation is in progress.",
                true),
            ShadowArchivePresentationState.Removed or ShadowArchivePresentationState.NotFound => new(
                "Story unavailable",
                safeDetail ?? "This public story revision could not be found.",
                false),
            ShadowArchivePresentationState.RateLimited => new(
                "Archive is busy",
                RetryDetail(safeDetail, error?.RetryAfter),
                true),
            ShadowArchivePresentationState.AuthenticationRequired when surface is not ShadowArchivePhoneSurface.Signal => new(
                "Public access unavailable",
                "The public Archive unexpectedly requested a login, so no story was loaded.",
                true),
            ShadowArchivePresentationState.Forbidden => new(
                surface == ShadowArchivePhoneSurface.Signal ? "Signal not allowed" : "Public access unavailable",
                safeDetail ?? "This action is not allowed for the current account.",
                false),
            _ => new(
                surface == ShadowArchivePhoneSurface.Signal ? "Signal unavailable" : "Archive unavailable",
                safeDetail ?? "No public story or Signal response was assumed.",
                true)
        };
    }

    private static string RetryDetail(string? safeDetail, TimeSpan? retryAfter)
    {
        string detail = safeDetail ?? "Too many requests reached the Archive. Try again later.";
        return retryAfter is { } delay && delay > TimeSpan.Zero
            ? $"{detail} Retry in about {Math.Ceiling(delay.TotalSeconds):0} seconds."
            : detail;
    }
}
