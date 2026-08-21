#!/usr/bin/env python3
"""Prove Chummer5 creation/career top-level weapon-location adds on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "cmdAddWeaponLocation"
CONTROL_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
)
PROFILE_TARGETS = {
    "CharacterCreate": (
        "Creation Added Weapon Location E2E",
        "Creation Existing Weapon Location E2E",
        "Creation existing weapon notes E2E",
    ),
    "CharacterCareer": (
        "Career Added Weapon Location E2E",
        "Career Existing Weapon Location E2E",
        "Career existing weapon notes E2E",
    ),
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_weapon_locations(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-section-tab-gear",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        "build-action-tab-gear-weaponlocations",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.wait(
        "weapon-location-open-add",
        timeout=120,
        scroll=True,
        max_scrolls=24,
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


def assert_workspace_location(
    device: shared.Device,
    expected: str,
    existing: str,
    existing_notes: str,
) -> None:
    observed: list[list[str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        locations = character.findall("./weaponlocations/location")
        names = [location.findtext("name", default="") for location in locations]
        observed.append(names)
        added = [location for location in locations if location.findtext("name", default="") == expected]
        preserved = [location for location in locations if location.findtext("name", default="") == existing]
        if len(added) != 1 or len(preserved) != 1:
            continue
        try:
            uuid.UUID(added[0].findtext("guid", default=""))
        except (ValueError, AttributeError):
            continue
        added_notes = added[0].find("notes")
        if (
            added_notes is not None
            and (added_notes.text or "") == ""
            and preserved[0].findtext("notes", default="") == existing_notes
        ):
            return
    device.capture("weapon-location-workspace-not-persisted")
    raise RuntimeError(
        f"Weapon location {expected!r} was not durably added while preserving {existing!r}; "
        f"observed {observed!r}"
    )


def add_location(device: shared.Device, name: str) -> None:
    open_weapon_locations(device)
    device.tap("weapon-location-open-add", timeout=60, scroll=True)
    device.wait("weapon-location-add-page", timeout=60)
    device.set_text("weapon-location-name", "Location name", name)
    device.tap("weapon-location-add", timeout=240, scroll=True)
    device.wait("weapon-location-open-add", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected, existing, existing_notes = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    add_location(device, expected)
    assert_workspace_location(device, expected, existing, existing_notes)
    open_weapon_locations(device)
    device.assert_text(expected, timeout=30)
    device.capture(f"weapon-location-{profile.lower()}-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_location(device, expected, existing, existing_notes)
    open_weapon_locations(device)
    device.assert_text(expected, timeout=30)
    device.capture(f"weapon-location-{profile.lower()}-after-process-restart")


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
        default=fixtures / "creation-weapon-location-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-weapon-location-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "weaponLocationPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "WeaponLocationAddPage.cs",
        "buildFlowPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "weaponLocationContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WeaponLocationAddRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Weapon-location E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Weapon-location E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    prove_profile(device, creation_fixture, "CharacterCreate")
    prove_profile(device, career_fixture, "CharacterCareer")

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
        "journey": "weapon-location-add",
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
            "creationWeaponLocationAdded": "pass",
            "creationWorkspaceXmlPersisted": "pass",
            "creationSurfaceReopened": "pass",
            "creationProcessRestartPersistence": "pass",
            "careerRunnerImported": "pass",
            "careerWeaponLocationAdded": "pass",
            "careerWorkspaceXmlPersisted": "pass",
            "careerSurfaceReopened": "pass",
            "careerProcessRestartPersistence": "pass",
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
        print(f"weapon-location E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
