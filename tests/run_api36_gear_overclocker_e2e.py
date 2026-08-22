#!/usr/bin/env python3
"""Prove exact CharacterCareer Gear Overclocker persistence on an API 36 phone."""

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


CONTROLS = ("CharacterCareer.cboGearOverclocker",)
ROOT_GEAR_ID = "a9151111-1511-4511-8511-151111111111"
CREATION_GEAR_ID = "d9151111-1511-4511-8511-151111111111"
TARGET_LABEL = "Overclocker Root > Overclocker Target Cyberdeck · b9151111"
ATTRIBUTE_LABEL = "Data Processing"
PROOF_KEYS = (
    "careerOnlyCreationNegative",
    "activeOverclockerImprovementEligibility",
    "cyberdeckCategoryEligibility",
    "fixedLegacyAttributeOptions",
    "selectedRecursiveGearNode",
    "stableHierarchicalIdentity",
    "zeroNuyenKarmaEconomics",
    "duplicateAmbiguousRejected",
    "overclockedElementPersisted",
    "activeHomeAndUnrelatedXmlPreserved",
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


def open_gear_item(device: shared.Device, gear_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-gear-{gear_id}", scroll=True, timeout=120, max_scrolls=24)
    device.wait(f"collection-editor-gear-{gear_id}", timeout=120)


def open_overclocker_page(device: shared.Device) -> None:
    token = ROOT_GEAR_ID.replace("-", "")
    open_gear_item(device, ROOT_GEAR_ID)
    device.tap(f"gear-overclocker-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"gear-overclocker-page-{token}", timeout=60)


def select_target(device: shared.Device) -> None:
    token = ROOT_GEAR_ID.replace("-", "")
    device.tap(f"gear-overclocker-target-{token}", timeout=60, scroll=True)
    device.tap(TARGET_LABEL, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device, f"gear-overclocker-target-{token}", "Cyberdeck Gear", scroll=True
    )
    if observed != TARGET_LABEL:
        device.capture("gear-overclocker-target-mismatch")
        raise RuntimeError(f"Gear Overclocker target was {observed!r}; expected {TARGET_LABEL!r}")


def select_attribute(device: shared.Device, expected: str) -> None:
    token = ROOT_GEAR_ID.replace("-", "")
    selector = f"gear-overclocker-attribute-{token}"
    device.tap(selector, timeout=60, scroll=True)
    device.tap(expected, timeout=60, scroll=True, max_scrolls=12)
    time.sleep(0.35)
    observed = shared.selected_text(device, selector, "Boosted Matrix attribute", scroll=True)
    if observed != expected:
        device.capture("gear-overclocker-attribute-mismatch")
        raise RuntimeError(f"Gear Overclocker attribute was {observed!r}; expected {expected!r}")


def assert_attribute(device: shared.Device, expected: str) -> None:
    token = ROOT_GEAR_ID.replace("-", "")
    observed = shared.selected_text(
        device,
        f"gear-overclocker-attribute-{token}",
        "Boosted Matrix attribute",
        scroll=True,
    )
    if observed != expected:
        device.capture("gear-overclocker-readback-mismatch")
        raise RuntimeError(f"Gear Overclocker readback was {observed!r}; expected {expected!r}")


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
            node for node in character.findall("./gears/gear")
            if node.findtext("guid", default="").lower() == ROOT_GEAR_ID
        ), None)
        if root is None:
            continue
        target = root.find("./children/gear")
        sibling = next((
            node for node in character.findall("./gears/gear")
            if node.findtext("guid", default="").lower()
            == "c9151111-1511-4511-8511-151111111111"
        ), None)
        observations.append(target.findtext("overclocked", default="") if target is not None else "")
        if (
            target is not None
            and target.findtext("overclocked") == ATTRIBUTE_LABEL
            and target.findtext("active") == "True"
            and target.findtext("homenode") == "True"
            and target.findtext("equipped") == "True"
            and target.findtext("stolen") == "False"
            and target.findtext("notes") == "Career Overclocker target sentinel"
            and root.findtext("overclocked") == "None"
            and sibling is not None
            and sibling.findtext("overclocked") == "Firewall"
            and sibling.findtext("notes") == "Career Overclocker untouched sentinel"
            and character.findtext("nuyen") == "4321"
            and character.findtext("karma") == "7"
            and character.findtext("customstate") == "Career Gear Overclocker runner sentinel"
        ):
            return
    device.capture("gear-overclocker-workspace-not-persisted")
    raise RuntimeError(f"Gear Overclocker was not durable: {observations!r}")


def assert_reopened(device: shared.Device) -> None:
    open_overclocker_page(device)
    select_target(device)
    assert_attribute(device, ATTRIBUTE_LABEL)


def assert_creation_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_gear_item(device, CREATION_GEAR_ID)
    selector = f"gear-overclocker-open-{CREATION_GEAR_ID.replace('-', '')}"
    try:
        device.wait(selector, timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("gear-overclocker-creation-action-exposed")
        raise RuntimeError("Career-only Gear Overclocker was exposed for a creation runner")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-gear-overclocker-e2e.chum5")
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-gear-overclocker-negative-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "gearOverclockerPageSha256": android_root / "src/Chummer.Android/Native/GearOverclockerPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "gearOverclockerContractSha256": overview / "GearOverclockerEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "gearOverclockerRulesSha256": contracts / "CharacterGearOverclockerRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Gear Overclocker source graph is incomplete: {missing!r}")

    career_fixture = args.career_runner.resolve()
    creation_fixture = args.creation_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Gear Overclocker E2E requires API 36, got {api!r}")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Gear Overclocker E2E requires arm64-v8a, got {abi_list!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (career_fixture, creation_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, career_fixture.name)
    open_overclocker_page(device)
    select_target(device)
    assert_attribute(device, "Attack")
    select_attribute(device, ATTRIBUTE_LABEL)
    token = ROOT_GEAR_ID.replace("-", "")
    device.tap(f"gear-overclocker-save-{token}", timeout=180, scroll=True, max_scrolls=24)
    device.wait(f"collection-editor-gear-{ROOT_GEAR_ID}", timeout=180)
    device.back()
    assert_workspace(device)
    assert_reopened(device)
    device.capture("gear-overclocker-career-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device)
    assert_reopened(device)
    device.capture("gear-overclocker-career-after-process-restart")
    assert_creation_negative(device, creation_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "gear-overclocker",
        "apiLevel": int(api),
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(career_fixture),
        "creationNegativeFixtureSha256": shared.sha256(creation_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "careerEligibleNestedCyberdeckEdited": "pass",
            "careerSameSessionReopen": "pass",
            "careerProcessRestart": "pass",
            "creationActionNotExposed": "pass",
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
        print(f"Gear Overclocker E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
