using Android.App;
using Android.Content;
using Chummer.Android.Native;
using Google.Android.Play.Core.Review;
using Google.Android.Play.Core.Review.Testing;

namespace Chummer.Android.Native
{
    public sealed class RunnerSessionCoordinator;

    public static class PlayReviewSafety
    {
        public static PlayReviewSafetyContext Capture(RunnerSessionCoordinator coordinator)
            => new(
                IsExplicitSafeSurface: true,
                IsRootNavigation: true,
                HasModal: false,
                HasActiveDialog: false,
                HasUnsavedMutation: false,
                HasActionInFlight: false);
    }
}

namespace Chummer.Android
{
    public static class PlayReviewBindingProbe
    {
        public static IReviewManager CreateOfficialFakeManager(Context context)
            => new FakeReviewManager(context);
    }

    public sealed class MainActivity : Activity
    {
        public string? InstallerPackageName { get; set; }

        public bool IsPlayReviewDebugOverride { get; set; }

        public bool CanLaunchPlayReviewNow(bool explicitTestOverride) => true;
    }
}
