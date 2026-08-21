import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_quantity_e2e.py"
FIXTURE = REPO / "tests" / "fixtures" / "career-gear-quantity-e2e.chum5"


class Api36GearQuantityE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_all_four_actions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "gear-quantity-lifecycle"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"cmdGearIncreaseQty"', source)
        self.assertIn('"cmdGearReduceQty"', source)
        self.assertIn('"cmdGearSplitQty"', source)
        self.assertIn('"cmdGearMergeQty"', source)
        self.assertIn('device.wait("Confirm quantity reduction"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"gearQuantityRulesSha256"', source)
        self.assertIn('"gearQuantityContractSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixture_is_career_only_with_unique_stable_ids_and_merge_superficials(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("10000", root.findtext("nuyen"))
        gear = root.findall("./gears/gear")
        self.assertEqual(5, len(gear))
        ids = [uuid.UUID(item.findtext("guid", default="")) for item in gear]
        self.assertEqual(len(ids), len(set(ids)))
        merge = [item for item in gear if item.findtext("name") == "Merge Stack E2E"]
        self.assertEqual(2, len(merge))
        self.assertEqual(merge[0].findtext("category"), merge[1].findtext("category"))
        self.assertEqual(merge[0].findtext("rating"), merge[1].findtext("rating"))
        self.assertEqual(merge[0].findtext("extra"), merge[1].findtext("extra"))
        self.assertNotEqual(merge[0].findtext("gearname"), merge[1].findtext("gearname"))
        self.assertNotEqual(merge[0].findtext("notes"), merge[1].findtext("notes"))


if __name__ == "__main__":
    unittest.main()
