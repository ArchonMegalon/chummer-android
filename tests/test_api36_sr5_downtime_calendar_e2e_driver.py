import copy
from contextlib import redirect_stderr
from dataclasses import replace
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
    return driver._expected_downtime_journal(
        fixture, workspace_id="workspace-calendar", workspace_revision=7,
        payload_sha256="1" * 64, document_sha256=fixture["runnerFixtureSha256"],
        owner_id="eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        version=3 if applied else 1, phase=2 if applied else 0,
        successor_payload_sha256="2" * 64 if applied else None,
        successor_document_sha256="3" * 64 if applied else None,
    )


def validate(
    value: dict[str, object], fixture: dict[str, object], *, applied: bool
) -> dict[str, str]:
    return driver.validate_journal(
        value, fixture, workspace_id="workspace-calendar", workspace_revision=7,
        payload_sha256="1" * 64, document_sha256=fixture["runnerFixtureSha256"],
        version=3 if applied else 1, phase=2 if applied else 0,
        successor_payload_sha256="2" * 64 if applied else None,
        successor_document_sha256="3" * 64 if applied else None,
    )


class Api36Sr5DowntimeCalendarDriverTests(unittest.TestCase):
    def test_initial_save_authority_requires_exact_1_1_unchanged_fixture(self) -> None:
        fixture_sha256 = "a" * 64
        imported = driver.physical.shared.WorkspaceAuthority(
            "workspace-downtime", 1, 0, fixture_sha256, "b" * 64
        )
        exact = driver.physical.shared.WorkspaceAuthority(
            "workspace-downtime", 1, 1, fixture_sha256, "c" * 64
        )

        driver.require_initial_saved_fixture_authority(imported, exact, fixture_sha256)

        hostile_saved = (
            imported,
            replace(exact, workspace_id="foreign-workspace"),
            replace(exact, content_revision=2, saved_revision=2),
            replace(exact, saved_revision=0),
            replace(exact, payload_sha256="d" * 64),
        )
        with self.assertRaises(RuntimeError):
            driver.require_initial_saved_fixture_authority(
                replace(imported, saved_revision=1), exact, fixture_sha256
            )
        for candidate in hostile_saved:
            with self.subTest(candidate=candidate), self.assertRaises(RuntimeError):
                driver.require_initial_saved_fixture_authority(
                    imported, candidate, fixture_sha256
                )

    def test_initial_save_is_established_before_downtime_entry_without_replay(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        proof = source.split("def prove_downtime(", maxsplit=1)[1].split(
            "\ndef parse_args", maxsplit=1
        )[0]

        self.assertEqual(1, proof.count("save_and_read_workspace_authority"))
        self.assertLess(
            proof.index("save_and_read_workspace_authority"),
            proof.index("open_downtime(device)"),
        )
        self.assertIn(
            "require_initial_saved_fixture_authority(imported, initial_saved, runner_sha256)",
            proof,
        )
        self.assertIn('"initialSaved":', proof)

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
        validate(reviewed, fixture, applied=False)
        tampered_journal = copy.deepcopy(reviewed)
        tampered_journal["Review"]["Preview"]["Notes"] = "different"
        with self.assertRaises(RuntimeError):
            validate(tampered_journal, fixture, applied=False)

    def test_applied_receipt_binds_review_action_postcondition_and_successor(self) -> None:
        fixture = driver.load_fixture()
        reviewed = validate(journal(fixture, applied=False), fixture, applied=False)
        applied_payload = journal(fixture, applied=True)
        applied = validate(applied_payload, fixture, applied=True)
        for field in ("actionId", "previewDigest", "expectedPostconditionDigest"):
            self.assertEqual(reviewed[field], applied[field])
        self.assertRegex(applied["receiptDigest"], r"^sha256:[0-9a-f]{64}$")
        tampered = copy.deepcopy(applied_payload)
        tampered["Receipt"]["AppliedWorkspaceRevision"] = 9
        with self.assertRaises(RuntimeError):
            validate(tampered, fixture, applied=True)

    def test_hostile_self_consistent_foreign_review_and_preview_are_rejected(self) -> None:
        fixture = driver.load_fixture()
        foreign = copy.deepcopy(fixture)
        foreign["edit"]["notes"] = "Foreign but self-consistent"
        payload = driver._expected_downtime_journal(
            foreign, workspace_id="workspace-calendar", workspace_revision=7,
            payload_sha256="1" * 64,
            document_sha256=fixture["runnerFixtureSha256"],
            owner_id="eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", version=1, phase=0,
        )
        with self.assertRaises(RuntimeError):
            validate(payload, fixture, applied=False)

    def test_hostile_arbitrary_journal_receipt_and_summary_digests_are_rejected(self) -> None:
        fixture = driver.load_fixture()
        paths = (
            ("JournalDigest",), ("Receipt", "ReceiptDigest"),
            ("Review", "Preview", "PreviewDigest"),
            ("Review", "Preview", "Summary"),
        )
        for path in paths:
            payload = journal(fixture, applied=True)
            target = payload
            for part in path[:-1]:
                target = target[part]
            target[path[-1]] = "sha256:" + "0" * 64
            with self.subTest(path=path), self.assertRaises(RuntimeError):
                validate(payload, fixture, applied=True)

    def test_hostile_target_and_preserved_unrelated_xml_changes_are_rejected(self) -> None:
        fixture = driver.load_fixture()
        runner = driver.DEFAULT_FIXTURE.parent / fixture["runnerFixture"]
        valid = ET.parse(runner).getroot()
        target = next(
            week for week in valid.findall("./calendar/week")
            if week.findtext("guid") == fixture["target"]["weekId"]
        )
        target.find("notes").text = fixture["edit"]["notes"]
        target.find("notesColor").text = fixture["edit"]["notesColor"]
        driver.assert_calendar(valid, fixture, edited=True)
        for mutate in (
            lambda root: setattr(root.find("./calendar/week/year"), "text", "2082"),
            lambda root: setattr(root.findall("./calendar/week")[1].find("guid"), "text", "99999999-9999-9999-9999-999999999999"),
            lambda root: setattr(root.findall("./calendar/week")[1].find("custom"), "text", "changed"),
            lambda root: setattr(root.find("name"), "text", "Foreign Runner"),
        ):
            changed = ET.fromstring(ET.tostring(valid))
            mutate(changed)
            with self.subTest(mutate=mutate), self.assertRaises(RuntimeError):
                driver.assert_calendar(changed, fixture, edited=True)

    def test_unknown_fields_wrong_types_duplicate_identity_and_foreign_successor_fail(self) -> None:
        fixture = driver.load_fixture()
        unknown = journal(fixture, applied=True)
        unknown["Receipt"]["Foreign"] = True
        wrong_type = journal(fixture, applied=True)
        wrong_type["Review"]["WorkspaceRevision"] = "7"
        duplicate_identity = journal(fixture, applied=True)
        duplicate_identity["ActionId"] = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
        for payload in (unknown, wrong_type, duplicate_identity):
            with self.subTest(payload=payload), self.assertRaises(RuntimeError):
                validate(payload, fixture, applied=True)
        with self.assertRaises(RuntimeError):
            driver.validate_journal(
                journal(fixture, applied=True), fixture,
                workspace_id="workspace-calendar", workspace_revision=7,
                payload_sha256="1" * 64,
                document_sha256=fixture["runnerFixtureSha256"],
                version=3, phase=2, successor_payload_sha256="4" * 64,
                successor_document_sha256="3" * 64,
            )

    def test_driver_is_apk_source_arm64_restart_reconfirm_ack_and_reopen_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        compile(source, str(DRIVER), "exec")
        self.assertIn('"sr5-career/calendar", timeout=120', source)
        self.assertIn('"sr5-career-action-calendar", timeout=120', source)
        self.assertLess(
            source.index('"sr5-career/calendar", timeout=120'),
            source.index('physical.wait_exact_route(device, "sr5-career/calendar"'),
        )
        self.assertLess(
            source.index('physical.wait_exact_route(device, "sr5-career/calendar"'),
            source.index('"sr5-career-action-calendar", timeout=120'),
        )
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
