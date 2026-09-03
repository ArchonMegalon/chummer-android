"""Strict reader for the opt-in, app-private API-36 proof observation.

The file is supplemental evidence only. It cannot tap controls, mutate the app, or replace the
black-box user-route assertions retained by each journey.
"""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
import re
from pathlib import Path
import subprocess
import time
from typing import Any


SCHEMA = "chummer.android.api36-proof-state/v2"
DIGEST_SCHEMA = "chummer.android.api36-proof-state-digest/v2"
PACKAGE = "com.myexternalbrain.chummer"
RELATIVE_PATH = "files/api36-proof/state.v2.json"
READ_ARGUMENTS = ("exec-out", "run-as", PACKAGE, "cat", RELATIVE_PATH)
STAT_ARGUMENTS = (
    "exec-out", "run-as", PACKAGE, "stat", "-c", "%d:%i:%s:%Y:%f",
    RELATIVE_PATH,
)
READ_RECEIPT_SCHEMA = "chummer.android.api36-proof-state-read/v1"
READ_RECEIPT_NAME = "api36-proof-state-read.json"
READ_ATTEMPT_MAX_SECONDS = 3.0
READ_RETRY_DELAY_SECONDS = 0.2
MAX_STATE_BYTES = 32 * 1024
IMPORT_SCHEMA = "chummer.android.api36-import-proof-state/v1"
IMPORT_DIGEST_SCHEMA = "chummer.android.api36-import-proof-state-digest/v1"
IMPORT_RELATIVE_PATH = "files/api36-proof/import.v1.json"
IMPORT_READ_ARGUMENTS = (
    "exec-out", "run-as", PACKAGE, "cat", IMPORT_RELATIVE_PATH,
)
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA64 = re.compile(r"^[0-9a-f]{64}$")
DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
TOKEN = re.compile(r"^[A-Za-z0-9._/:-]+$")
STAT_METADATA = re.compile(
    r"(?P<device>[0-9]+):(?P<inode>[1-9][0-9]*):(?P<size>[0-9]+):"
    r"(?P<modified>[0-9]+):(?P<mode>[0-9a-f]+)"
)

ROOT_FIELDS = {
    "schema", "sequence", "processId", "processInstanceId",
    "e2eAuthorityGeneration", "build", "surface", "workspace",
    "transaction", "creationResources", "stateDigest",
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
CREATION_RESOURCES_FIELDS = {
    "pageIdentity", "workspaceId", "workspaceRevision", "contentRevision",
    "savedRevision", "authorityDigest", "sourceDigest", "rulesDigest",
    "runtimeDigest", "snapshotDigest", "rawCharacterXmlDigest",
    "auxiliaryStateDigest", "prerequisiteDraftRevision",
    "prerequisiteDraftDigest", "priorityNuyen", "totalStartingNuyen",
    "pendingOptionId", "pendingDraftRevision", "pendingDraftDigest",
}
IMPORT_ROOT_FIELDS = {
    "schema", "sequence", "processId", "processInstanceId",
    "e2eAuthorityGeneration", "build", "operationId", "stage", "picker",
    "stream", "workspace", "activationIssued", "failureCode", "stateDigest",
}
IMPORT_PICKER_FIELDS = {"requestCode", "result", "uriPresent", "uriSha256"}
IMPORT_STREAM_FIELDS = {"displayName", "mediaType", "byteLength", "contentSha256"}
IMPORT_WORKSPACE_FIELDS = {"expectedPayloadSha256", "authority"}


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
    read_observation: dict[str, Any] | None = None


@dataclass(frozen=True)
class ImportProofStateSnapshot:
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


def _require_digest(value: object, label: str) -> str:
    result = _optional_digest(value, label)
    if result is None:
        raise RuntimeError(f"{label} is absent")
    return result


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
    resources = state["creationResources"]
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
        None if resources is None else resources["pageIdentity"],
        None if resources is None else resources["workspaceId"],
        None if resources is None else str(resources["workspaceRevision"]),
        None if resources is None else str(resources["contentRevision"]),
        None if resources is None else str(resources["savedRevision"]),
        None if resources is None else resources["authorityDigest"],
        None if resources is None else resources["sourceDigest"],
        None if resources is None else resources["rulesDigest"],
        None if resources is None else resources["runtimeDigest"],
        None if resources is None else resources["snapshotDigest"],
        None if resources is None else resources["rawCharacterXmlDigest"],
        None if resources is None else resources["auxiliaryStateDigest"],
        None if resources is None else str(resources["prerequisiteDraftRevision"]),
        None if resources is None else resources["prerequisiteDraftDigest"],
        None if resources is None else str(resources["priorityNuyen"]),
        None if resources is None else str(resources["totalStartingNuyen"]),
        None if resources is None else resources["pendingOptionId"],
        None if resources is None or resources["pendingDraftRevision"] is None
            else str(resources["pendingDraftRevision"]),
        None if resources is None else resources["pendingDraftDigest"],
    ])


def expected_import_state_digest(state: dict[str, Any]) -> str:
    build = state["build"]
    picker = state["picker"]
    stream = state["stream"]
    workspace = state["workspace"]
    authority = None if workspace is None else workspace["authority"]
    return _length_prefixed_hash([
        IMPORT_DIGEST_SCHEMA,
        state["schema"],
        str(state["sequence"]),
        str(state["processId"]),
        state["processInstanceId"],
        str(state["e2eAuthorityGeneration"]),
        build["sourceCommit"], build["sourceTree"], build["gateContractSha256"],
        build["proofBuildId"], build["packageName"], build["versionName"],
        build["versionCode"], build["runtimeIdentifier"],
        state["operationId"],
        state["stage"],
        None if picker is None else str(picker["requestCode"]),
        None if picker is None else picker["result"],
        "true" if picker is not None and picker["uriPresent"] else "false",
        None if picker is None else picker["uriSha256"],
        None if stream is None else stream["displayName"],
        None if stream is None else stream["mediaType"],
        None if stream is None else str(stream["byteLength"]),
        None if stream is None else stream["contentSha256"],
        None if workspace is None else workspace["expectedPayloadSha256"],
        None if authority is None else authority["workspaceId"],
        None if authority is None else str(authority["contentRevision"]),
        None if authority is None else str(authority["savedRevision"]),
        None if authority is None else authority["payloadSha256"],
        None if authority is None else authority["documentSha256"],
        None if authority is None else authority["snapshotDigest"],
        "true" if state["activationIssued"] else "false",
        state["failureCode"],
    ])


def _validate_build(build_value: object, expected: ProofBuildExpectation) -> dict[str, Any]:
    build = _require_exact_object(build_value, BUILD_FIELDS, "Proof build")
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
    return build


def validate_import_state(
    raw: bytes,
    *,
    expected: ProofBuildExpectation,
    live_process_id: int,
) -> ImportProofStateSnapshot:
    if not 0 < len(raw) <= 16 * 1024 or raw.endswith(b"\n"):
        raise RuntimeError("API-36 import proof bytes are empty, oversized, or noncanonical")
    try:
        state = json.loads(
            raw.decode("utf-8", errors="strict"),
            object_pairs_hook=object_without_duplicates,
        )
    except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as error:
        raise RuntimeError("API-36 import proof JSON is invalid") from error
    state = _require_exact_object(state, IMPORT_ROOT_FIELDS, "API-36 import proof")
    if state["schema"] != IMPORT_SCHEMA:
        raise RuntimeError("API-36 import proof schema is not exact")
    _require_int(state["sequence"], "Import proof sequence", minimum=1)
    process_id = _require_int(state["processId"], "Import proof process ID", minimum=1)
    if process_id != live_process_id:
        raise RuntimeError("API-36 import proof belongs to a stale process")
    import uuid
    try:
        process_instance = uuid.UUID(state["processInstanceId"])
        operation_id = uuid.UUID(state["operationId"])
    except (ValueError, TypeError, AttributeError) as error:
        raise RuntimeError("Import proof identities are not canonical UUIDs") from error
    if (
        str(process_instance) != state["processInstanceId"]
        or process_instance.int == 0
        or str(operation_id) != state["operationId"]
        or operation_id.int == 0
    ):
        raise RuntimeError("Import proof identities are not canonical")
    _require_int(state["e2eAuthorityGeneration"], "Import authority generation")
    _validate_build(state["build"], expected)
    stage = _require_token(state["stage"], "Import proof stage", 64)
    if type(state["activationIssued"]) is not bool:
        raise RuntimeError("Import activation marker is not Boolean")
    if state["failureCode"] is not None:
        _require_token(state["failureCode"], "Import failure code", 128)

    picker = state["picker"]
    if picker is not None:
        picker = _require_exact_object(picker, IMPORT_PICKER_FIELDS, "Import picker")
        if picker["requestCode"] != 6411 or picker["result"] not in {"ok", "cancelled"}:
            raise RuntimeError("Import picker result is not exact")
        if type(picker["uriPresent"]) is not bool:
            raise RuntimeError("Import picker URI marker is not Boolean")
        if picker["uriPresent"] != (picker["result"] == "ok"):
            raise RuntimeError("Import picker URI marker contradicts its result")
        if picker["uriPresent"]:
            if not isinstance(picker["uriSha256"], str) or SHA64.fullmatch(picker["uriSha256"]) is None:
                raise RuntimeError("Import picker URI digest is not canonical")
        elif picker["uriSha256"] is not None:
            raise RuntimeError("Cancelled import picker retained a URI digest")

    stream = state["stream"]
    if stream is not None:
        stream = _require_exact_object(stream, IMPORT_STREAM_FIELDS, "Import stream")
        if not isinstance(stream["displayName"], str) or not 0 < len(stream["displayName"]) <= 256:
            raise RuntimeError("Import stream display name is invalid")
        if stream["mediaType"] is not None and (
            not isinstance(stream["mediaType"], str) or len(stream["mediaType"]) > 256
        ):
            raise RuntimeError("Import stream media type is invalid")
        byte_length = _require_int(stream["byteLength"], "Import byte length", minimum=1)
        if byte_length > 8 * 1024 * 1024:
            raise RuntimeError("Import stream exceeds the bounded input")
        if not isinstance(stream["contentSha256"], str) or SHA64.fullmatch(stream["contentSha256"]) is None:
            raise RuntimeError("Import stream digest is not canonical")

    workspace = state["workspace"]
    if workspace is not None:
        workspace = _require_exact_object(workspace, IMPORT_WORKSPACE_FIELDS, "Import workspace")
        if not isinstance(workspace["expectedPayloadSha256"], str) or SHA64.fullmatch(workspace["expectedPayloadSha256"]) is None:
            raise RuntimeError("Import expected payload digest is not canonical")
        authority = _require_exact_object(
            workspace["authority"], WORKSPACE_FIELDS, "Import workspace authority",
        )
        if (
            not isinstance(authority["workspaceId"], str)
            or not authority["workspaceId"].strip()
            or len(authority["workspaceId"]) > 256
        ):
            raise RuntimeError("Import workspace identity is absent")
        _require_int(authority["contentRevision"], "Import content revision", minimum=1)
        _require_int(authority["savedRevision"], "Import saved revision")
        for key in ("payloadSha256", "documentSha256"):
            if not isinstance(authority[key], str) or SHA64.fullmatch(authority[key]) is None:
                raise RuntimeError(f"Import workspace {key} is not canonical")
        _optional_digest(authority["snapshotDigest"], "Import snapshot digest")
        if workspace["expectedPayloadSha256"] != authority["payloadSha256"]:
            raise RuntimeError("Import workspace does not match the selected stream")
        if stream is None or workspace["expectedPayloadSha256"] != stream["contentSha256"]:
            raise RuntimeError("Import workspace is not bound to the read document stream")

    successful_picker = picker is not None and picker["result"] == "ok"
    exact_stage = {
        "picker-launched": picker is None and stream is None and workspace is None
            and not state["activationIssued"] and state["failureCode"] is None,
        "picker-callback": successful_picker and stream is None and workspace is None
            and not state["activationIssued"] and state["failureCode"] is None,
        "stream-read": successful_picker and stream is not None and workspace is None
            and not state["activationIssued"] and state["failureCode"] is None,
        "workspace-verified": successful_picker and stream is not None and workspace is not None
            and not state["activationIssued"] and state["failureCode"] is None,
        "activation-issued": successful_picker and stream is not None and workspace is not None
            and state["activationIssued"] and state["failureCode"] is None,
        "cancelled": picker is not None and picker["result"] == "cancelled"
            and stream is None and workspace is None and not state["activationIssued"]
            and state["failureCode"] is None,
        "failed": not state["activationIssued"] and state["failureCode"] is not None,
    }.get(stage, False)
    if not exact_stage:
        raise RuntimeError("Import proof stage does not match its accumulated authority")
    if not isinstance(state["stateDigest"], str) or DIGEST.fullmatch(state["stateDigest"]) is None:
        raise RuntimeError("Import proof digest is not canonical")
    if state["stateDigest"] != expected_import_state_digest(state):
        raise RuntimeError("Import proof digest does not match the exact fields")
    return ImportProofStateSnapshot(state, hashlib.sha256(raw).hexdigest())


def validate_state(
    raw: bytes,
    *,
    expected: ProofBuildExpectation,
    live_process_id: int,
) -> ProofStateSnapshot:
    if not 0 < len(raw) <= MAX_STATE_BYTES or raw.endswith(b"\n"):
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

    resources = state["creationResources"]
    creation_resources_surface = (
        surface["pageAutomationId"] == "creation-resources-page"
        or surface["wizardLane"] == "creation-resources"
    )
    if creation_resources_surface != (resources is not None):
        raise RuntimeError("Creation Resources surface and typed state disagree")
    if resources is not None:
        resources = _require_exact_object(
            resources, CREATION_RESOURCES_FIELDS, "Creation Resources proof state"
        )
        if (
            workspace is None
            or resources["pageIdentity"] != surface["pageAutomationId"]
            or resources["pageIdentity"] != "creation-resources-page"
            or surface["wizardLane"] != "creation-resources"
            or surface["stage"] != "authority-ready"
            or surface["settled"] is not True
            or resources["workspaceId"] != workspace["workspaceId"]
        ):
            raise RuntimeError("Creation Resources state is not bound to its page and workspace")
        for key in ("workspaceRevision", "contentRevision"):
            _require_int(resources[key], f"Creation Resources {key}", minimum=1)
        _require_int(resources["savedRevision"], "Creation Resources savedRevision")
        if (
            resources["workspaceRevision"] != resources["contentRevision"]
            or resources["contentRevision"] != workspace["contentRevision"]
            or resources["savedRevision"] != workspace["savedRevision"]
        ):
            raise RuntimeError("Creation Resources revisions are not bound to the workspace")
        for key in (
            "authorityDigest", "sourceDigest", "rulesDigest", "runtimeDigest",
            "snapshotDigest", "rawCharacterXmlDigest", "prerequisiteDraftDigest",
        ):
            _require_digest(resources[key], f"Creation Resources {key}")
        if resources["snapshotDigest"] != workspace["snapshotDigest"]:
            raise RuntimeError("Creation Resources snapshot is not bound to the workspace")
        if (
            not isinstance(resources["auxiliaryStateDigest"], str)
            or SHA64.fullmatch(resources["auxiliaryStateDigest"]) is None
        ):
            raise RuntimeError("Creation Resources auxiliary digest is not canonical")
        _require_int(
            resources["prerequisiteDraftRevision"],
            "Creation Resources prerequisite draft revision",
            minimum=1,
        )
        for key in ("priorityNuyen", "totalStartingNuyen"):
            if not isinstance(resources[key], (int, float)) or isinstance(resources[key], bool):
                raise RuntimeError(f"Creation Resources {key} is not numeric")
            if (
                isinstance(resources[key], float)
                and not math.isfinite(resources[key])
            ) or resources[key] < 0:
                raise RuntimeError(f"Creation Resources {key} is not finite and nonnegative")
        pending = (
            resources["pendingOptionId"],
            resources["pendingDraftRevision"],
            resources["pendingDraftDigest"],
        )
        if pending != (None, None, None):
            _require_token(pending[0], "Creation Resources pending option", 128)
            _require_int(pending[1], "Creation Resources pending draft revision", minimum=1)
            _require_digest(pending[2], "Creation Resources pending draft digest")

    if not isinstance(state["stateDigest"], str) or DIGEST.fullmatch(state["stateDigest"]) is None:
        raise RuntimeError("Proof-state digest is not canonical")
    if state["stateDigest"] != expected_state_digest(state):
        raise RuntimeError("Proof-state digest does not match the exact fields")
    return ProofStateSnapshot(state, hashlib.sha256(raw).hexdigest())


def require_creation_resources(snapshot: ProofStateSnapshot) -> dict[str, Any]:
    resources = snapshot.payload.get("creationResources")
    if not isinstance(resources, dict):
        raise RuntimeError("API-36 proof state has no Creation Resources authority")
    return resources


def _parse_state_metadata(output: object) -> dict[str, int] | None:
    if not isinstance(output, str):
        return None
    match = STAT_METADATA.fullmatch(output.strip())
    if match is None:
        return None
    mode = int(match.group("mode"), 16)
    return {
        "deviceId": int(match.group("device")),
        "inode": int(match.group("inode")),
        "sizeBytes": int(match.group("size")),
        "modifiedSeconds": int(match.group("modified")),
        "mode": mode,
        "regularFile": (mode & 0o170000) == 0o100000,
    }


def _stream_summary(value: object) -> dict[str, object]:
    if isinstance(value, str):
        raw = value.encode("utf-8", errors="strict")
        return {"type": "text", "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}
    if isinstance(value, bytes):
        return {"type": "bytes", "bytes": len(value), "sha256": hashlib.sha256(value).hexdigest()}
    return {"type": type(value).__name__, "bytes": None, "sha256": None}


def _write_state_read_receipt(device: object, receipt: dict[str, Any]) -> None:
    evidence_value = getattr(device, "evidence", None)
    if not isinstance(evidence_value, (str, Path)):
        return
    evidence = Path(evidence_value)
    evidence.mkdir(parents=True, exist_ok=True)
    destination = evidence / READ_RECEIPT_NAME
    temporary = evidence / f"{READ_RECEIPT_NAME}.tmp"
    temporary.write_text(
        json.dumps(receipt, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    temporary.replace(destination)


def _state_file_observation(
    device: object,
    *,
    deadline: float,
    attempt: int,
) -> tuple[bytes | None, int | None, dict[str, Any]]:
    process_before_output = device.shell(
        "pidof", PACKAGE, timeout=READ_ATTEMPT_MAX_SECONDS, deadline=deadline
    )
    process_before_tokens = process_before_output.split()
    process_before = (
        int(process_before_tokens[0])
        if len(process_before_tokens) == 1 and process_before_tokens[0].isdigit()
        else None
    )
    if process_before is None or process_before <= 0:
        return None, None, {
            "attempt": attempt,
            "status": "retry",
            "reconciliation": "live-process-unavailable",
            "processBefore": process_before_tokens,
            "processAfter": None,
            "metadataBefore": None,
            "metadataAfter": None,
            "contentBytes": None,
            "contentSha256": None,
            "lastByteHex": None,
        }

    before = device.run(
        *STAT_ARGUMENTS,
        timeout=READ_ATTEMPT_MAX_SECONDS,
        deadline=deadline,
        check=False,
    )
    content = device.run(
        *READ_ARGUMENTS,
        timeout=READ_ATTEMPT_MAX_SECONDS,
        deadline=deadline,
        text=False,
        check=False,
    )
    after = device.run(
        *STAT_ARGUMENTS,
        timeout=READ_ATTEMPT_MAX_SECONDS,
        deadline=deadline,
        check=False,
    )
    process_after_output = device.shell(
        "pidof", PACKAGE, timeout=READ_ATTEMPT_MAX_SECONDS, deadline=deadline
    )
    process_after_tokens = process_after_output.split()
    process_after = (
        int(process_after_tokens[0])
        if len(process_after_tokens) == 1 and process_after_tokens[0].isdigit()
        else None
    )
    before_metadata = _parse_state_metadata(before.stdout)
    after_metadata = _parse_state_metadata(after.stdout)
    raw = content.stdout
    content_bytes = len(raw) if isinstance(raw, bytes) else None
    metadata_identical = (
        before_metadata is not None
        and before_metadata == after_metadata
        and before_metadata["regularFile"] is True
    )
    exact = (
        process_before == process_after
        and before.returncode == 0
        and content.returncode == 0
        and after.returncode == 0
        and metadata_identical
        and isinstance(raw, bytes)
        and content_bytes == before_metadata["sizeBytes"]
    )
    if process_before != process_after or process_after is None:
        reconciliation = "process-identity-drift"
    elif before.returncode != 0 or content.returncode != 0 or after.returncode != 0:
        reconciliation = "file-observation-unavailable"
    elif before_metadata is None or after_metadata is None:
        reconciliation = "metadata-noncanonical"
    elif not metadata_identical:
        reconciliation = "metadata-identity-drift"
    elif not isinstance(raw, bytes):
        reconciliation = "content-type-noncanonical"
    elif content_bytes != before_metadata["sizeBytes"]:
        reconciliation = "content-size-mismatch"
    else:
        reconciliation = "metadata-content-metadata-identity"
    observation: dict[str, Any] = {
        "attempt": attempt,
        "status": "pass" if exact else "retry",
        "reconciliation": reconciliation,
        "processBefore": process_before,
        "processAfter": process_after,
        "metadataArguments": list(STAT_ARGUMENTS),
        "contentArguments": list(READ_ARGUMENTS),
        "metadataBeforeReturnCode": before.returncode,
        "contentReturnCode": content.returncode,
        "metadataAfterReturnCode": after.returncode,
        "metadataBefore": before_metadata,
        "metadataAfter": after_metadata,
        "metadataBeforeOutput": _stream_summary(before.stdout),
        "contentOutput": _stream_summary(raw),
        "metadataAfterOutput": _stream_summary(after.stdout),
        "contentBytes": content_bytes,
        "contentSha256": hashlib.sha256(raw).hexdigest() if isinstance(raw, bytes) else None,
        "lastByteHex": raw[-1:].hex() if isinstance(raw, bytes) and raw else None,
        "withinPayloadBound": (
            isinstance(raw, bytes) and 0 < len(raw) <= MAX_STATE_BYTES
        ),
        "canonicalTerminalByte": isinstance(raw, bytes) and bool(raw) and not raw.endswith(b"\n"),
    }
    return (raw if exact else None), process_before, observation


def wait_for_state(
    device: object,
    *,
    expected: ProofBuildExpectation,
    page_automation_id: str,
    stage: str,
    wizard_lane: str | None,
    timeout: float = 30,
) -> ProofStateSnapshot:
    if not math.isfinite(timeout) or timeout <= 0:
        raise ValueError("Proof-state wait timeout must be finite and positive")
    deadline = time.monotonic() + timeout
    last_detail = "state file unavailable"
    attempts: list[dict[str, Any]] = []
    started = time.monotonic()
    while time.monotonic() < deadline:
        try:
            raw, live_process_id, observation = _state_file_observation(
                device,
                deadline=deadline,
                attempt=len(attempts) + 1,
            )
        except Exception as error:
            receipt = {
                "schema": READ_RECEIPT_SCHEMA,
                "status": "fail",
                "requestedSurface": {
                    "pageAutomationId": page_automation_id,
                    "stage": stage,
                    "wizardLane": wizard_lane,
                },
                "maximumPayloadBytes": MAX_STATE_BYTES,
                "attempts": attempts,
                "acceptedAttempt": None,
                "failure": {"type": type(error).__name__, "message": str(error)},
                "mutationCommandsRetried": 0,
                "elapsedMs": round((time.monotonic() - started) * 1000),
            }
            _write_state_read_receipt(device, receipt)
            raise
        attempts.append(observation)
        if raw is None or live_process_id is None:
            last_detail = observation["reconciliation"]
            _write_state_read_receipt(
                device,
                {
                    "schema": READ_RECEIPT_SCHEMA,
                    "status": "retrying-read-only",
                    "requestedSurface": {
                        "pageAutomationId": page_automation_id,
                        "stage": stage,
                        "wizardLane": wizard_lane,
                    },
                    "maximumPayloadBytes": MAX_STATE_BYTES,
                    "attempts": attempts,
                    "acceptedAttempt": None,
                    "failure": None,
                    "mutationCommandsRetried": 0,
                    "elapsedMs": round((time.monotonic() - started) * 1000),
                },
            )
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                break
            time.sleep(min(READ_RETRY_DELAY_SECONDS, remaining))
            continue
        try:
            snapshot = validate_state(
                raw,
                expected=expected,
                live_process_id=live_process_id,
            )
        except RuntimeError as error:
            if str(error) != "API-36 proof state belongs to a stale process":
                observation["status"] = "fail"
                observation["validationFailure"] = str(error)
                _write_state_read_receipt(
                    device,
                    {
                        "schema": READ_RECEIPT_SCHEMA,
                        "status": "fail",
                        "requestedSurface": {
                            "pageAutomationId": page_automation_id,
                            "stage": stage,
                            "wizardLane": wizard_lane,
                        },
                        "maximumPayloadBytes": MAX_STATE_BYTES,
                        "attempts": attempts,
                        "acceptedAttempt": None,
                        "failure": {"type": type(error).__name__, "message": str(error)},
                        "mutationCommandsRetried": 0,
                        "elapsedMs": round((time.monotonic() - started) * 1000),
                    },
                )
                raise
            last_detail = "state file belongs to the preceding process"
            observation["status"] = "retry"
            observation["validationFailure"] = str(error)
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                break
            time.sleep(min(READ_RETRY_DELAY_SECONDS, remaining))
            continue
        surface = snapshot.payload["surface"]
        if (
            surface["pageAutomationId"] == page_automation_id
            and surface["stage"] == stage
            and surface["wizardLane"] == wizard_lane
            and surface["settled"] is True
        ):
            receipt = {
                "schema": READ_RECEIPT_SCHEMA,
                "status": "pass",
                "requestedSurface": {
                    "pageAutomationId": page_automation_id,
                    "stage": stage,
                    "wizardLane": wizard_lane,
                },
                "maximumPayloadBytes": MAX_STATE_BYTES,
                "attempts": attempts,
                "acceptedAttempt": observation["attempt"],
                "failure": None,
                "mutationCommandsRetried": 0,
                "elapsedMs": round((time.monotonic() - started) * 1000),
            }
            _write_state_read_receipt(device, receipt)
            return ProofStateSnapshot(
                snapshot.payload,
                snapshot.serialized_sha256,
                dict(observation),
            )
        last_detail = (
            f"page={surface['pageAutomationId']!r} stage={surface['stage']!r} "
            f"lane={surface['wizardLane']!r} settled={surface['settled']!r}"
        )
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        time.sleep(min(READ_RETRY_DELAY_SECONDS, remaining))
    _write_state_read_receipt(
        device,
        {
            "schema": READ_RECEIPT_SCHEMA,
            "status": "fail",
            "requestedSurface": {
                "pageAutomationId": page_automation_id,
                "stage": stage,
                "wizardLane": wizard_lane,
            },
            "maximumPayloadBytes": MAX_STATE_BYTES,
            "attempts": attempts,
            "acceptedAttempt": None,
            "failure": {"type": "RuntimeError", "message": last_detail},
            "mutationCommandsRetried": 0,
            "elapsedMs": round((time.monotonic() - started) * 1000),
        },
    )
    raise RuntimeError(f"Timed out waiting for exact API-36 proof state: {last_detail}")


def wait_for_import_activation(
    device: object,
    *,
    expected: ProofBuildExpectation,
    content_sha256: str,
    timeout: float = 120,
) -> ImportProofStateSnapshot:
    if SHA64.fullmatch(content_sha256) is None:
        raise RuntimeError("Expected import content digest is not canonical")
    deadline = time.monotonic() + timeout
    last_detail = "import proof file unavailable"
    while time.monotonic() < deadline:
        process_output = device.shell("pidof", PACKAGE)
        process_ids = process_output.split()
        if len(process_ids) != 1 or not process_ids[0].isdigit():
            last_detail = f"expected one live process, got {process_ids!r}"
            time.sleep(0.2)
            continue
        result = device.run(*IMPORT_READ_ARGUMENTS, text=False, check=False)
        if result.returncode != 0 or not result.stdout:
            last_detail = "import proof file unavailable"
            time.sleep(0.2)
            continue
        try:
            snapshot = validate_import_state(
                result.stdout,
                expected=expected,
                live_process_id=int(process_ids[0]),
            )
        except RuntimeError as error:
            if str(error) != "API-36 import proof belongs to a stale process":
                raise
            last_detail = "import proof belongs to the preceding process"
            time.sleep(0.2)
            continue
        state = snapshot.payload
        stage = state["stage"]
        stream = state["stream"]
        if stage in {"cancelled", "failed"}:
            raise RuntimeError(
                "Document import failed before workspace activation: "
                f"stage={stage!r}, failureCode={state['failureCode']!r}, "
                f"picker={state['picker']!r}"
            )
        if stream is not None and stream["contentSha256"] != content_sha256:
            raise RuntimeError("Document import stream differs from the governed fixture")
        if stage == "activation-issued":
            workspace = state["workspace"]
            if (
                stream is None
                or workspace is None
                or stream["contentSha256"] != content_sha256
                or workspace["expectedPayloadSha256"] != content_sha256
                or not state["activationIssued"]
            ):
                raise RuntimeError("Document activation is not bound to the governed fixture")
            return snapshot
        last_detail = f"stage={stage!r}, sequence={state['sequence']!r}"
        time.sleep(0.2)
    raise RuntimeError(
        f"Timed out waiting for exact API-36 import activation: {last_detail}"
    )
