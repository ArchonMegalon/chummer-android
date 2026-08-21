import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_active_commlink_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-gear-active-commlink-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-gear-active-commlink-e2e.chum5",
)


class Api36GearActiveCommlinkE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_and_digest_binds_the_full_authority_graph(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "gear-active-commlink"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"CharacterCreate.chkGearActiveCommlink"', source)
        self.assertIn('"CharacterCareer.chkGearActiveCommlink"', source)
        self.assertIn('f"gear-active-commlink-open-{compact_id}"', source)
        self.assertIn('f"gear-active-commlink-toggle-{compact_id}"', source)
        self.assertIn('f"gear-active-commlink-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"expectedRevisionAtomicSave"', source)
        self.assertIn('"exclusiveCharacterWideActiveCommlink"', source)
        self.assertIn('"gearActiveCommlinkRulesSha256"', source)
        self.assertIn('"gearActiveCommlinkContractSha256"', source)
        self.assertIn('"collectionEditorProjectorSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_pair_creation_and_career_with_unique_stable_ids_and_cross_kind_prior_active(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            gears = root.findall("./gears/gear")
            armors = root.findall("./armors/armor")
            self.assertEqual(2, len(gears))
            self.assertEqual(1, len(armors))
            self.assertEqual("False", gears[0].findtext("active"))
            self.assertEqual("Self", gears[0].findtext("canformpersona"))
            self.assertEqual("False", gears[1].findtext("active"))
            self.assertEqual("True", armors[0].findtext("active"))
            self.assertTrue(root.findtext("./customstate/active", default="").endswith("unrelated active text"))

            local_ids = [
                uuid.UUID(item.findtext("guid", default=""))
                for item in [*gears, *armors]
            ]
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)
            self.assertTrue(all(item.findtext("notes", default="") for item in [*gears, *armors]))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(6, len(all_ids))


if __name__ == "__main__":
    unittest.main()
