#!/usr/bin/env python3
"""Digest-bound API 36 arm64 phone proof for all four root Vehicle Matrix swaps."""
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
import run_api36_gear_attack_swap_e2e as matrix_shared

CONTROLS = (
    "CharacterCreate.cboVehicleAttack",
    "CharacterCreate.cboVehicleSleaze",
    "CharacterCreate.cboVehicleDataProcessing",
    "CharacterCreate.cboVehicleFirewall",
    "CharacterCareer.cboVehicleAttack",
    "CharacterCareer.cboVehicleSleaze",
    "CharacterCareer.cboVehicleDataProcessing",
    "CharacterCareer.cboVehicleFirewall",
)
PROOF_KEYS = (
    "exactVehicleHandlers", "rootVehicleGuidIdentity", "rawSavedPermutation",
    "attributeArrayAndCanSwapPreserved", "bonusesDisplayOnly", "sensorPreserved",
    "dataProcessingNotificationOnly", "activeHomeStatePreserved", "zeroNuyenKarmaEconomics",
    "revisionBoundAtomicSave", "sameSessionReopened", "processRestartWorkspacePersisted",
    "processRestartUiReadback", "descendantTargetsFailClosedCoverage",
)
JOURNEYS = (
    dict(phase="creation-attack", fixture="creation-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="91111111-1111-4111-8111-111111111111", changed="Attack", other="Sleaze",
         attack="{Pilot}", sleaze="7", dp="5", firewall="4", nuyen="4321", karma="7"),
    dict(phase="creation-sleaze", fixture="creation-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="91111111-1111-4111-8111-111111111111", changed="Sleaze", other="Data Processing",
         attack="7", sleaze="5", dp="{Pilot}", firewall="4", nuyen="4321", karma="7"),
    dict(phase="creation-dp", fixture="creation-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="91111111-1111-4111-8111-111111111111", changed="Data Processing", other="Attack",
         attack="5", sleaze="{Pilot}", dp="7", firewall="4", nuyen="4321", karma="7"),
    dict(phase="creation-firewall", fixture="creation-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="91111111-1111-4111-8111-111111111111", changed="Firewall", other="Sleaze",
         attack="7", sleaze="4", dp="5", firewall="{Pilot}", nuyen="4321", karma="7"),
    dict(phase="career-attack", fixture="career-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="92222222-2222-4222-8222-222222222222", changed="Attack", other="Firewall",
         attack="5", sleaze="7", dp="{Pilot}", firewall="8", nuyen="8765", karma="19"),
    dict(phase="career-sleaze", fixture="career-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="92222222-2222-4222-8222-222222222222", changed="Sleaze", other="Data Processing",
         attack="8", sleaze="{Pilot}", dp="7", firewall="5", nuyen="8765", karma="19"),
    dict(phase="career-dp", fixture="career-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="92222222-2222-4222-8222-222222222222", changed="Data Processing", other="Attack",
         attack="{Pilot}", sleaze="7", dp="8", firewall="5", nuyen="8765", karma="19"),
    dict(phase="career-firewall", fixture="career-vehicle-dp-firewall-swap-e2e.chum5",
         vehicle="92222222-2222-4222-8222-222222222222", changed="Firewall", other="Data Processing",
         attack="8", sleaze="7", dp="5", firewall="{Pilot}", nuyen="8765", karma="19"),
)

def prepare(device: shared.Device, fixture: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture)
    shared.wait_for_phone_runner_route(device, timeout=120)

def open_page(device: shared.Device, journey: dict[str, str]) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-vehicles", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-vehicle-{journey['vehicle']}", scroll=True, timeout=120, max_scrolls=24)
    token = journey["vehicle"].replace("-", "")
    device.tap(f"vehicle-dp-firewall-swap-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"vehicle-dp-firewall-swap-page-{token}", timeout=60)

def select_and_save(device: shared.Device, journey: dict[str, str]) -> None:
    token = journey["vehicle"].replace("-", "")
    changed = f"vehicle-dp-firewall-swap-changed-{token}"
    target = f"vehicle-dp-firewall-swap-target-{token}"
    device.tap(changed, scroll=True, timeout=60); device.tap(journey["changed"], timeout=60)
    device.tap(target, scroll=True, timeout=60); device.tap(journey["other"], timeout=60)
    time.sleep(.25)
    device.tap(f"vehicle-dp-firewall-swap-save-{token}", scroll=True, timeout=180, max_scrolls=24)

def assert_workspace(device: shared.Device, journey: dict[str, str]) -> None:
    creation = journey["phase"].startswith("creation")
    preserved = {
        "attributearray": "7,6,5,4" if creation else "8,7,6,5",
        "canswapattributes": "True", "modattack": "2" if creation else "6",
        "modsleaze": "3" if creation else "10", "moddataprocessing": "9" if creation else "11",
        "modfirewall": "5" if creation else "12", "active": "True" if creation else "False",
        "homenode": "False" if creation else "True", "sensor": "6" if creation else "4",
        "cost": "50000" if creation else "65000",
        "notes": "creation vehicle sentinel" if creation else "career vehicle sentinel",
    }
    for payload in matrix_shared.workspace_payloads(device):
        try: root = ET.fromstring(payload)
        except ET.ParseError: continue
        vehicle = next((node for node in root.findall("./vehicles/vehicle")
                        if node.findtext("guid", "").lower() == journey["vehicle"]), None)
        if vehicle is not None and vehicle.findtext("attack") == journey["attack"] \
                and vehicle.findtext("sleaze") == journey["sleaze"] \
                and vehicle.findtext("dataprocessing") == journey["dp"] \
                and vehicle.findtext("firewall") == journey["firewall"] \
                and all(vehicle.findtext(key) == value for key, value in preserved.items()) \
                and root.findtext("nuyen") == journey["nuyen"] and root.findtext("karma") == journey["karma"]:
            return
    raise RuntimeError(f"Vehicle Matrix {journey['phase']} swap was not durable/preserving")

def run_journey(device: shared.Device, journey: dict[str, str]) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare(device, journey["fixture"]); open_page(device, journey); select_and_save(device, journey)
    assert_workspace(device, journey); open_page(device, journey)
    device.shell("am", "force-stop", shared.PACKAGE); shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120); assert_workspace(device, journey); open_page(device, journey)

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("adb", "apk", "evidence", "receipt", "workspace-root"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    parser.add_argument("--serial", required=True); args = parser.parse_args()
    driver = Path(__file__).resolve(); android = driver.parents[1]; workspace = args.workspace_root.resolve()
    sources = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "matrixSharedDriverSha256": Path(matrix_shared.__file__).resolve(),
        "vehicleMatrixPageSha256": android / "src/Chummer.Android/Native/VehicleDataProcessingFirewallSwapPage.cs",
        "collectionRouteSha256": android / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "vehicleMatrixRequestSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/VehicleDataProcessingFirewallSwapEditRequest.cs",
        "mutationCatalogSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "vehicleMatrixRulesSha256": workspace / "chummer-core-engine/Chummer.Contracts/Characters/CharacterVehicleMatrixSwapRules.cs",
        "sharedMatrixAuthoritySha256": workspace / "chummer-core-engine/Chummer.Contracts/Characters/CharacterMatrixPermutationAuthority.cs",
        "presenterPersistenceSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "workspaceStoreSha256": workspace / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "creationFixtureSha256": android / "tests/fixtures/creation-vehicle-dp-firewall-swap-e2e.chum5",
        "careerFixtureSha256": android / "tests/fixtures/career-vehicle-dp-firewall-swap-e2e.chum5",
    }
    if any(not path.is_file() for path in sources.values()): raise RuntimeError("Vehicle Matrix source graph incomplete")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk"); abi = device.shell("getprop", "ro.product.cpu.abilist")
    if api != "36" or "arm64-v8a" not in abi.split(","): raise RuntimeError("Vehicle Matrix requires API 36 arm64-v8a")
    subprocess.run([str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
                   check=True, timeout=300)
    fixtures = driver.parent / "fixtures"
    for name in {journey["fixture"] for journey in JOURNEYS}: device.push(fixtures / name, f"/sdcard/Download/{name}")
    for journey in JOURNEYS: run_journey(device, journey)
    receipt = {"schema": "chummer.android.editing-e2e/v1", "status": "pass",
               "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "profile": "phone",
               "journey": "vehicle-matrix-swap", "apiLevel": 36, "abi": "arm64-v8a",
               "package": shared.PACKAGE, "driverSha256": shared.sha256(driver),
               **{key: shared.sha256(path) for key, path in sources.items()},
               "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS}}
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return 0

if __name__ == "__main__":
    try: raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Vehicle Matrix E2E failed: {error}", file=sys.stderr); raise SystemExit(1)
