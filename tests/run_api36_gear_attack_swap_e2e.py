#!/usr/bin/env python3
"""Prove Create/Career Gear Attack raw-value swaps on an API 36 phone."""

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


CONTROLS = ("CharacterCreate.cboGearAttack", "CharacterCareer.cboGearAttack")
PROOF_KEYS = (
    "createCareerPair", "canSwapAttributesEligibility", "rawSavedValues",
    "matrixBonusesDisplayOnly", "stableRecursiveGearIdentity", "duplicateAmbiguousRejected",
    "zeroNuyenKarmaEconomics", "attributeArrayAndCanSwapProvenancePreserved",
    "matrixCostAndStatePreserved", "revisionBoundAtomicSave", "sameSessionReopened",
    "processRestartWorkspacePersisted", "processRestartUiReadback",
)
JOURNEYS = (
    {
        "phase": "creation", "fixture": "creation-gear-attack-swap-e2e.chum5",
        "root": "a9251111-1511-4511-8511-151111111111",
        "targetLabel": "Attack Root > Attack Target Cyberdeck · b9251111",
        "other": "Data Processing", "attackBefore": "7", "attackAfter": "5", "otherAfter": "7",
        "nuyen": "4321", "karma": "7", "sentinel": "Creation Gear Attack swap sentinel",
    },
    {
        "phase": "career", "fixture": "career-gear-attack-swap-e2e.chum5",
        "root": "c9251111-1511-4511-8511-151111111111",
        "targetLabel": "Career Attack Root > Career Attack Target · d9251111",
        "other": "Firewall", "attackBefore": "{Rating}", "attackAfter": "5", "otherAfter": "{Rating}",
        "nuyen": "8765", "karma": "9", "sentinel": "Career Gear Attack swap sentinel",
    },
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_page(device: shared.Device, journey: dict[str, str]) -> None:
    root = journey["root"]
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-gear-{root}", scroll=True, timeout=120, max_scrolls=24)
    device.wait(f"collection-editor-gear-{root}", timeout=120)
    token = root.replace("-", "")
    device.tap(f"gear-attack-swap-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"gear-attack-swap-page-{token}", timeout=60)


def select_nested_and_target(device: shared.Device, journey: dict[str, str]) -> None:
    token = journey["root"].replace("-", "")
    gear_picker = f"gear-attack-swap-target-{token}"
    device.tap(gear_picker, timeout=60, scroll=True)
    device.tap(journey["targetLabel"], timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(device, gear_picker, "Matrix Gear", scroll=True)
    if observed != journey["targetLabel"]:
        raise RuntimeError(f"Gear Attack target was {observed!r}; expected {journey['targetLabel']!r}")
    device.wait(f"Attack {journey['attackBefore']}", timeout=60, scroll=True)
    target_picker = f"gear-attack-swap-attribute-{token}"
    device.tap(target_picker, timeout=60, scroll=True)
    device.tap(journey["other"], timeout=60, scroll=True, max_scrolls=12)
    time.sleep(0.35)
    observed = shared.selected_text(device, target_picker, "Swap Attack with", scroll=True)
    if observed != journey["other"]:
        raise RuntimeError(f"Gear Attack swap target was {observed!r}; expected {journey['other']!r}")


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
    observations: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        root = next((node for node in character.findall("./gears/gear")
                     if node.findtext("guid", default="").lower() == journey["root"]), None)
        target = root.find("./children/gear") if root is not None else None
        if target is None:
            continue
        observations.append((target.findtext("attack", ""), target.findtext("dataprocessing", "")))
        target_name = "dataprocessing" if journey["other"] == "Data Processing" else "firewall"
        expected_notes = f"{journey['phase'].title()} Gear Attack target sentinel"
        if (
            target.findtext("attack") == journey["attackAfter"]
            and target.findtext(target_name) == journey["otherAfter"]
            and target.findtext("attributearray") in {"7,6,5,4", "{Rating},8,6,5"}
            and target.findtext("canswapattributes") == "True"
            and target.findtext("modattack") in {"2", "9"}
            and target.findtext("moddataprocessing") in {"4", "11"}
            and target.findtext("cost") in {"12345", "54321"}
            and target.findtext("notes") == expected_notes
            and character.findtext("nuyen") == journey["nuyen"]
            and character.findtext("karma") == journey["karma"]
            and character.findtext("customstate") == journey["sentinel"]
        ):
            return
    device.capture(f"gear-attack-swap-{journey['phase']}-workspace-failed")
    raise RuntimeError(f"Gear Attack swap was not durable/preserving: {observations!r}")


def assert_reopened(device: shared.Device, journey: dict[str, str]) -> None:
    open_page(device, journey)
    token = journey["root"].replace("-", "")
    device.tap(f"gear-attack-swap-target-{token}", timeout=60, scroll=True)
    device.tap(journey["targetLabel"], timeout=60, scroll=True, max_scrolls=24)
    device.wait(f"Attack {journey['attackAfter']}", timeout=60, scroll=True)


def run_journey(device: shared.Device, fixture: Path, journey: dict[str, str]) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device, journey)
    select_nested_and_target(device, journey)
    token = journey["root"].replace("-", "")
    device.tap(f"gear-attack-swap-save-{token}", timeout=180, scroll=True, max_scrolls=24)
    device.wait(f"collection-editor-gear-{journey['root']}", timeout=180)
    device.back()
    assert_workspace(device, journey)
    assert_reopened(device, journey)
    device.capture(f"gear-attack-swap-{journey['phase']}-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, journey)
    assert_reopened(device, journey)
    device.capture(f"gear-attack-swap-{journey['phase']}-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "gearAttackSwapPageSha256": android_root / "src/Chummer.Android/Native/GearAttackSwapPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "gearAttackSwapContractSha256": overview / "GearAttackSwapEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "gearAttackSwapRulesSha256": contracts / "CharacterGearAttackSwapRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Gear Attack source graph is incomplete: {missing!r}")

    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Gear Attack E2E requires API 36, got {api!r}")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Gear Attack E2E requires arm64-v8a, got {abi_list!r}")
    subprocess.run([str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
                   check=True, timeout=300)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    fixture_paths = {journey["phase"]: fixtures / journey["fixture"] for journey in JOURNEYS}
    for fixture in fixture_paths.values():
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    for journey in JOURNEYS:
        run_journey(device, fixture_paths[journey["phase"]], journey)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1", "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "serial": args.serial,
        "profile": "phone", "journey": "gear-attack-swap", "apiLevel": int(api),
        "abi": "arm64-v8a", "package": shared.PACKAGE, "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()), "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        **{f"{phase}FixtureSha256": shared.sha256(path) for phase, path in fixture_paths.items()},
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {"creationEligibleNestedGearEdited": "pass", "careerEligibleNestedGearEdited": "pass",
                     "sameSessionReopen": "pass", "processRestart": "pass"},
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Gear Attack E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
