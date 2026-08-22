import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_vehicle_dp_firewall_swap_e2e.py"

class DriverTests(unittest.TestCase):
    def test_driver_is_eight_row_phone_api36_arm64_digest_restart_bound(self):
        source = DRIVER.read_text(encoding="utf-8"); module = ast.parse(source)
        controls = next(node for node in module.body if isinstance(node, ast.Assign)
                        and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets))
        self.assertEqual(("CharacterCreate.cboVehicleAttack", "CharacterCreate.cboVehicleSleaze",
                          "CharacterCreate.cboVehicleDataProcessing", "CharacterCreate.cboVehicleFirewall",
                          "CharacterCareer.cboVehicleAttack", "CharacterCareer.cboVehicleSleaze",
                          "CharacterCareer.cboVehicleDataProcessing", "CharacterCareer.cboVehicleFirewall"),
                         tuple(ast.literal_eval(controls.value)))
        for marker in ('"profile": "phone"', 'api != "36"', '"arm64-v8a" not in abi.split(",")',
                       "shared.PACKAGE", 'device.shell("am", "force-stop"', '"vehicleMatrixRulesSha256"',
                       '"workspaceStoreSha256"', '"creationFixtureSha256"', '"careerFixtureSha256"',
                       '"descendantTargetsFailClosedCoverage"'):
            self.assertIn(marker, source)
        journeys = next(node for node in module.body if isinstance(node, ast.Assign)
                        and any(isinstance(target, ast.Name) and target.id == "JOURNEYS" for target in node.targets))
        values = [{keyword.arg: ast.literal_eval(keyword.value) for keyword in journey.keywords}
                  for journey in journeys.value.elts]
        self.assertEqual(("creation-attack", "creation-sleaze", "creation-dp", "creation-firewall",
                          "career-attack", "career-sleaze", "career-dp", "career-firewall"),
                         tuple(value["phase"] for value in values))
        self.assertEqual({"Attack", "Sleaze", "Data Processing", "Firewall"},
                         {value["changed"] for value in values})
        self.assertNotIn('"tablet"', source)

    def test_fixtures_pin_vehicle_raw_values_bonuses_state_and_unique_identity(self):
        for name, created in (("creation-vehicle-dp-firewall-swap-e2e.chum5", "False"),
                              ("career-vehicle-dp-firewall-swap-e2e.chum5", "True")):
            root = ET.parse(REPO / "tests/fixtures" / name).getroot(); self.assertEqual(created, root.findtext("created"))
            vehicle = root.find("./vehicles/vehicle"); self.assertIsNotNone(vehicle)
            for field in ("attack", "sleaze", "dataprocessing", "firewall", "attributearray",
                          "modattack", "modsleaze", "moddataprocessing", "modfirewall", "sensor", "cost"):
                self.assertTrue(vehicle.findtext(field))
            self.assertEqual("True", vehicle.findtext("canswapattributes"))
            ids = [node.findtext("guid") for node in root.iter() if node.find("guid") is not None]
            self.assertEqual(len(ids), len(set(ids)))

if __name__ == "__main__": unittest.main()
