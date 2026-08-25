#!/usr/bin/env python3
"""Prove all Chummer5 creation/career location renames on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
)
LOCATION_KINDS = (
    ("Gear", "gearlocations", "tsGearRenameLocation"),
    ("Weapon", "weaponlocations", "tsWeaponRenameLocation"),
    ("Armor", "armorlocations", "tsArmorRenameLocation"),
    ("Vehicle", "vehiclelocations", "tsVehicleRenameLocation"),
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "Gear": ("11111111-1111-1111-1111-111111111111", "Creation Gear Renamed E2E", "Creation Gear Notes E2E"),
        "Weapon": ("12121212-1212-1212-1212-121212121212", "Creation Weapon Renamed E2E", "Creation Weapon Notes E2E"),
        "Armor": ("13131313-1313-1313-1313-131313131313", "Creation Armor Renamed E2E", "Creation Armor Notes E2E"),
        "Vehicle": ("14141414-1414-1414-1414-141414141414", "Creation Vehicle Renamed E2E", "Creation Vehicle Notes E2E"),
    },
    "CharacterCareer": {
        "Gear": ("21111111-2111-2111-2111-211111111111", "Career Gear Renamed E2E", "Career Gear Notes E2E"),
        "Weapon": ("22222222-2222-2222-2222-222222222222", "Career Weapon Renamed E2E", "Career Weapon Notes E2E"),
        "Armor": ("23333333-2333-2333-2333-233333333333", "Career Armor Renamed E2E", "Career Armor Notes E2E"),
        "Vehicle": ("24444444-2444-2444-2444-244444444444", "Career Vehicle Renamed E2E", "Career Vehicle Notes E2E"),
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_location_section(
    device: shared.Device,
    kind: str,
    section_id: str,
    location_id: str,
) -> str:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-section-tab-gear",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"build-action-tab-gear-{section_id}",
        scroll=True,
        timeout=120,
        max_scrolls=30,
        scroll_distance_ratio=0.22,
    )
    selector = f"location-rename-open-{kind.lower()}-{location_id.replace('-', '').lower()}"
    device.wait(
        selector,
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    return selector


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


def assert_workspace_locations(
    device: shared.Device,
    expected: dict[str, tuple[str, str, str]],
) -> None:
    observations: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue

        observed: dict[str, str] = {}
        complete = True
        for kind, section_id, _control in LOCATION_KINDS:
            location_id, expected_name, expected_notes = expected[kind]
            matches = [
                location
                for location in character.findall(f"./{section_id}/location")
                if location.findtext("guid", default="").lower() == location_id.lower()
            ]
            if len(matches) != 1:
                complete = False
                break
            location = matches[0]
            name = location.findtext("name", default="")
            notes = location.findtext("notes", default="")
            observed[kind] = name
            if name != expected_name or notes != expected_notes:
                complete = False
                break
        observations.append(observed)
        if complete:
            return

    device.capture("location-rename-workspace-not-persisted")
    raise RuntimeError(f"Location renames were not durably persisted: {observations!r}")


def rename_location(
    device: shared.Device,
    kind: str,
    section_id: str,
    location_id: str,
    new_name: str,
) -> None:
    selector = open_location_section(device, kind, section_id, location_id)
    device.tap(selector, timeout=60, scroll=True)
    device.wait("location-rename-page", timeout=60)
    device.set_text("location-rename-name", "Location name", new_name)
    device.tap("location-rename-save", timeout=240, scroll=True)
    device.wait(selector, timeout=120, scroll=True)


def prove_profile(
    device: shared.Device,
    fixture: Path,
    profile: str,
) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    for kind, section_id, _control in LOCATION_KINDS:
        location_id, new_name, _notes = expected[kind]
        rename_location(device, kind, section_id, location_id, new_name)

    assert_workspace_locations(device, expected)
    for kind, section_id, _control in LOCATION_KINDS:
        location_id, new_name, _notes = expected[kind]
        open_location_section(device, kind, section_id, location_id)
        device.assert_text(new_name, timeout=30)
        device.capture(f"location-rename-{profile.lower()}-{kind.lower()}-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_locations(device, expected)
    for kind, section_id, _control in LOCATION_KINDS:
        location_id, new_name, _notes = expected[kind]
        open_location_section(device, kind, section_id, location_id)
        device.assert_text(new_name, timeout=30)
        device.capture(f"location-rename-{profile.lower()}-{kind.lower()}-after-process-restart")


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
        default=fixtures / "creation-location-rename-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-location-rename-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "locationRenamePageSha256": android_root / "src" / "Chummer.Android" / "Native" / "LocationRenamePage.cs",
        "buildFlowPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "locationStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceLocationEditorState.cs",
        "locationRenameContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "LocationRenameRequest.cs",
        "sectionRendererSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceSectionRenderer.cs",
        "overviewStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewState.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Location-rename E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Location-rename E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    prove_profile(device, creation_fixture, "CharacterCreate")
    prove_profile(device, career_fixture, "CharacterCareer")

    controls = {
        f"{profile}.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for profile in ("CharacterCreate", "CharacterCareer")
        for _kind, _section_id, control in LOCATION_KINDS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "location-rename",
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
            "creationAllLocationsRenamed": "pass",
            "creationWorkspaceXmlPersisted": "pass",
            "creationAllSurfacesReopened": "pass",
            "creationProcessRestartPersistence": "pass",
            "careerRunnerImported": "pass",
            "careerAllLocationsRenamed": "pass",
            "careerWorkspaceXmlPersisted": "pass",
            "careerAllSurfacesReopened": "pass",
            "careerProcessRestartPersistence": "pass",
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
        print(f"location-rename E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
