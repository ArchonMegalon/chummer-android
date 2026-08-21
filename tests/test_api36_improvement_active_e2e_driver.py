import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_improvement_active_e2e.py"
CAREER = REPO / "tests" / "fixtures" / "career-improvement-active-e2e.chum5"
CREATION = REPO / "tests" / "fixtures" / "creation-improvement-active-negative-e2e.chum5"
CONTROLS = ("CharacterCareer.chkImprovementActive",)


class Api36ImprovementActiveE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_restart_bound_and_exactly_one_career_control(self) -> None:
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
        self.assertIn('"journey": "improvement-active"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"creationActionNotExposed": "pass"', source)
        self.assertIn('"careerDirectImprovementEdited": "pass"', source)
        for digest in (
            "improvementActivePageSha256",
            "improvementActiveContractSha256",
            "improvementActiveRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "careerFixtureSha256",
            "creationNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_career_direct_identity_and_creation_negative(self) -> None:
        career = ET.parse(CAREER).getroot()
        creation = ET.parse(CREATION).getroot()
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual("False", creation.findtext("created"))
        improvements = career.findall("./improvements/improvement")
        self.assertEqual(3, len(improvements))
        identities = [
            (
                item.findtext("sourcename"),
                item.findtext("improvementttype"),
                item.findtext("improvementsource"),
                item.findtext("improvedname"),
            )
            for item in improvements
        ]
        self.assertEqual(len(identities), len(set(identities)))
        target = next(item for item in improvements if item.findtext("improvedname") == "AGI")
        self.assertEqual("0", target.findtext("enabled"))
        self.assertEqual("Creation negative sentinel", creation.findtext("./improvements/improvement/notes"))


if __name__ == "__main__":
    unittest.main()
