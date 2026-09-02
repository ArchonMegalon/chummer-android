from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "src" / "Chummer.Android"
REVIEW_RESOURCES = PROJECT / "Resources" / "Localization"


class PlayReviewSourceContractTests(unittest.TestCase):
    def test_review_strings_have_exact_nonempty_de_en_es_parity(self) -> None:
        catalogs = {}
        for language, filename in (
            ("en", "ReviewStrings.resx"),
            ("de", "ReviewStrings.de.resx"),
            ("es", "ReviewStrings.es.resx"),
        ):
            root = ET.parse(REVIEW_RESOURCES / filename).getroot()
            catalogs[language] = {
                entry.attrib["name"]: (entry.findtext("value") or "").strip()
                for entry in root.findall("data")
            }

        expected_keys = set(catalogs["en"])
        self.assertEqual({"SettingsSection", "RateOnGooglePlay", "RateOnGooglePlayDescription"}, expected_keys)
        for language, catalog in catalogs.items():
            with self.subTest(language=language):
                self.assertEqual(expected_keys, set(catalog))
                self.assertTrue(all(catalog.values()))
        self.assertEqual("Valorar Chummer en Google Play", catalogs["es"]["RateOnGooglePlay"])

    def test_official_play_review_binding_and_unchanged_flow_are_used(self) -> None:
        project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        launcher = (
            PROJECT / "Platforms" / "Android" / "AndroidPlayReviewLauncher.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('Xamarin.Google.Android.Play.Review" Version="2.0.2.9"', project)
        self.assertIn("com.google.android.play:review:2.0.2", project)
        self.assertIn("ReviewManagerFactory.Create", launcher)
        self.assertIn("RequestReviewFlow", launcher)
        self.assertIn("LaunchReviewFlow", launcher)
        self.assertIn("disclose whether", launcher)
        self.assertNotIn("DisplayAlert", launcher)
        self.assertNotIn("stars", launcher.lower())
        self.assertNotIn("opinion", launcher.lower())

    def test_current_activity_is_bound_to_maui_not_the_local_platform_namespace(self) -> None:
        launcher = (
            PROJECT / "Platforms" / "Android" / "AndroidPlayReviewLauncher.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "using MauiApplicationPlatform = Microsoft.Maui.ApplicationModel.Platform;",
            launcher,
        )
        self.assertEqual(4, launcher.count("MauiApplicationPlatform.CurrentActivity"))
        self.assertNotIn(" Platform.CurrentActivity", launcher)

    def test_policy_is_local_durable_versioned_and_install_bound(self) -> None:
        policy = (PROJECT / "Native" / "PlayReviewPolicy.cs").read_text(encoding="utf-8")
        launcher = (
            PROJECT / "Platforms" / "Android" / "AndroidPlayReviewLauncher.cs"
        ).read_text(encoding="utf-8")

        for token in (
            "TimeSpan.FromHours(1)",
            "TimeSpan.FromDays(30)",
            "ForegroundMilliseconds",
            "LastAttemptUtc",
            "LastAttemptVersion",
            "InstallIdentity",
            "Stopwatch.GetTimestamp",
            "play-review-policy.json",
            "CanonicalApplicationId",
            "GooglePlayInstallerPackage",
            "BypassEligibilityHistory",
            "SignalMeaningfulSuccess",
            "MeaningfulSuccessWindow",
        ):
            self.assertIn(token, policy + launcher)
        self.assertIn("NoBackupFilesDir", launcher)
        self.assertIn("play-review-install-id", launcher)
        for forbidden in ("RatingValue", "StarsSubmitted", "ReviewDisplayed", "ReviewSubmitted"):
            self.assertNotIn(forbidden, policy)

    def test_only_resumed_foreground_and_explicit_safe_roots_can_trigger(self) -> None:
        activity = (
            PROJECT / "Platforms" / "Android" / "MainActivity.cs"
        ).read_text(encoding="utf-8")
        safety = (PROJECT / "Native" / "PlayReviewSafety.cs").read_text(encoding="utf-8")
        page_base = (PROJECT / "Native" / "NativePageBase.cs").read_text(encoding="utf-8")
        home = (PROJECT / "Native" / "HomePage.cs").read_text(encoding="utf-8")
        more = (PROJECT / "Native" / "MorePage.cs").read_text(encoding="utf-8")
        build = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        settings = (PROJECT / "Native" / "ApplicationSettingsPage.cs").read_text(encoding="utf-8")

        self.assertIn("OnForegrounded", activity)
        self.assertIn("CheckpointForegroundUse", activity)
        self.assertIn("OnBackgrounded", activity)
        self.assertIn("protected override void OnPause()", activity)
        for token in (
            "IsExplicitSafeSurface",
            "IsRootNavigation",
            "HasModal",
            "HasActiveDialog",
            "HasUnsavedMutation",
            "HasActionInFlight",
            "HasBusyWork",
            "coordinator.State.IsDirty",
            "coordinator.State.ActiveDialog",
            "coordinator.IsBusy",
        ):
            self.assertIn(token, safety)
        self.assertIn("IPlayReviewSafeSurface", home)
        self.assertIn("IPlayReviewSafeSurface", more)
        self.assertNotIn("IPlayReviewSafeSurface", build)
        self.assertNotIn("IPlayReviewSafeSurface", settings)
        self.assertIn("SignalMeaningfulSuccess", page_base)
        self.assertIn("signalMeaningfulSuccess: false", page_base)
        self.assertIn("PlayReviewMeaningfulState", page_base)
        self.assertIn("before != CapturePlayReviewMeaningfulState()", page_base)

    def test_release_package_installer_debug_override_and_kill_switch_fail_closed(self) -> None:
        project = (PROJECT / "Chummer.Android.csproj").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        activity = (
            PROJECT / "Platforms" / "Android" / "MainActivity.cs"
        ).read_text(encoding="utf-8")
        policy = (PROJECT / "Native" / "PlayReviewPolicy.cs").read_text(encoding="utf-8")

        self.assertIn('ApplicationId>com.myexternalbrain.chummer<', project)
        self.assertIn("com.myexternalbrain.chummer", policy)
        self.assertIn("com.android.vending", policy)
        self.assertIn("IsReleaseBuild", policy)
        self.assertIn("#if DEBUG", activity)
        self.assertIn("DEBUG_PLAY_REVIEW", activity)
        self.assertIn("GetBooleanExtra", activity)
        self.assertIn("ChummerPlayReviewEnabled", project)
        self.assertIn("CHUMMER_DISABLE_PLAY_REVIEW", program)

    def test_manual_settings_action_is_localized_accessible_and_independent(self) -> None:
        settings = (PROJECT / "Native" / "ApplicationSettingsPage.cs").read_text(encoding="utf-8")
        theme = (PROJECT / "Native" / "NativeTheme.cs").read_text(encoding="utf-8")
        launcher = (
            PROJECT / "Platforms" / "Android" / "AndroidPlayReviewLauncher.cs"
        ).read_text(encoding="utf-8")
        english = (
            PROJECT / "Resources" / "Localization" / "ReviewStrings.resx"
        ).read_text(encoding="utf-8")
        german = (
            PROJECT / "Resources" / "Localization" / "ReviewStrings.de.resx"
        ).read_text(encoding="utf-8")

        self.assertIn('automationId: "settings-rate-on-google-play"', settings)
        self.assertIn("PlayReviewStrings.RateOnGooglePlay", settings)
        self.assertIn("SemanticProperties.SetDescription", theme)
        self.assertIn("market://details?id=", launcher)
        self.assertIn("https://play.google.com/store/apps/details?id=", launcher)
        self.assertIn("PlayReviewPolicy.CanonicalApplicationId", launcher)
        self.assertIn("Rate Chummer on Google Play", english)
        self.assertIn("Chummer bei Google Play bewerten", german)

    def test_official_fake_manager_is_pinned_for_non_play_tests(self) -> None:
        probe = (
            REPO
            / "tests"
            / "Chummer.Android.PlayReview.BindingCompileCheck"
            / "CompileStubs.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("Google.Android.Play.Core.Review.Testing", probe)
        self.assertIn("new FakeReviewManager(context)", probe)

    def test_real_ui_proof_is_documented_as_internal_track_only(self) -> None:
        release = (REPO / "docs" / "PLAY_RELEASE.md").read_text(encoding="utf-8")
        self.assertIn("Real UI proof is internal-track-only", release)
        self.assertIn("not evidence that the card appeared", release)
        self.assertIn("was submitted.", release)
        self.assertIn("Support and product-feedback channels are separate", release)


if __name__ == "__main__":
    unittest.main()
