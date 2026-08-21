import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_tradition_drain_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-tradition-drain-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-tradition-drain-e2e.chum5",
)
CUSTOM_SOURCE_ID = "616ba093-306c-45fc-8f41-0b98c8cccb46"


class Api36TraditionDrainDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_catalog_and_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            '"CharacterCreate.cboDrain"',
            '"CharacterCareer.cboDrain"',
            '"profile": "phone"',
            '"journey": "tradition-drain"',
            'api != "36"',
            '"build-tradition-drain"',
            '"tradition-drain-value"',
            '"tradition-drain-save"',
            '"traditionDrainRulesSha256"',
            '"sourceResolverSha256"',
            '"traditionsCatalogSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
            '"adeptOnlyAndUnknownCatalogValuesRejectedBySourceContract": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 1)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_both_modes_and_exact_custom_drain_identity(self) -> None:
        roots = [ET.parse(path).getroot() for path in FIXTURES]
        self.assertEqual(["False", "True"], [root.findtext("created") for root in roots])
        self.assertEqual(["False", "False"], [root.findtext("adept") for root in roots])
        self.assertEqual(["True", "True"], [root.findtext("magician") for root in roots])
        for root in roots:
            traditions = root.findall("tradition")
            self.assertEqual(1, len(traditions))
            self.assertEqual(CUSTOM_SOURCE_ID, traditions[0].findtext("sourceid"))
            self.assertEqual("MAG", traditions[0].findtext("traditiontype"))
            self.assertTrue(traditions[0].findtext("drain", default=""))
            self.assertTrue(traditions[0].findtext("extra", default=""))


if __name__ == "__main__":
    unittest.main()
