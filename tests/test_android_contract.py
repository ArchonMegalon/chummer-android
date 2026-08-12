import json
import os
import re
import struct
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
WORKSPACE = Path(os.environ.get("CHUMMER_COMPLETE_ROOT", REPO.parent)).resolve()
RUN_SERVICES = Path(
    os.environ.get("CHUMMER_RUN_SERVICES_ROOT", WORKSPACE / "chummer.run-services")
).resolve()
PROJECT = REPO / "src" / "Chummer.Android"
REGISTRY = WORKSPACE / "chummer-design" / "products" / "chummer" / "ANDROID_WINDOWS_FEATURE_PARITY.yaml"
WINDOWS_COMMANDS = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Shell" / "DesktopMenuProjectionCatalog.cs"
WINDOWS_STARTUP_SURFACES = (
    WORKSPACE / "chummer-presentation" / "Chummer.Desktop.Runtime" / "DesktopStartupSurfaceCatalog.cs"
)


class AndroidContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.registry = json.loads(REGISTRY.read_text(encoding="utf-8"))

    def test_every_windows_menu_command_has_android_behavior(self) -> None:
        source = WINDOWS_COMMANDS.read_text(encoding="utf-8")
        catalog = source[source.index("VisibleMenuCommandsByMenuId"):source.index("public static IReadOnlyList<string> GetVisibleMenuIds")]
        command_ids = set(re.findall(r'"([a-z][a-z0-9_]*)"', catalog))
        command_ids.difference_update({"file", "edit", "special", "tools", "windows", "help"})
        command_ids.add("runtime_inspector")
        self.assertEqual(command_ids, set(self.registry["commands"]))

    def test_startup_parity_contract_is_complete(self) -> None:
        source = WINDOWS_STARTUP_SURFACES.read_text(encoding="utf-8")
        expected = set(re.findall(r'public const string [A-Za-z]+ = "([a-z][a-z0-9_]*)";', source))
        self.assertEqual(expected, set(self.registry["startup_surfaces"]))
        declared_source = WORKSPACE / self.registry["source"]["startup_surfaces"]
        self.assertEqual(WINDOWS_STARTUP_SURFACES, declared_source)
        self.assertTrue(declared_source.is_file())

    def test_manifest_is_privacy_minimal(self) -> None:
        manifest = (PROJECT / "Platforms" / "Android" / "AndroidManifest.xml").read_text(encoding="utf-8")
        self.assertIn("android.permission.INTERNET", manifest)
        self.assertNotIn("MANAGE_EXTERNAL_STORAGE", manifest)
        self.assertNotIn("READ_EXTERNAL_STORAGE", manifest)
        self.assertNotIn("WRITE_EXTERNAL_STORAGE", manifest)
        app = (PROJECT / "Platforms" / "Android" / "MainApplication.cs").read_text(encoding="utf-8")
        self.assertIn("AllowBackup = false", app)
        self.assertIn("UsesCleartextTraffic = false", app)

    def test_play_identity_and_release_target_are_stable(self) -> None:
        project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        self.assertIn("<ApplicationId>com.myexternalbrain.chummer</ApplicationId>", project)
        self.assertIn("<TargetSdkVersion>36</TargetSdkVersion>", project)
        self.assertIn("<AndroidMinSdkVersion>24</AndroidMinSdkVersion>", project)
        self.assertIn("<ApplicationDisplayVersion>0.1.0-preview.3</ApplicationDisplayVersion>", project)
        self.assertIn("<ApplicationVersion>3</ApplicationVersion>", project)
        self.assertIn("<AndroidPackageFormats Condition=\"'$(Configuration)' == 'Release'\">aab</AndroidPackageFormats>", project)
        self.assertIn('<ChummerDesktopRuntimeIdentifiers Condition="\'$(ChummerDesktopRuntimeIdentifiers)\' == \'\'">android-arm64;android-x64</ChummerDesktopRuntimeIdentifiers>', project)
        self.assertIn("<EmbedAssembliesIntoApk Condition=\"'$(Configuration)' == 'Debug'\">true</EmbedAssembliesIntoApk>", project)

    def test_android_uses_play_updates_and_verified_links(self) -> None:
        project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        system_service = (PROJECT / "Platforms" / "Android" / "AndroidSystemService.cs").read_text(encoding="utf-8")
        activity = (PROJECT / "Platforms" / "Android" / "MainActivity.cs").read_text(encoding="utf-8")
        policy = (PROJECT / "Platforms" / "Android" / "AndroidInAppUpdatePolicy.cs").read_text(encoding="utf-8")
        more = (PROJECT / "Native" / "MorePage.cs").read_text(encoding="utf-8")
        privacy = (PROJECT / "Native" / "AccountPrivacyPage.cs").read_text(encoding="utf-8")
        self.assertIn('Xamarin.Google.Android.Play.App.Update" Version="2.1.0.19"', project)
        self.assertIn("AppUpdateManagerFactory.Create(this)", activity)
        self.assertIn("IsInstalledByGooglePlay", activity)
        self.assertIn('"com.android.vending"', activity)
        self.assertIn("StartUpdateFlowForResult", activity)
        self.assertIn("AppUpdateType.Flexible", activity)
        self.assertIn("RegisterListener", activity)
        self.assertIn("CompleteUpdate", activity)
        self.assertIn("CheckForPlayUpdateAsync(userInitiated: false)", activity)
        self.assertIn("UpdateAvailability.UpdateAvailable", policy)
        self.assertIn("InstallStatus.Downloaded", policy)
        self.assertIn("CheckForUpdatesAsync", system_service)
        self.assertIn("Coordinator.CheckForUpdatesAsync()", more)
        self.assertNotIn("market://details", system_service)
        self.assertNotIn("play.google.com/store/apps/details", system_service)
        self.assertNotIn("OpenStoreListingAsync", system_service)
        self.assertIn("IsGooglePlayManaged", activity)
        self.assertIn("PlayManagedRequired", system_service)
        check_method = system_service[system_service.index("public async Task<AndroidUpdateCheckResult> CheckForUpdatesAsync()"):
                                      system_service.index("public Task ShareTextAsync")]
        self.assertNotIn("Launcher.Default", check_method)
        self.assertIn('"Updates come through Google Play"', more)
        self.assertIn('button.Text = "Checking"', more)
        self.assertNotIn("await RunAsync(async () =>", more[more.index("private async Task CheckUpdatesAsync"):])
        self.assertNotIn("DesktopUpdateRuntime", "".join(p.read_text(encoding="utf-8") for p in PROJECT.rglob("*.cs")))
        self.assertIn('DataHost = "chummer.run"', activity)
        self.assertIn('DataPathPrefix = "/app"', activity)
        self.assertIn("AutoVerify = true", activity)

    def test_android_account_link_uses_device_proof_and_encrypted_storage(self) -> None:
        service = (PROJECT / "Platform" / "AndroidAccountLinkService.cs").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        activity = (PROJECT / "Platforms" / "Android" / "MainActivity.cs").read_text(encoding="utf-8")
        more = (PROJECT / "Native" / "MorePage.cs").read_text(encoding="utf-8")
        privacy = (PROJECT / "Native" / "AccountPrivacyPage.cs").read_text(encoding="utf-8")
        home = (PROJECT / "Native" / "HomePage.cs").read_text(encoding="utf-8")
        self.assertIn("SecureStorage.Default", service)
        self.assertIn("ExportPkcs8PrivateKey", service)
        self.assertIn("RSASignaturePadding.Pkcs1", service)
        self.assertIn("chummer.install-link.remote-callback.v1", service)
        self.assertIn("/api/v1/install-linking/callbacks/poll", service)
        self.assertIn("/api/v1/install-linking/grants/status", service)
        self.assertIn("/api/v1/install-linking/grants/revoke", service)
        self.assertIn("response.StatusCode == HttpStatusCode.Conflict", service)
        self.assertIn("ClearAllCredentials();", service)
        self.assertNotIn("Preferences.Default", service)
        self.assertIn("AddSingleton<IAndroidAccountLinkService, AndroidAccountLinkService>", program)
        self.assertIn("OnNewIntent", activity)
        self.assertIn('"/app/install-link"', activity)
        self.assertIn("ResumePendingLinkAsync(uri)", activity)
        self.assertIn("Coordinator.BeginAccountLinkAsync()", home)
        self.assertIn('"Unlink this device?"', privacy)
        self.assertIn('"Account & privacy"', more + privacy)

    def test_account_deletion_is_native_confirmed_and_server_first(self) -> None:
        service = (PROJECT / "Platform" / "AndroidAccountLinkService.cs").read_text(encoding="utf-8")
        contract = (PROJECT / "Platform" / "IAndroidAccountLinkService.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        privacy = (PROJECT / "Native" / "AccountPrivacyPage.cs").read_text(encoding="utf-8")
        hub_contract = (
            RUN_SERVICES / "Chummer.Run.Contracts" / "AccountErasureContracts.cs"
        ).read_text(encoding="utf-8")
        required_phrase = "ERASE MY CHUMMER ACCOUNT"

        self.assertIn(required_phrase, contract)
        self.assertIn(required_phrase, hub_contract)
        self.assertIn('"/api/v1/android/linked/account/erase"', service)
        server_call = coordinator.index("await _account.EraseAccountAsync")
        local_delete = coordinator.index("await _presenter.DeleteWorkspaceAsync")
        credentials_clear = service.index("ClearAllCredentials();", service.index("EraseAccountAsync"))
        response_validation = service.index("if (!receipt.Erased", service.index("EraseAccountAsync"))
        self.assertLess(server_call, local_delete)
        self.assertLess(response_validation, credentials_clear)
        self.assertIn("AccountDeletionPage", privacy)
        self.assertIn("Remove runners saved on this device", privacy)
        self.assertIn("This cannot be undone.", privacy)
        self.assertIn("ChummerWebRoutes.AccountDeletion", coordinator)
        self.assertIn("Preferences.Default.Remove(SelectedGroupPreferenceKey)", coordinator)
        data_safety = (REPO / "play" / "data-safety.md").read_text(encoding="utf-8")
        release = (REPO / "docs" / "PLAY_RELEASE.md").read_text(encoding="utf-8")
        self.assertIn("More → Account & privacy → Delete account", data_safety + release)
        self.assertIn("https://chummer.run/account/delete", data_safety + release)
        self.assertIn("newer than the frozen version-code-3 AAB", release)

    def test_android_navigation_is_compact_and_app_owned(self) -> None:
        shell = (PROJECT / "MainShell.cs").read_text(encoding="utf-8")
        project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "NativePageBase.cs").read_text(encoding="utf-8")
        self.assertEqual(5, shell.count("tabs.Items.Add(CreateTab<"))
        for label in ("Home", "Build", "Play", "Campaign", "More"):
            self.assertIn(f'"{label}"', shell)
        self.assertIn("TabBar", shell)
        self.assertIn("Shell.SetTabBar", shell)
        self.assertIn("ContentPage", page)
        self.assertNotIn("Microsoft.NET.Sdk.Razor", project)
        self.assertNotIn("Components.WebView.Maui", project)
        self.assertNotIn("Chummer.Blazor", project)

    def test_build_uses_native_drill_down_navigation_without_horizontal_pwa_tabs(self) -> None:
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        commands = (PROJECT / "Native" / "NativeCommandPage.cs").read_text(encoding="utf-8")
        theme = (PROJECT / "Native" / "NativeTheme.cs").read_text(encoding="utf-8")

        self.assertIn("BuildSectionPage", build + flow)
        self.assertIn("BuildValueGroupPage", flow)
        self.assertIn("BuildNavigation.GroupKey", flow)
        self.assertIn("NativeCommandGroupPage", commands)
        self.assertIn("NativeTheme.NavigationRow", build + flow + commands)
        self.assertIn("SemanticProperties.SetDescription", theme)
        self.assertNotIn("ScrollOrientation.Horizontal", build)
        self.assertNotIn("AddTabs", build)
        self.assertNotIn("Show all", build)

    def test_android_copy_is_short_and_human(self) -> None:
        account = (PROJECT / "Platform" / "AndroidAccountLinkService.cs").read_text(encoding="utf-8")
        rendered_copy = account + "".join(
            path.read_text(encoding="utf-8") for path in (PROJECT / "Native").glob("*.cs")
        )
        for phrase in (
            "Runner desk",
            "Campaign command",
            "GM command",
            "Organizer desk",
            "Finish the handoff",
            "protected device identity",
            "incomplete grant",
            "new runner workflow",
        ):
            self.assertNotIn(phrase, rendered_copy)
        self.assertNotIn("workbench", rendered_copy.lower())
        self.assertIn("Your runners", rendered_copy)
        self.assertIn("Online runners", rendered_copy)
        self.assertIn("All actions", rendered_copy)

    def test_android_replaces_shared_layout_radios_with_a_compact_combobox(self) -> None:
        dialog = (PROJECT / "Native" / "NativeDialogPage.cs").read_text(encoding="utf-8")
        play = (PROJECT / "Native" / "PlayPage.cs").read_text(encoding="utf-8")
        campaign = (PROJECT / "Native" / "CampaignPage.cs").read_text(encoding="utf-8")
        self.assertIn("field.Options is { Count: > 0 }", dialog)
        self.assertIn("Picker picker", dialog)
        self.assertIn("Picker groups", play)
        self.assertIn("Picker picker", campaign)
        self.assertNotIn("RadioButton", dialog + play + campaign)

    def test_android_handoffs_use_canonical_public_routes(self) -> None:
        routes = (PROJECT / "Platform" / "ChummerWebRoutes.cs").read_text(encoding="utf-8")
        campaign = (PROJECT / "Native" / "CampaignPage.cs").read_text(encoding="utf-8")
        public_controller = (
            WORKSPACE / "chummer.run-services" / "Chummer.Run.Api" / "Controllers" / "PublicLandingController.cs"
        ).read_text(encoding="utf-8")
        self.assertIn('AccountAccess = "/account/access"', routes)
        self.assertNotIn("/account/devices", routes + campaign)
        for route in ("/gm", "/organizers", "/play", "/account/delete"):
            self.assertIn(route, public_controller)
        self.assertIn("CreateGroupInviteAsync", campaign)
        self.assertIn("Clipboard.Default.SetTextAsync", campaign)

    def test_play_and_groups_stay_inside_the_android_app(self) -> None:
        play = (PROJECT / "Native" / "PlayPage.cs").read_text(encoding="utf-8")
        campaign = (PROJECT / "Native" / "CampaignPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        service = (PROJECT / "Platform" / "AndroidAccountLinkService.cs").read_text(encoding="utf-8")
        project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        activity = (PROJECT / "Platforms" / "Android" / "MainActivity.cs").read_text(encoding="utf-8")

        self.assertIn("RollDice", play)
        self.assertIn("SetDamage", play)
        self.assertIn("SetPlayNotes", play)
        self.assertIn("SelectedGroup", play + coordinator)
        self.assertIn("CreateGroupAsync", campaign)
        self.assertIn("UpdateGroupAsync", campaign)
        self.assertIn("CreateGroupInviteAsync", campaign)
        self.assertIn('"/api/v1/android/linked/groups"', service)
        self.assertNotIn("WebView", play + campaign + coordinator + project)
        self.assertNotIn("IAndroidPlayHostService", activity)

    def test_chronicle_studio_is_native_and_preserves_approval_boundaries(self) -> None:
        campaign = (PROJECT / "Native" / "CampaignPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        service = (PROJECT / "Platform" / "AndroidAccountLinkService.cs").read_text(encoding="utf-8")
        controller = (
            WORKSPACE
            / "chummer.run-services"
            / "Chummer.Run.Api"
            / "Controllers"
            / "AndroidLinkedCampaignController.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("ChronicleEditorPage", campaign)
        self.assertIn("Picker(BookLabels", campaign)
        self.assertIn('"Approve source"', campaign)
        self.assertIn('"Approve source upload"', campaign)
        self.assertIn('"Approve generation"', campaign)
        self.assertIn('"Approve reviewed outline"', campaign)
        self.assertIn('"Add finished export"', campaign)
        self.assertIn('"Approve publication"', campaign)
        self.assertIn('"Approve external sharing"', campaign)
        self.assertIn('"Finished books shared with this group."', campaign)
        self.assertIn("else if (!string.IsNullOrWhiteSpace(selected.ExportFormat))", campaign)
        self.assertIn("SpoilerReviewConfirmed", campaign + service + controller)
        self.assertIn("UploadApprovedAtUtc", service + controller)
        self.assertNotIn('"approve_handoff"', campaign + controller)
        self.assertIn("SaveChroniclePacketAsync", coordinator)
        self.assertIn("AdvanceChronicleAsync", coordinator)
        self.assertIn("/chronicles/create", service)
        self.assertIn("/chronicles/{chronicleProjectId}/actions", controller)
        self.assertIn("CryptographicOperations.ZeroMemory(packet)", controller)
        self.assertNotIn("WebView", campaign + coordinator + service)

    def test_runner_output_is_routed_to_native_android(self) -> None:
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        system_service = (PROJECT / "Platforms" / "Android" / "AndroidSystemService.cs").read_text(encoding="utf-8")
        self.assertIn("PendingDownload", coordinator)
        self.assertIn("PendingExport", coordinator)
        self.assertIn("PendingPrint", coordinator)
        self.assertIn("SaveAsAsync", coordinator)
        self.assertIn("PrintPdfAsync", coordinator)
        self.assertIn("PdfFilePrintDocumentAdapter", system_service)
        self.assertIn("PrintManager", system_service)
        self.assertNotIn("PrintCurrentViewAsync", system_service)

    def test_web_shell_is_not_built_into_the_android_app(self) -> None:
        project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        self.assertIn('<Content Remove="wwwroot/**" />', project)
        self.assertNotIn("BlazorWebView", project + program)
        self.assertNotIn("AddMauiBlazorWebView", program)
        self.assertNotIn("IWorkbenchCoachApiClient", program)

    def test_android_document_picker_imports_into_shared_presenter(self) -> None:
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        service = (PROJECT / "Platforms" / "Android" / "AndroidDocumentService.cs").read_text(encoding="utf-8")
        self.assertIn("_documents.OpenAsync", coordinator)
        self.assertIn("_presenter.ImportAsync", coordinator)
        self.assertIn("WorkspaceImportDocument.FromUtf8Bytes", coordinator)
        self.assertIn("OpenInputStream", service)
        self.assertIn("MaxDocumentBytes", service)
        self.assertIn("CryptographicOperations.ZeroMemory", service)

    def test_android_shell_has_direct_new_runner_and_durable_feedback(self) -> None:
        home = (PROJECT / "Native" / "HomePage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("Coordinator.CreateRunnerAsync()", home)
        self.assertIn('ExecuteCommandAsync("new_character"', coordinator)
        self.assertIn('Preferences.Default.Set(SelectedGroupPreferenceKey', coordinator)
        self.assertIn('Opened {document.DisplayName}', coordinator)

    def test_android_shell_accessibility_and_output_copy_are_polished(self) -> None:
        theme = (PROJECT / "Native" / "NativeTheme.cs").read_text(encoding="utf-8")
        more = (PROJECT / "Native" / "MorePage.cs").read_text(encoding="utf-8")
        dialog = (PROJECT / "Native" / "NativeDialogPage.cs").read_text(encoding="utf-8")
        self.assertIn("HeightRequest = 50", theme)
        self.assertIn("LineBreakMode.WordWrap", theme)
        self.assertIn('NativeTheme.SecondaryButton("Print")', more)
        self.assertIn('Text = "Close"', dialog)
        self.assertNotIn("Print current view", more + dialog)

    def test_android_uses_native_maui_pages_over_shared_presenters(self) -> None:
        android_project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn('Project Sdk="Microsoft.NET.Sdk"', android_project)
        self.assertNotIn("Chummer.Blazor.Mobile.csproj", android_project)
        self.assertIn("AddSingleton<ICharacterOverviewPresenter", program)
        self.assertIn("AddSingleton<RunnerSessionCoordinator>", program)
        self.assertIn("ICharacterOverviewPresenter presenter", coordinator)
        self.assertIn("IShellSurfaceResolver surfaceResolver", coordinator)

    def test_linked_http_client_is_registered_after_local_runtime(self) -> None:
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        runtime_registration = program.index("AddChummerLocalRuntimeClient")
        http_registration = program.index("AddSingleton(new HttpClient")
        self.assertLess(runtime_registration, http_registration)
        self.assertIn("AddSingleton<IAndroidAccountLinkService", program)

    def test_no_signing_secret_or_broad_provider_file_is_tracked(self) -> None:
        forbidden_suffixes = {".jks", ".keystore", ".p12"}
        for path in REPO.rglob("*"):
            if path.is_file():
                self.assertNotIn(path.suffix.lower(), forbidden_suffixes)
                self.assertNotEqual(path.name, "google-services.json")

    def test_release_automation_is_fail_closed(self) -> None:
        build = (REPO / "scripts" / "build-release.sh").read_text(encoding="utf-8")
        bootstrap = (REPO / "scripts" / "bootstrap-build-environment.sh").read_text(encoding="utf-8")
        provision = (REPO / "scripts" / "provision-upload-key.sh").read_text(encoding="utf-8")
        recovery = (REPO / "scripts" / "import-signing-recovery.py").read_text(encoding="utf-8")
        validate = (REPO / "scripts" / "validate-aab.sh").read_text(encoding="utf-8")
        inspect = (REPO / "scripts" / "inspect_aab.py").read_text(encoding="utf-8")
        version_reader = (REPO / "scripts" / "read_android_version.py").read_text(encoding="utf-8")
        self.assertIn("set -euo pipefail", build)
        self.assertIn("AndroidSigningKeyStore", build)
        self.assertNotIn("ChummerAndroidSigningStorePass", build)
        self.assertIn("CHUMMER_ANDROID_SIGNING_DIR", provision)
        self.assertIn("must be stored outside the Chummer Android repository", provision)
        self.assertIn("Refusing to replace existing signing material", provision)
        self.assertIn("-storepass:env CHUMMER_PROVISION_STORE_PASSWORD", provision)
        self.assertIn("AndroidSigningKeyStore=", provision)
        self.assertIn("CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH=", provision)
        self.assertNotIn("echo ${chummer_store_password}", provision)
        self.assertIn("signing material must be stored outside", recovery)
        self.assertIn("base64.b64decode", recovery)
        self.assertIn("validate=True", recovery)
        self.assertIn("certificate_sha256_fingerprint", recovery)
        self.assertIn("CHUMMER_RECOVERY_STORE_PASSWORD", recovery)
        self.assertNotIn("shell=True", recovery)
        self.assertIn("Signed releases require CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH", build)
        self.assertIn("read_android_version.py", build)
        self.assertNotIn('version_name="0.1.0-preview.', build)
        self.assertIn('output_sha256="$(sha256sum "$output_aab"', build)
        self.assertIn('artifacts/%s\\n', build)
        self.assertIn("CHUMMER_JARSIGNER", build)
        self.assertIn("CHUMMER_BUNDLETOOL_JAR", validate)
        self.assertIn("bundletool validation passed", validate)
        self.assertIn("-verify -certs", validate)
        self.assertIn("AAB signer does not match", validate)
        self.assertIn("ALLOWED_PERMISSIONS", inspect)
        self.assertIn("read_project_version(PROJECT_PATH)", inspect)
        self.assertNotIn('versionName") == "0.1.0-preview.', inspect)
        self.assertIn('native_abis == {"arm64-v8a"}', inspect)
        self.assertIn("ApplicationDisplayVersion", version_reader)
        self.assertIn("ApplicationVersion", version_reader)
        self.assertIn("set -euo pipefail", bootstrap)
        self.assertIn('approval_token="install-android-sdk36-jdk-and-accept-licenses"', bootstrap)
        self.assertIn("CHUMMER_ANDROID_TOOLCHAIN_APPROVAL", bootstrap)
        self.assertIn("CHUMMER_ANDROID_TOOLCHAIN_DIR must be an explicit absolute path", bootstrap)
        self.assertIn("Resolved Android toolchain directory must remain outside", bootstrap)
        self.assertIn("CHUMMER_ANDROID_TOOLCHAIN_REPLACE_ENV=replace", bootstrap)
        self.assertIn("Refusing symlinked Android SDK or Java SDK directory", bootstrap)
        self.assertIn("-t:InstallAndroidDependencies", bootstrap)
        self.assertIn("-p:AcceptAndroidSDKLicenses=True", bootstrap)
        self.assertIn('runtime_identifier="android-arm64"', bootstrap)
        self.assertGreaterEqual(bootstrap.count('-p:ChummerDesktopRuntimeIdentifiers="$runtime_identifier"'), 2)
        self.assertIn('--runtime "$runtime_identifier"', bootstrap)
        self.assertIn('compile_check_path=', bootstrap)
        self.assertIn("-p:AndroidSdkDirectory", bootstrap)
        self.assertIn("-p:JavaSdkDirectory", bootstrap)
        self.assertIn('chmod 0600 "$environment_temp"', bootstrap)
        self.assertNotIn("AcceptAndroidSDKLicenses=True", build)

    def test_release_version_reader_matches_project(self) -> None:
        import importlib.util

        reader_path = REPO / "scripts" / "read_android_version.py"
        spec = importlib.util.spec_from_file_location("android_version_reader", reader_path)
        self.assertIsNotNone(spec)
        self.assertIsNotNone(spec.loader)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        self.assertEqual(
            ("0.1.0-preview.3", "3"),
            module.read_project_version(PROJECT / "Chummer.Android.csproj"),
        )

    def test_store_listing_limits_and_truthful_preview_copy(self) -> None:
        listing = REPO / "play" / "listing" / "en-US"
        title = (listing / "title.txt").read_text(encoding="utf-8").strip()
        short_description = (listing / "short-description.txt").read_text(encoding="utf-8").strip()
        full_description = (listing / "full-description.txt").read_text(encoding="utf-8").strip()
        release_notes = (listing / "release-notes-3.txt").read_text(encoding="utf-8").strip()
        self.assertLessEqual(len(title), 30)
        self.assertLessEqual(len(short_description), 80)
        self.assertLessEqual(len(full_description), 4000)
        self.assertLessEqual(len(release_notes), 500)
        combined = "\n".join((short_description, full_description, release_notes)).lower()
        self.assertNotIn("verified app links", combined)
        self.assertNotIn("queued, synced", combined)

    def test_store_graphics_have_upload_dimensions(self) -> None:
        assets = REPO / "play" / "assets"
        feature = self._png_header(assets / "feature-graphic-1024x500.png")
        icon = self._png_header(assets / "app-icon-512x512.png")
        self.assertEqual((1024, 500), feature[:2])
        self.assertNotIn(feature[2], {4, 6}, "feature graphic must not have alpha")
        self.assertEqual((512, 512), icon[:2])

        phones = sorted((assets / "screenshots").glob("phone-*.png"))
        tablets = sorted((assets / "screenshots").glob("tablet-*.png"))
        self.assertGreaterEqual(len(phones), 5)
        self.assertGreaterEqual(len(tablets), 4)
        self.assertTrue(
            {
                "phone-01-home.png",
                "phone-02-build.png",
                "phone-03-new-runner.png",
                "phone-04-play.png",
                "phone-05-campaign.png",
            }.issubset({path.name for path in phones})
        )
        self.assertNotIn("phone-04-import.png", {path.name for path in phones})
        self.assertTrue(
            {
                "tablet-01-home.png",
                "tablet-02-build.png",
                "tablet-03-new-runner.png",
                "tablet-04-native-tools.png",
            }.issubset({path.name for path in tablets})
        )
        self.assertTrue(all(self._png_header(path)[:2] == (1080, 2400) for path in phones))
        self.assertTrue(all(self._png_header(path)[:2] == (1440, 2560) for path in tablets))

    def test_android_lifecycle_and_sensitive_print_cleanup_are_explicit(self) -> None:
        activity = (PROJECT / "Platforms" / "Android" / "MainActivity.cs").read_text(encoding="utf-8")
        shell = (PROJECT / "MainShell.cs").read_text(encoding="utf-8")
        print_service = (PROJECT / "Platforms" / "Android" / "AndroidSystemService.cs").read_text(encoding="utf-8")
        broker = (PROJECT / "Platforms" / "Android" / "DocumentIntentBroker.cs").read_text(encoding="utf-8")
        self.assertIn("HandleBackNavigation", activity)
        self.assertIn("RegisterOnBackInvokedCallback", activity)
        self.assertIn("EnableOnBackInvokedCallback = true", activity)
        self.assertIn("navigation?.ModalStack", activity)
        self.assertIn("navigation?.NavigationStack", activity)
        self.assertIn("FlyoutBehavior.Disabled", shell)
        self.assertIn("OnFinish", print_service)
        self.assertIn("File.Delete(_path)", print_service)
        self.assertIn("CryptographicOperations.ZeroMemory(buffer)", print_service)
        self.assertIn("Interlocked.CompareExchange", broker)

    @staticmethod
    def _png_header(path: Path) -> tuple[int, int, int]:
        data = path.read_bytes()[:33]
        if data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
            raise AssertionError(f"not a PNG: {path}")
        width, height, _depth, color_type, _compression, _filter, _interlace = struct.unpack(
            ">IIBBBBB", data[16:29]
        )
        return width, height, color_type


if __name__ == "__main__":
    unittest.main()
