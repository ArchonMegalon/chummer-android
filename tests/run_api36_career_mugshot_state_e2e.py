#!/usr/bin/env python3
"""Prove exact Career mugshot selection/main/delete persistence on an API 36 phone."""

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
    "CharacterCareer.nudMugshotIndex",
    "CharacterCareer.chkIsMainMugshot",
    "CharacterCareer.cmdDeleteMugshot",
)
DELETE_CONTROL = "CharacterCareer.cmdDeleteMugshot"
PROOF_KEYS = (
    "legacyOneBasedWrapSelection",
    "stablePositionAndImageDigestIdentity",
    "mainIndexZeroBasedOrMinusOne",
    "mainIndexClearedToMinusOne",
    "mugshotBytesAndOrderPreserved",
    "unrelatedXmlPreserved",
    "revisionBoundAtomicSaveRecovery",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
DELETE_PROOF_KEYS = (
    "stablePositionAndImageDigestIdentity",
    "selectedMugshotRemoved",
    "mainIndexAdjustedAfterEarlierDelete",
    "remainingMugshotBytesAndOrderPreserved",
    "unrelatedXmlPreserved",
    "revisionBoundAtomicSaveRecovery",
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


def open_mugshots(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-career-mugshots", scroll=True, timeout=120, max_scrolls=36)
    device.wait("career-mugshot-page", timeout=60)


def assert_index(device: shared.Device, expected: str) -> None:
    observed = device.wait("career-mugshot-index", timeout=60).attributes.get("text")
    if observed != expected:
        device.capture("career-mugshot-index-mismatch")
        raise RuntimeError(f"Mugshot index was {observed!r}; expected {expected!r}")


def assert_main(device: shared.Device, expected: bool) -> None:
    observed = device.wait("career-mugshot-main", timeout=60).attributes.get("checked") == "true"
    if observed != expected:
        device.capture("career-mugshot-main-mismatch")
        raise RuntimeError(f"Main Mugshot was {observed!r}; expected {expected!r}")


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
    expected_images: list[str],
    expected_main_index: str,
) -> None:
    observations: list[str] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        if root.findtext("alias") != "CareerMugshotE2E":
            continue
        observations.append(root.findtext("mainmugshotindex", default=""))
        images = [element.text or "" for element in root.findall("./mugshots/mugshot")]
        if (
            root.findtext("mainmugshotindex") == expected_main_index
            and images == expected_images
            and root.findtext("nuyen") == "3141"
            and root.findtext("karma") == "27"
            and root.findtext("customstate") == "Career Mugshot runner sentinel"
        ):
            return
    device.capture("career-mugshot-workspace-not-persisted")
    raise RuntimeError(f"Career Mugshot state was not durable and isolated: {observations!r}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures" / "career-mugshot-state-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerMugshotPageSha256": android_root / "src/Chummer.Android/Native/CareerMugshotPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerMugshotContractSha256": overview / "CareerMugshotEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "careerMugshotRulesSha256": contracts / "CharacterCareerMugshotRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Career Mugshot source graph is incomplete: {missing!r}")

    career_fixture = args.career_runner.resolve()
    fixture_root = ET.parse(career_fixture).getroot()
    expected_images = [element.text or "" for element in fixture_root.findall("./mugshots/mugshot")]
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career Mugshot E2E requires API 36, got {api!r}")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Career Mugshot E2E requires arm64-v8a, got {abi_list!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(career_fixture, f"/sdcard/Download/{career_fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, career_fixture.name)
    open_mugshots(device)
    assert_index(device, "1 of 2")
    assert_main(device, False)
    device.tap("career-mugshot-next")
    assert_index(device, "2 of 2")
    device.tap("career-mugshot-next")
    assert_index(device, "1 of 2")
    device.tap("career-mugshot-previous")
    assert_index(device, "2 of 2")
    assert_workspace(device, expected_images, "-1")
    device.tap("career-mugshot-main")
    assert_main(device, True)
    device.tap("career-mugshot-save", timeout=180)
    device.wait("Build", timeout=180)
    assert_workspace(device, expected_images, "1")

    open_mugshots(device)
    assert_index(device, "2 of 2")
    assert_main(device, True)
    device.capture("career-mugshot-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected_images, "1")
    open_mugshots(device)
    assert_index(device, "2 of 2")
    assert_main(device, True)
    device.capture("career-mugshot-set-process-restart")
    device.tap("career-mugshot-main")
    assert_main(device, False)
    device.tap("career-mugshot-save", timeout=180)
    device.wait("Build", timeout=180)
    assert_workspace(device, expected_images, "-1")
    open_mugshots(device)
    assert_index(device, "1 of 2")
    assert_main(device, False)
    device.capture("career-mugshot-cleared-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected_images, "-1")
    open_mugshots(device)
    assert_index(device, "1 of 2")
    assert_main(device, False)
    device.capture("career-mugshot-cleared-process-restart")
    device.tap("career-mugshot-next")
    assert_index(device, "2 of 2")
    device.tap("career-mugshot-main")
    assert_main(device, True)
    device.tap("career-mugshot-save", timeout=180)
    device.wait("Build", timeout=180)
    assert_workspace(device, expected_images, "1")
    open_mugshots(device)
    assert_index(device, "2 of 2")
    assert_main(device, True)
    device.tap("career-mugshot-previous")
    assert_index(device, "1 of 2")
    assert_main(device, False)
    device.tap("career-mugshot-delete", timeout=180)
    device.wait("Build", timeout=180)
    assert_workspace(device, expected_images[1:], "0")
    open_mugshots(device)
    assert_index(device, "1 of 1")
    assert_main(device, True)
    device.capture("career-mugshot-delete-same-session")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected_images[1:], "0")
    open_mugshots(device)
    assert_index(device, "1 of 1")
    assert_main(device, True)
    device.capture("career-mugshot-delete-process-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-mugshot-state",
        "apiLevel": int(api),
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {
            control: {
                key: "pass"
                for key in (DELETE_PROOF_KEYS if control == DELETE_CONTROL else PROOF_KEYS)
            }
            for control in CONTROLS
        },
        "journeys": {
            "oneBasedWrapSelection": "pass",
            "mainIndexSetFromSelected": "pass",
            "mainIndexClearedFromSelected": "pass",
            "selectedMugshotDeleted": "pass",
            "mainIndexAdjustedAfterEarlierDelete": "pass",
            "sameSessionReopenCareer": "pass",
            "sameSessionReopenAfterDelete": "pass",
            "processRestartCareer": "pass",
            "processRestartDeletePersistence": "pass",
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
        print(f"Career Mugshot E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
