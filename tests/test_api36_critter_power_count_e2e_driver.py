import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_critter_power_count_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-critter-power-count-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-critter-power-count-e2e.chum5",
)


class Api36CritterPowerCountE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_digest_bound_and_proves_both_directions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "critter-power-count"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-section-tab-critter"', source)
        self.assertIn('"build-action-tab-critter-critterpowers"', source)
        self.assertIn('f"collection-item-critterpower-{expected[\'target_id\']}"', source)
        self.assertIn('f"critter-power-count-open-{compact_id}"', source)
        self.assertIn('f"critter-power-count-toggle-{compact_id}"', source)
        self.assertIn('f"critter-power-count-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"legacyDefaultTrue"', source)
        self.assertIn('"expectedRevisionAtomicSave"', source)
        self.assertIn('"excludedAndIncluded"', source)
        self.assertIn('"critterPowerCountRulesSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_create_and_career_unique_ids_defaults_and_unrelated_xml(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for index, fixture in enumerate(FIXTURES):
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            powers = root.findall("./critterpowers/critterpower")
            self.assertEqual(2, len(powers))
            identities = [uuid.UUID(power.findtext("guid", default="")) for power in powers]
            self.assertEqual(2, len(set(identities)))
            self.assertTrue(all(identity.int != 0 for identity in identities))
            self.assertTrue(all(identity not in all_ids for identity in identities))
            all_ids.update(identities)
            target_flag = powers[0].find("counttowardslimit")
            if index == 0:
                self.assertIsNone(target_flag, "creation fixture must prove the legacy true default")
            else:
                self.assertEqual("True", target_flag.text)
            self.assertEqual("False", powers[1].findtext("counttowardslimit"))
            self.assertTrue(all(power.findtext("notes", default="") for power in powers))
            self.assertTrue(root.findtext("./customstate/counttowardslimit", default="").endswith("unrelated count text"))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(4, len(all_ids))


if __name__ == "__main__":
    unittest.main()
