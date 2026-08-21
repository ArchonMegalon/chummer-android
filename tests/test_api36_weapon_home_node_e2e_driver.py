import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_weapon_home_node_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-weapon-home-node-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-weapon-home-node-e2e.chum5",
)


class Api36WeaponHomeNodeE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_ai_rule_and_both_directions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "weapon-home-node"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-action-tab-gear-weapons"', source)
        self.assertIn('f"weapon-home-node-open-{compact_id}"', source)
        self.assertIn('f"weapon-home-node-toggle-{compact_id}"', source)
        self.assertIn('f"weapon-home-node-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"aiEligibilityReadback"', source)
        self.assertIn('"enabledAsExclusiveHomeNode"', source)
        self.assertIn('"disabledFromHomeNode"', source)
        self.assertIn('"processRestartWorkspacePersisted"', source)
        self.assertIn('"processRestartUiReadback"', source)
        self.assertIn('"weaponHomeNodeRulesSha256"', source)
        self.assertIn('"weaponParentResolverSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_both_modes_exact_ai_rule_unique_ids_and_one_prior_home_node(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            self.assertEqual("True", root.findtext("depenabled"))
            attributes = {
                attribute.findtext("name"): attribute
                for attribute in root.findall("./attributes/attribute")
            }
            self.assertEqual("0", attributes["BOD"].findtext("metatypemax"))
            self.assertEqual("4", attributes["DEP"].findtext("totalvalue"))

            gears = root.findall("./gears/gear")
            weapons = root.findall("./weapons/weapon")
            self.assertEqual(1, len(gears))
            self.assertEqual(2, len(weapons))
            owner = gears[0]
            target = weapons[0]
            self.assertEqual("True", owner.findtext("homenode"))
            self.assertTrue(all(weapon.findtext("homenode") == "False" for weapon in weapons))
            self.assertEqual(owner.findtext("guid"), target.findtext("parentid"))
            self.assertEqual("Self", owner.findtext("canformpersona"))
            self.assertEqual("3", owner.findtext("devicerating"))
            self.assertEqual("2", owner.findtext("programlimit"))

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
        self.assertEqual(6, len(all_ids))


if __name__ == "__main__":
    unittest.main()
