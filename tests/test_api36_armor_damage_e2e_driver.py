import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_armor_damage_e2e.py"
FIXTURE = REPO / "tests" / "fixtures" / "career-armor-damage-e2e.chum5"


class Api36ArmorDamageE2EDriverTests(unittest.TestCase):
    def test_driver_is_career_phone_only_api36_restart_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "armor-damage"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"cmdArmorIncrease"', source)
        self.assertIn('"cmdArmorDecrease"', source)
        self.assertIn('"build-action-tab-gear-armors"', source)
        self.assertIn('f"armor-damage-repair-{compact_id}"', source)
        self.assertIn('f"armor-damage-degrade-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"exactLegacyDirection"', source)
        self.assertIn('"exactBoundaryEnablement"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"armorDamageRulesSha256"', source)
        self.assertIn('"mutationCatalogSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixture_is_career_only_with_unique_stable_ids_and_boundary_values(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        armors = root.findall("./armors/armor")
        self.assertEqual(2, len(armors))
        identities = [uuid.UUID(armor.findtext("guid", default="")) for armor in armors]
        self.assertEqual(len(identities), len(set(identities)))
        self.assertTrue(all(identity.int != 0 for identity in identities))
        self.assertEqual("2", armors[0].findtext("armor"))
        self.assertEqual("0", armors[0].findtext("damage"))
        self.assertEqual("3", armors[1].findtext("damage"))
        self.assertTrue(all(armor.findtext("notes", default="") for armor in armors))
        self.assertEqual("Career unrelated damage text", root.findtext("./customstate/damage"))


if __name__ == "__main__":
    unittest.main()
