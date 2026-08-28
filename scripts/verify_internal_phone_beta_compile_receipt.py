#!/usr/bin/env python3
"""Verify durable evidence behind an internal phone-beta compile receipt."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
from pathlib import Path


CONTRACT = "chummer.android.internal-phone-beta-native-compile/v1"
ROOT = Path(__file__).resolve().parents[1]
EXPECTED_EVIDENCE = (
    "authority-intake.log",
    "authority-binding.json",
    "restore.log",
    "owned-compile-graph.log",
    "compile-graph.json",
    "build.log",
    "command-journal.jsonl",
)
DIGEST_BINDINGS = {
    "authority-binding.json": "authorityBindingSha256",
    "restore.log": "restoreOutputSha256",
    "compile-graph.json": "compileGraphSha256",
    "build.log": "buildOutputSha256",
    "command-journal.jsonl": "journalSha256",
}
AUTHORITY_CLASS = "internal_phone_beta_only"
PROOF_SCOPE = "Native.CompileCheck_dependency_only"
BLOCKED_DOES_NOT_ASSERT = (
    "full_maui_build",
    "core_data_lang_content",
    "api36_device_execution",
    "google_play_upload",
    "public_release_readiness",
)
PASS_ONLY_FIELDS = (
    "schema",
    "dependencyMode",
    "serializedBuild",
    "sdkVersion",
    "androidCommit",
    "androidTree",
    "presentationCommit",
    "presentationTree",
    "authorityReceiptSha256",
    "authorityJournalSha256",
    "authorityBindingSha256",
    "executionBounds",
    "compileGraphSha256",
    "restoreOutputSha256",
    "buildOutputSha256",
    "lockSha256",
    "assetsSha256",
    "artifact",
    "fullMauiBuild",
    "coreDataLangContentVerified",
    "laterDeviceGateRequirements",
    "androidWorktreeClean",
    "lockSizeBytes",
    "packageAuthoritySha256",
    "desktopRuntimeLockSha256",
    "producerSdkVersion",
    "packageOnly",
    "restoreLockedMode",
    "sourceCheckoutsPresent",
    "siblingsAllowed",
    "phaseResults",
    "evidenceBindings",
)
BLOCKED_ALLOWED_KEYS = frozenset({
    "contractName", "status", "authorityClass", "publicationAuthorized",
    "retryPerformed", "failureStage", "journalSha256", "journalSizeBytes",
    "evidenceDirectory", "evidence", "proofScope", "doesNotAssert",
})
PASS_ALLOWED_KEYS = frozenset({
    "contractName", "schema", "status", "authorityClass",
    "publicationAuthorized", "dependencyMode", "packageOnly",
    "restoreLockedMode", "sourceCheckoutsPresent", "siblingsAllowed",
    "serializedBuild", "sdkVersion", "producerSdkVersion", "androidCommit",
    "androidTree", "androidWorktreeClean", "presentationCommit",
    "presentationTree", "authorityReceiptSha256", "authorityJournalSha256",
    "packageAuthoritySha256", "desktopRuntimeLockSha256",
    "authorityBindingSha256", "executionBounds", "journalSha256",
    "journalSizeBytes", "evidenceDirectory", "evidence", "evidenceBindings",
    "compileGraphSha256", "restoreOutputSha256", "buildOutputSha256",
    "lockSha256", "lockSizeBytes", "assetsSha256", "artifact", "phaseResults",
    "proofScope", "fullMauiBuild", "coreDataLangContentVerified",
    "laterDeviceGateRequirements", "doesNotAssert",
})
PASS_DOES_NOT_ASSERT = (
    "full_maui_build",
    "core_data_lang_content",
    "api36_device_execution",
    "google_play_upload",
    "public_release_readiness",
    "publication_authority",
    "tablet_readiness",
)
LATER_DEVICE_REQUIREMENTS = (
    "full_maui_build",
    "core_data_lang_content",
    "apk_install",
    "physical_api36_execution",
)
PRESENTATION_COMMIT = "a8a317aff534dc5fd47f2db1bc39466799021990"
PRESENTATION_TREE = "f8214243280030de5d134351f39ea4b23afbe394"
AUTHORITY_RECEIPT_SHA256 = "aaf2c755ef7233f2b21bc257e306ea5de60ac42125c3c8b47501aa05a3b949dd"
AUTHORITY_JOURNAL_SHA256 = "4b41a6c2afded5acf83b07a59ac73605faf5037948a0dd0fbed772440ff54bec"
PACKAGE_AUTHORITY_SHA256 = "4dfc8bff234ced999792797b7ac4e5f5dd5d371c1c261bc56b2f6adcfa382c4b"
DESKTOP_RUNTIME_LOCK_SHA256 = "202a29a35b4768c3306349ee40a34d8f23ada97c0b0ef11e104763b5ff9cc60e"
AUTHORITY_BINDING_SHA256 = "7f7ab5b827f69eee79addcc5cd47204d9cfa7387acd06c6894329088b6bae839"
ANDROID_LOCK_SHA256 = "64454d5420e2a5430a046d392c6eea2ca41d9105c1667f2b8a66e1f61064cccc"
ANDROID_LOCK_SIZE = 17968
PRODUCER_SDK_VERSION = "10.0.103"
CONSUMER_SDK_VERSION = "10.0.111"
EXPECTED_PHASE_RESULTS = {
    "authorityIntake": {"status": "pass"},
    "lockedRestore": {"status": "pass"},
    "ownedCompileGraph": {"status": "pass"},
    "packageCompileGraph": {"status": "pass"},
    "serializedNativeCompile": {"status": "pass", "warnings": 0, "errors": 0},
}
AUTHORITY_INTAKE_KEYS = frozenset({
    "authorityClass", "contractName", "doesNotAssert", "ownerPackagePinCount",
    "packagePinCount", "publicationAuthorized", "receiptSha256", "status",
})
AUTHORITY_BINDING_KEYS = frozenset({
    "androidConsumerLock", "authority", "authorityClass", "authorityState",
    "contractName", "dependencyMode", "doesNotAssert", "headlessRuntimeBinding",
    "lockFiles", "ownerPackagePins", "packagePins", "presentationSource",
    "publicationAuthorized", "sdkAuthority", "verificationReceipt",
})
OWNED_GRAPH_KEYS = frozenset({
    "compileProject", "compiledOwnedSourceCount", "generatedProjectReferenceCount",
    "issues", "repoRoot", "schema", "status", "workspaceRoot",
})
PACKAGE_GRAPH_KEYS = frozenset({
    "chummerPackageCount", "contractName", "dependencyMode", "doesNotAssert",
    "projectCount", "projectLibraries", "publicationAuthorized", "status",
})
JOURNAL_STARTED_KEYS = frozenset({
    "command", "contractName", "event", "phase", "processGroupTermination",
    "publicationAuthorized", "timeoutSeconds",
})
JOURNAL_FINISHED_KEYS = frozenset({
    "contractName", "elapsedSeconds", "event", "exitCode", "outputSha256",
    "phase", "processGroupTermination", "publicationAuthorized", "termination",
    "timedOut",
})
JOURNAL_BLOCKED_KEYS = frozenset({
    "contractName", "phase", "event", "reason", "publicationAuthorized",
})
PHASES = (
    ("authority-intake", "authority-intake.log"),
    ("locked-restore", "restore.log"),
    ("owned-compile-graph", "owned-compile-graph.log"),
    ("package-compile-graph", "compile-graph.json"),
    ("serialized-native-compile", "build.log"),
)
PREFLIGHT_STAGE = "preflight"
POST_COMPILE_STAGE = "post-compile-seal"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    payload: dict[str, object] = {}
    for key, value in pairs:
        if key in payload:
            raise ValueError(f"duplicate JSON key: {key}")
        payload[key] = value
    return payload


def parse_json_bytes(value: bytes, label: str) -> object:
    try:
        return json.loads(value.decode("utf-8"), object_pairs_hook=reject_duplicate_keys)
    except UnicodeDecodeError as exc:
        raise ValueError(f"{label} must be UTF-8 JSON") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"{label} is invalid JSON: {exc.msg}") from exc


def parse_json_file(path: Path, label: str) -> object:
    return parse_json_bytes(path.read_bytes(), label)


def require_exact_keys(payload: dict[str, object], expected: frozenset[str], label: str) -> None:
    actual = frozenset(payload)
    if actual != expected:
        missing = sorted(expected - actual)
        extra = sorted(actual - expected)
        raise ValueError(f"{label} keys mismatch; missing={missing}, extra={extra}")


def require_sha(value: object, label: str) -> str:
    if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{40}", value) is None:
        raise ValueError(f"{label} must be a lowercase Git SHA")
    return value


def require_sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-256")
    return value


def require_regular(path: Path, label: str) -> None:
    try:
        mode = path.lstat().st_mode
    except FileNotFoundError as exc:
        raise ValueError(f"{label} is missing") from exc
    if not stat.S_ISREG(mode) or path.is_symlink():
        raise ValueError(f"{label} must be a non-symlink regular file")


def validate_status_contract(payload: dict[str, object]) -> str:
    status = payload.get("status")
    if status not in {"pass", "blocked"}:
        raise ValueError("compile receipt status must be pass or blocked")
    if payload.get("authorityClass") != AUTHORITY_CLASS:
        raise ValueError("compile receipt authority class mismatch")
    if payload.get("publicationAuthorized") is not False:
        raise ValueError("compile receipt must remain publication false")
    if payload.get("proofScope") != PROOF_SCOPE:
        raise ValueError("compile receipt proof scope mismatch")

    if status == "pass":
        if "failureStage" in payload or "retryPerformed" in payload:
            raise ValueError("passing receipt cannot contain blocked-result claims")
        require_exact_keys(payload, PASS_ALLOWED_KEYS, "passing receipt")
        return status

    failure_stage = payload.get("failureStage")
    if not isinstance(failure_stage, str) or not failure_stage.strip():
        raise ValueError("blocked receipt requires failureStage")
    if payload.get("retryPerformed") is not False:
        raise ValueError("blocked receipt must bind retryPerformed=false")
    if payload.get("doesNotAssert") != list(BLOCKED_DOES_NOT_ASSERT):
        raise ValueError("blocked receipt readiness boundary mismatch")
    forbidden = [field for field in PASS_ONLY_FIELDS if field in payload]
    if forbidden:
        raise ValueError(f"blocked receipt contains success-only fields: {', '.join(forbidden)}")
    require_exact_keys(payload, BLOCKED_ALLOWED_KEYS, "blocked receipt")
    return status


def validate_pass_contract(payload: dict[str, object], android_root: Path | None) -> None:
    exact_values = {
        "schema": CONTRACT,
        "authorityClass": AUTHORITY_CLASS,
        "publicationAuthorized": False,
        "dependencyMode": "locked_package_no_siblings",
        "packageOnly": True,
        "restoreLockedMode": True,
        "sourceCheckoutsPresent": False,
        "siblingsAllowed": False,
        "serializedBuild": True,
        "sdkVersion": CONSUMER_SDK_VERSION,
        "producerSdkVersion": PRODUCER_SDK_VERSION,
        "androidWorktreeClean": True,
        "presentationCommit": PRESENTATION_COMMIT,
        "presentationTree": PRESENTATION_TREE,
        "authorityReceiptSha256": AUTHORITY_RECEIPT_SHA256,
        "authorityJournalSha256": AUTHORITY_JOURNAL_SHA256,
        "packageAuthoritySha256": PACKAGE_AUTHORITY_SHA256,
        "desktopRuntimeLockSha256": DESKTOP_RUNTIME_LOCK_SHA256,
        "authorityBindingSha256": AUTHORITY_BINDING_SHA256,
        "lockSha256": ANDROID_LOCK_SHA256,
        "lockSizeBytes": ANDROID_LOCK_SIZE,
        "phaseResults": EXPECTED_PHASE_RESULTS,
        "executionBounds": {
            "perCommandSeconds": 900,
            "totalSeconds": 3600,
            "processGroupTermination": True,
        },
        "proofScope": PROOF_SCOPE,
        "fullMauiBuild": False,
        "coreDataLangContentVerified": False,
        "laterDeviceGateRequirements": list(LATER_DEVICE_REQUIREMENTS),
        "doesNotAssert": list(PASS_DOES_NOT_ASSERT),
    }
    for field, expected in exact_values.items():
        if payload.get(field) != expected:
            raise ValueError(f"passing receipt authoritative field mismatch: {field}")
    android_commit = require_sha(payload.get("androidCommit"), "androidCommit")
    android_tree = require_sha(payload.get("androidTree"), "androidTree")
    require_sha256(payload.get("assetsSha256"), "assetsSha256")

    artifact = payload.get("artifact")
    if not isinstance(artifact, dict):
        raise ValueError("passing receipt artifact must be an object")
    require_exact_keys(
        artifact,
        frozenset({"path", "kind", "scope", "sha256", "sizeBytes", "fullMauiArtifact"}),
        "passing receipt artifact",
    )
    if artifact.get("path") != (
        "tests/Chummer.Android.Native.CompileCheck/bin/Release/net10.0/"
        "Chummer.Android.Native.CompileCheck.dll"
    ):
        raise ValueError("passing receipt artifact path mismatch")
    if artifact.get("kind") != "native_compile_check_dependency_dll":
        raise ValueError("passing receipt artifact kind mismatch")
    if artifact.get("scope") != PROOF_SCOPE or artifact.get("fullMauiArtifact") is not False:
        raise ValueError("passing receipt artifact overclaims compile-only scope")
    artifact_sha256 = require_sha256(artifact.get("sha256"), "artifact.sha256")
    artifact_size = artifact.get("sizeBytes")
    if not isinstance(artifact_size, int) or isinstance(artifact_size, bool) or artifact_size <= 0:
        raise ValueError("passing receipt artifact size must be positive")

    if android_root is None:
        return
    if not android_root.is_absolute() or android_root.resolve(strict=True) != android_root:
        raise ValueError("android root must be canonical")
    command = ["git", "-C", str(android_root)]
    head = subprocess.run(
        [*command, "rev-parse", "HEAD"], check=True, capture_output=True, text=True, timeout=10
    ).stdout.strip()
    tree = subprocess.run(
        [*command, "rev-parse", "HEAD^{tree}"], check=True, capture_output=True, text=True, timeout=10
    ).stdout.strip()
    dirty = subprocess.run(
        [*command, "status", "--porcelain", "--untracked-files=all"],
        check=True, capture_output=True, text=True, timeout=10,
    ).stdout
    if (android_commit, android_tree, dirty) != (head, tree, ""):
        raise ValueError("passing receipt Android clean commit/tree mismatch")
    lock = android_root / "tests/Chummer.Android.Native.CompileCheck/packages.lock.json"
    require_regular(lock, "Android compile lock")
    if sha256(lock) != ANDROID_LOCK_SHA256 or lock.stat().st_size != ANDROID_LOCK_SIZE:
        raise ValueError("passing receipt Android lock bytes mismatch")
    assets = android_root / "tests/Chummer.Android.Native.CompileCheck/obj/project.assets.json"
    require_regular(assets, "Android compile assets")
    if sha256(assets) != payload["assetsSha256"]:
        raise ValueError("passing receipt assets bytes mismatch")
    artifact_path = android_root / str(artifact["path"])
    require_regular(artifact_path, "compile-only artifact")
    if sha256(artifact_path) != artifact_sha256 or artifact_path.stat().st_size != artifact_size:
        raise ValueError("passing receipt compile-only artifact bytes mismatch")


def require_object(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be a JSON object")
    return value


def validate_authority_evidence(paths: dict[str, Path]) -> None:
    if "authority-intake.log" in paths:
        intake = require_object(
            parse_json_file(paths["authority-intake.log"], "authority intake evidence"),
            "authority intake evidence",
        )
        require_exact_keys(intake, AUTHORITY_INTAKE_KEYS, "authority intake evidence")
        expected = {
            "authorityClass": AUTHORITY_CLASS,
            "contractName": "chummer.android.internal-phone-beta-package-authority/v1",
            "doesNotAssert": [
                "api36_device_execution", "google_play_upload", "public_release_readiness",
                "publication_authority", "tablet_readiness",
            ],
            "ownerPackagePinCount": 7,
            "packagePinCount": 6,
            "publicationAuthorized": False,
            "receiptSha256": AUTHORITY_RECEIPT_SHA256,
            "status": "pass",
        }
        if intake != expected:
            raise ValueError("authority intake evidence facts mismatch")
    if "authority-binding.json" in paths:
        authority = require_object(
            parse_json_file(paths["authority-binding.json"], "authority binding evidence"),
            "authority binding evidence",
        )
        require_exact_keys(authority, AUTHORITY_BINDING_KEYS, "authority binding evidence")
        presentation = require_object(authority.get("presentationSource"), "presentationSource")
        verification = require_object(authority.get("verificationReceipt"), "verificationReceipt")
        sdk = require_object(authority.get("sdkAuthority"), "sdkAuthority")
        dependency = require_object(authority.get("dependencyMode"), "dependencyMode")
        android_lock = require_object(authority.get("androidConsumerLock"), "androidConsumerLock")
        desktop_locks = [
            row for row in authority.get("lockFiles", [])
            if isinstance(row, dict) and row.get("path") == "Chummer.Desktop.Runtime/packages.lock.json"
        ]
        checks = (
            authority.get("contractName") == "chummer.android.internal-phone-beta-package-authority/v1",
            authority.get("authorityClass") == AUTHORITY_CLASS,
            authority.get("authorityState") == "independently_audited",
            authority.get("publicationAuthorized") is False,
            presentation.get("packageAuthorityCommit") == PRESENTATION_COMMIT,
            presentation.get("packageAuthorityTree") == PRESENTATION_TREE,
            verification.get("sha256") == AUTHORITY_RECEIPT_SHA256,
            verification.get("journalSha256") == AUTHORITY_JOURNAL_SHA256,
            require_object(authority.get("authority"), "authority").get("sha256") == PACKAGE_AUTHORITY_SHA256,
            sdk.get("packageProofSdkVersion") == PRODUCER_SDK_VERSION,
            sdk.get("selectedAndroidConsumerSdkVersion") == CONSUMER_SDK_VERSION,
            dependency == {
                "packageOnly": True,
                "restoreLockedMode": True,
                "sourceCheckoutsPresent": False,
                "siblingsAllowed": False,
            },
            android_lock.get("sha256") == ANDROID_LOCK_SHA256,
            android_lock.get("sizeBytes") == ANDROID_LOCK_SIZE,
            len(desktop_locks) == 1,
            desktop_locks[0].get("sha256") == DESKTOP_RUNTIME_LOCK_SHA256 if desktop_locks else False,
        )
        if not all(checks):
            raise ValueError("authority binding evidence facts mismatch")


def validate_graph_evidence(paths: dict[str, Path]) -> None:
    if "owned-compile-graph.log" in paths:
        owned = require_object(
            parse_json_file(paths["owned-compile-graph.log"], "owned compile graph evidence"),
            "owned compile graph evidence",
        )
        require_exact_keys(owned, OWNED_GRAPH_KEYS, "owned compile graph evidence")
        if not (
            owned.get("schema") == "chummer.android.native-compile-graph/v1"
            and owned.get("status") == "pass"
            and owned.get("compiledOwnedSourceCount") == 210
            and owned.get("generatedProjectReferenceCount") == 3
            and owned.get("issues") == []
            and isinstance(owned.get("compileProject"), str)
            and str(owned["compileProject"]).endswith(
                "/tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj"
            )
        ):
            raise ValueError("owned compile graph evidence facts mismatch")
    if "compile-graph.json" in paths:
        package = require_object(
            parse_json_file(paths["compile-graph.json"], "package compile graph evidence"),
            "package compile graph evidence",
        )
        require_exact_keys(package, PACKAGE_GRAPH_KEYS, "package compile graph evidence")
        expected = {
            "chummerPackageCount": 14,
            "contractName": "chummer.android.internal-phone-beta-compile-graph/v1",
            "dependencyMode": "locked_package_no_siblings",
            "doesNotAssert": ["api36_device_execution", "public_release_readiness"],
            "projectCount": 3,
            "projectLibraries": [
                "Chummer.Desktop.Runtime/1.0.0", "Chummer.Presentation/1.0.0",
            ],
            "publicationAuthorized": False,
            "status": "pass",
        }
        if package != expected:
            raise ValueError("package compile graph evidence facts mismatch")


def validate_journal(
    path: Path,
    status: str,
    evidence_rows: dict[str, dict[str, object]],
) -> str | None:
    lines = path.read_bytes().splitlines()
    rows = [
        require_object(parse_json_bytes(line, f"journal row {index}"), f"journal row {index}")
        for index, line in enumerate(lines, start=1)
    ]
    if not rows or len(rows) > len(PHASES) * 2:
        raise ValueError("command journal row count is invalid")
    row_index = 0
    phase_index = 0
    failure_stage: str | None = None
    while row_index < len(rows):
        if phase_index >= len(PHASES):
            raise ValueError("command journal contains a later phase/result after completion")
        phase, evidence_name = PHASES[phase_index]
        started = rows[row_index]
        if started.get("event") == "blocked":
            require_exact_keys(started, JOURNAL_BLOCKED_KEYS, f"journal {phase} blocked row")
            if started != {
                "contractName": "chummer.android.internal-phone-beta-command-journal/v1",
                "phase": phase,
                "event": "blocked",
                "reason": "total-deadline-expired",
                "publicationAuthorized": False,
            }:
                raise ValueError(f"command journal deadline block mismatch: {phase}")
            if evidence_name in evidence_rows:
                raise ValueError(f"deadline-blocked phase cannot claim output evidence: {phase}")
            failure_stage = phase
            row_index += 1
            if row_index != len(rows):
                raise ValueError("command journal contains a later phase/result after failure")
            break
        require_exact_keys(started, JOURNAL_STARTED_KEYS, f"journal {phase} started row")
        if row_index + 1 >= len(rows):
            raise ValueError(f"command journal has an incomplete started phase: {phase}")
        finished = rows[row_index + 1]
        require_exact_keys(finished, JOURNAL_FINISHED_KEYS, f"journal {phase} finished row")
        timeout_seconds = started.get("timeoutSeconds")
        if (
            not isinstance(timeout_seconds, (int, float))
            or isinstance(timeout_seconds, bool)
            or not 0 < float(timeout_seconds) <= 900
        ):
            raise ValueError(f"command journal timeout bound mismatch: {phase}")
        if evidence_name not in evidence_rows:
            raise ValueError(f"command journal output evidence is missing: {phase}")
        common = (
            started.get("contractName") == "chummer.android.internal-phone-beta-command-journal/v1",
            finished.get("contractName") == "chummer.android.internal-phone-beta-command-journal/v1",
            started.get("phase") == phase,
            finished.get("phase") == phase,
            started.get("event") == "started",
            finished.get("event") == "finished",
            started.get("processGroupTermination") is True,
            finished.get("processGroupTermination") is True,
            started.get("publicationAuthorized") is False,
            finished.get("publicationAuthorized") is False,
            isinstance(started.get("command"), list) and bool(started.get("command")),
            isinstance(finished.get("elapsedSeconds"), (int, float))
            and not isinstance(finished.get("elapsedSeconds"), bool)
            and float(finished["elapsedSeconds"]) >= 0,
            finished.get("outputSha256") == evidence_rows[evidence_name]["sha256"],
        )
        termination = require_object(finished.get("termination"), "journal termination")
        require_exact_keys(
            termination,
            frozenset({"groupAbsent", "sigkillSent", "sigtermSent"}),
            "journal termination",
        )
        if not all(isinstance(termination.get(field), bool) for field in termination):
            raise ValueError(f"command journal termination fields must be booleans: {phase}")
        exit_code = finished.get("exitCode")
        timed_out = finished.get("timedOut")
        if not isinstance(exit_code, int) or isinstance(exit_code, bool):
            raise ValueError(f"command journal exitCode must be an integer: {phase}")
        if not isinstance(timed_out, bool):
            raise ValueError(f"command journal timedOut must be a boolean: {phase}")
        if not all(common) or termination.get("groupAbsent") is not True:
            raise ValueError(f"command journal phase facts mismatch: {phase}")
        command = [str(value) for value in started["command"]]
        required_arguments = {
            "authority-intake": (
                "verify_internal_phone_beta_package_authority.py",
            ),
            "locked-restore": (
                "restore", "--locked-mode", "--disable-parallel",
                "-p:ChummerUseLocalCompatibilityTree=false",
                "-p:ChummerUseLockedOwnerContractPackages=true",
                "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
            ),
            "owned-compile-graph": ("verify_native_compile_graph.py", "--require-assets"),
            "package-compile-graph": ("verify_internal_phone_beta_compile_graph.py",),
            "serialized-native-compile": (
                "build", "--no-restore", "--warnaserror", "-m:1",
                "-p:BuildInParallel=false",
                "-p:ChummerUseLocalCompatibilityTree=false",
                "-p:ChummerUseLockedOwnerContractPackages=true",
                "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
            ),
        }[phase]
        if not all(any(required in argument for argument in command) for required in required_arguments):
            raise ValueError(f"command journal locked command mismatch: {phase}")
        if any(
            forbidden in argument
            for argument in command
            for forbidden in (
                "ChummerUseLocalCompatibilityTree=true",
                "ChummerUseLockedOwnerContractPackages=false",
                "ChummerLocalContractsProject=",
                "ChummerLocalCampaignContractsProject=",
                "ChummerLocalRunContractsProject=",
                "ChummerLocalUiKitProject=",
            )
        ):
            raise ValueError(f"command journal contains source fallback: {phase}")
        clean_termination = {
            "groupAbsent": True, "sigkillSent": False, "sigtermSent": False,
        }
        if timed_out:
            if not (
                exit_code == 124
                and termination.get("sigtermSent") is True
                and termination.get("groupAbsent") is True
            ):
                raise ValueError(f"command journal timeout termination mismatch: {phase}")
            failure_stage = phase
        elif exit_code == 0:
            if termination != clean_termination:
                raise ValueError(f"passing phase forged process termination: {phase}")
        else:
            if exit_code == 124 or termination != clean_termination:
                raise ValueError(f"non-timeout failure termination mismatch: {phase}")
            failure_stage = phase

        row_index += 2
        phase_index += 1
        if failure_stage is not None:
            if row_index != len(rows):
                raise ValueError("command journal contains a later phase/result after failure")
            break

    if status == "pass":
        if failure_stage is not None or phase_index != len(PHASES) or row_index != len(rows):
            raise ValueError("passing command journal must contain five passing phases")
        return None
    if failure_stage is not None:
        return failure_stage
    if phase_index == len(PHASES) and row_index == len(rows):
        if tuple(evidence_rows) != EXPECTED_EVIDENCE:
            raise ValueError("post-compile failure requires complete evidence")
        return POST_COMPILE_STAGE
    raise ValueError("blocked journal has no authenticated failing phase")


def validate_pass_text_evidence(paths: dict[str, Path]) -> None:
    restore = paths["restore.log"].read_text(encoding="utf-8")
    restored = "Restored " in restore or "All projects are up-to-date for restore." in restore
    if not restored or re.search(r"\b(?:warning|error)\b", restore, re.IGNORECASE):
        raise ValueError("locked restore evidence does not prove a clean pass")
    build = paths["build.log"].read_text(encoding="utf-8")
    if not (
        "Build succeeded." in build
        and re.search(r"\b0 Warning\(s\)", build)
        and re.search(r"\b0 Error\(s\)", build)
    ):
        raise ValueError("serialized compile evidence does not prove warnings=0/errors=0")


def verify_receipt(
    receipt_path: Path,
    evidence_directory: Path | None = None,
    android_root: Path | None = None,
) -> dict[str, object]:
    if not receipt_path.is_absolute():
        raise ValueError("receipt path must be absolute")
    require_regular(receipt_path, "receipt")
    payload = require_object(parse_json_file(receipt_path, "compile receipt"), "compile receipt")
    if payload.get("contractName") != CONTRACT:
        raise ValueError("compile receipt contract mismatch")
    status = validate_status_contract(payload)
    if status == "pass":
        validate_pass_contract(payload, android_root)

    declared_directory = payload.get("evidenceDirectory")
    if not isinstance(declared_directory, str) or not declared_directory.startswith("/"):
        raise ValueError("evidenceDirectory must be absolute")
    expected_directory = evidence_directory or Path(f"{receipt_path}.evidence")
    if Path(declared_directory) != expected_directory:
        raise ValueError("evidenceDirectory does not match the canonical output path")
    try:
        directory_mode = expected_directory.lstat().st_mode
    except FileNotFoundError as exc:
        raise ValueError("evidence directory is missing") from exc
    if not stat.S_ISDIR(directory_mode) or expected_directory.is_symlink():
        raise ValueError("evidence directory must be a non-symlink directory")
    if expected_directory.resolve(strict=True) != expected_directory:
        raise ValueError("evidence directory must be canonical")

    rows = payload.get("evidence")
    if not isinstance(rows, list):
        raise ValueError("evidence inventory must be an array")
    names: list[str] = []
    actual_rows: list[dict[str, object]] = []
    evidence_paths: dict[str, Path] = {}
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("evidence row must be an object")
        require_exact_keys(row, frozenset({"path", "sha256", "sizeBytes"}), "evidence row")
        name = row.get("path")
        if not isinstance(name, str) or name not in EXPECTED_EVIDENCE:
            raise ValueError("evidence path is noncanonical")
        if name in names:
            raise ValueError("duplicate evidence path")
        names.append(name)
        path = expected_directory / name
        require_regular(path, f"evidence {name}")
        digest = sha256(path)
        size = path.stat().st_size
        require_sha256(row.get("sha256"), f"evidence {name} sha256")
        if (
            not isinstance(row.get("sizeBytes"), int)
            or isinstance(row.get("sizeBytes"), bool)
            or int(row["sizeBytes"]) < 0
        ):
            raise ValueError(f"evidence {name} size must be a nonnegative integer")
        if row.get("sha256") != digest or row.get("sizeBytes") != size:
            raise ValueError(f"evidence digest/size mismatch: {name}")
        binding = DIGEST_BINDINGS.get(name)
        if status == "pass" and binding is not None and payload.get(binding) != digest:
            raise ValueError(f"receipt digest binding mismatch: {name}")
        if name == "command-journal.jsonl" and payload.get("journalSizeBytes") != size:
            raise ValueError("receipt journal size binding mismatch")
        actual_rows.append({"path": name, "sha256": digest, "sizeBytes": size})
        evidence_paths[name] = path

    canonical_names = [name for name in EXPECTED_EVIDENCE if name in names]
    if names != canonical_names:
        raise ValueError("evidence inventory order is noncanonical")
    directory_names = sorted(entry.name for entry in os.scandir(expected_directory))
    if directory_names != sorted(names):
        raise ValueError("evidence directory has missing or extra files")
    if status == "pass" and tuple(names) != EXPECTED_EVIDENCE:
        raise ValueError("passing receipt requires the complete evidence inventory")
    validate_authority_evidence(evidence_paths)
    validate_graph_evidence(evidence_paths)
    evidence_by_name = {str(row["path"]): row for row in actual_rows}
    derived_failure_stage: str | None = None
    if "command-journal.jsonl" in evidence_paths:
        derived_failure_stage = validate_journal(
            evidence_paths["command-journal.jsonl"], status, evidence_by_name
        )
    if status == "pass":
        expected_bindings = {
            str(row["path"]): {
                "sha256": row["sha256"],
                "sizeBytes": row["sizeBytes"],
            }
            for row in actual_rows
        }
        if payload.get("evidenceBindings") != expected_bindings:
            raise ValueError("passing receipt top-level evidence bindings mismatch")
        validate_pass_text_evidence(evidence_paths)
    if status == "blocked":
        journal = next((row for row in actual_rows if row["path"] == "command-journal.jsonl"), None)
        expected_digest = journal["sha256"] if journal is not None else ""
        expected_size = journal["sizeBytes"] if journal is not None else 0
        if payload.get("journalSha256") != expected_digest:
            raise ValueError("blocked receipt journal digest binding mismatch")
        if payload.get("journalSizeBytes") != expected_size:
            raise ValueError("blocked receipt journal size binding mismatch")
        failure_stage = payload.get("failureStage")
        allowed_stages = {PREFLIGHT_STAGE, POST_COMPILE_STAGE, *(phase for phase, _ in PHASES)}
        if failure_stage not in allowed_stages:
            raise ValueError("blocked receipt failureStage is outside compile-only scope")
        if journal is None:
            if failure_stage != PREFLIGHT_STAGE or actual_rows:
                raise ValueError("journal-free blocked receipt must be an empty preflight failure")
        else:
            if failure_stage != derived_failure_stage:
                raise ValueError("blocked receipt failureStage does not match authenticated journal")
            if failure_stage in {phase for phase, _ in PHASES}:
                failed_index = next(
                    index for index, (phase, _name) in enumerate(PHASES)
                    if phase == failure_stage
                )
                later_outputs = {name for _phase, name in PHASES[failed_index + 1:]}
                if later_outputs.intersection(evidence_by_name):
                    raise ValueError("blocked receipt claims evidence after the failing phase")

    return {
        "contractName": CONTRACT,
        "status": "pass",
        "verifiedReceiptStatus": status,
        "publicationAuthorized": False,
        "evidence": actual_rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--evidence-directory", type=Path)
    parser.add_argument("--android-root", type=Path, default=ROOT)
    args = parser.parse_args()
    try:
        result = verify_receipt(args.receipt, args.evidence_directory, args.android_root)
    except (OSError, ValueError, json.JSONDecodeError, subprocess.SubprocessError) as exc:
        print(json.dumps({
            "contractName": CONTRACT,
            "status": "blocked",
            "publicationAuthorized": False,
            "error": str(exc),
        }, sort_keys=True))
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
