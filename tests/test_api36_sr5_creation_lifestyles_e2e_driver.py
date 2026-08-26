"""Source and pure-helper contracts for the SR5 creation Lifestyles phone proof."""

from __future__ import annotations

import ast
import importlib.util
from pathlib import Path
import re
import sys
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_sr5_creation_lifestyles_e2e.py"
FIXTURE = REPO / "tests/fixtures/creation-lifestyles-e2e.chum5"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("creation_lifestyles_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


def authority(
    revision: int,
    payload: str,
    *,
    workspace_id: str = "workspace-lifestyles-e2e",
) -> driver.shared.WorkspaceAuthority:
    return driver.shared.WorkspaceAuthority(
        workspace_id=workspace_id,
        content_revision=revision,
        saved_revision=revision,
        payload_sha256=payload,
        document_sha256=("d" if revision == 1 else "e") * 64,
    )


def rendered(title: str, value: str) -> dict[str, str]:
    text = f"{title} · {value}"
    return {"text": text, "content-desc": text}


def receipt_values(**overrides: str) -> dict[str, dict[str, str]]:
    values = {
        "creation-lifestyle-receipt-id": rendered(
            "Receipt ID",
            "creation-lifestyle-" + "1" * 24,
        ),
        "creation-lifestyle-receipt-digest": rendered(
            "Receipt digest",
            "sha256:" + "a" * 64,
        ),
        "creation-lifestyle-receipt-content-before": rendered(
            "Content before",
            "sha256:" + "b" * 64,
        ),
        "creation-lifestyle-receipt-content-after": rendered(
            "Content after",
            "sha256:" + "c" * 64,
        ),
        "creation-lifestyle-receipt-source": rendered(
            "Source",
            "sha256:" + "d" * 64,
        ),
        "creation-lifestyle-receipt-rules": rendered(
            "Rules",
            "sha256:" + "e" * 64,
        ),
        "creation-lifestyle-receipt-runtime": rendered(
            "Runtime",
            "sha256:" + "f" * 64,
        ),
    }
    titles = {
        "creation-lifestyle-receipt-id": "Receipt ID",
        "creation-lifestyle-receipt-digest": "Receipt digest",
        "creation-lifestyle-receipt-content-before": "Content before",
        "creation-lifestyle-receipt-content-after": "Content after",
        "creation-lifestyle-receipt-source": "Source",
        "creation-lifestyle-receipt-rules": "Rules",
        "creation-lifestyle-receipt-runtime": "Runtime",
    }
    for key, value in overrides.items():
        values[key] = rendered(titles[key], value)
    return values


class CatalogDevice:
    def __init__(self, nodes: list[driver.shared.UiNode]) -> None:
        self.nodes = nodes
        self.captures: list[str] = []
        self.swipes = 0

    def hierarchy(self) -> list[driver.shared.UiNode]:
        return self.nodes

    @staticmethod
    def node_has_tappable_bounds(_node: driver.shared.UiNode) -> bool:
        return True

    def swipe_up(self, **_kwargs: object) -> None:
        self.swipes += 1

    def capture(self, value: str) -> None:
        self.captures.append(value)


def catalog_node(
    suffix: str,
    *,
    description: str = "Low. 2000 per month · SR5 369",
) -> driver.shared.UiNode:
    return driver.shared.UiNode(
        {
            "resource-id": f"com.chummer6.app:id/{driver.CATALOG_PREFIX}{suffix}",
            "content-desc": description,
            "text": "",
            "enabled": "true",
            "clickable": "true",
            "bounds": "[10,10][1000,120]",
        }
    )


class Api36Sr5CreationLifestylesE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_source_and_apk_bound_without_foundation(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            'STAGE_ID = "creation-stage-contacts-lifestyles"',
            'CATALOG_PREFIX = "creation-lifestyle-catalog-"',
            '"profile": "phone"',
            '"journey": "sr5-priority-creation-lifestyles-physical"',
            "load_and_verify_manifest(",
            "physical.source_graph_snapshot(",
            "physical.android_device_observation(device)",
            'device.require_transport_stability(expected_api_level="36")',
            "device.install_verified(",
            "device.push_verified(",
            "shared.force_stop_and_launch_new_process",
            "physical.write_receipt_atomically",
            '"contactsHelperSha256"',
            '"lifestylesContractSha256"',
            '"lifestylesPresenterSha256"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertNotIn("creation-stage-foundation", source)
        self.assertNotIn("creation-foundation-page", source)
        self.assertNotIn('"profile": "tablet"', source)

    def test_journey_orders_catalog_configure_preview_confirm_and_reopens(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        journey = source[
            source.index("def prove_lifestyles_journey(") : source.index("def parse_args(")
        ]
        markers = (
            "before_list = open_lifestyles(device, expected_lifestyle_id=None)",
            "selected_catalog, before_edit = open_editor_from_catalog(device)",
            'set_exact_text(device, "creation-lifestyle-name", CREATED_NAME)',
            'set_exact_text(device, "creation-lifestyle-increments", CREATED_INCREMENTS)',
            'set_exact_text(device, "creation-lifestyle-city", CREATED_CITY)',
            'tap_enabled_control(device, "creation-lifestyle-preview")',
            'require_control_state(device, "creation-lifestyle-confirm", enabled=False)',
            "checkbox = require_control_state(",
            'tap_enabled_control(device, "creation-lifestyle-confirm")',
            "receipt = receipt_projection(receipt_values)",
            "validate_receipt_projection(receipt, imported=imported, saved=saved)",
            "same_session = assert_reopened_lifestyle(",
            "restart = shared.force_stop_and_launch_new_process",
            "shared.require_restored_authority(saved, restored)",
            "after_restart = assert_reopened_lifestyle(",
        )
        positions = [journey.index(marker) for marker in markers]
        self.assertEqual(sorted(positions), positions)
        for identifier in (
            "creation-lifestyles-binding",
            "creation-lifestyles-budget",
            "creation-lifestyles-authority",
            "creation-lifestyle-edit-binding",
            "creation-lifestyle-preview-binding",
            "creation-lifestyle-preview-digest",
            "creation-lifestyle-plan-digest",
            "creation-lifestyle-write-1-create",
            "creation-lifestyle-preview-preservation",
            "creation-lifestyle-confirm-receipt",
            "creation-lifestyle-receipt-content-before",
            "creation-lifestyle-receipt-content-after",
        ):
            self.assertIn(identifier, source)

    def test_binding_parsers_accept_only_exact_revision_identity_and_digest_shapes(self) -> None:
        listing = driver.parse_list_binding(
            "Revision 7 · saved 7 · snapshot sha256:123456789abc · source abcdef0123456789abc"
        )
        self.assertEqual(7, listing["contentRevision"])
        lifestyle_id = "87796157-0366-4154-836a-034326e8e924"
        edit = driver.parse_edit_binding(
            f"Revision 7 · snapshot sha256:123456789abc · Lifestyle {lifestyle_id}"
        )
        self.assertEqual(lifestyle_id, edit["lifestyleId"])
        preview = driver.parse_preview_binding(
            f"Revision 7 · saved 7 · Create {lifestyle_id}",
            lifestyle_id,
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
            driver.parse_preview_binding(
                f"Revision 7 · saved 7 · Edit {lifestyle_id}",
                lifestyle_id,
            )
        with self.assertRaises(RuntimeError):
            driver.parse_edit_binding(
                "Revision 7 · snapshot sha256:123456789abc · Lifestyle "
                "11111111-1111-1111-1111-111111111111",
                lifestyle_id,
            )

    def test_labeled_value_rejects_missing_conflicting_or_wrong_labels(self) -> None:
        self.assertEqual(
            "sha256:" + "a" * 64,
            driver.labeled_value(
                rendered("Preview digest", "sha256:" + "a" * 64),
                "Preview digest",
                "preview",
            ),
        )
        hostile = (
            {"text": "sha256:" + "a" * 64, "content-desc": "sha256:" + "a" * 64},
            rendered("Wrong", "sha256:" + "a" * 64),
            {"text": "Preview digest · one", "content-desc": "Preview digest · two"},
        )
        for attributes in hostile:
            with self.subTest(attributes=attributes), self.assertRaises(RuntimeError):
                driver.labeled_value(attributes, "Preview digest", "preview")

    def test_receipt_parser_and_validation_bind_exact_payload_successor(self) -> None:
        receipt = driver.receipt_projection(receipt_values())
        imported = authority(1, "b" * 64)
        saved = authority(2, "c" * 64)
        driver.validate_receipt_projection(receipt, imported=imported, saved=saved)
        hostile_receipts = (
            {"creation-lifestyle-receipt-id": "creation-lifestyle-pass"},
            {"creation-lifestyle-receipt-digest": "a" * 64},
            {"creation-lifestyle-receipt-runtime": "sha256:xyz"},
        )
        for override in hostile_receipts:
            with self.subTest(override=override), self.assertRaises(RuntimeError):
                driver.receipt_projection(receipt_values(**override))
        with self.assertRaisesRegex(RuntimeError, "saved successor"):
            driver.validate_receipt_projection(
                receipt,
                imported=imported,
                saved=authority(3, "c" * 64),
            )
        with self.assertRaisesRegex(RuntimeError, "content-after"):
            driver.validate_receipt_projection(
                receipt,
                imported=imported,
                saved=authority(2, "f" * 64),
            )
        with self.assertRaisesRegex(RuntimeError, "workspace"):
            driver.validate_receipt_projection(
                receipt,
                imported=imported,
                saved=authority(2, "c" * 64, workspace_id="other"),
            )

    def test_catalog_discovery_is_accessibility_cardinality_and_blocker_bound(self) -> None:
        option = catalog_node("lifestyle-451eef87-d18e-4bee-a972-1ee165b08522")
        device = CatalogDevice([option])
        with mock.patch.object(driver.shared, "reset_scroll_to_top"):
            observed = driver.collect_catalog_options(device, max_scrolls=2)
        self.assertEqual(1, len(observed))
        self.assertEqual(driver.resource_id(option), observed[0].resource_id)
        self.assertEqual("Low. 2000 per month · SR5 369", observed[0].label)

        unlabeled = catalog_node("unlabeled", description="")
        with mock.patch.object(
            driver.shared,
            "reset_scroll_to_top",
        ), self.assertRaises(RuntimeError):
            driver.collect_catalog_options(CatalogDevice([unlabeled]), max_scrolls=1)
        with mock.patch.object(driver.shared, "reset_scroll_to_top"), self.assertRaisesRegex(
            RuntimeError,
            "cardinality 2",
        ):
            driver.collect_catalog_options(CatalogDevice([option, option]), max_scrolls=1)
        blocker = driver.shared.UiNode(
            {
                "resource-id": f"com.chummer6.app:id/{driver.BLOCKER_IDS[0]}",
                "bounds": "[10,10][1000,120]",
            }
        )
        with mock.patch.object(driver.shared, "reset_scroll_to_top"), self.assertRaisesRegex(
            RuntimeError,
            "blocker",
        ):
            driver.collect_catalog_options(CatalogDevice([blocker]), max_scrolls=1)

    def test_lifestyle_controls_fail_closed_on_state_or_tappability_drift(self) -> None:
        device = mock.Mock()
        device.wait_exact_resource_id_bidirectional.return_value = driver.shared.UiNode(
            {
                "enabled": "false",
                "clickable": "false",
                "checked": "false",
                "bounds": "[10,10][100,70]",
            }
        )
        driver.require_control_state(device, "preview", enabled=False, checked=False)
        with self.assertRaisesRegex(RuntimeError, "enabled state differs"):
            driver.require_control_state(device, "preview", enabled=True)

        device.wait_exact_resource_id_bidirectional.return_value = driver.shared.UiNode(
            {
                "enabled": "true",
                "clickable": "true",
                "checked": "false",
                "bounds": "[10,10][100,70]",
            }
        )
        device.node_has_tappable_bounds.return_value = False
        with self.assertRaisesRegex(RuntimeError, "not clickable and tappable"):
            driver.require_control_state(device, "confirm", enabled=True)

    def test_fixture_is_creation_only_empty_and_preserves_unrelated_state(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        driver.assert_fixture_state(root, lifestyle_id=None)
        self.assertEqual("default.xml", root.findtext("settings"))
        self.assertEqual("10000", root.findtext("startingnuyen"))
        self.assertEqual([], root.findall("./lifestyles/lifestyle"))
        contact = root.find("./contacts/contact")
        self.assertIsNotNone(contact)
        self.assertEqual("UnrelatedStateSentinel", contact.findtext("role"))

    def test_fixture_saved_projection_requires_exact_fields_and_siblings(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        lifestyle_id = "87796157-0366-4154-836a-034326e8e924"
        lifestyles = root.find("lifestyles")
        assert lifestyles is not None
        row = ET.SubElement(lifestyles, "lifestyle")
        expected = {
            "sourceid": "451eef87-d18e-4bee-a972-1ee165b08522",
            "guid": lifestyle_id,
            "name": driver.CREATED_NAME,
            "months": driver.CREATED_INCREMENTS,
            "percentage": "100",
            "roommates": "0",
            "city": driver.CREATED_CITY,
            "district": driver.CREATED_DISTRICT,
            "type": "Standard",
            "increment": "Month",
            "cost": "2000",
            "baselifestyle": "Low",
        }
        for key, value in expected.items():
            ET.SubElement(row, key).text = value
        driver.assert_fixture_state(root, lifestyle_id=lifestyle_id)
        row.find("city").text = "Drifted"
        with self.assertRaises(RuntimeError):
            driver.assert_fixture_state(root, lifestyle_id=lifestyle_id)

    def test_argument_and_failure_receipts_never_claim_pass(self) -> None:
        argument = driver.argument_failure_receipt(2)
        self.assertEqual("fail", argument["status"])
        self.assertEqual("fail", argument["executionStatus"])
        args = type("Args", (), {"serial": "physical-phone"})()
        failure = driver.failure_receipt(args, RuntimeError("boom"), {})
        self.assertEqual("fail", failure["status"])
        self.assertEqual("RuntimeError", failure["failure"]["type"])
        self.assertNotIn("journeys", failure)

    def test_main_help_is_side_effect_free(self) -> None:
        with mock.patch.object(driver, "execute") as execute:
            self.assertEqual(0, driver.main(["--help"]))
        execute.assert_not_called()

    def test_receipt_and_fixture_paths_are_not_inside_tracked_source_by_default(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        self.assertIn("physical.validate_external_output_path(", source)
        self.assertIn("physical.validate_output_layout(", source)
        self.assertIn("physical.safe_fixture_basename(fixture)", source)
        self.assertIn("source_after != source_before", source)
        self.assertIn("shared.authorize_remote_cleanup_once(remote)", source)
        self.assertNotRegex(source, re.compile(r"write_text\([^)]*status.*pass", re.DOTALL))


if __name__ == "__main__":
    unittest.main()
