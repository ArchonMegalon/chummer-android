#!/usr/bin/env python3
"""Digest-bound API 36 arm64 phone proof for Create/Career Vehicle Weapon firing mode."""
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
import run_api36_gear_attack_swap_e2e as workspace_shared


CONTROLS = (
    "CharacterCreate.cboVehicleWeaponFiringMode",
    "CharacterCareer.cboVehicleWeaponFiringMode",
)
PROOF_KEYS = (
    "exactLegacyHandlerAndRefreshGuards",
    "typedDirectVehicleWeaponGuidIdentity",
    "fiveModeAllowlist",
    "rangeAndAmmoVisibilityRules",
    "zeroNuyenKarmaEconomics",
    "descendantTargetsFailClosedCoverage",
    "revisionBoundAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
JOURNEYS = (
    dict(
        phase="creation",
        fixture="creation-vehicle-weapon-firing-mode-e2e.chum5",
        vehicle="93333333-3333-4333-8333-333333333333",
        weapon="94444444-4444-4444-8444-444444444444",
        descendant="96666666-6666-4666-8666-666666666666",
        hidden="95555555-5555-4555-8555-555555555555",
        choice="Remote Operated",
        expected="RemoteOperated",
        original="DogBrain",
        nuyen="4321",
        karma="7",
    ),
    dict(
        phase="career",
        fixture="career-vehicle-weapon-firing-mode-e2e.chum5",
        vehicle="97777777-7777-4777-8777-777777777777",
        weapon="98888888-8888-4888-8888-888888888888",
        descendant="9aaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        hidden="99999999-9999-4999-8999-999999999999",
        choice="Gunnery Command Device",
        expected="GunneryCommandDevice",
        original="ManualOperation",
        nuyen="8765",
        karma="19",
    ),
)


def prepare(device: shared.Device, fixture: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture)
    shared.wait_for_phone_runner_route(device, timeout=120)


def token(journey: dict[str, str]) -> str:
    return f"{journey['vehicle'].replace('-', '')}-{journey['weapon'].replace('-', '')}"


def open_page(device: shared.Device, journey: dict[str, str]) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-vehicles", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-vehicle-{journey['vehicle']}", scroll=True, timeout=120, max_scrolls=24)
    vehicle_token = journey["vehicle"].replace("-", "")
    device.tap(
        f"vehicle-weapon-firing-mode-list-open-{vehicle_token}",
        scroll=True,
        timeout=120,
        max_scrolls=36,
    )
    device.wait(f"vehicle-weapon-firing-mode-list-{vehicle_token}", timeout=60)
    device.tap(f"vehicle-weapon-firing-mode-open-{token(journey)}", scroll=True, timeout=120)
    device.wait(f"vehicle-weapon-firing-mode-page-{token(journey)}", timeout=60)


def select_and_save(device: shared.Device, journey: dict[str, str]) -> None:
    device.tap(f"vehicle-weapon-firing-mode-picker-{token(journey)}", scroll=True, timeout=60)
    device.tap(journey["choice"], timeout=60)
    time.sleep(0.25)
    device.tap(
        f"vehicle-weapon-firing-mode-save-{token(journey)}",
        scroll=True,
        timeout=180,
        max_scrolls=24,
    )


def direct_weapon(vehicle: ET.Element, weapon_id: str) -> ET.Element | None:
    return next(
        (
            node
            for node in vehicle.findall("./weapons/weapon")
            if node.findtext("guid", "").lower() == weapon_id
        ),
        None,
    )


def assert_workspace(device: shared.Device, journey: dict[str, str]) -> None:
    for payload in workspace_shared.workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        vehicle = next(
            (
                node
                for node in root.findall("./vehicles/vehicle")
                if node.findtext("guid", "").lower() == journey["vehicle"]
            ),
            None,
        )
        if vehicle is None:
            continue
        weapon = direct_weapon(vehicle, journey["weapon"])
        hidden = direct_weapon(vehicle, journey["hidden"])
        descendant = next(
            (
                node
                for node in vehicle.findall("./weapons/weapon/underbarrel/weapon")
                if node.findtext("guid", "").lower() == journey["descendant"]
            ),
            None,
        )
        if (
            weapon is not None
            and weapon.findtext("firingmode") == journey["expected"]
            and weapon.findtext("type") == "Ranged"
            and weapon.findtext("ammo") in {"100(belt)", "20(c)"}
            and weapon.findtext("cost") in {"12345", "54321"}
            and weapon.findtext("notes")
            in {"creation root weapon sentinel", "career root weapon sentinel"}
            and hidden is not None
            and hidden.findtext("firingmode") == "Skill"
            and hidden.findtext("type") == "Melee"
            and hidden.findtext("ammo") == "0"
            and descendant is not None
            and descendant.findtext("firingmode") == "Skill"
            and vehicle.findtext("cost") in {"50000", "65000"}
            and root.findtext("nuyen") == journey["nuyen"]
            and root.findtext("karma") == journey["karma"]
        ):
            return
    raise RuntimeError(f"Vehicle Weapon firing-mode {journey['phase']} edit was not durable/preserving")


def run_journey(device: shared.Device, journey: dict[str, str]) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare(device, journey["fixture"])
    open_page(device, journey)
    select_and_save(device, journey)
    assert_workspace(device, journey)
    open_page(device, journey)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, journey)
    open_page(device, journey)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("adb", "apk", "evidence", "receipt", "workspace-root"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    args = parser.parse_args()
    driver = Path(__file__).resolve()
    android = driver.parents[1]
    workspace = args.workspace_root.resolve()
    sources = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "workspaceSharedDriverSha256": Path(workspace_shared.__file__).resolve(),
        "vehicleWeaponFiringModePageSha256": android / "src/Chummer.Android/Native/VehicleWeaponFiringModePage.cs",
        "collectionRouteSha256": android / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "vehicleWeaponFiringModeRequestSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/VehicleWeaponFiringModeEditRequest.cs",
        "mutationCatalogSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "vehicleWeaponFiringModeRulesSha256": workspace / "chummer-core-engine/Chummer.Contracts/Characters/CharacterVehicleWeaponFiringModeRules.cs",
        "presenterPersistenceSha256": workspace / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "workspaceStoreSha256": workspace / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "creationFixtureSha256": android / "tests/fixtures/creation-vehicle-weapon-firing-mode-e2e.chum5",
        "careerFixtureSha256": android / "tests/fixtures/career-vehicle-weapon-firing-mode-e2e.chum5",
    }
    if any(not path.is_file() for path in sources.values()):
        raise RuntimeError("Vehicle Weapon firing-mode source graph incomplete")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abilist")
    if api != "36" or "arm64-v8a" not in abi.split(","):
        raise RuntimeError("Vehicle Weapon firing-mode proof requires API 36 arm64-v8a")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    fixtures = driver.parent / "fixtures"
    for name in {journey["fixture"] for journey in JOURNEYS}:
        device.push(fixtures / name, f"/sdcard/Download/{name}")
    for journey in JOURNEYS:
        run_journey(device, journey)
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "vehicle-weapon-firing-mode",
        "apiLevel": 36,
        "abi": "arm64-v8a",
        "package": shared.PACKAGE,
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in sources.items()},
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Vehicle Weapon firing-mode E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
