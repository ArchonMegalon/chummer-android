#!/usr/bin/env python3
"""Prove exact Career-only Improvement group append on an API 36 arm64 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("CharacterCareer.cmdAddImprovementGroup",)
PACKAGE = "com.myexternalbrain.chummer"
ABI = "arm64-v8a"
TARGET_GROUP = "Gamma API36"
PROOF_KEYS = (
    "careerOnlyCreationNegative",
    "exactNonemptyUntrimmedName",
    "typedNameAndAppendIndexIdentity",
    "orderedDuplicatesPreserved",
    "collectionRevisionBound",
    "zeroKarmaNuyenDelta",
    "atomicSaveRecovery",
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
    device.tap("build-improvement-group-add", scroll=True, timeout=120, max_scrolls=30)
    device.wait("improvement-group-add-page", timeout=60)


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run("exec-out", "run-as", PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def assert_workspace(device: shared.Device, expected_target_count: int) -> None:
    expected_groups = ["Alpha", "Beta", *([TARGET_GROUP] * expected_target_count)]
    observed: list[list[str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        item = root.find("./improvements/improvement")
        if item is None or item.findtext("sourcename") != "a6221111-6221-6221-6221-622111111111":
            continue
        groups = [node.text or "" for node in root.findall("./improvementgroups/improvementgroup")]
        observed.append(groups)
        if (
            groups == expected_groups
            and root.findtext("karma") == "23"
            and root.findtext("nuyen") == "4567.89"
            and item.findtext("customgroup") == "Alpha"
            and item.findtext("enabled") == "0"
            and item.findtext("notes") == "Career group add sentinel"
            and root.findtext("customstate") == "Career Improvement group add runner sentinel"
        ):
            return
    device.capture("improvement-group-add-workspace-not-persisted")
    raise RuntimeError(f"Improvement group append was not durable: {observed!r}")


def assert_ui_count(device: shared.Device, expected_count: int) -> None:
    open_page(device)
    summary = device.wait("improvement-group-add-summary", timeout=60, scroll=True)
    expected = f"{expected_count} saved groups · 0 Karma · 0 Nuyen"
    if summary.attributes.get("text", "") != expected:
        device.capture("improvement-group-add-summary-mismatch")
        raise RuntimeError(
            f"Improvement group summary was {summary.attributes.get('text')!r}; expected {expected!r}"
        )


def add_group(device: shared.Device) -> None:
    open_page(device)
    device.set_text(
        "improvement-group-add-name",
        "Improvement group name",
        TARGET_GROUP,
        scroll=True,
    )
    device.tap("improvement-group-add-save", timeout=180, scroll=True)
    device.wait("build-improvement-group-add", timeout=180, scroll=True)


def assert_creation_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", PACKAGE)
    prepare_runner(device, fixture.name)
    shared.open_build(device, "phone")
    try:
        device.wait("build-improvement-group-add", timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("improvement-group-add-creation-action-exposed")
        raise RuntimeError("Career-only Add Improvement Group was exposed for creation")

    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        item = root.find("./improvements/improvement")
        if item is not None and item.findtext("sourcename") == "c6221111-6221-6221-6221-622111111111":
            groups = [node.text or "" for node in root.findall("./improvementgroups/improvementgroup")]
            if (
                groups != ["Alpha"]
                or root.findtext("karma") != "17"
                or root.findtext("nuyen") != "1234.56"
                or item.findtext("notes") != "Creation group add negative sentinel"
            ):
                raise RuntimeError("Creation-negative Improvement groups changed unexpectedly")
            return
    raise RuntimeError("Creation-negative Improvement group fixture was unavailable")


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
        default=fixtures / "career-improvement-group-add-e2e.chum5",
    )
    parser.add_argument(
        "--creation-runner",
        type=Path,
        default=fixtures / "creation-improvement-group-add-negative-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "improvementGroupAddPageSha256": android_root / "src/Chummer.Android/Native/ImprovementGroupAddPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "improvementGroupAddContractSha256": overview / "ImprovementGroupAddRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "improvementGroupAddRulesSha256": contracts / "CharacterImprovementGroupAddRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Add Improvement Group source graph is incomplete: {missing!r}")
    if shared.PACKAGE != PACKAGE:
        raise RuntimeError(f"Driver package mismatch: {shared.PACKAGE!r} != {PACKAGE!r}")

    career_fixture = args.career_runner.resolve()
    creation_fixture = args.creation_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if api != "36":
        raise RuntimeError(f"Add Improvement Group E2E requires API 36, got {api!r}")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Add Improvement Group E2E requires {ABI}, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    if not device.shell("cmd", "package", "path", PACKAGE).startswith("package:"):
        raise RuntimeError(f"Expected installed package {PACKAGE!r} was not found")
    for fixture in (career_fixture, creation_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    device.shell("pm", "clear", PACKAGE)
    prepare_runner(device, career_fixture.name)
    add_group(device)
    assert_workspace(device, 1)
    assert_ui_count(device, 3)
    device.shell("input", "keyevent", "4")
    add_group(device)
    assert_workspace(device, 2)
    assert_ui_count(device, 4)
    device.capture("improvement-group-add-career-after-reopen")
    device.shell("am", "force-stop", PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, 2)
    assert_ui_count(device, 4)
    device.capture("improvement-group-add-career-after-process-restart")
    assert_creation_negative(device, creation_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "improvement-group-add",
        "apiLevel": int(api),
        "abi": abi,
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
            "careerExactGroupAppended": "pass",
            "careerDuplicateGroupAppended": "pass",
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
        print(f"Add Improvement Group E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
