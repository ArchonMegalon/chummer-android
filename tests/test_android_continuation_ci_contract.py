"""Lightweight script contracts; these tests never invoke dotnet or build products."""

import os
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPTS = {
    "build-debug.sh": "Debug",
    "compile-native-release-no-package.sh": "Release",
}
FLAGS = (
    "--android-continuation-transport",
    "--android-continuation-roaming",
    "--android-continuation-native-content-root",
    "--android-account-owner-content-root",
    "--creation-bootstrap-owner-content-root",
    "--creation-contacts-owner-content-root",
    "--creation-finalization-owner-content-root",
    "--persistence-owner-content-root",
    "--account-erasure-owner-content-root",
)
START = "# Required account/continuation suites reuse the just-built binary without rebuilding."
END = "# End required account/continuation suites."
CONTENT_START = "# Continuation tests use only this build's canonical Core content, never ambient data."


class AndroidContinuationCiContractTests(unittest.TestCase):
    def script(self, name: str) -> str:
        return (REPO / "scripts" / name).read_text(encoding="utf-8")

    def test_nine_suites_follow_the_same_successful_interaction_build(self) -> None:
        program = (REPO / "tests/Chummer.Android.Native.InteractionTests/Program.cs").read_text()
        project = (REPO / "tests/Chummer.Android.Native.InteractionTests/Chummer.Android.Native.InteractionTests.csproj").read_text()
        self.assertIn("<TargetFramework>net10.0</TargetFramework>", project)
        for name, configuration in SCRIPTS.items():
            with self.subTest(script=name):
                script = self.script(name)
                self.assertIn("set -euo pipefail", script)
                start = script.index(START)
                self.assertLess(script.index('build "$interaction_tests_path"'), start)
                block = script[start:script.index(END)]
                self.assertIn(f"/bin/{configuration}/net10.0/Chummer.Android.Native.InteractionTests.dll", block)
                for flag in FLAGS:
                    self.assertEqual(1, block.count(flag))
                    self.assertIn(f'args[0] == "{flag}"', program)
                self.assertNotIn('" run ', block)
                self.assertNotIn('" build ', block)
                self.assertNotIn('" restore ', block)
                self.assertNotIn("set +e", block)
                self.assertNotIn("|| true", block)

    def test_finalization_flag_dispatches_the_actual_unconditional_native_suite(self) -> None:
        directory = REPO / "tests/Chummer.Android.Native.InteractionTests"
        program = (directory / "Program.cs").read_text(encoding="utf-8")
        dispatch = program.split(
            'if (args.Length == 2 && args[0] == "--creation-finalization-owner-content-root")', 1
        )[1].split("}", 1)[0]
        self.assertEqual(
            '{\n            await AfterRunAuthorityHarness.RunCreationFinalizationOwnerCasesAsync(args[1]);\n            return;',
            dispatch.strip(),
        )
        project = ET.parse(directory / "Chummer.Android.Native.InteractionTests.csproj").getroot()
        matches = [(group, item) for group in project.findall("ItemGroup")
                   for item in group.findall("Compile")
                   if item.get("Include") == "CreationFinalizationOwnerRuntimeTests.cs"]
        self.assertEqual(1, len(matches))
        group, item = matches[0]
        self.assertNotIn("Condition", group.attrib)
        self.assertNotIn("Condition", item.attrib)
        self.assertIn("public static async Task RunCreationFinalizationOwnerCasesAsync(string contentRoot)",
                      (directory / "CreationFinalizationOwnerRuntimeTests.cs").read_text(encoding="utf-8"))

    def test_hosted_managed_gate_uses_runtime_core_content_not_the_apk_content_checkout(self) -> None:
        workflow = (REPO / ".github/workflows/api36-editing-e2e.yml").read_text(encoding="utf-8")
        for step in ("Build the emulator APK and native compile gate", "Build the ARM64 hosted debug candidate"):
            with self.subTest(step=step):
                block = workflow.split("- name: " + step, 1)[1].split("\n      - name:", 1)[0]
                self.assertIn("working-directory: chummer-android", block)
                self.assertIn("CHUMMER_CORE_ENGINE_ROOT: ${{ github.workspace }}/chummer-core-engine", block)
                self.assertIn("run: scripts/build-debug.sh", block)
                self.assertNotIn("chummer-core-content", block)
                self.assertNotIn("continue-on-error", block)

    def run_suites(self, name: str, failed_flag: str = "", binary: bool = True):
        script = self.script(name)
        block = script[script.index(START):script.index(END)]
        with tempfile.TemporaryDirectory(prefix="continuation-ci-") as temporary:
            root = Path(temporary)
            project = root / "test project/Chummer.Android.Native.InteractionTests.csproj"
            assembly = project.parent / f"bin/{SCRIPTS[name]}/net10.0/Chummer.Android.Native.InteractionTests.dll"
            if binary:
                assembly.parent.mkdir(parents=True)
                assembly.touch()
            env = {**os.environ, "interaction_tests_path": str(project),
                   "native_content_root": str(root / "exact Core/Chummer"), "failed_flag": failed_flag}
            # A shell function stands in for dotnet; only the actual new command block runs.
            result = subprocess.run(
                ["bash", "-c", 'set -euo pipefail\n'
                 'record_invocation() { printf "%s\\n" "$1|$2|${3-}"; '
                 '[[ "$2" != "$failed_flag" ]] || return 37; }\n'
                 'dotnet_command=record_invocation\n' + block],
                env=env, capture_output=True, text=True, timeout=10, check=False,
            )
            expected = [f"{assembly}|{flag}|" + (env["native_content_root"] if index >= 2 else "")
                        for index, flag in enumerate(FLAGS)]
            return result, expected

    def test_all_suites_receive_the_same_binary_and_exact_quoted_content_root(self) -> None:
        for name in SCRIPTS:
            with self.subTest(script=name):
                result, expected = self.run_suites(name)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(expected, result.stdout.splitlines())

    def test_each_failure_stops_the_gate_without_skipping_or_replaying(self) -> None:
        for name in SCRIPTS:
            for index, flag in enumerate(FLAGS):
                with self.subTest(script=name, failed=flag):
                    result, expected = self.run_suites(name, failed_flag=flag)
                    self.assertEqual(37, result.returncode, result.stderr)
                    self.assertEqual(expected[:index + 1], result.stdout.splitlines())

    def test_missing_binary_is_not_silently_built_or_skipped(self) -> None:
        for name in SCRIPTS:
            with self.subTest(script=name):
                result, _ = self.run_suites(name, binary=False)
                self.assertEqual(64, result.returncode)
                self.assertEqual("", result.stdout)

    def test_content_is_bound_to_the_same_core_root_before_any_build(self) -> None:
        for name in SCRIPTS:
            with self.subTest(script=name):
                script = self.script(name)
                start = script.index(CONTENT_START)
                block = script[start:script.index("\n}", start) + 2]
                self.assertLess(start, script.index('build "$interaction_tests_path"'))
                core_variable = "core_engine_root" if name == "build-debug.sh" else "CHUMMER_CORE_ENGINE_ROOT"
                self.assertIn(f'native_content_root="${core_variable}/Chummer"', block)
                self.assertNotIn(":-", block)
                if name == "build-debug.sh":
                    self.assertIn('"-p:ChummerCoreEngineRoot=$core_engine_root"', script)
                else:
                    self.assertIn('require_governed_source_root CHUMMER_CORE_ENGINE_ROOT', script[:start])
                    build = script[script.index('build "$interaction_tests_path"'):script.index(START)]
                    self.assertIn('-p:ChummerCoreEngineRoot="$CHUMMER_CORE_ENGINE_ROOT"', build)
                with tempfile.TemporaryDirectory(prefix="continuation-content-") as temporary:
                    root = Path(temporary)
                    core = root / "exact Core"
                    data = core / "Chummer/data"
                    data.mkdir(parents=True)
                    env = {**os.environ, core_variable: str(core)}
                    for state in ("valid", "missing", "redirected"):
                        with self.subTest(state=state):
                            if state == "missing":
                                data.rmdir()
                            elif state == "redirected":
                                alternate = root / "ambient-data"
                                alternate.mkdir()
                                data.symlink_to(alternate, target_is_directory=True)
                            result = subprocess.run(
                                ["bash", "-c", "set -euo pipefail\n" + block], env=env,
                                capture_output=True, text=True, timeout=10, check=False,
                            )
                            self.assertEqual(0 if state == "valid" else 64, result.returncode, result.stderr)


if __name__ == "__main__":
    unittest.main()
