#!/usr/bin/env python3
"""Prove the phone metatype-Karma workflow on an API 36 emulator."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared
import run_api36_new_character_priority_e2e as priority_helpers


EXPECTED_KARMA_XML = {
    "buildmethod": "Karma",
    "metatypecategory": "Metahuman",
    "metatype": "Elf",
    "metavariant": "Dryad",
}
EXPECTED_SPIRIT_XML = {
    "buildmethod": "Karma",
    "metatypecategory": "Spirits",
    "metatype": "Spirit of Fire",
    "force": "8",
    "possessionmethod": "Possession",
}


def assert_persisted_character(
    device: shared.Device,
    expected: dict[str, str],
    *,
    possession_power: str | None = None,
) -> None:
    observed: list[dict[str, str]] = []
    for payload in priority_helpers.workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        values = {key: character.findtext(key, default="") for key in expected}
        observed.append(values)
        if values != expected:
            continue
        if possession_power is None:
            return
        power = next(
            (
                candidate
                for candidate in character.findall("critterpowers/critterpower")
                if candidate.findtext("name", default="") == possession_power
            ),
            None,
        )
        if (
            power is not None
            and power.findtext("sourceid", default="")
            == "a142b612-2f4c-4c97-8b1b-fd15c9f68866"
            and power.findtext("action", default="") == "Complex"
            and power.findtext("duration", default="") == "Sustained"
        ):
            return
    device.capture("metatype-karma-not-persisted")
    raise RuntimeError(
        "Phone metatype-Karma selections were not durable in the workspace store; "
        f"observed {observed!r}"
    )


def open_karma_workflow(device: shared.Device) -> None:
    device.tap_until_visible("home-new-runner", "Select Build Method")
    priority_helpers.select_option(
        device,
        "dialog-field-newcharacterbuildmethod",
        "Karma",
    )
    device.tap("dialog-action-create-character", scroll=True, max_scrolls=16)
    device.wait("Select Metatype", timeout=60)


def assert_standard_profile_readback(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=16)
    device.assert_text("Elf", timeout=30)
    device.assert_text("Dryad", timeout=30)


def assert_spirit_profile_readback(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=16)
    device.assert_text("Spirit of Fire", timeout=30)


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
    helper_driver_path = Path(priority_helpers.__file__).resolve()
    android_root = driver_path.parents[1]
    configured_workspace_root = os.environ.get("CHUMMER_COMPLETE_ROOT")
    workspace_candidates = (
        [Path(configured_workspace_root).resolve()]
        if configured_workspace_root
        else [candidate.resolve() for candidate in android_root.parents]
    )
    workspace_root = next(
        (
            candidate
            for candidate in workspace_candidates
            if (
                candidate
                / "chummer-presentation"
                / "Chummer.Presentation"
                / "Overview"
            ).is_dir()
        ),
        None,
    )
    if workspace_root is None:
        searched = ", ".join(str(candidate) for candidate in workspace_candidates)
        raise FileNotFoundError(
            "Could not locate the Chummer workspace root containing "
            f"chummer-presentation; searched: {searched}"
        )
    presentation_root = (
        workspace_root
        / "chummer-presentation"
        / "Chummer.Presentation"
        / "Overview"
    )
    dialog_factory_path = presentation_root / "DesktopDialogFactory.cs"
    dialog_coordinator_path = presentation_root / "DialogCoordinator.cs"
    native_dialog_path = (
        android_root
        / "src"
        / "Chummer.Android"
        / "Native"
        / "NativeDialogPage.cs"
    )
    build_page_path = native_dialog_path.with_name("BuildPage.cs")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Metatype-Karma E2E requires API 36, got {api!r}")

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
    open_karma_workflow(device)

    priority_helpers.select_option(
        device,
        "dialog-field-newcharactermetatypecategory",
        "Non-human choices",
    )
    device.set_text(
        "dialog-field-newcharactermetatypesearch",
        "Search metatypes",
        "Elf",
        scroll=True,
        max_scrolls=16,
        scroll_distance_ratio=0.22,
    )
    priority_helpers.select_option(
        device,
        "dialog-field-newcharactermetatype",
        "Elf",
    )
    priority_helpers.select_option(
        device,
        "dialog-field-newcharactermetavariant",
        "Dryad",
    )
    device.tap(
        "dialog-action-complete-new-character-workflow",
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    shared.wait_for_phone_runner_route(device, timeout=90)

    assert_persisted_character(device, EXPECTED_KARMA_XML)
    assert_standard_profile_readback(device)
    device.capture("phone-metatype-karma-persisted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=90)
    assert_persisted_character(device, EXPECTED_KARMA_XML)
    assert_standard_profile_readback(device)
    device.capture("phone-metatype-karma-after-restart")

    device.shell("pm", "clear", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=90)
    open_karma_workflow(device)

    priority_helpers.select_option(
        device,
        "dialog-field-newcharactermetatypecategory",
        "Spirit choices",
    )
    priority_helpers.select_option(
        device,
        "dialog-field-newcharactermetatype",
        "Spirit of Fire",
    )
    device.set_text(
        "dialog-field-newcharacterforce",
        "Force",
        "8",
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        "dialog-field-newcharacterpossessionbased",
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    priority_helpers.select_option(
        device,
        "dialog-field-newcharacterpossessionmethod",
        "Possession",
    )
    device.tap(
        "dialog-action-complete-new-character-workflow",
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    shared.wait_for_phone_runner_route(device, timeout=90)

    assert_persisted_character(
        device,
        EXPECTED_SPIRIT_XML,
        possession_power="Possession",
    )
    assert_spirit_profile_readback(device)
    device.capture("phone-karma-spirit-force-possession-persisted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=90)
    assert_persisted_character(
        device,
        EXPECTED_SPIRIT_XML,
        possession_power="Possession",
    )
    assert_spirit_profile_readback(device)
    device.capture("phone-karma-spirit-force-possession-after-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "new-character-metatype-karma",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver_path),
        "sharedDriverSha256": shared.sha256(shared_driver_path),
        "helperDriverSha256": shared.sha256(helper_driver_path),
        "dialogFactorySha256": shared.sha256(dialog_factory_path),
        "dialogCoordinatorSha256": shared.sha256(dialog_coordinator_path),
        "nativeDialogPageSha256": shared.sha256(native_dialog_path),
        "buildPageSha256": shared.sha256(build_page_path),
        "journeys": {
            "buildMethodKarmaSelected": "pass",
            "metatypeSearchEdited": "pass",
            "metatypeSearchFiltered": "pass",
            "metatypeCategoryEdited": "pass",
            "metatypeEdited": "pass",
            "metavariantEdited": "pass",
            "forceEdited": "pass",
            "possessionBasedEnabled": "pass",
            "possessionMethodEdited": "pass",
            "creationCommitCompleted": "pass",
            "metatypeUiReadback": "pass",
            "metavariantUiReadback": "pass",
            "workspaceKarmaPersisted": "pass",
            "processRestartKarmaPersistence": "pass",
            "spiritUiReadback": "pass",
            "workspaceSpiritPossessionPersisted": "pass",
            "processRestartSpiritPossessionPersistence": "pass",
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
        print(f"metatype-Karma e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
