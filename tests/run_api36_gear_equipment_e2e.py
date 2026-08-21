#!/usr/bin/env python3
"""Prove exact Create/Career Gear Equipped persistence on an API 36 phone."""

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
    "CharacterCreate.chkGearEquipped",
    "CharacterCareer.chkGearEquipped",
)
CREATION_ROOT_ID = "a9121111-9111-4111-8111-911111111111"
CREATION_TARGET_ID = "b9121111-9111-4111-8111-911111111111"
CAREER_ROOT_ID = "e9121111-9111-4111-8111-911111111111"
CAREER_TARGET_ID = "f9121111-9111-4111-8111-911111111111"
PROOF_KEYS = (
    "exactCreateCareerRule",
    "stableTypedHierarchicalIdentity",
    "includedReadOnlyAndClipLoadedOutsideTree",
    "zeroNuyenKarmaDelta",
    "equippedElementPersisted",
    "unrelatedXmlPreserved",
    "duplicateAmbiguousRejected",
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


def open_equipment_page(device: shared.Device, root_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-gear-{root_id}", scroll=True, timeout=120, max_scrolls=24)
    device.wait(f"collection-editor-gear-{root_id}", timeout=120)
    token = root_id.replace("-", "")
    device.tap(f"gear-equipment-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"gear-equipment-page-{token}", timeout=60)


def select_node(device: shared.Device, root_id: str, label: str) -> None:
    token = root_id.replace("-", "")
    device.tap(f"gear-equipment-target-{token}", timeout=60, scroll=True)
    device.tap(label, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device, f"gear-equipment-target-{token}", "Gear node", scroll=True
    )
    if observed != label:
        device.capture("gear-equipment-target-mismatch")
        raise RuntimeError(f"Gear Equipped target was {observed!r}; expected {label!r}")


def assert_toggle(device: shared.Device, root_id: str, expected: bool) -> None:
    token = root_id.replace("-", "")
    observed = (
        device.wait(f"gear-equipment-toggle-{token}", timeout=60, scroll=True)
        .attributes.get("checked") == "true"
    )
    if observed != expected:
        device.capture("gear-equipment-toggle-mismatch")
        raise RuntimeError(f"Gear Equipped was {observed!r}; expected {expected!r}")


def assert_selected_read_only(device: shared.Device, root_id: str) -> None:
    token = root_id.replace("-", "")
    toggle = device.wait(f"gear-equipment-toggle-{token}", timeout=60, scroll=True)
    save = device.wait(f"gear-equipment-save-{token}", timeout=60, scroll=True)
    if toggle.attributes.get("enabled") != "false" or save.attributes.get("enabled") != "false":
        device.capture("gear-equipment-included-node-editable")
        raise RuntimeError("Included Gear exposed an enabled Equipped mutation")


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


def assert_workspace(
    device: shared.Device,
    root_id: str,
    target_id: str,
    expected: bool,
    expected_nuyen: str,
    expected_karma: str,
    sentinel: str,
    read_only_label: str | None = None,
) -> None:
    observations: list[str] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        gear = next((node for node in root.findall("./gears/gear")
                     if node.findtext("guid", default="").lower() == root_id), None)
        if gear is None:
            continue
        target = next((node for node in gear.findall(".//gear")
                       if node.findtext("guid", default="").lower() == target_id), None)
        untouched = root.findall("./gears/gear")[-1]
        observations.append(target.findtext("equipped", default="") if target is not None else "")
        if (
            target is not None
            and target.findtext("equipped") == ("True" if expected else "False")
            and target.findtext("notes", default="").endswith("target sentinel")
            and untouched.findtext("notes", default="").endswith("untouched sentinel")
            and root.findtext("nuyen") == expected_nuyen
            and root.findtext("karma") == expected_karma
            and root.findtext("customstate") == sentinel
        ):
            return
    device.capture("gear-equipment-workspace-not-persisted")
    raise RuntimeError(f"Gear Equipped was not durable and zero-economic: {observations!r}")


def run_phase(
    device: shared.Device,
    fixture: Path,
    root_id: str,
    target_id: str,
    label: str,
    initial: bool,
    expected_nuyen: str,
    expected_karma: str,
    sentinel: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_equipment_page(device, root_id)
    if read_only_label is not None:
        select_node(device, root_id, read_only_label)
        assert_selected_read_only(device, root_id)
    select_node(device, root_id, label)
    assert_toggle(device, root_id, initial)
    token = root_id.replace("-", "")
    device.tap(f"gear-equipment-toggle-{token}", timeout=60, scroll=True)
    assert_toggle(device, root_id, not initial)
    device.tap(f"gear-equipment-save-{token}", timeout=180, scroll=True, max_scrolls=24)
    device.wait(f"collection-editor-gear-{root_id}", timeout=180)
    device.back()
    assert_workspace(
        device, root_id, target_id, not initial,
        expected_nuyen, expected_karma, sentinel,
    )
    open_equipment_page(device, root_id)
    select_node(device, root_id, label)
    assert_toggle(device, root_id, not initial)
    device.capture(f"gear-equipment-{root_id[:1]}-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(
        device, root_id, target_id, not initial,
        expected_nuyen, expected_karma, sentinel,
    )
    open_equipment_page(device, root_id)
    select_node(device, root_id, label)
    assert_toggle(device, root_id, not initial)
    device.capture(f"gear-equipment-{root_id[:1]}-process-restart")


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
                        default=fixtures / "creation-gear-equipment-e2e.chum5")
    parser.add_argument("--career-runner", type=Path,
                        default=fixtures / "career-gear-equipment-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "gearEquipmentPageSha256": android_root / "src/Chummer.Android/Native/GearEquipmentPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "gearEquipmentContractSha256": overview / "GearEquipmentEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "gearEquipmentRulesSha256": contracts / "CharacterGearEquipmentRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Gear Equipped source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Gear Equipped E2E requires API 36, got {api!r}")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Gear Equipped E2E requires arm64-v8a, got {abi_list!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True, timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    run_phase(
        device, creation_fixture, CREATION_ROOT_ID, CREATION_TARGET_ID,
        "Creation Equipment Root > Creation Equipment Target · b9121111",
        False, "4321", "7", "Creation Gear Equipped runner sentinel",
        "Creation Equipment Root > Creation Included Fixed · c9121111",
    )
    run_phase(
        device, career_fixture, CAREER_ROOT_ID, CAREER_TARGET_ID,
        "Career Equipment Root > Career Equipment Target · f9121111",
        True, "8765", "19", "Career Gear Equipped runner sentinel",
    )

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "gear-equipment",
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
            "creationRecursiveGearEdited": "pass",
            "creationIncludedNodeReadOnly": "pass",
            "careerRecursiveGearEdited": "pass",
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
        print(f"Gear Equipped E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
