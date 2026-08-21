import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_cyberware_active_commlink_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-cyberware-active-commlink-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-cyberware-active-commlink-e2e.chum5",
)


class Api36CyberwareActiveCommlinkE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_arm64_and_digest_binds_full_authority(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "cyberware-active-commlink"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('"package": shared.PACKAGE', source)
        self.assertIn('"CharacterCreate.chkCyberwareActiveCommlink"', source)
        self.assertIn('"CharacterCareer.chkCyberwareActiveCommlink"', source)
        self.assertIn('f"cyberware-active-commlink-open-{compact_id}"', source)
        self.assertIn('f"cyberware-active-commlink-toggle-{compact_id}"', source)
        self.assertIn('f"cyberware-active-commlink-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"expectedRevisionAtomicSave"', source)
        self.assertIn('"exclusiveCharacterWideActiveCommlink"', source)
        self.assertIn('"cyberwareActiveCommlinkRulesSha256"', source)
        self.assertIn('"weaponHomeNodeRulesSha256"', source)
        self.assertIn('"cyberwareActiveCommlinkContractSha256"', source)
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
            cyberwares = root.findall("./cyberwares/cyberware")
            gears = root.findall("./gears/gear")
            self.assertEqual(2, len(cyberwares))
            self.assertEqual(1, len(gears))
            self.assertEqual("False", cyberwares[0].findtext("active"))
            self.assertEqual("Self", cyberwares[0].findtext("canformpersona"))
            self.assertEqual("False", cyberwares[1].findtext("active"))
            self.assertEqual("True", gears[0].findtext("active"))
            self.assertTrue(root.findtext("./customstate/active", default="").endswith("unrelated active text"))

            local_ids = [
                uuid.UUID(item.findtext("guid", default=""))
                for item in [*cyberwares, *gears]
            ]
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)
            self.assertTrue(all(item.findtext("notes", default="") for item in [*cyberwares, *gears]))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(6, len(all_ids))


if __name__ == "__main__":
    unittest.main()
