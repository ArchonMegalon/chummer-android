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
PACKAGE5_INACTIVE_REFERENCE_FILES = (
    "scripts/api36_arm64_physical_contract.py",
    "scripts/run-api36-arm64-physical-e2e.sh",
    "scripts/verify-api36-arm64-physical-aggregate.py",
)
PACKAGE5_DRIVER_GUARD_FILES = (
    "tests/test_api36_sr5_priority_legal_path_e2e_driver.py",
    "tests/test_api36_sr5_career_active_skill_wizard_e2e_driver.py",
    "tests/test_run_api36_sr5_before_run_edge_physical_e2e_driver.py",
    "tests/test_api36_sr5_after_run_settlement_contract.py",
    "tests/test_api36_sr5_downtime_calendar_e2e_driver.py",
    "tests/test_run_api36_sr5_playtime_weapon_physical_e2e_driver.py",
    "tests/test_api36_arm64_physical_contract.py",
)
PACKAGE5_AGGREGATE_GUARDS = {
    "test_symlink_and_aggregate_order_extra_seal_and_authority_tamper_fail_closed",
    "test_exact_integrated_driver_git_authority_and_cli_contracts",
    "test_orchestrator_is_bounded_external_and_inactive",
}
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("sr5_priority_legal_path_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


def package5_inactive_driver_reference_violations(
    root: Path, driver_relative: str,
) -> list[str]:
    """Validate the sole non-release path from Package 5 into physical drivers."""

    violations: list[str] = []
    allowed = set(PACKAGE5_INACTIVE_REFERENCE_FILES)
    references: set[str] = set()
    for activation_root in (root / ".github", root / "scripts"):
        if not activation_root.exists():
            continue
        for path in activation_root.rglob("*"):
            if not path.is_file() or path.suffix not in {".py", ".yml", ".yaml", ".sh", ".json"}:
                continue
            if driver_relative in path.read_text(encoding="utf-8", errors="replace"):
                references.add(path.relative_to(root).as_posix())

    foreign = sorted(references - allowed)
    if foreign:
        violations.append(f"foreign driver references: {foreign!r}")
    if PACKAGE5_INACTIVE_REFERENCE_FILES[0] not in references:
        violations.append("exact Package 5 driver authority reference is missing")

    workflow_needles = (driver_relative, *PACKAGE5_INACTIVE_REFERENCE_FILES)
    workflow_root = root / ".github"
    if workflow_root.exists():
        for path in workflow_root.rglob("*"):
            if not path.is_file():
                continue
            source = path.read_text(encoding="utf-8", errors="replace")
            if any(needle in source for needle in workflow_needles):
                violations.append(
                    f"workflow activation reference: {path.relative_to(root).as_posix()}"
                )

    required = (*PACKAGE5_INACTIVE_REFERENCE_FILES, *PACKAGE5_DRIVER_GUARD_FILES)
    missing = [
        relative for relative in required
        if not (root / relative).is_file() or (root / relative).is_symlink()
    ]
    if missing:
        violations.append(f"Package 5 inactive authority/guard files missing: {missing!r}")
        return violations

    authority_source = (root / PACKAGE5_INACTIVE_REFERENCE_FILES[0]).read_text(encoding="utf-8")
    for marker in (
        '"publicationAuthorized": False',
        'repository_root.resolve(strict=True) != repository_root',
        '("status", "--porcelain=v1", "--untracked-files=all")',
        '("ls-tree", head, "--", relative)',
        'source graph does not bind the clean Package 5 Android commit/tree',
    ):
        if marker not in authority_source:
            violations.append(f"Package 5 driver authority omitted {marker!r}")
    if '"publicationAuthorized": True' in authority_source:
        violations.append("Package 5 driver authority enables publication")

    orchestrator_source = (root / PACKAGE5_INACTIVE_REFERENCE_FILES[1]).read_text(encoding="utf-8")
    for marker in (
        'journeys=(priority career before-run after-run downtime playtime)',
        'driver_authority_args+=(--driver "$journey=${drivers[$journey]}")',
        '--timeout-seconds 3600',
        'deadline_epoch="$(( $(date +%s) + 28800 ))"',
        'run_bounded "journey-$journey"',
    ):
        if orchestrator_source.count(marker) != 1:
            violations.append(f"Package 5 bounded orchestrator marker is not exact: {marker!r}")
    if orchestrator_source.count("publication_authorized=false") != 2:
        violations.append("Package 5 orchestrator publication=false outcomes are not exact")
    if "publication_authorized=true" in orchestrator_source.lower():
        violations.append("Package 5 orchestrator enables publication")
    for forbidden in ("dotnet build", "dotnet publish", "https://", "google play"):
        if forbidden in orchestrator_source.lower():
            violations.append(f"Package 5 orchestrator contains release/build activation: {forbidden!r}")

    verifier_source = (root / PACKAGE5_INACTIVE_REFERENCE_FILES[2]).read_text(encoding="utf-8")
    for marker in (
        "capture_driver_authority",
        'parser.add_argument("--driver", action="append", default=[], required=True)',
        "physical_build_inputs=pass publication_authorized=false",
        "physical_six_journey_aggregate=pass publication_authorized=false",
    ):
        if marker not in verifier_source:
            violations.append(f"Package 5 verifier omitted {marker!r}")

    aggregate_test = root / "tests/test_api36_arm64_physical_contract.py"
    try:
        aggregate_tree = ast.parse(aggregate_test.read_text(encoding="utf-8"))
    except SyntaxError as error:
        violations.append(f"Package 5 aggregate guard does not parse: {error}")
    else:
        test_names = {
            node.name for node in ast.walk(aggregate_tree)
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
        }
        absent_guards = sorted(PACKAGE5_AGGREGATE_GUARDS - test_names)
        if absent_guards:
            violations.append(f"Package 5 aggregate guards missing: {absent_guards!r}")

    for relative in PACKAGE5_DRIVER_GUARD_FILES[:-1]:
        try:
            guard_tree = ast.parse((root / relative).read_text(encoding="utf-8"))
        except SyntaxError as error:
            violations.append(f"Package 5 driver guard {relative} does not parse: {error}")
            continue
        if not any(
            isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
            and node.name.startswith("test_")
            for node in ast.walk(guard_tree)
        ):
            violations.append(f"Package 5 driver guard has no executable tests: {relative}")
    return violations


class FakePhysicalDevice:
    def __init__(self, **overrides: str) -> None:
        self.serial = overrides.pop("serial", "R5CT30PHYSICAL")
        self.properties = {
            "ro.build.version.sdk": "36",
            "ro.product.cpu.abi": "arm64-v8a",
            "ro.product.cpu.abilist": "arm64-v8a,armeabi-v7a",
            "ro.kernel.qemu": "",
            "ro.boot.qemu": "",
            "ro.product.manufacturer": "Google",
            "ro.product.model": "Pixel 9",
            "ro.product.device": "tokay",
            "ro.product.name": "tokay",
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


class FinalizationDevice:
    def __init__(self, overrides: dict[str, str] | None = None) -> None:
        self.values = {
            "creation-finalization-binding": (
                f"Revision 16 · plan sha256:{PLAN_DIGEST[:11]}… · "
                f"preview sha256:{PREVIEW_DIGEST[:11]}…"
            ),
            "creation-finalization-costs": "costs",
            "creation-finalization-atomic-boundary": "atomic",
            "creation-finalization-content-revision": "16",
            "creation-finalization-plan-digest": PLAN_DIGEST,
            "creation-finalization-preview-digest": PREVIEW_DIGEST,
            "creation-finalization-receipt": "durable receipt",
            "creation-finalization-receipt-previous-content-revision": "16",
            "creation-finalization-receipt-content-revision": "17",
            "creation-finalization-receipt-saved-revision": "17",
            "creation-finalization-receipt-build-method": "Priority",
            "creation-finalization-receipt-plan-digest": PLAN_DIGEST,
            "creation-finalization-receipt-preview-digest": PREVIEW_DIGEST,
            "creation-finalization-receipt-digest": RECEIPT_DIGEST,
            "creation-finalization-career-reopen": "Fresh reopen verified: this runner is now using Career mode.",
            "creation-finalization-authority-ready": "ready",
            "creation-finalization-open-review": "review",
            "creation-finalization-confirm": "confirm",
            "creation-finalization-open-career": "open",
        }
        self.values.update(overrides or {})
        self.taps: list[tuple[str, ...]] = []

    def wait_exact_resource_id_bidirectional(self, selector: str, **_kwargs: object) -> driver.shared.UiNode:
        if selector not in self.values:
            raise AssertionError(selector)
        return driver.shared.UiNode({
            "content-desc": self.values[selector], "enabled": "true",
            "clickable": "true", "bounds": "[0,0][20,20]",
        })

    def wait_for_single_exact_resource_id(self, *_args: object, **_kwargs: object) -> object:
        return object()

    def shell(self, *arguments: str) -> str:
        self.taps.append(arguments)
        return ""

    def capture(self, _name: str) -> None:
        return None


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
    result: dict[str, object] = {
        "stepId": stage.step_id,
        "routeId": stage.route_id,
        "requiredByCurrentFinalizer": stage.required_by_finalizer,
        "routeStatus": "typed-authority-visible",
        "authorityVisible": True,
        "draftFabricated": False,
    }
    if stage.step_id == "method":
        result["buildMethod"] = "Priority"
    return result


PLAN_DIGEST = "64879f7d6b960a01909762d911a32d4582c20010c5641ee90278b644a9e3b525"
PREVIEW_DIGEST = "5975cf1bba432391c94667f5886225f69377c0aa8b9fa21fddfb21c89bcf9092"
RECEIPT_DIGEST = "6f32860910ca0fb2a20c7fda143666b09dbf8db5238195c90a586fb542ff0cad"


def exact_finalization() -> dict[str, object]:
    return {
        "review": "sealed-core-whole-build-plan",
        "sealedPlanAuthority": {
            "contentRevision": 16,
            "planDigest": PLAN_DIGEST,
            "previewDigest": PREVIEW_DIGEST,
        },
        "receiptAuthority": {
            "previousContentRevision": 16,
            "contentRevision": 17,
            "savedRevision": 17,
            "buildMethod": "Priority",
            "planDigest": PLAN_DIGEST,
            "previewDigest": PREVIEW_DIGEST,
            "receiptDigest": RECEIPT_DIGEST,
        },
        "confirmation": "explicit-atomic-once",
        "receipt": "durable",
        "careerReopen": "verified",
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
        self.assertEqual("metadata-only-non-authoritative", fixture["authority"])
        self.assertFalse(fixture["runtimeConsumed"])
        self.assertEqual(driver.IDENTITY_CONTRACT_BLOCKER, fixture["identityGap"]["blocker"])
        self.assertNotIn(CONTRACT_FIXTURE.name, DRIVER.read_text(encoding="utf-8"))

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
            "creation-prerequisite-build-method-id",
            "cross_bind_finalization_authorities",
            "provenance_file_identity",
            'device.install_verified(apk, expected_apk_sha256, "--no-streaming", "-r")',
            '"status": "device-pass-source-bound"',
            '"physicalDeviceProof": True',
            '"installedArtifactBound": True',
            '"releaseAttested": False',
            '"publicationAuthorized": False',
            '"disposableDeviceAuthorization"',
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
            ({"ro.boot.qemu": "1"}, "emulator"),
            ({"serial": "emulator-5554"}, "emulator"),
            ({"serial": "localhost:5555"}, "emulator"),
            ({"serial": "127.0.0.1:5555"}, "emulator"),
            ({"ro.hardware": "ranchu"}, "emulator"),
            ({"ro.build.fingerprint": "generic/sdk_gphone64_arm64/emulator:16/test"}, "emulator"),
            ({"ro.product.device": "aosp_cf_arm64_phone"}, "emulator"),
            ({"ro.product.name": "sdk_gphone64_arm64"}, "emulator"),
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

    def test_runtime_build_method_binding_rejects_every_non_priority_value(self) -> None:
        self.assertEqual("Priority", driver.require_priority_build_method("Priority", "test"))
        for value in ("", "priority", "SumToTen", "Karma", "Priority "):
            with self.subTest(value=value):
                with self.assertRaisesRegex(RuntimeError, "exactly BuildMethod 'Priority'"):
                    driver.require_priority_build_method(value, "test")

    def test_full_finalization_authorities_reject_placeholders_revision_and_digest_mismatch(self) -> None:
        review = driver.FinalizationReviewAuthority(16, PLAN_DIGEST, PREVIEW_DIGEST)
        receipt = driver.FinalizationReceiptAuthority(
            16, 17, 17, "Priority", PLAN_DIGEST, PREVIEW_DIGEST, RECEIPT_DIGEST,
        )
        driver.cross_bind_finalization_authorities(review, receipt)
        rejected = (
            (review.__class__(16, "plan-placeholder", PREVIEW_DIGEST), receipt),
            (review.__class__(16, PLAN_DIGEST, "preview-placeholder"), receipt),
            (review, receipt.__class__(16, 17, 17, "Priority", "abcd…", PREVIEW_DIGEST, RECEIPT_DIGEST)),
            (review, receipt.__class__(15, 17, 17, "Priority", PLAN_DIGEST, PREVIEW_DIGEST, RECEIPT_DIGEST)),
            (review, receipt.__class__(16, 18, 18, "Priority", PLAN_DIGEST, PREVIEW_DIGEST, RECEIPT_DIGEST)),
            (review, receipt.__class__(16, 17, 16, "Priority", PLAN_DIGEST, PREVIEW_DIGEST, RECEIPT_DIGEST)),
            (review, receipt.__class__(16, 17, 17, "SumToTen", PLAN_DIGEST, PREVIEW_DIGEST, RECEIPT_DIGEST)),
            (review, receipt.__class__(16, 17, 17, "Priority", RECEIPT_DIGEST, PREVIEW_DIGEST, RECEIPT_DIGEST)),
            (review, receipt.__class__(16, 17, 17, "Priority", PLAN_DIGEST, RECEIPT_DIGEST, RECEIPT_DIGEST)),
            (review, receipt.__class__(16, 17, 17, "Priority", PLAN_DIGEST, PREVIEW_DIGEST, "receipt-placeholder")),
        )
        for candidate_review, candidate_receipt in rejected:
            with self.subTest(candidate_receipt=candidate_receipt):
                with self.assertRaises(RuntimeError):
                    driver.cross_bind_finalization_authorities(candidate_review, candidate_receipt)

    def test_finalize_uses_machine_authority_and_only_formats_visible_typed_digest_markers(self) -> None:
        result = driver.finalize_exact_build(FinalizationDevice())
        self.assertEqual(16, result["sealedPlanAuthority"]["contentRevision"])
        self.assertEqual(PLAN_DIGEST, result["sealedPlanAuthority"]["planDigest"])
        self.assertEqual(PREVIEW_DIGEST, result["sealedPlanAuthority"]["previewDigest"])
        self.assertEqual(RECEIPT_DIGEST, result["receiptAuthority"]["receiptDigest"])
        self.assertNotIn("reviewedAuthority", result)
        self.assertIn("visibleReviewEvidence", result)
        display_only = driver.finalize_exact_build(FinalizationDevice({
            "creation-finalization-binding": (
                "Revision 999 · plan sha256:aaaaaaaaaaa… · preview sha256:bbbbbbbbbbb…"
            ),
        }))
        self.assertEqual(result["sealedPlanAuthority"], display_only["sealedPlanAuthority"])
        for overrides, message in (
            ({"creation-finalization-plan-digest": "1" * 18 + "…"}, "full lowercase SHA-256"),
            ({"creation-finalization-plan-digest": "0" * 64}, "full lowercase SHA-256"),
            ({"creation-finalization-plan-digest": "sha256:" + PLAN_DIGEST}, "full lowercase SHA-256"),
            ({"creation-finalization-plan-digest": PLAN_DIGEST.upper()}, "full lowercase SHA-256"),
            ({"creation-finalization-receipt-plan-digest": RECEIPT_DIGEST}, "plan digest does not match"),
            ({"creation-finalization-receipt-preview-digest": RECEIPT_DIGEST}, "preview digest does not match"),
            ({"creation-finalization-receipt-build-method": "SumToTen"}, "exactly BuildMethod 'Priority'"),
            ({"creation-finalization-receipt-previous-content-revision": "15"}, "reviewed workspace revision"),
            ({"creation-finalization-receipt-content-revision": "18"}, "advance the reviewed revision exactly once"),
            ({"creation-finalization-receipt-digest": "receipt-placeholder"}, "full lowercase SHA-256"),
        ):
            with self.subTest(overrides=overrides):
                with self.assertRaisesRegex(RuntimeError, message):
                    driver.finalize_exact_build(FinalizationDevice(overrides))
        for visible in (
            f"Revision 16 · plan {PLAN_DIGEST[:11]}… · preview sha256:{PREVIEW_DIGEST[:11]}…",
            f"Revision 16 · plan sha512:{PLAN_DIGEST[:11]}… · preview sha256:{PREVIEW_DIGEST[:11]}…",
            f"Revision 16 · plan SHA256:{PLAN_DIGEST[:11]}… · preview sha256:{PREVIEW_DIGEST[:11]}…",
            f"Revision 16 · plan sha256:{PLAN_DIGEST[:12]}… · preview sha256:{PREVIEW_DIGEST[:11]}…",
            f"Revision 16 · plan sha256:{PLAN_DIGEST} · preview sha256:{PREVIEW_DIGEST}",
        ):
            with self.subTest(visible=visible):
                with self.assertRaisesRegex(RuntimeError, "canonical truncated sha256 markers"):
                    driver.finalize_exact_build(FinalizationDevice({
                        "creation-finalization-binding": visible,
                    }))

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
            mock.patch.object(driver, "finalize_exact_build", return_value=exact_finalization()),
            mock.patch.object(driver, "require_career_surface") as career_surface,
            mock.patch.object(driver.shared, "read_phone_workspace_authority", side_effect=[persisted, persisted]),
            mock.patch.object(driver, "read_persisted_creation_receipt_digest", side_effect=[RECEIPT_DIGEST, RECEIPT_DIGEST]),
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
            mock.patch.object(driver, "finalize_exact_build", return_value=exact_finalization()),
            mock.patch.object(driver, "require_career_surface"),
            mock.patch.object(driver.shared, "read_phone_workspace_authority", side_effect=[persisted, drifted]),
            mock.patch.object(driver, "read_persisted_creation_receipt_digest", return_value=RECEIPT_DIGEST),
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
            manifest_bytes = b'{"contractName":"test-build-provenance"}\n'
            args.build_provenance_manifest.write_bytes(manifest_bytes)
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
        self.assertEqual("Priority", receipt["buildMethod"])
        self.assertEqual("a" * 64, receipt["apkSha256"])
        self.assertEqual(manifest, receipt["buildProvenance"])
        self.assertTrue(receipt["buildProvenanceRecheckedAfterRun"])
        self.assertTrue(receipt["buildProvenanceFileRecheckedAfterRun"])
        self.assertEqual(len(manifest_bytes), receipt["buildProvenanceFile"]["size"])
        self.assertRegex(receipt["buildProvenanceFile"]["sha256"], r"^[0-9a-f]{64}$")
        self.assertEqual(
            {
                "authorized": True,
                "flag": driver.DISPOSABLE_DEVICE_FLAG,
                "serial": "R5CT30PHYSICAL",
                "scope": "install-apk-and-atomically-finalize-one-pending-runner",
            },
            receipt["disposableDeviceAuthorization"],
        )
        self.assertTrue(receipt["physicalDeviceProof"])
        self.assertTrue(receipt["installedArtifactBound"])
        self.assertFalse(receipt["draftStateFabricated"])
        self.assertFalse(receipt["releaseAttested"])
        self.assertFalse(receipt["publicationAuthorized"])
        self.assertEqual(journey, receipt["authorityProofStages"])

    def test_execute_rejects_raw_manifest_byte_drift_even_when_parsed_payload_is_equal(self) -> None:
        manifest = {"artifact": {"sha256": "a" * 64}, "contractName": "same-payload"}
        launch = driver.shared.LaunchState(("101",), "component", "")
        device = mock.Mock()
        device.transport_summary.return_value = {"status": "pass"}
        observation = {"apiLevel": 36, "abi": "arm64-v8a"}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            manifest_path = root / "build-provenance.json"
            manifest_path.write_bytes(b'{"artifact":{"sha256":"' + b"a" * 64 + b'"}}\n')
            args = SimpleNamespace(
                adb=root / "adb", apk=root / "app.apk",
                build_provenance_manifest=manifest_path,
                serial="R5CT30PHYSICAL", evidence=root / "evidence",
                receipt=root / "receipt.json", workspace_root=root,
                allow_destructive_disposable_device=True,
            )

            def mutate_manifest(_device: object, _launch: object) -> dict[str, object]:
                manifest_path.write_bytes(b'{ "artifact": { "sha256": "' + b"a" * 64 + b'" } }\n')
                return {"receipt": "would-have-passed"}

            with (
                mock.patch.object(driver, "load_and_verify_manifest", return_value=manifest) as verify,
                mock.patch.object(driver.shared, "Device", return_value=device),
                mock.patch.object(driver, "physical_device_observation", return_value=observation),
                mock.patch.object(driver.shared, "launch_app", return_value=launch),
                mock.patch.object(driver, "prove_priority_journey", side_effect=mutate_manifest),
            ):
                with self.assertRaisesRegex(RuntimeError, "manifest bytes changed during physical execution"):
                    driver.execute(args, {})
            self.assertEqual(1, verify.call_count)

    def test_receipt_digest_drift_after_new_process_fails_closed(self) -> None:
        persisted = driver.shared.WorkspaceAuthority("workspace-priority", 17, 17, "1" * 64, "2" * 64)
        launch = driver.shared.LaunchState(("101",), "component", "")
        restart = driver.shared.ProcessRestartProof(
            launch, driver.shared.LaunchState((), None, ""), driver.shared.LaunchState(("202",), "component", "")
        )
        with (
            mock.patch.object(driver.shared, "wait_for_phone_runner_route"),
            mock.patch.object(driver, "open_exact_stage", side_effect=stage_projection),
            mock.patch.object(driver, "finalize_exact_build", return_value=exact_finalization()),
            mock.patch.object(driver, "require_career_surface"),
            mock.patch.object(driver.shared, "read_phone_workspace_authority", side_effect=[persisted, persisted]),
            mock.patch.object(
                driver, "read_persisted_creation_receipt_digest",
                side_effect=[RECEIPT_DIGEST, "4" * 64],
            ),
            mock.patch.object(driver.shared, "force_stop_and_launch_new_process", return_value=restart),
        ):
            with self.assertRaisesRegex(RuntimeError, "receipt digest changed"):
                driver.prove_priority_journey(JourneyDevice(), launch)

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
        self.assertEqual([], package5_inactive_driver_reference_violations(ROOT, relative))

    def test_inactive_package5_allowance_rejects_foreign_active_and_workflow_references(self) -> None:
        relative = DRIVER.relative_to(ROOT).as_posix()

        def seed(root: Path) -> None:
            for source_relative in (
                *PACKAGE5_INACTIVE_REFERENCE_FILES,
                *PACKAGE5_DRIVER_GUARD_FILES,
            ):
                destination = root / source_relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes((ROOT / source_relative).read_bytes())

        with tempfile.TemporaryDirectory() as temporary:
            fixture_root = Path(temporary).resolve()
            seed(fixture_root)
            self.assertEqual(
                [], package5_inactive_driver_reference_violations(fixture_root, relative)
            )

            foreign = fixture_root / "scripts/foreign-physical-runner.py"
            foreign.write_text(f'FOREIGN_DRIVER = "{relative}"\n', encoding="utf-8")
            violations = package5_inactive_driver_reference_violations(fixture_root, relative)
            self.assertTrue(any("foreign driver references" in item for item in violations))
            foreign.unlink()

            authority = fixture_root / PACKAGE5_INACTIVE_REFERENCE_FILES[0]
            original_authority = authority.read_text(encoding="utf-8")
            authority.write_text(
                original_authority.replace(
                    '"publicationAuthorized": False',
                    '"publicationAuthorized": True',
                    1,
                ),
                encoding="utf-8",
            )
            violations = package5_inactive_driver_reference_violations(fixture_root, relative)
            self.assertTrue(any("enables publication" in item for item in violations))
            authority.write_text(original_authority, encoding="utf-8")

            workflow = fixture_root / ".github/workflows/activate-package5.yml"
            workflow.parent.mkdir(parents=True, exist_ok=True)
            workflow.write_text(
                f"run: python3 {PACKAGE5_INACTIVE_REFERENCE_FILES[1]}\n",
                encoding="utf-8",
            )
            violations = package5_inactive_driver_reference_violations(fixture_root, relative)
            self.assertTrue(any("workflow activation reference" in item for item in violations))
            workflow.unlink()

            orchestrator = fixture_root / PACKAGE5_INACTIVE_REFERENCE_FILES[1]
            original_orchestrator = orchestrator.read_text(encoding="utf-8")
            orchestrator.write_text(
                original_orchestrator.replace("--timeout-seconds 3600", "--timeout-seconds 0", 1),
                encoding="utf-8",
            )
            violations = package5_inactive_driver_reference_violations(fixture_root, relative)
            self.assertTrue(any("bounded orchestrator marker" in item for item in violations))
            orchestrator.write_text(original_orchestrator, encoding="utf-8")

            guard = fixture_root / "tests/test_api36_arm64_physical_contract.py"
            guard.unlink()
            violations = package5_inactive_driver_reference_violations(fixture_root, relative)
            self.assertTrue(any("guard files missing" in item for item in violations))


if __name__ == "__main__":
    unittest.main()
