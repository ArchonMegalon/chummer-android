import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_name_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-gear-name-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-gear-name-e2e.chum5",
)


class Api36GearNameDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_stable_identity_and_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            '"CharacterCreate.tsGearName"',
            '"CharacterCareer.tsGearName"',
            '"profile": "phone"',
            '"journey": "gear-name"',
            'api != "36"',
            '"collectionEditorPagesSha256"',
            '"collectionRequestSha256"',
            '"collectionProjectorSha256"',
            '"mutationCatalogSha256"',
            '"presenterPersistenceSha256"',
            '"sectionModelsSha256"',
            '"sectionServiceSha256"',
            '"workspaceStoreSha256"',
            '"careerNestedGearNameEdited": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 1)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_creation_top_level_and_career_nested_gear(self) -> None:
        creation, career = [ET.parse(path).getroot() for path in FIXTURES]
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual(1, len(creation.findall("./gears/gear")))
        self.assertIsNotNone(career.find("./gears/gear/children/gear"))
        for root in (creation, career):
            for gear in root.findall(".//gear"):
                self.assertTrue(gear.findtext("guid", default=""))
                self.assertIsNotNone(gear.find("gearname"))


if __name__ == "__main__":
    unittest.main()
