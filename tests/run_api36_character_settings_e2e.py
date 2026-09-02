#!/usr/bin/env python3
"""Prove the complete phone Character Settings route on API 36."""

from __future__ import annotations

import argparse
import html
import json
import re
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

SECTION_IDS = {
    "Ware, armor, and vehicles": "ware",
    "Sourcebooks": "sourcebooks",
    "Rules and options": "rules",
    "Formulas and formatting": "formulas",
    "Karma costs": "karma",
    "Custom data": "custom-data",
    "Limits and initiative": "limits",
    "Build method": "build",
}

UI_READBACK_OVERRIDES = {
    "chkGrade": "Betaware",
    "treCustomDataDirectories": "[x] phone-e2e",
}

VALUE_OPERATIONS = frozenset(
    {
        "set_value",
        "edit_sourcebooks",
        "edit_custom_data_directories",
    }
)
CONTROL_VALUE_OVERRIDES: dict[str, bool | str] = {
    "chkGrade": "Betaware",
    "treSourcebook": "SR5X",
    "chkDontUseCyberlimbCalculation": True,
    "chkEnforceCapacity": False,
    "nudNuyenDecimalsMinimum": "1",
    "nudKarmaMysticAdeptPowerPoint": "6",
    "treCustomDataDirectories": "phone-e2e",
    "chkNoArmorEncumbrance": True,
    "cboBuildMethod": "Karma",
}
SELECT_OPTIONS = {
    "cboLimbCount": (
        "4 limbs (2 arms, 2 legs)",
        "5 limbs (include skull)",
        "5 limbs (include torso)",
        "6",
    ),
    "cboBuildMethod": ("Priority", "SumtoTen", "Karma", "LifeModule"),
}
TEXT_VALUE_OVERRIDES = {
    "txtGameplayOptionName": "Phone E2E",
    "txtPriorities": "EDCBA",
}
CONTROL_PROOF_KEYS = (
    "mutated",
    "catalogPersisted",
    "processRestartUiReadback",
)


class CharacterSettingsDevice(shared.Device):
    """Keep this long journey resilient without invalidating unrelated E2E receipts."""

    def shell(
        self,
        *arguments: str,
        timeout: float = 120,
        deadline: float | None = None,
    ) -> str:
        if arguments in (
            shared.ADB_FILE_HIERARCHY_REMOVE_SHELL_ARGUMENTS,
            shared.ADB_FILE_HIERARCHY_DUMP_SHELL_ARGUMENTS,
        ):
            timeout = min(timeout, 30)
        return super().shell(
            *arguments,
            timeout=timeout,
            deadline=deadline,
        )

    def hierarchy(
        self,
        *,
        deadline: float | None = None,
    ) -> list[shared.UiNode]:
        try:
            return super().hierarchy(deadline=deadline)
        except subprocess.TimeoutExpired as error:
            detail_parts: list[str] = []
            for part in (str(error), error.stdout, error.stderr):
                if not part:
                    continue
                detail_parts.append(
                    part.decode(errors="replace")
                    if isinstance(part, bytes)
                    else str(part)
                )
            (self.evidence / "last-invalid-hierarchy.txt").write_text(
                "\n".join(detail_parts),
                encoding="utf-8",
            )
            return []


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


def wait_exact_field(
    device: shared.Device,
    selector: str,
    *,
    timeout: int = 90,
    max_scrolls: int = 96,
    require_tappable: bool = True,
) -> shared.UiNode:
    deadline = time.monotonic() + timeout
    scrolls = 0
    while time.monotonic() < deadline:
        node = find_exact(device, selector)
        if node is not None and (
            not require_tappable or device.node_has_tappable_bounds(node)
        ):
            return node
        if device.dismiss_system_ui_anr():
            time.sleep(2)
            continue
        if scrolls < max_scrolls:
            device.swipe_up(distance_ratio=0.22)
            scrolls += 1
        time.sleep(0.75)
    device.capture(f"missing-exact-{selector}")
    raise RuntimeError(f"Timed out waiting for exact UI field {selector!r}")


def tap_exact_field(device: shared.Device, selector: str) -> None:
    node = wait_exact_field(device, selector)
    device.shell("input", "tap", *(str(value) for value in node.center))


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
    tap_exact_field(device, selector)
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
    node = wait_exact_field(device, selector)
    if (node.attributes.get("checked") == "true") != expected:
        tap_exact_field(device, selector)
        time.sleep(1)
    applied = find_exact(device, selector)
    if applied is None or (applied.attributes.get("checked") == "true") != expected:
        device.capture(f"checkbox-not-applied-{selector}")
        raise RuntimeError(f"Checkbox {selector!r} did not retain {expected!r}")


def set_exact_text(device: shared.Device, selector: str, value: str) -> None:
    node = wait_exact_field(device, selector)
    focused: shared.UiNode | None = None
    for _ in range(3):
        device.shell("input", "tap", *(str(coordinate) for coordinate in node.center))
        time.sleep(0.5)
        focused = find_exact(device, selector)
        if focused is not None and focused.attributes.get("focused") == "true":
            break
        if device.keyboard_visible():
            device.dismiss_keyboard()
        node = wait_exact_field(device, selector)
    if focused is None or focused.attributes.get("focused") != "true":
        device.capture(f"exact-field-focus-failed-{selector}")
        raise RuntimeError(f"Exact field {selector!r} did not receive focus")
    device.shell("input", "keycombination", "113", "29")
    time.sleep(0.25)
    if value:
        device.shell("input", "text", value.replace(" ", "%s"))
    else:
        device.shell("input", "keyevent", "67")
    time.sleep(0.25)
    updated = find_exact(device, selector)
    if updated is None or updated.attributes.get("text") != value:
        device.capture(f"exact-field-value-failed-{selector}")
        actual = None if updated is None else updated.attributes.get("text")
        raise RuntimeError(
            f"Exact field {selector!r} did not receive {value!r}; rendered {actual!r}"
        )
    device.dismiss_keyboard()


def create_runner(device: shared.Device) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
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
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_character_settings(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    device.tap_until_visible("Actions", "command-search", timeout=90)
    device.set_text("command-search", "Find an action", "character settings")
    device.tap("command-action-character-settings", timeout=60)
    device.wait("dialog-field-charactersettingsprofile", timeout=90)


def activate_section(device: shared.Device, label: str) -> None:
    shared.reset_scroll_to_top(device, swipes=16)
    select_option(device, "dialog-field-charactersettingssection", label)
    shared.reset_scroll_to_top(device, swipes=16)


def android_token(value: str) -> str:
    return "".join(character if character.isalnum() else "-" for character in value.strip().lower())


def load_value_controls(contract_path: Path) -> dict[str, dict[str, object]]:
    payload = json.loads(contract_path.read_text(encoding="utf-8"))
    controls = {
        str(row["legacyControl"]): row
        for row in payload.get("controls", [])
        if isinstance(row, dict)
        and row.get("semanticOperation") in VALUE_OPERATIONS
    }
    if len(controls) != 150:
        raise RuntimeError(
            "Character Settings E2E requires the exact 150-control value contract; "
            f"observed {len(controls)}"
        )
    return controls


def selector_for_control(control: str) -> str:
    return f"dialog-field-charactersettingscontrol-{android_token(control)}"


def discover_section_controls(
    runtime_contract_path: Path,
    controls: dict[str, dict[str, object]],
) -> dict[str, list[str]]:
    sections = {section: [] for section in SECTION_FIELDS}
    section_labels = {section_id: label for label, section_id in SECTION_IDS.items()}
    field_pattern = re.compile(
        r'^\s*new\("([^"]+)",\s*"[^"]*",\s*"([^"]+)",',
        re.MULTILINE,
    )
    for control, section_id in field_pattern.findall(
        runtime_contract_path.read_text(encoding="utf-8")
    ):
        if control not in controls:
            continue
        section = section_labels.get(section_id)
        if section is None:
            raise RuntimeError(
                f"Character Settings control {control!r} uses unknown section {section_id!r}"
            )
        sections[section].append(control)
    rules = sections["Rules and options"]
    for parent, dependent in (
        ("chkExceedNegativeQualities", "chkExceedNegativeQualitiesNoBonus"),
        ("chkExceedPositiveQualities", "chkExceedPositiveQualitiesCostDoubled"),
    ):
        rules.remove(parent)
        rules.insert(rules.index(dependent), parent)
    discovered = {
        control
        for section_controls in sections.values()
        for control in section_controls
    }
    expected = set(controls)
    if discovered != expected:
        missing = sorted(expected - discovered)
        unexpected = sorted(discovered - expected)
        raise RuntimeError(
            "Character Settings runtime contract did not expose the exact value-control contract; "
            f"missing={missing!r}, unexpected={unexpected!r}"
        )
    return sections


def control_kind(control: str) -> str:
    if control in SELECT_OPTIONS:
        return "select"
    if control.startswith("chk") and control != "chkGrade":
        return "checkbox"
    if control.startswith("nud"):
        return "number"
    return "text"


def alternate_text_value(control: str, current: str) -> str:
    override = CONTROL_VALUE_OVERRIDES.get(control)
    if isinstance(override, str):
        return override
    if control in TEXT_VALUE_OVERRIDES:
        return TEXT_VALUE_OVERRIDES[control]
    return "1" if current.strip() != "1" else "2"


def alternate_number_value(control: str, current: str) -> str:
    override = CONTROL_VALUE_OVERRIDES.get(control)
    if isinstance(override, str):
        return override
    return "3" if current.strip() == "2" else "2"


def alternate_select_value(control: str, current: str) -> str:
    override = CONTROL_VALUE_OVERRIDES.get(control)
    if isinstance(override, str):
        return override
    return next(
        (option for option in SELECT_OPTIONS[control] if option != current),
        SELECT_OPTIONS[control][0],
    )


def edit_all_value_controls(
    device: shared.Device,
    controls: dict[str, dict[str, object]],
    sections: dict[str, list[str]],
) -> dict[str, dict[str, object]]:
    expectations: dict[str, dict[str, object]] = {}
    for section, section_controls in sections.items():
        print(
            f"character-settings e2e: editing {section} ({len(section_controls)} controls)",
            flush=True,
        )
        activate_section(device, section)
        for control in section_controls:
            selector = selector_for_control(control)
            node = wait_exact_field(device, selector)
            kind = control_kind(control)
            if kind == "checkbox":
                override = CONTROL_VALUE_OVERRIDES.get(control)
                expected: bool | str = (
                    override
                    if isinstance(override, bool)
                    else node.attributes.get("checked") != "true"
                )
                set_checkbox(device, selector, bool(expected))
            elif kind == "select":
                expected = alternate_select_value(control, node.attributes.get("text", ""))
                select_option(device, selector, str(expected))
            else:
                current = node.attributes.get("text", "")
                expected = (
                    alternate_number_value(control, current)
                    if kind == "number"
                    else alternate_text_value(control, current)
                )
                set_exact_text(device, selector, str(expected))
            expectations[control] = {
                "kind": kind,
                "value": expected,
                "paths": list(controls[control].get("persistencePaths", [])),
                "section": section,
            }
    return expectations


def assert_all_ui_readback(
    device: shared.Device,
    expectations: dict[str, dict[str, object]],
    sections: dict[str, list[str]],
) -> None:
    for section, section_controls in sections.items():
        print(
            f"character-settings e2e: verifying {section} ({len(section_controls)} controls)",
            flush=True,
        )
        activate_section(device, section)
        for control in section_controls:
            selector = selector_for_control(control)
            node = wait_exact_field(device, selector)
            expectation = expectations[control]
            expected = expectation["value"]
            if expectation["kind"] == "checkbox":
                actual: bool | str = node.attributes.get("checked") == "true"
            else:
                actual = node.attributes.get("text", "")
                expected = UI_READBACK_OVERRIDES.get(control, str(expected))
            if actual != expected:
                device.capture(f"readback-failed-{android_token(control)}")
                raise RuntimeError(
                    f"Persisted field {selector!r} rendered {actual!r}, expected {expected!r}"
                )


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


def active_settings(catalog: dict[str, object]) -> ET.Element:
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
    return ET.fromstring(str(active.get("Xml", "")))


def settings_path_values(settings: ET.Element, path: str) -> tuple[str, ...]:
    relative = path.removeprefix("settings/")
    if relative == "settings":
        return (ET.tostring(settings, encoding="unicode"),)
    return tuple(element.text or "" for element in settings.findall(relative))


def assert_all_controls_persisted(
    baseline_catalog: dict[str, object],
    saved_catalog: dict[str, object],
    expectations: dict[str, dict[str, object]],
) -> None:
    baseline = active_settings(baseline_catalog)
    saved = active_settings(saved_catalog)
    unchanged: dict[str, list[str]] = {}
    for control, expectation in expectations.items():
        paths = [str(path) for path in expectation["paths"]]
        if not paths or not any(
            settings_path_values(baseline, path) != settings_path_values(saved, path)
            for path in paths
        ):
            unchanged[control] = paths
    if unchanged:
        raise RuntimeError(
            "Character Settings controls did not change any mapped catalog XML path: "
            f"{unchanged!r}"
        )


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
    settings = active_settings(catalog)
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
    if settings.findtext("nuyenformat", default="") != "#,0.0##":
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
    contract_path = android_root / "docs" / "CHUMMER5_CHARACTER_SETTINGS_CONTRACT.generated.json"
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
    controls = load_value_controls(contract_path)
    sections = discover_section_controls(
        source_paths["characterSettingsContractSha256"],
        controls,
    )

    device = CharacterSettingsDevice(
        args.adb.resolve(),
        args.serial,
        args.evidence.resolve(),
    )
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
    device.tap(
        "dialog-action-save",
        scroll=True,
        timeout=300,
        max_scrolls=48,
        scroll_distance_ratio=0.22,
    )
    device.wait("dialog-field-charactersettingsprofile", timeout=90)
    baseline_catalog = read_catalog(device)
    expectations = edit_all_value_controls(device, controls, sections)
    shared.reset_scroll_to_top(device, swipes=8)
    device.set_text(
        "dialog-field-charactersettingsprofilename",
        "Profile name",
        "Phone E2E",
    )
    device.tap(
        "dialog-action-save-and-close",
        scroll=True,
        timeout=300,
        max_scrolls=48,
        scroll_distance_ratio=0.22,
    )
    device.wait("command-search", timeout=90)
    saved_catalog = read_catalog(device)
    assert_all_controls_persisted(baseline_catalog, saved_catalog, expectations)
    assert_catalog_xml(saved_catalog)
    device.capture("phone-character-settings-saved")
    device.back()

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_character_settings(device)
    assert_all_ui_readback(device, expectations, sections)
    restarted_catalog = read_catalog(device)
    assert_all_controls_persisted(baseline_catalog, restarted_catalog, expectations)
    assert_catalog_xml(restarted_catalog)
    device.capture("phone-character-settings-after-restart")

    control_proofs = {
        control: {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in sorted(expectations)
    }

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
        "valueControlCount": len(control_proofs),
        "controls": control_proofs,
        "journeys": {
            "actionSearchRoute": "pass",
            "allEightPhoneSectionsReachable": "pass",
            "checkboxEdited": "pass",
            "textEdited": "pass",
            "numberEdited": "pass",
            "pickerEdited": "pass",
            "sourcebookCollectionEdited": "pass",
            "customDataCollectionEdited": "pass",
            "profileSavedWithoutClosing": "pass",
            "profileSavedAndClosed": "pass",
            "catalogXmlPersisted": "pass",
            "processRestartCatalogPersistence": "pass",
            "processRestartUiReadback": "pass",
            "allValueControlsEdited": "pass",
            "allValueControlsCatalogPersisted": "pass",
            "allValueControlsRestartUiReadback": "pass",
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
