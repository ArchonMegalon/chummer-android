#!/usr/bin/env python3
"""Validate pinned Google AAR bytes; seed only originals, never extracted build state."""
from __future__ import annotations
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import stat
import zipfile
from seal_release_restore_consumption import (
    EXPECTED_ANDROID_TARGETS, _admit_zip_directory, _feed_identity, _feed_names,
    _feed_read, _private_directory, _strict_json_bytes,
)

PACKAGES = {
    "appupdate": "Xamarin.Google.Android.Play.App.Update",
    "corecommon": "Xamarin.Google.Android.Play.Core.Common",
    "playservicesbasement": "Xamarin.GooglePlayServices.Basement",
    "playservicestasks": "Xamarin.GooglePlayServices.Tasks",
    "review": "Xamarin.Google.Android.Play.Review",
}
MAX_ARCHIVE = 4 * 1024 * 1024
# Xamarin.Build.Download 0.11.4 moves Kind=Uncompressed archives into their
# stem directory and writes this marker; the successful path retains its lock.
# Exact admitted DLL evidence and method tokens: docs/ANDROID_AAR_CACHE_LAYOUT.md.
UNPACKED_MARKER = b"This marks that the extraction completed successfully"


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def read_input(path, limit):
    require(path.is_absolute() and path.resolve(strict=True) == path, "input path is not canonical")
    with os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK), "rb") as stream:
        before = os.fstat(stream.fileno())
        require(stat.S_ISREG(before.st_mode) and before.st_uid == os.getuid() and before.st_nlink == 1
                and not before.st_mode & 0o022 and 0 < before.st_size <= limit, "input type/size/ownership")
        data = stream.read(limit + 1)
        require(len(data) == before.st_size and _feed_identity(before) == _feed_identity(os.fstat(stream.fileno()))
                == _feed_identity(path.lstat()), "input changed during read")
    return data


def pins(aar_lock, project_lock):
    inputs = {aar_lock: read_input(aar_lock, 16384), project_lock: read_input(project_lock, 4 * 1024 * 1024)}
    document = _strict_json_bytes(inputs[aar_lock], "AAR pins")
    rows = document.get("archives")
    require(set(document) == {"archives"} and isinstance(rows, list) and len(rows) == 5, "expected five AAR pins")
    names, expected = set(), {}
    for row in rows:
        require(isinstance(row, dict) and set(row) == {"fileName", "packageId", "packageVersion", "sizeBytes", "sha256"}, "AAR pin shape")
        name, package = row["fileName"], row["packageId"]
        require(isinstance(name, str) and re.fullmatch(r"[a-z]+-[0-9]+(?:\.[0-9]+){2}\.aar", name)
                and PACKAGES.get(name.split("-", 1)[0]) == package and name not in names
                and package not in expected, "AAR identity is duplicate or unsupported")
        require(isinstance(row["packageVersion"], str) and re.fullmatch(r"[0-9]+(?:\.[0-9]+){2,3}", row["packageVersion"])
                and type(row["sizeBytes"]) is int and 0 < row["sizeBytes"] <= MAX_ARCHIVE
                and isinstance(row["sha256"], str) and re.fullmatch(r"[0-9a-f]{64}", row["sha256"]), "AAR pin bounds")
        names.add(name); expected[package] = row["packageVersion"]
    require(set(expected) == set(PACKAGES.values()), "AAR package closure differs")
    lock = _strict_json_bytes(inputs[project_lock], "Android package lock")
    targets = lock.get("dependencies")
    require(type(lock.get("version")) is int and lock["version"] in (1, 2) and isinstance(targets, dict)
            and set(targets) == EXPECTED_ANDROID_TARGETS, "Android lock target mismatch")
    found = set()
    for table in targets.values():
        require(isinstance(table, dict) and len(table) <= 1024, "Android lock package bound")
        require(len({name.casefold() for name in table}) == len(table), "duplicate casefold package identity")
        for name, record in table.items():
            if name.casefold() in {package.casefold() for package in expected}:
                require(name in expected and isinstance(record, dict) and record.get("type") in {"Direct", "Transitive"}
                        and record.get("resolved") == expected[name], "AAR NuGet version differs from Android lock")
                found.add(name)
    require(found == set(expected), "Android lock is missing an AAR package")
    return rows, inputs


def admit_archive(data):
    _admit_zip_directory(data)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        rows, names, files, total = archive.infolist(), set(), set(), 0
        require(len(rows) <= 2048, "AAR member count")
        for row in rows:
            name = row.filename.rstrip("/")
            require(row.orig_filename == row.filename and name and not any(ord(c) < 32 for c in name)
                    and "\\" not in name and ":" not in name and not any(p in ("", ".", "..") for p in name.split("/"))
                    and name.casefold() not in names and not row.flag_bits & 1
                    and stat.S_IFMT(row.external_attr >> 16) in (0, stat.S_IFREG, stat.S_IFDIR)
                    and (stat.S_IFMT(row.external_attr >> 16) != stat.S_IFDIR or row.is_dir())
                    and row.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED), "unsafe AAR member")
            names.add(name.casefold()); total += row.file_size
            if not row.is_dir():
                files.add(name.casefold())
            require(total <= 32 * 1024 * 1024, "AAR expanded-byte bound")
            with archive.open(row) as member:
                count = 0
                while chunk := member.read(65536):
                    count += len(chunk)
                    require(count <= row.file_size, "AAR member size changed")
                require(count == row.file_size, "AAR member truncated")
        require(not any("/".join(name.split("/")[:i]) in files for name in names
                        for i in range(1, len(name.split("/")))), "AAR file/directory collision")
        require({"androidmanifest.xml", "classes.jar"} <= files, "AAR required members missing")


def empty_cache_lock(directory, name):
    descriptor = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
    try:
        before = os.fstat(descriptor)
        require(stat.S_ISREG(before.st_mode) and before.st_uid == os.getuid()
                and not before.st_mode & 0o077 and before.st_nlink == 1 and before.st_size == 0,
                "AAR cache lock must be private, singly linked and empty")
        require(os.read(descriptor, 1) == b""
                and _feed_identity(before) == _feed_identity(os.fstat(descriptor))
                == _feed_identity(os.stat(name, dir_fd=directory, follow_symlinks=False)),
                "AAR cache lock changed")
    finally:
        os.close(descriptor)


def cache_layout(names, rows):
    """Each pinned original is either flat or consumed, never absent or both."""
    expected, consumed, valid = set(), set(), True
    for row in rows:
        name, stem = row["fileName"], Path(row["fileName"]).stem
        if name in names:
            expected.add(name)
            valid = valid and stem not in names
        else:
            consumed.add(name)
            expected.update((stem, stem + ".unpacked", stem + ".locked"))
    if not valid or names != expected:
        # Names only, bounded and escaped; never archive bytes or arbitrary state.
        observed = [name[:96] for name in sorted(names)[:20]]
        raise ValueError("AAR cache layout mismatch: expected=" + json.dumps(sorted(expected))
                         + "; observed=" + json.dumps(observed) + "; count=" + str(len(names)))
    return consumed


def consumed_archive(directory, row):
    name, stem = row["fileName"], Path(row["fileName"]).stem
    descriptor = os.open(stem, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=directory)
    try:
        before = os.fstat(descriptor)
        require(stat.S_ISDIR(before.st_mode) and before.st_uid == os.getuid() and not before.st_mode & 0o077,
                "consumed AAR directory must be private and owner-owned")
        require(set(_feed_names(descriptor).values()) == {name}, "unexpected consumed AAR entry")
        data = _feed_read(descriptor, name, MAX_ARCHIVE)
        require(_feed_read(directory, stem + ".unpacked", len(UNPACKED_MARKER)) == UNPACKED_MARKER,
                "AAR cache completion marker differs")
        empty_cache_lock(directory, stem + ".locked")
        require(_feed_identity(before) == _feed_identity(os.fstat(descriptor))
                == _feed_identity(os.stat(stem, dir_fd=directory, follow_symlinks=False)),
                "consumed AAR directory changed")
        return data
    finally:
        os.close(descriptor)


def inventory(root, rows, *, generated=False):
    _private_directory(root, "AAR directory")
    descriptor = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        before = _feed_identity(os.fstat(descriptor))
        names = set(_feed_names(descriptor).values())
        expected = {row["fileName"] for row in rows}
        if generated:
            consumed = cache_layout(names, rows)
        else:
            require(names == expected, "AAR feed must contain exactly five originals")
            consumed = set()
        payloads = {}
        for row in rows:
            data = (consumed_archive(descriptor, row) if row["fileName"] in consumed
                    else _feed_read(descriptor, row["fileName"], MAX_ARCHIVE))
            require(len(data) == row["sizeBytes"] and hashlib.sha256(data).hexdigest() == row["sha256"], "AAR byte pin mismatch")
            admit_archive(data)
            payloads[row["fileName"]] = data
        require(before == _feed_identity(os.fstat(descriptor)) == _feed_identity(root.lstat())
                and root.resolve(strict=True) == root, "AAR directory changed")
        return payloads
    finally:
        os.close(descriptor)


def prepare(operation, *, source=None, cache=None, aar_lock, project_lock, exclude_roots=()):
    require(operation in {"validate", "seed", "verify"}, "unknown AAR operation")
    require((operation == "validate") == (cache is None), "cache argument differs from operation")
    require(len(exclude_roots) <= 16, "too many protected roots")
    for excluded in exclude_roots:
        require(excluded.is_absolute() and excluded.is_dir() and excluded.resolve(strict=True) == excluded,
                "protected root must be an existing canonical directory")
    for root in (source, cache):
        if root is not None:
            require(root.is_absolute() and root.resolve() == root, "AAR directory must be canonical")
            for excluded in exclude_roots:
                require(not root.is_relative_to(excluded) and not excluded.is_relative_to(root), "AAR path overlaps protected root")
    if source is not None and cache is not None:
        require(not source.is_relative_to(cache) and not cache.is_relative_to(source), "AAR source/cache overlap")
    if operation == "seed":
        _private_directory(cache.parent, "AAR cache parent")
        require(not cache.exists() and not cache.is_symlink(), "AAR cache must be fresh")
    if source is None:
        require(operation == "seed", "offline AAR source is required")
        cache.mkdir(mode=0o700)
        return {"offline": False, "archiveCount": 0, "originAuthenticated": False}
    rows, inputs = pins(aar_lock, project_lock)
    payloads = inventory(source, rows)
    if operation == "seed":
        cache.mkdir(mode=0o700)
        for name, data in payloads.items():
            with os.fdopen(os.open(cache / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600), "wb") as stream:
                stream.write(data); stream.flush(); os.fsync(stream.fileno())
    if cache is not None:
        require(inventory(cache, rows, generated=operation == "verify") == payloads, "AAR cache differs from source")
    require(inventory(source, rows) == payloads and all(read_input(path, len(data)) == data for path, data in inputs.items()), "AAR inputs changed")
    return {"offline": True, "archiveCount": 5, "originAuthenticated": False,
            "aarPinsSha256": hashlib.sha256(inputs[aar_lock]).hexdigest(), "projectLockSha256": hashlib.sha256(inputs[project_lock]).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("validate", "seed", "verify"))
    for name in ("source", "cache", "aar-lock", "project-lock"):
        parser.add_argument("--" + name, required=name.endswith("lock"))
    parser.add_argument("--exclude-root", action="append", default=[])
    args = parser.parse_args()
    for value in (args.source, args.cache, args.aar_lock, args.project_lock, *args.exclude_root):
        require(value is None or (value and str(Path(value)) == value and Path(value).is_absolute()), "empty/relative/aliased AAR path")
    result = prepare(args.operation, source=Path(args.source) if args.source is not None else None,
                     cache=Path(args.cache) if args.cache is not None else None, aar_lock=Path(args.aar_lock),
                     project_lock=Path(args.project_lock), exclude_roots=tuple(map(Path, args.exclude_root)))
    print(json.dumps(result, sort_keys=True))


if __name__ == "__main__":
    main()
