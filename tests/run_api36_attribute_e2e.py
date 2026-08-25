#!/usr/bin/env python3
"""Exercise phone attribute editing on an already-booted API 36 emulator."""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def open_phone_attribute_section(device: shared.Device) -> None:
    # Build navigation can restore the last nested phone route directly. In
    # that case the overview's section selector is intentionally absent.
    if any(
        device.find(selector) is not None
        for selector in ("attribute-body", "attribute-reaction", "attribute-strength")
    ):
        shared.reset_scroll_to_top(device, swipes=12)
        device.wait("attribute-body", timeout=45, scroll=True, max_scrolls=16)
        return
    shared.open_attribute_section(device, "phone")


def edit_body_values(
    device: shared.Device,
    *,
    base_value: int,
    karma_value: int,
) -> None:
    open_phone_attribute_section(device)
    device.tap("attribute-body", scroll=True)
    device.tap("attribute-base-body", scroll=True)
    device.tap(str(base_value), scroll=True)
    device.tap("attribute-karma-body", scroll=True)
    device.tap(str(karma_value), scroll=True)
    device.tap("attribute-save-body", scroll=True)
    time.sleep(2)
    device.back()


def assert_body_values(
    device: shared.Device,
    *,
    expected_base: int,
    expected_karma: int,
) -> None:
    open_phone_attribute_section(device)
    device.tap("attribute-body", scroll=True)

    expected = {
        "Base": ("attribute-base-body", expected_base),
        "Karma": ("attribute-karma-body", expected_karma),
    }
    for label, (selector, value) in expected.items():
        node = device.find(selector, field_after_label=label)
        actual = None if node is None else node.attributes.get("text")
        if actual != str(value):
            device.capture(f"phone-attribute-{label.lower()}-not-persisted")
            raise RuntimeError(
                f"Body {label.lower()} did not persist in the phone native editor; "
                f"expected {value}, got {actual!r}"
            )

    device.back()


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
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Attribute E2E requires API 36, got {api!r}")

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
    shared.wait_for_phone_runners(device, timeout=90)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True)
    device.wait("dialog-action-complete-new-character-workflow", timeout=45, scroll=True)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True)
    shared.wait_for_phone_runner_route(device, timeout=90)

    shared.open_build(device, "phone")
    edit_body_values(device, base_value=2, karma_value=1)
    assert_body_values(device, expected_base=2, expected_karma=1)
    device.back()
    device.capture("attribute-values-persisted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=90)
    shared.open_build(device, "phone")
    assert_body_values(device, expected_base=2, expected_karma=1)
    device.capture("attribute-values-after-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "attributes",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "sharedDriverSha256": sha256(shared_driver_path),
        "journeys": {
            "newRunner": "pass",
            "attributeBaseEditPersisted": "pass",
            "attributeKarmaEditPersisted": "pass",
            "processRestartAttributePersistence": "pass",
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
        print(f"attribute e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
