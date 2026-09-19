#!/usr/bin/env python3
"""Select a small documentation check, never produce Android runtime evidence."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
import sys


SHA = re.compile(r"[0-9a-f]{40}")
OBSERVATION = re.compile(
    r"play/evidence/preview[1-9][0-9]*-(?:internal-observation\.md|"
    r"internal-browser-readback\.json|local-signing\.json|local-verification\.json)"
)
MAX_DOCUMENT_BYTES = 1024 * 1024


def git(repo: Path, *args: str) -> bytes:
    return subprocess.run(
        ["git", "-C", str(repo), *args], check=True, capture_output=True, timeout=30
    ).stdout


def document_path(path: str) -> bool:
    # Do not allow arbitrary docs/**: generated inventories there are authority.
    return path == "docs/PLAY_RELEASE.md" or OBSERVATION.fullmatch(path) is not None


def documentation_only(repo: Path, base: str, head: str) -> bool:
    if not SHA.fullmatch(base) or not SHA.fullmatch(head) or base == "0" * 40:
        return False
    for revision in (base, head):
        git(repo, "rev-parse", "--verify", f"{revision}^{{commit}}")
    raw = git(repo, "diff", "--raw", "--no-abbrev", "--no-renames", "-z",
              "--ignore-submodules=none", base, head, "--")
    if not raw:
        return False
    fields = raw.rstrip(b"\0").split(b"\0")
    if len(fields) % 2:
        raise ValueError("incomplete changed-file inventory")
    for header, name in zip(fields[::2], fields[1::2]):
        parts = header.decode("ascii").split()
        if len(parts) != 5 or not parts[0].startswith(":"):
            raise ValueError("invalid changed-file inventory")
        old_mode, new_mode = parts[0][1:], parts[1]
        if (parts[4] not in {"A", "M", "D"}
                or old_mode not in {"000000", "100644"}
                or new_mode not in {"000000", "100644"}
                or not document_path(name.decode("utf-8"))):
            return False
    return True


def strict_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate document JSON key")
        result[key] = value
    return result


def reject_constant(value):
    raise ValueError("non-finite document JSON value")


def validate_documents(repo: Path, base: str, head: str) -> None:
    # Called only after the *entire* tree diff passed the closed path/mode list.
    git(repo, "diff", "--check", base, head, "--")
    names = git(repo, "diff", "--name-only", "--no-renames", "-z",
                "--diff-filter=AM", base, head, "--").split(b"\0")
    for name in filter(None, names):
        path = name.decode("utf-8")
        ref = f"{head}:{path}"
        if int(git(repo, "cat-file", "-s", ref)) > MAX_DOCUMENT_BYTES:
            raise ValueError("release document exceeds byte limit")
        text = git(repo, "show", ref).decode("utf-8")
        if "\0" in text:
            raise ValueError("release document is not text")
        if path.endswith(".json"):
            value = json.loads(text, object_pairs_hook=strict_object,
                               parse_constant=reject_constant)
            if not isinstance(value, dict):
                raise ValueError("release observation must be a JSON object")
            if path.endswith("-internal-browser-readback.json"):
                # Scripts cannot change in a docs-only diff; reuse the owner validator.
                sys.path.insert(0, str(repo / "scripts"))
                from materialize_next_play_internal_publication_receipt import validate_browser_readback
                validate_browser_readback(value)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, required=True)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    args = parser.parse_args()
    try:
        docs = documentation_only(args.repository, args.base, args.head)
        if docs:
            validate_documents(args.repository, args.base, args.head)
        print(f"docs-only={str(docs).lower()}")
        return 0
    except (OSError, ValueError, UnicodeError, subprocess.SubprocessError):
        # Never emit a successful docs decision after a missing/truncated inventory.
        print("change classification or document validation failed", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
