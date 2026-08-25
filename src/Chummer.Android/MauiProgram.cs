using Chummer.Android.Platform;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Tools;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Files;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.Logging;

namespace Chummer.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        string contentPath = AndroidBundledContentMaterializer.Materialize();
        Environment.SetEnvironmentVariable("CHUMMER_REQUIRE_CONTENT_BUNDLE", "true");
        string statePath = Path.Combine(FileSystem.AppDataDirectory, "state");
        Directory.CreateDirectory(statePath);
        Environment.SetEnvironmentVariable("CHUMMER_STATE_PATH", statePath);

        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        builder.Services.AddSingleton<IAndroidDocumentService, AndroidDocumentService>();
        builder.Services.AddSingleton<IAndroidImageDocumentService, AndroidImageDocumentService>();
        builder.Services.AddSingleton<IAndroidLinkedCharacterFileService, AndroidLinkedCharacterFileService>();
        builder.Services.AddSingleton<IAndroidSystemService, AndroidSystemService>();
        builder.Services.AddSingleton<IAndroidAccountLinkService, AndroidAccountLinkService>();
        builder.Services.AddSingleton<ICharacterRosterFavoriteStore>(
            new FileCharacterRosterFavoriteStore(statePath));
        builder.Services.AddSingleton<CharacterRosterFavoritePresenter>();
        builder.Services.AddSingleton<IApplicationDeleteConfirmationStore>(
            new FileApplicationDeleteConfirmationStore(
                statePath,
                typeof(MauiProgram).Assembly.GetName().Version ?? new Version(0, 0)));
        builder.Services.AddSingleton<ApplicationDeleteConfirmationPresenter>();
        builder.Services.AddChummerLocalRuntimeClient(
            contentPath,
            contentPath,
            "android");
        builder.Services.AddSingleton<ICareerQualityAtomicWorkspace,
            AndroidCareerQualityAtomicWorkspace>();
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri("https://chummer.run"),
            Timeout = TimeSpan.FromSeconds(20)
        });
        builder.Services.AddSingleton<IShellBootstrapDataProvider, ShellBootstrapDataProvider>();
        builder.Services.AddSingleton<IWorkspaceOverviewStateFactory>(provider =>
            new WorkspaceOverviewStateFactory(
                provider.GetRequiredService<ICharacterCreationFoundationService>()));
        builder.Services.AddSingleton<ICharacterCreationFoundationInteractionPresenter>(provider =>
            new CharacterCreationFoundationInteractionPresenter(
                provider.GetRequiredService<ICharacterCreationFoundationService>()));
        builder.Services.AddSingleton<IWorkspaceOperationCoordinator, WorkspaceOperationCoordinator>();
        builder.Services.AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>();
        builder.Services.AddSingleton<IShellPresenter, ShellPresenter>();
        builder.Services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
        builder.Services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();
        builder.Services.AddSingleton<RunnerSessionCoordinator>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<RunnersPage>();
        builder.Services.AddTransient<RosterFavoritesPage>();
        builder.Services.AddTransient<ApplicationSettingsPage>();
        builder.Services.AddTransient<BuildPage>();
        builder.Services.AddTransient<TabletBuildPage>();
        builder.Services.AddTransient<PlayPage>();
        builder.Services.AddTransient<CampaignPage>();
        builder.Services.AddTransient<MorePage>();
        builder.Services.AddTransient<PhoneMorePage>();
        builder.Services.AddSingleton<MainShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
