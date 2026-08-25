#!/usr/bin/env python3
"""Prove every phone Origin Dossier field on an already-booted API 36 emulator."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


FIELDS: dict[str, tuple[str, str, str, str]] = {
    "txtCharacterName": ("identity", "name", "Name", "origin-name"),
    "txtAlias": ("identity", "alias", "Alias", "origin-alias"),
    "txtPlayerName": ("identity", "playername", "Player", "origin-player-name"),
    "txtGender": ("appearance", "sex", "Sex", "origin-sex"),
    "txtAge": ("appearance", "age", "Age", "origin-age"),
    "txtHeight": ("appearance", "height", "Height", "origin-height"),
    "txtWeight": ("appearance", "weight", "Weight", "origin-weight"),
    "txtHair": ("appearance", "hair", "Hair", "origin-hair"),
    "txtEyes": ("appearance", "eyes", "Eyes", "origin-eyes"),
    "txtSkin": ("appearance", "skin", "Skin", "origin-skin"),
    "rtfConcept": ("story", "concept", "Concept", "origin-concept"),
    "rtfDescription": ("story", "description", "Description", "origin-description"),
    "rtfBackground": ("story", "background", "Background", "origin-background"),
}
SECTIONS = {
    "identity": ("origin-dossier-identity", "origin-dossier-identity-save"),
    "appearance": ("origin-dossier-appearance", "origin-dossier-appearance-save"),
    "story": ("origin-dossier-story", "origin-dossier-story-save"),
}
CASE_VALUES = {
    "CharacterCreate": {
        "txtCharacterName": "CreateNameE2E",
        "txtAlias": "CreateAliasE2E",
        "txtPlayerName": "CreatePlayerE2E",
        "txtGender": "CreateGenderE2E",
        "txtAge": "31",
        "txtHeight": "181cm",
        "txtWeight": "76kg",
        "txtHair": "CreateHairE2E",
        "txtEyes": "CreateEyesE2E",
        "txtSkin": "CreateSkinE2E",
        "rtfConcept": "CreateConceptE2E",
        "rtfDescription": "CreateDescriptionE2E",
        "rtfBackground": "CreateBackgroundE2E",
    },
    "CharacterCareer": {
        "txtCharacterName": "CareerNameE2E",
        "txtAlias": "CareerAliasE2E",
        "txtPlayerName": "CareerPlayerE2E",
        "txtGender": "CareerGenderE2E",
        "txtAge": "42",
        "txtHeight": "192cm",
        "txtWeight": "88kg",
        "txtHair": "CareerHairE2E",
        "txtEyes": "CareerEyesE2E",
        "txtSkin": "CareerSkinE2E",
        "rtfConcept": "CareerConceptE2E",
        "rtfDescription": "CareerDescriptionE2E",
        "rtfBackground": "CareerBackgroundE2E",
    },
}
CONTROL_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)


def find_exact(device: shared.Device, selector: str) -> shared.UiNode | None:
    matches: list[shared.UiNode] = []
    for node in device.hierarchy():
        attributes = node.attributes
        resource_id = attributes.get("resource-id", "").rsplit("/", 1)[-1]
        if selector in {
            attributes.get("text", ""),
            attributes.get("content-desc", ""),
            resource_id,
        }:
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
    max_scrolls: int = 32,
) -> shared.UiNode:
    deadline = time.monotonic() + timeout
    scrolls = 0
    while time.monotonic() < deadline:
        node = find_exact(device, selector)
        if node is not None:
            return node
        if device.dismiss_system_ui_anr():
            time.sleep(2)
            continue
        if scrolls < max_scrolls:
            device.swipe_up(distance_ratio=0.22)
            scrolls += 1
        time.sleep(0.75)
    device.capture(f"missing-origin-field-{selector}")
    raise RuntimeError(f"Timed out waiting for exact Origin Dossier field {selector!r}")


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


def assert_workspace_origin(
    device: shared.Device,
    expected: dict[str, str],
    form_name: str,
) -> None:
    expected_xml = {
        FIELDS[control][1]: value
        for control, value in expected.items()
    }
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        values = {
            element: character.findtext(element, default="")
            for element in expected_xml
        }
        observed.append(values)
        if values == expected_xml:
            return
    device.capture(f"{form_name.lower()}-origin-workspace-not-persisted")
    raise RuntimeError(
        f"{form_name} Origin Dossier values were not durable in workspace XML; "
        f"observed {observed!r}"
    )


def assert_editor_fields(
    device: shared.Device,
    section: str,
    expected: dict[str, str],
    form_name: str,
) -> None:
    shared.reset_scroll_to_top(device, swipes=12)
    for control, (field_section, _element, _label, selector) in FIELDS.items():
        if field_section != section:
            continue
        node = wait_exact_field(device, selector)
        actual = node.attributes.get("text", "")
        wanted = expected[control]
        if actual != wanted:
            device.capture(f"{form_name.lower()}-origin-readback-{control.lower()}")
            raise RuntimeError(
                f"{form_name} field {control!r} rendered {actual!r}, expected {wanted!r}"
            )


def edit_origin_dossier(
    device: shared.Device,
    expected: dict[str, str],
    form_name: str,
) -> None:
    shared.open_build(device, "phone")
    shared.open_origin_dossier(device, "phone")
    for section, (row_selector, save_selector) in SECTIONS.items():
        device.tap(row_selector, scroll=True, max_scrolls=16, scroll_distance_ratio=0.22)
        shared.reset_scroll_to_top(device, swipes=12)
        for control, (field_section, _element, label, selector) in FIELDS.items():
            if field_section != section:
                continue
            device.set_text(
                selector,
                label,
                expected[control],
                scroll=True,
                max_scrolls=32,
                scroll_distance_ratio=0.22,
            )
        device.tap(
            save_selector,
            scroll=True,
            timeout=240,
            max_scrolls=48,
            scroll_distance_ratio=0.22,
        )
        time.sleep(1)
        assert_editor_fields(device, section, expected, form_name)
        device.back()
    device.back()


def assert_origin_ui_after_restart(
    device: shared.Device,
    expected: dict[str, str],
    form_name: str,
) -> None:
    shared.open_build(device, "phone")
    shared.open_origin_dossier(device, "phone")
    for section, (row_selector, _save_selector) in SECTIONS.items():
        device.tap(row_selector, scroll=True, max_scrolls=16, scroll_distance_ratio=0.22)
        assert_editor_fields(device, section, expected, form_name)
        device.back()
    device.back()


def prepare_creation_runner(device: shared.Device) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True, max_scrolls=16)
    device.wait(
        "dialog-action-complete-new-character-workflow",
        timeout=60,
        scroll=True,
        max_scrolls=16,
    )
    device.tap(
        "dialog-action-complete-new-character-workflow",
        scroll=True,
        max_scrolls=16,
    )
    shared.wait_for_phone_runner_route(device, timeout=120)


def prepare_career_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def exercise_case(
    device: shared.Device,
    form_name: str,
    fixture_name: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    if form_name == "CharacterCreate":
        prepare_creation_runner(device)
    else:
        prepare_career_runner(device, fixture_name)
    expected = CASE_VALUES[form_name]
    edit_origin_dossier(device, expected, form_name)
    assert_workspace_origin(device, expected, form_name)
    device.capture(f"phone-origin-{form_name.lower()}-persisted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_origin(device, expected, form_name)
    assert_origin_ui_after_restart(device, expected, form_name)
    device.capture(f"phone-origin-{form_name.lower()}-after-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "career-condition-monitor-e2e.chum5",
    )
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    android_root = driver_path.parents[1]
    presentation_root = args.workspace_root.resolve() / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "originDossierPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "OriginDossierPage.cs",
        "runnerSessionCoordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "workspaceMutationsSha256": presentation_root / "CharacterOverviewPresenter.WorkspaceMutations.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Origin Dossier E2E source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Origin Dossier E2E requires API 36, got {api!r}")
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
    device.push(fixture, f"/sdcard/Download/{fixture.name}")

    for form_name in CASE_VALUES:
        print(f"origin-dossier e2e: exercising {form_name} (13 fields)", flush=True)
        exercise_case(device, form_name, fixture.name)

    control_proofs = {
        f"{form_name}.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for form_name in CASE_VALUES
        for control in FIELDS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "origin-dossier",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver_path),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixture": str(fixture),
        "careerFixtureSha256": shared.sha256(fixture),
        "controlCount": len(control_proofs),
        "controls": control_proofs,
        "journeys": {
            "creationRunnerCreated": "pass",
            "allCreationOriginFieldsEdited": "pass",
            "creationWorkspaceXmlPersisted": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerRunnerImported": "pass",
            "allCareerOriginFieldsEdited": "pass",
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
        print(f"origin-dossier E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
