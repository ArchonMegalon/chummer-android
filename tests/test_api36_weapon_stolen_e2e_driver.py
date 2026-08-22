import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_weapon_stolen_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-weapon-stolen-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-weapon-stolen-negative-e2e.chum5"
CONTROL = "CharacterCreate.chkWeaponStolen"


class Api36WeaponStolenE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_arm64_restart_and_exact_control_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        controls_assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual((CONTROL,), tuple(ast.literal_eval(controls_assignment.value)))
        self.assertIn('if api != "36"', source)
        self.assertIn('"arm64-v8a" not in abi_list.split(",")', source)
        self.assertIn('"abi": "arm64-v8a"', source)
        self.assertIn('"package": shared.PACKAGE', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "weapon-stolen"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"careerActionNotExposed": "pass"', source)
        self.assertIn('"creationEligibleWeaponHierarchyEdited": "pass"', source)
        self.assertIn('"zeroNuyenKarmaEconomics"', source)
        for digest in (
            "weaponStolenPageSha256",
            "weaponStolenContractSha256",
            "weaponStolenRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "creationFixtureSha256",
            "careerNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_creation_eligibility_typed_identity_economics_and_career(self) -> None:
        creation = ET.parse(CREATION).getroot()
        career = ET.parse(CAREER).getroot()
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        improvement = creation.find("./improvements/improvement")
        self.assertIsNotNone(improvement)
        self.assertEqual("Nuyen", improvement.findtext("improvementttype"))
        self.assertEqual("Stolen", improvement.findtext("improvedname"))
        self.assertEqual("create", improvement.findtext("condition"))
        self.assertEqual("0", improvement.findtext("addtorating"))
        self.assertEqual("1", improvement.findtext("enabled"))
        root = creation.find("./weapons/weapon")
        self.assertIsNotNone(root)
        accessory = root.find("./accessories/accessory")
        target = accessory.find("./gears/gear/children/gear")
        underbarrel = root.find("./underbarrel/weapon")
        self.assertIsNotNone(target)
        self.assertIsNotNone(underbarrel)
        identities = [node.findtext("guid", default="") for node in creation.findall(".//weapon")]
        identities += [node.findtext("guid", default="") for node in creation.findall(".//accessory")]
        identities += [node.findtext("guid", default="") for node in creation.findall(".//gear")]
        self.assertEqual(len(identities), len(set(identities)))
        self.assertEqual("False", target.findtext("stolen"))
        self.assertEqual("4321", creation.findtext("nuyen"))
        self.assertEqual("7", creation.findtext("karma"))
        self.assertEqual("False", career.findtext("./weapons/weapon/stolen"))


if __name__ == "__main__":
    unittest.main()
