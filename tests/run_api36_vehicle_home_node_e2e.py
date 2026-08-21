#!/usr/bin/env python3
"""Prove exact Chummer5 vehicle Home Node behavior on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "chkVehicleHomeNode"
CONTROL_PROOF_KEYS = (
    "enabledAsExclusiveHomeNode",
    "disabledFromHomeNode",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "vehicle_id": "51222222-5122-5122-5122-512222222222",
        "other_vehicle_id": "51333333-5133-5133-5133-513333333333",
        "gear_id": "51111111-5111-5111-5111-511111111111",
        "target_notes": "Creation target notes must remain intact",
        "other_notes": "Creation untouched notes",
        "gear_notes": "Creation gear must remain intact",
    },
    "CharacterCareer": {
        "vehicle_id": "52222222-5222-5222-5222-522222222222",
        "other_vehicle_id": "52333333-5233-5233-5233-523333333333",
        "gear_id": "52111111-5211-5211-5211-521111111111",
        "target_notes": "Career target notes must remain intact",
        "other_notes": "Career untouched notes",
        "gear_notes": "Career gear must remain intact",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_selected_vehicle(device: shared.Device, vehicle_id: str) -> None:
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
        "build-action-tab-gear-vehicles",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"collection-item-vehicle-{vehicle_id}",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.wait(f"collection-editor-vehicle-{vehicle_id}", timeout=120)
    device.wait(
        f"vehicle-home-node-open-{vehicle_id.replace('-', '')}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )


def open_home_node_page(device: shared.Device, vehicle_id: str) -> None:
    compact_id = vehicle_id.replace("-", "")
    open_selected_vehicle(device, vehicle_id)
    device.tap(f"vehicle-home-node-open-{compact_id}", timeout=60, scroll=True)
    device.wait(f"vehicle-home-node-page-{compact_id}", timeout=60)


def assert_toggle(device: shared.Device, vehicle_id: str, expected: bool) -> None:
    node = device.wait(f"vehicle-home-node-toggle-{vehicle_id.replace('-', '')}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("vehicle-home-node-toggle-mismatch")
        raise RuntimeError(f"Vehicle Home Node switch was {observed!r}; expected {expected!r}")


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


def assert_workspace_home_node(device: shared.Device, expected: dict[str, str], enabled: bool) -> None:
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        vehicles = {
            vehicle.findtext("guid", default="").lower(): vehicle
            for vehicle in character.findall("./vehicles/vehicle")
        }
        gears = {
            gear.findtext("guid", default="").lower(): gear
            for gear in character.findall("./gears/gear")
        }
        target = vehicles.get(expected["vehicle_id"])
        other = vehicles.get(expected["other_vehicle_id"])
        gear = gears.get(expected["gear_id"])
        if target is None or other is None or gear is None:
            continue
        target_flag = target.findtext("homenode", default="")
        other_flag = other.findtext("homenode", default="")
        gear_flag = gear.findtext("homenode", default="")
        observed.append({"target": target_flag, "other": other_flag, "gear": gear_flag})
        if (
            target_flag == ("True" if enabled else "False")
            and other_flag == "False"
            and gear_flag == "False"
            and target.findtext("notes", default="") == expected["target_notes"]
            and other.findtext("notes", default="") == expected["other_notes"]
            and gear.findtext("notes", default="") == expected["gear_notes"]
        ):
            true_flags = [
                node for node in character.iter("homenode")
                if (node.text or "").strip().lower() == "true"
            ]
            if len(true_flags) == (1 if enabled else 0):
                return
    device.capture("vehicle-home-node-workspace-not-persisted")
    raise RuntimeError(f"Vehicle Home Node invariant was not durable; observed {observed!r}")


def set_home_node(device: shared.Device, vehicle_id: str, enabled: bool) -> None:
    compact_id = vehicle_id.replace("-", "")
    open_home_node_page(device, vehicle_id)
    assert_toggle(device, vehicle_id, not enabled)
    device.tap(f"vehicle-home-node-toggle-{compact_id}", timeout=60)
    assert_toggle(device, vehicle_id, enabled)
    device.tap(f"vehicle-home-node-save-{compact_id}", timeout=240, scroll=True)
    device.wait(f"vehicle-home-node-open-{compact_id}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    set_home_node(device, expected["vehicle_id"], True)
    assert_workspace_home_node(device, expected, True)
    open_home_node_page(device, expected["vehicle_id"])
    assert_toggle(device, expected["vehicle_id"], True)
    device.capture(f"vehicle-home-node-{profile.lower()}-enabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_home_node(device, expected, True)
    open_home_node_page(device, expected["vehicle_id"])
    assert_toggle(device, expected["vehicle_id"], True)
    device.capture(f"vehicle-home-node-{profile.lower()}-enabled-after-process-restart")

    compact_id = expected["vehicle_id"].replace("-", "")
    device.tap(f"vehicle-home-node-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected["vehicle_id"], False)
    device.tap(f"vehicle-home-node-save-{compact_id}", timeout=240, scroll=True)
    assert_workspace_home_node(device, expected, False)
    open_home_node_page(device, expected["vehicle_id"])
    assert_toggle(device, expected["vehicle_id"], False)
    device.capture(f"vehicle-home-node-{profile.lower()}-disabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_home_node(device, expected, False)
    open_home_node_page(device, expected["vehicle_id"])
    assert_toggle(device, expected["vehicle_id"], False)
    device.capture(f"vehicle-home-node-{profile.lower()}-disabled-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-vehicle-home-node-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-vehicle-home-node-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "vehicleHomeNodePageSha256": android_root / "src" / "Chummer.Android" / "Native" / "VehicleHomeNodePage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "vehicleHomeNodeContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "VehicleHomeNodeEditRequest.cs",
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
        raise RuntimeError(f"Vehicle Home Node E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Vehicle Home Node E2E requires API 36, got {api!r}")

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
        "journey": "vehicle-home-node",
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
            "creationVehicleEnabledExclusive": "pass",
            "creationVehicleEnabledReopened": "pass",
            "creationVehicleEnabledProcessRestart": "pass",
            "creationVehicleDisabled": "pass",
            "creationVehicleDisabledReopened": "pass",
            "creationVehicleDisabledProcessRestart": "pass",
            "careerRunnerImported": "pass",
            "careerVehicleEnabledExclusive": "pass",
            "careerVehicleEnabledReopened": "pass",
            "careerVehicleEnabledProcessRestart": "pass",
            "careerVehicleDisabled": "pass",
            "careerVehicleDisabledReopened": "pass",
            "careerVehicleDisabledProcessRestart": "pass",
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
        print(f"vehicle Home Node E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
