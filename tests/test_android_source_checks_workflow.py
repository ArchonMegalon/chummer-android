from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]


class AndroidSourceChecksWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text = (ROOT / ".github/workflows/android-source-checks.yml").read_text()

    def test_all_review_changes_receive_the_separately_named_check(self):
        triggers = self.text.split("on:\n", 1)[1].split("permissions:\n", 1)[0]
        self.assertIn("  pull_request:\n  merge_group:\n", triggers)
        self.assertIn("  push:\n    branches: [main]\n", triggers)
        self.assertNotIn("paths:", triggers)
        self.assertNotIn("paths-ignore:", triggers)
        self.assertIn("name: Android source and safety checks", self.text)
        self.assertNotIn("name: Aggregate exact API 36 phone evidence", self.text)

    def test_check_has_no_credentials_build_signing_or_runtime_authority(self):
        self.assertIn("permissions:\n  contents: read\n", self.text)
        self.assertIn("persist-credentials: false", self.text)
        self.assertIn("timeout-minutes: 5", self.text)
        for forbidden in (
            "secrets.", "pull_request_target:", "self-hosted", "environment:",
            "continue-on-error:", "actions/upload-artifact@", "setup-dotnet@",
            "build-debug.sh", "build-release.sh", "dotnet publish", "docker ",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.text)
        self.assertIn("No API-36 execution, aggregate, two-green or publication authority", self.text)
        pins = re.findall(r"uses: ([^\s]+)", self.text)
        self.assertEqual(1, len(pins))
        self.assertRegex(pins[0], r"^actions/checkout@[a-f0-9]{40}$")

    def test_diff_is_checked_without_embedding_untrusted_event_text(self):
        self.assertIn('[[ "$CHUMMER_CHECK_BASE" =~ ^[0-9a-f]{40}$ ]]', self.text)
        self.assertIn('git cat-file -e "$CHUMMER_CHECK_BASE^{commit}"', self.text)
        self.assertIn('git diff --check "$CHUMMER_CHECK_BASE" HEAD', self.text)
        self.assertIn("scripts/classify_android_ci_changes.py", self.text)
        for body in self.text.split("run: |\n")[1:]:
            script = body.split("\n      - name:", 1)[0]
            self.assertNotIn("${{", script)

    def test_existing_negative_security_regressions_are_not_dropped(self):
        for required in (
            "verify_release_private_key_hygiene.py --repo-root",
            "test_release_artifact_hygiene.py",
            "test_release_aab_proof_exclusion.py",
            "test_private_key_hygiene_rejects_tracked_and_ignored_key_material",
            "test_api36_e2e_workflow.py",
        ):
            with self.subTest(required=required):
                self.assertIn(required, self.text)


if __name__ == "__main__":
    unittest.main()
