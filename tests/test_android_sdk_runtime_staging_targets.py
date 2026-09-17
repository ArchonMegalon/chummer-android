"""Exercise unmodified workaround XML using real MSBuild, not an app build.

The default helper fixture injects a small test policy, never into production.
Set CHUMMER_TEST_SDK_RUNTIME_SOURCE to the pinned public SDK DLL to exercise
the unmodified production helper as well. The ILLink target below is a model
of SDK item replacement, not a claim that Android's full linker was executed.
"""
from __future__ import annotations

import hashlib
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
TARGET = REPO / "src/Chummer.Android/Microsoft.Android.Sdk.Linux.36.1.69.Workaround.targets"
HELPER = REPO / "scripts/stage_android_sdk_runtime.py"
SUFFIX = "Microsoft.Android.Runtime.36.android/36.1.69/runtimes/android/lib/net10.0/Mono.Android.Runtime.dll"
FIXTURE_BYTES = b"msbuild-runtime-fixture"


def escape_msbuild(value: str) -> str:
    for char in "%$@;?*'()":
        value = value.replace(char, f"%{ord(char):02X}")
    return value


@unittest.skipUnless(shutil.which("dotnet") and os.name == "posix", "Linux MSBuild required")
class AndroidSdkRuntimeStagingTargetTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="sdk-wiring-")
        self.root = Path(self.temp.name)
        self.fixture = self.root / "fixture space $cash `false`"
        self.fixture.mkdir()
        target = self.fixture / "src/Chummer.Android" / TARGET.name
        target.parent.mkdir(parents=True)
        shutil.copyfile(TARGET, target)
        self.target = target
        helper = self.fixture / "scripts/stage_android_sdk_runtime.py"
        helper.parent.mkdir()
        self.source = self.root / "public SDK $cash `false`" / SUFFIX
        self.source.parent.mkdir(parents=True)
        pinned_source = os.environ.get("CHUMMER_TEST_SDK_RUNTIME_SOURCE")
        if pinned_source:
            shutil.copyfile(pinned_source, self.source)
            shutil.copyfile(HELPER, helper)
        else:
            self.source.write_bytes(FIXTURE_BYTES)
            # Only the test fixture policy changes; XML is byte-identical.
            source = HELPER.read_text(encoding="utf-8")
            source = source.replace("EXPECTED_SIZE_BYTES = 22368", f"EXPECTED_SIZE_BYTES = {len(FIXTURE_BYTES)}")
            source = source.replace(
                'EXPECTED_SHA256 = "7d9c809d92b5527556c69988deaa6a1faadbd1757cb11e9e9f167082e37d550f"',
                f'EXPECTED_SHA256 = "{hashlib.sha256(FIXTURE_BYTES).hexdigest()}"',
            )
            helper.write_text(source, encoding="utf-8")
        self.source.chmod(0o744)
        self.intermediate = self.root / "private obj $cash `false`"
        self.intermediate.mkdir(mode=0o700)
        self.linked = self.intermediate / "linked"
        self.linked.mkdir(mode=0o700)
        self.output = self.root / "observed.txt"

    def tearDown(self) -> None:
        self.temp.cleanup()

    def project(self, *, count: int = 1, postprocess: str = "true", configuration: str = "Release",
                governed: bool = True, version: str = "36.1.69", trimmed: bool = True,
                output_mode: str = "normal") -> Path:
        project = ET.Element("Project")
        properties = ET.SubElement(project, "PropertyGroup")
        for key, value in {
            "Configuration": configuration,
            "PublishTrimmed": str(trimmed).lower(),
            "AndroidNETSdkVersion": version,
            "ChummerReleaseIntermediateRoot": str(self.intermediate) if governed else "",
            "IntermediateOutputPath": str(self.intermediate) + "/",
            "IntermediateLinkDir": str(self.linked) + "/",
        }.items():
            ET.SubElement(properties, key).text = escape_msbuild(value)
        ET.SubElement(project, "Import", Project=escape_msbuild(str(self.target)))
        populate = ET.SubElement(project, "Target", Name="_ComputeAssembliesToPostprocessOnPublish")
        items = ET.SubElement(populate, "ItemGroup")
        for _ in range(count):
            runtime = ET.SubElement(items, "ResolvedFileToPublish", Include=escape_msbuild(str(self.source)))
            for key, value in {"PostprocessAssembly": postprocess, "AssetType": "runtime",
                               "DestinationSubPath": "Mono.Android.Runtime.dll", "RelativePath": "Mono.Android.Runtime.dll",
                               "RuntimeIdentifier": "android-arm64", "FrameworkAssembly": "true",
                               "Sentinel": "retained"}.items():
                ET.SubElement(runtime, key).text = value
        ET.SubElement(items, "ResolvedFileToPublish", Include="/unrelated/preserve.txt")
        capture = ET.SubElement(project, "Target", Name="_ComputeManagedAssemblyToLink",
                                DependsOnTargets="_ComputeAssembliesToPostprocessOnPublish")
        ET.SubElement(capture, "WriteLinesToFile", File=str(self.output), Overwrite="true",
                      Lines="@(ResolvedFileToPublish->'%(Identity)|%(PostprocessAssembly)|%(AssetType)|%(DestinationSubPath)|%(RelativePath)|%(RuntimeIdentifier)|%(FrameworkAssembly)|%(Sentinel)')")
        link = ET.SubElement(project, "Target", Name="ILLink", DependsOnTargets="_ComputeManagedAssemblyToLink")
        if output_mode != "missing":
            ET.SubElement(link, "Copy", SourceFiles="@(_ChummerSdkRuntimeStaged)",
                          DestinationFolder="$(IntermediateLinkDir)", Condition="'$(_ChummerSdkRuntimeInputStaged)' == 'true'")
        if output_mode != "original":
            linked_items = ET.SubElement(link, "ItemGroup", Condition="'$(_ChummerSdkRuntimeInputStaged)' == 'true'")
            ET.SubElement(linked_items, "ResolvedFileToPublish", Remove="@(_ChummerSdkRuntimeStaged)")
            for _ in range(2 if output_mode == "duplicate" else 1):
                ET.SubElement(linked_items, "ResolvedFileToPublish", Include="$(IntermediateLinkDir)Mono.Android.Runtime.dll")
        ET.SubElement(project, "Target", Name="Verify", DependsOnTargets="ILLink")
        path = self.root / "probe.proj"
        ET.ElementTree(project).write(path, encoding="unicode")
        return path

    def run_probe(self, **kwargs: object) -> subprocess.CompletedProcess[str]:
        return subprocess.run([shutil.which("dotnet"), "msbuild", str(self.project(**kwargs)),
                               "-nologo", "-target:Verify", "-verbosity:minimal", "-nodeReuse:false"],
                              capture_output=True, text=True, timeout=30)

    def test_remaps_before_capture_preserving_metadata_and_other_items(self) -> None:
        result = self.run_probe()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        staged = self.intermediate / "chummer-android-sdk-36.1.69-runtime/Mono.Android.Runtime.dll"
        lines = self.output.read_text().splitlines()
        self.assertEqual(2, len(lines))
        self.assertIn("/unrelated/preserve.txt|||||||", lines)
        self.assertIn(f"{staged}|true|runtime|Mono.Android.Runtime.dll|Mono.Android.Runtime.dll|android-arm64|true|retained", lines)
        self.assertEqual(self.source.read_bytes(), staged.read_bytes())
        self.assertEqual(0o600, stat.S_IMODE(staged.stat().st_mode))
        self.assertEqual(0o600, stat.S_IMODE((self.linked / staged.name).stat().st_mode))
        self.assertEqual(0o744, stat.S_IMODE(self.source.stat().st_mode))
        again = self.run_probe()
        self.assertEqual(0, again.returncode, again.stdout + again.stderr)
        self.assertIn("reused=true", again.stdout)

    def test_missing_duplicate_or_nonpostprocessed_input_rejected_before_capture(self) -> None:
        for kwargs in ({"count": 0}, {"count": 2}, {"postprocess": "false"}):
            with self.subTest(**kwargs):
                result = self.run_probe(**kwargs)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("Governed Android SDK 36.1.69", result.stdout + result.stderr)
                self.assertFalse(self.output.exists())
                self.assertFalse((self.intermediate / "chummer-android-sdk-36.1.69-runtime").exists())

    def test_linked_output_must_exist_and_have_one_correct_publish_identity(self) -> None:
        for mode, message in (("missing", "lost its required linked output"),
                              ("original", "did not converge on its linked output"),
                              ("duplicate", "requires one linked publish output")):
            with self.subTest(mode=mode):
                result = self.run_probe(output_mode=mode)
                self.assertNotEqual(0, result.returncode)
                self.assertIn(message, result.stdout + result.stderr)

    def test_unqualified_builds_do_not_stage(self) -> None:
        for kwargs in ({"configuration": "Debug"}, {"governed": False}, {"version": "36.2.0"}, {"trimmed": False}):
            with self.subTest(**kwargs):
                result = self.run_probe(**kwargs)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertFalse((self.intermediate / "chummer-android-sdk-36.1.69-runtime").exists())
                self.assertIn(str(self.source), self.output.read_text())

    def test_semicolon_path_rejected_before_exec(self) -> None:
        self.intermediate = self.root / "unsafe;path"
        self.intermediate.mkdir(mode=0o700)
        result = self.run_probe()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("environment-list separators", result.stdout + result.stderr)
        self.assertFalse((self.intermediate / "chummer-android-sdk-36.1.69-runtime").exists())


if __name__ == "__main__":
    unittest.main()
