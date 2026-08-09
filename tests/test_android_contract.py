import json
import re
import struct
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
WORKSPACE = REPO.parent
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
        self.assertIn("<AndroidPackageFormats Condition=\"'$(Configuration)' == 'Release'\">aab</AndroidPackageFormats>", project)
        self.assertIn("<EmbedAssembliesIntoApk Condition=\"'$(Configuration)' == 'Debug'\">true</EmbedAssembliesIntoApk>", project)

    def test_android_uses_play_updates_and_verified_links(self) -> None:
        system_service = (PROJECT / "Platforms" / "Android" / "AndroidSystemService.cs").read_text(encoding="utf-8")
        activity = (PROJECT / "Platforms" / "Android" / "MainActivity.cs").read_text(encoding="utf-8")
        self.assertIn("market://details", system_service)
        self.assertNotIn("DesktopUpdateRuntime", "".join(p.read_text(encoding="utf-8") for p in PROJECT.rglob("*.cs")))
        self.assertIn('DataHost = "chummer.run"', activity)
        self.assertIn("AutoVerify = true", activity)

    def test_android_handoffs_use_canonical_public_routes(self) -> None:
        routes = (PROJECT / "Platform" / "ChummerWebRoutes.cs").read_text(encoding="utf-8")
        host = (PROJECT / "Components" / "AndroidAppHost.razor").read_text(encoding="utf-8")
        public_controller = (
            WORKSPACE / "chummer.run-services" / "Chummer.Run.Api" / "Controllers" / "PublicLandingController.cs"
        ).read_text(encoding="utf-8")
        self.assertIn('AccountAccess = "/account/access"', routes)
        self.assertNotIn("/account/devices", routes + host)
        for route in ("/gm", "/organizers", "/play", "/account/delete"):
            self.assertIn(route, public_controller)
        self.assertIn("ChummerWebRoutes.CampaignRoster", host)
        self.assertIn("ChummerWebRoutes.RulesetStudio", host)

    def test_workbench_output_is_routed_to_native_android(self) -> None:
        interop = (PROJECT / "wwwroot" / "js" / "android-interop.js").read_text(encoding="utf-8")
        system_service = (PROJECT / "Platforms" / "Android" / "AndroidSystemService.cs").read_text(encoding="utf-8")
        self.assertIn("downloads.downloadBase64", interop)
        self.assertIn("downloads.saveRecoveryStream", interop)
        self.assertIn("exports.downloadBase64", interop)
        self.assertIn("prints.openBase64", interop)
        self.assertIn("PdfFilePrintDocumentAdapter", system_service)
        self.assertIn("PrintManager", system_service)
        self.assertNotIn("PrintCurrentViewAsync", system_service)

    def test_shared_workbench_browser_runtimes_are_loaded(self) -> None:
        index = (PROJECT / "wwwroot" / "index.html").read_text(encoding="utf-8")
        interop = (PROJECT / "wwwroot" / "js" / "android-interop.js").read_text(encoding="utf-8")
        self.assertIn("build-pwa-layout.js", index)
        self.assertIn("build-pwa-integrity.js", index)
        self.assertIn("chummerDialogs", interop)
        self.assertIn("restorePendingDialogScroll", interop)

    def test_android_document_picker_imports_into_shared_workbench(self) -> None:
        host = (PROJECT / "Components" / "AndroidAppHost.razor").read_text(encoding="utf-8")
        service = (PROJECT / "Platforms" / "Android" / "AndroidDocumentService.cs").read_text(encoding="utf-8")
        shell = (
            WORKSPACE / "chummer-presentation" / "Chummer.Blazor" / "Components" / "Layout" / "DesktopShell.razor.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("DocumentInbox.Add(document)", host)
        self.assertIn("OpenInputStream", service)
        self.assertIn("MaxDocumentBytes", service)
        self.assertIn("CryptographicOperations.ZeroMemory", service)
        self.assertIn("IWorkbenchExternalDocumentInbox", shell)
        self.assertIn("ImportExternalDocumentsAsync", shell)
        self.assertIn("document.Content.LongLength > MaxImportBytes", shell)

    def test_android_shell_has_direct_new_runner_and_durable_feedback(self) -> None:
        host = (PROJECT / "Components" / "AndroidAppHost.razor").read_text(encoding="utf-8")
        state = (PROJECT / "Platform" / "AndroidAppState.cs").read_text(encoding="utf-8")
        self.assertIn('NewCharacterCommandId = "new_character"', host)
        self.assertIn("ExecuteCommandFromSurfaceAsync(commandId)", host)
        self.assertIn("Navigate(AndroidDestination.Workbench, clearMessage: false)", host)
        self.assertIn("Opening {document.DisplayName}", host)
        self.assertIn("public void SetMessage", state)
        self.assertIn("DestinationPreferenceKey", state)

    def test_android_shell_accessibility_and_output_copy_are_polished(self) -> None:
        host = (PROJECT / "Components" / "AndroidAppHost.razor").read_text(encoding="utf-8")
        css = (PROJECT / "wwwroot" / "css" / "android.css").read_text(encoding="utf-8")
        index = (PROJECT / "wwwroot" / "index.html").read_text(encoding="utf-8")
        self.assertIn("android-skip-link", host)
        self.assertIn('aria-current="@AriaCurrent', host)
        self.assertIn("aria-live=\"polite\"", host)
        self.assertIn(":focus-visible", css)
        self.assertIn("prefers-reduced-motion", css)
        self.assertIn("role=\"status\"", index)
        self.assertNotIn("Print current view", host)
        self.assertIn("File › Print or File › Export", host)

    def test_blazor_workbench_is_consumed_as_a_component_library(self) -> None:
        android_project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        host = (PROJECT / "Components" / "AndroidAppHost.razor").read_text(encoding="utf-8")
        mobile_project = (
            WORKSPACE / "chummer-presentation" / "Chummer.Blazor" / "Chummer.Blazor.Mobile.csproj"
        ).read_text(encoding="utf-8")
        self.assertIn("Chummer.Blazor.Mobile.csproj", android_project)
        self.assertIn("<AssemblyName>Chummer.Blazor.Mobile</AssemblyName>", mobile_project)
        self.assertIn('<Content Include="Components/**/*.razor" Exclude="Components/App.razor" />', mobile_project)
        self.assertNotIn("Chummer.Workspaces.Postgres", mobile_project)
        self.assertIn("<DesktopShell", host)
        self.assertNotIn("<DesktopAppHost />", host)

    def test_remote_coach_http_client_is_registered_after_local_runtime(self) -> None:
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        runtime_registration = program.index("AddChummerLocalRuntimeClient")
        http_registration = program.index("AddSingleton(new HttpClient")
        coach_registration = program.index("AddSingleton<IWorkbenchCoachApiClient")
        self.assertLess(runtime_registration, http_registration)
        self.assertLess(http_registration, coach_registration)

    def test_no_signing_secret_or_broad_provider_file_is_tracked(self) -> None:
        forbidden_suffixes = {".jks", ".keystore", ".p12"}
        for path in REPO.rglob("*"):
            if path.is_file():
                self.assertNotIn(path.suffix.lower(), forbidden_suffixes)
                self.assertNotEqual(path.name, "google-services.json")

    def test_release_automation_is_fail_closed(self) -> None:
        build = (REPO / "scripts" / "build-release.sh").read_text(encoding="utf-8")
        provision = (REPO / "scripts" / "provision-upload-key.sh").read_text(encoding="utf-8")
        validate = (REPO / "scripts" / "validate-aab.sh").read_text(encoding="utf-8")
        inspect = (REPO / "scripts" / "inspect_aab.py").read_text(encoding="utf-8")
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
        self.assertIn("Signed releases require CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH", build)
        self.assertIn("CHUMMER_JARSIGNER", build)
        self.assertIn("CHUMMER_BUNDLETOOL_JAR", validate)
        self.assertIn("bundletool validation passed", validate)
        self.assertIn("-verify -certs", validate)
        self.assertIn("AAB signer does not match", validate)
        self.assertIn("ALLOWED_PERMISSIONS", inspect)
        self.assertIn('native_abis == {"arm64-v8a"}', inspect)

    def test_store_listing_limits_and_truthful_preview_copy(self) -> None:
        listing = REPO / "play" / "listing" / "en-US"
        title = (listing / "title.txt").read_text(encoding="utf-8").strip()
        short_description = (listing / "short-description.txt").read_text(encoding="utf-8").strip()
        full_description = (listing / "full-description.txt").read_text(encoding="utf-8").strip()
        release_notes = (listing / "release-notes-1.txt").read_text(encoding="utf-8").strip()
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
        self.assertTrue(all(self._png_header(path)[:2] == (1080, 2400) for path in phones))
        self.assertTrue(all(self._png_header(path)[:2] == (1440, 2560) for path in tablets))

    def test_android_lifecycle_and_sensitive_print_cleanup_are_explicit(self) -> None:
        activity = (PROJECT / "Platforms" / "Android" / "MainActivity.cs").read_text(encoding="utf-8")
        state = (PROJECT / "Platform" / "AndroidAppState.cs").read_text(encoding="utf-8")
        print_service = (PROJECT / "Platforms" / "Android" / "AndroidSystemService.cs").read_text(encoding="utf-8")
        broker = (PROJECT / "Platforms" / "Android" / "DocumentIntentBroker.cs").read_text(encoding="utf-8")
        self.assertIn("TryNavigateBack", activity)
        self.assertIn("RegisterOnBackInvokedCallback", activity)
        self.assertIn("EnableOnBackInvokedCallback = true", activity)
        self.assertIn("TryNavigateBack", state)
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
