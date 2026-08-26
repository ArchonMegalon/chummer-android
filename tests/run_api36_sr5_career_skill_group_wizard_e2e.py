#!/usr/bin/env python3
"""Fail closed until an exact SR5 SkillGroup physical import fixture is committed."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_sr5_career_active_skill_wizard_e2e as physical


SCHEMA = "chummer.android.sr5-career-skill-group-physical-e2e/v1"
UNAVAILABLE_REASON = (
    "No exact committed SR5 SkillGroup physical import fixture binds group identity, "
    "enabled-member projection, Karma expense, receipt, and saved XML successor. "
    "The typed UI and managed authority exist, but this lane makes no passing device claim."
)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
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
    return {
        "schema": SCHEMA,
        "status": "unavailable",
        "executionStatus": "not-run",
        "physicalDeviceProof": False,
        "releaseEvidenceEligible": False,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "buildProvenance": provenance,
        "unavailableReason": UNAVAILABLE_REASON,
        "requiredNextArtifact": "tests/fixtures/career-skill-group-advance-e2e.chum5",
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
