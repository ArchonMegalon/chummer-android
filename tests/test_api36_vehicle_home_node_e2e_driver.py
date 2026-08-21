import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_vehicle_home_node_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-vehicle-home-node-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-vehicle-home-node-e2e.chum5",
)


class Api36VehicleHomeNodeE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_proves_both_boolean_directions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "vehicle-home-node"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-action-tab-gear-vehicles"', source)
        self.assertIn('f"vehicle-home-node-open-{compact_id}"', source)
        self.assertIn('f"vehicle-home-node-toggle-{compact_id}"', source)
        self.assertIn('f"vehicle-home-node-save-{compact_id}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"enabledAsExclusiveHomeNode"', source)
        self.assertIn('"disabledFromHomeNode"', source)
        self.assertIn('"processRestartWorkspacePersisted"', source)
        self.assertIn('"processRestartUiReadback"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"characterSectionModelsSha256"', source)
        self.assertIn('"collectionEditorProjectorSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_both_modes_unique_ids_and_one_prior_home_node(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            gears = root.findall("./gears/gear")
            vehicles = root.findall("./vehicles/vehicle")
            self.assertEqual(1, len(gears))
            self.assertEqual(2, len(vehicles))
            self.assertEqual("True", gears[0].findtext("homenode"))
            self.assertTrue(all(vehicle.findtext("homenode") == "False" for vehicle in vehicles))

            local_ids = [
                uuid.UUID(element.findtext("guid", default=""))
                for element in [*gears, *vehicles]
            ]
            self.assertEqual(len(local_ids), len(set(local_ids)))
            self.assertTrue(all(identity.int != 0 for identity in local_ids))
            self.assertTrue(all(identity not in all_ids for identity in local_ids))
            all_ids.update(local_ids)
            self.assertTrue(all(element.findtext("notes", default="") for element in [*gears, *vehicles]))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(6, len(all_ids))


if __name__ == "__main__":
    unittest.main()
