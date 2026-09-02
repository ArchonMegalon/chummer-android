#!/usr/bin/env python3
"""Materialize the deterministic package authority consumed by Play release builds.

The materializer does not accept package facts on the command line.  It first
validates the sealed Presentation receipt and retained package cache using the
same fail-closed verifier as the physical-device lane, then projects only the
packages consumed by the Android release graph.  Package dependencies are read
from the authenticated nupkg nuspecs rather than copied from a handwritten
manifest.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from typing import Any, Mapping

import verify_internal_phone_beta_package_authority as internal_authority


CONTRACT = "chummer.android.release-package-authority/v2"
DEPENDENCY_MODE = "locked_package"
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")

CORE_PACKAGE_IDS = (
    "Chummer.Application",
    "Chummer.Engine.Contracts",
    "Chummer.Infrastructure",
    "Chummer.Rulesets.Hosting",
    "Chummer.Rulesets.Sr4",
    "Chummer.Rulesets.Sr5",
    "Chummer.Rulesets.Sr6",
)
OWNER_PACKAGE_SPECS = (
    (
        "Chummer.Campaign.Contracts",
        "chummer6-hub",
        "ui-owner",
        "ui-owner-packages.receipt.json",
        "ui-owner-packages.inventory.json",
        "ui-owner-package-plane.lock.json",
    ),
    (
        "Chummer.Play.Contracts",
        "chummer6-hub",
        "hub-canonical",
        "hub-receipt.json",
        "hub-inventory.json",
        "hub-lock.json",
    ),
    (
        "Chummer.Run.Contracts",
        "chummer6-hub",
        "hub-canonical",
        "hub-receipt.json",
        "hub-inventory.json",
        "hub-lock.json",
    ),
    (
        "Chummer.Hub.Registry.Contracts",
        "chummer6-hub-registry",
        "hub-canonical",
        "hub-receipt.json",
        "hub-inventory.json",
        "hub-lock.json",
    ),
    (
        "Chummer.Ui.Kit",
        "chummer6-ui-kit",
        "ui-owner",
        "ui-owner-packages.receipt.json",
        "ui-owner-packages.inventory.json",
        "ui-owner-package-plane.lock.json",
    ),
)
REPOSITORIES = {
    "chummer6-core": (
        ("chummer-core-engine",),
        "https://github.com/ArchonMegalon/chummer6-core.git",
        "coreRuntimeSourceCommit",
    ),
    "chummer6-hub": (
        ("chummer.run-services",),
        "https://github.com/ArchonMegalon/chummer6-hub.git",
        "hubProducerCommit",
    ),
    "chummer6-hub-registry": (
        ("chummer-hub-registry",),
        "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
        "registryCommit",
    ),
    "chummer6-ui-kit": (
        ("chummer-ui-kit",),
        "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
        "uiKitCommit",
    ),
}
REPOSITORY_BY_URL = {value[1]: key for key, value in REPOSITORIES.items()}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _git(root: Path, *arguments: str) -> str:
    return subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _strict_json(path: Path, label: str) -> dict[str, Any]:
    def reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{label} contains duplicate key {key!r}")
            result[key] = value
        return result

    try:
        payload = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=reject_duplicates,
            parse_constant=lambda value: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite number {value}")
            ),
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read {label}: {error}") from error
    if not isinstance(payload, dict):
        raise ValueError(f"{label} must be one JSON object")
    return payload


def _canonical_directory(path: Path, label: str) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be one absolute non-symlinked directory")
    resolved = path.resolve(strict=True)
    if resolved != path:
        raise ValueError(f"{label} must use its canonical path")
    return resolved


def _repository_roots(
    workspace_root: Path,
    source_graph: Mapping[str, Any],
) -> dict[str, Path]:
    roots: dict[str, Path] = {}
    for name, (parts, repository, revision_field) in REPOSITORIES.items():
        root = workspace_root.joinpath(*parts)
        if root.is_symlink() or not root.is_dir() or root.resolve() != root:
            raise ValueError(f"release package source repository is unavailable: {name}")
        expected_revision = source_graph.get(revision_field)
        if not isinstance(expected_revision, str) or not SHA40.fullmatch(expected_revision):
            raise ValueError(f"release package source revision is malformed: {name}")
        if _git(root, "status", "--porcelain", "--untracked-files=all"):
            raise ValueError(f"release package source repository is dirty: {name}")
        if _git(root, "rev-parse", "HEAD") != expected_revision:
            raise ValueError(f"release package source repository head drifted: {name}")
        if _git(root, "remote", "get-url", "origin") != repository:
            raise ValueError(f"release package source repository origin drifted: {name}")
        roots[name] = root
    return roots


def _authority_artifacts(cache_root: Path, cache: Mapping[str, Any]) -> dict[str, dict[str, str]]:
    authority_root = cache_root / "authority"
    if authority_root.is_symlink() or not authority_root.is_dir():
        raise ValueError("retained package authority directory is unavailable")
    rows = cache.get("authorityArtifacts")
    if not isinstance(rows, list) or not rows:
        raise ValueError("retained package authority artifact inventory is unavailable")
    expected: dict[str, str] = {}
    for index, row in enumerate(rows):
        if not isinstance(row, dict) or set(row) != {"fileName", "sha256"}:
            raise ValueError(f"retained package authority artifact row {index} is malformed")
        name = row.get("fileName")
        digest = row.get("sha256")
        if (
            not isinstance(name, str)
            or not name
            or Path(name).name != name
            or name in expected
            or not isinstance(digest, str)
            or not SHA256.fullmatch(digest)
        ):
            raise ValueError("retained package authority artifact inventory is not canonical")
        expected[name] = digest
    actual = {path.name: path for path in authority_root.iterdir() if path.is_file()}
    if set(actual) != set(expected):
        raise ValueError("retained package authority artifact bytes do not match the inventory")
    result: dict[str, dict[str, str]] = {}
    for name, digest in expected.items():
        path = actual[name]
        if (
            path.is_symlink()
            or stat.S_IMODE(path.stat().st_mode) & 0o077
            or path.stat().st_uid != os.getuid()
            or _sha256(path) != digest
        ):
            raise ValueError(f"retained package authority artifact drifted: {name}")
        result[name] = {"path": f"authority/{name}", "sha256": digest}
    return result


def _package_rows(cache: Mapping[str, Any], package_feed: Path) -> dict[tuple[str, str], dict[str, Any]]:
    rows = cache.get("packages")
    if not isinstance(rows, list) or not rows:
        raise ValueError("retained package cache rows are unavailable")
    result: dict[tuple[str, str], dict[str, Any]] = {}
    for index, row in enumerate(rows):
        if not isinstance(row, dict) or set(row) != {
            "commit", "fileName", "packageId", "plane", "repository",
            "sha256", "sizeBytes", "version",
        }:
            raise ValueError(f"retained package cache row {index} is malformed")
        package_id = row.get("packageId")
        plane = row.get("plane")
        key = (str(plane), str(package_id))
        if key in result:
            raise ValueError(f"retained package cache contains duplicate authority row: {key}")
        path = package_feed / str(row.get("fileName"))
        if (
            path.is_symlink()
            or not path.is_file()
            or path.stat().st_size != row.get("sizeBytes")
            or _sha256(path) != row.get("sha256")
        ):
            raise ValueError(f"retained package bytes drifted: {row.get('fileName')}")
        result[key] = dict(row)
    return result


def _dependencies(
    package_path: Path,
    all_packages: Mapping[tuple[str, str], Mapping[str, Any]],
) -> list[str]:
    try:
        with zipfile.ZipFile(package_path) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                raise ValueError("package archive contains duplicate entries")
            nuspecs = [name for name in names if "/" not in name and name.endswith(".nuspec")]
            if len(nuspecs) != 1:
                raise ValueError("package archive must contain one root nuspec")
            root = ET.fromstring(archive.read(nuspecs[0]))
    except (OSError, zipfile.BadZipFile, ET.ParseError) as error:
        raise ValueError(f"cannot inspect authenticated package dependencies: {error}") from error
    available_versions: dict[str, set[str]] = {}
    for row in all_packages.values():
        available_versions.setdefault(str(row["packageId"]), set()).add(str(row["version"]))
    dependencies: dict[str, str] = {}
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] != "dependency":
            continue
        package_id = element.get("id", "")
        version = element.get("version", "")
        if not package_id.startswith("Chummer."):
            continue
        if package_id in dependencies and dependencies[package_id] != version:
            raise ValueError(f"package dependency has conflicting versions: {package_id}")
        if version not in available_versions.get(package_id, set()):
            raise ValueError(f"package dependency is absent from the retained exact cache: {package_id}")
        dependencies[package_id] = version
    return sorted(dependencies)


def derive_authority(
    *,
    workspace_root: Path,
    package_feed: Path,
    cache: Mapping[str, Any],
    source_graph: Mapping[str, Any],
) -> dict[str, Any]:
    workspace_root = _canonical_directory(workspace_root, "release workspace root")
    package_feed = _canonical_directory(package_feed, "retained package feed")
    cache_root = package_feed.parent
    roots = _repository_roots(workspace_root, source_graph)
    artifacts = _authority_artifacts(cache_root, cache)
    rows = _package_rows(cache, package_feed)

    package_pins: list[dict[str, Any]] = []
    for package_id in CORE_PACKAGE_IDS:
        row = rows.get(("core-runtime", package_id))
        if row is None:
            raise ValueError(f"Core runtime package authority is missing: {package_id}")
        if (
            row.get("repository") != REPOSITORIES["chummer6-core"][1]
            or row.get("commit") != source_graph.get("coreRuntimeSourceCommit")
        ):
            raise ValueError(f"Core runtime package source authority drifted: {package_id}")
        package_pins.append({
            "package_id": package_id,
            "version": row["version"],
            "sha256": row["sha256"],
            "repository": "chummer6-core",
            "commit": row["commit"],
        })

    owner_pins: list[dict[str, Any]] = []
    dependency_closure: list[dict[str, Any]] = []
    for package_id, owner, plane, receipt_name, inventory_name, lock_name in OWNER_PACKAGE_SPECS:
        row = rows.get((plane, package_id))
        if row is None:
            raise ValueError(f"owner package authority is missing: {package_id}")
        expected_repository = REPOSITORIES[owner][1]
        commit = row.get("commit")
        if row.get("repository") != expected_repository or not isinstance(commit, str) or not SHA40.fullmatch(commit):
            raise ValueError(f"owner package source authority drifted: {package_id}")
        try:
            source_tree = _git(roots[owner], "rev-parse", f"{commit}^{{tree}}")
        except subprocess.CalledProcessError as error:
            raise ValueError(f"owner package source commit is unavailable: {package_id}") from error
        if not SHA40.fullmatch(source_tree):
            raise ValueError(f"owner package source tree is malformed: {package_id}")
        owner_pins.append({
            "package_id": package_id,
            "version": row["version"],
            "sha256": row["sha256"],
            "size_bytes": row["sizeBytes"],
            "owner_repository": owner,
            "source_commit": commit,
            "source_tree": source_tree,
            "authority_receipt": artifacts[receipt_name],
            "package_inventory": artifacts[inventory_name],
            "package_plane_lock": artifacts[lock_name],
            "dependency_mode": DEPENDENCY_MODE,
        })
        dependency_closure.append({
            "package_id": package_id,
            "dependencies": _dependencies(package_feed / str(row["fileName"]), rows),
        })
    run = next(row for row in dependency_closure if row["package_id"] == "Chummer.Run.Contracts")
    if "Chummer.Play.Contracts" not in run["dependencies"]:
        raise ValueError("Run Contracts authority is missing its exact Play Contracts dependency")
    return {
        "contractName": CONTRACT,
        "packagePins": package_pins,
        "ownerPackagePins": owner_pins,
        "dependencyClosure": dependency_closure,
    }


def materialize(
    *,
    android_root: Path,
    workspace_root: Path,
    presentation_root: Path,
    receipt_path: Path,
    package_feed: Path,
) -> dict[str, Any]:
    android_root = _canonical_directory(android_root, "Android root")
    workspace_root = _canonical_directory(workspace_root, "release workspace root")
    presentation_root = _canonical_directory(presentation_root, "Presentation root")
    if android_root != workspace_root / "chummer-android":
        raise ValueError("Android root is not the coherent release-workspace sibling")
    if presentation_root != workspace_root / "chummer-presentation":
        raise ValueError("Presentation root is not the coherent release-workspace sibling")
    manifest = internal_authority.validate_manifest(
        android_root / "eng/internal-phone-beta-package-authority.json"
    )
    presentation = internal_authority.validate_presentation_repository(presentation_root)
    receipt = internal_authority.validate_receipt(receipt_path)
    internal_authority.validate_bound_authority_claims(manifest, presentation, receipt)
    internal_authority.validate_android_sdk_authority(
        android_root,
        manifest,
        internal_authority.require_string(
            presentation.get("packageProofSdkVersion"),
            "bound package proof SDK authority",
        ),
    )
    cache = internal_authority.validate_package_feed(package_feed)
    internal_authority.validate_receipt_cache_equivalence(receipt, cache)
    source_graph = manifest.get("sourceGraph")
    if not isinstance(source_graph, dict):
        raise ValueError("internal phone-beta source graph is unavailable")
    return derive_authority(
        workspace_root=workspace_root,
        package_feed=package_feed,
        cache=cache,
        source_graph=source_graph,
    )


def write_exclusive(path: Path, payload: Mapping[str, Any]) -> None:
    if not path.is_absolute() or path.parent.is_symlink() or not path.parent.is_dir():
        raise ValueError("release package authority output parent must be one existing absolute directory")
    if path.parent.resolve(strict=True) != path.parent:
        raise ValueError("release package authority output parent must be canonical")
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        0o600,
    )
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def verify_existing(path: Path, expected: Mapping[str, Any]) -> None:
    path = internal_authority.require_private_regular_file(path, "release package authority")
    if _strict_json(path, "release package authority") != expected:
        raise ValueError("release package authority bytes do not match freshly derived authority")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--android-root", required=True, type=Path)
    parser.add_argument("--workspace-root", required=True, type=Path)
    parser.add_argument("--presentation-root", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--package-feed", required=True, type=Path)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--output", type=Path)
    action.add_argument("--verify-existing", type=Path)
    args = parser.parse_args()
    try:
        authority = materialize(
            android_root=args.android_root,
            workspace_root=args.workspace_root,
            presentation_root=args.presentation_root,
            receipt_path=args.receipt,
            package_feed=args.package_feed,
        )
        if args.output is not None:
            write_exclusive(args.output, authority)
            action_name = "materialized"
        else:
            verify_existing(args.verify_existing, authority)
            action_name = "verified"
        print(json.dumps({
            "contractName": CONTRACT,
            "status": "pass",
            "action": action_name,
            "corePackagePinCount": len(authority["packagePins"]),
            "ownerPackagePinCount": len(authority["ownerPackagePins"]),
            "publicationAuthorized": False,
        }, sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(json.dumps({
            "contractName": CONTRACT,
            "status": "blocked",
            "publicationAuthorized": False,
            "error": str(error),
        }, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
