using Android.Content;
using Android.Gms.Extensions;
using Chummer.Android.Native;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Google.Android.Play.Core.Review;

namespace Chummer.Android;

public sealed class AndroidPlayReviewLauncher : IPlayReviewLauncher
{
    private const string PlayStorePackageName = "com.android.vending";

    public PlayReviewInstallContext InstallContext
    {
        get
        {
            MainActivity? activity = Platform.CurrentActivity as MainActivity;
#if DEBUG
            const bool isReleaseBuild = false;
#else
            const bool isReleaseBuild = true;
#endif
            return new PlayReviewInstallContext(
                activity?.PackageName ?? AppInfo.Current.PackageName ?? string.Empty,
                activity?.InstallerPackageName,
                AndroidPlayReviewInstallIdentity.GetOrCreate(),
                isReleaseBuild);
        }
    }

    public bool IsRuntimeAvailable
        => Platform.CurrentActivity is MainActivity activity
           && activity.CanLaunchPlayReviewNow(activity.IsPlayReviewDebugOverride);

    public async Task RequestReviewAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Platform.CurrentActivity is not MainActivity activity
                || !activity.CanLaunchPlayReviewNow(activity.IsPlayReviewDebugOverride))
            {
                return;
            }

            using IReviewManager manager = ReviewManagerFactory.Create(activity);
            ReviewInfo reviewInfo = await manager.RequestReviewFlow().AsAsync<ReviewInfo>();
            cancellationToken.ThrowIfCancellationRequested();
            RunnerSessionCoordinator? coordinator = IPlatformApplication.Current?.Services
                .GetService<RunnerSessionCoordinator>();
            if (!activity.CanLaunchPlayReviewNow(activity.IsPlayReviewDebugOverride)
                || coordinator is null
                || !PlayReviewSafety.Capture(coordinator).IsSafe)
            {
                return;
            }

            // Completion intentionally carries no success/review signal. Google Play does not
            // disclose whether the card was displayed or whether the user submitted a review.
            await manager.LaunchReviewFlow(activity, reviewInfo).AsAsync();
        });
    }

    public async Task OpenStoreListingAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Platform.CurrentActivity is not MainActivity activity)
            {
                return;
            }

            const string packageName = PlayReviewPolicy.CanonicalApplicationId;

            try
            {
                using Intent marketIntent = CreateViewIntent(
                    $"market://details?id={System.Uri.EscapeDataString(packageName)}");
                marketIntent.SetPackage(PlayStorePackageName);
                activity.StartActivity(marketIntent);
                return;
            }
            catch (ActivityNotFoundException)
            {
                // Browser fallback below.
            }
            catch (Exception)
            {
                // Browser fallback below.
            }

            try
            {
                using Intent webIntent = CreateViewIntent(
                    $"https://play.google.com/store/apps/details?id={System.Uri.EscapeDataString(packageName)}");
                activity.StartActivity(webIntent);
            }
            catch (Exception)
            {
                // Devices without Play Store or a browser continue without an error surface.
            }
        });
    }

    private static Intent CreateViewIntent(string uri)
    {
        Intent intent = new(Intent.ActionView, global::Android.Net.Uri.Parse(uri));
        intent.AddFlags(ActivityFlags.NoHistory | ActivityFlags.NewDocument | ActivityFlags.MultipleTask);
        return intent;
    }
}

internal static class AndroidPlayReviewInstallIdentity
{
    private static readonly object Sync = new();
    private static string? _cached;

    public static string GetOrCreate()
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(_cached))
            {
                return _cached;
            }

            try
            {
                Java.IO.File? noBackupDirectory = global::Android.App.Application.Context.NoBackupFilesDir;
                if (noBackupDirectory?.AbsolutePath is not string directory
                    || string.IsNullOrWhiteSpace(directory))
                {
                    return string.Empty;
                }

                string path = Path.Combine(directory, "play-review-install-id");
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (Guid.TryParseExact(existing, "N", out _))
                    {
                        _cached = existing;
                        return existing;
                    }
                }

                Directory.CreateDirectory(directory);
                string created = Guid.NewGuid().ToString("N");
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, created);
                File.Move(temporary, path, overwrite: true);
                _cached = created;
                return created;
            }
            catch (Exception)
            {
                // Missing no-backup storage makes automatic review ineligible.
                return string.Empty;
            }
        }
    }
}
