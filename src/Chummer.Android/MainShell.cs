using Chummer.Android.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android;

public sealed class MainShell : Shell
{
    private readonly PhoneInitialRoutePolicy _initialPhoneRoute = new();
    private RunnerSessionCoordinator? _initialPhoneCoordinator;
    private int _initialPhoneRouteStarted;
    private int _initialPhoneRouteQueued;
    private bool _initialPhoneRouteUnloaded;

    public MainShell(IServiceProvider services)
    {
        BackgroundColor = NativeTheme.Paper;
        Shell.SetBackgroundColor(this, NativeTheme.Surface);
        Shell.SetForegroundColor(this, NativeTheme.Ink);
        Shell.SetTitleColor(this, NativeTheme.Ink);
        Shell.SetTabBarBackgroundColor(this, NativeTheme.Surface);
        Shell.SetTabBarForegroundColor(this, NativeTheme.Ink);
        Shell.SetTabBarUnselectedColor(this, NativeTheme.Muted);
        Shell.SetTabBarTitleColor(this, NativeTheme.Ink);

        DisplayInfo display = DeviceDisplay.Current.MainDisplayInfo;
        double widthDip = display.Density > 0 ? display.Width / display.Density : display.Width;
        UsesTabletComposition = TabletLayoutPolicy.UseTabletComposition(DeviceInfo.Current.Idiom, widthDip);
        if (UsesTabletComposition)
        {
            BuildTabletShell(services);
        }
        else
        {
            BuildPhoneShell(services);
        }
    }

    public bool UsesTabletComposition { get; }

    private void BuildPhoneShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        TabBar tabs = new();
        tabs.Items.Add(CreatePhoneTab<RunnersPage>(
            services,
            PhoneStrings.Get("ShellRunners", "Runners"),
            PhoneShellRoutes.Runners,
            "⌂"));
        tabs.Items.Add(CreatePhoneTab<BuildPage>(
            services,
            PhoneStrings.Get("ShellRunner", "Runner"),
            PhoneShellRoutes.Runner,
            "✎"));
        tabs.Items.Add(CreatePhoneTab<ShadowArchivePage>(
            services,
            PhoneStrings.Get("ShellStories", "Stories"),
            PhoneShellRoutes.Archive,
            "▤"));
        tabs.Items.Add(CreatePhoneTab<PhoneMorePage>(
            services,
            PhoneStrings.Get("ShellMore", "More"),
            PhoneShellRoutes.More,
            "•••"));
        Items.Add(tabs);

        RunnerSessionCoordinator coordinator = services.GetRequiredService<RunnerSessionCoordinator>();
        _initialPhoneCoordinator = coordinator;
        Loaded += async (_, _) =>
        {
            await ResolveInitialPhoneRouteAsync(coordinator);
            QueueInitialPhoneRoute();
        };
        Unloaded += (_, _) =>
        {
            _initialPhoneRouteUnloaded = true;
            RetireInitialPhoneRoute();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Loaded is not the sole lifecycle signal: both entries join the same
        // one-shot startup and neither repeats coordinator initialization.
        if (_initialPhoneCoordinator is { } coordinator)
        {
            _ = ResolveInitialPhoneRouteAsync(coordinator);
            QueueInitialPhoneRoute();
        }
    }

    private async Task ResolveInitialPhoneRouteAsync(RunnerSessionCoordinator coordinator)
    {
        if (_initialPhoneRoute.IsRetired
            || Interlocked.CompareExchange(ref _initialPhoneRouteStarted, 1, 0) != 0) return;
        coordinator.Changed += OnInitialPhoneReadinessChanged;
        Navigating += OnInitialPhoneNavigating;
        _initialPhoneRoute.Observe(coordinator.CaptureInitialPhoneRouteReadiness());
        try
        {
            await coordinator.InitializeAsync();
            QueueInitialPhoneRoute();
        }
        catch (Exception exception)
        {
            RetireInitialPhoneRoute();
            ReportInitialPhoneRouteFailure(exception);
        }
    }

    private void OnInitialPhoneReadinessChanged(object? sender, EventArgs args)
    {
        // Record an intervening owner epoch even if UI callbacks are coalesced.
        // Capture only observes local state; all navigation is queued below.
        if (_initialPhoneCoordinator is { } coordinator)
            _initialPhoneRoute.Observe(coordinator.CaptureInitialPhoneRouteReadiness());
        QueueInitialPhoneRoute();
    }

    private void QueueInitialPhoneRoute()
    {
        if (_initialPhoneRouteUnloaded) return;
        if (Interlocked.CompareExchange(ref _initialPhoneRouteQueued, 1, 0) != 0) return;
        // Always post, including on the UI thread. Changed can still own an
        // account gate, so BeginInvoke-on-main-thread's inline path is unsafe.
        if (!Dispatcher.Dispatch(async () =>
            {
                Interlocked.Exchange(ref _initialPhoneRouteQueued, 0);
                await ApplyInitialPhoneRouteAsync();
            }))
        {
            Interlocked.Exchange(ref _initialPhoneRouteQueued, 0);
            RetireInitialPhoneRoute();
            System.Diagnostics.Trace.TraceError("Initial phone route dispatch was unavailable.");
        }
    }

    private async Task ApplyInitialPhoneRouteAsync()
    {
        RunnerSessionCoordinator? coordinator = _initialPhoneCoordinator;
        if (coordinator is null || _initialPhoneRoute.IsRetired)
        {
            RetireInitialPhoneRoute();
            return;
        }
        // OnAppearing may precede handler attachment. Loaded/appearance queues
        // another observation; never spend the intent on a premature GoToAsync.
        if (Handler is null || Window?.Handler is null) return;
        PhoneInitialRouteReadiness readiness = coordinator.CaptureInitialPhoneRouteReadiness();
        if (!_initialPhoneRoute.TryResolve(readiness))
        {
            if (_initialPhoneRoute.IsRetired) RetireInitialPhoneRoute();
            return;
        }
        RetireInitialPhoneRoute();
        // Recheck the live owner, both owner-bound projections, and the exact
        // selected workspace immediately before starting this one navigation.
        if (coordinator.CaptureInitialPhoneRouteReadiness() != readiness) return;
        try
        {
            if (readiness.HasProfile && readiness.WorkspaceId is not null
                && coordinator.State.Profile is not null
                && coordinator.State.WorkspaceId is not null)
                await GoToAsync(PhoneShellRoutes.RunnerAbsolute);
            // Ready with no runner is terminal too. Runners is already the
            // default; do not issue a redundant navigation or wait for Hub.
        }
        catch (Exception exception) { ReportInitialPhoneRouteFailure(exception); }
    }

    private void OnInitialPhoneNavigating(object? sender, ShellNavigatingEventArgs args)
    {
        // Any actual departure wins, including leave-and-return before a queued
        // readiness callback. The automatic navigation unsubscribes first.
        _initialPhoneRoute.ObserveNavigation(args.Current?.Location?.OriginalString,
            args.Target?.Location?.OriginalString, CurrentPage is null or RunnersPage);
        if (_initialPhoneRoute.IsRetired) RetireInitialPhoneRoute();
    }

    private void RetireInitialPhoneRoute()
    {
        _initialPhoneRoute.Retire();
        if (_initialPhoneCoordinator is { } coordinator)
            coordinator.Changed -= OnInitialPhoneReadinessChanged;
        Navigating -= OnInitialPhoneNavigating;
    }

    private void ReportInitialPhoneRouteFailure(Exception exception)
    {
        System.Diagnostics.Trace.TraceError("Initial phone route failed ({0}).", exception.GetType().Name);
        if (_initialPhoneRouteUnloaded) return;
        Dispatcher.Dispatch(async () =>
        {
            if (_initialPhoneRouteUnloaded) return;
            try
            {
                await DisplayAlertAsync("Chummer",
                    PhoneStrings.Get("InitialRunnerRouteUnavailable", "The runner could not be opened automatically. Use Runners to open it."),
                    PhoneStrings.Get("OK", "OK"));
            }
            catch (Exception reportError)
            {
                System.Diagnostics.Trace.TraceError("Initial phone route notice failed ({0}).", reportError.GetType().Name);
            }
        });
    }

    private void BuildTabletShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutWidth = 248;
        FlyoutHeader = new VerticalStackLayout
        {
            Padding = new Thickness(22, 30, 22, 18),
            Children =
            {
                NativeTheme.Eyebrow("Chummer"),
                NativeTheme.Title("Your runners", 22)
            }
        };
        Items.Add(CreateTabletDestination<HomePage>(services, "Home", "tablet-home", "⌂"));
        Items.Add(CreateTabletDestination<TabletBuildPage>(services, "Build", "tablet-build", "✎"));
        Items.Add(CreateTabletDestination<PlayPage>(services, "Play", "tablet-play", "◆"));
        Items.Add(CreateTabletDestination<CampaignPage>(services, "Campaign", "tablet-campaign", "♟"));
        Items.Add(CreateTabletDestination<MorePage>(services, "More", "tablet-more", "•••"));
    }

    private static ShellContent CreatePhoneTab<TPage>(
        IServiceProvider services,
        string title,
        string route,
        string glyph)
        where TPage : Page
        => new()
        {
            Title = title,
            Route = route,
            AutomationId = $"phone-destination-{route}",
            Icon = new FontImageSource
            {
                Glyph = glyph,
                Size = 20,
                Color = NativeTheme.Ink
            },
            ContentTemplate = new DataTemplate(() => services.GetRequiredService<TPage>())
        };

    private static FlyoutItem CreateTabletDestination<TPage>(
        IServiceProvider services,
        string title,
        string route,
        string glyph)
        where TPage : Page
    {
        FlyoutItem destination = new()
        {
            Title = title,
            Route = route,
            AutomationId = $"tablet-destination-{route}",
            Icon = new FontImageSource
            {
                Glyph = glyph,
                Size = 22,
                Color = NativeTheme.Ink
            }
        };
        destination.Items.Add(new ShellContent
        {
            Title = title,
            ContentTemplate = new DataTemplate(() => services.GetRequiredService<TPage>())
        });
        return destination;
    }
}
