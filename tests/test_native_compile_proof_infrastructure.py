import importlib.util
import json
import os
import subprocess
import sys
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


class NativeCompileSourcePathTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        # Own the fixture workspace even when TMPDIR is nested in a real checkout
        # with an outer integration shelf. Do not inherit the host's marker.
        (self.root / ".integration-worktrees").mkdir()
        self.repo = self.root / "android"
        self.project = self.repo / "tests/Compile/Compile.csproj"
        self.project.parent.mkdir(parents=True)
        self.project.write_text(
            '<Project><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
            '</PropertyGroup><Import Project="NativeCompileInputs.props" /></Project>',
            encoding="utf-8",
        )
        sources = (
            "src/Chummer.Android/MainShell.cs",
            "src/Chummer.Android/MauiProgram.cs",
            "src/Chummer.Android/Platform/IAndroidImageDocumentService.cs",
            "src/Chummer.Android/Native/Page.cs",
            "tests/Compile/CompileStubs.cs",
        )
        includes = []
        for relative in sources:
            source = self.repo / relative
            source.parent.mkdir(parents=True, exist_ok=True)
            source.write_text("// owned fixture source\n", encoding="utf-8")
            includes.append(f'<Compile Include="../../{relative}" />')
        self.manifest = self.project.parent / "NativeCompileInputs.props"
        self.manifest.write_text(
            "<Project><ItemGroup>" + "".join(includes) + "</ItemGroup></Project>",
            encoding="utf-8",
        )

    def test_regular_owned_relative_paths_remain_accepted(self) -> None:
        compiled, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertEqual([], issues)
        self.assertEqual(5, len(compiled))

    def _add_proof_inputs(self) -> str:
        paths = (
            "src/Chummer.Android/Proof/Api36ProofState.cs",
            "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs",
        )
        for relative in paths:
            target = self.repo / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("// owned proof-only fixture source\n", encoding="utf-8")
        group = ('<ItemGroup Condition="\'$(ChummerNativeProofCaptureTests)\' == \'true\'">'
                 + "".join(f'<Compile Include="../../{path}" />' for path in paths)
                 + "</ItemGroup>")
        text = self.manifest.read_text(encoding="utf-8").replace("</Project>", group + "</Project>")
        self.manifest.write_text(text, encoding="utf-8")
        return text

    def test_proof_sources_are_counted_only_in_explicit_opt_in(self) -> None:
        self._add_proof_inputs()
        default, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertEqual([], issues)
        enabled, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=True)
        self.assertEqual([], issues)
        self.assertEqual(5, len(default))
        self.assertEqual(7, len(enabled))
        self.assertEqual({
            self.repo / "src/Chummer.Android/Proof/Api36ProofState.cs",
            self.repo / "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs",
        }, set(enabled) - set(default))

    def test_requested_proof_mode_cannot_silently_use_an_ordinary_manifest(self) -> None:
        _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=True)
        self.assertTrue(any("proof-capture-input-group-count" in issue for issue in issues), issues)
        for forged in ("true", "false", 1, 0, None):
            with self.subTest(forged=forged):
                _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=forged)
                self.assertEqual(["proof-capture-option-must-be-boolean"], issues)

    def test_unknown_manifest_conditions_fail_closed_in_both_modes(self) -> None:
        original = self._add_proof_inputs()
        replacements = (
            ("<Project>", '<Project Condition="true">'),
            ("'$(ChummerNativeProofCaptureTests)' == 'true'", "'$(UnreviewedFlag)' == 'true'"),
            ("'$(ChummerNativeProofCaptureTests)' == 'true'", "'$(ChummerNativeProofCaptureTests)' != 'false'"),
            ('<Compile Include="../../src/Chummer.Android/Native/Page.cs"',
             '<Compile Condition="false" Include="../../src/Chummer.Android/Native/Page.cs"'),
            ('<Compile Include="../../src/Chummer.Android/Native/Page.cs"',
             '<Compile Exclude="**" Include="../../src/Chummer.Android/Native/Page.cs"'),
        )
        for old, new in replacements:
            self.assertIn(old, original)
            for enabled in (False, True):
                with self.subTest(replacement=new, enabled=enabled):
                    self.manifest.write_text(original.replace(old, new), encoding="utf-8")
                    _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=enabled)
                    self.assertTrue(any("owned-input-manifest-invalid" in issue for issue in issues), issues)

    def test_proof_manifest_requires_exact_two_conditioned_sources(self) -> None:
        original = self._add_proof_inputs()
        group_start = original.index('<ItemGroup Condition=')
        group = original[group_start:original.index('</ItemGroup>', group_start) + len('</ItemGroup>')]
        variants = (
            original.replace(' Condition="\'$(ChummerNativeProofCaptureTests)\' == \'true\'"', ""),
            original.replace('<Compile Include="../../src/Chummer.Android/Proof/Api36ProofState.cs" />', ""),
            original.replace('Api36ProofStatePublisher.cs', 'Unapproved.cs'),
            original.replace('</Project>', group + '</Project>'),
            original.replace('<Compile Include="../../src/Chummer.Android/Proof/Api36ProofState.cs" />',
                             '<Compile Include="../../src/Chummer.Android/Proof/Api36ProofStatePublisher.cs" />'),
        )
        for index, text in enumerate(variants):
            for enabled in (False, True):
                with self.subTest(index=index, enabled=enabled):
                    self.manifest.write_text(text, encoding="utf-8")
                    _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=enabled)
                    self.assertTrue(any("proof-capture-input" in issue for issue in issues), issues)

    def test_unconditional_proof_aliases_and_globs_cannot_bypass_opt_in(self) -> None:
        original = self._add_proof_inputs()
        for include in ("../../src/Chummer.Android/Native/../Proof/Api36ProofState.cs",
                        "../../src/Chummer.Android/P?oof/Api36Proof*.cs"):
            for enabled in (False, True):
                with self.subTest(include=include, enabled=enabled):
                    self.manifest.write_text(original.replace("</ItemGroup>",
                        f'<Compile Include="{include}" /></ItemGroup>', 1), encoding="utf-8")
                    _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=enabled)
                    self.assertTrue(any("proof-capture-resolved-input-set-mismatch" in issue
                                        or "compile-input-resolves-more-than-once" in issue for issue in issues), issues)
        extra = self.repo / "src/Chummer.Android/Proof/Unapproved.cs"
        extra.write_text("// unapproved proof fixture\n", encoding="utf-8")
        self.manifest.write_text(original.replace("</ItemGroup>",
            '<Compile Include="../../src/Chummer.Android/Native/../Proof/Unapproved.cs" /></ItemGroup>', 1), encoding="utf-8")
        _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=True)
        self.assertIn("proof-capture-resolved-input-set-mismatch", issues)

    def test_opted_in_proof_file_cannot_be_a_link_or_missing(self) -> None:
        self._add_proof_inputs()
        path = self.repo / "src/Chummer.Android/Proof/Api36ProofState.cs"
        target = path.with_name("Actual.cs")
        path.rename(target)
        _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=True)
        self.assertTrue(any("compile-input-unavailable" in issue for issue in issues), issues)
        path.symlink_to(target.name)
        _, issues = compile_graph.verify_source_graph(self.repo, self.project, proof_capture_tests=True)
        self.assertTrue(any("symlink" in issue for issue in issues), issues)

    def test_cli_records_explicit_proof_mode_and_rejects_ambiguous_options(self) -> None:
        self._add_proof_inputs()
        command = [sys.executable, "-B", str(REPO / "scripts/verify_native_compile_graph.py"),
                   "--repo-root", str(self.repo), "--project", str(self.project)]
        for arguments, count, enabled in (([], 5, False), (["--proof-capture-tests", "false"], 5, False),
                                          (["--proof-capture-tests", "true"], 7, True)):
            with self.subTest(arguments=arguments):
                result = subprocess.run(command + arguments, capture_output=True, text=True, timeout=10, check=False)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                payload = json.loads(result.stdout)
                self.assertIs(enabled, payload["proofCaptureTests"])
                self.assertEqual(count, payload["compiledOwnedSourceCount"])
        for arguments in (["--proof-capture-tests", "1"], ["--proof-capture-tests", "True"],
                          ["--proof-capture-tests", "true", "--assets-only"]):
            with self.subTest(arguments=arguments):
                result = subprocess.run(command + arguments, capture_output=True, text=True, timeout=10, check=False)
                self.assertNotEqual(0, result.returncode)

    def test_msbuild_dot_segment_repo_root_infers_the_canonical_workspace(self) -> None:
        dependency = self.root / "chummer-presentation/Presentation.csproj"
        dependency.parent.mkdir()
        dependency.write_text("<Project />", encoding="utf-8")
        obj = self.project.parent / "obj"
        obj.mkdir()
        NativeCompileProofInfrastructureTests._write_assets(obj, self.project, dependency)
        NativeCompileProofInfrastructureTests._write_dgspec(obj, self.project, dependency)
        # Match $(MSBuildProjectDirectory)/../.. from the embedded Exec target.
        lexical_repo = self.project.parent / "../.."
        for mode in ("--require-assets", "--assets-only"):
            with self.subTest(mode=mode):
                command = [
                    sys.executable, "-B", str(REPO / "scripts/verify_native_compile_graph.py"),
                    "--repo-root", str(lexical_repo), "--project", str(self.project), mode,
                ]
                result = subprocess.run(command, capture_output=True, text=True, timeout=10, check=False)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                payload = json.loads(result.stdout)
                self.assertEqual(str(self.repo.resolve()), payload["repoRoot"])
                self.assertEqual(str(self.root.resolve()), payload["workspaceRoot"])
                self.assertEqual(2, payload["generatedProjectReferenceCount"])
                self.assertEqual([], payload["issues"])
                restricted = subprocess.run(
                    [*command, "--workspace-root", str(self.repo)],
                    capture_output=True, text=True, timeout=10, check=False,
                )
                self.assertEqual(2, restricted.returncode, restricted.stdout + restricted.stderr)
                self.assertIn("project-reference-outside-workspace", restricted.stdout)

    def test_msbuild_dot_segments_cannot_erase_a_symlinked_repo_component(self) -> None:
        target = self.project.parent / "nested"
        target.mkdir()
        alias = self.project.parent / "alias"
        alias.symlink_to(target, target_is_directory=True)
        lexical_repo = alias / "../../.."
        self.assertEqual(self.repo.resolve(), lexical_repo.resolve())
        for mode in ("--require-assets", "--assets-only"):
            with self.subTest(mode=mode):
                result = subprocess.run(
                    [sys.executable, "-B", str(REPO / "scripts/verify_native_compile_graph.py"),
                     "--repo-root", str(lexical_repo), "--project", str(self.project), mode],
                    capture_output=True, text=True, timeout=10, check=False,
                )
                self.assertEqual(2, result.returncode, result.stdout + result.stderr)
                self.assertIn(f"source-root-symlink:{alias}", json.loads(result.stdout)["issues"])

    def test_linked_source_inside_owner_is_rejected_before_resolution(self) -> None:
        source = self.project.parent / "CompileStubs.cs"
        target = source.with_name("Redirected.cs")
        source.rename(target)
        source.symlink_to(target.name)
        _, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertTrue(any("symlink" in issue for issue in issues), issues)

    def test_linked_source_parent_inside_owner_is_rejected(self) -> None:
        source_parent = self.repo / "src/Chummer.Android/Platform"
        target = source_parent.with_name("RedirectedPlatform")
        source_parent.rename(target)
        source_parent.symlink_to(target.name, target_is_directory=True)
        _, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertTrue(any("symlink" in issue for issue in issues), issues)

    def test_linked_compile_project_is_rejected(self) -> None:
        target = self.project.with_name("Redirected.csproj")
        self.project.rename(target)
        self.project.symlink_to(target.name)
        _, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertTrue(any("symlink" in issue for issue in issues), issues)

    def test_linked_owner_root_is_rejected(self) -> None:
        alias = self.root / "alias"
        alias.symlink_to(self.repo.name, target_is_directory=True)
        _, issues = compile_graph.verify_source_graph(alias, alias / "tests/Compile/Compile.csproj")
        self.assertTrue(any("symlink" in issue for issue in issues), issues)

    def test_cli_does_not_erase_linked_owner_root(self) -> None:
        alias = self.root / "alias"
        alias.symlink_to(self.repo.name, target_is_directory=True)
        result = subprocess.run(
            [sys.executable, "-B", str(REPO / "scripts/verify_native_compile_graph.py"),
             "--repo-root", str(alias), "--project", str(alias / "tests/Compile/Compile.csproj")],
            capture_output=True, text=True, timeout=10, check=False,
        )
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        payload = json.loads(result.stdout)
        self.assertEqual("blocked", payload["status"])
        self.assertTrue(any("symlink" in issue for issue in payload["issues"]))

    def test_cli_does_not_erase_linked_compile_project(self) -> None:
        target = self.project.with_name("Redirected.csproj")
        self.project.rename(target)
        self.project.symlink_to(target.name)
        result = subprocess.run(
            [sys.executable, "-B", str(REPO / "scripts/verify_native_compile_graph.py"),
             "--repo-root", str(self.repo), "--project", str(self.project)],
            capture_output=True, text=True, timeout=10, check=False,
        )
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertTrue(any("symlink" in issue for issue in json.loads(result.stdout)["issues"]))

    def test_parent_traversal_does_not_erase_a_linked_input_component(self) -> None:
        target = self.repo / "redirect-target/child"
        target.mkdir(parents=True)
        (target.parent / "Extra.cs").write_text("// redirected source\n", encoding="utf-8")
        alias = self.project.parent / "alias"
        alias.symlink_to(target, target_is_directory=True)
        self.manifest.write_text(
            self.manifest.read_text(encoding="utf-8").replace(
                "</ItemGroup>", '<Compile Include="alias/../Extra.cs" /></ItemGroup>',
            ),
            encoding="utf-8",
        )
        _, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertTrue(any("symlink" in issue for issue in issues), issues)

    def test_missing_required_source_is_reported_without_reading_it(self) -> None:
        (self.repo / "src/Chummer.Android/MauiProgram.cs").unlink()
        _, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertTrue(any("compile-input-unavailable" in issue for issue in issues), issues)

    def test_linked_manifest_remains_rejected(self) -> None:
        target = self.manifest.with_name("Redirected.props")
        self.manifest.rename(target)
        self.manifest.symlink_to(target.name)
        _, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertTrue(issues)

    def test_looping_source_link_fails_closed_without_crashing(self) -> None:
        source = self.project.parent / "CompileStubs.cs"
        source.unlink()
        source.symlink_to(source.name)
        _, issues = compile_graph.verify_source_graph(self.repo, self.project)
        self.assertTrue(any("symlink" in issue for issue in issues), issues)


class NativeCompileProofInfrastructureTests(unittest.TestCase):
    def test_generated_graph_rejects_lexical_symlinks_before_resolution(self) -> None:
        for linked_part in ("project", "dependency", "workspace", "default-obj", "explicit-obj"):
            with self.subTest(linked_part=linked_part), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                workspace = root / "workspace"
                project = workspace / "android/Compile.csproj"
                dependency = workspace / "core/Contracts.csproj"
                project.parent.mkdir(parents=True)
                dependency.parent.mkdir(parents=True)
                project.write_text("<Project />", encoding="utf-8")
                dependency.write_text("<Project />", encoding="utf-8")
                obj = project.parent / "obj"
                obj.mkdir()
                self._write_assets(obj, project, dependency)
                self._write_dgspec(obj, project, dependency)
                selected = {
                    "project": project, "dependency": dependency, "workspace": workspace,
                    "default-obj": obj, "explicit-obj": obj,
                }[linked_part]
                target = selected.with_name(selected.name + "-real")
                selected.rename(target)
                selected.symlink_to(target.name, target_is_directory=target.is_dir())
                _, issues = compile_graph.verify_asset_graph(
                    project, workspace, obj if linked_part == "explicit-obj" else None,
                )
                self.assertTrue(any("symlink" in issue for issue in issues), issues)

    def test_assets_only_cli_preserves_explicit_workspace_identity(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            workspace = root / "workspace"
            project = workspace / "android/Compile.csproj"
            dependency = workspace / "core/Contracts.csproj"
            project.parent.mkdir(parents=True)
            dependency.parent.mkdir(parents=True)
            project.write_text("<Project />", encoding="utf-8")
            dependency.write_text("<Project />", encoding="utf-8")
            obj = project.parent / "obj"
            obj.mkdir()
            self._write_assets(obj, project, dependency)
            self._write_dgspec(obj, project, dependency)
            alias = root / "alias"
            alias.symlink_to(workspace.name, target_is_directory=True)
            result = subprocess.run(
                [sys.executable, "-B", str(REPO / "scripts/verify_native_compile_graph.py"),
                 "--repo-root", str(project.parent), "--project", str(project),
                 "--workspace-root", str(alias), "--assets-only"],
                capture_output=True, text=True, timeout=10, check=False,
            )
            self.assertEqual(2, result.returncode, result.stdout + result.stderr)
            self.assertTrue(any("symlink" in issue for issue in json.loads(result.stdout)["issues"]))

    def test_default_workspace_includes_validated_integration_shelves(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary) / "workspace"
            repo = workspace / ".integration-worktrees/coherent/chummer-android"
            repo.mkdir(parents=True)
            self.assertEqual(workspace, compile_graph._default_workspace_root(repo))

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

    def test_generated_graph_can_bind_one_explicit_isolated_restore_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            workspace = root / "workspace"
            project = workspace / "chummer-android/tests/Compile/Compile.csproj"
            dependency = workspace / "chummer-core/Contracts/Contracts.csproj"
            project.parent.mkdir(parents=True)
            dependency.parent.mkdir(parents=True)
            project.write_text("<Project />\n", encoding="utf-8")
            dependency.write_text("<Project />\n", encoding="utf-8")
            isolated = root / "private-release/obj"
            isolated.mkdir(parents=True)
            self._write_assets(isolated, project, dependency)
            self._write_dgspec(isolated, project, dependency)

            referenced, issues = compile_graph.verify_asset_graph(
                project, workspace, isolated,
            )

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
