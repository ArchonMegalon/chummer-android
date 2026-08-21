import ast
import importlib.util
from pathlib import Path
import sys
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_location_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-gear-location-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-gear-location-e2e.chum5",
)
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
SPEC = importlib.util.spec_from_file_location("run_api36_gear_location_e2e", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


class Api36GearLocationE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_restart_complete(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "gear-location-add"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-action-tab-gear-gearlocations"', source)
        self.assertIn('"gear-location-open-add"', source)
        self.assertIn('"gear-location-name"', source)
        self.assertIn('"gear-location-add"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"processRestartWorkspacePersisted"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"gearLocationContractSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_have_exact_creation_career_modes_and_stable_existing_locations(self) -> None:
        created_values = []
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            locations = root.findall("./gearlocations/location")
            self.assertEqual(1, len(locations))
            self.assertTrue(locations[0].findtext("guid"))
            self.assertIn("Existing Location E2E", locations[0].findtext("name", default=""))
            self.assertIsNotNone(locations[0].find("notes"))

        self.assertEqual(["False", "True"], created_values)


if __name__ == "__main__":
    unittest.main()
