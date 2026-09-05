#!/usr/bin/env python3
"""Create a short-lived detached approval for one exact two-green receipt.

This command belongs in the protected release-builder environment.  Its output
authorizes release preparation only; it cannot authorize AAB signing, Play
upload, tester mutation, or publication.
"""

from __future__ import annotations

import argparse
import base64
from datetime import UTC, datetime, timedelta
import hashlib
import importlib.util
import inspect
import json
import os
from pathlib import Path
import secrets
import stat
import subprocess
import tempfile


REPO_ROOT = Path(__file__).resolve().parents[1]
VERIFIER_PATH = REPO_ROOT / "scripts/verify_api36_two_green_release_eligibility.py"
TWO_GREEN_PATH = REPO_ROOT / "scripts/materialize-api36-two-green-eligibility.py"


def _load(path: Path, name: str):
    specification = importlib.util.spec_from_file_location(name, path)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"cannot load {name}")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


VERIFIER = _load(VERIFIER_PATH, "android_two_green_release_approval_verifier")
TWO_GREEN = _load(TWO_GREEN_PATH, "android_two_green_release_approval_contract")
PROVENANCE_REPLAY_CONTRACT = "chummer.android.two-green-provenance-replay/v1"
PROVENANCE_REPLAY_PATH_FIELDS = {
    "android_root",
    "policy",
    "environment_policy",
    "source_workflow",
    "review_run",
    "review_jobs",
    "review_artifacts",
    "review_aggregate_archive",
    "review_p0_archive",
    "review_pull_request",
    "review_head_pull_requests",
    "review_aggregate_check_run",
    "review_base_commit",
    "review_head_commit",
    "review_event_commit",
    "main_run",
    "main_jobs",
    "main_artifacts",
    "main_aggregate_archive",
    "main_p0_archive",
    "main_commit",
}
PROVENANCE_REPLAY_SCALAR_FIELDS = {
    "review_run_id",
    "review_pull_request_number",
    "review_event_sha",
    "main_run_id",
}


def _private_key(path: Path) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_file():
        raise ValueError("release approval private key must be an absolute regular non-symlink file")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if resolved != path or metadata.st_uid != os.getuid() or stat.S_IMODE(metadata.st_mode) & 0o077:
        raise ValueError("release approval private key must be canonical and owner-only")
    return resolved


def _write_exclusive(path: Path, raw: bytes) -> None:
    if (
        not path.is_absolute()
        or path.exists()
        or path.is_symlink()
        or not path.parent.is_dir()
        or path.parent.is_symlink()
        or path.parent.resolve(strict=True) != path.parent
        or path.parent.stat().st_uid != os.getuid()
        or stat.S_IMODE(path.parent.stat().st_mode) & 0o077
    ):
        raise ValueError("release approval output must be new in one canonical owner-only directory")
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        0o600,
    )
    try:
        with os.fdopen(descriptor, "wb") as output:
            output.write(raw)
            output.flush()
            os.fsync(output.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def _deep_provenance_replay(
    replay_path: Path,
    receipt_raw: bytes,
    receipt: dict[str, object],
) -> tuple[str, str]:
    replay_raw = VERIFIER._stable_bytes(
        replay_path,
        label="two-green authenticated provenance replay",
        limit=VERIFIER.MAX_AUTHORITY_BYTES,
        owner_only=True,
    )
    replay = VERIFIER._strict_json(
        replay_raw, label="two-green authenticated provenance replay"
    )
    expected_parameters = set(inspect.signature(TWO_GREEN.create_authority).parameters)
    expected_fields = {
        "contractName",
        *PROVENANCE_REPLAY_PATH_FIELDS,
        *PROVENANCE_REPLAY_SCALAR_FIELDS,
    }
    if set(replay) != expected_fields or expected_parameters != (
        PROVENANCE_REPLAY_PATH_FIELDS | PROVENANCE_REPLAY_SCALAR_FIELDS
    ):
        raise ValueError("two-green provenance replay fields are not exact")
    if replay.get("contractName") != PROVENANCE_REPLAY_CONTRACT:
        raise ValueError("two-green provenance replay contract is not exact")
    def replay_input_digests() -> dict[str, str]:
        return {
            name: hashlib.sha256(
                VERIFIER._stable_bytes(
                    Path(replay[name]),
                    label=f"two-green provenance replay input {name}",
                    limit=VERIFIER.MAX_AUTHORITY_BYTES,
                    owner_only=False,
                )
            ).hexdigest()
            for name in sorted(PROVENANCE_REPLAY_PATH_FIELDS - {"android_root"})
        }

    input_sha256 = replay_input_digests()
    arguments: dict[str, object] = {}
    for name in PROVENANCE_REPLAY_PATH_FIELDS:
        value = replay.get(name)
        if not isinstance(value, str):
            raise ValueError(f"two-green provenance replay path is invalid: {name}")
        arguments[name] = Path(value)
    for name in PROVENANCE_REPLAY_SCALAR_FIELDS:
        arguments[name] = replay.get(name)
    rebuilt = TWO_GREEN.create_authority(**arguments)
    if replay_input_digests() != input_sha256:
        raise ValueError("two-green provenance replay inputs changed during validation")
    if rebuilt != receipt or TWO_GREEN.pretty_json_bytes(rebuilt) != receipt_raw:
        raise ValueError(
            "two-green receipt does not replay from complete authenticated provenance"
        )
    validator_raw = VERIFIER._stable_bytes(
        TWO_GREEN_PATH,
        label="two-green deep provenance validator",
        limit=VERIFIER.MAX_AUTHORITY_BYTES,
        owner_only=False,
    )
    validator_sha256 = hashlib.sha256(validator_raw).hexdigest()
    replay_binding = {
        "contractName": PROVENANCE_REPLAY_CONTRACT,
        "validatorSha256": validator_sha256,
        "receiptSha256": hashlib.sha256(receipt_raw).hexdigest(),
        "eligibilitySha256": receipt["eligibilitySha256"],
        "reviewRunId": replay["review_run_id"],
        "reviewPullRequestNumber": replay["review_pull_request_number"],
        "reviewEventSha": replay["review_event_sha"],
        "mainRunId": replay["main_run_id"],
        "inputSha256": input_sha256,
    }
    return validator_sha256, hashlib.sha256(
        VERIFIER._canonical_json_bytes(replay_binding)
    ).hexdigest()


def sign(
    receipt_path: Path,
    provenance_replay_path: Path,
    private_key_path: Path,
    output_path: Path,
) -> dict[str, object]:
    receipt_raw = VERIFIER._stable_bytes(
        receipt_path,
        label="two-green eligibility receipt",
        limit=VERIFIER.MAX_RECEIPT_BYTES,
        owner_only=True,
    )
    receipt = VERIFIER._strict_json(receipt_raw, label="two-green eligibility receipt")
    TWO_GREEN.validate_authority(receipt)
    provenance_validator_sha256, provenance_replay_sha256 = _deep_provenance_replay(
        provenance_replay_path,
        receipt_raw,
        receipt,
    )
    generated = datetime.now(UTC).replace(microsecond=0)
    expires = generated + timedelta(hours=6)
    unsigned = VERIFIER.release_approval_unsigned(
        receipt_raw,
        receipt,
        generated_at_utc=generated.isoformat().replace("+00:00", "Z"),
        expires_at_utc=expires.isoformat().replace("+00:00", "Z"),
        challenge_nonce=secrets.token_hex(32),
        provenance_validator_sha256=provenance_validator_sha256,
        provenance_replay_sha256=provenance_replay_sha256,
    )
    private_key = _private_key(private_key_path)
    with tempfile.TemporaryDirectory(prefix="chummer-android-release-approval-sign-") as directory:
        payload = Path(directory) / "payload.json"
        payload.write_bytes(VERIFIER._canonical_json_bytes(unsigned))
        completed = subprocess.run(
            [
                os.fspath(VERIFIER.OPENSSL), "pkeyutl", "-sign", "-inkey",
                os.fspath(private_key), "-rawin", "-in", os.fspath(payload),
            ],
            check=False,
            capture_output=True,
            timeout=20,
            env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
        )
    if completed.returncode != 0 or len(completed.stdout) != 64:
        raise ValueError("release approval signing failed")
    approval = {
        **unsigned,
        "signatureBase64": base64.b64encode(completed.stdout).decode("ascii"),
    }
    raw = (json.dumps(approval, indent=2, sort_keys=True) + "\n").encode("utf-8")
    _write_exclusive(output_path, raw)
    try:
        VERIFIER._verify_release_approval(
            output_path,
            receipt_raw=receipt_raw,
            receipt=receipt,
            now=generated,
        )
    except Exception:
        output_path.unlink(missing_ok=True)
        raise
    return {
        "status": "pass",
        "releasePreparationApproved": True,
        "signingAuthorized": False,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "approvalSha256": hashlib.sha256(raw).hexdigest(),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--provenance-replay", required=True, type=Path)
    parser.add_argument("--private-key", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    try:
        result = sign(
            arguments.receipt,
            arguments.provenance_replay,
            arguments.private_key,
            arguments.output,
        )
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        result = {
            "status": "fail",
            "releasePreparationApproved": False,
            "signingAuthorized": False,
            "publicationAuthorized": False,
            "googlePlayUploadAuthorized": False,
            "failures": [str(error)],
        }
    print(json.dumps(result, sort_keys=True))
    return 0 if result["status"] == "pass" else 2


if __name__ == "__main__":
    raise SystemExit(main())
