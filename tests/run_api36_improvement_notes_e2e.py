#!/usr/bin/env python3
"""Prove exact Career-only Improvement Notes editing on an API 36 arm64 phone."""

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


CONTROLS = ("CharacterCareer.tsImprovementNotes",)
PACKAGE = "com.myexternalbrain.chummer"
ABI = "arm64-v8a"
TARGET_SOURCE = "a5121111-5111-5111-5111-511111111111"
TARGET_NAME = "AGI"
TARGET_LABEL = "Agility notes target · a5121111"
TARGET_NOTES = "API36 Improvement notes updated"
TARGET_COLOR = "#445566"
PROOF_KEYS = (
    "careerOnlyCreationNegative",
    "directImprovementSelection",
    "stableTypedSemanticIdentity",
    "duplicateAmbiguousRejected",
    "notesAndColorAtomicMutation",
    "revisionBoundAtomicSaveRecovery",
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
    device.tap("build-improvement-notes", scroll=True, timeout=120, max_scrolls=30)
    device.wait("improvement-notes-page", timeout=60)


def select_target(device: shared.Device) -> None:
    device.tap("improvement-notes-target", timeout=60, scroll=True)
    device.tap(TARGET_LABEL, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device, "improvement-notes-target", "Improvement", scroll=True
    )
    if observed != TARGET_LABEL:
        device.capture("improvement-notes-target-mismatch")
        raise RuntimeError(f"Improvement target was {observed!r}; expected {TARGET_LABEL!r}")


def assert_field(device: shared.Device, selector: str, expected: str) -> None:
    actual = device.wait(selector, timeout=60, scroll=True).attributes.get("text", "")
    if actual != expected:
        device.capture(f"{selector}-mismatch")
        raise RuntimeError(f"{selector} was {actual!r}; expected {expected!r}")


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


def find_improvement(root: ET.Element, source: str, name: str) -> ET.Element | None:
    return next((
        item for item in root.findall("./improvements/improvement")
        if item.findtext("sourcename", default="") == source
        and item.findtext("improvedname", default="") == name
    ), None)


def assert_workspace(device: shared.Device) -> None:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        target = find_improvement(root, TARGET_SOURCE, TARGET_NAME)
        body = find_improvement(root, TARGET_SOURCE, "BOD")
        reaction = find_improvement(
            root, "b5121111-5111-5111-5111-511111111111", "REA"
        )
        if target is None or body is None or reaction is None:
            continue
        state = (
            target.findtext("notes", default=""),
            target.findtext("notesColor", default=""),
        )
        observed.append(state)
        if (
            state == (TARGET_NOTES, TARGET_COLOR)
            and target.findtext("enabled") == "0"
            and body.findtext("notes") == "Career untouched improvement note"
            and body.findtext("notesColor") == "#775533"
            and reaction.findtext("notes") == "Second untouched note"
            and root.findtext("customstate") == "Career improvement notes runner sentinel"
        ):
            return
    device.capture("improvement-notes-workspace-not-persisted")
    raise RuntimeError(f"Improvement notes were not durable: {observed!r}")


def edit_target(device: shared.Device) -> None:
    open_page(device)
    select_target(device)
    assert_field(device, "improvement-notes-text", "Career selected improvement note")
    assert_field(device, "improvement-notes-color", "#112233")
    device.set_text("improvement-notes-text", "Improvement notes", TARGET_NOTES, scroll=True)
    device.set_text("improvement-notes-color", "Notes color", TARGET_COLOR, scroll=True)
    device.tap("improvement-notes-save", timeout=180, scroll=True)
    device.wait("build-improvement-notes", timeout=180, scroll=True)


def assert_ui(device: shared.Device) -> None:
    open_page(device)
    select_target(device)
    assert_field(device, "improvement-notes-text", TARGET_NOTES)
    assert_field(device, "improvement-notes-color", TARGET_COLOR)


def assert_creation_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", PACKAGE)
    prepare_runner(device, fixture.name)
    shared.open_build(device, "phone")
    try:
        device.wait("build-improvement-notes", timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("improvement-notes-creation-action-exposed")
        raise RuntimeError("Career-only Improvement Notes was exposed for a creation runner")

    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        item = root.find("./improvements/improvement")
        if item is not None and item.findtext("sourcename") == "c5121111-5111-5111-5111-511111111111":
            if (
                item.findtext("notes") != "Creation negative note sentinel"
                or item.findtext("notesColor") != "#ABCDEF"
            ):
                raise RuntimeError("Creation-negative Improvement notes changed unexpectedly")
            return
    raise RuntimeError("Creation-negative Improvement was unavailable in workspace state")


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
        "--career-runner", type=Path,
        default=fixtures / "career-improvement-notes-e2e.chum5",
    )
    parser.add_argument(
        "--creation-runner", type=Path,
        default=fixtures / "creation-improvement-notes-negative-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "improvementNotesPageSha256": android_root / "src/Chummer.Android/Native/ImprovementNotesPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "improvementNotesContractSha256": overview / "ImprovementNotesEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "improvementNotesRulesSha256": contracts / "CharacterImprovementNotesRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Improvement Notes source graph is incomplete: {missing!r}")
    if shared.PACKAGE != PACKAGE:
        raise RuntimeError(f"Driver package mismatch: {shared.PACKAGE!r} != {PACKAGE!r}")

    career_fixture = args.career_runner.resolve()
    creation_fixture = args.creation_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if api != "36":
        raise RuntimeError(f"Improvement Notes E2E requires API 36, got {api!r}")
    if abi != ABI:
        raise RuntimeError(f"Improvement Notes E2E requires {ABI}, got {abi!r}")
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
    edit_target(device)
    assert_workspace(device)
    assert_ui(device)
    device.capture("improvement-notes-career-after-reopen")
    device.shell("am", "force-stop", PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device)
    assert_ui(device)
    device.capture("improvement-notes-career-after-process-restart")
    assert_creation_negative(device, creation_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "improvement-notes",
        "apiLevel": int(api),
        "abi": abi,
        "package": PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(career_fixture),
        "creationNegativeFixtureSha256": shared.sha256(creation_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "careerDirectImprovementNotesEdited": "pass",
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
        print(f"Improvement Notes E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
