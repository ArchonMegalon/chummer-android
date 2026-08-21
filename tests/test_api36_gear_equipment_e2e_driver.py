import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_equipment_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-gear-equipment-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-gear-equipment-e2e.chum5"
CONTROLS = (
    "CharacterCreate.chkGearEquipped",
    "CharacterCareer.chkGearEquipped",
)


class Api36GearEquipmentE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_arm64_package_restart_and_exact_receipt_bound(self) -> None:
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
            '"journey": "gear-equipment"',
            'device.shell("am", "force-stop"',
            '"creationRecursiveGearEdited": "pass"',
            '"creationIncludedNodeReadOnly": "pass"',
            '"careerRecursiveGearEdited": "pass"',
            '"zeroEconomicDeltaBothPhases": "pass"',
            '"sameSessionReopenBothPhases": "pass"',
            '"processRestartBothPhases": "pass"',
        ):
            self.assertIn(marker, source)
        for digest in (
            "apkSha256",
            "driverSha256",
            "gearEquipmentPageSha256",
            "gearEquipmentContractSha256",
            "gearEquipmentRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "creationFixtureSha256",
            "careerFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_both_phases_recursive_identity_eligibility_and_economics(self) -> None:
        creation = ET.parse(CREATION).getroot()
        career = ET.parse(CAREER).getroot()
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual(("4321", "7"), (creation.findtext("nuyen"), creation.findtext("karma")))
        self.assertEqual(("8765", "19"), (career.findtext("nuyen"), career.findtext("karma")))
        self.assertIsNotNone(creation.find("./gears/gear/children/gear"))
        self.assertIsNotNone(career.find("./gears/gear/children/gear"))
        included = creation.findall("./gears/gear/children/gear")[1]
        self.assertEqual("included-source", included.findtext("parentid"))
        for root in (creation, career):
            identities = [node.findtext("guid", default="") for node in root.findall(".//gear")]
            self.assertEqual(len(identities), len(set(identities)))
            self.assertTrue(all(node.findtext("equipped") in {"True", "False"}
                                for node in root.findall(".//gear")))


if __name__ == "__main__":
    unittest.main()
