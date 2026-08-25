#!/usr/bin/env python3
"""Prove both Chummer5 vehicle-location add branches on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "cmdAddVehicleLocation"
CONTROL_PROOF_KEYS = (
    "globalBranchMutated",
    "selectedVehicleBranchMutated",
    "workspacePersisted",
    "bothSurfacesReopened",
    "processRestartWorkspacePersisted",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "global_added": "Creation Added Global Vehicle Location E2E",
        "global_existing": "Creation Existing Global Vehicle Location E2E",
        "global_notes": "Creation existing global vehicle notes E2E",
        "vehicle_id": "41222222-4122-4122-4122-412222222222",
        "vehicle_name": "Creation Roadmaster E2E",
        "nested_added": "Creation Added Nested Vehicle Location E2E",
        "nested_existing": "Creation Existing Nested Vehicle Location E2E",
        "nested_notes": "Creation existing nested vehicle notes E2E",
        "untouched_vehicle_id": "41444444-4144-4144-4144-414444444444",
        "untouched_name": "Creation Untouched Nested Vehicle Location E2E",
        "untouched_notes": "Creation untouched nested vehicle notes E2E",
    },
    "CharacterCareer": {
        "global_added": "Career Added Global Vehicle Location E2E",
        "global_existing": "Career Existing Global Vehicle Location E2E",
        "global_notes": "Career existing global vehicle notes E2E",
        "vehicle_id": "42222222-4222-4222-4222-422222222222",
        "vehicle_name": "Career Roadmaster E2E",
        "nested_added": "Career Added Nested Vehicle Location E2E",
        "nested_existing": "Career Existing Nested Vehicle Location E2E",
        "nested_notes": "Career existing nested vehicle notes E2E",
        "untouched_vehicle_id": "42444444-4244-4244-4244-424444444444",
        "untouched_name": "Career Untouched Nested Vehicle Location E2E",
        "untouched_notes": "Career untouched nested vehicle notes E2E",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_gear_action(device: shared.Device, action: str) -> None:
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
        f"build-action-tab-gear-{action}",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )


def open_global_vehicle_locations(device: shared.Device) -> None:
    open_gear_action(device, "vehiclelocations")
    device.wait(
        "vehicle-location-open-add-global",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )


def open_selected_vehicle(device: shared.Device, vehicle_id: str) -> None:
    open_gear_action(device, "vehicles")
    target = f"collection-item-vehicle-{vehicle_id}"
    device.tap(target, timeout=120, scroll=True, max_scrolls=24, scroll_distance_ratio=0.22)
    device.wait(f"collection-editor-vehicle-{vehicle_id}", timeout=120)
    device.wait(
        f"vehicle-location-open-add-{vehicle_id.replace('-', '')}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
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


def _has_stable_empty_location(locations: list[ET.Element], name: str) -> bool:
    matches = [location for location in locations if location.findtext("name", default="") == name]
    if len(matches) != 1 or matches[0].find("notes") is None:
        return False
    try:
        location_id = uuid.UUID(matches[0].findtext("guid", default=""))
    except (ValueError, AttributeError):
        return False
    return location_id.int != 0 and (matches[0].findtext("notes", default="") or "") == ""


def assert_workspace_locations(device: shared.Device, expected: dict[str, str]) -> None:
    observed: list[dict[str, list[str]]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        global_locations = character.findall("./vehiclelocations/location")
        vehicles = character.findall("./vehicles/vehicle")
        targets = [vehicle for vehicle in vehicles if vehicle.findtext("guid", default="").lower() == expected["vehicle_id"]]
        untouched = [vehicle for vehicle in vehicles if vehicle.findtext("guid", default="").lower() == expected["untouched_vehicle_id"]]
        if len(targets) != 1 or len(untouched) != 1:
            continue
        nested_locations = targets[0].findall("./locations/location")
        untouched_locations = untouched[0].findall("./locations/location")
        observed.append(
            {
                "global": [location.findtext("name", default="") for location in global_locations],
                "nested": [location.findtext("name", default="") for location in nested_locations],
                "untouched": [location.findtext("name", default="") for location in untouched_locations],
            }
        )
        global_existing = [location for location in global_locations if location.findtext("name", default="") == expected["global_existing"]]
        nested_existing = [location for location in nested_locations if location.findtext("name", default="") == expected["nested_existing"]]
        if (
            len(global_locations) == 2
            and len(nested_locations) == 2
            and len(untouched_locations) == 1
            and _has_stable_empty_location(global_locations, expected["global_added"])
            and _has_stable_empty_location(nested_locations, expected["nested_added"])
            and len(global_existing) == 1
            and (global_existing[0].findtext("notes", default="") or "") == expected["global_notes"]
            and len(nested_existing) == 1
            and (nested_existing[0].findtext("notes", default="") or "") == expected["nested_notes"]
            and untouched_locations[0].findtext("name", default="") == expected["untouched_name"]
            and (untouched_locations[0].findtext("notes", default="") or "") == expected["untouched_notes"]
        ):
            return
    device.capture("vehicle-location-workspace-not-persisted")
    raise RuntimeError(f"Both vehicle-location branches were not durably isolated; observed {observed!r}")


def add_global_location(device: shared.Device, name: str) -> None:
    open_global_vehicle_locations(device)
    device.tap("vehicle-location-open-add-global", timeout=60, scroll=True)
    device.wait("vehicle-location-add-page-global", timeout=60)
    device.set_text("vehicle-location-name-global", "Location name", name)
    device.tap("vehicle-location-add-global", timeout=240, scroll=True)
    device.wait("vehicle-location-open-add-global", timeout=120, scroll=True)


def add_selected_vehicle_location(device: shared.Device, vehicle_id: str, name: str) -> None:
    compact_id = vehicle_id.replace("-", "")
    open_selected_vehicle(device, vehicle_id)
    device.tap(f"vehicle-location-open-add-{compact_id}", timeout=60, scroll=True)
    device.wait(f"vehicle-location-add-page-{compact_id}", timeout=60)
    device.set_text(f"vehicle-location-name-{compact_id}", "Location name", name)
    device.tap(f"vehicle-location-add-{compact_id}", timeout=240, scroll=True)
    device.wait(f"vehicle-location-open-add-{compact_id}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    add_global_location(device, expected["global_added"])
    add_selected_vehicle_location(device, expected["vehicle_id"], expected["nested_added"])
    assert_workspace_locations(device, expected)

    open_global_vehicle_locations(device)
    device.assert_text(expected["global_added"], timeout=30)
    open_selected_vehicle(device, expected["vehicle_id"])
    device.assert_text(expected["nested_added"], timeout=30)
    device.capture(f"vehicle-location-{profile.lower()}-both-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_locations(device, expected)
    open_global_vehicle_locations(device)
    device.assert_text(expected["global_added"], timeout=30)
    open_selected_vehicle(device, expected["vehicle_id"])
    device.assert_text(expected["nested_added"], timeout=30)
    device.capture(f"vehicle-location-{profile.lower()}-both-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-vehicle-location-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-vehicle-location-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "vehicleLocationPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "VehicleLocationAddPage.cs",
        "buildFlowPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "vehicleLocationContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "VehicleLocationAddRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Vehicle-location E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Vehicle-location E2E requires API 36, got {api!r}")

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
        f"{profile}.{CONTROL}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for profile in ("CharacterCreate", "CharacterCareer")
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "vehicle-location-add",
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
            "creationGlobalVehicleLocationAdded": "pass",
            "creationSelectedVehicleLocationAdded": "pass",
            "creationBothBranchesWorkspaceXmlPersisted": "pass",
            "creationBothSurfacesReopened": "pass",
            "creationProcessRestartPersistence": "pass",
            "careerRunnerImported": "pass",
            "careerGlobalVehicleLocationAdded": "pass",
            "careerSelectedVehicleLocationAdded": "pass",
            "careerBothBranchesWorkspaceXmlPersisted": "pass",
            "careerBothSurfacesReopened": "pass",
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
        print(f"vehicle-location E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
