using Chummer.Android.Platform;
using Chummer.Android.Native;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
using Chummer.Android.Proof;
#endif
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Tools;
using Chummer.Application.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Files;
using Chummer.Presentation.Overview;
using Chummer.Presentation.OriginBooks;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.Logging;

namespace Chummer.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        PhoneLocalePolicy.InitializeFromSystemCulture();
        string contentPath = AndroidBundledContentMaterializer.Materialize();
        Environment.SetEnvironmentVariable("CHUMMER_REQUIRE_CONTENT_BUNDLE", "true");
        string statePath = Path.Combine(FileSystem.AppDataDirectory, "state");
        Directory.CreateDirectory(statePath);
        Environment.SetEnvironmentVariable("CHUMMER_STATE_PATH", statePath);

        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        builder.Services.AddSingleton<IAndroidDocumentService, AndroidDocumentService>();
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        builder.Services.AddSingleton<Api36ProofStatePublisher>();
#endif
        builder.Services.AddSingleton<IAndroidImageDocumentService, AndroidImageDocumentService>();
        builder.Services.AddSingleton<IAndroidLinkedCharacterFileService, AndroidLinkedCharacterFileService>();
        builder.Services.AddSingleton<IAndroidSystemService, AndroidSystemService>();
        builder.Services.AddSingleton<IAndroidAccountLinkService, AndroidAccountLinkService>();
        // Hub transport and public-catalog composition are not present in this graph. Bind the
        // exact Presentation contract and keep the missing list capability separately fail-closed.
        builder.Services.AddSingleton<IShadowArchivePresentationClient,
            UnavailableShadowArchivePresentationClient>();
        builder.Services.AddSingleton<ShadowArchivePresenter>();
        builder.Services.AddSingleton<IShadowArchivePublicCatalogPort,
            UnavailableShadowArchivePublicCatalogPort>();
        builder.Services.AddSingleton<IPlayReviewClock, SystemPlayReviewClock>();
        builder.Services.AddSingleton<IPlayReviewStateStore>(
            new FilePlayReviewStateStore(statePath));
        builder.Services.AddSingleton<IPlayReviewLauncher, AndroidPlayReviewLauncher>();
        builder.Services.AddSingleton<IPlayReviewService>(provider =>
            new PlayReviewService(
                provider.GetRequiredService<IPlayReviewStateStore>(),
                provider.GetRequiredService<IPlayReviewClock>(),
                provider.GetRequiredService<IPlayReviewLauncher>(),
                $"{AppInfo.Current.VersionString}+{AppInfo.Current.BuildString}",
                AutomaticPlayReviewEnabled()));
        builder.Services.AddSingleton<ICharacterRosterFavoriteStore>(
            new FileCharacterRosterFavoriteStore(statePath));
        builder.Services.AddSingleton<CharacterRosterFavoritePresenter>();
        builder.Services.AddSingleton<IApplicationDeleteConfirmationStore>(
            new FileApplicationDeleteConfirmationStore(
                statePath,
                typeof(MauiProgram).Assembly.GetName().Version ?? new Version(0, 0)));
        builder.Services.AddSingleton<ApplicationDeleteConfirmationPresenter>();
        builder.Services.AddSingleton<IOriginDossierDraftTimelineStore>(
            new FileOriginDossierDraftTimelineStore(statePath));
        builder.Services.AddSingleton<ISr5AfterRunManualProposalBackend>(
            new FileSr5AfterRunManualProposalBackend(statePath));
        builder.Services.AddSingleton<IAndroidAfterRunWorkspaceSnapshotSource>(provider =>
            new AndroidAfterRunWorkspaceSnapshotSource(
                provider.GetRequiredService<IWorkspaceStore>()));
        builder.Services.AddSingleton<Sr5AfterRunManualProposalSource>(provider =>
            new Sr5AfterRunManualProposalSource(
                provider.GetRequiredService<IAndroidAfterRunWorkspaceSnapshotSource>(),
                provider.GetRequiredService<ISr5AfterRunManualProposalBackend>()));
        builder.Services.AddSingleton<
            ICharacterAfterRunSettlementProposalProjectionSource>(provider =>
                provider.GetRequiredService<Sr5AfterRunManualProposalSource>());
        builder.Services.AddSingleton<IAndroidAfterRunProposalCatalog>(provider =>
            provider.GetRequiredService<Sr5AfterRunManualProposalSource>());
        builder.Services.AddSingleton<ISr5AfterRunManualProposalAuthority>(provider =>
            provider.GetRequiredService<Sr5AfterRunManualProposalSource>());
        builder.Services.AddSingleton<IAndroidCareerSkillGroupSettingsCatalog,
            PreferencesAndroidCareerSkillGroupSettingsCatalog>();
        builder.Services.AddSingleton<ICharacterCareerSkillGroupAdvanceWorkspace,
            AndroidCharacterCareerSkillGroupAdvanceWorkspace>();
        builder.Services.AddChummerLocalRuntimeClient(
            contentPath,
            contentPath,
            "android");
        builder.Services.AddSingleton<ILifeModuleDecisionAuthority>(provider =>
            new CharacterCreationFoundationLifeModuleDecisionAuthority(
                provider.GetRequiredService<IWorkspaceStore>(),
                provider.GetRequiredService<ICharacterCreationFoundationService>(),
                provider.GetRequiredService<ICharacterFileQueries>()));
        builder.Services.AddSingleton<LifeModuleOriginDossierService>();
        builder.Services.AddSingleton<LifeModuleOriginDossierInteractionService>();
        builder.Services.AddSingleton<OriginDossierLifeModulePhoneRuntime>();
        builder.Services.AddSingleton<ICareerQualityAtomicWorkspace,
            AndroidCareerQualityAtomicWorkspace>();
        builder.Services.AddSingleton<ISr5CareerCyberwarePurchaseCheckpointStore,
            PreferencesSr5CareerCyberwarePurchaseCheckpointStore>();
        builder.Services.AddSingleton<ISr5CareerCyberwareWorkspaceStore,
            AndroidSr5CareerCyberwareWorkspaceStore>();
        builder.Services.AddSingleton<Sr5CareerCyberwarePurchaseService>();
        builder.Services.AddSingleton<ISr5CareerCustomDrugRecipeCheckpointStore,
            PreferencesSr5CareerCustomDrugRecipeCheckpointStore>();
        builder.Services.AddSingleton<ISr5CareerCustomDrugWorkspaceStore,
            AndroidSr5CareerCustomDrugWorkspaceStore>();
        builder.Services.AddSingleton<Sr5CareerCustomDrugRecipeService>();
        builder.Services.AddSingleton<ISr5CareerVehicleWorkshopCheckpointStore,
            PreferencesSr5CareerVehicleWorkshopCheckpointStore>();
        builder.Services.AddSingleton<ISr5CareerVehicleWorkshopWorkspaceStore,
            AndroidSr5CareerVehicleWorkshopWorkspaceStore>();
        builder.Services.AddSingleton<Sr5CareerVehicleWorkshopService>();
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri("https://chummer.run"),
            Timeout = TimeSpan.FromSeconds(20)
        });
        builder.Services.AddSingleton<IShellBootstrapDataProvider, ShellBootstrapDataProvider>();
        builder.Services.AddSingleton<IWorkspaceOverviewStateFactory>(provider =>
            new WorkspaceOverviewStateFactory(
                provider.GetRequiredService<ICharacterCreationFoundationService>(),
                provider.GetService<ICharacterCreationContactsService>(),
                provider.GetService<ICharacterCreationQualitiesService>(),
                provider.GetService<ICharacterCreationMagicResonanceService>()));
        builder.Services.AddSingleton<ICharacterCreationFoundationInteractionPresenter>(provider =>
            new CharacterCreationFoundationInteractionPresenter(
                provider.GetRequiredService<ICharacterCreationFoundationService>()));
        builder.Services.AddSingleton<ICharacterCreationContactsInteractionPresenter>(provider =>
            new CharacterCreationContactsInteractionPresenter(
                provider.GetRequiredService<ICharacterCreationContactsService>()));
        builder.Services.AddSingleton<ICharacterCreationLifestylesInteractionPresenter>(provider =>
            new CharacterCreationLifestylesInteractionPresenter(
                provider.GetRequiredService<ICharacterCreationLifestylesService>()));
        builder.Services.AddSingleton<ICharacterCreationResourcesInteractionPresenter>(provider =>
            new CharacterCreationResourcesInteractionPresenter(
                provider.GetRequiredService<ICharacterCreationResourcesService>()));
        builder.Services.AddSingleton<ICharacterCreationGearInteractionPresenter>(provider =>
            new CharacterCreationGearInteractionPresenter(
                provider.GetRequiredService<ICharacterCreationGearService>()));
        builder.Services.AddSingleton<IWorkspaceOperationCoordinator, WorkspaceOperationCoordinator>();
        builder.Services.AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>();
        builder.Services.AddSingleton<IShellPresenter, ShellPresenter>();
        builder.Services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
        builder.Services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();
        builder.Services.AddSingleton<RunnerSessionCoordinator>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<ShadowArchivePage>();
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

    private static bool AutomaticPlayReviewEnabled()
    {
#if CHUMMER_PLAY_REVIEW_ENABLED
        const bool buildEnabled = true;
#else
        const bool buildEnabled = false;
#endif
        return buildEnabled
               && !string.Equals(
                   Environment.GetEnvironmentVariable("CHUMMER_DISABLE_PLAY_REVIEW"),
                   "1",
                   StringComparison.Ordinal);
    }
}
