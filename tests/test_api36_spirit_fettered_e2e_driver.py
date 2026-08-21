import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_spirit_fettered_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-spirit-fettered-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-spirit-fettered-e2e.chum5",
)


class Api36SpiritFetteredE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_digest_bound_and_shared_row_exact(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('CONTROL = "SpiritControl.chkFettered"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "spirit-fettered"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-section-tab-magician"', source)
        self.assertIn('"build-action-tab-magician-spirits"', source)
        self.assertIn('f"collection-item-spirit-{expected[\'target_id\']}"', source)
        self.assertIn('f"spirit-fettered-open-{compact_id}"', source)
        self.assertIn('f"spirit-fettered-toggle-{compact_id}"', source)
        self.assertIn('f"spirit-fettered-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"expectedRevisionAtomicSave"', source)
        self.assertIn('"careerKarmaCostAndUndo"', source)
        self.assertIn('"spiritFetteringRulesSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn('"buildPageSha256"', source)
        self.assertIn('"buildFlowPagesSha256"', source)
        self.assertIn('"sr5ShellCatalogSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"controlCount": 1', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_create_and_career_unique_ids_cost_and_unrelated_xml(self) -> None:
        all_ids: set[uuid.UUID] = set()
        created_values: list[str | None] = []
        for index, fixture in enumerate(FIXTURES):
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            spirits = root.findall("./spirits/spirit")
            self.assertEqual(2, len(spirits))
            identities = [uuid.UUID(spirit.findtext("guid", default="")) for spirit in spirits]
            self.assertEqual(2, len(set(identities)))
            self.assertTrue(all(identity.int != 0 for identity in identities))
            self.assertTrue(all(identity not in all_ids for identity in identities))
            all_ids.update(identities)
            self.assertTrue(all(spirit.findtext("type") == "Spirit" for spirit in spirits))
            self.assertTrue(all(spirit.findtext("fettered") == "False" for spirit in spirits))
            self.assertTrue(all(spirit.findtext("notes", default="") for spirit in spirits))
            self.assertTrue(root.findtext("./customstate/fettered", default="").endswith("unrelated fettered text"))
            self.assertIsNotNone(root.find("improvements"))
            if index == 0:
                self.assertIsNone(root.find("karmaspiritfettering"))
                self.assertEqual("0", root.findtext("karma"))
            else:
                self.assertEqual("3", root.findtext("karmaspiritfettering"))
                self.assertEqual("20", root.findtext("karma"))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(4, len(all_ids))


if __name__ == "__main__":
    unittest.main()
