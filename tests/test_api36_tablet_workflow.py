"""Contract checks for optional tablet evidence beside the unchanged phone gate."""

from pathlib import Path
import re
import unittest


REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = REPO_ROOT / ".github/workflows/api36-editing-e2e.yml"
PHONE_JOURNEYS = [
    "creation-prerequisite",
    "career-active-skill-advance",
    "career-weapon-fire",
    "before-run-edge",
    "playtime-short-burst",
    "downtime-calendar",
    "after-run-settlement",
]
TABLET_COMMAND = "python3 chummer-android/scripts/run-api36-tablet-skill-group-ci.py"


def job_block(workflow: str, job: str) -> str:
    match = re.search(
        rf"^  {re.escape(job)}:\n(?P<body>.*?)(?=^  [A-Za-z0-9_-]+:\n|\Z)",
        workflow,
        re.MULTILINE | re.DOTALL,
    )
    if match is None:
        raise AssertionError(f"Missing workflow job: {job}")
    return match.group("body")


def step_block(job: str, name: str) -> str:
    match = re.search(
        rf"^      - name: {re.escape(name)}\n(?P<body>.*?)(?=^      - name: |\Z)",
        job,
        re.MULTILINE | re.DOTALL,
    )
    if match is None:
        raise AssertionError(f"Missing workflow step: {name}")
    return match.group("body")


class Api36TabletWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.tablet = job_block(cls.workflow, "tablet-skill-group-e2e")
        cls.phone = job_block(cls.workflow, "phone-wizard-e2e")
        cls.aggregate = job_block(cls.workflow, "phone-evidence-aggregate")
        cls.build = job_block(cls.workflow, "build")

    def test_tablet_requires_explicit_manual_boolean_opt_in(self) -> None:
        dispatch = self.workflow.split("  workflow_dispatch:\n", 1)[1].split("\npermissions:", 1)[0]
        self.assertIn("      run_tablet_skill_group:\n", dispatch)
        for field in ("type: boolean", "required: false", "default: false"):
            self.assertIn(f"        {field}\n", dispatch)
        self.assertIn(
            "    if: ${{ github.event_name == 'workflow_dispatch' && inputs.run_tablet_skill_group == true }}\n",
            self.tablet,
        )
        self.assertIn("    needs: build\n", self.tablet)
        self.assertIn(
            "      CHUMMER_TABLET_SKILL_GROUP_OPT_IN: ${{ inputs.run_tablet_skill_group }}\n",
            self.tablet,
        )
        self.assertIn("      CHUMMER_E2E_PROFILE: tablet\n", self.tablet)
        self.assertNotIn("continue-on-error:", self.tablet)

    def test_tablet_consumes_the_exact_shared_apk_and_producing_attempt(self) -> None:
        bindings = {
            "ID": "apk-artifact-id",
            "DIGEST": "apk-artifact-digest",
            "NAME": "apk-artifact-name",
            "ATTEMPT": "apk-artifact-attempt",
        }
        for suffix, output in bindings.items():
            with self.subTest(output=output):
                self.assertIn(
                    f"      CHUMMER_E2E_APK_ARTIFACT_{suffix}: ${{{{ needs.build.outputs.{output} }}}}\n",
                    self.tablet,
                )
        self.assertIn("      CHUMMER_E2E_APK_SHA256: ${{ needs.build.outputs.apk-sha256 }}\n", self.tablet)
        self.assertIn("      apk-artifact-attempt: ${{ steps.apk.outputs.artifact-attempt }}\n", self.build)
        download = step_block(self.tablet, "Download the exact shared proof APK")
        self.assertIn("artifact-ids: ${{ needs.build.outputs.apk-artifact-id }}", download)
        self.assertIn("path: ${{ runner.temp }}/chummer-android-apk", download)
        self.assertNotIn("pattern:", download)
        self.assertNotIn("run-id:", download)
        self.assertNotIn("scripts/build-debug.sh", self.tablet)

    def test_preflight_runs_after_download_and_before_virtualization(self) -> None:
        preflight = step_block(self.tablet, "Verify tablet opt-in and shared artifact before emulator launch")
        self.assertIn(f"        run: {TABLET_COMMAND} --preflight-only\n", preflight)
        self.assertNotIn("if:", preflight)
        self.assertLess(self.tablet.index("Download the exact shared proof APK"), self.tablet.index("--preflight-only"))
        self.assertLess(self.tablet.index("--preflight-only"), self.tablet.index("Enable KVM for the disposable tablet emulator"))
        self.assertLess(self.tablet.index("--preflight-only"), self.tablet.index("uses: ReactiveCircus/android-emulator-runner@"))
        checkout = step_block(self.tablet, "Check out the exact tablet driver")
        self.assertIn("path: chummer-android", checkout)
        self.assertIn("persist-credentials: false", checkout)
        self.assertNotIn("ref:", checkout)

    def test_tablet_uses_bounded_api36_x64_emulator_and_fresh_action_owned_avd(self) -> None:
        self.assertIn("    runs-on: ubuntu-24.04\n", self.tablet)
        self.assertIn("    timeout-minutes: 45\n", self.tablet)
        self.assertNotIn("ANDROID_AVD_HOME:", self.tablet)
        self.assertIn("The pinned action owns HOME/.android/avd in this fresh hosted job.", self.tablet)
        emulator = step_block(self.tablet, "Run the native tablet Skill Group journey")
        self.assertIn("ReactiveCircus/android-emulator-runner@a421e43855164a8197daf9d8d40fe71c6996bb0d", emulator)
        for option in (
            "api-level: 36", "target: google_apis", "arch: x86_64", "profile: pixel_c",
            "avd-name: test", "force-avd-creation: true", "disable-animations: true",
        ):
            with self.subTest(option=option):
                self.assertIn(f"          {option}\n", emulator)
        self.assertIn("-cores 2 -memory 2048", emulator)
        self.assertIn("-no-snapshot-save", emulator)
        self.assertIn("-stdouterr-file $RUNNER_TEMP/chummer-api36-emulator-live.log", emulator)
        self.assertIn("pre-emulator-launch-script: python3 chummer-android/scripts/prepare-api36-emulator-live-log.py", emulator)
        self.assertIn(f"          script: {TABLET_COMMAND}\n", emulator)
        kvm = step_block(self.tablet, "Enable KVM for the disposable tablet emulator")
        self.assertIn('KERNEL=="kvm"', kvm)
        self.assertIn("sudo udevadm trigger --name-match=kvm", kvm)
        self.assertLess(self.tablet.index("Enable KVM for the disposable tablet emulator"), self.tablet.index("Run the native tablet Skill Group journey"))

    def test_tablet_uploads_separate_supplemental_evidence_even_on_failure(self) -> None:
        upload = step_block(self.tablet, "Upload supplemental tablet evidence")
        self.assertIn("        if: ${{ always() }}\n", upload)
        self.assertIn(
            "name: chummer-android-api36-tablet-skill-group-x64-evidence-${{ github.run_id }}-${{ github.run_attempt }}",
            upload,
        )
        self.assertIn("path: ${{ runner.temp }}/chummer-api36-tablet-skill-group-evidence", upload)
        self.assertIn("overwrite: false", upload)
        self.assertIn("include-hidden-files: false", upload)
        self.assertIn("Explicit supplemental engineering evidence", self.tablet)
        self.assertNotIn("chummer-android-api36-phone-", self.tablet)
        for phone_authority_tool in (
            "run-api36-editing-e2e-ci.sh", "finalize-api36-e2e-journey-receipt.py",
            "materialize-api36-proof-environment-receipt.py", "materialize-android-p0-pr-authority.py",
        ):
            self.assertNotIn(phone_authority_tool, self.tablet)

    def test_seven_phone_journeys_and_aggregate_dependencies_remain_exact(self) -> None:
        matrix = self.phone.split("        journey:\n", 1)[1].split("    runs-on:", 1)[0]
        self.assertEqual(PHONE_JOURNEYS, re.findall(r"^          - ([A-Za-z0-9_-]+)$", matrix, re.MULTILINE))
        self.assertIn("      CHUMMER_E2E_PROFILE: phone\n", self.phone)
        self.assertIn("          profile: pixel_6\n", self.phone)
        self.assertIn("    needs: build\n", self.phone)
        needs = self.aggregate.split("    needs:\n", 1)[1].split("    if:", 1)[0]
        self.assertEqual(["build", "phone-wizard-e2e"], re.findall(r"^      - ([A-Za-z0-9_-]+)$", needs, re.MULTILINE))
        self.assertIn("pattern: chummer-android-api36-phone-*-evidence-${{ github.run_id }}", self.aggregate)
        self.assertNotIn("tablet-skill-group", self.phone)
        self.assertNotIn("tablet-skill-group", self.aggregate)


if __name__ == "__main__":
    unittest.main()
