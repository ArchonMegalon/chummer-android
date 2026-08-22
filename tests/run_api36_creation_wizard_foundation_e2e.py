#!/usr/bin/env python3
"""Scripted phone proof for wizard routing and durable local non-mutating Rook chat.

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


def tap_first_enabled_prefix(
    device: shared.Device,
    prefix: str,
    *,
    required: bool = True,
    max_scrolls: int = 18,
) -> str | None:
    for scroll_index in range(max_scrolls + 1):
        for node in device.hierarchy():
            if (
                shared.Device._matches(node, prefix)
                and node.attributes.get("enabled") == "true"
                and node.attributes.get("clickable") == "true"
                and device.node_has_tappable_bounds(node)
            ):
                resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                x, y = node.center
                device.shell("input", "tap", str(x), str(y))
                return resource_id or prefix
        if scroll_index < max_scrolls:
            device.swipe_up()
    if required:
        device.capture(f"missing-enabled-{prefix.rstrip('-')}")
        raise RuntimeError(f"No enabled authoritative option matched {prefix!r}")
    return None


def select_projected_follow_ups(device: shared.Device) -> list[str]:
    """Select the first exact enabled value for every rendered select prompt."""
    selected_prompts: set[str] = set()
    shared.reset_scroll_to_top(device, swipes=18)
    scroll_index = 0
    while scroll_index <= 22:
        candidate = None
        candidate_prompt = None
        for node in device.hierarchy():
            resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            if (
                resource_id.startswith("creation-foundation-follow-up-")
                and "-option-" in resource_id
                and node.attributes.get("enabled") == "true"
                and node.attributes.get("clickable") == "true"
                and device.node_has_tappable_bounds(node)
            ):
                prompt_id = resource_id.split("-option-", 1)[0]
                if prompt_id not in selected_prompts:
                    candidate = node
                    candidate_prompt = prompt_id
                    break
        if candidate is not None and candidate_prompt is not None:
            x, y = candidate.center
            device.shell("input", "tap", str(x), str(y))
            selected_prompts.add(candidate_prompt)
            shared.reset_scroll_to_top(device, swipes=18)
            scroll_index = 0
            continue
        if device.find("creation-foundation-prepare-preview") is not None:
            break
        device.swipe_up()
        scroll_index += 1
    return sorted(selected_prompts)


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

    # Foundation and Nationality remain a phone-only, authority-rendered route. The driver uses
    # only enabled option IDs present in the rendered projection and never supplies defaults.
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap_until_visible(
        "creation-stage-foundation",
        "creation-foundation-page",
        scroll=True,
        max_scrolls=18,
    )
    device.wait("creation-foundation-budget", timeout=60, scroll=True, max_scrolls=18)
    shared.reset_scroll_to_top(device, swipes=18)
    metatype_option = tap_first_enabled_prefix(device, "creation-foundation-metatype-")
    nationality_option = tap_first_enabled_prefix(device, "creation-foundation-nationality-")
    shared.reset_scroll_to_top(device, swipes=18)
    version_option = tap_first_enabled_prefix(
        device,
        "creation-foundation-version-",
        required=False,
    )
    selected_follow_ups = select_projected_follow_ups(device)
    device.tap("creation-foundation-prepare-preview", scroll=True, max_scrolls=22)
    device.wait("creation-foundation-preview-page", timeout=60)
    device.wait("creation-foundation-preview-budget", timeout=45, scroll=True, max_scrolls=18)
    device.wait("creation-foundation-preview-diff-", timeout=45, scroll=True, max_scrolls=22)
    effects_before = node_text(
        device,
        "creation-foundation-character-effects-applied",
        scroll=True,
    )
    if "false" not in effects_before.lower():
        raise RuntimeError(f"Foundation preview claimed effects were applied: {effects_before!r}")
    device.tap("creation-foundation-confirm", scroll=True, max_scrolls=22)
    device.wait("creation-foundation-confirm-receipt", timeout=90, scroll=True, max_scrolls=22)
    compilation = node_text(device, "creation-foundation-compilation-status", scroll=True)
    if "pending" not in compilation.lower():
        raise RuntimeError(f"Confirmed Foundation draft was not compilation-pending: {compilation!r}")
    effects_after = node_text(
        device,
        "creation-foundation-character-effects-applied",
        scroll=True,
    )
    if "false" not in effects_after.lower():
        raise RuntimeError(f"Foundation confirmation claimed effects were applied: {effects_after!r}")
    device.tap("creation-foundation-save", scroll=True, max_scrolls=22)
    device.wait("creation-foundation-save", timeout=45, scroll=True, max_scrolls=22)
    device.capture("creation-foundation-confirmed-draft")
    device.tap("creation-foundation-back-to-build", scroll=True, max_scrolls=22)
    device.wait("creation-wizard-dashboard", timeout=60)
    binding_after_foundation = node_text(device, "creation-wizard-binding", scroll=True)
    if binding_after_foundation == binding_before:
        raise RuntimeError("Foundation confirmation did not advance the authoritative wizard binding")

    # Reopening in the same process proves the coordinator refreshed the revision-bound overview.
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap_until_visible(
        "creation-stage-foundation",
        "creation-foundation-page",
        scroll=True,
        max_scrolls=18,
    )
    device.wait("creation-foundation-pending-draft", timeout=60, scroll=True, max_scrolls=18)
    device.wait("creation-foundation-pending-compilation-status", scroll=True, max_scrolls=18)
    device.back()
    device.wait("creation-wizard-dashboard", timeout=45)

    # Rook remains non-mutating after the Foundation revision advances.
    shared.reset_scroll_to_top(device, swipes=18)
    binding_before_rook = node_text(device, "creation-wizard-binding", scroll=True)
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
    assert_same_binding(binding_before_rook, binding_after)

    # Reopening proves that the workspace-scoped thread survives page visits.
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap("creation-wizard-rook", scroll=True)
    persisted_binding = node_text(device, "rook-message-binding-1", scroll=True)
    if persisted_binding != assistant_binding:
        raise RuntimeError("Rook transcript did not survive leaving and reopening the page")
    device.capture("creation-wizard-rook-local-thread")

    # A real process boundary is required: page navigation alone cannot prove durable local
    # conversation storage. Do not clear app data or reinstall between stop and relaunch.
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Your runners", timeout=90)
    shared.open_build(device, "phone")
    device.wait("creation-wizard-dashboard", timeout=90)
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap_until_visible(
        "creation-stage-foundation",
        "creation-foundation-page",
        scroll=True,
        max_scrolls=18,
    )
    device.wait("creation-foundation-pending-draft", timeout=60, scroll=True, max_scrolls=18)
    device.wait("creation-foundation-pending-character-effects-applied", scroll=True, max_scrolls=18)
    device.capture("creation-foundation-process-restart")
    device.back()
    device.wait("creation-wizard-dashboard", timeout=45)
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap("creation-wizard-rook", scroll=True)
    device.assert_text("What can I do next?", timeout=45)
    restarted_binding = node_text(device, "rook-message-binding-1", scroll=True)
    if restarted_binding != assistant_binding:
        raise RuntimeError("Rook transcript did not survive a force-stop and process restart")
    device.capture("creation-wizard-rook-process-restart")

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
            "foundationExactOptionSelection": "pass",
            "foundationMetatypeOption": metatype_option,
            "foundationNationalityOption": nationality_option,
            "foundationVersionOption": version_option,
            "foundationFollowUpSelections": selected_follow_ups,
            "foundationPreviewBeforeExplicitConfirm": "pass",
            "foundationBudgetAndTypedDiffVisible": "pass",
            "foundationConfirmationRefreshesBinding": "pass",
            "foundationDraftSaveReloadAndProcessRestart": "pass",
            "foundationCharacterEffectsAppliedFalse": "pass",
            "foundationCompilationPending": "pass",
            "rookLocalFallbackVisible": "pass",
            "rookTranscriptSurvivesPageVisits": "pass",
            "rookTranscriptSurvivesProcessRestart": "pass",
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
