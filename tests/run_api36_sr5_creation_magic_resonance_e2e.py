#!/usr/bin/env python3
"""Fail closed until an exact SR5 Priority Magic/Resonance fixture is committed."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_sr5_career_active_skill_wizard_e2e as physical


SCHEMA = "chummer.android.sr5-priority-creation-magic-resonance-physical-e2e/v1"
UNAVAILABLE_REASON = (
    "No committed unfinished SR5 Standard Priority fixture currently binds an exact "
    "Priority Talent, metatype and Attributes prerequisite, source/custom-data/GM/runtime "
    "digests, complete Tradition/Stream/Power/Spell/Form catalogs, auxiliary receipt, "
    "process restart, and saved successor. Source and managed contract tests must not be "
    "widened into physical API-36 proof."
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
        "requiredNextArtifact": (
            "tests/fixtures/sr5-priority-creation-magic-resonance-e2e.chum5"
        ),
        "requiredJourney": [
            "creation-stage-magic-resonance",
            "creation-magic-resonance-catalog-page",
            "creation-magic-resonance-option-page",
            "creation-magic-resonance-review-page",
            "creation-magic-resonance-confirm-receipt",
            "process-restart",
            "resolve-interrupted-idempotent-confirm",
            "saved-draft-reopen",
        ],
        "requiredAssertions": [
            "exact-budget-prerequisite-blocker-source-anchor-rendering",
            "unsupported-custom-ai-remains-disabled",
            "typed-draft-review-confirm-only",
            "character-document-unchanged",
            "receipt-and-build-provenance-digests-match",
        ],
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
