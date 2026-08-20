#!/usr/bin/env python3
"""Prove the five Chummer5 career reputation controls on a real API 36 phone."""

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


CONTROLS: dict[str, tuple[str, str, int]] = {
    "nudStreetCred": ("streetcred", "career-reputation-street-cred", 21),
    "nudNotoriety": ("notoriety", "career-reputation-notoriety", 22),
    "nudPublicAware": ("publicawareness", "career-reputation-public-awareness", 23),
    "nudAstralReputation": ("baseastralreputation", "career-reputation-astral", 24),
    "nudWildReputation": ("basewildreputation", "career-reputation-wild", 25),
}
CONTROL_PROOF_KEYS = ("mutated", "workspacePersisted", "processRestartUiReadback")


def prepare_career_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_reputation(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-reputation",
        scroll=True,
        timeout=60,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.wait("career-reputation", timeout=60)
    device.wait("career-reputation-street-cred", timeout=45)


def assert_core_only_visibility(device: shared.Device) -> None:
    open_reputation(device)
    device.wait("career-reputation-notoriety", timeout=45, scroll=True, max_scrolls=12)
    device.wait("career-reputation-public-awareness", timeout=45, scroll=True, max_scrolls=12)
    shared.reset_scroll_to_top(device, swipes=12)
    if device.find("career-reputation-astral") is not None or device.find("career-reputation-wild") is not None:
        device.capture("career-reputation-core-only-source-visibility-failed")
        raise RuntimeError("Source-specific reputation controls were visible for the core-only settings profile")
    device.capture("career-reputation-core-only-source-visibility")


def select_picker_value(device: shared.Device, selector: str, value: int) -> None:
    device.tap(
        selector,
        scroll=True,
        timeout=60,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(str(value), timeout=60, scroll=True, max_scrolls=8)
    time.sleep(0.5)
    actual = shared.selected_text(device, selector, selector, scroll=True)
    if actual != str(value):
        device.capture(f"{selector}-value-not-selected")
        raise RuntimeError(f"{selector} expected {value}, got {actual!r}")


def edit_all_reputation(device: shared.Device) -> None:
    open_reputation(device)
    shared.reset_scroll_to_top(device, swipes=12)
    for _control, (_element, selector, value) in CONTROLS.items():
        select_picker_value(device, selector, value)
    device.tap(
        "career-reputation-save",
        scroll=True,
        timeout=240,
        max_scrolls=28,
        scroll_distance_ratio=0.22,
    )
    device.wait(
        "build-career-reputation",
        timeout=120,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )


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


def assert_workspace_values(device: shared.Device) -> None:
    expected = {element: str(value) for element, _selector, value in CONTROLS.values()}
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        values = {element: character.findtext(element, default="") for element in expected}
        observed.append(values)
        if values == expected:
            return
    device.capture("career-reputation-workspace-not-persisted")
    raise RuntimeError(f"Career reputation was not durable in workspace XML; observed {observed!r}")


def assert_ui_values(device: shared.Device) -> None:
    shared.reset_scroll_to_top(device, swipes=12)
    for _element, selector, value in CONTROLS.values():
        actual = shared.selected_text(device, selector, selector, scroll=True)
        if actual != str(value):
            device.capture(f"{selector}-readback-mismatch")
            raise RuntimeError(f"{selector} expected {value}, got {actual!r}")


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
        "--core-only-runner",
        type=Path,
        default=fixtures / "career-reputation-core-only-e2e.chum5",
    )
    parser.add_argument(
        "--full-runner",
        type=Path,
        default=fixtures / "career-reputation-full-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "reputationPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "CareerReputationPage.cs",
        "buildPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "reputationContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CareerReputationEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "progressContractSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "sectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "sourceContractSha256": workspace_root / "chummer-core-engine" / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Career-reputation E2E source graph is incomplete: {missing!r}")

    core_fixture = args.core_only_runner.resolve()
    full_fixture = args.full_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career-reputation E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(core_fixture, f"/sdcard/Download/{core_fixture.name}")
    device.push(full_fixture, f"/sdcard/Download/{full_fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_career_runner(device, core_fixture.name)
    assert_core_only_visibility(device)

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_career_runner(device, full_fixture.name)
    edit_all_reputation(device)
    assert_workspace_values(device)
    open_reputation(device)
    assert_ui_values(device)
    device.capture("career-reputation-after-reopen")
    device.back()
    device.back()

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_values(device)
    open_reputation(device)
    assert_ui_values(device)
    device.capture("career-reputation-after-process-restart")

    controls = {
        f"CharacterCareer.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-reputation",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "coreFixtureSha256": shared.sha256(core_fixture),
        "fullFixtureSha256": shared.sha256(full_fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "coreOnlySourceVisibilityEnforced": "pass",
            "fullSourceProfileImported": "pass",
            "allCareerReputationEdited": "pass",
            "careerWorkspaceXmlPersisted": "pass",
            "careerUiReopenReadback": "pass",
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
        print(f"career-reputation E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
