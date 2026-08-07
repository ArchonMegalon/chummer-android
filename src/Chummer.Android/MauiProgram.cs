using Chummer.Android.Platform;
using Chummer.Blazor;
using Chummer.Blazor.Services;
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

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<IAndroidDocumentService, AndroidDocumentService>();
        builder.Services.AddSingleton<AndroidDocumentInbox>();
        builder.Services.AddSingleton<IWorkbenchExternalDocumentInbox>(services => services.GetRequiredService<AndroidDocumentInbox>());
        builder.Services.AddSingleton<IAndroidSystemService, AndroidSystemService>();
        builder.Services.AddSingleton<AndroidJsBridge>();
        builder.Services.AddSingleton<AndroidAppState>();
        builder.Services.AddChummerLocalRuntimeClient(
            AppContext.BaseDirectory,
            FileSystem.AppDataDirectory,
            "android");
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri("https://chummer.run"),
            Timeout = TimeSpan.FromSeconds(20)
        });
        builder.Services.AddSingleton<IWorkbenchCoachApiClient, WorkbenchCoachApiClient>();
        builder.Services.AddSingleton<IWorkspacePrivacyLifecycleCapabilities>(
            HostedBuildPrivacyLifecycleCapabilities.Instance);
        builder.Services.AddSingleton<IShellBootstrapDataProvider, ShellBootstrapDataProvider>();
        builder.Services.AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>();
        builder.Services.AddSingleton<IShellPresenter, ShellPresenter>();
        builder.Services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
        builder.Services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
