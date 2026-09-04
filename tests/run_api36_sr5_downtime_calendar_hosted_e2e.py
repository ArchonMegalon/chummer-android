#!/usr/bin/env python3
"""Prove the typed SR5 Downtime Calendar wizard on the hosted API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys

import run_api36_editing_e2e as shared
import run_api36_sr5_downtime_calendar_e2e as downtime
import api36_proof_state as proof_state


RECEIPT_SCHEMA = "chummer.android.editing-e2e/v1"
JOURNEY = "sr5-downtime-calendar"
DEFAULT_FIXTURE = downtime.DEFAULT_FIXTURE
DEFAULT_RUNNER = DEFAULT_FIXTURE.parent / "career-calendar-edit-e2e.chum5"


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    # Kept for parity with the other hosted wizard drivers; the typed Calendar
    # proof is self-contained in the Android candidate and governed fixture.
    parser.add_argument("--workspace-root", type=Path)
    parser.add_argument("--runner", type=Path, default=DEFAULT_RUNNER)
    return parser.parse_args(argv)


def _receipt_path(path: Path) -> Path:
    candidate = path.absolute()
    if not candidate.is_absolute() or candidate.is_symlink():
        raise RuntimeError("Receipt must be one absolute non-symlink path")
    candidate.parent.mkdir(parents=True, exist_ok=True)
    if candidate.exists() and not candidate.is_file():
        raise RuntimeError("Receipt must be one regular file")
    return candidate


def _write_receipt(path: Path, receipt: dict[str, object]) -> None:
    path.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def execute(args: argparse.Namespace) -> dict[str, object]:
    apk = args.apk.resolve()
    runner = args.runner.resolve()
    fixture = downtime.load_fixture()
    if runner != DEFAULT_FIXTURE.parent / str(fixture["runnerFixture"]):
        raise RuntimeError("Hosted Downtime proof requires the committed governed runner")
    if not apk.is_file():
        raise RuntimeError("APK is missing")
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Hosted Downtime E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(f"Hosted Downtime E2E requires x86_64, got {abi!r}")
    device.require_shared_storage_readiness()
    apk_sha256 = shared.sha256(apk)
    runner_sha256 = shared.sha256(runner)
    device.install_verified(apk, apk_sha256, "--no-streaming", "-r")
    provider_registration = device.publish_document_for_documents_ui(
        runner,
        runner_sha256,
    )
    android_root = Path(__file__).resolve().parents[1]
    proof_build_id = (
        f"hosted-{os.environ['GITHUB_RUN_ID']}-"
        f"{os.environ['CHUMMER_E2E_APK_ARTIFACT_ATTEMPT']}"
    )
    proof_expectation = proof_state.expected_build(
        android_root,
        android_root / "eng/api36-sr5-wizard-gate-authority.json",
        proof_build_id,
    )
    journey = downtime.prove_downtime(
        device,
        runner,
        runner_sha256,
        fixture,
        proof_expectation,
    )
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": JOURNEY,
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apkSha256": apk_sha256,
        "driverSha256": shared.sha256(Path(__file__).resolve()),
        "downtimeDriverSha256": shared.sha256(Path(downtime.__file__).resolve()),
        "downtimeFixtureSha256": shared.sha256(DEFAULT_FIXTURE),
        "runnerFixtureSha256": runner_sha256,
        "verifiedRemoteRunnerSha256": provider_registration["sha256"],
        "documentsUiProviderRegistration": provider_registration,
        "authorityProofStages": journey,
        "journeys": {
            "exactCalendarEdit": "pass",
            "durableReview": "pass",
            "reviewRestartAndReconfirm": "pass",
            "atomicApplyAndReceipt": "pass",
            "receiptRestartRecovery": "pass",
            "acknowledgeAndReopen": "pass",
            "finalRestartSuccessor": "pass",
        },
    }


def main(argv: list[str] | None = None) -> int:
    raw_args = list(sys.argv[1:] if argv is None else argv)
    receipt_path: Path | None = None
    try:
        parsed = parse_args(raw_args)
        receipt_path = _receipt_path(parsed.receipt)
        receipt = execute(parsed)
    except (RuntimeError, OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        if receipt_path is not None:
            _write_receipt(
                receipt_path,
                {
                    "schema": RECEIPT_SCHEMA,
                    "status": "fail",
                    "executionStatus": "fail",
                    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                    "profile": "phone",
                    "journey": JOURNEY,
                    "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
                },
            )
        print(f"Hosted SR5 Downtime Calendar E2E failed: {error}", file=sys.stderr)
        return 1
    _write_receipt(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
