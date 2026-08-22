import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
COMPILE_PROJECT = (
    REPO
    / "tests"
    / "Chummer.Android.Native.CompileCheck"
    / "Chummer.Android.Native.CompileCheck.csproj"
)


def load_script(name: str):
    path = REPO / "scripts" / f"{name}.py"
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


compile_graph = load_script("verify_native_compile_graph")
toolchain = load_script("preflight_native_android_toolchain")


class NativeCompileProofInfrastructureTests(unittest.TestCase):
    def test_owned_compile_inputs_cover_native_pages_and_platform_stubs(self) -> None:
        compiled, issues = compile_graph.verify_source_graph(REPO, COMPILE_PROJECT)
        self.assertEqual([], issues)
        relative = {path.relative_to(REPO).as_posix() for path in compiled}
        self.assertIn("src/Chummer.Android/Platform/IAndroidImageDocumentService.cs", relative)
        self.assertIn("tests/Chummer.Android.Native.CompileCheck/CompileStubs.cs", relative)
        self.assertTrue(
            {
                path.relative_to(REPO).as_posix()
                for path in (REPO / "src/Chummer.Android/Native").glob("*.cs")
            }.issubset(relative)
        )
        self.assertFalse(any("src/Chummer.Android/Platforms/Android/" in path for path in relative))
        stubs = (
            REPO / "tests/Chummer.Android.Native.CompileCheck/CompileStubs.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("class AndroidImageDocumentService : IAndroidImageDocumentService", stubs)

    def test_generated_graph_accepts_only_current_in_workspace_projects(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary) / "workspace"
            project = workspace / "chummer-android/tests/Compile/Compile.csproj"
            dependency = workspace / "chummer-core/Contracts/Contracts.csproj"
            project.parent.mkdir(parents=True)
            dependency.parent.mkdir(parents=True)
            project.write_text("<Project />\n", encoding="utf-8")
            dependency.write_text("<Project />\n", encoding="utf-8")
            obj = project.parent / "obj"
            obj.mkdir()
            self._write_assets(obj, project, dependency)
            self._write_dgspec(obj, project, dependency)

            referenced, issues = compile_graph.verify_asset_graph(project, workspace)
            self.assertEqual([], issues)
            self.assertEqual({project.resolve(), dependency.resolve()}, set(referenced))

    def test_generated_graph_rejects_deleted_outside_and_copied_authority(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            workspace = root / "workspace"
            project = workspace / "chummer-android/tests/Compile/Compile.csproj"
            copied_from = workspace / "old-android/tests/Compile/Compile.csproj"
            deleted = workspace / "deleted/Contracts.csproj"
            outside = root / "outside/Contracts.csproj"
            project.parent.mkdir(parents=True)
            copied_from.parent.mkdir(parents=True)
            outside.parent.mkdir(parents=True)
            project.write_text("<Project />\n", encoding="utf-8")
            copied_from.write_text("<Project />\n", encoding="utf-8")
            outside.write_text("<Project />\n", encoding="utf-8")
            obj = project.parent / "obj"
            obj.mkdir()
            payload = {
                "project": {"restore": {"projectPath": str(copied_from)}},
                "libraries": {
                    "Deleted/1.0.0": {"type": "project", "path": str(deleted)},
                    "Outside/1.0.0": {"type": "project", "path": str(outside)},
                },
            }
            (obj / "project.assets.json").write_text(json.dumps(payload), encoding="utf-8")
            self._write_dgspec(obj, project, outside)

            _, issues = compile_graph.verify_asset_graph(project, workspace)
            joined = "\n".join(issues)
            self.assertIn("generated-assets-bound-to-different-project", joined)
            self.assertIn("project-reference-missing", joined)
            self.assertIn("project-reference-outside-workspace", joined)

    def test_preflight_reports_missing_sdk_before_compile(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repo, dotnet, java_sdk = self._fake_repo_and_base_toolchain(root)
            payload = toolchain.inspect_toolchain(
                repo,
                str(dotnet),
                root / "missing-android-sdk",
                java_sdk,
                {"HOME": str(root)},
            )
            self.assertEqual("toolchain_missing", payload["status"])
            self.assertEqual(toolchain.TOOLCHAIN_MISSING, payload["exitCode"])
            self.assertIn("android_sdk_missing", {issue["code"] for issue in payload["issues"]})

    def test_preflight_accepts_exact_api_and_latest_patch_sdk(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repo, dotnet, java_sdk = self._fake_repo_and_base_toolchain(root)
            android_sdk = root / "android-sdk"
            android_jar = android_sdk / "platforms/android-36/android.jar"
            aapt2 = android_sdk / "build-tools/36.0.0/aapt2"
            android_jar.parent.mkdir(parents=True)
            aapt2.parent.mkdir(parents=True)
            android_jar.write_bytes(b"android-36")
            aapt2.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
            aapt2.chmod(0o755)

            payload = toolchain.inspect_toolchain(
                repo,
                str(dotnet),
                android_sdk,
                java_sdk,
                {"HOME": str(root)},
            )
            self.assertEqual("ready", payload["status"])
            self.assertEqual(toolchain.READY, payload["exitCode"])
            self.assertEqual("10.0.111", payload["actualDotnetSdk"])

    def test_preflight_rejects_unpinned_sdk_policy(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repo, dotnet, java_sdk = self._fake_repo_and_base_toolchain(root)
            global_json = repo / "global.json"
            policy = json.loads(global_json.read_text(encoding="utf-8"))
            policy["sdk"]["rollForward"] = "major"
            global_json.write_text(json.dumps(policy), encoding="utf-8")

            payload = toolchain.inspect_toolchain(
                repo,
                str(dotnet),
                root / "missing-android-sdk",
                java_sdk,
                {"HOME": str(root)},
            )
            self.assertEqual("invalid_configuration", payload["status"])
            self.assertEqual(toolchain.INVALID_CONFIGURATION, payload["exitCode"])
            self.assertIn(
                "pinned_dotnet_policy_invalid",
                {issue["code"] for issue in payload["issues"]},
            )

    def test_release_wrapper_is_compile_only_and_classifies_post_preflight_failure(self) -> None:
        wrapper = (REPO / "scripts/compile-native-release-no-package.sh").read_text(
            encoding="utf-8"
        )
        preflight = wrapper.index("preflight_native_android_toolchain.py")
        compile_command = wrapper.index('"$dotnet_command" build "$project_path"')
        self.assertLess(preflight, compile_command)
        self.assertIn("-t:Compile", wrapper)
        self.assertIn("--no-restore", wrapper)
        self.assertIn("C# compile failed after the pinned toolchain preflight passed", wrapper)
        for forbidden in ("dotnet restore", "-t:InstallAndroidDependencies", " publish ", " pack "):
            self.assertNotIn(forbidden, wrapper)

    @staticmethod
    def _write_assets(obj: Path, project: Path, dependency: Path) -> None:
        payload = {
            "project": {"restore": {"projectPath": str(project)}},
            "libraries": {
                "Dependency/1.0.0": {
                    "type": "project",
                    "path": str(dependency),
                    "msbuildProject": str(dependency),
                }
            },
        }
        (obj / "project.assets.json").write_text(json.dumps(payload), encoding="utf-8")

    @staticmethod
    def _write_dgspec(obj: Path, project: Path, dependency: Path) -> None:
        payload = {
            "projects": {
                str(project): {
                    "restore": {
                        "projectPath": str(project),
                        "projectUniqueName": str(project),
                    },
                    "frameworks": {
                        "net10.0": {"projectReferences": {str(dependency): {}}}
                    },
                },
                str(dependency): {
                    "restore": {
                        "projectPath": str(dependency),
                        "projectUniqueName": str(dependency),
                    },
                    "frameworks": {},
                },
            }
        }
        (obj / "Compile.csproj.nuget.dgspec.json").write_text(
            json.dumps(payload), encoding="utf-8"
        )

    @staticmethod
    def _fake_repo_and_base_toolchain(root: Path) -> tuple[Path, Path, Path]:
        repo = root / "repo"
        project = repo / "src/Chummer.Android/Chummer.Android.csproj"
        project.parent.mkdir(parents=True)
        project.write_text(
            "<Project><PropertyGroup>"
            "<TargetFramework>net10.0-android36.0</TargetFramework>"
            "<TargetSdkVersion>36</TargetSdkVersion>"
            "</PropertyGroup></Project>\n",
            encoding="utf-8",
        )
        (repo / "global.json").write_text(
            json.dumps(
                {
                    "sdk": {
                        "version": "10.0.110",
                        "rollForward": "latestPatch",
                        "allowPrerelease": False,
                    }
                }
            ),
            encoding="utf-8",
        )
        dotnet_root = root / "dotnet-root"
        dotnet = dotnet_root / "dotnet"
        pack = dotnet_root / "packs/Microsoft.Android.Sdk.Linux/36.1.69"
        pack.mkdir(parents=True)
        dotnet.write_text("#!/bin/sh\nprintf '10.0.111\\n'\n", encoding="utf-8")
        dotnet.chmod(0o755)
        java_sdk = root / "java-sdk"
        (java_sdk / "bin").mkdir(parents=True)
        for name in ("java", "javac"):
            executable = java_sdk / "bin" / name
            executable.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
            executable.chmod(0o755)
        return repo, dotnet, java_sdk


if __name__ == "__main__":
    unittest.main()
