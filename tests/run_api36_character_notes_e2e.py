#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys

from run_api36_editing_e2e import Device, PACKAGE, launch_app, open_build, sha256


NOTE_VALUE = "NativeNotesE2E"


def open_character_notes(device: Device) -> None:
    device.tap(
        "build-character-notes",
        scroll=True,
        timeout=60,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait("character-notes-editor", timeout=45)


def assert_character_notes(device: Device, expected: str) -> None:
    actual = device.wait("character-notes-editor", timeout=45).attributes.get("text", "")
    if actual != expected:
        device.capture("character-notes-value-mismatch")
        raise RuntimeError(
            f"Character notes did not persist: expected {expected!r}, got {actual!r}"
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", default="emulator-5554")
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    shared_driver = driver.with_name("run_api36_editing_e2e.py")
    device = Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Character-notes E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-incremental", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.shell("pm", "clear", PACKAGE)
    launch_app(device)
    device.wait("Your runners", timeout=90)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True)
    device.wait("dialog-action-complete-new-character-workflow", timeout=45, scroll=True)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True)
    device.wait("Continue building", timeout=90)

    open_build(device, "phone")
    open_character_notes(device)
    device.set_text("character-notes-editor", "Character notes", NOTE_VALUE)
    device.tap("character-notes-save", scroll=True)
    device.wait("build-character-notes", timeout=45, scroll=True)

    open_character_notes(device)
    assert_character_notes(device, NOTE_VALUE)
    device.capture("character-notes-after-reopen")
    device.back()

    device.shell("am", "force-stop", PACKAGE)
    launch_app(device)
    device.wait("Continue building", timeout=90)
    open_build(device, "phone")
    open_character_notes(device)
    assert_character_notes(device, NOTE_VALUE)
    device.capture("character-notes-after-process-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "character-notes",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver),
        "sharedDriverSha256": sha256(shared_driver),
        "journeys": {
            "newRunner": "pass",
            "characterNotesEditPersisted": "pass",
            "characterNotesReopenReadback": "pass",
            "processRestartCharacterNotesPersistence": "pass",
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
