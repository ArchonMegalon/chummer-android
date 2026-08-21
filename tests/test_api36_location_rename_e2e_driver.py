import ast
import importlib.util
from pathlib import Path
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_location_rename_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-location-rename-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-location-rename-e2e.chum5",
)
SECTIONS = ("gearlocations", "weaponlocations", "armorlocations", "vehiclelocations")
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
SPEC = importlib.util.spec_from_file_location("run_api36_location_rename_e2e", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


class Api36LocationRenameE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_bound_and_restart_complete(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "location-rename"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-section-tab-gear"', source)
        for kind, section_id, control in driver.LOCATION_KINDS:
            self.assertIn(section_id, source)
            self.assertIn(control, source)
            self.assertIn(f'"{kind}"', source)
        self.assertIn('f"build-action-tab-gear-{section_id}"', source)
        self.assertIn('f"location-rename-open-{kind.lower()}-', source)
        self.assertIn('"location-rename-page"', source)
        self.assertIn('"location-rename-name"', source)
        self.assertIn('"location-rename-save"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertGreaterEqual(source.count("device.assert_text(new_name"), 2)
        self.assertIn('"processRestartWorkspacePersisted"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"locationStateSha256"', source)
        self.assertIn('"locationRenameContractSha256"', source)
        self.assertIn('"controlCount": len(controls)', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_have_exact_modes_and_all_four_stable_location_types(self) -> None:
        created_values = []
        all_ids: set[uuid.UUID] = set()
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            for section in SECTIONS:
                locations = root.findall(f"./{section}/location")
                self.assertEqual(1, len(locations), f"{fixture.name}:{section}")
                location_id = uuid.UUID(locations[0].findtext("guid", default=""))
                self.assertNotIn(location_id, all_ids)
                all_ids.add(location_id)
                self.assertIn("Old E2E", locations[0].findtext("name", default=""))
                notes = locations[0].find("notes")
                self.assertIsNotNone(notes)
                self.assertIn("Notes E2E", notes.text or "")

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(8, len(all_ids))


if __name__ == "__main__":
    unittest.main()
