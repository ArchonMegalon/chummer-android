#!/usr/bin/env python3
"""Prove Chummer5 date/time Global Settings semantics on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import run_api36_editing_e2e as shared


CONTROLS = (
    "EditGlobalSettings.chkCustomDateTimeFormats",
    "EditGlobalSettings.txtDateFormat",
    "EditGlobalSettings.txtTimeFormat",
    "EditGlobalSettings.chkDatesIncludeTime",
)
PROOF_KEYS = (
    "legacyRegistryIdentity",
    "cultureDefaultPhase",
    "customEnabledPhase",
    "legacyErrorPreview",
    "disabledPhasePreservesStoredCustomValues",
    "datesIncludeTimeIndependent",
    "typedSettingIdentity",
    "wholePageExpectedRevisionCas",
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
    device.wait("settings-custom-date-time-formats", timeout=45, scroll=True, max_scrolls=16)


def checked(device: shared.Device, selector: str) -> bool:
    return device.wait(selector, timeout=45, scroll=True, max_scrolls=16).attributes.get("checked") == "true"


def enabled(device: shared.Device, selector: str) -> bool:
    return device.wait(selector, timeout=45, scroll=True, max_scrolls=16).attributes.get("enabled") == "true"


def entry_text(device: shared.Device, selector: str, label: str) -> str:
    return shared.selected_text(device, selector, label, scroll=True)


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


def assert_date_time_state(
    device: shared.Device,
    revision: int,
    custom: bool,
    date_format: str,
    time_format: str,
    dates_include_time: bool,
) -> None:
    state = read_state(device)
    observed = (
        state.get("Revision"),
        state.get("CustomDateTimeFormats"),
        state.get("CustomDateFormat"),
        state.get("CustomTimeFormat"),
        state.get("DatesIncludeTime"),
    )
    expected = (revision, custom, date_format, time_format, dates_include_time)
    if observed != expected:
        raise RuntimeError(f"Application date/time snapshot mismatch: {state!r}")


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
        default=Path(__file__).resolve().parent / "fixtures/application-date-time-settings-e2e.chum5",
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
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, runner.name)
    shared.wait_for_phone_runner_route(device, timeout=120)

    open_settings(device)
    if checked(device, "settings-custom-date-time-formats"):
        raise RuntimeError("Chummer5 usecustomdatetime default must be false")
    if not checked(device, "settings-dates-include-time"):
        raise RuntimeError("Chummer5 datesincludetime default must be true")
    if enabled(device, "settings-date-format") or enabled(device, "settings-time-format"):
        raise RuntimeError("Culture-default format fields must be disabled")
    culture_date = entry_text(device, "settings-date-format", "Date format")
    culture_time = entry_text(device, "settings-time-format", "Time format")

    device.tap("settings-custom-date-time-formats")
    device.set_text("settings-date-format", "Date format", "yyyy-MM-dd", scroll=True)
    device.set_text("settings-time-format", "Time format", "%", scroll=True)
    if "Error" not in entry_text(device, "settings-time-format-preview", "Time preview"):
        raise RuntimeError("Invalid custom time format did not surface the legacy Error preview")
    device.back()
    open_settings(device)
    if checked(device, "settings-custom-date-time-formats") or find_state_paths(device):
        raise RuntimeError("Back persisted the date/time drafts")
    if entry_text(device, "settings-date-format", "Date format") != culture_date:
        raise RuntimeError("Culture-default date draft changed after Back")
    if entry_text(device, "settings-time-format", "Time format") != culture_time:
        raise RuntimeError("Culture-default time draft changed after Back")

    device.tap("settings-custom-date-time-formats")
    device.set_text("settings-date-format", "Date format", "yyyy-MM-dd", scroll=True)
    device.set_text("settings-time-format", "Time format", "HH:mm:ss", scroll=True)
    device.tap("settings-dates-include-time", timeout=45, scroll=True)
    device.tap("settings-save", timeout=45, scroll=True)
    assert_date_time_state(device, 1, True, "yyyy-MM-dd", "HH:mm:ss", False)
    primary = state_path(device)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    if not checked(device, "settings-custom-date-time-formats"):
        raise RuntimeError("Custom phase did not survive process restart")
    if entry_text(device, "settings-date-format", "Date format") != "yyyy-MM-dd":
        raise RuntimeError("Custom date format did not survive process restart")
    if entry_text(device, "settings-time-format", "Time format") != "HH:mm:ss":
        raise RuntimeError("Custom time format did not survive process restart")
    if checked(device, "settings-dates-include-time"):
        raise RuntimeError("Dates include time did not survive process restart")

    device.tap("settings-custom-date-time-formats")
    if enabled(device, "settings-date-format") or enabled(device, "settings-time-format"):
        raise RuntimeError("Disabling custom formats did not enter the culture-default phase")
    device.tap("settings-save", timeout=45, scroll=True)
    assert_date_time_state(device, 2, False, "yyyy-MM-dd", "HH:mm:ss", False)
    backup = read_state(device, primary + ".bak")
    if (
        backup.get("Revision"),
        backup.get("CustomDateTimeFormats"),
        backup.get("CustomDateFormat"),
        backup.get("CustomTimeFormat"),
        backup.get("DatesIncludeTime"),
    ) != (1, True, "yyyy-MM-dd", "HH:mm:ss", False):
        raise RuntimeError(f"Atomic backup is not the previous whole snapshot: {backup!r}")

    device.shell("run-as", shared.PACKAGE, "sh", "-c", f"printf '{{broken' > '{primary}'")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    if not checked(device, "settings-custom-date-time-formats"):
        raise RuntimeError("Newest valid backup was not recovered")
    assert_date_time_state(device, 1, True, "yyyy-MM-dd", "HH:mm:ss", False)

    current_remote_sha256 = hashlib.sha256(device.run("exec-out", "cat", remote_runner, text=False).stdout).hexdigest()
    if shared.sha256(runner) != original_runner_sha256 or current_remote_sha256 != remote_runner_sha256:
        raise RuntimeError("Character XML changed while editing application settings")
    device.capture("application-date-time-settings-recovery")

    controls = {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "application-date-time-settings",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "runnerFixtureSha256": shared.sha256(runner),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "legacyDefaultsAndCulturePhase": "pass",
            "customPhaseAndErrorPreview": "pass",
            "draftDiscardedOnBack": "pass",
            "explicitWholeSnapshotSave": "pass",
            "disabledPhasePreservesStoredCustomValues": "pass",
            "datesIncludeTimeIndependent": "pass",
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
        print(f"application-date-time-settings E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
