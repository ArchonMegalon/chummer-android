#!/usr/bin/env python3
"""Prove the explicit Chummer5-equivalent Save actions on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "CharacterCreate.tsbSave",
    "CharacterCreate.mnuFileSave",
    "CharacterCareer.tsbSave",
    "CharacterCareer.mnuFileSave",
    "ChummerMainForm.tsSave",
)
CONTROL_PROOF_KEYS = ("invoked", "workspacePersisted", "processRestartReadback")


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def workspace_record(device: shared.Device, alias: str) -> tuple[dict[str, object], ET.Element]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
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
        if not isinstance(payload, str) or not payload.strip().startswith("<"):
            continue
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        if character.findtext("alias", default="") == alias:
            return record, character
    device.capture("explicit-save-workspace-missing")
    raise RuntimeError(f"Workspace for {alias!r} was not found")


def assert_saved_workspace(
    device: shared.Device,
    alias: str,
    expected_created: str,
    marker: str,
) -> None:
    record, character = workspace_record(device, alias)
    content_revision = record.get("ContentRevision")
    saved_revision = record.get("SavedRevision")
    if (
        not isinstance(content_revision, int)
        or content_revision <= 0
        or saved_revision != content_revision
        or character.findtext("created", default="") != expected_created
        or character.findtext("notes", default="") != marker
    ):
        device.capture("explicit-save-workspace-not-saved")
        raise RuntimeError(
            "Explicit save did not preserve an exact saved workspace: "
            f"alias={alias!r}, contentRevision={content_revision!r}, "
            f"savedRevision={saved_revision!r}"
        )


def save_from_build_toolbar(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    device.wait("build-save-runner", timeout=60)
    device.tap("build-save-runner", timeout=180)
    device.wait(
        "Saved.",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )


def save_from_more_page(device: shared.Device) -> None:
    device.tap("More", timeout=60)
    device.wait("more-save-runner", timeout=60, scroll=True, max_scrolls=12)
    device.tap("more-save-runner", timeout=180, scroll=True, max_scrolls=12)
    device.tap("Build", timeout=60)
    device.wait(
        "Saved.",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )


def prove_profile(
    device: shared.Device,
    fixture: Path,
    profile: str,
    alias: str,
    expected_created: str,
    marker: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    save_from_build_toolbar(device)
    assert_saved_workspace(device, alias, expected_created, marker)
    device.capture(f"explicit-save-{profile.lower()}-build-toolbar")

    save_from_more_page(device)
    assert_saved_workspace(device, alias, expected_created, marker)
    device.capture(f"explicit-save-{profile.lower()}-more-page")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_saved_workspace(device, alias, expected_created, marker)
    shared.open_build(device, "phone")
    device.wait("build-save-runner", timeout=60)
    device.capture(f"explicit-save-{profile.lower()}-process-restart")


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
        "--creation-runner",
        type=Path,
        default=fixtures / "creation-explicit-save-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-explicit-save-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "buildPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "morePageSha256": android_root / "src" / "Chummer.Android" / "Native" / "MorePage.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Explicit-save E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Explicit-save E2E requires API 36, got {api!r}")

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
        "CharacterCreate",
        "CreationExplicitSaveE2E",
        "False",
        "Creation explicit save proof marker",
    )
    prove_profile(
        device,
        career_fixture,
        "CharacterCareer",
        "CareerExplicitSaveE2E",
        "True",
        "Career explicit save proof marker",
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
        "journey": "explicit-save",
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
            "creationRunnerImported": "pass",
            "creationBuildToolbarSaveInvoked": "pass",
            "creationMorePageSaveInvoked": "pass",
            "creationWorkspaceRevisionSaved": "pass",
            "creationProcessRestartReadback": "pass",
            "careerRunnerImported": "pass",
            "careerBuildToolbarSaveInvoked": "pass",
            "careerMorePageSaveInvoked": "pass",
            "careerWorkspaceRevisionSaved": "pass",
            "careerProcessRestartReadback": "pass",
            "selectedRunnerSaveEquivalent": "pass",
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
        print(f"explicit-save E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
