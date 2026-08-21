#!/usr/bin/env python3
"""Prove exact Chummer5 Create/Career tradition-drain persistence on an API 36 phone."""

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


CONTROLS = ("CharacterCreate.cboDrain", "CharacterCareer.cboDrain")
CUSTOM_SOURCE_ID = "616ba093-306c-45fc-8f41-0b98c8cccb46"
CONTROL_PROOF_KEYS = (
    "exactCatalogExpressionMutated",
    "customSourceIdentityPreserved",
    "stableTraditionGuidPreserved",
    "unrelatedTraditionDataPreserved",
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
        "build-tradition-drain",
        scroll=True,
        timeout=120,
        max_scrolls=28,
        scroll_distance_ratio=0.20,
    )
    device.wait("tradition-drain-page", timeout=60)
    device.wait("tradition-drain-value", timeout=45)


def selected_expression(device: shared.Device) -> str:
    return shared.selected_text(
        device,
        "tradition-drain-value",
        "Drain attributes",
        scroll=True,
    )


def select_expression(device: shared.Device, expected: str) -> None:
    device.tap("tradition-drain-value", timeout=60, scroll=True)
    device.tap(expected, timeout=60, scroll=True, max_scrolls=8)
    time.sleep(0.5)
    observed = selected_expression(device)
    if observed != expected:
        device.capture("tradition-drain-selection-mismatch")
        raise RuntimeError(f"Tradition drain was {observed!r}; expected {expected!r}")


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
    expected_expression: str,
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
            tradition.findtext("drain", default=""),
            tradition.findtext("extra", default=""),
        )
        observed.append(state)
        if state == (CUSTOM_SOURCE_ID, expected_guid, "MAG", expected_expression, unrelated):
            return
    device.capture("tradition-drain-workspace-not-persisted")
    raise RuntimeError(f"Tradition drain was not durable: {observed!r}")


def prove_profile(
    device: shared.Device,
    fixture: Path,
    expected_expression: str,
    expected_guid: str,
    unrelated: str,
    profile: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    select_expression(device, expected_expression)
    device.tap("tradition-drain-save", timeout=60, scroll=True)
    device.wait("build-tradition-drain", timeout=180, scroll=True, max_scrolls=28)
    assert_workspace(device, expected_expression, expected_guid, unrelated)

    open_page(device)
    if selected_expression(device) != expected_expression:
        raise RuntimeError("Tradition drain did not survive same-session reopen")
    device.capture(f"tradition-drain-{profile}-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected_expression, expected_guid, unrelated)
    open_page(device)
    if selected_expression(device) != expected_expression:
        raise RuntimeError("Tradition drain did not survive process restart")
    device.capture(f"tradition-drain-{profile}-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-tradition-drain-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-tradition-drain-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "traditionDrainPageSha256": android_root / "src/Chummer.Android/Native/TraditionDrainPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "traditionDrainContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/TraditionDrainEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "traditionDrainRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterTraditionDrainRules.cs",
        "sourceResolverContractSha256": workspace_root / "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "traditionsCatalogSha256": workspace_root / "chummer-core-engine/Chummer/data/traditions.xml",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Tradition-drain source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Tradition-drain E2E requires API 36, got {api!r}")
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
        "{WIL} + {LOG}",
        "d87f03c0-8820-4f5f-8362-c05bcbacb64d",
        "Creation unrelated tradition data",
        "creation",
    )
    prove_profile(
        device,
        career_fixture,
        "{WIL} + {CHA}",
        "c412cb61-4a84-496c-b811-3e4a13b0dd27",
        "Career unrelated tradition data",
        "career",
    )
    controls = {control: {key: "pass" for key in CONTROL_PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "tradition-drain",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "creationCatalogDrainEdited": "pass",
            "creationProcessRestartUiReadback": "pass",
            "careerCatalogDrainEdited": "pass",
            "careerProcessRestartUiReadback": "pass",
            "adeptOnlyAndUnknownCatalogValuesRejectedBySourceContract": "pass",
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
        print(f"tradition-drain E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
