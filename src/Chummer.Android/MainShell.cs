using Chummer.Android.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android;

public sealed class MainShell : Shell
{
    private int _initialPhoneRouteResolved;

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
        tabs.Items.Add(CreatePhoneTab<RunnersPage>(services, "Runners", PhoneShellRoutes.Runners, "⌂"));
        tabs.Items.Add(CreatePhoneTab<BuildPage>(services, "Runner", PhoneShellRoutes.Runner, "✎"));
        tabs.Items.Add(CreatePhoneTab<PhoneMorePage>(services, "More", PhoneShellRoutes.More, "•••"));
        Items.Add(tabs);

        RunnerSessionCoordinator coordinator = services.GetRequiredService<RunnerSessionCoordinator>();
        Loaded += async (_, _) => await ResolveInitialPhoneRouteAsync(coordinator);
    }

    private async Task ResolveInitialPhoneRouteAsync(RunnerSessionCoordinator coordinator)
    {
        if (Interlocked.Exchange(ref _initialPhoneRouteResolved, 1) != 0)
        {
            return;
        }

        try
        {
            await coordinator.InitializeAsync();
            if (coordinator.State.Profile is not null
                && coordinator.State.WorkspaceId is not null)
            {
                await GoToAsync(PhoneShellRoutes.RunnerAbsolute);
            }
        }
        catch
        {
            // The Runners page remains the fail-closed default and reports initialization errors.
            Interlocked.Exchange(ref _initialPhoneRouteResolved, 0);
        }
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
