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
KEY_HYGIENE_PATH = ROOT / "scripts/verify_release_private_key_hygiene.py"
APPROVAL_SIGNER_PATH = ROOT / "scripts/sign_api36_two_green_release_approval.py"
CONTRACT = "chummer.android.release-build-attestation/v2"
SCOPE = "android_internal_release_artifact_binding"
ROLE = "android_internal_release_builder"
SOURCE_GRAPH_CONTRACT = "chummer.android.release-source-graph/v3"
VALIDATION_CONTRACT = "chummer.android.protected-release-build-validation/v1"
JAVA_TOOLCHAIN_CONTRACT = "chummer.android.trusted-release-toolchain/v1"
JAVA_TOOLCHAIN_ROLE = "android_release_toolchain_approver"
JAVA_TOOLCHAIN_SCOPE = "android_release_toolchain"
MAX_AAB_BYTES = 512 * 1024 * 1024
EXPECTED_BUNDLETOOL_SHA256 = "a099cfa1543f55593bc2ed16a70a7c67fe54b1747bb7301f37fdfd6d91028e29"
EXPECTED_UPLOAD_CERTIFICATE_SHA256 = "D9:C4:B6:35:12:15:44:D5:52:2A:BF:1E:C2:DF:DA:3C:19:38:AA:B9:3D:67:26:BB:93:C9:87:1E:C9:ED:1D:15"
REVISION_BY_REPOSITORY = {
    "chummer-android": "CHUMMER_ANDROID_REVISION",
    "chummer6-ui": "CHUMMER_PRESENTATION_REVISION",
    "chummer6-core": "CHUMMER_CORE_ENGINE_REVISION",
    "chummer6-ui-kit": "CHUMMER_UI_KIT_REVISION",
    "chummer6-hub": "CHUMMER_RUN_SERVICES_REVISION",
    "chummer6-hub-registry": "CHUMMER_HUB_REGISTRY_REVISION",
    "chummer6-media-factory": "CHUMMER_MEDIA_FACTORY_REVISION",
    "chummer6-design": "CHUMMER_DESIGN_REVISION",
}


def _load(path: Path, name: str) -> Any:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError(f"cannot load {name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


VERIFY = _load(VERIFY_PATH, "android_release_build_attestation_verifier")
KEY_HYGIENE = _load(KEY_HYGIENE_PATH, "android_release_private_key_hygiene")
APPROVAL_SIGNER = _load(
    APPROVAL_SIGNER_PATH, "android_release_build_authenticated_provenance"
)


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
    KEY_HYGIENE.verify(ROOT)
    return KEY_HYGIENE.private_key(path, ROOT, "build attestation private key")


def _canonical_file(path: Path, label: str, *, owner_only: bool) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_file():
        raise ValueError(f"{label} must be one absolute regular file")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if resolved != path or metadata.st_uid != os.getuid():
        raise ValueError(f"{label} must be canonical and owner-owned")
    if owner_only and stat.S_IMODE(metadata.st_mode) & 0o077:
        raise ValueError(f"{label} must be owner-only")
    return resolved


def _canonical_directory(path: Path, label: str, *, owner_only: bool) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be one absolute directory")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if resolved != path or metadata.st_uid != os.getuid():
        raise ValueError(f"{label} must be canonical and owner-owned")
    if owner_only and stat.S_IMODE(metadata.st_mode) & 0o077:
        raise ValueError(f"{label} must be owner-only")
    return resolved


def _trusted_tool(path: Path, root: Path, label: str) -> Path:
    if (
        not path.is_absolute()
        or path.is_symlink()
        or not path.is_file()
        or path.resolve(strict=True) != path
        or not path.is_relative_to(root)
    ):
        raise ValueError(f"{label} is not one canonical tool below the trusted Java root")
    metadata = path.stat()
    if metadata.st_uid != 0 or stat.S_IMODE(metadata.st_mode) & 0o022:
        raise ValueError(f"{label} must be root-owned and not writable by another account")
    if not os.access(path, os.X_OK):
        raise ValueError(f"{label} is not executable")
    return path


def _trusted_tool_root(path: Path, label: str) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be one absolute canonical directory")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if (
        resolved != path
        or resolved.is_relative_to(ROOT)
        or metadata.st_uid != 0
        or stat.S_IMODE(metadata.st_mode) & 0o022
    ):
        raise ValueError(
            f"{label} must be root-owned, outside the repository, and not writable by another account"
        )
    return resolved


def _java_version_digest(java: Path) -> str:
    completed = subprocess.run(
        [os.fspath(java), "-version"],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        timeout=20,
        env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
    )
    if completed.returncode != 0 or not completed.stdout or len(completed.stdout) > 64 * 1024:
        raise ValueError("trusted Java version probe failed")
    return hashlib.sha256(completed.stdout).hexdigest()


def _dotnet_version_digest(dotnet: Path) -> str:
    completed = subprocess.run(
        [os.fspath(dotnet), "--info"],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        timeout=30,
        env={
            "PATH": "/usr/bin:/bin",
            "LANG": "C",
            "LC_ALL": "C",
            "DOTNET_CLI_HOME": "/tmp",
            "DOTNET_NOLOGO": "1",
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        },
    )
    if completed.returncode != 0 or not completed.stdout or len(completed.stdout) > 256 * 1024:
        raise ValueError("trusted dotnet version probe failed")
    return hashlib.sha256(completed.stdout).hexdigest()


def _java_toolchain_unsigned(java_sdk: Path, dotnet: Path) -> dict[str, Any]:
    java_sdk = _trusted_tool_root(java_sdk, "trusted Java SDK")
    _trusted_tool_root(java_sdk / "bin", "trusted Java binary directory")
    tools = {
        name: _trusted_tool(java_sdk / "bin" / name, java_sdk, f"trusted Java {name}")
        for name in ("java", "javac", "jarsigner", "keytool")
    }
    dotnet_root = _trusted_tool_root(dotnet.parent, "trusted dotnet root")
    dotnet = _trusted_tool(dotnet, dotnet_root, "trusted dotnet")
    dotnet_claim = {
        "absolutePath": os.fspath(dotnet),
        "sha256": _sha256_file(dotnet, "trusted dotnet", 256 * 1024 * 1024),
        "sizeBytes": dotnet.stat().st_size,
        "versionOutputSha256": _dotnet_version_digest(dotnet),
    }
    return {
        "contractName": JAVA_TOOLCHAIN_CONTRACT,
        "algorithm": "ed25519",
        "keyId": VERIFY.RELEASE_APPROVER_KEY_ID,
        "role": JAVA_TOOLCHAIN_ROLE,
        "authorityScope": JAVA_TOOLCHAIN_SCOPE,
        "javaSdkRoot": os.fspath(java_sdk),
        "javaVersionOutputSha256": _java_version_digest(tools["java"]),
        "tools": {
            name: {
                "relativePath": f"bin/{name}",
                "sha256": _sha256_file(path, f"trusted Java {name}", 128 * 1024 * 1024),
                "sizeBytes": path.stat().st_size,
            }
            for name, path in tools.items()
        },
        "dotnet": dotnet_claim,
        "publicationAuthorized": False,
    }


def sign_java_toolchain_authority(
    java_sdk: Path,
    dotnet: Path,
    private_key: Path,
    output: Path,
) -> dict[str, Any]:
    unsigned = _java_toolchain_unsigned(java_sdk, dotnet)
    with tempfile.TemporaryDirectory(prefix="chummer-android-java-toolchain-authority-") as directory:
        payload = Path(directory) / "payload.json"
        payload.write_bytes(VERIFY._canonical_json_bytes(unsigned))
        completed = subprocess.run(
            [
                "/usr/bin/openssl", "pkeyutl", "-sign",
                "-inkey", os.fspath(_private_key(private_key)), "-rawin",
                "-in", os.fspath(payload),
            ],
            check=False,
            capture_output=True,
            timeout=20,
            env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
        )
    if completed.returncode != 0 or len(completed.stdout) != 64:
        raise ValueError("trusted Java toolchain authority signing failed")
    authority = {
        **unsigned,
        "signatureBase64": base64.b64encode(completed.stdout).decode("ascii"),
    }
    _write_exclusive(output, _pretty(authority))
    try:
        _load_trusted_java_toolchain(output)
    except Exception:
        output.unlink(missing_ok=True)
        raise
    return authority


def _load_trusted_java_toolchain(authority_path: Path) -> dict[str, Any]:
    authority_path = _canonical_file(
        authority_path, "trusted Java toolchain authority", owner_only=True
    )
    if authority_path.is_relative_to(ROOT):
        raise ValueError("trusted Java toolchain authority must be outside the repository")
    raw = _read(
        authority_path,
        "trusted Java toolchain authority",
        VERIFY.MAX_APPROVAL_BYTES,
        True,
    )
    value = VERIFY._strict_json(raw, label="trusted Java toolchain authority")
    signature = value.pop("signatureBase64", None)
    expected_fields = {
        "contractName", "algorithm", "keyId", "role", "authorityScope",
        "javaSdkRoot", "javaVersionOutputSha256", "tools", "dotnet",
        "publicationAuthorized",
    }
    if set(value) != expected_fields or (
        value.get("contractName") != JAVA_TOOLCHAIN_CONTRACT
        or value.get("algorithm") != "ed25519"
        or value.get("keyId") != VERIFY.RELEASE_APPROVER_KEY_ID
        or value.get("role") != JAVA_TOOLCHAIN_ROLE
        or value.get("authorityScope") != JAVA_TOOLCHAIN_SCOPE
        or value.get("publicationAuthorized") is not False
    ):
        raise ValueError("trusted Java toolchain authority fields are not exact")
    VERIFY._sha256(
        value.get("javaVersionOutputSha256"), "trusted Java version output digest"
    )
    VERIFY._verify_ed25519_signature(value, signature, label="trusted Java toolchain authority")
    if raw != _pretty({**value, "signatureBase64": signature}):
        raise ValueError("trusted Java toolchain authority is not canonical")

    java_sdk_value = value.get("javaSdkRoot")
    if not isinstance(java_sdk_value, str):
        raise ValueError("trusted Java SDK root is absent")
    java_sdk = _trusted_tool_root(Path(java_sdk_value), "trusted Java SDK")
    _trusted_tool_root(java_sdk / "bin", "trusted Java binary directory")
    tool_claims = value.get("tools")
    if not isinstance(tool_claims, dict) or set(tool_claims) != {
        "java", "javac", "jarsigner", "keytool"
    }:
        raise ValueError("trusted Java tool inventory is not exact")
    tools: dict[str, Path] = {}
    for name, claim in tool_claims.items():
        if not isinstance(claim, dict) or set(claim) != {"relativePath", "sha256", "sizeBytes"}:
            raise ValueError("trusted Java tool claim is not exact")
        if claim.get("relativePath") != f"bin/{name}":
            raise ValueError("trusted Java tool path differs")
        tool = _trusted_tool(java_sdk / "bin" / name, java_sdk, f"trusted Java {name}")
        if (
            not isinstance(claim.get("sizeBytes"), int)
            or isinstance(claim.get("sizeBytes"), bool)
            or claim["sizeBytes"] <= 0
            or tool.stat().st_size != claim["sizeBytes"]
            or _sha256_file(tool, f"trusted Java {name}", 128 * 1024 * 1024)
            != VERIFY._sha256(claim.get("sha256"), f"trusted Java {name} digest")
        ):
            raise ValueError("trusted Java tool bytes differ")
        tools[name] = tool
    if _java_version_digest(tools["java"]) != value["javaVersionOutputSha256"]:
        raise ValueError("trusted Java version output differs")
    dotnet_claim = value.get("dotnet")
    if not isinstance(dotnet_claim, dict) or set(dotnet_claim) != {
        "absolutePath", "sha256", "sizeBytes", "versionOutputSha256"
    }:
        raise ValueError("trusted dotnet claim is not exact")
    dotnet_value = dotnet_claim.get("absolutePath")
    if not isinstance(dotnet_value, str):
        raise ValueError("trusted dotnet path is absent")
    dotnet_root = _trusted_tool_root(Path(dotnet_value).parent, "trusted dotnet root")
    dotnet = _trusted_tool(Path(dotnet_value), dotnet_root, "trusted dotnet")
    if (
        not isinstance(dotnet_claim.get("sizeBytes"), int)
        or isinstance(dotnet_claim.get("sizeBytes"), bool)
        or dotnet_claim["sizeBytes"] <= 0
        or dotnet.stat().st_size != dotnet_claim["sizeBytes"]
        or _sha256_file(dotnet, "trusted dotnet", 256 * 1024 * 1024)
        != VERIFY._sha256(dotnet_claim.get("sha256"), "trusted dotnet digest")
        or _dotnet_version_digest(dotnet)
        != VERIFY._sha256(
            dotnet_claim.get("versionOutputSha256"), "trusted dotnet version digest"
        )
    ):
        raise ValueError("trusted dotnet bytes or version differ")
    return {
        "authoritySha256": hashlib.sha256(raw).hexdigest(),
        "javaSdkRoot": java_sdk,
        "javaVersionOutputSha256": value["javaVersionOutputSha256"],
        "toolSha256": {name: tool_claims[name]["sha256"] for name in sorted(tool_claims)},
        "tools": tools,
        "dotnet": dotnet,
        "dotnetSha256": dotnet_claim["sha256"],
        "dotnetVersionOutputSha256": dotnet_claim["versionOutputSha256"],
    }


def _sha256_file(path: Path, label: str, limit: int) -> str:
    return hashlib.sha256(_read(path, label, limit, False)).hexdigest()


def _lease_identity(metadata: os.stat_result) -> tuple[int, ...]:
    return (
        metadata.st_dev,
        metadata.st_ino,
        metadata.st_mode,
        metadata.st_uid,
        metadata.st_gid,
        metadata.st_nlink,
        metadata.st_size,
        metadata.st_mtime_ns,
        metadata.st_ctime_ns,
    )


def _lease(path: Path, expected_sha256: str, limit: int, label: str) -> dict[str, Any]:
    descriptor = os.open(
        path,
        os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
    )
    try:
        before = os.fstat(descriptor)
        path_before = os.stat(path, follow_symlinks=False)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_size <= 0
            or before.st_size > limit
            or _lease_identity(before) != _lease_identity(path_before)
        ):
            raise ValueError(f"{label} cannot be held as one trusted file")
        digest = hashlib.sha256()
        observed = 0
        while chunk := os.read(descriptor, 1024 * 1024):
            observed += len(chunk)
            if observed > limit:
                raise ValueError(f"{label} exceeds its trusted bound")
            digest.update(chunk)
        if observed != before.st_size or digest.hexdigest() != expected_sha256:
            raise ValueError(f"{label} bytes differ before protected validation")
        os.lseek(descriptor, 0, os.SEEK_SET)
        return {
            "descriptor": descriptor,
            "path": path,
            "identity": _lease_identity(before),
            "sha256": expected_sha256,
            "limit": limit,
            "label": label,
        }
    except Exception:
        os.close(descriptor)
        raise


def _close_leases(leases: list[dict[str, Any]], *, verify: bool) -> None:
    failure: ValueError | None = None
    for lease in reversed(leases):
        descriptor = lease["descriptor"]
        try:
            if verify:
                metadata = os.fstat(descriptor)
                path_metadata = os.stat(lease["path"], follow_symlinks=False)
                os.lseek(descriptor, 0, os.SEEK_SET)
                digest = hashlib.sha256()
                observed = 0
                while chunk := os.read(descriptor, 1024 * 1024):
                    observed += len(chunk)
                    if observed > lease["limit"]:
                        raise ValueError(f"{lease['label']} changed during protected validation")
                    digest.update(chunk)
                if (
                    _lease_identity(metadata) != lease["identity"]
                    or _lease_identity(path_metadata) != lease["identity"]
                    or observed != metadata.st_size
                    or digest.hexdigest() != lease["sha256"]
                ):
                    raise ValueError(f"{lease['label']} changed during protected validation")
        except (OSError, ValueError) as error:
            if failure is None:
                failure = ValueError(str(error))
        finally:
            os.close(descriptor)
    if failure is not None:
        raise failure


def _run_validator(arguments: list[str], environment: dict[str, str], label: str, timeout: int) -> str:
    completed = subprocess.run(
        arguments,
        check=False,
        capture_output=True,
        timeout=timeout,
        env=environment,
        text=True,
    )
    if completed.returncode != 0:
        raise ValueError(f"protected {label} failed")
    return hashlib.sha256(completed.stdout.encode("utf-8")).hexdigest()


def _protected_validation_inputs(
    claims: dict[str, Any],
    aab: Path,
    graph: Path,
    receipt: Path,
    approval: Path,
    *,
    workspace_root: Path,
    package_authority: Path,
    authority_root: Path,
    bundletool: Path,
    upload_certificate: Path,
    java_tool_authority: Path,
) -> dict[str, Any]:
    workspace_root = _canonical_directory(workspace_root, "release workspace", owner_only=False)
    if ROOT.resolve(strict=True) != (workspace_root / "chummer-android").resolve(strict=True):
        raise ValueError("protected build attester is not running in the canonical Android workspace")
    authority_root = _canonical_directory(authority_root, "package authority root", owner_only=False)
    package_authority = _canonical_file(package_authority, "release package authority", owner_only=True)
    bundletool = _canonical_file(bundletool, "bundletool", owner_only=True)
    upload_certificate = _canonical_file(upload_certificate, "upload certificate", owner_only=True)
    trusted_java = _load_trusted_java_toolchain(java_tool_authority)
    tools = trusted_java["tools"]
    bundletool_sha = _sha256_file(bundletool, "bundletool", 64 * 1024 * 1024)
    if bundletool_sha != EXPECTED_BUNDLETOOL_SHA256:
        raise ValueError("protected build attester bundletool digest differs")
    upload_certificate_file_sha = _sha256_file(
        upload_certificate, "upload certificate", 1024 * 1024
    )
    leases = [
        _lease(bundletool, bundletool_sha, 64 * 1024 * 1024, "bundletool"),
        _lease(
            upload_certificate,
            upload_certificate_file_sha,
            1024 * 1024,
            "upload certificate",
        ),
        *(
            _lease(
                tools[name],
                trusted_java["toolSha256"][name],
                128 * 1024 * 1024,
                f"trusted Java {name}",
            )
            for name in ("java", "javac", "jarsigner", "keytool")
        ),
        _lease(
            trusted_java["dotnet"],
            trusted_java["dotnetSha256"],
            256 * 1024 * 1024,
            "trusted dotnet",
        ),
    ]
    certificate_result = subprocess.run(
        ["/usr/bin/openssl", "x509", "-in", os.fspath(upload_certificate), "-noout", "-fingerprint", "-sha256"],
        check=False,
        capture_output=True,
        timeout=20,
        env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
        text=True,
    )
    if certificate_result.returncode != 0:
        raise ValueError("protected build attester cannot inspect the upload certificate")
    certificate_sha = certificate_result.stdout.strip().removeprefix(
        "sha256 Fingerprint="
    ).removeprefix("SHA256 Fingerprint=")
    if certificate_sha != EXPECTED_UPLOAD_CERTIFICATE_SHA256:
        raise ValueError("protected build attester upload certificate differs")

    graph_rows = claims["graph"].get("repositories")
    if not isinstance(graph_rows, list):
        raise ValueError("release source graph repository inventory is absent")
    revisions: dict[str, str] = {}
    for row in graph_rows:
        if not isinstance(row, dict) or row.get("name") not in REVISION_BY_REPOSITORY:
            raise ValueError("release source graph repository inventory is not closed")
        revisions[REVISION_BY_REPOSITORY[row["name"]]] = VERIFY._sha40(
            row.get("commit"), f"{row.get('name')} source commit"
        )
    if set(revisions) != set(REVISION_BY_REPOSITORY.values()):
        raise ValueError("release source graph repository inventory is incomplete")
    base_environment = {
        "PATH": "/usr/bin:/bin",
        "LANG": "C",
        "LC_ALL": "C",
        "CHUMMER_BUNDLETOOL_JAR": os.fspath(bundletool),
        "CHUMMER_JAVA": os.fspath(tools["java"]),
        "CHUMMER_JARSIGNER": os.fspath(tools["jarsigner"]),
        "CHUMMER_KEYTOOL": os.fspath(tools["keytool"]),
        "CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH": os.fspath(upload_certificate),
    }
    aab_validation = _run_validator(
        [os.fspath(ROOT / "scripts/validate-aab.sh"), os.fspath(aab)],
        base_environment,
        "AAB structure/signature/version/ABI/proof validation",
        240,
    )
    hygiene_validation = _run_validator(
        [
            "/usr/bin/python3",
            os.fspath(ROOT / "scripts/verify_release_artifact_hygiene.py"),
            "--aab", os.fspath(aab),
            "--forbidden-path", os.fspath(receipt),
            "--forbidden-path", os.fspath(approval),
        ],
        {"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
        "protected-input artifact hygiene",
        120,
    )
    identity = claims["graph"]["releaseIdentity"]
    source_environment = {
        "PATH": "/usr/bin:/bin",
        "LANG": "C",
        "LC_ALL": "C",
        **revisions,
    }
    source_validation = _run_validator(
        [
            "/usr/bin/python3",
            os.fspath(ROOT / "scripts/verify_release_source_graph.py"),
            "--android-root", os.fspath(ROOT),
            "--workspace-root", os.fspath(workspace_root),
            "--package-authority", os.fspath(package_authority),
            "--authority-root", os.fspath(authority_root),
            "--expected-version-name", str(identity["versionName"]),
            "--expected-version-code", str(identity["versionCode"]),
            "--verify-existing", os.fspath(graph),
        ],
        source_environment,
        "canonical clean source graph",
        120,
    )
    validator_paths = (
        ROOT / "scripts/validate-aab.sh",
        ROOT / "scripts/inspect_aab.py",
        ROOT / "scripts/verify_release_aab_excludes_api36_proof.py",
        ROOT / "scripts/verify_release_artifact_hygiene.py",
        ROOT / "scripts/verify_release_source_graph.py",
    )
    result = {
        "contractName": VALIDATION_CONTRACT,
        "status": "pass",
        "bundletoolSha256": bundletool_sha,
        "uploadCertificateSha256": certificate_sha,
        "uploadCertificateFileSha256": upload_certificate_file_sha,
        "javaToolAuthoritySha256": trusted_java["authoritySha256"],
        "javaVersionOutputSha256": trusted_java["javaVersionOutputSha256"],
        "javaToolSha256": trusted_java["toolSha256"],
        "dotnetSha256": trusted_java["dotnetSha256"],
        "dotnetVersionOutputSha256": trusted_java["dotnetVersionOutputSha256"],
        "aabValidationOutputSha256": aab_validation,
        "artifactHygieneOutputSha256": hygiene_validation,
        "sourceGraphValidationOutputSha256": source_validation,
        "validatorSha256": {
            path.name: _sha256_file(path, f"{path.name} validator", 8 * 1024 * 1024)
            for path in validator_paths
        },
        "publicationAuthorized": False,
    }
    _close_leases(leases, verify=True)
    return result


def _protected_validation(
    claims: dict[str, Any],
    aab: Path,
    graph: Path,
    receipt: Path,
    approval: Path,
    **inputs: Any,
) -> dict[str, Any]:
    # Read each candidate exactly once through the stable-file verifier and run
    # every external validator against owner-only copies of those exact bytes.
    # A caller cannot swap a benign AAB/graph in for validation and restore
    # different bytes before the detached attestation is written.
    aab_raw = _read(aab, "protected validation AAB", MAX_AAB_BYTES, False)
    graph_raw = _read(
        graph,
        "protected validation source graph",
        VERIFY.MAX_AUTHORITY_BYTES,
        True,
    )
    if (
        hashlib.sha256(aab_raw).hexdigest() != claims["aab"]["sha256"]
        or hashlib.sha256(graph_raw).hexdigest() != claims["sourceGraph"]["sha256"]
    ):
        raise ValueError("protected validation inputs changed before validation")
    with tempfile.TemporaryDirectory(prefix="chummer-android-protected-build-validation-") as directory:
        root = Path(directory)
        captured_aab = root / aab.name
        captured_graph = root / graph.name
        captured_aab.write_bytes(aab_raw)
        captured_graph.write_bytes(graph_raw)
        captured_aab.chmod(0o400)
        captured_graph.chmod(0o400)
        return _protected_validation_inputs(
            claims,
            captured_aab,
            captured_graph,
            receipt,
            approval,
            **inputs,
        )


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


def _validate_validation_claims(value: object) -> dict[str, Any]:
    validator_names = {
        "validate-aab.sh",
        "inspect_aab.py",
        "verify_release_aab_excludes_api36_proof.py",
        "verify_release_artifact_hygiene.py",
        "verify_release_source_graph.py",
    }
    expected = {
        "contractName", "status", "bundletoolSha256", "uploadCertificateSha256",
        "uploadCertificateFileSha256",
        "javaToolAuthoritySha256", "javaVersionOutputSha256", "javaToolSha256",
        "dotnetSha256", "dotnetVersionOutputSha256",
        "aabValidationOutputSha256", "artifactHygieneOutputSha256",
        "sourceGraphValidationOutputSha256", "validatorSha256",
        "publicationAuthorized",
    }
    if not isinstance(value, dict) or set(value) != expected:
        raise ValueError("protected build validation fields are not exact")
    if (
        value.get("contractName") != VALIDATION_CONTRACT
        or value.get("status") != "pass"
        or value.get("publicationAuthorized") is not False
        or value.get("bundletoolSha256") != EXPECTED_BUNDLETOOL_SHA256
        or value.get("uploadCertificateSha256") != EXPECTED_UPLOAD_CERTIFICATE_SHA256
    ):
        raise ValueError("protected build validation authority is invalid")
    VERIFY._sha256(value.get("javaToolAuthoritySha256"), "trusted Java authority digest")
    VERIFY._sha256(
        value.get("uploadCertificateFileSha256"), "upload certificate file digest"
    )
    VERIFY._sha256(value.get("javaVersionOutputSha256"), "trusted Java version digest")
    VERIFY._sha256(value.get("dotnetSha256"), "trusted dotnet digest")
    VERIFY._sha256(value.get("dotnetVersionOutputSha256"), "trusted dotnet version digest")
    java_tools = value.get("javaToolSha256")
    if not isinstance(java_tools, dict) or set(java_tools) != {
        "java", "javac", "jarsigner", "keytool"
    }:
        raise ValueError("protected build Java tool inventory is not exact")
    for name, digest in java_tools.items():
        VERIFY._sha256(digest, f"protected build Java {name} digest")
    for name in (
        "aabValidationOutputSha256",
        "artifactHygieneOutputSha256",
        "sourceGraphValidationOutputSha256",
    ):
        VERIFY._sha256(value.get(name), f"protected build validation {name}")
    validators = value.get("validatorSha256")
    if not isinstance(validators, dict) or set(validators) != validator_names:
        raise ValueError("protected build validator inventory is not exact")
    for name, digest in validators.items():
        path = ROOT / "scripts" / name
        current = _sha256_file(path, f"{name} validator", 8 * 1024 * 1024)
        if VERIFY._sha256(digest, f"{name} validator digest") != current:
            raise ValueError("protected build validator source changed")
    return value


def _unsigned(
    claims: dict[str, Any],
    qualification: dict[str, Any],
    validation: dict[str, Any],
    generated: str,
    nonce: str,
) -> dict[str, Any]:
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
        "protectedValidation": validation,
        "signingAuthorized": False,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
    }


def sign(
    aab: Path,
    graph: Path,
    sidecar: Path,
    receipt: Path,
    approval: Path,
    private_key: Path,
    output: Path,
    github_token_file: Path,
    *,
    workspace_root: Path | None = None,
    package_authority: Path | None = None,
    authority_root: Path | None = None,
    bundletool: Path | None = None,
    upload_certificate: Path | None = None,
    java_tool_authority: Path | None = None,
) -> dict[str, Any]:
    claims = _artifact_claims(aab, graph, sidecar, receipt, approval)
    identity = claims["graph"]["releaseIdentity"]
    qualification = VERIFY.verify_release_eligibility(
        receipt, approval, android_root=ROOT,
        expected_version_name=identity["versionName"],
        expected_version_code=identity["versionCode"], source_graph_path=graph,
    )
    receipt_raw = _read(
        receipt, "two-green eligibility receipt", VERIFY.MAX_RECEIPT_BYTES, True
    )
    receipt_value = VERIFY._strict_json(
        receipt_raw, label="two-green eligibility receipt"
    )
    provenance_validator_sha256, provenance_replay_sha256 = (
        APPROVAL_SIGNER._authenticated_github_replay(
            receipt_raw, receipt_value, github_token_file
        )
    )
    protected_approval = qualification.get("protectedApproval")
    if (
        not isinstance(protected_approval, dict)
        or protected_approval.get("provenanceValidatorSha256")
        != provenance_validator_sha256
        or protected_approval.get("provenanceReplaySha256")
        != provenance_replay_sha256
    ):
        raise ValueError("build attestation authenticated provenance differs from protected approval")
    required = {
        "workspace_root": workspace_root,
        "package_authority": package_authority,
        "authority_root": authority_root,
        "bundletool": bundletool,
        "upload_certificate": upload_certificate,
        "java_tool_authority": java_tool_authority,
    }
    if any(value is None for value in required.values()):
        raise ValueError("protected build validation inputs are incomplete")
    validation = _protected_validation(
        claims,
        aab,
        graph,
        receipt,
        approval,
        **required,
    )
    validation = _validate_validation_claims(validation)
    unsigned = _unsigned(
        claims, qualification, validation,
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
        {
            "contractName": VALIDATION_CONTRACT,
            "status": "pass",
            "bundletoolSha256": EXPECTED_BUNDLETOOL_SHA256,
            "uploadCertificateSha256": EXPECTED_UPLOAD_CERTIFICATE_SHA256,
            "uploadCertificateFileSha256": "0" * 64,
            "javaToolAuthoritySha256": "0" * 64,
            "javaVersionOutputSha256": "0" * 64,
            "javaToolSha256": {
                "java": "0" * 64,
                "javac": "0" * 64,
                "jarsigner": "0" * 64,
                "keytool": "0" * 64,
            },
            "dotnetSha256": "0" * 64,
            "dotnetVersionOutputSha256": "0" * 64,
            "aabValidationOutputSha256": "0" * 64,
            "artifactHygieneOutputSha256": "0" * 64,
            "sourceGraphValidationOutputSha256": "0" * 64,
            "validatorSha256": {
                name: "0" * 64 for name in (
                    "validate-aab.sh", "inspect_aab.py",
                    "verify_release_aab_excludes_api36_proof.py",
                    "verify_release_artifact_hygiene.py",
                    "verify_release_source_graph.py",
                )
            },
            "publicationAuthorized": False,
        },
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
    validation = _validate_validation_claims(value.get("protectedValidation"))
    claims = _artifact_claims(aab, graph, sidecar, receipt, approval)
    identity = claims["graph"]["releaseIdentity"]
    qualification = VERIFY.verify_release_eligibility(
        receipt, approval, android_root=ROOT,
        expected_version_name=identity["versionName"],
        expected_version_code=identity["versionCode"], source_graph_path=graph,
        approval_effective_time=datetime.fromisoformat(value["generatedAtUtc"].removesuffix("Z") + "+00:00"),
    )
    expected = _unsigned(
        claims,
        qualification,
        validation,
        value["generatedAtUtc"],
        value["challengeNonce"],
    )
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
            child.add_argument("--github-token-file", required=True, type=Path)
            child.add_argument("--workspace-root", required=True, type=Path)
            child.add_argument("--package-authority", required=True, type=Path)
            child.add_argument("--authority-root", required=True, type=Path)
            child.add_argument("--bundletool", required=True, type=Path)
            child.add_argument("--upload-certificate", required=True, type=Path)
            child.add_argument("--java-tool-authority", required=True, type=Path)
    java_authority = actions.add_parser("sign-java-toolchain")
    java_authority.add_argument("--java-sdk", required=True, type=Path)
    java_authority.add_argument("--dotnet", required=True, type=Path)
    java_authority.add_argument("--private-key", required=True, type=Path)
    java_authority.add_argument("--output", required=True, type=Path)
    verify_toolchain = actions.add_parser("verify-toolchain")
    verify_toolchain.add_argument("--authority", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.action == "sign-java-toolchain":
            result = sign_java_toolchain_authority(
                args.java_sdk, args.dotnet, args.private_key, args.output
            )
            print(json.dumps({
                "status": "pass",
                "publicationAuthorized": False,
                "javaToolAuthoritySha256": hashlib.sha256(
                    _read(args.output, "trusted Java toolchain authority", VERIFY.MAX_APPROVAL_BYTES, True)
                ).hexdigest(),
            }, sort_keys=True))
            return 0
        if args.action == "verify-toolchain":
            trusted = _load_trusted_java_toolchain(args.authority)
            print(json.dumps({
                "status": "pass",
                "publicationAuthorized": False,
                "authoritySha256": trusted["authoritySha256"],
                "javaSdkRoot": os.fspath(trusted["javaSdkRoot"]),
                "dotnetPath": os.fspath(trusted["dotnet"]),
            }, sort_keys=True))
            return 0
        common = (args.aab, args.source_graph, args.build_sidecar, args.two_green_receipt, args.two_green_approval)
        result = (
            sign(
                *common,
                args.private_key,
                args.output,
                args.github_token_file,
                workspace_root=args.workspace_root,
                package_authority=args.package_authority,
                authority_root=args.authority_root,
                bundletool=args.bundletool,
                upload_certificate=args.upload_certificate,
                java_tool_authority=args.java_tool_authority,
            )
            if args.action == "sign"
            else verify(args.attestation, *common)
        )
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
