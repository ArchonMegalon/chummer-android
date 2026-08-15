#!/usr/bin/env python3
"""Prove the phone metatype-priority workflow on an API 36 emulator."""

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


EXPECTED_XML = {
    "metatypecategory": "Metahuman",
    "metatype": "Elf",
    "prioritymetatype": "A,4",
    "priorityattributes": "C,2",
    "priorityspecial": "B,3",
    "priorityskills": "D,1",
    "priorityresources": "E,0",
    "prioritytalent": "Adept",
    "adept": "True",
    "magician": "False",
    "technomancer": "False",
}


def sha256(path: Path) -> str:
    return shared.sha256(path)


def find_exact(device: shared.Device, selector: str) -> shared.UiNode | None:
    matches: list[shared.UiNode] = []
    for node in device.hierarchy():
        attributes = node.attributes
        resource_id = attributes.get("resource-id", "").rsplit("/", 1)[-1]
        if selector in {
            resource_id,
            attributes.get("content-desc", ""),
        }:
            matches.append(node)
    return next(
        (node for node in matches if node.attributes.get("clickable") == "true"),
        matches[0] if matches else None,
    )


def tap_exact_field(
    device: shared.Device,
    selector: str,
    *,
    scroll: bool,
) -> None:
    deadline = time.monotonic() + 45
    scrolls = 0
    while time.monotonic() < deadline:
        node = find_exact(device, selector)
        if node is not None and device.node_has_tappable_bounds(node):
            x, y = node.center
            device.shell("input", "tap", str(x), str(y))
            return
        if scroll and scrolls < 18:
            device.swipe_up(distance_ratio=0.22)
            scrolls += 1
        time.sleep(0.75)
    device.capture(f"missing-exact-{selector}")
    raise RuntimeError(f"Timed out waiting for exact UI field {selector!r}")


def tap_exact_option(device: shared.Device, option_label: str) -> None:
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        option = next(
            (
                node
                for node in device.hierarchy()
                if node.attributes.get("text") == option_label
                and node.attributes.get("class", "").endswith("CheckedTextView")
            ),
            None,
        )
        if option is not None and device.node_has_tappable_bounds(option):
            x, y = option.center
            device.shell("input", "tap", str(x), str(y))
            return
        time.sleep(0.5)
    device.capture(f"missing-option-{option_label}")
    raise RuntimeError(f"Timed out waiting for exact picker option {option_label!r}")


def select_option(
    device: shared.Device,
    selector: str,
    option_label: str,
    *,
    scroll: bool = True,
) -> None:
    tap_exact_field(device, selector, scroll=scroll)
    tap_exact_option(device, option_label)
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        field = find_exact(device, selector)
        if field is not None and field.attributes.get("text") == option_label:
            time.sleep(0.75)
            return
        time.sleep(0.5)
    device.capture(f"picker-{selector}-not-applied")
    raise RuntimeError(f"Picker {selector!r} did not retain {option_label!r}")


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


def assert_persisted_priority(device: shared.Device) -> None:
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        values = {
            key: character.findtext(key, default="")
            for key in EXPECTED_XML
        }
        observed.append(values)
        if values == EXPECTED_XML:
            return
    device.capture("metatype-priority-not-persisted")
    raise RuntimeError(
        "Phone metatype-priority selections were not durable in the workspace store; "
        f"observed {observed!r}"
    )


def assert_metatype_readback(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=16)
    device.assert_text("Elf", timeout=30)


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
    presentation_root = (
        workspace_root
        / "chummer-presentation"
        / "Chummer.Presentation"
        / "Overview"
    )
    dialog_factory_path = presentation_root / "DesktopDialogFactory.cs"
    dialog_coordinator_path = presentation_root / "DialogCoordinator.cs"
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
        raise RuntimeError(f"Metatype-priority E2E requires API 36, got {api!r}")

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
    device.tap("dialog-action-create-character", scroll=True, max_scrolls=16)
    device.wait("Select Metatype Priority", timeout=60)

    select_option(
        device,
        "dialog-field-newcharactermetatypecategory",
        "Non-human choices",
    )
    select_option(device, "dialog-field-newcharactermetatype", "Elf")
    select_option(device, "dialog-field-newcharacterpriorityheritage", "A")
    select_option(device, "dialog-field-newcharacterpriorityattributes", "C")
    select_option(device, "dialog-field-newcharacterprioritytalent", "B")
    select_option(device, "dialog-field-newcharacterpriorityskills", "D")
    select_option(device, "dialog-field-newcharacterpriorityresources", "E")
    select_option(device, "dialog-field-newcharacterprioritytalentchoice", "Adept")
    device.tap(
        "dialog-action-complete-new-character-workflow",
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait("Continue building", timeout=90)

    assert_persisted_priority(device)
    assert_metatype_readback(device)
    device.capture("phone-metatype-priority-persisted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=90)
    assert_persisted_priority(device)
    assert_metatype_readback(device)
    device.capture("phone-metatype-priority-after-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "new-character-metatype-priority",
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
            "metatypeCategoryEdited": "pass",
            "metatypeEdited": "pass",
            "heritagePriorityEdited": "pass",
            "attributesPriorityEdited": "pass",
            "talentPriorityEdited": "pass",
            "skillsPriorityEdited": "pass",
            "resourcesPriorityEdited": "pass",
            "talentChoiceEdited": "pass",
            "creationCommitCompleted": "pass",
            "metatypeUiReadback": "pass",
            "workspacePriorityPersisted": "pass",
            "processRestartPriorityPersistence": "pass",
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
        print(f"metatype-priority e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
