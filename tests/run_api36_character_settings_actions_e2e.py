#!/usr/bin/env python3
"""Prove every remaining phone Character Settings action on API 36."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_character_settings_e2e as settings


shared = settings.shared
CONTROLS = (
    "cboSetting",
    "cmdSaveAs",
    "cmdRestoreDefaults",
    "cmdDelete",
    "cmdRename",
)
CONTROL_PROOF_KEYS = (
    "mutated",
    "catalogPersisted",
    "processRestartReadback",
)
PERSISTED_PROFILE = "Action Renamed"
THROWAWAY_PROFILE = "Delete Me"


def profiles(catalog: dict[str, object]) -> list[dict[str, object]]:
    value = catalog.get("Profiles")
    if not isinstance(value, list) or not all(isinstance(item, dict) for item in value):
        raise RuntimeError("Character Settings catalog profiles are missing")
    return value


def profile_named(catalog: dict[str, object], name: str) -> dict[str, object]:
    profile = next((item for item in profiles(catalog) if item.get("Name") == name), None)
    if profile is None:
        raise RuntimeError(f"Character Settings profile {name!r} is missing")
    return profile


def profile_xml(catalog: dict[str, object], name: str) -> ET.Element:
    return ET.fromstring(str(profile_named(catalog, name).get("Xml", "")))


def assert_active_profile(catalog: dict[str, object], name: str) -> None:
    profile = profile_named(catalog, name)
    if catalog.get("ActiveProfileId") != profile.get("Id"):
        raise RuntimeError(f"Character Settings active profile is not {name!r}")


def assert_profile_names(catalog: dict[str, object], expected: set[str]) -> None:
    observed = {str(profile.get("Name")) for profile in profiles(catalog)}
    if observed != expected:
        raise RuntimeError(
            f"Character Settings profile names were {sorted(observed)!r}, "
            f"expected {sorted(expected)!r}"
        )


def tap_action(device: shared.Device, action: str) -> None:
    device.tap(
        f"dialog-action-{action}",
        scroll=True,
        timeout=300,
        max_scrolls=64,
        scroll_distance_ratio=0.22,
    )
    device.wait("dialog-field-charactersettingsprofile", timeout=120)


def set_profile_name(device: shared.Device, name: str) -> None:
    settings.shared.reset_scroll_to_top(device, swipes=12)
    settings.set_exact_text(
        device,
        "dialog-field-charactersettingsprofilename",
        name,
    )


def run_actions(device: shared.Device) -> None:
    settings.create_runner(device)
    settings.open_character_settings(device)

    set_profile_name(device, "Action Copy")
    tap_action(device, "save-as")
    catalog = settings.read_catalog(device)
    assert_profile_names(catalog, {"Standard", "Action Copy"})
    assert_active_profile(catalog, "Action Copy")

    set_profile_name(device, PERSISTED_PROFILE)
    tap_action(device, "rename")
    catalog = settings.read_catalog(device)
    assert_profile_names(catalog, {"Standard", PERSISTED_PROFILE})
    assert_active_profile(catalog, PERSISTED_PROFILE)

    settings.shared.reset_scroll_to_top(device, swipes=12)
    settings.select_option(device, "dialog-field-charactersettingsprofile", "Standard")
    profile_node = settings.wait_exact_field(device, "dialog-field-charactersettingsprofile")
    if profile_node.attributes.get("text") != "Standard":
        raise RuntimeError("Character Settings profile selector did not switch to Standard")
    settings.activate_section(device, "Creation")
    settings.select_option(
        device,
        "dialog-field-charactersettingscontrol-cbobuildmethod",
        "Karma",
    )
    tap_action(device, "restore-defaults")
    restored = settings.wait_exact_field(
        device,
        "dialog-field-charactersettingscontrol-cbobuildmethod",
    )
    if restored.attributes.get("text") != "Priority":
        raise RuntimeError("Restore Defaults did not restore the Standard build method draft")
    tap_action(device, "save")

    settings.shared.reset_scroll_to_top(device, swipes=12)
    settings.select_option(
        device,
        "dialog-field-charactersettingsprofile",
        PERSISTED_PROFILE,
    )
    tap_action(device, "save")
    assert_active_profile(settings.read_catalog(device), PERSISTED_PROFILE)

    set_profile_name(device, THROWAWAY_PROFILE)
    tap_action(device, "save-as")
    catalog = settings.read_catalog(device)
    assert_profile_names(catalog, {"Standard", PERSISTED_PROFILE, THROWAWAY_PROFILE})
    assert_active_profile(catalog, THROWAWAY_PROFILE)
    tap_action(device, "delete")
    catalog = settings.read_catalog(device)
    assert_profile_names(catalog, {"Standard", PERSISTED_PROFILE})

    settings.shared.reset_scroll_to_top(device, swipes=12)
    settings.select_option(
        device,
        "dialog-field-charactersettingsprofile",
        PERSISTED_PROFILE,
    )
    tap_action(device, "save")
    catalog = settings.read_catalog(device)
    assert_active_profile(catalog, PERSISTED_PROFILE)
    if profile_xml(catalog, "Standard").findtext("buildmethod", default="") != "Priority":
        raise RuntimeError("Restored Standard build method was not saved")
    device.capture("phone-character-settings-actions-saved")


def assert_after_restart(device: shared.Device) -> None:
    catalog = settings.read_catalog(device)
    assert_profile_names(catalog, {"Standard", PERSISTED_PROFILE})
    assert_active_profile(catalog, PERSISTED_PROFILE)
    if profile_xml(catalog, "Standard").findtext("buildmethod", default="") != "Priority":
        raise RuntimeError("Restore Defaults result did not survive process restart")

    active = settings.wait_exact_field(device, "dialog-field-charactersettingsprofile")
    if active.attributes.get("text") != PERSISTED_PROFILE:
        raise RuntimeError("Profile selection did not survive process restart")
    settings.shared.reset_scroll_to_top(device, swipes=12)
    settings.select_option(device, "dialog-field-charactersettingsprofile", "Standard")
    settings.activate_section(device, "Creation")
    restored = settings.wait_exact_field(
        device,
        "dialog-field-charactersettingscontrol-cbobuildmethod",
    )
    if restored.attributes.get("text") != "Priority":
        raise RuntimeError("Restored defaults UI did not survive process restart")
    device.capture("phone-character-settings-actions-after-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    android_root = driver_path.parents[1]
    workspace_root = args.workspace_root.resolve()
    presentation_root = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    source_paths = {
        "characterSettingsDriverSha256": Path(settings.__file__).resolve(),
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "nativeCommandPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "NativeCommandPage.cs",
        "nativeDialogPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs",
        "runnerSessionCoordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "dialogFactorySha256": presentation_root / "DesktopDialogFactory.cs",
        "characterSettingsDialogSha256": presentation_root / "DesktopDialogFactory.CharacterSettings.cs",
        "characterSettingsProfilesSha256": presentation_root / "Chummer5CharacterSettingsProfiles.cs",
        "characterSettingsContractSha256": presentation_root / "Chummer5CharacterSettingsRuntimeContract.Generated.cs",
        "dialogCoordinatorSha256": presentation_root / "DialogCoordinator.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Character Settings action E2E source graph is incomplete: {missing!r}")

    device = settings.CharacterSettingsDevice(
        args.adb.resolve(),
        args.serial,
        args.evidence.resolve(),
    )
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Character Settings action E2E requires API 36, got {api!r}")
    subprocess.run(
        [
            str(args.adb),
            "-s",
            args.serial,
            "install",
            "--streaming",
            "-r",
            str(args.apk.resolve()),
        ],
        check=True,
        timeout=300,
    )
    device.shell("pm", "clear", shared.PACKAGE)
    run_actions(device)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    settings.open_character_settings(device)
    assert_after_restart(device)

    control_proofs = {
        control: {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "character-settings-actions",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver_path),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "controlCount": len(control_proofs),
        "controls": control_proofs,
        "journeys": {
            "profileSavedAs": "pass",
            "profileRenamed": "pass",
            "profileSelected": "pass",
            "defaultsRestored": "pass",
            "profileDeleted": "pass",
            "processRestartCatalogPersistence": "pass",
            "processRestartUiReadback": "pass",
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
        print(f"character-settings-actions E2E failed: {error}", flush=True)
        raise SystemExit(1) from error
