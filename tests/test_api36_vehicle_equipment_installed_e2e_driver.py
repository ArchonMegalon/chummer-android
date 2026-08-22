import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_vehicle_equipment_installed_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-vehicle-equipment-installed-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-vehicle-equipment-installed-e2e.chum5"
CONTROLS = (
    "CharacterCreate.chkVehicleWeaponAccessoryInstalled",
    "CharacterCareer.chkVehicleWeaponAccessoryInstalled",
)


class Api36VehicleEquipmentInstalledE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_arm64_package_restart_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual(CONTROLS, tuple(ast.literal_eval(assignment.value)))
        for marker in (
            'if api != "36"',
            '"arm64-v8a" not in abi_list.split(",")',
            '"abi": "arm64-v8a"',
            '"package": shared.PACKAGE',
            '"profile": "phone"',
            '"journey": "vehicle-equipment-installed"',
            'device.shell("am", "force-stop"',
            '"creationWeaponAccessoryEdited": "pass"',
            '"careerVehicleModEdited": "pass"',
            '"sensorVehicleModFailClosedBothPhases": "pass"',
            '"parentInstalledWeaponReadOnlyBothPhases": "pass"',
            '"zeroEconomicDeltaBothPhases": "pass"',
            '"sameSessionReopenBothPhases": "pass"',
            '"processRestartBothPhases": "pass"',
        ):
            self.assertIn(marker, source)
        for digest in (
            "apkSha256",
            "driverSha256",
            "vehicleEquipmentInstalledPageSha256",
            "vehicleEquipmentInstalledContractSha256",
            "vehicleEquipmentInstalledRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "creationFixtureSha256",
            "careerFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_both_phases_union_paths_enable_rules_and_economics(self) -> None:
        creation = ET.parse(CREATION).getroot()
        career = ET.parse(CAREER).getroot()
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual(("4321", "7"), (creation.findtext("nuyen"), creation.findtext("karma")))
        self.assertEqual(("8765", "19"), (career.findtext("nuyen"), career.findtext("karma")))
        for root in (creation, career):
            vehicle = root.find("./vehicles/vehicle")
            self.assertIsNotNone(vehicle)
            self.assertIsNotNone(vehicle.find("./weaponmounts/weaponmount"))
            self.assertIsNotNone(vehicle.find("./weaponmounts/weaponmount/mods/mod"))
            self.assertIsNotNone(vehicle.find("./weaponmounts/weaponmount/weapons/weapon"))
            self.assertIsNotNone(vehicle.find("./weaponmounts/weaponmount/weapons/weapon/accessories/accessory"))
            sensor_mod = vehicle.find("./mods/mod")
            self.assertEqual("2", sensor_mod.findtext("./bonus/sensor"))
            underbarrel = vehicle.find("./weapons/weapon/underbarrel/weapon")
            self.assertEqual(
                vehicle.findtext("./weapons/weapon/guid"),
                underbarrel.findtext("parentid"),
            )
            identities = [
                node.findtext("guid", default="")
                for node in root.iter()
                if node.find("guid") is not None
            ]
            self.assertEqual(len(identities), len(set(identities)))
            equipment_tags = {"weaponmount", "mod", "weapon", "accessory"}
            self.assertTrue(all(
                node.findtext("equipped") in {"True", "False"}
                for node in root.iter()
                if node.tag in equipment_tags
            ))


if __name__ == "__main__":
    unittest.main()
