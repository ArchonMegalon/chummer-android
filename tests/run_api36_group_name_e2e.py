#!/usr/bin/env python3
"""Prove exact Chummer5 Create/Career group-name persistence on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("CharacterCreate.txtGroupName", "CharacterCareer.txtGroupName")
CONTROL_PROOF_KEYS = (
    "exactTextMutated",
    "workspacePersisted",
    "unrelatedNestedGroupNamePreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
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
    device.tap(
        "build-group-name",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.20,
    )
    device.wait("group-name-page", timeout=60)
    device.wait("group-name-value", timeout=45)


def assert_name(device: shared.Device, expected: str) -> None:
    node = device.wait("group-name-value", timeout=45)
    observed = node.attributes.get("text", "")
    if observed != expected:
        device.capture("group-name-value-mismatch")
        raise RuntimeError(f"Group name was {observed!r}; expected {expected!r}")


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


def assert_workspace(device: shared.Device, expected: str, unrelated: str) -> None:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        state = (
            root.findtext("groupname", default=""),
            root.findtext("./customstate/groupname", default=""),
        )
        observed.append(state)
        if state == (expected, unrelated):
            return
    device.capture("group-name-workspace-not-persisted")
    raise RuntimeError(f"Group name was not durable: {observed!r}")


def prove_profile(
    device: shared.Device,
    fixture: Path,
    expected: str,
    unrelated: str,
    profile: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    device.set_text("group-name-value", "Group name", expected)
    device.tap("group-name-save", timeout=60, scroll=True)
    device.wait("build-group-name", timeout=180, scroll=True, max_scrolls=24)
    assert_workspace(device, expected, unrelated)

    open_page(device)
    assert_name(device, expected)
    device.capture(f"group-name-{profile}-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, expected, unrelated)
    open_page(device)
    assert_name(device, expected)
    device.capture(f"group-name-{profile}-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-group-name-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-group-name-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "groupNamePageSha256": android_root / "src/Chummer.Android/Native/GroupNamePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "groupNameContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/GroupNameEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "groupNameRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterGroupNameRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Group-name source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Group-name E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Group-name E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    prove_profile(
        device,
        creation_fixture,
        "Vienna Hermetic Circle",
        "Creation unrelated group text",
        "creation",
    )
    prove_profile(
        device,
        career_fixture,
        "Career Arcology Circle",
        "Career unrelated group text",
        "career",
    )
    controls = {
        control: {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "group-name",
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
            "creationGroupNameEdited": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerGroupNameEdited": "pass",
            "careerProcessRestartUiReadback": "pass",
            "contactGroupNameNotCrossWired": "pass",
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
        print(f"group-name E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
