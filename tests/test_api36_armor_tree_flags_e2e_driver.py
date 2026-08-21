import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_armor_tree_flags_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-armor-tree-flags-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-armor-tree-flags-negative-e2e.chum5"
CONTROLS = (
    "CharacterCreate.chkArmorStolen",
    "CharacterCreate.chkArmorBlackMarketDiscount",
)


class Api36ArmorTreeFlagsE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_restart_bound_and_exactly_two_create_controls(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        controls_assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual(CONTROLS, tuple(ast.literal_eval(controls_assignment.value)))
        self.assertIn('if api != "36"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "armor-tree-flags"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"careerActionNotExposed": "pass"', source)
        self.assertIn('"creationAllThreeNodeKindsAndBothGearParents": "pass"', source)
        for digest in (
            "armorTreeFlagPageSha256",
            "armorTreeFlagContractSha256",
            "armorTreeFlagRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "creationFixtureSha256",
            "careerNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_all_three_node_kinds_recursive_gear_under_both_and_career_negative(self) -> None:
        creation = ET.parse(CREATION).getroot()
        career = ET.parse(CAREER).getroot()
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        armor = creation.find("./armors/armor")
        self.assertIsNotNone(armor)
        self.assertIsNotNone(armor.find("./armormods/armormod"))
        self.assertIsNotNone(armor.find("./gears/gear/children/gear"))
        self.assertIsNotNone(armor.find("./armormods/armormod/gears/gear/children/gear"))
        identities = [
            node.findtext("guid", default="")
            for node in (
                [armor]
                + armor.findall("./armormods/armormod")
                + armor.findall(".//gear")
            )
        ]
        self.assertEqual(len(identities), len(set(identities)))
        for node in [armor] + armor.findall("./armormods/armormod") + armor.findall(".//gear"):
            self.assertIn(node.findtext("stolen"), {"True", "False"})
            self.assertIn(node.findtext("discountedcost"), {"True", "False"})


if __name__ == "__main__":
    unittest.main()
