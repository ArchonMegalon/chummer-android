"""Strict reader for the opt-in, app-private API-36 proof observation.

The file is supplemental evidence only. It cannot tap controls, mutate the app, or replace the
black-box user-route assertions retained by each journey.
"""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import re
from pathlib import Path
import subprocess
import time
from typing import Any


SCHEMA = "chummer.android.api36-proof-state/v1"
DIGEST_SCHEMA = "chummer.android.api36-proof-state-digest/v1"
PACKAGE = "com.myexternalbrain.chummer"
RELATIVE_PATH = "files/api36-proof/state.v1.json"
READ_ARGUMENTS = ("exec-out", "run-as", PACKAGE, "cat", RELATIVE_PATH)
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA64 = re.compile(r"^[0-9a-f]{64}$")
DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
TOKEN = re.compile(r"^[A-Za-z0-9._/:-]+$")

ROOT_FIELDS = {
    "schema", "sequence", "processId", "processInstanceId",
    "e2eAuthorityGeneration", "build", "surface", "workspace",
    "transaction", "stateDigest",
}
BUILD_FIELDS = {
    "sourceCommit", "sourceTree", "gateContractSha256", "proofBuildId",
    "packageName", "versionName", "versionCode", "runtimeIdentifier",
}
SURFACE_FIELDS = {
    "shellDestination", "pageAutomationId", "navigationDepth", "wizardLane",
    "stage", "settled",
}
WORKSPACE_FIELDS = {
    "workspaceId", "contentRevision", "savedRevision", "payloadSha256",
    "documentSha256", "snapshotDigest",
}
TRANSACTION_FIELDS = {
    "checkpointReadStatus", "phase", "journalVersion", "transactionId",
    "journalDigest", "actionId", "actionKind", "actionDigest",
    "expectedWorkspaceRevision", "appliedWorkspaceRevision",
    "expectedPostconditionDigest", "observedPostconditionDigest", "receiptDigest",
    "resumeRestored", "canConfirm", "statusCode",
}


@dataclass(frozen=True)
class ProofBuildExpectation:
    source_commit: str
    source_tree: str
    gate_contract_sha256: str
    proof_build_id: str


@dataclass(frozen=True)
class ProofStateSnapshot:
    payload: dict[str, Any]
    serialized_sha256: str


def object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, member in pairs:
        if key in value:
            raise ValueError(f"duplicate API-36 proof-state field: {key}")
        value[key] = member
    return value


def expected_build(repo_root: Path, gate_contract: Path, proof_build_id: str) -> ProofBuildExpectation:
    source_commit = subprocess.run(
        ["git", "-C", str(repo_root), "rev-parse", "HEAD"],
        check=True, text=True, capture_output=True,
    ).stdout.strip()
    source_tree = subprocess.run(
        ["git", "-C", str(repo_root), "rev-parse", "HEAD^{tree}"],
        check=True, text=True, capture_output=True,
    ).stdout.strip()
    return ProofBuildExpectation(
        source_commit,
        source_tree,
        hashlib.sha256(gate_contract.read_bytes()).hexdigest(),
        proof_build_id,
    )


def _require_exact_object(value: object, fields: set[str], label: str) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != fields:
        raise RuntimeError(f"{label} fields are not exact")
    return value


def _require_int(value: object, label: str, *, minimum: int = 0) -> int:
    if type(value) is not int or value < minimum:
        raise RuntimeError(f"{label} is not an exact integer")
    return value


def _require_token(value: object, label: str, maximum: int) -> str:
    if (
        not isinstance(value, str)
        or not 0 < len(value) <= maximum
        or TOKEN.fullmatch(value) is None
    ):
        raise RuntimeError(f"{label} is not a bounded token")
    return value


def _optional_digest(value: object, label: str) -> str | None:
    if value is None:
        return None
    if not isinstance(value, str) or DIGEST.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not a canonical typed SHA-256")
    return value


def _optional_int(value: object, label: str) -> int | None:
    if value is None:
        return None
    return _require_int(value, label, minimum=1)


def _length_prefixed_hash(values: list[str | None]) -> str:
    digest = hashlib.sha256()
    for value in values:
        if value is None:
            digest.update((-1).to_bytes(4, "big", signed=True))
            continue
        encoded = value.encode("utf-8", errors="strict")
        digest.update(len(encoded).to_bytes(4, "big", signed=True))
        digest.update(encoded)
    return "sha256:" + digest.hexdigest()


def expected_state_digest(state: dict[str, Any]) -> str:
    build = state["build"]
    surface = state["surface"]
    workspace = state["workspace"]
    transaction = state["transaction"]
    return _length_prefixed_hash([
        DIGEST_SCHEMA,
        state["schema"],
        str(state["sequence"]),
        str(state["processId"]),
        state["processInstanceId"],
        str(state["e2eAuthorityGeneration"]),
        build["sourceCommit"], build["sourceTree"], build["gateContractSha256"],
        build["proofBuildId"], build["packageName"], build["versionName"],
        build["versionCode"], build["runtimeIdentifier"],
        surface["shellDestination"], surface["pageAutomationId"],
        str(surface["navigationDepth"]), surface["wizardLane"], surface["stage"],
        "true" if surface["settled"] else "false",
        None if workspace is None else workspace["workspaceId"],
        None if workspace is None else str(workspace["contentRevision"]),
        None if workspace is None else str(workspace["savedRevision"]),
        None if workspace is None else workspace["payloadSha256"],
        None if workspace is None else workspace["documentSha256"],
        None if workspace is None else workspace["snapshotDigest"],
        None if transaction is None else transaction["checkpointReadStatus"],
        None if transaction is None else transaction["phase"],
        None if transaction is None or transaction["journalVersion"] is None
            else str(transaction["journalVersion"]),
        None if transaction is None else transaction["transactionId"],
        None if transaction is None else transaction["journalDigest"],
        None if transaction is None else transaction["actionId"],
        None if transaction is None else transaction["actionKind"],
        None if transaction is None else transaction["actionDigest"],
        None if transaction is None or transaction["expectedWorkspaceRevision"] is None
            else str(transaction["expectedWorkspaceRevision"]),
        None if transaction is None or transaction["appliedWorkspaceRevision"] is None
            else str(transaction["appliedWorkspaceRevision"]),
        None if transaction is None else transaction["expectedPostconditionDigest"],
        None if transaction is None else transaction["observedPostconditionDigest"],
        None if transaction is None else transaction["receiptDigest"],
        "true" if transaction is not None and transaction["resumeRestored"] else "false",
        "true" if transaction is not None and transaction["canConfirm"] else "false",
        None if transaction is None else transaction["statusCode"],
    ])


def validate_state(
    raw: bytes,
    *,
    expected: ProofBuildExpectation,
    live_process_id: int,
) -> ProofStateSnapshot:
    if not 0 < len(raw) <= 32 * 1024 or raw.endswith(b"\n"):
        raise RuntimeError("API-36 proof-state bytes are empty, oversized, or noncanonical")
    try:
        state = json.loads(raw.decode("utf-8", errors="strict"), object_pairs_hook=object_without_duplicates)
    except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as error:
        raise RuntimeError("API-36 proof-state JSON is invalid") from error
    state = _require_exact_object(state, ROOT_FIELDS, "API-36 proof state")
    if state["schema"] != SCHEMA:
        raise RuntimeError("API-36 proof-state schema is not exact")
    _require_int(state["sequence"], "Proof sequence", minimum=1)
    process_id = _require_int(state["processId"], "Proof process ID", minimum=1)
    if process_id != live_process_id:
        raise RuntimeError("API-36 proof state belongs to a stale process")
    try:
        import uuid
        process_instance = uuid.UUID(state["processInstanceId"])
    except (ValueError, TypeError, AttributeError) as error:
        raise RuntimeError("Proof process instance is not a canonical UUID") from error
    if str(process_instance) != state["processInstanceId"] or process_instance.int == 0:
        raise RuntimeError("Proof process instance is not canonical")
    _require_int(state["e2eAuthorityGeneration"], "E2E authority generation")

    build = _require_exact_object(state["build"], BUILD_FIELDS, "Proof build")
    if build != {
        "sourceCommit": expected.source_commit,
        "sourceTree": expected.source_tree,
        "gateContractSha256": expected.gate_contract_sha256,
        "proofBuildId": expected.proof_build_id,
        "packageName": PACKAGE,
        "versionName": "0.1.0-preview.10",
        "versionCode": "10",
        "runtimeIdentifier": "android-x64",
    }:
        raise RuntimeError("API-36 proof-state build identity is not exact")
    if SHA40.fullmatch(build["sourceCommit"]) is None or SHA40.fullmatch(build["sourceTree"]) is None:
        raise RuntimeError("Proof source identity is not canonical")
    if SHA64.fullmatch(build["gateContractSha256"]) is None:
        raise RuntimeError("Proof gate-contract digest is not canonical")

    surface = _require_exact_object(state["surface"], SURFACE_FIELDS, "Proof surface")
    _require_token(surface["shellDestination"], "Proof shell destination", 64)
    _require_token(surface["pageAutomationId"], "Proof page identity", 128)
    _require_int(surface["navigationDepth"], "Proof navigation depth")
    if surface["navigationDepth"] > 64:
        raise RuntimeError("Proof navigation depth is unbounded")
    if surface["wizardLane"] is not None:
        _require_token(surface["wizardLane"], "Proof wizard lane", 64)
    _require_token(surface["stage"], "Proof stage", 64)
    if type(surface["settled"]) is not bool:
        raise RuntimeError("Proof settled marker is not Boolean")

    workspace = state["workspace"]
    if workspace is not None:
        workspace = _require_exact_object(workspace, WORKSPACE_FIELDS, "Proof workspace")
        if (
            not isinstance(workspace["workspaceId"], str)
            or not workspace["workspaceId"].strip()
            or len(workspace["workspaceId"]) > 256
        ):
            raise RuntimeError("Proof workspace identity is absent")
        _require_int(workspace["contentRevision"], "Proof content revision", minimum=1)
        _require_int(workspace["savedRevision"], "Proof saved revision")
        for key in ("payloadSha256", "documentSha256"):
            if not isinstance(workspace[key], str) or SHA64.fullmatch(workspace[key]) is None:
                raise RuntimeError(f"Proof workspace {key} is not canonical")
        _optional_digest(workspace["snapshotDigest"], "Proof snapshot digest")

    transaction = state["transaction"]
    if transaction is not None:
        transaction = _require_exact_object(transaction, TRANSACTION_FIELDS, "Proof transaction")
        _require_token(transaction["checkpointReadStatus"], "Checkpoint status", 32)
        if transaction["phase"] is not None:
            _require_token(transaction["phase"], "Transaction phase", 32)
        _optional_int(transaction["journalVersion"], "Journal version")
        if transaction["transactionId"] is not None:
            try:
                import uuid
                transaction_id = uuid.UUID(transaction["transactionId"])
            except (ValueError, TypeError, AttributeError) as error:
                raise RuntimeError("Transaction identity is not a canonical UUID") from error
            if str(transaction_id) != transaction["transactionId"] or transaction_id.int == 0:
                raise RuntimeError("Transaction identity is not canonical")
        for key in (
            "journalDigest", "actionDigest", "expectedPostconditionDigest",
            "observedPostconditionDigest", "receiptDigest",
        ):
            _optional_digest(transaction[key], f"Transaction {key}")
        for key, maximum in (("actionId", 256), ("actionKind", 64), ("statusCode", 128)):
            if transaction[key] is not None:
                _require_token(transaction[key], f"Transaction {key}", maximum)
        _optional_int(transaction["expectedWorkspaceRevision"], "Expected workspace revision")
        _optional_int(transaction["appliedWorkspaceRevision"], "Applied workspace revision")
        if type(transaction["resumeRestored"]) is not bool or type(transaction["canConfirm"]) is not bool:
            raise RuntimeError("Transaction Boolean fields are not exact")
        if (
            workspace is not None
            and transaction["expectedWorkspaceRevision"] is not None
            and transaction["appliedWorkspaceRevision"] is None
            and transaction["expectedWorkspaceRevision"] != workspace["contentRevision"]
        ):
            raise RuntimeError("Transaction is not bound to the observed workspace revision")

    if not isinstance(state["stateDigest"], str) or DIGEST.fullmatch(state["stateDigest"]) is None:
        raise RuntimeError("Proof-state digest is not canonical")
    if state["stateDigest"] != expected_state_digest(state):
        raise RuntimeError("Proof-state digest does not match the exact fields")
    return ProofStateSnapshot(state, hashlib.sha256(raw).hexdigest())


def wait_for_state(
    device: object,
    *,
    expected: ProofBuildExpectation,
    page_automation_id: str,
    stage: str,
    wizard_lane: str | None,
    timeout: float = 30,
) -> ProofStateSnapshot:
    deadline = time.monotonic() + timeout
    last_detail = "state file unavailable"
    while time.monotonic() < deadline:
        process_output = device.shell("pidof", PACKAGE)
        process_ids = process_output.split()
        if len(process_ids) != 1 or not process_ids[0].isdigit():
            last_detail = f"expected one live process, got {process_ids!r}"
            time.sleep(0.2)
            continue
        result = device.run(*READ_ARGUMENTS, text=False, check=False)
        if result.returncode != 0 or not result.stdout:
            last_detail = "state file unavailable"
            time.sleep(0.2)
            continue
        try:
            snapshot = validate_state(
                result.stdout,
                expected=expected,
                live_process_id=int(process_ids[0]),
            )
        except RuntimeError as error:
            if str(error) != "API-36 proof state belongs to a stale process":
                raise
            last_detail = "state file belongs to the preceding process"
            time.sleep(0.2)
            continue
        surface = snapshot.payload["surface"]
        if (
            surface["pageAutomationId"] == page_automation_id
            and surface["stage"] == stage
            and surface["wizardLane"] == wizard_lane
            and surface["settled"] is True
        ):
            return snapshot
        last_detail = (
            f"page={surface['pageAutomationId']!r} stage={surface['stage']!r} "
            f"lane={surface['wizardLane']!r} settled={surface['settled']!r}"
        )
        time.sleep(0.2)
    raise RuntimeError(f"Timed out waiting for exact API-36 proof state: {last_detail}")
