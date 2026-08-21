import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_lifestyle_name_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-lifestyle-name-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-lifestyle-name-e2e.chum5",
)


class Api36LifestyleNameDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_stable_identity_and_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            '"CharacterCreate.tsLifestyleName"',
            '"CharacterCareer.tsLifestyleName"',
            '"profile": "phone"',
            '"journey": "lifestyle-name"',
            'api != "36"',
            '"build-action-tab-lifestyle-lifestyles"',
            '"collectionEditorPagesSha256"',
            '"collectionRequestSha256"',
            '"collectionProjectorSha256"',
            '"mutationCatalogSha256"',
            '"presenterPersistenceSha256"',
            '"sectionModelsSha256"',
            '"sectionServiceSha256"',
            '"workspaceStoreSha256"',
            '"notesAndNotesColorPreserved": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 1)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_both_modes_and_exact_preservation_fields(self) -> None:
        creation, career = [ET.parse(path).getroot() for path in FIXTURES]
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        for root in (creation, career):
            lifestyles = root.findall("./lifestyles/lifestyle")
            self.assertEqual(1, len(lifestyles))
            lifestyle = lifestyles[0]
            self.assertTrue(lifestyle.findtext("guid", default=""))
            self.assertIsNotNone(lifestyle.find("extra"))
            self.assertIsNotNone(lifestyle.find("notes"))
            self.assertIsNotNone(lifestyle.find("notesColor"))


if __name__ == "__main__":
    unittest.main()
