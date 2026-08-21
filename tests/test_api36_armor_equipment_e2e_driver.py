import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_armor_equipment_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-armor-equipment-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-armor-equipment-e2e.chum5",
)


class Api36ArmorEquipmentE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_six_control_restart_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "armor-equipment"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"chkArmorEquipped"', source)
        self.assertIn('"cmdArmorEquipAll"', source)
        self.assertIn('"cmdArmorUnEquipAll"', source)
        self.assertIn('f"armor-equipment-toggle-{token}"', source)
        self.assertIn('f"armor-equipment-equip-all-{token}"', source)
        self.assertIn('f"armor-equipment-unequip-all-{token}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"nestedEquipmentFlagsPreserved"', source)
        self.assertIn('"exactEligibility"', source)
        self.assertIn('"armorEquipmentRulesSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_create_and_career_with_exact_nested_preservation(self) -> None:
        created = []
        identities: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created.append(root.findtext("created"))
            armors = root.findall("./armors/armor")
            self.assertEqual(["False", "True"], [armor.findtext("equipped") for armor in armors])
            local = [uuid.UUID(armor.findtext("guid", default="")) for armor in armors]
            self.assertEqual(2, len(set(local)))
            self.assertTrue(all(identity not in identities for identity in local))
            identities.update(local)
            self.assertEqual("True", armors[0].findtext("./armormods/armormod/equipped"))
            self.assertEqual("False", armors[0].findtext("./gears/gear/equipped"))
            self.assertTrue(root.findtext("./customstate/equipped"))
        self.assertEqual(["False", "True"], created)


if __name__ == "__main__":
    unittest.main()
