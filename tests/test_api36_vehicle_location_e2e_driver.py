import ast
import importlib.util
from pathlib import Path
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_vehicle_location_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-vehicle-location-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-vehicle-location-e2e.chum5",
)
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
SPEC = importlib.util.spec_from_file_location("run_api36_vehicle_location_e2e", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


class Api36VehicleLocationE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_both_context_branches(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "vehicle-location-add"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('open_gear_action(device, "vehiclelocations")', source)
        self.assertIn('open_gear_action(device, "vehicles")', source)
        self.assertIn('"vehicle-location-open-add-global"', source)
        self.assertIn('f"vehicle-location-open-add-{compact_id}"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertGreaterEqual(source.count('device.assert_text(expected["global_added"]'), 2)
        self.assertGreaterEqual(source.count('device.assert_text(expected["nested_added"]'), 2)
        self.assertIn('"globalBranchMutated"', source)
        self.assertIn('"selectedVehicleBranchMutated"', source)
        self.assertIn('"processRestartWorkspacePersisted"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"characterSectionModelsSha256"', source)
        self.assertIn('"collectionEditorProjectorSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_have_exact_modes_and_distinct_global_nested_and_untouched_identities(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            global_locations = root.findall("./vehiclelocations/location")
            vehicles = root.findall("./vehicles/vehicle")
            self.assertEqual(1, len(global_locations))
            self.assertEqual(2, len(vehicles))
            self.assertTrue(all(len(vehicle.findall("./locations/location")) == 1 for vehicle in vehicles))

            local_ids = [
                uuid.UUID(element.findtext("guid", default=""))
                for element in [*global_locations, *vehicles]
            ]
            local_ids.extend(
                uuid.UUID(location.findtext("guid", default=""))
                for vehicle in vehicles
                for location in vehicle.findall("./locations/location")
            )
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)
            self.assertIn("Existing Global Vehicle Location E2E", global_locations[0].findtext("name", default=""))
            self.assertIn("Existing Nested Vehicle Location E2E", vehicles[0].findtext("./locations/location/name", default=""))
            self.assertIn("Untouched Nested Vehicle Location E2E", vehicles[1].findtext("./locations/location/name", default=""))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(10, len(all_ids))


if __name__ == "__main__":
    unittest.main()
