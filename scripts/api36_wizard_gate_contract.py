#!/usr/bin/env python3
"""Validate and digest-bind the exact API-36 SR5 wizard gate contract."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
CONTRACT_RELATIVE_PATH = Path("eng/api36-sr5-wizard-gate-authority.json")
DEFAULT_CONTRACT = REPO_ROOT / CONTRACT_RELATIVE_PATH
CONTRACT_SCHEMA = "chummer.android.api36-sr5-wizard-gate-authority/v1"
BINDING_SCHEMA = "chummer.android.api36-sr5-wizard-gate-binding/v1"
AGGREGATE_SCHEMA = "chummer.android.api36-sr5-wizard-e2e-aggregate/v1"
AUTHORITY_CLASS = "internal_phone_beta_sr5_wizard_only"
PROOF_SCOPE = "sr5_wizards_only"
SHA256 = re.compile(r"^[0-9a-f]{64}$")
REQUIRED_JOURNEY_SPECS = (
    {
        "matrixJourney": "creation-prerequisite",
        "driverJourney": "creation-prerequisite",
        "receiptSchema": "chummer.android.creation-prerequisite-e2e/v1",
    },
    {
        "matrixJourney": "career-active-skill-advance",
        "driverJourney": "career-active-skill-advance",
        "receiptSchema": "chummer.android.editing-e2e/v1",
    },
    {
        "matrixJourney": "career-weapon-fire",
        "driverJourney": "career-weapon-fire",
        "receiptSchema": "chummer.android.editing-e2e/v1",
    },
    {
        "matrixJourney": "before-run-edge",
        "driverJourney": "before-run-edge",
        "receiptSchema": "chummer.android.sr5-before-run-edge-e2e/v1",
    },
)
EXCLUDED_FROM_GATE = (
    {
        "matrixJourney": "full-editing",
        "status": "not_required_not_proven",
        "maySatisfyRequiredJourney": False,
    },
)
DOES_NOT_ASSERT = (
    "full_editing_pass",
    "exhaustive_chummer5_edit_parity",
    "tablet_readiness",
    "google_play_upload",
    "public_release_readiness",
    "publication_authority",
)


def object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def expected_contract() -> dict[str, Any]:
    return {
        "schema": CONTRACT_SCHEMA,
        "authorityClass": AUTHORITY_CLASS,
        "proofScope": PROOF_SCOPE,
        "requiredJourneyCount": len(REQUIRED_JOURNEY_SPECS),
        "requiredJourneys": [dict(spec) for spec in REQUIRED_JOURNEY_SPECS],
        "excludedFromGate": [dict(spec) for spec in EXCLUDED_FROM_GATE],
        "publicationAuthorized": False,
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }


def validate_contract(value: dict[str, Any]) -> dict[str, Any]:
    expected = expected_contract()
    required_count = len(REQUIRED_JOURNEY_SPECS)
    if value != expected:
        raise ValueError(
            f"API-36 wizard gate contract differs from the exact {required_count}-journey "
            "wizard-only authority"
        )
    if value["requiredJourneyCount"] != required_count:
        raise ValueError(
            f"API-36 wizard gate must require exactly {required_count} journeys"
        )
    required = [row["matrixJourney"] for row in value["requiredJourneys"]]
    if "full-editing" in required:
        raise ValueError("Full Editing cannot be a required wizard-gate journey")
    return value


def load_contract(path: Path = DEFAULT_CONTRACT) -> dict[str, Any]:
    candidate = path.absolute()
    if candidate.is_symlink() or not candidate.is_file():
        raise ValueError(f"wizard gate contract is not one regular file: {candidate}")
    with candidate.open("r", encoding="utf-8") as stream:
        value = json.load(stream, object_pairs_hook=object_without_duplicates)
    if not isinstance(value, dict):
        raise ValueError("wizard gate contract root must be an object")
    return validate_contract(value)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def contract_binding(path: Path = DEFAULT_CONTRACT) -> dict[str, Any]:
    contract = load_contract(path)
    digest = sha256(path)
    if not SHA256.fullmatch(digest):
        raise ValueError("wizard gate contract SHA-256 is not canonical")
    return {
        "schema": BINDING_SCHEMA,
        "contractPath": CONTRACT_RELATIVE_PATH.as_posix(),
        "contractSha256": digest,
        "authorityClass": contract["authorityClass"],
        "proofScope": contract["proofScope"],
        "requiredJourneyCount": contract["requiredJourneyCount"],
        "requiredJourneys": [
            row["matrixJourney"] for row in contract["requiredJourneys"]
        ],
        "publicationAuthorized": False,
    }


def journey_map() -> dict[str, tuple[str, str]]:
    return {
        spec["matrixJourney"]: (
            spec["driverJourney"],
            spec["receiptSchema"],
        )
        for spec in REQUIRED_JOURNEY_SPECS
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_CONTRACT)
    args = parser.parse_args()
    binding = contract_binding(args.manifest)
    print(
        "api36_wizard_gate_contract=pass "
        f"scope={binding['proofScope']} "
        f"required_journeys={binding['requiredJourneyCount']} "
        f"contract_sha256={binding['contractSha256']} "
        "publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
