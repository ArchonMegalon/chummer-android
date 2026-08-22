import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_vehicle_active_commlink_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-vehicle-active-commlink-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-vehicle-active-commlink-e2e.chum5",
)


class Api36VehicleActiveCommlinkE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_arm64_and_digest_binds_full_authority(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "vehicle-active-commlink"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('"package": shared.PACKAGE', source)
        self.assertIn('"CharacterCreate.chkVehicleActiveCommlink"', source)
        self.assertIn('"CharacterCareer.chkVehicleActiveCommlink"', source)
        self.assertIn('f"vehicle-active-commlink-open-{compact_id}"', source)
        self.assertIn('f"vehicle-active-commlink-toggle-{compact_id}"', source)
        self.assertIn('f"vehicle-active-commlink-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"expectedRevisionAtomicSave"', source)
        self.assertIn('"exclusiveCharacterWideActiveCommlink"', source)
        self.assertIn('"zeroNuyenKarmaEconomics"', source)
        self.assertIn('"descendantTargetsFailClosedCoverage"', source)
        self.assertIn('"vehicleActiveCommlinkRulesSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn('"legacyCreateHandlerSha256"', source)
        self.assertIn('"legacyCareerHandlerSha256"', source)
        self.assertIn('"legacyMatrixAttributesSha256"', source)
        self.assertIn('"legacyVehicleRulesSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_pair_creation_and_career_with_unique_top_level_vehicle_ids(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            vehicles = root.findall("./vehicles/vehicle")
            top_gears = root.findall("./gears/gear")
            persona_gears = vehicles[0].findall("./gears/gear")
            self.assertEqual(2, len(vehicles))
            self.assertEqual(1, len(top_gears))
            self.assertEqual(1, len(persona_gears))
            self.assertEqual("False", vehicles[0].findtext("active"))
            self.assertEqual("Parent", persona_gears[0].findtext("canformpersona"))
            self.assertEqual("True", top_gears[0].findtext("active"))
            self.assertTrue(root.findtext("./customstate/active", default="").endswith("unrelated active text"))

            items = [*vehicles, *top_gears, *persona_gears]
            local_ids = [uuid.UUID(item.findtext("guid", default="")) for item in items]
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)
            self.assertTrue(all(item.findtext("notes", default="") for item in items))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(8, len(all_ids))


if __name__ == "__main__":
    unittest.main()
