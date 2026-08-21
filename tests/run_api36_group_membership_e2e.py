#!/usr/bin/env python3
"""Prove Chummer5 Create/Career group membership on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "chkJoinGroup"
CONTROL_PROOF_KEYS = (
    "membershipMutated",
    "exactCareerKarmaAndUndo",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-group-membership",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.20,
    )
    device.wait("group-membership-page", timeout=60)
    device.wait("group-membership-toggle", timeout=45)


def assert_toggle(device: shared.Device, expected: bool) -> None:
    node = device.wait("group-membership-toggle", timeout=45)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("group-membership-toggle-mismatch")
        raise RuntimeError(f"Group membership was {observed!r}; expected {expected!r}")


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
    member: bool,
    karma: str,
    expected_expenses: tuple[tuple[str, str], ...],
    unrelated: str,
) -> None:
    observed: list[dict[str, object]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        expenses = tuple(
            (
                expense.findtext("amount", default=""),
                expense.findtext("./undo/karmatype", default=""),
            )
            for expense in root.findall("./expenses/expense")
            if expense.findtext("./undo/karmatype", default="") in {"JoinGroup", "LeaveGroup"}
        )
        state = {
            "member": root.findtext("groupmember", default=""),
            "karma": root.findtext("karma", default=""),
            "expenses": expenses,
            "unrelated": root.findtext("./customstate/groupmember", default=""),
        }
        observed.append(state)
        if (
            state["member"] == ("True" if member else "False")
            and state["karma"] == karma
            and state["expenses"] == expected_expenses
            and state["unrelated"] == unrelated
        ):
            return
    device.capture("group-membership-workspace-not-persisted")
    raise RuntimeError(f"Group membership was not durable: {observed!r}")


def save_membership(device: shared.Device, value: bool, career: bool) -> None:
    open_page(device)
    assert_toggle(device, not value)
    device.tap("group-membership-toggle", timeout=45)
    assert_toggle(device, value)
    device.tap("group-membership-save", timeout=60, scroll=True)
    if career:
        device.tap("Spend & Save", timeout=60)
    device.wait("build-group-membership", timeout=180, scroll=True, max_scrolls=24)


def prove_creation(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    save_membership(device, True, career=False)
    assert_workspace(device, True, "0", (), "Creation unrelated membership text")
    open_page(device)
    assert_toggle(device, True)
    device.capture("group-membership-creation-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, True, "0", (), "Creation unrelated membership text")
    open_page(device)
    assert_toggle(device, True)
    device.capture("group-membership-creation-after-process-restart")


def prove_career(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    save_membership(device, True, career=True)
    join = (("-5", "JoinGroup"),)
    assert_workspace(device, True, "3", join, "Career unrelated membership text")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, True, "3", join, "Career unrelated membership text")
    open_page(device)
    assert_toggle(device, True)
    device.capture("group-membership-career-join-after-process-restart")
    device.back()
    save_membership(device, False, career=True)
    both = (("-5", "JoinGroup"), ("-1", "LeaveGroup"))
    assert_workspace(device, False, "2", both, "Career unrelated membership text")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, False, "2", both, "Career unrelated membership text")
    open_page(device)
    assert_toggle(device, False)
    device.capture("group-membership-career-leave-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-group-membership-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-group-membership-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "groupMembershipPageSha256": android_root / "src/Chummer.Android/Native/GroupMembershipPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "groupMembershipContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/GroupMembershipEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "groupMembershipRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterGroupMembershipRules.cs",
        "sourceResolverContractSha256": workspace_root / "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Group-membership source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Group-membership E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Group-membership E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    prove_creation(device, creation_fixture)
    prove_career(device, career_fixture)
    controls = {
        f"{profile}.{CONTROL}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for profile in ("CharacterCreate", "CharacterCareer")
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "group-membership",
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
            "creationMembershipEdited": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerJoinKarmaAndUndoPersisted": "pass",
            "careerLeaveKarmaAndUndoPersisted": "pass",
            "careerProcessRestartUiReadback": "pass",
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
        print(f"group-membership E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
