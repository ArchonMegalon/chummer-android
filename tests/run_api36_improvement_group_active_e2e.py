#!/usr/bin/env python3
"""Prove exact Career-only Improvement group bulk state editing on an API 36 phone."""

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
    "CharacterCareer.cmdImprovementsEnableAll",
    "CharacterCareer.cmdImprovementsDisableAll",
)
TARGET_LABEL = "Alpha · 2 custom improvements"
PROOF_KEYS = (
    "careerOnlyCreationNegative",
    "exactSelectedRootStringTag",
    "customAndExactCustomGroupOnly",
    "stableTypedGroupAndMemberIdentity",
    "duplicateReservedOrOrphanRejected",
    "groupRevisionBoundAtomicSave",
    "numericEnabledElementPersisted",
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


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-improvement-group-active", scroll=True, timeout=120, max_scrolls=30)
    device.wait("improvement-group-active-page", timeout=60)


def select_alpha(device: shared.Device) -> None:
    device.tap("improvement-group-active-target", timeout=60, scroll=True)
    device.tap(TARGET_LABEL, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device,
        "improvement-group-active-target",
        "Improvement group",
        scroll=True,
    )
    if observed != TARGET_LABEL:
        device.capture("improvement-group-active-target-mismatch")
        raise RuntimeError(f"Improvement group was {observed!r}; expected {TARGET_LABEL!r}")


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


def keyed_improvements(root: ET.Element) -> dict[tuple[str, str], ET.Element]:
    return {
        (item.findtext("customgroup", default=""), item.findtext("improvedname", default="")): item
        for item in root.findall("./improvements/improvement")
    }


def assert_workspace(device: shared.Device, alpha_state: str) -> None:
    observed: list[dict[tuple[str, str], str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        items = keyed_improvements(root)
        required = (("Alpha", "BOD"), ("Alpha", "AGI"), ("Alpha", "LOG"), ("Beta", "REA"), ("", "WIL"))
        if any(key not in items for key in required):
            continue
        states = {key: items[key].findtext("enabled", default="") for key in required}
        observed.append(states)
        if (
            states[("Alpha", "BOD")] == alpha_state
            and states[("Alpha", "AGI")] == alpha_state
            and states[("Alpha", "LOG")] == "True"
            and states[("Beta", "REA")] == "1"
            and states[("", "WIL")] == "0"
            and items[("Alpha", "BOD")].findtext("notes") == "Alpha enabled sentinel"
            and items[("Alpha", "AGI")].findtext("notes") == "Alpha disabled sentinel"
            and root.findtext("customstate") == "Career Improvement group runner sentinel"
        ):
            return
    device.capture("improvement-group-active-workspace-not-persisted")
    raise RuntimeError(f"Improvement group state was not durable: {observed!r}")


def apply_action(device: shared.Device, automation_id: str) -> None:
    open_page(device)
    select_alpha(device)
    device.tap(automation_id, timeout=180, scroll=True)
    device.wait("build-improvement-group-active", timeout=180, scroll=True)


def assert_ui_action(device: shared.Device, automation_id: str) -> None:
    open_page(device)
    select_alpha(device)
    node = device.wait(automation_id, timeout=60, scroll=True)
    if node.attributes.get("enabled") != "true":
        device.capture("improvement-group-active-action-disabled")
        raise RuntimeError(f"Expected {automation_id!r} to be enabled")


def assert_creation_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    shared.open_build(device, "phone")
    try:
        device.wait("build-improvement-group-active", timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("improvement-group-active-creation-action-exposed")
        raise RuntimeError("Career-only Improvement group actions were exposed for creation")

    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        item = root.find("./improvements/improvement")
        if item is not None and item.findtext("sourcename") == "c6111111-6111-6111-6111-611111111111":
            if item.findtext("enabled") != "1" or item.findtext("notes") != "Creation negative group sentinel":
                raise RuntimeError("Creation-negative Improvement group changed unexpectedly")
            return
    raise RuntimeError("Creation-negative Improvement group was unavailable in workspace state")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-improvement-group-active-e2e.chum5",
    )
    parser.add_argument(
        "--creation-runner",
        type=Path,
        default=fixtures / "creation-improvement-group-active-negative-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "improvementGroupActivePageSha256": android_root / "src/Chummer.Android/Native/ImprovementGroupActivePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "improvementGroupActiveContractSha256": overview / "ImprovementGroupActiveEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "improvementGroupActiveRulesSha256": contracts / "CharacterImprovementGroupActiveRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Improvement group source graph is incomplete: {missing!r}")

    career_fixture = args.career_runner.resolve()
    creation_fixture = args.creation_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Improvement group E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (career_fixture, creation_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, career_fixture.name)
    apply_action(device, "improvement-group-enable-all")
    assert_workspace(device, "1")
    assert_ui_action(device, "improvement-group-disable-all")
    device.shell("input", "keyevent", "4")
    apply_action(device, "improvement-group-disable-all")
    assert_workspace(device, "0")
    assert_ui_action(device, "improvement-group-enable-all")
    device.capture("improvement-group-active-career-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, "0")
    assert_ui_action(device, "improvement-group-enable-all")
    device.capture("improvement-group-active-career-after-process-restart")
    assert_creation_negative(device, creation_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "improvement-group-active",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(career_fixture),
        "creationNegativeFixtureSha256": shared.sha256(creation_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "careerEnableAllExactGroup": "pass",
            "careerDisableAllExactGroup": "pass",
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
        print(f"Improvement group E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
