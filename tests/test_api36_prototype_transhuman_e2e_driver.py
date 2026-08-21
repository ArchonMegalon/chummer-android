import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_prototype_transhuman_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-prototype-transhuman-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-prototype-transhuman-e2e.chum5"


class Api36PrototypeTranshumanE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_and_digest_binds_full_authority(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROL = "CharacterCreate.chkPrototypeTranshuman"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "prototype-transhuman"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('f"prototype-transhuman-open-{compact}"', source)
        self.assertIn('f"prototype-transhuman-toggle-{compact}"', source)
        self.assertIn('f"prototype-transhuman-save-{compact}"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"recursiveDescendantPersistence"', source)
        self.assertIn('"expectedRevisionAtomicSave"', source)
        self.assertIn('"prototypeTranshumanRulesSha256"', source)
        self.assertIn('"prototypeTranshumanContractSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_prove_creation_positive_and_career_negative_with_unique_hierarchy(self) -> None:
        creation = ET.parse(CREATION).getroot()
        career = ET.parse(CAREER).getroot()
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        for root in (creation, career):
            improvement = root.find("./improvements/improvement")
            self.assertIsNotNone(improvement)
            self.assertEqual("PrototypeTranshuman", improvement.findtext("improvementttype"))
            self.assertGreater(float(improvement.findtext("val", default="0")), 0)
            ids = [uuid.UUID(item.findtext("guid", default="")) for item in root.findall(".//cyberware")]
            self.assertEqual(len(ids), len(set(ids)))
            self.assertTrue(all(identity.int != 0 for identity in ids))
            top_level = root.findall("./cyberwares/cyberware")
            self.assertTrue(top_level)
            self.assertEqual("Bioware", top_level[0].findtext("improvementsource"))
            self.assertTrue(all(item.findtext("prototypetranshuman") == "False" for item in root.findall(".//cyberware")))


if __name__ == "__main__":
    unittest.main()
