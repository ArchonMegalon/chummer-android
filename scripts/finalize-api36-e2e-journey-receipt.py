#!/usr/bin/env python3
"""Bind one API-36 journey receipt to the build job's immutable APK authority."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import tempfile
from pathlib import Path
from typing import Any

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

from api36_wizard_gate_contract import (  # noqa: E402
    DEFAULT_CONTRACT,
    contract_binding,
    journey_map,
)


SHA256 = re.compile(r"^[0-9a-f]{64}$")
ARTIFACT_DIGEST = re.compile(r"^(?:sha256:)?[0-9a-f]{64}$")
POSITIVE_INTEGER = re.compile(r"^[1-9][0-9]*$")
JOURNEYS = journey_map()


def object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json_object(path: Path) -> dict[str, Any]:
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"journey receipt is not one regular file: {path}")
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream, object_pairs_hook=object_without_duplicates)
    if not isinstance(value, dict):
        raise ValueError("journey receipt root must be an object")
    return value


def bind_receipt(
    receipt: dict[str, Any],
    *,
    run_id: str,
    matrix_journey: str,
    driver_journey: str,
    artifact_id: str,
    artifact_digest: str,
    artifact_name: str,
    artifact_attempt: str,
    apk_sha256: str,
    gate_contract_path: Path = DEFAULT_CONTRACT,
) -> dict[str, Any]:
    gate_authority = contract_binding(gate_contract_path)
    expected = JOURNEYS.get(matrix_journey)
    if expected is None:
        raise ValueError(f"unsupported matrix journey: {matrix_journey}")
    expected_driver, expected_schema = expected
    if driver_journey != expected_driver:
        raise ValueError(
            f"matrix/driver journey mismatch: {matrix_journey} -> {driver_journey}"
        )
    if not POSITIVE_INTEGER.fullmatch(run_id):
        raise ValueError("run id is not a positive integer")
    if not POSITIVE_INTEGER.fullmatch(artifact_id):
        raise ValueError("artifact id is not a positive integer")
    if not POSITIVE_INTEGER.fullmatch(artifact_attempt):
        raise ValueError("artifact attempt is not a positive integer")
    if not ARTIFACT_DIGEST.fullmatch(artifact_digest):
        raise ValueError("artifact digest is not canonical SHA-256")
    if not SHA256.fullmatch(apk_sha256):
        raise ValueError("APK SHA-256 is not canonical")
    expected_name = (
        f"chummer-android-api36-x64-debug-{run_id}-{artifact_attempt}"
    )
    if artifact_name != expected_name:
        raise ValueError(
            f"artifact name is not bound to run and attempt: {artifact_name!r}"
        )
    if receipt.get("schema") != expected_schema:
        raise ValueError(
            f"unexpected receipt schema for {matrix_journey}: {receipt.get('schema')!r}"
        )
    if receipt.get("status") != "pass":
        raise ValueError("journey receipt is not passing")
    if "executionStatus" in receipt and receipt["executionStatus"] != "pass":
        raise ValueError("journey execution status is not passing")
    if receipt.get("profile") != "phone":
        raise ValueError("journey receipt is not phone-only")
    if receipt.get("apkSha256") != apk_sha256:
        raise ValueError("journey receipt APK SHA-256 differs from build authority")
    receipt_driver_journey = receipt.get("journey")
    if receipt_driver_journey is not None and receipt_driver_journey != driver_journey:
        raise ValueError(
            "journey receipt driver route differs from the explicit matrix mapping"
        )
    for reserved in (
        "matrixJourney",
        "driverJourney",
        "gateAuthority",
        "artifactAuthority",
    ):
        if reserved in receipt:
            raise ValueError(f"journey receipt already contains reserved field {reserved}")

    receipt["matrixJourney"] = matrix_journey
    receipt["driverJourney"] = driver_journey
    receipt["gateAuthority"] = gate_authority
    receipt["artifactAuthority"] = {
        "schema": "chummer.android.api36-apk-authority/v1",
        "runId": int(run_id),
        "artifactId": artifact_id,
        "artifactDigest": artifact_digest,
        "artifactName": artifact_name,
        "artifactAttempt": int(artifact_attempt),
        "apkSha256": apk_sha256,
    }
    return receipt


def write_atomically(path: Path, value: dict[str, Any]) -> None:
    encoded = (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=path.parent,
            prefix=f".{path.name}.",
            delete=False,
        ) as stream:
            temporary_name = stream.name
            os.fchmod(stream.fileno(), 0o644)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_name, path)
        temporary_name = None
    finally:
        if temporary_name is not None:
            Path(temporary_name).unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--gate-contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--matrix-journey", required=True)
    parser.add_argument("--driver-journey", required=True)
    parser.add_argument("--artifact-id", required=True)
    parser.add_argument("--artifact-digest", required=True)
    parser.add_argument("--artifact-name", required=True)
    parser.add_argument("--artifact-attempt", required=True)
    parser.add_argument("--apk-sha256", required=True)
    args = parser.parse_args()

    receipt_candidate = args.receipt.absolute()
    if receipt_candidate.is_symlink():
        raise ValueError("journey receipt path must not be a symlink")
    receipt_path = receipt_candidate.resolve(strict=True)
    receipt = read_json_object(receipt_path)
    bind_receipt(
        receipt,
        run_id=args.run_id,
        matrix_journey=args.matrix_journey,
        driver_journey=args.driver_journey,
        artifact_id=args.artifact_id,
        artifact_digest=args.artifact_digest,
        artifact_name=args.artifact_name,
        artifact_attempt=args.artifact_attempt,
        apk_sha256=args.apk_sha256,
        gate_contract_path=args.gate_contract,
    )
    write_atomically(receipt_path, receipt)
    print(
        "api36_journey_receipt=bound "
        f"matrix_journey={args.matrix_journey} "
        f"scope=sr5_wizards_only artifact_id={args.artifact_id} "
        f"apk_sha256={args.apk_sha256} publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"api36 journey receipt binding failed: {error}")
        raise SystemExit(1) from error
