import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_vehicle_weapon_firing_mode_e2e.py"


class DriverTests(unittest.TestCase):
    def test_driver_is_two_row_phone_api36_arm64_digest_restart_bound(self):
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        controls = next(
            node
            for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual(
            (
                "CharacterCreate.cboVehicleWeaponFiringMode",
                "CharacterCareer.cboVehicleWeaponFiringMode",
            ),
            tuple(ast.literal_eval(controls.value)),
        )
        for marker in (
            '"profile": "phone"',
            'api != "36"',
            '"arm64-v8a" not in abi.split(",")',
            "shared.PACKAGE",
            'device.shell("am", "force-stop"',
            '"vehicleWeaponFiringModeRulesSha256"',
            '"workspaceStoreSha256"',
            '"creationFixtureSha256"',
            '"careerFixtureSha256"',
            '"descendantTargetsFailClosedCoverage"',
        ):
            self.assertIn(marker, source)
        journeys = next(
            node
            for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "JOURNEYS" for target in node.targets)
        )
        values = [
            {keyword.arg: ast.literal_eval(keyword.value) for keyword in journey.keywords}
            for journey in journeys.value.elts
        ]
        self.assertEqual(("creation", "career"), tuple(value["phase"] for value in values))
        self.assertEqual(
            {"Remote Operated", "Gunnery Command Device"},
            {value["choice"] for value in values},
        )
        self.assertNotIn('"tablet"', source)

    def test_fixtures_pin_direct_hidden_and_descendant_weapon_identity_and_economics(self):
        for name, created in (
            ("creation-vehicle-weapon-firing-mode-e2e.chum5", "False"),
            ("career-vehicle-weapon-firing-mode-e2e.chum5", "True"),
        ):
            root = ET.parse(REPO / "tests/fixtures" / name).getroot()
            self.assertEqual(created, root.findtext("created"))
            vehicle = root.find("./vehicles/vehicle")
            self.assertIsNotNone(vehicle)
            direct = vehicle.findall("./weapons/weapon")
            self.assertEqual(2, len(direct))
            self.assertEqual("Ranged", direct[0].findtext("type"))
            self.assertTrue(direct[0].findtext("firingmode"))
            self.assertEqual("Melee", direct[1].findtext("type"))
            self.assertEqual("0", direct[1].findtext("ammo"))
            self.assertIsNotNone(direct[0].find("./underbarrel/weapon"))
            ids = [node.findtext("guid") for node in root.iter() if node.find("guid") is not None]
            self.assertEqual(len(ids), len(set(ids)))
            self.assertTrue(root.findtext("nuyen"))
            self.assertTrue(root.findtext("karma"))


if __name__ == "__main__":
    unittest.main()
