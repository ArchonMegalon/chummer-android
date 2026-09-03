#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Mapping


SOURCE_GRAPH_CONTRACT = "chummer.android.release-source-graph/v3"
PACKAGE_ID = "com.myexternalbrain.chummer"
HISTORICAL_VERSION_CODE_FLOOR = 10
PACKAGE_AUTHORITY_CONTRACT = "chummer.android.release-package-authority/v2"
PRESENTATION_SOURCE_COMMIT = "732a33cb8d3c704b8a86e1249eab46508339a105"
PRESENTATION_SOURCE_TREE = "db56a83e5fee94d9aec7fd56a4b0df078c7dda62"
LOCKED_DEPENDENCY_MODE = "locked_package"
SOURCE_COMPATIBILITY_MODE = "source_compatibility"
SHA40 = 40
SHA256 = 64
PACKAGE_VERSION_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?")
RELEASE_VERSION_NAME_PATTERN = re.compile(
    r"[0-9]+(?:\.[0-9]+){2}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"
)
RELEASE_VERSION_CODE_PATTERN = re.compile(r"[1-9][0-9]*")

RUNTIME_PACKAGE_IDS = (
    "Chummer.Application",
    "Chummer.Engine.Contracts",
    "Chummer.Infrastructure",
    "Chummer.Rulesets.Hosting",
    "Chummer.Rulesets.Sr4",
    "Chummer.Rulesets.Sr5",
    "Chummer.Rulesets.Sr6",
)
OWNER_PACKAGE_SPECS = (
    ("Chummer.Campaign.Contracts", "chummer6-hub"),
    ("Chummer.Play.Contracts", "chummer6-hub"),
    ("Chummer.Run.Contracts", "chummer6-hub"),
    ("Chummer.Hub.Registry.Contracts", "chummer6-hub-registry"),
    ("Chummer.Ui.Kit", "chummer6-ui-kit"),
)
OWNER_PACKAGE_IDS = tuple(package_id for package_id, _ in OWNER_PACKAGE_SPECS)
OWNER_REPOSITORY_BY_PACKAGE = dict(OWNER_PACKAGE_SPECS)

REPOSITORY_SPECS = (
    (
        "chummer-android", "app", ("chummer-android",), "CHUMMER_ANDROID_REVISION",
        "https://github.com/ArchonMegalon/chummer-android.git",
    ),
    (
        "chummer6-ui", "runtime", ("chummer-presentation",), "CHUMMER_PRESENTATION_REVISION",
        "https://github.com/ArchonMegalon/chummer6-ui.git",
    ),
    (
        "chummer6-core", "runtime", ("chummer-core-engine",), "CHUMMER_CORE_ENGINE_REVISION",
        "https://github.com/ArchonMegalon/chummer6-core.git",
    ),
    (
        "chummer6-ui-kit", "runtime", ("chummer-ui-kit",), "CHUMMER_UI_KIT_REVISION",
        "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
    ),
    (
        "chummer6-hub", "contracts_and_validation", ("chummer.run-services",),
        "CHUMMER_RUN_SERVICES_REVISION", "https://github.com/ArchonMegalon/chummer6-hub.git",
    ),
    (
        "chummer6-hub-registry", "contracts", ("chummer-hub-registry",),
        "CHUMMER_HUB_REGISTRY_REVISION",
        "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
    ),
    (
        "chummer6-media-factory",
        "contracts",
        ("fleet", "repos", "chummer-media-factory"),
        "CHUMMER_MEDIA_FACTORY_REVISION",
        "https://github.com/ArchonMegalon/chummer6-media-factory.git",
    ),
    (
        "chummer6-design", "validation", ("chummer-design",), "CHUMMER_DESIGN_REVISION",
        "https://github.com/ArchonMegalon/chummer6-design.git",
    ),
)


def git(root: Path, *arguments: str, binary: bool = False) -> str | bytes:
    environment = dict(os.environ)
    environment["GIT_NO_REPLACE_OBJECTS"] = "1"
    completed = subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=True,
        capture_output=True,
        env=environment,
        text=not binary,
    )
    return completed.stdout if binary else completed.stdout.strip()


def _require_ancestor(
    root: Path,
    ancestor: str,
    descendant: str,
    label: str,
) -> None:
    environment = dict(os.environ)
    environment["GIT_NO_REPLACE_OBJECTS"] = "1"
    completed = subprocess.run(
        ["git", "-C", os.fspath(root), "merge-base", "--is-ancestor", ancestor, descendant],
        check=False,
        capture_output=True,
        env=environment,
        text=True,
    )
    if completed.returncode == 0:
        return
    if completed.returncode == 1:
        raise ValueError(f"{label} is not an ancestor of the pinned owner repository head")
    raise ValueError(f"cannot verify {label} reachability from the pinned owner repository head")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _require_hex(value: object, length: int, label: str) -> str:
    if (
        not isinstance(value, str)
        or len(value) != length
        or any(character not in "0123456789abcdef" for character in value)
    ):
        raise ValueError(f"{label} must be lowercase {length}-character hex")
    return value


def _require_string(value: object, label: str) -> str:
    if not isinstance(value, str) or not value or value != value.strip():
        raise ValueError(f"{label} must be one canonical non-empty string")
    return value


def _strict_json(path: Path) -> dict[str, object]:
    def reject_duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
        result: dict[str, object] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"package authority contains duplicate key {key!r}")
            result[key] = value
        return result

    try:
        payload = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=reject_duplicates,
            parse_constant=lambda value: (_ for _ in ()).throw(
                ValueError(f"package authority contains non-finite number {value}")
            ),
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read release package authority: {error}") from error
    if not isinstance(payload, dict):
        raise ValueError("release package authority must be one JSON object")
    return payload


def _contained_binding(authority_root: Path, binding: object, label: str) -> str:
    if not isinstance(binding, dict) or set(binding) != {"path", "sha256"}:
        raise ValueError(f"{label} must use exact path and sha256 fields")
    relative = _require_string(binding.get("path"), f"{label}.path")
    posix = PurePosixPath(relative)
    if posix.is_absolute() or ".." in posix.parts or "\\" in relative:
        raise ValueError(f"{label} path escapes the retained authority root")
    current = authority_root
    for part in posix.parts:
        current = current / part
        if current.is_symlink():
            raise ValueError(f"{label} path traverses a symlink")
    try:
        resolved = current.resolve(strict=True)
        resolved.relative_to(authority_root.resolve(strict=True))
    except (OSError, ValueError) as error:
        raise ValueError(f"{label} path escapes the retained authority root") from error
    if (
        not resolved.is_file()
        or stat.S_IMODE(resolved.stat().st_mode) & 0o077
        or resolved.stat().st_uid != os.getuid()
    ):
        raise ValueError(f"{label} is not an owner-only regular file")
    expected = _require_hex(binding.get("sha256"), SHA256, f"{label}.sha256")
    if _sha256(resolved) != expected:
        raise ValueError(f"{label} digest does not match its exact bytes")
    return expected


def repository_record(
    name: str,
    role: str,
    root: Path,
    expected_revision: str,
    expected_repository: str,
) -> dict[str, str]:
    if root.is_symlink() or root.absolute() != root.resolve():
        raise ValueError(f"release source checkout must be an exact non-symlinked sibling: {name}")
    resolved = root.resolve()
    if not resolved.is_dir():
        raise ValueError(f"release source checkout is missing: {name}")
    _require_hex(expected_revision, SHA40, f"release source expected revision for {name}")
    status = git(resolved, "status", "--porcelain", "--untracked-files=all")
    if status:
        raise ValueError(f"release source checkout is dirty: {name}")
    commit = str(git(resolved, "rev-parse", "HEAD"))
    _require_hex(commit, SHA40, f"release source commit for {name}")
    if commit != expected_revision:
        raise ValueError(f"release source checkout revision drifted: {name}")
    remote = str(git(resolved, "remote", "get-url", "origin"))
    if remote != expected_repository:
        raise ValueError(f"release source repository authority drifted: {name}")
    tree = str(git(resolved, "rev-parse", "HEAD^{tree}"))
    _require_hex(tree, SHA40, f"release source tree for {name}")
    listing = git(resolved, "ls-tree", "-r", "-z", "--full-tree", "HEAD", binary=True)
    assert isinstance(listing, bytes)
    return {
        "name": name,
        "role": role,
        "commit": commit,
        "tree": tree,
        "tree_sha256": hashlib.sha256(listing).hexdigest(),
        "repository": expected_repository,
    }


def _canonical_runtime_package_pins(rows: object, core: dict[str, str]) -> list[dict[str, str]]:
    if not isinstance(rows, list) or len(rows) != len(RUNTIME_PACKAGE_IDS):
        raise ValueError("package authority must preserve the exact seven Core runtime package pins")
    result: list[dict[str, str]] = []
    for expected_id, row in zip(RUNTIME_PACKAGE_IDS, rows, strict=True):
        if not isinstance(row, dict) or set(row) != {
            "package_id", "version", "sha256", "repository", "commit"
        }:
            raise ValueError("Core runtime package pin must use the exact v1 fields")
        package_id = _require_string(row.get("package_id"), "Core package pin package_id")
        if package_id != expected_id:
            raise ValueError("Core runtime package pins are missing, duplicated, extra, or noncanonical")
        version = _require_string(row.get("version"), f"{package_id}.version")
        if not PACKAGE_VERSION_PATTERN.fullmatch(version):
            raise ValueError(f"{package_id}.version is not canonical")
        digest = _require_hex(row.get("sha256"), SHA256, f"{package_id}.sha256")
        if row.get("repository") != "chummer6-core" or row.get("commit") != core["commit"]:
            raise ValueError(f"Core runtime package pin source authority is incorrect for {package_id}")
        result.append({
            "package_id": package_id,
            "version": version,
            "sha256": digest,
            "repository": "chummer6-core",
            "commit": core["commit"],
        })
    if len({row["version"] for row in result}) != 1:
        raise ValueError("Core runtime package pins must share one exact version")
    if len({row["sha256"] for row in result}) != len(result):
        raise ValueError("Core runtime package pins must not reuse package hashes")
    return result


def _canonical_owner_package_pins(
    rows: object,
    roots: Mapping[str, Path],
    repositories: Mapping[str, dict[str, str]],
    authority_root: Path,
) -> list[dict[str, object]]:
    if not isinstance(rows, list) or len(rows) != len(OWNER_PACKAGE_IDS):
        raise ValueError("package authority must bind the exact five owner package pins")
    expected_fields = {
        "package_id", "version", "sha256", "size_bytes", "owner_repository",
        "source_commit", "source_tree", "authority_receipt", "package_inventory",
        "package_plane_lock", "dependency_mode",
    }
    result: list[dict[str, object]] = []
    for expected_id, row in zip(OWNER_PACKAGE_IDS, rows, strict=True):
        if not isinstance(row, dict) or set(row) != expected_fields:
            raise ValueError("owner package pin must use the exact v2 binding fields")
        package_id = _require_string(row.get("package_id"), "owner package pin package_id")
        if package_id != expected_id:
            raise ValueError("owner package pins are missing, duplicated, extra, or noncanonical")
        owner = OWNER_REPOSITORY_BY_PACKAGE[package_id]
        if row.get("owner_repository") != owner:
            raise ValueError(f"owner package pin is misowned: {package_id}")
        source_commit = _require_hex(
            row.get("source_commit"), SHA40, f"{package_id}.source_commit"
        )
        try:
            resolved_source_commit = str(
                git(roots[owner], "rev-parse", "--verify", f"{source_commit}^{{commit}}")
            )
            if resolved_source_commit != source_commit:
                raise ValueError(
                    f"owner package source authority is not an exact commit for {package_id}"
                )
            source_tree = str(git(roots[owner], "rev-parse", f"{source_commit}^{{tree}}"))
        except subprocess.CalledProcessError as error:
            raise ValueError(f"owner package source commit is unavailable for {package_id}") from error
        _require_hex(source_tree, SHA40, f"{package_id}.source_tree")
        if row.get("source_tree") != source_tree:
            raise ValueError(f"owner package source authority is incorrect for {package_id}")
        owner_head_commit = repositories[owner]["commit"]
        owner_head_tree = repositories[owner]["tree"]
        _require_ancestor(
            roots[owner], source_commit, owner_head_commit, f"{package_id}.source_commit"
        )
        if row.get("dependency_mode") != LOCKED_DEPENDENCY_MODE:
            raise ValueError(f"locked owner package cannot fall back to source: {package_id}")
        version = _require_string(row.get("version"), f"{package_id}.version")
        if not PACKAGE_VERSION_PATTERN.fullmatch(version):
            raise ValueError(f"{package_id}.version is not canonical")
        digest = _require_hex(row.get("sha256"), SHA256, f"{package_id}.sha256")
        size = row.get("size_bytes")
        if not isinstance(size, int) or isinstance(size, bool) or size <= 0:
            raise ValueError(f"{package_id}.size_bytes must be a positive integer")
        receipt = _contained_binding(
            authority_root, row.get("authority_receipt"), f"{package_id}.authority_receipt"
        )
        inventory = _contained_binding(
            authority_root, row.get("package_inventory"), f"{package_id}.package_inventory"
        )
        lock = _contained_binding(
            authority_root, row.get("package_plane_lock"), f"{package_id}.package_plane_lock"
        )
        result.append({
            "package_id": package_id,
            "version": version,
            "sha256": digest,
            "size_bytes": size,
            "owner_repository": owner,
            "source_commit": source_commit,
            "source_tree": source_tree,
            "source_authority": {
                "owner_head_commit": owner_head_commit,
                "owner_head_tree": owner_head_tree,
                "relationship": "ancestor_or_equal",
                "verification": "git-merge-base-is-ancestor-without-replace-objects",
            },
            "authority_receipt_sha256": receipt,
            "package_inventory_sha256": inventory,
            "package_plane_lock_sha256": lock,
            "dependency_mode": LOCKED_DEPENDENCY_MODE,
        })
    if len({row["sha256"] for row in result}) != len(result):
        raise ValueError("owner package pins must not reuse package hashes")
    return result


def _canonical_dependency_closure(rows: object) -> list[dict[str, object]]:
    if not isinstance(rows, list) or len(rows) != len(OWNER_PACKAGE_IDS):
        raise ValueError("package authority must bind dependency closure for every owner package")
    result: list[dict[str, object]] = []
    for expected_id, row in zip(OWNER_PACKAGE_IDS, rows, strict=True):
        if not isinstance(row, dict) or set(row) != {"package_id", "dependencies"}:
            raise ValueError("owner dependency closure row must use exact fields")
        if row.get("package_id") != expected_id:
            raise ValueError("owner dependency closure is missing, duplicated, extra, or noncanonical")
        dependencies = row.get("dependencies")
        if (
            not isinstance(dependencies, list)
            or any(not isinstance(item, str) or not item for item in dependencies)
            or dependencies != sorted(set(dependencies))
        ):
            raise ValueError(f"owner dependency closure is not canonical for {expected_id}")
        if expected_id == "Chummer.Run.Contracts" and "Chummer.Play.Contracts" not in dependencies:
            raise ValueError("owner dependency closure is missing transitive Chummer.Play.Contracts")
        result.append({"package_id": expected_id, "dependencies": list(dependencies)})
    return result


def _generator_binding(android_root: Path) -> dict[str, object]:
    path = android_root / "scripts" / "verify_release_source_graph.py"
    if path.is_symlink() or not path.is_file():
        raise ValueError("release source graph generator must be one tracked regular file")
    return {
        "path": "scripts/verify_release_source_graph.py",
        "sha256": _sha256(path),
        "size_bytes": path.stat().st_size,
    }


def build_graph(
    android_root: Path,
    workspace_root: Path,
    package_authority: Mapping[str, object],
    authority_root: Path,
    environment: Mapping[str, str] | None = None,
    *,
    expected_version_name: str,
    expected_version_code: str,
) -> dict[str, object]:
    environment = os.environ if environment is None else environment
    workspace_root = workspace_root.absolute()
    if workspace_root.is_symlink() or workspace_root != workspace_root.resolve():
        raise ValueError("release workspace root must be an exact non-symlinked directory")
    if android_root.resolve() != (workspace_root / "chummer-android").resolve():
        raise ValueError("Android release source must be the coherent workspace chummer-android sibling")
    if (
        not authority_root.is_absolute()
        or authority_root.is_symlink()
        or not authority_root.is_dir()
        or authority_root.resolve(strict=True) != authority_root
    ):
        raise ValueError("retained package authority root must be one canonical absolute directory")
    repositories: list[dict[str, str]] = []
    roots: dict[str, Path] = {}
    for name, role, relative_parts, revision_variable, expected_repository in REPOSITORY_SPECS:
        root = android_root if name == "chummer-android" else workspace_root.joinpath(*relative_parts)
        roots[name] = root
        repositories.append(
            repository_record(
                name,
                role,
                root,
                environment.get(revision_variable, ""),
                expected_repository,
            )
        )
    by_name = {row["name"]: row for row in repositories}
    presentation = by_name["chummer6-ui"]
    if presentation["commit"] != PRESENTATION_SOURCE_COMMIT or presentation["tree"] != PRESENTATION_SOURCE_TREE:
        raise ValueError("Presentation source row does not match the exact reviewed commit and tree")
    if set(package_authority) != {
        "contractName", "packagePins", "ownerPackagePins", "dependencyClosure"
    } or package_authority.get("contractName") != PACKAGE_AUTHORITY_CONTRACT:
        raise ValueError("release package authority must use the exact v2 schema")
    package_pins = _canonical_runtime_package_pins(
        package_authority.get("packagePins"), by_name["chummer6-core"]
    )
    owner_pins = _canonical_owner_package_pins(
        package_authority.get("ownerPackagePins"), roots, by_name, authority_root
    )
    closure = _canonical_dependency_closure(package_authority.get("dependencyClosure"))
    if (
        len(expected_version_name) > 128
        or RELEASE_VERSION_NAME_PATTERN.fullmatch(expected_version_name) is None
    ):
        raise ValueError("release version name must be one canonical explicit value")
    if RELEASE_VERSION_CODE_PATTERN.fullmatch(expected_version_code) is None:
        raise ValueError("release version code must be one canonical explicit value")
    version_code = int(expected_version_code)
    if version_code <= HISTORICAL_VERSION_CODE_FLOOR:
        raise ValueError(
            "release version code must be greater than the immutable Preview.10 floor"
        )
    return {
        "contractName": SOURCE_GRAPH_CONTRACT,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "authorityState": "local_review_required",
        "publicationAuthorized": False,
        "releaseIdentity": {
            "packageId": PACKAGE_ID,
            "versionName": expected_version_name,
            "versionCode": version_code,
            "intentAuthority": "explicit_build_input",
            "minimumExclusiveVersionCode": HISTORICAL_VERSION_CODE_FLOOR,
        },
        "generator": _generator_binding(android_root),
        "repositories": repositories,
        "packagePins": package_pins,
        "ownerPackagePins": owner_pins,
        "dependencyClosure": closure,
        "presentationSource": {
            "repository": "chummer6-ui",
            "commit": PRESENTATION_SOURCE_COMMIT,
            "tree": PRESENTATION_SOURCE_TREE,
            "source_path": "chummer-presentation",
            "authority_state": "local_review_required",
            "publication_authorized": False,
            "dependency_mode": SOURCE_COMPATIBILITY_MODE,
        },
        "doesNotAssert": [
            "google_play_upload",
            "google_play_processing",
            "tester_installation",
            "production_rollout",
            "presentation_package_authority",
        ],
    }


def write_graph_exclusive(path: Path, graph: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags, 0o600)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            json.dump(graph, output, indent=2)
            output.write("\n")
            output.flush()
            os.fsync(output.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def verify_existing_graph(path: Path, graph: dict[str, object]) -> None:
    if path.is_symlink() or not path.is_file():
        raise ValueError("release source graph is missing or not a regular file")
    existing = _strict_json(path)
    if set(existing) != set(graph):
        raise ValueError("release source graph changed during packaging: structure")
    generated_at = existing.get("generatedAtUtc")
    if not isinstance(generated_at, str) or not generated_at.endswith("Z"):
        raise ValueError("release source graph changed during packaging: generatedAtUtc")
    for field in graph:
        if field != "generatedAtUtc" and existing.get(field) != graph.get(field):
            raise ValueError(f"release source graph changed during packaging: {field}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--android-root", required=True, type=Path)
    parser.add_argument("--workspace-root", required=True, type=Path)
    parser.add_argument("--package-authority", required=True, type=Path)
    parser.add_argument("--authority-root", required=True, type=Path)
    parser.add_argument("--expected-version-name", required=True)
    parser.add_argument("--expected-version-code", required=True)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--output", type=Path)
    action.add_argument("--verify-existing", type=Path)
    arguments = parser.parse_args()
    graph = build_graph(
        arguments.android_root,
        arguments.workspace_root,
        _strict_json(arguments.package_authority),
        arguments.authority_root,
        expected_version_name=arguments.expected_version_name,
        expected_version_code=arguments.expected_version_code,
    )
    if arguments.output is not None:
        write_graph_exclusive(arguments.output, graph)
        print(f"release source graph local-review evidence written: {arguments.output}")
    else:
        verify_existing_graph(arguments.verify_existing, graph)
        print(f"release source graph remained exact: {arguments.verify_existing}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
