from __future__ import annotations

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "verify_android_release_intent.py"
sys.path.insert(0, str(SCRIPT.parent))


def load_module():
    spec = importlib.util.spec_from_file_location("verify_android_release_intent", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_project(path: Path, version_name: str, version_code: str) -> Path:
    project = path / "Chummer.Android.csproj"
    project.write_text(
        "<Project><PropertyGroup>"
        f"<ApplicationDisplayVersion>{version_name}</ApplicationDisplayVersion>"
        f"<ApplicationVersion>{version_code}</ApplicationVersion>"
        "</PropertyGroup></Project>\n",
        encoding="utf-8",
    )
    return project


class AndroidReleaseIntentTests(unittest.TestCase):
    def setUp(self) -> None:
        self.module = load_module()

    def test_exact_next_version_is_resolved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            project = write_project(Path(temporary), "0.1.0-preview.11", "11")
            self.assertEqual(
                ("0.1.0-preview.11", 11),
                self.module.resolve_release_intent(
                    project,
                    "0.1.0-preview.11",
                    "11",
                ),
            )

    def test_missing_malformed_historical_and_mismatched_intents_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            project = write_project(Path(temporary), "0.1.0-preview.11", "11")
            cases = (
                ("", "11", "name is missing"),
                ("0.1.0-preview.11\n0.1.0-preview.12", "11", "name is missing"),
                ("0.1.0-preview..11", "11", "name is missing"),
                ("0.1.0-" + "a" * 129, "11", "name is missing"),
                ("0.1.0-preview.11", "", "code is missing"),
                ("0.1.0-preview.11", "011", "code is missing"),
                ("0.1.0-preview.10", "10", "greater than 10"),
                ("0.1.0-preview.12", "11", "name does not match"),
                ("0.1.0-preview.11", "12", "code does not match"),
            )
            for version_name, version_code, message in cases:
                with self.subTest(version_name=version_name, version_code=version_code):
                    with self.assertRaisesRegex(ValueError, message):
                        self.module.resolve_release_intent(
                            project,
                            version_name,
                            version_code,
                        )

    def test_cli_emits_one_unambiguous_tab_delimited_pair(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            project = write_project(Path(temporary), "1.0.0", "11")
            completed = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--project",
                    str(project),
                    "--expected-version-name",
                    "1.0.0",
                    "--expected-version-code",
                    "11",
                ],
                check=True,
                capture_output=True,
                text=True,
            )
            self.assertEqual("1.0.0\t11\n", completed.stdout)
            self.assertEqual("", completed.stderr)

    def test_release_shell_requires_intent_before_workspace_or_signing_inputs(self) -> None:
        completed = subprocess.run(
            ["bash", str(REPO / "scripts" / "build-release.sh")],
            check=False,
            capture_output=True,
            text=True,
            env={"PATH": "/usr/local/bin:/usr/bin:/bin"},
        )
        self.assertNotEqual(0, completed.returncode)
        self.assertEqual(
            "android_release=failed stage=release-version-intent-missing\n",
            completed.stderr,
        )

    def test_historical_preview10_cannot_enter_the_new_release_lane(self) -> None:
        completed = subprocess.run(
            ["bash", str(REPO / "scripts" / "build-release.sh")],
            check=False,
            capture_output=True,
            text=True,
            env={
                "PATH": "/usr/local/bin:/usr/bin:/bin",
                "CHUMMER_ANDROID_EXPECTED_VERSION_NAME": "0.1.0-preview.10",
                "CHUMMER_ANDROID_EXPECTED_VERSION_CODE": "10",
            },
        )
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("expected version code must be greater than 10", completed.stderr)
        self.assertTrue(
            completed.stderr.endswith(
                "android_release=failed stage=release-version-intent-invalid\n"
            )
        )

    def test_release_shell_has_no_google_play_mutation_transport(self) -> None:
        build = (REPO / "scripts" / "build-release.sh").read_text(encoding="utf-8")
        source_graph = (REPO / "scripts" / "verify_release_source_graph.py").read_text(
            encoding="utf-8"
        )
        for forbidden in (
            "androidpublisher.googleapis.com",
            "play.google.com/console",
            "edits.tracks",
            "google_play_upload.py",
        ):
            self.assertNotIn(forbidden, build)
        self.assertIn('"publicationAuthorized": False', source_graph)
        self.assertIn('"google_play_upload"', source_graph)


if __name__ == "__main__":
    unittest.main()
