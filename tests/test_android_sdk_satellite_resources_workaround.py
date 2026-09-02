from __future__ import annotations

import copy
import re
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
PROJECT_ROOT = REPO / "src" / "Chummer.Android"
PROJECT = PROJECT_ROOT / "Chummer.Android.csproj"
DIRECTORY_TARGETS = PROJECT_ROOT / "Directory.Build.targets"
AFTER_SDK_TARGETS = PROJECT_ROOT / "Chummer.Android.AfterMicrosoftNETSdk.targets"
WORKAROUND = PROJECT_ROOT / "Microsoft.Android.Sdk.Linux.36.1.69.Workaround.targets"


class AndroidSdkSatelliteResourcesWorkaroundTests(unittest.TestCase):
    def test_late_hook_preserves_sdk_project_and_gates_the_workaround_version(self) -> None:
        project = ET.parse(PROJECT).getroot()
        self.assertEqual("Microsoft.NET.Sdk", project.attrib.get("Sdk"))
        self.assertEqual([], project.findall("Import"))

        directory_targets = ET.parse(DIRECTORY_TARGETS).getroot()
        late_hook = directory_targets.find("./PropertyGroup/AfterMicrosoftNETSdkTargets")
        self.assertIsNotNone(late_hook)
        assert late_hook is not None
        self.assertEqual(
            "$(AfterMicrosoftNETSdkTargets);"
            "$(MSBuildThisFileDirectory)Chummer.Android.AfterMicrosoftNETSdk.targets",
            late_hook.text,
        )

        after_sdk = ET.parse(AFTER_SDK_TARGETS).getroot()
        guard = after_sdk.find("Target")
        self.assertIsNotNone(guard)
        assert guard is not None
        self.assertEqual("_ChummerRequirePinnedAndroidSdkForRelease", guard.attrib["Name"])
        self.assertEqual("PrepareForBuild", guard.attrib["BeforeTargets"])
        self.assertEqual("'$(Configuration)' == 'Release'", guard.attrib["Condition"])
        self.assertEqual(
            "'$(AndroidNETSdkVersion)' != '36.1.69'",
            guard.find("Error").attrib["Condition"],
        )
        imports = after_sdk.findall("Import")
        self.assertEqual(1, len(imports))
        self.assertEqual(
            "Microsoft.Android.Sdk.Linux.36.1.69.Workaround.targets",
            imports[0].attrib["Project"],
        )
        self.assertEqual(
            "'$(AndroidNETSdkVersion)' == '36.1.69'",
            imports[0].attrib["Condition"],
        )

    def test_preprocessed_sdk_target_is_unmodified_and_staging_runs_before_capture(self) -> None:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")

        with tempfile.TemporaryDirectory() as temporary:
            preprocessed = Path(temporary) / "Chummer.Android.preprocessed.xml"
            subprocess.run(
                [dotnet, "msbuild", str(PROJECT), "-nologo", f"-preprocess:{preprocessed}"],
                check=True,
                capture_output=True,
                text=True,
            )
            text = preprocessed.read_text(encoding="utf-8")

        official = [
            match.start()
            for match in re.finditer('<Target Name="_AfterILLinkAdditionalSteps"', text)
        ]
        self.assertEqual(1, len(official))
        official_target = text[official[0] : official[0] + 2200]
        self.assertIn('DestinationFiles="@(ResolvedAssemblies)"', official_target)
        self.assertIn('SourceFiles="@(ResolvedAssemblies)"', official_target)
        self.assertIn('ResolvedAssemblies="@(_AllResolvedAssemblies)"', official_target)
        self.assertNotIn("_ChummerSatellite", official_target)

        staging = text.index('<Target Name="_ChummerStageAndroidSdk36169Satellites"')
        capture = text.index('<Target Name="_LinkAssembliesNoShrinkInputs"')
        self.assertGreater(staging, official[0])
        self.assertIn('BeforeTargets="_LinkAssembliesNoShrinkInputs"', text[staging:])
        self.assertLess(capture, official[0])

    def test_release_guard_rejects_toolchain_drift_but_debug_uses_upstream(self) -> None:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")

        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "guard.proj"
            probe = ET.Element("Project")
            ET.SubElement(probe, "Import", Project=str(AFTER_SDK_TARGETS))
            ET.SubElement(probe, "Target", Name="PrepareForBuild")
            ET.ElementTree(probe).write(project, encoding="unicode")

            def run(configuration: str, version: str) -> subprocess.CompletedProcess[str]:
                return subprocess.run(
                    [
                        dotnet,
                        "msbuild",
                        str(project),
                        "-nologo",
                        "-target:PrepareForBuild",
                        f"-property:Configuration={configuration}",
                        f"-property:AndroidNETSdkVersion={version}",
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                )

            self.assertEqual(0, run("Debug", "36.2.0").returncode)
            self.assertEqual(0, run("Release", "36.1.69").returncode)
            drifted = run("Release", "36.2.0")
            self.assertNotEqual(0, drifted.returncode)
            self.assertIn(
                "Release requires Microsoft.Android.Sdk.Linux 36.1.69",
                drifted.stdout + drifted.stderr,
            )

    def test_staging_contract_preserves_official_pipeline_and_downstream_lists(self) -> None:
        target = self._target()
        self.assertEqual(
            {
                "Name": "_ChummerStageAndroidSdk36169Satellites",
                "BeforeTargets": "_LinkAssembliesNoShrinkInputs",
                "Condition": (
                    "'$(Configuration)' == 'Release' And "
                    "'$(PublishTrimmed)' == 'true'"
                ),
            },
            target.attrib,
        )
        self.assertEqual([], target.findall("AssemblyModifierPipeline"))

        copies = target.findall("CopyIfChanged")
        self.assertEqual(1, len(copies))
        self.assertEqual(
            {
                "SourceFiles": "@(_ChummerSatelliteSource)",
                "DestinationFiles": "@(_ChummerSatelliteStaged)",
            },
            copies[0].attrib,
        )
        replacement = target.findall("ItemGroup")[2]
        replacement_text = ET.tostring(replacement, encoding="unicode")
        for item_name in (
            "ResolvedAssemblies",
            "ResolvedUserAssemblies",
            "ResolvedFrameworkAssemblies",
            "_ShrunkAssemblies",
        ):
            self.assertIn(f"<{item_name} Remove=", replacement_text)
            self.assertIn(f"<{item_name} Include=", replacement_text)
        self.assertNotIn("ResolvedFileToPublish", replacement_text)
        self.assertNotIn("_AndroidResolvedSatellitePaths", replacement_text)
        self.assertNotIn("ResolvedSymbols", replacement_text)

        hashes = target.findall("Hash")
        self.assertEqual(1, len(hashes))
        self.assertEqual("@(ResolvedAssemblies)", hashes[0].attrib["ItemsToHash"])
        self.assertEqual(
            "_ResolvedUserAssembliesHash",
            hashes[0].find("Output").attrib["PropertyName"],
        )
        delete = target.find("Delete")
        self.assertIsNotNone(delete)
        assert delete is not None
        self.assertEqual("$(_AdditionalPostLinkerStepsFlag)", delete.attrib["Files"])

    def test_staging_selects_only_exact_satellites_and_fails_closed(self) -> None:
        target = self._target()
        first_items = target.findall("ItemGroup")[0]
        sources = [item for item in first_items if item.tag == "_ChummerSatelliteSource"]
        source = sources[-1]
        condition = source.attrib["Condition"]
        self.assertEqual("@(ResolvedAssemblies)", source.attrib["Include"])
        self.assertEqual("@(_ChummerSatelliteRegular)", source.attrib["Exclude"])
        self.assertIn("'%(ResolvedAssemblies.Extension)' == '.dll'", condition)
        self.assertIn(".EndsWith('.resources'", condition)
        self.assertIn("'%(ResolvedAssemblies.AssetType)' == 'resources'", condition)
        self.assertIn("'%(ResolvedAssemblies.DestinationSubPath)' != ''", condition)
        self.assertIn("_ChummerSatellitePipelineRoot", ET.tostring(source, encoding="unicode"))
        self.assertIn("[MSBuild]::NormalizePath", WORKAROUND.read_text(encoding="utf-8"))

        errors = "\n".join(error.attrib["Text"] for error in target.findall("Error"))
        for expected in (
            "unsupported unprocessed assemblies",
            "marked satellite resource assemblies for post-processing",
            "unsafe satellite staging path",
            "colliding satellite staging paths",
            "exactly one user/framework list",
            "map every satellite shrunk-assembly destination",
            "Release symbols-disabled pipeline",
            "failed to stage satellite inputs",
        ):
            self.assertIn(expected, errors)

        text = WORKAROUND.read_text(encoding="utf-8")
        self.assertNotIn("NUGET_PACKAGES", text)
        self.assertNotIn("RestorePackagesPath", text)
        self.assertNotIn("NuGetLockFilePath", text)

    def test_msbuild_mapping_redirects_resolved_and_shrunk_outputs(self) -> None:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            stage_root = root / "obj" / "chummer-android-sdk-36.1.69-satellites"
            (stage_root / "arm64-v8a" / "de").mkdir(parents=True)
            (stage_root / "arm64-v8a" / "de" / "App.resources.dll").write_bytes(b"dll")
            project, output = self._mapping_probe(root, include_symbol=False)
            completed = subprocess.run(
                [dotnet, "msbuild", str(project), "-nologo", "-target:Map"],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
            lines = output.read_text(encoding="utf-8").splitlines()
            satellite = stage_root / "arm64-v8a" / "de" / "App.resources.dll"
            shrunk = satellite.parent / "shrunk" / satellite.name
            self.assertEqual(
                [
                    "resolved=/linked/App.dll",
                    f"resolved={satellite}",
                    f"users={satellite}",
                    f"shrunk={shrunk}",
                ],
                lines,
            )

    def test_msbuild_selection_rejects_unsupported_unsafe_and_colliding_inputs(self) -> None:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")

        cases = (
            ("unsupported", [("Other.dll", "runtime", "Other.dll")], "unsupported"),
            (
                "unsafe",
                [("App.resources.dll", "resources", "../App.resources.dll")],
                "unsafe satellite staging path",
            ),
            (
                "collision",
                [
                    ("One.resources.dll", "resources", "de/App.resources.dll"),
                    ("Two.resources.dll", "resources", "de/App.resources.dll"),
                ],
                "colliding satellite staging paths",
            ),
        )
        for name, items, expected in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                completed = self._selection_rejection_probe(Path(temporary), items)
                self.assertNotEqual(0, completed.returncode)
                self.assertIn(expected, completed.stdout + completed.stderr)

    def _mapping_probe(self, root: Path, *, include_symbol: bool) -> tuple[Path, Path]:
        source_target = self._target()
        output = root / "mapped.txt"
        probe = ET.Element("Project")
        target = ET.SubElement(probe, "Target", Name="Map")
        properties = ET.SubElement(target, "PropertyGroup")
        ET.SubElement(properties, "IntermediateOutputPath").text = str(root / "obj") + "/"
        inputs = ET.SubElement(target, "ItemGroup")
        self._add_item(inputs, "ResolvedAssemblies", "/linked/App.dll", "true", "runtime", "App.dll")
        self._add_item(
            inputs,
            "ResolvedAssemblies",
            "/cache/de/App.resources.dll",
            "false",
            "resources",
            "arm64-v8a/de/App.resources.dll",
        )
        self._add_item(
            inputs,
            "ResolvedUserAssemblies",
            "/cache/de/App.resources.dll",
            "false",
            "resources",
            "arm64-v8a/de/App.resources.dll",
        )
        self._add_item(
            inputs,
            "_ShrunkAssemblies",
            "/cache/de/shrunk/App.resources.dll",
            "false",
            "resources",
            "arm64-v8a/de/App.resources.dll",
        )
        if include_symbol:
            self._add_item(
                inputs,
                "ResolvedSymbols",
                "/cache/de/App.resources.pdb",
                "false",
                "resources",
                "arm64-v8a/de/App.resources.pdb",
            )
        target.append(copy.deepcopy(source_target.find("PropertyGroup")))
        target.append(copy.deepcopy(source_target.findall("ItemGroup")[0]))
        for error in source_target.findall("Error"):
            target.append(copy.deepcopy(error))
        target.append(copy.deepcopy(source_target.findall("ItemGroup")[1]))
        target.append(copy.deepcopy(source_target.findall("ItemGroup")[2]))
        for name in (
            "ResolvedAssemblies",
            "ResolvedUserAssemblies",
            "ResolvedSymbols",
            "_ShrunkAssemblies",
        ):
            ET.SubElement(
                target,
                "WriteLinesToFile",
                File=str(output),
                Lines=f"@({name}->'{self._prefix(name)}=%(Identity)')",
                Overwrite="true" if name == "ResolvedAssemblies" else "false",
            )
        project = root / "map.proj"
        ET.ElementTree(probe).write(project, encoding="unicode")
        return project, output

    def _selection_rejection_probe(
        self,
        root: Path,
        items: list[tuple[str, str, str]],
    ) -> subprocess.CompletedProcess[str]:
        source_target = self._target()
        probe = ET.Element("Project")
        target = ET.SubElement(probe, "Target", Name="Select")
        properties = ET.SubElement(target, "PropertyGroup")
        ET.SubElement(properties, "IntermediateOutputPath").text = str(root / "obj") + "/"
        inputs = ET.SubElement(target, "ItemGroup")
        for filename, asset_type, destination in items:
            self._add_item(
                inputs,
                "ResolvedAssemblies",
                f"/cache/{filename}",
                "false",
                asset_type,
                destination,
            )
            self._add_item(
                inputs,
                "ResolvedUserAssemblies",
                f"/cache/{filename}",
                "false",
                asset_type,
                destination,
            )
            self._add_item(
                inputs,
                "_ShrunkAssemblies",
                f"/cache/shrunk/{filename}",
                "false",
                asset_type,
                destination,
            )
        target.append(copy.deepcopy(source_target.find("PropertyGroup")))
        target.append(copy.deepcopy(source_target.findall("ItemGroup")[0]))
        for error in source_target.findall("Error")[:6]:
            target.append(copy.deepcopy(error))
        project = root / "reject.proj"
        ET.ElementTree(probe).write(project, encoding="unicode")
        return subprocess.run(
            [shutil.which("dotnet"), "msbuild", str(project), "-nologo", "-target:Select"],
            check=False,
            capture_output=True,
            text=True,
        )

    @staticmethod
    def _add_item(
        group: ET.Element,
        name: str,
        path: str,
        postprocess: str,
        asset_type: str,
        destination: str,
    ) -> None:
        item = ET.SubElement(group, name, Include=path)
        ET.SubElement(item, "PostprocessAssembly").text = postprocess
        ET.SubElement(item, "AssetType").text = asset_type
        ET.SubElement(item, "DestinationSubPath").text = destination
        ET.SubElement(item, "FrameworkAssembly").text = "false"

    @staticmethod
    def _prefix(item_name: str) -> str:
        return {
            "ResolvedAssemblies": "resolved",
            "ResolvedUserAssemblies": "users",
            "ResolvedSymbols": "symbols",
            "_ShrunkAssemblies": "shrunk",
        }[item_name]

    @staticmethod
    def _target() -> ET.Element:
        target = ET.parse(WORKAROUND).getroot().find("Target")
        assert target is not None
        return target


if __name__ == "__main__":
    unittest.main()
