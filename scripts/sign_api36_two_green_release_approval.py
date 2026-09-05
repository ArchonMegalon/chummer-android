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
import importlib.util
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


def sign(receipt_path: Path, private_key_path: Path, output_path: Path) -> dict[str, object]:
    receipt_raw = VERIFIER._stable_bytes(
        receipt_path,
        label="two-green eligibility receipt",
        limit=VERIFIER.MAX_RECEIPT_BYTES,
        owner_only=True,
    )
    receipt = VERIFIER._strict_json(receipt_raw, label="two-green eligibility receipt")
    TWO_GREEN.validate_authority(receipt)
    generated = datetime.now(UTC).replace(microsecond=0)
    expires = generated + timedelta(hours=6)
    unsigned = VERIFIER.release_approval_unsigned(
        receipt_raw,
        receipt,
        generated_at_utc=generated.isoformat().replace("+00:00", "Z"),
        expires_at_utc=expires.isoformat().replace("+00:00", "Z"),
        challenge_nonce=secrets.token_hex(32),
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
        "approval": os.fspath(output_path),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--private-key", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    try:
        result = sign(arguments.receipt, arguments.private_key, arguments.output)
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
