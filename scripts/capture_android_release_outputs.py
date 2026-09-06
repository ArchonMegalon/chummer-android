#!/usr/bin/env python3
"""Capture release outputs through stable file descriptors and promote by inode."""

from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import stat


CONTRACT = "chummer.android.stable-release-output-capture/v1"
AUTHENTICATION_ALGORITHM = "hmac-sha256"
AUTHENTICATION_KEY_BYTES = 32
MAX_AAB_BYTES = 512 * 1024 * 1024
MAX_GRAPH_BYTES = 16 * 1024 * 1024
CHUNK = 1024 * 1024


def _canonical_json(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def _authentication_key(
    path: Path, capture_dir: Path, expected_sha256: str
) -> bytes:
    if not path.is_absolute() or path.is_symlink() or not path.is_file():
        raise ValueError("release capture authentication key must be an absolute regular file")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if (
        resolved != path
        or metadata.st_uid != os.getuid()
        or stat.S_IMODE(metadata.st_mode) & 0o077
        or resolved.is_relative_to(capture_dir)
    ):
        raise ValueError(
            "release capture authentication key must be canonical, owner-only, and outside the capture directory"
        )
    descriptor = os.open(
        resolved,
        os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
    )
    try:
        before = os.fstat(descriptor)
        chunks: list[bytes] = []
        observed = 0
        while observed <= AUTHENTICATION_KEY_BYTES:
            chunk = os.read(descriptor, AUTHENTICATION_KEY_BYTES + 1 - observed)
            if not chunk:
                break
            chunks.append(chunk)
            observed += len(chunk)
        raw = b"".join(chunks)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    if (
        _identity(before) != _identity(after)
        or len(raw) != AUTHENTICATION_KEY_BYTES
        or before.st_size != AUTHENTICATION_KEY_BYTES
        or len(expected_sha256) != 64
        or any(character not in "0123456789abcdef" for character in expected_sha256)
        or not hmac.compare_digest(hashlib.sha256(raw).hexdigest(), expected_sha256)
    ):
        raise ValueError("release capture authentication key is not one stable 256-bit key")
    return raw


def _authentication(unsigned: dict[str, object], key: bytes) -> dict[str, str]:
    return {
        "algorithm": AUTHENTICATION_ALGORITHM,
        "keyId": hashlib.sha256(key).hexdigest(),
        "tagHex": hmac.new(key, _canonical_json(unsigned), hashlib.sha256).hexdigest(),
    }


def _directory(path: Path, label: str, *, private: bool) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be an absolute regular directory")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if resolved != path or metadata.st_uid != os.getuid():
        raise ValueError(f"{label} must be canonical and owner-owned")
    if private and stat.S_IMODE(metadata.st_mode) & 0o077:
        raise ValueError(f"{label} must be owner-only")
    return resolved


def _identity(metadata: os.stat_result) -> dict[str, int]:
    return {
        "device": metadata.st_dev,
        "inode": metadata.st_ino,
        "mode": metadata.st_mode,
        "uid": metadata.st_uid,
        "gid": metadata.st_gid,
        "linkCount": metadata.st_nlink,
        "sizeBytes": metadata.st_size,
        "mtimeNs": metadata.st_mtime_ns,
        "ctimeNs": metadata.st_ctime_ns,
    }


def _stable_capture(source: Path, target: Path, *, limit: int, mode: int) -> dict[str, object]:
    if not source.is_absolute() or source.is_symlink() or not source.is_file():
        raise ValueError("release capture source must be an absolute regular non-symlink file")
    source_parent = source.parent.resolve(strict=True)
    if source_parent != source.parent:
        raise ValueError("release capture source parent is not canonical")
    source_fd = os.open(source, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    target_fd = -1
    try:
        before = os.fstat(source_fd)
        path_before = os.stat(source, follow_symlinks=False)
        if not stat.S_ISREG(before.st_mode) or _identity(before) != _identity(path_before):
            raise ValueError("release capture source path does not identify the opened file")
        if before.st_size <= 0 or before.st_size > limit:
            raise ValueError("release capture source size is outside its bound")
        target_fd = os.open(
            target,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
            mode,
        )
        digest = hashlib.sha256()
        copied = 0
        while True:
            chunk = os.read(source_fd, CHUNK)
            if not chunk:
                break
            copied += len(chunk)
            if copied > limit:
                raise ValueError("release capture source grew beyond its bound")
            digest.update(chunk)
            view = memoryview(chunk)
            while view:
                written = os.write(target_fd, view)
                if written <= 0:
                    raise OSError("release capture write made no progress")
                view = view[written:]
        os.fsync(target_fd)
        after = os.fstat(source_fd)
        path_after = os.stat(source, follow_symlinks=False)
        if (
            _identity(before) != _identity(after)
            or _identity(after) != _identity(path_after)
            or copied != before.st_size
        ):
            raise ValueError("release capture source changed while being captured")
        captured = os.fstat(target_fd)
        if not stat.S_ISREG(captured.st_mode) or captured.st_size != copied:
            raise ValueError("captured release output is not the exact copied file")
        return {
            "sourceIdentity": _identity(before),
            "capturedIdentity": _identity(captured),
            "sha256": digest.hexdigest(),
            "sizeBytes": copied,
        }
    except Exception:
        target.unlink(missing_ok=True)
        raise
    finally:
        if target_fd >= 0:
            os.close(target_fd)
        os.close(source_fd)


def _write_new(path: Path, raw: bytes, mode: int) -> None:
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        mode,
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def capture(
    aab: Path,
    graph: Path,
    capture_dir: Path,
    final_aab_name: str,
    final_graph_name: str,
    authentication_key: Path,
    expected_authentication_key_sha256: str,
) -> dict[str, object]:
    capture_dir = _directory(capture_dir, "release capture directory", private=True)
    key = _authentication_key(
        authentication_key, capture_dir, expected_authentication_key_sha256
    )
    if any(capture_dir.iterdir()):
        raise ValueError("release capture directory must be empty")
    if (
        Path(final_aab_name).name != final_aab_name
        or Path(final_graph_name).name != final_graph_name
        or not final_aab_name.endswith(".aab")
        or not final_graph_name.endswith(".json")
    ):
        raise ValueError("final release output names are not canonical")
    captured_aab = capture_dir / "captured.aab"
    captured_graph = capture_dir / "captured-source-graph.json"
    aab_claim = _stable_capture(aab, captured_aab, limit=MAX_AAB_BYTES, mode=0o444)
    graph_claim = _stable_capture(graph, captured_graph, limit=MAX_GRAPH_BYTES, mode=0o400)
    sidecar = capture_dir / "captured-output.sha256"
    sidecar_raw = (
        f"{aab_claim['sha256']}  artifacts/{final_aab_name}\n"
        f"{graph_claim['sha256']}  artifacts/{final_graph_name}\n"
    ).encode("ascii")
    _write_new(sidecar, sidecar_raw, 0o400)
    sidecar_metadata = sidecar.stat()
    unsigned_receipt = {
        "contractName": CONTRACT,
        "publicationAuthorized": False,
        "outputs": {
            "aab": {
                "capturedName": captured_aab.name,
                "finalName": final_aab_name,
                **aab_claim,
            },
            "sourceGraph": {
                "capturedName": captured_graph.name,
                "finalName": final_graph_name,
                **graph_claim,
            },
            "buildSidecar": {
                "capturedName": sidecar.name,
                "finalName": f"{final_aab_name}.sha256",
                "capturedIdentity": _identity(sidecar_metadata),
                "sha256": hashlib.sha256(sidecar_raw).hexdigest(),
                "sizeBytes": len(sidecar_raw),
            },
        },
    }
    receipt = {
        **unsigned_receipt,
        "authentication": _authentication(unsigned_receipt, key),
    }
    receipt_path = capture_dir / "capture-receipt.json"
    _write_new(receipt_path, _canonical_json(receipt), 0o400)
    return receipt


def _load_receipt(
    capture_dir: Path,
    authentication_key: Path,
    expected_authentication_key_sha256: str,
) -> dict[str, object]:
    key = _authentication_key(
        authentication_key, capture_dir, expected_authentication_key_sha256
    )
    receipt_path = capture_dir / "capture-receipt.json"
    descriptor = os.open(
        receipt_path,
        os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
    )
    try:
        before = os.fstat(descriptor)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_uid != os.getuid()
            or stat.S_IMODE(before.st_mode) != 0o400
            or before.st_size <= 0
            or before.st_size > 1024 * 1024
        ):
            raise ValueError("release capture receipt is not one bounded regular file")
        raw = b""
        while chunk := os.read(descriptor, min(CHUNK, 1024 * 1024 + 1 - len(raw))):
            raw += chunk
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    path_metadata = os.stat(receipt_path, follow_symlinks=False)
    if (
        _identity(before) != _identity(after)
        or _identity(after) != _identity(path_metadata)
        or len(raw) != before.st_size
        or len(raw) > 1024 * 1024
    ):
        raise ValueError("release capture receipt changed during bounded capture")
    try:
        value = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("release capture receipt is invalid") from error
    if (
        _canonical_json(value) != raw
        or value.get("contractName") != CONTRACT
        or set(value) != {"contractName", "publicationAuthorized", "outputs", "authentication"}
    ):
        raise ValueError("release capture receipt is not canonical")
    if value.get("publicationAuthorized") is not False:
        raise ValueError("release capture receipt escalates publication authority")
    authentication = value.pop("authentication", None)
    expected_authentication = _authentication(value, key)
    if (
        not isinstance(authentication, dict)
        or set(authentication) != {"algorithm", "keyId", "tagHex"}
        or authentication.get("algorithm") != AUTHENTICATION_ALGORITHM
        or not hmac.compare_digest(
            str(authentication.get("keyId", "")), expected_authentication["keyId"]
        )
        or not hmac.compare_digest(
            str(authentication.get("tagHex", "")), expected_authentication["tagHex"]
        )
    ):
        raise ValueError("release capture receipt authentication failed")
    return value


def _promote_from_descriptor(source: Path, destination: Path, claim: dict[str, object]) -> None:
    source_descriptor = os.open(
        source,
        os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
    )
    destination_descriptor = -1
    try:
        source_before = os.fstat(source_descriptor)
        path_before = os.stat(source, follow_symlinks=False)
        if (
            not stat.S_ISREG(source_before.st_mode)
            or _identity(source_before) != claim.get("capturedIdentity")
            or _identity(source_before) != _identity(path_before)
        ):
            raise ValueError("captured release output identity changed")
        captured_identity = claim.get("capturedIdentity")
        if not isinstance(captured_identity, dict) or not isinstance(
            captured_identity.get("mode"), int
        ):
            raise ValueError("captured release output mode claim is invalid")
        expected_mode = stat.S_IMODE(captured_identity["mode"])
        if expected_mode not in (0o400, 0o444):
            raise ValueError("captured release output mode is not immutable")
        destination_descriptor = os.open(
            destination,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_CLOEXEC", 0)
            | getattr(os, "O_NOFOLLOW", 0),
            expected_mode,
        )
        digest = hashlib.sha256()
        copied = 0
        while chunk := os.read(source_descriptor, CHUNK):
            digest.update(chunk)
            copied += len(chunk)
            view = memoryview(chunk)
            while view:
                written = os.write(destination_descriptor, view)
                if written <= 0:
                    raise OSError("release artifact promotion write made no progress")
                view = view[written:]
        os.fsync(destination_descriptor)
        source_after = os.fstat(source_descriptor)
        path_after = os.stat(source, follow_symlinks=False)
        destination_after = os.fstat(destination_descriptor)
        destination_path_after = os.stat(destination, follow_symlinks=False)
        if (
            _identity(source_before) != _identity(source_after)
            or _identity(source_after) != _identity(path_after)
            or copied != claim.get("sizeBytes")
            or destination_after.st_size != copied
            or stat.S_IMODE(destination_after.st_mode) != expected_mode
            or _identity(destination_after) != _identity(destination_path_after)
            or digest.hexdigest() != claim.get("sha256")
        ):
            raise ValueError("captured release output changed during descriptor-bound promotion")
    except Exception:
        destination.unlink(missing_ok=True)
        raise
    finally:
        if destination_descriptor >= 0:
            os.close(destination_descriptor)
        os.close(source_descriptor)


def promote(
    capture_dir: Path,
    output_aab: Path,
    output_graph: Path,
    output_sidecar: Path,
    authentication_key: Path,
    expected_authentication_key_sha256: str,
) -> None:
    capture_dir = _directory(capture_dir, "release capture directory", private=True)
    receipt = _load_receipt(
        capture_dir, authentication_key, expected_authentication_key_sha256
    )
    outputs = receipt.get("outputs")
    if not isinstance(outputs, dict) or set(outputs) != {"aab", "sourceGraph", "buildSidecar"}:
        raise ValueError("release capture output inventory is invalid")
    destinations = {
        "aab": output_aab,
        "sourceGraph": output_graph,
        "buildSidecar": output_sidecar,
    }
    promoted: list[Path] = []
    try:
        for name, destination in destinations.items():
            claim = outputs[name]
            if not isinstance(claim, dict):
                raise ValueError("release capture output claim is invalid")
            source = capture_dir / str(claim.get("capturedName"))
            if source.parent != capture_dir or destination.name != claim.get("finalName"):
                raise ValueError("release capture output naming changed")
            _directory(destination.parent, "release artifact directory", private=False)
            if destination.exists() or destination.is_symlink():
                raise ValueError("release artifact output already exists")
            _promote_from_descriptor(source, destination, claim)
            promoted.append(destination)
    except Exception:
        for path in reversed(promoted):
            path.unlink(missing_ok=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    capture_parser = actions.add_parser("capture")
    capture_parser.add_argument("--aab", required=True, type=Path)
    capture_parser.add_argument("--source-graph", required=True, type=Path)
    capture_parser.add_argument("--capture-dir", required=True, type=Path)
    capture_parser.add_argument("--authentication-key", required=True, type=Path)
    capture_parser.add_argument("--expected-authentication-key-sha256", required=True)
    capture_parser.add_argument("--final-aab-name", required=True)
    capture_parser.add_argument("--final-graph-name", required=True)
    promote_parser = actions.add_parser("promote")
    promote_parser.add_argument("--capture-dir", required=True, type=Path)
    promote_parser.add_argument("--authentication-key", required=True, type=Path)
    promote_parser.add_argument("--expected-authentication-key-sha256", required=True)
    promote_parser.add_argument("--output-aab", required=True, type=Path)
    promote_parser.add_argument("--output-source-graph", required=True, type=Path)
    promote_parser.add_argument("--output-sidecar", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.action == "capture":
            result = capture(
                args.aab,
                args.source_graph,
                args.capture_dir,
                args.final_aab_name,
                args.final_graph_name,
                args.authentication_key,
                args.expected_authentication_key_sha256,
            )
            print(json.dumps({"status": "pass", "publicationAuthorized": False, "outputs": result["outputs"]}, sort_keys=True))
        else:
            promote(
                args.capture_dir,
                args.output_aab,
                args.output_source_graph,
                args.output_sidecar,
                args.authentication_key,
                args.expected_authentication_key_sha256,
            )
            print("android_release_output_promotion=passed publication_authorized=false")
    except (OSError, ValueError) as error:
        print(f"android_release_output_capture=failed error={error}")
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
