#!/usr/bin/env python3
"""Prove Chummer5 nested collection notes on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CASES: dict[str, dict[str, dict[str, str]]] = {
    "CharacterCreate": {
        "tsWeaponAccessoryNotes": {
            "section": "weaponaccessories",
            "kind": "weapon",
            "target": "weapon-accessory",
            "value": "CreationWeaponAccessoryNotesE2E",
        },
        "tsArmorModNotes": {
            "section": "armormods",
            "kind": "armor",
            "target": "armor-mod",
            "value": "CreationArmorModNotesE2E",
        },
    },
    "CharacterCareer": {
        "tsWeaponAccessoryNotes": {
            "section": "weaponaccessories",
            "kind": "weapon",
            "target": "weapon-accessory",
            "value": "CareerWeaponAccessoryNotesE2E",
        },
        "tsArmorModNotes": {
            "section": "armormods",
            "kind": "armor",
            "target": "armor-mod",
            "value": "CareerArmorModNotesE2E",
        },
        "tsGearPluginNotes": {
            "section": "gear",
            "kind": "gear",
            "target": "gear-plugin",
            "value": "CareerGearPluginNotesE2E",
        },
    },
}
TARGET_PATHS: dict[str, tuple[str, str, str, str, str, str]] = {
    "weapon-accessory": (
        "weapons",
        "weapon",
        "weapon-parent",
        "accessories",
        "accessory",
        "weapon-accessory",
    ),
    "armor-mod": (
        "armors",
        "armor",
        "armor-parent",
        "armormods",
        "armormod",
        "armor-mod",
    ),
    "gear-plugin": (
        "gears",
        "gear",
        "gear-parent",
        "children",
        "gear",
        "gear-plugin",
    ),
}
CONTROL_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)


def token(value: str) -> str:
    return "".join(character if character.isalnum() else "-" for character in value.lower())


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run(
                "exec-out",
                "run-as",
                shared.PACKAGE,
                "cat",
                path,
            ).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def find_by_guid(elements: list[ET.Element], guid: str) -> ET.Element | None:
    return next(
        (element for element in elements if element.findtext("guid", default="").strip() == guid),
        None,
    )


def read_nested_note(character: ET.Element, target: str) -> str | None:
    (
        parent_container_name,
        parent_item_name,
        parent_guid,
        child_container_name,
        child_item_name,
        child_guid,
    ) = TARGET_PATHS[target]
    parent_container = character.find(parent_container_name)
    if parent_container is None:
        return None
    parent = find_by_guid(list(parent_container.findall(parent_item_name)), parent_guid)
    if parent is None:
        return None
    child_container = parent.find(child_container_name)
    if child_container is None:
        return None
    child = find_by_guid(list(child_container.findall(child_item_name)), child_guid)
    return None if child is None else child.findtext("notes", default="")


def assert_workspace_notes(
    device: shared.Device,
    form_name: str,
    expected: dict[str, str],
) -> None:
    observed: list[dict[str, str | None]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        values = {target: read_nested_note(character, target) for target in expected}
        observed.append(values)
        if values == expected:
            return
    device.capture(f"{form_name.lower()}-nested-notes-workspace-not-persisted")
    raise RuntimeError(
        f"{form_name} nested notes were not durable in workspace XML; observed {observed!r}"
    )


def open_fixture(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_nested_editor(device: shared.Device, case: dict[str, str]) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-section-tab-gear",
        scroll=True,
        timeout=90,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"build-action-tab-gear-{case['section']}",
        scroll=True,
        timeout=90,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    item_selector = f"collection-item-{case['kind']}-{token(case['target'])}"
    shared.tap_collection_item(device, item_selector)
    device.wait(
        f"collection-field-notes-{token(case['target'])}",
        timeout=90,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )


def edit_nested_note(device: shared.Device, case: dict[str, str]) -> None:
    target = token(case["target"])
    device.set_text(
        f"collection-field-notes-{target}",
        "Notes",
        case["value"],
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"collection-save-{target}",
        scroll=True,
        timeout=240,
        max_scrolls=32,
        scroll_distance_ratio=0.22,
    )


def assert_nested_note_ui(
    device: shared.Device,
    form_name: str,
    control: str,
    case: dict[str, str],
) -> None:
    selector = f"collection-field-notes-{token(case['target'])}"
    node = device.wait(
        selector,
        timeout=90,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    actual = node.attributes.get("text", "")
    if actual != case["value"]:
        device.capture(f"{form_name.lower()}-{control.lower()}-value-mismatch")
        raise RuntimeError(
            f"{form_name}.{control} did not persist: expected {case['value']!r}, got {actual!r}"
        )


def return_to_build_overview(device: shared.Device) -> None:
    device.back()
    device.back()
    device.wait("build-section-tab-gear", timeout=90, scroll=True)


def exercise_form(
    device: shared.Device,
    form_name: str,
    fixture_name: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    open_fixture(device, fixture_name)
    expected: dict[str, str] = {}
    for control, case in CASES[form_name].items():
        open_nested_editor(device, case)
        edit_nested_note(device, case)
        expected[case["target"]] = case["value"]
        assert_workspace_notes(device, form_name, expected)
        assert_nested_note_ui(device, form_name, control, case)
        return_to_build_overview(device)

    for control, case in CASES[form_name].items():
        open_nested_editor(device, case)
        assert_nested_note_ui(device, form_name, control, case)
        device.capture(f"nested-notes-{form_name.lower()}-{control.lower()}-after-reopen")
        return_to_build_overview(device)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_notes(device, form_name, expected)
    for control, case in CASES[form_name].items():
        open_nested_editor(device, case)
        assert_nested_note_ui(device, form_name, control, case)
        device.capture(f"nested-notes-{form_name.lower()}-{control.lower()}-after-process-restart")
        return_to_build_overview(device)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(
        "--creation-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "creation-nested-notes-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "career-nested-notes-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    fixtures = {
        "CharacterCreate": args.creation_runner.resolve(),
        "CharacterCareer": args.career_runner.resolve(),
    }
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "collectionPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "sectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "shellCatalogSha256": workspace_root / "chummer-core-engine" / "Chummer.Rulesets.Hosting" / "Presentation" / "WorkspaceSurfaceActionCatalog.cs",
        "projectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "collectionMutationRequestSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
    }
    required_paths = [*fixtures.values(), *source_paths.values()]
    if not all(path.is_file() for path in required_paths):
        missing = [str(path) for path in required_paths if not path.is_file()]
        raise RuntimeError(f"Nested-notes E2E source graph is incomplete: {missing!r}")

    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Nested-notes E2E requires API 36, got {api!r}")
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
    for fixture in fixtures.values():
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    for form_name, fixture in fixtures.items():
        print(
            f"nested-notes e2e: exercising {form_name} ({len(CASES[form_name])} controls)",
            flush=True,
        )
        exercise_form(device, form_name, fixture.name)

    control_proofs = {
        f"{form_name}.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for form_name, controls in CASES.items()
        for control in controls
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "nested-collection-notes",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(fixtures["CharacterCreate"]),
        "careerFixtureSha256": shared.sha256(fixtures["CharacterCareer"]),
        "controlCount": len(control_proofs),
        "controls": control_proofs,
        "journeys": {
            "creationRunnerImported": "pass",
            "allCreationNestedNotesEdited": "pass",
            "creationWorkspaceXmlPersisted": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerRunnerImported": "pass",
            "allCareerNestedNotesEdited": "pass",
            "careerWorkspaceXmlPersisted": "pass",
            "careerProcessRestartUiReadback": "pass",
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
        print(f"nested-notes E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
