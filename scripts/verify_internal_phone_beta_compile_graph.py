#!/usr/bin/env python3
"""Fail closed unless the internal native compile graph uses the current package graph."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
from typing import Any

import verify_internal_phone_beta_package_authority as authority


CONTRACT = "chummer.android.internal-phone-beta-compile-graph/v1"
EXPECTED_CHUMMER_IDS = tuple(authority.EXPECTED_COMPILE_PACKAGES)


def load_object(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"expected one JSON object: {path}")
    return value


def validate_compile_graph(
    project: Path,
    android_root: Path,
    presentation_root: Path,
) -> dict[str, Any]:
    project = project.resolve(strict=True)
    android_root = android_root.resolve(strict=True)
    presentation_root = presentation_root.resolve(strict=True)
    expected_project = android_root / "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
    if project != expected_project:
        raise ValueError("internal phone-beta compile project is not exact")
    assets_path = project.parent / "obj/project.assets.json"
    dgspecs = sorted(project.parent.joinpath("obj").glob("*.nuget.dgspec.json"))
    lock_path = project.parent / "packages.lock.json"
    if assets_path.is_symlink() or not assets_path.is_file() or len(dgspecs) != 1 or lock_path.is_symlink() or not lock_path.is_file():
        raise ValueError("internal phone-beta restore assets, dgspec, and lock must all exist")
    assets = load_object(assets_path)
    dgspec = load_object(dgspecs[0])
    lock = load_object(lock_path)
    allowed_projects = {
        project,
        presentation_root / "Chummer.Presentation/Chummer.Presentation.csproj",
        presentation_root / "Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
    }
    actual_projects = {Path(value).resolve() for value in dgspec.get("projects", {}) if isinstance(value, str) and value.endswith(".csproj")}
    for metadata in dgspec.get("projects", {}).values():
        if not isinstance(metadata, dict):
            continue
        frameworks = metadata.get("frameworks", {})
        if not isinstance(frameworks, dict):
            continue
        for framework in frameworks.values():
            references = framework.get("projectReferences", {}) if isinstance(framework, dict) else {}
            if isinstance(references, dict):
                actual_projects.update(Path(value).resolve() for value in references if isinstance(value, str) and value.endswith(".csproj"))
    if actual_projects != allowed_projects:
        raise ValueError("internal phone-beta graph contains a missing or sibling project reference")
    libraries = assets.get("libraries")
    if not isinstance(libraries, dict):
        raise ValueError("internal phone-beta assets libraries are missing")
    project_libraries = {
        identity for identity, metadata in libraries.items()
        if isinstance(metadata, dict) and metadata.get("type") == "project"
    }
    if project_libraries != {"Chummer.Desktop.Runtime/1.0.0", "Chummer.Presentation/1.0.0"}:
        raise ValueError("internal phone-beta assets contain a missing or sibling project library")
    chummer_libraries: dict[str, str] = {}
    for identity, metadata in libraries.items():
        if not identity.startswith("Chummer.") or not isinstance(metadata, dict) or metadata.get("type") != "package":
            continue
        package_id, separator, version = identity.partition("/")
        if not separator or package_id in chummer_libraries:
            raise ValueError("internal phone-beta assets contain duplicate or malformed Chummer packages")
        chummer_libraries[package_id] = version
    if tuple(package_id for package_id in EXPECTED_CHUMMER_IDS if package_id in chummer_libraries) != EXPECTED_CHUMMER_IDS or set(chummer_libraries) != set(EXPECTED_CHUMMER_IDS):
        raise ValueError("internal phone-beta assets do not contain the exact current compile closure")
    for package_id, version in chummer_libraries.items():
        if version != authority.EXPECTED_COMPILE_PACKAGES[package_id]:
            raise ValueError(f"internal phone-beta package version drifted: {package_id}")
    dependencies = lock.get("dependencies")
    if not isinstance(dependencies, dict) or set(dependencies) != {"net10.0"}:
        raise ValueError("internal phone-beta compile lock target is not exact")
    locked = dependencies["net10.0"]
    if not isinstance(locked, dict):
        raise ValueError("internal phone-beta compile lock dependencies are malformed")
    locked_chummer = {
        package_id: row
        for package_id, row in locked.items()
        if package_id.startswith("Chummer.")
    }
    if set(locked_chummer) != set(EXPECTED_CHUMMER_IDS):
        raise ValueError("internal phone-beta lock does not contain the exact current compile closure")
    for package_id, row in locked_chummer.items():
        if not isinstance(row, dict) or row.get("resolved") != authority.EXPECTED_COMPILE_PACKAGES[package_id] or not isinstance(row.get("contentHash"), str):
            raise ValueError(f"internal phone-beta lock package drifted: {package_id}")
    return {
        "contractName": CONTRACT,
        "status": "pass",
        "publicationAuthorized": False,
        "projectCount": len(actual_projects),
        "projectLibraries": sorted(project_libraries),
        "chummerPackageCount": len(chummer_libraries),
        "dependencyMode": "locked_package_no_siblings",
        "doesNotAssert": ["api36_device_execution", "public_release_readiness"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--android-root", type=Path, required=True)
    parser.add_argument("--presentation-root", type=Path, required=True)
    args = parser.parse_args()
    try:
        payload = validate_compile_graph(args.project, args.android_root, args.presentation_root)
        print(json.dumps(payload, sort_keys=True))
        return 0
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(json.dumps({
            "contractName": CONTRACT,
            "status": "blocked",
            "publicationAuthorized": False,
            "error": str(error),
        }, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
