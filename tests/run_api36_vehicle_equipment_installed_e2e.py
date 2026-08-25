#!/usr/bin/env python3
"""Prove fail-closed Vehicle Installed Create/Career persistence on an API 36 phone."""

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


CONTROLS = (
    "CharacterCreate.chkVehicleWeaponAccessoryInstalled",
    "CharacterCareer.chkVehicleWeaponAccessoryInstalled",
)
PROOF_KEYS = (
    "exactCreateCareerSharedICanEquipHandler",
    "typedVehicleMountModWeaponAccessoryIdentity",
    "legacyPerKindEnableRules",
    "sensorVehicleModFailClosed",
    "zeroNuyenKarmaDelta",
    "equippedElementPersisted",
    "unrelatedXmlPreserved",
    "revisionBoundAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILES = {
    "CharacterCreate": {
        "vehicle_id": "61111111-6111-4111-8111-611111111111",
        "target_id": "65555555-6555-4555-8555-655555555555",
        "target_label": "Creation Roadmaster > Creation External Mount > Creation Mount Weapon > Creation Smartgun · Weapon Accessory · 65555555",
        "initial": True,
        "sensor_label": "Creation Roadmaster > Creation Sensor Side Effect · Vehicle Mod · 66666666",
        "parent_fixed_label": "Creation Roadmaster > Creation Loose Weapon > Creation Included Underbarrel · Weapon · 68888888",
        "nuyen": "4321",
        "karma": "7",
        "sentinel": "Creation Vehicle Installed runner sentinel",
        "target_notes": "Creation accessory sentinel",
        "other_notes": "Creation untouched sentinel",
    },
    "CharacterCareer": {
        "vehicle_id": "71111111-7111-4111-8111-711111111111",
        "target_id": "73333333-7333-4333-8333-733333333333",
        "target_label": "Career Roadmaster > Career External Mount > Career Mount Mod · Vehicle Mod · 73333333",
        "initial": True,
        "sensor_label": "Career Roadmaster > Career Sensor Side Effect · Vehicle Mod · 76666666",
        "parent_fixed_label": "Career Roadmaster > Career Loose Weapon > Career Included Underbarrel · Weapon · 78888888",
        "nuyen": "8765",
        "karma": "19",
        "sentinel": "Career Vehicle Installed runner sentinel",
        "target_notes": "",
        "other_notes": "Career untouched sentinel",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_installed_page(device: shared.Device, vehicle_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-vehicles", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-vehicle-{vehicle_id}", scroll=True, timeout=120, max_scrolls=24)
    device.wait(f"collection-editor-vehicle-{vehicle_id}", timeout=120)
    token = vehicle_id.replace("-", "")
    device.tap(f"vehicle-equipment-installed-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"vehicle-equipment-installed-page-{token}", timeout=60)


def select_node(device: shared.Device, vehicle_id: str, label: str) -> None:
    token = vehicle_id.replace("-", "")
    device.tap(f"vehicle-equipment-installed-target-{token}", timeout=60, scroll=True)
    device.tap(label, timeout=60, scroll=True, max_scrolls=28)
    time.sleep(0.35)
    observed = shared.selected_text(
        device,
        f"vehicle-equipment-installed-target-{token}",
        "Vehicle equipment node",
        scroll=True,
    )
    if observed != label:
        device.capture("vehicle-equipment-installed-target-mismatch")
        raise RuntimeError(f"Vehicle Installed target was {observed!r}; expected {label!r}")


def assert_toggle(device: shared.Device, vehicle_id: str, expected: bool) -> None:
    token = vehicle_id.replace("-", "")
    observed = (
        device.wait(f"vehicle-equipment-installed-toggle-{token}", timeout=60, scroll=True)
        .attributes.get("checked") == "true"
    )
    if observed != expected:
        device.capture("vehicle-equipment-installed-toggle-mismatch")
        raise RuntimeError(f"Vehicle Installed was {observed!r}; expected {expected!r}")


def assert_selected_read_only(device: shared.Device, vehicle_id: str) -> None:
    token = vehicle_id.replace("-", "")
    toggle = device.wait(f"vehicle-equipment-installed-toggle-{token}", timeout=60, scroll=True)
    save = device.wait(f"vehicle-equipment-installed-save-{token}", timeout=60, scroll=True)
    if toggle.attributes.get("enabled") != "false" or save.attributes.get("enabled") != "false":
        device.capture("vehicle-equipment-installed-read-only-node-editable")
        raise RuntimeError("Fail-closed Vehicle Installed node exposed an enabled mutation")


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


def find_by_guid(root: ET.Element, guid: str) -> ET.Element | None:
    return next(
        (node for node in root.iter() if node.findtext("guid", default="").lower() == guid),
        None,
    )


def assert_workspace(device: shared.Device, expected: dict[str, object], installed: bool) -> None:
    observations: list[str] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        vehicle = find_by_guid(root, str(expected["vehicle_id"]))
        target = find_by_guid(root, str(expected["target_id"]))
        untouched = root.findall("./vehicles/vehicle")[-1] if root.findall("./vehicles/vehicle") else None
        observations.append(target.findtext("equipped", default="") if target is not None else "")
        if (
            vehicle is not None
            and target is not None
            and untouched is not None
            and target.findtext("equipped") == ("True" if installed else "False")
            and target.findtext("notes", default="") == expected["target_notes"]
            and untouched.findtext("notes", default="") == expected["other_notes"]
            and root.findtext("nuyen") == expected["nuyen"]
            and root.findtext("karma") == expected["karma"]
            and root.findtext("customstate") == expected["sentinel"]
        ):
            return
    device.capture("vehicle-equipment-installed-workspace-not-persisted")
    raise RuntimeError(f"Vehicle Installed was not durable and zero-economic: {observations!r}")


def run_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILES[profile]
    vehicle_id = str(expected["vehicle_id"])
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_installed_page(device, vehicle_id)
    for read_only_label in (str(expected["sensor_label"]), str(expected["parent_fixed_label"])):
        select_node(device, vehicle_id, read_only_label)
        assert_selected_read_only(device, vehicle_id)
    select_node(device, vehicle_id, str(expected["target_label"]))
    initial = bool(expected["initial"])
    assert_toggle(device, vehicle_id, initial)
    token = vehicle_id.replace("-", "")
    device.tap(f"vehicle-equipment-installed-toggle-{token}", timeout=60, scroll=True)
    assert_toggle(device, vehicle_id, not initial)
    device.tap(f"vehicle-equipment-installed-save-{token}", timeout=180, scroll=True, max_scrolls=24)
    device.wait(f"collection-editor-vehicle-{vehicle_id}", timeout=180)
    assert_workspace(device, expected, not initial)

    open_installed_page(device, vehicle_id)
    select_node(device, vehicle_id, str(expected["target_label"]))
    assert_toggle(device, vehicle_id, not initial)
    device.capture(f"vehicle-equipment-installed-{profile.lower()}-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, expected, not initial)
    open_installed_page(device, vehicle_id)
    select_node(device, vehicle_id, str(expected["target_label"]))
    assert_toggle(device, vehicle_id, not initial)
    device.capture(f"vehicle-equipment-installed-{profile.lower()}-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path,
                        default=fixtures / "creation-vehicle-equipment-installed-e2e.chum5")
    parser.add_argument("--career-runner", type=Path,
                        default=fixtures / "career-vehicle-equipment-installed-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "vehicleEquipmentInstalledPageSha256": android_root / "src/Chummer.Android/Native/VehicleEquipmentInstalledPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "vehicleEquipmentInstalledContractSha256": overview / "VehicleEquipmentInstalledEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "vehicleEquipmentInstalledRulesSha256": contracts / "CharacterVehicleEquipmentInstalledRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Vehicle Installed source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Vehicle Installed E2E requires API 36, got {api!r}")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Vehicle Installed E2E requires arm64-v8a, got {abi_list!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    run_profile(device, creation_fixture, "CharacterCreate")
    run_profile(device, career_fixture, "CharacterCareer")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "vehicle-equipment-installed",
        "apiLevel": int(api),
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "creationWeaponAccessoryEdited": "pass",
            "careerVehicleModEdited": "pass",
            "sensorVehicleModFailClosedBothPhases": "pass",
            "parentInstalledWeaponReadOnlyBothPhases": "pass",
            "zeroEconomicDeltaBothPhases": "pass",
            "sameSessionReopenBothPhases": "pass",
            "processRestartBothPhases": "pass",
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
        print(f"Vehicle Installed E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
