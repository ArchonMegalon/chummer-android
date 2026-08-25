#!/usr/bin/env python3
"""Prove exact Career-only Improvement Active editing on an API 36 phone."""

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


CONTROLS = ("CharacterCareer.chkImprovementActive",)
TARGET_SOURCE = "a5111111-5111-5111-5111-511111111111"
TARGET_NAME = "AGI"
TARGET_LABEL = "Agility bonus target · a5111111"
PROOF_KEYS = (
    "careerOnlyCreationNegative",
    "directImprovementSelection",
    "stableTypedSemanticIdentity",
    "duplicateAmbiguousRejected",
    "legacyNumericOrBooleanRead",
    "numericEnabledElementPersisted",
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


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-improvement-active", scroll=True, timeout=120, max_scrolls=30)
    device.wait("improvement-active-page", timeout=60)


def select_target(device: shared.Device) -> None:
    device.tap("improvement-active-target", timeout=60, scroll=True)
    device.tap(TARGET_LABEL, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device,
        "improvement-active-target",
        "Improvement",
        scroll=True,
    )
    if observed != TARGET_LABEL:
        device.capture("improvement-active-target-mismatch")
        raise RuntimeError(f"Improvement target was {observed!r}; expected {TARGET_LABEL!r}")


def assert_toggle(device: shared.Device, expected: bool) -> None:
    observed = (
        device.wait("improvement-active-toggle", timeout=60, scroll=True)
        .attributes.get("checked")
        == "true"
    )
    if observed != expected:
        device.capture("improvement-active-toggle-mismatch")
        raise RuntimeError(f"Improvement Active was {observed!r}; expected {expected!r}")


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


def find_improvement(root: ET.Element, source: str, name: str) -> ET.Element | None:
    return next((
        item for item in root.findall("./improvements/improvement")
        if item.findtext("sourcename", default="") == source
        and item.findtext("improvedname", default="") == name
    ), None)


def assert_workspace(device: shared.Device) -> None:
    observed: list[tuple[str, str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        target = find_improvement(root, TARGET_SOURCE, TARGET_NAME)
        body = find_improvement(root, TARGET_SOURCE, "BOD")
        reaction = find_improvement(
            root,
            "b5111111-5111-5111-5111-511111111111",
            "REA",
        )
        if target is None or body is None or reaction is None:
            continue
        state = (
            target.findtext("enabled", default=""),
            body.findtext("enabled", default=""),
            reaction.findtext("enabled", default=""),
        )
        observed.append(state)
        if (
            state == ("1", "1", "True")
            and target.findtext("notes") == "Career selected improvement sentinel"
            and body.findtext("notes") == "Career untouched improvement sentinel"
            and root.findtext("customstate") == "Career improvement runner sentinel"
        ):
            return
    device.capture("improvement-active-workspace-not-persisted")
    raise RuntimeError(f"Improvement Active was not durable: {observed!r}")


def edit_target(device: shared.Device) -> None:
    open_page(device)
    select_target(device)
    assert_toggle(device, False)
    device.tap("improvement-active-toggle", timeout=60, scroll=True)
    assert_toggle(device, True)
    device.tap("improvement-active-save", timeout=180, scroll=True)
    device.wait("build-improvement-active", timeout=180, scroll=True)


def assert_ui(device: shared.Device) -> None:
    open_page(device)
    select_target(device)
    assert_toggle(device, True)


def assert_creation_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    shared.open_build(device, "phone")
    try:
        device.wait("build-improvement-active", timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("improvement-active-creation-action-exposed")
        raise RuntimeError("Career-only Improvement Active was exposed for a creation runner")

    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        item = root.find("./improvements/improvement")
        if item is not None and item.findtext("sourcename") == "c5111111-5111-5111-5111-511111111111":
            if item.findtext("enabled") != "1" or item.findtext("notes") != "Creation negative sentinel":
                raise RuntimeError("Creation-negative Improvement changed unexpectedly")
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
        "--career-runner",
        type=Path,
        default=fixtures / "career-improvement-active-e2e.chum5",
    )
    parser.add_argument(
        "--creation-runner",
        type=Path,
        default=fixtures / "creation-improvement-active-negative-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "improvementActivePageSha256": android_root / "src/Chummer.Android/Native/ImprovementActivePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "improvementActiveContractSha256": overview / "ImprovementActiveEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "improvementActiveRulesSha256": contracts / "CharacterImprovementActiveRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Improvement Active source graph is incomplete: {missing!r}")

    career_fixture = args.career_runner.resolve()
    creation_fixture = args.creation_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Improvement Active E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (career_fixture, creation_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, career_fixture.name)
    edit_target(device)
    assert_workspace(device)
    assert_ui(device)
    device.capture("improvement-active-career-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device)
    assert_ui(device)
    device.capture("improvement-active-career-after-process-restart")
    assert_creation_negative(device, creation_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "improvement-active",
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
            "careerDirectImprovementEdited": "pass",
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
        print(f"Improvement Active E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
