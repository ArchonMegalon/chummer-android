#!/usr/bin/env python3
"""Materialize or verify a non-attested hosted ARM64 debug-build observation."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import zipfile


CONTRACT = "chummer.android.api36-arm64-hosted-debug-candidate/v1"
RUNTIME = "android-arm64"
TARGET_FRAMEWORK = "net10.0-android36.0"
CONFIGURATION = "Debug"
APPLICATION_ID = "com.myexternalbrain.chummer"
EXPECTED_SOURCES = {
    "android": "https://github.com/ArchonMegalon/chummer-android.git",
    "core-content": "https://github.com/ArchonMegalon/chummer6-core.git",
    "core-runtime": "https://github.com/ArchonMegalon/chummer6-core.git",
    "hub": "https://github.com/ArchonMegalon/chummer6-hub.git",
    "media": "https://github.com/ArchonMegalon/chummer6-media-factory.git",
    "presentation": "https://github.com/ArchonMegalon/chummer6-ui.git",
    "registry": "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
    "ui-kit": "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
}
DOES_NOT_ASSERT = (
    "api36_device_execution",
    "apk_install",
    "dependency_closure_attestation",
    "google_play_processing",
    "google_play_upload",
    "physical_build_provenance",
    "physical_device_execution",
    "physical_device_observation",
    "physical_journey_pass",
    "public_release_readiness",
    "publication_authority",
    "release_attestation",
    "release_build",
    "release_signing",
    "tablet_readiness",
    "tester_distribution",
    "tester_installation",
)
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")


def canonical_json_bytes(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def canonical_sha256(value: object) -> str:
    return hashlib.sha256(canonical_json_bytes(value)).hexdigest()


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
    before_identity = (
        before.st_dev, before.st_ino, before.st_mode, before.st_size,
        before.st_mtime_ns, before.st_ctime_ns,
    )
    after_identity = (
        after.st_dev, after.st_ino, after.st_mode, after.st_size,
        after.st_mtime_ns, after.st_ctime_ns,
    )
    if before_identity != after_identity:
        raise ValueError(f"{label} changed during capture")
    data = b"".join(chunks)
    if len(data) != before.st_size:
        raise ValueError(f"{label} size changed during capture")
    return data


def run_git(root: Path, *arguments: str) -> str:
    completed = subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=False,
        capture_output=True,
        text=True,
        timeout=10,
    )
    if completed.returncode != 0:
        raise ValueError(f"git {' '.join(arguments)} failed for {root}")
    return completed.stdout.strip()


def normalize_repository(value: str) -> str:
    normalized = value.strip().rstrip("/")
    if normalized.startswith("git@github.com:"):
        normalized = "https://github.com/" + normalized[len("git@github.com:") :]
    if not normalized.endswith(".git"):
        normalized += ".git"
    return normalized


def source_observation(
    *, name: str, repository: str, expected_commit: str, root: Path,
) -> dict[str, object]:
    if name not in EXPECTED_SOURCES or repository != EXPECTED_SOURCES[name]:
        raise ValueError(f"{name} repository is not the canonical source")
    if SHA40.fullmatch(expected_commit) is None:
        raise ValueError(f"{name} expected commit must be a lowercase SHA-40")
    if not root.is_absolute() or root.is_symlink() or root.resolve(strict=True) != root:
        raise ValueError(f"{name} source root must be absolute, canonical, and non-symlinked")
    actual_repository = normalize_repository(run_git(root, "remote", "get-url", "origin"))
    if actual_repository != repository:
        raise ValueError(f"{name} origin is not the canonical repository")
    commit = run_git(root, "rev-parse", "HEAD")
    tree = run_git(root, "rev-parse", "HEAD^{tree}")
    if commit != expected_commit or SHA40.fullmatch(tree) is None:
        raise ValueError(f"{name} source identity does not match its exact pin")
    if run_git(root, "status", "--porcelain=v1", "--untracked-files=no"):
        raise ValueError(f"{name} tracked source is dirty")
    return {"commit": commit, "repository": repository, "tree": tree}


def apk_abis(path: Path) -> list[str]:
    with zipfile.ZipFile(path) as archive:
        abis = sorted(
            {
                name.split("/", 2)[1]
                for name in archive.namelist()
                if name.startswith("lib/") and name.count("/") >= 2
            }
        )
    if abis != ["arm64-v8a"]:
        raise ValueError("hosted candidate APK must contain only arm64-v8a native libraries")
    return abis


def file_binding(path: Path, label: str) -> dict[str, object]:
    data = stable_regular_bytes(path, label)
    return {
        "path": path.name,
        "sha256": hashlib.sha256(data).hexdigest(),
        "sizeBytes": len(data),
    }


def content_receipt_binding(path: Path, apk_sha256: str) -> dict[str, object]:
    data = stable_regular_bytes(path, "canonical content receipt")
    try:
        receipt = json.loads(data)
    except json.JSONDecodeError as error:
        raise ValueError("canonical content receipt is not valid JSON") from error
    if (
        not isinstance(receipt, dict)
        or receipt.get("status") != "pass"
        or receipt.get("schema") != "chummer.android.content-bundle/v1"
        or receipt.get("coreRevision")
        != "3260ac73714d8b001a3599d6776196e394dc6c35"
        or receipt.get("apkVerified") is not True
        or receipt.get("apkSha256") != apk_sha256
        or receipt.get("issues") != []
    ):
        raise ValueError("canonical content receipt does not bind the exact ARM64 APK")
    return {
        "contractName": receipt["schema"],
        "coreRevision": receipt["coreRevision"],
        "sha256": hashlib.sha256(data).hexdigest(),
        "sizeBytes": len(data),
        "status": receipt["status"],
    }


def create_observation(
    *, sources: list[list[str]], runtime: str, application_id: str, apk: Path,
    content_receipt: Path, workflow: Path, build_script: Path,
    event_name: str, event_sha: str, head_sha: str, base_sha: str,
    run_id: str, run_attempt: str,
) -> dict[str, object]:
    if runtime != RUNTIME:
        raise ValueError(f"runtime must be exactly {RUNTIME}")
    if application_id != APPLICATION_ID:
        raise ValueError(f"application ID must be exactly {APPLICATION_ID}")
    rows: dict[str, dict[str, object]] = {}
    for values in sources:
        name, repository, expected_commit, raw_root = values
        if name in rows:
            raise ValueError(f"duplicate source role: {name}")
        rows[name] = source_observation(
            name=name,
            repository=repository,
            expected_commit=expected_commit,
            root=Path(raw_root),
        )
    if set(rows) != set(EXPECTED_SOURCES):
        raise ValueError("hosted candidate source roles are not the exact build graph")
    apk_bytes = stable_regular_bytes(apk, "ARM64 APK")
    if apk.suffix != ".apk":
        raise ValueError("hosted candidate artifact must be an APK")
    if not run_id.isdecimal() or int(run_id) <= 0:
        raise ValueError("run ID must be a positive decimal integer")
    if not run_attempt.isdecimal() or int(run_attempt) <= 0:
        raise ValueError("run attempt must be a positive decimal integer")
    if event_name not in {"pull_request", "push", "workflow_dispatch"}:
        raise ValueError("GitHub event name is not an allowed workflow trigger")
    for label, value in (
        ("event SHA", event_sha), ("head SHA", head_sha), ("base SHA", base_sha),
    ):
        if SHA40.fullmatch(value) is None:
            raise ValueError(f"{label} must be a lowercase SHA-40")
    if rows["android"]["commit"] != event_sha:
        raise ValueError("checked-out Android source must equal the GitHub event SHA")
    apk_sha256 = hashlib.sha256(apk_bytes).hexdigest()
    unsigned = {
        "contractName": CONTRACT,
        "status": "candidate",
        "evidenceClass": "hosted_debug_build_observation",
        "releaseAttested": False,
        "publicationAuthorized": False,
        "physicalDeviceTested": False,
        "releaseEligible": False,
        "githubRun": {
            "attempt": int(run_attempt),
            "baseSha": base_sha,
            "eventName": event_name,
            "eventSha": event_sha,
            "headSha": head_sha,
            "id": int(run_id),
        },
        "dependencyMode": {
            "localCompatibilityTree": True,
            "packageOnly": False,
        },
        "build": {
            "applicationId": application_id,
            "configuration": CONFIGURATION,
            "runtimeIdentifier": runtime,
            "targetFramework": TARGET_FRAMEWORK,
        },
        "sources": {name: rows[name] for name in sorted(rows)},
        "artifact": {
            "apkAbis": apk_abis(apk),
            "fileName": apk.name,
            "sha256": apk_sha256,
            "sizeBytes": len(apk_bytes),
        },
        "canonicalContentReceipt": content_receipt_binding(
            content_receipt, apk_sha256,
        ),
        "reviewedInputs": {
            "buildScript": file_binding(build_script, "ARM64 build script"),
            "workflow": file_binding(workflow, "hosted workflow"),
        },
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    return {**unsigned, "observationSha256": canonical_sha256(unsigned)}


def validate_observation(value: object) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {
        "contractName", "status", "evidenceClass", "releaseAttested",
        "publicationAuthorized", "physicalDeviceTested", "releaseEligible",
        "githubRun", "dependencyMode", "build", "sources", "artifact",
        "canonicalContentReceipt", "reviewedInputs", "doesNotAssert",
        "observationSha256",
    }:
        raise ValueError("hosted candidate observation schema is not exact")
    if (
        value["contractName"] != CONTRACT
        or value["status"] != "candidate"
        or value["evidenceClass"] != "hosted_debug_build_observation"
        or value["releaseAttested"] is not False
        or value["publicationAuthorized"] is not False
        or value["physicalDeviceTested"] is not False
        or value["releaseEligible"] is not False
        or value["doesNotAssert"] != list(DOES_NOT_ASSERT)
    ):
        raise ValueError("hosted candidate observation claims more than it proves")
    if value["build"] != {
        "applicationId": APPLICATION_ID,
        "configuration": CONFIGURATION,
        "runtimeIdentifier": RUNTIME,
        "targetFramework": TARGET_FRAMEWORK,
    }:
        raise ValueError("hosted candidate build identity is not exact")
    github_run = value["githubRun"]
    if (
        not isinstance(github_run, dict)
        or set(github_run)
        != {"attempt", "baseSha", "eventName", "eventSha", "headSha", "id"}
        or not isinstance(github_run["id"], int)
        or github_run["id"] <= 0
        or not isinstance(github_run["attempt"], int)
        or github_run["attempt"] <= 0
        or github_run["eventName"] not in {"pull_request", "push", "workflow_dispatch"}
        or any(
            not isinstance(github_run[field], str)
            or SHA40.fullmatch(github_run[field]) is None
            for field in ("baseSha", "eventSha", "headSha")
        )
    ):
        raise ValueError("hosted candidate GitHub run identity is invalid")
    if value["dependencyMode"] != {
        "localCompatibilityTree": True,
        "packageOnly": False,
    }:
        raise ValueError("hosted candidate dependency mode is not exact")
    sources = value["sources"]
    if not isinstance(sources, dict) or set(sources) != set(EXPECTED_SOURCES):
        raise ValueError("hosted candidate source graph is not exact")
    if sources["android"]["commit"] != github_run["eventSha"]:
        raise ValueError("hosted candidate checkout/event SHA binding is invalid")
    for name, source in sources.items():
        if (
            not isinstance(source, dict)
            or set(source) != {"commit", "repository", "tree"}
            or source["repository"] != EXPECTED_SOURCES[name]
            or not isinstance(source["commit"], str)
            or SHA40.fullmatch(source["commit"]) is None
            or not isinstance(source["tree"], str)
            or SHA40.fullmatch(source["tree"]) is None
        ):
            raise ValueError(f"hosted candidate {name} source binding is invalid")
    artifact = value["artifact"]
    if (
        not isinstance(artifact, dict)
        or set(artifact) != {"apkAbis", "fileName", "sha256", "sizeBytes"}
        or artifact["apkAbis"] != ["arm64-v8a"]
        or not isinstance(artifact["fileName"], str)
        or not artifact["fileName"].endswith(".apk")
        or not isinstance(artifact["sha256"], str)
        or SHA256.fullmatch(artifact["sha256"]) is None
        or not isinstance(artifact["sizeBytes"], int)
        or artifact["sizeBytes"] <= 0
    ):
        raise ValueError("hosted candidate APK binding is invalid")
    content = value["canonicalContentReceipt"]
    if (
        not isinstance(content, dict)
        or set(content) != {"contractName", "coreRevision", "sha256", "sizeBytes", "status"}
        or content["contractName"] != "chummer.android.content-bundle/v1"
        or content["coreRevision"]
        != "3260ac73714d8b001a3599d6776196e394dc6c35"
        or content["status"] != "pass"
        or not isinstance(content["sha256"], str)
        or SHA256.fullmatch(content["sha256"]) is None
        or not isinstance(content["sizeBytes"], int)
        or content["sizeBytes"] <= 0
    ):
        raise ValueError("hosted candidate content receipt binding is invalid")
    reviewed = value["reviewedInputs"]
    if not isinstance(reviewed, dict) or set(reviewed) != {"buildScript", "workflow"}:
        raise ValueError("hosted candidate reviewed inputs are not exact")
    for name, binding in reviewed.items():
        if (
            not isinstance(binding, dict)
            or set(binding) != {"path", "sha256", "sizeBytes"}
            or not isinstance(binding["path"], str)
            or not binding["path"]
            or not isinstance(binding["sha256"], str)
            or SHA256.fullmatch(binding["sha256"]) is None
            or not isinstance(binding["sizeBytes"], int)
            or binding["sizeBytes"] <= 0
        ):
            raise ValueError(f"hosted candidate reviewed {name} binding is invalid")
    digest = value["observationSha256"]
    unsigned = {key: item for key, item in value.items() if key != "observationSha256"}
    if not isinstance(digest, str) or digest != canonical_sha256(unsigned):
        raise ValueError("hosted candidate observation digest is invalid")
    return value


def write_exclusive(path: Path, value: dict[str, object]) -> None:
    if path.name != "hosted-build-candidate.json":
        raise ValueError("output must use the non-attested hosted candidate filename")
    if (
        not path.is_absolute()
        or path.exists()
        or path.is_symlink()
        or not path.parent.is_dir()
        or path.parent.is_symlink()
        or path.parent.resolve(strict=True) != path.parent
    ):
        raise ValueError("output must be an absent absolute canonical path")
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        0o600,
    )
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(canonical_json_bytes(value))
        stream.flush()
        os.fsync(stream.fileno())


def load_json(path: Path) -> object:
    return json.loads(stable_regular_bytes(path, "hosted candidate observation"))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    materialize = subparsers.add_parser("materialize")
    materialize.add_argument("--source", nargs=4, action="append", required=True)
    materialize.add_argument("--runtime", required=True)
    materialize.add_argument("--application-id", required=True)
    materialize.add_argument("--apk", type=Path, required=True)
    materialize.add_argument("--content-receipt", type=Path, required=True)
    materialize.add_argument("--workflow", type=Path, required=True)
    materialize.add_argument("--build-script", type=Path, required=True)
    materialize.add_argument("--event-name", required=True)
    materialize.add_argument("--event-sha", required=True)
    materialize.add_argument("--head-sha", required=True)
    materialize.add_argument("--base-sha", required=True)
    materialize.add_argument("--run-id", required=True)
    materialize.add_argument("--run-attempt", required=True)
    materialize.add_argument("--output", type=Path, required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args(argv)
    if args.command == "materialize":
        observation = create_observation(
            sources=args.source,
            runtime=args.runtime,
            application_id=args.application_id,
            apk=args.apk,
            content_receipt=args.content_receipt,
            workflow=args.workflow,
            build_script=args.build_script,
            event_name=args.event_name,
            event_sha=args.event_sha,
            head_sha=args.head_sha,
            base_sha=args.base_sha,
            run_id=args.run_id,
            run_attempt=args.run_attempt,
        )
        validate_observation(observation)
        write_exclusive(args.output, observation)
        print(
            "api36_hosted_arm64_candidate=pass "
            "release_attested=false publication_authorized=false "
            "physical_device_tested=false"
        )
        return 0
    validate_observation(load_json(args.receipt))
    print(
        "api36_hosted_arm64_candidate_verify=pass "
        "release_attested=false publication_authorized=false "
        "physical_device_tested=false"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        print(f"api36_hosted_arm64_candidate=blocked reason={error}", file=sys.stderr)
        raise SystemExit(2) from error
