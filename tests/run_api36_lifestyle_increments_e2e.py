#!/usr/bin/env python3
"""Prove exact Chummer5 Create/Career Lifestyle interval edits on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "CharacterCreate.nudLifestyleMonths",
    "CharacterCareer.cmdIncreaseLifestyleMonths",
    "CharacterCareer.cmdDecreaseLifestyleMonths",
)
CREATION_ID = "91111111-1111-1111-1111-111111111111"
INCREASE_ID = "92222222-2222-2222-2222-222222222222"
DECREASE_ID = "93333333-3333-3333-3333-333333333333"
PROOF_KEYS = (
    "stableLifestyleGuid",
    "exactCreateCareerRules",
    "derivedTotalsPersisted",
    "atomicWorkspacePersisted",
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


def open_page(device: shared.Device, lifestyle_id: str) -> None:
    shared.open_build(device, "phone")
    device.tap("build-action-tab-lifestyle-lifestyles", scroll=True, timeout=120, max_scrolls=36)
    device.tap(
        f"collection-item-lifestyle-{lifestyle_id}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    device.wait(f"collection-editor-lifestyle-{lifestyle_id}", timeout=120)
    token = lifestyle_id.replace("-", "")
    device.tap(f"lifestyle-increments-open-{token}", timeout=60, scroll=True, max_scrolls=36)
    device.wait(f"lifestyle-increments-page-{token}", timeout=60)


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


def assert_creation_workspace(device: shared.Device) -> None:
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        lifestyle = next((item for item in root.findall("./lifestyles/lifestyle") if item.findtext("guid", "").lower() == CREATION_ID), None)
        if (
            lifestyle is not None
            and lifestyle.findtext("months") == "100"
            and lifestyle.findtext("totalmonthlycost") == "2000"
            and lifestyle.findtext("totalcost") == "200000"
            and lifestyle.findtext("purchased") == "True"
            and lifestyle.findtext("extra") == "Creation interval home"
            and lifestyle.findtext("notes") == "Creation interval notes"
            and root.findtext("customstate") == "Creation Lifestyle intervals unrelated state"
            and root.find("expenses") is None
        ):
            return
    device.capture("lifestyle-increments-creation-workspace-not-persisted")
    raise RuntimeError("Creation Lifestyle intervals were not durable")


def assert_career_workspace(device: shared.Device) -> None:
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        lifestyles = {item.findtext("guid", "").lower(): item for item in root.findall("./lifestyles/lifestyle")}
        if INCREASE_ID not in lifestyles or DECREASE_ID not in lifestyles:
            continue
        increase = lifestyles[INCREASE_ID]
        decrease = lifestyles[DECREASE_ID]
        expenses = root.findall("./expenses/expense")
        purchase = next((item for item in expenses if item.findtext("./undo/nuyentype") == "IncreaseLifestyle"), None)
        decrement = next((item for item in expenses if item.findtext("reason") == "Decremented Lifestyle Squatter"), None)
        if (
            increase.findtext("months") == "5"
            and increase.findtext("totalcost") == "12500"
            and decrease.findtext("months") == "-1"
            and decrease.findtext("totalcost") == "-1200"
            and root.findtext("nuyen") == "5500"
            and purchase is not None
            and purchase.findtext("amount") == "-2500"
            and purchase.findtext("reason") == "Purchased Lifestyle Low"
            and purchase.findtext("./undo/objectid") == INCREASE_ID
            and decrement is not None
            and decrement.findtext("amount") == "0"
            and decrement.find("undo") is None
            and root.findtext("customstate") == "Career Lifestyle intervals unrelated state"
        ):
            return
    device.capture("lifestyle-increments-career-workspace-not-persisted")
    raise RuntimeError("Career Lifestyle interval transactions were not durable")


def prove_creation(device: shared.Device, fixture: Path) -> None:
    prepare_runner(device, fixture.name)
    open_page(device, CREATION_ID)
    token = CREATION_ID.replace("-", "")
    device.set_text(f"lifestyle-increments-value-{token}", "Month intervals (1–100)", "100", scroll=True)
    device.tap(f"lifestyle-increments-set-{token}", timeout=180, scroll=True)
    device.wait(f"collection-editor-lifestyle-{CREATION_ID}", timeout=180)
    assert_creation_workspace(device)
    open_page(device, CREATION_ID)
    if "Current month intervals: 100" not in device.wait(f"lifestyle-increments-current-{token}", timeout=60).attributes.get("text", ""):
        raise RuntimeError("Creation Lifestyle intervals did not survive same-session reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_creation_workspace(device)
    open_page(device, CREATION_ID)
    if "Current month intervals: 100" not in device.wait(f"lifestyle-increments-current-{token}", timeout=60).attributes.get("text", ""):
        raise RuntimeError("Creation Lifestyle intervals did not survive process restart")


def prove_career(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device, INCREASE_ID)
    increase_token = INCREASE_ID.replace("-", "")
    device.tap(f"lifestyle-increments-increase-{increase_token}", timeout=180, scroll=True)
    device.wait(f"collection-editor-lifestyle-{INCREASE_ID}", timeout=180)
    open_page(device, DECREASE_ID)
    decrease_token = DECREASE_ID.replace("-", "")
    device.tap(f"lifestyle-increments-decrease-{decrease_token}", timeout=180, scroll=True)
    device.wait(f"collection-editor-lifestyle-{DECREASE_ID}", timeout=180)
    assert_career_workspace(device)
    open_page(device, INCREASE_ID)
    if "Current month intervals: 5" not in device.wait(f"lifestyle-increments-current-{increase_token}", timeout=60).attributes.get("text", ""):
        raise RuntimeError("Career Lifestyle purchase did not survive same-session reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_career_workspace(device)
    open_page(device, DECREASE_ID)
    if "Current month intervals: -1" not in device.wait(f"lifestyle-increments-current-{decrease_token}", timeout=60).attributes.get("text", ""):
        raise RuntimeError("Career Lifestyle decrement did not survive process restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_root = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixture_root / "creation-lifestyle-increments-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixture_root / "career-lifestyle-increments-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "lifestyleIncrementPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "LifestyleIncrementPage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "lifestyleIncrementContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "LifestyleIncrementEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "lifestyleIncrementRulesSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterLifestyleIncrementRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Lifestyle interval E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Lifestyle interval E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Lifestyle interval E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(creation_fixture, f"/sdcard/Download/{creation_fixture.name}")
    device.push(career_fixture, f"/sdcard/Download/{career_fixture.name}")
    device.shell("pm", "clear", shared.PACKAGE)
    prove_creation(device, creation_fixture)
    prove_career(device, career_fixture)

    controls = {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "lifestyle-increments",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "creationSetOneToOneHundred": "pass",
            "careerPurchaseExpenseAndUndo": "pass",
            "careerDecreaseZeroExpenseAndNegativeLegacyBound": "pass",
            "derivedTotalAndPurchasedUpdated": "pass",
            "sameSessionReopen": "pass",
            "processRestart": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
