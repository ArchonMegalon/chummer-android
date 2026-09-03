#!/usr/bin/env python3
"""Materialize or verify the machine-only Android P0 PR authority envelope.

The envelope composes existing hosted build and journey aggregate receipts.  It
does not read a pull-request body and it never grants release or publication
authority.
"""

from __future__ import annotations

import argparse
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


SCRIPT_DIRECTORY = Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

from api36_wizard_gate_contract import (  # noqa: E402
    AGGREGATE_SCHEMA,
    AUTHORITY_CLASS,
    PROOF_SCOPE,
    contract_binding,
    journey_map,
)


CONTRACT = "chummer.android.p0-pr-authority/v1"
OUTPUT_NAME = "ANDROID_P0_PR_AUTHORITY.generated.json"
WORKFLOW_RELATIVE_PATH = ".github/workflows/api36-editing-e2e.yml"
HOSTED_CANDIDATE_RELATIVE_PATH = "hosted-build-candidate.json"
AGGREGATE_RELATIVE_PATH = "receipt.json"
X64_APK_NAME = "chummer-android-x64-debug.apk"
ARM64_APK_NAME = "chummer-android-arm64-debug.apk"
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
ARTIFACT_DIGEST = re.compile(r"^(?:sha256:)?[0-9a-f]{64}$")
EXPECTED_DEPENDENCY_COMMITS = {
    "core-content": "c06f22c185c7b733637fdb76b3cf333f31716781",
    "core-runtime": "60112dccb6a3faad330d32c3c98eef0aa81d97af",
    "hub": "bc199cbe0982833ec2fc9ce625826e612759d67a",
    "media": "415c8163d3d90b1211e4014fef332bdec6d75f73",
    "presentation": "732a33cb8d3c704b8a86e1249eab46508339a105",
    "registry": "af9a7e19c3bf331e96411dfb8f9e7820a98cab29",
    "ui-kit": "d51ecd99cf72098d4adc8db0192bff7bf9fd8e61",
}
DOES_NOT_ASSERT = (
    "full_editing_pass",
    "exhaustive_chummer5_edit_parity",
    "tablet_readiness",
    "arm64_device_execution",
    "physical_device_execution",
    "release_build",
    "release_signing",
    "google_play_upload",
    "public_release_readiness",
    "publication_authority",
)


def _load_hosted_candidate_module() -> Any:
    path = SCRIPT_DIRECTORY / "materialize-api36-hosted-arm64-candidate.py"
    specification = importlib.util.spec_from_file_location(
        "android_p0_hosted_candidate_contract", path
    )
    if specification is None or specification.loader is None:
        raise ValueError("cannot load the hosted ARM64 candidate contract")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


HOSTED = _load_hosted_candidate_module()


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


def stable_regular_bytes(path: Path, label: str) -> bytes:
    if not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path:
        raise ValueError(f"{label} must be an absolute canonical non-symlink path")
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError(f"{label} must be a regular file")
        chunks: list[bytes] = []
        while chunk := os.read(descriptor, 1024 * 1024):
            chunks.append(chunk)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    identity = lambda value: (  # noqa: E731
        value.st_dev,
        value.st_ino,
        value.st_mode,
        value.st_size,
        value.st_mtime_ns,
        value.st_ctime_ns,
    )
    if identity(before) != identity(after):
        raise ValueError(f"{label} changed during capture")
    data = b"".join(chunks)
    if len(data) != before.st_size:
        raise ValueError(f"{label} size changed during capture")
    return data


def strict_json_object(data: bytes, label: str) -> dict[str, object]:
    try:
        value = json.loads(
            data.decode("utf-8", errors="strict"),
            object_pairs_hook=object_without_duplicates,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"non-finite JSON value: {token}")
            ),
        )
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one JSON object")
    return value


def file_binding(data: bytes, path: str) -> dict[str, object]:
    return {
        "path": path,
        "sha256": hashlib.sha256(data).hexdigest(),
        "sizeBytes": len(data),
    }


def _require_sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or SHA256.fullmatch(value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-256")
    return value


def validate_aggregate(
    value: dict[str, object], gate: dict[str, object]
) -> dict[str, object]:
    fields = {
        "schema", "status", "generatedAtUtc", "authorityClass", "proofScope",
        "publicationAuthorized", "gateAuthority", "artifactAuthority",
        "requiredJourneyCount", "requiredJourneys", "journeyCount", "journeys",
    }
    required_map = {
        matrix_journey: specification[0]
        for matrix_journey, specification in journey_map().items()
    }
    required = list(required_map)
    if set(value) != fields:
        raise ValueError("wizard aggregate fields are not exact")
    if (
        value["schema"] != AGGREGATE_SCHEMA
        or value["status"] != "pass"
        or value["authorityClass"] != AUTHORITY_CLASS
        or value["proofScope"] != PROOF_SCOPE
        or value["publicationAuthorized"] is not False
        or value["gateAuthority"] != gate
        or value["requiredJourneyCount"] != len(required)
        or value["requiredJourneys"] != required
        or value["journeyCount"] != len(required)
    ):
        raise ValueError("wizard aggregate authority is not the exact passing gate")
    journeys = value["journeys"]
    if not isinstance(journeys, dict) or set(journeys) != set(required):
        raise ValueError("wizard aggregate journey set or cardinality differs")
    for matrix_journey, driver_journey in required_map.items():
        row = journeys.get(matrix_journey)
        if (
            not isinstance(row, dict)
            or set(row) != {"status", "driverJourney", "receiptSha256"}
            or row["status"] != "pass"
            or row["driverJourney"] != driver_journey
        ):
            raise ValueError(f"wizard aggregate journey differs: {matrix_journey}")
        _require_sha256(row["receiptSha256"], f"{matrix_journey} receipt SHA-256")
    artifact = value["artifactAuthority"]
    if (
        not isinstance(artifact, dict)
        or set(artifact) != {
            "schema", "runId", "artifactId", "artifactDigest", "artifactName",
            "artifactAttempt", "apkSha256",
        }
        or artifact["schema"] != "chummer.android.api36-apk-authority/v1"
        or type(artifact["runId"]) is not int
        or artifact["runId"] <= 0
        or not isinstance(artifact["artifactId"], str)
        or not artifact["artifactId"].isdecimal()
        or int(artifact["artifactId"]) <= 0
        or not isinstance(artifact["artifactAttempt"], int)
        or artifact["artifactAttempt"] <= 0
        or not isinstance(artifact["artifactDigest"], str)
        or ARTIFACT_DIGEST.fullmatch(artifact["artifactDigest"]) is None
        or artifact["artifactName"]
        != f"chummer-android-api36-x64-debug-{artifact['runId']}-{artifact['artifactAttempt']}"
    ):
        raise ValueError("wizard aggregate APK authority is invalid")
    _require_sha256(artifact["apkSha256"], "x64 APK SHA-256")
    return value


def validate_dependency_graph(candidate: dict[str, object]) -> dict[str, object]:
    sources = candidate["sources"]
    assert isinstance(sources, dict)
    for name, commit in EXPECTED_DEPENDENCY_COMMITS.items():
        source = sources.get(name)
        if not isinstance(source, dict) or source.get("commit") != commit:
            raise ValueError(f"hosted dependency commit differs: {name}")
    android = sources.get("android")
    if (
        not isinstance(android, dict)
        or SHA40.fullmatch(str(android.get("commit", ""))) is None
        or SHA40.fullmatch(str(android.get("tree", ""))) is None
    ):
        raise ValueError("hosted Android source head/tree is invalid")
    graph = {
        "mode": candidate["dependencyMode"],
        "sources": {name: sources[name] for name in sorted(sources)},
    }
    return {**graph, "sha256": canonical_sha256(graph)}


def git_identity(android_root: Path) -> dict[str, str]:
    if (
        not android_root.is_absolute()
        or android_root.is_symlink()
        or android_root.resolve(strict=True) != android_root
        or not android_root.is_dir()
    ):
        raise ValueError("Android source root must be absolute, canonical, and non-symlinked")
    environment = dict(os.environ)
    environment["GIT_NO_REPLACE_OBJECTS"] = "1"

    def run(*arguments: str) -> str:
        completed = subprocess.run(
            ["git", "-C", os.fspath(android_root), *arguments],
            check=False,
            capture_output=True,
            text=True,
            timeout=10,
            env=environment,
        )
        if completed.returncode != 0:
            raise ValueError(f"cannot authenticate Android source with git {' '.join(arguments)}")
        return completed.stdout.strip()

    status = run("status", "--porcelain=v1", "--untracked-files=no")
    if status:
        raise ValueError("Android tracked source is dirty while creating PR authority")
    commit = run("rev-parse", "HEAD")
    tree = run("rev-parse", "HEAD^{tree}")
    repository = HOSTED.normalize_repository(run("remote", "get-url", "origin"))
    if (
        SHA40.fullmatch(commit) is None
        or SHA40.fullmatch(tree) is None
        or repository != HOSTED.EXPECTED_SOURCES["android"]
    ):
        raise ValueError("Android source identity is not canonical")
    return {"commit": commit, "tree": tree, "repository": repository}


def create_authority(
    *, android_root: Path, hosted_candidate: Path, aggregate: Path, workflow: Path,
    x64_apk: Path, arm64_apk: Path,
) -> dict[str, object]:
    hosted_bytes = stable_regular_bytes(hosted_candidate, "hosted ARM64 candidate")
    aggregate_bytes = stable_regular_bytes(aggregate, "wizard aggregate")
    workflow_bytes = stable_regular_bytes(workflow, "API-36 workflow")
    x64_apk_bytes = stable_regular_bytes(x64_apk, "x64 APK")
    arm64_apk_bytes = stable_regular_bytes(arm64_apk, "ARM64 APK")

    candidate = HOSTED.validate_observation(
        strict_json_object(hosted_bytes, "hosted ARM64 candidate")
    )
    gate = contract_binding()
    aggregate_value = validate_aggregate(
        strict_json_object(aggregate_bytes, "wizard aggregate"), gate
    )
    dependency_graph = validate_dependency_graph(candidate)
    checked_out_android = git_identity(android_root)
    github_run = candidate["githubRun"]
    sources = candidate["sources"]
    reviewed_workflow = candidate["reviewedInputs"]["workflow"]
    x64_authority = aggregate_value["artifactAuthority"]
    arm64_artifact = candidate["artifact"]
    assert isinstance(github_run, dict)
    assert isinstance(sources, dict)
    assert isinstance(reviewed_workflow, dict)
    assert isinstance(x64_authority, dict)
    assert isinstance(arm64_artifact, dict)
    if (
        github_run["id"] != x64_authority["runId"]
        or github_run["attempt"] != x64_authority["artifactAttempt"]
    ):
        raise ValueError("hosted candidate and wizard aggregate are from different run attempts")
    workflow_sha256 = hashlib.sha256(workflow_bytes).hexdigest()
    if (
        reviewed_workflow != {
            "path": Path(WORKFLOW_RELATIVE_PATH).name,
            "sha256": workflow_sha256,
            "sizeBytes": len(workflow_bytes),
        }
    ):
        raise ValueError("hosted candidate does not bind the exact authority workflow")
    x64_sha256 = hashlib.sha256(x64_apk_bytes).hexdigest()
    arm64_sha256 = hashlib.sha256(arm64_apk_bytes).hexdigest()
    if x64_apk.name != X64_APK_NAME or x64_sha256 != x64_authority["apkSha256"]:
        raise ValueError("x64 APK bytes differ from the aggregate authority")
    if (
        arm64_sha256 != arm64_artifact["sha256"]
        or len(arm64_apk_bytes) != arm64_artifact["sizeBytes"]
        or arm64_apk.name != ARM64_APK_NAME
        or arm64_apk.name != arm64_artifact["fileName"]
    ):
        raise ValueError("ARM64 APK bytes differ from the hosted candidate authority")
    android_source = sources["android"]
    assert isinstance(android_source, dict)
    if (
        github_run["eventSha"] != android_source["commit"]
        or android_source != checked_out_android
    ):
        raise ValueError("hosted event SHA does not equal the checked-out Android head")

    required_map = {
        matrix_journey: specification[0]
        for matrix_journey, specification in journey_map().items()
    }
    journey_rows = [
        {
            "matrixJourney": matrix_journey,
            "driverJourney": required_map[matrix_journey],
            "status": aggregate_value["journeys"][matrix_journey]["status"],
            "receiptSha256": aggregate_value["journeys"][matrix_journey]["receiptSha256"],
        }
        for matrix_journey in required_map
    ]
    unsigned = {
        "schema": CONTRACT,
        "status": "pass",
        "authorityClass": AUTHORITY_CLASS,
        "proofScope": PROOF_SCOPE,
        "publicationAuthorized": False,
        "humanPullRequestBodyAuthoritative": False,
        "githubRun": github_run,
        "androidSource": {
            "checkedOutHead": android_source["commit"],
            "checkedOutTree": android_source["tree"],
            "repository": android_source["repository"],
        },
        "dependencyGraph": dependency_graph,
        "workflow": file_binding(workflow_bytes, WORKFLOW_RELATIVE_PATH),
        "apks": {
            "android-x64": {
                "fileName": x64_apk.name,
                "sha256": x64_sha256,
                "sizeBytes": len(x64_apk_bytes),
                "artifactAuthority": x64_authority,
                "deviceJourneyAggregate": True,
            },
            "android-arm64": {
                "fileName": arm64_apk.name,
                "sha256": arm64_sha256,
                "sizeBytes": len(arm64_apk_bytes),
                "hostedBuildOnly": True,
                "deviceTested": False,
                "releaseAttested": False,
            },
        },
        "requiredJourneyCount": len(journey_rows),
        "journeys": journey_rows,
        "aggregate": {
            **file_binding(aggregate_bytes, AGGREGATE_RELATIVE_PATH),
            "schema": aggregate_value["schema"],
            "status": aggregate_value["status"],
        },
        "inputs": {
            "hostedCandidate": file_binding(
                hosted_bytes, HOSTED_CANDIDATE_RELATIVE_PATH
            ),
            "wizardGate": gate,
        },
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    return {**unsigned, "authoritySha256": canonical_sha256(unsigned)}


def validate_authority(value: dict[str, object]) -> dict[str, object]:
    expected_fields = {
        "schema", "status", "authorityClass", "proofScope",
        "publicationAuthorized", "humanPullRequestBodyAuthoritative", "githubRun",
        "androidSource", "dependencyGraph", "workflow", "apks",
        "requiredJourneyCount", "journeys", "aggregate", "inputs",
        "doesNotAssert", "authoritySha256",
    }
    if set(value) != expected_fields:
        raise ValueError("P0 PR authority fields are not exact")
    if (
        value["schema"] != CONTRACT
        or value["status"] != "pass"
        or value["authorityClass"] != AUTHORITY_CLASS
        or value["proofScope"] != PROOF_SCOPE
        or value["publicationAuthorized"] is not False
        or value["humanPullRequestBodyAuthoritative"] is not False
        or value["doesNotAssert"] != list(DOES_NOT_ASSERT)
    ):
        raise ValueError("P0 PR authority posture is invalid")
    unsigned = {key: member for key, member in value.items() if key != "authoritySha256"}
    if value["authoritySha256"] != canonical_sha256(unsigned):
        raise ValueError("P0 PR authority digest is invalid")
    return value


def write_atomically(path: Path, value: dict[str, object]) -> None:
    if path.name != OUTPUT_NAME:
        raise ValueError(f"output filename must be exactly {OUTPUT_NAME}")
    if not path.is_absolute() or path.is_symlink():
        raise ValueError("authority output must be an absolute non-symlink path")
    parent = path.parent
    if parent.is_symlink() or parent.resolve(strict=True) != parent:
        raise ValueError("authority output parent must be canonical and non-symlinked")
    if path.exists() and not path.is_file():
        raise ValueError("authority output target must be absent or a regular file")
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
    parser.add_argument("--hosted-candidate", type=Path, required=True)
    parser.add_argument("--aggregate", type=Path, required=True)
    parser.add_argument("--workflow", type=Path, required=True)
    parser.add_argument("--x64-apk", type=Path, required=True)
    parser.add_argument("--arm64-apk", type=Path, required=True)


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
    expected = create_authority(
        android_root=args.android_root,
        hosted_candidate=args.hosted_candidate,
        aggregate=args.aggregate,
        workflow=args.workflow,
        x64_apk=args.x64_apk,
        arm64_apk=args.arm64_apk,
    )
    validate_authority(expected)
    if args.command == "materialize":
        write_atomically(args.output, expected)
    else:
        actual_bytes = stable_regular_bytes(args.authority, "P0 PR authority")
        actual = validate_authority(strict_json_object(actual_bytes, "P0 PR authority"))
        if actual != expected or actual_bytes != pretty_json_bytes(expected):
            raise ValueError("P0 PR authority differs from the exact authenticated inputs")
    print(
        "android_p0_pr_authority=pass "
        f"journeys={expected['requiredJourneyCount']} "
        "human_pr_body_authoritative=false publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"android_p0_pr_authority=blocked reason={error}", file=sys.stderr)
        raise SystemExit(2) from error
