#!/usr/bin/env python3
"""Prove Chummer5 creation/career primary-arm behavior on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "cboPrimaryArm"
CONTROL_PROOF_KEYS = ("mutated", "workspacePersisted", "processRestartUiReadback")


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_primary_arm(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-primary-arm",
        scroll=True,
        timeout=60,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait("primary-arm", timeout=60)
    device.wait("primary-arm-choice", timeout=45)


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


def assert_workspace_value(device: shared.Device, expected: str) -> None:
    observed: list[str] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        value = character.findtext("primaryarm", default="")
        observed.append(value)
        if value == expected:
            return
    device.capture("primary-arm-workspace-not-persisted")
    raise RuntimeError(f"Primary arm {expected!r} was not durable; observed {observed!r}")


def assert_ui_value(device: shared.Device, expected: str) -> None:
    actual = shared.selected_text(
        device,
        "primary-arm-choice",
        "primary-arm-choice",
        scroll=True,
    )
    if actual != expected:
        device.capture("primary-arm-ui-readback-mismatch")
        raise RuntimeError(f"Primary arm expected {expected!r}, got {actual!r}")


def edit_primary_arm(device: shared.Device, expected: str) -> None:
    open_primary_arm(device)
    device.tap("primary-arm-choice", timeout=60)
    device.tap(expected, timeout=60)
    assert_ui_value(device, expected)
    device.tap("primary-arm-save", timeout=240)
    device.wait(
        "build-primary-arm",
        timeout=120,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )


def prove_editable_profile(
    device: shared.Device,
    fixture: Path,
    profile: str,
    expected: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    edit_primary_arm(device, expected)
    assert_workspace_value(device, expected)
    open_primary_arm(device)
    assert_ui_value(device, expected)
    device.capture(f"primary-arm-{profile.lower()}-after-reopen")
    device.back()
    device.back()

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_value(device, expected)
    open_primary_arm(device)
    assert_ui_value(device, expected)
    device.capture(f"primary-arm-{profile.lower()}-after-process-restart")


def prove_ambidextrous_gate(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_primary_arm(device)
    picker = device.wait("primary-arm-choice", timeout=45)
    save = device.wait("primary-arm-save", timeout=45, scroll=True, max_scrolls=8)
    actual = shared.selected_text(
        device,
        "primary-arm-choice",
        "primary-arm-choice",
        scroll=True,
    )
    if (
        actual != "Ambidextrous"
        or picker.attributes.get("enabled") != "false"
        or save.attributes.get("enabled") != "false"
    ):
        device.capture("primary-arm-ambidextrous-gate-failed")
        raise RuntimeError(
            "Ambidextrous primary arm was not projected read-only: "
            f"value={actual!r}, pickerEnabled={picker.attributes.get('enabled')!r}, "
            f"saveEnabled={save.attributes.get('enabled')!r}"
        )
    assert_workspace_value(device, "Right")
    device.capture("primary-arm-ambidextrous-gate")


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
        default=fixtures / "creation-primary-arm-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-primary-arm-e2e.chum5",
    )
    parser.add_argument(
        "--ambidextrous-runner",
        type=Path,
        default=fixtures / "ambidextrous-primary-arm-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "primaryArmPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "PrimaryArmPage.cs",
        "buildPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "primaryArmContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "PrimaryArmEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "profileContractSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "sectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Primary-arm E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    ambidextrous_fixture = args.ambidextrous_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Primary-arm E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture, ambidextrous_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    prove_editable_profile(device, creation_fixture, "CharacterCreate", "Left")
    prove_editable_profile(device, career_fixture, "CharacterCareer", "Right")
    prove_ambidextrous_gate(device, ambidextrous_fixture)

    controls = {
        f"{profile}.{CONTROL}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for profile in ("CharacterCreate", "CharacterCareer")
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "primary-arm",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "ambidextrousFixtureSha256": shared.sha256(ambidextrous_fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "creationRunnerImported": "pass",
            "creationPrimaryArmEdited": "pass",
            "creationWorkspaceXmlPersisted": "pass",
            "creationUiReopenReadback": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerRunnerImported": "pass",
            "careerPrimaryArmEdited": "pass",
            "careerWorkspaceXmlPersisted": "pass",
            "careerUiReopenReadback": "pass",
            "careerProcessRestartUiReadback": "pass",
            "ambidextrousReadOnlyGateEnforced": "pass",
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
        print(f"primary-arm E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
