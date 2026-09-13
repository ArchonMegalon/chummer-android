#!/usr/bin/env python3
"""Seal and revalidate the exact locked restore consumed by an Android release.

The release build uses a fresh owner-only root.  This tool copies the selected
Chummer nupkgs through no-follow descriptors, inventories every byte in the
isolated global-packages and restore-intermediate trees, binds assets/dgspec/
lock inputs, and proves the selected twelve-package Chummer closure.  Publish
may add controlled bin/obj outputs, but may not alter any sealed restore byte.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import os
import re
import stat
import struct
import xml.etree.ElementTree as ET
import zipfile
import zlib
from pathlib import Path, PurePosixPath
from typing import Any, Iterable, Mapping


CONTRACT = "chummer.android.release-restore-consumption/v2"
DRIFT_DIAGNOSTIC_CONTRACT = "chummer.android.release-restore-drift-diagnostic/v1"
AUTHORITY_CONTRACT = "chummer.android.release-package-authority/v2"
EXPECTED_SOURCE_PROJECTS = {
    "Chummer.Desktop.Runtime": (
        "1.0.0",
        PurePosixPath("chummer-presentation/Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj"),
    ),
    "Chummer.Presentation": (
        "1.0.0",
        PurePosixPath("chummer-presentation/Chummer.Presentation/Chummer.Presentation.csproj"),
    ),
}
EXPECTED_ANDROID_TARGETS = {
    "net10.0-android36.0",
    "net10.0-android36.0/android-arm64",
}
EXPECTED_ROUTED_LOCKS = {
    "Chummer.Android.packages.lock.json",
    "Chummer.Desktop.Runtime.packages.lock.json",
    "Chummer.Presentation.packages.lock.json",
}
EXPECTED_INTERMEDIATE_PROJECTS = {"Chummer.Android", *EXPECTED_SOURCE_PROJECTS}
PRIMARY_ASSETS = "Chummer.Android/project.assets.json"
PRIMARY_DGSPEC = "Chummer.Android/Chummer.Android.csproj.nuget.dgspec.json"
VERIFY_PHASES = {"pre-publish", "post-publish"}
# Transport/read limits, not a quota for NuGet extraction or later build output.
FEED_ARCHIVE_BYTES = 128 * 1024 * 1024
FEED_TOTAL_BYTES = 2 * 1024 * 1024 * 1024
FEED_EXPANDED_BYTES = 512 * 1024 * 1024
FEED_TOTAL_EXPANDED_BYTES = 8 * 1024 * 1024 * 1024
FEED_MEMBER_COUNT = 8192
FEED_CENTRAL_BYTES = 16 * 1024 * 1024
FEED_ZIP_RECORD_BYTES = 8192
FEED_MEMBER_NAME_BYTES = 1024
FEED_MEMBER_DEPTH = 32
FEED_PACKAGE_COUNT = 1024
FEED_LOCK_BYTES = 4 * 1024 * 1024
FEED_NUSPEC_BYTES = 1024 * 1024
PACKAGE_ID = re.compile(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}\Z")
PACKAGE_VERSION = re.compile(r"[0-9]+(?:\.[0-9]+){0,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?\Z")


class InventoryDriftError(ValueError):
    """Fail-closed inventory drift carrying complete, byte-free diagnostics."""

    def __init__(self, message: str, diagnostic: Mapping[str, Any]) -> None:
        super().__init__(message)
        self.diagnostic = dict(diagnostic)


def _strict_json_bytes(data: bytes, label: str) -> dict[str, Any]:
    def reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{label} contains duplicate key {key!r}")
            result[key] = value
        return result

    try:
        value = json.loads(
            data.decode("utf-8"),
            object_pairs_hook=reject_duplicates,
            parse_constant=lambda item: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite number {item}")
            ),
        )
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read {label}: {error}") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one JSON object")
    return value


def _strict_json(path: Path, label: str) -> dict[str, Any]:
    data, _ = _stable_file(path, label)
    return _strict_json_bytes(data, label)


def _private_directory(path: Path, label: str, *, empty: bool = False) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be one absolute non-symlinked directory")
    resolved = path.resolve(strict=True)
    info = path.stat()
    if resolved != path or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) & 0o077:
        raise ValueError(f"{label} must be canonical, owner-owned, and owner-only")
    if empty and any(path.iterdir()):
        raise ValueError(f"{label} must be empty")
    return path


def _owned_directory(path: Path, label: str) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be one absolute non-symlinked directory")
    resolved = path.resolve(strict=True)
    if resolved != path or path.stat().st_uid != os.getuid():
        raise ValueError(f"{label} must be canonical and owner-owned")
    return path


def _outside(root: Path, workspace: Path) -> None:
    try:
        root.relative_to(workspace)
    except ValueError:
        return
    raise ValueError("release restore input root must remain outside the coherent workspace")


def _stable_file(path: Path, label: str) -> tuple[bytes, os.stat_result]:
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(path, flags)
    except OSError as error:
        raise ValueError(f"cannot open {label} without following links: {error}") from error
    try:
        before = os.fstat(descriptor)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_uid != os.getuid()
            or stat.S_IMODE(before.st_mode) & 0o077
            or before.st_nlink != 1
        ):
            raise ValueError(f"{label} must be an owner-only, singly-linked regular file")
        chunks: list[bytes] = []
        while True:
            chunk = os.read(descriptor, 1024 * 1024)
            if not chunk:
                break
            chunks.append(chunk)
        after = os.fstat(descriptor)
        if (
            (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
            != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns)
        ):
            raise ValueError(f"{label} changed while it was captured")
        return b"".join(chunks), after
    finally:
        os.close(descriptor)


def _file_row(path: Path, relative: str, label: str) -> dict[str, Any]:
    data, info = _stable_file(path, label)
    return {
        "path": relative,
        "sizeBytes": info.st_size,
        "sha256": hashlib.sha256(data).hexdigest(),
    }


def _tree_inventory(root: Path, label: str) -> list[dict[str, Any]]:
    root = _private_directory(root, label)
    rows: list[dict[str, Any]] = []
    for current_text, directories, files in os.walk(root, topdown=True, followlinks=False):
        current = Path(current_text)
        directories.sort()
        files.sort()
        for name in directories:
            path = current / name
            info = path.lstat()
            if (
                not stat.S_ISDIR(info.st_mode)
                or path.is_symlink()
                or info.st_uid != os.getuid()
                or stat.S_IMODE(info.st_mode) & 0o077
            ):
                raise ValueError(f"{label} contains an unsafe directory: {path.relative_to(root)}")
        for name in files:
            path = current / name
            relative = path.relative_to(root).as_posix()
            rows.append(_file_row(path, relative, f"{label} file {relative}"))
    return rows


def _routed_lock_inventory(root: Path) -> list[dict[str, Any]]:
    root = _private_directory(root, "routed project-lock root")
    entries = list(root.iterdir())
    if {path.name for path in entries} != EXPECTED_ROUTED_LOCKS or len(entries) != 3:
        raise ValueError("routed project-lock root must contain exactly three approved locks")
    if any(path.is_symlink() or not path.is_file() for path in entries):
        raise ValueError("routed project-lock root contains an unsafe lock")
    return _tree_inventory(root, "routed project-lock root")


def _workspace_build_state(workspace_root: Path) -> tuple[list[str], list[dict[str, Any]]]:
    workspace_root = workspace_root.resolve(strict=True)
    roots: list[str] = []
    rows: list[dict[str, Any]] = []
    for current_text, directories, _files in os.walk(workspace_root, topdown=True, followlinks=False):
        current = Path(current_text)
        selected = [name for name in directories if name in {"bin", "obj"}]
        directories[:] = sorted(name for name in directories if name not in {"bin", "obj"})
        for name in sorted(selected):
            root = current / name
            relative_root = root.relative_to(workspace_root).as_posix()
            roots.append(relative_root)
            for row in _tree_inventory(root, f"workspace build-state root {relative_root}"):
                rows.append({**row, "path": f"{relative_root}/{row['path']}"})
    return sorted(roots), sorted(rows, key=lambda row: row["path"])


def assert_clean_workspace_build_state(workspace_root: Path) -> None:
    roots, rows = _workspace_build_state(workspace_root)
    if roots or rows:
        raise ValueError("coherent release workspace contains stale bin/obj build state")


def _inventory_digest(rows: Iterable[Mapping[str, Any]]) -> str:
    encoded = json.dumps(list(rows), sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _restore_roots(
    input_root: Path, workspace_root: Path, intermediate_root: Path,
    owner_feed: Path, packages_root: Path, routed_lock_root: Path,
) -> None:
    _private_directory(input_root, "release restore input root")
    _owned_directory(workspace_root, "coherent workspace")
    _outside(input_root, workspace_root)
    if workspace_root.is_relative_to(input_root):
        raise ValueError("release restore input root and coherent workspace must be disjoint")
    roots = (
        (intermediate_root, "isolated restore intermediate root"),
        (owner_feed, "private selected package feed"),
        (packages_root, "isolated global-packages cache"),
        (routed_lock_root, "routed project-lock root"),
    )
    for path, label in roots:
        _private_directory(path, label)
        if path == input_root or not path.is_relative_to(input_root):
            raise ValueError(f"{label} must remain strictly inside the release restore input root")
    for index, (left, _label) in enumerate(roots):
        for right, _other_label in roots[index + 1:]:
            if left.is_relative_to(right) or right.is_relative_to(left):
                raise ValueError("release restore roots must not overlap")


def _is_restore_metadata(name: str) -> bool:
    return name in {"project.assets.json", "project.nuget.cache"} or name.endswith(
        (".nuget.dgspec.json", ".nuget.g.props", ".nuget.g.targets")
    )


def _restore_inventory(root: Path) -> list[dict[str, Any]]:
    rows = _tree_inventory(root, "isolated restore intermediates")
    if any(not item.is_dir() or item.name not in EXPECTED_INTERMEDIATE_PROJECTS
           for item in root.iterdir()):
        raise ValueError("isolated restore intermediates contain an unexpected project root")
    for row in rows:
        parts = PurePosixPath(row["path"]).parts
        if len(parts) < 2 or parts[0] not in EXPECTED_INTERMEDIATE_PROJECTS:
            raise ValueError("isolated restore intermediate path is not project-scoped")
        name = parts[-1]
        if _is_restore_metadata(name):
            allowed = {
                "project.assets.json", "project.nuget.cache",
                f"{parts[0]}.csproj.nuget.dgspec.json",
                f"{parts[0]}.csproj.nuget.g.props",
                f"{parts[0]}.csproj.nuget.g.targets",
            }
            if len(parts) != 2 or name not in allowed:
                raise ValueError("duplicate or misplaced restore metadata in intermediate root")
    paths = {row["path"] for row in rows}
    if not {PRIMARY_ASSETS, PRIMARY_DGSPEC}.issubset(paths):
        raise ValueError("restore must produce exactly one primary Android project.assets.json and dgspec")
    return rows


def _root_identity(root: Path) -> dict[str, int]:
    info = root.stat()
    return {"device": info.st_dev, "inode": info.st_ino}


def _post_publish_workspace_state(workspace_root: Path) -> None:
    roots, rows = _workspace_build_state(workspace_root)
    project_dirs = {
        PurePosixPath("chummer-android/src/Chummer.Android"),
        *(relative.parent for _version, relative in EXPECTED_SOURCE_PROJECTS.values()),
    }
    allowed = {(project / "bin").as_posix() for project in project_dirs}
    if not set(roots).issubset(allowed):
        raise ValueError("workspace build outputs must remain in the exact source-project bin roots")
    if any(_is_restore_metadata(PurePosixPath(row["path"]).name) for row in rows):
        raise ValueError("workspace build outputs contain unsealed restore metadata")


def _authority_packages(authority: Mapping[str, Any]) -> list[dict[str, str]]:
    if authority.get("contractName") != AUTHORITY_CONTRACT:
        raise ValueError("release package authority contract is not exact")
    core = authority.get("packagePins")
    owners = authority.get("ownerPackagePins")
    if not isinstance(core, list) or not isinstance(owners, list):
        raise ValueError("release package authority pins are unavailable")
    rows: list[dict[str, str]] = []
    for row in [*core, *owners]:
        if not isinstance(row, dict):
            raise ValueError("release package authority pin is malformed")
        package_id = row.get("package_id")
        version = row.get("version")
        digest = row.get("sha256")
        if (
            not isinstance(package_id, str) or not package_id.startswith("Chummer.")
            or not isinstance(version, str) or not version
            or not isinstance(digest, str) or len(digest) != 64
        ):
            raise ValueError("release package authority pin identity is malformed")
        rows.append({"packageId": package_id, "version": version, "nupkgSha256": digest})
    expected_ids = {
        "Chummer.Engine.Contracts", "Chummer.Application", "Chummer.Infrastructure",
        "Chummer.Rulesets.Hosting", "Chummer.Rulesets.Sr4", "Chummer.Rulesets.Sr5",
        "Chummer.Rulesets.Sr6", "Chummer.Campaign.Contracts", "Chummer.Play.Contracts",
        "Chummer.Run.Contracts", "Chummer.Hub.Registry.Contracts", "Chummer.Ui.Kit",
    }
    if len(rows) != 12 or {row["packageId"] for row in rows} != expected_ids:
        raise ValueError("release package authority must bind the exact twelve-package Chummer closure")
    return sorted(rows, key=lambda row: row["packageId"])


def _feed_identity(info: os.stat_result) -> tuple[int, ...]:
    return (info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_nlink,
            info.st_size, info.st_mtime_ns, info.st_ctime_ns)


def _feed_read(directory: int, name: str, limit: int) -> bytes:
    """Read one bounded regular file through the captured directory, never a link."""
    descriptor = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=directory)
    try:
        before = os.fstat(descriptor)
        if (not stat.S_ISREG(before.st_mode) or before.st_uid != os.getuid()
            or before.st_mode & 0o077 or before.st_nlink != 1 or not 0 < before.st_size <= limit):
            raise ValueError("feed input must be bounded, private and singly linked")
        chunks, size = [], 0
        while chunk := os.read(descriptor, min(1024 * 1024, limit + 1 - size)):
            size += len(chunk)
            if size > limit:
                raise ValueError("feed input exceeds its byte limit")
            chunks.append(chunk)
        if (size != before.st_size or _feed_identity(before) != _feed_identity(os.fstat(descriptor))
            or _feed_identity(before) != _feed_identity(os.stat(name, dir_fd=directory, follow_symlinks=False))):
            raise ValueError("feed input changed during snapshot")
        return b"".join(chunks)
    finally:
        os.close(descriptor)


def _feed_names(directory: int) -> dict[str, str]:
    names = {}
    with os.scandir(directory) as entries:
        for entry in entries:
            if len(names) >= FEED_PACKAGE_COUNT or entry.name.lower() in names:
                raise ValueError("offline feed has too many or duplicate filenames")
            names[entry.name.lower()] = entry.name
    return names


def _external_packages(data: bytes, owners: list[dict[str, str]]) -> list[dict[str, str]]:
    lock = _strict_json_bytes(data, "offline project lock")
    dependencies = lock.get("dependencies")
    if (type(lock.get("version")) is not int or lock["version"] not in {1, 2}
        or not isinstance(dependencies, dict) or set(dependencies) != EXPECTED_ANDROID_TARGETS):
        raise ValueError("offline project lock targets or version are not exact")
    selected: dict[str, dict[str, str]] = {}
    owner_versions = {row["packageId"].lower(): row["version"] for row in owners}
    seen_owners = set()
    for rows in dependencies.values():
        if not isinstance(rows, dict) or len(rows) > FEED_PACKAGE_COUNT:
            raise ValueError("offline project lock package table is invalid")
        per_target = set()
        for package_id, row in rows.items():
            key = package_id.lower()
            if not PACKAGE_ID.fullmatch(package_id) or key in per_target or not isinstance(row, dict):
                raise ValueError("offline project lock has duplicate or invalid identities")
            per_target.add(key)
            if row.get("type") == "Project":
                if key not in {name.lower() for name in EXPECTED_SOURCE_PROJECTS}:
                    raise ValueError("offline project lock has an unexpected source project")
                continue
            version, content_hash = row.get("resolved"), row.get("contentHash")
            if (row.get("type") not in {"Direct", "Transitive"} or not isinstance(version, str)
                or len(version) > 100 or not PACKAGE_VERSION.fullmatch(version)
                or not isinstance(content_hash, str) or len(content_hash) != 88):
                raise ValueError("offline project lock package identity or contentHash is malformed")
            try:
                digest = base64.b64decode(content_hash, validate=True)
            except ValueError as error:
                raise ValueError("offline project lock contentHash is malformed") from error
            if len(digest) != 64 or base64.b64encode(digest).decode() != content_hash:
                raise ValueError("offline project lock contentHash is malformed")
            value = {"packageId": package_id, "version": version, "contentHash": content_hash}
            if key in selected and selected[key] != value:
                raise ValueError("offline project lock has conflicting identities")
            selected[key] = value
            if key.startswith("chummer."):
                if owner_versions.get(key) != version:
                    raise ValueError("offline project lock differs from Chummer owner authority")
                seen_owners.add(key)
    if seen_owners != set(owner_versions) or len(selected) > FEED_PACKAGE_COUNT:
        raise ValueError("offline project lock owner closure or package count is not exact")
    return [row for key, row in sorted(selected.items()) if key not in owner_versions]


def _version_identity(value: str) -> tuple[tuple[int, ...], str]:
    # Nuspec may spell 1.0 as 1.0.0; build metadata does not select a NuGet version.
    if len(value) > 100 or not PACKAGE_VERSION.fullmatch(value):
        raise ValueError("offline package nuspec version is malformed")
    numeric, _, suffix = value.split("+", 1)[0].partition("-")
    parts = tuple(int(part) for part in numeric.split("."))
    return parts + (0,) * (4 - len(parts)), suffix.lower()


def _admit_zip_directory(data: bytes) -> None:
    """Bound physical metadata before ZipFile allocates one object per entry.

    Admit contiguous single-disk ZIP and fixed ZIP64 end records. Multipart,
    prepended archives, ZIP64 extensible data and Unicode-name overrides are
    unsupported; no payload extraction or declared-count-based allocation.
    """
    if not 22 <= len(data) <= FEED_ARCHIVE_BYTES or data[:4] != b"PK\x03\x04":
        raise ValueError("offline archive byte size is invalid")
    end = data.rfind(b"PK\x05\x06", max(0, len(data) - 22 - 65535))
    if end < 0 or end + 22 > len(data):
        raise ValueError("offline archive end record is invalid")
    _, disk, directory_disk, disk_count, count, size, start, comment = struct.unpack_from("<4s4H2IH", data, end)
    if end + 22 + comment != len(data) or disk or directory_disk or disk_count != count:
        raise ValueError("offline archive end span or disk layout is unsupported")
    directory_end = end
    if end >= 20 and data[end - 20:end - 16] == b"PK\x06\x07":
        _, disk64, offset64, disks64 = struct.unpack_from("<4sIQI", data, end - 20)
        if disk64 or disks64 != 1 or offset64 + 56 != end - 20:
            raise ValueError("offline archive ZIP64 span is unsupported")
        record = struct.unpack_from("<4sQHHIIQQQQ", data, offset64)
        signature, record_size, _, _, disk64, directory_disk64, disk_count64, count64, size64, start64 = record
        if (signature != b"PK\x06\x06" or record_size != 44 or disk64 or directory_disk64
            or disk_count64 != count64 or count not in {65535, count64}
            or size not in {0xffffffff, size64} or start not in {0xffffffff, start64}):
            raise ValueError("offline archive ZIP64 end record is unsupported")
        count, size, start, directory_end = count64, size64, start64, offset64
    elif count == 65535 or size == 0xffffffff or start == 0xffffffff:
        raise ValueError("offline archive ZIP64 end record is absent")
    if not 0 < size <= FEED_CENTRAL_BYTES or start + size != directory_end:
        raise ValueError("offline archive central directory span is invalid")
    actual, position = 0, start
    while position < directory_end:
        # Count real records, even when the EOCD maliciously declares fewer.
        if actual >= FEED_MEMBER_COUNT or directory_end - position < 46:
            raise ValueError("offline archive physical member count or header is invalid")
        header = struct.unpack_from("<4s6H3I5H2I", data, position)
        name_size, extra_size, comment_size, member_disk = header[10:14]
        record_size = 46 + name_size + extra_size + comment_size
        if (header[0] != b"PK\x01\x02" or member_disk or not 0 < name_size <= FEED_MEMBER_NAME_BYTES
            or record_size > FEED_ZIP_RECORD_BYTES or position + record_size > directory_end):
            raise ValueError("offline archive central member record is invalid")
        name_end = position + 46 + name_size
        name = data[position + 46:name_end]
        if name.rstrip(b"/").count(b"/") + 1 > FEED_MEMBER_DEPTH:
            raise ValueError("offline archive member path is too deep")
        extra_position, extra_end = name_end, name_end + extra_size
        while extra_position < extra_end:
            if extra_end - extra_position < 4:
                raise ValueError("offline archive extra field is invalid")
            kind, length = struct.unpack_from("<HH", data, extra_position)
            extra_position += 4 + length
            if extra_position > extra_end or kind == 0x7075:
                raise ValueError("offline archive extra field or name override is unsupported")
        position += record_size
        actual += 1
    if not actual or actual != count:
        raise ValueError("offline archive declared and physical member counts differ")


def _external_archive(data: bytes, package: Mapping[str, str]) -> int:
    """Check transport safety/identity, not signatures or NuGet's content hash."""
    try:
        _admit_zip_directory(data)
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            members = archive.infolist()
            if not 0 < len(members) <= FEED_MEMBER_COUNT:
                raise ValueError("offline archive member count is invalid")
            total, names, files, nuspec = 0, set(), set(), None
            for member in members:
                name = member.filename.rstrip("/")
                parts = name.split("/")
                folded = name.lower()
                mode = stat.S_IFMT(member.external_attr >> 16)
                if (member.orig_filename != member.filename or not name or "\\" in name or ":" in name
                    or any(ord(character) < 32 for character in name)
                    or any(part in {"", ".", ".."} for part in parts) or folded in names
                    or member.flag_bits & 1 or mode not in {0, stat.S_IFREG, stat.S_IFDIR}
                    or member.compress_type not in {zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED}
                    or (mode == stat.S_IFDIR and not member.is_dir())):
                    raise ValueError("offline archive contains unsafe or duplicate members")
                names.add(folded)
                if not member.is_dir():
                    files.add(folded)
                total += member.file_size
                if total > FEED_EXPANDED_BYTES:
                    raise ValueError("offline archive exceeds expanded-byte limit")
                if len(parts) == 1 and name.lower().endswith(".nuspec"):
                    if nuspec is not None or member.file_size > FEED_NUSPEC_BYTES:
                        raise ValueError("offline archive nuspec is absent or ambiguous")
                    nuspec = member
            if any("/".join(name.split("/")[:index]) in files
                   for name in names for index in range(1, len(name.split("/")))):
                raise ValueError("offline archive has conflicting file and directory paths")
            if nuspec is None:
                raise ValueError("offline archive nuspec is absent")
            # Stream every member to check CRC/truncation without extracting host files.
            for member in members:
                consumed = 0
                with archive.open(member) as stream:
                    while chunk := stream.read(1024 * 1024):
                        consumed += len(chunk)
                        if consumed > member.file_size:
                            raise ValueError("offline archive member exceeds declared size")
                if consumed != member.file_size:
                    raise ValueError("offline archive member is truncated")
            try:
                xml = archive.read(nuspec).decode("utf-8-sig")
            except UnicodeError as error:
                raise ValueError("offline archive nuspec must be UTF-8") from error
            declaration = re.match(r"\s*<\?xml\b[^?]*\?>", xml)
            encodings = re.findall(r"encoding\s*=\s*['\"]([^'\"]+)['\"]", declaration[0]) if declaration else []
            if "\x00" in xml or any(value.lower() != "utf-8" for value in encodings):
                raise ValueError("offline archive nuspec must be UTF-8")
            if "<!DOCTYPE" in xml.upper() or "<!ENTITY" in xml.upper():
                raise ValueError("offline archive nuspec declarations are forbidden")
            root = ET.fromstring(xml)
            metadata = [node for node in root if node.tag.rsplit("}", 1)[-1] == "metadata"]
            if root.tag.rsplit("}", 1)[-1] != "package" or len(metadata) != 1:
                raise ValueError("offline archive nuspec metadata is malformed")
            values = {}
            for key in ("id", "version"):
                matches = [node.text for node in metadata[0] if node.tag.rsplit("}", 1)[-1] == key]
                if len(matches) != 1 or not isinstance(matches[0], str):
                    raise ValueError("offline archive nuspec identity is malformed")
                values[key] = matches[0]
            if (values["id"].lower() != package["packageId"].lower()
                or _version_identity(values["version"]) != _version_identity(package["version"])):
                raise ValueError("offline archive nuspec differs from locked identity")
            return total
    except (zipfile.BadZipFile, ET.ParseError, RuntimeError, NotImplementedError, EOFError,
            zlib.error, struct.error, UnicodeError) as error:
        raise ValueError("offline package archive is invalid") from error


def _snapshot_external_feed(source: Path, destination: Path, external: Path, project_lock: Path,
                            owners: list[dict[str, str]]) -> dict[str, Any]:
    roots = [source, destination, external, project_lock.parent]
    for root in roots:
        _owned_directory(root, "offline snapshot root")
    _private_directory(external, "offline external feed")
    for left, right in ((source, destination), (external, destination), (source, external)):
        if left == right or left in right.parents or right in left.parents:
            raise ValueError("offline snapshot directories must be disjoint")
    if project_lock.parent == destination or destination in project_lock.parents:
        raise ValueError("offline project lock overlaps the snapshot destination")
    descriptors = []
    try:
        for root in roots:
            before = _feed_identity(root.lstat())
            descriptor = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            descriptors.append(descriptor)
            if _feed_identity(os.fstat(descriptor)) != before:
                raise ValueError("offline snapshot root changed while opening")
        source_fd, destination_fd, external_fd, lock_fd = descriptors
        if _feed_names(destination_fd):
            raise ValueError("offline snapshot destination must be empty")
        identities = [_feed_identity(os.fstat(fd)) for fd in descriptors]
        lock_bytes = _feed_read(lock_fd, project_lock.name, FEED_LOCK_BYTES)
        packages = _external_packages(lock_bytes, owners)
        plan = []
        for fd, rows, exact in ((source_fd, owners, False), (external_fd, packages, True)):
            entries = _feed_names(fd)
            expected = {f'{row["packageId"]}.{row["version"]}.nupkg'.lower() for row in rows}
            if (exact and set(entries) != expected) or not expected <= set(entries):
                raise ValueError("offline feed package membership is not exact")
            plan.extend((fd, entries[f'{row["packageId"]}.{row["version"]}.nupkg'.lower()], row, exact)
                        for row in rows)
        total, expanded, inventory = 0, 0, []
        for fd, name, row, is_external in plan:
            data = _feed_read(fd, name, min(FEED_ARCHIVE_BYTES, FEED_TOTAL_BYTES - total))
            total += len(data)
            digest = hashlib.sha256(data).hexdigest()
            if is_external:
                expanded += _external_archive(data, row)
                if expanded > FEED_TOTAL_EXPANDED_BYTES:
                    raise ValueError("offline feed exceeds expanded-byte limit")
            elif digest != row["nupkgSha256"]:
                raise ValueError("selected retained package digest drifted")
            output = f'{row["packageId"]}.{row["version"]}.nupkg'
            descriptor = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                                 0o600, dir_fd=destination_fd)
            try:
                remaining = memoryview(data)
                while remaining:
                    written = os.write(descriptor, remaining)
                    if written <= 0:
                        raise ValueError("offline snapshot write was incomplete")
                    remaining = remaining[written:]
                os.fsync(descriptor)
            finally:
                os.close(descriptor)
            inventory.append({"path": output, "sizeBytes": len(data), "sha256": digest})
        os.fsync(destination_fd)
        inventory.sort(key=lambda row: row["path"])
        if set(_feed_names(destination_fd).values()) != {row["path"] for row in inventory}:
            raise ValueError("offline snapshot membership changed")
        for row in inventory:
            copied = _feed_read(destination_fd, row["path"], row["sizeBytes"])
            if len(copied) != row["sizeBytes"] or hashlib.sha256(copied).hexdigest() != row["sha256"]:
                raise ValueError("offline snapshot bytes changed")
        for index, (root, fd) in enumerate(zip(roots, descriptors)):
            current = os.fstat(fd)
            if _feed_identity(current) != _feed_identity(root.lstat()):
                raise ValueError("offline snapshot directory changed")
            if index != 1 and _feed_identity(current) != identities[index]:
                raise ValueError("offline snapshot input directory changed")
        if _feed_read(lock_fd, project_lock.name, FEED_LOCK_BYTES) != lock_bytes:
            raise ValueError("offline project lock changed during snapshot")
        # Native locked restore validates contentHash, including signed packages.
        # Raw archive SHA256 here binds transport bytes only.
        return {"selectedPackageCount": len(plan), "selectedOwnerPackageCount": len(owners),
                "selectedExternalPackageCount": len(packages), "selectedPackages": owners,
                "inventory": inventory, "inventorySha256": _inventory_digest(inventory),
                "publicationAuthorized": False}
    finally:
        for descriptor in reversed(descriptors):
            os.close(descriptor)


def snapshot_feed(authority_path: Path, source: Path, destination: Path, *,
                  external_source: Path | None = None, project_lock: Path | None = None) -> dict[str, Any]:
    if external_source is not None or project_lock is not None:
        _owned_directory(authority_path.parent, "offline authority root")
        descriptor = os.open(authority_path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            authority_data = _feed_read(descriptor, authority_path.name, FEED_LOCK_BYTES)
        finally:
            os.close(descriptor)
    else:
        authority_data, _ = _stable_file(authority_path, "release package authority")
    authority = _strict_json_bytes(authority_data, "release package authority")
    packages = _authority_packages(authority)
    source = _owned_directory(source, "retained package feed")
    destination = _private_directory(destination, "private selected package feed",
                                     empty=external_source is None and project_lock is None)
    if external_source is not None or project_lock is not None:
        if external_source is None or project_lock is None:
            raise ValueError("offline snapshot requires both external feed and project lock")
        return _snapshot_external_feed(source, destination, external_source, project_lock, packages)
    source_files = {path.name.lower(): path for path in source.iterdir() if path.is_file()}
    for package in packages:
        name = f'{package["packageId"]}.{package["version"]}.nupkg'
        source_path = source_files.get(name.lower())
        if source_path is None:
            raise ValueError(f"selected package is absent from the retained feed: {name}")
        data, _ = _stable_file(source_path, f"retained package {name}")
        if hashlib.sha256(data).hexdigest() != package["nupkgSha256"]:
            raise ValueError(f"selected retained package digest drifted: {name}")
        output = destination / name
        descriptor = os.open(
            output,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
            0o600,
        )
        try:
            os.write(descriptor, data)
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
    inventory = _tree_inventory(destination, "private selected package feed")
    return {
        "selectedPackageCount": len(packages),
        "selectedPackages": packages,
        "inventory": inventory,
        "inventorySha256": _inventory_digest(inventory),
        "publicationAuthorized": False,
    }


def _resolve_project_path(raw: object, project_dir: Path, label: str) -> Path:
    if not isinstance(raw, str) or not raw or not raw.endswith(".csproj"):
        raise ValueError(f"{label} is not one project path")
    candidate = Path(raw)
    try:
        resolved = (candidate if candidate.is_absolute() else project_dir / candidate).resolve(
            strict=True
        )
    except OSError as error:
        raise ValueError(f"{label} does not resolve to a project") from error
    if not resolved.is_file() or resolved.is_symlink():
        raise ValueError(f"{label} is not one regular non-symlinked project")
    return resolved


def _dgspec_projects(
    dgspec: Mapping[str, Any], workspace_root: Path, project_dir: Path,
) -> list[dict[str, str]]:
    root_project = project_dir / "Chummer.Android.csproj"
    expected_projects = {
        (workspace_root / relative).resolve(strict=True)
        for _version, relative in EXPECTED_SOURCE_PROJECTS.values()
    }
    expected_graph = {root_project.resolve(strict=True), *expected_projects}
    projects = dgspec.get("projects")
    if not isinstance(projects, dict):
        raise ValueError("restore dgspec projects are unavailable")
    actual: dict[Path, Mapping[str, Any]] = {}
    for raw_identity, metadata in projects.items():
        path = _resolve_project_path(raw_identity, project_dir, "restore dgspec project identity")
        if path in actual or not isinstance(metadata, dict):
            raise ValueError("restore dgspec project identity is duplicated or malformed")
        actual[path] = metadata
    if set(actual) != expected_graph:
        raise ValueError("restore dgspec does not bind the exact three-project source graph")
    presentation_path = (
        workspace_root / EXPECTED_SOURCE_PROJECTS["Chummer.Presentation"][1]
    ).resolve(strict=True)
    desktop_path = (
        workspace_root / EXPECTED_SOURCE_PROJECTS["Chummer.Desktop.Runtime"][1]
    ).resolve(strict=True)
    expected_restore_graph = {
        root_project.resolve(strict=True): (
            "net10.0-android36.0", {desktop_path, presentation_path}
        ),
        desktop_path: ("net10.0", {presentation_path}),
        presentation_path: ("net10.0", set()),
    }
    for project_path, metadata in actual.items():
        restore = metadata.get("restore")
        if not isinstance(restore, dict):
            raise ValueError("restore dgspec project restore binding is unavailable")
        for key in ("projectPath", "projectUniqueName"):
            if _resolve_project_path(
                restore.get(key), project_dir, f"restore dgspec {key}"
            ) != project_path:
                raise ValueError("restore dgspec project self-binding is incorrect")
        expected_framework, expected_references = expected_restore_graph[project_path]
        restore_frameworks = restore.get("frameworks")
        if (
            not isinstance(restore_frameworks, dict)
            or set(restore_frameworks) != {expected_framework}
        ):
            raise ValueError("restore dgspec project framework is not exact")
        framework = restore_frameworks[expected_framework]
        references = framework.get("projectReferences") if isinstance(framework, dict) else None
        if not isinstance(references, dict):
            raise ValueError("restore dgspec project references are unavailable")
        actual_references: set[Path] = set()
        for raw_reference, binding in references.items():
            reference = _resolve_project_path(
                raw_reference, project_dir, "restore dgspec project reference"
            )
            if reference in actual_references or not isinstance(binding, dict):
                raise ValueError("restore dgspec duplicates or malforms a project reference")
            if _resolve_project_path(
                binding.get("projectPath"), project_dir,
                "restore dgspec project reference binding",
            ) != reference:
                raise ValueError("restore dgspec project reference binding is incorrect")
            actual_references.add(reference)
        if actual_references != expected_references:
            raise ValueError("restore dgspec project references are not exact")
    return [
        {"path": path.relative_to(workspace_root).as_posix()}
        for path in sorted(actual)
    ]


def _closure(
    assets: Mapping[str, Any], dgspec: Mapping[str, Any], packages_root: Path,
    expected: list[dict[str, str]], workspace_root: Path,
) -> tuple[list[dict[str, str]], list[dict[str, str]], list[dict[str, str]]]:
    package_folders = assets.get("packageFolders")
    if (
        not isinstance(package_folders, dict)
        or len(package_folders) != 1
        or Path(next(iter(package_folders))).resolve(strict=True) != packages_root
    ):
        raise ValueError("project.assets.json package root does not bind the isolated cache")
    libraries = assets.get("libraries")
    if not isinstance(libraries, dict):
        raise ValueError("project.assets.json libraries are unavailable")
    project_restore = assets.get("project")
    restore = project_restore.get("restore") if isinstance(project_restore, dict) else None
    project_dir = workspace_root / "chummer-android/src/Chummer.Android"
    if (
        not isinstance(restore, dict)
        or _resolve_project_path(
            restore.get("projectPath"), project_dir, "project.assets.json restore project"
        ) != (project_dir / "Chummer.Android.csproj").resolve(strict=True)
    ):
        raise ValueError("project.assets.json is not bound to the exact Android project")
    selected: dict[str, tuple[str, Mapping[str, Any]]] = {}
    source_projects: dict[str, tuple[str, Mapping[str, Any]]] = {}
    for identity, row in libraries.items():
        malformed_chummer = (
            isinstance(identity, str) and identity.startswith("Chummer.")
            and ("/" not in identity or not isinstance(row, dict))
        )
        if malformed_chummer:
            raise ValueError("project.assets.json contains a malformed Chummer library identity")
        if not isinstance(identity, str) or "/" not in identity or not isinstance(row, dict):
            continue
        package_id, version = identity.rsplit("/", 1)
        library_type = row.get("type")
        if library_type == "project":
            if package_id in source_projects:
                raise ValueError(
                    f"project.assets.json duplicates source project identity: {package_id}"
                )
            source_projects[package_id] = (version, row)
        elif not package_id.startswith("Chummer."):
            continue
        elif library_type == "package":
            if package_id in selected:
                raise ValueError(
                    f"project.assets.json duplicates Chummer package identity: {package_id}"
                )
            selected[package_id] = (version, row)
        else:
            raise ValueError(f"project.assets.json Chummer library type is invalid: {package_id}")
    expected_by_id = {row["packageId"]: row for row in expected}
    if set(selected) != set(expected_by_id):
        raise ValueError("project.assets.json does not select the exact twelve-package Chummer closure")
    if set(source_projects) != set(EXPECTED_SOURCE_PROJECTS):
        raise ValueError("project.assets.json source project references are not exact")
    expected_project_identities = {
        f"{project_id}/{version}"
        for project_id, (version, _path) in EXPECTED_SOURCE_PROJECTS.items()
    }
    targets = assets.get("targets")
    if not isinstance(targets, dict) or set(targets) != EXPECTED_ANDROID_TARGETS:
        raise ValueError("project.assets.json Android targets are not exact")
    for target_name, target in targets.items():
        if not isinstance(target, dict):
            raise ValueError("project.assets.json target is malformed")
        target_project_identities: set[str] = set()
        for identity, row in target.items():
            if isinstance(row, dict) and row.get("type") == "project":
                if not isinstance(identity, str):
                    raise ValueError("project.assets.json target project identity is malformed")
                target_project_identities.add(identity)
        if target_project_identities != expected_project_identities:
            raise ValueError(
                f"project.assets.json target project identities are not exact: {target_name}"
            )
    run_version, _run_row = selected["Chummer.Run.Contracts"]
    run_target_rows = []
    if isinstance(targets, dict):
        for target in targets.values():
            if isinstance(target, dict):
                row = target.get(f"Chummer.Run.Contracts/{run_version}")
                if isinstance(row, dict):
                    run_target_rows.append(row)
    if not run_target_rows or any(
        not isinstance(row.get("dependencies"), dict)
        or row["dependencies"].get("Chummer.Play.Contracts")
        != selected["Chummer.Play.Contracts"][0]
        for row in run_target_rows
    ):
        raise ValueError("project.assets.json Run.Contracts does not bind exact Play.Contracts")
    result: list[dict[str, str]] = []
    for package_id in sorted(selected):
        version, row = selected[package_id]
        expected_row = expected_by_id[package_id]
        if version != expected_row["version"]:
            raise ValueError(f"project.assets.json selected version drifted: {package_id}")
        sha512 = row.get("sha512")
        path = row.get("path")
        if not isinstance(sha512, str) or not sha512 or not isinstance(path, str):
            raise ValueError(f"project.assets.json package metadata is incomplete: {package_id}")
        package_path = packages_root / PurePosixPath(path)
        rows = _tree_inventory(package_path, f"selected package directory {package_id}")
        sha_file = package_path / f"{package_id.lower()}.{version}.nupkg.sha512"
        sha_bytes, _ = _stable_file(sha_file, f"selected package sha512 {package_id}")
        if sha_bytes.decode("ascii").strip() != sha512:
            raise ValueError(f"selected package cache sha512 drifted: {package_id}")
        try:
            base64.b64decode(sha512, validate=True)
        except ValueError as error:
            raise ValueError(f"selected package cache sha512 is malformed: {package_id}") from error
        result.append({
            "packageId": package_id,
            "version": version,
            "contentSha512": sha512,
            "packageDirectorySha256": _inventory_digest(rows),
            "authorityNupkgSha256": expected_row["nupkgSha256"],
        })
    project_result: list[dict[str, str]] = []
    for project_id in sorted(source_projects):
        version, row = source_projects[project_id]
        expected_version, expected_relative = EXPECTED_SOURCE_PROJECTS[project_id]
        expected_path = (workspace_root / expected_relative).resolve(strict=True)
        if version != expected_version:
            raise ValueError(
                f"project.assets.json source project version is not exact: {project_id}"
            )
        path = row.get("path")
        msbuild_project = row.get("msbuildProject")
        if (
            not isinstance(path, str) or not path
            or not isinstance(msbuild_project, str) or not msbuild_project
        ):
            raise ValueError(
                f"project.assets.json source project metadata is incomplete: {project_id}"
            )
        if (
            _resolve_project_path(path, project_dir, f"{project_id}.path") != expected_path
            or _resolve_project_path(
                msbuild_project, project_dir, f"{project_id}.msbuildProject"
            ) != expected_path
        ):
            raise ValueError(
                f"project.assets.json source project path is not exact: {project_id}"
            )
        project_result.append({
            "projectId": project_id,
            "version": version,
            "path": path,
            "msbuildProject": msbuild_project,
            "canonicalPath": expected_relative.as_posix(),
        })
    dgspec_result = _dgspec_projects(dgspec, workspace_root, project_dir)
    return result, project_result, dgspec_result


def materialize_payload(
    *, input_root: Path, workspace_root: Path, authority_path: Path, owner_feed: Path,
    packages_root: Path, routed_lock_root: Path, project_lock: Path, intermediate_root: Path,
) -> dict[str, Any]:
    _restore_roots(input_root, workspace_root, intermediate_root, owner_feed, packages_root, routed_lock_root)
    assert_clean_workspace_build_state(workspace_root)
    root_identity = _root_identity(intermediate_root)
    authority_data, _ = _stable_file(authority_path, "release package authority")
    authority = json.loads(authority_data)
    expected = _authority_packages(authority)
    package_rows = _tree_inventory(packages_root, "isolated global-packages cache")
    intermediate_rows = _restore_inventory(intermediate_root)
    by_path = {row["path"]: row for row in intermediate_rows}
    assets_path = intermediate_root / PRIMARY_ASSETS
    assets = _strict_json(assets_path, "project.assets.json")
    dgspec_path = intermediate_root / PRIMARY_DGSPEC
    dgspec = _strict_json(dgspec_path, "restore dgspec")
    chummer_closure, source_projects, dgspec_projects = _closure(
        assets, dgspec, packages_root, expected, workspace_root
    )
    lock_row = _file_row(project_lock, project_lock.name, "packages.lock.json")
    if _root_identity(intermediate_root) != root_identity:
        raise ValueError("isolated restore intermediate root changed during capture")
    _require_inventory_unchanged(
        _restore_inventory(intermediate_root), intermediate_rows,
        label="isolated restore intermediates", message="restore intermediates changed during capture",
    )
    return {
        "contractName": CONTRACT,
        "publicationAuthorized": False,
        "inputRoot": os.fspath(input_root),
        "workspaceRoot": os.fspath(workspace_root),
        "authoritySha256": hashlib.sha256(authority_data).hexdigest(),
        "projectLock": lock_row,
        "projectAssets": by_path[PRIMARY_ASSETS],
        "dependencyGraphSpec": by_path[PRIMARY_DGSPEC],
        "chummerClosure": chummer_closure,
        "sourceProjectReferences": source_projects,
        "dependencyGraphProjects": dgspec_projects,
        "ownerFeed": {
            "files": _tree_inventory(owner_feed, "private selected package feed"),
        },
        "packages": {"files": package_rows, "inventorySha256": _inventory_digest(package_rows)},
        "routedProjectLocks": {
            "files": _routed_lock_inventory(routed_lock_root),
        },
        "restoreIntermediates": {
            "root": os.fspath(intermediate_root),
            "identity": root_identity,
            "files": intermediate_rows,
            "inventorySha256": _inventory_digest(intermediate_rows),
        },
        "workspaceBuildState": {"roots": [], "files": [], "inventorySha256": _inventory_digest([])},
        "buildOutputsInitiallyEmpty": True,
    }


def _write_exclusive(path: Path, payload: Mapping[str, Any]) -> None:
    _private_directory(path.parent, "restore consumption manifest parent")
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        0o600,
    )
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def _write_drift_diagnostic(
    path: Path,
    *,
    input_root: Path,
    workspace_root: Path,
    manifest_sha256: str,
    error: InventoryDriftError,
) -> None:
    if not path.is_absolute() or path.exists() or path.is_symlink():
        raise ValueError("restore drift diagnostic output must be a fresh absolute path")
    input_root = input_root.resolve(strict=True)
    parent = _private_directory(path.parent, "restore drift diagnostic parent")
    if parent != input_root.parent:
        raise ValueError(
            "restore drift diagnostic output must be an owner-only sibling of the release input root"
        )
    _outside(parent, workspace_root.resolve(strict=True))
    _write_exclusive(
        path,
        {
            "contractName": DRIFT_DIAGNOSTIC_CONTRACT,
            "publicationAuthorized": False,
            "status": "blocked",
            "error": str(error),
            "restoreConsumptionManifestSha256": manifest_sha256,
            "drift": error.diagnostic,
        },
    )


def _rows_by_path(rows: object, label: str) -> dict[str, Mapping[str, Any]]:
    if not isinstance(rows, list):
        raise ValueError(f"{label} inventory is malformed")
    result: dict[str, Mapping[str, Any]] = {}
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"path", "sizeBytes", "sha256"}:
            raise ValueError(f"{label} inventory row is malformed")
        path = row.get("path")
        if (not isinstance(path, str) or not path or path in result
            or PurePosixPath(path).is_absolute() or "\\" in path
            or ".." in PurePosixPath(path).parts or PurePosixPath(path).as_posix() != path
            or type(row.get("sizeBytes")) is not int or row["sizeBytes"] < 0
            or not isinstance(row.get("sha256"), str) or len(row["sha256"]) != 64
            or any(character not in "0123456789abcdef" for character in row["sha256"])):
            raise ValueError(f"{label} inventory path is malformed")
        result[path] = row
    return result


def _inventory_drift(
    actual_rows: object,
    expected_rows: object,
    *,
    label: str,
) -> dict[str, Any] | None:
    actual = _rows_by_path(actual_rows, f"actual {label}")
    expected = _rows_by_path(expected_rows, f"sealed {label}")
    added_paths = sorted(actual.keys() - expected.keys())
    removed_paths = sorted(expected.keys() - actual.keys())
    changed_paths = sorted(
        path for path in actual.keys() & expected.keys()
        if actual[path] != expected[path]
    )
    if not added_paths and not removed_paths and not changed_paths:
        return None

    return {
        "label": label,
        "actualInventorySha256": _inventory_digest(
            actual[path] for path in sorted(actual)
        ),
        "sealedInventorySha256": _inventory_digest(
            expected[path] for path in sorted(expected)
        ),
        "addedCount": len(added_paths),
        "removedCount": len(removed_paths),
        "changedCount": len(changed_paths),
        "exact": True,
        "added": [dict(actual[path]) for path in added_paths],
        "removed": [dict(expected[path]) for path in removed_paths],
        "changed": [
            {
                "path": path,
                "sealed": dict(expected[path]),
                "actual": dict(actual[path]),
            }
            for path in changed_paths
        ],
    }


def _require_inventory_unchanged(
    actual_rows: object,
    expected_rows: object,
    *,
    label: str,
    message: str,
) -> None:
    drift = _inventory_drift(actual_rows, expected_rows, label=label)
    if drift is not None:
        raise InventoryDriftError(message, drift)


def verify_post_publish(
    manifest: Mapping[str, Any], *, packages_root: Path, workspace_root: Path,
    owner_feed: Path, routed_lock_root: Path, project_lock: Path, intermediate_root: Path,
    phase: str,
) -> None:
    if manifest.get("contractName") != CONTRACT or manifest.get("publicationAuthorized") is not False:
        raise ValueError("restore consumption manifest posture is not exact")
    if phase not in VERIFY_PHASES:
        raise ValueError("restore verification phase must be pre-publish or post-publish")
    input_root = manifest.get("inputRoot")
    if not isinstance(input_root, str):
        raise ValueError("restore consumption manifest input root is absent")
    _restore_roots(Path(input_root), workspace_root, intermediate_root, owner_feed, packages_root, routed_lock_root)
    if manifest.get("workspaceRoot") != os.fspath(workspace_root):
        raise ValueError("restore consumption manifest workspace root drifted")
    restore = manifest.get("restoreIntermediates")
    if (not isinstance(restore, dict) or set(restore) != {"root", "identity", "files", "inventorySha256"}
        or restore.get("root") != os.fspath(intermediate_root)
        or restore.get("identity") != _root_identity(intermediate_root)):
        raise ValueError("restore consumption manifest intermediate root drifted or is malformed")
    sealed = _rows_by_path(restore.get("files"), "sealed restore intermediates")
    if (restore.get("inventorySha256") != _inventory_digest(restore["files"])
        or manifest.get("projectAssets") != sealed.get(PRIMARY_ASSETS)
        or manifest.get("dependencyGraphSpec") != sealed.get(PRIMARY_DGSPEC)
        or PRIMARY_ASSETS not in sealed or PRIMARY_DGSPEC not in sealed
        or manifest.get("buildOutputsInitiallyEmpty") is not True
        or manifest.get("workspaceBuildState") != {"roots": [], "files": [], "inventorySha256": _inventory_digest([])}):
        raise ValueError("restore consumption manifest intermediate inventory is inconsistent")
    actual_packages = _tree_inventory(packages_root, "isolated global-packages cache")
    expected_packages = manifest.get("packages", {}).get("files") if isinstance(manifest.get("packages"), dict) else None
    _require_inventory_unchanged(
        actual_packages,
        expected_packages,
        label="isolated global-packages cache",
        message="isolated global-packages cache changed after restore",
    )
    actual_feed = _tree_inventory(owner_feed, "private selected package feed")
    expected_feed = manifest.get("ownerFeed", {}).get("files") if isinstance(manifest.get("ownerFeed"), dict) else None
    _require_inventory_unchanged(
        actual_feed,
        expected_feed,
        label="private selected package feed",
        message="private selected package feed changed after snapshot",
    )
    actual_locks = _routed_lock_inventory(routed_lock_root)
    expected_locks = (
        manifest.get("routedProjectLocks", {}).get("files")
        if isinstance(manifest.get("routedProjectLocks"), dict) else None
    )
    _require_inventory_unchanged(
        actual_locks,
        expected_locks,
        label="routed project locks",
        message="routed project locks changed after restore",
    )
    lock = _file_row(project_lock, project_lock.name, "packages.lock.json")
    if lock != manifest.get("projectLock"):
        raise ValueError("packages.lock.json changed after restore")
    actual_rows = _restore_inventory(intermediate_root)
    actual_intermediate = _rows_by_path(actual_rows, "actual intermediates")
    for path, row in sealed.items():
        if actual_intermediate.get(path) != row:
            raise ValueError(f"sealed restore intermediate changed during publish: {path}")
    if phase == "pre-publish":
        assert_clean_workspace_build_state(workspace_root)
        _require_inventory_unchanged(
            actual_rows, restore["files"], label="isolated restore intermediates",
            message="isolated restore intermediates changed before publish",
        )
    else:
        _post_publish_workspace_state(workspace_root)
        for path in actual_intermediate.keys() - sealed.keys():
            if _is_restore_metadata(PurePosixPath(path).name):
                raise ValueError(f"publish introduced unsealed restore metadata: {path}")
    if _root_identity(intermediate_root) != restore["identity"]:
        raise ValueError("isolated restore intermediate root changed during verification")


def verify_context(
    manifest: Mapping[str, Any], *, input_root: Path, workspace_root: Path,
    authority_path: Path, owner_feed: Path, packages_root: Path, routed_lock_root: Path,
    intermediate_root: Path,
) -> None:
    if manifest.get("contractName") != CONTRACT or manifest.get("publicationAuthorized") is not False:
        raise ValueError("restore consumption manifest posture is not exact")
    _restore_roots(input_root, workspace_root, intermediate_root, owner_feed, packages_root, routed_lock_root)
    if manifest.get("inputRoot") != os.fspath(input_root):
        raise ValueError("restore consumption manifest input root drifted")
    authority_data, _ = _stable_file(authority_path, "release package authority")
    if manifest.get("authoritySha256") != hashlib.sha256(authority_data).hexdigest():
        raise ValueError("release package authority changed after restore")
    for path, label in (
        (owner_feed, "private selected package feed"),
        (packages_root, "isolated global-packages cache"),
        (routed_lock_root, "routed project-lock root"),
    ):
        _private_directory(path, label)
        try:
            path.relative_to(input_root)
        except ValueError as error:
            raise ValueError(f"{label} escaped the release restore input root") from error


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="action", required=True)
    snapshot = subparsers.add_parser("snapshot-feed")
    snapshot.add_argument("--authority", required=True, type=Path)
    snapshot.add_argument("--source", required=True, type=Path)
    snapshot.add_argument("--destination", required=True, type=Path)
    snapshot.add_argument("--external-source", type=Path)
    snapshot.add_argument("--project-lock", type=Path)
    clean = subparsers.add_parser("assert-clean")
    clean.add_argument("--workspace-root", required=True, type=Path)
    materialize = subparsers.add_parser("materialize")
    verify = subparsers.add_parser("verify")
    for command in (materialize, verify):
        command.add_argument("--input-root", required=True, type=Path)
        command.add_argument("--workspace-root", required=True, type=Path)
        command.add_argument("--authority", required=True, type=Path)
        command.add_argument("--owner-feed", required=True, type=Path)
        command.add_argument("--packages-root", required=True, type=Path)
        command.add_argument("--routed-lock-root", required=True, type=Path)
        command.add_argument("--intermediate-root", required=True, type=Path)
        command.add_argument("--project-lock", required=True, type=Path)
        command.add_argument("--manifest", required=True, type=Path)
    verify.add_argument("--drift-diagnostic", type=Path)
    verify.add_argument("--phase", required=True, choices=sorted(VERIFY_PHASES))
    args = parser.parse_args()
    manifest_sha256: str | None = None
    try:
        if args.action == "snapshot-feed":
            result = snapshot_feed(args.authority, args.source, args.destination,
                                   external_source=args.external_source, project_lock=args.project_lock)
            print(json.dumps(result, sort_keys=True))
            return 0
        if args.action == "assert-clean":
            assert_clean_workspace_build_state(args.workspace_root)
            print(json.dumps({"contractName": CONTRACT, "status": "clean", "publicationAuthorized": False}, sort_keys=True))
            return 0
        if args.action == "materialize":
            payload = materialize_payload(
                input_root=args.input_root, workspace_root=args.workspace_root,
                authority_path=args.authority, owner_feed=args.owner_feed,
                packages_root=args.packages_root, routed_lock_root=args.routed_lock_root,
                project_lock=args.project_lock, intermediate_root=args.intermediate_root,
            )
            _write_exclusive(args.manifest, payload)
            print(json.dumps({"contractName": CONTRACT, "status": "sealed", "publicationAuthorized": False}, sort_keys=True))
            return 0
        manifest_data, _ = _stable_file(
            args.manifest,
            "restore consumption manifest",
        )
        manifest = _strict_json_bytes(
            manifest_data,
            "restore consumption manifest",
        )
        manifest_sha256 = hashlib.sha256(manifest_data).hexdigest()
        if args.action == "verify":
            verify_context(
                manifest, input_root=args.input_root, workspace_root=args.workspace_root,
                authority_path=args.authority, owner_feed=args.owner_feed,
                packages_root=args.packages_root, routed_lock_root=args.routed_lock_root,
                intermediate_root=args.intermediate_root,
            )
            verify_post_publish(
                manifest, packages_root=args.packages_root, workspace_root=args.workspace_root,
                owner_feed=args.owner_feed, routed_lock_root=args.routed_lock_root,
                project_lock=args.project_lock, intermediate_root=args.intermediate_root, phase=args.phase,
            )
            print(json.dumps({"contractName": CONTRACT, "status": "verified", "publicationAuthorized": False}, sort_keys=True))
            return 0
        raise ValueError("unknown action")
    except (OSError, UnicodeError, ValueError, json.JSONDecodeError) as error:
        blocked: dict[str, Any] = {
            "contractName": CONTRACT,
            "status": "blocked",
            "publicationAuthorized": False,
            "error": str(error),
        }
        drift_output = getattr(args, "drift_diagnostic", None)
        if (
            isinstance(error, InventoryDriftError)
            and drift_output is not None
            and manifest_sha256 is not None
        ):
            try:
                _write_drift_diagnostic(
                    drift_output,
                    input_root=args.input_root,
                    workspace_root=args.workspace_root,
                    manifest_sha256=manifest_sha256,
                    error=error,
                )
            except (OSError, UnicodeError, ValueError, json.JSONDecodeError) as diagnostic_error:
                blocked["diagnosticError"] = str(diagnostic_error)
            else:
                blocked["driftDiagnostic"] = os.fspath(drift_output)
        print(json.dumps(blocked, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
