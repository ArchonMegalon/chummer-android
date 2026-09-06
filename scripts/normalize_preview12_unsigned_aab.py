#!/usr/bin/env python3
"""Normalize one unsigned AAB into deterministic, signer-ready ZIP bytes."""

from __future__ import annotations

import argparse
import os
from pathlib import Path, PurePosixPath
import stat
import tempfile
import zipfile


MAX_ENTRIES = 20_000
MAX_SOURCE_BYTES = 512 * 1024 * 1024
MAX_ENTRY_BYTES = 256 * 1024 * 1024
MAX_EXPANDED_BYTES = 1024 * 1024 * 1024
EXPECTED_OUTPUT = "chummer-android-0.1.0-preview.12-unsigned.aab"
SIGNATURE_SUFFIXES = (".SF", ".RSA", ".DSA", ".EC")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def canonical_name(name: str) -> bool:
    if not name or "\\" in name or name.startswith("/") or "\x00" in name:
        return False
    raw = name[:-1] if name.endswith("/") else name
    if not raw:
        return False
    parts = raw.split("/")
    return (
        all(part not in ("", ".", "..") for part in parts)
        and PurePosixPath(raw).as_posix() == raw
    )


def file_identity(value: os.stat_result) -> tuple[int, ...]:
    return (
        value.st_dev,
        value.st_ino,
        value.st_mode,
        value.st_uid,
        value.st_gid,
        value.st_nlink,
        value.st_size,
        value.st_mtime_ns,
        value.st_ctime_ns,
    )


def normalize(source: Path, output: Path) -> None:
    require(source.is_absolute() and output.is_absolute(), "AAB paths must be absolute")
    require(source.is_file() and not source.is_symlink(), "source AAB must be a regular file")
    require(source.resolve(strict=True) == source, "source AAB path must be canonical")
    require(output.name == EXPECTED_OUTPUT, "normalized AAB output filename differs")
    require(not output.exists() and not output.is_symlink(), "normalized AAB output must be absent")
    require(output.parent.is_dir() and not output.parent.is_symlink(), "output parent is unsafe")
    require(output.parent.resolve(strict=True) == output.parent, "output parent must be canonical")
    temporary: str | None = None
    try:
        source_descriptor = os.open(source, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
        source_stream = os.fdopen(source_descriptor, "rb")
        try:
            source_before = os.fstat(source_stream.fileno())
            require(
                stat.S_ISREG(source_before.st_mode)
                and 0 < source_before.st_size <= MAX_SOURCE_BYTES,
                "source AAB is not one bounded regular file",
            )
            descriptor, temporary = tempfile.mkstemp(
                prefix=".preview12-aab.", dir=output.parent
            )
            os.close(descriptor)
        except BaseException:
            source_stream.close()
            raise
        with source_stream, zipfile.ZipFile(source_stream, "r") as incoming:
            entries = incoming.infolist()
            require(0 < len(entries) <= MAX_ENTRIES, "source AAB entry count is invalid")
            names = [entry.filename for entry in entries]
            require(len(names) == len(set(names)), "source AAB contains duplicate members")
            expanded = 0
            for entry in entries:
                require(canonical_name(entry.filename), f"unsafe AAB member: {entry.filename}")
                require(not (entry.flag_bits & 0x1), f"encrypted AAB member: {entry.filename}")
                require(
                    ((entry.external_attr >> 16) & 0o170000) != stat.S_IFLNK,
                    f"symlink AAB member: {entry.filename}",
                )
                member_type = (entry.external_attr >> 16) & 0o170000
                require(
                    member_type in (0, stat.S_IFREG, stat.S_IFDIR),
                    f"special AAB member: {entry.filename}",
                )
                require(
                    (entry.is_dir() and member_type in (0, stat.S_IFDIR))
                    or (not entry.is_dir() and member_type in (0, stat.S_IFREG)),
                    f"AAB member type/name mismatch: {entry.filename}",
                )
                require(entry.file_size <= MAX_ENTRY_BYTES, f"oversized AAB member: {entry.filename}")
                expanded += entry.file_size
                require(expanded <= MAX_EXPANDED_BYTES, "source AAB expands beyond its bound")
                upper = entry.filename.upper()
                require(
                    not (upper.startswith("META-INF/") and upper.endswith(SIGNATURE_SUFFIXES)),
                    "source AAB already contains a JAR signature",
                )
            with zipfile.ZipFile(
                temporary,
                "w",
                compression=zipfile.ZIP_DEFLATED,
                compresslevel=9,
                strict_timestamps=True,
            ) as outgoing:
                for entry in sorted(entries, key=lambda item: item.filename.encode("utf-8")):
                    info = zipfile.ZipInfo(entry.filename, date_time=(1980, 1, 1, 0, 0, 0))
                    info.compress_type = zipfile.ZIP_STORED if entry.is_dir() else zipfile.ZIP_DEFLATED
                    info.create_system = 3
                    info.external_attr = (0o40755 if entry.is_dir() else 0o100644) << 16
                    info.flag_bits = 0x800
                    info.extra = b""
                    info.comment = b""
                    if entry.is_dir():
                        outgoing.writestr(info, b"")
                        continue
                    with incoming.open(entry, "r") as reader, outgoing.open(info, "w", force_zip64=True) as writer:
                        observed = 0
                        while chunk := reader.read(1024 * 1024):
                            observed += len(chunk)
                            require(observed <= entry.file_size, f"AAB member grew: {entry.filename}")
                            writer.write(chunk)
                        require(observed == entry.file_size, f"AAB member size drifted: {entry.filename}")
            source_after = os.fstat(source_stream.fileno())
            require(
                file_identity(source_before) == file_identity(source_after)
                and file_identity(source_after)
                == file_identity(os.stat(source, follow_symlinks=False)),
                "source AAB changed during normalization",
            )
        os.chmod(temporary, 0o600)
        os.replace(temporary, output)
        temporary = None
    except (OSError, RuntimeError, zipfile.BadZipFile) as error:
        raise ValueError(f"cannot normalize unsigned Preview.12 AAB: {error}") from error
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    normalize(args.source, args.output)
    print("preview12_unsigned_aab_normalization=pass signing_authorized=false")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ValueError as error:
        print(f"preview12_unsigned_aab_normalization=blocked reason={error}", file=__import__("sys").stderr)
        raise SystemExit(2) from error
