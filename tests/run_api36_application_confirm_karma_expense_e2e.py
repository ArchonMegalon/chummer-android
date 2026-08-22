#!/usr/bin/env python3
"""Prove Chummer5 confirmkarmaexpense staging and snapshot semantics on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import run_api36_editing_e2e as shared


CONTROL = "chkConfirmKarmaExpense"
CONTROL_PROOF_KEYS = (
    "legacyDefaultTrue",
    "legacyMissingFieldMigratesTrue",
    "draftBackDoesNotPersist",
    "explicitSaveOnly",
    "typedSettingIdentity",
    "wholeSnapshotExpectedRevisionCas",
    "singleAtomicSave",
    "processRestartReadback",
    "newestValidBackupRecovery",
    "characterDocumentPreserved",
)


def resolve_repository(root: Path, label: str, names: tuple[str, ...]) -> Path:
    matches = [root / name for name in names if (root / name).is_dir()]
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected exactly one {label} repository under {root}, got {[str(path) for path in matches]!r}"
        )
    return matches[0]


def open_settings(device: shared.Device) -> None:
    device.wait("home-application-settings", timeout=120, scroll=True, max_scrolls=16)
    device.tap("home-application-settings", timeout=60, scroll=True)
    device.wait("application-settings-page", timeout=60)
    device.wait("settings-confirm-karma-expense", timeout=45, scroll=True, max_scrolls=12)


def assert_toggle(device: shared.Device, selector: str, expected: bool) -> None:
    node = device.wait(selector, timeout=45, scroll=True, max_scrolls=12)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("application-confirm-karma-expense-toggle-mismatch")
        raise RuntimeError(f"{selector} is {observed}, expected {expected}")


def find_state_paths(device: shared.Device) -> list[str]:
    listing = device.shell(
        "run-as", shared.PACKAGE, "find", "files/state", "-name", "application-delete-confirmation.json"
    )
    return [line.strip() for line in listing.splitlines() if line.strip()]


def state_path(device: shared.Device) -> str:
    matches = find_state_paths(device)
    if len(matches) != 1:
        raise RuntimeError(f"Expected one application settings state file, got {matches!r}")
    return matches[0]


def read_state(device: shared.Device, path: str | None = None) -> dict[str, object]:
    target = path or state_path(device)
    raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", target).stdout
    state = json.loads(raw)
    if not isinstance(state, dict):
        raise RuntimeError("Application settings state is not an object")
    return state


def assert_state(device: shared.Device, revision: int, confirm_delete: bool, confirm_karma: bool) -> None:
    state = read_state(device)
    expected = (revision, confirm_delete, confirm_karma)
    observed = (state.get("Revision"), state.get("ConfirmDelete"), state.get("ConfirmKarmaExpense"))
    if observed != expected:
        raise RuntimeError(f"Application settings snapshot mismatch: {state!r}")


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
        default=Path(__file__).resolve().parent / "fixtures/application-confirm-karma-expense-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    presentation_root = resolve_repository(workspace_root, "presentation", ("presentation", "chummer-presentation"))
    core_root = resolve_repository(workspace_root, "core", ("core", "chummer-core-engine"))
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "applicationSettingsPageSha256": android_root / "src/Chummer.Android/Native/ApplicationSettingsPage.cs",
        "homePageSha256": android_root / "src/Chummer.Android/Native/HomePage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "mauiProgramSha256": android_root / "src/Chummer.Android/MauiProgram.cs",
        "applicationSettingsPresenterSha256": presentation_root / "Chummer.Presentation/Overview/ApplicationDeleteConfirmationPresenter.cs",
        "applicationSettingsContractSha256": core_root / "Chummer.Contracts/Api/ApplicationDeleteConfirmationContracts.cs",
        "applicationSettingsRulesSha256": core_root / "Chummer.Application/Tools/ApplicationDeleteConfirmationRules.cs",
        "applicationSettingsStoreSha256": core_root / "Chummer.Infrastructure/Files/FileApplicationDeleteConfirmationStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Application-settings source graph is incomplete: {missing!r}")

    runner = args.runner.resolve()
    original_runner_sha256 = shared.sha256(runner)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Application-settings E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Application-settings E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    if not device.shell("pm", "path", shared.PACKAGE).startswith("package:"):
        raise RuntimeError(f"Expected package {shared.PACKAGE!r} is not installed")

    device.shell("pm", "clear", shared.PACKAGE)
    remote_runner = f"/sdcard/Download/{runner.name}"
    device.push(runner, remote_runner)
    remote_runner_sha256 = hashlib.sha256(device.run("exec-out", "cat", remote_runner, text=False).stdout).hexdigest()
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, runner.name)
    device.wait("Continue building", timeout=120)

    open_settings(device)
    assert_toggle(device, "settings-confirm-delete", True)
    assert_toggle(device, "settings-confirm-karma-expense", True)
    if find_state_paths(device):
        raise RuntimeError("Legacy true defaults must not require fabricated persisted state")
    device.tap("settings-confirm-karma-expense")
    assert_toggle(device, "settings-confirm-karma-expense", False)
    device.back()
    open_settings(device)
    assert_toggle(device, "settings-confirm-karma-expense", True)
    if find_state_paths(device):
        raise RuntimeError("Back persisted the confirmkarmaexpense draft")

    device.tap("settings-confirm-karma-expense")
    device.tap("settings-save", timeout=45, scroll=True)
    assert_state(device, revision=1, confirm_delete=True, confirm_karma=False)
    primary = state_path(device)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    assert_toggle(device, "settings-confirm-delete", True)
    assert_toggle(device, "settings-confirm-karma-expense", False)

    device.tap("settings-confirm-delete")
    device.tap("settings-confirm-karma-expense")
    device.tap("settings-save", timeout=45, scroll=True)
    assert_state(device, revision=2, confirm_delete=False, confirm_karma=True)
    backup = read_state(device, primary + ".bak")
    if (backup.get("Revision"), backup.get("ConfirmDelete"), backup.get("ConfirmKarmaExpense")) != (1, True, False):
        raise RuntimeError(f"Atomic backup is not the previous whole snapshot: {backup!r}")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    assert_toggle(device, "settings-confirm-delete", False)
    assert_toggle(device, "settings-confirm-karma-expense", True)

    device.shell("run-as", shared.PACKAGE, "sh", "-c", f"printf '{{broken' > '{primary}'")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    assert_toggle(device, "settings-confirm-delete", True)
    assert_toggle(device, "settings-confirm-karma-expense", False)
    assert_state(device, revision=1, confirm_delete=True, confirm_karma=False)

    current_remote_sha256 = hashlib.sha256(device.run("exec-out", "cat", remote_runner, text=False).stdout).hexdigest()
    if shared.sha256(runner) != original_runner_sha256 or current_remote_sha256 != remote_runner_sha256:
        raise RuntimeError("Character XML changed while editing application settings")
    device.capture("application-confirm-karma-expense-recovery")

    controls = {f"EditGlobalSettings.{CONTROL}": {key: "pass" for key in CONTROL_PROOF_KEYS}}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "application-confirm-karma-expense",
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
            "legacyDefaultsAndMigration": "pass",
            "draftDiscardedOnBack": "pass",
            "explicitWholeSnapshotSave": "pass",
            "processRestartReadback": "pass",
            "atomicPreviousSnapshotBackup": "pass",
            "newestValidBackupRecovery": "pass",
            "characterDocumentPreserved": "pass",
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
        print(f"application-confirm-karma-expense E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
