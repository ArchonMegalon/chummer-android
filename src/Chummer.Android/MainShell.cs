using Chummer.Android.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Android;

public sealed class MainShell : Shell
{
    public MainShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        BackgroundColor = NativeTheme.Paper;
        Shell.SetBackgroundColor(this, NativeTheme.Surface);
        Shell.SetForegroundColor(this, NativeTheme.Ink);
        Shell.SetTitleColor(this, NativeTheme.Ink);
        Shell.SetTabBarBackgroundColor(this, NativeTheme.Surface);
        Shell.SetTabBarForegroundColor(this, NativeTheme.Ink);
        Shell.SetTabBarUnselectedColor(this, NativeTheme.Muted);
        Shell.SetTabBarTitleColor(this, NativeTheme.Ink);

        TabBar tabs = new();
        tabs.Items.Add(CreateTab<HomePage>(services, "Home", "home", "⌂"));
        tabs.Items.Add(CreateTab<BuildPage>(services, "Build", "build", "✎"));
        tabs.Items.Add(CreateTab<PlayPage>(services, "Play", "play", "◆"));
        tabs.Items.Add(CreateTab<CampaignPage>(services, "Campaign", "campaign", "♟"));
        tabs.Items.Add(CreateTab<MorePage>(services, "More", "more", "•••"));
        Items.Add(tabs);
    }

    private static ShellContent CreateTab<TPage>(
        IServiceProvider services,
        string title,
        string route,
        string glyph)
        where TPage : Page
        => new()
        {
            Title = title,
            Route = route,
            Icon = new FontImageSource
            {
                Glyph = glyph,
                Size = 20,
                Color = NativeTheme.Ink
            },
            ContentTemplate = new DataTemplate(() => services.GetRequiredService<TPage>())
        };
}
