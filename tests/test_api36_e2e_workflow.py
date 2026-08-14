import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "api36-editing-e2e.yml"


class Api36EditingE2EWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text = WORKFLOW.read_text(encoding="utf-8")

    def test_runs_phone_and_tablet_profiles_on_api_36(self) -> None:
        self.assertIn("api-level: 36", self.text)
        self.assertIn("profile: phone", self.text)
        self.assertIn("avd_profile: pixel_6", self.text)
        self.assertIn("profile: tablet", self.text)
        self.assertIn("avd_profile: pixel_c", self.text)
        self.assertIn("fail-fast: false", self.text)

    def test_builds_the_native_x64_candidate_once(self) -> None:
        self.assertIn("CHUMMER_ANDROID_RUNTIME_ID: android-x64", self.text)
        self.assertIn("scripts/build-debug.sh", self.text)
        self.assertIn("test \"${#apks[@]}\" -eq 1", self.text)
        self.assertIn("chummer-android-x64-debug.apk.sha256", self.text)
        self.assertIn("needs: build", self.text)

    def test_executes_the_existing_persistence_driver(self) -> None:
        self.assertIn("tests/run_api36_editing_e2e.py", self.text)
        self.assertIn('--serial emulator-5554', self.text)
        self.assertIn('--profile "$CHUMMER_E2E_PROFILE"', self.text)
        self.assertIn('--receipt "$evidence_root/receipt.json"', self.text)

    def test_actions_are_commit_pinned_and_evidence_survives_failure(self) -> None:
        self.assertNotIn("uses: actions/checkout@v", self.text)
        self.assertNotIn("uses: actions/setup-dotnet@v", self.text)
        self.assertNotIn("uses: actions/upload-artifact@v", self.text)
        self.assertNotIn("uses: actions/download-artifact@v", self.text)
        self.assertNotIn("uses: ReactiveCircus/android-emulator-runner@v", self.text)
        self.assertIn("if: ${{ always() }}", self.text)
        self.assertIn("if-no-files-found: warn", self.text)


if __name__ == "__main__":
    unittest.main()
