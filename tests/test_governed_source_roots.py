from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ElementTree
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "src" / "Chummer.Android" / "Chummer.Android.csproj"
TARGETS = (
    REPO
    / "src"
    / "Chummer.Android"
    / "Chummer.Android.AfterMicrosoftNETSdk.targets"
)
README = REPO / "README.md"
RELEASE_BUILD = REPO / "scripts" / "build-release.sh"
RELEASE_PREPARE = REPO / "scripts" / "prepare-release-inputs.sh"
RELEASE_COMPILE = REPO / "scripts" / "compile-native-release-no-package.sh"
PHYSICAL_CANDIDATE = REPO / "scripts" / "build-api36-physical-candidate.sh"
PREVIEW9_WORKFLOW = REPO / ".github" / "workflows" / "preview9-arm64-aab.yml"


class GovernedSourceRootsTests(unittest.TestCase):
    def test_contract_gates_only_release_or_nonlocal_full_app_builds(self) -> None:
        project = ElementTree.parse(PROJECT).getroot()
        properties = {
            child.tag: child
            for group in project.findall("PropertyGroup")
            for child in group
        }
        self.assertEqual(
            "../../../chummer-presentation",
            properties["ChummerPresentationRoot"].text,
        )
        self.assertEqual(
            "'$(ChummerPresentationRoot)' == ''",
            properties["ChummerPresentationRoot"].attrib["Condition"],
        )
        self.assertEqual(
            "$(ChummerPresentationRoot)/../chummer-core-engine",
            properties["ChummerCoreEngineRoot"].text,
        )
        self.assertEqual(
            "'$(ChummerCoreEngineRoot)' == ''",
            properties["ChummerCoreEngineRoot"].attrib["Condition"],
        )

        targets = ElementTree.parse(TARGETS).getroot()
        guard = targets.find(
            "./Target[@Name='_ChummerRequireExplicitGovernedSourceRoots']"
        )
        self.assertIsNotNone(guard)
        assert guard is not None
        self.assertEqual("PrepareForBuild", guard.attrib["BeforeTargets"])
        self.assertEqual(
            "'$(Configuration)' == 'Release' Or "
            "'$(ChummerUseLocalCompatibilityTree)' == 'false'",
            guard.attrib["Condition"],
        )
        errors = guard.findall("Error")
        self.assertEqual(2, len(errors))
        self.assertEqual(
            {
                "ChummerPresentationRoot": (
                    "'$([System.IO.Path]::IsPathFullyQualified("
                    "$(ChummerPresentationRoot)))' != 'True'"
                ),
                "ChummerCoreEngineRoot": (
                    "'$([System.IO.Path]::IsPathFullyQualified("
                    "$(ChummerCoreEngineRoot)))' != 'True'"
                ),
            },
            {
                name: next(
                    error.attrib["Condition"]
                    for error in errors
                    if name in error.attrib["Text"]
                )
                for name in ("ChummerPresentationRoot", "ChummerCoreEngineRoot")
            },
        )
        for error in errors:
            self.assertIn("ambient legacy sibling discovery is disabled", error.attrib["Text"])

    def test_hostile_msbuild_roots_fail_before_prepare_for_build(self) -> None:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            self.skipTest("dotnet is unavailable")

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            presentation = root / "presentation"
            core = root / "core"
            presentation.mkdir()
            core.mkdir()
            marker = root / "prepare-for-build-ran.txt"
            probe = root / "governed-roots.proj"
            probe.write_text(
                "<Project>\n"
                f'  <Import Project="{TARGETS}" />\n'
                '  <Target Name="PrepareForBuild">\n'
                f'    <WriteLinesToFile File="{marker}" Lines="ran" />\n'
                "  </Target>\n"
                "</Project>\n",
                encoding="utf-8",
            )

            def run(*properties: str) -> subprocess.CompletedProcess[str]:
                marker.unlink(missing_ok=True)
                return subprocess.run(
                    [
                        dotnet,
                        "msbuild",
                        str(probe),
                        "-nologo",
                        "-target:PrepareForBuild",
                        *[f"-property:{value}" for value in properties],
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                )

            for label, properties, expected_root in (
                (
                    "governed-missing",
                    ("Configuration=Debug", "ChummerUseLocalCompatibilityTree=false"),
                    "ChummerPresentationRoot",
                ),
                (
                    "governed-relative-presentation",
                    (
                        "Configuration=Debug",
                        "ChummerUseLocalCompatibilityTree=false",
                        "ChummerPresentationRoot=../ambient-presentation",
                        f"ChummerCoreEngineRoot={core}",
                    ),
                    "ChummerPresentationRoot",
                ),
                (
                    "governed-relative-core",
                    (
                        "Configuration=Debug",
                        "ChummerUseLocalCompatibilityTree=false",
                        f"ChummerPresentationRoot={presentation}",
                        "ChummerCoreEngineRoot=../ambient-core",
                    ),
                    "ChummerCoreEngineRoot",
                ),
                (
                    "release-cannot-opt-back-into-ambient",
                    (
                        "Configuration=Release",
                        "AndroidNETSdkVersion=36.1.69",
                        "ChummerUseLocalCompatibilityTree=true",
                        "ChummerPresentationRoot=../ambient-presentation",
                        "ChummerCoreEngineRoot=../ambient-core",
                    ),
                    "ChummerPresentationRoot",
                ),
            ):
                with self.subTest(label=label):
                    completed = run(*properties)
                    self.assertNotEqual(0, completed.returncode)
                    self.assertIn(
                        f"explicit absolute {expected_root}",
                        completed.stdout + completed.stderr,
                    )
                    self.assertFalse(marker.exists())

            for label, properties in (
                (
                    "ordinary-local-debug-keeps-defaults",
                    (
                        "Configuration=Debug",
                        "AndroidNETSdkVersion=36.2.0",
                        "ChummerPresentationRoot=../../../chummer-presentation",
                        "ChummerCoreEngineRoot=../../../chummer-core-engine",
                    ),
                ),
                (
                    "governed-debug-explicit-absolute",
                    (
                        "Configuration=Debug",
                        "ChummerUseLocalCompatibilityTree=false",
                        f"ChummerPresentationRoot={presentation}",
                        f"ChummerCoreEngineRoot={core}",
                    ),
                ),
                (
                    "release-explicit-absolute",
                    (
                        "Configuration=Release",
                        "AndroidNETSdkVersion=36.1.69",
                        f"ChummerPresentationRoot={presentation}",
                        f"ChummerCoreEngineRoot={core}",
                    ),
                ),
            ):
                with self.subTest(label=label):
                    completed = run(*properties)
                    self.assertEqual(
                        0,
                        completed.returncode,
                        completed.stdout + completed.stderr,
                    )
                    self.assertEqual("ran\n", marker.read_text(encoding="utf-8"))

    def test_governed_entry_points_bind_both_absolute_root_properties(self) -> None:
        release = RELEASE_BUILD.read_text(encoding="utf-8")
        prepare = RELEASE_PREPARE.read_text(encoding="utf-8")
        physical = PHYSICAL_CANDIDATE.read_text(encoding="utf-8")
        compile_only = RELEASE_COMPILE.read_text(encoding="utf-8")
        preview9 = PREVIEW9_WORKFLOW.read_text(encoding="utf-8")

        for script in (release, prepare, physical, compile_only, preview9):
            self.assertIn("ChummerPresentationRoot=", script)
            self.assertIn("ChummerCoreEngineRoot=", script)

        self.assertIn(
            "require_governed_source_root CHUMMER_PRESENTATION_ROOT",
            compile_only,
        )
        self.assertIn(
            "require_governed_source_root CHUMMER_CORE_ENGINE_ROOT",
            compile_only,
        )
        root_guard = compile_only.index(
            "require_governed_source_root CHUMMER_PRESENTATION_ROOT"
        )
        self.assertLess(root_guard, compile_only.index("preflight_native_android_toolchain.py"))
        self.assertLess(root_guard, compile_only.index('build "$project_path"'))
        self.assertIn(
            '-p:ChummerPresentationRoot="$CHUMMER_PRESENTATION_ROOT"',
            compile_only,
        )
        self.assertIn(
            '-p:ChummerCoreEngineRoot="$CHUMMER_CORE_ENGINE_ROOT"',
            compile_only,
        )
        self.assertIn(
            "-p:ChummerPresentationRoot=${{ github.workspace }}/chummer-presentation",
            preview9,
        )
        self.assertIn(
            "-p:ChummerCoreEngineRoot=${{ github.workspace }}/chummer-core-engine",
            preview9,
        )

    def test_release_compile_wrapper_rejects_ambient_roots_before_preflight(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            presentation = root / "presentation"
            core = root / "core"
            presentation.mkdir()
            core.mkdir()
            base_environment = os.environ.copy()
            base_environment.pop("CHUMMER_PRESENTATION_ROOT", None)
            base_environment.pop("CHUMMER_CORE_ENGINE_ROOT", None)

            for label, overrides, expected_root in (
                ("missing", {}, "CHUMMER_PRESENTATION_ROOT"),
                (
                    "relative-presentation",
                    {
                        "CHUMMER_PRESENTATION_ROOT": "../ambient-presentation",
                        "CHUMMER_CORE_ENGINE_ROOT": str(core),
                    },
                    "CHUMMER_PRESENTATION_ROOT",
                ),
                (
                    "relative-core",
                    {
                        "CHUMMER_PRESENTATION_ROOT": str(presentation),
                        "CHUMMER_CORE_ENGINE_ROOT": "../ambient-core",
                    },
                    "CHUMMER_CORE_ENGINE_ROOT",
                ),
            ):
                with self.subTest(label=label):
                    completed = subprocess.run(
                        ["bash", str(RELEASE_COMPILE)],
                        cwd=REPO,
                        env={**base_environment, **overrides},
                        check=False,
                        capture_output=True,
                        text=True,
                    )
                    self.assertEqual(64, completed.returncode)
                    self.assertIn(
                        f"{expected_root} must be an explicit absolute",
                        completed.stderr,
                    )
                    self.assertNotIn("toolchain", completed.stdout)

    def test_documented_boundary_preserves_proof_and_publication_scope(self) -> None:
        readme = README.read_text(encoding="utf-8")
        boundary = readme[
            readme.index("The `sealed_multi_repo_source_assembly` boundary") :
            readme.index("When Android SDK 36 is not available")
        ]
        boundary = " ".join(boundary.split())
        self.assertIn("Configuration=Release", boundary)
        self.assertIn("ChummerUseLocalCompatibilityTree=false", boundary)
        self.assertIn("ChummerPresentationRoot", boundary)
        self.assertIn("ChummerCoreEngineRoot", boundary)
        self.assertIn("locked Core packages", boundary)
        self.assertIn("authorizes neither", boundary)
        self.assertIn("publication nor any additional API 36 journey", boundary)


if __name__ == "__main__":
    unittest.main()
