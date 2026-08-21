import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_cyberware_commerce_e2e.py"
FIXTURE = REPO / "tests" / "fixtures" / "career-cyberware-commerce-e2e.chum5"


class Api36CyberwareCommerceE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_both_controls(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "cyberware-commerce"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"tsCyberwareUpgrade"', source)
        self.assertIn('"tsCyberwareSell"', source)
        self.assertIn('device.wait("Confirm Cyberware upgrade"', source)
        self.assertIn('device.wait("Confirm Cyberware sale"', source)
        self.assertIn('device.tap("Cancel")', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"commerceRulesSha256"', source)
        self.assertIn('"commerceContractSha256"', source)
        self.assertIn('"fileSourceResolverSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixture_is_career_source_backed_and_has_stable_guarded_identities(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("10000", root.findtext("nuyen"))
        self.assertEqual("223a11ff-80e0-428b-89a9-6ef1c243b8b6", root.findtext("settings"))
        ware = root.findall(".//cyberware")
        identities = [uuid.UUID(item.findtext("guid", default="")) for item in ware]
        self.assertEqual(len(identities), len(set(identities)))
        source_ids = {item.findtext("sourceid") for item in ware}
        self.assertIn("eb9e691a-8002-4138-ac8d-d9714d398b1e", source_ids)
        self.assertIn("b57eadaa-7c3b-4b80-8d79-cbbd922c1196", source_ids)
        linked = next(item for item in ware if item.findtext("guid") == "96666666-6666-6666-6666-666666666666")
        self.assertEqual("[*]", linked.findtext("capacity"))
        self.assertIsNotNone(root.find("expenses"))
        self.assertIsNone(root.find("improvements"))


if __name__ == "__main__":
    unittest.main()
