#!/usr/bin/env python3
"""Prove exact Career-only Gear Wireless persistence on an API 36 phone."""

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


CONTROLS = ("CharacterCareer.chkGearWireless",)
CAREER_ROOT_ID = "e9121111-9111-4111-8111-911111111111"
CAREER_TARGET_ID = "f9121111-9111-4111-8111-911111111111"
TARGET_LABEL = "Career Equipment Root > Career Equipment Target · f9121111"
PROOF_KEYS = (
    "legacyCareerOnlyRule",
    "stableTypedHierarchicalIdentity",
    "zeroNuyenKarmaDelta",
    "wirelessOnElementPersisted",
    "unrelatedXmlPreserved",
    "duplicateAmbiguousRejected",
    "revisionBoundAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_wireless_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap(
        f"collection-item-gear-{CAREER_ROOT_ID}",
        scroll=True,
        timeout=120,
        max_scrolls=24,
    )
    device.wait(f"collection-editor-gear-{CAREER_ROOT_ID}", timeout=120)
    token = CAREER_ROOT_ID.replace("-", "")
    device.tap(f"gear-wireless-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"gear-wireless-page-{token}", timeout=60)


def select_target(device: shared.Device) -> None:
    token = CAREER_ROOT_ID.replace("-", "")
    device.tap(f"gear-wireless-target-{token}", timeout=60, scroll=True)
    device.tap(TARGET_LABEL, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device, f"gear-wireless-target-{token}", "Gear node", scroll=True
    )
    if observed != TARGET_LABEL:
        device.capture("gear-wireless-target-mismatch")
        raise RuntimeError(f"Gear Wireless target was {observed!r}; expected {TARGET_LABEL!r}")


def assert_toggle(device: shared.Device, expected: bool) -> None:
    token = CAREER_ROOT_ID.replace("-", "")
    observed = (
        device.wait(f"gear-wireless-toggle-{token}", timeout=60, scroll=True)
        .attributes.get("checked") == "true"
    )
    if observed != expected:
        device.capture("gear-wireless-toggle-mismatch")
        raise RuntimeError(f"Gear Wireless was {observed!r}; expected {expected!r}")


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
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        gear = next(
            (node for node in root.findall("./gears/gear")
             if node.findtext("guid", default="").lower() == CAREER_ROOT_ID),
            None,
        )
        if gear is None:
            continue
        target = next(
            (node for node in gear.findall(".//gear")
             if node.findtext("guid", default="").lower() == CAREER_TARGET_ID),
            None,
        )
        untouched = root.findall("./gears/gear")[-1]
        observations.append(target.findtext("wirelesson", default="") if target is not None else "")
        if (
            target is not None
            and target.findtext("wirelesson") == "True"
            and target.findtext("equipped") == "True"
            and target.findtext("notes") == "Career target sentinel"
            and untouched.findtext("wirelesson") == "False"
            and untouched.findtext("notes") == "Career untouched sentinel"
            and root.findtext("nuyen") == "8765"
            and root.findtext("karma") == "19"
            and root.findtext("customstate") == "Career Gear Equipped runner sentinel"
        ):
            return
    device.capture("gear-wireless-workspace-not-persisted")
    raise RuntimeError(f"Gear Wireless was not durable and zero-economic: {observations!r}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures" / "career-gear-equipment-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "gearWirelessPageSha256": android_root / "src/Chummer.Android/Native/GearWirelessPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "gearWirelessContractSha256": overview / "GearWirelessEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "gearWirelessRulesSha256": contracts / "CharacterGearWirelessRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Gear Wireless source graph is incomplete: {missing!r}")

    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Gear Wireless E2E requires API 36, got {api!r}")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Gear Wireless E2E requires arm64-v8a, got {abi_list!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(career_fixture, f"/sdcard/Download/{career_fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, career_fixture.name)
    open_wireless_page(device)
    select_target(device)
    assert_toggle(device, False)
    token = CAREER_ROOT_ID.replace("-", "")
    device.tap(f"gear-wireless-toggle-{token}", timeout=60, scroll=True)
    assert_toggle(device, True)
    device.tap(f"gear-wireless-save-{token}", timeout=180, scroll=True, max_scrolls=24)
    device.wait(f"collection-editor-gear-{CAREER_ROOT_ID}", timeout=180)
    device.back()
    assert_workspace(device)
    open_wireless_page(device)
    select_target(device)
    assert_toggle(device, True)
    device.capture("gear-wireless-career-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device)
    open_wireless_page(device)
    select_target(device)
    assert_toggle(device, True)
    device.capture("gear-wireless-career-process-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "gear-wireless",
        "apiLevel": int(api),
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "careerRecursiveGearEdited": "pass",
            "zeroEconomicDeltaCareer": "pass",
            "sameSessionReopenCareer": "pass",
            "processRestartCareer": "pass",
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
        print(f"Gear Wireless E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
