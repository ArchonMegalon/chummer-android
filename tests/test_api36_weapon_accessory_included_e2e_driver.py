import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_weapon_accessory_included_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-weapon-accessory-included-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-weapon-accessory-included-e2e.chum5",
)


class Api36WeaponAccessoryIncludedE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_both_boolean_directions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "weapon-accessory-included"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-action-tab-gear-weaponaccessories"', source)
        self.assertIn('f"collection-item-weapon-{expected[\'accessory_id\']}"', source)
        self.assertIn('f"weapon-accessory-included-open-{compact_id}"', source)
        self.assertIn('f"weapon-accessory-included-toggle-{compact_id}"', source)
        self.assertIn('f"weapon-accessory-included-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"enabledForSelectedAccessory"', source)
        self.assertIn('"disabledForSelectedAccessory"', source)
        self.assertIn('"stableParentAndAccessoryIdentity"', source)
        self.assertIn('"unrelatedXmlPreserved"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"characterSectionModelsSha256"', source)
        self.assertIn('"collectionEditorProjectorSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_both_modes_unique_parent_and_accessory_ids_and_saved_flags(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            weapons = root.findall("./weapons/weapon")
            self.assertEqual(1, len(weapons))
            accessories = weapons[0].findall("./accessories/accessory")
            self.assertEqual(2, len(accessories))
            self.assertEqual(["False", "True"], [item.findtext("included") for item in accessories])
            self.assertTrue(weapons[0].findtext("notes", default=""))
            self.assertTrue(all(item.findtext("notes", default="") for item in accessories))
            self.assertTrue(root.findtext("./customstate/included", default="").endswith("unrelated included text"))

            local_ids = [uuid.UUID(weapons[0].findtext("guid", default=""))]
            local_ids.extend(uuid.UUID(item.findtext("guid", default="")) for item in accessories)
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(6, len(all_ids))


if __name__ == "__main__":
    unittest.main()
