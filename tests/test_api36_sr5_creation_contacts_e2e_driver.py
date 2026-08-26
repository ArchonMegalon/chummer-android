from __future__ import annotations

import ast
import copy
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_sr5_creation_contacts_e2e.py"
FIXTURE = REPO / "tests/fixtures/creation-contact-pet-e2e.chum5"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("creation_contacts_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


def authority(
    revision: int,
    payload: str,
    *,
    workspace_id: str = "workspace-contacts-e2e",
) -> driver.shared.WorkspaceAuthority:
    return driver.shared.WorkspaceAuthority(
        workspace_id=workspace_id,
        content_revision=revision,
        saved_revision=revision,
        payload_sha256=payload,
        document_sha256=("d" if revision == 1 else "e") * 64,
    )


def receipt_values(**overrides: str) -> dict[str, dict[str, str]]:
    digest = "sha256:" + "a" * 64
    values = {
        "creation-contact-receipt-id": "creation-contact-" + "1" * 24,
        "creation-contact-receipt-previous-workspace-revision": "1",
        "creation-contact-receipt-workspace-revision": "2",
        "creation-contact-receipt-previous-content-revision": "1",
        "creation-contact-receipt-content-revision": "2",
        "creation-contact-receipt-previous-saved-revision": "1",
        "creation-contact-receipt-saved-revision": "2",
        "creation-contact-receipt-digest": digest,
        "creation-contact-receipt-content-before": "sha256:" + "b" * 64,
        "creation-contact-receipt-content-after": "sha256:" + "c" * 64,
        "creation-contact-receipt-idempotency-digest": "sha256:" + "d" * 64,
        "creation-contact-receipt-command-digest": "sha256:" + "e" * 64,
    }
    values.update(overrides)
    return {key: {"text": value, "content-desc": value} for key, value in values.items()}


class FakeControlDevice:
    def __init__(self, node: driver.shared.UiNode, tappable: bool = True) -> None:
        self.node = node
        self.tappable = tappable
        self.captures: list[str] = []

    def wait_exact_resource_id_bidirectional(self, *_args: object, **_kwargs: object) -> driver.shared.UiNode:
        return self.node

    def node_has_tappable_bounds(self, _node: driver.shared.UiNode) -> bool:
        return self.tappable

    def capture(self, value: str) -> None:
        self.captures.append(value)


class Api36Sr5CreationContactsE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_source_and_apk_bound_and_never_uses_foundation_route(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            'STAGE_ID = "creation-stage-contacts-lifestyles"',
            'CONTACT_ID = "50d92979-524d-4cb5-898e-196771e3c786"',
            '"profile": "phone"',
            '"journey": "sr5-priority-creation-contacts-physical"',
            "load_and_verify_manifest(",
            "physical.source_graph_snapshot(",
            "physical.android_device_observation(device)",
            "device.require_transport_stability(expected_api_level=\"36\")",
            "device.install_verified(",
            "device.push_verified(",
            "shared.force_stop_and_launch_new_process",
            "physical.write_receipt_atomically",
            '"physicalHelperSha256"',
            '"buildProvenanceHelperSha256"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertNotIn("creation-stage-foundation", source)
        self.assertNotIn("creation-foundation-page", source)
        self.assertNotIn('"profile": "tablet"', source)

    def test_journey_orders_disabled_preview_edit_preview_disabled_confirm_apply_and_reopens(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        journey = source[source.index("def prove_contacts_journey(") : source.index("def parse_args(")]
        markers = (
            'require_control_state(device, "creation-contact-preview", enabled=False)',
            'set_exact_text(device, "creation-contact-field-name", UPDATED_NAME)',
            'set_exact_text(device, "creation-contact-field-role", UPDATED_ROLE)',
            'tap_enabled_control(device, "creation-contact-preview")',
            'require_control_state(device, "creation-contact-confirm", enabled=False)',
            "checkbox = require_control_state(",
            'tap_enabled_control(device, "creation-contact-confirm")',
            "receipt = receipt_projection(receipt_values)",
            "validate_receipt_projection(receipt, imported=imported, saved=saved)",
            "same_session = assert_updated_contact_surface",
            "restart = shared.force_stop_and_launch_new_process",
            "shared.require_restored_authority(saved, restored)",
            "after_restart = assert_updated_contact_surface",
        )
        positions = [journey.index(marker) for marker in markers]
        self.assertEqual(sorted(positions), positions)
        for identifier in (
            "creation-contact-preview-digest",
            "creation-contact-plan-digest",
            "creation-contact-write-1-name",
            "creation-contact-write-2-role",
            "creation-contact-confirm-receipt",
            "creation-contact-receipt-previous-workspace-revision",
            "creation-contact-receipt-workspace-revision",
            "creation-contact-receipt-previous-content-revision",
            "creation-contact-receipt-content-revision",
            "creation-contact-receipt-previous-saved-revision",
            "creation-contact-receipt-saved-revision",
            "creation-contact-receipt-content-before",
            "creation-contact-receipt-content-after",
        ):
            self.assertIn(identifier, source)

    def test_binding_parsers_accept_only_exact_revision_identity_and_digest_shapes(self) -> None:
        self.assertEqual(
            7,
            driver.parse_list_binding(
                "Revision 7 · saved 7 · snapshot sha256:123456789abc · source abcdef0123456789abc"
            )["contentRevision"],
        )
        edit = driver.parse_edit_binding(
            "Revision 7 · snapshot sha256:123456789abc · Contact "
            + driver.CONTACT_ID
            + " · authority abcdef0123456789abc"
        )
        self.assertEqual(driver.CONTACT_ID, edit["contactId"])
        preview = driver.parse_preview_binding(
            "Revision 7 · saved 7 · Contact " + driver.CONTACT_ID
        )
        self.assertEqual(7, preview["savedRevision"])
        hostile = (
            "Revision 7 · saved 7 · snapshot unavailable · source abcdef0123456789abc",
            "Revision -1 · saved 7 · snapshot sha256:123456789abc · source abcdef0123456789abc",
            "Revision 7 saved 7 snapshot sha256:123456789abc source abcdef0123456789abc",
        )
        for value in hostile:
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                driver.parse_list_binding(value)
        with self.assertRaises(RuntimeError):
            driver.parse_edit_binding(
                "Revision 7 · snapshot sha256:123456789abc · Contact "
                "11111111-1111-1111-1111-111111111111 · authority abcdef0123456789abc"
            )

    def test_disabled_and_enabled_control_states_fail_closed(self) -> None:
        disabled = FakeControlDevice(
            driver.shared.UiNode(
                {
                    "enabled": "false",
                    "clickable": "false",
                    "bounds": "[10,10][100,70]",
                }
            )
        )
        driver.require_control_state(disabled, "preview", enabled=False)
        with self.assertRaises(RuntimeError):
            driver.require_control_state(disabled, "preview", enabled=True)

        enabled = FakeControlDevice(
            driver.shared.UiNode(
                {
                    "enabled": "true",
                    "clickable": "true",
                    "checked": "false",
                    "bounds": "[10,10][100,70]",
                }
            )
        )
        driver.require_control_state(enabled, "confirm", enabled=True, checked=False)
        enabled.tappable = False
        with self.assertRaisesRegex(RuntimeError, "not clickable and tappable"):
            driver.require_control_state(enabled, "confirm", enabled=True)

    def test_receipt_parser_rejects_malformed_values_and_validation_binds_exact_successor(self) -> None:
        parsed = driver.receipt_projection(receipt_values())
        imported = authority(1, "b" * 64)
        saved = authority(2, "c" * 64)
        driver.validate_receipt_projection(parsed, imported=imported, saved=saved)

        hostile_values = (
            {"creation-contact-receipt-content-revision": "02"},
            {"creation-contact-receipt-id": "creation-contact-pass"},
            {"creation-contact-receipt-digest": "a" * 64},
            {"creation-contact-receipt-idempotency-digest": "sha256:xyz"},
            {"creation-contact-receipt-workspace-revision": "3"},
        )
        for override in hostile_values:
            with self.subTest(override=override):
                values = receipt_values(**override)
                if "creation-contact-receipt-workspace-revision" in override:
                    candidate = driver.receipt_projection(values)
                    with self.assertRaises(RuntimeError):
                        driver.validate_receipt_projection(
                            candidate,
                            imported=imported,
                            saved=saved,
                        )
                else:
                    with self.assertRaises(RuntimeError):
                        driver.receipt_projection(values)

        replay = driver.receipt_projection(receipt_values())
        with self.assertRaises(RuntimeError):
            driver.validate_receipt_projection(
                replay,
                imported=imported,
                saved=authority(3, "c" * 64),
            )

    def test_receipt_validation_rejects_workspace_drift_and_same_payload(self) -> None:
        parsed = driver.receipt_projection(receipt_values())
        imported = authority(1, "b" * 64)
        with self.assertRaisesRegex(RuntimeError, "workspace identity"):
            driver.validate_receipt_projection(
                parsed,
                imported=imported,
                saved=authority(2, "c" * 64, workspace_id="different-workspace"),
            )

        same_payload = driver.receipt_projection(
            receipt_values(
                **{
                    "creation-contact-receipt-content-before": "sha256:" + "c" * 64,
                    "creation-contact-receipt-content-after": "sha256:" + "c" * 64,
                }
            )
        )
        with self.assertRaisesRegex(RuntimeError, "no payload change"):
            driver.validate_receipt_projection(
                same_payload,
                imported=authority(1, "c" * 64),
                saved=authority(2, "c" * 64),
            )

    def test_fixture_pins_creation_identity_target_and_unrelated_siblings(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("False", root.findtext("created"))
        self.assertEqual("Priority", root.findtext("buildmethod"))
        self.assertEqual("SR5", root.findtext("gameedition"))
        target = driver.target_contact(root)
        self.assertEqual(driver.ORIGINAL_NAME, target.findtext("name"))
        self.assertEqual(driver.ORIGINAL_ROLE, target.findtext("role"))
        driver.assert_contact_xml(root, changed=False)
        self.assertEqual(5, len(root.findall("./contacts/contact")))

    def test_fixture_contract_rejects_target_identity_and_unrelated_field_tampering(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        identity_drift = copy.deepcopy(root)
        target = driver.target_contact(identity_drift)
        target.find("guid").text = "99999999-9999-9999-9999-999999999999"
        with self.assertRaises(RuntimeError):
            driver.target_contact(identity_drift)

        unrelated_drift = copy.deepcopy(root)
        driver.target_contact(unrelated_drift).find("location").text = "Nowhere"
        with self.assertRaisesRegex(RuntimeError, "unrelated field"):
            driver.assert_contact_xml(unrelated_drift, changed=False)

    def test_output_contract_requires_external_atomic_receipt_and_failure_replacement(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "physical.locate_explicit_receipt(raw)",
            "physical.validate_external_output_path(",
            "physical.validate_output_layout(",
            "physical.prepare_receipt_target(receipt_path)",
            "receipt = failure_receipt(args, error, context)",
            "physical.write_receipt_atomically(receipt_path, receipt)",
        ):
            self.assertIn(marker, source)
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "receipt.json"
            driver.physical.prepare_receipt_target(path)
            driver.physical.write_receipt_atomically(path, {"status": "fail"})
            self.assertEqual('{\n  "status": "fail"\n}\n', path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
