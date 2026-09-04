from __future__ import annotations

import argparse
import ast
import copy
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_playtime_short_burst_e2e.py"
FIXTURE = ROOT / "tests/fixtures/sr5-playtime-weapon-physical-e2e.chum5"
sys.path.insert(0, str(DRIVER.parent))
MODULE_SPEC = importlib.util.spec_from_file_location("playtime_short_burst_driver", DRIVER)
assert MODULE_SPEC is not None and MODULE_SPEC.loader is not None
driver = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_SPEC.name] = driver
MODULE_SPEC.loader.exec_module(driver)


class Api36Sr5PlaytimeShortBurstDriverTests(unittest.TestCase):
    def test_hosted_wrapper_is_exactly_one_nonpublication_phone_action(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertEqual("playtime-short-burst", driver.SPEC.journey)
        self.assertEqual("chummer.android.editing-e2e/v1", driver.SPEC.receipt_schema)
        self.assertEqual("playtime", driver.SPEC.lane)
        self.assertEqual(1, driver.SPEC.lane_value)
        self.assertEqual(2, driver.SPEC.action_kind)
        self.assertEqual("playtime.weapon.fire", driver.SPEC.expected_action_id)
        self.assertEqual(1, driver.SPEC.successor_action_count)
        self.assertIn("Full Editing", driver.SPEC.excluded_scope)
        self.assertIn("tablet", driver.SPEC.excluded_scope)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertNotIn('"profile": "tablet"', source)
        self.assertNotIn('"publicationAuthorized": True', source)
        self.assertIn("device.publish_document_for_documents_ui(", source)
        self.assertIn("proof_expectation=proof_expectation", source)
        self.assertIn("import subprocess", source)
        self.assertIsNotNone(driver.subprocess.CalledProcessError)
        self.assertNotIn("device.push_verified(", source)

    def test_wrapper_reuses_exact_typed_review_apply_receipt_and_xml_authority(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        preserved = driver.playtime.assert_before_state(root)
        saved = copy.deepcopy(root)
        saved_clip = driver.playtime.weapon.active_clip(saved).find("count")
        saved_ammo = driver.playtime.weapon.linked_ammo(saved).find("qty")
        assert saved_clip is not None and saved_ammo is not None
        saved_clip.text = "8"
        saved_ammo.text = "8"
        result = driver.playtime.assert_after_state(saved, preserved)
        self.assertEqual("ShortBurst", result["fireMode"])
        self.assertEqual(3, result["roundsConsumed"])
        self.assertEqual(8, result["ammoRemaining"])
        hostile = copy.deepcopy(saved)
        hostile.find("karma").text = "18"  # type: ignore[union-attr]
        with self.assertRaisesRegex(RuntimeError, "outside the exact"):
            driver.playtime.assert_after_state(hostile, preserved)

    def test_shared_lane_opens_table_family_without_advancement_detour(self) -> None:
        device = mock.Mock(spec=driver.shared.Device)
        with (
            mock.patch.object(driver.lane.physical, "open_career_hub") as open_hub,
            mock.patch.object(driver.lane.physical, "open_choose") as open_choose,
            mock.patch.object(driver.lane.physical, "wait_exact_route") as wait_route,
        ):
            driver.lane.open_lane(device, driver.SPEC)

        open_hub.assert_called_once_with(device)
        open_choose.assert_not_called()
        self.assertEqual(
            [
                mock.call(
                    "sr5-career/table",
                    timeout=90,
                    backward_scrolls=0,
                    forward_scrolls=24,
                    scroll_distance_ratio=0.18,
                    evidence_prefix="sr5-career-table-family",
                    surface_name="SR5 Career table family route",
                ),
                mock.call(
                    driver.SPEC.action_route,
                    timeout=90,
                    backward_scrolls=0,
                    forward_scrolls=24,
                    scroll_distance_ratio=0.18,
                    evidence_prefix="sr5-playtime-route",
                    surface_name="SR5 playtime typed route",
                ),
            ],
            device.tap_exact_resource_id_bidirectional.call_args_list,
        )
        self.assertEqual(
            [
                mock.call(device, "sr5-career/table", timeout=90),
                mock.call(device, driver.SPEC.lane_route, timeout=120),
            ],
            wait_route.call_args_list,
        )

    def test_playtime_proof_trace_uses_lane_exact_process_groups(self) -> None:
        process_by_label = {
            "imported-runner-pending-save": "process-1",
            "imported-runner-checkpointed": "process-1",
            "playtime-ready": "process-1",
            "quote-ready": "process-1",
            "review-ready": "process-1",
            "review-restart-runner": "process-2",
            "review-resumed": "process-2",
            "receipt-ready": "process-2",
            "saved-runner": "process-2",
            "receipt-recovered": "process-3",
            "final-restored-runner": "process-4",
            "saved-successor-ready": "process-4",
        }
        trace = [
            {"label": label, "processInstanceId": process_id}
            for label, process_id in process_by_label.items()
        ]
        driver.lane.require_proof_process_transitions(trace, "playtime")

        near_lane = copy.deepcopy(trace)
        near_lane[2]["label"] = "before-run-ready"
        with self.assertRaisesRegex(RuntimeError, "trace is incomplete"):
            driver.lane.require_proof_process_transitions(near_lane, "playtime")

        reused_process = copy.deepcopy(trace)
        for item in reused_process:
            if item["label"] == "receipt-recovered":
                item["processInstanceId"] = "process-4"
        with self.assertRaisesRegex(RuntimeError, "rotate process identity"):
            driver.lane.require_proof_process_transitions(reused_process, "playtime")

    def test_exact_resource_route_preserves_embedded_slashes(self) -> None:
        route = driver.shared.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/sr5-career/table"
                ),
                "content-desc": "At the table",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,366][984,519]",
            }
        )
        self.assertTrue(
            driver.shared.Device._has_exact_resource_id(route, "sr5-career/table")
        )
        self.assertFalse(driver.shared.Device._has_exact_resource_id(route, "table"))

        device = object.__new__(driver.shared.Device)
        with (
            mock.patch.object(
                driver.shared.Device,
                "hierarchy",
                return_value=[route],
            ),
            mock.patch.object(
                driver.shared.Device,
                "node_has_tappable_bounds",
                return_value=True,
            ),
        ):
            self.assertIs(
                route,
                device.wait_exact_resource_id_bidirectional(
                    "sr5-career/table",
                    timeout=1,
                    backward_scrolls=0,
                    forward_scrolls=0,
                ),
            )
            self.assertIs(
                route,
                device.wait_for_single_exact_accessibility_value(
                    "sr5-career/table",
                    timeout=1,
                    evidence_prefix="sr5-career-route",
                    surface_name="SR5 Career route",
                ),
            )

    def test_source_graph_binds_existing_app_presentation_core_and_shared_proof(self) -> None:
        paths = driver.source_paths(Path("/workspace"))
        self.assertEqual(
            Path(driver.lane.__file__).resolve(),
            paths["typedLaneAuthorityHelperSha256"],
        )
        self.assertEqual(
            Path(driver.playtime.__file__).resolve(),
            paths["playtimeShortBurstAuthorityHelperSha256"],
        )
        self.assertEqual(
            ROOT / "src/Chummer.Android/Native/Sr5TableWizardPage.cs",
            paths["tableWizardPageSha256"],
        )
        self.assertEqual(
            ROOT / "src/Chummer.Android/Proof/Api36ProofState.cs",
            paths["api36ProofStateSha256"],
        )
        self.assertEqual(
            ROOT / "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs",
            paths["api36ProofPublisherSha256"],
        )
        self.assertEqual(
            ROOT / "tests/api36_proof_state.py",
            paths["api36ProofReaderSha256"],
        )
        self.assertEqual(
            Path(
                "/workspace/chummer-presentation/Chummer.Presentation/Overview/"
                "Sr5TableWizardSession.cs"
            ),
            paths["tableWizardSessionSha256"],
        )
        self.assertEqual(
            Path(
                "/workspace/chummer-core-engine/Chummer.Contracts/Characters/"
                "CharacterWeaponFireRules.cs"
            ),
            paths["careerWeaponRulesSha256"],
        )

    def _args(self, root: Path, *, serial: str = "emulator-5554") -> argparse.Namespace:
        apk = root / "candidate.apk"
        apk.write_bytes(b"apk")
        evidence = root / "evidence"
        return argparse.Namespace(
            adb=root / "adb",
            apk=apk,
            serial=serial,
            evidence=evidence,
            receipt=root / "receipt.json",
            workspace_root=root,
            career_runner=FIXTURE,
        )

    def test_execute_delegates_one_exact_hosted_journey_and_emits_fail_closed_scope(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            authority = root / "source"
            authority.write_text("authority", encoding="utf-8")
            device = mock.Mock(spec=driver.shared.Device)
            device.shell.side_effect = ["36", "x86_64", "1"]
            provider_registration = {
                "sha256": driver.shared.sha256(FIXTURE),
                "schema": "chummer.android.documentsui-provider-registration/v3",
                "status": "pass",
            }
            device.publish_document_for_documents_ui.return_value = provider_registration
            proof = {
                "scope": {
                    "representativeAction": driver.SPEC.representative_action,
                    "excluded": list(driver.SPEC.excluded_scope),
                    "claim": "one representative typed action only",
                }
            }
            args = self._args(root)
            with (
                mock.patch.object(driver, "source_paths", return_value={"sourceSha256": authority}),
                mock.patch.object(driver.shared, "Device", return_value=device),
                mock.patch.dict(
                    driver.os.environ,
                    {"GITHUB_RUN_ID": "42", "CHUMMER_E2E_APK_ARTIFACT_ATTEMPT": "3"},
                ),
                mock.patch.object(driver.lane, "prove_lane", return_value=proof) as prove,
            ):
                receipt = driver.execute(args)

            device.install_verified.assert_called_once_with(
                args.apk.resolve(), driver.shared.sha256(args.apk), "--no-streaming", "-r"
            )
            device.publish_document_for_documents_ui.assert_called_once_with(
                FIXTURE, driver.shared.sha256(FIXTURE)
            )
            device.require_shared_storage_readiness.assert_called_once_with(
                deadline=mock.ANY,
                hosted_api_level="36",
                hosted_abi="x86_64",
                hosted_emulator="1",
                hosted_proof_attempt=True,
            )
            prove.assert_called_once_with(
                device,
                driver.SPEC,
                FIXTURE,
                driver.shared.sha256(FIXTURE),
                assert_before=driver.playtime.assert_before_state,
                assert_after=driver.playtime.assert_after_state,
                proof_expectation=mock.ANY,
            )
            self.assertEqual("pass", receipt["status"])
            self.assertEqual("phone", receipt["profile"])
            self.assertEqual("playtime-short-burst", receipt["journey"])
            self.assertEqual(36, receipt["apiLevel"])
            self.assertEqual("x86_64", receipt["abi"])
            self.assertFalse(receipt["publicationAuthorized"])
            self.assertEqual(
                provider_registration,
                receipt["documentsUiProviderRegistration"],
            )
            self.assertEqual({driver.CONTROL: "pass"}, receipt["controls"])
            self.assertEqual(set(driver.PROOF_STAGES), set(receipt["journeys"]))
            self.assertEqual(
                ["Full Editing", "tablet readiness", "publication authority"],
                receipt["doesNotAssert"],
            )

    def test_hosted_device_contract_rejects_wrong_api_abi_and_unsafe_serial(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            authority = root / "source"
            authority.write_text("authority", encoding="utf-8")
            cases = (
                ("unsafe serial!", [], "safe ASCII"),
                ("emulator-5554", ["35"], "requires API 36"),
                ("emulator-5554", ["36", "arm64-v8a"], "hosted x86_64"),
            )
            for serial, observations, message in cases:
                with self.subTest(serial=serial, observations=observations):
                    device = mock.Mock(spec=driver.shared.Device)
                    device.shell.side_effect = observations
                    with (
                        mock.patch.object(
                            driver,
                            "source_paths",
                            return_value={"sourceSha256": authority},
                        ),
                        mock.patch.object(driver.shared, "Device", return_value=device),
                        self.assertRaisesRegex(RuntimeError, message),
                    ):
                        driver.execute(self._args(root, serial=serial))
                    device.install_verified.assert_not_called()

    def test_source_graph_change_after_proof_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            authority = root / "source"
            authority.write_text("before", encoding="utf-8")
            device = mock.Mock(spec=driver.shared.Device)
            device.shell.side_effect = ["36", "x86_64", "1"]
            device.publish_document_for_documents_ui.return_value = {
                "sha256": driver.shared.sha256(FIXTURE),
            }

            def mutate_authority(*_args: object, **_kwargs: object) -> dict[str, object]:
                authority.write_text("after", encoding="utf-8")
                return {"scope": {}}

            with (
                mock.patch.object(driver, "source_paths", return_value={"sourceSha256": authority}),
                mock.patch.object(driver.shared, "Device", return_value=device),
                mock.patch.dict(
                    driver.os.environ,
                    {"GITHUB_RUN_ID": "42", "CHUMMER_E2E_APK_ARTIFACT_ATTEMPT": "3"},
                ),
                mock.patch.object(driver.lane, "prove_lane", side_effect=mutate_authority),
                self.assertRaisesRegex(RuntimeError, "source authority changed"),
            ):
                driver.execute(self._args(root))


if __name__ == "__main__":
    unittest.main()
