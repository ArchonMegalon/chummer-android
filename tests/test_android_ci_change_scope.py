from __future__ import annotations

import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "android_ci_change_scope", ROOT / "scripts/classify_android_ci_changes.py"
)
SCOPE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SCOPE)


class AndroidCiChangeScopeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("config", "user.name", "Fixture")
        self.write("docs/PLAY_RELEASE.md", "# Release\n")
        self.write("src/App.cs", "class App {}\n")
        self.base = self.commit()

    def git(self, *args):
        return subprocess.run(["git", "-C", str(self.repo), *args], check=True,
                              capture_output=True, text=True).stdout.strip()

    def write(self, name, text):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        return path

    def commit(self):
        self.git("add", "-A")
        self.git("commit", "-qm", "fixture")
        return self.git("rev-parse", "HEAD")

    def test_document_and_local_observation_change_is_lightweight(self):
        self.write("docs/PLAY_RELEASE.md", "# Actual Internal observation\n")
        self.write("play/evidence/preview12-local-signing.json", '{"signed": true}\n')
        head = self.commit()
        self.assertTrue(SCOPE.documentation_only(self.repo, self.base, head))
        SCOPE.validate_documents(self.repo, self.base, head)

    def test_mixed_runtime_change_never_counts_as_documentation(self):
        self.write("docs/PLAY_RELEASE.md", "# Updated\n")
        self.write("src/App.cs", "class App { int changed; }\n")
        self.assertFalse(SCOPE.documentation_only(self.repo, self.base, self.commit()))

    def test_unknown_policy_script_workflow_and_authority_paths_require_runtime(self):
        for name in (
            "AGENTS.md", ".github/workflows/api36-editing-e2e.yml",
            "scripts/classify_android_ci_changes.py", "tests/test_example.py",
            "eng/design-policy-authority.json", "global.json",
            "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json",
            "docs/editability-evidence/authority.md",
            "play/evidence/preview10-internal-publication.json",
            "play/listing/en-US/release-notes-13.txt",
            "docs/PLAY_RELEASE.md\nspoof", "docs/OTHER.md",
        ):
            with self.subTest(name=name):
                self.assertFalse(SCOPE.document_path(name))

    def test_source_renamed_into_allowlisted_path_is_still_runtime(self):
        target = self.repo / "play/evidence/preview12-internal-observation.md"
        target.parent.mkdir(parents=True)
        (self.repo / "src/App.cs").rename(target)
        self.assertFalse(SCOPE.documentation_only(self.repo, self.base, self.commit()))

    def test_symlink_and_executable_mode_changes_are_not_docs_only(self):
        path = self.repo / "docs/PLAY_RELEASE.md"
        path.chmod(0o755)
        executable = self.commit()
        self.assertFalse(SCOPE.documentation_only(self.repo, self.base, executable))
        path.unlink()
        path.symlink_to("../src/App.cs")
        self.assertFalse(SCOPE.documentation_only(self.repo, self.base, self.commit()))

    def test_empty_or_invalid_comparison_never_grants_skip(self):
        for base in (self.base, "", "0" * 40, "--help", "not-a-sha"):
            self.assertFalse(SCOPE.documentation_only(self.repo, base, self.base))
        with self.assertRaises(subprocess.CalledProcessError):
            SCOPE.documentation_only(self.repo, "f" * 40, self.base)

    def test_deleted_regular_document_is_docs_only(self):
        (self.repo / "docs/PLAY_RELEASE.md").unlink()
        head = self.commit()
        self.assertTrue(SCOPE.documentation_only(self.repo, self.base, head))
        SCOPE.validate_documents(self.repo, self.base, head)

    def test_json_invalid_duplicate_and_nonfinite_values_fail_check(self):
        for value in ("not-json", '{"x": 1, "x": 2}', '{"x": NaN}', "[]"):
            with self.subTest(value=value):
                self.write("play/evidence/preview12-local-verification.json", value + "\n")
                head = self.commit()
                self.assertTrue(SCOPE.documentation_only(self.repo, self.base, head))
                with self.assertRaises(ValueError):
                    SCOPE.validate_documents(self.repo, self.base, head)

    def test_oversized_document_fails_check(self):
        self.write("docs/PLAY_RELEASE.md", "x" * (SCOPE.MAX_DOCUMENT_BYTES + 1) + "\n")
        head = self.commit()
        with self.assertRaises(ValueError):
            SCOPE.validate_documents(self.repo, self.base, head)

    def test_workflow_keeps_real_runtime_and_no_proof_docs_paths_separate(self):
        workflow = (ROOT / ".github/workflows/api36-editing-e2e.yml").read_text()
        self.assertIn('git show "$CHUMMER_CHANGE_BASE:scripts/classify_android_ci_changes.py"', workflow)
        self.assertIn('"$CHUMMER_CHANGE_EVENT" == workflow_dispatch', workflow)
        self.assertIn('echo \'docs-only=false\' >> "$GITHUB_OUTPUT"', workflow)
        build = workflow.split("\n  build:\n")[1].split("\n  phone-wizard-e2e:")[0]
        guard = "if: ${{ needs.change-scope.outputs.docs-only == 'false' }}"
        self.assertIn("needs: change-scope", build)
        self.assertIn(guard, build)
        aggregate = workflow.split("\n  phone-evidence-aggregate:\n")[1]
        self.assertIn("if: ${{ always() }}", aggregate)
        self.assertIn('test "$SCOPE_RESULT" = success', aggregate)
        self.assertIn('test "$BUILD_RESULT" = skipped', aggregate)
        self.assertIn('test "$JOURNEY_RESULT" = skipped', aggregate)
        self.assertIn('test "$BUILD_RESULT" = success', aggregate)
        self.assertIn('test "$JOURNEY_RESULT" = success', aggregate)
        self.assertIn("no API-36 execution or release eligibility is asserted", aggregate)
        steps = aggregate.split("      - name: ")[1:]
        self.assertEqual(10, len(steps))
        for step in steps[1:]:
            self.assertIn(guard, step)


if __name__ == "__main__":
    unittest.main()
