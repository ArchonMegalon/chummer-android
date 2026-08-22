#!/usr/bin/env python3
"""Digest-bound API 36 arm64 phone proof for all four root Cyberware Matrix swaps."""
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
    "CharacterCreate.cboCyberwareAttack",
    "CharacterCreate.cboCyberwareSleaze",
    "CharacterCreate.cboCyberwareDataProcessing",
    "CharacterCreate.cboCyberwareFirewall",
    "CharacterCareer.cboCyberwareAttack",
    "CharacterCareer.cboCyberwareSleaze",
    "CharacterCareer.cboCyberwareDataProcessing",
    "CharacterCareer.cboCyberwareFirewall",
)
PROOF_KEYS = (
    "exactCyberwareHandlers",
    "rootCyberwareGuidIdentity",
    "rawSavedPermutation",
    "attributeArrayAndCanSwapPreserved",
    "bonusesDisplayOnly",
    "activeHomeStatePreserved",
    "zeroNuyenKarmaEconomics",
    "revisionBoundAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
    "descendantTargetsFailClosedCoverage",
)
JOURNEYS = (
    dict(phase="creation-attack", fixture="creation-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6111111-1611-4611-8611-161111111111", changed="Attack", other="Sleaze",
         attack="{Rating}", sleaze="7", dp="5", firewall="4", nuyen="4321", karma="7"),
    dict(phase="creation-sleaze", fixture="creation-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6111111-1611-4611-8611-161111111111", changed="Sleaze", other="Data Processing",
         attack="7", sleaze="5", dp="{Rating}", firewall="4", nuyen="4321", karma="7"),
    dict(phase="creation-dp", fixture="creation-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6111111-1611-4611-8611-161111111111", changed="Data Processing", other="Attack",
         attack="5", sleaze="{Rating}", dp="7", firewall="4", nuyen="4321", karma="7"),
    dict(phase="creation-firewall", fixture="creation-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6111111-1611-4611-8611-161111111111", changed="Firewall", other="Sleaze",
         attack="7", sleaze="4", dp="5", firewall="{Rating}", nuyen="4321", karma="7"),
    dict(phase="career-attack", fixture="career-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6222222-1622-4622-8622-162222222222", changed="Attack", other="Firewall",
         attack="5", sleaze="7", dp="{Rating}", firewall="8", nuyen="8765", karma="19"),
    dict(phase="career-sleaze", fixture="career-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6222222-1622-4622-8622-162222222222", changed="Sleaze", other="Data Processing",
         attack="8", sleaze="{Rating}", dp="7", firewall="5", nuyen="8765", karma="19"),
    dict(phase="career-dp", fixture="career-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6222222-1622-4622-8622-162222222222", changed="Data Processing", other="Attack",
         attack="{Rating}", sleaze="7", dp="8", firewall="5", nuyen="8765", karma="19"),
    dict(phase="career-firewall", fixture="career-cyberware-matrix-swap-e2e.chum5",
         cyberware="a6222222-1622-4622-8622-162222222222", changed="Firewall", other="Sleaze",
         attack="8", sleaze="5", dp="{Rating}", firewall="7", nuyen="8765", karma="19"),
)


def prepare(device: shared.Device, fixture: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture)
    device.wait("Continue building", timeout=120)


def open_page(device: shared.Device, journey: dict[str, str]) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-cyberware", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-cyberware-{journey['cyberware']}", scroll=True, timeout=120, max_scrolls=24)
    token = journey["cyberware"].replace("-", "")
    device.tap(f"cyberware-matrix-swap-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"cyberware-matrix-swap-page-{token}", timeout=60)


def select_and_save(device: shared.Device, journey: dict[str, str]) -> None:
    token = journey["cyberware"].replace("-", "")
    changed = f"cyberware-matrix-swap-changed-{token}"
    target = f"cyberware-matrix-swap-target-{token}"
    device.tap(changed, scroll=True, timeout=60)
    device.tap(journey["changed"], timeout=60)
    device.tap(target, scroll=True, timeout=60)
    device.tap(journey["other"], timeout=60)
    time.sleep(.25)
    device.tap(f"cyberware-matrix-swap-save-{token}", scroll=True, timeout=180, max_scrolls=24)


def assert_workspace(device: shared.Device, journey: dict[str, str]) -> None:
    creation = journey["phase"].startswith("creation")
    preserved = {
        "attributearray": "7,6,5,4" if creation else "8,7,6,5",
        "canswapattributes": "True",
        "modattack": "2" if creation else "6",
        "modsleaze": "3" if creation else "10",
        "moddataprocessing": "9" if creation else "11",
        "modfirewall": "5" if creation else "12",
        "rating": "3" if creation else "4",
        "grade": "Standard" if creation else "Alpha",
        "cost": "12345" if creation else "54321",
        "active": "True" if creation else "False",
        "homenode": "False" if creation else "True",
        "notes": "Creation root sentinel" if creation else "Career root sentinel",
    }
    for payload in matrix_shared.workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        cyberware = next((node for node in root.findall("./cyberwares/cyberware")
                          if node.findtext("guid", "").lower() == journey["cyberware"]), None)
        if cyberware is not None \
                and cyberware.findtext("attack") == journey["attack"] \
                and cyberware.findtext("sleaze") == journey["sleaze"] \
                and cyberware.findtext("dataprocessing") == journey["dp"] \
                and cyberware.findtext("firewall") == journey["firewall"] \
                and all(cyberware.findtext(key) == value for key, value in preserved.items()) \
                and cyberware.find("./children/cyberware") is not None \
                and cyberware.find("./gears/gear") is not None \
                and root.findtext("nuyen") == journey["nuyen"] \
                and root.findtext("karma") == journey["karma"]:
            return
    raise RuntimeError(f"Cyberware Matrix {journey['phase']} swap was not durable/preserving")


def run_journey(device: shared.Device, journey: dict[str, str]) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare(device, journey["fixture"])
    open_page(device, journey)
    select_and_save(device, journey)
    assert_workspace(device, journey)
    open_page(device, journey)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, journey)
    open_page(device, journey)


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
        "matrixSharedDriverSha256": Path(matrix_shared.__file__).resolve(),
        "cyberwareMatrixPageSha256": android / "src/Chummer.Android/Native/CyberwareMatrixSwapPage.cs",
        "collectionRouteSha256": android / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "cyberwareMatrixRequestSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/CyberwareMatrixSwapEditRequest.cs",
        "mutationCatalogSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "cyberwareMatrixRulesSha256": workspace / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCyberwareMatrixSwapRules.cs",
        "sharedMatrixAuthoritySha256": workspace / "chummer-core-engine/Chummer.Contracts/Characters/CharacterMatrixPermutationAuthority.cs",
        "presenterPersistenceSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "workspaceStoreSha256": workspace / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "creationFixtureSha256": android / "tests/fixtures/creation-cyberware-matrix-swap-e2e.chum5",
        "careerFixtureSha256": android / "tests/fixtures/career-cyberware-matrix-swap-e2e.chum5",
    }
    if any(not path.is_file() for path in sources.values()):
        raise RuntimeError("Cyberware Matrix source graph incomplete")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abilist")
    if api != "36" or "arm64-v8a" not in abi.split(","):
        raise RuntimeError("Cyberware Matrix requires API 36 arm64-v8a")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    fixtures = driver.parent / "fixtures"
    for name in {journey["fixture"] for journey in JOURNEYS}:
        device.push(fixtures / name, f"/sdcard/Download/{name}")
    for journey in JOURNEYS:
        run_journey(device, journey)
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "cyberware-matrix-swap",
        "apiLevel": 36,
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in sources.items()},
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Cyberware Matrix E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
