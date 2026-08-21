import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_stolen_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-gear-stolen-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-gear-stolen-negative-e2e.chum5"
CONTROL = "CharacterCreate.chkGearStolen"


class Api36GearStolenE2EDriverTests(unittest.TestCase):
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
        self.assertIn('"journey": "gear-stolen"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"careerActionNotExposed": "pass"', source)
        self.assertIn('"creationEligibleRecursiveGearEdited": "pass"', source)
        for digest in (
            "gearStolenPageSha256",
            "gearStolenContractSha256",
            "gearStolenRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "creationFixtureSha256",
            "careerNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_creation_eligibility_recursive_identity_and_career_negative(self) -> None:
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
        root = creation.find("./gears/gear")
        self.assertIsNotNone(root)
        target = root.find("./children/gear/children/gear")
        self.assertIsNotNone(target)
        identities = [node.findtext("guid", default="") for node in creation.findall(".//gear")]
        self.assertEqual(len(identities), len(set(identities)))
        self.assertEqual("False", target.findtext("stolen"))
        self.assertEqual("False", career.findtext("./gears/gear/stolen"))


if __name__ == "__main__":
    unittest.main()
