#!/usr/bin/env python3
"""Scripted phone proof for wizard routing and local non-mutating Rook chat.

This driver is intentionally committed without being executed in this change. It requires an
operator-provided, already-booted API 36 target and a reviewed APK.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


SCRIPT_STATUS = "scripted_not_executed"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def node_text(device: shared.Device, selector: str, *, scroll: bool = False) -> str:
    node = device.wait(selector, timeout=60, scroll=scroll, max_scrolls=18)
    return node.attributes.get("text") or node.attributes.get("content-desc") or ""


def assert_absent(device: shared.Device, selector: str) -> None:
    if device.find(selector) is not None:
        device.capture(f"wizard-forbidden-{selector}")
        raise RuntimeError(f"Creation wizard exposed forbidden control {selector!r}")


def assert_same_binding(before: str, after: str) -> None:
    if not before or before != after:
        raise RuntimeError(
            "Local Rook chat changed the wizard workspace binding; "
            f"before={before!r}, after={after!r}"
        )


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
        raise RuntimeError(f"Creation wizard E2E requires API 36, got {api!r}")

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
    device.tap("dialog-action-create-character", scroll=True)
    device.wait("dialog-action-complete-new-character-workflow", timeout=45, scroll=True)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True)

    # The completed setup must hand off directly; this driver never taps Continue building.
    device.wait("creation-wizard-dashboard", timeout=90)
    device.wait("creation-stage-basics", timeout=60, scroll=True, max_scrolls=18)
    shared.reset_scroll_to_top(device, swipes=18)
    binding_before = node_text(device, "creation-wizard-binding", scroll=True)
    assert_absent(device, "build-free-sprite-conversion")
    assert_absent(device, "build-origin-dossier")
    assert_absent(device, "Actions")

    shared.reset_scroll_to_top(device, swipes=18)
    device.tap("creation-wizard-rook", scroll=True)
    device.wait("rook-local-grounded-fallback", timeout=45)
    device.set_text("rook-question", "Follow-up question", "What can I do next?")
    device.tap("rook-send-question")
    assistant_binding = node_text(device, "rook-message-binding-1", scroll=True)
    if "stale" in assistant_binding.lower():
        raise RuntimeError("A fresh local Rook answer was immediately marked stale")
    device.back()

    device.wait("creation-wizard-dashboard", timeout=45)
    binding_after = node_text(device, "creation-wizard-binding", scroll=True)
    assert_same_binding(binding_before, binding_after)

    # Reopening proves that the workspace-scoped thread survives page visits.
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap("creation-wizard-rook", scroll=True)
    persisted_binding = node_text(device, "rook-message-binding-1", scroll=True)
    if persisted_binding != assistant_binding:
        raise RuntimeError("Rook transcript did not survive leaving and reopening the page")
    device.capture("creation-wizard-rook-local-thread")

    receipt = {
        "schema": "chummer.android.creation-wizard-foundation-e2e/v1",
        "status": "scripted_not_executed",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "sharedDriverSha256": sha256(shared_driver_path),
        "journeys": {
            "authoritativeUncreatedProfileRoutesDirectlyToWizard": "pass",
            "exhaustiveCreationActionsHidden": "pass",
            "rookLocalFallbackVisible": "pass",
            "rookTranscriptSurvivesPageVisits": "pass",
            "rookQuestionDoesNotChangeRevisionOrSnapshotBinding": "pass",
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
        print(f"creation wizard foundation e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
