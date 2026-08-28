import copy
from contextlib import redirect_stderr
import io
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import run_api36_sr5_downtime_calendar_e2e as driver


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_downtime_calendar_e2e.py"


def journal(fixture: dict[str, object], *, applied: bool) -> dict[str, object]:
    target = fixture["target"]
    review = {
        "WorkspaceId": "workspace-calendar", "WorkspaceRevision": 7,
        "SnapshotDigest": "sha256:" + "1" * 64,
        "Schema": "chummer.android.sr5-downtime-calendar.checkpoint/v1",
        "Preview": {
            "PreviewDigest": "sha256:" + "2" * 64,
            "Schema": "chummer.android.sr5-downtime-calendar.preview/v1",
            "Operation": 1, "WeekId": target["weekId"],
            "Year": target["year"], "Week": target["isoWeek"],
            "Notes": fixture["edit"]["notes"],
            "NotesColor": fixture["edit"]["notesColor"],
            "ExpectedCalendarRevision": "3" * 64,
            "ExpectedLogicalRevision": "sha256:" + "4" * 64,
            "ExpectedSourceRevision": "sha256:" + "5" * 64,
            "Summary": f"Edit exact week {target['weekId']}",
        },
    }
    expected_postcondition = "sha256:" + "6" * 64
    receipt = None
    if applied:
        receipt = {
            "ContractName": "chummer.android.sr5-downtime-calendar.persistence-receipt/v1",
            "WorkspaceId": "workspace-calendar",
            "ExpectedWorkspaceRevision": 7, "AppliedWorkspaceRevision": 8,
            "ActionId": target["weekId"], "Operation": 1,
            "PreviewDigest": review["Preview"]["PreviewDigest"],
            "ExpectedPostconditionDigest": expected_postcondition,
            "ObservedPostconditionDigest": expected_postcondition,
            "CalendarRevisionAfter": "7" * 64,
            "SourceDigestAfter": "sha256:" + "8" * 64,
            "ContentDigestAfter": "sha256:" + "9" * 64,
            "ReceiptDigest": "sha256:" + "a" * 64,
        }
    return {
        "SchemaVersion": 1, "Version": 3 if applied else 1,
        "Phase": 2 if applied else 0,
        "OwnerId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        "ActionId": target["weekId"], "Review": review,
        "ExpectedPostconditionDigest": expected_postcondition,
        "Receipt": receipt, "JournalDigest": "sha256:" + "b" * 64,
    }


class Api36Sr5DowntimeCalendarDriverTests(unittest.TestCase):
    def test_fixture_pins_exact_runner_target_and_preserved_state(self) -> None:
        fixture = driver.load_fixture()
        runner = driver.DEFAULT_FIXTURE.parent / fixture["runnerFixture"]
        self.assertEqual(fixture["runnerFixtureSha256"], driver.physical.shared.sha256(runner))
        root = ET.parse(runner).getroot()
        by_id = {week.findtext("guid"): week for week in root.findall("./calendar/week")}
        target = fixture["target"]
        self.assertEqual(target["notesBefore"], by_id[target["weekId"]].findtext("notes"))
        self.assertEqual(target["notesColorBefore"], by_id[target["weekId"]].findtext("notesColor"))
        self.assertEqual(
            fixture["preserve"]["sentinel"], root.findtext("./customstate/sentinel")
        )

    def test_fixture_and_journal_tampering_fail_closed(self) -> None:
        fixture = driver.load_fixture()
        tampered_fixture = copy.deepcopy(fixture)
        tampered_fixture["target"]["weekId"] = tampered_fixture["preserve"]["weekId"]
        with self.assertRaises(RuntimeError):
            driver.validate_fixture(tampered_fixture)
        reviewed = journal(fixture, applied=False)
        driver.validate_journal(
            reviewed, fixture, workspace_id="workspace-calendar",
            workspace_revision=7, version=1, phase=0,
        )
        tampered_journal = copy.deepcopy(reviewed)
        tampered_journal["Review"]["Preview"]["Notes"] = "different"
        with self.assertRaises(RuntimeError):
            driver.validate_journal(
                tampered_journal, fixture, workspace_id="workspace-calendar",
                workspace_revision=7, version=1, phase=0,
            )

    def test_applied_receipt_binds_review_action_postcondition_and_successor(self) -> None:
        fixture = driver.load_fixture()
        reviewed = driver.validate_journal(
            journal(fixture, applied=False), fixture,
            workspace_id="workspace-calendar", workspace_revision=7,
            version=1, phase=0,
        )
        applied_payload = journal(fixture, applied=True)
        applied = driver.validate_journal(
            applied_payload, fixture, workspace_id="workspace-calendar",
            workspace_revision=7, version=3, phase=2,
        )
        for field in ("actionId", "previewDigest", "expectedPostconditionDigest"):
            self.assertEqual(reviewed[field], applied[field])
        self.assertEqual("sha256:" + "a" * 64, applied["receiptDigest"])
        tampered = copy.deepcopy(applied_payload)
        tampered["Receipt"]["AppliedWorkspaceRevision"] = 9
        with self.assertRaises(RuntimeError):
            driver.validate_journal(
                tampered, fixture, workspace_id="workspace-calendar",
                workspace_revision=7, version=3, phase=2,
            )

    def test_driver_is_apk_source_arm64_restart_reconfirm_ack_and_reopen_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        compile(source, str(DRIVER), "exec")
        for marker in (
            "load_and_verify_manifest", "source_graph_snapshot",
            "android_device_observation", "expected_apk_sha256",
            "sr5-downtime-calendar-operation", "Edit week",
            "Reviewed preview restored. Confirm it again before saving.",
            "sr5-downtime-calendar-confirm", "sr5-downtime-calendar-apply",
            "sr5-downtime-calendar-clear-applied", "acknowledgeAndReopen",
            "finalRestartSuccessor",
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count("force_stop_and_launch_new_process"), 3)
        self.assertIn('"status": "device-pass-source-bound"', source)
        self.assertNotIn('"releaseAttested": True', source)

    def test_argument_failure_writes_nonpassing_receipt_without_device_access(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "receipt.json"
            with redirect_stderr(io.StringIO()):
                self.assertEqual(2, driver.main(["--receipt", str(receipt)]))
            value = json.loads(receipt.read_text(encoding="utf-8"))
            self.assertEqual("fail", value["status"])
            self.assertEqual("manifest-not-verified", value["releaseEvidenceStatus"])


if __name__ == "__main__":
    unittest.main()
