#!/usr/bin/env python3
"""Fail closed until an exact governed SR5 After Run fixture is committed."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_sr5_career_active_skill_wizard_e2e as physical


SCHEMA = "chummer.android.sr5-after-run-settlement-physical-e2e/v1"
UNAVAILABLE_REASON = (
    "No exact committed governed After Run fixture binds run/proposal/character IDs, "
    "reward receipt, both review digests, Heat/reputation/contact deltas, atomic Core "
    "receipt, saved successor workspace, and restart recovery. The native surface and "
    "managed authority skeleton exist, but this lane makes no passing device claim."
)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument(
        "--fixture",
        type=Path,
        default=Path("tests/fixtures/sr5-after-run-settlement-e2e.json"),
    )
    return parser.parse_args(argv)


def execute(args: argparse.Namespace) -> dict[str, object]:
    android_root = Path(__file__).resolve().parents[1]
    workspace_root = args.workspace_root.resolve()
    provenance = load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=workspace_root / "chummer-core-engine",
        presentation_root=workspace_root / "chummer-presentation",
        apk=args.apk.resolve(),
    )
    fixture = args.fixture.resolve()
    return {
        "schema": SCHEMA,
        "status": "unavailable",
        "executionStatus": "not-run",
        "physicalDeviceProof": False,
        "releaseEvidenceEligible": False,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "buildProvenance": provenance,
        "unavailableReason": UNAVAILABLE_REASON,
        "requiredNextArtifact": str(fixture),
        "fixtureExists": fixture.is_file(),
        "requiredProof": {
            "apiLevel": 36,
            "transportPreflightRequiredBeforeMutation": True,
            "transportPreflightConsecutiveObservations": (
                physical.shared.ADB_PREFLIGHT_REQUIRED_CONSECUTIVE
            ),
            "transportPreflightMaximumObservations": (
                physical.shared.ADB_PREFLIGHT_MAX_OBSERVATIONS
            ),
            "transportPreflightObservationDelaySeconds": (
                physical.shared.ADB_PREFLIGHT_OBSERVATION_DELAY_SECONDS
            ),
            "readOnlyTransportRetryBounded": True,
            "readOnlyTransportMaximumAttempts": (
                physical.shared.ADB_READ_ONLY_MAX_ATTEMPTS
            ),
            "explicitAdbReconnectCommandAllowed": False,
            "mutatingOrAmbiguousCommandReplayAllowed": False,
            "mutatingOrAmbiguousCommandMaximumAttempts": 1,
            "exactFixture": True,
            "exactProposalRunCharacterIds": True,
            "bothReviewDigests": True,
            "beforeAfterWorkspaceDigestsDiffer": True,
            "coreReceiptDigest": True,
            "forceStopAndNewProcess": True,
            "editSaveReopenRestart": True,
        },
    }


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    receipt = execute(args)
    physical.prepare_receipt_target(args.receipt.resolve())
    physical.write_receipt_atomically(args.receipt.resolve(), receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 3


if __name__ == "__main__":
    raise SystemExit(main())
