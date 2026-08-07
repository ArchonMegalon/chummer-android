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
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _backInvokedCallback = new BackInvokedCallback(HandleBackNavigation);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                IOnBackInvokedDispatcher.PriorityDefault,
                _backInvokedCallback);
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
