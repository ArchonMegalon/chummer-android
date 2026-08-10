using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Window;
using Chummer.Android.Platform;
using Microsoft.Maui;

namespace Chummer.Android;

[Activity(
    Theme = "@style/Chummer.SplashTheme",
    MainLauncher = true,
    Exported = true,
    EnableOnBackInvokedCallback = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize
                           | ConfigChanges.Orientation
                           | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "chummer.run",
    DataPathPrefix = "/app",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "chummer")]
public sealed class MainActivity : MauiAppCompatActivity
{
    private IOnBackInvokedCallback? _backInvokedCallback;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleAccountLinkIntent(Intent);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _backInvokedCallback = new BackInvokedCallback(HandleBackNavigation);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                IOnBackInvokedDispatcher.PriorityDefault,
                _backInvokedCallback);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null)
        {
            HandleAccountLinkIntent(intent);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        IAndroidAccountLinkService? accountLink = IPlatformApplication.Current?.Services
            .GetService<IAndroidAccountLinkService>();
        if (accountLink is not null)
        {
            _ = accountLink.ResumePendingLinkAsync();
        }
    }

    protected override void OnDestroy()
    {
        if (_backInvokedCallback is not null && OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backInvokedCallback);
            _backInvokedCallback.Dispose();
            _backInvokedCallback = null;
        }

        base.OnDestroy();
    }

    public override void OnBackPressed()
    {
        if (HandleBackNavigation())
        {
            return;
        }

#pragma warning disable CS0612 // AppCompat's compatible fallback remains required below API 33.
        base.OnBackPressed();
#pragma warning restore CS0612
    }

    private bool HandleBackNavigation()
    {
        AndroidAppState? state = IPlatformApplication.Current?.Services.GetService<AndroidAppState>();
        if (state?.TryNavigateBack() == true)
        {
            return true;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            MoveTaskToBack(nonRoot: true);
            return true;
        }

        return false;
    }

    private static void HandleAccountLinkIntent(Intent? intent)
    {
        string? value = intent?.DataString;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "chummer.run", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.AbsolutePath, "/app/install-link", StringComparison.Ordinal))
        {
            return;
        }

        IAndroidAccountLinkService? accountLink = IPlatformApplication.Current?.Services
            .GetService<IAndroidAccountLinkService>();
        if (accountLink is not null)
        {
            _ = accountLink.ResumePendingLinkAsync(uri);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode is Platform.DocumentIntentBroker.OpenRequestCode or Platform.DocumentIntentBroker.CreateRequestCode)
        {
            Platform.DocumentIntentBroker.Complete(resultCode == Result.Ok ? data?.Data : null);
        }
    }

    private sealed class BackInvokedCallback : Java.Lang.Object, IOnBackInvokedCallback
    {
        private readonly Func<bool> _callback;

        public BackInvokedCallback(Func<bool> callback)
        {
            _callback = callback;
        }

        public void OnBackInvoked() => _callback();
    }
}
