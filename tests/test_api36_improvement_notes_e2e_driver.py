import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_improvement_notes_e2e.py"
CAREER = REPO / "tests" / "fixtures" / "career-improvement-notes-e2e.chum5"
CREATION = REPO / "tests" / "fixtures" / "creation-improvement-notes-negative-e2e.chum5"
CONTROLS = ("CharacterCareer.tsImprovementNotes",)


class Api36ImprovementNotesE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_arm64_package_restart_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        controls_assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual(CONTROLS, tuple(ast.literal_eval(controls_assignment.value)))
        self.assertIn('if api != "36"', source)
        self.assertIn('ABI = "arm64-v8a"', source)
        self.assertIn('PACKAGE = "com.myexternalbrain.chummer"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "improvement-notes"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"creationActionNotExposed": "pass"', source)
        self.assertIn('"careerDirectImprovementNotesEdited": "pass"', source)
        for digest in (
            "improvementNotesPageSha256",
            "improvementNotesContractSha256",
            "improvementNotesRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "careerFixtureSha256",
            "creationNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_career_notes_color_and_creation_negative(self) -> None:
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
        self.assertEqual("Career selected improvement note", target.findtext("notes"))
        self.assertEqual("#112233", target.findtext("notesColor"))
        self.assertEqual(
            "Creation negative note sentinel",
            creation.findtext("./improvements/improvement/notes"),
        )
        self.assertEqual(
            "#ABCDEF",
            creation.findtext("./improvements/improvement/notesColor"),
        )


if __name__ == "__main__":
    unittest.main()
