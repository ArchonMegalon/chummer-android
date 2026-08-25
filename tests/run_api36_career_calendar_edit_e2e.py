#!/usr/bin/env python3
"""Prove atomic Career Calendar add/edit/delete on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "cmdAddWeek",
    "cmdDeleteWeek",
    "cmdEditWeek",
    "cmdChangeStartWeek",
    "lstCalendar",
    "SelectCalendarStart.nudYear",
    "SelectCalendarStart.nudWeek",
    "SelectCalendarStart.cmdOK",
)
LATEST_ID = "11111111-1111-1111-1111-111111111111"
EARLIER_ID = "22222222-2222-2222-2222-222222222222"
CONTROL_PROOF_KEYS = (
    "stableWeekGuid",
    "exactIsoWeekProgression",
    "firstWeekSelectorBounds",
    "notesAndColorEditable",
    "deleteConfirmation",
    "changeStartFailClosed",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "savedPayloadDigestBound",
    "surfaceReopened",
    "twoProcessRestarts",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(
    device: shared.Device,
    fixture_name: str,
    fixture_sha256: str,
) -> tuple[shared.LaunchState, shared.WorkspaceAuthority]:
    launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("CareerCalendarEditE2E", timeout=120)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    authority = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-calendar",
        scroll=True,
        timeout=120,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    device.wait("career-calendar-page", timeout=60)


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def root_for_authority(
    device: shared.Device,
    authority: shared.WorkspaceAuthority,
) -> ET.Element:
    matches = [
        payload
        for payload in workspace_payloads(device)
        if hashlib.sha256(payload.encode("utf-8")).hexdigest() == authority.payload_sha256
    ]
    if len(matches) != 1:
        device.capture("career-calendar-authority-payload-ambiguous")
        raise RuntimeError(
            "Expected one exact calendar payload bound to workspace authority, "
            f"got {len(matches)}"
        )
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != "CareerCalendarEditE2E":
        raise RuntimeError("The authority digest selected a different runner payload")
    return root


def calendar_weeks(root: ET.Element) -> list[ET.Element]:
    return root.findall("./calendar/week")


def assert_siblings(root: ET.Element) -> None:
    if root.findtext("karma") != "10" or root.findtext("nuyen") != "1000":
        raise RuntimeError("Calendar mutation changed career balances")
    if root.findtext("./customstate/calendar/week/sentinel") != "nested-calendar-must-survive":
        raise RuntimeError("Calendar mutation selected or changed a nested calendar")
    sentinel = root.find("./customstate/sentinel")
    if (
        sentinel is None
        or sentinel.get("guid") != "nested-sentinel"
        or sentinel.text != "keep-nested-structure"
    ):
        raise RuntimeError("Calendar mutation changed unrelated XML")


def week_by_id(root: ET.Element, week_id: str) -> ET.Element:
    matches = [week for week in calendar_weeks(root) if week.findtext("guid") == week_id]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one calendar week {week_id}, got {len(matches)}")
    return matches[0]


def assert_originals(root: ET.Element, edited: bool) -> None:
    latest = week_by_id(root, LATEST_ID)
    earlier = week_by_id(root, EARLIER_ID)
    if latest.findtext("year") != "2081" or latest.findtext("week") != "12":
        raise RuntimeError("Latest stable week coordinate changed")
    expected_notes = "After-run complete" if edited else "Run night"
    expected_color = "Chocolate" if edited else "#A52A2A"
    if latest.findtext("notes") != expected_notes or latest.findtext("notesColor") != expected_color:
        raise RuntimeError("Latest week notes/color do not match the expected mutation")
    if latest.findtext("custom") != "keep-latest":
        raise RuntimeError("Latest week custom XML changed")
    if (
        earlier.findtext("year") != "2081"
        or earlier.findtext("week") != "11"
        or earlier.findtext("notes") != "Legwork"
        or earlier.find("notesColor") is not None
        or earlier.findtext("custom") != "keep-earlier"
    ):
        raise RuntimeError("Non-target calendar week changed")
    assert_siblings(root)


def read_saved_authority(device: shared.Device) -> shared.WorkspaceAuthority:
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    authority = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(authority)
    return authority


def require_atomic_transition(
    before: shared.WorkspaceAuthority,
    after: shared.WorkspaceAuthority,
) -> None:
    if after.workspace_id != before.workspace_id:
        raise RuntimeError("Calendar save changed workspace identity")
    if after.content_revision != before.content_revision + 1:
        raise RuntimeError(
            "Calendar save did not apply exactly one revision: "
            f"before={before.content_revision}, after={after.content_revision}"
        )
    shared.require_saved_authority(after)
    if after.payload_sha256 == before.payload_sha256:
        raise RuntimeError("Calendar save did not change the authority payload digest")


def return_home_from_page(device: shared.Device) -> None:
    device.back()
    device.wait("build-career-calendar", timeout=90, scroll=True, max_scrolls=36)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)


def assert_ui_readback(device: shared.Device) -> None:
    notes = device.wait("career-calendar-notes", timeout=45, scroll=True, max_scrolls=10)
    color = device.wait("career-calendar-notes-color", timeout=45, scroll=True, max_scrolls=10)
    if notes.attributes.get("text") != "After-run complete":
        raise RuntimeError("Calendar notes were not read back after reopen")
    if color.attributes.get("text") != "Chocolate":
        raise RuntimeError("Calendar notes color was not read back after reopen")
    disabled = device.wait(
        "career-calendar-change-start-disabled",
        timeout=45,
        scroll=True,
        max_scrolls=14,
    )
    if disabled.attributes.get("enabled") != "false":
        raise RuntimeError("Pinned Chummer5 Change Starting Date defect was not fail-closed")


def prove_calendar_crud(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture.name, fixture_sha256)
    open_page(device)
    device.set_text(
        "career-calendar-notes",
        "Selected week notes",
        "After-run complete",
        scroll=True,
    )
    device.set_text(
        "career-calendar-notes-color",
        "Notes color (name or #RRGGBB)",
        "Chocolate",
        scroll=True,
    )
    device.tap("career-calendar-save", scroll=True, timeout=180, max_scrolls=16)
    device.wait("build-career-calendar", timeout=180, scroll=True, max_scrolls=36)
    edited = read_saved_authority(device)
    require_atomic_transition(imported, edited)
    edited_root = root_for_authority(device, edited)
    assert_originals(edited_root, edited=True)

    open_page(device)
    assert_ui_readback(device)
    return_home_from_page(device)
    open_page(device)
    device.tap("career-calendar-add", scroll=True, timeout=180, max_scrolls=14)
    device.wait("build-career-calendar", timeout=180, scroll=True, max_scrolls=36)
    added = read_saved_authority(device)
    require_atomic_transition(edited, added)
    added_root = root_for_authority(device, added)
    assert_originals(added_root, edited=True)
    new_weeks = [
        week
        for week in calendar_weeks(added_root)
        if week.findtext("guid") not in {LATEST_ID, EARLIER_ID}
    ]
    if len(new_weeks) != 1:
        raise RuntimeError("Calendar add did not create exactly one new stable week")
    new_id = new_weeks[0].findtext("guid") or ""
    uuid.UUID(new_id)
    if (
        new_weeks[0].findtext("year") != "2081"
        or new_weeks[0].findtext("week") != "13"
        or new_weeks[0].findtext("notes") != ""
        or new_weeks[0].findtext("notesColor") != "Chocolate"
    ):
        raise RuntimeError("Calendar add did not use the exact next ISO week defaults")

    open_page(device)
    device.tap("career-calendar-delete", scroll=True, timeout=60, max_scrolls=14)
    device.tap("Cancel", timeout=60)
    device.wait("career-calendar-page", timeout=30)
    device.tap("career-calendar-delete", scroll=True, timeout=60, max_scrolls=14)
    device.tap("Delete", timeout=60)
    device.wait("build-career-calendar", timeout=180, scroll=True, max_scrolls=36)
    deleted = read_saved_authority(device)
    require_atomic_transition(added, deleted)
    deleted_root = root_for_authority(device, deleted)
    assert_originals(deleted_root, edited=True)
    if len(calendar_weeks(deleted_root)) != 2:
        raise RuntimeError("Confirmed delete did not remove exactly the added week")

    open_page(device)
    assert_ui_readback(device)
    device.capture("career-calendar-same-session-reopen")
    return_home_from_page(device)

    first_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    first_restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(deleted, first_restored)
    assert_originals(root_for_authority(device, first_restored), edited=True)
    open_page(device)
    assert_ui_readback(device)
    device.capture("career-calendar-first-process-restart")
    return_home_from_page(device)

    second_restart = shared.force_stop_and_launch_new_process(device, first_restart.restarted)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    second_restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(deleted, second_restored)
    assert_originals(root_for_authority(device, second_restored), edited=True)
    open_page(device)
    assert_ui_readback(device)
    device.capture("career-calendar-second-process-restart")
    return {
        "import": shared.workspace_authority_json(imported),
        "edited": shared.workspace_authority_json(edited),
        "added": shared.workspace_authority_json(added),
        "deleted": shared.workspace_authority_json(deleted),
        "firstRestored": shared.workspace_authority_json(first_restored),
        "secondRestored": shared.workspace_authority_json(second_restored),
        "generatedWeekGuid": new_id,
        "restartProcessIds": [
            list(first_restart.restarted.process_ids),
            list(second_restart.restarted.process_ids),
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures/career-calendar-edit-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerCalendarPageSha256": android_root
        / "src/Chummer.Android/Native/CareerCalendarPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerCalendarContractSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerCalendarEditRequest.cs",
        "careerCalendarMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerCalendarMutation.cs",
        "mutationCatalogSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "careerCalendarRulesSha256": workspace_root
        / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerCalendarRules.cs",
        "workspaceStoreSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Career Calendar source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    fixture_sha256 = shared.sha256(fixture)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career Calendar E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(f"Career Calendar E2E requires the hosted x86_64 phone lane, got {abi!r}")
    subprocess.run(
        [
            str(args.adb),
            "-s",
            args.serial,
            "install",
            "--no-streaming",
            "-r",
            str(args.apk.resolve()),
        ],
        check=True,
        timeout=300,
    )
    verified_remote_fixture_sha256 = device.push_verified(
        fixture,
        f"/sdcard/Download/{fixture.name}",
        fixture_sha256,
    )
    journey = prove_calendar_crud(device, fixture, fixture_sha256)
    controls = {
        f"CharacterCareer.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-calendar-edit",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote_fixture_sha256,
        "controlCount": len(controls),
        "controls": controls,
        "authorityProofStages": journey,
        "journeys": {
            "editNotesAndColor": "pass",
            "addExactNextIsoWeek": "pass",
            "cancelThenConfirmDelete": "pass",
            "sameSessionReopen": "pass",
            "twoProcessRestarts": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Career Calendar E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
