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
    def test_late_hook_preserves_sdk_project_and_gates_the_override_version(self) -> None:
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
        guards = after_sdk.findall("Target")
        self.assertEqual(1, len(guards))
        self.assertEqual(
            {
                "Name": "_ChummerRequirePinnedAndroidSdkForRelease",
                "BeforeTargets": "PrepareForBuild",
                "Condition": "'$(Configuration)' == 'Release'",
            },
            guards[0].attrib,
        )
        errors = guards[0].findall("Error")
        self.assertEqual(1, len(errors))
        self.assertEqual(
            "'$(AndroidNETSdkVersion)' != '36.1.69'",
            errors[0].attrib["Condition"],
        )
        imports = after_sdk.findall("Import")
        self.assertEqual(1, len(imports))
        self.assertEqual(
            {
                "Project": "Microsoft.Android.Sdk.Linux.36.1.69.Workaround.targets",
                "Condition": "'$(AndroidNETSdkVersion)' == '36.1.69'",
            },
            imports[0].attrib,
        )

    def test_preprocessed_override_follows_the_android_sdk_definition(self) -> None:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")

        with tempfile.TemporaryDirectory() as temporary:
            preprocessed = Path(temporary) / "Chummer.Android.preprocessed.xml"
            subprocess.run(
                [
                    dotnet,
                    "msbuild",
                    str(PROJECT),
                    "-nologo",
                    f"-preprocess:{preprocessed}",
                ],
                check=True,
                capture_output=True,
                text=True,
            )
            text = preprocessed.read_text(encoding="utf-8")

        definitions = [
            match.start()
            for match in re.finditer(
                '<Target Name="_AfterILLinkAdditionalSteps"', text
            )
        ]
        self.assertGreaterEqual(len(definitions), 2)
        sdk_definition = text[definitions[-2] : definitions[-1]]
        local_definition = text[definitions[-1] :]
        self.assertIn('SourceFiles="@(ResolvedAssemblies)"', sdk_definition)
        self.assertNotIn("_ChummerAssemblyModifierPipelineFile", sdk_definition)
        self.assertIn("_ChummerAssemblyModifierPipelineFile", local_definition)
        self.assertIn(
            "Chummer.Android.AfterMicrosoftNETSdk.targets", local_definition
        )

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

            debug = run("Debug", "36.2.0")
            self.assertEqual(0, debug.returncode, debug.stdout + debug.stderr)

            pinned_release = run("Release", "36.1.69")
            self.assertEqual(
                0,
                pinned_release.returncode,
                pinned_release.stdout + pinned_release.stderr,
            )

            drifted_release = run("Release", "36.2.0")
            self.assertNotEqual(0, drifted_release.returncode)
            self.assertIn(
                "Release requires Microsoft.Android.Sdk.Linux 36.1.69",
                drifted_release.stdout,
            )

    def test_override_preserves_sdk_target_and_task_contract(self) -> None:
        workaround = ET.parse(WORKAROUND).getroot()
        targets = workaround.findall("Target")
        self.assertEqual(1, len(targets))

        target = targets[0]
        self.assertEqual(
            {
                "Name": "_AfterILLinkAdditionalSteps",
                "DependsOnTargets": "_LinkAssembliesNoShrinkInputs",
                "Condition": "'$(PublishTrimmed)' == 'true'",
                "Inputs": "$(_AndroidLinkFlag)",
                "Outputs": "$(_AdditionalPostLinkerStepsFlag)",
            },
            target.attrib,
        )

        pipelines = target.findall("AssemblyModifierPipeline")
        self.assertEqual(1, len(pipelines))
        self.assertEqual(
            {
                "ApplicationJavaClass": "$(AndroidApplicationJavaClass)",
                "CodeGenerationTarget": "$(_AndroidJcwCodegenTarget)",
                "Debug": "$(AndroidIncludeDebugSymbols)",
                "DestinationFiles": "@(_ChummerAssemblyModifierPipelineFile)",
                "Deterministic": "$(Deterministic)",
                "EnableMarshalMethods": "$(_AndroidUseMarshalMethods)",
                "ErrorOnCustomJavaObject": "$(AndroidErrorOnCustomJavaObject)",
                "PackageNamingPolicy": "$(AndroidPackageNamingPolicy)",
                "ReadSymbols": "$(_AndroidLinkAssembliesReadSymbols)",
                "ResolvedAssemblies": "@(_AllResolvedAssemblies)",
                "ResolvedUserAssemblies": "@(ResolvedUserAssemblies)",
                "SourceFiles": "@(_ChummerAssemblyModifierPipelineFile)",
                "TargetName": "$(TargetName)",
            },
            pipelines[0].attrib,
        )

        touches = target.findall("Touch")
        self.assertEqual(1, len(touches))
        self.assertEqual(
            {
                "Files": "$(_AdditionalPostLinkerStepsFlag)",
                "AlwaysCreate": "true",
            },
            touches[0].attrib,
        )

    def test_override_uses_postprocess_metadata_and_fails_closed_on_drift(self) -> None:
        workaround = ET.parse(WORKAROUND).getroot()
        target = workaround.find("Target")
        self.assertIsNotNone(target)
        assert target is not None

        item_groups = target.findall("ItemGroup")
        self.assertEqual(1, len(item_groups))
        items = list(item_groups[0])
        self.assertEqual(
            [
                "_ChummerAssemblyModifierPipelineFile",
                "_ChummerAssemblyModifierPipelineExcludedFile",
                "_ChummerAssemblyModifierPipelineUnexpectedExcludedFile",
                "_ChummerAssemblyModifierPipelineUnexpectedIncludedSatellite",
                "_ChummerAssemblyModifierPipelineFile",
                "_ChummerAssemblyModifierPipelineExcludedFile",
                "_ChummerAssemblyModifierPipelineUnexpectedExcludedFile",
                "_ChummerAssemblyModifierPipelineUnexpectedIncludedSatellite",
            ],
            [item.tag for item in items],
        )
        for item in items[:4]:
            self.assertEqual({"Remove": f"@({item.tag})"}, item.attrib)
        self.assertEqual(
            {
                "Include": (
                    "@(ResolvedAssemblies->WithMetadataValue("
                    "'PostprocessAssembly', 'true'))"
                )
            },
            items[4].attrib,
        )
        self.assertEqual(
            {
                "Include": "@(ResolvedAssemblies)",
                "Exclude": "@(_ChummerAssemblyModifierPipelineFile)",
            },
            items[5].attrib,
        )
        exclusion_condition = items[6].attrib["Condition"]
        self.assertIn(".EndsWith('.resources'", exclusion_condition)
        self.assertIn("'%(AssetType)' != 'resources'", exclusion_condition)
        self.assertIn("'%(DestinationSubPath)' == ''", exclusion_condition)
        self.assertIn(
            "'%(DestinationSubPath)' == '%(Filename)%(Extension)'",
            exclusion_condition,
        )
        self.assertIn(".EndsWith('.resources'", items[7].attrib["Condition"])

        errors = target.findall("Error")
        self.assertEqual(2, len(errors))
        self.assertIn(
            "_ChummerAssemblyModifierPipelineUnexpectedExcludedFile",
            errors[0].attrib["Condition"],
        )
        self.assertIn(
            "_ChummerAssemblyModifierPipelineUnexpectedIncludedSatellite",
            errors[1].attrib["Condition"],
        )

        text = WORKAROUND.read_text(encoding="utf-8")
        self.assertNotIn("NUGET_PACKAGES", text)
        self.assertNotIn("RestorePackagesPath", text)
        self.assertNotIn("NuGetLockFilePath", text)

    def test_msbuild_filter_keeps_full_resolver_lists_and_only_drops_satellites(self) -> None:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")

        workaround = ET.parse(WORKAROUND).getroot()
        source_target = workaround.find("Target")
        self.assertIsNotNone(source_target)
        assert source_target is not None
        source_items = source_target.find("ItemGroup")
        self.assertIsNotNone(source_items)
        assert source_items is not None

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = root / "filtered.txt"
            probe = ET.Element("Project")
            target = ET.SubElement(probe, "Target", Name="Filter")
            inputs = ET.SubElement(target, "ItemGroup")
            for path, postprocess, asset_type, destination in (
                ("/inputs/App.dll", "true", "runtime", "App.dll"),
                (
                    "/inputs/en/App.resources.dll",
                    "false",
                    "resources",
                    "en/App.resources.dll",
                ),
                (
                    "/inputs/de/App.resources.dll",
                    "false",
                    "resources",
                    "de/App.resources.dll",
                ),
                (
                    "/inputs/App.resources.exe",
                    "true",
                    "runtime",
                    "App.resources.exe",
                ),
                ("/inputs/resources.dll", "true", "runtime", "resources.dll"),
                (
                    "/inputs/App.resources.json",
                    "true",
                    "runtime",
                    "App.resources.json",
                ),
            ):
                item = ET.SubElement(inputs, "ResolvedAssemblies", Include=path)
                ET.SubElement(item, "PostprocessAssembly").text = postprocess
                ET.SubElement(item, "AssetType").text = asset_type
                ET.SubElement(item, "DestinationSubPath").text = destination
            filters = ET.SubElement(target, "ItemGroup")
            for item in source_items:
                filters.append(copy.deepcopy(item))
            for error in source_target.findall("Error"):
                target.append(copy.deepcopy(error))
            ET.SubElement(
                target,
                "WriteLinesToFile",
                File=str(output),
                Lines="@(_ChummerAssemblyModifierPipelineFile->'%(Filename)%(Extension)')",
                Overwrite="true",
            )
            project = root / "filter.proj"
            ET.ElementTree(probe).write(project, encoding="unicode")

            completed = subprocess.run(
                [dotnet, "msbuild", str(project), "-nologo", "-target:Filter"],
                check=True,
                capture_output=True,
                text=True,
            )
            self.assertEqual("", completed.stderr)
            self.assertEqual(
                [
                    "App.dll",
                    "App.resources.exe",
                    "resources.dll",
                    "App.resources.json",
                ],
                output.read_text(encoding="utf-8").splitlines(),
            )

            unexpected = ET.SubElement(
                inputs,
                "ResolvedAssemblies",
                Include="/inputs/Evil.resources.dll",
            )
            ET.SubElement(unexpected, "PostprocessAssembly").text = "false"
            ET.SubElement(unexpected, "AssetType").text = "resources"
            ET.SubElement(unexpected, "DestinationSubPath").text = "Evil.resources.dll"
            ET.ElementTree(probe).write(project, encoding="unicode")
            rejected = subprocess.run(
                [dotnet, "msbuild", str(project), "-nologo", "-target:Filter"],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(0, rejected.returncode)
            self.assertIn(
                "refused to exclude non-satellite assemblies", rejected.stdout
            )
