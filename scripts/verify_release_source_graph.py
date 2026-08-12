#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import subprocess
from datetime import datetime, timezone
from pathlib import Path


def git(root: Path, *arguments: str) -> str:
    completed = subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=True,
        capture_output=True,
        text=True,
    )
    return completed.stdout.strip()


def resolve_media_root(workspace_root: Path) -> Path:
    configured = os.environ.get("CHUMMER_MEDIA_FACTORY_ROOT")
    candidates = [
        Path(configured) if configured else None,
        workspace_root / "fleet" / "repos" / "chummer-media-factory",
        workspace_root.parent / "fleet" / "repos" / "chummer-media-factory",
    ]
    for candidate in candidates:
        if candidate is not None and (candidate / ".git").exists():
            return candidate.resolve()
    raise ValueError("Chummer media-factory source checkout is missing")


def repository_record(name: str, role: str, root: Path) -> dict[str, str]:
    resolved = root.resolve()
    if not resolved.is_dir():
        raise ValueError(f"release source checkout is missing: {name}")
    status = git(resolved, "status", "--porcelain", "--untracked-files=all")
    if status:
        raise ValueError(f"release source checkout is dirty: {name}")
    commit = git(resolved, "rev-parse", "HEAD")
    if len(commit) != 40 or any(character not in "0123456789abcdef" for character in commit):
        raise ValueError(f"release source commit is invalid: {name}")
    remote = git(resolved, "remote", "get-url", "origin")
    return {"name": name, "role": role, "commit": commit, "repository": remote}


def build_graph(android_root: Path, workspace_root: Path) -> dict[str, object]:
    run_services = Path(
        os.environ.get("CHUMMER_RUN_SERVICES_ROOT", workspace_root / "chummer.run-services")
    )
    repositories = [
        repository_record("chummer-android", "app", android_root),
        repository_record("chummer6-ui", "runtime", workspace_root / "chummer-presentation"),
        repository_record("chummer6-core", "runtime", workspace_root / "chummer-core-engine"),
        repository_record("chummer6-ui-kit", "runtime", workspace_root / "chummer-ui-kit"),
        repository_record("chummer6-hub", "contracts_and_validation", run_services),
        repository_record("chummer6-hub-registry", "contracts", workspace_root / "chummer-hub-registry"),
        repository_record("chummer6-media-factory", "contracts", resolve_media_root(workspace_root)),
        repository_record("chummer6-design", "validation", workspace_root / "chummer-design"),
    ]
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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--android-root", required=True, type=Path)
    parser.add_argument("--workspace-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    graph = build_graph(arguments.android_root, arguments.workspace_root.resolve())
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = arguments.output.with_suffix(arguments.output.suffix + ".tmp")
    temporary.write_text(json.dumps(graph, indent=2) + "\n", encoding="utf-8")
    temporary.replace(arguments.output)
    print(f"release source graph verified: {arguments.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
