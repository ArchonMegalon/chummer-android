import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_tradition_name_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-tradition-name-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-tradition-name-e2e.chum5",
)
CUSTOM_SOURCE_ID = "616ba093-306c-45fc-8f41-0b98c8cccb46"


class Api36TraditionNameDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_digest_bound_and_restart_safe(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            '"CharacterCreate.txtTraditionName"',
            '"CharacterCareer.txtTraditionName"',
            '"profile": "phone"',
            '"journey": "tradition-name"',
            'api != "36"',
            '"build-tradition-name"',
            '"tradition-name-value"',
            '"tradition-name-save"',
            '"traditionNameRulesSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
            '"nonCustomTraditionRejectedBySourceContract": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 1)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_both_modes_and_exact_custom_identity(self) -> None:
        created = []
        guids = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created.append(root.findtext("created"))
            tradition = root.find("tradition")
            self.assertIsNotNone(tradition)
            self.assertEqual(CUSTOM_SOURCE_ID, tradition.findtext("sourceid"))
            self.assertEqual("MAG", tradition.findtext("traditiontype"))
            self.assertTrue(tradition.findtext("name", default="").endswith("Old Tradition"))
            self.assertTrue(tradition.findtext("./extra/name", default="").endswith("unrelated nested name"))
            guids.add(tradition.findtext("guid"))
        self.assertEqual(["False", "True"], created)
        self.assertEqual(2, len(guids))


if __name__ == "__main__":
    unittest.main()
