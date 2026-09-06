#!/usr/bin/env python3
"""Fail-closed checks for the unique Android release publish staging directory."""

from __future__ import annotations

import argparse
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
    candidates = sorted(directory.glob("*.aab"))
    if len(candidates) != 1:
        raise ValueError("release publish staging must contain exactly one unsigned AAB")
    candidate = candidates[0]
    if candidate.name != f"{package_id}.aab":
        raise ValueError("release publish unsigned AAB identity is unexpected")
    if candidate.is_symlink() or not candidate.is_file():
        raise ValueError("release publish unsigned AAB must be a regular non-symlink file")
    if candidate.absolute() != candidate.resolve() or candidate.resolve().parent != directory:
        raise ValueError("release publish unsigned AAB escaped its staging directory")
    return candidate.resolve()


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
