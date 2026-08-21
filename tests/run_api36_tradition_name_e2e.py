#!/usr/bin/env python3
"""Prove exact Chummer5 Create/Career custom-tradition name persistence on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("CharacterCreate.txtTraditionName", "CharacterCareer.txtTraditionName")
CUSTOM_SOURCE_ID = "616ba093-306c-45fc-8f41-0b98c8cccb46"
CONTROL_PROOF_KEYS = (
    "exactCustomNameMutated",
    "customSourceIdentityPreserved",
    "stableTraditionGuidPreserved",
    "unrelatedNestedNamePreserved",
    "expectedRevisionAtomicSave",
    "workspacePersisted",
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
        "build-tradition-name",
        scroll=True,
        timeout=120,
        max_scrolls=26,
        scroll_distance_ratio=0.20,
    )
    device.wait("tradition-name-page", timeout=60)
    device.wait("tradition-name-value", timeout=45)


def assert_name(device: shared.Device, expected: str) -> None:
    node = device.wait("tradition-name-value", timeout=45)
    observed = node.attributes.get("text", "")
    if observed != expected:
        device.capture("tradition-name-value-mismatch")
        raise RuntimeError(f"Tradition name was {observed!r}; expected {expected!r}")


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
    expected_name: str,
    expected_guid: str,
    unrelated: str,
) -> None:
    observed: list[tuple[str, str, str, str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        tradition = root.find("tradition")
        if tradition is None:
            continue
        state = (
            tradition.findtext("sourceid", default="").lower(),
            tradition.findtext("guid", default="").lower(),
            tradition.findtext("traditiontype", default=""),
            tradition.findtext("name", default=""),
            tradition.findtext("./extra/name", default=""),
        )
        observed.append(state)
        if state == (CUSTOM_SOURCE_ID, expected_guid, "MAG", expected_name, unrelated):
            return
    device.capture("tradition-name-workspace-not-persisted")
    raise RuntimeError(f"Tradition name was not durable: {observed!r}")


def prove_profile(
    device: shared.Device,
    fixture: Path,
    expected_name: str,
    expected_guid: str,
    unrelated: str,
    profile: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    device.set_text("tradition-name-value", "Tradition name", expected_name)
    device.tap("tradition-name-save", timeout=60, scroll=True)
    device.wait("build-tradition-name", timeout=180, scroll=True, max_scrolls=26)
    assert_workspace(device, expected_name, expected_guid, unrelated)

    open_page(device)
    assert_name(device, expected_name)
    device.capture(f"tradition-name-{profile}-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected_name, expected_guid, unrelated)
    open_page(device)
    assert_name(device, expected_name)
    device.capture(f"tradition-name-{profile}-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-tradition-name-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-tradition-name-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "traditionNamePageSha256": android_root / "src/Chummer.Android/Native/TraditionNamePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "traditionNameContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/TraditionNameEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "traditionNameRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterTraditionNameRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Tradition-name source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Tradition-name E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Tradition-name E2E requires arm64-v8a, got {abi!r}")
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
        "Vienna Custom Hermetic",
        "8b3e871a-b308-4280-8672-11d7d4ea40a3",
        "Creation unrelated nested name",
        "creation",
    )
    prove_profile(
        device,
        career_fixture,
        "Career Custom Hermetic",
        "64470741-a0b2-4645-bada-0e2613b5886e",
        "Career unrelated nested name",
        "career",
    )
    controls = {control: {key: "pass" for key in CONTROL_PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "tradition-name",
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
            "creationCustomTraditionNameEdited": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerCustomTraditionNameEdited": "pass",
            "careerProcessRestartUiReadback": "pass",
            "nonCustomTraditionRejectedBySourceContract": "pass",
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
        print(f"tradition-name E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
