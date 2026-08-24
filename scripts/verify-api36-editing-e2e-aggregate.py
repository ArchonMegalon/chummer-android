#!/usr/bin/env python3
"""Require the exact API-36 phone journey set bound to one APK authority."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SHA256 = re.compile(r"^[0-9a-f]{64}$")
ARTIFACT_DIGEST = re.compile(r"^(?:sha256:)?[0-9a-f]{64}$")
POSITIVE_INTEGER = re.compile(r"^[1-9][0-9]*$")
JOURNEYS = {
    "full-editing": "full",
    "creation-prerequisite": "creation-prerequisite",
    "career-active-skill-advance": "career-active-skill-advance",
    "career-weapon-fire": "career-weapon-fire",
}
STARTED_FIELDS = {
    "profile",
    "matrix_journey",
    "driver_journey",
    "artifact_id",
    "artifact_digest",
    "artifact_name",
    "artifact_attempt",
    "apk_sha256",
}


def object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json_object(path: Path) -> dict[str, Any]:
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"expected one regular JSON receipt: {path}")
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream, object_pairs_hook=object_without_duplicates)
    if not isinstance(value, dict):
        raise ValueError(f"JSON receipt root is not an object: {path}")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def expected_artifact_directory(journey: str, run_id: str) -> str:
    return f"chummer-android-api36-phone-{journey}-evidence-{run_id}"


def read_execution_started(path: Path) -> dict[str, str]:
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"execution-started evidence is missing: {path}")
    result: dict[str, str] = {}
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        key, separator, value = raw_line.partition("=")
        if not separator or not key or not value or key in result:
            raise ValueError(f"execution-started evidence is ambiguous: {path}")
        result[key] = value
    if set(result) != STARTED_FIELDS:
        raise ValueError(
            f"execution-started fields differ: expected={sorted(STARTED_FIELDS)!r}, "
            f"actual={sorted(result)!r}"
        )
    return result


def require_portable_receipt_seal(receipt: Path, seal: Path) -> str:
    if seal.is_symlink() or not seal.is_file():
        raise ValueError(f"journey receipt seal is missing: {seal}")
    fields = seal.read_text(encoding="utf-8").strip().split()
    if len(fields) != 2 or fields[1] != "receipt.json" or not SHA256.fullmatch(fields[0]):
        raise ValueError(f"journey receipt seal is not canonical: {seal}")
    actual = sha256(receipt)
    if actual != fields[0]:
        raise ValueError(f"journey receipt seal mismatch: {receipt}")
    return actual


def canonical_authority(
    *,
    run_id: str,
    artifact_id: str,
    artifact_digest: str,
    artifact_name: str,
    artifact_attempt: str,
    apk_sha256: str,
) -> dict[str, Any]:
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
    expected_name = f"chummer-android-api36-x64-debug-{run_id}-{artifact_attempt}"
    if artifact_name != expected_name:
        raise ValueError("artifact name is not bound to the expected run and attempt")
    return {
        "schema": "chummer.android.api36-apk-authority/v1",
        "runId": int(run_id),
        "artifactId": artifact_id,
        "artifactDigest": artifact_digest,
        "artifactName": artifact_name,
        "artifactAttempt": int(artifact_attempt),
        "apkSha256": apk_sha256,
    }


def validate_aggregate(
    evidence_root: Path,
    *,
    run_id: str,
    build_result: str,
    matrix_result: str,
    artifact_id: str,
    artifact_digest: str,
    artifact_name: str,
    artifact_attempt: str,
    apk_sha256: str,
) -> dict[str, Any]:
    if build_result != "success":
        raise ValueError(f"build job did not succeed: {build_result!r}")
    if matrix_result != "success":
        raise ValueError(f"phone journey matrix did not succeed: {matrix_result!r}")
    if evidence_root.is_symlink() or not evidence_root.is_dir():
        raise ValueError("journey evidence root is not one regular directory")

    authority = canonical_authority(
        run_id=run_id,
        artifact_id=artifact_id,
        artifact_digest=artifact_digest,
        artifact_name=artifact_name,
        artifact_attempt=artifact_attempt,
        apk_sha256=apk_sha256,
    )
    expected_directories = {
        expected_artifact_directory(journey, run_id): journey for journey in JOURNEYS
    }
    actual_entries = list(evidence_root.iterdir())
    if any(entry.is_symlink() or not entry.is_dir() for entry in actual_entries):
        raise ValueError("journey evidence root contains a non-directory or link")
    actual_names = {entry.name for entry in actual_entries}
    if actual_names != set(expected_directories):
        raise ValueError(
            "journey evidence artifact cardinality/name mismatch: "
            f"expected={sorted(expected_directories)!r}, actual={sorted(actual_names)!r}"
        )

    receipt_paths: list[Path] = []
    for directory in actual_entries:
        for root, directories, files in os.walk(directory, followlinks=False):
            root_path = Path(root)
            if any((root_path / child).is_symlink() for child in directories):
                raise ValueError("journey evidence contains a directory symlink")
            if any((root_path / child).is_symlink() for child in files):
                raise ValueError("journey evidence contains a file symlink")
            receipt_paths.extend(root_path / child for child in files if child == "receipt.json")
    expected_receipt_paths = {
        evidence_root / directory / "receipt.json" for directory in expected_directories
    }
    if len(receipt_paths) != len(JOURNEYS) or set(receipt_paths) != expected_receipt_paths:
        raise ValueError(
            f"exactly {len(JOURNEYS)} top-level named journey receipts are required; "
            f"found={sorted(str(path) for path in receipt_paths)!r}"
        )

    aggregate_journeys: dict[str, Any] = {}
    for directory_name, journey in expected_directories.items():
        driver_journey = JOURNEYS[journey]
        directory = evidence_root / directory_name
        receipt_path = directory / "receipt.json"
        receipt = read_json_object(receipt_path)
        receipt_sha256 = require_portable_receipt_seal(
            receipt_path,
            directory / "receipt.json.sha256",
        )
        started = read_execution_started(directory / "execution-started.txt")
        expected_started = {
            "profile": "phone",
            "matrix_journey": journey,
            "driver_journey": driver_journey,
            "artifact_id": artifact_id,
            "artifact_digest": artifact_digest,
            "artifact_name": artifact_name,
            "artifact_attempt": artifact_attempt,
            "apk_sha256": apk_sha256,
        }
        if started != expected_started:
            raise ValueError(f"execution-started authority differs for {journey}")
        if receipt.get("status") != "pass":
            raise ValueError(f"journey receipt is not passing: {journey}")
        if "executionStatus" in receipt and receipt["executionStatus"] != "pass":
            raise ValueError(f"journey execution did not pass: {journey}")
        if receipt.get("profile") != "phone":
            raise ValueError(f"journey receipt is not phone-only: {journey}")
        if receipt.get("matrixJourney") != journey:
            raise ValueError(f"matrix journey receipt binding differs: {journey}")
        if receipt.get("driverJourney") != driver_journey:
            raise ValueError(f"driver journey receipt binding differs: {journey}")
        if receipt.get("apkSha256") != apk_sha256:
            raise ValueError(f"APK SHA-256 differs: {journey}")
        if receipt.get("artifactAuthority") != authority:
            raise ValueError(f"artifact authority differs: {journey}")
        aggregate_journeys[journey] = {
            "status": "pass",
            "driverJourney": driver_journey,
            "receiptSha256": receipt_sha256,
        }

    return {
        "schema": "chummer.android.api36-editing-e2e-aggregate/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "artifactAuthority": authority,
        "journeyCount": len(JOURNEYS),
        "journeys": aggregate_journeys,
    }


def write_atomically(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_symlink() or (path.exists() and not path.is_file()):
        raise ValueError("aggregate receipt path is not a regular file target")
    encoded = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")
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
    parser.add_argument("--evidence-root", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--build-result", required=True)
    parser.add_argument("--matrix-result", required=True)
    parser.add_argument("--artifact-id", required=True)
    parser.add_argument("--artifact-digest", required=True)
    parser.add_argument("--artifact-name", required=True)
    parser.add_argument("--artifact-attempt", required=True)
    parser.add_argument("--apk-sha256", required=True)
    args = parser.parse_args()

    evidence_root = args.evidence_root.absolute()
    if evidence_root.is_symlink():
        raise ValueError("journey evidence root must not be a symlink")
    receipt_path = args.receipt.absolute()
    if receipt_path.is_symlink():
        raise ValueError("aggregate receipt path must not be a symlink")

    aggregate = validate_aggregate(
        evidence_root,
        run_id=args.run_id,
        build_result=args.build_result,
        matrix_result=args.matrix_result,
        artifact_id=args.artifact_id,
        artifact_digest=args.artifact_digest,
        artifact_name=args.artifact_name,
        artifact_attempt=args.artifact_attempt,
        apk_sha256=args.apk_sha256,
    )
    write_atomically(receipt_path, aggregate)
    print(
        "api36_phone_evidence_aggregate=pass "
        f"journeys={len(JOURNEYS)} artifact_id={args.artifact_id} "
        f"apk_sha256={args.apk_sha256}"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"api36 phone evidence aggregate failed: {error}")
        raise SystemExit(1) from error
