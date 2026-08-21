import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_quality_level_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-quality-level-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-quality-level-e2e.chum5"


class Api36QualityLevelE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_create_and_career_recovery(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "quality-level"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"CharacterCreate", "CharacterCareer"', source)
        self.assertIn('"nudQualityLevel"', source)
        self.assertIn('device.wait("Confirm Quality Level increase"', source)
        self.assertIn('"careerDecrease": "pass"', source)
        self.assertIn('== "RemoveQuality"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"qualityLevelContractSha256"', source)
        self.assertIn('"sourceResolverContractSha256"', source)
        self.assertIn('"fileSourceResolverSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_unique_stable_free_illness_levels_in_both_modes(self) -> None:
        for fixture, created in ((CREATION, "False"), (CAREER, "True")):
            root = ET.parse(fixture).getroot()
            self.assertEqual(created, root.findtext("created"))
            self.assertEqual("67e25032-2a4e-42ca-97fa-69f7f608236c", root.findtext("settings"))
            qualities = root.findall("./qualities/quality")
            self.assertEqual(1, len(qualities))
            quality = qualities[0]
            self.assertEqual("d537536d-893d-4bd6-89c6-03b7dd5bd24c", quality.findtext("sourceid"))
            self.assertEqual("Illness", quality.findtext("name"))
            self.assertEqual("0", quality.findtext("bp"))
            self.assertEqual("Negative", quality.findtext("qualitytype"))
            self.assertEqual("Selected", quality.findtext("qualitysource"))
            self.assertNotEqual(uuid.UUID(int=0), uuid.UUID(quality.findtext("guid", default="")))
            bonus = quality.find("bonus")
            self.assertIsNotNone(bonus)
            self.assertEqual([], list(bonus))
            self.assertEqual([], root.findall("./expenses/expense"))


if __name__ == "__main__":
    unittest.main()
