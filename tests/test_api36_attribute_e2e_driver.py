import importlib.util
import sys
import unittest
from pathlib import Path
from unittest.mock import Mock, call, patch


REPO_ROOT = Path(__file__).resolve().parents[1]
DRIVER_PATH = REPO_ROOT / "tests" / "run_api36_attribute_e2e.py"
SPEC = importlib.util.spec_from_file_location("run_api36_attribute_e2e", DRIVER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {DRIVER_PATH}")
DRIVER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = DRIVER
SPEC.loader.exec_module(DRIVER)


class Api36AttributeE2EDriverTests(unittest.TestCase):
    def test_phone_edit_sets_base_and_karma_before_saving(self) -> None:
        device = Mock()

        with patch.object(DRIVER, "open_phone_attribute_section") as open_section, patch.object(
            DRIVER.time, "sleep"
        ):
            DRIVER.edit_body_values(
                device,
                base_value=2,
                karma_value=1,
            )

        open_section.assert_called_once_with(device)
        self.assertEqual(
            [
                call("attribute-body", scroll=True),
                call("attribute-base-body", scroll=True),
                call("2", scroll=True),
                call("attribute-karma-body", scroll=True),
                call("1", scroll=True),
                call("attribute-save-body", scroll=True),
            ],
            device.tap.call_args_list,
        )
        device.back.assert_called_once_with()

    def test_phone_assertion_reads_both_editor_values(self) -> None:
        device = Mock()
        device.find.side_effect = [
            DRIVER.shared.UiNode({"text": "2"}),
            DRIVER.shared.UiNode({"text": "1"}),
        ]

        with patch.object(DRIVER, "open_phone_attribute_section") as open_section:
            DRIVER.assert_body_values(
                device,
                expected_base=2,
                expected_karma=1,
            )

        open_section.assert_called_once_with(device)
        self.assertEqual(
            [
                call("attribute-base-body", field_after_label="Base"),
                call("attribute-karma-body", field_after_label="Karma"),
            ],
            device.find.call_args_list,
        )
        device.back.assert_called_once_with()

    def test_direct_attribute_route_skips_missing_overview_selector(self) -> None:
        device = Mock()
        device.find.side_effect = [None, DRIVER.shared.UiNode({"resource-id": "attribute-reaction"})]

        with patch.object(DRIVER.shared, "reset_scroll_to_top") as reset_scroll, patch.object(
            DRIVER.shared, "open_attribute_section"
        ) as open_section:
            DRIVER.open_phone_attribute_section(device)

        reset_scroll.assert_called_once_with(device, swipes=12)
        device.wait.assert_called_once_with(
            "attribute-body", timeout=45, scroll=True, max_scrolls=16
        )
        open_section.assert_not_called()

    def test_receipt_binds_both_driver_layers_and_restart_journey(self) -> None:
        source = DRIVER_PATH.read_text(encoding="utf-8")

        self.assertIn('"journey": "attributes"', source)
        self.assertIn('"sharedDriverSha256": sha256(shared_driver_path)', source)
        self.assertIn('"attributeBaseEditPersisted": "pass"', source)
        self.assertIn('"attributeKarmaEditPersisted": "pass"', source)
        self.assertIn('"processRestartAttributePersistence": "pass"', source)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', source)
        self.assertIn('"--no-streaming"', source)
        self.assertNotIn('choices=("phone", "tablet")', source)
        self.assertNotIn('"profile": args.profile', source)


if __name__ == "__main__":
    unittest.main()
