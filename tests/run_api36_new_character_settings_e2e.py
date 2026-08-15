#!/usr/bin/env python3
"""Prove phone build-setting selection on an already-booted API 36 emulator."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


CHARACTER_SETTING = "Street Rules"


def sha256(path: Path) -> str:
    return shared.sha256(path)


def set_ignore_rules(device: shared.Device) -> None:
    selector = "dialog-field-newcharacterignorerules"
    toggle = device.wait(selector, timeout=45, scroll=True, max_scrolls=12)
    if toggle.attributes.get("checked") != "true":
        device.tap(selector, scroll=True, max_scrolls=12)
        time.sleep(1)
    applied = device.find(selector)
    if applied is None or applied.attributes.get("checked") != "true":
        device.capture("ignore-rules-toggle-not-applied")
        raise RuntimeError("Ignore Character Creation Rules did not remain enabled")


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
        if not isinstance(payload, str) or not payload.strip().startswith("<"):
            continue
        payloads.append(payload)
    return payloads


def assert_persisted_build_settings(device: shared.Device) -> None:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        setting = character.findtext("settings", default="")
        ignore_rules = character.findtext("ignorerules", default="")
        observed.append((setting, ignore_rules))
        if setting == CHARACTER_SETTING and ignore_rules == "True":
            return
    device.capture("build-settings-not-persisted")
    raise RuntimeError(
        "Phone build settings were not durable in the workspace store; "
        f"observed {observed!r}"
    )


def assert_setting_readback(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=16)
    device.assert_text(CHARACTER_SETTING, timeout=30)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    shared_driver_path = Path(shared.__file__).resolve()
    workspace_root = driver_path.parents[2]
    dialog_factory_path = (
        workspace_root
        / "chummer-presentation"
        / "Chummer.Presentation"
        / "Overview"
        / "DesktopDialogFactory.cs"
    )
    dialog_coordinator_path = dialog_factory_path.with_name("DialogCoordinator.cs")
    native_dialog_path = (
        workspace_root
        / "chummer-android"
        / "src"
        / "Chummer.Android"
        / "Native"
        / "NativeDialogPage.cs"
    )
    build_page_path = native_dialog_path.with_name("BuildPage.cs")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Build-setting E2E requires API 36, got {api!r}")

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
    device.shell("pm", "clear", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Your runners", timeout=90)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.set_text(
        "dialog-field-newcharactersetting",
        "Character Setting",
        CHARACTER_SETTING,
        scroll=True,
        max_scrolls=12,
        scroll_distance_ratio=0.22,
    )
    set_ignore_rules(device)
    device.tap("dialog-action-create-character", scroll=True, max_scrolls=16)
    device.wait("dialog-action-complete-new-character-workflow", timeout=60, scroll=True, max_scrolls=16)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True, max_scrolls=16)
    device.wait("Continue building", timeout=90)

    assert_persisted_build_settings(device)
    assert_setting_readback(device)
    device.capture("phone-build-settings-persisted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=90)
    assert_persisted_build_settings(device)
    assert_setting_readback(device)
    device.capture("phone-build-settings-after-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "new-character-build-settings",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "sharedDriverSha256": sha256(shared_driver_path),
        "dialogFactorySha256": sha256(dialog_factory_path),
        "dialogCoordinatorSha256": sha256(dialog_coordinator_path),
        "nativeDialogPageSha256": sha256(native_dialog_path),
        "buildPageSha256": sha256(build_page_path),
        "journeys": {
            "characterSettingEdited": "pass",
            "ignoreCreationRulesEnabled": "pass",
            "creationCommitCompleted": "pass",
            "characterSettingUiReadback": "pass",
            "workspaceBuildSettingsPersisted": "pass",
            "processRestartBuildSettingsPersistence": "pass",
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
        print(f"build-setting e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
