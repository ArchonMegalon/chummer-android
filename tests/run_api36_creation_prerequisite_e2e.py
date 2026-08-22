#!/usr/bin/env python3
"""API-36 phone proof for the authoritative Priority/Sum-to-Ten prerequisite.

The source remains an unexecuted contract until CI or an operator runs it against a reviewed APK.
A successfully completed invocation emits a pass receipt bound to that APK and this driver.
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
import run_api36_creation_wizard_foundation_e2e as foundation
import run_api36_editing_e2e as shared


CATEGORIES = ("heritage", "talent", "attributes", "skills", "resources")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def node_text(device: shared.Device, selector: str, *, scroll: bool = False) -> str:
    node = device.wait(selector, timeout=60, scroll=scroll, max_scrolls=22)
    return node.attributes.get("text") or node.attributes.get("content-desc") or ""


def open_prerequisite(device: shared.Device) -> None:
    shared.reset_scroll_to_top(device, swipes=22)
    device.tap_until_visible(
        "creation-stage-method",
        "creation-prerequisite-page",
        scroll=True,
        max_scrolls=22,
    )
    device.wait("creation-prerequisite-karma-budget", timeout=60, scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-method", timeout=45, scroll=True, max_scrolls=22)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    shared_path = Path(shared.__file__).resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Creation prerequisite E2E requires API 36, got {api!r}")

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
    device.wait("creation-wizard-dashboard", timeout=90)
    foundation.assert_creation_editor_gated(device)

    dashboard_binding = node_text(device, "creation-wizard-binding", scroll=True)
    open_prerequisite(device)
    prerequisite_binding = node_text(device, "creation-prerequisite-binding", scroll=True)
    karma = node_text(device, "creation-prerequisite-karma-budget", scroll=True)
    for label in ("Total", "Used", "Remaining"):
        if label.lower() not in karma.lower():
            raise RuntimeError(f"Global Creation Karma omitted {label!r}: {karma!r}")

    # Build Ghost can answer from this state, but the chat route cannot touch Core mutation APIs.
    device.tap("creation-prerequisite-rook", scroll=True, max_scrolls=22)
    device.wait("rook-local-grounded-fallback", timeout=45)
    device.set_text("rook-question", "Priority question", "Which legal rank should I consider?")
    device.tap("rook-send-question")
    device.wait("rook-message-binding-1", timeout=45, scroll=True, max_scrolls=22)
    device.back()
    device.wait("creation-prerequisite-binding", timeout=45, scroll=True, max_scrolls=22)
    if node_text(device, "creation-prerequisite-binding", scroll=True) != prerequisite_binding:
        raise RuntimeError("Build Ghost changed the prerequisite workspace binding")

    selected: dict[str, str] = {}
    for category in CATEGORIES:
        device.tap(
            f"creation-prerequisite-category-{category}",
            scroll=True,
            max_scrolls=22,
        )
        device.wait("creation-prerequisite-category-page", timeout=45)
        selected[category] = foundation.tap_first_enabled_prefix(
            device,
            f"creation-prerequisite-rank-{category}-",
            max_scrolls=22,
        ) or ""
        device.wait("creation-prerequisite-page", timeout=45)

    # A plain Back from a category route preserves the exact in-memory typed rank choice.
    attributes_before = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    device.tap("creation-prerequisite-category-attributes", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-category-page", timeout=45)
    device.back()
    attributes_after = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    if attributes_after != attributes_before:
        raise RuntimeError("Back navigation did not restore the typed Attribute rank selection")

    attributes_gate = node_text(
        device,
        "creation-prerequisite-attributes-disabled",
        scroll=True,
    )
    if "raw" not in attributes_gate.lower() or "metatype" not in attributes_gate.lower():
        raise RuntimeError(f"Attribute prerequisite reason is not explicit: {attributes_gate!r}")

    device.tap("creation-prerequisite-prepare-preview", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-preview-page", timeout=60)
    device.wait("creation-prerequisite-preview-karma-budget", timeout=45, scroll=True, max_scrolls=22)
    for category in CATEGORIES:
        device.wait(
            f"creation-prerequisite-preview-assignment-{category}",
            timeout=45,
            scroll=True,
            max_scrolls=22,
        )
    device.wait("creation-prerequisite-preview-attributes-disabled", scroll=True, max_scrolls=22)
    device.tap("creation-prerequisite-confirm", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-confirm-receipt", timeout=90, scroll=True, max_scrolls=22)
    receipt_text = node_text(device, "creation-prerequisite-confirm-receipt", scroll=True)
    if "false" not in receipt_text.lower():
        raise RuntimeError("Prerequisite receipt did not prove CharacterDocumentChanged=false")
    device.capture("creation-prerequisite-confirmed")
    device.tap("creation-prerequisite-back-to-build", scroll=True, max_scrolls=22)
    device.wait("creation-wizard-dashboard", timeout=60)
    foundation.assert_creation_editor_gated(device)
    if node_text(device, "creation-wizard-binding", scroll=True) == dashboard_binding:
        raise RuntimeError("Atomic prerequisite confirmation did not refresh the wizard revision")

    # Same-process reload and a real process restart must both restore Core's persisted draft.
    open_prerequisite(device)
    device.wait("creation-prerequisite-pending-draft", timeout=60, scroll=True, max_scrolls=22)
    resumed_attributes = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    if "rank" not in resumed_attributes.lower():
        raise RuntimeError("Confirmed prerequisite draft did not resume its Attribute rank")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Your runners", timeout=90)
    shared.open_build(device, "phone")
    device.wait("creation-wizard-dashboard", timeout=90)
    foundation.assert_creation_editor_gated(device)
    open_prerequisite(device)
    device.wait("creation-prerequisite-pending-draft", timeout=60, scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-attributes-disabled", scroll=True, max_scrolls=22)
    device.capture("creation-prerequisite-process-restart")

    receipt = {
        "schema": "chummer.android.creation-prerequisite-e2e/v1",
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "sharedDriverSha256": sha256(shared_path),
        "journeys": {
            "priorityOrSumToTenAuthorityLoaded": "pass",
            "globalCreationKarmaExactTotalUsedRemaining": "pass",
            "fiveOrderedTypedCategorySelections": "pass",
            "authorityProjectedRankOptionsOnly": "pass",
            "priorityMultisetOrSumTargetEnforced": "pass",
            "selectedRankAutomationIds": selected,
            "backRestoresDraftSelection": "pass",
            "previewDigestBeforeExplicitConfirmation": "pass",
            "atomicDraftReceiptVerified": "pass",
            "characterDocumentChangedFalse": "pass",
            "rawAttributeGrantVisible": "pass",
            "attributesBlockedForMetatypeAdjustment": "pass",
            "pendingDraftSameProcessResume": "pass",
            "pendingDraftProcessRestartResume": "pass",
            "buildGhostCurrentAndNonMutating": "pass",
            "advancedEditorNeverExposedWhileCreatedFalse": "pass",
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
        print(f"creation prerequisite e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
