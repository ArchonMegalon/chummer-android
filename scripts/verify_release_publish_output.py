#!/usr/bin/env python3
"""Fail-closed checks for the unique Android release publish staging directory."""

from __future__ import annotations

import argparse
import re
import stat
from pathlib import Path


def canonical_directory(path: Path) -> Path:
    if path.is_symlink() or not path.is_dir():
        raise ValueError("release publish staging must be a regular directory")
    absolute = path.absolute()
    resolved = path.resolve()
    if absolute != resolved:
        raise ValueError("release publish staging must be canonical")
    return resolved


def require_empty(path: Path) -> None:
    directory = canonical_directory(path)
    if next(directory.iterdir(), None) is not None:
        raise ValueError("release publish staging contains preexisting output")


def resolve_exact_signed_aab(path: Path, package_id: str) -> Path:
    directory = canonical_directory(path)
    candidates = sorted(directory.glob("*-Signed.aab"))
    if len(candidates) != 1:
        raise ValueError("release publish staging must contain exactly one signed AAB")
    candidate = candidates[0]
    if candidate.name != f"{package_id}-Signed.aab":
        raise ValueError("release publish signed AAB identity is unexpected")
    if candidate.is_symlink() or not candidate.is_file():
        raise ValueError("release publish signed AAB must be a regular non-symlink file")
    if candidate.absolute() != candidate.resolve() or candidate.resolve().parent != directory:
        raise ValueError("release publish signed AAB escaped its staging directory")
    return candidate.resolve()


def resolve_exact_unsigned_aab(path: Path, package_id: str) -> Path:
    directory = canonical_directory(path)
    if not isinstance(package_id, str) or not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+", package_id):
        raise ValueError("release publish package identity is invalid")
    raw_name, sidecar_name = f"{package_id}.aab", f"{package_id}-Signed.aab"
    found = set()
    # Android SDK Publish may also emit this exact sidecar. Its presence is
    # tolerated, not its signer trusted; only raw bytes enter unsigned validation.
    for candidate in directory.iterdir():
        if not candidate.name.casefold().endswith(".aab"):
            continue
        if candidate.name not in (raw_name, sidecar_name):
            raise ValueError("release publish AAB identity is unexpected")
        metadata = candidate.lstat()
        if not stat.S_ISREG(metadata.st_mode) or metadata.st_nlink != 1 or metadata.st_size <= 0:
            raise ValueError("release publish AAB must be a nonempty regular single-link file")
        if candidate.absolute() != candidate.resolve() or candidate.resolve().parent != directory:
            raise ValueError("release publish AAB escaped its staging directory")
        found.add(candidate.name)
    if raw_name not in found:
        raise ValueError("release publish staging is missing the exact unsigned AAB")
    return directory / raw_name


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--publish-dir", required=True, type=Path)
    parser.add_argument("--package-id", required=True)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--require-empty", action="store_true")
    action.add_argument("--resolve-exact-signed-aab", action="store_true")
    action.add_argument("--resolve-exact-unsigned-aab", action="store_true")
    arguments = parser.parse_args()

    if arguments.require_empty:
        require_empty(arguments.publish_dir)
    elif arguments.resolve_exact_signed_aab:
        print(resolve_exact_signed_aab(arguments.publish_dir, arguments.package_id))
    else:
        print(resolve_exact_unsigned_aab(arguments.publish_dir, arguments.package_id))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
