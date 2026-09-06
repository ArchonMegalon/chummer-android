#!/usr/bin/env python3
"""Reject private signing material anywhere in the Android repository."""

from __future__ import annotations

import argparse
import os
from pathlib import Path, PurePosixPath
import stat
import subprocess


PRIVATE_SUFFIXES = (
    ".key",
    ".p8",
    ".pk8",
    ".p12",
    ".pfx",
    ".jks",
    ".keystore",
    ".private.pem",
)
PRIVATE_MARKERS = (
    b"-----BEGIN " + b"PRIVATE KEY-----",
    b"-----BEGIN ENCRYPTED " + b"PRIVATE KEY-----",
    b"-----BEGIN RSA " + b"PRIVATE KEY-----",
    b"-----BEGIN EC " + b"PRIVATE KEY-----",
    b"-----BEGIN OPENSSH " + b"PRIVATE KEY-----",
)
PRIVATE_HEADER_LINES = tuple(
    ending
    for marker in PRIVATE_MARKERS
    for ending in (marker + b"\n", marker + b"\r\n")
)
REQUIRED_IGNORES = (
    "*.private.pem",
    "*.key",
    "*.p8",
    "*.pk8",
    "*.p12",
    "*.pfx",
    "*.jks",
    "*.keystore",
    "service-account*.json",
)
SCAN_CHUNK_BYTES = 1024 * 1024
MAX_SCAN_FILE_BYTES = 512 * 1024 * 1024
MAX_SCAN_TOTAL_BYTES = 4 * 1024 * 1024 * 1024


def _git_paths(root: Path, *arguments: str) -> tuple[str, ...]:
    completed = subprocess.run(
        ["git", "-C", os.fspath(root), *arguments, "-z"],
        check=True,
        capture_output=True,
        env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
    )
    try:
        return tuple(
            item.decode("utf-8") for item in completed.stdout.split(b"\0") if item
        )
    except UnicodeDecodeError as error:
        raise ValueError("repository contains a non-UTF-8 path") from error


def _safe_file(root: Path, relative: str) -> Path:
    posix = PurePosixPath(relative)
    if posix.is_absolute() or ".." in posix.parts or "\\" in relative:
        raise ValueError("repository key scan path is unsafe")
    path = root.joinpath(*posix.parts)
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"repository key scan encountered unsafe file: {relative}")
    resolved = path.resolve(strict=True)
    if not resolved.is_relative_to(root):
        raise ValueError(f"repository key scan escaped repository: {relative}")
    return resolved


def _scan_content(path: Path, relative: str, remaining: int) -> int:
    descriptor = os.open(
        path,
        os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
    )
    try:
        before = os.fstat(descriptor)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_size > MAX_SCAN_FILE_BYTES
            or before.st_size > remaining
        ):
            raise ValueError(f"repository key scan bound exceeded: {relative}")
        overlap = max(map(len, PRIVATE_HEADER_LINES)) - 1
        tail = b""
        observed = 0
        while chunk := os.read(descriptor, SCAN_CHUNK_BYTES):
            chunk_start = observed
            observed += len(chunk)
            if observed > before.st_size:
                raise ValueError(f"repository key scan file changed: {relative}")
            window = tail + chunk
            window_start = chunk_start - len(tail)
            for marker in PRIVATE_HEADER_LINES:
                offset = window.find(marker)
                while offset >= 0:
                    absolute_offset = window_start + offset
                    if absolute_offset == 0 or (
                        offset > 0 and window[offset - 1] in (10, 13)
                    ):
                        raise ValueError(
                            f"repository contains a private key marker: {relative}"
                        )
                    offset = window.find(marker, offset + 1)
            tail = window[-overlap:]
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    identity = lambda value: (
        value.st_dev,
        value.st_ino,
        value.st_mode,
        value.st_uid,
        value.st_size,
        value.st_mtime_ns,
        value.st_ctime_ns,
    )
    try:
        path_after = os.stat(path, follow_symlinks=False)
    except OSError as error:
        raise ValueError(f"repository key scan file changed: {relative}") from error
    if (
        identity(before) != identity(after)
        or identity(after) != identity(path_after)
        or observed != before.st_size
    ):
        raise ValueError(f"repository key scan file changed: {relative}")
    return observed


def verify(root: Path) -> None:
    if not root.is_absolute() or root.is_symlink() or not root.is_dir():
        raise ValueError("repository key scan root must be one canonical directory")
    root = root.resolve(strict=True)
    ignore = _safe_file(root, ".gitignore").read_text(encoding="utf-8").splitlines()
    missing = [item for item in REQUIRED_IGNORES if item not in ignore]
    if missing:
        raise ValueError("repository key ignore policy is incomplete")

    paths = set(_git_paths(root, "ls-files"))
    paths.update(_git_paths(root, "ls-files", "--others", "--exclude-standard"))
    ignored_paths = set(
        _git_paths(root, "ls-files", "--others", "--ignored", "--exclude-standard")
    )
    for relative in sorted(ignored_paths):
        lower = relative.lower()
        if lower.endswith(PRIVATE_SUFFIXES) or PurePosixPath(lower).name.startswith(
            "service-account"
        ):
            raise ValueError(f"repository contains ignored private-key-shaped material: {relative}")
    paths.update(ignored_paths)
    scanned = 0
    for relative in sorted(paths):
        lower = relative.lower()
        if lower.endswith(PRIVATE_SUFFIXES) or PurePosixPath(lower).name.startswith(
            "service-account"
        ):
            raise ValueError(f"repository contains private-key-shaped material: {relative}")
        path = _safe_file(root, relative)
        metadata = path.stat()
        if not stat.S_ISREG(metadata.st_mode):
            raise ValueError(f"repository key scan encountered non-regular file: {relative}")
        scanned += _scan_content(path, relative, MAX_SCAN_TOTAL_BYTES - scanned)


def private_key(path: Path, root: Path, label: str) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_file():
        raise ValueError(f"{label} must be an absolute regular non-symlink file")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if (
        resolved != path
        or metadata.st_uid != os.getuid()
        or stat.S_IMODE(metadata.st_mode) & 0o077
        or resolved.is_relative_to(root.resolve(strict=True))
    ):
        raise ValueError(f"{label} must be canonical, owner-only, and outside the repository")
    return resolved


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", required=True, type=Path)
    arguments = parser.parse_args()
    try:
        verify(arguments.repo_root)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"android_release_private_key_hygiene=failed error={error}")
        return 2
    print("android_release_private_key_hygiene=passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
