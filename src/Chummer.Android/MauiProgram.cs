using Chummer.Android.Platform;
using Chummer.Android.Native;
using Chummer.Desktop.Runtime;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.Logging;

namespace Chummer.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        string statePath = Path.Combine(FileSystem.AppDataDirectory, "state");
        Directory.CreateDirectory(statePath);
        Environment.SetEnvironmentVariable("CHUMMER_STATE_PATH", statePath);

        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        builder.Services.AddSingleton<IAndroidDocumentService, AndroidDocumentService>();
        builder.Services.AddSingleton<IAndroidSystemService, AndroidSystemService>();
        builder.Services.AddSingleton<IAndroidAccountLinkService, AndroidAccountLinkService>();
        builder.Services.AddChummerLocalRuntimeClient(
            AppContext.BaseDirectory,
            FileSystem.AppDataDirectory,
            "android");
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri("https://chummer.run"),
            Timeout = TimeSpan.FromSeconds(20)
        });
        builder.Services.AddSingleton<IShellBootstrapDataProvider, ShellBootstrapDataProvider>();
        builder.Services.AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>();
        builder.Services.AddSingleton<IShellPresenter, ShellPresenter>();
        builder.Services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
        builder.Services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();
        builder.Services.AddSingleton<RunnerSessionCoordinator>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<BuildPage>();
        builder.Services.AddTransient<PlayPage>();
        builder.Services.AddTransient<CampaignPage>();
        builder.Services.AddTransient<MorePage>();
        builder.Services.AddSingleton<MainShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
