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


P0 = _load_p0_module()
REPO_ROOT = SCRIPT_DIRECTORY.parent
POLICY_PATH = REPO_ROOT / "eng/api36-two-consecutive-green-authority.json"
SOURCE_WORKFLOW = REPO_ROOT / ".github/workflows/api36-editing-e2e.yml"
POLICY_SCHEMA = "chummer.android.api36-ordered-review-main-green-policy/v1"
CONTRACT = "chummer.android.api36-ordered-review-main-green-eligibility/v1"
OUTPUT_NAME = "ANDROID_API36_TWO_GREEN_ELIGIBILITY.generated.json"
REPOSITORY = "ArchonMegalon/chummer-android"
WORKFLOW_NAME = "API 36 phone beta SR5 wizard E2E"
WORKFLOW_PATH = ".github/workflows/api36-editing-e2e.yml"
P0_SCHEMA = "chummer.android.p0-pr-authority/v1"
ELIGIBILITY_SCOPE = "preview11_internal_testing_candidate"
REVIEW_EVENTS = ("pull_request", "merge_group")
MAIN_REF = "refs/heads/main"
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
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

    def json(self) -> dict[str, object]:
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
        if not isinstance(value, dict):
            raise ValueError(f"{self.label} must contain one JSON object")
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
        "sequenceSemantics": (
            "reviewed_green_followed_later_by_main_green_not_run_adjacency"
        ),
        "requiresExactSameAndroidTree": True,
        "requiresExactSameAuthorityIdentities": True,
        "requiresCompatibleEnvironmentFingerprints": True,
        "internalTestingEligibleWhenSatisfied": True,
        "publicationAuthorized": False,
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


def _utc(value: object, label: str) -> datetime:
    if not isinstance(value, str) or RFC3339_UTC.fullmatch(value) is None:
        raise ValueError(f"{label} must be canonical UTC RFC3339 seconds")
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.utcoffset() != timezone.utc.utcoffset(parsed):
        raise ValueError(f"{label} must be UTC")
    return parsed


def git_tree(android_root: Path) -> str:
    if not android_root.is_absolute() or android_root.is_symlink():
        raise ValueError("Android root must be an absolute non-symlink directory")
    completed = subprocess.run(
        ["git", "-C", str(android_root), "rev-parse", "HEAD^{tree}"],
        check=True,
        capture_output=True,
        text=True,
    )
    return _sha40(completed.stdout.strip(), "checked-out Android tree")


def validate_run_metadata(
    value: dict[str, object], *, expected_id: int, role: str
) -> dict[str, object]:
    repository = value.get("repository")
    if not isinstance(repository, dict) or repository.get("full_name") != REPOSITORY:
        raise ValueError(f"{role} run repository differs")
    run_id = _positive_integer(value.get("id"), f"{role} run ID")
    attempt = _positive_integer(value.get("run_attempt"), f"{role} run attempt")
    workflow_id = _positive_integer(value.get("workflow_id"), f"{role} workflow ID")
    event = value.get("event")
    branch = value.get("head_branch")
    if run_id != expected_id:
        raise ValueError(f"{role} run ID differs from explicit operator input")
    if value.get("name") != WORKFLOW_NAME or value.get("path") != WORKFLOW_PATH:
        raise ValueError(f"{role} run workflow identity differs")
    if value.get("status") != "completed" or value.get("conclusion") != "success":
        raise ValueError(f"{role} run is not completed successfully")
    if role == "review":
        if event not in REVIEW_EVENTS:
            raise ValueError("review run is not pull_request or merge_group")
        if not isinstance(branch, str) or not branch or branch == "main":
            raise ValueError("review run head branch is invalid")
        if event == "pull_request":
            pull_requests = value.get("pull_requests")
            if not isinstance(pull_requests, list) or len(pull_requests) != 1:
                raise ValueError("pull_request run must bind exactly one pull request")
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
    return {
        "id": run_id,
        "attempt": attempt,
        "event": event,
        "ref": MAIN_REF if role == "main" else f"refs/heads/{branch}",
        "headBranch": branch,
        "headSha": _sha40(value.get("head_sha"), f"{role} head SHA"),
        "workflowId": workflow_id,
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
        if (
            raw.get("status") != "completed"
            or raw.get("conclusion") != "success"
            or raw.get("workflow_name") != WORKFLOW_NAME
            or raw.get("run_id") != run["id"]
            or raw.get("run_attempt") != run["attempt"]
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
        }
    if set(rows) != set(REQUIRED_JOB_NAMES):
        raise ValueError(f"{role} run job names differ from the exact gate")
    return {name: rows[name] for name in REQUIRED_JOB_NAMES}


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


def validate_proof_artifacts(
    *,
    p0: dict[str, object],
    aggregate: dict[str, object],
    aggregate_bytes: bytes,
    run: dict[str, object],
    workflow_binding: dict[str, object],
    local_tree: str,
    role: str,
) -> dict[str, object]:
    P0.validate_authority(p0)
    gate = contract_binding(DEFAULT_WIZARD_GATE)
    P0.validate_aggregate(aggregate, gate)
    github_run = p0.get("githubRun")
    android_source = p0.get("androidSource")
    aggregate_binding = p0.get("aggregate")
    if (
        not isinstance(github_run, dict)
        or github_run.get("id") != run["id"]
        or github_run.get("attempt") != run["attempt"]
        or github_run.get("eventName") != run["event"]
        or github_run.get("headSha") != run["headSha"]
        or not isinstance(android_source, dict)
        or android_source.get("checkedOutTree") != local_tree
        or android_source.get("checkedOutHead") != github_run.get("eventSha")
        or p0.get("workflow") != workflow_binding
        or not isinstance(aggregate_binding, dict)
        or aggregate_binding.get("schema") != AGGREGATE_SCHEMA
        or aggregate_binding.get("status") != "pass"
        or aggregate_binding.get("sha256") != hashlib.sha256(aggregate_bytes).hexdigest()
        or aggregate_binding.get("sizeBytes") != len(aggregate_bytes)
    ):
        raise ValueError(f"{role} P0/run/tree/aggregate authority differs")
    if aggregate.get("artifactAuthority", {}).get("runId") != run["id"]:
        raise ValueError(f"{role} aggregate run authority differs")
    environment = aggregate["environmentAuthority"]
    assert isinstance(environment, dict)
    build = environment["build"]
    assert isinstance(build, dict)
    return {
        "androidTree": local_tree,
        "authorityClass": p0["authorityClass"],
        "proofScope": p0["proofScope"],
        "dependencyGraph": p0["dependencyGraph"],
        "workflow": p0["workflow"],
        "wizardGate": p0["inputs"]["wizardGate"],
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
        "aggregateStatus": aggregate["status"],
    }
    return evidence, common


def create_authority(
    *,
    android_root: Path,
    policy: Path,
    source_workflow: Path,
    review_run_id: int,
    main_run_id: int,
    review_run: Path,
    review_jobs: Path,
    review_artifacts: Path,
    review_aggregate_archive: Path,
    review_p0_archive: Path,
    main_run: Path,
    main_jobs: Path,
    main_artifacts: Path,
    main_aggregate_archive: Path,
    main_p0_archive: Path,
) -> dict[str, object]:
    if review_run_id == main_run_id:
        raise ValueError("review and main run IDs must be distinct")
    snapshots = {
        "policy": StableFile(policy, "two-green policy"),
        "workflow": StableFile(source_workflow, "source API-36 workflow"),
        "reviewRun": StableFile(review_run, "review run metadata"),
        "reviewJobs": StableFile(review_jobs, "review jobs metadata"),
        "reviewArtifacts": StableFile(review_artifacts, "review artifacts metadata"),
        "reviewAggregate": StableFile(review_aggregate_archive, "review aggregate archive"),
        "reviewP0": StableFile(review_p0_archive, "review P0 archive"),
        "mainRun": StableFile(main_run, "main run metadata"),
        "mainJobs": StableFile(main_jobs, "main jobs metadata"),
        "mainArtifacts": StableFile(main_artifacts, "main artifacts metadata"),
        "mainAggregate": StableFile(main_aggregate_archive, "main aggregate archive"),
        "mainP0": StableFile(main_p0_archive, "main P0 archive"),
    }
    policy_authority = policy_binding(snapshots["policy"])
    workflow_binding = {
        "path": WORKFLOW_PATH,
        "sha256": snapshots["workflow"].sha256,
        "sizeBytes": snapshots["workflow"].size,
    }
    local_tree = git_tree(android_root)
    review, review_common = run_evidence(
        role="review",
        expected_run_id=review_run_id,
        run_snapshot=snapshots["reviewRun"],
        jobs_snapshot=snapshots["reviewJobs"],
        artifacts_snapshot=snapshots["reviewArtifacts"],
        aggregate_archive_snapshot=snapshots["reviewAggregate"],
        p0_archive_snapshot=snapshots["reviewP0"],
        workflow_binding=workflow_binding,
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
        local_tree=local_tree,
    )
    if review_common != main_common:
        raise ValueError("review and main authority or environment compatibility differs")
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
        "sourceTree": local_tree,
        "commonAuthority": review_common,
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
        "sourceTree", "commonAuthority", "reviewRun", "mainRun", "decisionTimeUtc",
        "doesNotAssert", "eligibilitySha256",
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
    _sha40(value["sourceTree"], "two-green source tree")
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
    parser.add_argument("--source-workflow", type=Path, required=True)
    parser.add_argument("--review-run-id", type=int, required=True)
    parser.add_argument("--main-run-id", type=int, required=True)
    for role in ("review", "main"):
        parser.add_argument(f"--{role}-run", type=Path, required=True)
        parser.add_argument(f"--{role}-jobs", type=Path, required=True)
        parser.add_argument(f"--{role}-artifacts", type=Path, required=True)
        parser.add_argument(f"--{role}-aggregate-archive", type=Path, required=True)
        parser.add_argument(f"--{role}-p0-archive", type=Path, required=True)


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
