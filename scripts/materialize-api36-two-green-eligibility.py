#!/usr/bin/env python3
"""Materialize a read-only ordered review-to-main green eligibility receipt.

The input metadata must be the canonical GitHub Actions REST responses for two
explicit operator-selected runs: one reviewed green followed by one later main
green for the same source tree. This lifecycle ordering does not assert that no
other workflow runs occurred between them. This tool never discovers a run by
branch or "latest" ordering and never grants signing, Play upload, or
publication authority.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
from typing import Any
import zipfile


SCRIPT_DIRECTORY = Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

from api36_wizard_gate_contract import (  # noqa: E402
    AGGREGATE_SCHEMA,
    AUTHORITY_CLASS,
    DEFAULT_CONTRACT as DEFAULT_WIZARD_GATE,
    PROOF_SCOPE,
    contract_binding,
    journey_map,
)
from read_android_version import read_project_version_bytes  # noqa: E402


def _load_p0_module() -> Any:
    path = SCRIPT_DIRECTORY / "materialize-android-p0-pr-authority.py"
    specification = importlib.util.spec_from_file_location(
        "android_two_green_p0_authority", path
    )
    if specification is None or specification.loader is None:
        raise ValueError("cannot load Android P0 authority contract")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def _load_environment_module() -> Any:
    path = SCRIPT_DIRECTORY / "api36_proof_environment_authority.py"
    specification = importlib.util.spec_from_file_location(
        "android_two_green_environment_authority", path
    )
    if specification is None or specification.loader is None:
        raise ValueError("cannot load API-36 proof environment authority contract")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


P0 = _load_p0_module()
ENVIRONMENT = _load_environment_module()
REPO_ROOT = SCRIPT_DIRECTORY.parent
POLICY_PATH = REPO_ROOT / "eng/api36-two-consecutive-green-authority.json"
ENVIRONMENT_POLICY_PATH = REPO_ROOT / "eng/api36-proof-environment-authority.json"
SOURCE_WORKFLOW = REPO_ROOT / ".github/workflows/api36-editing-e2e.yml"
POLICY_SCHEMA = "chummer.android.api36-ordered-review-main-green-policy/v2"
CONTRACT = "chummer.android.api36-ordered-review-main-green-eligibility/v2"
OUTPUT_NAME = "ANDROID_API36_TWO_GREEN_ELIGIBILITY.generated.json"
REPOSITORY = "ArchonMegalon/chummer-android"
PACKAGE_ID = "com.myexternalbrain.chummer"
HISTORICAL_VERSION_CODE_FLOOR = 11
WORKFLOW_NAME = "API 36 phone beta SR5 wizard E2E"
WORKFLOW_PATH = ".github/workflows/api36-editing-e2e.yml"
PROJECT_PATH = "src/Chummer.Android/Chummer.Android.csproj"
P0_SCHEMA = "chummer.android.p0-pr-authority/v1"
ELIGIBILITY_SCOPE = "current_preview_internal_testing_candidate"
REVIEW_EVENTS = ("pull_request",)
MAIN_REF = "refs/heads/main"
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
VERSION_NAME = re.compile(
    r"^[0-9]+(?:\.[0-9]+){2}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$"
)
ARTIFACT_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
RFC3339_UTC = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")
MAX_JSON_ARTIFACT_BYTES = 16 * 1024 * 1024
REQUIRED_JOB_NAMES = (
    "Build x64 emulator and ARM64 hosted debug candidates",
    "phone API 36 SR5 wizard persistence (creation-prerequisite)",
    "phone API 36 SR5 wizard persistence (career-active-skill-advance)",
    "phone API 36 SR5 wizard persistence (career-weapon-fire)",
    "phone API 36 SR5 wizard persistence (before-run-edge)",
    "phone API 36 SR5 wizard persistence (playtime-short-burst)",
    "phone API 36 SR5 wizard persistence (downtime-calendar)",
    "phone API 36 SR5 wizard persistence (after-run-settlement)",
    "Aggregate exact API 36 phone evidence",
)
DOES_NOT_ASSERT = (
    "google_play_upload",
    "google_play_processing",
    "tester_distribution",
    "tester_installation",
    "release_signing",
    "public_release_readiness",
    "publication_authority",
    "zero_intervening_workflow_runs",
    "non_android_dependency_commit_tree_reconstruction",
)


def canonical_json_bytes(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def pretty_json_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def canonical_sha256(value: object) -> str:
    return hashlib.sha256(canonical_json_bytes(value)).hexdigest()


def object_without_duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


class StableFile:
    def __init__(self, path: Path, label: str) -> None:
        if not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path:
            raise ValueError(f"{label} must be an absolute canonical non-symlink path")
        descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
        try:
            before = os.fstat(descriptor)
            if not stat.S_ISREG(before.st_mode):
                raise ValueError(f"{label} must be one regular file")
            chunks: list[bytes] = []
            while chunk := os.read(descriptor, 1024 * 1024):
                chunks.append(chunk)
            after = os.fstat(descriptor)
        finally:
            os.close(descriptor)
        self._identity = self._stat_identity(before)
        if self._identity != self._stat_identity(after):
            raise ValueError(f"{label} changed during capture")
        self.path = path
        self.label = label
        self.data = b"".join(chunks)
        if len(self.data) != before.st_size:
            raise ValueError(f"{label} size changed during capture")
        self.sha256 = hashlib.sha256(self.data).hexdigest()
        self.size = len(self.data)

    @staticmethod
    def _stat_identity(value: os.stat_result) -> tuple[int, ...]:
        return (
            value.st_dev,
            value.st_ino,
            value.st_mode,
            value.st_size,
            value.st_mtime_ns,
            value.st_ctime_ns,
        )

    def recheck(self) -> None:
        current = os.stat(self.path, follow_symlinks=False)
        if self._stat_identity(current) != self._identity:
            raise ValueError(f"{self.label} changed after capture")

    def json_value(self) -> object:
        try:
            value = json.loads(
                self.data.decode("utf-8", errors="strict"),
                object_pairs_hook=object_without_duplicates,
                parse_constant=lambda token: (_ for _ in ()).throw(
                    ValueError(f"non-finite JSON value: {token}")
                ),
            )
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError(f"{self.label} is not strict UTF-8 JSON") from error
        return value

    def json(self) -> dict[str, object]:
        value = self.json_value()
        if not isinstance(value, dict):
            raise ValueError(f"{self.label} must contain one JSON object")
        return value

    def json_array(self) -> list[object]:
        value = self.json_value()
        if not isinstance(value, list):
            raise ValueError(f"{self.label} must contain one JSON array")
        return value


def expected_policy() -> dict[str, object]:
    return {
        "schema": POLICY_SCHEMA,
        "repository": REPOSITORY,
        "sourceWorkflow": {"name": WORKFLOW_NAME, "path": WORKFLOW_PATH},
        "reviewEvents": list(REVIEW_EVENTS),
        "mainRun": {"event": "push", "headBranch": "main", "ref": MAIN_REF},
        "requiredJobs": list(REQUIRED_JOB_NAMES),
        "requiredArtifacts": [
            "chummer-android-api36-phone-sr5-wizard-aggregate",
            "chummer-android-p0-pr-authority",
        ],
        "aggregateSchema": AGGREGATE_SCHEMA,
        "p0AuthoritySchema": P0_SCHEMA,
        "eligibilityScope": ELIGIBILITY_SCOPE,
        "requiresDistinctRuns": True,
        "requiresReviewCompletionBeforeMainStart": True,
        "requiresExactPullRequestAuthority": True,
        "requiresEmptyActionsPullRequestSummaries": True,
        "requiresCommitAssociatedPullRequest": True,
        "requiresExactMergeCommitGraphs": True,
        "requiresExactAggregateCheckRun": True,
        "requiresCanonicalActionsDetailsUrls": True,
        "sequenceSemantics": (
            "reviewed_green_followed_later_by_main_green_not_run_adjacency"
        ),
        "requiresExactSameAndroidTree": True,
        "requiresExactMainCommit": True,
        "requiresExactReleaseIdentity": True,
        "requiresExactDependencyGraph": True,
        "requiresExactSameAuthorityIdentities": True,
        "requiresCompatibleEnvironmentFingerprints": True,
        "requiresEnvironmentCompatibilityPass": True,
        "requiresSuccessfulMainRun": True,
        "requiresSuccessfulMainAggregate": True,
        "internalTestingEligibleWhenSatisfied": True,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }


def policy_binding(snapshot: StableFile) -> dict[str, object]:
    if snapshot.json() != expected_policy():
        raise ValueError("two-green policy differs from the closed authority")
    return {
        "schema": POLICY_SCHEMA,
        "path": "eng/api36-two-consecutive-green-authority.json",
        "sha256": snapshot.sha256,
        "sizeBytes": snapshot.size,
        "publicationAuthorized": False,
    }


def _positive_integer(value: object, label: str) -> int:
    if type(value) is not int or value <= 0:
        raise ValueError(f"{label} must be a positive integer")
    return value


def _sha40(value: object, label: str) -> str:
    if not isinstance(value, str) or SHA40.fullmatch(value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-40")
    return value


def _sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or SHA256.fullmatch(value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-256")
    return value


def release_identity(project: StableFile) -> dict[str, object]:
    try:
        version_name, version_code_text = read_project_version_bytes(project.data)
    except SystemExit as error:
        raise ValueError("Android release identity is not canonical") from error
    if (
        len(version_name) > 128
        or VERSION_NAME.fullmatch(version_name) is None
        or not version_code_text.isascii()
        or not version_code_text.isdigit()
        or version_code_text.startswith("0")
    ):
        raise ValueError("Android release identity is not canonical")
    version_code = int(version_code_text)
    if version_code <= HISTORICAL_VERSION_CODE_FLOOR:
        raise ValueError("Android release identity is not newer than Preview.11")
    return {
        "packageId": PACKAGE_ID,
        "versionName": version_name,
        "versionCode": version_code,
        "intentAuthority": "android_project_at_exact_main_tree",
    }


def _utc(value: object, label: str) -> datetime:
    if not isinstance(value, str) or RFC3339_UTC.fullmatch(value) is None:
        raise ValueError(f"{label} must be canonical UTC RFC3339 seconds")
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.utcoffset() != timezone.utc.utcoffset(parsed):
        raise ValueError(f"{label} must be UTC")
    return parsed


def git_source_tree(
    android_root: Path,
    workflow: StableFile,
    project: StableFile,
) -> str:
    """Bind the governed workflow to one clean tracked HEAD tree.

    The current Android checkout is available, so its cleanliness and workflow
    blob are verified directly instead of inferred.  Non-Android repositories
    are not inputs, so their commit-to-tree relationships remain transitively
    bound by the immutable P0 artifact rather than reconstructed here.
    """
    if (
        not android_root.is_absolute()
        or android_root.is_symlink()
        or android_root.resolve(strict=True) != android_root
        or not android_root.is_dir()
    ):
        raise ValueError(
            "Android root must be an absolute canonical non-symlink directory"
        )
    expected_workflow = android_root / WORKFLOW_PATH
    if workflow.path != expected_workflow:
        raise ValueError("source API-36 workflow must be the governed checkout path")
    expected_project = android_root / PROJECT_PATH
    if project.path != expected_project:
        raise ValueError("Android project must be the governed checkout path")
    environment = {
        "PATH": "/usr/bin:/bin",
        "LANG": "C",
        "LC_ALL": "C",
        "GIT_NO_REPLACE_OBJECTS": "1",
    }

    def git_bytes(*arguments: str) -> bytes:
        try:
            completed = subprocess.run(
                ["/usr/bin/git", "-C", os.fspath(android_root), *arguments],
                check=False,
                capture_output=True,
                timeout=20,
                env=environment,
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            raise ValueError(
                "cannot authenticate the governed Android checkout"
            ) from error
        if completed.returncode != 0:
            raise ValueError("cannot authenticate the governed Android checkout")
        return completed.stdout

    if git_bytes("status", "--porcelain=v1", "--untracked-files=all"):
        raise ValueError("governed Android checkout is not clean")
    head = _sha40(
        git_bytes("rev-parse", "HEAD").decode("ascii").strip(),
        "checked-out Android head",
    )
    tree = _sha40(
        git_bytes("rev-parse", f"{head}^{{tree}}").decode("ascii").strip(),
        "checked-out Android tree",
    )
    entry = git_bytes("ls-tree", "-z", head, "--", WORKFLOW_PATH)
    expected_prefix = f"100644 blob ".encode("ascii")
    suffix = b"\t" + WORKFLOW_PATH.encode("utf-8") + b"\x00"
    if (
        len(entry) != len(expected_prefix) + 40 + len(suffix)
        or not entry.startswith(expected_prefix)
        or entry[-len(suffix):] != suffix
        or SHA40.fullmatch(
            entry[len(expected_prefix):len(expected_prefix) + 40].decode("ascii")
        )
        is None
    ):
        raise ValueError("source API-36 workflow is not one exact tracked regular file")
    tracked_bytes = git_bytes("show", f"{head}:{WORKFLOW_PATH}")
    if tracked_bytes != workflow.data:
        raise ValueError("source API-36 workflow bytes differ from tracked HEAD")
    project_entry = git_bytes("ls-tree", "-z", head, "--", PROJECT_PATH)
    project_suffix = b"\t" + PROJECT_PATH.encode("utf-8") + b"\x00"
    if (
        len(project_entry) != len(expected_prefix) + 40 + len(project_suffix)
        or not project_entry.startswith(expected_prefix)
        or project_entry[-len(project_suffix):] != project_suffix
        or SHA40.fullmatch(
            project_entry[
                len(expected_prefix):len(expected_prefix) + 40
            ].decode("ascii")
        )
        is None
    ):
        raise ValueError("Android project is not one exact tracked regular file")
    if git_bytes("show", f"{head}:{PROJECT_PATH}") != project.data:
        raise ValueError("Android project bytes differ from tracked HEAD")
    tracked_index = git_bytes("ls-files", "-v", "-z")
    if any(
        not entry.startswith(b"H ")
        for entry in tracked_index.split(b"\0")
        if entry
    ):
        raise ValueError("governed Android checkout contains hidden index flags")
    if (
        git_bytes("rev-parse", "HEAD").decode("ascii").strip() != head
        or git_bytes("status", "--porcelain=v1", "--untracked-files=all")
        or git_bytes("ls-files", "-v", "-z") != tracked_index
    ):
        raise ValueError("governed Android checkout changed during authentication")
    return tree


def validate_run_metadata(
    value: dict[str, object], *, expected_id: int, role: str
) -> dict[str, object]:
    repository = value.get("repository")
    head_repository = value.get("head_repository")
    if (
        not isinstance(repository, dict)
        or repository.get("full_name") != REPOSITORY
        or repository.get("url") != f"https://api.github.com/repos/{REPOSITORY}"
        or repository.get("html_url") != f"https://github.com/{REPOSITORY}"
    ):
        raise ValueError(f"{role} run repository differs")
    if (
        not isinstance(head_repository, dict)
        or head_repository.get("full_name") != REPOSITORY
        or head_repository.get("url")
        != f"https://api.github.com/repos/{REPOSITORY}"
        or head_repository.get("html_url") != f"https://github.com/{REPOSITORY}"
    ):
        raise ValueError(f"{role} run head repository differs")
    run_id = _positive_integer(value.get("id"), f"{role} run ID")
    attempt = _positive_integer(value.get("run_attempt"), f"{role} run attempt")
    workflow_id = _positive_integer(value.get("workflow_id"), f"{role} workflow ID")
    event = value.get("event")
    branch = value.get("head_branch")
    if run_id != expected_id:
        raise ValueError(f"{role} run ID differs from explicit operator input")
    api_root = f"https://api.github.com/repos/{REPOSITORY}"
    html_root = f"https://github.com/{REPOSITORY}"
    check_suite_id = _positive_integer(
        value.get("check_suite_id"), f"{role} check suite ID"
    )
    if (
        value.get("url") != f"{api_root}/actions/runs/{run_id}"
        or value.get("html_url") != f"{html_root}/actions/runs/{run_id}"
        or value.get("jobs_url") != f"{api_root}/actions/runs/{run_id}/jobs"
        or value.get("artifacts_url")
        != f"{api_root}/actions/runs/{run_id}/artifacts"
        or value.get("check_suite_url")
        != f"{api_root}/check-suites/{check_suite_id}"
    ):
        raise ValueError(f"{role} run API/details authority differs")
    if value.get("name") != WORKFLOW_NAME or value.get("path") != WORKFLOW_PATH:
        raise ValueError(f"{role} run workflow identity differs")
    if value.get("status") != "completed" or value.get("conclusion") != "success":
        raise ValueError(f"{role} run is not completed successfully")
    if role == "review":
        if event not in REVIEW_EVENTS:
            raise ValueError("review run is not an exact pull_request run")
        if not isinstance(branch, str) or not branch or branch == "main":
            raise ValueError("review run head branch is invalid")
    elif role == "main":
        if event != "push" or branch != "main":
            raise ValueError("main run must be a refs/heads/main push")
    else:
        raise ValueError(f"unsupported run role: {role}")
    created = _utc(value.get("created_at"), f"{role} created_at")
    started = _utc(value.get("run_started_at"), f"{role} run_started_at")
    updated = _utc(value.get("updated_at"), f"{role} updated_at")
    if not created <= started <= updated:
        raise ValueError(f"{role} run timestamps are not monotonic")
    pull_requests = value.get("pull_requests")
    if not isinstance(pull_requests, list):
        raise ValueError(f"{role} run pull request summary is malformed")
    if pull_requests:
        raise ValueError(
            f"{role} run must use the independently authenticated empty "
            "pull request summary path"
        )
    return {
        "id": run_id,
        "attempt": attempt,
        "event": event,
        "ref": MAIN_REF if role == "main" else f"refs/heads/{branch}",
        "headBranch": branch,
        "headSha": _sha40(value.get("head_sha"), f"{role} head SHA"),
        "workflowId": workflow_id,
        "checkSuiteId": check_suite_id,
        "reportedPullRequests": [],
        "createdAtUtc": value["created_at"],
        "startedAtUtc": value["run_started_at"],
        "completedAtUtc": value["updated_at"],
        "status": "completed",
        "conclusion": "success",
    }


def validate_jobs(
    value: dict[str, object], *, run: dict[str, object], role: str
) -> dict[str, object]:
    jobs = value.get("jobs")
    total = value.get("total_count")
    if type(total) is not int or not isinstance(jobs, list) or total != len(jobs):
        raise ValueError(f"{role} jobs response is truncated or malformed")
    if len(jobs) != len(REQUIRED_JOB_NAMES):
        raise ValueError(f"{role} run must contain exactly the required jobs")
    rows: dict[str, dict[str, object]] = {}
    identifiers: set[int] = set()
    for raw in jobs:
        if not isinstance(raw, dict) or not isinstance(raw.get("name"), str):
            raise ValueError(f"{role} job row is malformed")
        name = raw["name"]
        if name in rows:
            raise ValueError(f"{role} job name is duplicated: {name}")
        job_id = _positive_integer(raw.get("id"), f"{role} job ID")
        if job_id in identifiers:
            raise ValueError(f"{role} job ID is duplicated")
        identifiers.add(job_id)
        api_root = f"https://api.github.com/repos/{REPOSITORY}"
        html_root = f"https://github.com/{REPOSITORY}"
        if (
            raw.get("status") != "completed"
            or raw.get("conclusion") != "success"
            or raw.get("workflow_name") != WORKFLOW_NAME
            or raw.get("run_id") != run["id"]
            or raw.get("run_attempt") != run["attempt"]
            or raw.get("head_sha") != run["headSha"]
            or raw.get("url") != f"{api_root}/actions/jobs/{job_id}"
            or raw.get("html_url")
            != f"{html_root}/actions/runs/{run['id']}/job/{job_id}"
            or raw.get("check_run_url") != f"{api_root}/check-runs/{job_id}"
        ):
            raise ValueError(f"{role} job is not exact and successful: {name}")
        started = _utc(raw.get("started_at"), f"{role} job started_at")
        completed = _utc(raw.get("completed_at"), f"{role} job completed_at")
        if started > completed:
            raise ValueError(f"{role} job timestamps are not monotonic: {name}")
        rows[name] = {
            "id": job_id,
            "status": "completed",
            "conclusion": "success",
            "startedAtUtc": raw["started_at"],
            "completedAtUtc": raw["completed_at"],
            "detailsUrl": raw["html_url"],
            "checkRunUrl": raw["check_run_url"],
        }
    if set(rows) != set(REQUIRED_JOB_NAMES):
        raise ValueError(f"{role} run job names differ from the exact gate")
    return {name: rows[name] for name in REQUIRED_JOB_NAMES}


def validate_git_commit_authority(
    snapshot: StableFile,
    *,
    expected_sha: str,
    expected_tree: str | None,
    expected_parents: list[str] | None,
    label: str,
) -> dict[str, object]:
    value = snapshot.json()
    api_root = f"https://api.github.com/repos/{REPOSITORY}"
    html_root = f"https://github.com/{REPOSITORY}"
    tree = value.get("tree")
    parents = value.get("parents")
    if not isinstance(tree, dict):
        raise ValueError(f"{label} commit identity differs")
    observed_tree = _sha40(tree.get("sha"), f"{label} commit tree")
    if (
        value.get("sha") != expected_sha
        or value.get("url") != f"{api_root}/git/commits/{expected_sha}"
        or value.get("html_url") != f"{html_root}/commit/{expected_sha}"
        or (expected_tree is not None and observed_tree != expected_tree)
        or tree.get("url") != f"{api_root}/git/trees/{observed_tree}"
        or not isinstance(parents, list)
        or (
            expected_parents is not None
            and len(parents) != len(expected_parents)
        )
    ):
        raise ValueError(f"{label} commit identity differs")
    parent_shas: list[str] = []
    for index, parent in enumerate(parents):
        expected_parent = (
            expected_parents[index]
            if expected_parents is not None
            else _sha40(
                parent.get("sha") if isinstance(parent, dict) else None,
                f"{label} commit parent SHA",
            )
        )
        if (
            not isinstance(parent, dict)
            or parent.get("sha") != expected_parent
            or parent.get("url") != f"{api_root}/git/commits/{expected_parent}"
            or parent.get("html_url") != f"{html_root}/commit/{expected_parent}"
        ):
            raise ValueError(f"{label} commit parent authority differs")
        parent_shas.append(expected_parent)
    return {
        "sha": expected_sha,
        "tree": observed_tree,
        "parents": parent_shas,
        "apiSnapshotSha256": snapshot.sha256,
        "apiSnapshotSizeBytes": snapshot.size,
    }


def validate_commit_pull_request_association(
    snapshot: StableFile,
    *,
    number: int,
    base_sha: str,
    merged_at: str,
    review_run: dict[str, object],
    main_run: dict[str, object],
) -> dict[str, object]:
    rows = snapshot.json_array()
    if len(rows) != 1 or not isinstance(rows[0], dict):
        raise ValueError("review head must be associated with exactly one pull request")
    value = rows[0]
    api_root = f"https://api.github.com/repos/{REPOSITORY}"
    html_root = f"https://github.com/{REPOSITORY}"
    base = value.get("base")
    head = value.get("head")
    if (
        value.get("number") != number
        or value.get("url") != f"{api_root}/pulls/{number}"
        or value.get("html_url") != f"{html_root}/pull/{number}"
        or value.get("state") != "closed"
        or value.get("merge_commit_sha") != main_run["headSha"]
        or value.get("merged_at") != merged_at
        or not isinstance(base, dict)
        or base.get("ref") != "main"
        or base.get("sha") != base_sha
        or not isinstance(base.get("repo"), dict)
        or base["repo"].get("full_name") != REPOSITORY
        or base["repo"].get("url") != api_root
        or base["repo"].get("html_url") != html_root
        or not isinstance(head, dict)
        or head.get("ref") != review_run["headBranch"]
        or head.get("sha") != review_run["headSha"]
        or not isinstance(head.get("repo"), dict)
        or head["repo"].get("full_name") != REPOSITORY
        or head["repo"].get("url") != api_root
        or head["repo"].get("html_url") != html_root
    ):
        raise ValueError("review head pull request association differs")
    return {
        "endpoint": f"{api_root}/commits/{review_run['headSha']}/pulls",
        "number": number,
        "apiSnapshotSha256": snapshot.sha256,
        "apiSnapshotSizeBytes": snapshot.size,
    }


def validate_aggregate_check_run_authority(
    snapshot: StableFile,
    *,
    review: dict[str, object],
) -> dict[str, object]:
    value = snapshot.json()
    run = review["run"]
    jobs = review["jobs"]
    assert isinstance(run, dict)
    assert isinstance(jobs, dict)
    aggregate_job = jobs[REQUIRED_JOB_NAMES[-1]]
    assert isinstance(aggregate_job, dict)
    check_run_id = aggregate_job["id"]
    check_suite = value.get("check_suite")
    app = value.get("app")
    api_url = f"https://api.github.com/repos/{REPOSITORY}/check-runs/{check_run_id}"
    details_url = (
        f"https://github.com/{REPOSITORY}/actions/runs/{run['id']}/job/"
        f"{check_run_id}"
    )
    if (
        value.get("id") != check_run_id
        or value.get("name") != REQUIRED_JOB_NAMES[-1]
        or value.get("head_sha") != run["headSha"]
        or value.get("status") != "completed"
        or value.get("conclusion") != "success"
        or value.get("url") != api_url
        or value.get("html_url") != details_url
        or value.get("details_url") != details_url
        or aggregate_job.get("checkRunUrl") != api_url
        or aggregate_job.get("detailsUrl") != details_url
        or not isinstance(check_suite, dict)
        or check_suite.get("id") != run["checkSuiteId"]
        or not isinstance(app, dict)
        or app.get("id") != 15368
        or app.get("slug") != "github-actions"
    ):
        raise ValueError("review aggregate check-run authority differs")
    pull_requests = value.get("pull_requests")
    if not isinstance(pull_requests, list) or pull_requests:
        raise ValueError("review aggregate check-run pull request summary is malformed")
    return {
        "id": check_run_id,
        "name": REQUIRED_JOB_NAMES[-1],
        "headSha": run["headSha"],
        "checkSuiteId": run["checkSuiteId"],
        "app": {"id": 15368, "slug": "github-actions"},
        "status": "completed",
        "conclusion": "success",
        "detailsUrl": details_url,
        "reportedPullRequests": [],
        "apiSnapshotSha256": snapshot.sha256,
        "apiSnapshotSizeBytes": snapshot.size,
    }


def validate_review_pull_request_authority(
    *,
    pull_request_number: int,
    pull_request_snapshot: StableFile,
    head_pull_requests_snapshot: StableFile,
    base_commit_snapshot: StableFile,
    head_commit_snapshot: StableFile,
    review_event_commit_snapshot: StableFile,
    main_commit_snapshot: StableFile,
    review: dict[str, object],
    main: dict[str, object],
    local_tree: str,
) -> dict[str, object]:
    number = _positive_integer(pull_request_number, "review pull request number")
    review_run = review["run"]
    main_run = main["run"]
    assert isinstance(review_run, dict)
    assert isinstance(main_run, dict)
    value = pull_request_snapshot.json()
    api_root = f"https://api.github.com/repos/{REPOSITORY}"
    html_root = f"https://github.com/{REPOSITORY}"
    base = value.get("base")
    head = value.get("head")
    repository_api_url = f"{api_root}"
    repository_html_url = f"{html_root}"
    if (
        value.get("number") != number
        or value.get("url") != f"{api_root}/pulls/{number}"
        or value.get("html_url") != f"{html_root}/pull/{number}"
        or value.get("commits_url") != f"{api_root}/pulls/{number}/commits"
        or value.get("statuses_url")
        != f"{api_root}/statuses/{review_run['headSha']}"
        or value.get("state") != "closed"
        or value.get("merged") is not True
        or value.get("merge_commit_sha") != main_run["headSha"]
        or not isinstance(base, dict)
        or not isinstance(base.get("repo"), dict)
        or base["repo"].get("full_name") != REPOSITORY
        or base["repo"].get("url") != repository_api_url
        or base["repo"].get("html_url") != repository_html_url
        or base.get("ref") != "main"
        or not isinstance(head, dict)
        or not isinstance(head.get("repo"), dict)
        or head["repo"].get("full_name") != REPOSITORY
        or head["repo"].get("url") != repository_api_url
        or head["repo"].get("html_url") != repository_html_url
        or head.get("ref") != review_run["headBranch"]
        or head.get("sha") != review_run["headSha"]
    ):
        raise ValueError("review pull request identity differs")
    base_sha = _sha40(base.get("sha"), "review pull request base SHA")
    if review.get("p0BaseSha") != base_sha:
        raise ValueError("review pull request base differs from the P0 authority")
    merged_at = _utc(value.get("merged_at"), "review pull request merged_at")
    review_completed = _utc(
        review_run["completedAtUtc"], "review completion time"
    )
    main_started = _utc(main_run["startedAtUtc"], "main start time")
    if not review_completed <= merged_at <= main_started:
        raise ValueError("pull request merge is not between review and main runs")
    reported = review_run.get("reportedPullRequests")
    if reported not in ([], [number]):
        raise ValueError("Actions run pull request summary disagrees with PR authority")
    parents = [base_sha, review_run["headSha"]]
    base_commit = validate_git_commit_authority(
        base_commit_snapshot,
        expected_sha=base_sha,
        expected_tree=None,
        expected_parents=None,
        label="pull request base",
    )
    head_commit = validate_git_commit_authority(
        head_commit_snapshot,
        expected_sha=review_run["headSha"],
        expected_tree=local_tree,
        expected_parents=None,
        label="pull request head",
    )
    review_commit = validate_git_commit_authority(
        review_event_commit_snapshot,
        expected_sha=review["p0EventSha"],
        expected_tree=local_tree,
        expected_parents=parents,
        label="review event",
    )
    main_commit = validate_git_commit_authority(
        main_commit_snapshot,
        expected_sha=main_run["headSha"],
        expected_tree=local_tree,
        expected_parents=parents,
        label="main merge",
    )
    association = validate_commit_pull_request_association(
        head_pull_requests_snapshot,
        number=number,
        base_sha=base_sha,
        merged_at=value["merged_at"],
        review_run=review_run,
        main_run=main_run,
    )
    return {
        "number": number,
        "repository": REPOSITORY,
        "url": value["html_url"],
        "base": {"ref": "main", "sha": base_sha},
        "head": {"ref": head["ref"], "sha": head["sha"]},
        "commitAssociation": association,
        "baseCommit": base_commit,
        "headCommit": head_commit,
        "reviewEventCommit": review_commit,
        "mainMergeCommit": main_commit,
        "apiSnapshotSha256": pull_request_snapshot.sha256,
        "apiSnapshotSizeBytes": pull_request_snapshot.size,
    }


def validate_artifact_metadata(
    value: dict[str, object], *, run: dict[str, object], role: str
) -> dict[str, dict[str, object]]:
    artifacts = value.get("artifacts")
    total = value.get("total_count")
    if type(total) is not int or not isinstance(artifacts, list) or total != len(artifacts):
        raise ValueError(f"{role} artifacts response is truncated or malformed")
    expected_names = {
        "aggregate": (
            f"chummer-android-api36-phone-sr5-wizard-aggregate-"
            f"{run['id']}-{run['attempt']}"
        ),
        "p0": f"chummer-android-p0-pr-authority-{run['id']}-{run['attempt']}",
    }
    selected: dict[str, dict[str, object]] = {}
    for kind, expected_name in expected_names.items():
        matches = [row for row in artifacts if isinstance(row, dict) and row.get("name") == expected_name]
        if len(matches) != 1:
            raise ValueError(f"{role} {kind} artifact cardinality differs")
        raw = matches[0]
        workflow_run = raw.get("workflow_run")
        digest = raw.get("digest")
        if (
            raw.get("expired") is not False
            or not isinstance(workflow_run, dict)
            or workflow_run.get("id") != run["id"]
            or workflow_run.get("head_sha") != run["headSha"]
            or not isinstance(digest, str)
            or ARTIFACT_DIGEST.fullmatch(digest) is None
        ):
            raise ValueError(f"{role} {kind} artifact authority differs")
        selected[kind] = {
            "id": _positive_integer(raw.get("id"), f"{role} {kind} artifact ID"),
            "name": expected_name,
            "sizeBytes": _positive_integer(
                raw.get("size_in_bytes"), f"{role} {kind} artifact size"
            ),
            "digest": digest,
            "createdAtUtc": raw.get("created_at"),
            "expiresAtUtc": raw.get("expires_at"),
        }
        artifact_created = _utc(
            raw.get("created_at"), f"{role} {kind} artifact created_at"
        )
        artifact_expires = _utc(
            raw.get("expires_at"), f"{role} {kind} artifact expires_at"
        )
        if artifact_created > artifact_expires:
            raise ValueError(f"{role} {kind} artifact timestamps are not monotonic")
    if selected["aggregate"]["id"] == selected["p0"]["id"]:
        raise ValueError(f"{role} artifacts must be distinct")
    return selected


def extract_exact_json_archive(
    snapshot: StableFile,
    *,
    metadata: dict[str, object],
    expected_member: str,
) -> tuple[dict[str, object], dict[str, object], bytes]:
    if snapshot.size != metadata["sizeBytes"]:
        raise ValueError(f"artifact archive size differs: {metadata['name']}")
    if f"sha256:{snapshot.sha256}" != metadata["digest"]:
        raise ValueError(f"artifact archive digest differs: {metadata['name']}")
    try:
        with zipfile.ZipFile(snapshot.path, "r") as archive:
            members = archive.infolist()
            if len(members) != 1 or members[0].filename != expected_member:
                raise ValueError(
                    f"artifact archive must contain exactly {expected_member}"
                )
            member = members[0]
            unix_mode = (member.external_attr >> 16) & 0xFFFF
            if (
                member.is_dir()
                or member.flag_bits & 0x1
                or stat.S_IFMT(unix_mode) == stat.S_IFLNK
                or member.file_size <= 0
                or member.file_size > MAX_JSON_ARTIFACT_BYTES
                or member.filename != Path(member.filename).name
            ):
                raise ValueError("artifact archive member is unsafe")
            data = archive.read(member)
    except (zipfile.BadZipFile, RuntimeError) as error:
        raise ValueError("artifact archive is invalid") from error
    if len(data) != member.file_size:
        raise ValueError("artifact member size changed during extraction")
    try:
        value = json.loads(
            data.decode("utf-8", errors="strict"),
            object_pairs_hook=object_without_duplicates,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"non-finite JSON value: {token}")
            ),
        )
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("artifact member is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise ValueError("artifact member must be one JSON object")
    binding = {
        **metadata,
        "archiveSha256": snapshot.sha256,
        "memberName": expected_member,
        "memberSha256": hashlib.sha256(data).hexdigest(),
        "memberSizeBytes": len(data),
    }
    return value, binding, data


def normalized_dependency_authority(
    *,
    p0: dict[str, object],
    local_tree: str,
    role: str,
) -> dict[str, object]:
    """Bind the common dependency graph while excluding only Android commit identity.

    A pull-request run checks out GitHub's tested merge commit, while the later
    main push normally has a distinct merge commit with the same tree.  Those
    commit identities are already bound by each run's P0 authority.  The
    two-green common authority therefore compares the exact Android tree and
    every non-Android source identity, but not those two expected run-local
    Android commit IDs.
    """
    graph = p0.get("dependencyGraph")
    if not isinstance(graph, dict) or set(graph) != {"mode", "sources", "sha256"}:
        raise ValueError(f"{role} P0 dependency graph fields differ")
    mode = graph.get("mode")
    if mode != {"localCompatibilityTree": True, "packageOnly": False}:
        raise ValueError(f"{role} P0 dependency mode differs")
    sources = graph.get("sources")
    expected_repositories = P0.HOSTED.EXPECTED_SOURCES
    if not isinstance(sources, dict) or set(sources) != set(expected_repositories):
        raise ValueError(f"{role} P0 dependency source set differs")

    validated_sources: dict[str, dict[str, object]] = {}
    for name in sorted(expected_repositories):
        source = sources.get(name)
        if (
            not isinstance(source, dict)
            or set(source) != {"commit", "repository", "tree"}
            or source.get("repository") != expected_repositories[name]
        ):
            raise ValueError(f"{role} P0 dependency source authority differs: {name}")
        commit = _sha40(source.get("commit"), f"{role} {name} dependency commit")
        tree = _sha40(source.get("tree"), f"{role} {name} dependency tree")
        if name != "android" and commit != P0.EXPECTED_DEPENDENCY_COMMITS[name]:
            raise ValueError(f"{role} P0 dependency commit differs: {name}")
        validated_sources[name] = {
            "commit": commit,
            "repository": source["repository"],
            "tree": tree,
        }

    raw_graph = {"mode": mode, "sources": validated_sources}
    if graph.get("sha256") != canonical_sha256(raw_graph):
        raise ValueError(f"{role} P0 dependency graph digest differs")

    github_run = p0.get("githubRun")
    android_source = p0.get("androidSource")
    android = validated_sources["android"]
    if (
        not isinstance(github_run, dict)
        or android["commit"] != github_run.get("eventSha")
        or android["tree"] != local_tree
        or android_source != {
            "checkedOutHead": android["commit"],
            "checkedOutTree": android["tree"],
            "repository": android["repository"],
        }
    ):
        raise ValueError(f"{role} P0 Android commit/tree authority differs")

    common_sources = {
        name: (
            {"repository": source["repository"], "tree": source["tree"]}
            if name == "android"
            else source
        )
        for name, source in validated_sources.items()
    }
    common_graph = {"mode": mode, "sources": common_sources}
    return {**common_graph, "sha256": canonical_sha256(common_graph)}


def validate_proof_artifacts(
    *,
    p0: dict[str, object],
    aggregate: dict[str, object],
    aggregate_bytes: bytes,
    run: dict[str, object],
    workflow_binding: dict[str, object],
    environment_policy_authority: dict[str, object],
    local_tree: str,
    role: str,
) -> dict[str, object]:
    P0.validate_authority(p0)
    gate = contract_binding(DEFAULT_WIZARD_GATE)
    P0.validate_aggregate(aggregate, gate)
    github_run = p0.get("githubRun")
    android_source = p0.get("androidSource")
    aggregate_binding = p0.get("aggregate")
    p0_inputs = p0.get("inputs")
    if (
        not isinstance(github_run, dict)
        or set(github_run)
        != {"attempt", "baseSha", "eventName", "eventSha", "headSha", "id"}
        or any(
            not isinstance(github_run.get(field), str)
            or SHA40.fullmatch(github_run[field]) is None
            for field in ("baseSha", "eventSha", "headSha")
        )
        or github_run.get("id") != run["id"]
        or github_run.get("attempt") != run["attempt"]
        or github_run.get("eventName") != run["event"]
        or github_run.get("headSha") != run["headSha"]
        or (role == "main" and github_run.get("eventSha") != run["headSha"])
        or (role == "main" and github_run.get("baseSha") != run["headSha"])
        or not isinstance(android_source, dict)
        or android_source.get("checkedOutTree") != local_tree
        or android_source.get("checkedOutHead") != github_run.get("eventSha")
        or p0.get("workflow") != workflow_binding
        or not isinstance(p0_inputs, dict)
        or set(p0_inputs) != {"hostedCandidate", "wizardGate"}
        or p0_inputs.get("wizardGate") != gate
        or not isinstance(aggregate_binding, dict)
        or aggregate_binding.get("schema") != AGGREGATE_SCHEMA
        or aggregate_binding.get("status") != "pass"
        or aggregate_binding.get("sha256") != hashlib.sha256(aggregate_bytes).hexdigest()
        or aggregate_binding.get("sizeBytes") != len(aggregate_bytes)
    ):
        raise ValueError(f"{role} P0/run/tree/aggregate authority differs")
    if aggregate.get("artifactAuthority", {}).get("runId") != run["id"]:
        raise ValueError(f"{role} aggregate run authority differs")
    dependency_authority = normalized_dependency_authority(
        p0=p0,
        local_tree=local_tree,
        role=role,
    )
    environment = aggregate["environmentAuthority"]
    assert isinstance(environment, dict)
    if environment["policyAuthority"] != environment_policy_authority:
        raise ValueError(f"{role} aggregate environment policy authority differs")
    build = environment["build"]
    assert isinstance(build, dict)
    return {
        "androidTree": local_tree,
        "authorityClass": p0["authorityClass"],
        "proofScope": p0["proofScope"],
        "dependencyGraph": dependency_authority,
        "workflow": p0["workflow"],
        "wizardGate": gate,
        "aggregateSchema": aggregate["schema"],
        "requiredJourneys": aggregate["requiredJourneys"],
        "environmentPolicy": environment["policyAuthority"],
        "buildEnvironmentCompatibilitySha256": _sha256(
            build.get("compatibilitySha256"), f"{role} build environment compatibility"
        ),
        "journeyEnvironmentCompatibilitySha256": _sha256(
            environment.get("journeyCompatibilitySha256"),
            f"{role} journey environment compatibility",
        ),
        "environmentCompatibilityStatus": "pass",
    }


def run_evidence(
    *,
    role: str,
    expected_run_id: int,
    run_snapshot: StableFile,
    jobs_snapshot: StableFile,
    artifacts_snapshot: StableFile,
    aggregate_archive_snapshot: StableFile,
    p0_archive_snapshot: StableFile,
    workflow_binding: dict[str, object],
    environment_policy_authority: dict[str, object],
    local_tree: str,
) -> tuple[dict[str, object], dict[str, object]]:
    run = validate_run_metadata(
        run_snapshot.json(), expected_id=expected_run_id, role=role
    )
    jobs = validate_jobs(jobs_snapshot.json(), run=run, role=role)
    artifact_metadata = validate_artifact_metadata(
        artifacts_snapshot.json(), run=run, role=role
    )
    aggregate, aggregate_artifact, aggregate_bytes = extract_exact_json_archive(
        aggregate_archive_snapshot,
        metadata=artifact_metadata["aggregate"],
        expected_member="receipt.json",
    )
    p0, p0_artifact, _ = extract_exact_json_archive(
        p0_archive_snapshot,
        metadata=artifact_metadata["p0"],
        expected_member=P0.OUTPUT_NAME,
    )
    common = validate_proof_artifacts(
        p0=p0,
        aggregate=aggregate,
        aggregate_bytes=aggregate_bytes,
        run=run,
        workflow_binding=workflow_binding,
        environment_policy_authority=environment_policy_authority,
        local_tree=local_tree,
        role=role,
    )
    evidence = {
        "run": run,
        "jobs": jobs,
        "actionsMetadata": {
            "runSha256": run_snapshot.sha256,
            "jobsSha256": jobs_snapshot.sha256,
            "artifactsSha256": artifacts_snapshot.sha256,
        },
        "artifacts": {
            "aggregate": aggregate_artifact,
            "p0Authority": p0_artifact,
        },
        "p0AuthoritySha256": p0["authoritySha256"],
        "p0BaseSha": p0["githubRun"]["baseSha"],
        "p0EventSha": p0["githubRun"]["eventSha"],
        "aggregateStatus": aggregate["status"],
    }
    return evidence, common


def create_authority(
    *,
    android_root: Path,
    policy: Path,
    environment_policy: Path,
    source_workflow: Path,
    review_run_id: int,
    review_pull_request_number: int,
    review_event_sha: str,
    main_run_id: int,
    review_run: Path,
    review_jobs: Path,
    review_artifacts: Path,
    review_aggregate_archive: Path,
    review_p0_archive: Path,
    review_pull_request: Path,
    review_head_pull_requests: Path,
    review_aggregate_check_run: Path,
    review_base_commit: Path,
    review_head_commit: Path,
    review_event_commit: Path,
    main_run: Path,
    main_jobs: Path,
    main_artifacts: Path,
    main_aggregate_archive: Path,
    main_p0_archive: Path,
    main_commit: Path,
) -> dict[str, object]:
    if review_run_id == main_run_id:
        raise ValueError("review and main run IDs must be distinct")
    snapshots = {
        "policy": StableFile(policy, "two-green policy"),
        "environmentPolicy": StableFile(
            environment_policy, "API-36 proof environment policy"
        ),
        "workflow": StableFile(source_workflow, "source API-36 workflow"),
        "project": StableFile(
            android_root / PROJECT_PATH,
            "Android release project",
        ),
        "reviewRun": StableFile(review_run, "review run metadata"),
        "reviewJobs": StableFile(review_jobs, "review jobs metadata"),
        "reviewArtifacts": StableFile(review_artifacts, "review artifacts metadata"),
        "reviewAggregate": StableFile(review_aggregate_archive, "review aggregate archive"),
        "reviewP0": StableFile(review_p0_archive, "review P0 archive"),
        "reviewPullRequest": StableFile(
            review_pull_request, "review pull request API response"
        ),
        "reviewHeadPullRequests": StableFile(
            review_head_pull_requests,
            "review head commit-associated pull requests API response",
        ),
        "reviewAggregateCheckRun": StableFile(
            review_aggregate_check_run, "review aggregate check-run API response"
        ),
        "reviewBaseCommit": StableFile(
            review_base_commit, "review pull request base commit API response"
        ),
        "reviewHeadCommit": StableFile(
            review_head_commit, "review pull request head commit API response"
        ),
        "reviewEventCommit": StableFile(
            review_event_commit, "review event commit API response"
        ),
        "mainRun": StableFile(main_run, "main run metadata"),
        "mainJobs": StableFile(main_jobs, "main jobs metadata"),
        "mainArtifacts": StableFile(main_artifacts, "main artifacts metadata"),
        "mainAggregate": StableFile(main_aggregate_archive, "main aggregate archive"),
        "mainP0": StableFile(main_p0_archive, "main P0 archive"),
        "mainCommit": StableFile(main_commit, "main commit API response"),
    }
    policy_authority = policy_binding(snapshots["policy"])
    environment_policy_authority = ENVIRONMENT.policy_binding(
        snapshots["environmentPolicy"]
    )
    workflow_binding = {
        "path": WORKFLOW_PATH,
        "sha256": snapshots["workflow"].sha256,
        "sizeBytes": snapshots["workflow"].size,
    }
    local_tree = git_source_tree(
        android_root,
        snapshots["workflow"],
        snapshots["project"],
    )
    review, review_common = run_evidence(
        role="review",
        expected_run_id=review_run_id,
        run_snapshot=snapshots["reviewRun"],
        jobs_snapshot=snapshots["reviewJobs"],
        artifacts_snapshot=snapshots["reviewArtifacts"],
        aggregate_archive_snapshot=snapshots["reviewAggregate"],
        p0_archive_snapshot=snapshots["reviewP0"],
        workflow_binding=workflow_binding,
        environment_policy_authority=environment_policy_authority,
        local_tree=local_tree,
    )
    main, main_common = run_evidence(
        role="main",
        expected_run_id=main_run_id,
        run_snapshot=snapshots["mainRun"],
        jobs_snapshot=snapshots["mainJobs"],
        artifacts_snapshot=snapshots["mainArtifacts"],
        aggregate_archive_snapshot=snapshots["mainAggregate"],
        p0_archive_snapshot=snapshots["mainP0"],
        workflow_binding=workflow_binding,
        environment_policy_authority=environment_policy_authority,
        local_tree=local_tree,
    )
    if review_common != main_common:
        raise ValueError("review and main authority or environment compatibility differs")
    if _sha40(review_event_sha, "review event SHA") != review["p0EventSha"]:
        raise ValueError("explicit review event SHA differs from the P0 authority")
    pull_request_authority = validate_review_pull_request_authority(
        pull_request_number=review_pull_request_number,
        pull_request_snapshot=snapshots["reviewPullRequest"],
        head_pull_requests_snapshot=snapshots["reviewHeadPullRequests"],
        base_commit_snapshot=snapshots["reviewBaseCommit"],
        head_commit_snapshot=snapshots["reviewHeadCommit"],
        review_event_commit_snapshot=snapshots["reviewEventCommit"],
        main_commit_snapshot=snapshots["mainCommit"],
        review=review,
        main=main,
        local_tree=local_tree,
    )
    review["aggregateCheckRun"] = validate_aggregate_check_run_authority(
        snapshots["reviewAggregateCheckRun"],
        review=review,
    )
    review_completed = _utc(
        review["run"]["completedAtUtc"], "review completion time"
    )
    main_started = _utc(main["run"]["startedAtUtc"], "main start time")
    if review_run_id >= main_run_id or review_completed >= main_started:
        raise ValueError("main push run is not distinct and later than review completion")
    unsigned = {
        "schema": CONTRACT,
        "status": "pass",
        "eligibilityScope": ELIGIBILITY_SCOPE,
        "eligible": True,
        "internalTestingEligible": True,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "policyAuthority": policy_authority,
        "sourceCommit": main["run"]["headSha"],
        "sourceTree": local_tree,
        "releaseIdentity": release_identity(snapshots["project"]),
        "commonAuthority": review_common,
        "reviewPullRequest": pull_request_authority,
        "reviewRun": review,
        "mainRun": main,
        "decisionTimeUtc": main["run"]["completedAtUtc"],
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    result = {**unsigned, "eligibilitySha256": canonical_sha256(unsigned)}
    for snapshot in snapshots.values():
        snapshot.recheck()
    validate_authority(result)
    return result


def validate_authority(value: dict[str, object]) -> dict[str, object]:
    fields = {
        "schema", "status", "eligibilityScope", "eligible", "internalTestingEligible",
        "publicationAuthorized", "googlePlayUploadAuthorized", "policyAuthority",
        "sourceCommit", "sourceTree", "releaseIdentity", "commonAuthority",
        "reviewRun", "mainRun", "decisionTimeUtc",
        "reviewPullRequest", "doesNotAssert", "eligibilitySha256",
    }
    if set(value) != fields:
        raise ValueError("two-green eligibility fields are not exact")
    if (
        value["schema"] != CONTRACT
        or value["status"] != "pass"
        or value["eligibilityScope"] != ELIGIBILITY_SCOPE
        or value["eligible"] is not True
        or value["internalTestingEligible"] is not True
        or value["publicationAuthorized"] is not False
        or value["googlePlayUploadAuthorized"] is not False
        or value["doesNotAssert"] != list(DOES_NOT_ASSERT)
    ):
        raise ValueError("two-green eligibility posture is invalid")
    source_commit = _sha40(value["sourceCommit"], "two-green source commit")
    _sha40(value["sourceTree"], "two-green source tree")
    release = value["releaseIdentity"]
    if (
        not isinstance(release, dict)
        or set(release)
        != {"packageId", "versionName", "versionCode", "intentAuthority"}
        or release.get("packageId") != PACKAGE_ID
        or not isinstance(release.get("versionName"), str)
        or VERSION_NAME.fullmatch(release["versionName"]) is None
        or type(release.get("versionCode")) is not int
        or release["versionCode"] <= 10
        or release.get("intentAuthority") != "android_project_at_exact_main_tree"
    ):
        raise ValueError("two-green release identity is invalid")
    main_run = value["mainRun"]
    if (
        not isinstance(main_run, dict)
        or not isinstance(main_run.get("run"), dict)
        or main_run["run"].get("headSha") != source_commit
        or main_run.get("p0EventSha") != source_commit
        or main_run.get("aggregateStatus") != "pass"
    ):
        raise ValueError("two-green main commit or aggregate authority is invalid")
    common = value["commonAuthority"]
    if (
        not isinstance(common, dict)
        or common.get("androidTree") != value["sourceTree"]
        or common.get("environmentCompatibilityStatus") != "pass"
    ):
        raise ValueError("two-green common environment authority is invalid")
    unsigned = {key: member for key, member in value.items() if key != "eligibilitySha256"}
    if value["eligibilitySha256"] != canonical_sha256(unsigned):
        raise ValueError("two-green eligibility digest is invalid")
    return value


def write_atomically(path: Path, value: dict[str, object]) -> None:
    if path.name != OUTPUT_NAME:
        raise ValueError(f"output filename must be exactly {OUTPUT_NAME}")
    if not path.is_absolute() or path.is_symlink():
        raise ValueError("output must be an absolute non-symlink path")
    parent = path.parent
    if parent.is_symlink() or parent.resolve(strict=True) != parent:
        raise ValueError("output parent must be canonical and non-symlinked")
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb", dir=parent, prefix=f".{OUTPUT_NAME}.", delete=False
        ) as stream:
            temporary = stream.name
            os.fchmod(stream.fileno(), 0o600)
            stream.write(pretty_json_bytes(value))
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        temporary = None
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def _input_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--android-root", type=Path, required=True)
    parser.add_argument("--policy", type=Path, required=True)
    parser.add_argument("--environment-policy", type=Path, required=True)
    parser.add_argument("--source-workflow", type=Path, required=True)
    parser.add_argument("--review-run-id", type=int, required=True)
    parser.add_argument("--review-pull-request-number", type=int, required=True)
    parser.add_argument("--review-event-sha", required=True)
    parser.add_argument("--main-run-id", type=int, required=True)
    for role in ("review", "main"):
        parser.add_argument(f"--{role}-run", type=Path, required=True)
        parser.add_argument(f"--{role}-jobs", type=Path, required=True)
        parser.add_argument(f"--{role}-artifacts", type=Path, required=True)
        parser.add_argument(f"--{role}-aggregate-archive", type=Path, required=True)
        parser.add_argument(f"--{role}-p0-archive", type=Path, required=True)
    parser.add_argument("--review-pull-request", type=Path, required=True)
    parser.add_argument("--review-head-pull-requests", type=Path, required=True)
    parser.add_argument("--review-aggregate-check-run", type=Path, required=True)
    parser.add_argument("--review-base-commit", type=Path, required=True)
    parser.add_argument("--review-head-commit", type=Path, required=True)
    parser.add_argument("--review-event-commit", type=Path, required=True)
    parser.add_argument("--main-commit", type=Path, required=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    materialize = subparsers.add_parser("materialize")
    _input_arguments(materialize)
    materialize.add_argument("--output", type=Path, required=True)
    verify = subparsers.add_parser("verify")
    _input_arguments(verify)
    verify.add_argument("--authority", type=Path, required=True)
    args = parser.parse_args(argv)
    keyword_arguments = {
        key: value for key, value in vars(args).items()
        if key not in {"command", "output", "authority"}
    }
    expected = create_authority(**keyword_arguments)
    if args.command == "materialize":
        write_atomically(args.output, expected)
        print(
            f"two_green_eligibility=pass review_run={args.review_run_id} "
            f"main_run={args.main_run_id} internal_testing_eligible=true "
            "publication_authorized=false"
        )
        return 0
    observed = StableFile(args.authority, "two-green authority").json()
    validate_authority(observed)
    if observed != expected:
        raise ValueError("two-green authority does not replay from exact inputs")
    print("two_green_eligibility=verified publication_authorized=false")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
