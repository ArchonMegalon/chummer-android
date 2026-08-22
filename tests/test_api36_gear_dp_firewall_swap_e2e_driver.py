import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_gear_dp_firewall_swap_e2e.py"

class DriverTests(unittest.TestCase):
    def test_driver_is_four_row_phone_api36_arm64_digest_restart_bound(self):
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        assignment = next(node for node in module.body if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets))
        self.assertEqual((
            "CharacterCreate.cboGearDataProcessing", "CharacterCreate.cboGearFirewall",
            "CharacterCareer.cboGearDataProcessing", "CharacterCareer.cboGearFirewall",
        ), tuple(ast.literal_eval(assignment.value)))
        for marker in ('"profile":"phone"', 'api != "36"', '"arm64-v8a" not in abi.split(",")',
                       'shared.PACKAGE', 'device.shell("am", "force-stop"',
                       '"dataProcessingNotificationOnly"', '"activeHomeStatePreserved"',
                       '"gearMatrixSwapRulesSha256"', '"workspaceStoreSha256"',
                       '"creationFixtureSha256"', '"careerFixtureSha256"'):
            self.assertIn(marker, source)
        journeys = next(node for node in module.body if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "JOURNEYS" for target in node.targets))
        self.assertIsInstance(journeys.value, ast.Tuple)
        self.assertEqual(4, len(journeys.value.elts))

    def test_fixtures_pin_raw_bonus_state_provenance_and_unique_identity(self):
        for name, created in (("creation-gear-dp-firewall-swap-e2e.chum5", "False"),
                              ("career-gear-dp-firewall-swap-e2e.chum5", "True")):
            root = ET.parse(REPO / "tests/fixtures" / name).getroot()
            self.assertEqual(created, root.findtext("created"))
            target = root.find("./gears/gear/children/gear")
            self.assertIsNotNone(target)
            self.assertEqual("True", target.findtext("canswapattributes"))
            self.assertTrue(target.findtext("attributearray"))
            self.assertTrue(target.findtext("moddataprocessing"))
            self.assertTrue(target.findtext("modfirewall"))
            self.assertEqual("True", target.findtext("homenode"))
            ids = [node.findtext("guid") for node in root.findall(".//gear")]
            self.assertEqual(len(ids), len(set(ids)))

if __name__ == "__main__":
    unittest.main()
