#!/usr/bin/env python3
"""Sign and verify the exact AAB/source-graph/build-sidecar transaction."""

from __future__ import annotations

import argparse
import base64
from datetime import UTC, datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import secrets
import stat
import subprocess
import tempfile
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
VERIFY_PATH = ROOT / "scripts/verify_api36_two_green_release_eligibility.py"
CONTRACT = "chummer.android.release-build-attestation/v1"
SCOPE = "android_internal_release_artifact_binding"
ROLE = "android_internal_release_builder"
SOURCE_GRAPH_CONTRACT = "chummer.android.release-source-graph/v3"
MAX_AAB_BYTES = 512 * 1024 * 1024


def _load(path: Path, name: str) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError(f"cannot load {name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


VERIFY = _load(VERIFY_PATH, "android_release_build_attestation_verifier")


def _pretty(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def _write_exclusive(path: Path, raw: bytes) -> None:
    if (
        not path.is_absolute() or path.exists() or path.is_symlink()
        or not path.parent.is_dir() or path.parent.is_symlink()
        or path.parent.resolve(strict=True) != path.parent
        or path.parent.stat().st_uid != os.getuid()
        or stat.S_IMODE(path.parent.stat().st_mode) & 0o077
    ):
        raise ValueError("build attestation output must be new in an owner-only directory")
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0), 0o600)
    try:
        with os.fdopen(descriptor, "wb") as output_file:
            output_file.write(raw)
            output_file.flush()
            os.fsync(output_file.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def _private_key(path: Path) -> Path:
    if not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path:
        raise ValueError("build attestation private key is not canonical")
    mode = path.stat()
    if not stat.S_ISREG(mode.st_mode) or mode.st_uid != os.getuid() or stat.S_IMODE(mode.st_mode) & 0o077:
        raise ValueError("build attestation private key is not owner-only")
    return path


def _read(path: Path, label: str, limit: int, owner_only: bool) -> bytes:
    return VERIFY._stable_bytes(path, label=label, limit=limit, owner_only=owner_only)


def _sidecar_claims(sidecar: Path, aab: Path, graph: Path) -> dict[str, str]:
    raw = _read(sidecar, "release build sidecar", 16 * 1024, True)
    try:
        lines = raw.decode("ascii").splitlines()
    except UnicodeDecodeError as error:
        raise ValueError("release build sidecar is not ASCII") from error
    expected_names = (f"artifacts/{aab.name}", f"artifacts/{graph.name}")
    claims: dict[str, str] = {}
    if len(lines) != 2:
        raise ValueError("release build sidecar must contain exactly two claims")
    for line, expected_name in zip(lines, expected_names, strict=True):
        parts = line.split("  ")
        if len(parts) != 2 or parts[1] != expected_name:
            raise ValueError("release build sidecar artifact names are not exact")
        claims[expected_name] = VERIFY._sha256(parts[0], "release build sidecar digest")
    return {"rawSha256": hashlib.sha256(raw).hexdigest(), **claims}


def _artifact_claims(
    aab: Path, graph: Path, sidecar: Path, receipt: Path, approval: Path
) -> dict[str, Any]:
    aab_raw = _read(aab, "release AAB", MAX_AAB_BYTES, False)
    graph_raw = _read(graph, "release source graph", VERIFY.MAX_AUTHORITY_BYTES, True)
    graph_value = VERIFY._strict_json(graph_raw, label="release source graph")
    if graph_value.get("contractName") != SOURCE_GRAPH_CONTRACT:
        raise ValueError("release source graph contract is not exact")
    repositories = graph_value.get("repositories")
    if not isinstance(repositories, list) or not repositories:
        raise ValueError("release source graph repository inventory is absent")
    by_name: dict[str, dict[str, Any]] = {}
    for row in repositories:
        if not isinstance(row, dict) or set(row) != {
            "name", "role", "commit", "tree", "tree_sha256", "repository"
        }:
            raise ValueError("release source graph repository binding is not exact")
        name = row.get("name")
        if not isinstance(name, str) or name in by_name:
            raise ValueError("release source graph repository inventory is ambiguous")
        VERIFY._sha40(row.get("commit"), f"{name} source commit")
        VERIFY._sha40(row.get("tree"), f"{name} source tree")
        VERIFY._sha256(row.get("tree_sha256"), f"{name} repository tree digest")
        by_name[name] = row
    if "chummer-android" not in by_name or "chummer6-design" not in by_name:
        raise ValueError("release source graph omits Android or Design authority")
    sidecar_claims = _sidecar_claims(sidecar, aab, graph)
    aab_sha = hashlib.sha256(aab_raw).hexdigest()
    graph_sha = hashlib.sha256(graph_raw).hexdigest()
    if (
        sidecar_claims[f"artifacts/{aab.name}"] != aab_sha
        or sidecar_claims[f"artifacts/{graph.name}"] != graph_sha
    ):
        raise ValueError("release build sidecar differs from exact release outputs")
    receipt_raw = _read(receipt, "two-green eligibility receipt", VERIFY.MAX_AUTHORITY_BYTES, True)
    approval_raw = _read(approval, "two-green release approval", VERIFY.MAX_APPROVAL_BYTES, True)
    return {
        "sourceCommit": by_name["chummer-android"]["commit"],
        "sourceTree": by_name["chummer-android"]["tree"],
        "designCommit": by_name["chummer6-design"]["commit"],
        "designTree": by_name["chummer6-design"]["tree"],
        "designTreeSha256": by_name["chummer6-design"]["tree_sha256"],
        "aab": {"fileName": aab.name, "sha256": aab_sha, "sizeBytes": len(aab_raw)},
        "sourceGraph": {"fileName": graph.name, "sha256": graph_sha, "sizeBytes": len(graph_raw)},
        "buildSidecar": {"fileName": sidecar.name, "sha256": sidecar_claims["rawSha256"]},
        "twoGreen": {
            "receiptSha256": hashlib.sha256(receipt_raw).hexdigest(),
            "approvalSha256": hashlib.sha256(approval_raw).hexdigest(),
        },
        "graph": graph_value,
    }


def _unsigned(claims: dict[str, Any], qualification: dict[str, Any], generated: str, nonce: str) -> dict[str, Any]:
    graph_identity = claims["graph"]["releaseIdentity"]
    return {
        "contractName": CONTRACT,
        "algorithm": "ed25519",
        "keyId": VERIFY.RELEASE_APPROVER_KEY_ID,
        "role": ROLE,
        "attestationScope": SCOPE,
        "generatedAtUtc": generated,
        "challengeNonce": VERIFY._sha256(nonce, "build attestation nonce"),
        "releaseIdentity": {
            "packageId": graph_identity["packageId"],
            "versionName": graph_identity["versionName"],
            "versionCode": graph_identity["versionCode"],
        },
        "sourceCommit": claims["sourceCommit"],
        "sourceTree": claims["sourceTree"],
        "designCommit": claims["designCommit"],
        "designTree": claims["designTree"],
        "designTreeSha256": claims["designTreeSha256"],
        "aab": claims["aab"],
        "sourceGraph": claims["sourceGraph"],
        "buildSidecar": claims["buildSidecar"],
        "twoGreen": {
            **claims["twoGreen"],
            "eligibilitySha256": qualification["eligibilitySha256"],
            "provenanceReplaySha256": qualification["protectedApproval"]["provenanceReplaySha256"],
        },
        "signingAuthorized": False,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
    }


def sign(aab: Path, graph: Path, sidecar: Path, receipt: Path, approval: Path, private_key: Path, output: Path) -> dict[str, Any]:
    claims = _artifact_claims(aab, graph, sidecar, receipt, approval)
    identity = claims["graph"]["releaseIdentity"]
    qualification = VERIFY.verify_release_eligibility(
        receipt, approval, android_root=ROOT,
        expected_version_name=identity["versionName"],
        expected_version_code=identity["versionCode"], source_graph_path=graph,
    )
    unsigned = _unsigned(
        claims, qualification,
        datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        secrets.token_hex(32),
    )
    with tempfile.TemporaryDirectory(prefix="chummer-android-build-attestation-") as directory:
        payload = Path(directory) / "payload.json"
        payload.write_bytes(VERIFY._canonical_json_bytes(unsigned))
        completed = subprocess.run(
            ["/usr/bin/openssl", "pkeyutl", "-sign", "-inkey", os.fspath(_private_key(private_key)), "-rawin", "-in", os.fspath(payload)],
            check=False, capture_output=True, timeout=20,
            env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
        )
    if completed.returncode != 0 or len(completed.stdout) != 64:
        raise ValueError("build attestation signing failed")
    attestation = {**unsigned, "signatureBase64": base64.b64encode(completed.stdout).decode("ascii")}
    _write_exclusive(output, _pretty(attestation))
    try:
        verify(output, aab, graph, sidecar, receipt, approval)
    except Exception:
        output.unlink(missing_ok=True)
        raise
    return attestation


def verify(attestation: Path, aab: Path, graph: Path, sidecar: Path, receipt: Path, approval: Path) -> dict[str, Any]:
    raw = _read(attestation, "release build attestation", VERIFY.MAX_AUTHORITY_BYTES, True)
    value = VERIFY._strict_json(raw, label="release build attestation")
    signature = value.pop("signatureBase64", None)
    required = set(_unsigned(
        {
            "sourceCommit": "0" * 40, "sourceTree": "0" * 40,
            "designCommit": "0" * 40, "designTree": "0" * 40,
            "designTreeSha256": "0" * 64, "aab": {}, "sourceGraph": {},
            "buildSidecar": {}, "twoGreen": {},
            "graph": {"releaseIdentity": {"packageId": "", "versionName": "", "versionCode": 1}},
        },
        {"eligibilitySha256": "0" * 64, "protectedApproval": {"provenanceReplaySha256": "0" * 64}},
        "1970-01-01T00:00:00Z", "0" * 64,
    ))
    if set(value) != required:
        raise ValueError("release build attestation fields are not exact")
    if value.get("contractName") != CONTRACT or value.get("role") != ROLE or value.get("attestationScope") != SCOPE:
        raise ValueError("release build attestation authority is invalid")
    if any(value.get(field) is not False for field in ("signingAuthorized", "publicationAuthorized", "googlePlayUploadAuthorized")):
        raise ValueError("release build attestation posture escalates authority")
    attestation_time = VERIFY._utc_timestamp(
        value.get("generatedAtUtc"), "release build attestation generatedAtUtc"
    )
    if attestation_time > datetime.now(UTC) + VERIFY.APPROVAL_CLOCK_SKEW:
        raise ValueError("release build attestation is dated in the future")
    VERIFY._verify_ed25519_signature(value, signature, label="release build attestation")
    claims = _artifact_claims(aab, graph, sidecar, receipt, approval)
    identity = claims["graph"]["releaseIdentity"]
    qualification = VERIFY.verify_release_eligibility(
        receipt, approval, android_root=ROOT,
        expected_version_name=identity["versionName"],
        expected_version_code=identity["versionCode"], source_graph_path=graph,
        approval_effective_time=datetime.fromisoformat(value["generatedAtUtc"].removesuffix("Z") + "+00:00"),
    )
    expected = _unsigned(claims, qualification, value["generatedAtUtc"], value["challengeNonce"])
    if value != expected or raw != _pretty({**value, "signatureBase64": signature}):
        raise ValueError("release build attestation differs from exact protected outputs")
    graph_time = datetime.fromisoformat(claims["graph"]["generatedAtUtc"].removesuffix("Z") + "+00:00")
    if graph_time > attestation_time:
        raise ValueError("release build attestation predates source graph")
    return value


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for action in ("sign", "verify"):
        child = actions.add_parser(action)
        for name in ("aab", "source-graph", "build-sidecar", "two-green-receipt", "two-green-approval"):
            child.add_argument(f"--{name}", required=True, type=Path)
        child.add_argument("--attestation" if action == "verify" else "--output", required=True, type=Path)
        if action == "sign":
            child.add_argument("--private-key", required=True, type=Path)
    args = parser.parse_args()
    try:
        common = (args.aab, args.source_graph, args.build_sidecar, args.two_green_receipt, args.two_green_approval)
        result = sign(*common, args.private_key, args.output) if args.action == "sign" else verify(args.attestation, *common)
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(json.dumps({"status": "fail", "publicationAuthorized": False, "error": str(error)}, sort_keys=True))
        return 2
    attestation_path = args.output if args.action == "sign" else args.attestation
    attestation_sha256 = hashlib.sha256(
        _read(attestation_path, "release build attestation", VERIFY.MAX_AUTHORITY_BYTES, True)
    ).hexdigest()
    print(json.dumps({"status": "pass", "publicationAuthorized": False, "attestationSha256": attestation_sha256}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
