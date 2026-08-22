using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Window;
using Android.Gms.Extensions;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Microsoft.Maui;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

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
    private const int InAppUpdateRequestCode = 9201;
    private const string E2EAuthorityIntentExtra =
        "com.myexternalbrain.chummer.extra.E2E_AUTHORITY";

    private IOnBackInvokedCallback? _backInvokedCallback;
    private IAppUpdateManager? _appUpdateManager;
    private InstallStateListener? _installStateListener;
    private bool _destroyed;
    private bool _googlePlayManaged;
    private bool _updateCheckRunning;
    private bool _updateFlowRequested;
    private bool _updateFlowDeferred;
    private bool _updateCompletionDeferred;
    private bool _updateCompletionPromptVisible;

    public bool IsGooglePlayManaged => _googlePlayManaged;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        AndroidE2EAuthority.ConfigureForCurrentProcess(
            Intent?.GetBooleanExtra(E2EAuthorityIntentExtra, false) == true);
#endif
        base.OnCreate(savedInstanceState);
        HandleAccountLinkIntent(Intent);
        _googlePlayManaged = IsInstalledByGooglePlay();
        if (_googlePlayManaged)
        {
            InitializeInAppUpdates();
        }
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
#if DEBUG
            AndroidE2EAuthority.ConfigureForCurrentProcess(
                intent.GetBooleanExtra(E2EAuthorityIntentExtra, false));
#endif
            HandleAccountLinkIntent(intent);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_googlePlayManaged)
        {
            _ = CheckForPlayUpdateAsync(userInitiated: false);
        }
        IAndroidAccountLinkService? accountLink = IPlatformApplication.Current?.Services
            .GetService<IAndroidAccountLinkService>();
        if (accountLink is not null)
        {
            _ = accountLink.ResumePendingLinkAsync();
        }
    }

    protected override void OnDestroy()
    {
        _destroyed = true;
        if (_appUpdateManager is not null && _installStateListener is not null)
        {
            try
            {
                _appUpdateManager.UnregisterListener(_installStateListener);
            }
            catch (Exception)
            {
                // Google Play may already have released the listener during shutdown.
            }
        }
        _installStateListener?.Dispose();
        _installStateListener = null;
        _appUpdateManager?.Dispose();
        _appUpdateManager = null;

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
        Microsoft.Maui.Controls.INavigation? navigation = Microsoft.Maui.Controls.Shell.Current?.Navigation;
        if (navigation?.ModalStack.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(async () => await navigation.PopModalAsync());
            return true;
        }

        if (navigation?.NavigationStack.Count > 1)
        {
            MainThread.BeginInvokeOnMainThread(async () => await navigation.PopAsync());
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
        if (requestCode == InAppUpdateRequestCode)
        {
            _updateFlowRequested = false;
            _updateFlowDeferred = resultCode != Result.Ok;
            return;
        }

        if (requestCode is Platform.DocumentIntentBroker.OpenRequestCode
            or Platform.DocumentIntentBroker.CreateRequestCode
            or Platform.DocumentIntentBroker.ImageOpenRequestCode)
        {
            Platform.DocumentIntentBroker.Complete(resultCode == Result.Ok ? data?.Data : null);
        }
    }

    public async Task<AndroidUpdateCheckResult> CheckForPlayUpdateAsync(bool userInitiated)
    {
        if (!_googlePlayManaged || _appUpdateManager is null)
        {
            return AndroidUpdateCheckResult.Unavailable;
        }

        if (_destroyed || _updateCheckRunning)
        {
            return AndroidUpdateCheckResult.Checking;
        }

        if (_updateFlowRequested)
        {
            return AndroidUpdateCheckResult.Started;
        }

        if (userInitiated)
        {
            _updateFlowDeferred = false;
            _updateCompletionDeferred = false;
        }

        _updateCheckRunning = true;
        try
        {
            AppUpdateInfo info = await _appUpdateManager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>();
            if (_destroyed)
            {
                return AndroidUpdateCheckResult.Unavailable;
            }

            if (AndroidInAppUpdatePolicy.ShouldOfferCompletion(info.InstallStatus()))
            {
                ShowUpdateCompletionPrompt();
                return AndroidUpdateCheckResult.ReadyToInstall;
            }

            using AppUpdateOptions options = AppUpdateOptions
                .NewBuilder(AppUpdateType.Flexible)
                .SetAllowAssetPackDeletion(false)
                .Build();
            if (!AndroidInAppUpdatePolicy.ShouldStartFlexibleUpdate(
                    info.UpdateAvailability(),
                    info.IsUpdateTypeAllowed(options)))
            {
                return AndroidUpdateCheckResult.Current;
            }

            if (_updateFlowDeferred || _updateFlowRequested)
            {
                return AndroidUpdateCheckResult.Current;
            }

            _updateFlowRequested = true;
            bool started = _appUpdateManager.StartUpdateFlowForResult(
                info,
                this,
                options,
                InAppUpdateRequestCode);
            if (!started)
            {
                _updateFlowRequested = false;
                return AndroidUpdateCheckResult.Unavailable;
            }

            return AndroidUpdateCheckResult.Started;
        }
        catch (Exception)
        {
            return AndroidUpdateCheckResult.Unavailable;
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void InitializeInAppUpdates()
    {
        _appUpdateManager = AppUpdateManagerFactory.Create(this);
        _installStateListener = new InstallStateListener(OnInstallStateChanged);
        _appUpdateManager.RegisterListener(_installStateListener);
        _ = CheckForPlayUpdateAsync(userInitiated: false);
    }

    private void OnInstallStateChanged(InstallState? state)
    {
        if (state is not null && AndroidInAppUpdatePolicy.ShouldOfferCompletion(state.InstallStatus()))
        {
            RunOnUiThread(ShowUpdateCompletionPrompt);
        }
    }

    private void ShowUpdateCompletionPrompt()
    {
        if (_appUpdateManager is null
            || _destroyed
            || IsFinishing
            || IsDestroyed
            || _updateCompletionDeferred
            || _updateCompletionPromptVisible)
        {
            return;
        }

        _updateCompletionPromptVisible = true;
        AndroidX.AppCompat.App.AlertDialog dialog = new AndroidX.AppCompat.App.AlertDialog.Builder(this)
            .SetTitle("Update ready")!
            .SetMessage("Chummer has downloaded the update. Restart now to finish.")!
            .SetPositiveButton("Restart", (_, _) =>
            {
                _updateCompletionPromptVisible = false;
                _ = _appUpdateManager?.CompleteUpdate();
            })!
            .SetNegativeButton("Later", (_, _) =>
            {
                _updateCompletionDeferred = true;
                _updateCompletionPromptVisible = false;
            })!
            .Create();
        dialog.DismissEvent += (_, _) => _updateCompletionPromptVisible = false;
        dialog.Show();
    }

    private bool IsInstalledByGooglePlay()
    {
        try
        {
            string? installer;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                installer = PackageManager?.GetInstallSourceInfo(PackageName!)?.InstallingPackageName;
            }
            else
            {
#pragma warning disable CS0618 // Required on Android versions below API 30.
                installer = PackageManager?.GetInstallerPackageName(PackageName!);
#pragma warning restore CS0618
            }

            return string.Equals(installer, "com.android.vending", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
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

    private sealed class InstallStateListener : Java.Lang.Object, IInstallStateUpdatedListener
    {
        private readonly Action<InstallState?> _callback;

        public InstallStateListener(Action<InstallState?> callback)
        {
            _callback = callback;
        }

        public void OnStateUpdate(InstallState? state) => _callback(state);
    }
}
