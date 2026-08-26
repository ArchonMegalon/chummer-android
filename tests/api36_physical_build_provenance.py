#!/usr/bin/env python3
"""Fail-closed source/build authority for physical API-36 proof journeys."""

from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
import zipfile


SCHEMA = "chummer.android.api36-arm64-build-provenance/v1"
AUTHORITY_CLASS = "clean-git-tree-local-build-binding"
PACKAGE = "com.myexternalbrain.chummer"
SHA1 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
REPOSITORY_KEYS = ("android", "core", "presentation")


def _object_without_duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
    value: dict[str, object] = {}
    for key, item in pairs:
        if key in value:
            raise RuntimeError(f"Duplicate JSON key in provenance manifest: {key}")
        value[key] = item
    return value


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(
        value,
        allow_nan=False,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _require_regular(path: Path, label: str) -> None:
    if not path.is_absolute():
        raise RuntimeError(f"{label} must be an absolute path")
    try:
        mode = os.lstat(path).st_mode
    except FileNotFoundError as error:
        raise RuntimeError(f"{label} does not exist: {path}") from error
    if not stat.S_ISREG(mode) or stat.S_ISLNK(mode):
        raise RuntimeError(f"{label} must be one regular non-symlink file")


def apk_abis(path: Path) -> list[str]:
    _require_regular(path, "ARM64 APK")
    try:
        with zipfile.ZipFile(path) as archive:
            result = sorted(
                {
                    parts[1]
                    for name in archive.namelist()
                    if len(parts := name.split("/")) >= 3
                    and parts[0] == "lib"
                    and parts[1]
                }
            )
    except (OSError, zipfile.BadZipFile) as error:
        raise RuntimeError("ARM64 artifact is not a readable APK") from error
    if "arm64-v8a" not in result:
        raise RuntimeError(f"APK has no arm64-v8a native payload: {result!r}")
    return result


def _git(repository: Path, *arguments: str) -> str:
    return subprocess.run(
        ["git", *arguments],
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout.strip()


def repository_identity(repository: Path, *, require_clean: bool = True) -> dict[str, str]:
    repository = repository.resolve()
    if Path(_git(repository, "rev-parse", "--show-toplevel")) != repository:
        raise RuntimeError(f"Repository root is not exact: {repository}")
    commit = _git(repository, "rev-parse", "HEAD")
    tree = _git(repository, "rev-parse", "HEAD^{tree}")
    if SHA1.fullmatch(commit) is None or SHA1.fullmatch(tree) is None:
        raise RuntimeError(f"Repository identity is malformed: {repository}")
    if require_clean:
        status = _git(repository, "status", "--porcelain=v1", "--untracked-files=all")
        if status:
            raise RuntimeError(f"Repository is dirty and cannot mint build authority: {repository}")
    return {"commit": commit, "tree": tree}


def create_manifest(
    *,
    android_root: Path,
    core_root: Path,
    presentation_root: Path,
    apk: Path,
) -> dict[str, object]:
    apk = apk.resolve()
    repositories = {
        "android": repository_identity(android_root),
        "core": repository_identity(core_root),
        "presentation": repository_identity(presentation_root),
    }
    artifact = {
        "basename": apk.name,
        "byteLength": apk.stat().st_size,
        "sha256": file_sha256(apk),
        "abis": apk_abis(apk),
        "package": PACKAGE,
        "apiLevel": 36,
        "configuration": "Debug",
        "runtimeIdentifier": "android-arm64",
        "targetFramework": "net10.0-android36.0",
    }
    authority = {
        "schema": SCHEMA,
        "authorityClass": AUTHORITY_CLASS,
        "releaseAttested": False,
        "repositories": repositories,
        "artifact": artifact,
        "buildCommand": "CHUMMER_ANDROID_RUNTIME_ID=android-arm64 scripts/build-debug.sh",
    }
    return {
        **authority,
        "authoritySha256": canonical_sha256(authority),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    }


def write_manifest(path: Path, manifest: dict[str, object]) -> None:
    path = path.resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    encoded = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile("wb", dir=path.parent, delete=False) as stream:
            temporary = stream.name
            os.fchmod(stream.fileno(), 0o600)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def load_and_verify_manifest(
    manifest_path: Path,
    *,
    android_root: Path,
    core_root: Path,
    presentation_root: Path,
    apk: Path,
) -> dict[str, object]:
    manifest_path = manifest_path.resolve()
    _require_regular(manifest_path, "Build-provenance manifest")
    try:
        manifest = json.loads(
            manifest_path.read_text(encoding="utf-8"),
            object_pairs_hook=_object_without_duplicates,
        )
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise RuntimeError("Build-provenance manifest is not strict UTF-8 JSON") from error
    if not isinstance(manifest, dict):
        raise RuntimeError("Build-provenance manifest is not an object")
    if set(manifest) != {
        "schema",
        "authorityClass",
        "releaseAttested",
        "repositories",
        "artifact",
        "buildCommand",
        "authoritySha256",
        "generatedAtUtc",
    }:
        raise RuntimeError("Build-provenance manifest top-level fields are not exact")
    if (
        manifest["schema"] != SCHEMA
        or manifest["authorityClass"] != AUTHORITY_CLASS
        or manifest["releaseAttested"] is not False
    ):
        raise RuntimeError("Build-provenance authority posture is not exact")
    authority = {key: value for key, value in manifest.items() if key not in {"authoritySha256", "generatedAtUtc"}}
    if manifest["authoritySha256"] != canonical_sha256(authority):
        raise RuntimeError("Build-provenance authority digest is invalid")

    repositories = manifest["repositories"]
    if not isinstance(repositories, dict) or tuple(repositories) != REPOSITORY_KEYS:
        raise RuntimeError("Build-provenance repository graph is not exact or ordered")
    roots = {
        "android": android_root.resolve(),
        "core": core_root.resolve(),
        "presentation": presentation_root.resolve(),
    }
    for name in REPOSITORY_KEYS:
        expected = repositories[name]
        if not isinstance(expected, dict) or set(expected) != {"commit", "tree"}:
            raise RuntimeError(f"Build-provenance {name} identity is malformed")
        actual = repository_identity(roots[name])
        if actual != expected:
            raise RuntimeError(
                f"Current {name} source differs from the APK provenance manifest"
            )

    artifact = manifest["artifact"]
    if not isinstance(artifact, dict) or set(artifact) != {
        "basename",
        "byteLength",
        "sha256",
        "abis",
        "package",
        "apiLevel",
        "configuration",
        "runtimeIdentifier",
        "targetFramework",
    }:
        raise RuntimeError("Build-provenance artifact fields are not exact")
    apk = apk.resolve()
    _require_regular(apk, "ARM64 APK")
    actual_artifact = {
        "basename": apk.name,
        "byteLength": apk.stat().st_size,
        "sha256": file_sha256(apk),
        "abis": apk_abis(apk),
        "package": PACKAGE,
        "apiLevel": 36,
        "configuration": "Debug",
        "runtimeIdentifier": "android-arm64",
        "targetFramework": "net10.0-android36.0",
    }
    if artifact != actual_artifact:
        raise RuntimeError("Current APK differs from the build-provenance manifest")
    return manifest
