import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_overclocker_e2e.py"
CAREER = REPO / "tests" / "fixtures" / "career-gear-overclocker-e2e.chum5"
CREATION = REPO / "tests" / "fixtures" / "creation-gear-overclocker-negative-e2e.chum5"
CONTROL = "CharacterCareer.cboGearOverclocker"


class Api36GearOverclockerE2EDriverTests(unittest.TestCase):
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
        self.assertIn('"journey": "gear-overclocker"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"creationActionNotExposed": "pass"', source)
        self.assertIn('"careerEligibleNestedCyberdeckEdited": "pass"', source)
        self.assertIn('"zeroNuyenKarmaEconomics"', source)
        self.assertIn('"activeHomeAndUnrelatedXmlPreserved"', source)
        for digest in (
            "gearOverclockerPageSha256",
            "gearOverclockerContractSha256",
            "gearOverclockerRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "careerFixtureSha256",
            "creationNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_phase_eligibility_hierarchy_options_and_economics(self) -> None:
        career = ET.parse(CAREER).getroot()
        creation = ET.parse(CREATION).getroot()
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual("False", creation.findtext("created"))
        improvement = career.find("./improvements/improvement")
        self.assertIsNotNone(improvement)
        self.assertEqual("Overclocker", improvement.findtext("improvementttype"))
        self.assertEqual("1", improvement.findtext("enabled"))
        root = career.find("./gears/gear")
        self.assertIsNotNone(root)
        target = root.find("./children/gear")
        self.assertIsNotNone(target)
        self.assertEqual("Cyberdecks", target.findtext("category"))
        self.assertEqual("Attack", target.findtext("overclocked"))
        identities = [node.findtext("guid", default="") for node in career.findall(".//gear")]
        self.assertEqual(len(identities), len(set(identities)))
        self.assertEqual("4321", career.findtext("nuyen"))
        self.assertEqual("7", career.findtext("karma"))
        self.assertEqual("Attack", creation.findtext("./gears/gear/overclocked"))


if __name__ == "__main__":
    unittest.main()
