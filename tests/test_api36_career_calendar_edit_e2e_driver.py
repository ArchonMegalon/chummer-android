import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_calendar_edit_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-calendar-edit-e2e.chum5"
LATEST_ID = "11111111-1111-1111-1111-111111111111"
EARLIER_ID = "22222222-2222-2222-2222-222222222222"


class Api36CareerCalendarEditDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_full_graph_digest_revision_and_two_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for control in (
            "cmdAddWeek",
            "cmdDeleteWeek",
            "cmdEditWeek",
            "cmdChangeStartWeek",
            "lstCalendar",
            "SelectCalendarStart.nudYear",
            "SelectCalendarStart.nudWeek",
            "SelectCalendarStart.cmdOK",
        ):
            self.assertIn(f'"{control}"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-calendar-edit"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertNotIn('"tablet"', source)
        self.assertIn('"careerCalendarRulesSha256"', source)
        self.assertIn('"careerCalendarMutationSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn("after.content_revision != before.content_revision + 1", source)
        self.assertIn("after.payload_sha256 == before.payload_sha256", source)
        self.assertEqual(2, source.count("shared.force_stop_and_launch_new_process"))
        self.assertIn("shared.require_restored_authority(deleted, first_restored)", source)
        self.assertIn("shared.require_restored_authority(deleted, second_restored)", source)
        self.assertIn('device.tap("Cancel"', source)
        self.assertIn('device.tap("Delete"', source)
        self.assertIn('attributes.get("enabled") != "false"', source)
        self.assertIn('notes.attributes.get("text") != "After-run complete"', source)

    def test_fixture_has_exact_stable_weeks_missing_color_and_nested_authority(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("CareerCalendarEditE2E", root.findtext("alias"))
        weeks = root.findall("./calendar/week")
        self.assertEqual(2, len(weeks))
        by_id = {week.findtext("guid"): week for week in weeks}
        self.assertEqual({LATEST_ID, EARLIER_ID}, set(by_id))
        for identity in by_id:
            uuid.UUID(identity)
        self.assertEqual("2081", by_id[LATEST_ID].findtext("year"))
        self.assertEqual("12", by_id[LATEST_ID].findtext("week"))
        self.assertEqual("#A52A2A", by_id[LATEST_ID].findtext("notesColor"))
        self.assertEqual("11", by_id[EARLIER_ID].findtext("week"))
        self.assertIsNone(by_id[EARLIER_ID].find("notesColor"))
        self.assertEqual(
            "nested-calendar-must-survive",
            root.findtext("./customstate/calendar/week/sentinel"),
        )
        sentinel = root.find("./customstate/sentinel")
        self.assertIsNotNone(sentinel)
        self.assertEqual("nested-sentinel", sentinel.get("guid"))
        self.assertEqual("keep-nested-structure", sentinel.text)


if __name__ == "__main__":
    unittest.main()
