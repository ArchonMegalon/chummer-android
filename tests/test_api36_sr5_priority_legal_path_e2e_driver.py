from __future__ import annotations

import ast
import importlib.util
import json
import os
from pathlib import Path
from types import SimpleNamespace
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_priority_legal_path_e2e.py"
CONTRACT_FIXTURE = ROOT / "tests/fixtures/sr5-priority-physical-e2e-contract.json"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("sr5_priority_legal_path_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


class FakePhysicalDevice:
    def __init__(self, **overrides: str) -> None:
        self.serial = overrides.pop("serial", "R5CT30PHYSICAL")
        self.properties = {
            "ro.build.version.sdk": "36",
            "ro.product.cpu.abi": "arm64-v8a",
            "ro.product.cpu.abilist": "arm64-v8a,armeabi-v7a",
            "ro.kernel.qemu": "",
            "ro.product.manufacturer": "Google",
            "ro.product.model": "Pixel 9",
            "ro.hardware": "tensor",
            "ro.build.fingerprint": "google/tokay/tokay:16/BP2A/test:user/release-keys",
            "ro.build.id": "BP2A",
            "ro.build.version.security_patch": "2026-08-05",
            "ro.boot.verifiedbootstate": "green",
        }
        self.properties.update(overrides)

    def run(self, *arguments: str) -> SimpleNamespace:
        if arguments != ("get-state",):
            raise AssertionError(arguments)
        return SimpleNamespace(stdout="device\n")

    def shell(self, *arguments: str) -> str:
        if arguments[:1] != ("getprop",):
            raise AssertionError(arguments)
        return self.properties[arguments[1]]


class JourneyDevice:
    def __init__(self) -> None:
        self.captures: list[str] = []

    def wait_for_single_exact_resource_id(self, selector: str, **_kwargs: object) -> object:
        if selector != "creation-wizard-dashboard":
            raise AssertionError(selector)
        return object()

    def capture(self, name: str) -> None:
        self.captures.append(name)


def stage_projection(_device: object, stage: driver.LegalPathStage) -> dict[str, object]:
    if stage.step_id == "identity-story":
        return {
            "stepId": "identity-story",
            "routeId": "creation-stage-identity-story",
            "requiredByCurrentFinalizer": False,
            "routeStatus": "typed-contract-unavailable",
            "authorityVisible": False,
            "draftFabricated": False,
            "blocker": driver.IDENTITY_CONTRACT_BLOCKER,
        }
    return {
        "stepId": stage.step_id,
        "routeId": stage.route_id,
        "requiredByCurrentFinalizer": stage.required_by_finalizer,
        "routeStatus": "typed-authority-visible",
        "authorityVisible": True,
        "draftFabricated": False,
    }


class Api36Sr5PriorityLegalPathDriverTests(unittest.TestCase):
    def test_contract_fixture_and_stage_catalog_are_exact_and_contain_no_draft_state(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        fixture = json.loads(CONTRACT_FIXTURE.read_text(encoding="utf-8"))
        driver.validate_stage_catalog()
        self.assertEqual(
            fixture["stageIds"],
            [stage.step_id for stage in driver.LEGAL_PATH_STAGES],
        )
        self.assertEqual(
            fixture["requiredByCurrentFinalizer"],
            [stage.step_id for stage in driver.LEGAL_PATH_STAGES if stage.required_by_finalizer],
        )
        self.assertFalse(fixture["containsDraftState"])
        self.assertEqual(driver.IDENTITY_CONTRACT_BLOCKER, fixture["identityGap"]["blocker"])

    def test_driver_is_physical_api36_arm64_apk_and_build_provenance_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        execute = source[source.index("def execute("):]
        for marker in (
            "--apk",
            "--build-provenance-manifest",
            driver.DISPOSABLE_DEVICE_FLAG,
            "load_and_verify_manifest",
            'device.require_transport_stability(expected_api_level="36")',
            "physical_device_observation",
            'device.install_verified(apk, expected_apk_sha256, "--no-streaming", "-r")',
            '"status": "device-pass-source-bound"',
            '"physicalDeviceProof": True',
            '"installedArtifactBound": True',
            '"releaseAttested": False',
            '"publicationAuthorized": False',
        ):
            self.assertIn(marker, source)
        self.assertLess(execute.index("load_and_verify_manifest"), execute.index("install_verified"))
        self.assertLess(execute.index("install_verified"), execute.index("prove_priority_journey"))
        self.assertGreater(execute.count("load_and_verify_manifest"), 1)
        self.assertNotIn('device.shell("pm", "clear"', source)
        self.assertNotIn("--acknowledge-unverified-build-provenance", source)

    def test_physical_observation_rejects_emulator_api_and_abi_drift(self) -> None:
        observed = driver.physical_device_observation(FakePhysicalDevice())
        self.assertEqual("non-emulator-arm64-api36", observed["classification"])
        self.assertEqual(36, observed["apiLevel"])
        self.assertEqual("arm64-v8a", observed["abi"])
        for overrides, message in (
            ({"ro.build.version.sdk": "35"}, "API 36"),
            ({"ro.product.cpu.abi": "x86_64"}, "arm64-v8a"),
            ({"ro.kernel.qemu": "1"}, "emulator"),
            ({"serial": "emulator-5554"}, "emulator"),
        ):
            with self.subTest(overrides=overrides):
                with self.assertRaisesRegex(RuntimeError, message):
                    driver.physical_device_observation(FakePhysicalDevice(**overrides))

    def test_identity_gap_is_observed_without_tap_fallback_or_draft(self) -> None:
        class Row:
            attributes = {
                "enabled": "false",
                "clickable": "false",
                "content-desc": driver.IDENTITY_CONTRACT_BLOCKER,
            }
            center = (10, 10)

        class Device:
            captures: list[str] = []

            def wait_exact_resource_id_bidirectional(self, *_args: object, **_kwargs: object) -> Row:
                return Row()

            def capture(self, name: str) -> None:
                self.captures.append(name)

            def shell(self, *_args: object) -> str:
                raise AssertionError("The blocked Identity gap must not be tapped")

        identity = next(stage for stage in driver.LEGAL_PATH_STAGES if stage.step_id == "identity-story")
        result = driver.open_exact_stage(Device(), identity)
        self.assertEqual("typed-contract-unavailable", result["routeStatus"])
        self.assertFalse(result["authorityVisible"])
        self.assertFalse(result["draftFabricated"])
        self.assertEqual(driver.IDENTITY_CONTRACT_BLOCKER, result["blocker"])

    def test_whole_build_finalize_restart_and_exact_saved_workspace_are_required(self) -> None:
        persisted = driver.shared.WorkspaceAuthority("workspace-priority", 17, 17, "1" * 64, "2" * 64)
        launch = driver.shared.LaunchState(("101",), "com.myexternalbrain.chummer/.MainActivity", "")
        restart = driver.shared.ProcessRestartProof(
            launch,
            driver.shared.LaunchState((), None, ""),
            driver.shared.LaunchState(("202",), launch.resumed_component, ""),
        )
        device = JourneyDevice()
        with (
            mock.patch.object(driver.shared, "wait_for_phone_runner_route"),
            mock.patch.object(driver, "open_exact_stage", side_effect=stage_projection),
            mock.patch.object(driver, "finalize_exact_build", return_value={"receipt": "durable"}),
            mock.patch.object(driver, "require_career_surface") as career_surface,
            mock.patch.object(driver.shared, "read_phone_workspace_authority", side_effect=[persisted, persisted]),
            mock.patch.object(driver.shared, "force_stop_and_launch_new_process", return_value=restart) as force_stop,
        ):
            result = driver.prove_priority_journey(device, launch)
        self.assertEqual(2, career_surface.call_count)
        force_stop.assert_called_once_with(device, launch)
        self.assertEqual(result["savedCareerWorkspace"], result["restoredCareerWorkspace"])
        self.assertEqual(["101"], result["processRestart"]["beforeProcessIds"])
        self.assertEqual([], result["processRestart"]["afterForceStopProcessIds"])
        self.assertEqual(["202"], result["processRestart"]["restartedProcessIds"])
        self.assertTrue(result["processRestart"]["newPidVerified"])
        self.assertFalse(result["identityGap"]["draftFabricated"])

    def test_workspace_digest_drift_after_new_process_fails_closed(self) -> None:
        persisted = driver.shared.WorkspaceAuthority("workspace-priority", 17, 17, "1" * 64, "2" * 64)
        drifted = driver.shared.WorkspaceAuthority("workspace-priority", 17, 17, "1" * 64, "3" * 64)
        launch = driver.shared.LaunchState(("101",), "component", "")
        restart = driver.shared.ProcessRestartProof(
            launch, driver.shared.LaunchState((), None, ""), driver.shared.LaunchState(("202",), "component", "")
        )
        with (
            mock.patch.object(driver.shared, "wait_for_phone_runner_route"),
            mock.patch.object(driver, "open_exact_stage", side_effect=stage_projection),
            mock.patch.object(driver, "finalize_exact_build", return_value={"receipt": "durable"}),
            mock.patch.object(driver, "require_career_surface"),
            mock.patch.object(driver.shared, "read_phone_workspace_authority", side_effect=[persisted, drifted]),
            mock.patch.object(driver.shared, "force_stop_and_launch_new_process", return_value=restart),
        ):
            with self.assertRaisesRegex(RuntimeError, "does not match the exact saved document"):
                driver.prove_priority_journey(JourneyDevice(), launch)

    def test_execute_orders_manifest_transport_install_journey_and_provenance_recheck(self) -> None:
        events: list[str] = []
        manifest = {"artifact": {"sha256": "a" * 64}, "contractName": "test-build-provenance"}
        launch = driver.shared.LaunchState(("101",), "com.myexternalbrain.chummer/.MainActivity", "")
        journey = {
            "finalization": {"confirmation": "explicit-atomic-once", "receipt": "durable"},
            "processRestart": {"beforeProcessIds": ["101"], "restartedProcessIds": ["202"]},
        }
        observation = {
            "classification": "non-emulator-arm64-api36",
            "apiLevel": 36,
            "abi": "arm64-v8a",
        }

        class ContractDevice:
            def __init__(self, adb: Path, serial: str, evidence: Path) -> None:
                self.adb = adb
                self.serial = serial
                self.evidence = evidence

            def require_transport_stability(self, *, expected_api_level: str) -> None:
                self.assert_equal("36", expected_api_level)
                events.append("preflight")

            def install_verified(self, apk: Path, digest: str, *arguments: str) -> None:
                self.assert_equal("a" * 64, digest)
                self.assert_equal(("--no-streaming", "-r"), arguments)
                events.append("install")

            def transport_summary(self) -> dict[str, str]:
                events.append("transport")
                return {"status": "pass"}

            @staticmethod
            def assert_equal(expected: object, actual: object) -> None:
                if expected != actual:
                    raise AssertionError((expected, actual))

        def verify_manifest(*_args: object, **_kwargs: object) -> dict[str, object]:
            events.append("manifest-before" if "manifest-before" not in events else "manifest-after")
            return manifest

        def observe(_device: object) -> dict[str, object]:
            events.append("physical")
            return observation

        def launch_app(_device: object) -> driver.shared.LaunchState:
            events.append("launch")
            return launch

        def prove(_device: object, initial_launch: driver.shared.LaunchState) -> dict[str, object]:
            self.assertEqual(launch, initial_launch)
            events.append("journey")
            return journey

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            args = SimpleNamespace(
                adb=root / "adb",
                apk=root / "app-arm64.apk",
                build_provenance_manifest=root / "build-provenance.json",
                serial="R5CT30PHYSICAL",
                evidence=root / "evidence",
                receipt=root / "receipt.json",
                workspace_root=root,
                allow_destructive_disposable_device=True,
            )
            context: dict[str, object] = {}
            with (
                mock.patch.object(driver, "load_and_verify_manifest", side_effect=verify_manifest),
                mock.patch.object(driver.shared, "Device", ContractDevice),
                mock.patch.object(driver, "physical_device_observation", side_effect=observe),
                mock.patch.object(driver.shared, "launch_app", side_effect=launch_app),
                mock.patch.object(driver, "prove_priority_journey", side_effect=prove),
            ):
                receipt = driver.execute(args, context)

        self.assertEqual(
            ["manifest-before", "preflight", "physical", "install", "launch", "journey", "manifest-after", "transport"],
            events,
        )
        self.assertEqual("device-pass-source-bound", receipt["status"])
        self.assertEqual("a" * 64, receipt["apkSha256"])
        self.assertEqual(manifest, receipt["buildProvenance"])
        self.assertTrue(receipt["buildProvenanceRecheckedAfterRun"])
        self.assertTrue(receipt["physicalDeviceProof"])
        self.assertTrue(receipt["installedArtifactBound"])
        self.assertFalse(receipt["draftStateFabricated"])
        self.assertFalse(receipt["releaseAttested"])
        self.assertFalse(receipt["publicationAuthorized"])
        self.assertEqual(journey, receipt["authorityProofStages"])

    def test_durable_receipt_is_new_mode_600_and_never_overwritten(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "priority-receipt.json"
            driver.write_receipt_durably(receipt, {"status": "pass"})
            self.assertEqual({"status": "pass"}, json.loads(receipt.read_text(encoding="utf-8")))
            self.assertEqual(0o600, os.stat(receipt).st_mode & 0o777)
            with self.assertRaisesRegex(RuntimeError, "already exists"):
                driver.write_receipt_durably(receipt, {"status": "forged"})
            self.assertEqual({"status": "pass"}, json.loads(receipt.read_text(encoding="utf-8")))

    def test_main_writes_fail_receipt_without_device_or_pass_claim(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            receipt = root / "receipt.json"
            argv = [
                "--adb", str(root / "adb"), "--apk", str(root / "app.apk"),
                "--build-provenance-manifest", str(root / "manifest.json"),
                "--serial", "R5CT30PHYSICAL", "--evidence", str(root / "evidence"),
                "--receipt", str(receipt), "--workspace-root", str(root),
                driver.DISPOSABLE_DEVICE_FLAG,
            ]
            with mock.patch.object(driver, "execute", side_effect=RuntimeError("deterministic blocker")):
                self.assertEqual(1, driver.main(argv))
            payload = json.loads(receipt.read_text(encoding="utf-8"))
            self.assertEqual("fail", payload["status"])
            self.assertFalse(payload["releaseAttested"])
            self.assertFalse(payload["publicationAuthorized"])
            self.assertEqual("deterministic blocker", payload["failure"]["message"])

    def test_main_rejects_relative_or_stale_receipt_without_invoking_device(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            common = [
                "--adb", str(root / "adb"), "--apk", str(root / "app.apk"),
                "--build-provenance-manifest", str(root / "manifest.json"),
                "--serial", "R5CT30PHYSICAL", "--evidence", str(root / "evidence"),
                "--workspace-root", str(root), driver.DISPOSABLE_DEVICE_FLAG,
            ]
            with mock.patch.object(driver, "execute") as execute:
                self.assertEqual(2, driver.main([*common, "--receipt", "relative.json"]))
                execute.assert_not_called()
            stale = root / "stale.json"
            stale.write_text('{"status":"old-pass"}\n', encoding="utf-8")
            with mock.patch.object(driver, "execute") as execute:
                self.assertEqual(2, driver.main([*common, "--receipt", str(stale)]))
                execute.assert_not_called()
            self.assertEqual({"status": "old-pass"}, json.loads(stale.read_text(encoding="utf-8")))

    def test_driver_is_not_activated_as_release_or_workflow_authority(self) -> None:
        relative = DRIVER.relative_to(ROOT).as_posix()
        matches: list[str] = []
        for activation_root in (ROOT / ".github", ROOT / "scripts"):
            if not activation_root.exists():
                continue
            for path in activation_root.rglob("*"):
                if path.is_file() and path.suffix in {".py", ".yml", ".yaml", ".sh", ".json"}:
                    if relative in path.read_text(encoding="utf-8", errors="replace"):
                        matches.append(path.relative_to(ROOT).as_posix())
        self.assertEqual([], matches)


if __name__ == "__main__":
    unittest.main()
