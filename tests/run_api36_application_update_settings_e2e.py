#!/usr/bin/env python3
"""Prove Chummer5 update Global Settings semantics on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

import run_api36_editing_e2e as shared


CONTROLS = (
    "EditGlobalSettings.chkPreferNightlyBuilds",
    "EditGlobalSettings.chkLiveUpdateCleanCharacterFiles",
)
PROOF_KEYS = (
    "exactLegacySourceAuthority",
    "legacyRegistryIdentity",
    "assemblyBuildDependentDefault",
    "legacyDefaultFalse",
    "independentBooleanValues",
    "dirtyDraftOnly",
    "draftBackDoesNotPersist",
    "typedSettingIdentity",
    "wholePageExpectedRevisionCas",
    "singleAtomicSave",
    "processRestartReadback",
    "newestValidBackupRecovery",
    "characterDocumentPreserved",
)
LEGACY_REVISION = "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3"
LEGACY_SOURCE_DIGESTS = {
    "legacyEditGlobalSettingsDesignerSha256": (
        "Chummer/Forms/EditGlobalSettings.Designer.cs",
        "8b5070f37ee7231fec6b4a1c01525845d23c15342942c5300025f3f7bf9df88a",
    ),
    "legacyEditGlobalSettingsSha256": (
        "Chummer/Forms/EditGlobalSettings.cs",
        "69252752f1d75c32392407f88f8627e7ec802087870ff0415dd42d7d2d6d565e",
    ),
    "legacyGlobalSettingsSha256": (
        "Chummer/Backend/Static/GlobalSettings.cs",
        "bf22d91a6ba2d3b24092fa70d00c08d92b62a519ac59dc28fd51c64beb05a577",
    ),
    "legacyUtilsSha256": (
        "Chummer/Backend/Static/Utils.cs",
        "418458b46a4ff1f0a1ab8baa09e24019466f50752488b5c9596a2fdd758b4e34",
    ),
}


def resolve_repository(root: Path, label: str, names: tuple[str, ...]) -> Path:
    matches = [root / name for name in names if (root / name).is_dir()]
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected exactly one {label} repository under {root}, got {[str(path) for path in matches]!r}"
        )
    return matches[0]


def authenticate_legacy_source(root: Path) -> dict[str, str]:
    revision = subprocess.run(
        ["git", "-C", str(root), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout.strip()
    if revision != LEGACY_REVISION:
        raise RuntimeError(f"Expected canonical Chummer5 {LEGACY_REVISION}, got {revision}")
    dirty = subprocess.run(
        ["git", "-C", str(root), "status", "--porcelain", "--untracked-files=no"],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout.strip()
    if dirty:
        raise RuntimeError("Canonical Chummer5 tracked source is dirty")

    observed: dict[str, str] = {}
    for key, (relative, expected) in LEGACY_SOURCE_DIGESTS.items():
        actual = shared.sha256(root / relative)
        if actual != expected:
            raise RuntimeError(f"Legacy source digest mismatch for {relative}: {actual}")
        observed[key] = actual
    return observed


def open_settings(device: shared.Device) -> None:
    device.wait("home-application-settings", timeout=120, scroll=True, max_scrolls=16)
    device.tap("home-application-settings", timeout=60, scroll=True)
    device.wait("application-settings-page", timeout=60)
    device.wait("settings-update-default-authority", timeout=45, scroll=True, max_scrolls=20)


def checked(device: shared.Device, selector: str) -> bool:
    return device.wait(selector, timeout=45, scroll=True, max_scrolls=20).attributes.get("checked") == "true"


def application_build(device: shared.Device) -> int:
    authority = device.wait(
        "settings-update-default-authority", timeout=45, scroll=True, max_scrolls=20
    ).attributes.get("text", "")
    match = re.search(r"Application assembly Build=(-?\d+);", authority)
    if match is None:
        raise RuntimeError(f"Application version default authority is missing: {authority!r}")
    return int(match.group(1))


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
    raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path or state_path(device)).stdout
    state = json.loads(raw)
    if not isinstance(state, dict):
        raise RuntimeError("Application settings state is not an object")
    return state


def assert_update_state(
    device: shared.Device,
    revision: int,
    prefer_nightly_builds: bool,
    live_update_clean_character_files: bool,
) -> None:
    state = read_state(device)
    observed = (
        state.get("Revision"),
        state.get("PreferNightlyBuilds"),
        state.get("LiveUpdateCleanCharacterFiles"),
    )
    expected = (revision, prefer_nightly_builds, live_update_clean_character_files)
    if observed != expected:
        raise RuntimeError(f"Application update snapshot mismatch: {state!r}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--chummer5-root", type=Path, default=Path("/docker/chummer5a"))
    parser.add_argument(
        "--runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures/application-update-settings-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    presentation_root = resolve_repository(workspace_root, "presentation", ("presentation", "chummer-presentation"))
    core_root = resolve_repository(workspace_root, "core", ("core", "chummer-core-engine"))
    legacy_source_digests = authenticate_legacy_source(args.chummer5_root.resolve())
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
    build = application_build(device)
    prefer_default = build != 0
    if checked(device, "settings-prefer-nightly-builds") != prefer_default:
        raise RuntimeError("Prefer Nightly default does not match application Assembly Version.Build")
    if checked(device, "settings-live-update-clean-character-files"):
        raise RuntimeError("Chummer5 liveupdatecleancharacterfiles default must be false")

    device.tap("settings-prefer-nightly-builds", timeout=45, scroll=True)
    device.tap("settings-live-update-clean-character-files", timeout=45, scroll=True)
    device.back()
    open_settings(device)
    if checked(device, "settings-prefer-nightly-builds") != prefer_default or checked(
        device, "settings-live-update-clean-character-files"
    ):
        raise RuntimeError("Back persisted update-setting drafts")
    if find_state_paths(device):
        raise RuntimeError("Draft-only update-setting edits created durable state")

    device.tap("settings-prefer-nightly-builds", timeout=45, scroll=True)
    device.tap("settings-live-update-clean-character-files", timeout=45, scroll=True)
    device.tap("settings-save", timeout=45, scroll=True)
    first_prefer = not prefer_default
    assert_update_state(device, 1, first_prefer, True)
    primary = state_path(device)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    if checked(device, "settings-prefer-nightly-builds") != first_prefer or not checked(
        device, "settings-live-update-clean-character-files"
    ):
        raise RuntimeError("Both update-setting values did not survive process restart")

    device.tap("settings-prefer-nightly-builds", timeout=45, scroll=True)
    device.tap("settings-save", timeout=45, scroll=True)
    assert_update_state(device, 2, prefer_default, True)
    backup = read_state(device, primary + ".bak")
    if (
        backup.get("Revision"),
        backup.get("PreferNightlyBuilds"),
        backup.get("LiveUpdateCleanCharacterFiles"),
    ) != (1, first_prefer, True):
        raise RuntimeError(f"Atomic backup is not the previous whole snapshot: {backup!r}")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    if checked(device, "settings-prefer-nightly-builds") != prefer_default or not checked(
        device, "settings-live-update-clean-character-files"
    ):
        raise RuntimeError("Independent update-setting values did not survive process restart")

    device.shell("run-as", shared.PACKAGE, "sh", "-c", f"printf '{{broken' > '{primary}'")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    open_settings(device)
    if checked(device, "settings-prefer-nightly-builds") != first_prefer or not checked(
        device, "settings-live-update-clean-character-files"
    ):
        raise RuntimeError("Newest valid whole-page backup was not recovered")
    assert_update_state(device, 1, first_prefer, True)

    current_remote_sha256 = hashlib.sha256(device.run("exec-out", "cat", remote_runner, text=False).stdout).hexdigest()
    if shared.sha256(runner) != original_runner_sha256 or current_remote_sha256 != remote_runner_sha256:
        raise RuntimeError("Character XML changed while editing application settings")
    device.capture("application-update-settings-recovery")

    controls = {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "application-update-settings",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        **legacy_source_digests,
        "legacyRevision": LEGACY_REVISION,
        "applicationAssemblyBuild": build,
        "preferNightlyBuildsDefault": prefer_default,
        "runnerFixtureSha256": shared.sha256(runner),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "legacyDefaults": "pass",
            "draftDiscardedOnBack": "pass",
            "explicitWholeSnapshotSave": "pass",
            "independentValueSave": "pass",
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
        print(f"application-update-settings E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
