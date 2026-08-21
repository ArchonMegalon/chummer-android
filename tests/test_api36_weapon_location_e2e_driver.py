import ast
import importlib.util
from pathlib import Path
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_weapon_location_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-weapon-location-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-weapon-location-e2e.chum5",
)
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
SPEC = importlib.util.spec_from_file_location("run_api36_weapon_location_e2e", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


class Api36WeaponLocationE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_restart_complete(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "weapon-location-add"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-action-tab-gear-weaponlocations"', source)
        self.assertIn('"weapon-location-open-add"', source)
        self.assertIn('"weapon-location-name"', source)
        self.assertIn('"weapon-location-add"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertGreaterEqual(source.count("device.assert_text(expected"), 2)
        self.assertIn('"processRestartWorkspacePersisted"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"weaponLocationContractSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_have_exact_modes_and_stable_existing_weapon_locations(self) -> None:
        created_values = []
        ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            locations = root.findall("./weaponlocations/location")
            self.assertEqual(1, len(locations))
            location_id = uuid.UUID(locations[0].findtext("guid", default=""))
            self.assertNotIn(location_id, ids)
            ids.add(location_id)
            self.assertIn("Existing Weapon Location E2E", locations[0].findtext("name", default=""))
            self.assertIn("existing weapon notes E2E", locations[0].findtext("notes", default=""))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(2, len(ids))


if __name__ == "__main__":
    unittest.main()
