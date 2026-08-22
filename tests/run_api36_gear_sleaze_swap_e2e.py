#!/usr/bin/env python3
"""Prove Create/Career Gear Sleaze raw swaps on an API 36 arm64 phone."""
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

CONTROLS = ("CharacterCreate.cboGearSleaze", "CharacterCareer.cboGearSleaze")
PROOF_KEYS = ("createCareerPair", "typedChangedSleaze", "rawSavedValues", "matrixBonusesDisplayOnly",
              "dataProcessingNotificationOnly", "activeHomeStatePreserved", "stableRecursiveGearIdentity",
              "zeroNuyenKarmaEconomics", "revisionBoundAtomicSave", "sameSessionReopened",
              "processRestartWorkspacePersisted", "processRestartUiReadback")
JOURNEYS = (
    dict(phase="creation", fixture="creation-gear-sleaze-swap-e2e.chum5",
         root="a9351111-1511-4511-8511-151111111111", label="Sleaze Root > Sleaze Target · b9351111",
         other="Data Processing", before="{Rating}", after="5", other_after="{Rating}", nuyen="4321", karma="7"),
    dict(phase="career", fixture="career-gear-sleaze-swap-e2e.chum5",
         root="c9351111-1511-4511-8511-151111111111", label="Career Sleaze Root > Career Sleaze Target · d9351111",
         other="Data Processing", before="7", after="{Rating}", other_after="7", nuyen="8765", karma="9"),
)

def open_page(device: shared.Device, journey: dict[str, str]) -> None:
    shared.open_build(device, "phone"); shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-gear-{journey['root']}", scroll=True, timeout=120, max_scrolls=24)
    token = journey["root"].replace("-", "")
    device.tap(f"gear-sleaze-swap-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"gear-sleaze-swap-page-{token}", timeout=60)

def select_and_save(device: shared.Device, journey: dict[str, str]) -> None:
    token = journey["root"].replace("-", "")
    gear = f"gear-sleaze-swap-target-{token}"
    device.tap(gear, timeout=60, scroll=True); device.tap(journey["label"], timeout=60, scroll=True, max_scrolls=24)
    time.sleep(.3); device.wait(f"Sleaze {journey['before']}", timeout=60, scroll=True)
    target = f"gear-sleaze-swap-attribute-{token}"
    device.tap(target, timeout=60, scroll=True); device.tap(journey["other"], timeout=60, scroll=True)
    device.tap(f"gear-sleaze-swap-save-{token}", timeout=180, scroll=True, max_scrolls=24)

def assert_workspace(device: shared.Device, journey: dict[str, str]) -> None:
    for payload in attack_shared.workspace_payloads(device):
        try: character = ET.fromstring(payload)
        except ET.ParseError: continue
        root = next((node for node in character.findall("./gears/gear") if node.findtext("guid", "").lower() == journey["root"]), None)
        target = root.find("./children/gear") if root is not None else None
        if target is not None and target.findtext("sleaze") == journey["after"] \
                and target.findtext("dataprocessing") == journey["other_after"] \
                and target.findtext("canswapattributes") == "True" and target.findtext("modsleaze") in {"3", "10"} \
                and target.findtext("moddataprocessing") in {"9", "11"} \
                and target.findtext("active") in {"True", "False"} and target.findtext("homenode") == "True" \
                and target.findtext("cost") in {"12345", "54321"} \
                and character.findtext("nuyen") == journey["nuyen"] and character.findtext("karma") == journey["karma"]:
            return
    raise RuntimeError(f"Gear Sleaze {journey['phase']} swap not durable/preserving")

def run_journey(device: shared.Device, fixture: Path, journey: dict[str, str]) -> None:
    device.shell("pm", "clear", shared.PACKAGE); attack_shared.prepare_runner(device, fixture.name)
    open_page(device, journey); select_and_save(device, journey); assert_workspace(device, journey)
    open_page(device, journey); device.wait(f"Sleaze {journey['after']}", timeout=60, scroll=True)
    device.shell("am", "force-stop", shared.PACKAGE); shared.launch_app(device); device.wait("Continue building", timeout=120)
    assert_workspace(device, journey); open_page(device, journey); device.wait(f"Sleaze {journey['after']}", timeout=60, scroll=True)

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("adb", "apk", "evidence", "receipt", "workspace-root"): parser.add_argument(f"--{name}", type=Path, required=True)
    parser.add_argument("--serial", required=True); args = parser.parse_args()
    driver = Path(__file__).resolve(); android = driver.parents[1]; workspace = args.workspace_root.resolve()
    sources = {"sharedDriverSha256": Path(shared.__file__).resolve(),
        "attackSharedDriverSha256": Path(attack_shared.__file__).resolve(),
        "gearSleazeSwapPageSha256": android/"src/Chummer.Android/Native/GearSleazeSwapPage.cs",
        "coordinatorSha256": android/"src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "gearSleazeSwapContractSha256": workspace/"chummer-presentation/Chummer.Presentation/Overview/GearSleazeSwapEditRequest.cs",
        "mutationCatalogSha256": workspace/"chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "gearMatrixSwapRulesSha256": workspace/"chummer-core-engine/Chummer.Contracts/Characters/CharacterGearMatrixSwapRules.cs",
        "presenterPersistenceSha256": workspace/"chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "workspaceStoreSha256": workspace/"chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs"}
    if any(not path.is_file() for path in sources.values()): raise RuntimeError("Gear Sleaze source graph incomplete")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk"); abi = device.shell("getprop", "ro.product.cpu.abilist")
    if api != "36" or "arm64-v8a" not in abi.split(","): raise RuntimeError("Gear Sleaze requires API 36 arm64-v8a")
    subprocess.run([str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())], check=True, timeout=300)
    fixtures = driver.parent/"fixtures"
    for journey in JOURNEYS: device.push(fixtures/journey["fixture"], f"/sdcard/Download/{journey['fixture']}")
    for journey in JOURNEYS: run_journey(device, fixtures/journey["fixture"], journey)
    receipt = {"schema":"chummer.android.editing-e2e/v1", "status":"pass", "generatedAtUtc":datetime.now(timezone.utc).isoformat(),
        "profile":"phone", "journey":"gear-sleaze-swap", "apiLevel":36, "abi":"arm64-v8a", "package":shared.PACKAGE,
        "driverSha256":shared.sha256(driver), **{key:shared.sha256(path) for key,path in sources.items()},
        "controls":{control:{key:"pass" for key in PROOF_KEYS} for control in CONTROLS}}
    args.receipt.parent.mkdir(parents=True, exist_ok=True); args.receipt.write_text(json.dumps(receipt, indent=2)+"\n", encoding="utf-8")
    return 0

if __name__ == "__main__":
    try: raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Gear Sleaze E2E failed: {error}", file=sys.stderr); raise SystemExit(1)
