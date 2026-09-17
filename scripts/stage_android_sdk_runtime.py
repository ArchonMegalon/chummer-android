#!/usr/bin/env python3
"""Stage the one SDK runtime assembly needed by the unsigned Android build.

The SDK is immutable input.  This helper verifies the exact public file before
creating a fresh, owner-only copy in the build-owned intermediate directory.
It never chmods, replaces, repairs, or removes an existing destination.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import os
from pathlib import Path
import stat
import sys
from typing import BinaryIO


SOURCE_SUFFIX = Path(
    "Microsoft.Android.Runtime.36.android/36.1.69/runtimes/android/"
    "lib/net10.0/Mono.Android.Runtime.dll"
)
DESTINATION_DIRECTORY = Path("chummer-android-sdk-36.1.69-runtime")
DESTINATION_NAME = "Mono.Android.Runtime.dll"
EXPECTED_SIZE_BYTES = 22368
EXPECTED_SHA256 = "7d9c809d92b5527556c69988deaa6a1faadbd1757cb11e9e9f167082e37d550f"
CHUNK_BYTES = 1024 * 1024
_NOFOLLOW = getattr(os, "O_NOFOLLOW", 0)
_CLOEXEC = getattr(os, "O_CLOEXEC", 0)


@dataclass(frozen=True)
class StagingPolicy:
    source_suffix: Path
    destination_directory: Path
    destination_name: str
    size_bytes: int
    sha256: str


PRODUCTION_POLICY = StagingPolicy(
    source_suffix=SOURCE_SUFFIX,
    destination_directory=DESTINATION_DIRECTORY,
    destination_name=DESTINATION_NAME,
    size_bytes=EXPECTED_SIZE_BYTES,
    sha256=EXPECTED_SHA256,
)


def _identity(info: os.stat_result) -> tuple[int, ...]:
    return (
        info.st_dev,
        info.st_ino,
        info.st_mode,
        info.st_uid,
        info.st_gid,
        info.st_nlink,
        info.st_size,
        info.st_mtime_ns,
        info.st_ctime_ns,
    )


def _inode_identity(info: os.stat_result) -> tuple[int, ...]:
    return (info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid)


def _canonical_components(path: Path, label: str) -> Path:
    if not path.is_absolute() or path != path.resolve(strict=True):
        raise ValueError(f"{label} must be an absolute canonical path")
    current = Path(path.anchor)
    parts = path.parts[1:]
    for index, part in enumerate(parts):
        current /= part
        info = os.lstat(current)
        if stat.S_ISLNK(info.st_mode):
            raise ValueError(f"{label} contains a symlink ancestor")
        if index < len(parts) - 1 and not stat.S_ISDIR(info.st_mode):
            raise ValueError(f"{label} contains a non-directory ancestor")
    return path


def _private_directory(path: Path, label: str) -> os.stat_result:
    _canonical_components(path, label)
    info = os.lstat(path)
    if (
        not stat.S_ISDIR(info.st_mode)
        or info.st_uid != os.getuid()
        or stat.S_IMODE(info.st_mode) & 0o077
    ):
        raise ValueError(f"{label} must be an owner-only directory")
    return info


def _source_suffix_matches(source: Path, policy: StagingPolicy) -> None:
    suffix = policy.source_suffix.parts
    if tuple(source.parts[-len(suffix) :]) != suffix:
        raise ValueError("source is not the pinned Android runtime path")


def _source_adjacent_is_clean(source: Path) -> None:
    """Reject linker sidecars which would make source selection ambiguous."""
    parent = source.parent
    _canonical_components(parent, "source parent")
    parent_fd = os.open(parent, os.O_RDONLY | os.O_DIRECTORY | _NOFOLLOW | _CLOEXEC)
    try:
        stem = source.name.removesuffix(".dll")
        forbidden = {
            f"{stem}.pdb",
            f"{stem}.mdb",
            f"{source.name}.mdb",
            f"{source.name}.config",
        }
        for name in os.listdir(parent_fd):
            if name in forbidden:
                raise ValueError(f"unexpected source sidecar: {name}")
    finally:
        os.close(parent_fd)


def _source_snapshot(source: Path, policy: StagingPolicy) -> bytes:
    _canonical_components(source, "source")
    _source_suffix_matches(source, policy)
    _source_adjacent_is_clean(source)
    flags = os.O_RDONLY | os.O_NONBLOCK | _NOFOLLOW | _CLOEXEC
    try:
        descriptor = os.open(source, flags)
    except OSError as error:
        raise ValueError(f"source cannot be opened without following links: {error}") from error
    try:
        before = os.fstat(descriptor)
        allowed_uids = {0, os.getuid()}
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_uid not in allowed_uids
            or stat.S_IMODE(before.st_mode) & 0o022
            or before.st_nlink != 1
            or before.st_size != policy.size_bytes
            or _identity(before) != _identity(os.stat(source, follow_symlinks=False))
        ):
            raise ValueError("source custody or pinned size is invalid")
        chunks: list[bytes] = []
        observed = 0
        while True:
            chunk = os.read(descriptor, CHUNK_BYTES)
            if not chunk:
                break
            observed += len(chunk)
            if observed > policy.size_bytes:
                raise ValueError("source grew beyond its exact size")
            chunks.append(chunk)
        after = os.fstat(descriptor)
        raw = b"".join(chunks)
        if (
            observed != policy.size_bytes
            or _identity(before) != _identity(after)
            or _identity(after) != _identity(os.stat(source, follow_symlinks=False))
            or hashlib.sha256(raw).hexdigest() != policy.sha256
        ):
            raise ValueError("source changed or does not match the pinned bytes")
        return raw
    finally:
        os.close(descriptor)


def _open_intermediate(path: Path) -> tuple[int, os.stat_result]:
    info = _private_directory(path, "intermediate root")
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | _NOFOLLOW | _CLOEXEC)
    if _identity(os.fstat(descriptor)) != _identity(info):
        os.close(descriptor)
        raise ValueError("intermediate root changed while opening")
    return descriptor, info


def _open_destination_directory(intermediate_fd: int, name: str) -> tuple[int, bool, os.stat_result]:
    created = False
    try:
        descriptor = os.open(name, os.O_RDONLY | os.O_DIRECTORY | _NOFOLLOW | _CLOEXEC,
                             dir_fd=intermediate_fd)
    except FileNotFoundError:
        try:
            os.mkdir(name, 0o700, dir_fd=intermediate_fd)
            created = True
            descriptor = os.open(name, os.O_RDONLY | os.O_DIRECTORY | _NOFOLLOW | _CLOEXEC,
                                 dir_fd=intermediate_fd)
        except OSError as error:
            raise ValueError(f"staging destination directory cannot be opened safely: {error}") from error
    except OSError as error:
        raise ValueError(f"staging destination directory cannot be opened safely: {error}") from error
    info = os.fstat(descriptor)
    if (
        not stat.S_ISDIR(info.st_mode)
        or info.st_uid != os.getuid()
        or stat.S_IMODE(info.st_mode) & 0o077
    ):
        os.close(descriptor)
        raise ValueError("staging destination directory is not private and owner-owned")
    return descriptor, created, info


def _read_existing(descriptor: int, name: str, expected: bytes, policy: StagingPolicy) -> None:
    flags = os.O_RDONLY | os.O_NONBLOCK | _NOFOLLOW | _CLOEXEC
    try:
        source_fd = os.open(name, flags, dir_fd=descriptor)
    except OSError as error:
        raise ValueError(f"existing destination cannot be opened safely: {error}") from error
    try:
        before = os.fstat(source_fd)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_uid != os.getuid()
            or stat.S_IMODE(before.st_mode) != 0o600
            or before.st_nlink != 1
            or before.st_size != policy.size_bytes
        ):
            raise ValueError("existing destination custody is invalid; refusing overwrite")
        chunks: list[bytes] = []
        observed = 0
        while chunk := os.read(source_fd, CHUNK_BYTES):
            observed += len(chunk)
            if observed > policy.size_bytes:
                raise ValueError("existing destination grew beyond its exact size")
            chunks.append(chunk)
        raw = b"".join(chunks)
        after = os.fstat(source_fd)
        path_info = os.stat(name, dir_fd=descriptor, follow_symlinks=False)
        if (
            raw != expected
            or hashlib.sha256(raw).hexdigest() != policy.sha256
            or _identity(before) != _identity(after)
            or _identity(after) != _identity(path_info)
        ):
            raise ValueError("existing destination bytes or identity do not match; refusing overwrite")
    finally:
        os.close(source_fd)


def _copy_new(descriptor: int, name: str, raw: bytes, policy: StagingPolicy) -> None:
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | _NOFOLLOW | _CLOEXEC
    output_fd: int | None = None
    try:
        output_fd = os.open(name, flags, 0o600, dir_fd=descriptor)
        created_identity = _inode_identity(os.fstat(output_fd))
        if stat.S_IMODE(os.fstat(output_fd).st_mode) != 0o600:
            raise ValueError("new destination did not receive mode 0600")
        view = memoryview(raw)
        digest = hashlib.sha256()
        while view:
            written = os.write(output_fd, view)
            if written <= 0:
                raise ValueError("destination write made no progress")
            digest.update(view[:written])
            view = view[written:]
        os.fsync(output_fd)
        after = os.fstat(output_fd)
        path_info = os.stat(name, dir_fd=descriptor, follow_symlinks=False)
        if (
            _inode_identity(after) != created_identity
            or _inode_identity(after) != _inode_identity(path_info)
            or after.st_uid != os.getuid()
            or stat.S_IMODE(after.st_mode) != 0o600
            or after.st_nlink != 1
            or after.st_size != policy.size_bytes
            or digest.hexdigest() != policy.sha256
        ):
            raise ValueError("new destination failed exact custody verification")
    except Exception:
        # Preserve a failed fresh output for diagnosis.  A later pathname
        # check could race a same-UID replacement and unlink the wrong inode;
        # strict validation on the next invocation rejects non-matching bytes.
        raise
    finally:
        if output_fd is not None:
            os.close(output_fd)


def _revalidate_path_bindings(
    intermediate_root: Path,
    intermediate_fd: int,
    destination_fd: int,
    policy: StagingPolicy,
    expected: bytes,
) -> None:
    """Prove the returned lexical path still names the held directories/output."""
    _private_directory(intermediate_root, "intermediate root")
    rebound_intermediate = os.open(
        intermediate_root, os.O_RDONLY | os.O_DIRECTORY | _NOFOLLOW | _CLOEXEC
    )
    try:
        if _inode_identity(os.fstat(rebound_intermediate)) != _inode_identity(
            os.fstat(intermediate_fd)
        ):
            raise ValueError("intermediate root pathname was rebound")
        destination_path = intermediate_root / policy.destination_directory
        _canonical_components(destination_path, "staging destination directory")
        rebound_destination = os.open(
            destination_path, os.O_RDONLY | os.O_DIRECTORY | _NOFOLLOW | _CLOEXEC
        )
        try:
            if _inode_identity(os.fstat(rebound_destination)) != _inode_identity(
                os.fstat(destination_fd)
            ):
                raise ValueError("staging destination pathname was rebound")
            _read_existing(rebound_destination, policy.destination_name, expected, policy)
        finally:
            os.close(rebound_destination)
    finally:
        os.close(rebound_intermediate)


def stage(source: Path, intermediate_root: Path, *, policy: StagingPolicy = PRODUCTION_POLICY) -> dict[str, object]:
    source = Path(source)
    intermediate_root = Path(intermediate_root)
    intermediate_info = _private_directory(intermediate_root, "intermediate root")
    _canonical_components(source, "source")
    try:
        source.relative_to(intermediate_root)
    except ValueError:
        pass
    else:
        raise ValueError("source must remain outside intermediate root")
    raw = _source_snapshot(source, policy)
    intermediate_fd, opened_info = _open_intermediate(intermediate_root)
    destination_fd: int | None = None
    destination_created = False
    destination_info: os.stat_result | None = None
    try:
        if _inode_identity(opened_info) != _inode_identity(intermediate_info):
            raise ValueError("intermediate root changed before staging")
        destination_fd, destination_created, destination_info = _open_destination_directory(
            intermediate_fd, policy.destination_directory.name
        )
        names = os.listdir(destination_fd)
        if any(name != policy.destination_name for name in names):
            raise ValueError("staging destination contains unknown bytes")
        if policy.destination_name in names:
            _read_existing(destination_fd, policy.destination_name, raw, policy)
            reused = True
        else:
            _copy_new(destination_fd, policy.destination_name, raw, policy)
            os.fsync(destination_fd)
            reused = False
        if _inode_identity(os.fstat(destination_fd)) != _inode_identity(destination_info):
            raise ValueError("staging destination directory changed")
        if _inode_identity(os.fstat(intermediate_fd)) != _inode_identity(opened_info):
            raise ValueError("intermediate root changed during staging")
        _revalidate_path_bindings(
            intermediate_root, intermediate_fd, destination_fd, policy, raw
        )
        return {
            "path": str(intermediate_root / policy.destination_directory / policy.destination_name),
            "sizeBytes": policy.size_bytes,
            "sha256": policy.sha256,
            "reused": reused,
        }
    finally:
        if destination_fd is not None:
            os.close(destination_fd)
        os.close(intermediate_fd)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--intermediate-root", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        result = stage(args.source, args.intermediate_root)
    except (OSError, ValueError) as error:
        print(f"sdk_runtime_staging=blocked reason={error}", file=sys.stderr)
        return 2
    print(
        "sdk_runtime_staging=passed "
        f"path={result['path']} sizeBytes={result['sizeBytes']} "
        f"sha256={result['sha256']} reused={str(result['reused']).lower()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
