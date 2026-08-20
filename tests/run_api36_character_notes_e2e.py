#!/usr/bin/env python3
"""Prove all Chummer5 character-note fields on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CASES: dict[str, dict[str, tuple[str, str, str]]] = {
    "CharacterCreate": {
        "rtfNotes": ("notes", "character-notes-editor", "CreateCharacterNotesE2E"),
        "txtGroupNotes": ("groupnotes", "character-group-notes-editor", "CreateGroupNotesE2E"),
    },
    "CharacterCareer": {
        "rtfNotes": ("notes", "character-notes-editor", "CareerCharacterNotesE2E"),
        "rtfGameNotes": ("gamenotes", "character-game-notes-editor", "CareerGameNotesE2E"),
        "txtGroupNotes": ("groupnotes", "character-group-notes-editor", "CareerGroupNotesE2E"),
    },
}
FIELD_LABELS = {
    "notes": "Character notes",
    "gamenotes": "Game notes",
    "groupnotes": "Group notes",
}
CONTROL_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)


def open_character_notes(device: shared.Device, form_name: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-character-notes",
        scroll=True,
        timeout=60,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait("character-notes-editor", timeout=45)
    device.wait(
        "character-group-notes-editor",
        timeout=45,
        scroll=True,
        max_scrolls=16,
        scroll_distance_ratio=0.22,
    )
    if form_name == "CharacterCareer":
        shared.reset_scroll_to_top(device, swipes=12)
        device.wait(
            "character-game-notes-editor",
            timeout=45,
            scroll=True,
            max_scrolls=12,
            scroll_distance_ratio=0.22,
        )


def expected_xml(form_name: str) -> dict[str, str]:
    return {
        element: value
        for element, _selector, value in CASES[form_name].values()
    }


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run(
                "exec-out",
                "run-as",
                shared.PACKAGE,
                "cat",
                path,
            ).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def assert_workspace_notes(device: shared.Device, form_name: str) -> None:
    wanted = expected_xml(form_name)
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        values = {
            element: character.findtext(element, default="")
            for element in wanted
        }
        observed.append(values)
        if values == wanted:
            return
    device.capture(f"{form_name.lower()}-notes-workspace-not-persisted")
    raise RuntimeError(
        f"{form_name} notes were not durable in workspace XML; observed {observed!r}"
    )


def assert_notes_ui(device: shared.Device, form_name: str) -> None:
    shared.reset_scroll_to_top(device, swipes=12)
    for element, selector, wanted in CASES[form_name].values():
        node = device.wait(
            selector,
            timeout=60,
            scroll=True,
            max_scrolls=24,
            scroll_distance_ratio=0.22,
        )
        actual = node.attributes.get("text", "")
        if actual != wanted:
            device.capture(f"{form_name.lower()}-{element}-value-mismatch")
            raise RuntimeError(
                f"{form_name} {element} did not persist: expected {wanted!r}, got {actual!r}"
            )


def edit_notes(device: shared.Device, form_name: str) -> None:
    shared.reset_scroll_to_top(device, swipes=12)
    for element, selector, value in CASES[form_name].values():
        device.set_text(
            selector,
            FIELD_LABELS[element],
            value,
            scroll=True,
            max_scrolls=24,
            scroll_distance_ratio=0.22,
        )
    device.tap(
        "character-notes-save",
        scroll=True,
        timeout=240,
        max_scrolls=32,
        scroll_distance_ratio=0.22,
    )
    device.wait(
        "build-character-notes",
        timeout=90,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )


def prepare_creation_runner(device: shared.Device) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True, max_scrolls=16)
    device.wait(
        "dialog-action-complete-new-character-workflow",
        timeout=60,
        scroll=True,
        max_scrolls=16,
    )
    device.tap(
        "dialog-action-complete-new-character-workflow",
        scroll=True,
        max_scrolls=16,
    )
    device.wait("Continue building", timeout=120)


def prepare_career_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def exercise_case(
    device: shared.Device,
    form_name: str,
    fixture_name: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    if form_name == "CharacterCreate":
        prepare_creation_runner(device)
    else:
        prepare_career_runner(device, fixture_name)

    open_character_notes(device, form_name)
    edit_notes(device, form_name)
    assert_workspace_notes(device, form_name)

    open_character_notes(device, form_name)
    assert_notes_ui(device, form_name)
    device.capture(f"character-notes-{form_name.lower()}-after-reopen")
    device.back()
    device.back()

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_notes(device, form_name)
    open_character_notes(device, form_name)
    assert_notes_ui(device, form_name)
    device.capture(f"character-notes-{form_name.lower()}-after-process-restart")


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
        default=Path(__file__).resolve().parent / "fixtures" / "career-condition-monitor-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "notesPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "CharacterNotesPage.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "updateWorkspaceMetadataContractSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Workspaces" / "CharacterWorkspaceModels.cs",
        "profileContractSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterFileServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterFileService.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "presentationPersistenceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Character-notes E2E source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Character-notes E2E requires API 36, got {api!r}")

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
    device.push(fixture, f"/sdcard/Download/{fixture.name}")

    for form_name in CASES:
        print(
            f"character-notes e2e: exercising {form_name} ({len(CASES[form_name])} fields)",
            flush=True,
        )
        exercise_case(device, form_name, fixture.name)

    control_proofs = {
        f"{form_name}.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for form_name, controls in CASES.items()
        for control in controls
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "character-notes",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixture": str(fixture),
        "careerFixtureSha256": shared.sha256(fixture),
        "controlCount": len(control_proofs),
        "controls": control_proofs,
        "journeys": {
            "newRunner": "pass",
            "characterNotesEditPersisted": "pass",
            "allCreationNotesEdited": "pass",
            "creationWorkspaceXmlPersisted": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerRunnerImported": "pass",
            "allCareerNotesEdited": "pass",
            "careerWorkspaceXmlPersisted": "pass",
            "careerProcessRestartUiReadback": "pass",
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
        print(f"character-notes E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
