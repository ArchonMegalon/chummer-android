import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_armor_active_commlink_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-armor-active-commlink-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-armor-active-commlink-e2e.chum5",
)


class Api36ArmorActiveCommlinkE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_both_boolean_directions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "armor-active-commlink"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-action-tab-gear-armors"', source)
        self.assertIn('f"armor-active-commlink-open-{compact_id}"', source)
        self.assertIn('f"armor-active-commlink-toggle-{compact_id}"', source)
        self.assertIn('f"armor-active-commlink-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"enabledAsExclusiveActiveCommlink"', source)
        self.assertIn('"disabledFromActiveCommlink"', source)
        self.assertIn('"legacyPersonaEligibility"', source)
        self.assertIn('"processRestartWorkspacePersisted"', source)
        self.assertIn('"processRestartUiReadback"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"characterSectionModelsSha256"', source)
        self.assertIn('"collectionEditorProjectorSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_both_modes_unique_ids_persona_eligibility_and_one_prior_active(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            gears = root.findall("./gears/gear")
            armors = root.findall("./armors/armor")
            self.assertEqual(1, len(gears))
            self.assertEqual(2, len(armors))
            self.assertEqual("True", gears[0].findtext("active"))
            self.assertEqual("Self", gears[0].findtext("canformpersona"))
            self.assertTrue(all(armor.findtext("active") == "False" for armor in armors))
            self.assertEqual("Self", armors[0].findtext("canformpersona"))
            self.assertEqual("", armors[1].findtext("canformpersona", default=""))
            self.assertTrue(root.findtext("./customstate/active", default="").endswith("unrelated active text"))

            local_ids = [
                uuid.UUID(element.findtext("guid", default=""))
                for element in [*gears, *armors]
            ]
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)
            self.assertTrue(all(element.findtext("notes", default="") for element in [*gears, *armors]))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(6, len(all_ids))


if __name__ == "__main__":
    unittest.main()
