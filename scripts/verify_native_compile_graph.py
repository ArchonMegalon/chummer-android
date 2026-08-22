#!/usr/bin/env python3
"""Fail-closed source and generated-asset guard for the native compile check."""

from __future__ import annotations

import argparse
import glob
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Iterable


SCHEMA = "chummer.android.native-compile-graph/v1"
DEFAULT_PROJECT = Path("tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj")
INPUTS_FILE = "NativeCompileInputs.props"


def _is_within(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def _load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return value


def _compile_includes(inputs_path: Path) -> list[str]:
    root = ET.parse(inputs_path).getroot()
    return [
        element.attrib["Include"]
        for element in root.findall(".//Compile")
        if "Include" in element.attrib
    ]


def verify_source_graph(repo_root: Path, project_path: Path) -> tuple[list[Path], list[str]]:
    repo_root = repo_root.resolve()
    project_path = project_path.resolve()
    issues: list[str] = []
    if not _is_within(project_path, repo_root) or not project_path.is_file():
        return [], [f"compile-project-unavailable:{project_path}"]

    project_text = project_path.read_text(encoding="utf-8")
    if "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>" not in project_text:
        issues.append("default-compile-items-must-be-disabled")
    expected_import = f'<Import Project="{INPUTS_FILE}" />'
    if project_text.count(expected_import) != 1:
        issues.append("owned-input-manifest-import-required")
    if "<Compile Include=" in project_text:
        issues.append("compile-items-must-live-in-owned-input-manifest")

    inputs_path = project_path.parent / INPUTS_FILE
    if not inputs_path.is_file() or inputs_path.is_symlink():
        return [], issues + [f"owned-input-manifest-unavailable:{inputs_path}"]

    includes = _compile_includes(inputs_path)
    if len(includes) != len(set(includes)):
        issues.append("duplicate-compile-input")
    compiled: list[Path] = []
    for include in includes:
        if Path(include).is_absolute() or "**" in include:
            issues.append(f"unsafe-compile-input:{include}")
            continue
        matches = sorted(Path(value).resolve() for value in glob.glob(str(project_path.parent / include)))
        if not matches:
            issues.append(f"compile-input-unavailable:{include}")
            continue
        for match in matches:
            if not match.is_file() or match.is_symlink() or not _is_within(match, repo_root):
                issues.append(f"compile-input-outside-owned-source:{include}:{match}")
                continue
            compiled.append(match)

    if len(compiled) != len(set(compiled)):
        issues.append("compile-input-resolves-more-than-once")
    compiled_set = set(compiled)
    native_root = (repo_root / "src/Chummer.Android/Native").resolve()
    expected_native = set(native_root.glob("*.cs"))
    actual_native = {path for path in compiled_set if path.parent == native_root}
    if actual_native != expected_native:
        missing = sorted(str(path.relative_to(repo_root)) for path in expected_native - actual_native)
        unexpected = sorted(str(path.relative_to(repo_root)) for path in actual_native - expected_native)
        issues.append(f"native-input-set-mismatch:missing={missing}:unexpected={unexpected}")

    required_owned = {
        (repo_root / "src/Chummer.Android/MainShell.cs").resolve(),
        (repo_root / "src/Chummer.Android/MauiProgram.cs").resolve(),
        (repo_root / "src/Chummer.Android/Platform/IAndroidImageDocumentService.cs").resolve(),
        (project_path.parent / "CompileStubs.cs").resolve(),
    }
    for required in sorted(required_owned):
        if required not in compiled_set:
            issues.append(f"required-owned-input-missing:{required.relative_to(repo_root)}")
    platform_android = (repo_root / "src/Chummer.Android/Platforms/Android").resolve()
    for path in compiled_set:
        if _is_within(path, platform_android):
            issues.append(f"android-framework-source-in-neutral-gate:{path.relative_to(repo_root)}")

    combined = "\n".join(path.read_text(encoding="utf-8") for path in compiled)
    maui_program = (repo_root / "src/Chummer.Android/MauiProgram.cs").read_text(encoding="utf-8")
    registrations = re.findall(
        r"AddSingleton<(?P<contract>IAndroid[A-Za-z0-9_]+),\s*(?P<implementation>Android[A-Za-z0-9_]+)>",
        maui_program,
    )
    for contract, implementation in registrations:
        if re.search(rf"\binterface\s+{re.escape(contract)}\b", combined) is None:
            issues.append(f"registered-contract-not-compiled:{contract}")
        if re.search(rf"\bclass\s+{re.escape(implementation)}\b", combined) is None:
            issues.append(f"registered-implementation-not-compiled-or-stubbed:{implementation}")
    return sorted(compiled_set), sorted(set(issues))


def _project_references_from_assets(assets: dict[str, Any], project_dir: Path) -> Iterable[tuple[str, Path]]:
    restore = assets.get("project", {}).get("restore", {})
    project_path = restore.get("projectPath")
    if isinstance(project_path, str):
        yield "assets:restore-project", Path(project_path)
    for identity, metadata in assets.get("libraries", {}).items():
        if not isinstance(metadata, dict) or metadata.get("type") != "project":
            continue
        for key in ("path", "msbuildProject"):
            raw = metadata.get(key)
            if isinstance(raw, str):
                candidate = Path(raw)
                yield f"assets:{identity}:{key}", candidate if candidate.is_absolute() else project_dir / candidate


def _project_references_from_dgspec(dgspec: dict[str, Any]) -> Iterable[tuple[str, Path]]:
    projects = dgspec.get("projects", {})
    if not isinstance(projects, dict):
        return
    for identity, metadata in projects.items():
        if isinstance(identity, str) and identity.endswith(".csproj"):
            yield "dgspec:project", Path(identity)
        if not isinstance(metadata, dict):
            continue
        restore = metadata.get("restore", {})
        for key in ("projectPath", "projectUniqueName"):
            raw = restore.get(key) if isinstance(restore, dict) else None
            if isinstance(raw, str) and raw.endswith(".csproj"):
                yield f"dgspec:restore:{key}", Path(raw)
        frameworks = metadata.get("frameworks", {})
        if not isinstance(frameworks, dict):
            continue
        for framework in frameworks.values():
            references = framework.get("projectReferences", {}) if isinstance(framework, dict) else {}
            if isinstance(references, dict):
                for raw in references:
                    if isinstance(raw, str) and raw.endswith(".csproj"):
                        yield "dgspec:project-reference", Path(raw)


def verify_asset_graph(
    project_path: Path,
    workspace_root: Path,
) -> tuple[list[Path], list[str]]:
    project_path = project_path.resolve()
    workspace_root = workspace_root.resolve()
    project_dir = project_path.parent
    obj_dir = project_dir / "obj"
    assets_path = obj_dir / "project.assets.json"
    issues: list[str] = []
    referenced: list[Path] = []
    own_restore: list[Path] = []
    if not assets_path.is_file() or assets_path.is_symlink():
        return [], [f"generated-assets-missing:{assets_path}"]
    try:
        references = list(_project_references_from_assets(_load_json(assets_path), project_dir))
    except (OSError, ValueError, json.JSONDecodeError) as error:
        return [], [f"generated-assets-invalid:{assets_path}:{error}"]

    dgspecs = sorted(obj_dir.glob("*.nuget.dgspec.json"))
    if len(dgspecs) != 1:
        issues.append(f"generated-dgspec-count:{len(dgspecs)}")
    for dgspec_path in dgspecs:
        if dgspec_path.is_symlink():
            issues.append(f"generated-dgspec-symlink:{dgspec_path}")
            continue
        try:
            references.extend(_project_references_from_dgspec(_load_json(dgspec_path)))
        except (OSError, ValueError, json.JSONDecodeError) as error:
            issues.append(f"generated-dgspec-invalid:{dgspec_path}:{error}")

    for origin, raw_path in references:
        candidate = raw_path if raw_path.is_absolute() else project_dir / raw_path
        resolved = candidate.resolve()
        referenced.append(resolved)
        if origin in {"assets:restore-project", "dgspec:project"}:
            own_restore.append(resolved)
        if not _is_within(resolved, workspace_root):
            issues.append(f"project-reference-outside-workspace:{origin}:{resolved}")
        elif not resolved.is_file():
            issues.append(f"project-reference-missing:{origin}:{resolved}")
        if origin == "assets:restore-project" and resolved != project_path:
            issues.append(f"generated-assets-bound-to-different-project:{resolved}")
    if project_path not in own_restore:
        issues.append(f"generated-assets-not-bound-to-current-project:{project_path}")
    return sorted(set(referenced)), sorted(set(issues))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--project", type=Path)
    parser.add_argument("--workspace-root", type=Path)
    parser.add_argument("--require-assets", action="store_true")
    parser.add_argument("--assets-only", action="store_true")
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    project_path = (args.project or repo_root / DEFAULT_PROJECT).resolve()
    workspace_root = (args.workspace_root or repo_root.parent).resolve()
    compiled: list[Path] = []
    issues: list[str] = []
    if not args.assets_only:
        compiled, issues = verify_source_graph(repo_root, project_path)
    referenced: list[Path] = []
    if args.require_assets or args.assets_only:
        referenced, asset_issues = verify_asset_graph(project_path, workspace_root)
        issues.extend(asset_issues)
    payload = {
        "schema": SCHEMA,
        "status": "pass" if not issues else "blocked",
        "repoRoot": str(repo_root),
        "workspaceRoot": str(workspace_root),
        "compileProject": str(project_path),
        "compiledOwnedSourceCount": len(compiled),
        "generatedProjectReferenceCount": len(referenced),
        "issues": sorted(set(issues)),
    }
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 0 if not issues else 2


if __name__ == "__main__":
    raise SystemExit(main())
