#!/usr/bin/env python3
"""Import an EA-custodied Android signing bundle without exposing secrets."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import ssl
import stat
import subprocess
import tempfile


PACKAGE_ID = "com.myexternalbrain.chummer"
MAX_BUNDLE_BYTES = 1024 * 1024
REQUIRED_ENV_KEYS = (
    "AndroidSigningKeyStore",
    "ChummerAndroidSigningStorePass",
    "ChummerAndroidSigningKeyAlias",
    "ChummerAndroidSigningKeyPass",
    "CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH",
)


def _fingerprint_from_pem(pem: str) -> str:
    der = ssl.PEM_cert_to_DER_cert(pem)
    digest = hashlib.sha256(der).hexdigest().upper()
    return ":".join(digest[index : index + 2] for index in range(0, len(digest), 2))


def _normalize_fingerprint(value: str) -> str:
    normalized = re.sub(r"[^0-9A-Fa-f]", "", value).upper()
    if len(normalized) != 64:
        raise ValueError("expected certificate fingerprint must contain exactly 32 bytes")
    return ":".join(normalized[index : index + 2] for index in range(0, 64, 2))


def _parse_release_environment(content: str) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw_line in content.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            raise ValueError("release environment contains a malformed line")
        key, value = line.split("=", 1)
        if key not in REQUIRED_ENV_KEYS or key in values:
            raise ValueError("release environment contains an unexpected or duplicate key")
        if "\x00" in value or "\r" in value or "\n" in value:
            raise ValueError("release environment contains an invalid value")
        values[key] = value
    if tuple(sorted(values)) != tuple(sorted(REQUIRED_ENV_KEYS)):
        raise ValueError("release environment is incomplete")
    return values


def _write_private(path: Path, content: bytes) -> None:
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(descriptor, "wb") as output:
            output.write(content)
            output.flush()
            os.fsync(output.fileno())
    except Exception:
        try:
            os.close(descriptor)
        except OSError:
            pass
        raise


def _load_bundle(path: Path) -> dict[str, object]:
    metadata = path.lstat()
    if not stat.S_ISREG(metadata.st_mode) or path.is_symlink():
        raise ValueError("recovery bundle must be a regular, non-symlinked file")
    if metadata.st_size <= 0 or metadata.st_size > MAX_BUNDLE_BYTES:
        raise ValueError("recovery bundle size is outside the accepted bounds")
    if metadata.st_mode & 0o077:
        raise ValueError("recovery bundle must use owner-only permissions")
    loaded = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(loaded, dict):
        raise ValueError("recovery bundle must be a JSON object")
    return loaded


def import_bundle(bundle_path: Path, target_dir: Path, expected_fingerprint: str, keytool: Path) -> dict[str, str]:
    repo_root = Path(__file__).resolve().parent.parent
    resolved_target = target_dir.resolve(strict=False)
    if resolved_target == repo_root or repo_root in resolved_target.parents:
        raise ValueError("signing material must be stored outside the Chummer Android repository")
    if target_dir.exists() or target_dir.is_symlink():
        raise FileExistsError("refusing to replace existing signing material")
    if not keytool.is_file() or not os.access(keytool, os.X_OK):
        raise ValueError("keytool must be an executable file")

    bundle = _load_bundle(bundle_path)
    if bundle.get("schema_version") != 1 or bundle.get("package_id") != PACKAGE_ID:
        raise ValueError("recovery bundle identity is not supported")
    if bundle.get("keystore_type") != "PKCS12":
        raise ValueError("recovery bundle keystore type is not supported")

    keystore = base64.b64decode(str(bundle.get("keystore_base64") or ""), validate=True)
    certificate_pem = str(bundle.get("certificate_pem") or "")
    release_values = _parse_release_environment(str(bundle.get("release_environment") or ""))
    certificate_fingerprint = _fingerprint_from_pem(certificate_pem)
    expected = _normalize_fingerprint(expected_fingerprint)
    recorded = _normalize_fingerprint(str(bundle.get("certificate_sha256_fingerprint") or ""))
    if certificate_fingerprint != expected or recorded != expected:
        raise ValueError("recovery certificate does not match the expected Play upload certificate")
    if hashlib.sha256(keystore).hexdigest() != str(bundle.get("keystore_sha256") or "").lower():
        raise ValueError("recovery keystore digest does not match its custody record")
    if hashlib.sha256(certificate_pem.encode("utf-8")).hexdigest() != str(
        bundle.get("certificate_sha256") or ""
    ).lower():
        raise ValueError("recovery certificate digest does not match its custody record")

    resolved_target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    temporary_dir = Path(
        tempfile.mkdtemp(prefix=f".{resolved_target.name}.import-", dir=resolved_target.parent)
    )
    os.chmod(temporary_dir, 0o700)
    try:
        keystore_path = temporary_dir / "chummer-upload.p12"
        certificate_path = temporary_dir / "chummer-upload-cert.pem"
        environment_path = temporary_dir / "android-release.env"
        _write_private(keystore_path, keystore)
        _write_private(certificate_path, certificate_pem.encode("utf-8"))

        validation_environment = os.environ.copy()
        validation_environment["CHUMMER_RECOVERY_STORE_PASSWORD"] = release_values[
            "ChummerAndroidSigningStorePass"
        ]
        exported = subprocess.run(
            [
                str(keytool),
                "-exportcert",
                "-rfc",
                "-keystore",
                str(keystore_path),
                "-storetype",
                "PKCS12",
                "-storepass:env",
                "CHUMMER_RECOVERY_STORE_PASSWORD",
                "-alias",
                release_values["ChummerAndroidSigningKeyAlias"],
            ],
            check=False,
            capture_output=True,
            text=True,
            env=validation_environment,
        )
        if exported.returncode != 0:
            raise ValueError("keytool could not open the recovered keystore")
        if _fingerprint_from_pem(exported.stdout) != expected:
            raise ValueError("recovered keystore does not contain the expected upload certificate")

        final_keystore = resolved_target / keystore_path.name
        final_certificate = resolved_target / certificate_path.name
        environment_content = "\n".join(
            (
                f"AndroidSigningKeyStore={shlex.quote(str(final_keystore))}",
                "ChummerAndroidSigningStorePass="
                + shlex.quote(release_values["ChummerAndroidSigningStorePass"]),
                "ChummerAndroidSigningKeyAlias="
                + shlex.quote(release_values["ChummerAndroidSigningKeyAlias"]),
                "ChummerAndroidSigningKeyPass="
                + shlex.quote(release_values["ChummerAndroidSigningKeyPass"]),
                f"CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH={shlex.quote(str(final_certificate))}",
                "",
            )
        )
        _write_private(environment_path, environment_content.encode("utf-8"))
        os.rename(temporary_dir, resolved_target)
    except Exception:
        shutil.rmtree(temporary_dir, ignore_errors=True)
        raise

    return {
        "status": "imported",
        "package_id": PACKAGE_ID,
        "certificate_sha256_fingerprint": expected,
        "signing_dir": str(resolved_target),
        "release_environment": str(resolved_target / "android-release.env"),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--target-dir", type=Path, required=True)
    parser.add_argument("--expected-certificate-sha256", required=True)
    parser.add_argument("--keytool", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    result = import_bundle(
        args.bundle,
        args.target_dir,
        args.expected_certificate_sha256,
        args.keytool,
    )
    print(json.dumps(result, indent=2, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
