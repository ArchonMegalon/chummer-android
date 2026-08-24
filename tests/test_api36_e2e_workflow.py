import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "api36-editing-e2e.yml"
PREVIEW_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "preview9-arm64-aab.yml"
COMPATIBILITY_GRAPH = {
    "ArchonMegalon/chummer6-ui":
        "e906ec909d337b7a907ba7ae8c526c3aad89a1e3",
    "ArchonMegalon/chummer6-core":
        "e9874a31d8d25b98dd196dd629c423e9a9c39297",
    "ArchonMegalon/chummer6-hub":
        "d29a880f624ec94aabedd0c2901ae8fed2f93ed4",
    "ArchonMegalon/chummer6-hub-registry":
        "af9a7e19c3bf331e96411dfb8f9e7820a98cab29",
    "ArchonMegalon/chummer6-ui-kit":
        "d51ecd99cf72098d4adc8db0192bff7bf9fd8e61",
    "ArchonMegalon/chummer6-media-factory":
        "415c8163d3d90b1211e4014fef332bdec6d75f73",
}
INVENTORY_AUTHORITIES = {
    "ArchonMegalon/chummer6-design":
        "a833259208c92e75620850f104bff8718077e0d3",
    "ArchonMegalon/chummer5a":
        "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3",
}


class Api36EditingE2EWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text = WORKFLOW.read_text(encoding="utf-8")
        cls.preview_text = PREVIEW_WORKFLOW.read_text(encoding="utf-8")

    def test_runs_only_the_phone_beta_profile_on_api_36(self) -> None:
        self.assertIn("api-level: 36", self.text)
        self.assertIn("CHUMMER_E2E_PROFILE: phone", self.text)
        self.assertIn("profile: pixel_6", self.text)
        self.assertNotIn("profile: pixel_c", self.text)
        self.assertNotIn("matrix.profile", self.text)
        self.assertIn("launches only pixel_6", self.text)
        self.assertIn("makes no tablet-readiness claim", self.text)

    def test_builds_the_native_x64_candidate_once(self) -> None:
        self.assertIn("CHUMMER_ANDROID_RUNTIME_ID: android-x64", self.text)
        self.assertIn("scripts/build-debug.sh", self.text)
        self.assertIn("test \"${#apks[@]}\" -eq 1", self.text)
        self.assertIn("chummer-android-x64-debug.apk.sha256", self.text)
        seal_start = self.text.index("Seal the unique signed debug APK")
        seal_end = self.text.index("Verify canonical content in the exact signed APK")
        seal = self.text[seal_start:seal_end]
        self.assertIn('cd "$RUNNER_TEMP/chummer-android-apk"', seal)
        self.assertIn(
            "sha256sum chummer-android-x64-debug.apk \\\n              >chummer-android-x64-debug.apk.sha256",
            seal,
        )
        self.assertIn(
            "sha256sum --check chummer-android-x64-debug.apk.sha256",
            seal,
        )
        self.assertNotIn(
            'sha256sum \\\n            "$RUNNER_TEMP/chummer-android-apk/chummer-android-x64-debug.apk"',
            seal,
        )
        content_check = "python3 chummer-android/scripts/verify_android_content_bundle.py"
        self.assertEqual(2, self.text.count(content_check))
        self.assertEqual(2, self.text.count("--core-root chummer-core-engine"))
        self.assertIn(
            '--apk "$RUNNER_TEMP/chummer-android-apk/chummer-android-x64-debug.apk"',
            self.text,
        )
        self.assertIn(
            '--receipt "$RUNNER_TEMP/chummer-android-apk/chummer-android-content-bundle-receipt.json"',
            self.text,
        )
        self.assertLess(
            self.text.index("Seal the unique signed debug APK"),
            self.text.rindex(content_check),
        )
        self.assertLess(
            self.text.rindex(content_check),
            self.text.index("Upload the exact APK under test"),
        )
        self.assertLess(
            self.text.index(content_check),
            self.text.index("Install the governed .NET SDK"),
        )
        self.assertIn("needs: build", self.text)

    def test_full_local_compatibility_tree_is_commit_pinned(self) -> None:
        for repository, commit in COMPATIBILITY_GRAPH.items():
            with self.subTest(repository=repository):
                self.assertIn(f"repository: {repository}", self.text)
                self.assertIn(f"ref: {commit}", self.text)

    def test_phone_path_fails_closed_on_stale_inventory(self) -> None:
        for repository, commit in INVENTORY_AUTHORITIES.items():
            with self.subTest(repository=repository):
                self.assertIn(f"repository: {repository}", self.text)
                self.assertIn(f"ref: {commit}", self.text)

        check = "python3 scripts/materialize_chummer5_editability_inventory.py --check"
        self.assertEqual(1, self.text.count(check))
        settings_check = (
            "python3 scripts/materialize_chummer5_character_settings_contract.py --check"
        )
        self.assertEqual(1, self.text.count(settings_check))
        self.assertIn("CHUMMER_COMPLETE_ROOT: ${{ github.workspace }}", self.text)
        self.assertIn("CHUMMER5A_ROOT: ${{ github.workspace }}/chummer5a", self.text)
        self.assertLess(self.text.index(check), self.text.index("actions/setup-dotnet@"))
        self.assertLess(self.text.index(settings_check), self.text.index("actions/setup-dotnet@"))
        self.assertLess(self.text.index(check), self.text.index(settings_check))
        self.assertLess(self.text.index(check), self.text.index("run: scripts/build-debug.sh"))
        self.assertIn("needs: build", self.text)

    def test_inventory_inputs_trigger_the_phone_gate(self) -> None:
        self.assertEqual(
            2,
            self.text.count(
                '"docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"'
            ),
        )
        self.assertEqual(
            2,
            self.text.count('"docs/CHUMMER5_CHARACTER_SETTINGS_CONTRACT.generated.json"'),
        )
        self.assertEqual(2, self.text.count('"docs/editability-evidence/**"'))
        self.assertEqual(2, self.text.count('"scripts/**"'))

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
        self.assertEqual(1, runner.count("tests/run_api36_editing_e2e.py"))
        self.assertIn("--serial emulator-5554", runner)
        self.assertIn('--profile "$profile"', runner)
        self.assertIn('--receipt "$evidence_root/receipt.json"', runner)
        self.assertNotIn('--journey contact-pet', runner)
        self.assertNotIn("contact_pet_root", runner)
        standalone_driver = (
            REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
        ).read_text(encoding="utf-8")
        self.assertIn('"contact-pet"', standalone_driver)
        self.assertTrue(
            (REPO_ROOT / "tests" / "fixtures" / "creation-contact-pet-e2e.chum5").is_file()
        )
        self.assertIn('if [[ "$profile" != "phone" ]]; then', runner)
        self.assertIn("tablet beta proof is deferred", runner)
        self.assertNotIn('phone|tablet', runner)
        self.assertIn("tests/run_api36_creation_prerequisite_e2e.py", runner)
        self.assertIn('--evidence "$prerequisite_root/screenshots"', runner)
        self.assertIn('--receipt "$prerequisite_root/receipt.json"', runner)
        self.assertIn("tests/run_api36_career_active_skill_advance_e2e.py", runner)
        self.assertIn('--workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}"', runner)
        self.assertIn('--evidence "$active_skill_root/screenshots"', runner)
        self.assertIn('--receipt "$active_skill_root/receipt.json"', runner)
        self.assertLess(
            runner.index('install -d -m 0755 "$evidence_root"'),
            runner.index("python3 chummer-android/tests/run_api36_editing_e2e.py"),
        )
        full = runner.index("python3 chummer-android/tests/run_api36_editing_e2e.py")
        prerequisite = runner.index("tests/run_api36_creation_prerequisite_e2e.py")
        active_skill = runner.index("tests/run_api36_career_active_skill_advance_e2e.py")
        self.assertLess(full, prerequisite)
        self.assertLess(prerequisite, active_skill)

    def test_downloaded_artifact_verifies_the_portable_apk_seal_before_emulation(self) -> None:
        resolve = self.text.index("Resolve the authoritative APK artifact")
        download = self.text.index("Download the exact APK under test")
        verify = self.text.index("Verify the portable downloaded APK seal")
        emulator = self.text.index("Enable KVM for the disposable emulator")

        self.assertLess(resolve, download)
        self.assertLess(download, verify)
        self.assertLess(verify, emulator)
        resolve_block = self.text[resolve:download]
        self.assertIn("actions: read", self.text)
        self.assertIn("GH_TOKEN: ${{ github.token }}", resolve_block)
        self.assertIn(
            "actions/runs/${GITHUB_RUN_ID}/artifacts?per_page=100",
            resolve_block,
        )
        self.assertIn(
            "actions/runs/${GITHUB_RUN_ID}/jobs?filter=all&per_page=100",
            resolve_block,
        )
        self.assertIn('.name == "Build x64 native APK"', resolve_block)
        self.assertIn('.run_attempt == $attempt', resolve_block)
        self.assertIn('.conclusion == "success"', resolve_block)
        self.assertIn("artifact_attempt > GITHUB_RUN_ATTEMPT", resolve_block)
        self.assertIn("successful_builds != 1", resolve_block)
        self.assertIn("duplicate artifacts exist", resolve_block)
        self.assertIn("no non-expired APK artifact", resolve_block)
        download_block = self.text[download:verify]
        self.assertIn(
            "artifact-ids: ${{ steps.authoritative-apk.outputs.artifact-id }}",
            download_block,
        )
        self.assertIn("merge-multiple: true", download_block)
        self.assertNotIn("github.run_attempt", download_block)
        verify_block = self.text[verify:emulator]
        self.assertIn(
            "working-directory: ${{ runner.temp }}/chummer-android-apk",
            verify_block,
        )
        self.assertIn(
            "run: sha256sum --check chummer-android-x64-debug.apk.sha256",
            verify_block,
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
