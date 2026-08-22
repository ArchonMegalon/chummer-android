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
DESIGN = Path(
    os.environ.get("CHUMMER_DESIGN_ROOT", WORKSPACE / "chummer-design")
).resolve()
_REVIEWED_HUB_MARKER = RUN_SERVICES / "Chummer.Run.Contracts" / "AccountErasureContracts.cs"
if not _REVIEWED_HUB_MARKER.is_file():
    raise RuntimeError(
        "Android contract tests require a reviewed Hub checkout that contains "
        f"{_REVIEWED_HUB_MARKER}. The sibling chummer.run-services tree is not "
        "an authority when that file is missing. Set CHUMMER_RUN_SERVICES_ROOT "
        "to the origin/main Hub worktree."
    )
REGISTRY = DESIGN / "products" / "chummer" / "ANDROID_WINDOWS_FEATURE_PARITY.yaml"
EDITABILITY_INVENTORY = REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"
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
        self.assertIn("<ApplicationDisplayVersion>0.1.0-preview.9</ApplicationDisplayVersion>", project)
        self.assertIn("<ApplicationVersion>9</ApplicationVersion>", project)
        self.assertIn("<AndroidPackageFormats Condition=\"'$(Configuration)' == 'Release'\">aab</AndroidPackageFormats>", project)
        self.assertIn('<ChummerAndroidRuntimeIdentifier Condition="\'$(ChummerAndroidRuntimeIdentifier)\' == \'\'">android-arm64</ChummerAndroidRuntimeIdentifier>', project)
        self.assertIn('<RuntimeIdentifier Condition="\'$(RuntimeIdentifier)\' == \'\'">$(ChummerAndroidRuntimeIdentifier)</RuntimeIdentifier>', project)
        self.assertEqual(2, project.count('GlobalPropertiesToRemove="RuntimeIdentifier;RuntimeIdentifiers;SelfContained"'))
        self.assertEqual(2, project.count('AdditionalProperties="RuntimeIdentifier=;RuntimeIdentifiers=;SelfContained=false"'))
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
        self.assertIn("expiresAtUtc is null || expiresAtUtc <= DateTimeOffset.UtcNow", service)
        self.assertIn("IsPendingLinkCurrent(pendingStarted)", service)
        self.assertIn("bool resumeCurrentAttempt", service)
        self.assertIn("string state = resumeCurrentAttempt ? savedState! : NewBase64UrlToken(24);", service)
        self.assertIn("HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized", service)
        self.assertIn("startedAtUtc <= now.AddMinutes(2)", service)
        self.assertIn("HttpStatusCode.NotFound or HttpStatusCode.Gone", service)
        browser_failure = service[service.index('if (!await _systemService.OpenUriAsync'):]
        self.assertLess(browser_failure.index("ClearPending();"), browser_failure.index('"Browser unavailable"'))
        self.assertIn('string expectedPath = $"/groups/join/{Uri.EscapeDataString(code)}";', service)
        self.assertIn("!string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal)", service)
        self.assertIn("!string.IsNullOrEmpty(uri.Query)", service)
        self.assertIn("!string.IsNullOrEmpty(uri.Fragment)", service)
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
        more = (PROJECT / "Native" / "MorePage.cs").read_text(encoding="utf-8")
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
        response_validation = service.index("if (!IsCompleteAccountErasureReceipt(receipt))", service.index("EraseAccountAsync"))
        self.assertLess(server_call, local_delete)
        self.assertLess(response_validation, credentials_clear)
        self.assertIn("AccountDeletionPage", privacy)
        self.assertIn("AccountDeletionInfoPage", privacy)
        self.assertIn("if (!Coordinator.Account.IsLinked)", privacy)
        self.assertNotIn("Coordinator.OpenAccountDeletionInfoAsync", privacy)
        self.assertIn("still under review", privacy)
        self.assertNotIn("Active data is removed within 24 hours", privacy)
        self.assertNotIn("Chummer-controlled backups age out within 30 days", privacy)
        self.assertNotIn("Permanently remove your Chummer account", privacy)
        self.assertIn("RequiredErasureComponents", service)
        self.assertIn("IsCompleteAccountErasureReceipt", service)
        self.assertIn("completed.SetEquals(RequiredErasureComponents)", service)
        self.assertIn("Copy receipt", privacy)
        self.assertIn("Clipboard.Default.SetTextAsync(result.Receipt.ReceiptSha256)", privacy)
        self.assertIn("Remove runners saved on this device", privacy)
        self.assertIn("This cannot be undone.", privacy)
        self.assertIn("ChummerWebRoutes.AccountDeletion", coordinator)
        self.assertIn("Preferences.Default.Remove(SelectedGroupPreferenceKey)", coordinator)
        data_safety = (REPO / "play" / "data-safety.md").read_text(encoding="utf-8")
        release = (REPO / "docs" / "PLAY_RELEASE.md").read_text(encoding="utf-8")
        self.assertIn("More → Account & privacy → Delete account", data_safety + release)
        self.assertIn("https://chummer.run/account/delete", data_safety + release)
        self.assertIn("Personal info → User IDs", data_safety)
        self.assertIn("App activity → Other user-generated content", data_safety)
        self.assertIn("Device or other IDs", data_safety)
        self.assertIn("opaque installation grant", data_safety)
        self.assertIn("does not upload a runner file", data_safety)
        self.assertIn("no assistant-prompt", data_safety)
        self.assertNotIn("Assistant prompt/context and generated response", data_safety)
        self.assertNotIn("Support report and user-selected diagnostics", data_safety)
        normalized_release = re.sub(r"\s+", " ", release)
        self.assertIn("version code 7 (`0.1.0-preview.7`)", normalized_release)
        self.assertIn("supersedes preview.6", normalized_release)

        more_account = more[more.index("private void AddAccount()"):more.index("private void AddApp()")]
        linked_branch = more_account.index("if (Coordinator.Account.IsLinked)")
        privacy_row = more_account.index('"Account & privacy"')
        self.assertGreater(privacy_row, linked_branch)

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
        self.assertEqual(2, page.count("Interlocked.Increment(ref _runningActionDepth)"))
        self.assertEqual(2, page.count("Interlocked.Decrement(ref _runningActionDepth)"))
        self.assertIn("Volatile.Read(ref _runningActionDepth) > 0", page)
        self.assertNotIn("Microsoft.NET.Sdk.Razor", project)
        self.assertNotIn("Components.WebView.Maui", project)
        self.assertNotIn("Chummer.Blazor", project)

    def test_android_restores_the_last_local_workspace_after_process_restart(self) -> None:
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        initialize = coordinator[coordinator.index("public async Task InitializeAsync") :]
        initialize = initialize[: initialize.index("public async Task OpenLocalAsync")]
        restore = coordinator[coordinator.index("private async Task RestoreSelectedWorkspaceAsync") :]
        restore = restore[: restore.index("private void RefreshSurface")]

        self.assertIn("SelectedWorkspacePreferenceKey", coordinator)
        self.assertLess(
            initialize.index("await _presenter.InitializeAsync"),
            initialize.index("await RestoreSelectedWorkspaceAsync"),
        )
        self.assertIn("State.OpenWorkspaces.Count == 1", restore)
        self.assertIn("State.Session.ActiveWorkspaceId", restore)
        self.assertIn("State.Profile is not null", restore)
        self.assertIn("new CharacterWorkspaceId(selectedId)", restore)
        self.assertIn("await _presenter.LoadAsync(workspaceId.Value", restore)
        self.assertIn("await _presenter.LoadAsync", restore)
        self.assertNotIn("await _presenter.SwitchWorkspaceAsync", restore)
        self.assertIn("else if (_initialized && State.OpenWorkspaces.Count == 0)", coordinator)
        self.assertIn("Preferences.Default.Set(SelectedWorkspacePreferenceKey", coordinator)

    def test_character_settings_catalog_survives_android_process_restart(self) -> None:
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        initialize = coordinator[coordinator.index("public async Task InitializeAsync") :]
        initialize = initialize[: initialize.index("public async Task OpenLocalAsync")]
        state_changed = coordinator[coordinator.index("private void OnPresenterStateChanged") :]
        state_changed = state_changed[: state_changed.index("private void OnShellStateChanged")]

        self.assertIn("CharacterSettingsCatalogPreferenceKey", coordinator)
        self.assertLess(
            initialize.index("RestoreCharacterSettingsCatalog();"),
            initialize.index("await _presenter.InitializeAsync"),
        )
        self.assertIn("PersistCharacterSettingsCatalog();", state_changed)
        self.assertIn("DesktopPreferenceStateRuntime.SetCurrent", coordinator)

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
        navigation_row = theme[theme.index("public static Border NavigationRow") :]
        navigation_row = navigation_row[: navigation_row.index("public static Label FieldLabel")]
        self.assertIn("Button interaction = new()", navigation_row)
        self.assertIn("interaction.Clicked +=", navigation_row)
        self.assertIn("Grid.SetColumnSpan(interaction, 2)", navigation_row)
        self.assertNotIn("TapGestureRecognizer", navigation_row)
        self.assertIn("SemanticProperties.SetDescription", theme)
        self.assertIn("CollectionItemTitle(item.Label)", flow)
        collection_first = flow[flow.index("if (Coordinator.State.ActiveCollectionEditor is not null)") :]
        collection_first = collection_first[: collection_first.index("else")]
        self.assertLess(collection_first.index("AddValueGroups();"), collection_first.index("AddQuickActions();"))
        self.assertLess(collection_first.index("AddQuickActions();"), collection_first.index("AddSectionActions();"))
        self.assertNotIn("ScrollOrientation.Horizontal", build)
        self.assertNotIn("AddTabs", build)
        self.assertNotIn("Show all", build)

    def test_action_search_and_groups_present_dynamic_dialogs(self) -> None:
        commands = (PROJECT / "Native" / "NativeCommandPage.cs").read_text(encoding="utf-8")
        dialog = (PROJECT / "Native" / "NativeDialogPage.cs").read_text(encoding="utf-8")

        self.assertIn('AutomationId = "command-search"', commands)
        self.assertEqual(
            2,
            commands.count(
                "await Navigation.PushModalAsync(new NavigationPage(new NativeDialogPage"
            ),
        )
        self.assertIn('row.AutomationId = $"command-action-{Token(command.Id)}"', commands)
        self.assertIn("RequiresStructuralRerender", dialog)
        self.assertIn("FieldShapeMatches", dialog)
        self.assertIn("OptionsMatch", dialog)

    def test_tablet_collection_keeps_item_name_visible_ahead_of_metadata(self) -> None:
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        self.assertIn("CollectionItemCopy(item.Label)", tablet)
        self.assertIn('label.IndexOf(" · ", StringComparison.Ordinal)', tablet)

    def test_dialog_commit_recovers_the_active_section_projection(self) -> None:
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("State.ContentRevision > contentRevision", coordinator)
        self.assertIn("State.ActiveDialog is null", coordinator)
        self.assertIn("_presenter.ExecuteWorkspaceActionAsync(activeSectionAction", coordinator)

    def test_attributes_and_origin_dossier_have_native_mutation_paths(self) -> None:
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        attributes = (PROJECT / "Native" / "AttributeEditPage.cs").read_text(encoding="utf-8")
        dossier = (PROJECT / "Native" / "OriginDossierPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('"Origin dossier"', build + dossier)
        self.assertIn('automationId: "build-origin-dossier"', build)
        self.assertIn("AttributeWorkbenchProjector.BuildRows", flow + attributes)
        self.assertIn("AttributeEditRequest", attributes + coordinator)
        self.assertIn("OriginDossierEditRequest", dossier + coordinator)
        self.assertIn("_presenter.ApplyAttributeEditAsync", coordinator)
        self.assertIn("_presenter.ApplyOriginDossierEditAsync", coordinator)
        for automation_id in (
            "attribute-save-",
            "origin-dossier-identity",
            "origin-dossier-story",
        ):
            self.assertIn(automation_id, attributes + dossier)
        self.assertIn('save.AutomationId = $"origin-dossier-', dossier)
        self.assertNotIn("WebView", attributes + dossier)

    def test_character_notes_have_revision_bound_phone_save_path(self) -> None:
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        notes = (PROJECT / "Native" / "CharacterNotesPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('automationId: "build-character-notes"', build)
        self.assertIn('"character-notes-editor"', notes)
        self.assertIn('"character-game-notes-editor"', notes)
        self.assertIn('"character-group-notes-editor"', notes)
        self.assertIn("if (profile.Created)", notes)
        self.assertIn('_save.AutomationId = "character-notes-save"', notes)
        self.assertIn("CharacterNotesEditRequest", notes + coordinator)
        self.assertIn("ExpectedContentRevision", coordinator)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("_presenter.UpdateMetadataAsync", coordinator)
        self.assertIn("new UpdateWorkspaceMetadata(profile.Name, profile.Alias, request.CharacterNotes)", coordinator)
        self.assertIn("GameNotes = request.GameNotes", coordinator)
        self.assertIn("GroupNotes = request.GroupNotes", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertIn("coordinator.CharacterNotes", notes)
        self.assertIn("coordinator.GameNotes", notes)
        self.assertIn("coordinator.GroupNotes", notes)
        self.assertIn("State.Profile?.CharacterNotes", coordinator)
        self.assertIn("State.Profile?.GameNotes", coordinator)
        self.assertIn("State.Profile?.GroupNotes", coordinator)
        self.assertIn("_characterNotes = request.CharacterNotes", coordinator)
        self.assertIn("_gameNotes = request.GameNotes", coordinator)
        self.assertIn("_groupNotes = request.GroupNotes", coordinator)
        self.assertIn("await Navigation.PopAsync", notes)
        self.assertNotIn("XDocument", notes + coordinator)
        self.assertNotIn("<notes>", notes + coordinator)

    def test_character_notes_have_digest_bound_api36_restart_proof_lane(self) -> None:
        driver = (REPO / "tests" / "run_api36_character_notes_e2e.py").read_text(
            encoding="utf-8"
        )

        self.assertIn('"character-notes"', driver)
        self.assertIn('"characterNotesEditPersisted": "pass"', driver)
        self.assertIn('"allCreationNotesEdited": "pass"', driver)
        self.assertIn('"creationWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"creationProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"allCareerNotesEdited": "pass"', driver)
        self.assertIn('"careerWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"careerProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"controls": control_proofs', driver)
        self.assertIn('api != "36"', driver)
        self.assertIn('"apkSha256": shared.sha256(args.apk.resolve())', driver)
        self.assertIn('"driverSha256": shared.sha256(driver)', driver)
        self.assertIn('"sharedDriverSha256": Path(shared.__file__).resolve()', driver)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', driver)

    def test_career_reputation_has_revision_and_source_bound_phone_save_path(self) -> None:
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "CareerReputationPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('automationId: "build-career-reputation"', build)
        self.assertIn("if (Coordinator.State.Profile?.Created == true)", build)
        self.assertIn("PrepareCareerReputationEditAsync", build + coordinator)
        self.assertIn("ApplyCareerReputationEditAsync", page + coordinator)
        self.assertIn("CareerReputationEditRequest", page)
        self.assertIn("BurnStreetCredRequest", page + coordinator)
        self.assertIn("ApplyBurnStreetCredAsync", page + coordinator)
        self.assertIn("ExpectedContentRevision", coordinator)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        for automation_id in (
            "career-reputation-street-cred",
            "career-reputation-notoriety",
            "career-reputation-public-awareness",
            "career-reputation-astral",
            "career-reputation-wild",
            "career-reputation-save",
            "career-reputation-burn-street-cred",
        ):
            self.assertIn(automation_id, page)
        self.assertIn("if (editor.AstralReputationVisible)", page)
        self.assertIn("if (editor.WildReputationVisible)", page)
        self.assertIn("Enumerable.Range(0, 101)", page)
        self.assertNotIn("XDocument", page + coordinator)

    def test_career_reputation_has_digest_bound_api36_restart_proof_lane(self) -> None:
        driver = (REPO / "tests" / "run_api36_career_reputation_e2e.py").read_text(
            encoding="utf-8"
        )

        self.assertIn('"career-reputation"', driver)
        for control in (
            "nudStreetCred",
            "nudNotoriety",
            "nudPublicAware",
            "nudAstralReputation",
            "nudWildReputation",
        ):
            self.assertIn(f'"{control}"', driver)
        self.assertIn('"coreOnlySourceVisibilityEnforced": "pass"', driver)
        self.assertIn('"allCareerReputationEdited": "pass"', driver)
        self.assertIn('"streetCredBurnConfirmed": "pass"', driver)
        self.assertIn('"burntStreetCredIncrementedByTwo": "pass"', driver)
        self.assertIn('"cmdBurnStreetCred"', driver)
        self.assertIn('"careerWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"careerUiReopenReadback": "pass"', driver)
        self.assertIn('"careerProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"controls": controls', driver)
        self.assertIn('api != "36"', driver)
        self.assertIn('"apkSha256": shared.sha256(args.apk.resolve())', driver)
        self.assertIn('"driverSha256": shared.sha256(driver)', driver)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', driver)

    def test_situational_modifiers_have_revision_bound_creation_and_career_save_path(self) -> None:
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "SituationalModifiersPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('automationId: "build-situational-modifiers"', build)
        self.assertIn("PrepareSituationalModifiersEditAsync", build + coordinator)
        self.assertIn("ApplySituationalModifiersEditAsync", page + coordinator)
        self.assertIn("SituationalModifiersEditRequest", page)
        self.assertIn("ExpectedContentRevision", coordinator)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertIn('"situational-counterspelling-dice"', page)
        self.assertIn('"situational-lift-carry-hits"', page)
        self.assertIn('"situational-modifiers-save"', page)
        self.assertIn("Enumerable.Range(0, 101)", page)
        self.assertNotIn("XDocument", page + coordinator)

    def test_situational_modifiers_have_digest_bound_api36_restart_proof_lane(self) -> None:
        driver = (REPO / "tests" / "run_api36_situational_modifiers_e2e.py").read_text(
            encoding="utf-8"
        )

        self.assertIn('"journey": "situational-modifiers"', driver)
        for form in ("CharacterCreate", "CharacterCareer"):
            self.assertIn(f'"{form}"', driver)
        for control in ("nudCounterspellingDice", "nudLiftCarryHits"):
            self.assertIn(f'"{control}"', driver)
        self.assertIn('"allCreationSituationalModifiersEdited": "pass"', driver)
        self.assertIn('"creationWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"creationUiReopenReadback": "pass"', driver)
        self.assertIn('"creationProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"allCareerSituationalModifiersEdited": "pass"', driver)
        self.assertIn('"careerWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"careerUiReopenReadback": "pass"', driver)
        self.assertIn('"careerProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"controls": controls', driver)
        self.assertIn('api != "36"', driver)
        self.assertIn('"apkSha256": shared.sha256(args.apk.resolve())', driver)
        self.assertIn('"driverSha256": shared.sha256(driver)', driver)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', driver)

    def test_primary_arm_has_revision_bound_ambidextrous_safe_phone_path(self) -> None:
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "PrimaryArmPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('automationId: "build-primary-arm"', build)
        self.assertIn("PreparePrimaryArmEditAsync", build + coordinator)
        self.assertIn("ApplyPrimaryArmEditAsync", page + coordinator)
        self.assertIn("PrimaryArmEditRequest", page)
        self.assertIn("ExpectedContentRevision", coordinator)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertIn('["Ambidextrous"]', page)
        self.assertIn('["Left", "Right"]', page)
        self.assertIn('"primary-arm-choice"', page)
        self.assertIn('"primary-arm-save"', page)
        self.assertNotIn("XDocument", page + coordinator)

    def test_primary_arm_has_digest_bound_api36_restart_and_gate_proof_lane(self) -> None:
        driver = (REPO / "tests" / "run_api36_primary_arm_e2e.py").read_text(
            encoding="utf-8"
        )

        self.assertIn('"journey": "primary-arm"', driver)
        for form in ("CharacterCreate", "CharacterCareer"):
            self.assertIn(f'"{form}"', driver)
        self.assertIn('CONTROL = "cboPrimaryArm"', driver)
        self.assertIn('"creationPrimaryArmEdited": "pass"', driver)
        self.assertIn('"creationWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"creationProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"careerPrimaryArmEdited": "pass"', driver)
        self.assertIn('"careerWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"careerProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"ambidextrousReadOnlyGateEnforced": "pass"', driver)
        self.assertIn('picker.attributes.get("enabled") != "false"', driver)
        self.assertIn('save.attributes.get("enabled") != "false"', driver)
        self.assertIn('"controls": controls', driver)
        self.assertIn('api != "36"', driver)
        self.assertIn('"apkSha256": shared.sha256(args.apk.resolve())', driver)
        self.assertIn('"driverSha256": shared.sha256(driver)', driver)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', driver)

    def test_explicit_save_actions_are_truthful_and_have_api36_restart_proof(self) -> None:
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        more = (PROJECT / "Native" / "MorePage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        driver = (REPO / "tests" / "run_api36_explicit_save_e2e.py").read_text(
            encoding="utf-8"
        )

        self.assertIn('AutomationId = "build-save-runner"', build)
        self.assertIn('save.AutomationId = "more-save-runner"', more)
        self.assertIn("Coordinator.SaveAsync", build + more)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertIn('_notice = State.Error is null ? "Saved." : null', coordinator)
        self.assertIn('"journey": "explicit-save"', driver)
        self.assertIn('"creationBuildToolbarSaveInvoked": "pass"', driver)
        self.assertIn('"creationMorePageSaveInvoked": "pass"', driver)
        self.assertIn('"careerBuildToolbarSaveInvoked": "pass"', driver)
        self.assertIn('"careerMorePageSaveInvoked": "pass"', driver)
        self.assertIn('saved_revision != content_revision', driver)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', driver)
        self.assertIn('"controls": controls', driver)
        self.assertIn('api != "36"', driver)
        self.assertIn('"apkSha256": shared.sha256(args.apk.resolve())', driver)
        self.assertIn('"driverSha256": shared.sha256(driver)', driver)

    def test_nested_collection_notes_have_digest_bound_api36_restart_proof_lane(self) -> None:
        driver = (
            REPO / "tests" / "run_api36_nested_collection_notes_e2e.py"
        ).read_text(encoding="utf-8")

        self.assertIn('"nested-collection-notes"', driver)
        self.assertIn('"CharacterCreate"', driver)
        self.assertIn('"CharacterCareer"', driver)
        self.assertIn('"tsWeaponAccessoryNotes"', driver)
        self.assertIn('"tsArmorModNotes"', driver)
        self.assertIn('"tsGearPluginNotes"', driver)
        self.assertIn('"allCreationNestedNotesEdited": "pass"', driver)
        self.assertIn('"creationWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"creationProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"allCareerNestedNotesEdited": "pass"', driver)
        self.assertIn('"careerWorkspaceXmlPersisted": "pass"', driver)
        self.assertIn('"careerProcessRestartUiReadback": "pass"', driver)
        self.assertIn('"controls": control_proofs', driver)
        self.assertIn('api != "36"', driver)
        self.assertIn('"apkSha256": shared.sha256(args.apk.resolve())', driver)
        self.assertIn('"driverSha256": shared.sha256(driver)', driver)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', driver)
        self.assertIn('read_nested_note(character, target)', driver)

    def test_collection_items_have_typed_stable_id_phone_editing_paths(self) -> None:
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        editor = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "NativePageBase.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn("ActiveCollectionEditor", flow + editor)
        self.assertIn("WorkspaceCollectionItemTarget", editor)
        self.assertIn("WorkspacePatchCollectionItemRequest", editor)
        self.assertIn("WorkspaceMoveCollectionItemRequest", editor)
        self.assertIn("WorkspaceDeleteCollectionItemRequest", editor)
        self.assertIn("WorkspaceAddNestedCollectionItemRequest", editor)
        self.assertIn("SectionQuickActionCatalog.ForSection", flow)
        self.assertIn("_presenter.ApplyCollectionMutationAsync", coordinator)
        self.assertIn("_presenter.HandleUiControlAsync", coordinator)
        self.assertIn("Coordinator.HandleUiControlAsync(action.ControlId)", flow)
        self.assertNotIn("Coordinator.ExecuteCommandAsync(action.ControlId)", flow)
        self.assertIn("new CollectionItemEditorPage", flow)
        self.assertIn("new NestedCollectionAddPage", editor)
        self.assertIn("RunWithConditionalRefreshAsync", page + editor + tablet)
        self.assertIn("private async Task<bool> SaveAsync", editor)
        self.assertIn("private async Task<bool> SaveInspectorAsync", tablet)
        self.assertIn("return false;", editor + tablet)
        for automation_id in (
            "collection-item-",
            "collection-field-",
            "collection-save-",
            "collection-move-up-",
            "collection-move-down-",
            "collection-delete-",
            "collection-add-",
            "nested-save",
            "section-quick-",
        ):
            self.assertIn(automation_id, flow + editor)
        self.assertNotIn("XDocument", editor)
        self.assertNotIn("XPath", editor)
        self.assertNotIn("<character", editor)

    def test_condition_monitors_have_closed_career_phone_and_tablet_editors(self) -> None:
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        phone = (PROJECT / "Native" / "ConditionMonitorEditPage.cs").read_text(encoding="utf-8")
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceXmlMutationCatalog.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("ActiveConditionMonitor", flow + phone + tablet)
        self.assertIn("ConditionMonitorEditRequest", phone + tablet + coordinator)
        self.assertIn("_presenter.ApplyConditionMonitorEditAsync", coordinator)
        self.assertIn("Condition monitors can only be changed for a created/career runner", presentation)
        for automation_id in (
            "condition-monitor-editor-",
            "condition-monitor-filled-",
            "condition-monitor-save-",
            "condition-monitor-clear-",
            "tablet-condition-track-",
            "tablet-condition-filled-",
            "tablet-condition-save-",
            "tablet-condition-clear-",
        ):
            self.assertIn(automation_id, flow + phone + tablet)
        self.assertNotIn("XDocument", phone + tablet)
        self.assertNotIn("XPath", phone + tablet)
        self.assertNotIn("<character", phone + tablet)

    def test_gear_location_add_is_revision_bound_phone_deep_navigation(self) -> None:
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "GearLocationAddPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('"gearlocations"', flow)
        self.assertIn("new GearLocationAddPage", flow)
        self.assertIn('automationId: "gear-location-open-add"', flow)
        self.assertIn('AutomationId = "gear-location-name"', page)
        self.assertIn('AutomationId = "gear-location-add"', page)
        self.assertIn("GearLocationAddRequest.MaximumNameLength", page)
        self.assertIn("_workspaceId", page)
        self.assertIn("_contentRevision", page)
        self.assertIn("Coordinator.ApplyGearLocationAddAsync", page)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("_presenter.ApplyGearLocationAddAsync", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertNotIn("XDocument", page + coordinator)
        self.assertNotIn("XPath", page + coordinator)
        self.assertNotIn("<gearlocations", page + coordinator)

    def test_weapon_location_add_is_revision_bound_phone_deep_navigation(self) -> None:
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "WeaponLocationAddPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('case "weaponlocations"', flow)
        self.assertIn("new WeaponLocationAddPage", flow)
        self.assertIn('automationId: "weapon-location-open-add"', flow)
        self.assertIn('AutomationId = "weapon-location-name"', page)
        self.assertIn('AutomationId = "weapon-location-add"', page)
        self.assertIn("WeaponLocationAddRequest.MaximumNameLength", page)
        self.assertIn("_workspaceId", page)
        self.assertIn("_contentRevision", page)
        self.assertIn("Coordinator.ApplyWeaponLocationAddAsync", page)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("_presenter.ApplyWeaponLocationAddAsync", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertNotIn("XDocument", page + coordinator)
        self.assertNotIn("XPath", page + coordinator)
        self.assertNotIn("<weaponlocations", page + coordinator)

    def test_vehicle_location_add_covers_global_and_selected_vehicle_branches_with_typed_identity(self) -> None:
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        editor = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "VehicleLocationAddPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('case "vehiclelocations"', flow)
        self.assertIn("new VehicleLocationAddPage", flow + editor)
        self.assertIn('automationId: "vehicle-location-open-add-global"', flow)
        self.assertIn('"vehicle-location-open-add-{vehicleId:N}"', editor)
        self.assertIn("item.VehicleLocations is null", editor)
        self.assertIn('Guid.TryParseExact(_target.ItemId, "D"', editor)
        self.assertIn('vehicleId?.ToString("N") ?? "global"', page)
        self.assertIn("VehicleLocationAddRequest.MaximumNameLength", page)
        self.assertIn("_vehicleId", page)
        self.assertIn("_workspaceId", page)
        self.assertIn("_contentRevision", page)
        self.assertIn("Coordinator.ApplyVehicleLocationAddAsync", page)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("_presenter.ApplyVehicleLocationAddAsync", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertNotIn("XDocument", page + editor + coordinator)
        self.assertNotIn("XPath", page + editor + coordinator)
        self.assertNotIn("<vehiclelocations", page + editor + coordinator)
        self.assertNotIn("<locations", page + editor + coordinator)

    def test_location_rename_is_typed_stable_revision_bound_phone_navigation(self) -> None:
        flow = (PROJECT / "Native" / "BuildFlowPages.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "LocationRenamePage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn("ActiveLocationEditor", flow)
        self.assertIn("WorkspaceLocationItemState", flow)
        self.assertIn("new LocationRenamePage", flow)
        self.assertIn('"location-rename-open-', flow)
        self.assertIn('AutomationId = "location-rename-name"', page)
        self.assertIn('AutomationId = "location-rename-save"', page)
        self.assertIn("LocationRenameRequest.MaximumNameLength", page)
        self.assertIn("_location.Id", page)
        self.assertIn("Coordinator.ApplyLocationRenameAsync", page)
        self.assertIn("State.WorkspaceId != request.WorkspaceId", coordinator)
        self.assertIn("State.ContentRevision != request.ExpectedContentRevision", coordinator)
        self.assertIn("_presenter.ApplyLocationRenameAsync", coordinator)
        self.assertIn("await _presenter.SaveAsync", coordinator)
        self.assertNotIn("XDocument", page + coordinator)
        self.assertNotIn("XPath", page + coordinator)
        self.assertNotIn("<location", page + coordinator)

    def test_device_damage_tracks_use_exact_shared_phone_and_tablet_patch(self) -> None:
        phone = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        state = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionEditorState.cs"
        ).read_text(encoding="utf-8")
        request = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionMutationRequest.cs"
        ).read_text(encoding="utf-8")
        mutation = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceXmlMutationCatalog.cs"
        ).read_text(encoding="utf-8")
        projector = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionEditorProjector.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("WorkspaceItemConditionMonitorState", state)
        self.assertIn("PhysicalConditionMonitor", state + phone + tablet)
        self.assertIn("MatrixConditionMonitor", state + phone + tablet)
        self.assertIn("VehiclePhysicalDamage", request + phone + tablet)
        self.assertIn("VehicleMatrixDamage", request + phone + tablet)
        self.assertIn("GearMatrixDamage", request + phone + tablet)
        self.assertIn("ArmorMatrixDamage", request + phone + tablet)
        self.assertIn("WeaponMatrixDamage", request + phone + tablet)
        self.assertIn("CyberwareMatrixDamage", request + phone + tablet)
        self.assertIn("CharacterVehicleConditionMonitorCalculator.TryCalculatePhysicalMaximum", mutation)
        self.assertIn("CharacterMatrixConditionMonitorCalculator.TryCalculateMaximum", mutation)
        self.assertIn("WorkspaceNestedCollectionKind.Gear", projector)
        self.assertIn("exact condition-monitor maximum is unavailable", mutation)
        self.assertIn("collection-vehicle-physical-damage-", phone)
        self.assertIn("collection-vehicle-matrix-damage-", phone)
        self.assertIn("collection-gear-matrix-damage-", phone)
        self.assertIn("collection-armor-matrix-damage-", phone)
        self.assertIn("collection-weapon-matrix-damage-", phone)
        self.assertIn("collection-cyberware-matrix-damage-", phone)
        self.assertIn('AutomationId = "tablet-vehicle-physical-damage"', tablet)
        self.assertIn('"tablet-vehicle-matrix-damage"', tablet)
        self.assertIn('"tablet-gear-matrix-damage"', tablet)
        self.assertIn('"tablet-armor-matrix-damage"', tablet)
        self.assertIn('"tablet-weapon-matrix-damage"', tablet)
        self.assertIn('"tablet-cyberware-matrix-damage"', tablet)
        self.assertIn("WorkspacePatchCollectionItemRequest", phone + tablet)
        self.assertNotIn("XDocument", phone + tablet)
        self.assertNotIn("XPath", phone + tablet)
        self.assertNotIn("<character", phone + tablet)

    def test_contacts_use_shared_chummer5_semantics_on_phone_and_tablet(self) -> None:
        phone = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        e2e = (REPO / "tests" / "run_api36_editing_e2e.py").read_text(encoding="utf-8")
        core = (
            WORKSPACE
            / "chummer-core-engine"
            / "Chummer.Contracts"
            / "Characters"
            / "CharacterContactEditSemantics.cs"
        ).read_text(encoding="utf-8")
        state = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionEditorState.cs"
        ).read_text(encoding="utf-8")
        request = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionMutationRequest.cs"
        ).read_text(encoding="utf-8")
        mutation = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceXmlMutationCatalog.cs"
        ).read_text(encoding="utf-8")

        for marker in (
            "FriendsInHighPlaces",
            "ContactForceGroup",
            "ContactMakeFree",
            "ContactForcedLoyalty",
            "IdentityEditable",
            "ConnectionMaximum",
            "CanDelete",
        ):
            self.assertIn(marker, core)
        self.assertIn("WorkspaceContactEditorState", state + phone + tablet)
        self.assertIn("ContactConnection", request + mutation + phone + tablet)
        self.assertIn("ContactLoyalty", request + mutation + phone + tablet)
        self.assertIn("original.IsEnabled", phone + tablet)
        self.assertIn("IsEnabled = value.IsEnabled", phone + tablet)
        self.assertIn("collection-contact-connection-", phone)
        self.assertIn("collection-contact-loyalty-", phone)
        self.assertIn('"tablet-contact-connection"', tablet)
        self.assertIn('"tablet-contact-loyalty"', tablet)
        self.assertIn("item.CanDelete", phone + tablet)
        self.assertIn("WorkspacePatchCollectionItemRequest", phone + tablet)
        for marker in (
            "build-section-tab-relationships",
            "build-action-tab-relationships-contacts",
            "tablet-build-tab-tab-relationships",
            "tablet-build-action-tab-relationships-contacts",
            "collection-contact-connection-",
            "tablet-contact-connection",
            '"contactInvalidBoundsRejected": "pass"',
            '"contactEditPersisted": "pass"',
            '"contactDeletePersisted": "pass"',
            '"processRestartContactPersistence": "pass"',
        ):
            self.assertIn(marker, e2e)
        self.assertNotIn("XDocument", phone + tablet)
        self.assertNotIn("XPath", phone + tablet)
        self.assertNotIn("<character", phone + tablet)

    def test_pets_use_type_safe_shared_phone_and_tablet_editing(self) -> None:
        phone = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        e2e = (REPO / "tests" / "run_api36_editing_e2e.py").read_text(encoding="utf-8")
        core = (
            WORKSPACE
            / "chummer-core-engine"
            / "Chummer.Contracts"
            / "Characters"
            / "CharacterPetEditSemantics.cs"
        ).read_text(encoding="utf-8")
        request = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionMutationRequest.cs"
        ).read_text(encoding="utf-8")
        projector = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionEditorProjector.cs"
        ).read_text(encoding="utf-8")
        mutation = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceXmlMutationCatalog.cs"
        ).read_text(encoding="utf-8")
        dialog = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "DialogCoordinator.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("CharacterPetEditSemanticsResolver", core)
        self.assertIn("IdentityEditable", core)
        self.assertIn("WorkspaceCollectionKind.Pet", projector + mutation)
        self.assertIn('"pets" => new(key, "contacts", WorkspaceCollectionKind.Pet)', projector)
        self.assertIn("IsExpectedContactRecordType", mutation)
        self.assertIn("ResolvePetSemantics", mutation)
        self.assertIn("AddPet", mutation)
        self.assertIn("WorkspaceQuickAddKinds.Pet", dialog)
        self.assertIn("    Pet,", request)
        for marker in (
            "collection-field-",
            "collection-delete-",
            "original.IsEnabled",
            "WorkspacePatchCollectionItemRequest",
        ):
            self.assertIn(marker, phone)
        for marker in (
            "tablet-field-",
            "tablet-inspector-delete",
            "original.IsEnabled",
            "WorkspacePatchCollectionItemRequest",
        ):
            self.assertIn(marker, tablet)
        for marker in (
            "build-action-tab-relationships-pets",
            "tablet-build-action-tab-relationships-pets",
            'f"{prefix}-field-metatype"',
            'f"{prefix}-field-notes"',
            '"petInvalidNameRejected": "pass"',
            '"petEditPersisted": "pass"',
            '"petDeletePersisted": "pass"',
            '"processRestartPetPersistence": "pass"',
        ):
            self.assertIn(marker, e2e)
        self.assertNotIn("XDocument", phone + tablet)
        self.assertNotIn("XPath", phone + tablet)
        self.assertNotIn("<character", phone + tablet)

    def test_linked_chummer5_runners_use_governed_phone_and_tablet_mutations(self) -> None:
        phone = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        staging = (PROJECT / "Platform" / "IAndroidLinkedCharacterFileService.cs").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        e2e = (REPO / "tests" / "run_api36_editing_e2e.py").read_text(encoding="utf-8")
        state = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionEditorState.cs"
        ).read_text(encoding="utf-8")
        request = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceCollectionMutationRequest.cs"
        ).read_text(encoding="utf-8")
        mutation = (
            WORKSPACE
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "WorkspaceXmlMutationCatalog.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("WorkspaceLinkedCharacterState", state)
        self.assertIn("WorkspaceSetLinkedCharacterRequest", request + mutation + coordinator)
        self.assertIn("WorkspaceRemoveLinkedCharacterRequest", request + mutation + coordinator)
        self.assertIn("ICharacterLinkedDocumentCodec", staging)
        self.assertIn('DirectoryName = "linked-characters"', staging)
        self.assertIn("SHA256.HashData(selected.Content)", staging)
        self.assertIn("File.Move(temporaryPath, finalPath, overwrite: true)", staging)
        self.assertIn("CryptographicOperations.ZeroMemory(selected.Content)", staging)
        self.assertIn("BuildTargetPrefix(target)", staging)
        self.assertIn("AddSingleton<IAndroidLinkedCharacterFileService, AndroidLinkedCharacterFileService>", program)
        self.assertIn("_linkedCharacters.StageAsync", coordinator)
        self.assertIn("_linkedCharacters.DeleteOwnedAsync", coordinator)
        for marker in (
            "collection-linked-status-",
            "collection-linked-attach-",
            "collection-linked-remove-",
        ):
            self.assertIn(marker, phone)
        for marker in (
            "tablet-linked-status",
            "tablet-linked-attach",
            "tablet-linked-remove",
        ):
            self.assertIn(marker, tablet)
        for marker in (
            '"/sdcard/Download/linked-runner-e2e.chum5"',
            '"/sdcard/Download/invalid-linked-runner-e2e.chum5"',
            "assert_linked_identity",
            "assert_link_persisted_then_remove",
            '"linkedRunnerInvalidDocumentRejected": "pass"',
            '"contactLinkedRunnerAttachPersisted": "pass"',
            '"contactLinkedRunnerRemoveRestoredIdentity": "pass"',
            '"petLinkedRunnerAttachPersisted": "pass"',
            '"petLinkedRunnerRemoveRestoredIdentity": "pass"',
        ):
            self.assertIn(marker, e2e)

        for marker in (
            '"/sdcard/Download/career-condition-monitor-e2e.chum5"',
            'if args.journey in {"condition-monitor", "contact-pet"}:',
            'select_android_document(device, fixture_name)',
            '"careerRunnerImport": "pass"',
            '"inputFixtureSha256": sha256(args.condition_runner.resolve())',
        ):
            self.assertIn(marker, e2e)
        self.assertNotIn("XDocument", phone + tablet + coordinator + staging)
        self.assertNotIn("XPath", phone + tablet + coordinator + staging)

    def test_tablet_uses_explicit_large_screen_shell_and_persistent_editing_panes(self) -> None:
        shell = (PROJECT / "MainShell.cs").read_text(encoding="utf-8")
        policy = (PROJECT / "Native" / "TabletLayoutPolicy.cs").read_text(encoding="utf-8")
        tablet = (PROJECT / "Native" / "TabletBuildPage.cs").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        e2e = (REPO / "tests" / "run_api36_editing_e2e.py").read_text(encoding="utf-8")

        self.assertIn("ExpandedWidthDip = 840d", policy)
        self.assertIn("idiom == DeviceIdiom.Tablet || widthDip >= ExpandedWidthDip", policy)
        self.assertIn("TabletLayoutPolicy.UseTabletComposition", shell)
        self.assertIn("BuildTabletShell", shell)
        self.assertIn("FlyoutBehavior.Flyout", shell)
        self.assertIn("CreateTabletDestination<TabletBuildPage>", shell)
        self.assertIn("AddTransient<TabletBuildPage>", program)
        for automation_id in (
            "tablet-build-layout",
            "tablet-build-navigation-pane",
            "tablet-build-collection-pane",
            "tablet-build-inspector-pane",
            "tablet-inspector-save",
            "tablet-inspector-delete",
            "tablet-origin-dossier",
            "tablet-attribute-",
            "tablet-attribute-base-",
            "tablet-attribute-karma-",
            "tablet-attribute-save-",
            "tablet-attribute-improve-",
            "tablet-attribute-burn-edge",
            "tablet-condition-track-",
            "tablet-condition-filled-",
            "tablet-condition-save-",
            "tablet-condition-clear-",
            "tablet-vehicle-physical-damage",
            "tablet-vehicle-matrix-damage",
            "tablet-gear-matrix-damage",
            "tablet-armor-matrix-damage",
            "tablet-weapon-matrix-damage",
            "tablet-cyberware-matrix-damage",
            "tablet-contact-connection",
            "tablet-contact-loyalty",
        ):
            self.assertIn(automation_id, tablet)
        self.assertIn("SizeChanged", tablet)
        self.assertIn("_selectedTarget", tablet)
        self.assertIn("_selectedAttributeName", tablet)
        self.assertIn("AttributeWorkbenchProjector.BuildRows", tablet)
        self.assertIn("AttributeEditRequest", tablet)
        self.assertIn("WorkspacePatchCollectionItemRequest", tablet)
        self.assertIn("WorkspaceMoveCollectionItemRequest", tablet)
        self.assertIn("WorkspaceDeleteCollectionItemRequest", tablet)
        self.assertIn("SectionQuickActionCatalog.ForSection", tablet)
        self.assertIn("Coordinator.HandleUiControlAsync(action.ControlId)", tablet)
        self.assertNotIn("Coordinator.ExecuteCommandAsync(action.ControlId)", tablet)
        self.assertNotIn("WebView", tablet)
        self.assertNotIn("XDocument", tablet)
        for automation_id in (
            "tablet-build-tab-tab-attributes",
            "tablet-attribute-body",
            "tablet-attribute-base-body",
            "tablet-attribute-save-body",
        ):
            self.assertIn(automation_id, e2e)
        self.assertIn('"attributeBaseEditPersisted": "pass"', e2e)
        self.assertNotIn("not_covered", e2e)

    def test_editing_parity_matrix_is_explicit_and_fail_closed(self) -> None:
        parity = self.registry["editing_parity"]
        surfaces = parity["surfaces"]
        control_inventory = parity["control_inventory"]
        profiles = self.registry["ui_profiles"]
        self.assertIn("Every value that Chummer5 allows", parity["goal"])
        self.assertIn("API 36 emulator persistence journey", parity["completion_rule"])
        self.assertEqual("deep_native_navigation", profiles["phone"]["composition"])
        self.assertEqual("adaptive_master_detail_multi_pane", profiles["tablet"]["composition"])
        self.assertEqual("implemented_pending_emulator", surfaces["attributes"]["status"])
        self.assertEqual("implemented_pending_emulator", surfaces["origin_dossier"]["status"])
        self.assertGreaterEqual(len(surfaces), 20)
        allowed = set(parity["status_legend"])
        self.assertTrue(all(surface["status"] in allowed for surface in surfaces.values()))
        self.assertTrue(all(surface["phone_status"] in allowed for surface in surfaces.values()))
        self.assertTrue(all(surface["tablet_status"] in allowed for surface in surfaces.values()))
        self.assertTrue(any(surface["status"] == "missing" for surface in surfaces.values()))
        self.assertTrue(any(surface["status"] == "partial_create_only" for surface in surfaces.values()))
        self.assertTrue(any(surface["tablet_status"] == "missing" for surface in surfaces.values()))
        self.assertEqual(
            "chummer.android.chummer5-editability-inventory/v1",
            control_inventory["schema"],
        )
        self.assertEqual(
            "chummer-android/docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json",
            control_inventory["artifact"],
        )
        self.assertEqual(
            "materialized_incomplete_fail_closed",
            control_inventory["status"],
        )

        inventory = json.loads(EDITABILITY_INVENTORY.read_text(encoding="utf-8"))
        rows = inventory["rows"]
        self.assertEqual(control_inventory["schema"], inventory["schema"])
        self.assertEqual("incomplete_fail_closed", inventory["status"])
        self.assertFalse(inventory["completionProven"])
        self.assertEqual(len(rows), inventory["summary"]["rowCount"])
        self.assertGreater(len(rows), 1000)
        self.assertEqual(len(rows), len({row["id"] for row in rows}))
        self.assertEqual(0, inventory["summary"]["reviewRequiredCount"])
        self.assertEqual(len(rows), inventory["summary"]["legacyReviewCompleteCount"])
        self.assertGreater(inventory["summary"]["reviewedNonMutatingCount"], 0)
        self.assertEqual(0, inventory["summary"]["unclassifiedCount"])
        self.assertEqual(
            inventory["summary"]["reviewedNonMutatingCount"] + 75,
            inventory["summary"]["completionProvenCount"],
        )
        required_fields = set(inventory["requiredRowFields"])
        for row in rows:
            self.assertTrue(required_fields.issubset(row))
            self.assertTrue(row["legacyReviewComplete"])
            self.assertTrue(row["legacy"]["dispositionEvidence"])
            if row["completionProven"]:
                self.assertIn(
                    row["overallStatus"],
                    {"complete", "not_applicable_non_mutating"},
                )
            if not row["editParityRequired"]:
                self.assertTrue(row["completionProven"])
                self.assertEqual("not_applicable_non_mutating", row["overallStatus"])
            self.assertIn(row["mutationFamily"], surfaces)
            self.assertFalse(Path(row["legacy"]["sourcePath"]).is_absolute())

        product_spec = (DESIGN / "products" / "chummer" / "ANDROID_APP_PRODUCT_SPEC.md").read_text(
            encoding="utf-8"
        )
        normalized_spec = " ".join(product_spec.split())
        self.assertIn("### Phone composition", product_spec)
        self.assertIn("### Tablet composition", product_spec)
        self.assertIn("### Shared capability contract", product_spec)
        self.assertIn("A larger screen capture of the phone stack does not count", normalized_spec)
        self.assertIn("purpose-designed tablet surface", normalized_spec)

    def test_android_copy_is_short_and_human(self) -> None:
        account = (PROJECT / "Platform" / "AndroidAccountLinkService.cs").read_text(encoding="utf-8")
        ui_source = account + "".join(
            path.read_text(encoding="utf-8") for path in (PROJECT / "Native").glob("*.cs")
        )
        rendered_copy = "\n".join(re.findall(r'"([^"\\]*(?:\\.[^"\\]*)*)"', ui_source))
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
        self.assertIn(
            'string.Equals(field.InputType, "select", StringComparison.OrdinalIgnoreCase)',
            dialog,
        )
        self.assertIn("field.Options ?? []", dialog)
        self.assertIn("Picker picker", dialog)
        self.assertIn("Picker groups", play)
        self.assertIn("Picker picker", campaign)
        self.assertNotIn("RadioButton", dialog + play + campaign)

    def test_android_dynamic_dialog_fields_rerender_and_build_reads_metavariant(self) -> None:
        dialog = (PROJECT / "Native" / "NativeDialogPage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        update = dialog[dialog.index("private async Task UpdateFieldAsync") :]
        self.assertIn("await _coordinator.UpdateDialogFieldAsync(fieldId, value)", update)
        self.assertIn("DesktopDialogState? previous = _coordinator.State.ActiveDialog", update)
        self.assertIn("DesktopDialogState? next = _coordinator.State.ActiveDialog", update)
        self.assertIn("RequiresStructuralRerender(previous, next, fieldId)", update)
        self.assertIn("!FieldShapeMatches(previousField, nextField)", update)
        self.assertIn("!previous.Actions.SequenceEqual(next.Actions)", update)
        self.assertIn("Render(next)", update)
        self.assertIn('NativeTheme.Metric("Metavariant"', build)
        self.assertIn("Coordinator.State.Profile?.Metavariant", build)

    def test_android_handoffs_use_canonical_public_routes(self) -> None:
        routes = (PROJECT / "Platform" / "ChummerWebRoutes.cs").read_text(encoding="utf-8")
        campaign = (PROJECT / "Native" / "CampaignPage.cs").read_text(encoding="utf-8")
        public_controller = (
            RUN_SERVICES / "Chummer.Run.Api" / "Controllers" / "PublicLandingController.cs"
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
            RUN_SERVICES
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
        self.assertIn('"Save operator handoff"', campaign)
        self.assertIn('"Finished books shared with this group."', campaign)
        self.assertIn("else if (!string.IsNullOrWhiteSpace(selected.ExportFormat))", campaign)
        self.assertIn("SpoilerReviewConfirmed", campaign + service + controller)
        self.assertIn("UploadApprovedAtUtc", service + controller)
        self.assertNotIn('"approve_handoff"', campaign + controller)
        self.assertIn("SaveChroniclePacketAsync", coordinator)
        self.assertIn("SaveChronicleHandoffAsync", coordinator)
        self.assertIn("DownloadChronicleHandoffAsync", service)
        self.assertIn("AdvanceChronicleAsync", coordinator)
        self.assertIn("/chronicles/create", service)
        self.assertIn("/handoff", service)
        self.assertIn("/chronicles/{chronicleProjectId}/actions", controller)
        self.assertIn("CryptographicOperations.ZeroMemory(packet)", controller)
        self.assertIn("GetChronicleOperatorHandoff", controller)
        self.assertIn("CryptographicOperations.ZeroMemory(handoff)", controller)
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
        debug_build = (REPO / "scripts" / "build-debug.sh").read_text(encoding="utf-8")
        source_graph = (REPO / "scripts" / "verify_release_source_graph.py").read_text(encoding="utf-8")
        bootstrap = (REPO / "scripts" / "bootstrap-build-environment.sh").read_text(encoding="utf-8")
        provision = (REPO / "scripts" / "provision-upload-key.sh").read_text(encoding="utf-8")
        recovery = (REPO / "scripts" / "import-signing-recovery.py").read_text(encoding="utf-8")
        validate = (REPO / "scripts" / "validate-aab.sh").read_text(encoding="utf-8")
        inspect = (REPO / "scripts" / "inspect_aab.py").read_text(encoding="utf-8")
        version_reader = (REPO / "scripts" / "read_android_version.py").read_text(encoding="utf-8")
        self.assertIn("set -euo pipefail", build)
        self.assertIn("set -euo pipefail", debug_build)
        self.assertIn('runtime_identifier="${CHUMMER_ANDROID_RUNTIME_ID:-android-arm64}"', debug_build)
        self.assertIn('android-arm64|android-x64', debug_build)
        self.assertIn('-p:ChummerAndroidRuntimeIdentifier="$runtime_identifier"', debug_build)
        self.assertIn('restore "$solution_path" \\\n  --disable-parallel', debug_build)
        self.assertIn('restore "$solution_path" \\\n  --disable-parallel', bootstrap)
        self.assertIn('-p:ChummerDesktopRuntimeIdentifiers=', debug_build)
        self.assertNotIn('--runtime "$runtime_identifier"', debug_build)
        self.assertIn('--no-restore', debug_build)
        self.assertIn('compile_check_path=', debug_build)
        self.assertIn("ChummerUseLocalCompatibilityTree=true", debug_build)
        self.assertIn("AndroidSigningKeyStore", build)
        self.assertIn("verify_release_source_graph.py", build)
        self.assertIn("ChummerUseLocalCompatibilityTree=true", build)
        self.assertIn('"chummer.android.release-source-graph/v1"', source_graph)
        self.assertIn('"google_play_upload"', source_graph)
        self.assertIn('"tester_installation"', source_graph)
        self.assertIn('"status", "--porcelain", "--untracked-files=all"', source_graph)
        self.assertIn("release source checkout is dirty", source_graph)
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
        self.assertGreaterEqual(bootstrap.count('-p:ChummerAndroidRuntimeIdentifier="$runtime_identifier"'), 2)
        self.assertGreaterEqual(bootstrap.count('-p:ChummerDesktopRuntimeIdentifiers='), 3)
        self.assertNotIn('--runtime "$runtime_identifier"', bootstrap)
        self.assertIn('compile_check_path=', bootstrap)
        self.assertIn("ChummerUseLocalCompatibilityTree=true", bootstrap)
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
            ("0.1.0-preview.9", "9"),
            module.read_project_version(PROJECT / "Chummer.Android.csproj"),
        )

    def test_store_listing_limits_and_truthful_preview_copy(self) -> None:
        listing = REPO / "play" / "listing" / "en-US"
        title = (listing / "title.txt").read_text(encoding="utf-8").strip()
        short_description = (listing / "short-description.txt").read_text(encoding="utf-8").strip()
        full_description = (listing / "full-description.txt").read_text(encoding="utf-8").strip()
        release_notes = (listing / "release-notes-9.txt").read_text(encoding="utf-8").strip()
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

    def test_native_compile_gate_includes_linked_character_service(self) -> None:
        compile_project = (
            REPO
            / "tests"
            / "Chummer.Android.Native.CompileCheck"
            / "Chummer.Android.Native.CompileCheck.csproj"
        ).read_text(encoding="utf-8")
        self.assertIn(
            "../../src/Chummer.Android/Platform/"
            "IAndroidLinkedCharacterFileService.cs",
            compile_project,
        )

    def test_phone_gear_quantity_lifecycle_is_stable_revision_bound_and_career_exact(self) -> None:
        page = (PROJECT / "Native" / "GearQuantityPage.cs").read_text(encoding="utf-8")
        editor = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        request = (presentation / "GearQuantityEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = $"gear-quantity-page-{targetToken}"',
            '$"gear-quantity-increase-{targetToken}"',
            '$"gear-quantity-reduce-{targetToken}"',
            '$"gear-quantity-split-{targetToken}"',
            '$"gear-quantity-merge-{targetToken}"',
            'AutomationId = $"gear-quantity-amount-{targetToken}"',
            'AutomationId = $"gear-quantity-merge-target-{targetToken}"',
            "reductionConfirmed = await DisplayAlertAsync",
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: $"gear-quantity-open-{lifecycle.GearId:N}"', editor)
        self.assertIn("item.GearQuantityLifecycleRequired", editor)
        self.assertIn("ApplyGearQuantityEditAsync", coordinator)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request)
        self.assertIn("Guid GearId", request)
        self.assertIn("ApplyGearQuantityEdit", mutation)
        self.assertIn("CharacterGearQuantityRules.AreIdenticalForMerge", mutation)
        self.assertIn("AppendGearPurchaseExpense", mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", presenter)
        self.assertNotIn("WeaponAccessory", page)

    def test_phone_quality_level_is_stable_revision_bound_and_create_career_exact(self) -> None:
        page = (PROJECT / "Native" / "QualityLevelPage.cs").read_text(encoding="utf-8")
        editor = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        request = (presentation / "QualityLevelEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = $"quality-level-page-{token}"',
            '$"quality-level-current-{token}"',
            '$"quality-level-value-{token}"',
            '$"quality-level-save-{token}"',
            '"Confirm Quality Level increase"',
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: $"quality-level-open-{qualityId:N}"', editor)
        self.assertIn("item.QualityLevel is not { } qualityLevel", editor)
        self.assertIn("ApplyQualityLevelEditAsync", coordinator)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request)
        self.assertIn("Guid QualityId", request)
        self.assertIn("ExpectedLevel", request)
        self.assertIn("ApplyQualityLevelEdit", mutation)
        self.assertIn("CharacterSectionService(sourceDataResolver)", mutation)
        self.assertIn("AppendFreeCareerQualityExpense", mutation)
        self.assertIn("AppendFreeCareerNegativeQualityRemovalExpense", mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", presenter)
        self.assertNotIn("GearQuantity", page)

    def test_phone_critter_power_count_is_stable_revision_bound_and_legacy_exact(self) -> None:
        page = (PROJECT / "Native" / "CritterPowerCountPage.cs").read_text(encoding="utf-8")
        editor = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        core = WORKSPACE / "chummer-core-engine"
        request = (presentation / "CritterPowerCountEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        persistence = (presentation / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterCritterPowerCountRules.cs").read_text(encoding="utf-8")
        workspace_store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = $"critter-power-count-page-{targetToken}"',
            '$"critter-power-count-toggle-{targetToken}"',
            '$"critter-power-count-save-{targetToken}"',
            '"Counts towards Critter Power limit"',
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: $"critter-power-count-open-{critterPowerId:N}"', editor)
        self.assertIn("item.CritterPowerCount", editor)
        self.assertIn("ApplyCritterPowerCountEditAsync", coordinator)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request)
        self.assertIn("Guid CritterPowerId", request)
        self.assertIn("ApplyCritterPowerCountEdit", mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", presenter)
        self.assertIn("TryCaptureRecoveryPayloadAsync", persistence)
        self.assertIn("postcommit save recovery", persistence)
        self.assertIn("WriteRecordAtomically", workspace_store)
        self.assertIn("Flush(true)", workspace_store)
        self.assertIn("LegacyDefault = true", rules)
        self.assertIn("savedValues.Count > 1", rules)
        self.assertNotIn("nudQualityLevel", page)

    def test_phone_group_name_is_revision_bound_and_distinct_from_contact_groups(self) -> None:
        page = (PROJECT / "Native" / "GroupNamePage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        core = WORKSPACE / "chummer-core-engine"
        request = (presentation / "GroupNameEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        persistence = (presentation / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterGroupNameRules.cs").read_text(encoding="utf-8")
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = "group-name-page"',
            'AutomationId = "group-name-value"',
            'AutomationId = "group-name-save"',
            "MaxLength = CharacterGroupNameRules.MaximumLength",
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: "build-group-name"', build)
        self.assertIn("new GroupNamePage", build)
        self.assertIn("ApplyGroupNameEditAsync", coordinator)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request)
        self.assertIn("ExpectedGroupName", request)
        self.assertIn('root.Elements("groupname").Take(2)', request)
        self.assertIn("ApplyGroupNameEdit", mutation)
        self.assertIn("CharacterGroupNameRules.TryValidate", mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", presenter)
        self.assertIn("TryCaptureRecoveryPayloadAsync", persistence)
        self.assertIn("WriteRecordAtomically", store)
        self.assertIn("MaximumLength = 32_767", rules)
        self.assertNotIn("Contact", request)
        self.assertNotIn("GroupMembershipEditRequest", page)

    def test_phone_tradition_name_is_custom_source_revision_and_identity_bound(self) -> None:
        page = (PROJECT / "Native" / "TraditionNamePage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        core = WORKSPACE / "chummer-core-engine"
        request = (presentation / "TraditionNameEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        persistence = (presentation / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterTraditionNameRules.cs").read_text(encoding="utf-8")
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = "tradition-name-page"',
            'AutomationId = "tradition-name-value"',
            'AutomationId = "tradition-name-save"',
            "MaxLength = CharacterTraditionNameRules.MaximumLength",
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: "build-tradition-name"', build)
        self.assertIn("new TraditionNamePage", build)
        self.assertIn("ApplyTraditionNameEditAsync", coordinator)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request)
        self.assertIn("TraditionId", request)
        self.assertIn("ExpectedTraditionName", request)
        self.assertIn('root.Elements("tradition").Take(2)', request)
        self.assertIn("CharacterTraditionNameRules.CustomMagicalTraditionSourceId", request)
        self.assertIn("ApplyTraditionNameEdit", mutation)
        self.assertIn("CharacterTraditionNameRules.TryValidate", mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", presenter)
        self.assertIn("TryCaptureRecoveryPayloadAsync", persistence)
        self.assertIn("WriteRecordAtomically", store)
        self.assertIn("616ba093-306c-45fc-8f41-0b98c8cccb46", rules)
        self.assertIn("MaximumLength = 32_767", rules)
        self.assertNotIn("cboTradition", page)
        self.assertNotIn("cboDrain", page)

    def test_phone_tradition_drain_is_source_allowlisted_revision_and_identity_bound(self) -> None:
        page = (PROJECT / "Native" / "TraditionDrainPage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        core = WORKSPACE / "chummer-core-engine"
        request = (presentation / "TraditionDrainEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        persistence = (presentation / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterTraditionDrainRules.cs").read_text(encoding="utf-8")
        resolver_contract = (core / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs").read_text(encoding="utf-8")
        resolver = (core / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs").read_text(encoding="utf-8")
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = "tradition-drain-page"',
            'AutomationId = "tradition-drain-value"',
            'AutomationId = "tradition-drain-save"',
            "CharacterTraditionDrainRules.TryValidateRequestedExpression",
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: "build-tradition-drain"', build)
        self.assertIn("new TraditionDrainPage", build)
        self.assertIn("ApplyTraditionDrainEditAsync", coordinator)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request)
        self.assertIn("TraditionId", request)
        self.assertIn("ExpectedDrainExpression", request)
        self.assertIn('root.Elements("tradition").Take(2)', request)
        self.assertIn('ReadOptionalBoolean(root, "adept")', request)
        self.assertIn('ReadOptionalBoolean(root, "magician")', request)
        self.assertIn("TryResolveTraditionDrainExpressions", resolver_contract)
        self.assertIn('TryLoadEffectiveDocument(_catalog, "traditions.xml"', resolver)
        self.assertIn('Elements("drainattributes").Take(2)', resolver)
        self.assertIn("ApplyTraditionDrainEdit", mutation)
        self.assertIn("_characterSourceDataResolver", presenter)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", presenter)
        self.assertIn("TryCaptureRecoveryPayloadAsync", persistence)
        self.assertIn("WriteRecordAtomically", store)
        self.assertIn("CharacterTraditionNameRules.CustomMagicalTraditionSourceId", rules)
        self.assertIn("adeptEnabled && !magicianEnabled", rules)
        self.assertNotIn("Entry", page)

    def test_phone_gear_name_is_stable_guid_revision_and_exact_element_bound(self) -> None:
        page = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        overview = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        request = (overview / "WorkspaceCollectionMutationRequest.cs").read_text(encoding="utf-8")
        projector = (overview / "WorkspaceCollectionEditorProjector.cs").read_text(encoding="utf-8")
        mutation = (overview / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        workspace_mutations = (overview / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(
            encoding="utf-8"
        )
        persistence = (overview / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        core = WORKSPACE / "chummer-core-engine"
        models = (core / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs").read_text(encoding="utf-8")
        sections = (core / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs").read_text(encoding="utf-8")
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        self.assertIn("WorkspaceCollectionTextField.GearName", page)
        self.assertIn('=> "gearname"', page)
        self.assertIn("WorkspacePatchCollectionItemRequest", page)
        self.assertIn("ApplyCollectionMutationAsync", coordinator)
        self.assertIn("GearName", request)
        self.assertIn("WorkspaceSetCollectionTextRequest", request)
        self.assertIn("MaximumSelectTextLength = 32_767", projector)
        self.assertIn('WorkspaceCollectionTextField.GearName => "gearName"', projector)
        self.assertIn("WorkspaceNestedCollectionKind.Gear", projector)
        self.assertIn("MaximumSelectTextLength = 32_767", mutation)
        self.assertIn("WorkspaceCollectionTextField.GearName", mutation)
        self.assertIn('return "gearname"', mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", workspace_mutations)
        self.assertIn("TryCaptureRecoveryPayloadAsync", persistence)
        self.assertIn('string GearName = ""', models)
        self.assertIn('GearName: ReadValue(item, "gearname")', sections)
        self.assertIn("WriteRecordAtomically", store)

    def test_phone_lifestyle_name_is_stable_guid_revision_and_exact_extra_bound(self) -> None:
        page = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        overview = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        request = (overview / "WorkspaceCollectionMutationRequest.cs").read_text(encoding="utf-8")
        projector = (overview / "WorkspaceCollectionEditorProjector.cs").read_text(encoding="utf-8")
        mutation = (overview / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        workspace_mutations = (overview / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(
            encoding="utf-8"
        )
        persistence = (overview / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        core = WORKSPACE / "chummer-core-engine"
        models = (core / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs").read_text(
            encoding="utf-8"
        )
        sections = (core / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs").read_text(
            encoding="utf-8"
        )
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("WorkspaceCollectionKind.Lifestyle", page)
        self.assertIn("WorkspaceCollectionTextField.CustomName", page)
        self.assertIn("WorkspaceCollectionTextField.NotesColor", page)
        self.assertIn('"Lifestyle Name"', page)
        self.assertIn('"Notes Color"', page)
        self.assertIn('=> "lifestylename"', page)
        self.assertIn('=> "notescolor"', page)
        self.assertIn("!item.CanMove && !item.CanDelete", page)
        self.assertIn("ApplyCollectionMutationAsync", coordinator)
        self.assertIn("Lifestyle", request)
        self.assertIn("WorkspaceSetCollectionTextRequest", request)
        self.assertIn("NotesColor", request)
        self.assertIn('"lifestyles" => new(key, "lifestyles", WorkspaceCollectionKind.Lifestyle)', projector)
        self.assertIn("MaximumSelectTextLength = 32_767", projector)
        self.assertIn("MaximumRichTextLength = int.MaxValue", projector)
        self.assertIn("MaximumNotesColorLength = 32", projector)
        self.assertIn("CanDelete: schema.Kind != WorkspaceCollectionKind.Lifestyle", projector)
        self.assertIn("CanMove: schema.Kind != WorkspaceCollectionKind.Lifestyle", projector)
        self.assertIn("WorkspaceCollectionKind.Lifestyle", mutation)
        self.assertIn('new(["lifestyles"], "lifestyle")', mutation)
        self.assertIn('return "extra"', mutation)
        self.assertIn('return "notesColor"', mutation)
        self.assertIn("NormalizeNotesColor", mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", workspace_mutations)
        self.assertIn("TryCaptureRecoveryPayloadAsync", persistence)
        self.assertIn("CharacterLifestyleSummary", models)
        self.assertIn('string CustomName = ""', models)
        self.assertIn('string NotesColor = ""', models)
        self.assertIn('CustomName: ReadValue(lifestyle, "extra")', sections)
        self.assertIn('NotesColor: ReadValue(lifestyle, "notesColor")', sections)
        self.assertIn("WriteRecordAtomically", store)

    def test_phone_lifestyle_intervals_preserve_exact_creation_and_career_transactions(self) -> None:
        page = (PROJECT / "Native" / "LifestyleIncrementPage.cs").read_text(encoding="utf-8")
        collection_page = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        request = (presentation / "LifestyleIncrementEditRequest.cs").read_text(encoding="utf-8")
        state = (presentation / "WorkspaceCollectionEditorState.cs").read_text(encoding="utf-8")
        projector = (presentation / "WorkspaceCollectionEditorProjector.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        core = WORKSPACE / "chummer-core-engine"
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterLifestyleIncrementRules.cs").read_text(encoding="utf-8")
        models = (core / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs").read_text(encoding="utf-8")
        sections = (core / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs").read_text(encoding="utf-8")

        self.assertIn("LifestyleIncrementPage", collection_page)
        self.assertIn('lifestyle-increments-open-', collection_page)
        self.assertIn('lifestyle-increments-set-', page)
        self.assertIn('lifestyle-increments-increase-', page)
        self.assertIn('lifestyle-increments-decrease-', page)
        self.assertIn("CharacterLifestyleIncrementRules.Quote", page)
        self.assertIn("LifestyleIncrementEditRequest", request + page + coordinator)
        self.assertIn("ExpectedContentRevision", request + presenter)
        self.assertIn("LifestyleIncrement", state + projector)
        self.assertIn("ApplyLifestyleIncrementEdit", mutation + presenter)
        self.assertIn('new XElement("nuyentype", "IncreaseLifestyle")', mutation)
        self.assertIn('new XElement("amount", amount.ToString', mutation)
        self.assertIn("CreationMinimum = 1", rules)
        self.assertIn("CreationMaximum = 100", rules)
        self.assertIn("Chummer5 intentionally does not impose a lower bound", rules)
        self.assertIn("CharacterLifestyleIncrementState", models)
        self.assertIn('ReadValue(lifestyle, "totalmonthlycost")', sections)

    def test_phone_psyche_active_is_shared_revision_bound_and_surface_exact(self) -> None:
        page = (PROJECT / "Native" / "SustainedObjectsPage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        contract = (presentation / "SustainedObjectEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        persistence = (presentation / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        core = WORKSPACE / "chummer-core-engine"
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterSustainedObjectRules.cs").read_text(encoding="utf-8")
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        self.assertIn('automationId: "build-sustained-effects"', build)
        self.assertIn('"sustained-psyche-active-magician"', page)
        self.assertIn('"sustained-psyche-active-technomancer"', page)
        self.assertIn("CharacterPsycheActiveSurface.Magician", page)
        self.assertIn("CharacterPsycheActiveSurface.Technomancer", page)
        self.assertIn("ApplyPsycheActiveEditAsync", page + coordinator + presenter)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("PsycheActiveEditRequest", contract)
        self.assertIn("ExpectedContentRevision", contract + presenter)
        self.assertIn("ProjectPsycheActiveState", contract + mutation)
        self.assertIn('SetElementValue(document.Root!, "psyche"', mutation)
        self.assertIn("CanSetPsycheActive", rules + mutation)
        self.assertIn("TryCaptureRecoveryPayloadAsync", persistence)
        self.assertIn("WriteRecordAtomically", store)

    def test_phone_manual_nuyen_is_revision_bound_source_exact_and_atomically_saved(self) -> None:
        page = (PROJECT / "Native" / "CareerManualNuyenPage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        core = WORKSPACE / "chummer-core-engine"
        request = (presentation / "CareerManualNuyenEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        persistence = (presentation / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterCareerManualNuyenRules.cs").read_text(encoding="utf-8")
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = "career-manual-nuyen-page"',
            'AutomationId = "career-manual-nuyen-amount"',
            'AutomationId = "career-manual-nuyen-percent"',
            '"career-manual-nuyen-refund"',
            '"career-manual-nuyen-exchange"',
            '"career-manual-nuyen-force-career-visible"',
            'AutomationId = "career-manual-nuyen-gain"',
            'AutomationId = "career-manual-nuyen-spend"',
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: "build-career-manual-nuyen"', build)
        self.assertIn("new CareerManualNuyenPage", build)
        self.assertIn("PrepareCareerManualNuyenEditAsync", coordinator + presenter)
        self.assertIn("ApplyCareerManualNuyenEditAsync", coordinator + presenter)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request + presenter)
        self.assertIn("CharacterCareerManualNuyenState ExpectedState", request)
        self.assertIn("TryResolveKarmaNuyenExchangeRates", request)
        self.assertIn("ApplyCareerManualNuyenEdit", mutation)
        self.assertIn("CharacterCareerManualNuyenRules.TryQuote", mutation)
        self.assertIn('EnsureElement(root, "nuyen")', mutation)
        self.assertIn('EnsureElement(root, "karma")', mutation)
        self.assertIn("InsertManualKarmaExpenseSorted", mutation)
        self.assertIn("TryBeginCaptureIntent", persistence)
        self.assertIn("WriteRecordAtomically", store)
        self.assertIn("enteredAmount * percent / 100m", rules)
        self.assertIn("decimal.ToInt32(nuyenAmount / conversionRate)", rules)

    def test_phone_nuyen_expense_edit_is_guid_revision_and_atomic_persistence_bound(self) -> None:
        page = (PROJECT / "Native" / "CareerNuyenExpensePage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        core = WORKSPACE / "chummer-core-engine"
        request = (presentation / "CareerNuyenExpenseEditRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        persistence = (presentation / "CharacterOverviewPresenter.Persistence.cs").read_text(encoding="utf-8")
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterCareerNuyenExpenseEditRules.cs").read_text(encoding="utf-8")
        store = (core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = "career-nuyen-expense-page"',
            'AutomationId = "career-nuyen-expense-picker"',
            'AutomationId = "career-nuyen-expense-amount"',
            '"career-nuyen-expense-reason"',
            'AutomationId = "career-nuyen-expense-date"',
            'AutomationId = "career-nuyen-expense-time"',
            'AutomationId = "career-nuyen-expense-save"',
            "_selected.AmountEditable",
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: "build-career-nuyen-expenses"', build)
        self.assertIn("new CareerNuyenExpensePage", build)
        self.assertIn("PrepareCareerNuyenExpenseEditAsync", coordinator + presenter)
        self.assertIn("ApplyCareerNuyenExpenseEditAsync", coordinator + presenter)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request + presenter)
        self.assertIn("CharacterCareerNuyenExpenseEntry ExpectedExpense", request)
        self.assertIn('ReadRequiredGuid(expense, "guid")', request)
        self.assertIn('ReadNuyenUndoType(expense)', request)
        self.assertIn("ApplyCareerNuyenExpenseEdit", mutation)
        self.assertIn("request.ExpectedAvailableNuyen", mutation)
        self.assertIn("request.ExpectedExpense.ExpenseId", mutation)
        self.assertIn("CharacterCareerNuyenExpenseEditRules.TryEdit", mutation)
        self.assertIn("IsAmountEditable", rules)
        self.assertIn("ManualAdd", rules)
        self.assertIn("ManualSubtract", rules)
        self.assertIn("amount - current.Amount", rules)
        self.assertIn("TryBeginCaptureIntent", persistence)
        self.assertIn("WriteRecordAtomically", store)

    def test_phone_cyberware_commerce_is_stable_revision_bound_and_source_exact(self) -> None:
        page = (PROJECT / "Native" / "CyberwareCommercePage.cs").read_text(encoding="utf-8")
        editor = (PROJECT / "Native" / "CollectionEditorPages.cs").read_text(encoding="utf-8")
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        presentation = WORKSPACE / "chummer-presentation" / "Chummer.Presentation" / "Overview"
        core = WORKSPACE / "chummer-core-engine"
        request = (presentation / "CyberwareCommerceRequest.cs").read_text(encoding="utf-8")
        mutation = (presentation / "WorkspaceXmlMutationCatalog.cs").read_text(encoding="utf-8")
        presenter = (presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs").read_text(encoding="utf-8")
        rules = (core / "Chummer.Contracts" / "Characters" / "CharacterCyberwareCommerceRules.cs").read_text(encoding="utf-8")
        section = (core / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs").read_text(encoding="utf-8")

        for token in (
            'AutomationId = $"cyberware-commerce-page-{token}"',
            '$"cyberware-commerce-grade-{token}"',
            '$"cyberware-commerce-rating-{token}"',
            '$"cyberware-commerce-refund-percent-{token}"',
            '$"cyberware-commerce-free-cost-{token}"',
            '$"cyberware-commerce-upgrade-{token}"',
            '$"cyberware-commerce-sell-{token}"',
            '"Confirm Cyberware upgrade"',
            '"Confirm Cyberware sale"',
        ):
            self.assertIn(token, page)
        self.assertIn('automationId: $"cyberware-commerce-open-{cyberwareId:N}"', editor)
        self.assertIn("CyberwareCommerceRequired", editor)
        self.assertIn("PrepareCyberwareCommerceEditAsync", coordinator)
        self.assertIn("ApplyCyberwareCommerceEditAsync", coordinator)
        self.assertIn("_presenter.SaveAsync", coordinator)
        self.assertIn("ExpectedContentRevision", request)
        self.assertIn("Guid CyberwareId", request)
        self.assertIn("string QuoteDigest", request)
        self.assertIn("ApplyCyberwareCommerceEdit", mutation)
        self.assertIn("QuoteUpgrade", mutation)
        self.assertIn("QuoteSale", mutation)
        self.assertIn('new XElement("nuyentype", "AddGear")', mutation)
        self.assertIn("ApplyWorkspaceXmlMutationAsync", presenter)
        self.assertIn("TryNormalizeRefundPercentage", rules)
        self.assertIn("TryPlanEssenceHole", rules)
        self.assertIn("Linked Capacity=[*] child", section)
        self.assertNotIn("ArmorDamage", page)

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
