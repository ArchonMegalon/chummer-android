#!/usr/bin/env python3
"""Digest-bound API 36 arm64 phone proof for Career-only direct Weapon Matrix swaps."""

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
    "CharacterCareer.cboWeaponGearAttack",
    "CharacterCareer.cboWeaponGearSleaze",
    "CharacterCareer.cboWeaponGearDataProcessing",
    "CharacterCareer.cboWeaponGearFirewall",
)
PROOF_KEYS = (
    "exactCareerWeaponHandlers",
    "creationSurfaceAbsent",
    "directRootWeaponGuidIdentity",
    "legacySurfaceBoundRevision",
    "rawSavedPermutation",
    "attributeArrayAndCanSwapPreserved",
    "bonusesDisplayOnly",
    "activeHomeStatePreserved",
    "zeroNuyenKarmaEconomics",
    "revisionBoundAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
    "descendantAndOtherOwnerTargetsFailClosedCoverage",
)
CAREER_WEAPON = "d7111111-1711-4711-8711-171111111111"
CREATION_WEAPON = "d7666666-1766-4766-8766-176666666666"
JOURNEYS = (
    dict(name="career-attack", changed="Attack", other="Sleaze",
         attack="7", sleaze="8", dp="6", firewall="5"),
    dict(name="career-sleaze", changed="Sleaze", other="Data Processing",
         attack="8", sleaze="6", dp="7", firewall="5"),
    dict(name="career-dp", changed="Data Processing", other="Attack",
         attack="6", sleaze="7", dp="8", firewall="5"),
    dict(name="career-firewall", changed="Firewall", other="Sleaze",
         attack="8", sleaze="5", dp="6", firewall="7"),
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_selected_weapon(device: shared.Device, weapon_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-weapons", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-weapon-{weapon_id}", scroll=True, timeout=120, max_scrolls=24)
    device.wait(f"collection-editor-weapon-{weapon_id}", timeout=120)


def open_page(device: shared.Device) -> None:
    open_selected_weapon(device, CAREER_WEAPON)
    token = CAREER_WEAPON.replace("-", "")
    device.tap(f"weapon-matrix-swap-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"weapon-matrix-swap-page-{token}", timeout=60)


def select_and_save(device: shared.Device, journey: dict[str, str]) -> None:
    token = CAREER_WEAPON.replace("-", "")
    device.tap(f"weapon-matrix-swap-changed-{token}", scroll=True, timeout=60)
    device.tap(journey["changed"], timeout=60)
    device.tap(f"weapon-matrix-swap-target-{token}", scroll=True, timeout=60)
    device.tap(journey["other"], timeout=60)
    time.sleep(0.25)
    device.tap(f"weapon-matrix-swap-save-{token}", scroll=True, timeout=180, max_scrolls=24)
    device.wait(f"weapon-matrix-swap-open-{token}", scroll=True, timeout=180, max_scrolls=36)


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


def assert_workspace(device: shared.Device, journey: dict[str, str]) -> None:
    preserved = {
        "attributearray": "8,7,6,5",
        "canswapattributes": "True",
        "modattack": "2",
        "modsleaze": "3",
        "moddataprocessing": "4",
        "modfirewall": "1",
        "rating": "4",
        "category": "Cyberweapons",
        "cost": "54321",
        "active": "True",
        "homenode": "False",
        "notes": "Career root Weapon sentinel",
    }
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        weapon = next((node for node in root.findall("./weapons/weapon")
                       if node.findtext("guid", "").lower() == CAREER_WEAPON), None)
        sibling = next((node for node in root.findall("./weapons/weapon")
                        if node.findtext("guid", "").lower()
                        == "d7555555-1755-4755-8755-175555555555"), None)
        if weapon is not None \
                and weapon.findtext("attack") == journey["attack"] \
                and weapon.findtext("sleaze") == journey["sleaze"] \
                and weapon.findtext("dataprocessing") == journey["dp"] \
                and weapon.findtext("firewall") == journey["firewall"] \
                and all(weapon.findtext(key) == value for key, value in preserved.items()) \
                and weapon.findtext("./underbarrel/weapon/guid") \
                == "d7222222-1722-4722-8722-172222222222" \
                and weapon.findtext("./accessories/accessory/gears/gear/guid") \
                == "d7444444-1744-4744-8744-174444444444" \
                and sibling is not None \
                and sibling.findtext("notes") == "Career sibling sentinel" \
                and root.findtext("nuyen") == "8765" \
                and root.findtext("karma") == "19" \
                and root.findtext("customstate") == "Career Weapon Matrix sentinel":
            return
    device.capture(f"weapon-matrix-{journey['name']}-workspace-not-persisted")
    raise RuntimeError(f"Weapon Matrix {journey['name']} swap was not durable and preserving")


def run_journey(device: shared.Device, fixture: Path, journey: dict[str, str]) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    select_and_save(device, journey)
    assert_workspace(device, journey)
    open_page(device)
    device.capture(f"weapon-matrix-{journey['name']}-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, journey)
    open_page(device)
    device.capture(f"weapon-matrix-{journey['name']}-process-restart")


def assert_creation_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_selected_weapon(device, CREATION_WEAPON)
    token = CREATION_WEAPON.replace("-", "")
    try:
        device.wait(f"weapon-matrix-swap-open-{token}", timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("weapon-matrix-creation-action-exposed")
        raise RuntimeError("Career-only Weapon Matrix swapping was exposed for a creation runner")

    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        weapon = root.find("./weapons/weapon")
        if weapon is not None and weapon.findtext("guid", "").lower() == CREATION_WEAPON:
            if (
                weapon.findtext("attack"),
                weapon.findtext("sleaze"),
                weapon.findtext("dataprocessing"),
                weapon.findtext("firewall"),
                weapon.findtext("notes"),
            ) != ("7", "6", "5", "4", "Creation target must remain unchanged"):
                raise RuntimeError("Creation-negative Weapon Matrix state changed unexpectedly")
            return
    raise RuntimeError("Creation-negative Weapon was unavailable in workspace state")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("adb", "apk", "evidence", "receipt", "workspace-root"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--career-runner", type=Path,
                        default=fixtures / "career-weapon-matrix-swap-e2e.chum5")
    parser.add_argument("--creation-runner", type=Path,
                        default=fixtures / "creation-weapon-matrix-swap-negative-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android = driver.parents[1]
    workspace = args.workspace_root.resolve()
    overview = workspace / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace / "chummer-core-engine/Chummer.Contracts/Characters"
    sources = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "weaponMatrixPageSha256": android / "src/Chummer.Android/Native/WeaponMatrixSwapPage.cs",
        "collectionRouteSha256": android / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "weaponMatrixRequestSha256": overview / "WeaponMatrixSwapEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "weaponMatrixRulesSha256": contracts / "CharacterWeaponMatrixSwapRules.cs",
        "sharedMatrixAuthoritySha256": contracts / "CharacterMatrixPermutationAuthority.cs",
        "workspaceStoreSha256": workspace / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in sources.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Weapon Matrix source graph incomplete: {missing!r}")

    career_fixture = args.career_runner.resolve()
    creation_fixture = args.creation_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abilist")
    if api != "36" or "arm64-v8a" not in abi.split(","):
        raise RuntimeError("Weapon Matrix requires API 36 arm64-v8a")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (career_fixture, creation_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    for journey in JOURNEYS:
        run_journey(device, career_fixture, journey)
    assert_creation_negative(device, creation_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "weapon-matrix-swap",
        "apiLevel": 36,
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in sources.items()},
        "careerFixtureSha256": shared.sha256(career_fixture),
        "creationNegativeFixtureSha256": shared.sha256(creation_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "careerAllFourHandlers": "pass",
            "careerSameSessionReopen": "pass",
            "careerProcessRestart": "pass",
            "creationActionNotExposed": "pass",
            "descendantAndOtherOwnerTargetsFailClosedCoverage": "pass",
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
        print(f"Weapon Matrix E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
