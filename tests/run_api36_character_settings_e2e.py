#!/usr/bin/env python3
"""Prove the complete phone Character Settings route on API 36."""

from __future__ import annotations

import argparse
import html
import json
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


SECTION_FIELDS = {
    "Ware, armor, and vehicles": (
        "dialog-field-charactersettingscontrol-chkdontusecyberlimbcalculation",
        "Dont Use Cyberlimb Calculation",
        "checkbox",
        "true",
    ),
    "Sourcebooks": (
        "dialog-field-charactersettingscontrol-tresourcebook",
        "Enabled sourcebooks",
        "text",
        "SR5X",
    ),
    "Rules and options": (
        "dialog-field-charactersettingscontrol-chkenforcecapacity",
        "Enforce Capacity",
        "checkbox",
        "false",
    ),
    "Formulas and formatting": (
        "dialog-field-charactersettingscontrol-nudnuyendecimalsminimum",
        "Min Nuyen Decimals",
        "text",
        "1",
    ),
    "Karma costs": (
        "dialog-field-charactersettingscontrol-nudkarmamysticadeptpowerpoint",
        "Karma Mystic Adept Power Point",
        "text",
        "6",
    ),
    "Custom data": (
        "dialog-field-charactersettingscontrol-trecustomdatadirectories",
        "Custom data directories (ordered)",
        "text",
        "phone-e2e",
    ),
    "Limits and initiative": (
        "dialog-field-charactersettingscontrol-chknoarmorencumbrance",
        "No Armor Encumbrance",
        "checkbox",
        "true",
    ),
    "Build method": (
        "dialog-field-charactersettingscontrol-cbobuildmethod",
        "Build Method",
        "select",
        "Karma",
    ),
}

UI_READBACK_OVERRIDES = {
    "Custom data": "[x] phone-e2e",
}


def find_exact(device: shared.Device, selector: str) -> shared.UiNode | None:
    matches: list[shared.UiNode] = []
    for node in device.hierarchy():
        attributes = node.attributes
        resource_id = attributes.get("resource-id", "").rsplit("/", 1)[-1]
        if selector in {resource_id, attributes.get("content-desc", "")}:
            matches.append(node)
    return next(
        (node for node in matches if node.attributes.get("clickable") == "true"),
        matches[0] if matches else None,
    )


def tap_exact_option(device: shared.Device, option_label: str) -> None:
    deadline = time.monotonic() + 60
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
            device.shell("input", "tap", *(str(value) for value in option.center))
            return
        time.sleep(0.5)
    device.capture(f"missing-option-{option_label.lower().replace(' ', '-')}")
    raise RuntimeError(f"Timed out waiting for picker option {option_label!r}")


def select_option(device: shared.Device, selector: str, option_label: str) -> None:
    node = device.wait(selector, timeout=60, scroll=True, max_scrolls=12, scroll_distance_ratio=0.22)
    device.shell("input", "tap", *(str(value) for value in node.center))
    tap_exact_option(device, option_label)
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        field = find_exact(device, selector)
        if field is not None and field.attributes.get("text") == option_label:
            time.sleep(1)
            return
        time.sleep(0.5)
    device.capture(f"picker-not-applied-{option_label.lower().replace(' ', '-')}")
    raise RuntimeError(f"Picker {selector!r} did not retain {option_label!r}")


def set_checkbox(device: shared.Device, selector: str, expected: bool) -> None:
    node = device.wait(selector, timeout=60, scroll=True, max_scrolls=12, scroll_distance_ratio=0.22)
    if (node.attributes.get("checked") == "true") != expected:
        device.shell("input", "tap", *(str(value) for value in node.center))
        time.sleep(1)
    applied = find_exact(device, selector)
    if applied is None or (applied.attributes.get("checked") == "true") != expected:
        device.capture(f"checkbox-not-applied-{selector}")
        raise RuntimeError(f"Checkbox {selector!r} did not retain {expected!r}")


def create_runner(device: shared.Device) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap_until_visible("home-new-runner", "Select Build Method", timeout=90)
    device.tap("dialog-action-create-character", scroll=True, timeout=90, max_scrolls=20)
    device.wait(
        "dialog-action-complete-new-character-workflow",
        timeout=90,
        scroll=True,
        max_scrolls=20,
    )
    device.tap(
        "dialog-action-complete-new-character-workflow",
        scroll=True,
        timeout=90,
        max_scrolls=20,
    )
    device.wait("Continue building", timeout=120)


def open_character_settings(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    device.tap_until_visible("Actions", "command-search", timeout=90)
    device.set_text("command-search", "Find an action", "character settings")
    device.tap("command-action-character-settings", timeout=60)
    device.wait("dialog-field-charactersettingsprofile", timeout=90)


def select_section(device: shared.Device, label: str, field_selector: str) -> None:
    shared.reset_scroll_to_top(device, swipes=4)
    select_option(device, "dialog-field-charactersettingssection", label)
    device.wait(field_selector, timeout=60, scroll=True, max_scrolls=8, scroll_distance_ratio=0.22)


def edit_representative_fields(device: shared.Device) -> None:
    for section, (selector, label, kind, value) in SECTION_FIELDS.items():
        print(f"character-settings e2e: editing {section}", flush=True)
        select_section(device, section, selector)
        if kind == "checkbox":
            set_checkbox(device, selector, value == "true")
        elif kind == "select":
            select_option(device, selector, value)
        else:
            device.set_text(
                selector,
                label,
                value,
                scroll=True,
                max_scrolls=10,
                scroll_distance_ratio=0.22,
            )
        time.sleep(1)


def assert_ui_readback(device: shared.Device) -> None:
    for section, (selector, _label, kind, value) in SECTION_FIELDS.items():
        print(f"character-settings e2e: verifying {section}", flush=True)
        select_section(device, section, selector)
        node = find_exact(device, selector)
        if node is None:
            raise RuntimeError(f"Persisted field {selector!r} was not rendered")
        if kind == "checkbox":
            actual = "true" if node.attributes.get("checked") == "true" else "false"
        else:
            actual = node.attributes.get("text", "")
        expected = UI_READBACK_OVERRIDES.get(section, value)
        if actual != expected:
            device.capture(f"readback-failed-{section.lower().replace(' ', '-')}")
            raise RuntimeError(f"Persisted field {selector!r} rendered {actual!r}, expected {expected!r}")


def read_catalog(device: shared.Device) -> dict[str, object]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "shared_prefs", "-type", "f")
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
        try:
            root = ET.fromstring(raw)
        except ET.ParseError:
            continue
        value = next(
            (
                element.text or ""
                for element in root.findall("string")
                if element.attrib.get("name") == "chummer.android.character-settings-catalog.v1"
            ),
            "",
        )
        if value:
            parsed = json.loads(html.unescape(value))
            if isinstance(parsed, dict):
                return parsed
    raise RuntimeError("Android preferences did not contain the Character Settings catalog")


def assert_catalog_xml(catalog: dict[str, object]) -> None:
    profiles = catalog.get("Profiles")
    active_id = catalog.get("ActiveProfileId")
    if not isinstance(profiles, list):
        raise RuntimeError("Character Settings catalog has no profiles")
    active = next(
        (profile for profile in profiles if isinstance(profile, dict) and profile.get("Id") == active_id),
        None,
    )
    if active is None:
        raise RuntimeError("Character Settings catalog has no active profile")
    if active.get("Name") != "Phone E2E":
        raise RuntimeError(f"Active Character Settings profile name was {active.get('Name')!r}")
    settings = ET.fromstring(str(active.get("Xml", "")))
    expected = {
        "books/book": "SR5X",
        "dontusecyberlimbcalculation": "True",
        "enforcecapacity": "False",
        "karmacost/karmamysadpp": "6",
        "noarmorencumbrance": "True",
        "buildmethod": "Karma",
    }
    observed = {path: settings.findtext(path, default="") for path in expected}
    if observed != expected:
        raise RuntimeError(f"Character Settings XML mismatch: {observed!r}")
    custom = settings.find("customdatadirectorynames/customdatadirectoryname")
    if custom is None or custom.findtext("directoryname", default="") != "phone-e2e":
        raise RuntimeError("Custom data directory did not persist")
    if settings.findtext("nuyenformat", default="") != "#,0.0#":
        raise RuntimeError("Nuyen decimal formatting did not persist")


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
        "driverSha256": driver_path,
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "nativeCommandPageSha256": android_root / "src/Chummer.Android/Native/NativeCommandPage.cs",
        "nativeDialogPageSha256": android_root / "src/Chummer.Android/Native/NativeDialogPage.cs",
        "runnerSessionCoordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "dialogFactorySha256": presentation_root / "DesktopDialogFactory.cs",
        "characterSettingsDialogSha256": presentation_root / "DesktopDialogFactory.CharacterSettings.cs",
        "characterSettingsProfilesSha256": presentation_root / "Chummer5CharacterSettingsProfiles.cs",
        "characterSettingsContractSha256": presentation_root / "Chummer5CharacterSettingsRuntimeContract.Generated.cs",
        "dialogCoordinatorSha256": presentation_root / "DialogCoordinator.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Character Settings E2E source graph is incomplete: {missing!r}")

    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Character Settings E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.shell("pm", "clear", shared.PACKAGE)
    create_runner(device)
    open_character_settings(device)
    edit_representative_fields(device)
    shared.reset_scroll_to_top(device, swipes=8)
    device.set_text(
        "dialog-field-charactersettingsprofilename",
        "Profile name",
        "Phone E2E",
    )
    device.tap("dialog-action-save-and-close", scroll=True, timeout=120, max_scrolls=32, scroll_distance_ratio=0.22)
    device.wait("command-search", timeout=90)
    assert_catalog_xml(read_catalog(device))
    device.capture("phone-character-settings-saved")
    device.back()

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_character_settings(device)
    assert_ui_readback(device)
    assert_catalog_xml(read_catalog(device))
    device.capture("phone-character-settings-after-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "character-settings",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "journeys": {
            "actionSearchRoute": "pass",
            "allEightPhoneSectionsReachable": "pass",
            "checkboxEdited": "pass",
            "textEdited": "pass",
            "numberEdited": "pass",
            "pickerEdited": "pass",
            "sourcebookCollectionEdited": "pass",
            "customDataCollectionEdited": "pass",
            "profileSavedAndClosed": "pass",
            "catalogXmlPersisted": "pass",
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
        print(f"character-settings e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
