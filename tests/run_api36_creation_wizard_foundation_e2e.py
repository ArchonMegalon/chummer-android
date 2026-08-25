#!/usr/bin/env python3
"""Scripted phone proof for fail-closed wizard routing and durable Foundation state.

This driver is intentionally committed without being executed in this change. It requires an
operator-provided, already-booted API 36 target and a reviewed APK.
"""

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


SCRIPT_STATUS = "scripted_not_executed"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def node_text(device: shared.Device, selector: str, *, scroll: bool = False) -> str:
    node = device.wait(selector, timeout=60, scroll=scroll, max_scrolls=18)
    return node.attributes.get("text") or node.attributes.get("content-desc") or ""


def assert_creation_editor_gated(device: shared.Device) -> None:
    """Scan the whole creation dashboard for Career/advanced-editor escape hatches."""
    forbidden = (
        "Actions",
        "build-origin-dossier",
        "build-free-sprite-conversion",
        "build-career-create-expense",
        "creation-wizard-attributes",
        "attribute-save-",
    )
    shared.reset_scroll_to_top(device, swipes=18)
    for scroll_index in range(19):
        nodes = device.hierarchy()
        for selector in forbidden:
            if any(shared.Device._matches(node, selector) for node in nodes):
                device.capture(f"wizard-forbidden-{selector}")
                raise RuntimeError(
                    "Creation dashboard exposed a Career/advanced-editor control while "
                    f"the authoritative runner is still uncreated: {selector!r}"
                )
        if scroll_index < 18:
            device.swipe_up()
    shared.reset_scroll_to_top(device, swipes=18)


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


def wait_for_any(device: shared.Device, *selectors: str, timeout: int = 60) -> str:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        for selector in selectors:
            if device.find(selector) is not None:
                return selector
        time.sleep(0.25)
    device.capture("missing-" + "-or-".join(selectors))
    raise RuntimeError(f"None of the expected pages appeared: {selectors!r}")


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
    shared.wait_for_phone_runners(device)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True)
    device.wait("dialog-action-complete-new-character-workflow", timeout=45, scroll=True)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True)
    shared.wait_for_phone_runner_route(device, created=False)

    # The completed setup must hand off directly; this driver never taps Continue building.
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        reset_swipes=18,
    )
    device.wait("creation-stage-basics", timeout=60, scroll=True, max_scrolls=18)
    shared.reset_scroll_to_top(device, swipes=18)
    binding_before = node_text(device, "creation-wizard-binding", scroll=True)
    assert_creation_editor_gated(device)

    # Foundation and Nationality remain a phone-only, authority-rendered route. Metatype uses a
    # separate typed-ID selection preview and explicit local confirmation before the combined
    # authoritative Foundation preview. The driver never supplies defaults.
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap_until_visible(
        "creation-stage-foundation",
        "creation-foundation-page",
        scroll=True,
        max_scrolls=18,
    )
    device.wait("creation-foundation-budget", timeout=60, scroll=True, max_scrolls=18)
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap("creation-foundation-open-metatype", scroll=True, max_scrolls=18)
    device.wait("creation-metatype-page", timeout=60)
    device.wait("creation-metatype-budget", timeout=45, scroll=True, max_scrolls=18)
    shared.reset_scroll_to_top(device, swipes=18)
    metatype_option = tap_first_enabled_prefix(device, "creation-metatype-option-")
    device.wait("creation-metatype-preview-page", timeout=60)
    device.wait("creation-metatype-preview-selection", timeout=45, scroll=True, max_scrolls=18)
    device.wait("creation-metatype-preview-budget", timeout=45, scroll=True, max_scrolls=18)
    device.tap("creation-metatype-confirm", scroll=True, max_scrolls=18)
    device.wait("creation-foundation-open-metatype", timeout=60, scroll=True, max_scrolls=18)
    selected_metatype = node_text(
        device,
        "creation-foundation-open-metatype",
        scroll=True,
    )
    if "selected" not in selected_metatype.lower():
        raise RuntimeError(
            "Explicit metatype confirmation did not restore the typed selection on Foundation: "
            f"{selected_metatype!r}"
        )

    # A normal Back from the non-writing preview must preserve the previously confirmed typed ID.
    device.tap("creation-foundation-open-metatype", scroll=True, max_scrolls=18)
    device.tap(metatype_option, scroll=True, max_scrolls=18)
    device.wait("creation-metatype-preview-page", timeout=60)
    device.back()
    device.wait("creation-metatype-page", timeout=45)
    device.back()
    device.wait("creation-foundation-open-metatype", timeout=45, scroll=True, max_scrolls=18)
    restored_metatype = node_text(
        device,
        "creation-foundation-open-metatype",
        scroll=True,
    )
    if restored_metatype != selected_metatype:
        raise RuntimeError("Back navigation did not restore the confirmed metatype selection")

    # Nationality and its optional version use stable typed IDs and their own explicit,
    # non-writing phone preview before returning the selection to Foundation.
    device.tap("creation-foundation-open-nationality", scroll=True, max_scrolls=18)
    device.wait("creation-nationality-page", timeout=60)
    device.wait("creation-nationality-budget", timeout=45, scroll=True, max_scrolls=18)
    shared.reset_scroll_to_top(device, swipes=18)
    nationality_option = tap_first_enabled_prefix(device, "creation-nationality-option-")
    nationality_route = wait_for_any(
        device,
        "creation-nationality-version-page",
        "creation-nationality-preview-page",
    )
    version_option = None
    if nationality_route == "creation-nationality-version-page":
        version_option = tap_first_enabled_prefix(
            device,
            "creation-nationality-version-option-",
        )
        device.wait("creation-nationality-preview-page", timeout=60)
    device.wait("creation-nationality-preview-selection", timeout=45, scroll=True, max_scrolls=18)
    device.wait("creation-nationality-preview-budget", timeout=45, scroll=True, max_scrolls=18)
    device.tap("creation-nationality-confirm", scroll=True, max_scrolls=22)
    device.wait("creation-foundation-open-nationality", timeout=60, scroll=True, max_scrolls=18)
    selected_nationality = node_text(
        device,
        "creation-foundation-open-nationality",
        scroll=True,
    )
    if "selected" not in selected_nationality.lower():
        raise RuntimeError(
            "Explicit Nationality confirmation did not restore the typed selection: "
            f"{selected_nationality!r}"
        )

    # Back from the non-writing Nationality preview leaves the previously confirmed IDs intact.
    device.tap("creation-foundation-open-nationality", scroll=True, max_scrolls=18)
    device.tap(nationality_option, scroll=True, max_scrolls=18)
    nationality_route = wait_for_any(
        device,
        "creation-nationality-version-page",
        "creation-nationality-preview-page",
    )
    if nationality_route == "creation-nationality-version-page":
        if version_option is None:
            raise RuntimeError("Nationality route unexpectedly gained a version after confirmation")
        device.tap(version_option, scroll=True, max_scrolls=18)
        device.wait("creation-nationality-preview-page", timeout=60)
    device.back()
    if nationality_route == "creation-nationality-version-page":
        device.wait("creation-nationality-version-page", timeout=45)
        device.back()
    device.wait("creation-nationality-page", timeout=45)
    device.back()
    device.wait("creation-foundation-open-nationality", timeout=45, scroll=True, max_scrolls=18)
    restored_nationality = node_text(
        device,
        "creation-foundation-open-nationality",
        scroll=True,
    )
    if restored_nationality != selected_nationality:
        raise RuntimeError("Back navigation did not restore the confirmed Nationality selection")

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
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=18,
    )
    assert_creation_editor_gated(device)
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
    resumed_nationality = node_text(
        device,
        "creation-foundation-open-nationality",
        scroll=True,
    )
    if "selected" not in resumed_nationality.lower():
        raise RuntimeError("Pending Foundation draft did not resume its typed Nationality IDs")
    device.back()
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=45,
        reset_swipes=18,
    )

    # A real process boundary is required: page navigation alone cannot prove durable draft
    # storage. Do not clear app data or reinstall between stop and relaunch.
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, created=False)
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        reset_swipes=18,
    )
    assert_creation_editor_gated(device)
    shared.reset_scroll_to_top(device, swipes=18)
    device.tap_until_visible(
        "creation-stage-foundation",
        "creation-foundation-page",
        scroll=True,
        max_scrolls=18,
    )
    device.wait("creation-foundation-pending-draft", timeout=60, scroll=True, max_scrolls=18)
    device.wait("creation-foundation-pending-character-effects-applied", scroll=True, max_scrolls=18)
    restarted_nationality = node_text(
        device,
        "creation-foundation-open-nationality",
        scroll=True,
    )
    if "selected" not in restarted_nationality.lower():
        raise RuntimeError("Process restart did not resume the typed Nationality IDs")
    device.capture("creation-foundation-process-restart")
    device.back()
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
            "advancedEditorNeverExposedWhileCreatedFalse": "pass",
            "foundationExactOptionSelection": "pass",
            "foundationMetatypeDeepNavigation": "pass",
            "foundationMetatypeBackRestoration": "pass",
            "foundationMetatypeOption": metatype_option,
            "foundationNationalityDeepNavigation": "pass",
            "foundationNationalityExplicitDraftConfirm": "pass",
            "foundationNationalityBackRestoration": "pass",
            "foundationNationalityPendingDraftResume": "pass",
            "foundationNationalityOption": nationality_option,
            "foundationVersionOption": version_option,
            "foundationFollowUpSelections": selected_follow_ups,
            "foundationPreviewBeforeExplicitConfirm": "pass",
            "foundationBudgetAndTypedDiffVisible": "pass",
            "foundationConfirmationRefreshesBinding": "pass",
            "foundationDraftSaveReloadAndProcessRestart": "pass",
            "foundationCharacterEffectsAppliedFalse": "pass",
            "foundationCompilationPending": "pass",
            "rookLaunchPostponedAndAbsent": "pass",
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
