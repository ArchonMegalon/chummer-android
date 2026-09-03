#!/usr/bin/env python3
"""Prove one durable SR5 Downtime Calendar edit on a physical API 36 ARM64 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_career_calendar_edit_e2e as calendar_leaf
import run_api36_sr5_career_active_skill_wizard_e2e as physical


SCHEMA = "chummer.android.sr5-downtime-calendar-physical-e2e/v1"
FIXTURE_SCHEMA = "chummer.android.sr5-downtime-calendar-physical-fixture/v1"
JOURNAL_KEY = "sr5.career.downtime-calendar.journal.v1"
ROUTE = "sr5-downtime-calendar-page"
DISPOSABLE_DEVICE_FLAG = physical.DISPOSABLE_DEVICE_FLAG
DEFAULT_FIXTURE = Path(__file__).resolve().parent / "fixtures/sr5-downtime-calendar-e2e.json"
FIXTURE_FIELDS = {
    "schema", "runnerFixture", "runnerFixtureSha256", "runnerAlias", "target",
    "edit", "preserve",
}
TARGET_FIELDS = {
    "weekId", "year", "isoWeek", "notesBefore", "notesColorBefore",
}
EDIT_FIELDS = {"notes", "notesColor"}
PRESERVE_FIELDS = {"weekId", "year", "isoWeek", "notes", "notesColor", "sentinel"}
JOURNAL_FIELDS = {
    "SchemaVersion", "Version", "Phase", "OwnerId", "ActionId", "Review",
    "ExpectedPostconditionDigest", "Receipt", "JournalDigest",
}
REVIEW_FIELDS = {"Schema", "WorkspaceId", "WorkspaceRevision", "SnapshotDigest", "Preview"}
PREVIEW_FIELDS = {
    "Schema", "Operation", "WeekId", "Year", "Week", "Notes", "NotesColor",
    "ExpectedCalendarRevision", "ExpectedLogicalRevision", "ExpectedSourceRevision",
    "Summary", "PreviewDigest",
}
RECEIPT_FIELDS = {
    "ContractName", "WorkspaceId", "ExpectedWorkspaceRevision",
    "AppliedWorkspaceRevision", "ActionId", "Operation", "PreviewDigest",
    "ExpectedPostconditionDigest", "ObservedPostconditionDigest",
    "CalendarRevisionAfter", "SourceDigestAfter", "ContentDigestAfter", "ReceiptDigest",
}
CHECKPOINT_SCHEMA = "chummer.sr5-downtime-calendar.checkpoint.v1"
PREVIEW_SCHEMA = "chummer.sr5-downtime-calendar.preview.v1"
RUNTIME_SCHEMA = "chummer.sr5-downtime-calendar.runtime.v1"
JOURNAL_SCHEMA = "chummer.android.sr5-downtime-calendar.journal.v1"
RECEIPT_SCHEMA = "chummer.android.sr5-downtime-calendar.persistence-receipt/v1"
CONTENT_SCHEMA = "chummer.android.sr5-downtime-calendar.content.v1"
SOURCE_AUTHORITY_DIGEST = "bbd5d2f9cf33eedbb7c2dc76c65717fe52f96653fd22b35a0daaee8c7f993338"
RUNTIME_FINGERPRINT = "sha256:8a8b74cc030e83ea266e18de58aef0918c054f74a7c7b3fa959ab730bfbe3e93"


def _mapping(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise RuntimeError(f"{label} must be one JSON object")
    return value


def _exact_fields(value: dict[str, object], fields: set[str], label: str) -> None:
    if set(value) != fields:
        raise RuntimeError(f"{label} fields are not exact")


def _integer(value: object, label: str, minimum: int = 0) -> int:
    if type(value) is not int or value < minimum:
        raise RuntimeError(f"{label} must be an integer >= {minimum}")
    return value


def _raw_digest(value: object, label: str) -> str:
    if not isinstance(value, str) or physical.LOWER_SHA256.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not raw lowercase SHA-256")
    return value


def _digest(value: object, label: str, *, prefixed: bool | None = None) -> str:
    if not isinstance(value, str):
        raise RuntimeError(f"{label} is not a digest")
    has_prefix = value.startswith("sha256:")
    if prefixed is not None and has_prefix != prefixed:
        raise RuntimeError(f"{label} prefix posture differs")
    _raw_digest(value.removeprefix("sha256:"), label)
    return value


def _append(material: str, value: object) -> str:
    rendered = str(value)
    return material + f"{len(rendered)}:{rendered};"


def _hash(material: str) -> str:
    return "sha256:" + hashlib.sha256(material.encode("utf-8")).hexdigest()


def _raw_hash(material: str) -> str:
    return hashlib.sha256(material.encode("utf-8")).hexdigest()


def _require_exact(actual: object, expected: object, label: str) -> None:
    if isinstance(expected, dict):
        mapped = _mapping(actual, label)
        _exact_fields(mapped, set(expected), label)
        for field, value in expected.items():
            _require_exact(mapped[field], value, f"{label}.{field}")
        return
    if isinstance(expected, list):
        if not isinstance(actual, list) or len(actual) != len(expected):
            raise RuntimeError(f"{label} list shape differs")
        for index, (left, right) in enumerate(zip(actual, expected, strict=True)):
            _require_exact(left, right, f"{label}[{index}]")
        return
    if type(actual) is not type(expected) or actual != expected:
        raise RuntimeError(f"{label} differs from exact product authority")


def _week_source(week: dict[str, object]) -> str:
    root = ET.Element("week")
    for name, value in (
        ("guid", week["weekId"]), ("year", week["year"]),
        ("week", week["isoWeek"]), ("notes", week["notes"]),
        ("notesColor", week["notesColor"]),
    ):
        ET.SubElement(root, name).text = str(value)
    return ET.tostring(root, encoding="unicode", short_empty_elements=True)


def _calendar_weeks(fixture: dict[str, object], *, edited: bool) -> list[dict[str, object]]:
    target = fixture["target"]
    edit = fixture["edit"]
    preserve = fixture["preserve"]
    raw = [
        {
            "weekId": target["weekId"], "year": target["year"],
            "isoWeek": target["isoWeek"],
            "notes": edit["notes"] if edited else target["notesBefore"],
            "notesColor": edit["notesColor"] if edited else target["notesColorBefore"],
        },
        {
            "weekId": preserve["weekId"], "year": preserve["year"],
            "isoWeek": preserve["isoWeek"], "notes": preserve["notes"],
            "notesColor": preserve["notesColor"],
        },
    ]
    result: list[dict[str, object]] = []
    for week in raw:
        source_revision = _raw_hash(_week_source(week))
        logical_revision = _raw_hash("\0".join([
            str(week["weekId"]), str(week["year"]), str(week["isoWeek"]),
            str(week["notes"]), str(week["notesColor"]), source_revision,
            SOURCE_AUTHORITY_DIGEST,
        ]))
        result.append({
            **week, "sourceRevision": source_revision,
            "logicalRevision": logical_revision,
        })
    return result


def _calendar_revision(weeks: list[dict[str, object]]) -> str:
    material = SOURCE_AUTHORITY_DIGEST + "\0" + str(len(weeks)) + "\0"
    for week in weeks:
        material += "\0".join([
            str(week["weekId"]), str(week["year"]), str(week["isoWeek"]),
            str(week["notes"]), str(week["notesColor"]),
            str(week["logicalRevision"]), str(week["sourceRevision"]), "",
        ])
    return _raw_hash(material)


def _content_digest(payload_sha256: str, document_sha256: str) -> str:
    material = CONTENT_SCHEMA
    material = _append(material, payload_sha256)
    material = _append(material, document_sha256)
    return _hash(material)


def _semantic_digest(weeks: list[dict[str, object]]) -> str:
    material = _append(
        "chummer.sr5-downtime-calendar.semantic-postcondition.v1",
        SOURCE_AUTHORITY_DIGEST,
    )
    for week in sorted(weeks, key=lambda item: item["weekId"]):
        for value in (
            week["weekId"], week["year"], week["isoWeek"], week["notes"],
            week["notesColor"],
        ):
            material = _append(material, value)
    return _hash(material)


def _expected_downtime_journal(
    fixture: dict[str, object], *, workspace_id: str, workspace_revision: int,
    payload_sha256: str, document_sha256: str, owner_id: str, version: int,
    phase: int, successor_payload_sha256: str | None = None,
    successor_document_sha256: str | None = None,
) -> dict[str, object]:
    _raw_digest(payload_sha256, "workspace payload digest")
    _raw_digest(document_sha256, "workspace document digest")
    before = _calendar_weeks(fixture, edited=False)
    after = _calendar_weeks(fixture, edited=True)
    before_revision = _calendar_revision(before)
    after_revision = _calendar_revision(after)
    content_digest = _content_digest(payload_sha256, document_sha256)
    source_digest = "sha256:" + SOURCE_AUTHORITY_DIGEST
    snapshot = "chummer.sr5-downtime-calendar.snapshot.v1"
    for value in (
        workspace_id, workspace_revision, workspace_revision, "sr5",
        RUNTIME_FINGERPRINT, source_digest, content_digest, before_revision,
        SOURCE_AUTHORITY_DIGEST,
    ):
        snapshot = _append(snapshot, value)
    for week in before:
        for value in (
            week["weekId"], week["year"], week["isoWeek"], week["notes"],
            week["notesColor"], week["logicalRevision"], week["sourceRevision"],
            SOURCE_AUTHORITY_DIGEST,
        ):
            snapshot = _append(snapshot, value)
    snapshot_digest = _hash(snapshot)
    target = before[0]
    summary = f"Edit notes for {target['year']} W{target['isoWeek']:02d}."
    preview_unsigned = {
        "Schema": PREVIEW_SCHEMA, "Operation": 1, "WeekId": target["weekId"],
        "Year": target["year"], "Week": target["isoWeek"],
        "Notes": fixture["edit"]["notes"],
        "NotesColor": fixture["edit"]["notesColor"],
        "ExpectedCalendarRevision": before_revision,
        "ExpectedLogicalRevision": target["logicalRevision"],
        "ExpectedSourceRevision": target["sourceRevision"], "Summary": summary,
    }
    preview_material = PREVIEW_SCHEMA
    for value in (
        snapshot_digest, "Edit", target["weekId"], target["year"],
        target["isoWeek"], fixture["edit"]["notes"], fixture["edit"]["notesColor"],
        before_revision, target["logicalRevision"], target["sourceRevision"], summary,
    ):
        preview_material = _append(preview_material, value)
    preview = {**preview_unsigned, "PreviewDigest": _hash(preview_material)}
    review = {
        "Schema": CHECKPOINT_SCHEMA, "WorkspaceId": workspace_id,
        "WorkspaceRevision": workspace_revision, "SnapshotDigest": snapshot_digest,
        "Preview": preview,
    }
    expected_postcondition = _semantic_digest(after)
    receipt: dict[str, object] | None = None
    if phase == 2:
        if successor_payload_sha256 is None or successor_document_sha256 is None:
            raise RuntimeError("Applied Downtime validation requires exact successor hashes")
        _raw_digest(successor_payload_sha256, "successor payload digest")
        _raw_digest(successor_document_sha256, "successor document digest")
        receipt = {
            "ContractName": RECEIPT_SCHEMA, "WorkspaceId": workspace_id,
            "ExpectedWorkspaceRevision": workspace_revision,
            "AppliedWorkspaceRevision": workspace_revision + 1,
            "ActionId": target["weekId"], "Operation": 1,
            "PreviewDigest": preview["PreviewDigest"],
            "ExpectedPostconditionDigest": expected_postcondition,
            "ObservedPostconditionDigest": expected_postcondition,
            "CalendarRevisionAfter": after_revision,
            "SourceDigestAfter": source_digest,
            "ContentDigestAfter": _content_digest(
                successor_payload_sha256, successor_document_sha256
            ),
            "ReceiptDigest": "",
        }
        receipt_material = RECEIPT_SCHEMA
        for value in (
            workspace_id, workspace_revision, workspace_revision + 1,
            target["weekId"], "Edit", preview["PreviewDigest"],
            expected_postcondition, expected_postcondition, after_revision,
            source_digest, receipt["ContentDigestAfter"],
        ):
            receipt_material = _append(receipt_material, value)
        receipt["ReceiptDigest"] = _hash(receipt_material)
    journal = {
        "SchemaVersion": 1, "Version": version, "Phase": phase,
        "OwnerId": owner_id, "ActionId": target["weekId"], "Review": review,
        "ExpectedPostconditionDigest": expected_postcondition, "Receipt": receipt,
        "JournalDigest": "",
    }
    journal_material = JOURNAL_SCHEMA
    phase_name = {0: "Review", 1: "Applying", 2: "Applied"}.get(phase)
    if phase_name is None:
        raise RuntimeError("Downtime journal phase is unsupported")
    for value in (
        1, version, phase_name, owner_id, target["weekId"], workspace_id,
        workspace_revision, snapshot_digest, CHECKPOINT_SCHEMA,
        preview["PreviewDigest"], PREVIEW_SCHEMA, "Edit", target["weekId"],
        target["year"], target["isoWeek"], fixture["edit"]["notes"],
        fixture["edit"]["notesColor"], before_revision, target["logicalRevision"],
        target["sourceRevision"], summary, expected_postcondition,
        "" if receipt is None else receipt["ReceiptDigest"],
    ):
        journal_material = _append(journal_material, value)
    journal["JournalDigest"] = _hash(journal_material)
    return journal


def _strict_json(path: Path) -> dict[str, object]:
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 32 * 1024:
        raise RuntimeError("Downtime fixture must be one bounded regular file")
    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=physical.object_without_duplicates,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        raise RuntimeError("Downtime fixture is not strict UTF-8 JSON") from error
    return _mapping(value, "Downtime fixture")


def validate_fixture(value: dict[str, object]) -> dict[str, object]:
    _exact_fields(value, FIXTURE_FIELDS, "Downtime fixture")
    if value["schema"] != FIXTURE_SCHEMA:
        raise RuntimeError("Downtime fixture schema differs")
    if value["runnerFixture"] != "career-calendar-edit-e2e.chum5":
        raise RuntimeError("Downtime runner fixture basename differs")
    _raw_digest(value["runnerFixtureSha256"], "runner fixture digest")
    if value["runnerAlias"] != "CareerCalendarEditE2E":
        raise RuntimeError("Downtime runner alias differs")
    target = _mapping(value["target"], "target")
    edit = _mapping(value["edit"], "edit")
    preserve = _mapping(value["preserve"], "preserve")
    _exact_fields(target, TARGET_FIELDS, "target")
    _exact_fields(edit, EDIT_FIELDS, "edit")
    _exact_fields(preserve, PRESERVE_FIELDS, "preserve")
    target_id = physical.canonical_guid(target["weekId"], "target week")
    preserve_id = physical.canonical_guid(preserve["weekId"], "preserved week")
    if target_id == preserve_id:
        raise RuntimeError("Target and preserved week identities match")
    _integer(target["year"], "target year", 1900)
    _integer(target["isoWeek"], "target ISO week", 1)
    _integer(preserve["year"], "preserved year", 1900)
    _integer(preserve["isoWeek"], "preserved ISO week", 1)
    for owner, field in (
        (target, "notesBefore"), (target, "notesColorBefore"),
        (edit, "notes"), (edit, "notesColor"), (preserve, "notes"),
        (preserve, "notesColor"), (preserve, "sentinel"),
    ):
        if not isinstance(owner[field], str) or not owner[field] or len(owner[field]) > 128:
            raise RuntimeError(f"Downtime fixture text {field} is invalid")
    if edit["notes"] == target["notesBefore"] or edit["notesColor"] == target["notesColorBefore"]:
        raise RuntimeError("Downtime edit does not change both governed values")
    return value


def load_fixture(path: Path = DEFAULT_FIXTURE) -> dict[str, object]:
    fixture = validate_fixture(_strict_json(path.resolve()))
    runner = path.resolve().parent / str(fixture["runnerFixture"])
    if runner.is_symlink() or not runner.is_file():
        raise RuntimeError("Governed Downtime runner is missing or not regular")
    if physical.shared.sha256(runner) != fixture["runnerFixtureSha256"]:
        raise RuntimeError("Governed Downtime runner bytes differ")
    root = ET.parse(runner).getroot()
    weeks = root.findall("./calendar/week")
    if len(weeks) != 2:
        raise RuntimeError("Governed Downtime runner week cardinality differs")
    by_id = {week.findtext("guid"): week for week in weeks}
    if set(by_id) != {fixture["target"]["weekId"], fixture["preserve"]["weekId"]}:
        raise RuntimeError("Downtime fixture identities differ from the governed runner")
    target = by_id[fixture["target"]["weekId"]]
    preserved = by_id[fixture["preserve"]["weekId"]]
    for actual, expected, label in (
        (target.findtext("year"), str(fixture["target"]["year"]), "target year"),
        (target.findtext("week"), str(fixture["target"]["isoWeek"]), "target week"),
        (target.findtext("notes"), fixture["target"]["notesBefore"], "target notes"),
        (target.findtext("notesColor"), fixture["target"]["notesColorBefore"], "target color"),
        (preserved.findtext("year"), str(fixture["preserve"]["year"]), "preserved year"),
        (preserved.findtext("week"), str(fixture["preserve"]["isoWeek"]), "preserved week"),
        (preserved.findtext("notes"), fixture["preserve"]["notes"], "preserved notes"),
    ):
        if actual != expected:
            raise RuntimeError(f"Downtime fixture {label} differs from governed runner")
    preserved_color = preserved.findtext("notesColor") or "Chocolate"
    if preserved_color != fixture["preserve"]["notesColor"]:
        raise RuntimeError("Downtime preserved color differs from product default")
    sentinel = root.find("./customstate/sentinel")
    if sentinel is None or sentinel.text != fixture["preserve"]["sentinel"]:
        raise RuntimeError("Downtime preserved sentinel differs from governed runner")
    assert_calendar(root, fixture, edited=False)
    return fixture


def read_journal(
    device: physical.shared.Device,
    *,
    required: bool = True,
) -> physical.CheckpointSnapshot | None:
    listing = device.shell(
        "run-as", physical.shared.PACKAGE, "find", "shared_prefs", "-type", "f",
        "-name", "*.xml",
    )
    matches: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run("exec-out", "run-as", physical.shared.PACKAGE, "cat", path).stdout
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as error:
            raise RuntimeError(f"Shared-preference file is malformed: {path}") from error
        matches.extend(
            element.text or "" for element in root.findall("string")
            if element.get("name") == JOURNAL_KEY
        )
    if not matches:
        if required:
            raise RuntimeError("Durable Downtime Calendar journal is missing")
        return None
    if len(matches) != 1 or not matches[0]:
        raise RuntimeError(f"Expected one Downtime journal, got {len(matches)}")
    serialized = matches[0]
    try:
        payload = json.loads(serialized, object_pairs_hook=physical.object_without_duplicates)
    except (json.JSONDecodeError, ValueError) as error:
        raise RuntimeError("Durable Downtime journal is not strict JSON") from error
    return physical.CheckpointSnapshot(
        _mapping(payload, "Downtime journal"),
        hashlib.sha256(serialized.encode("utf-8")).hexdigest(),
    )


def validate_journal(
    payload: dict[str, object],
    fixture: dict[str, object],
    *,
    workspace_id: str,
    workspace_revision: int,
    payload_sha256: str,
    document_sha256: str,
    version: int,
    phase: int,
    successor_payload_sha256: str | None = None,
    successor_document_sha256: str | None = None,
) -> dict[str, str]:
    _exact_fields(payload, JOURNAL_FIELDS, "Downtime journal")
    owner_id = physical.canonical_guid(payload["OwnerId"], "Downtime owner")
    expected = _expected_downtime_journal(
        fixture, workspace_id=workspace_id, workspace_revision=workspace_revision,
        payload_sha256=payload_sha256, document_sha256=document_sha256,
        owner_id=owner_id, version=version, phase=phase,
        successor_payload_sha256=successor_payload_sha256,
        successor_document_sha256=successor_document_sha256,
    )
    _require_exact(payload, expected, "Downtime journal")
    receipt = expected["Receipt"]
    return {
        "actionId": str(expected["ActionId"]),
        "previewDigest": str(expected["Review"]["Preview"]["PreviewDigest"]),
        "expectedPostconditionDigest": str(expected["ExpectedPostconditionDigest"]),
        "receiptDigest": "" if receipt is None else str(receipt["ReceiptDigest"]),
    }


def _tap_exact(device: physical.shared.Device, selector: str) -> None:
    device.tap_bidirectional(
        selector, timeout=120, backward_scrolls=36, forward_scrolls=36,
        scroll_distance_ratio=0.18, exact_resource_id=True,
    )


def _wait_resource_text(
    device: physical.shared.Device,
    selector: str,
    predicate: object,
    *,
    timeout: int,
) -> str:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        node = device.find_exact_resource_id(selector)
        text = "" if node is None else node.attributes.get("text") or ""
        if callable(predicate) and predicate(text):
            return text
        time.sleep(0.75)
    device.capture(f"{selector}-text-timeout")
    raise RuntimeError(f"Timed out waiting for exact text on {selector}")


def open_downtime(device: physical.shared.Device) -> None:
    physical.shared.open_build(device, "phone")
    physical.shared.reset_scroll_to_top(device, swipes=18)
    device.tap_exact_resource_id_bidirectional(
        "build-sr5-career-wizard",
        timeout=120,
        backward_scrolls=16,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        evidence_prefix="sr5-downtime-career-wizard",
        surface_name="SR5 Career wizard route",
    )
    physical.wait_exact_route(device, "sr5-career", timeout=90)
    device.tap_single_exact_resource_id(
        "sr5-career/calendar", timeout=120,
        evidence_prefix="sr5-downtime-family", surface_name="SR5 Calendar family",
    )
    physical.wait_exact_route(device, "sr5-career/calendar", timeout=90)
    device.tap_single_exact_resource_id(
        "sr5-career-action-calendar", timeout=120,
        evidence_prefix="sr5-downtime-action", surface_name="SR5 Downtime action",
    )
    physical.wait_exact_route(device, ROUTE, timeout=180)


def _same_xml(actual: ET.Element, expected: ET.Element) -> bool:
    if actual.tag != expected.tag or actual.attrib != expected.attrib:
        return False
    if (actual.text or "") != (expected.text or ""):
        return False
    left = list(actual)
    right = list(expected)
    return len(left) == len(right) and all(
        _same_xml(a, b) for a, b in zip(left, right, strict=True)
    )


def assert_calendar(root: ET.Element, fixture: dict[str, object], *, edited: bool) -> None:
    runner = DEFAULT_FIXTURE.parent / str(fixture["runnerFixture"])
    expected = ET.parse(runner).getroot()
    if edited:
        target_id = str(fixture["target"]["weekId"])
        targets = [
            week for week in expected.findall("./calendar/week")
            if week.findtext("guid") == target_id
        ]
        if len(targets) != 1:
            raise RuntimeError("Governed Downtime target is not unique")
        notes = targets[0].find("notes")
        color = targets[0].find("notesColor")
        if notes is None or color is None:
            raise RuntimeError("Governed Downtime target edit fields are missing")
        notes.text = str(fixture["edit"]["notes"])
        color.text = str(fixture["edit"]["notesColor"])
    if not _same_xml(root, expected):
        raise RuntimeError(
            "Downtime successor changed target identity/coordinate, preserved XML, or a non-permitted field"
        )


def require_initial_saved_fixture_authority(
    imported: physical.shared.WorkspaceAuthority,
    saved: physical.shared.WorkspaceAuthority,
    fixture_sha256: str,
) -> None:
    """Prove one explicit Save established the unchanged imported runner at 1/1."""
    physical.shared.require_import_authority(imported, fixture_sha256)
    if imported.content_revision != 1 or imported.saved_revision != 0:
        raise RuntimeError(
            "Downtime import did not expose the exact unsaved revision 1/0 authority"
        )
    physical.shared.require_import_authority(saved, fixture_sha256)
    physical.shared.require_saved_authority(saved)
    if (
        saved.workspace_id != imported.workspace_id
        or saved.content_revision != imported.content_revision
        or saved.saved_revision != imported.content_revision
        or saved.payload_sha256 != imported.payload_sha256
    ):
        raise RuntimeError(
            "Initial Downtime save changed the imported workspace, revision, or fixture bytes"
        )


def prove_downtime(
    device: physical.shared.Device,
    runner: Path,
    runner_sha256: str,
    fixture: dict[str, object],
    proof_expectation: object | None = None,
) -> dict[str, object]:
    device.shell("pm", "clear", physical.shared.PACKAGE)
    initial_launch, imported, import_proof = calendar_leaf.prepare_runner(
        device,
        runner.name,
        runner_sha256,
        proof_expectation,
    )
    initial_saved = physical.shared.save_and_read_workspace_authority(device, "phone")
    require_initial_saved_fixture_authority(imported, initial_saved, runner_sha256)
    assert_calendar(
        calendar_leaf.root_for_authority(device, initial_saved), fixture, edited=False
    )
    physical.shared.record_phone_ui_locale_evidence(device, evidence_prefix="sr5-downtime")
    open_downtime(device)
    _tap_exact(device, "sr5-downtime-calendar-operation")
    device.tap("Edit week", timeout=60)
    target_label = (
        f"{fixture['target']['year']} W{fixture['target']['isoWeek']:02d} · "
        f"{fixture['target']['weekId']}"
    )
    picker = device.wait_for_single_exact_resource_id(
        "sr5-downtime-calendar-week", timeout=60,
        evidence_prefix="sr5-downtime-week", surface_name="Downtime week picker",
    )
    if fixture["target"]["weekId"] not in (picker.attributes.get("text") or ""):
        _tap_exact(device, "sr5-downtime-calendar-week")
        device.tap(target_label, timeout=60)
    device.set_text(
        "sr5-downtime-calendar-notes", "Notes (edit only)", fixture["edit"]["notes"],
        scroll=True, max_scrolls=24, scroll_distance_ratio=0.18,
    )
    device.set_text(
        "sr5-downtime-calendar-notes-color", "Notes color (edit only)",
        fixture["edit"]["notesColor"], scroll=True, max_scrolls=24,
        scroll_distance_ratio=0.18,
    )
    _tap_exact(device, "sr5-downtime-calendar-review")
    reviewed = read_journal(device)
    if reviewed is None:
        raise RuntimeError("Reviewed Downtime journal disappeared")
    review_projection = validate_journal(
        reviewed.payload, fixture, workspace_id=initial_saved.workspace_id,
        workspace_revision=initial_saved.content_revision,
        payload_sha256=initial_saved.payload_sha256,
        document_sha256=initial_saved.document_sha256, version=1, phase=0,
    )
    device.capture("sr5-downtime-durable-review")

    reviewed_restart = physical.shared.force_stop_and_launch_new_process(device, initial_launch)
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    physical.shared.tap_phone_destination(device, "phone-destination-runners")
    physical.shared.wait_for_phone_runners(device, timeout=120)
    restored_before = physical.shared.read_phone_workspace_authority(device)
    physical.shared.require_restored_authority(initial_saved, restored_before)
    if read_journal(device) != reviewed:
        raise RuntimeError("Downtime reviewed journal bytes changed across restart")
    open_downtime(device)
    expected_recovery = "Reviewed preview restored. Confirm it again before saving."
    _wait_resource_text(
        device, "sr5-downtime-calendar-status", lambda text: text == expected_recovery,
        timeout=90,
    )
    _tap_exact(device, "sr5-downtime-calendar-confirm")
    device.tap("Confirm", timeout=60)
    _tap_exact(device, "sr5-downtime-calendar-apply")
    _wait_resource_text(
        device, "sr5-downtime-calendar-receipt",
        lambda text: "verified receipt sha256:" in text, timeout=240,
    )
    applied = read_journal(device)
    if applied is None:
        raise RuntimeError("Applied Downtime journal disappeared")
    device.capture("sr5-downtime-applied-receipt")
    saved = physical.shared.read_phone_workspace_authority(device)
    physical.shared.require_saved_authority(saved)
    if (
        saved.workspace_id != initial_saved.workspace_id
        or saved.content_revision != initial_saved.content_revision + 1
    ):
        raise RuntimeError("Downtime apply did not save one exact successor revision")
    if saved.payload_sha256 == initial_saved.payload_sha256:
        raise RuntimeError("Downtime successor payload digest did not change")
    assert_calendar(calendar_leaf.root_for_authority(device, saved), fixture, edited=True)
    applied_projection = validate_journal(
        applied.payload, fixture, workspace_id=initial_saved.workspace_id,
        workspace_revision=initial_saved.content_revision,
        payload_sha256=initial_saved.payload_sha256,
        document_sha256=initial_saved.document_sha256, version=3, phase=2,
        successor_payload_sha256=saved.payload_sha256,
        successor_document_sha256=saved.document_sha256,
    )
    if {
        key: applied_projection[key]
        for key in ("actionId", "previewDigest", "expectedPostconditionDigest")
    } != {
        key: review_projection[key]
        for key in ("actionId", "previewDigest", "expectedPostconditionDigest")
    }:
        raise RuntimeError("Downtime receipt changed the exact reviewed edit")
    if physical.shared.read_phone_workspace_authority(device) != saved:
        raise RuntimeError("Downtime saved authority changed after receipt navigation")

    applied_restart = physical.shared.force_stop_and_launch_new_process(
        device, reviewed_restart.restarted
    )
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    open_downtime(device)
    if read_journal(device) != applied:
        raise RuntimeError("Downtime receipt journal bytes changed during restart recovery")
    _wait_resource_text(
        device, "sr5-downtime-calendar-receipt",
        lambda text: applied_projection["receiptDigest"][:19] in text,
        timeout=90,
    )
    device.capture("sr5-downtime-recovered-receipt")
    _tap_exact(device, "sr5-downtime-calendar-clear-applied")
    time.sleep(1)
    if read_journal(device, required=False) is not None:
        raise RuntimeError("Acknowledged Downtime receipt journal was not removed")
    expected_ack = "Applied receipt cleared. A new preview may be created."
    _wait_resource_text(
        device, "sr5-downtime-calendar-status", lambda text: text == expected_ack,
        timeout=60,
    )
    physical.shared.tap_phone_destination(device, "phone-destination-runners")
    physical.shared.wait_for_phone_runners(device, timeout=120)
    open_downtime(device)
    if read_journal(device, required=False) is not None:
        raise RuntimeError("Reopened Downtime surface resurrected the acknowledged journal")
    device.capture("sr5-downtime-reopened-after-acknowledgement")

    final_restart = physical.shared.force_stop_and_launch_new_process(
        device, applied_restart.restarted
    )
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    physical.shared.tap_phone_destination(device, "phone-destination-runners")
    physical.shared.wait_for_phone_runners(device, timeout=120)
    final_saved = physical.shared.read_phone_workspace_authority(device)
    physical.shared.require_restored_authority(saved, final_saved)
    assert_calendar(calendar_leaf.root_for_authority(device, final_saved), fixture, edited=True)
    if read_journal(device, required=False) is not None:
        raise RuntimeError("Downtime acknowledgement did not survive final restart")
    result = {
        "import": physical.shared.workspace_authority_json(imported),
        "initialSaved": physical.shared.workspace_authority_json(initial_saved),
        "restoredBeforeApply": physical.shared.workspace_authority_json(restored_before),
        "savedSuccessor": physical.shared.workspace_authority_json(saved),
        "finalRestartSuccessor": physical.shared.workspace_authority_json(final_saved),
        "reviewedJournal": reviewed.payload,
        "reviewedJournalSha256": reviewed.serialized_sha256,
        "appliedJournal": applied.payload,
        "appliedJournalSha256": applied.serialized_sha256,
        "receiptAuthority": applied_projection,
        "restartProcessIds": [
            list(reviewed_restart.restarted.process_ids),
            list(applied_restart.restarted.process_ids),
            list(final_restart.restarted.process_ids),
        ],
    }
    if proof_expectation is not None:
        if import_proof is None:
            raise RuntimeError("API-36 Downtime import proof activation is missing")
        result["api36ImportProof"] = {
            "schema": import_proof.payload["schema"],
            "serializedSha256": import_proof.serialized_sha256,
            "observation": import_proof.payload,
        }
    return result


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(DISPOSABLE_DEVICE_FLAG, action="store_true")
    parser.add_argument("--fixture", type=Path, default=DEFAULT_FIXTURE)
    return parser.parse_args(argv)


def _remote(path: str, purpose: str) -> dict[str, object]:
    return {
        "path": path, "purpose": purpose, "precleanAttempted": False,
        "precleaned": False, "cleanupAttempted": False,
        "cleanupReplaySuppressed": False, "deletedAndVerified": False,
    }


def execute(args: argparse.Namespace, context: dict[str, object]) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{DISPOSABLE_DEVICE_FLAG} is required because the journey installs the APK, "
            "clears app data, imports a governed runner, and saves one calendar edit"
        )
    if physical.SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe ASCII grammar")
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    apk = args.apk.resolve()
    fixture_path = args.fixture.resolve()
    if fixture_path != DEFAULT_FIXTURE.resolve():
        raise RuntimeError("Physical Downtime proof requires the exact committed governed fixture")
    fixture = load_fixture(fixture_path)
    runner = fixture_path.parent / str(fixture["runnerFixture"])
    if runner.is_symlink() or not runner.is_file():
        raise RuntimeError("Governed Downtime runner is missing or not regular")
    runner_sha256 = physical.shared.sha256(runner)
    if runner_sha256 != fixture["runnerFixtureSha256"]:
        raise RuntimeError("Downtime runner bytes differ from governed fixture authority")
    provenance = load_and_verify_manifest(
        args.build_provenance_manifest, android_root=android_root, core_root=core_root,
        presentation_root=presentation_root, apk=apk,
    )
    repositories = _mapping(provenance.get("repositories"), "build repositories")
    artifact = _mapping(provenance.get("artifact"), "build artifact")
    expected_android = str(_mapping(repositories["android"], "android repository")["commit"])
    expected_core = str(_mapping(repositories["core"], "core repository")["commit"])
    expected_presentation = str(
        _mapping(repositories["presentation"], "presentation repository")["commit"]
    )
    expected_apk = str(artifact["sha256"])
    source_paths = {
        "driverSha256": driver, "fixtureSha256": fixture_path,
        "runnerFixtureSha256": runner,
        "physicalHarnessSha256": Path(physical.__file__).resolve(),
        "sharedDeviceHarnessSha256": Path(physical.shared.__file__).resolve(),
        "calendarImportHarnessSha256": Path(calendar_leaf.__file__).resolve(),
        "buildProvenanceVerifierSha256": Path(load_and_verify_manifest.__code__.co_filename).resolve(),
        "careerWizardPageSha256": android_root / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs",
        "downtimePageSha256": android_root / "src/Chummer.Android/Native/Sr5DowntimeCalendarWizardPage.cs",
        "downtimeModelSha256": android_root / "src/Chummer.Android/Native/Sr5DowntimeCalendarPhoneModel.cs",
        "downtimeAuthoritySha256": android_root / "src/Chummer.Android/Native/RunnerSessionSr5DowntimeCalendarAuthority.cs",
        "runnerCoordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Downtime source graph is incomplete: {missing!r}")
    source_before = physical.source_graph_snapshot(
        android_root=android_root, core_root=core_root, presentation_root=presentation_root,
        apk=apk, expected_apk_sha256=expected_apk,
        expected_android_revision=expected_android, expected_core_revision=expected_core,
        expected_presentation_revision=expected_presentation, source_paths=source_paths,
    )
    context.update({
        "releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested",
        "buildProvenance": provenance, "sourceGraphAuthority": source_before,
    })
    device = physical.shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    remote_runner = f"/sdcard/Download/{runner.name}"
    remotes = [
        _remote(remote_runner, "temporary governed Downtime runner"),
        _remote("/sdcard/chummer-editing-window.xml", "temporary UIAutomator hierarchy dump"),
    ]
    context["remoteTemporaryFiles"] = remotes
    errors: list[str] = []
    journey: dict[str, object] | None = None
    observation: dict[str, object] | None = None
    verified_remote = ""
    device_validated = False
    try:
        device.require_transport_stability(expected_api_level="36")
        observation = physical.android_device_observation(device)
        context["deviceObservation"] = observation
        device_validated = True
        for remote in remotes:
            remote["precleanAttempted"] = True
            physical.remove_remote_temporary_file(device, str(remote["path"]))
            remote["precleaned"] = True
        device.install_verified(apk, expected_apk, "--no-streaming", "-r")
        verified_remote = device.push_verified(runner, remote_runner, runner_sha256)
        journey = prove_downtime(device, runner, runner_sha256, fixture)
    except Exception as error:  # noqa: BLE001
        errors.append(f"journey failed: {type(error).__name__}: {error}")
    finally:
        if device_validated:
            for remote in remotes:
                if not physical.shared.authorize_remote_cleanup_once(remote):
                    errors.append(f"remote cleanup replay suppressed for {remote['path']}")
                    continue
                try:
                    physical.remove_remote_temporary_file(device, str(remote["path"]))
                    remote["deletedAndVerified"] = True
                except Exception as error:  # noqa: BLE001
                    errors.append(f"remote cleanup failed for {remote['path']}: {error}")
        context["adbTransport"] = device.transport_summary()
        try:
            source_after = physical.source_graph_snapshot(
                android_root=android_root, core_root=core_root,
                presentation_root=presentation_root, apk=apk,
                expected_apk_sha256=expected_apk,
                expected_android_revision=expected_android,
                expected_core_revision=expected_core,
                expected_presentation_revision=expected_presentation,
                source_paths=source_paths,
            )
            context["postRunSourceGraphAuthority"] = source_after
            if source_after != source_before:
                errors.append("source/APK authority changed during physical execution")
        except Exception as error:  # noqa: BLE001
            errors.append(f"source/APK authority recheck failed: {error}")
    if errors:
        raise RuntimeError("; ".join(errors))
    if journey is None or observation is None or not all(
        remote["deletedAndVerified"] for remote in remotes
    ):
        raise RuntimeError("Downtime journey, device, or cleanup proof is incomplete")
    return {
        "schema": SCHEMA, "status": "device-pass-source-bound", "executionStatus": "pass",
        "releaseEvidenceStatus": context["releaseEvidenceStatus"],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "profile": "phone",
        "journey": "sr5-downtime-calendar-physical", "serial": args.serial,
        "apiLevel": observation["apiLevel"], "abi": observation["abi"],
        "deviceObservation": observation, "buildProvenance": provenance,
        "sourceGraphAuthority": source_before,
        "postRunSourceGraphAuthoritySha256": source_before["authoritySha256"],
        "sourceGraphRecheckedAfterRun": True, "apkSha256": source_before["apkSha256"],
        "apkAbis": source_before["apkAbis"],
        "governedFixtureSha256": source_before["sourceFileSha256"]["fixtureSha256"],
        "careerRunnerSha256": runner_sha256,
        "verifiedRemoteRunnerSha256": verified_remote,
        "remoteTemporaryFiles": remotes, "authorityProofStages": journey,
        "journeys": {
            "exactCalendarEdit": "pass", "durableReview": "pass",
            "reviewRestartAndReconfirm": "pass", "atomicApplyAndReceipt": "pass",
            "receiptRestartRecovery": "pass", "acknowledgeAndReopen": "pass",
            "finalRestartSuccessor": "pass",
        },
    }


def failure_receipt(
    args: argparse.Namespace, error: Exception, context: dict[str, object]
) -> dict[str, object]:
    return {
        "schema": SCHEMA, "status": "fail", "executionStatus": "fail",
        "releaseEvidenceStatus": context.get("releaseEvidenceStatus", "manifest-not-verified"),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone", "journey": "sr5-downtime-calendar-physical",
        "serial": args.serial,
        "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
        **context,
    }


def argument_failure_receipt(exit_code: int) -> dict[str, object]:
    return {
        "schema": SCHEMA, "status": "fail", "executionStatus": "fail",
        "releaseEvidenceStatus": "manifest-not-verified",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone", "journey": "sr5-downtime-calendar-physical",
        "failure": {"type": "ArgumentParseError", "message": f"argparse exited {exit_code}"},
    }


def main(argv: list[str] | None = None) -> int:
    raw_args = list(sys.argv[1:] if argv is None else argv)
    if any(argument in {"-h", "--help"} for argument in raw_args):
        try:
            parse_args(["--help"])
        except SystemExit as error:
            return int(error.code or 0)
        return 0
    try:
        receipt_path = physical.locate_explicit_receipt(raw_args)
        physical.validate_external_output_path(
            receipt_path, label="Receipt path",
            repository_roots=physical.preparse_repository_roots(raw_args),
            expect_directory=False,
        )
        physical.prepare_receipt_target(receipt_path)
    except Exception as error:  # noqa: BLE001
        print(f"Cannot prepare explicit receipt target: {error}", file=sys.stderr)
        return 2
    try:
        args = parse_args(raw_args)
    except SystemExit as error:
        exit_code = int(error.code or 0)
        if exit_code:
            physical.write_receipt_atomically(receipt_path, argument_failure_receipt(exit_code))
        return exit_code
    context: dict[str, object] = {}
    try:
        repositories = physical.source_repository_roots(
            android_root=Path(__file__).resolve().parents[1],
            workspace_root=args.workspace_root.resolve(),
        )
        physical.validate_external_output_path(
            receipt_path, label="Receipt path", repository_roots=repositories,
            expect_directory=False,
        )
        physical.validate_external_output_path(
            args.evidence, label="Evidence path", repository_roots=repositories,
            expect_directory=True,
        )
        physical.validate_output_layout(receipt=receipt_path, evidence=args.evidence)
        receipt = execute(args, context)
    except Exception as error:  # noqa: BLE001
        receipt = failure_receipt(args, error, context)
        physical.write_receipt_atomically(receipt_path, receipt)
        print(f"Physical SR5 Downtime Calendar E2E failed: {error}", file=sys.stderr)
        return 1
    physical.write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
