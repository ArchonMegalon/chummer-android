import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "api36-editing-e2e.yml"
PREVIEW_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "preview9-arm64-aab.yml"
BUILD_SCRIPT = REPO_ROOT / "scripts" / "build-debug.sh"
GITIGNORE = REPO_ROOT / ".gitignore"
COMPATIBILITY_GRAPH = {
    "ArchonMegalon/chummer6-ui":
        "1438978f6f883be321c62de69165c9216e10e011",
    "ArchonMegalon/chummer6-core":
        "febd698752e195dceef79fbc3f83dc971564fe00",
    "ArchonMegalon/chummer6-hub":
        "8cc22cb6fdf9bdf2af3c390125f7a88de90700b3",
    "ArchonMegalon/chummer6-hub-registry":
        "af9a7e19c3bf331e96411dfb8f9e7820a98cab29",
    "ArchonMegalon/chummer6-ui-kit":
        "d51ecd99cf72098d4adc8db0192bff7bf9fd8e61",
    "ArchonMegalon/chummer6-media-factory":
        "415c8163d3d90b1211e4014fef332bdec6d75f73",
}
INVENTORY_AUTHORITIES = {
    "ArchonMegalon/chummer6-design":
        "c60c93f635e371d784812140f5d5181d1d954ae2",
    "ArchonMegalon/chummer5a":
        "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3",
}


class Api36EditingE2EWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text = WORKFLOW.read_text(encoding="utf-8")
        cls.preview_text = PREVIEW_WORKFLOW.read_text(encoding="utf-8")
        cls.build_script_text = BUILD_SCRIPT.read_text(encoding="utf-8")
        cls.gitignore_text = GITIGNORE.read_text(encoding="utf-8")

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
        self.assertIn(
            "CHUMMER_CORE_ENGINE_ROOT: ${{ github.workspace }}/chummer-core-engine",
            self.text,
        )
        self.assertIn("scripts/build-debug.sh", self.text)
        self.assertIn("test \"${#apks[@]}\" -eq 1", self.text)
        self.assertIn("chummer-android-x64-debug.apk.sha256", self.text)
        seal_start = self.text.index("Seal the unique signed debug APK")
        seal_end = self.text.index("Verify canonical content in the exact signed APK")
        seal = self.text[seal_start:seal_end]
        self.assertIn('cd "$RUNNER_TEMP/chummer-android-apk"', seal)
        self.assertIn(
            'apk_sha256="$(sha256sum chummer-android-x64-debug.apk | cut -d \' \' -f 1)"',
            seal,
        )
        self.assertIn('echo "apk-sha256=$apk_sha256"', seal)
        self.assertIn("artifact-name=chummer-android-api36-x64-debug-", seal)
        self.assertIn('echo "artifact-attempt=${GITHUB_RUN_ATTEMPT}"', seal)
        self.assertIn(
            "sha256sum --check chummer-android-x64-debug.apk.sha256",
            seal,
        )
        self.assertNotIn(
            'sha256sum \\\n            "$RUNNER_TEMP/chummer-android-apk/chummer-android-x64-debug.apk"',
            seal,
        )
        content_check = "python3 chummer-android/scripts/verify_android_content_bundle.py"
        self.assertEqual(3, self.text.count(content_check))
        self.assertEqual(2, self.text.count("--core-root chummer-core-content"))
        x64_content = self.text[
            self.text.index("Verify canonical content in the exact signed APK") :
            self.text.index("Upload the exact APK under test")
        ]
        self.assertEqual(1, x64_content.count(content_check))
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
            self.text.index(content_check, self.text.index("Seal the unique signed debug APK")),
        )
        self.assertLess(
            self.text.index(content_check, self.text.index("Seal the unique signed debug APK")),
            self.text.index("Upload the exact APK under test"),
        )
        self.assertLess(
            self.text.index(content_check),
            self.text.index("Install the governed .NET SDK"),
        )
        self.assertIn("needs: build", self.text)

    def test_local_compatibility_restore_preserves_tracked_package_locks(self) -> None:
        pre_build_clean_step = (
            "Verify clean Android source before local-compatibility restores"
        )
        post_x64_clean_step = (
            "Verify clean Android source after x64 local-compatibility restore"
        )
        self.assertEqual(1, self.text.count(pre_build_clean_step))
        self.assertEqual(1, self.text.count(post_x64_clean_step))
        self.assertEqual(
            2,
            self.text.count(
                "git -C chummer-android status --porcelain=v1 --untracked-files=no"
            ),
        )
        self.assertIn(
            'git -C chummer-android status --porcelain=v1 --untracked-files=no',
            self.text,
        )
        self.assertLess(
            self.text.index(pre_build_clean_step),
            self.text.index("Build the emulator APK and native compile gate"),
        )
        self.assertLess(
            self.text.index("Build the emulator APK and native compile gate"),
            self.text.index(post_x64_clean_step),
        )
        self.assertLess(
            self.text.index(post_x64_clean_step),
            self.text.index("Seal the unique signed debug APK"),
        )
        self.assertIn(
            '"-p:RestorePackagesWithLockFile=true"',
            self.build_script_text,
        )
        self.assertIn(
            '"-p:NuGetLockFilePath=obj/chummer.local-compat.${runtime_identifier}.packages.lock.json"',
            self.build_script_text,
        )
        self.assertNotIn(
            "src/Chummer.Android/packages.lock.json",
            self.build_script_text,
        )
        self.assertNotIn(
            "tests/Chummer.Android.Native.CompileCheck/packages.lock.json",
            self.build_script_text,
        )
        self.assertIn("obj/", self.gitignore_text.splitlines())
        self.assertNotIn("git restore", self.build_script_text)

    def test_full_local_compatibility_tree_is_commit_pinned(self) -> None:
        for repository, commit in COMPATIBILITY_GRAPH.items():
            with self.subTest(repository=repository):
                self.assertIn(f"repository: {repository}", self.text)
                self.assertIn(f"ref: {commit}", self.text)

    def test_large_dependencies_are_sparse_without_weakening_commit_pins(self) -> None:
        expected_sparse_paths = (
            "Chummer.Desktop.Runtime",
            "Chummer.Presentation",
            "Chummer.Campaign.Contracts",
            "Chummer.Play.Contracts",
            "Chummer.Run.Contracts",
            "Chummer.Hub.Registry.Contracts",
            "src/Chummer.Ui.Kit",
            "src/Chummer.Media.Contracts",
            "products/chummer",
            "scripts/ai",
            "Chummer",
            "Plugins/ChummerHub.Client/UI",
            "Translator",
            "CrashHandler",
            "ChummerDataViewer",
        )
        for path in expected_sparse_paths:
            with self.subTest(path=path):
                self.assertIn(path, self.text)

        hub_checkout = self.text[
            self.text.index("Check out the pinned Hub contract dependencies") :
            self.text.index("Check out the pinned registry contract dependency")
        ]
        self.assertIn("sparse-checkout:", hub_checkout)
        self.assertNotIn("Chummer.Run.Api", hub_checkout)
        self.assertIn("fetch-depth: 1", hub_checkout)

        presentation_checkout = self.text[
            self.text.index("Check out the pinned presentation dependency") :
            self.text.index("Check out the pinned engine dependency")
        ]
        self.assertIn("Chummer.Presentation", presentation_checkout)
        self.assertIn("Chummer.Desktop.Runtime", presentation_checkout)
        self.assertIn("Chummer.Tests", presentation_checkout)
        self.assertIn("\n            Chummer\n", presentation_checkout)

        content_checkout = self.text[
            self.text.index("Check out the pinned canonical Android content source") :
            self.text.index("Check out the pinned Hub contract dependencies")
        ]
        self.assertIn("repository: ArchonMegalon/chummer6-core", content_checkout)
        self.assertIn(
            "ref: 3260ac73714d8b001a3599d6776196e394dc6c35",
            content_checkout,
        )
        self.assertIn("path: chummer-core-content", content_checkout)
        self.assertIn("persist-credentials: false", content_checkout)
        self.assertIn("fetch-depth: 1", content_checkout)
        self.assertIn("sparse-checkout:", content_checkout)
        self.assertIn("Chummer/data", content_checkout)
        self.assertIn("Chummer/lang", content_checkout)

    def test_preview_source_compatibility_checkout_includes_run_hub_closure(self) -> None:
        for path in (
            "Chummer.Run.Hub.Contracts",
            "Chummer.Run.Hub",
            "eng/shared/ConfinedAtomicFile.cs",
        ):
            with self.subTest(path=path):
                self.assertIn(path, self.preview_text)
        self.assertIn(
            "-p:ChummerCompatibilityRoot=${{ github.workspace }}/",
            self.preview_text,
        )
        self.assertIn(
            "-p:ChummerLocalRunHubContractsProject=${{ github.workspace }}/chummer.run-services/Chummer.Run.Hub.Contracts/Chummer.Run.Hub.Contracts.csproj",
            self.preview_text,
        )
        self.assertIn(
            "-p:ChummerLocalRunHubProject=${{ github.workspace }}/chummer.run-services/Chummer.Run.Hub/Chummer.Run.Hub.csproj",
            self.preview_text,
        )

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

    def test_sr5_table_wizard_development_journey_is_recognized_without_release_claim(
        self,
    ) -> None:
        payload = json.loads(
            (
                REPO_ROOT
                / "docs"
                / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"
            ).read_text(encoding="utf-8")
        )
        recognition = payload["generationInputs"]["api36JourneyRecognition"]
        journey_id = "sr5-table-wizard-before-run-playtime"
        self.assertEqual(journey_id, recognition["journeyId"])
        self.assertEqual("sr5-career/table", recognition["parentCareerLane"])
        self.assertEqual(
            ["sr5-career/before-run", "sr5-career/playtime"],
            recognition["routes"],
        )
        self.assertEqual("recognized", recognition["recognitionStatus"])
        self.assertEqual("not_executed", recognition["executionStatus"])
        self.assertIsNone(recognition["matrixJourney"])
        self.assertFalse(recognition["releaseClaim"])
        self.assertEqual(0, recognition["completionCountContribution"])

        runner = (
            REPO_ROOT / "scripts" / "run-api36-editing-e2e-ci.sh"
        ).read_text(encoding="utf-8")
        aggregate = (
            REPO_ROOT / "scripts" / "verify-api36-editing-e2e-aggregate.py"
        ).read_text(encoding="utf-8")
        finalizer = (
            REPO_ROOT / "scripts" / "finalize-api36-e2e-journey-receipt.py"
        ).read_text(encoding="utf-8")
        for authority in (self.text, runner, aggregate, finalizer):
            with self.subTest(authority=authority[:40]):
                self.assertNotIn(journey_id, authority)

        contextual = payload["generationInputs"]["contextualMutationJourneyRecognition"]
        contextual_id = "sr5-downtime-playtime-typed-transactions"
        self.assertEqual(contextual_id, contextual["journeyId"])
        self.assertEqual(
            ["sr5-career/downtime", "sr5-career/playtime"],
            contextual["routes"],
        )
        self.assertEqual("not_executed", contextual["executionStatus"])
        self.assertIsNone(contextual["matrixJourney"])
        self.assertFalse(contextual["releaseClaim"])
        self.assertEqual(0, contextual["completionCountContribution"])
        for authority in (self.text, runner, aggregate, finalizer):
            with self.subTest(contextual_authority=authority[:40]):
                self.assertNotIn(contextual_id, authority)

    def test_phone_path_validates_the_pinned_phone_beta_contract(self) -> None:
        check = "python3 chummer-design/scripts/ai/validate_android_phone_beta_contract.py"
        self.assertEqual(1, self.text.count(check))
        self.assertLess(self.text.index(check), self.text.index("actions/setup-dotnet@"))
        self.assertLess(self.text.index(check), self.text.index("run: scripts/build-debug.sh"))

    def test_every_pull_request_runs_the_phone_gate_while_push_stays_bounded(self) -> None:
        trigger_block = self.text[self.text.index("on:\n") : self.text.index("permissions:\n")]
        self.assertIn("  pull_request:\n  push:\n", trigger_block)
        self.assertNotIn("  pull_request:\n    paths:", trigger_block)
        self.assertIn("  push:\n    branches:\n      - main\n    paths:\n", trigger_block)
        self.assertEqual(
            1,
            self.text.count(
                '"docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"'
            ),
        )
        self.assertEqual(
            1,
            self.text.count('"docs/CHUMMER5_CHARACTER_SETTINGS_CONTRACT.generated.json"'),
        )
        self.assertEqual(1, self.text.count('"docs/editability-evidence/**"'))
        self.assertEqual(1, self.text.count('"scripts/**"'))

    def test_preview_release_remains_independently_commit_pinned(self) -> None:
        self.assertNotIn("uses: actions/checkout@v", self.preview_text)
        for repository in COMPATIBILITY_GRAPH:
            with self.subTest(repository=repository):
                self.assertIn(f"repository: {repository}", self.preview_text)

    def test_build_emits_a_non_attested_arm64_hosted_debug_candidate(self) -> None:
        self.assertIn("CHUMMER_ANDROID_RUNTIME_ID: android-arm64", self.text)
        start = self.text.index("Seal ARM64 hosted debug candidate observation")
        end = self.text.index("Upload ARM64 hosted debug candidate")
        seal = self.text[start:end]
        invocation = (
            "python3 chummer-android/scripts/materialize-api36-hosted-arm64-candidate.py "
            "\\\n            materialize \\\n"
        )
        self.assertIn(invocation, seal)
        self.assertEqual(1, seal.count("materialize-api36-hosted-arm64-candidate.py"))
        self.assertNotIn("materialize-api36-physical-build-provenance.py", seal)
        self.assertNotIn("build-provenance.json", seal)
        self.assertIn("hosted-build-candidate.json", seal)
        self.assertIn("--runtime android-arm64", seal)
        self.assertIn("--application-id com.myexternalbrain.chummer", seal)
        self.assertIn(
            "android-arm64/com.myexternalbrain.chummer-Signed.apk",
            seal,
        )
        for role, repository in (
            ("android", "ArchonMegalon/chummer-android.git"),
            ("presentation", "ArchonMegalon/chummer6-ui.git"),
            ("core-runtime", "ArchonMegalon/chummer6-core.git"),
            ("core-content", "ArchonMegalon/chummer6-core.git"),
            ("hub", "ArchonMegalon/chummer6-hub.git"),
            ("registry", "ArchonMegalon/chummer6-hub-registry.git"),
            ("ui-kit", "ArchonMegalon/chummer6-ui-kit.git"),
            ("media", "ArchonMegalon/chummer6-media-factory.git"),
        ):
            with self.subTest(role=role):
                self.assertIn(f"--source {role} https://github.com/{repository}", seal)
        self.assertIn("chummer-android-arm64-debug.apk.sha256", self.text)
        self.assertIn("id: upload-arm64", self.text)
        self.assertIn("arm64-artifact-id: ${{ steps.upload-arm64.outputs.artifact-id }}", self.text)
        self.assertIn("chummer-android-api36-arm64-hosted-debug-candidate-", seal)
        self.assertNotIn("physical-candidate", seal)
        self.assertNotIn("candidate authority", seal.lower())
        self.assertNotIn("physical-phone", seal)
        self.assertNotIn("releaseAttested: true", seal)

    def test_hosted_arm64_materializer_subcommand_order_is_fail_closed(self) -> None:
        start = self.text.index("Seal ARM64 hosted debug candidate observation")
        end = self.text.index("Upload ARM64 hosted debug candidate")
        tokens = self.text[start:end].replace("\\\n", " ").split()
        script_index = tokens.index(
            "chummer-android/scripts/materialize-api36-hosted-arm64-candidate.py"
        )
        self.assertEqual("python3", tokens[script_index - 1])
        self.assertEqual("materialize", tokens[script_index + 1])
        self.assertEqual("--source", tokens[script_index + 2])
        self.assertNotIn("check-inputs", tokens[script_index + 1 : script_index + 3])
        self.assertNotIn("verify", tokens[script_index + 1 : script_index + 3])

    def test_executes_every_persistence_driver_as_an_isolated_matrix_journey(self) -> None:
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
        self.assertIn("--journey full", runner)
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
        journeys = (
            ("full-editing", "tests/run_api36_editing_e2e.py"),
            (
                "creation-prerequisite",
                "tests/run_api36_creation_prerequisite_e2e.py",
            ),
            (
                "career-active-skill-advance",
                "tests/run_api36_career_active_skill_advance_e2e.py",
            ),
            (
                "career-weapon-fire",
                "tests/run_api36_career_weapon_fire_e2e.py",
            ),
        )
        self.assertIn(
            'journey="${CHUMMER_E2E_JOURNEY:?CHUMMER_E2E_JOURNEY is required}"',
            runner,
        )
        self.assertIn(
            'evidence_root="$RUNNER_TEMP/chummer-api36-evidence/$profile/$journey"',
            runner,
        )
        for authority_field in (
            "CHUMMER_E2E_APK_ARTIFACT_ID",
            "CHUMMER_E2E_APK_ARTIFACT_DIGEST",
            "CHUMMER_E2E_APK_ARTIFACT_NAME",
            "CHUMMER_E2E_APK_ARTIFACT_ATTEMPT",
            "CHUMMER_E2E_APK_SHA256",
        ):
            with self.subTest(authority_field=authority_field):
                self.assertIn(authority_field, runner)
        self.assertIn("finalize-api36-e2e-journey-receipt.py", runner)
        self.assertIn("sha256sum receipt.json >receipt.json.sha256", runner)
        for journey, driver in journeys:
            with self.subTest(journey=journey):
                self.assertIn(f"  {journey})", runner)
                self.assertEqual(1, runner.count(driver))
                self.assertIn(f"          - {journey}", self.text)
        self.assertIn("CHUMMER_E2E_JOURNEY: ${{ matrix.journey }}", self.text)
        self.assertIn("fail-fast: false", self.text)
        self.assertIn(
            "phone API 36 persistence (${{ matrix.journey }})",
            self.text,
        )
        self.assertIn(
            "chummer-android-api36-phone-${{ matrix.journey }}-evidence-",
            self.text,
        )
        self.assertIn(
            "chummer-api36-evidence/phone/${{ matrix.journey }}",
            self.text,
        )
        self.assertEqual(
            2,
            self.text.count(
                "if: ${{ matrix.journey == 'career-active-skill-advance' || "
                "matrix.journey == 'career-weapon-fire' }}"
            ),
        )
        self.assertIn("path: chummer-presentation", self.text)
        self.assertIn("path: chummer-core-engine", self.text)
        self.assertIn('--workspace-root "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}"', runner)
        self.assertLess(
            runner.index('install -d -m 0755 "$evidence_root"'),
            runner.index('case "$journey" in', runner.index('case "$journey" in') + 1),
        )

    def test_matrix_consumes_single_build_job_artifact_authority(self) -> None:
        upload = self.text.index("Upload the exact APK under test")
        download = self.text.index("Download the exact APK under test")
        verify = self.text.index("Verify the shared portable APK authority")
        emulator = self.text.index("Enable KVM for the disposable emulator")

        self.assertLess(upload, download)
        self.assertLess(download, verify)
        self.assertLess(verify, emulator)
        build_block = self.text[
            self.text.index("  build:"):self.text.index("  phone-editing-e2e:")
        ]
        self.assertIn("id: upload-apk", build_block)
        for output in (
            "apk-artifact-id: ${{ steps.upload-apk.outputs.artifact-id }}",
            "apk-artifact-digest: ${{ steps.upload-apk.outputs.artifact-digest }}",
            "apk-artifact-name: ${{ steps.apk.outputs.artifact-name }}",
            "apk-artifact-attempt: ${{ steps.apk.outputs.artifact-attempt }}",
            "apk-sha256: ${{ steps.apk.outputs.apk-sha256 }}",
        ):
            with self.subTest(output=output):
                self.assertIn(output, build_block)
        phone_block = self.text[
            self.text.index("  phone-editing-e2e:"):
            self.text.index("  phone-evidence-aggregate:")
        ]
        self.assertNotIn("gh api", phone_block)
        self.assertNotIn("/artifacts?", phone_block)
        self.assertNotIn("/jobs?", phone_block)
        download_block = self.text[download:verify]
        self.assertIn(
            "artifact-ids: ${{ needs.build.outputs.apk-artifact-id }}",
            download_block,
        )
        self.assertIn("merge-multiple: true", download_block)
        self.assertNotIn("pattern:", download_block)
        self.assertNotIn("\n          name:", download_block)
        verify_block = self.text[verify:emulator]
        self.assertIn(
            "working-directory: ${{ runner.temp }}/chummer-android-apk",
            verify_block,
        )
        self.assertIn(
            'test "$actual_sha256" = "$CHUMMER_E2E_APK_SHA256"',
            verify_block,
        )

    def test_aggregate_requires_four_stable_authority_bound_receipts(self) -> None:
        aggregate = self.text[self.text.index("  phone-evidence-aggregate:"):]
        self.assertIn("needs:\n      - build\n      - phone-editing-e2e", aggregate)
        self.assertIn("if: ${{ always() }}", aggregate)
        self.assertIn(
            "pattern: chummer-android-api36-phone-*-evidence-"
            "${{ github.run_id }}",
            aggregate,
        )
        self.assertIn("merge-multiple: false", aggregate)
        self.assertIn("verify-api36-editing-e2e-aggregate.py", aggregate)
        for authority_output in (
            "needs.build.outputs.apk-artifact-id",
            "needs.build.outputs.apk-artifact-digest",
            "needs.build.outputs.apk-artifact-name",
            "needs.build.outputs.apk-artifact-attempt",
            "needs.build.outputs.apk-sha256",
        ):
            with self.subTest(authority_output=authority_output):
                self.assertIn(authority_output, aggregate)
        matrix = self.text[
            self.text.index("  phone-editing-e2e:"):
            self.text.index("  phone-evidence-aggregate:")
        ]
        self.assertIn(
            "chummer-android-api36-phone-"
            "${{ matrix.journey }}-evidence-"
            "${{ github.run_id }}",
            matrix,
        )
        self.assertIn("overwrite: true", matrix)
        self.assertNotIn("github.run_attempt }}", matrix)

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
