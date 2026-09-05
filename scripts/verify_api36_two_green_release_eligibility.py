#!/usr/bin/env python3
"""Verify one exact two-green receipt at an Android release boundary.

The receipt remains eligibility evidence only.  A passing verification permits
release preparation for Internal testing; it never grants signing, Play upload,
or publication authority by itself.  A short-lived detached Ed25519 approval
from the protected release boundary authenticates the exact receipt bytes and
their release/dependency claims.
"""

from __future__ import annotations

import argparse
import base64
import binascii
from datetime import UTC, datetime, timedelta
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
from typing import Any


SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIRECTORY.parent
TWO_GREEN_PATH = SCRIPT_DIRECTORY / "materialize-api36-two-green-eligibility.py"
MAX_RECEIPT_BYTES = 2 * 1024 * 1024
MAX_AUTHORITY_BYTES = 8 * 1024 * 1024
MAX_APPROVAL_BYTES = 64 * 1024
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
PACKAGE_ID = "com.myexternalbrain.chummer"
PACKAGE_AUTHORITY_CONTRACT = "chummer.android.release-package-authority/v2"
SOURCE_GRAPH_CONTRACT = "chummer.android.release-source-graph/v3"
RUNTIME_PACKAGES = (
    "Chummer.Application",
    "Chummer.Engine.Contracts",
    "Chummer.Infrastructure",
    "Chummer.Rulesets.Hosting",
    "Chummer.Rulesets.Sr4",
    "Chummer.Rulesets.Sr5",
    "Chummer.Rulesets.Sr6",
)
OWNER_PACKAGES = {
    "Chummer.Campaign.Contracts": "hub",
    "Chummer.Play.Contracts": "hub",
    "Chummer.Run.Contracts": "hub",
    "Chummer.Hub.Registry.Contracts": "registry",
    "Chummer.Ui.Kit": "ui-kit",
}
SOURCE_GRAPH_REPOSITORIES = {
    "android": "chummer-android",
    "presentation": "chummer6-ui",
    "core-runtime": "chummer6-core",
    "hub": "chummer6-hub",
    "registry": "chummer6-hub-registry",
    "ui-kit": "chummer6-ui-kit",
    "media": "chummer6-media-factory",
}
SOURCE_GRAPH_REQUIRED_REPOSITORIES = {
    *SOURCE_GRAPH_REPOSITORIES.values(),
    "chummer6-design",
}
SOURCE_GRAPH_REPOSITORY_AUTHORITY = {
    "chummer-android": ("app", "https://github.com/ArchonMegalon/chummer-android.git"),
    "chummer6-ui": ("runtime", "https://github.com/ArchonMegalon/chummer6-ui.git"),
    "chummer6-core": ("runtime", "https://github.com/ArchonMegalon/chummer6-core.git"),
    "chummer6-ui-kit": (
        "runtime",
        "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
    ),
    "chummer6-hub": (
        "contracts_and_validation",
        "https://github.com/ArchonMegalon/chummer6-hub.git",
    ),
    "chummer6-hub-registry": (
        "contracts",
        "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
    ),
    "chummer6-media-factory": (
        "contracts",
        "https://github.com/ArchonMegalon/chummer6-media-factory.git",
    ),
    "chummer6-design": (
        "validation",
        "https://github.com/ArchonMegalon/chummer6-design.git",
    ),
}
RELEASE_APPROVAL_CONTRACT = "chummer.android.two-green-release-approval/v1"
RELEASE_APPROVAL_SCOPE = "android_internal_release_preparation"
RELEASE_APPROVER_KEY_ID = "local-release-builder-2026"
RELEASE_APPROVER_ROLE = "android_internal_release_approver"
RELEASE_APPROVER_PUBLIC_KEY = (
    REPO_ROOT / "eng/trusted-release-approvers/local-release-builder-2026.public.pem"
)
RELEASE_APPROVER_PUBLIC_KEY_SHA256 = (
    "ed1fbe95fc7713bfc6d9d0fea21726c1ba3193533fc2d5523e054ad8fb86184c"
)
MAX_APPROVAL_LIFETIME = timedelta(hours=12)
APPROVAL_CLOCK_SKEW = timedelta(minutes=2)
OPENSSL = Path("/usr/bin/openssl")


def _load_two_green_module() -> Any:
    specification = importlib.util.spec_from_file_location(
        "android_release_two_green_contract", TWO_GREEN_PATH
    )
    if specification is None or specification.loader is None:
        raise ValueError("cannot load the two-green eligibility contract")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


TWO_GREEN = _load_two_green_module()


def _stable_bytes(
    path: Path,
    *,
    label: str,
    limit: int,
    owner_only: bool,
) -> bytes:
    if not path.is_absolute() or path.is_symlink():
        raise ValueError(f"{label} must be an absolute canonical non-symlink path")
    try:
        if path.resolve(strict=True) != path:
            raise ValueError(f"{label} must be an absolute canonical non-symlink path")
        descriptor = os.open(
            path,
            os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
        )
    except OSError as error:
        raise ValueError(f"{label} is unavailable") from error
    try:
        before = os.fstat(descriptor)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_size <= 0
            or before.st_size > limit
            or before.st_uid != os.getuid()
            or (owner_only and stat.S_IMODE(before.st_mode) & 0o077)
        ):
            raise ValueError(f"{label} must be one bounded owner file")
        chunks: list[bytes] = []
        total = 0
        while total <= limit:
            chunk = os.read(descriptor, min(1024 * 1024, limit + 1 - total))
            if not chunk:
                break
            chunks.append(chunk)
            total += len(chunk)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    def identity(value: os.stat_result) -> tuple[int, ...]:
        return (
            value.st_dev,
            value.st_ino,
            value.st_mode,
            value.st_uid,
            value.st_size,
            value.st_mtime_ns,
            value.st_ctime_ns,
        )

    raw = b"".join(chunks)
    if identity(before) != identity(after) or len(raw) != before.st_size or len(raw) > limit:
        raise ValueError(f"{label} changed during bounded capture")
    return raw


def _strict_json(raw: bytes, *, label: str) -> dict[str, Any]:
    def reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{label} contains duplicate key {key!r}")
            result[key] = value
        return result

    try:
        value = json.loads(
            raw.decode("utf-8"),
            object_pairs_hook=reject_duplicates,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite value {token}")
            ),
        )
    except (UnicodeDecodeError, json.JSONDecodeError, RecursionError) as error:
        raise ValueError(f"{label} is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must contain one JSON object")
    return value


def _sha40(value: object, label: str) -> str:
    if not isinstance(value, str) or SHA40.fullmatch(value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-40")
    return value


def _sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or SHA256.fullmatch(value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-256")
    return value


def _canonical_json_bytes(value: object) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")


def _utc_timestamp(value: object, label: str) -> datetime:
    if not isinstance(value, str) or not value.endswith("Z"):
        raise ValueError(f"{label} must be a canonical UTC timestamp")
    try:
        parsed = datetime.fromisoformat(value.removesuffix("Z") + "+00:00")
    except ValueError as error:
        raise ValueError(f"{label} must be a canonical UTC timestamp") from error
    if parsed.tzinfo is None or parsed.utcoffset() != timedelta(0):
        raise ValueError(f"{label} must be a canonical UTC timestamp")
    canonical = parsed.astimezone(UTC).isoformat().replace("+00:00", "Z")
    if canonical != value:
        raise ValueError(f"{label} must be a canonical UTC timestamp")
    return parsed.astimezone(UTC)


def _verify_ed25519_signature(
    unsigned: dict[str, Any],
    signature_text: object,
    *,
    label: str,
) -> None:
    public_key_raw = _stable_bytes(
        RELEASE_APPROVER_PUBLIC_KEY,
        label="trusted release approver public key",
        limit=16 * 1024,
        owner_only=False,
    )
    if hashlib.sha256(public_key_raw).hexdigest() != RELEASE_APPROVER_PUBLIC_KEY_SHA256:
        raise ValueError("trusted release approver public key digest differs")
    if not isinstance(signature_text, str) or len(signature_text) > 256:
        raise ValueError(f"{label} signature is invalid")
    try:
        signature = base64.b64decode(signature_text, validate=True)
    except (binascii.Error, ValueError) as error:
        raise ValueError(f"{label} signature is invalid") from error
    if len(signature) != 64:
        raise ValueError(f"{label} signature is invalid")
    if not OPENSSL.is_file():
        raise ValueError("trusted OpenSSL verifier is unavailable")
    with tempfile.TemporaryDirectory(prefix="chummer-android-release-signature-") as directory:
        root = Path(directory)
        payload_path = root / "payload.json"
        signature_path = root / "signature.bin"
        payload_path.write_bytes(_canonical_json_bytes(unsigned))
        signature_path.write_bytes(signature)
        completed = subprocess.run(
            [
                os.fspath(OPENSSL), "pkeyutl", "-verify", "-pubin",
                "-inkey", os.fspath(RELEASE_APPROVER_PUBLIC_KEY), "-rawin",
                "-in", os.fspath(payload_path), "-sigfile", os.fspath(signature_path),
            ],
            check=False,
            capture_output=True,
            timeout=20,
            env={"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C"},
        )
    if completed.returncode != 0:
        raise ValueError(f"{label} signature is invalid")


def release_approval_unsigned(
    receipt_raw: bytes,
    receipt: dict[str, Any],
    *,
    generated_at_utc: str,
    expires_at_utc: str,
    challenge_nonce: str,
    provenance_validator_sha256: str,
    provenance_replay_sha256: str,
) -> dict[str, Any]:
    common = receipt.get("commonAuthority")
    release = receipt.get("releaseIdentity")
    if not isinstance(common, dict) or not isinstance(release, dict):
        raise ValueError("two-green receipt cannot be approved without exact authority")
    dependency = common.get("dependencyGraph")
    environment = common.get("environmentPolicy")
    if not isinstance(dependency, dict) or not isinstance(environment, dict):
        raise ValueError("two-green receipt cannot be approved without exact dependency authority")
    return {
        "contractName": RELEASE_APPROVAL_CONTRACT,
        "algorithm": "ed25519",
        "keyId": RELEASE_APPROVER_KEY_ID,
        "role": RELEASE_APPROVER_ROLE,
        "approvalScope": RELEASE_APPROVAL_SCOPE,
        "generatedAtUtc": generated_at_utc,
        "expiresAtUtc": expires_at_utc,
        "challengeNonce": _sha256(challenge_nonce, "release approval challenge nonce"),
        "provenanceValidatorSha256": _sha256(
            provenance_validator_sha256,
            "two-green provenance validator digest",
        ),
        "provenanceReplaySha256": _sha256(
            provenance_replay_sha256,
            "two-green provenance replay digest",
        ),
        "receiptSha256": hashlib.sha256(receipt_raw).hexdigest(),
        "eligibilitySha256": _sha256(
            receipt.get("eligibilitySha256"), "two-green eligibility digest"
        ),
        "sourceCommit": _sha40(receipt.get("sourceCommit"), "two-green source commit"),
        "sourceTree": _sha40(receipt.get("sourceTree"), "two-green source tree"),
        "versionName": release.get("versionName"),
        "versionCode": release.get("versionCode"),
        "dependencyGraphSha256": _sha256(
            dependency.get("sha256"), "two-green dependency graph digest"
        ),
        "environmentPolicySha256": _sha256(
            environment.get("sha256"), "two-green environment policy digest"
        ),
        "signingAuthorized": False,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
    }


def _verify_release_approval(
    approval_path: Path,
    *,
    receipt_raw: bytes,
    receipt: dict[str, Any],
    now: datetime | None = None,
) -> dict[str, Any]:
    approval_raw = _stable_bytes(
        approval_path,
        label="two-green protected release approval",
        limit=MAX_APPROVAL_BYTES,
        owner_only=True,
    )
    approval = _strict_json(approval_raw, label="two-green protected release approval")
    fields = {
        "contractName", "algorithm", "keyId", "role", "approvalScope",
        "generatedAtUtc", "expiresAtUtc", "challengeNonce", "receiptSha256",
        "provenanceValidatorSha256", "provenanceReplaySha256",
        "eligibilitySha256", "sourceCommit", "sourceTree", "versionName",
        "versionCode", "dependencyGraphSha256", "environmentPolicySha256",
        "signingAuthorized", "publicationAuthorized", "googlePlayUploadAuthorized",
        "signatureBase64",
    }
    if set(approval) != fields:
        raise ValueError("two-green protected release approval fields are not exact")
    if (
        approval.get("contractName") != RELEASE_APPROVAL_CONTRACT
        or approval.get("algorithm") != "ed25519"
        or approval.get("keyId") != RELEASE_APPROVER_KEY_ID
        or approval.get("role") != RELEASE_APPROVER_ROLE
        or approval.get("approvalScope") != RELEASE_APPROVAL_SCOPE
        or approval.get("signingAuthorized") is not False
        or approval.get("publicationAuthorized") is not False
        or approval.get("googlePlayUploadAuthorized") is not False
    ):
        raise ValueError("two-green protected release approval posture is invalid")
    generated = _utc_timestamp(approval.get("generatedAtUtc"), "release approval generatedAtUtc")
    expires = _utc_timestamp(approval.get("expiresAtUtc"), "release approval expiresAtUtc")
    if now is not None and (now.tzinfo is None or now.utcoffset() is None):
        raise ValueError("release approval effective time must be timezone-aware")
    current = (now or datetime.now(UTC)).astimezone(UTC)
    if (
        generated > current + APPROVAL_CLOCK_SKEW
        or expires <= generated
        or expires - generated > MAX_APPROVAL_LIFETIME
        or current >= expires
    ):
        raise ValueError("two-green protected release approval is stale or outside its lifetime")
    validator_raw = _stable_bytes(
        TWO_GREEN_PATH,
        label="two-green deep provenance validator",
        limit=MAX_AUTHORITY_BYTES,
        owner_only=False,
    )
    expected_validator_sha256 = hashlib.sha256(validator_raw).hexdigest()
    if approval.get("provenanceValidatorSha256") != expected_validator_sha256:
        raise ValueError("two-green protected approval used a different provenance validator")
    unsigned = release_approval_unsigned(
        receipt_raw,
        receipt,
        generated_at_utc=approval["generatedAtUtc"],
        expires_at_utc=approval["expiresAtUtc"],
        challenge_nonce=approval["challengeNonce"],
        provenance_validator_sha256=approval["provenanceValidatorSha256"],
        provenance_replay_sha256=approval["provenanceReplaySha256"],
    )
    if any(approval.get(key) != value for key, value in unsigned.items()):
        raise ValueError("two-green protected release approval claims differ from the receipt")
    signature_text = approval.get("signatureBase64")
    _verify_ed25519_signature(
        unsigned,
        signature_text,
        label="two-green protected release approval",
    )
    return {
        "contractName": RELEASE_APPROVAL_CONTRACT,
        "keyId": RELEASE_APPROVER_KEY_ID,
        "role": RELEASE_APPROVER_ROLE,
        "approvalScope": RELEASE_APPROVAL_SCOPE,
        "approvalSha256": hashlib.sha256(approval_raw).hexdigest(),
        "receiptSha256": unsigned["receiptSha256"],
        "provenanceValidatorSha256": unsigned["provenanceValidatorSha256"],
        "provenanceReplaySha256": unsigned["provenanceReplaySha256"],
        "generatedAtUtc": unsigned["generatedAtUtc"],
        "expiresAtUtc": unsigned["expiresAtUtc"],
    }


def _positive_integer(value: object, label: str) -> int:
    if type(value) is not int or value <= 0:
        raise ValueError(f"{label} must be a positive integer")
    return value


def _git_identity(android_root: Path) -> tuple[str, str]:
    if (
        not android_root.is_absolute()
        or android_root.is_symlink()
        or not android_root.is_dir()
        or android_root.resolve(strict=True) != android_root
    ):
        raise ValueError("Android release root must be one canonical checkout")
    environment = {
        "PATH": "/usr/bin:/bin",
        "LANG": "C",
        "LC_ALL": "C",
        "GIT_NO_REPLACE_OBJECTS": "1",
    }

    def git(*arguments: str) -> str:
        try:
            completed = subprocess.run(
                ["/usr/bin/git", "-C", os.fspath(android_root), *arguments],
                check=False,
                capture_output=True,
                text=True,
                timeout=20,
                env=environment,
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            raise ValueError("cannot authenticate Android release source") from error
        if completed.returncode != 0:
            raise ValueError("cannot authenticate Android release source")
        return completed.stdout.strip()

    if git("rev-parse", "--show-toplevel") != os.fspath(android_root):
        raise ValueError("Android release root is not the checkout top level")
    if git("status", "--porcelain=v1", "--untracked-files=all"):
        raise ValueError("Android release checkout is not clean")
    tracked = git("ls-files", "-v", "-z")
    if any(
        not entry.startswith("H ")
        for entry in tracked.split("\0")
        if entry
    ):
        raise ValueError("Android release checkout contains hidden index flags")
    commit = _sha40(git("rev-parse", "HEAD"), "Android release commit")
    tree = _sha40(git("rev-parse", "HEAD^{tree}"), "Android release tree")
    return commit, tree


def _validate_run_evidence(
    evidence: object,
    *,
    role: str,
    source_commit: str,
) -> dict[str, Any]:
    fields = {
        "run",
        "jobs",
        "actionsMetadata",
        "artifacts",
        "p0AuthoritySha256",
        "p0BaseSha",
        "p0EventSha",
        "aggregateStatus",
    }
    if role == "review":
        fields.add("aggregateCheckRun")
    if not isinstance(evidence, dict) or set(evidence) != fields:
        raise ValueError(f"two-green {role} run evidence fields are not exact")
    run = evidence.get("run")
    run_fields = {
        "id", "attempt", "event", "ref", "headBranch", "headSha", "workflowId",
        "checkSuiteId", "reportedPullRequests", "createdAtUtc", "startedAtUtc",
        "completedAtUtc", "status", "conclusion",
    }
    if not isinstance(run, dict) or set(run) != run_fields:
        raise ValueError(f"two-green {role} run identity is not exact")
    _positive_integer(run.get("id"), f"two-green {role} run ID")
    _positive_integer(run.get("attempt"), f"two-green {role} run attempt")
    head = _sha40(run.get("headSha"), f"two-green {role} run head")
    expected_event = "push" if role == "main" else "pull_request"
    if (
        run.get("status") != "completed"
        or run.get("conclusion") != "success"
        or run.get("event") != expected_event
        or (role == "main" and run.get("ref") != TWO_GREEN.MAIN_REF)
        or (role == "main" and run.get("headBranch") != "main")
        or evidence.get("aggregateStatus") != "pass"
    ):
        raise ValueError(f"two-green {role} run or aggregate is not successful")
    if role == "main" and (
        head != source_commit
        or evidence.get("p0EventSha") != source_commit
        or evidence.get("p0BaseSha") != source_commit
    ):
        raise ValueError("two-green main run does not bind the exact release commit")
    jobs = evidence.get("jobs")
    if not isinstance(jobs, dict) or set(jobs) != set(TWO_GREEN.REQUIRED_JOB_NAMES):
        raise ValueError(f"two-green {role} governed job inventory is not exact")
    for name, row in jobs.items():
        if (
            not isinstance(row, dict)
            or set(row)
            != {
                "id", "status", "conclusion", "startedAtUtc", "completedAtUtc",
                "detailsUrl", "checkRunUrl",
            }
            or row.get("status") != "completed"
            or row.get("conclusion") != "success"
        ):
            raise ValueError(f"two-green {role} governed job is not successful: {name}")
    aggregate = jobs[TWO_GREEN.REQUIRED_JOB_NAMES[-1]]
    if aggregate["status"] != "completed" or aggregate["conclusion"] != "success":
        raise ValueError(f"two-green {role} aggregate job is not successful")
    _sha256(evidence.get("p0AuthoritySha256"), f"two-green {role} P0 authority")
    return run


def _validate_dependency_authority(
    authority: object,
    *,
    source_tree: str,
) -> dict[str, dict[str, Any]]:
    if not isinstance(authority, dict) or set(authority) != {"mode", "sources", "sha256"}:
        raise ValueError("two-green dependency graph fields are not exact")
    if authority.get("mode") != {"localCompatibilityTree": True, "packageOnly": False}:
        raise ValueError("two-green dependency graph mode is not exact")
    sources = authority.get("sources")
    expected_repositories = TWO_GREEN.P0.HOSTED.EXPECTED_SOURCES
    if not isinstance(sources, dict) or set(sources) != set(expected_repositories):
        raise ValueError("two-green dependency source inventory is not exact")
    for name, repository in expected_repositories.items():
        row = sources.get(name)
        expected_fields = {"repository", "tree"} if name == "android" else {
            "commit", "repository", "tree"
        }
        if (
            not isinstance(row, dict)
            or set(row) != expected_fields
            or row.get("repository") != repository
        ):
            raise ValueError(f"two-green dependency source is not exact: {name}")
        _sha40(row.get("tree"), f"two-green {name} dependency tree")
        if name == "android":
            if row["tree"] != source_tree:
                raise ValueError("two-green Android dependency tree differs")
        elif row.get("commit") != TWO_GREEN.P0.EXPECTED_DEPENDENCY_COMMITS[name]:
            raise ValueError(f"two-green dependency commit differs: {name}")
    unsigned = {"mode": authority["mode"], "sources": sources}
    if authority.get("sha256") != TWO_GREEN.canonical_sha256(unsigned):
        raise ValueError("two-green dependency graph digest is invalid")
    return sources


def _validate_package_authority(
    path: Path,
    *,
    sources: dict[str, dict[str, Any]],
) -> None:
    value = _strict_json(
        _stable_bytes(
            path,
            label="release package authority",
            limit=MAX_AUTHORITY_BYTES,
            owner_only=True,
        ),
        label="release package authority",
    )
    if set(value) != {"contractName", "packagePins", "ownerPackagePins", "dependencyClosure"}:
        raise ValueError("release package authority fields are not exact")
    if value.get("contractName") != PACKAGE_AUTHORITY_CONTRACT:
        raise ValueError("release package authority contract differs")
    runtime = value.get("packagePins")
    if (
        not isinstance(runtime, list)
        or [row.get("package_id") if isinstance(row, dict) else None for row in runtime]
        != list(RUNTIME_PACKAGES)
        or any(row.get("commit") != sources["core-runtime"]["commit"] for row in runtime)
    ):
        raise ValueError("release package authority differs from two-green Core dependency")
    owner = value.get("ownerPackagePins")
    if (
        not isinstance(owner, list)
        or len(owner) != len(OWNER_PACKAGES)
        or {
            row.get("package_id") if isinstance(row, dict) else None for row in owner
        }
        != set(OWNER_PACKAGES)
    ):
        raise ValueError("release owner package authority inventory differs")
    for row in owner:
        assert isinstance(row, dict)
        source_name = OWNER_PACKAGES[row["package_id"]]
        if (
            row.get("source_commit") != sources[source_name]["commit"]
            or row.get("source_tree") != sources[source_name]["tree"]
        ):
            raise ValueError(
                f"release owner package authority differs from two-green dependency: {source_name}"
            )


def _validate_current_dependency_pins(sources: dict[str, dict[str, Any]]) -> None:
    manifest_path = REPO_ROOT / "eng/internal-phone-beta-package-authority.json"
    manifest = _strict_json(manifest_path.read_bytes(), label="internal package authority")
    source_graph = manifest.get("sourceGraph")
    presentation = manifest.get("presentationSource")
    if not isinstance(source_graph, dict) or not isinstance(presentation, dict):
        raise ValueError("internal package authority dependency pins are missing")
    expected = {
        "core-content": source_graph.get("corePackageRecipeCommit"),
        "core-runtime": source_graph.get("coreRuntimeSourceCommit"),
        "hub": source_graph.get("hubProducerCommit"),
        "registry": source_graph.get("registryCommit"),
        "ui-kit": source_graph.get("uiKitCommit"),
        "presentation": presentation.get("commit"),
        "media": TWO_GREEN.P0.EXPECTED_DEPENDENCY_COMMITS["media"],
    }
    for name, commit in expected.items():
        if commit != sources[name].get("commit"):
            raise ValueError(f"current release dependency pin differs from two-green graph: {name}")


def _validate_source_graph(
    path: Path,
    *,
    source_commit: str,
    source_tree: str,
    version_name: str,
    version_code: int,
    sources: dict[str, dict[str, Any]],
) -> None:
    value = _strict_json(
        _stable_bytes(
            path,
            label="release source graph",
            limit=MAX_AUTHORITY_BYTES,
            owner_only=True,
        ),
        label="release source graph",
    )
    if value.get("contractName") != SOURCE_GRAPH_CONTRACT:
        raise ValueError("release source graph contract differs")
    if value.get("publicationAuthorized") is not False:
        raise ValueError("release source graph improperly authorizes publication")
    identity = value.get("releaseIdentity")
    if identity != {
        "packageId": PACKAGE_ID,
        "versionName": version_name,
        "versionCode": version_code,
        "intentAuthority": "explicit_build_input",
        "minimumExclusiveVersionCode": 10,
    }:
        raise ValueError("release source graph version differs from two-green eligibility")
    repositories = value.get("repositories")
    if not isinstance(repositories, list):
        raise ValueError("release source graph repository inventory is missing")
    by_name: dict[str, dict[str, Any]] = {}
    repository_fields = {
        "name", "role", "commit", "tree", "tree_sha256", "repository"
    }
    for row in repositories:
        if not isinstance(row, dict) or set(row) != repository_fields:
            raise ValueError("release source graph repository fields are not exact")
        name = row.get("name")
        if not isinstance(name, str) or name in by_name:
            raise ValueError("release source graph repository inventory is ambiguous")
        expected = SOURCE_GRAPH_REPOSITORY_AUTHORITY.get(name)
        if expected is None or (row.get("role"), row.get("repository")) != expected:
            raise ValueError("release source graph repository role or origin differs")
        _sha40(row.get("commit"), f"release source graph {name} commit")
        _sha40(row.get("tree"), f"release source graph {name} tree")
        _sha256(row.get("tree_sha256"), f"release source graph {name} tree digest")
        by_name[name] = row
    if len(by_name) != len(repositories):
        raise ValueError("release source graph repository inventory is ambiguous")
    if set(by_name) != SOURCE_GRAPH_REQUIRED_REPOSITORIES:
        raise ValueError("release source graph repository inventory is not exact")
    android = by_name.get("chummer-android")
    if (
        not isinstance(android, dict)
        or android.get("commit") != source_commit
        or android.get("tree") != source_tree
    ):
        raise ValueError("release source graph Android commit/tree differs from two-green eligibility")
    for source_name in ("presentation", "core-runtime", "media"):
        row = by_name.get(SOURCE_GRAPH_REPOSITORIES[source_name])
        if (
            not isinstance(row, dict)
            or row.get("commit") != sources[source_name]["commit"]
            or row.get("tree") != sources[source_name]["tree"]
        ):
            raise ValueError(f"release source graph differs from two-green dependency: {source_name}")
    owner = value.get("ownerPackagePins")
    if not isinstance(owner, list):
        raise ValueError("release source graph owner package pins are missing")
    owner_by_id = {
        row.get("package_id"): row
        for row in owner
        if isinstance(row, dict) and isinstance(row.get("package_id"), str)
    }
    if len(owner_by_id) != len(owner) or set(owner_by_id) != set(OWNER_PACKAGES):
        raise ValueError("release source graph owner package inventory differs")
    for package_id, source_name in OWNER_PACKAGES.items():
        row = owner_by_id[package_id]
        if (
            row.get("source_commit") != sources[source_name]["commit"]
            or row.get("source_tree") != sources[source_name]["tree"]
        ):
            raise ValueError(f"release source graph differs from two-green package source: {source_name}")


def verify_release_eligibility(
    receipt_path: Path,
    approval_path: Path,
    *,
    android_root: Path,
    expected_version_name: str,
    expected_version_code: int | str,
    package_authority_path: Path | None = None,
    source_graph_path: Path | None = None,
    approval_effective_time: datetime | None = None,
) -> dict[str, Any]:
    raw = _stable_bytes(
        receipt_path,
        label="two-green eligibility receipt",
        limit=MAX_RECEIPT_BYTES,
        owner_only=True,
    )
    receipt = _strict_json(raw, label="two-green eligibility receipt")
    TWO_GREEN.validate_authority(receipt)
    approval = _verify_release_approval(
        approval_path,
        receipt_raw=raw,
        receipt=receipt,
        now=approval_effective_time,
    )
    expected_digest = approval["receiptSha256"]
    expected_policy = TWO_GREEN.policy_binding(
        TWO_GREEN.StableFile(TWO_GREEN.POLICY_PATH, "current two-green policy")
    )
    if receipt.get("policyAuthority") != expected_policy:
        raise ValueError("two-green eligibility policy differs from the current release gate")
    common = receipt.get("commonAuthority")
    assert isinstance(common, dict)
    common_fields = {
        "androidTree", "authorityClass", "proofScope", "dependencyGraph", "workflow",
        "wizardGate", "aggregateSchema", "requiredJourneys", "environmentPolicy",
        "buildEnvironmentCompatibilitySha256", "journeyEnvironmentCompatibilitySha256",
        "environmentCompatibilityStatus",
    }
    if set(common) != common_fields:
        raise ValueError("two-green common authority fields are not exact")
    expected_environment = TWO_GREEN.ENVIRONMENT.policy_binding(
        TWO_GREEN.StableFile(
            TWO_GREEN.ENVIRONMENT_POLICY_PATH,
            "current API-36 environment policy",
        )
    )
    if (
        common.get("environmentPolicy") != expected_environment
        or common.get("environmentCompatibilityStatus") != "pass"
    ):
        raise ValueError("two-green environment compatibility did not pass the current policy")
    _sha256(
        common.get("buildEnvironmentCompatibilitySha256"),
        "two-green build environment compatibility",
    )
    _sha256(
        common.get("journeyEnvironmentCompatibilitySha256"),
        "two-green journey environment compatibility",
    )
    if (
        common.get("authorityClass") != TWO_GREEN.AUTHORITY_CLASS
        or common.get("proofScope") != TWO_GREEN.PROOF_SCOPE
        or common.get("aggregateSchema") != TWO_GREEN.AGGREGATE_SCHEMA
        or common.get("requiredJourneys") != list(TWO_GREEN.journey_map())
    ):
        raise ValueError("two-green aggregate authority differs from the governed gate")
    workflow_path = android_root / TWO_GREEN.WORKFLOW_PATH
    workflow_raw = workflow_path.read_bytes()
    if common.get("workflow") != {
        "path": TWO_GREEN.WORKFLOW_PATH,
        "sha256": hashlib.sha256(workflow_raw).hexdigest(),
        "sizeBytes": len(workflow_raw),
    }:
        raise ValueError("two-green workflow authority differs from the release source")
    if common.get("wizardGate") != TWO_GREEN.contract_binding(TWO_GREEN.DEFAULT_WIZARD_GATE):
        raise ValueError("two-green wizard gate differs from the release source")

    source_commit = _sha40(receipt.get("sourceCommit"), "two-green source commit")
    source_tree = _sha40(receipt.get("sourceTree"), "two-green source tree")
    local_commit, local_tree = _git_identity(android_root)
    if (source_commit, source_tree) != (local_commit, local_tree):
        raise ValueError("two-green Android commit/tree differs from the release checkout")
    try:
        version_code = int(str(expected_version_code))
    except ValueError as error:
        raise ValueError("expected release version code is not canonical") from error
    if str(version_code) != str(expected_version_code) or version_code <= 10:
        raise ValueError("expected release version code is not canonical")
    release_identity = receipt.get("releaseIdentity")
    if release_identity != {
        "packageId": PACKAGE_ID,
        "versionName": expected_version_name,
        "versionCode": version_code,
        "intentAuthority": "android_project_at_exact_main_tree",
    }:
        raise ValueError("two-green release version differs from the requested release")
    project = TWO_GREEN.StableFile(
        android_root / TWO_GREEN.PROJECT_PATH,
        "Android release project",
    )
    if TWO_GREEN.release_identity(project) != release_identity:
        raise ValueError("two-green release version differs from the Android project")
    project.recheck()

    review_run = _validate_run_evidence(
        receipt.get("reviewRun"), role="review", source_commit=source_commit
    )
    main_run = _validate_run_evidence(
        receipt.get("mainRun"), role="main", source_commit=source_commit
    )
    if receipt.get("decisionTimeUtc") != main_run["completedAtUtc"]:
        raise ValueError("two-green decision time differs from the main run completion")
    if review_run["id"] >= main_run["id"]:
        raise ValueError("two-green main run is not later than the reviewed run")

    sources = _validate_dependency_authority(
        common.get("dependencyGraph"), source_tree=source_tree
    )
    _validate_current_dependency_pins(sources)
    if package_authority_path is not None:
        _validate_package_authority(package_authority_path, sources=sources)
    if source_graph_path is not None:
        _validate_source_graph(
            source_graph_path,
            source_commit=source_commit,
            source_tree=source_tree,
            version_name=expected_version_name,
            version_code=version_code,
            sources=sources,
        )
    return {
        "contractName": TWO_GREEN.CONTRACT,
        "receiptSha256": expected_digest,
        "protectedApproval": approval,
        "eligibilitySha256": receipt["eligibilitySha256"],
        "sourceCommit": source_commit,
        "sourceTree": source_tree,
        "versionName": expected_version_name,
        "versionCode": version_code,
        "dependencyGraphSha256": common["dependencyGraph"]["sha256"],
        "environmentPolicySha256": common["environmentPolicy"]["sha256"],
        "buildEnvironmentCompatibilitySha256": common[
            "buildEnvironmentCompatibilitySha256"
        ],
        "journeyEnvironmentCompatibilitySha256": common[
            "journeyEnvironmentCompatibilitySha256"
        ],
        "mainRunId": main_run["id"],
        "mainRunAttempt": main_run["attempt"],
        "mainRunConclusion": "success",
        "mainAggregateConclusion": "success",
        "environmentCompatibilityStatus": "pass",
        "eligible": True,
        "internalTestingEligible": True,
        "publicationAuthorized": False,
        "googlePlayUploadAuthorized": False,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--approval", required=True, type=Path)
    parser.add_argument("--android-root", required=True, type=Path)
    parser.add_argument("--expected-version-name", required=True)
    parser.add_argument("--expected-version-code", required=True)
    parser.add_argument("--package-authority", type=Path)
    parser.add_argument("--source-graph", type=Path)
    arguments = parser.parse_args(argv)
    try:
        binding = verify_release_eligibility(
            arguments.receipt,
            arguments.approval,
            android_root=arguments.android_root,
            expected_version_name=arguments.expected_version_name,
            expected_version_code=arguments.expected_version_code,
            package_authority_path=arguments.package_authority,
            source_graph_path=arguments.source_graph,
        )
        result = {
            "status": "pass",
            "releasePreparationEligible": True,
            "signingAuthorizedByReceipt": False,
            "publicationAuthorized": False,
            "googlePlayUploadAuthorized": False,
            "eligibility": binding,
            "failures": [],
        }
    except (OSError, ValueError) as error:
        result = {
            "status": "fail",
            "releasePreparationEligible": False,
            "signingAuthorizedByReceipt": False,
            "publicationAuthorized": False,
            "googlePlayUploadAuthorized": False,
            "failures": [str(error)],
        }
    print(json.dumps(result, sort_keys=True))
    return 0 if result["status"] == "pass" else 2


if __name__ == "__main__":
    raise SystemExit(main())
