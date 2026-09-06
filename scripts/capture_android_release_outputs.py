#!/usr/bin/env python3
"""Validate and promote release outputs in one sealed-descriptor transaction.

The release build user is allowed to replace every filesystem path it owns.
Consequently neither a mode-0600 key nor a receipt stored beside captured files
can authorize a later promotion. This module takes immutable Linux memfd
snapshots, keeps their descriptors open across validation, and promotes only
those exact bytes. It deliberately exposes no capture/promote split.
"""

from __future__ import annotations

import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import stat
from typing import Any, Callable


CONTRACT = "chummer.android.stable-release-output-transaction/v2"
MAX_AAB_BYTES = 512 * 1024 * 1024
MAX_GRAPH_BYTES = 16 * 1024 * 1024
CHUNK = 1024 * 1024
REQUIRED_SEALS = (
    fcntl.F_SEAL_SEAL
    | fcntl.F_SEAL_SHRINK
    | fcntl.F_SEAL_GROW
    | fcntl.F_SEAL_WRITE
)


def _canonical_json(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


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


def _directory(path: Path, label: str) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be an absolute regular directory")
    resolved = path.resolve(strict=True)
    if resolved != path or resolved.stat().st_uid != os.getuid():
        raise ValueError(f"{label} must be canonical and owner-owned")
    return resolved


def _write_all(descriptor: int, raw: bytes) -> None:
    view = memoryview(raw)
    while view:
        written = os.write(descriptor, view)
        if written <= 0:
            raise OSError("release descriptor write made no progress")
        view = view[written:]


def _seal(descriptor: int) -> None:
    fcntl.fcntl(descriptor, fcntl.F_ADD_SEALS, REQUIRED_SEALS)
    if fcntl.fcntl(descriptor, fcntl.F_GET_SEALS) != REQUIRED_SEALS:
        raise ValueError("release snapshot did not acquire the complete immutable seal set")


def _sealed_bytes(raw: bytes, label: str) -> dict[str, Any]:
    if not raw:
        raise ValueError(f"{label} is empty")
    descriptor = os.memfd_create(
        f"chummer-{label}",
        getattr(os, "MFD_CLOEXEC", 0) | getattr(os, "MFD_ALLOW_SEALING", 0x0002),
    )
    try:
        _write_all(descriptor, raw)
        os.fsync(descriptor)
        _seal(descriptor)
        os.lseek(descriptor, 0, os.SEEK_SET)
        metadata = os.fstat(descriptor)
        return {
            "descriptor": descriptor,
            "identity": _identity(metadata),
            "sha256": hashlib.sha256(raw).hexdigest(),
            "sizeBytes": len(raw),
            "label": label,
        }
    except Exception:
        os.close(descriptor)
        raise


def _sealed_snapshot(source: Path, *, label: str, limit: int) -> dict[str, Any]:
    if not source.is_absolute() or source.is_symlink() or not source.is_file():
        raise ValueError(f"{label} must be an absolute regular non-symlink file")
    descriptor = os.open(
        source,
        os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
    )
    try:
        before = os.fstat(descriptor)
        path_before = os.stat(source, follow_symlinks=False)
        if (
            not stat.S_ISREG(before.st_mode)
            or _identity(before) != _identity(path_before)
            or before.st_size <= 0
            or before.st_size > limit
        ):
            raise ValueError(f"{label} is not one bounded stable file")
        chunks: list[bytes] = []
        observed = 0
        while chunk := os.read(descriptor, CHUNK):
            observed += len(chunk)
            if observed > limit:
                raise ValueError(f"{label} grew beyond its bound")
            chunks.append(chunk)
        after = os.fstat(descriptor)
        path_after = os.stat(source, follow_symlinks=False)
        if (
            observed != before.st_size
            or _identity(before) != _identity(after)
            or _identity(after) != _identity(path_after)
        ):
            raise ValueError(f"{label} changed while being snapshotted")
        snapshot = _sealed_bytes(b"".join(chunks), label)
        snapshot["sourceIdentity"] = _identity(before)
        return snapshot
    finally:
        os.close(descriptor)


def _fd_path(snapshot: dict[str, Any]) -> Path:
    return Path(f"/proc/self/fd/{snapshot['descriptor']}")


def _assert_sealed(snapshot: dict[str, Any]) -> None:
    descriptor = snapshot["descriptor"]
    metadata = os.fstat(descriptor)
    if (
        _identity(metadata) != snapshot["identity"]
        or fcntl.fcntl(descriptor, fcntl.F_GET_SEALS) != REQUIRED_SEALS
    ):
        raise ValueError(f"sealed {snapshot['label']} changed during release transaction")
    os.lseek(descriptor, 0, os.SEEK_SET)
    digest = hashlib.sha256()
    observed = 0
    while chunk := os.read(descriptor, CHUNK):
        observed += len(chunk)
        digest.update(chunk)
    os.lseek(descriptor, 0, os.SEEK_SET)
    if observed != snapshot["sizeBytes"] or digest.hexdigest() != snapshot["sha256"]:
        raise ValueError(f"sealed {snapshot['label']} bytes changed during release transaction")


def _promote(snapshot: dict[str, Any], destination: Path, mode: int) -> None:
    _directory(destination.parent, "release artifact directory")
    if destination.exists() or destination.is_symlink():
        raise ValueError("release artifact output already exists")
    source = snapshot["descriptor"]
    target = os.open(
        destination,
        os.O_WRONLY
        | os.O_CREAT
        | os.O_EXCL
        | getattr(os, "O_CLOEXEC", 0)
        | getattr(os, "O_NOFOLLOW", 0),
        mode,
    )
    try:
        os.fchmod(target, mode)
        os.lseek(source, 0, os.SEEK_SET)
        digest = hashlib.sha256()
        observed = 0
        while chunk := os.read(source, CHUNK):
            observed += len(chunk)
            digest.update(chunk)
            _write_all(target, chunk)
        os.fsync(target)
        target_metadata = os.fstat(target)
        path_metadata = os.stat(destination, follow_symlinks=False)
        if (
            observed != snapshot["sizeBytes"]
            or digest.hexdigest() != snapshot["sha256"]
            or _identity(target_metadata) != _identity(path_metadata)
            or stat.S_IMODE(target_metadata.st_mode) != mode
            or target_metadata.st_nlink != 1
        ):
            raise ValueError("release artifact promotion did not retain exact descriptor bytes")
    except Exception:
        destination.unlink(missing_ok=True)
        raise
    finally:
        os.close(target)


Validator = Callable[[Path, Path, Path, tuple[int, ...]], None]


def transaction(
    aab: Path,
    graph: Path,
    output_aab: Path,
    output_graph: Path,
    output_sidecar: Path,
    validator: Validator,
) -> dict[str, Any]:
    """Validate and promote exact sealed bytes without a filesystem receipt seam."""

    if (
        output_aab.name != output_aab.name.strip()
        or not output_aab.name.endswith(".aab")
        or not output_graph.name.endswith(".json")
        or output_sidecar.name != f"{output_aab.name}.sha256"
    ):
        raise ValueError("final release output names are not canonical")
    snapshots: list[dict[str, Any]] = []
    promoted: list[Path] = []
    try:
        aab_snapshot = _sealed_snapshot(aab, label="release-aab", limit=MAX_AAB_BYTES)
        snapshots.append(aab_snapshot)
        graph_snapshot = _sealed_snapshot(
            graph, label="release-source-graph", limit=MAX_GRAPH_BYTES
        )
        snapshots.append(graph_snapshot)
        sidecar_raw = (
            f"{aab_snapshot['sha256']}  artifacts/{output_aab.name}\n"
            f"{graph_snapshot['sha256']}  artifacts/{output_graph.name}\n"
        ).encode("ascii")
        sidecar_snapshot = _sealed_bytes(sidecar_raw, "release-build-sidecar")
        snapshots.append(sidecar_snapshot)
        inherited = tuple(snapshot["descriptor"] for snapshot in snapshots)
        validator(
            _fd_path(aab_snapshot),
            _fd_path(graph_snapshot),
            _fd_path(sidecar_snapshot),
            inherited,
        )
        for snapshot in snapshots:
            _assert_sealed(snapshot)
        for snapshot, destination, mode in (
            (aab_snapshot, output_aab, 0o444),
            (graph_snapshot, output_graph, 0o444),
            (sidecar_snapshot, output_sidecar, 0o444),
        ):
            _promote(snapshot, destination, mode)
            promoted.append(destination)
        return {
            "contractName": CONTRACT,
            "publicationAuthorized": False,
            "aabSha256": aab_snapshot["sha256"],
            "sourceGraphSha256": graph_snapshot["sha256"],
            "buildSidecarSha256": sidecar_snapshot["sha256"],
        }
    except Exception:
        for destination in reversed(promoted):
            destination.unlink(missing_ok=True)
        raise
    finally:
        for snapshot in reversed(snapshots):
            os.close(snapshot["descriptor"])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--library-only",
        action="store_true",
        help="confirm this module intentionally exposes no split capture/promote CLI",
    )
    arguments = parser.parse_args()
    if not arguments.library_only:
        parser.error("release output promotion is available only through the protected transaction")
    print(json.dumps({"contractName": CONTRACT, "publicationAuthorized": False}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
