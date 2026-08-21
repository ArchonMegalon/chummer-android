import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_weapon_active_commlink_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-weapon-active-commlink-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-weapon-active-commlink-e2e.chum5",
)


class Api36WeaponActiveCommlinkE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_both_directions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "weapon-active-commlink"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-action-tab-gear-weapons"', source)
        self.assertIn('f"weapon-active-commlink-open-{compact_id}"', source)
        self.assertIn('f"weapon-active-commlink-toggle-{compact_id}"', source)
        self.assertIn('f"weapon-active-commlink-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        for key in (
            '"matrixOwnerReadback"',
            '"enabledAsExclusiveActiveCommlink"',
            '"disabledFromActiveCommlink"',
            '"processRestartWorkspacePersisted"',
            '"processRestartUiReadback"',
            '"weaponActiveCommlinkRulesSha256"',
            '"weaponParentResolverSha256"',
            '"creationFixtureSha256"',
            '"careerFixtureSha256"',
        ):
            self.assertIn(key, source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_both_modes_unique_ids_and_one_prior_active_commlink(self) -> None:
        created_values = []
        dep_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            dep_values.append(root.findtext("depenabled"))
            gears = root.findall("./gears/gear")
            weapons = root.findall("./weapons/weapon")
            self.assertEqual(1, len(gears))
            self.assertEqual(2, len(weapons))
            owner = gears[0]
            target = weapons[0]
            self.assertEqual("True", owner.findtext("active"))
            self.assertTrue(all(weapon.findtext("active") == "False" for weapon in weapons))
            self.assertEqual(owner.findtext("guid"), target.findtext("parentid"))
            self.assertEqual("Self", owner.findtext("canformpersona"))
            local_ids = [
                uuid.UUID(element.findtext("guid", default=""))
                for element in [*gears, *weapons]
            ]
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)
            self.assertTrue(all(element.findtext("notes", default="") for element in [*gears, *weapons]))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(["False", "True"], dep_values)
        self.assertEqual(6, len(all_ids))


if __name__ == "__main__":
    unittest.main()
