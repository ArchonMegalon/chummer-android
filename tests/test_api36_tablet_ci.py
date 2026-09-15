"""Lightweight hosted adapter contracts. All Android/Java execution is mocked."""

from contextlib import ExitStack
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
spec = importlib.util.spec_from_file_location("tablet_ci", ROOT / "scripts/run-api36-tablet-skill-group-ci.py")
ci = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = ci
spec.loader.exec_module(ci)


class TabletCiTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name)
        self.repo = self.path / "source"
        for relative in (".github/workflows/api36-editing-e2e.yml", "scripts/build-debug.sh",
                         "eng/api36-sr5-wizard-gate-authority.json"):
            target = self.repo / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("test source\n")
        directory = self.path / "chummer-android-apk"
        directory.mkdir()
        self.apk = directory / ci.APK_NAME
        self.apk.write_bytes(b"unit-test APK bytes, not a device artifact")
        self.apk_sha = hashlib.sha256(self.apk.read_bytes()).hexdigest()
        self.sidecar = directory / (ci.APK_NAME + ".sha256")
        self.sidecar.write_text(f"{self.apk_sha}  {ci.APK_NAME}\n")
        self.content = directory / "chummer-android-content-bundle-receipt.json"
        self.content.write_text(json.dumps({"schema": "chummer.android.content-bundle/v1", "status": "pass",
            "apkVerified": True, "apkSha256": self.apk_sha, "coreRevision": ci.CORE_CONTENT, "issues": []}))
        self.environment = {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "workflow_dispatch",
            "CHUMMER_TABLET_SKILL_GROUP_OPT_IN": "true", "CHUMMER_E2E_PROFILE": "tablet",
            "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "2", "GITHUB_SHA": "a" * 40,
            "CHUMMER_E2E_APK_ARTIFACT_ATTEMPT": "1", "CHUMMER_E2E_APK_ARTIFACT_ID": "456",
            "CHUMMER_E2E_APK_ARTIFACT_DIGEST": "sha256:" + "d" * 64,
            "CHUMMER_E2E_APK_ARTIFACT_NAME": "chummer-android-api36-x64-debug-123-1",
            "CHUMMER_E2E_APK_SHA256": self.apk_sha, "RUNNER_TEMP": str(self.path)}
        self.source = {"sourceCommit": "a" * 40, "sourceTree": "b" * 40}

    def preflight(self, environment=None):
        with patch.object(ci, "source_binding", return_value=dict(self.source)):
            return ci.preflight(environment or self.environment, root=self.repo)

    def test_rerun_binds_apk_producing_attempt_not_current_execution(self):
        with patch.object(ci.tablet, "TabletDevice") as device, patch.object(ci.authority, "sdk_executable") as sdk:
            result = self.preflight()
        device.assert_not_called()
        sdk.assert_not_called()
        self.assertEqual(result.artifact["executionAttempt"], 2)
        self.assertEqual(result.artifact["producerAttempt"], 1)
        self.assertEqual(result.artifact["proofBuildId"], "hosted-123-1")
        self.assertEqual(result.artifact["artifactDigest"], "d" * 64)
        self.assertIs(result.artifact["archiveDigestIndependentlyRecomputed"], False)
        result.recheck()

    def test_opt_out_or_wrong_event_fails_before_source_sdk_or_device(self):
        mutations = {"CHUMMER_TABLET_SKILL_GROUP_OPT_IN": ("false", "", "True"),
                     "GITHUB_EVENT_NAME": ("push", "pull_request", "merge_group"),
                     "GITHUB_ACTIONS": ("false", ""), "CHUMMER_E2E_PROFILE": ("phone", "")}
        for key, values in mutations.items():
            for value in values:
                with self.subTest(key=key, value=value), patch.object(ci, "source_binding") as source, \
                     patch.object(ci.tablet, "TabletDevice") as device, patch.object(ci.authority, "sdk_executable") as sdk:
                    with self.assertRaisesRegex(RuntimeError, "manual tablet opt-in"):
                        ci.preflight({**self.environment, key: value}, root=self.repo)
                    source.assert_not_called()
                    sdk.assert_not_called()
                    device.assert_not_called()

    def test_artifact_binding_rejects_cross_run_attempt_name_or_digest(self):
        mutations = {"CHUMMER_E2E_APK_ARTIFACT_ATTEMPT": ("3", "0", "01"),
            "GITHUB_RUN_ID": ("0", "123\n"), "GITHUB_RUN_ATTEMPT": ("0", ""),
            "CHUMMER_E2E_APK_ARTIFACT_ID": ("0", "latest", "456 "),
            "CHUMMER_E2E_APK_ARTIFACT_DIGEST": ("d" * 63, "D" * 64, "sha256:" + "d" * 64 + "\n"),
            "CHUMMER_E2E_APK_ARTIFACT_NAME": ("chummer-android-api36-x64-debug-123-2", "chummer-android-api36-x64-debug-124-1"),
            "CHUMMER_E2E_APK_SHA256": ("a" * 64, "A" * 64)}
        for key, values in mutations.items():
            for value in values:
                with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                    self.preflight({**self.environment, key: value})

    def test_sidecar_content_receipt_and_live_file_drift_fail_closed(self):
        original = self.sidecar.read_bytes()
        self.sidecar.write_bytes(original + b"another.apk\n")
        with self.assertRaisesRegex(RuntimeError, "checksum sidecar"):
            self.preflight()
        self.sidecar.write_bytes(original)
        content = json.loads(self.content.read_text())
        for key, value in (("coreRevision", "c" * 40), ("apkSha256", "a" * 64),
                           ("apkVerified", False), ("issues", ["missing"]), ("status", "fail")):
            with self.subTest(key=key):
                self.content.write_text(json.dumps({**content, key: value}))
                with self.assertRaisesRegex(RuntimeError, "content receipt"):
                    self.preflight()
        self.content.write_text(json.dumps(content))
        checked = self.preflight()
        self.apk.write_bytes(b"changed after validation")
        with self.assertRaisesRegex(ValueError, "changed before receipt"):
            checked.recheck()

    def test_source_binding_requires_clean_exact_workflow_commit(self):
        clean = self.path / "clean-repository"
        subprocess.run(["git", "init", "-q", str(clean)], check=True, capture_output=True)
        # Real empty status output is valid; do not mock away the helper's rejection
        # of empty output that made the first draft fail on every clean checkout.
        with patch.object(ci.authority, "_run", side_effect=["a" * 40, "b" * 40]):
            self.assertEqual(ci.source_binding(clean, self.environment), self.source)
        with patch.object(ci.authority, "_run", side_effect=["a" * 40, "b" * 40]), \
             patch.object(ci.subprocess, "run", return_value=SimpleNamespace(stdout="", stderr="")):
            self.assertEqual(ci.source_binding(self.repo, self.environment), self.source)
        for commit, status in (("c" * 40, ""), ("a" * 40, " M production.cs")):
            with self.subTest(commit=commit, status=status), \
                 patch.object(ci.authority, "_run", side_effect=[commit, "b" * 40]), \
                 patch.object(ci.subprocess, "run", return_value=SimpleNamespace(stdout=status, stderr="")):
                with self.assertRaises(RuntimeError):
                    ci.source_binding(self.repo, self.environment)

    def test_preflight_cli_never_executes_device_journey(self):
        checked = self.preflight()
        with patch.object(ci, "preflight", return_value=checked), patch.object(ci, "execute") as execute, \
             patch("builtins.print") as output:
            self.assertEqual(ci.main(["--preflight-only"]), 0)
        execute.assert_not_called()
        receipt = json.loads(output.call_args.args[0])
        self.assertEqual(receipt["executionStatus"], "not-run")
        self.assertEqual(receipt["status"], "preflight-pass")

    def environment_fixture(self):
        sdk = self.path / "sdk"
        image = sdk / "system-images/android-36/google_apis/x86_64"
        image.mkdir(parents=True)
        runner_home = self.path / "runner-home"
        avd_home = runner_home / ".android/avd"
        avd = avd_home / "test.avd/config.ini"
        avd.parent.mkdir(parents=True)
        avd.write_text("image.sysdir.1=system-images/android-36/google_apis/x86_64/\n"
                       "hw.cpu.arch=x86_64\ntag.id=google_apis\nhw.device.name=pixel_c\n")
        java_env, _ = self.java_fixture()
        env = {**self.environment, "RUNNER_OS": "Linux", "RUNNER_ARCH": "X64", "ImageOS": "ubuntu24",
               "ImageVersion": "20260915.1.0", "ANDROID_AVD_HOME": str(avd_home), **java_env,
               "HOME": str(runner_home)}
        for relative in ("emulator", "platform-tools", "platforms/android-36", "system-images/android-36/google_apis/x86_64"):
            directory = sdk / relative
            directory.mkdir(parents=True, exist_ok=True)
            (directory / "package.xml").write_text("<test-package />")
        log = self.path / "chummer-api36-emulator-live.log"
        log.write_text("INFO | Android emulator version 36.2.6.0 (build_id 123456) (CL:N/A)\n")
        log.chmod(0o600)
        kvm = self.path / "fake-kvm"
        kvm.touch()
        return sdk, avd, env, kvm

    def test_avd_exact_image_arch_tag_and_duplicate_keys(self):
        sdk, avd, _, _ = self.environment_fixture()
        original = avd.read_text()
        snapshot = ci.authority.StableFile(avd, "test AVD")
        self.assertEqual(ci.avd_configuration(snapshot, sdk)["cpuArch"], "x86_64")
        avd.write_text(original + "hw.cpu.ncore=4\nhw.cpu.ncore=2\nhw.keyboard=no\nhw.keyboard=yes\n")
        self.assertEqual(ci.avd_configuration(ci.authority.StableFile(avd, "test AVD"), sdk)[
            "observedHardwareEntries"]["hw.cpu.ncore"], ["4", "2"])
        for content in (original.replace("x86_64\ntag", "arm64-v8a\ntag"),
                        original.replace("tag.id=google_apis", "tag.id=default"),
                        original + "hw.cpu.arch=x86_64\n", original.replace("image.sysdir.1=", "unknown.path=")):
            avd.write_text(content)
            with self.subTest(content=content), self.assertRaises(RuntimeError):
                ci.avd_configuration(ci.authority.StableFile(avd, "test AVD"), sdk)

    def test_environment_records_actual_width_and_exact_live_package_versions(self):
        sdk, avd, env, kvm = self.environment_fixture()
        checked = self.preflight()
        evidence = self.path / "environment-evidence"
        evidence.mkdir()
        inventory = "Installed packages:\n" + "\n".join(f"{name} | {version} | Test" for name, version in (
            ("emulator", "36.2.6"), ("platform-tools", "36.0.2"), ("platforms;android-36", "2"), (ci.IMAGE, "9")))
        device = Mock()
        device.run.return_value = SimpleNamespace(stdout="test\nOK\n")
        device.shell.return_value = "google/fingerprint/API36"
        android = {"apiLevel": 36, "abi": "x86_64", "smallestWidthDp": 900,
                   "configuration": "sw900dp-land", "deviceKind": "tablet-profile-emulator"}
        with ExitStack() as stack:
            stack.enter_context(patch.object(ci.stat, "S_ISCHR", return_value=True))
            stack.enter_context(patch.object(ci.authority, "sdk_executable", return_value=sdk / "mock-sdkmanager"))
            run = stack.enter_context(patch.object(ci.authority, "_run", side_effect=[inventory, 'openjdk version "17.0.20"\n']))
            width = stack.enter_context(patch.object(ci.tablet, "tablet_environment", return_value=dict(android)))
            result, snapshots = ci.capture_environment(device, checked, env, sdk, evidence, kvm_path=kvm)
            self.assertEqual(result["android"]["smallestWidthDp"], 900)
            self.assertEqual(result["liveEmulator"]["version"], "36.2.6.0")
            self.assertIn({"package": ci.IMAGE, "version": "9"}, result["installedPackages"])
            self.assertEqual(result["avd"]["configSha256"], hashlib.sha256(avd.read_bytes()).hexdigest())
            self.assertEqual(len(snapshots), 6)
            self.assertNotEqual(result["javaHomeBinding"]["advertisedJavaHome"],
                                result["javaHomeBinding"]["canonicalJavaHome"])
            self.assertEqual(run.call_args.args[0], [str(snapshots[-1].launcher.path), "-version"])
            self.assertEqual((evidence / "java-version.txt").read_text(), result["javaVersionOutput"])
            self.assertEqual((evidence / "java-release.txt").read_bytes(), snapshots[-1].release.data)
            width.assert_called_once_with(device)
            run.side_effect = [inventory.replace("36.2.6", "36.2.7")]
            with self.assertRaisesRegex(RuntimeError, "Live emulator version differs"):
                ci.capture_environment(device, checked, env, sdk, evidence, kvm_path=kvm)
            run.side_effect = [inventory]
            with self.assertRaisesRegex(RuntimeError, "action-owned AVD home"):
                ci.capture_environment(device, checked, {**env, "ANDROID_AVD_HOME": str(self.path)},
                                       sdk, evidence, kvm_path=kvm)
            run.side_effect = [inventory]
            device.run.return_value = SimpleNamespace(stdout="another-avd\nOK\n")
            with self.assertRaisesRegex(RuntimeError, "configured tablet AVD"):
                ci.capture_environment(device, checked, env, sdk, evidence, kvm_path=kvm)

    def java_fixture(self, *, alias=True):
        cache = self.path / "hostedtoolcache"
        advertised = cache / "Java_Temurin-Hotspot_jdk/17.0.20-1/x64"
        advertised.parent.mkdir(parents=True)
        canonical = self.path / "installed-temurin17" if alias else advertised
        (canonical / "bin").mkdir(parents=True)
        (canonical / "bin/java").write_bytes(b"mock Java launcher; never executed")
        (canonical / "bin/java").chmod(0o755)
        (canonical / "release").write_text('JAVA_VERSION="17.0.20"\nIMPLEMENTOR="Eclipse Adoptium"\n')
        if alias:
            advertised.symlink_to(canonical, target_is_directory=True)
        return {"JAVA_HOME": str(advertised), "JAVA_HOME_17_X64": str(advertised),
                "RUNNER_TOOL_CACHE": str(cache)}, canonical

    def test_java_toolcache_alias_is_bound_without_weakening_canonical_paths(self):
        environment, canonical = self.java_fixture()
        with self.assertRaisesRegex(RuntimeError, "canonical directory"):
            ci.canonical_directory(environment["JAVA_HOME"], "ordinary output")
        binding = ci.bind_java_home(environment)
        self.assertEqual(binding.launcher.path, canonical / "bin/java")
        self.assertEqual(binding.observation["canonicalJavaHome"], str(canonical))
        self.assertEqual(binding.observation["advertisedJavaHome"], environment["JAVA_HOME"])
        self.assertEqual(binding.observation["releaseMetadataSha256"], hashlib.sha256((canonical / "release").read_bytes()).hexdigest())
        binding.recheck()

    def test_java_toolcache_canonical_install_is_also_accepted(self):
        environment, canonical = self.java_fixture(alias=False)
        binding = ci.bind_java_home(environment)
        self.assertEqual(binding.observation["advertisedJavaHome"], str(canonical))
        binding.recheck()

    def test_java_alias_missing_cyclic_and_wrong_file_targets_fail_closed(self):
        environment, canonical = self.java_fixture()
        advertised = Path(environment["JAVA_HOME"])
        for target in (self.path / "missing-target", advertised, canonical / "release"):
            advertised.unlink()
            advertised.symlink_to(target)
            with self.subTest(target=target), self.assertRaises((OSError, RuntimeError, ValueError)):
                ci.bind_java_home(environment)

    def test_java_home_must_be_the_declared_temurin17_x64_toolcache_entry(self):
        environment, canonical = self.java_fixture()
        for change in ({"JAVA_HOME_17_X64": ""}, {"RUNNER_TOOL_CACHE": ""},
                       {"JAVA_HOME": str(canonical), "JAVA_HOME_17_X64": str(canonical)},
                       {"JAVA_HOME": "java", "JAVA_HOME_17_X64": "java"}):
            with self.subTest(change=change), self.assertRaises((RuntimeError, ValueError)):
                ci.bind_java_home({**environment, **change})

    def test_java_alias_target_and_file_identity_changes_are_not_reusable(self):
        environment, canonical = self.java_fixture()
        advertised = Path(environment["JAVA_HOME"])
        binding = ci.bind_java_home(environment)
        advertised.rename(advertised.with_name("retired-alias"))
        advertised.symlink_to(canonical)
        with self.assertRaisesRegex(RuntimeError, "identity changed"):
            binding.recheck()
        binding = ci.bind_java_home(environment)
        retired = canonical.with_name("retired-installation")
        canonical.rename(retired)
        (canonical / "bin").mkdir(parents=True)
        (canonical / "bin/java").write_bytes((retired / "bin/java").read_bytes())
        (canonical / "bin/java").chmod(0o755)
        (canonical / "release").write_bytes((retired / "release").read_bytes())
        with self.assertRaisesRegex(RuntimeError, "identity changed"):
            binding.recheck()
        for relative, snapshot_name in (("release", "release"), ("bin/java", "launcher")):
            binding = ci.bind_java_home(environment)
            snapshot = getattr(binding, snapshot_name)
            target = canonical / relative
            target.rename(target.with_name("retired-" + target.name))
            target.write_bytes(snapshot.data)
            if snapshot_name == "launcher":
                target.chmod(0o755)
            with self.subTest(file=relative), self.assertRaisesRegex(ValueError, "changed before receipt"):
                binding.recheck()

    def execute_patches(self, stack):
        checked = self.preflight()
        stack.enter_context(patch.object(ci, "preflight", return_value=checked))
        stack.enter_context(patch.object(ci, "source_binding", return_value=self.source))
        stack.enter_context(patch.object(ci.authority, "_canonical_sdk_root", return_value=self.path))
        stack.enter_context(patch.object(ci.authority, "sdk_executable", return_value=self.path / "mock-adb"))
        device = stack.enter_context(patch.object(ci.tablet, "TabletDevice")).return_value
        android = {"apiLevel": 36, "abi": "x86_64", "smallestWidthDp": 900, "configuration": "sw900dp-land"}
        capture = stack.enter_context(patch.object(ci, "capture_environment", return_value=(
            {"android": {**android, "buildFingerprint": "observed-build"}}, [])))
        def journey(args):
            self.assertEqual(args.proof_build_id, "hosted-123-1")
            self.assertTrue(args.allow_mutate_disposable_runner)
            self.assertFalse(args.evidence.exists())
            device.install_verified.assert_called_once_with(checked.apk.path, self.apk_sha, "--no-streaming", "-r")
            result = {"status": "device-pass", "executionStatus": "executed", "environment": android}
            args.receipt.write_text(json.dumps(result))
            return result
        execute = stack.enter_context(patch.object(ci.tablet, "execute", side_effect=journey))
        return checked, device, capture, execute

    def test_mocked_execution_preserves_supplemental_receipt_and_exact_apk_binding(self):
        with ExitStack() as stack:
            checked, device, capture, execute = self.execute_patches(stack)
            result = ci.execute(self.environment)
            capture.assert_called_once()
            execute.assert_called_once()
        self.assertEqual(result["status"], "device-pass")
        self.assertEqual(result["apkArtifact"], checked.artifact)
        for claim in ("physicalDeviceProof", "releaseEvidenceEligible", "preview12PhoneAuthority",
                      "managedOwnerGenerationRaceProof", "CoreDispatchReplayAttested"):
            self.assertIs(result[claim], False)
        receipt_path = self.path / "chummer-api36-tablet-skill-group-evidence/hosted-tablet-receipt.json"
        self.assertEqual(json.loads(receipt_path.read_text()), result)

    def test_unknown_install_outcome_is_not_retried_or_counted_as_device_pass(self):
        with ExitStack() as stack:
            _, device, _, execute = self.execute_patches(stack)
            device.install_verified.side_effect = TimeoutError("unknown install result")
            with self.assertRaises(TimeoutError):
                ci.execute(self.environment)
            device.install_verified.assert_called_once()
            execute.assert_not_called()
        receipt = json.loads((self.path / "chummer-api36-tablet-skill-group-evidence/hosted-tablet-receipt.json").read_text())
        self.assertEqual(receipt["status"], "fail")
        self.assertEqual(receipt["executionStatus"], "attempted")

    def test_java_identity_drift_after_capture_prevents_install_and_mutation(self):
        environment, canonical = self.java_fixture()
        binding = ci.bind_java_home(environment)
        advertised = Path(environment["JAVA_HOME"])
        advertised.rename(advertised.with_name("retired-alias"))
        advertised.symlink_to(canonical)
        with ExitStack() as stack:
            _, device, capture, execute = self.execute_patches(stack)
            capture.return_value = (capture.return_value[0], [binding])
            with self.assertRaisesRegex(RuntimeError, "identity changed"):
                ci.execute(self.environment)
            device.install_verified.assert_not_called()
            execute.assert_not_called()
        receipt = json.loads((self.path / "chummer-api36-tablet-skill-group-evidence/hosted-tablet-receipt.json").read_text())
        self.assertEqual(receipt["status"], "fail")
        self.assertEqual(receipt["executionStatus"], "not-run")

    def test_conflicting_sdk_root_is_refused_before_device_provisioning(self):
        with ExitStack() as stack:
            _, device, capture, execute = self.execute_patches(stack)
            ci.authority._canonical_sdk_root.side_effect = [self.path, self.path / "another-sdk"]
            with self.assertRaisesRegex(RuntimeError, "action's ANDROID_HOME"):
                ci.execute({**self.environment, "ANDROID_SDK_ROOT": str(self.path / "another-sdk")})
            device.require_transport_stability.assert_not_called()
            device.install_verified.assert_not_called()
            capture.assert_not_called()
            execute.assert_not_called()


class TabletCompositionTests(unittest.TestCase):
    def test_records_one_simultaneous_three_pane_hierarchy(self):
        nodes = [SimpleNamespace(attributes={"resource-id": "tablet-build-" + name + "-pane", "bounds": bounds})
                 for name, bounds in (("navigation", "[0,0][200,800]"), ("collection", "[200,0][600,800]"),
                                      ("inspector", "[600,0][1200,800]"))]
        result = ci.tablet.tablet_composition(nodes)
        self.assertEqual(result["inspector"], [600, 0, 1200, 800])
        for invalid in ([], nodes[:-1], nodes + nodes[:1], [*nodes[:2], SimpleNamespace(attributes={
            "resource-id": "tablet-build-inspector-pane", "bounds": "[500,0][1200,800]"})],
            [*nodes[:2], SimpleNamespace(attributes={"resource-id": "tablet-build-inspector-pane", "bounds": "[600,900][1200,1200]"})]):
            with self.subTest(invalid=invalid), self.assertRaises(RuntimeError):
                ci.tablet.tablet_composition(invalid)


if __name__ == "__main__":
    unittest.main()
