#!/usr/bin/env python3
"""Exercise phone career attribute actions on an API 36 emulator."""

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
import run_api36_attribute_e2e as creation_attributes
import run_api36_editing_e2e as shared


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def assert_attribute_total(
    device: shared.Device,
    attribute_name: str,
    expected_total: int,
) -> None:
    creation_attributes.open_phone_attribute_section(device)
    token = attribute_name.lower()
    node = device.wait(
        f"attribute-{token}",
        timeout=60,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    description = node.attributes.get("content-desc", "")
    if not description.startswith(f"{attribute_name}. {expected_total} ·"):
        device.capture(f"phone-career-{token}-not-persisted")
        raise RuntimeError(
            f"{attribute_name} did not persist in the phone attribute list; "
            f"expected total {expected_total}, got {description!r}"
        )


def improve_body(device: shared.Device) -> None:
    creation_attributes.open_phone_attribute_section(device)
    device.tap("attribute-body", scroll=True)
    device.wait("attribute-editor-body", timeout=45)
    device.tap("attribute-improve-body", scroll=True)
    time.sleep(2)
    device.back()
    assert_attribute_total(device, "Body", 3)


def burn_edge(device: shared.Device) -> None:
    creation_attributes.open_phone_attribute_section(device)
    device.tap(
        "attribute-edge",
        scroll=True,
        timeout=60,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait("attribute-editor-edge", timeout=45)
    device.tap("attribute-burn-edge", scroll=True)
    device.wait("Burn Edge?", timeout=30)
    device.tap("Burn")
    time.sleep(2)
    device.back()
    assert_attribute_total(device, "Edge", 1)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "career-attribute-e2e.chum5",
    )
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    creation_driver_path = Path(creation_attributes.__file__).resolve()
    shared_driver_path = Path(shared.__file__).resolve()
    fixture_path = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career attribute E2E requires API 36, got {api!r}")

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
    device.push(fixture_path, "/sdcard/Download/career-attribute-e2e.chum5")
    shared.launch_app(device)
    device.wait("Your runners", timeout=90)
    device.tap("home-open-file")
    shared.select_android_document(device, "career-attribute-e2e.chum5")
    device.wait("Continue building", timeout=90)
    shared.open_build(device, "phone")

    improve_body(device)
    burn_edge(device)
    device.capture("career-attribute-actions-persisted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=90)
    shared.open_build(device, "phone")
    assert_attribute_total(device, "Body", 3)
    assert_attribute_total(device, "Edge", 1)
    device.capture("career-attribute-actions-after-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-attributes",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "creationAttributeDriverSha256": sha256(creation_driver_path),
        "sharedDriverSha256": sha256(shared_driver_path),
        "inputFixture": str(fixture_path),
        "inputFixtureSha256": sha256(fixture_path),
        "journeys": {
            "careerRunnerImport": "pass",
            "attributeImprovePersisted": "pass",
            "attributeBurnEdgePersisted": "pass",
            "processRestartCareerAttributePersistence": "pass",
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
        print(f"career attribute e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
