using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Window;
using Android.Gms.Extensions;
using AndroidX.Core.View;
using Chummer.Android.Native;
using Chummer.Android.Platform;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
using Chummer.Android.Proof;
#endif
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
    DataPath = "/app/install-link",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "chummer")]
public sealed class MainActivity : MauiAppCompatActivity
{
    private const int InAppUpdateRequestCode = 9201;
    private const long ReviewHeartbeatMilliseconds = 30_000;
    private const string E2EAuthorityIntentExtra =
        "com.myexternalbrain.chummer.extra.E2E_AUTHORITY";
#if DEBUG
    private const string DebugPlayReviewIntentExtra =
        "com.myexternalbrain.chummer.extra.DEBUG_PLAY_REVIEW";
#endif

    private IOnBackInvokedCallback? _backInvokedCallback;
    private readonly NativePageActionGate _backNavigationGate = new();
    private IAppUpdateManager? _appUpdateManager;
    private InstallStateListener? _installStateListener;
    private Handler? _reviewHandler;
    private ReviewHeartbeatRunnable? _reviewHeartbeat;
    private bool _destroyed;
    private bool _resumed;
    private bool _reviewDebugOverride = false;
    private bool _googlePlayManaged;
    private string? _installerPackageName;
    private bool _updateCheckRunning;
    private bool _updateFlowRequested;
    private bool _updateFlowDeferred;
    private bool _updateCompletionDeferred;
    private bool _updateCompletionPromptVisible;

    public bool IsGooglePlayManaged => _googlePlayManaged;

    internal string? InstallerPackageName => _installerPackageName;

    internal bool IsPlayReviewDebugOverride => _reviewDebugOverride;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        AndroidE2EAuthority.ConfigureForCurrentProcess(
            ReadE2EAuthorityOptIn(Intent));
#endif
        base.OnCreate(savedInstanceState);
#if DEBUG
        ConfigurePlayReviewDebugOverride(Intent, resetWhenMissing: true);
#endif
        HandleAccountLinkIntent(Intent);
        _installerPackageName = ResolveInstallerPackageName();
        _googlePlayManaged = string.Equals(
            _installerPackageName,
            PlayReviewPolicy.GooglePlayInstallerPackage,
            StringComparison.Ordinal);
        if (_googlePlayManaged)
        {
            InitializeInAppUpdates();
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _backInvokedCallback = new BackInvokedCallback(HandleBackNavigation);
            // Shell owns the MAUI page stack. AndroidX can register a later
            // default-priority Fragment callback during page updates; letting
            // that empty native stack handle Back can finish this Activity.
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                IOnBackInvokedDispatcher.PriorityOverlay,
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
                ReadE2EAuthorityOptIn(intent));
            ConfigurePlayReviewDebugOverride(intent, resetWhenMissing: false);
#endif
            HandleAccountLinkIntent(intent);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _resumed = true;
        GetPlayReviewService()?.OnForegrounded();
        ScheduleReviewHeartbeat(_reviewDebugOverride ? 1_000 : ReviewHeartbeatMilliseconds);
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

    protected override void OnPause()
    {
        _resumed = false;
        CancelReviewHeartbeat();
        GetPlayReviewService()?.OnBackgrounded();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _destroyed = true;
        _resumed = false;
        Platform.DocumentIntentBroker.Cancel(this);
        CancelReviewHeartbeat();
        GetPlayReviewService()?.OnBackgrounded();
        _reviewHeartbeat?.Dispose();
        _reviewHeartbeat = null;
        _reviewHandler?.Dispose();
        _reviewHandler = null;
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

#if DEBUG
    private static bool ReadE2EAuthorityOptIn(Intent? intent)
    {
        // Deliberately require an Android Boolean extra (`am start --ez`). A
        // String lookalike (`--es ... true`) must fail closed rather than
        // enabling an instrumentation-only surface from untyped input.
        return intent?.GetBooleanExtra(E2EAuthorityIntentExtra, false) == true;
    }

    private void ConfigurePlayReviewDebugOverride(Intent? intent, bool resetWhenMissing)
    {
        if (!resetWhenMissing && intent?.HasExtra(DebugPlayReviewIntentExtra) != true)
        {
            return;
        }

        _reviewDebugOverride = intent?.GetBooleanExtra(DebugPlayReviewIntentExtra, false) == true;
        GetPlayReviewService()?.ConfigureDebugOverride(_reviewDebugOverride);
        if (_resumed)
        {
            ScheduleReviewHeartbeat(_reviewDebugOverride ? 1_000 : ReviewHeartbeatMilliseconds);
        }
    }
#endif

    internal bool CanLaunchPlayReviewNow(bool allowExplicitTestOverride = false)
        => (_googlePlayManaged || (allowExplicitTestOverride && _reviewDebugOverride))
           && _resumed
           && !_destroyed
           && !IsFinishing
           && !IsDestroyed
           && HasWindowFocus
           && !_updateCheckRunning
           && !_updateFlowRequested
           && !_updateCompletionPromptVisible;

    private IPlayReviewService? GetPlayReviewService()
        => IPlatformApplication.Current?.Services.GetService<IPlayReviewService>();

    private void ScheduleReviewHeartbeat(long delayMilliseconds)
    {
        if (!_resumed || _destroyed)
        {
            return;
        }

        _reviewHandler ??= new Handler(Looper.MainLooper!);
        _reviewHeartbeat ??= new ReviewHeartbeatRunnable(this);
        _reviewHandler.RemoveCallbacks(_reviewHeartbeat);
        _reviewHandler.PostDelayed(_reviewHeartbeat, delayMilliseconds);
    }

    private void CancelReviewHeartbeat()
    {
        if (_reviewHandler is not null && _reviewHeartbeat is not null)
        {
            _reviewHandler.RemoveCallbacks(_reviewHeartbeat);
        }
    }

    private async Task CheckpointAndMaybeRequestReviewAsync()
    {
        try
        {
            IPlayReviewService? review = GetPlayReviewService();
            if (review is null || !_resumed || _destroyed)
            {
                return;
            }

            review.CheckpointForegroundUse();
            RunnerSessionCoordinator? coordinator = IPlatformApplication.Current?.Services
                .GetService<RunnerSessionCoordinator>();
            if (coordinator is not null && CanLaunchPlayReviewNow(_reviewDebugOverride))
            {
                await review.TryRequestAtSafeMomentAsync(
                    PlayReviewSafety.Capture(coordinator));
            }
        }
        catch (Exception)
        {
            // Review policy and Play failures never interrupt Chummer.
        }
        finally
        {
            ScheduleReviewHeartbeat(ReviewHeartbeatMilliseconds);
        }
    }

    private bool HandleBackNavigation()
    {
        // Consume repeats even if the first pop has already changed the stack.
        // Queuing another pop can invalidate Shell's implicit page routes while
        // its first animated transition is still using them.
        if (_destroyed || _backNavigationGate.IsClaimed) return true;
        // Overlay priority also precedes the IME. Preserve the ordinary first
        // Back = dismiss keyboard behavior without popping the focused editor.
        var window = Window;
        var decor = window?.DecorView;
        if (decor is not null && ViewCompat.GetRootWindowInsets(decor)?.IsVisible(WindowInsetsCompat.Type.Ime()) == true)
        {
            WindowCompat.GetInsetsController(window!, decor)?.Hide(WindowInsetsCompat.Type.Ime());
            return true;
        }
        Microsoft.Maui.Controls.Shell? shell = Microsoft.Maui.Controls.Shell.Current;
        Microsoft.Maui.Controls.INavigation? navigation = shell?.Navigation;
        bool modal = navigation?.ModalStack.Count > 0;
        Microsoft.Maui.Controls.Page? expectedPage = modal
            ? navigation!.ModalStack.LastOrDefault()
            : navigation?.NavigationStack.Count > 1 ? navigation.NavigationStack.LastOrDefault() : null;
        if (shell is not null && navigation is not null && expectedPage is not null)
        {
            if (_backNavigationGate.TryClaim())
                _ = PopBackNavigationAsync(shell, navigation, expectedPage, modal);
            return true;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            MoveTaskToBack(nonRoot: true);
            return true;
        }

        return false;
    }

    private async Task PopBackNavigationAsync(
        Microsoft.Maui.Controls.Shell shell,
        Microsoft.Maui.Controls.INavigation navigation,
        Microsoft.Maui.Controls.Page expectedPage,
        bool modal)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_destroyed || !_resumed
                    || !ReferenceEquals(shell, Microsoft.Maui.Controls.Shell.Current)) return;
                var current = modal ? navigation.ModalStack.LastOrDefault()
                    : navigation.NavigationStack.LastOrDefault();
                // A tab switch, toolbar action or Activity replacement wins over
                // a stale callback. Never pop a different page or rebuild a draft.
                if (!ReferenceEquals(current, expectedPage)) return;
                if (modal) await navigation.PopModalAsync();
                else if (navigation.ModalStack.Count == 0) await navigation.PopAsync();
            });
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Back navigation failed ({0}).", exception.GetType().Name);
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (_destroyed || !_resumed
                        || !ReferenceEquals(shell, Microsoft.Maui.Controls.Shell.Current)) return;
                    await shell.DisplayAlertAsync("Chummer", PhoneStrings.Get("BackNavigationUnavailable",
                        "Could not go back. Please try again."), PhoneStrings.Get("OK", "OK"));
                });
            }
            catch (Exception reportError)
            {
                System.Diagnostics.Trace.TraceError("Back navigation notice failed ({0}).", reportError.GetType().Name);
            }
        }
        finally
        {
            _backNavigationGate.Release();
        }
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
        bool isDocumentResult = requestCode is Platform.DocumentIntentBroker.OpenRequestCode
            or Platform.DocumentIntentBroker.CreateRequestCode
            or Platform.DocumentIntentBroker.ImageOpenRequestCode;
        // Snapshot the granted URI before delegating to MAUI/AppCompat.  The Java
        // Intent belongs to this callback and must not be re-read after base
        // handlers have had an opportunity to consume or dispose its payload.
        global::Android.Net.Uri? documentUri = isDocumentResult && resultCode == Result.Ok
            ? data?.Data
            : null;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        if (isDocumentResult)
        {
            Api36ProofStatePublisher.TryRecordDocumentPickerCallback(
                requestCode,
                resultCode == Result.Ok,
                documentUri);
        }
#endif
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == InAppUpdateRequestCode)
        {
            _updateFlowRequested = false;
            _updateFlowDeferred = resultCode != Result.Ok;
            return;
        }

        if (isDocumentResult)
        {
            Platform.DocumentIntentBroker.Complete(this, requestCode, documentUri);
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

    private string? ResolveInstallerPackageName()
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

            return installer;
        }
        catch (Exception)
        {
            return null;
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

    private sealed class ReviewHeartbeatRunnable : Java.Lang.Object, Java.Lang.IRunnable
    {
        private readonly MainActivity _activity;

        public ReviewHeartbeatRunnable(MainActivity activity)
        {
            _activity = activity;
        }

        public void Run() => _ = _activity.CheckpointAndMaybeRequestReviewAsync();
    }
}
