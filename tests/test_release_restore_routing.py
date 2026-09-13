"""Execute the release scripts' actual graph-check commands, never a build/signing lane."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]


def commands(script_name, tool):
    lines = (REPO / "scripts" / script_name).read_text(encoding="utf-8").splitlines(keepends=True)
    result = []
    prefix = 'python3 "$repo_dir/scripts/' + tool + '"'
    for index, line in enumerate(lines):
        if not line.startswith(prefix):
            continue
        block = [line]
        while block[-1].rstrip().endswith("\\"):
            index += 1
            block.append(lines[index])
        result.append("".join(block))
    return result


class ReleaseRestoreRoutingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="release routing ")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.workspace = self.root / "workspace"
        self.repo = self.workspace / "chummer-android"
        self.project = self.repo / "src/Chummer.Android/Chummer.Android.csproj"
        self.dependency = self.workspace / "chummer-presentation/Chummer.Presentation/Chummer.Presentation.csproj"
        for project in (self.project, self.dependency):
            project.parent.mkdir(parents=True)
            project.write_text("<Project />", encoding="utf-8")
        scripts = self.repo / "scripts"
        scripts.mkdir()
        (scripts / "verify_native_compile_graph.py").write_bytes(
            (REPO / "scripts/verify_native_compile_graph.py").read_bytes()
        )
        self.intermediate = self.root / "private release/intermediate"
        self.assets_root = self.intermediate / "Chummer.Android"
        self.write_restore(self.assets_root)

    def write_restore(self, directory, project=None):
        project = project or self.project
        directory.mkdir(parents=True)
        assets = {
            "project": {"restore": {"projectPath": str(project)}},
            "libraries": {"Presentation/1.0": {
                "type": "project", "path": str(self.dependency),
                "msbuildProject": str(self.dependency),
            }},
        }
        dgspec = {"projects": {
            str(project): {
                "restore": {"projectPath": str(project), "projectUniqueName": str(project)},
                "frameworks": {"net10.0": {"projectReferences": {str(self.dependency): {}}}},
            },
            str(self.dependency): {
                "restore": {"projectPath": str(self.dependency), "projectUniqueName": str(self.dependency)},
                "frameworks": {},
            },
        }}
        (directory / "project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        (directory / "Chummer.Android.csproj.nuget.dgspec.json").write_text(json.dumps(dgspec), encoding="utf-8")

    def invoke_graph_command(self, script_name, intermediate=None):
        blocks = commands(script_name, "verify_native_compile_graph.py")
        self.assertEqual(1, len(blocks))
        environment = {
            "PATH": os.environ["PATH"], "LANG": "C.UTF-8", "PYTHONDONTWRITEBYTECODE": "1",
            "repo_dir": str(self.repo), "workspace_root": str(self.workspace),
            "project_path": str(self.project),
            "release_intermediate": str(intermediate or self.intermediate),
            "preparation_obj": str(intermediate or self.intermediate),
        }
        result = subprocess.run(["bash", "-euc", blocks[0]], env=environment,
                                capture_output=True, text=True, timeout=10, check=False)
        return result, json.loads(result.stdout)

    def test_both_release_commands_consume_external_restore_without_workspace_obj(self):
        self.assertFalse((self.project.parent / "obj").exists())
        for script in ("prepare-release-inputs.sh", "build-release.sh"):
            with self.subTest(script=script):
                result, receipt = self.invoke_graph_command(script)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual("pass", receipt["status"])
                self.assertEqual(2, receipt["generatedProjectReferenceCount"])
                self.assertEqual(str(self.project), receipt["compileProject"])

    def test_missing_external_restore_never_falls_back_to_valid_ambient_obj(self):
        self.write_restore(self.project.parent / "obj")
        result, receipt = self.invoke_graph_command("build-release.sh", self.root / "missing")
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("generated-assets-missing" in issue for issue in receipt["issues"]))

    def test_malformed_external_restore_never_falls_back_to_valid_ambient_obj(self):
        self.write_restore(self.project.parent / "obj")
        (self.assets_root / "project.assets.json").write_text("not json", encoding="utf-8")
        result, receipt = self.invoke_graph_command("build-release.sh")
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("generated-assets-invalid" in issue for issue in receipt["issues"]))

    def test_linked_external_root_is_rejected(self):
        alias = self.root / "alias"
        alias.symlink_to(self.intermediate, target_is_directory=True)
        result, receipt = self.invoke_graph_command("build-release.sh", alias)
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("generated-assets-root-symlink" in issue for issue in receipt["issues"]))

    def test_external_restore_must_bind_the_exact_android_project(self):
        other = self.workspace / "old-android/Other.csproj"
        other.parent.mkdir()
        other.write_text("<Project />", encoding="utf-8")
        payload = json.loads((self.assets_root / "project.assets.json").read_text())
        payload["project"]["restore"]["projectPath"] = str(other)
        (self.assets_root / "project.assets.json").write_text(json.dumps(payload), encoding="utf-8")
        result, receipt = self.invoke_graph_command("build-release.sh")
        self.assertEqual(2, result.returncode)
        self.assertTrue(any("different-project" in issue for issue in receipt["issues"]))

    def test_every_sealer_call_binds_the_same_root_and_exact_publish_phase(self):
        blocks = commands("build-release.sh", "seal_release_restore_consumption.py")
        materialize = [block for block in blocks if block.splitlines()[0].endswith(' materialize \\')]
        verify = [block for block in blocks if block.splitlines()[0].endswith(' verify \\')]
        self.assertEqual(1, len(materialize))
        self.assertEqual(2, len(verify))
        for block in [*materialize, *verify]:
            self.assertEqual(1, block.count('--intermediate-root "$release_intermediate"'))
        self.assertNotIn("--phase", materialize[0])
        self.assertIn("--phase pre-publish", verify[0])
        self.assertNotIn("--phase post-publish", verify[0])
        self.assertIn("--phase post-publish", verify[1])
        self.assertNotIn("--phase pre-publish", verify[1])
        script = (REPO / "scripts/build-release.sh").read_text()
        restore = script.index('"$dotnet_command" restore')
        publish = script.index('"$dotnet_command" publish')
        self.assertLess(restore, script.index(materialize[0]))
        self.assertLess(script.index(verify[0]), publish)
        self.assertLess(publish, script.index(verify[1]))
        self.assertEqual(2, script.count('-p:ChummerReleaseIntermediateRoot="$release_intermediate"'))

    def test_real_restore_command_routing_and_clean_child_environment(self):
        """Run real routing/clean_exec blocks with an argv recorder, never dotnet."""
        recorder = self.root / "record-tool"
        recorder.write_text('#!/usr/bin/python3\nimport json,os,sys\n'
                            'print(json.dumps({"argv":sys.argv[1:],"env":dict(os.environ)}))\n')
        recorder.chmod(0o700)
        for script_name in ("build-release.sh", "prepare-release-inputs.sh"):
            script = (REPO / "scripts" / script_name).read_text()
            clean_start = script.index('release_child_home=""')
            clean_end = script.index("\n}\n", script.index("clean_exec()", clean_start)) + 3
            clean = script[clean_start:clean_end]
            package_arguments = ""
            if script_name == "prepare-release-inputs.sh":
                arguments_start = script.index("package_arguments=(\n")
                arguments_end = script.index("\n)\n", arguments_start) + 3
                package_arguments = script[arguments_start:arguments_end]
            route_start = script.index("# Offline ")
            route_end = script.index('python3 "$repo_dir/scripts/seal_release_restore_consumption.py" assert-clean', route_start) if script_name == "build-release.sh" else script.index("export DOTNET_CLI_USE_MSBUILD_SERVER=0", route_start)
            routing = script[route_start:route_end]
            restore_start = script.index('clean_exec "$dotnet_command" restore')
            restore_lines = script[restore_start:].splitlines(keepends=True)
            restore = []
            for line in restore_lines:
                restore.append(line)
                if not line.rstrip().endswith("\\"):
                    break
            for offline in (False, True):
                with self.subTest(script=script_name, offline=offline):
                    input_dir = self.root / f"{script_name}-{offline}"
                    input_dir.mkdir(mode=0o700)
                    external = input_dir / "external"
                    external.mkdir(mode=0o700)
                    selected = input_dir / "selected"
                    selected.mkdir(mode=0o700)
                    environment = {
                        "PATH": "/usr/bin:/bin", "repo_dir": str(self.repo),
                        "workspace_root": str(self.workspace), "project_path": str(self.project),
                        "presentation_root": str(self.workspace / "chummer-presentation"),
                        "core_root": str(self.workspace / "chummer-core-engine"),
                        "dotnet_command": str(recorder), "selected_package_feed": str(selected),
                        "nuget_org_source": "https://api.nuget.org/v3/index.json",
                        "CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED": str(input_dir / "owner"),
                        "CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY": str(input_dir / "authority.json"),
                        "authority": str(input_dir / "authority.json"), "input_dir": str(input_dir),
                        "isolated_packages": str(input_dir / "fresh-cache"),
                        "nuget_packages": str(input_dir / "fresh-cache"), "NUGET_PACKAGES": str(input_dir / "fresh-cache"),
                        "runtime_id": "android-arm64", "routed_locks": str(input_dir / "locks"),
                        "project_locks": str(input_dir / "locks"),
                        "preparation_obj": str(input_dir / "intermediate"),
                        "release_intermediate": str(input_dir / "intermediate"),
                        "core_version": "1.2.3", "campaign_version": "1.2.3", "run_version": "1.2.3",
                        "registry_version": "1.2.3", "ui_kit_version": "1.2.3",
                        "AndroidSdkDirectory": "/not-executed/android", "JavaSdkDirectory": "/not-executed/java",
                        "HTTPS_PROXY": "must-not-leak", "http_proxy": "must-not-leak",
                        "NUGET_PLUGIN_PATHS": "must-not-leak", "RestoreSources": "must-not-leak",
                        "RestoreForceEvaluate": "true", "RestoreLockedMode": "false",
                        "RestorePackagesWithLockFile": "false",
                    }
                    if offline:
                        environment["CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED"] = str(external)
                    program = 'set -euo pipefail\n' + clean + '\n' + (
                        'require_exact_directory() { [[ -n "${!1}" && -d "${!1}" ]]; }\n'
                        'fail() { exit 91; }\n'
                        'python3() { /usr/bin/python3 -c \'import json,sys; print(json.dumps({"snapshot":sys.argv[1:]}))\' "$@"; }\n'
                    ) + package_arguments + '\n' + routing + '\n' + ''.join(restore)
                    result = subprocess.run(["/bin/bash", "-p", "-c", program], env=environment,
                                            text=True, capture_output=True, timeout=10, check=False)
                    self.assertEqual(0, result.returncode, result.stderr)
                    observed = [json.loads(line) for line in result.stdout.splitlines()]
                    command = observed[-1]["argv"]
                    sources = [command[index + 1] for index, item in enumerate(command) if item == "--source"]
                    expected_selected = str(input_dir / "selected-release-feed") if script_name == "prepare-release-inputs.sh" else str(selected)
                    if offline:
                        self.assertEqual([expected_selected], sources)
                        self.assertNotIn("https://api.nuget.org/v3/index.json", command)
                        snapshot = observed[0]["snapshot"]
                        self.assertEqual(str(external), snapshot[snapshot.index("--external-source") + 1])
                        self.assertEqual(str(self.repo / "src/Chummer.Android/packages.lock.json"), snapshot[snapshot.index("--project-lock") + 1])
                    else:
                        owner = str(input_dir / "owner") if script_name == "prepare-release-inputs.sh" else str(selected)
                        self.assertEqual([owner, "https://api.nuget.org/v3/index.json"], sources)
                    for flag in ("--locked-mode", "--disable-parallel", "--no-http-cache",
                                 "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true"):
                        self.assertIn(flag, command)
                    # Qualified consumption must not bypass native lock/hash validation.
                    # Scope this to the actual argv, not deliberately owned generation lanes.
                    for argument in command:
                        self.assertNotIn("--force-evaluate", argument.casefold())
                        self.assertNotIn("restoreforceevaluate", argument.casefold())
                    self.assertEqual(str(input_dir / "fresh-cache"), command[command.index("--packages") + 1])
                    child = observed[-1]["env"]
                    for forbidden in ("HTTPS_PROXY", "http_proxy", "NUGET_PLUGIN_PATHS", "RestoreSources",
                                      "CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED", "RestoreForceEvaluate",
                                      "RestoreLockedMode", "RestorePackagesWithLockFile"):
                        self.assertNotIn(forbidden, child)

    def test_preparation_exports_transport_path_not_a_prepared_cache_authority(self):
        script = (REPO / "scripts/prepare-release-inputs.sh").read_text()
        exports = script[script.index("{\n  printf 'export CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY="):]
        self.assertIn("export CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED=%q", exports)
        self.assertNotIn("selected-release-feed", exports)
        release = (REPO / "scripts/build-release.sh").read_text()
        self.assertIn('isolated_packages="$release_tmp/nuget-packages"', release)
        self.assertIn('NUGET_PACKAGES="$isolated_packages"', release)


if __name__ == "__main__":
    unittest.main()
