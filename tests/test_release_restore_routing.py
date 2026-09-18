"""Exercise actual release command blocks with recorders, never an SDK/signing lane."""

import json
import os
from pathlib import Path
import re
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

    def assert_exact_routing(self, command, input_dir):
        properties = {}
        for argument in command:
            for prefix in ("-p:", "/p:", "-property:", "/property:"):
                if argument.casefold().startswith(prefix):
                    name, separator, value = argument[len(prefix):].partition("=")
                    self.assertEqual("=", separator)
                    properties.setdefault(name.casefold(), []).append(value)
                    break
        for forbidden in ("custombeforemicrosoftcommonprops", "directorybuildpropspath",
                          "baseintermediateoutputpath", "msbuildprojectextensionspath", "nugetlockfilepath"):
            self.assertNotIn(forbidden, properties)
        for name, value in {
            "CustomBeforeDirectoryBuildProps": self.repo / "eng/ReleaseRestoreRouting.props",
            "ChummerReleaseLockRoot": input_dir / "locks",
            "ChummerReleaseIntermediateRoot": input_dir / "intermediate",
            "ChummerPresentationRoot": self.workspace / "chummer-presentation",
            "ChummerCoreEngineRoot": self.workspace / "chummer-core-engine",
        }.items():
            self.assertEqual([str(value)], properties.get(name.casefold()), name)

    def test_real_restore_and_publish_routing_and_clean_child_environment(self):
        """Run real routing/clean_exec blocks with an argv recorder, never dotnet."""
        recorder = self.root / "record-tool"
        recorder.write_text('#!/usr/bin/python3\nimport json,os,sys\n'
                            'print(json.dumps({"argv":sys.argv[1:],"env":dict(os.environ)}))\n')
        recorder.chmod(0o700)
        for script_name, operation in (("build-release.sh", "restore"),
                                       ("prepare-release-inputs.sh", "restore"),
                                       ("build-release.sh", "publish")):
            script = (REPO / "scripts" / script_name).read_text()
            clean_start = script.index('release_child_home=""')
            clean_end = script.index("\n}\n", script.index("clean_exec()", clean_start)) + 3
            clean = script[clean_start:clean_end]
            temporary_selection = re.search(r'mkdir -m 0700 -- "\$(?:release_tmp|input_dir)/[^"\n]+"\nrelease_child_tmp="[^"\n]+"\n', script).group(0)
            self.assertLess(script.index(temporary_selection), script.index('clean_exec "$dotnet_command" restore'))
            package_arguments = ""
            if script_name == "prepare-release-inputs.sh":
                arguments_start = script.index("package_arguments=(\n")
                arguments_end = script.index("\n)\n", arguments_start) + 3
                package_arguments = script[arguments_start:arguments_end]
            route_start = script.index("# Offline ")
            route_end = script.index('python3 "$repo_dir/scripts/seal_release_restore_consumption.py" assert-clean', route_start) if script_name == "build-release.sh" else script.index("export DOTNET_CLI_USE_MSBUILD_SERVER=0", route_start)
            routing = script[route_start:route_end] if operation == "restore" else ""
            command_start = script.index('clean_exec "$dotnet_command" ' + operation)
            command_lines = script[command_start:].splitlines(keepends=True)
            actual_command = []
            for line in command_lines:
                actual_command.append(line)
                if not line.rstrip().endswith("\\"):
                    break
            for offline in (False, True):
                with self.subTest(script=script_name, operation=operation, offline=offline):
                    input_dir = self.root / f"{script_name}-{operation}-{offline}"
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
                        "release_tmp": str(input_dir), "TMPDIR": "/hostile/ambient-tmp",
                        "release_child_tmp": "/hostile/caller-selected-tmp",
                        "isolated_packages": str(input_dir / "fresh-cache"),
                        "nuget_packages": str(input_dir / "fresh-cache"), "NUGET_PACKAGES": str(input_dir / "fresh-cache"),
                        "runtime_id": "android-arm64", "routed_locks": str(input_dir / "locks"),
                        "configuration": "Release", "framework": "net10.0-android36.0",
                        "staged_publish_dir": str(input_dir / "publish"),
                        "release_aar_cache": str(input_dir / "aar-cache"),
                        "version_name": "0.1.0-preview.12", "version_code": "12",
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
                        "XamarinBuildDownloadDir": "/hostile/ambient-aar-cache",
                        "CHUMMER_ANDROID_RELEASE_OFFLINE_AAR_FEED": "must-not-leak",
                    }
                    hostile_routing = ("CustomBeforeMicrosoftCommonProps", "CustomBeforeDirectoryBuildProps",
                                       "DirectoryBuildPropsPath", "ChummerReleaseLockRoot",
                                       "ChummerReleaseIntermediateRoot", "NuGetLockFilePath",
                                       "BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath",
                                       "PathMap", "ChummerReleaseDeterministicRoot", "Deterministic",
                                       "ContinuousIntegrationBuild", "AndroidAotAdditionalArguments")
                    environment.update({name: "/hostile/wrong-root" for name in hostile_routing})
                    if offline:
                        environment["CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED"] = str(external)
                    program = 'set -euo pipefail\n' + clean + '\n' + (
                        'require_exact_directory() { [[ -n "${!1}" && -d "${!1}" ]]; }\n'
                        'fail() { exit 91; }\n'
                        'python3() { /usr/bin/python3 -c \'import json,sys; print(json.dumps({"snapshot":sys.argv[1:]}))\' "$@"; }\n'
                        'mkdir() { clean_exec /usr/bin/mkdir "$@"; }\n'
                        'clean_exec "$dotnet_command" preinit\n'
                    ) + temporary_selection + package_arguments + '\n' + routing + '\n' + ''.join(actual_command)
                    result = subprocess.run(["/bin/bash", "-p", "-c", program], env=environment,
                                            text=True, capture_output=True, timeout=10, check=False)
                    self.assertEqual(0, result.returncode, result.stderr)
                    observed = [json.loads(line) for line in result.stdout.splitlines()]
                    before = observed.pop(0)
                    self.assertEqual(["preinit"], before["argv"])
                    self.assertNotIn("TMPDIR", before["env"])
                    command = observed[-1]["argv"]
                    self.assertEqual([operation, str(self.project)], command[:2])
                    self.assert_exact_routing(command, input_dir)
                    sources = [command[index + 1] for index, item in enumerate(command) if item == "--source"]
                    expected_selected = str(input_dir / "selected-release-feed") if script_name == "prepare-release-inputs.sh" else str(selected)
                    if operation == "publish":
                        self.assertEqual([], sources)
                        self.assertEqual(1, len(observed))
                        self.assertIn("--no-restore", command)
                        self.assertIn("-p:AndroidKeyStore=false", command)
                        for name, value in {
                            "Deterministic": "true", "ContinuousIntegrationBuild": "true",
                            "AndroidAotAdditionalArguments": "deterministic",
                            "ChummerReleaseDeterministicRoot": str(input_dir),
                        }.items():
                            self.assertEqual([f"-p:{name}={value}"], [arg for arg in command
                                             if arg.casefold().startswith(f"-p:{name}=".casefold())])
                        self.assertFalse(any(arg.casefold().startswith("-p:pathmap=") for arg in command))
                        self.assertNotIn("-p:AndroidEnableProfiledAot=false", command)
                        self.assertNotIn("-p:DebugType=none", command)
                        self.assertEqual([f'-p:XamarinBuildDownloadDir={input_dir}/aar-cache/'],
                                         [arg for arg in command if "xamarinbuilddownloaddir" in arg.casefold()])
                    elif offline:
                        self.assertEqual([expected_selected], sources)
                        self.assertNotIn("https://api.nuget.org/v3/index.json", command)
                        snapshot = observed[0]["snapshot"]
                        self.assertEqual(str(external), snapshot[snapshot.index("--external-source") + 1])
                        self.assertEqual(str(self.repo / "src/Chummer.Android/packages.lock.json"), snapshot[snapshot.index("--project-lock") + 1])
                    else:
                        owner = str(input_dir / "owner") if script_name == "prepare-release-inputs.sh" else str(selected)
                        self.assertEqual([owner, "https://api.nuget.org/v3/index.json"], sources)
                    for flag in ("-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true"):
                        self.assertIn(flag, command)
                    if operation == "restore":
                        for flag in ("--locked-mode", "--disable-parallel", "--no-http-cache"):
                            self.assertIn(flag, command)
                        self.assertEqual(str(input_dir / "fresh-cache"), command[command.index("--packages") + 1])
                    # Qualified consumption must not bypass native lock/hash validation.
                    # Scope this to the actual argv, not deliberately owned generation lanes.
                    for argument in command:
                        self.assertNotIn("--force-evaluate", argument.casefold())
                        self.assertNotIn("restoreforceevaluate", argument.casefold())
                    child = observed[-1]["env"]
                    expected_tmp = input_dir / ("child-tmp" if script_name == "build-release.sh" else "preparation-tmp")
                    self.assertEqual(str(expected_tmp), child["TMPDIR"])
                    self.assertEqual(0o700, expected_tmp.stat().st_mode & 0o777)
                    self.assertEqual(os.getuid(), expected_tmp.stat().st_uid)
                    self.assertEqual(before["env"]["HOME"], child["HOME"])
                    self.assertNotEqual(child["HOME"], child["TMPDIR"])
                    for forbidden in ("HTTPS_PROXY", "http_proxy", "NUGET_PLUGIN_PATHS", "RestoreSources",
                                      "CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED", "RestoreForceEvaluate",
                                      "RestoreLockedMode", "RestorePackagesWithLockFile", *hostile_routing):
                        self.assertNotIn(forbidden, child)
                    for forbidden in ("XamarinBuildDownloadDir", "CHUMMER_ANDROID_RELEASE_OFFLINE_AAR_FEED"):
                        self.assertNotIn(forbidden, child)
                    # These mutations of the recorded real argv must fail the same exact routing contract.
                    late = [arg.replace("CustomBeforeDirectoryBuildProps", "CustomBeforeMicrosoftCommonProps") for arg in command]
                    mutations = [("late-hook", late), ("missing-early-hook", [arg for arg in command
                                 if not arg.startswith("-p:CustomBeforeDirectoryBuildProps=")])]
                    for name in ("DirectoryBuildPropsPath", "BaseIntermediateOutputPath",
                                 "MSBuildProjectExtensionsPath", "NuGetLockFilePath"):
                        mutations.append((name, command + [f"/property:{name}=/hostile/replacement"]))
                    for name in ("CustomBeforeDirectoryBuildProps", "ChummerReleaseLockRoot",
                                 "ChummerReleaseIntermediateRoot", "ChummerPresentationRoot", "ChummerCoreEngineRoot"):
                        mutations.append((f"wrong-{name}", [f"-p:{name}=/hostile/wrong-root" if arg.startswith(f"-p:{name}=") else arg
                                                          for arg in command]))
                        mutations.append((f"duplicate-{name}", command + [f"/property:{name}=/hostile/duplicate-root"]))
                    for label, mutation in mutations:
                        with self.subTest(rejected_routing=label), self.assertRaises(AssertionError):
                            self.assert_exact_routing(mutation, input_dir)

    def test_preparation_exports_transport_path_not_a_prepared_cache_authority(self):
        script = (REPO / "scripts/prepare-release-inputs.sh").read_text()
        exports = script[script.index("{\n  printf 'export CHUMMER_ANDROID_RELEASE_PACKAGE_AUTHORITY="):]
        self.assertIn("export CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED=%q", exports)
        self.assertNotIn("selected-release-feed", exports)
        release = (REPO / "scripts/build-release.sh").read_text()
        self.assertIn('isolated_packages="$release_tmp/nuget-packages"', release)
        self.assertIn('NUGET_PACKAGES="$isolated_packages"', release)
        self.assertIn("export CHUMMER_ANDROID_RELEASE_OFFLINE_AAR_FEED=%q", exports)
        self.assertNotIn("XamarinBuildDownloadDir", exports)
        self.assertNotIn("aar-cache", exports)

    def test_aar_seed_and_checks_bracket_publish_without_preseeded_extractions(self):
        script = (REPO / "scripts/build-release.sh").read_text()
        publish = script.index('clean_exec "$dotnet_command" publish')
        self.assertLess(script.index('prepare_android_aar_cache.py" seed'), publish)
        checks = [match.start() for match in re.finditer('prepare_android_aar_cache.py" verify', script)]
        self.assertEqual(2, len(checks))
        self.assertLess(checks[0], publish); self.assertLess(publish, checks[1])
        self.assertIn('release_aar_cache="$release_tmp/aar-cache"', script)
        preparation = (REPO / "scripts/prepare-release-inputs.sh").read_text()
        self.assertIn('prepare_android_aar_cache.py" validate', preparation)
        self.assertNotIn('prepare_android_aar_cache.py" seed', preparation)
        for text in (script, preparation):
            for protected in ("AndroidSdkDirectory", "JavaSdkDirectory", "CHUMMER_ANDROID_RELEASE_OFFLINE_NUGET_FEED"):
                self.assertIn(f'--exclude-root "${protected}"', text)
            self.assertIn('--exclude-root "$(dirname -- "$dotnet_command")"', text)

    def test_real_aar_shell_blocks_preserve_unset_empty_and_explicit_source(self):
        script = (REPO / "scripts/build-release.sh").read_text()
        start = script.index("# Build-download state")
        seed = script[start:script.index("install -m 0600", start)]
        checks = []
        for match in re.finditer('prepare_android_aar_cache.py" verify', script):
            checks.append(script[script.rfind("if [[", 0, match.start()):script.index("\nfi", match.start()) + 3])
        for source in (None, "", str(self.root / "explicit-source")):
            environment = {"PATH": "/usr/bin:/bin", "repo_dir": str(self.repo), "release_tmp": str(self.root),
                           "isolated_packages": str(self.root / "nuget"), "selected_package_feed": str(self.root / "selected")}
            if source is not None:
                environment["CHUMMER_ANDROID_RELEASE_OFFLINE_AAR_FEED"] = source
            program = 'set -euo pipefail\naar_protected_roots=()\nfail() { exit 91; }\n' + (
                'python3() { /usr/bin/python3 -c \'import json,sys;print(json.dumps(sys.argv[1:]))\' "$@"; }\n') + seed + "\n" + "\n".join(checks)
            result = subprocess.run(["bash", "-p", "-c", program], env=environment, capture_output=True, text=True, timeout=5)
            self.assertEqual(0, result.returncode, result.stderr)
            commands = [json.loads(line) for line in result.stdout.splitlines()]
            self.assertEqual(["seed"] if source is None else ["seed", "verify", "verify"], [row[1] for row in commands])
            for row in commands:
                self.assertEqual(str(self.root / "aar-cache"), row[row.index("--cache") + 1])
                self.assertEqual(source is not None, "--source" in row)
                if source is not None:
                    self.assertEqual(source, row[row.index("--source") + 1])


if __name__ == "__main__":
    unittest.main()
