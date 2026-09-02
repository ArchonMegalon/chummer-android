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
import json
import os
import stat
from pathlib import Path, PurePosixPath
from typing import Any, Iterable, Mapping


CONTRACT = "chummer.android.release-restore-consumption/v1"
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


def snapshot_feed(authority_path: Path, source: Path, destination: Path) -> dict[str, Any]:
    authority_data, _ = _stable_file(authority_path, "release package authority")
    authority = _strict_json_bytes(authority_data, "release package authority")
    packages = _authority_packages(authority)
    source = _owned_directory(source, "retained package feed")
    destination = _private_directory(destination, "private selected package feed", empty=True)
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
    packages_root: Path, routed_lock_root: Path, project_lock: Path,
) -> dict[str, Any]:
    input_root = _private_directory(input_root, "release restore input root")
    workspace_root = workspace_root.resolve(strict=True)
    _outside(input_root, workspace_root)
    for path, label in (
        (owner_feed, "private selected package feed"),
        (packages_root, "isolated global-packages cache"),
        (routed_lock_root, "routed project-lock root"),
    ):
        _private_directory(path, label)
        try:
            path.relative_to(input_root)
        except ValueError as error:
            raise ValueError(f"{label} must remain inside the release restore input root") from error
    authority_data, _ = _stable_file(authority_path, "release package authority")
    authority = json.loads(authority_data)
    expected = _authority_packages(authority)
    package_rows = _tree_inventory(packages_root, "isolated global-packages cache")
    build_roots, intermediate_rows = _workspace_build_state(workspace_root)
    if any(PurePosixPath(root).name == "bin" for root in build_roots):
        raise ValueError("coherent release workspace bin outputs must remain absent before publish")
    assets_candidates = [
        row for row in intermediate_rows
        if PurePosixPath(row["path"]).name == "project.assets.json"
        and "Chummer.Android" in PurePosixPath(row["path"]).parts
    ]
    dgspec_candidates = [
        row for row in intermediate_rows
        if PurePosixPath(row["path"]).name == "Chummer.Android.csproj.nuget.dgspec.json"
    ]
    if len(assets_candidates) != 1 or len(dgspec_candidates) != 1:
        raise ValueError("restore must produce exactly one primary Android project.assets.json and dgspec")
    assets_path = workspace_root / PurePosixPath(assets_candidates[0]["path"])
    assets = _strict_json(assets_path, "project.assets.json")
    dgspec_path = workspace_root / PurePosixPath(dgspec_candidates[0]["path"])
    dgspec = _strict_json(dgspec_path, "restore dgspec")
    chummer_closure, source_projects, dgspec_projects = _closure(
        assets, dgspec, packages_root, expected, workspace_root
    )
    lock_row = _file_row(project_lock, project_lock.name, "packages.lock.json")
    return {
        "contractName": CONTRACT,
        "publicationAuthorized": False,
        "inputRoot": os.fspath(input_root),
        "authoritySha256": hashlib.sha256(authority_data).hexdigest(),
        "projectLock": lock_row,
        "projectAssets": assets_candidates[0],
        "dependencyGraphSpec": dgspec_candidates[0],
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
        "workspaceBuildState": {
            "roots": build_roots,
            "files": intermediate_rows,
            "inventorySha256": _inventory_digest(intermediate_rows),
        },
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
        if not isinstance(path, str) or path in result:
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
    owner_feed: Path, routed_lock_root: Path, project_lock: Path,
) -> None:
    if manifest.get("contractName") != CONTRACT or manifest.get("publicationAuthorized") is not False:
        raise ValueError("restore consumption manifest posture is not exact")
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
    _actual_roots, actual_rows = _workspace_build_state(workspace_root)
    actual_intermediate = _rows_by_path(actual_rows, "actual intermediates")
    sealed = _rows_by_path(
        manifest.get("workspaceBuildState", {}).get("files")
        if isinstance(manifest.get("workspaceBuildState"), dict) else None,
        "sealed intermediates",
    )
    for path, row in sealed.items():
        if actual_intermediate.get(path) != row:
            raise ValueError(f"sealed restore intermediate changed during publish: {path}")


def verify_context(
    manifest: Mapping[str, Any], *, input_root: Path, workspace_root: Path,
    authority_path: Path, owner_feed: Path, packages_root: Path, routed_lock_root: Path,
) -> None:
    input_root = _private_directory(input_root, "release restore input root")
    workspace_root = workspace_root.resolve(strict=True)
    _outside(input_root, workspace_root)
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
        command.add_argument("--project-lock", required=True, type=Path)
        command.add_argument("--manifest", required=True, type=Path)
    verify.add_argument("--drift-diagnostic", type=Path)
    args = parser.parse_args()
    manifest_sha256: str | None = None
    try:
        if args.action == "snapshot-feed":
            result = snapshot_feed(args.authority, args.source, args.destination)
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
                project_lock=args.project_lock,
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
            )
            verify_post_publish(
                manifest, packages_root=args.packages_root, workspace_root=args.workspace_root,
                owner_feed=args.owner_feed, routed_lock_root=args.routed_lock_root,
                project_lock=args.project_lock,
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
