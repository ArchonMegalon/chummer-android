import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "api36-editing-e2e.yml"
PREVIEW_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "preview9-arm64-aab.yml"
COMPATIBILITY_GRAPH = {
    "ArchonMegalon/chummer6-ui":
        "8090e53f6dd64794145d81d7698394e4881d0c02",
    "ArchonMegalon/chummer6-core":
        "c75d68d2233af980dd8b1ef6116dcbdeefcf3c71",
    "ArchonMegalon/chummer6-hub":
        "25f4906b1b92fa286c63ec364ea33ada63ba9431",
    "ArchonMegalon/chummer6-hub-registry":
        "7b54afec574a9327616c4ad7566da3a7b6b906a5",
    "ArchonMegalon/chummer6-ui-kit":
        "d51ecd99cf72098d4adc8db0192bff7bf9fd8e61",
    "ArchonMegalon/chummer6-media-factory":
        "415c8163d3d90b1211e4014fef332bdec6d75f73",
}


class Api36EditingE2EWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text = WORKFLOW.read_text(encoding="utf-8")
        cls.preview_text = PREVIEW_WORKFLOW.read_text(encoding="utf-8")

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

    def test_full_local_compatibility_tree_is_commit_pinned(self) -> None:
        for repository, commit in COMPATIBILITY_GRAPH.items():
            with self.subTest(repository=repository):
                self.assertIn(f"repository: {repository}", self.text)
                self.assertIn(f"ref: {commit}", self.text)

    def test_preview_release_uses_the_same_compiled_compatibility_graph(self) -> None:
        for repository, commit in COMPATIBILITY_GRAPH.items():
            with self.subTest(repository=repository):
                self.assertIn(f"repository: {repository}", self.preview_text)
                self.assertIn(f"ref: {commit}", self.preview_text)

    def test_executes_the_existing_persistence_driver(self) -> None:
        runner = (
            REPO_ROOT / "scripts" / "run-api36-editing-e2e-ci.sh"
        ).read_text(encoding="utf-8")
        self.assertIn(
            "bash chummer-android/scripts/run-api36-editing-e2e-ci.sh",
            self.text,
        )
        self.assertIn("tests/run_api36_editing_e2e.py", runner)
        self.assertIn("--serial emulator-5554", runner)
        self.assertIn('--profile "$profile"', runner)
        self.assertIn('--receipt "$evidence_root/receipt.json"', runner)
        self.assertLess(
            runner.index('install -d -m 0755 "$evidence_root"'),
            runner.index("python3 chummer-android/tests/run_api36_editing_e2e.py"),
        )

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
