import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_creation_lifestyle_delete_e2e.py"


class CreationLifestyleDeleteDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_arm64_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for token in (
            'CONTROL = "CharacterCreate.cmdDeleteLifestyle"',
            'PACKAGE = "com.myexternalbrain.chummer"',
            'ABI = "arm64-v8a"',
            'if api != "36"',
            'if abi != "arm64-v8a"',
            '"profile": "phone"',
            '"journey": "creation-lifestyle-delete"',
            '"creationCancelNoOp": "pass"',
            '"creationLifestyleAndQualityImprovementsDeleted": "pass"',
            '"creationProcessRestart": "pass"',
            '"lifestyleDeleteRulesSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
            '"creationFixtureSha256"',
        ):
            self.assertIn(token, source)

    def test_receipt_follows_cancel_delete_same_session_and_process_restart(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        receipt = source.index("receipt = {")
        self.assertGreater(receipt, source.index('device.tap("Cancel")'))
        self.assertGreater(receipt, source.index('device.tap("Delete")'))
        self.assertGreater(receipt, source.index('device.shell("am", "force-stop", PACKAGE)'))
        self.assertGreater(receipt, source.index('assert_workspace(device, target_present=False)'))

    def test_fixture_pins_identity_cascade_cost_and_zero_refund_sentinels(self) -> None:
        root = ET.parse(ROOT / "tests/fixtures/creation-lifestyle-delete-e2e.chum5").getroot()
        self.assertEqual("False", root.findtext("created"))
        self.assertEqual("8123.45", root.findtext("nuyen"))
        self.assertEqual("Unrelated expense sentinel", root.findtext("./expenses/expense/reason"))
        self.assertEqual(2, len(root.findall("./lifestyles/lifestyle")))
        self.assertEqual(
            "Deleted Lifestyle raw cost sentinel",
            root.findtext("./lifestyles/lifestyle[1]/notes"),
        )
        markers = {
            item.findtext("marker") for item in root.findall("./improvements/improvement")
        }
        self.assertEqual(
            {"remove-exact", "remove-legacy-prefix", "keep-quality", "keep-custom"},
            markers,
        )


if __name__ == "__main__":
    unittest.main()
