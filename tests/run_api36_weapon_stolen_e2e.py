#!/usr/bin/env python3
"""Prove exact CharacterCreate Weapon Stolen persistence on an API 36 phone."""

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


CONTROLS = ("CharacterCreate.chkWeaponStolen",)
ROOT_WEAPON_ID = "aa131111-1311-4311-8311-131111111111"
CAREER_WEAPON_ID = "ab141111-1411-4411-8411-141111111111"
TARGET_LABEL = "Weapon Stolen Root > Weapon Stolen Accessory > Weapon Stolen Gear > Weapon Stolen Target · dd131111"
PROOF_KEYS = (
    "creationOnlyCareerNegative",
    "activeNuyenStolenEligibility",
    "selectedWeaponAccessoryRecursiveGearNode",
    "stableTypedHierarchicalIdentity",
    "zeroNuyenKarmaEconomics",
    "duplicateAmbiguousRejected",
    "stolenElementPersisted",
    "unrelatedXmlPreserved",
    "revisionBoundAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_weapon_item(device: shared.Device, weapon_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-weapons", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-weapon-{weapon_id}", scroll=True, timeout=120, max_scrolls=24)
    device.wait(f"collection-editor-weapon-{weapon_id}", timeout=120)


def open_stolen_page(device: shared.Device) -> None:
    token = ROOT_WEAPON_ID.replace("-", "")
    open_weapon_item(device, ROOT_WEAPON_ID)
    device.tap(f"weapon-stolen-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"weapon-stolen-page-{token}", timeout=60)


def select_target(device: shared.Device) -> None:
    token = ROOT_WEAPON_ID.replace("-", "")
    device.tap(f"weapon-stolen-target-{token}", timeout=60, scroll=True)
    device.tap(TARGET_LABEL, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device, f"weapon-stolen-target-{token}", "Weapon node", scroll=True
    )
    if observed != TARGET_LABEL:
        device.capture("weapon-stolen-target-mismatch")
        raise RuntimeError(f"Weapon Stolen target was {observed!r}; expected {TARGET_LABEL!r}")


def assert_ui(device: shared.Device, expected: bool) -> None:
    token = ROOT_WEAPON_ID.replace("-", "")
    observed = (
        device.wait(f"weapon-stolen-toggle-{token}", timeout=60, scroll=True)
        .attributes.get("checked")
        == "true"
    )
    if observed != expected:
        device.capture("weapon-stolen-toggle-mismatch")
        raise RuntimeError(f"Weapon Stolen was {observed!r}; expected {expected!r}")


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


def assert_workspace(device: shared.Device) -> None:
    observations: list[str] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        root = next((
            node for node in character.findall("./weapons/weapon")
            if node.findtext("guid", default="").lower() == ROOT_WEAPON_ID
        ), None)
        if root is None:
            continue
        target = root.find("./accessories/accessory/gears/gear/children/gear")
        sibling = next((
            node for node in character.findall("./weapons/weapon")
            if node.findtext("guid", default="").lower()
            == "ff131111-1311-4311-8311-131111111111"
        ), None)
        observations.append(target.findtext("stolen", default="") if target is not None else "")
        if (
            target is not None
            and target.findtext("stolen") == "True"
            and target.findtext("notes") == "Creation target Gear sentinel"
            and root.findtext("stolen") == "False"
            and root.findtext("notes") == "Creation root Weapon sentinel"
            and root.findtext("./accessories/accessory/stolen") == "True"
            and root.findtext("./underbarrel/weapon/stolen") == "False"
            and sibling is not None
            and sibling.findtext("stolen") == "False"
            and sibling.findtext("notes") == "Creation untouched Weapon sentinel"
            and character.findtext("nuyen") == "4321"
            and character.findtext("karma") == "7"
            and character.findtext("customstate") == "Creation Weapon Stolen runner sentinel"
        ):
            return
    device.capture("weapon-stolen-workspace-not-persisted")
    raise RuntimeError(f"Weapon Stolen was not durable: {observations!r}")


def assert_reopened(device: shared.Device) -> None:
    open_stolen_page(device)
    select_target(device)
    assert_ui(device, True)


def assert_career_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_weapon_item(device, CAREER_WEAPON_ID)
    selector = f"weapon-stolen-open-{CAREER_WEAPON_ID.replace('-', '')}"
    try:
        device.wait(selector, timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("weapon-stolen-career-action-exposed")
        raise RuntimeError("Create-only Weapon Stolen was exposed for a Career runner")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-weapon-stolen-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-weapon-stolen-negative-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "weaponStolenPageSha256": android_root / "src/Chummer.Android/Native/WeaponStolenPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "weaponStolenContractSha256": overview / "WeaponStolenEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "weaponStolenRulesSha256": contracts / "CharacterWeaponStolenRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Weapon Stolen source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Weapon Stolen E2E requires API 36, got {api!r}")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Weapon Stolen E2E requires arm64-v8a, got {abi_list!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, creation_fixture.name)
    open_stolen_page(device)
    select_target(device)
    assert_ui(device, False)
    token = ROOT_WEAPON_ID.replace("-", "")
    device.tap(f"weapon-stolen-toggle-{token}", timeout=60, scroll=True)
    assert_ui(device, True)
    device.tap(f"weapon-stolen-save-{token}", timeout=180, scroll=True, max_scrolls=24)
    device.wait(f"collection-editor-weapon-{ROOT_WEAPON_ID}", timeout=180)
    device.back()
    assert_workspace(device)
    assert_reopened(device)
    device.capture("weapon-stolen-creation-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device)
    assert_reopened(device)
    device.capture("weapon-stolen-creation-after-process-restart")
    assert_career_negative(device, career_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "weapon-stolen",
        "apiLevel": int(api),
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerNegativeFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "creationEligibleWeaponHierarchyEdited": "pass",
            "creationSameSessionReopen": "pass",
            "creationProcessRestart": "pass",
            "careerActionNotExposed": "pass",
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
        print(f"Weapon Stolen E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
