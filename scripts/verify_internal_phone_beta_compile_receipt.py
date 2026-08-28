#!/usr/bin/env python3
"""Verify durable evidence behind an internal phone-beta compile receipt."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import stat
from pathlib import Path


CONTRACT = "chummer.android.internal-phone-beta-native-compile/v1"
EXPECTED_EVIDENCE = (
    "authority-intake.log",
    "authority-binding.json",
    "restore.log",
    "owned-compile-graph.log",
    "compile-graph.json",
    "build.log",
    "command-journal.jsonl",
)
DIGEST_BINDINGS = {
    "authority-binding.json": "authorityBindingSha256",
    "restore.log": "restoreOutputSha256",
    "compile-graph.json": "compileGraphSha256",
    "build.log": "buildOutputSha256",
    "command-journal.jsonl": "journalSha256",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_regular(path: Path, label: str) -> None:
    try:
        mode = path.lstat().st_mode
    except FileNotFoundError as exc:
        raise ValueError(f"{label} is missing") from exc
    if not stat.S_ISREG(mode) or path.is_symlink():
        raise ValueError(f"{label} must be a non-symlink regular file")


def verify_receipt(receipt_path: Path, evidence_directory: Path | None = None) -> dict[str, object]:
    if not receipt_path.is_absolute():
        raise ValueError("receipt path must be absolute")
    require_regular(receipt_path, "receipt")
    payload = json.loads(receipt_path.read_text(encoding="utf-8"))
    if payload.get("contractName") != CONTRACT:
        raise ValueError("compile receipt contract mismatch")
    if payload.get("status") not in {"pass", "blocked"}:
        raise ValueError("compile receipt status must be pass or blocked")
    if payload.get("publicationAuthorized") is not False:
        raise ValueError("compile receipt must remain publication false")

    declared_directory = payload.get("evidenceDirectory")
    if not isinstance(declared_directory, str) or not declared_directory.startswith("/"):
        raise ValueError("evidenceDirectory must be absolute")
    expected_directory = evidence_directory or Path(f"{receipt_path}.evidence")
    if Path(declared_directory) != expected_directory:
        raise ValueError("evidenceDirectory does not match the canonical output path")
    try:
        directory_mode = expected_directory.lstat().st_mode
    except FileNotFoundError as exc:
        raise ValueError("evidence directory is missing") from exc
    if not stat.S_ISDIR(directory_mode) or expected_directory.is_symlink():
        raise ValueError("evidence directory must be a non-symlink directory")
    if expected_directory.resolve(strict=True) != expected_directory:
        raise ValueError("evidence directory must be canonical")

    rows = payload.get("evidence")
    if not isinstance(rows, list):
        raise ValueError("evidence inventory must be an array")
    names: list[str] = []
    actual_rows: list[dict[str, object]] = []
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("evidence row must be an object")
        name = row.get("path")
        if not isinstance(name, str) or name not in EXPECTED_EVIDENCE:
            raise ValueError("evidence path is noncanonical")
        if name in names:
            raise ValueError("duplicate evidence path")
        names.append(name)
        path = expected_directory / name
        require_regular(path, f"evidence {name}")
        digest = sha256(path)
        size = path.stat().st_size
        if row.get("sha256") != digest or row.get("sizeBytes") != size:
            raise ValueError(f"evidence digest/size mismatch: {name}")
        binding = DIGEST_BINDINGS.get(name)
        if binding is not None and payload.get(binding) != digest:
            raise ValueError(f"receipt digest binding mismatch: {name}")
        if name == "command-journal.jsonl" and payload.get("journalSizeBytes") != size:
            raise ValueError("receipt journal size binding mismatch")
        actual_rows.append({"path": name, "sha256": digest, "sizeBytes": size})

    canonical_names = [name for name in EXPECTED_EVIDENCE if name in names]
    if names != canonical_names:
        raise ValueError("evidence inventory order is noncanonical")
    directory_names = sorted(entry.name for entry in os.scandir(expected_directory))
    if directory_names != sorted(names):
        raise ValueError("evidence directory has missing or extra files")
    if payload["status"] == "pass" and tuple(names) != EXPECTED_EVIDENCE:
        raise ValueError("passing receipt requires the complete evidence inventory")

    return {
        "contractName": CONTRACT,
        "status": "pass",
        "verifiedReceiptStatus": payload["status"],
        "publicationAuthorized": False,
        "evidence": actual_rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--evidence-directory", type=Path)
    args = parser.parse_args()
    try:
        result = verify_receipt(args.receipt, args.evidence_directory)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(json.dumps({
            "contractName": CONTRACT,
            "status": "blocked",
            "publicationAuthorized": False,
            "error": str(exc),
        }, sort_keys=True))
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
