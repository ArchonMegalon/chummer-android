#!/usr/bin/env python3
"""Prove Create/Career Gear Data Processing and Firewall raw swaps on an API 36 arm64 phone."""
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
import run_api36_gear_attack_swap_e2e as attack_shared

CONTROLS = (
    "CharacterCreate.cboGearDataProcessing",
    "CharacterCreate.cboGearFirewall",
    "CharacterCareer.cboGearDataProcessing",
    "CharacterCareer.cboGearFirewall",
)
PROOF_KEYS = (
    "createCareerFourRows", "typedChangedAttribute", "rawSavedPermutation",
    "matrixBonusesDisplayOnly", "dataProcessingNotificationOnly", "activeHomeStatePreserved",
    "attributeArrayAndCanSwapProvenancePreserved", "stableRecursiveGearIdentity",
    "zeroNuyenKarmaEconomics", "revisionBoundAtomicSave", "sameSessionReopened",
    "processRestartWorkspacePersisted", "processRestartUiReadback",
)
JOURNEYS = (
    dict(phase="creation-dp", fixture="creation-gear-dp-firewall-swap-e2e.chum5",
         root="e9361111-1511-4511-8511-151111111111", label="DP Firewall Root > DP Firewall Target · f9361111",
         changed="Data Processing", other="Attack", attack="5", sleaze="{Rating}",
         data_processing="7", firewall="4", nuyen="4321", karma="7"),
    dict(phase="creation-firewall", fixture="creation-gear-dp-firewall-swap-e2e.chum5",
         root="e9361111-1511-4511-8511-151111111111", label="DP Firewall Root > DP Firewall Target · f9361111",
         changed="Firewall", other="Sleaze", attack="7", sleaze="4",
         data_processing="5", firewall="{Rating}", nuyen="4321", karma="7"),
    dict(phase="career-dp", fixture="career-gear-dp-firewall-swap-e2e.chum5",
         root="a9361111-1511-4511-8511-151111111111", label="Career DP Firewall Root > Career DP Firewall Target · b9361111",
         changed="Data Processing", other="Attack", attack="{Rating}", sleaze="7",
         data_processing="8", firewall="5", nuyen="8765", karma="9"),
    dict(phase="career-firewall", fixture="career-gear-dp-firewall-swap-e2e.chum5",
         root="a9361111-1511-4511-8511-151111111111", label="Career DP Firewall Root > Career DP Firewall Target · b9361111",
         changed="Firewall", other="Data Processing", attack="8", sleaze="7",
         data_processing="5", firewall="{Rating}", nuyen="8765", karma="9"),
)

def open_page(device: shared.Device, journey: dict[str, str]) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-gear-{journey['root']}", scroll=True, timeout=120, max_scrolls=24)
    token = journey["root"].replace("-", "")
    device.tap(f"gear-dp-firewall-swap-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"gear-dp-firewall-swap-page-{token}", timeout=60)

def select_and_save(device: shared.Device, journey: dict[str, str]) -> None:
    token = journey["root"].replace("-", "")
    gear = f"gear-dp-firewall-swap-gear-{token}"
    device.tap(gear, timeout=60, scroll=True)
    device.tap(journey["label"], timeout=60, scroll=True, max_scrolls=24)
    time.sleep(.3)
    changed = f"gear-dp-firewall-swap-changed-{token}"
    device.tap(changed, timeout=60, scroll=True)
    device.tap(journey["changed"], timeout=60, scroll=True)
    target = f"gear-dp-firewall-swap-target-{token}"
    device.tap(target, timeout=60, scroll=True)
    device.tap(journey["other"], timeout=60, scroll=True)
    device.tap(f"gear-dp-firewall-swap-save-{token}", timeout=180, scroll=True, max_scrolls=24)

def assert_workspace(device: shared.Device, journey: dict[str, str]) -> None:
    creation = journey["phase"].startswith("creation")
    expected = {
        "attributearray": "7,6,5,4" if creation else "8,7,6,5",
        "modattack": "2" if creation else "6",
        "modsleaze": "3" if creation else "10",
        "moddataprocessing": "9" if creation else "11",
        "modfirewall": "5" if creation else "12",
        "active": "True" if creation else "False",
        "homenode": "True",
        "equipped": "True" if creation else "False",
        "stolen": "False" if creation else "True",
        "cost": "12345" if creation else "54321",
        "notes": "Creation DP Firewall target sentinel" if creation else "Career DP Firewall target sentinel",
    }
    for payload in attack_shared.workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        root = next((node for node in character.findall("./gears/gear")
                     if node.findtext("guid", "").lower() == journey["root"]), None)
        target = root.find("./children/gear") if root is not None else None
        if target is not None \
                and target.findtext("attack") == journey["attack"] \
                and target.findtext("sleaze") == journey["sleaze"] \
                and target.findtext("dataprocessing") == journey["data_processing"] \
                and target.findtext("firewall") == journey["firewall"] \
                and target.findtext("canswapattributes") == "True" \
                and all(target.findtext(name) == value for name, value in expected.items()) \
                and character.findtext("nuyen") == journey["nuyen"] \
                and character.findtext("karma") == journey["karma"] \
                and character.findtext("customstate") == (
                    "Creation DP Firewall sentinel" if creation else "Career DP Firewall sentinel"):
            return
    raise RuntimeError(f"Gear Data Processing/Firewall {journey['phase']} swap not durable/preserving")

def run_journey(device: shared.Device, journey: dict[str, str]) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    attack_shared.prepare_runner(device, journey["fixture"])
    open_page(device, journey)
    select_and_save(device, journey)
    assert_workspace(device, journey)
    open_page(device, journey)
    device.wait(f"{journey['changed']} {journey['firewall'] if journey['changed'] == 'Firewall' else journey['data_processing']}",
                timeout=60, scroll=True)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, journey)
    open_page(device, journey)
    device.wait(f"{journey['changed']} {journey['firewall'] if journey['changed'] == 'Firewall' else journey['data_processing']}",
                timeout=60, scroll=True)

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("adb", "apk", "evidence", "receipt", "workspace-root"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    args = parser.parse_args()
    driver = Path(__file__).resolve()
    android = driver.parents[1]
    workspace = args.workspace_root.resolve()
    sources = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "attackSharedDriverSha256": Path(attack_shared.__file__).resolve(),
        "gearDataProcessingFirewallSwapPageSha256": android / "src/Chummer.Android/Native/GearDataProcessingFirewallSwapPage.cs",
        "collectionEditorRouteSha256": android / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "gearDataProcessingFirewallSwapContractSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/GearDataProcessingFirewallSwapEditRequest.cs",
        "mutationCatalogSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "gearMatrixSwapRulesSha256": workspace / "chummer-core-engine/Chummer.Contracts/Characters/CharacterGearMatrixSwapRules.cs",
        "presenterPersistenceSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "workspaceStoreSha256": workspace / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "creationFixtureSha256": android / "tests/fixtures/creation-gear-dp-firewall-swap-e2e.chum5",
        "careerFixtureSha256": android / "tests/fixtures/career-gear-dp-firewall-swap-e2e.chum5",
    }
    if any(not path.is_file() for path in sources.values()):
        raise RuntimeError("Gear Data Processing/Firewall source graph incomplete")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abilist")
    if api != "36" or "arm64-v8a" not in abi.split(","):
        raise RuntimeError("Gear Data Processing/Firewall requires API 36 arm64-v8a")
    subprocess.run([str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
                   check=True, timeout=300)
    fixtures = driver.parent / "fixtures"
    for fixture in {journey["fixture"] for journey in JOURNEYS}:
        device.push(fixtures / fixture, f"/sdcard/Download/{fixture}")
    for journey in JOURNEYS:
        run_journey(device, journey)
    receipt = {
        "schema":"chummer.android.editing-e2e/v1", "status":"pass",
        "generatedAtUtc":datetime.now(timezone.utc).isoformat(), "profile":"phone",
        "journey":"gear-data-processing-firewall-swap", "apiLevel":36, "abi":"arm64-v8a",
        "package":shared.PACKAGE, "driverSha256":shared.sha256(driver),
        **{key:shared.sha256(path) for key,path in sources.items()},
        "controls":{control:{key:"pass" for key in PROOF_KEYS} for control in CONTROLS},
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return 0

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Gear Data Processing/Firewall E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
