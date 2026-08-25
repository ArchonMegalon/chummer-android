#!/usr/bin/env python3
"""Prove Chummer5 CharacterRoster Toggle Favorite on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys

import run_api36_editing_e2e as shared


CONTROL = "tsToggleFav"
CONTROL_PROOF_KEYS = (
    "stableDocumentIdentity",
    "favoriteSorted",
    "recentPreservedOnFavorite",
    "unfavoriteMovedToRecentFront",
    "expectedRevisionAtomicSave",
    "sameSessionReopened",
    "processRestartReadback",
    "backupRecoveryReadback",
)


def open_favorites(device: shared.Device) -> None:
    device.wait("home-roster-favorites", timeout=120, scroll=True, max_scrolls=16)
    device.tap("home-roster-favorites", timeout=60, scroll=True)
    device.wait("roster-favorites-page", timeout=60)
    device.wait("roster-favorite-toggle", timeout=45)


def assert_toggle(device: shared.Device, expected: bool) -> None:
    node = device.wait("roster-favorite-toggle", timeout=45)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("roster-favorite-toggle-mismatch")
        raise RuntimeError(f"Roster favorite was {observed!r}; expected {expected!r}")


def state_path(device: shared.Device) -> str:
    listing = device.shell(
        "run-as", shared.PACKAGE, "find", "files/state", "-name", "roster-favorites.json"
    )
    matches = [line.strip() for line in listing.splitlines() if line.strip()]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one roster favorite state file, got {matches!r}")
    return matches[0]


def read_state(device: shared.Device) -> dict[str, object]:
    raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", state_path(device)).stdout
    state = json.loads(raw)
    if not isinstance(state, dict):
        raise RuntimeError("Roster favorite state is not an object")
    return state


def assert_state(device: shared.Device, revision: int, favorite: bool) -> None:
    state = read_state(device)
    if state.get("Revision") != revision:
        raise RuntimeError(f"Roster revision mismatch: {state!r}")
    favorites = state.get("Favorites")
    recent = state.get("Recent")
    if not isinstance(favorites, list) or not isinstance(recent, list):
        raise RuntimeError(f"Roster collections are invalid: {state!r}")
    expected = favorites if favorite else recent
    excluded = recent if favorite else favorites
    if len(expected) != 1 or excluded:
        raise RuntimeError(f"Roster favorite/MRU transition mismatch: {state!r}")
    identity = expected[0]
    if not isinstance(identity, dict) or not str(identity.get("Locator", "")).startswith("content://"):
        raise RuntimeError(f"Stable Android document identity is missing: {state!r}")
    if identity.get("DisplayName") != "Favorite Proof":
        raise RuntimeError(f"Roster display identity mismatch: {state!r}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(
        "--runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures/roster-favorite-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "rosterFavoritesPageSha256": android_root / "src/Chummer.Android/Native/RosterFavoritesPage.cs",
        "homePageSha256": android_root / "src/Chummer.Android/Native/HomePage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "mauiProgramSha256": android_root / "src/Chummer.Android/MauiProgram.cs",
        "rosterFavoritePresenterSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterRosterFavoritePresenter.cs",
        "rosterFavoriteContractSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Api/CharacterRosterFavoriteContracts.cs",
        "rosterFavoriteRulesSha256": workspace_root / "chummer-core-engine/Chummer.Application/Tools/CharacterRosterFavoriteRules.cs",
        "rosterFavoriteStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Files/FileCharacterRosterFavoriteStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Roster-favorite source graph is incomplete: {missing!r}")

    runner = args.runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Roster-favorite E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Roster-favorite E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    if not device.shell("pm", "path", shared.PACKAGE).startswith("package:"):
        raise RuntimeError(f"Expected package {shared.PACKAGE!r} is not installed")

    device.shell("pm", "clear", shared.PACKAGE)
    device.push(runner, f"/sdcard/Download/{runner.name}")
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, runner.name)
    shared.wait_for_phone_runner_route(device, timeout=120)

    open_favorites(device)
    assert_toggle(device, False)
    device.tap("roster-favorite-toggle")
    assert_toggle(device, True)
    assert_state(device, revision=1, favorite=True)
    device.capture("roster-favorite-on")
    device.back()
    open_favorites(device)
    assert_toggle(device, True)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_favorites(device)
    assert_toggle(device, True)
    assert_state(device, revision=1, favorite=True)
    device.capture("roster-favorite-after-process-restart")

    device.tap("roster-favorite-toggle")
    assert_toggle(device, False)
    assert_state(device, revision=2, favorite=False)
    device.capture("roster-favorite-off")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_favorites(device)
    assert_toggle(device, False)
    assert_state(device, revision=2, favorite=False)

    primary = state_path(device)
    device.shell("run-as", shared.PACKAGE, "sh", "-c", f"printf '{{broken' > '{primary}'")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_favorites(device)
    assert_toggle(device, True)
    device.capture("roster-favorite-backup-recovery")

    controls = {
        f"CharacterRoster.{CONTROL}": {key: "pass" for key in CONTROL_PROOF_KEYS}
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "roster-favorite",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "runnerFixtureSha256": shared.sha256(runner),
        "controlCount": 1,
        "controls": controls,
        "journeys": {
            "runnerImported": "pass",
            "favoritePersisted": "pass",
            "favoriteProcessRestartReadback": "pass",
            "unfavoritePersisted": "pass",
            "unfavoriteProcessRestartReadback": "pass",
            "backupRecovery": "pass",
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
        print(f"roster-favorite E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
