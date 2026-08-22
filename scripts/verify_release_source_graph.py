#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Mapping


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


def git(root: Path, *arguments: str) -> str:
    completed = subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=True,
        capture_output=True,
        text=True,
    )
    return completed.stdout.strip()


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
    if len(expected_revision) != 40 or any(
        character not in "0123456789abcdef" for character in expected_revision
    ):
        raise ValueError(f"release source expected revision is missing or invalid: {name}")
    status = git(resolved, "status", "--porcelain", "--untracked-files=all")
    if status:
        raise ValueError(f"release source checkout is dirty: {name}")
    commit = git(resolved, "rev-parse", "HEAD")
    if len(commit) != 40 or any(character not in "0123456789abcdef" for character in commit):
        raise ValueError(f"release source commit is invalid: {name}")
    if commit != expected_revision:
        raise ValueError(f"release source checkout revision drifted: {name}")
    remote = git(resolved, "remote", "get-url", "origin")
    if remote != expected_repository:
        raise ValueError(f"release source repository authority drifted: {name}")
    return {"name": name, "role": role, "commit": commit, "repository": expected_repository}


def build_graph(
    android_root: Path,
    workspace_root: Path,
    environment: Mapping[str, str] | None = None,
) -> dict[str, object]:
    environment = os.environ if environment is None else environment
    workspace_root = workspace_root.absolute()
    if workspace_root.is_symlink() or workspace_root != workspace_root.resolve():
        raise ValueError("release workspace root must be an exact non-symlinked directory")
    if android_root.resolve() != (workspace_root / "chummer-android").resolve():
        raise ValueError("Android release source must be the coherent workspace chummer-android sibling")
    repositories = []
    for name, role, relative_parts, revision_variable, expected_repository in REPOSITORY_SPECS:
        root = android_root if name == "chummer-android" else workspace_root.joinpath(*relative_parts)
        repositories.append(
            repository_record(
                name,
                role,
                root,
                environment.get(revision_variable, ""),
                expected_repository,
            )
        )
    return {
        "contractName": "chummer.android.release-source-graph/v1",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "repositories": repositories,
        "doesNotAssert": [
            "google_play_upload",
            "google_play_processing",
            "tester_installation",
            "production_rollout",
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
    existing = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(existing, dict) or set(existing) != set(graph):
        raise ValueError("release source graph changed during packaging: structure")
    generated_at = existing.get("generatedAtUtc")
    if not isinstance(generated_at, str) or not generated_at.endswith("Z"):
        raise ValueError("release source graph changed during packaging: generatedAtUtc")
    for field in ("contractName", "repositories", "doesNotAssert"):
        if existing.get(field) != graph.get(field):
            raise ValueError(f"release source graph changed during packaging: {field}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--android-root", required=True, type=Path)
    parser.add_argument("--workspace-root", required=True, type=Path)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--output", type=Path)
    action.add_argument("--verify-existing", type=Path)
    arguments = parser.parse_args()
    graph = build_graph(arguments.android_root, arguments.workspace_root)
    if arguments.output is not None:
        write_graph_exclusive(arguments.output, graph)
        print(f"release source graph verified: {arguments.output}")
    else:
        verify_existing_graph(arguments.verify_existing, graph)
        print(f"release source graph remained exact: {arguments.verify_existing}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
