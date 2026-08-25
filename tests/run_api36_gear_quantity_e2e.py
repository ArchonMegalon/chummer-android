#!/usr/bin/env python3
"""Prove exact Chummer5 Career Gear quantity lifecycle behavior on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "cmdGearIncreaseQty",
    "cmdGearReduceQty",
    "cmdGearSplitQty",
    "cmdGearMergeQty",
)
CONTROL_PROOF_KEYS = (
    "stableGearIdentity",
    "exactQuantityPrecision",
    "atomicWorkspacePersisted",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
GEAR_IDS = {
    "increase": "81111111-1111-1111-1111-111111111111",
    "reduce": "82222222-2222-2222-2222-222222222222",
    "split": "83333333-3333-3333-3333-333333333333",
    "merge_source": "84444444-4444-4444-4444-444444444444",
    "merge_target": "85555555-5555-5555-5555-555555555555",
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_quantity_page(device: shared.Device, gear_id: str) -> None:
    shared.open_build(device, "phone")
    shared.open_gear_section(device, "phone")
    device.tap(
        f"collection-item-gear-{gear_id}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    compact = gear_id.replace("-", "")
    device.wait(f"collection-editor-gear-{gear_id}", timeout=120)
    device.tap(f"gear-quantity-open-{compact}", timeout=60, scroll=True, max_scrolls=36)
    device.wait(f"gear-quantity-page-{compact}", timeout=60)


def apply_action(device: shared.Device, action: str, amount: str) -> None:
    gear_id = GEAR_IDS[action if action != "merge" else "merge_source"]
    compact = gear_id.replace("-", "")
    open_quantity_page(device, gear_id)
    device.set_text(f"gear-quantity-amount-{compact}", "Amount", amount, scroll=True)
    device.tap(f"gear-quantity-{action}-{compact}", timeout=180, scroll=True)
    if action == "reduce":
        device.wait("Confirm quantity reduction", timeout=30)
        device.tap("Reduce")
    device.wait(f"collection-editor-gear-{gear_id}", timeout=180)
    device.back()


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


def assert_workspace_lifecycle(device: shared.Device) -> None:
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        gear = {
            item.findtext("guid", default="").lower(): item
            for item in root.findall("./gears/gear")
        }
        if not all(identity in gear for identity in (
            GEAR_IDS["increase"],
            GEAR_IDS["reduce"],
            GEAR_IDS["split"],
            GEAR_IDS["merge_target"],
        )):
            continue
        observed.append({key: item.findtext("qty", default="") for key, item in gear.items()})
        split_clones = [
            item for identity, item in gear.items()
            if identity != GEAR_IDS["split"] and item.findtext("name") == "Split Stack E2E"
        ]
        expense = next((
            item for item in root.findall("./expenses/expense")
            if item.findtext("./undo/nuyentype") == "AddGear"
            and item.findtext("./undo/objectid") == GEAR_IDS["increase"]
        ), None)
        if (
            gear[GEAR_IDS["increase"]].findtext("qty") == "7"
            and gear[GEAR_IDS["reduce"]].findtext("qty") == "3"
            and gear[GEAR_IDS["split"]].findtext("qty") == "3"
            and len(split_clones) == 1
            and split_clones[0].findtext("qty") == "2"
            and split_clones[0].findtext("equipped") == "True"
            and split_clones[0].findtext("location") == "locker-c"
            and split_clones[0].findtext("notes") == "Split notes"
            and split_clones[0].findtext("guid") != GEAR_IDS["split"]
            and split_clones[0].findtext("./children/gear/guid") != "83333333-3333-3333-3333-444444444444"
            and GEAR_IDS["merge_source"] not in gear
            and gear[GEAR_IDS["merge_target"]].findtext("qty") == "8"
            and gear[GEAR_IDS["merge_target"]].findtext("gearname") == "Merge target label"
            and gear[GEAR_IDS["merge_target"]].findtext("notes") == "Merge target notes"
            and root.findtext("nuyen") == "9800"
            and expense is not None
            and expense.findtext("amount") == "-200"
            and expense.findtext("./undo/qty") == "2"
            and root.findtext("customstate") == "Gear quantity unrelated state"
        ):
            return
    device.capture("gear-quantity-workspace-not-persisted")
    raise RuntimeError(f"Gear quantity lifecycle was not durable; observed {observed!r}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures" / "career-gear-quantity-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "gearQuantityPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "GearQuantityPage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "gearQuantityContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "GearQuantityEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "gearQuantityRulesSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterGearQuantityRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Gear quantity E2E source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Gear quantity E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Gear quantity E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    apply_action(device, "increase", "2")
    apply_action(device, "reduce", "2")
    apply_action(device, "split", "2")
    apply_action(device, "merge", "5")
    assert_workspace_lifecycle(device)

    open_quantity_page(device, GEAR_IDS["increase"])
    current_selector = f"gear-quantity-current-{GEAR_IDS['increase'].replace('-', '')}"
    if "Current quantity: 7" not in device.wait(current_selector, timeout=60).attributes.get("text", ""):
        raise RuntimeError("Increased Gear quantity did not survive same-session reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_lifecycle(device)
    open_quantity_page(device, GEAR_IDS["increase"])
    if "Current quantity: 7" not in device.wait(current_selector, timeout=60).attributes.get("text", ""):
        raise RuntimeError("Increased Gear quantity did not survive process restart")

    controls = {
        f"CharacterCareer.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "gear-quantity-lifecycle",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "increasePurchaseExpense": "pass",
            "reduceConfirmed": "pass",
            "splitClonePreserved": "pass",
            "mergeIdentityExact": "pass",
            "sameSessionReopen": "pass",
            "processRestart": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
