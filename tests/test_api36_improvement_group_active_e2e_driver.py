import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_improvement_group_active_e2e.py"
CAREER = REPO / "tests" / "fixtures" / "career-improvement-group-active-e2e.chum5"
CREATION = REPO / "tests" / "fixtures" / "creation-improvement-group-active-negative-e2e.chum5"
CONTROLS = (
    "CharacterCareer.cmdImprovementsEnableAll",
    "CharacterCareer.cmdImprovementsDisableAll",
)


class Api36ImprovementGroupActiveE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_restart_bound_and_exactly_two_career_controls(self) -> None:
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
        self.assertIn('"journey": "improvement-group-active"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"careerEnableAllExactGroup": "pass"', source)
        self.assertIn('"careerDisableAllExactGroup": "pass"', source)
        self.assertIn('"creationActionNotExposed": "pass"', source)
        for digest in (
            "improvementGroupActivePageSha256",
            "improvementGroupActiveContractSha256",
            "improvementGroupActiveRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "careerFixtureSha256",
            "creationNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_exact_custom_groups_and_creation_negative(self) -> None:
        career = ET.parse(CAREER).getroot()
        creation = ET.parse(CREATION).getroot()
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual(["Alpha", "Beta"], [node.text for node in career.findall("./improvementgroups/improvementgroup")])
        improvements = career.findall("./improvements/improvement")
        alpha_custom = [item for item in improvements if item.findtext("customgroup") == "Alpha" and item.findtext("custom") == "True"]
        self.assertEqual(["1", "0"], [item.findtext("enabled") for item in alpha_custom])
        alpha_noncustom = next(item for item in improvements if item.findtext("customgroup") == "Alpha" and item.findtext("custom") == "False")
        self.assertEqual("True", alpha_noncustom.findtext("enabled"))
        self.assertEqual("1", next(item for item in improvements if item.findtext("customgroup") == "Beta").findtext("enabled"))
        self.assertEqual("0", next(item for item in improvements if item.findtext("customgroup") == "").findtext("enabled"))
        self.assertEqual("Creation negative group sentinel", creation.findtext("./improvements/improvement/notes"))


if __name__ == "__main__":
    unittest.main()
