import importlib.util
import sys
import unittest
from pathlib import Path
from unittest.mock import Mock, call, patch


REPO_ROOT = Path(__file__).resolve().parents[1]
DRIVER_PATH = REPO_ROOT / "tests" / "run_api36_career_attribute_e2e.py"
SPEC = importlib.util.spec_from_file_location("run_api36_career_attribute_e2e", DRIVER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {DRIVER_PATH}")
DRIVER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = DRIVER
SPEC.loader.exec_module(DRIVER)


class Api36CareerAttributeE2EDriverTests(unittest.TestCase):
    def test_total_assertion_uses_phone_row_content_description(self) -> None:
        device = Mock()
        device.wait.return_value = DRIVER.shared.UiNode(
            {"content-desc": "Body. 3 · 1-6 · Aug 9"}
        )

        with patch.object(
            DRIVER.creation_attributes, "open_phone_attribute_section"
        ) as open_section:
            DRIVER.assert_attribute_total(device, "Body", 3)

        open_section.assert_called_once_with(device)
        device.wait.assert_called_once_with(
            "attribute-body",
            timeout=60,
            scroll=True,
            max_scrolls=20,
            scroll_distance_ratio=0.22,
        )

    def test_improve_and_burn_use_phone_actions_and_confirmation(self) -> None:
        device = Mock()
        with patch.object(DRIVER, "assert_attribute_total") as assert_total, patch.object(
            DRIVER.creation_attributes, "open_phone_attribute_section"
        ), patch.object(DRIVER.time, "sleep"):
            DRIVER.improve_body(device)
            DRIVER.burn_edge(device)

        self.assertIn(call("attribute-improve-body", scroll=True), device.tap.call_args_list)
        self.assertIn(call("attribute-burn-edge", scroll=True), device.tap.call_args_list)
        self.assertIn(call("Burn"), device.tap.call_args_list)
        self.assertEqual([call(device, "Body", 3), call(device, "Edge", 1)], assert_total.call_args_list)

    def test_receipt_is_phone_only_fixture_and_restart_bound(self) -> None:
        source = DRIVER_PATH.read_text(encoding="utf-8")

        self.assertIn('"journey": "career-attributes"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"inputFixtureSha256": sha256(fixture_path)', source)
        self.assertIn('"attributeImprovePersisted": "pass"', source)
        self.assertIn('"attributeBurnEdgePersisted": "pass"', source)
        self.assertIn('"processRestartCareerAttributePersistence": "pass"', source)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', source)
        self.assertNotIn("tablet", source.lower())


if __name__ == "__main__":
    unittest.main()
