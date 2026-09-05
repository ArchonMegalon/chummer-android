#!/usr/bin/env python3
"""Prove one governed SR5 After Run settlement on the hosted API 36 phone lane."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import time

import run_api36_editing_e2e as shared
import run_api36_sr5_after_run_settlement_e2e as settlement


SCHEMA = "chummer.android.sr5-after-run-settlement-hosted-e2e/v1"
JOURNEY = "sr5-after-run-settlement"
MATRIX_JOURNEY = "after-run-settlement"
HOSTED_ABI = "x86_64"
REMOTE_HIERARCHY = "/data/local/tmp/chummer-editing-window.xml"


def source_paths() -> dict[str, Path]:
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    return {
        "driverSha256": driver,
        "settlementAuthorityDriverSha256": Path(settlement.__file__).resolve(),
        "physicalHarnessSha256": Path(settlement.physical.__file__).resolve(),
        "sharedDeviceHarnessSha256": Path(shared.__file__).resolve(),
        "fixtureSha256": settlement.DEFAULT_FIXTURE.resolve(),
        "careerWizardPageSha256": android_root
        / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs",
        "manualProposalPageSha256": android_root
        / "src/Chummer.Android/Native/Sr5AfterRunManualProposalPage.cs",
        "manualProposalSourceSha256": android_root
        / "src/Chummer.Android/Native/Sr5AfterRunManualProposalSource.cs",
        "workspaceSnapshotSha256": android_root
        / "src/Chummer.Android/Native/AndroidAfterRunWorkspaceSnapshotSource.cs",
        "checkpointStoreSha256": android_root
        / "src/Chummer.Android/Native/Sr5AfterRunSettlementCheckpointStore.cs",
        "settlementCoordinatorSha256": android_root
        / "src/Chummer.Android/Native/Sr5AfterRunSettlementCoordinator.cs",
        "settlementModelSha256": android_root
        / "src/Chummer.Android/Native/Sr5AfterRunWizardModel.cs",
        "settlementPageSha256": android_root
        / "src/Chummer.Android/Native/Sr5AfterRunSettlementWizardPage.cs",
        "runnerCoordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
    }


def source_snapshot(paths: dict[str, Path]) -> dict[str, str]:
    missing = [str(path) for path in paths.values() if path.is_symlink() or not path.is_file()]
    if missing:
        raise RuntimeError(f"After Run hosted source graph is incomplete: {missing!r}")
    return {name: shared.sha256(path) for name, path in paths.items()}


def build_receipt(
    args: argparse.Namespace,
    *,
    api_level: int,
    abi: str,
    apk_sha256: str,
    source_sha256: dict[str, str],
    runner_sha256: str,
    verified_remote_runner_sha256: str,
    journey: dict[str, object],
    remote_temporary_files: list[dict[str, object]],
    adb_transport: dict[str, object],
) -> dict[str, object]:
    return {
        "schema": SCHEMA,
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": JOURNEY,
        "apiLevel": api_level,
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": apk_sha256,
        **source_sha256,
        "governedFixtureSha256": source_sha256["fixtureSha256"],
        "materializedRunnerSha256": runner_sha256,
        "verifiedRemoteRunnerSha256": verified_remote_runner_sha256,
        "remoteTemporaryFiles": remote_temporary_files,
        "adbTransport": adb_transport,
        "authorityProofStages": journey,
        "journeys": {
            "exactProposalRunCharacterIds": "pass",
            "rewardHeatReputationAndContacts": "pass",
            "gmAndOwnerReviewDigests": "pass",
            "durableReviewRestartResume": "pass",
            "atomicCoreReceiptAndSuccessor": "pass",
            "receiptRestartRecovery": "pass",
            "acknowledgementAndFinalRestart": "pass",
        },
        "scope": {
            "proof": "sr5-after-run-settlement-wizard-only",
            "fullEditing": "excluded",
            "tablet": "deferred",
        },
        "publicationAuthorized": False,
    }


def execute(args: argparse.Namespace) -> dict[str, object]:
    if settlement.physical.SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe ASCII grammar")
    fixture_path = args.fixture.resolve()
    if fixture_path != settlement.DEFAULT_FIXTURE.resolve():
        raise RuntimeError("Hosted After Run proof requires the exact committed governed fixture")
    fixture = settlement.load_fixture(fixture_path)
    evidence = args.evidence.resolve()
    paths = source_paths()
    source_before = source_snapshot(paths)
    apk = args.apk.resolve()
    apk_sha256 = shared.sha256(apk)
    runner, runner_sha256 = settlement.materialize_runner(fixture, evidence)
    device = shared.Device(args.adb.resolve(), args.serial, evidence)
    device.require_transport_stability(expected_api_level="36")
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"After Run hosted E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != HOSTED_ABI:
        raise RuntimeError(
            f"After Run hosted E2E requires the x86_64 phone lane, got {abi!r}"
        )
    emulator = device.shell("getprop", "ro.kernel.qemu")
    if emulator != "1":
        raise RuntimeError("After Run hosted E2E requires a hosted emulator")
    device.require_shared_storage_readiness(
        deadline=time.monotonic()
        + shared.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS,
        hosted_api_level=api,
        hosted_abi=abi,
        hosted_emulator=emulator,
        hosted_proof_attempt=True,
    )

    remote_runner = f"/sdcard/Download/{runner.name}"
    remote_temporary_files = [
        {
            "path": remote_runner,
            "purpose": "temporary governed After Run runner",
            "precleaned": False,
            "deletedAndVerified": False,
        },
        {
            "path": REMOTE_HIERARCHY,
            "purpose": "temporary UIAutomator hierarchy dump",
            "precleaned": False,
            "deletedAndVerified": False,
        },
    ]
    cleanup_errors: list[str] = []
    journey: dict[str, object] | None = None
    verified_remote = ""
    try:
        for remote in remote_temporary_files:
            settlement.physical.remove_remote_temporary_file(device, str(remote["path"]))
            remote["precleaned"] = True
        subprocess.run(
            [
                str(args.adb.resolve()), "-s", args.serial, "install",
                "--no-streaming", "-r", str(apk),
            ],
            check=True,
            timeout=300,
        )
        verified_remote = device.push_verified(runner, remote_runner, runner_sha256)
        journey = settlement.prove_after_run(device, runner, runner_sha256, fixture)
    finally:
        for remote in remote_temporary_files:
            try:
                settlement.physical.remove_remote_temporary_file(device, str(remote["path"]))
                remote["deletedAndVerified"] = True
            except Exception as error:  # noqa: BLE001 - cleanup is part of proof authority
                cleanup_errors.append(f"{remote['path']}: {type(error).__name__}: {error}")

    if cleanup_errors:
        raise RuntimeError(f"After Run hosted cleanup failed: {cleanup_errors!r}")
    if journey is None or not all(
        remote["precleaned"] and remote["deletedAndVerified"]
        for remote in remote_temporary_files
    ):
        raise RuntimeError("After Run hosted journey or temporary-file cleanup is incomplete")
    if source_snapshot(paths) != source_before:
        raise RuntimeError("After Run hosted source authority changed during execution")
    if shared.sha256(apk) != apk_sha256:
        raise RuntimeError("After Run hosted APK authority changed during execution")

    return build_receipt(
        args,
        api_level=int(api),
        abi=abi,
        apk_sha256=apk_sha256,
        source_sha256=source_before,
        runner_sha256=runner_sha256,
        verified_remote_runner_sha256=verified_remote,
        journey=journey,
        remote_temporary_files=remote_temporary_files,
        adb_transport=device.transport_summary(),
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, default=settlement.DEFAULT_FIXTURE)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    receipt = execute(args)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Hosted SR5 After Run E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
