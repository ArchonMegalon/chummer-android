#!/usr/bin/env python3
"""Prove Chummer5 counterspelling and lift/carry controls on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS: dict[str, tuple[str, str]] = {
    "nudCounterspellingDice": (
        "currentcounterspellingdice",
        "situational-counterspelling-dice",
    ),
    "nudLiftCarryHits": ("currentliftcarryhits", "situational-lift-carry-hits"),
}
TARGETS = {
    "CharacterCreate": {
        "currentcounterspellingdice": 31,
        "currentliftcarryhits": 32,
    },
    "CharacterCareer": {
        "currentcounterspellingdice": 41,
        "currentliftcarryhits": 42,
    },
}
CONTROL_PROOF_KEYS = ("mutated", "workspacePersisted", "processRestartUiReadback")


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_modifiers(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-situational-modifiers",
        scroll=True,
        timeout=60,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait("situational-modifiers", timeout=60)
    device.wait("situational-counterspelling-dice", timeout=45)


def select_picker_value(device: shared.Device, selector: str, value: int) -> None:
    device.tap(
        selector,
        scroll=True,
        timeout=60,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.tap(str(value), timeout=60, scroll=True, max_scrolls=8)
    time.sleep(0.5)
    actual = shared.selected_text(device, selector, selector, scroll=True)
    if actual != str(value):
        device.capture(f"{selector}-value-not-selected")
        raise RuntimeError(f"{selector} expected {value}, got {actual!r}")


def edit_modifiers(device: shared.Device, targets: dict[str, int]) -> None:
    open_modifiers(device)
    shared.reset_scroll_to_top(device, swipes=12)
    for element, selector in CONTROLS.values():
        select_picker_value(device, selector, targets[element])
    device.tap(
        "situational-modifiers-save",
        scroll=True,
        timeout=240,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait(
        "build-situational-modifiers",
        timeout=120,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def assert_workspace_values(device: shared.Device, targets: dict[str, int]) -> None:
    expected = {element: str(value) for element, value in targets.items()}
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        values = {element: character.findtext(element, default="") for element in expected}
        observed.append(values)
        if values == expected:
            return
    device.capture("situational-modifiers-workspace-not-persisted")
    raise RuntimeError(f"Situational modifiers were not durable in workspace XML; observed {observed!r}")


def assert_ui_values(device: shared.Device, targets: dict[str, int]) -> None:
    shared.reset_scroll_to_top(device, swipes=12)
    for element, selector in CONTROLS.values():
        actual = shared.selected_text(device, selector, selector, scroll=True)
        if actual != str(targets[element]):
            device.capture(f"{selector}-readback-mismatch")
            raise RuntimeError(f"{selector} expected {targets[element]}, got {actual!r}")


def prove_profile(
    device: shared.Device,
    fixture: Path,
    profile: str,
) -> None:
    targets = TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    edit_modifiers(device, targets)
    assert_workspace_values(device, targets)
    open_modifiers(device)
    assert_ui_values(device, targets)
    device.capture(f"situational-modifiers-{profile.lower()}-after-reopen")
    device.back()
    device.back()

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_values(device, targets)
    open_modifiers(device)
    assert_ui_values(device, targets)
    device.capture(f"situational-modifiers-{profile.lower()}-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument(
        "--creation-runner",
        type=Path,
        default=fixtures / "creation-situational-modifiers-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-situational-modifiers-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "modifiersPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "SituationalModifiersPage.cs",
        "buildPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "modifiersContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "SituationalModifiersEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "progressContractSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "sectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Situational-modifier E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Situational-modifier E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(creation_fixture, f"/sdcard/Download/{creation_fixture.name}")
    device.push(career_fixture, f"/sdcard/Download/{career_fixture.name}")

    prove_profile(device, creation_fixture, "CharacterCreate")
    prove_profile(device, career_fixture, "CharacterCareer")

    controls = {
        f"{profile}.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for profile in TARGETS
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "situational-modifiers",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "creationRunnerImported": "pass",
            "allCreationSituationalModifiersEdited": "pass",
            "creationWorkspaceXmlPersisted": "pass",
            "creationUiReopenReadback": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerRunnerImported": "pass",
            "allCareerSituationalModifiersEdited": "pass",
            "careerWorkspaceXmlPersisted": "pass",
            "careerUiReopenReadback": "pass",
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
        print(f"situational-modifier E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
