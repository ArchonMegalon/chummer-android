#!/usr/bin/env python3
"""Prove the first SR5 Life Modules Origin turn on a physical API-36 phone.

This is deliberately a non-gating, operator-invoked proof. It binds one
authorized disposable device, one caller-supplied APK and its sealed source
graph, then proves only the Nationality Origin turn. It contributes nothing to
the Android aggregate and grants no Play, publication, tablet, later-stage,
generic-editing, provider, or network-transport claim.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_editing_e2e as shared
import run_api36_sr5_career_attribute_wizard_e2e as physical


RECEIPT_SCHEMA = "chummer.android.sr5-life-module-nationality-origin-physical-e2e/v1"
JOURNEY = "sr5-life-module-nationality-origin-physical"
DISPOSABLE_DEVICE_FLAG = physical.DISPOSABLE_DEVICE_FLAG
FIXTURE_ALIAS = "LifeModuleNationalityOriginPhysicalE2E"
FIXTURE = (
    Path(__file__).resolve().parent
    / "fixtures/sr5-life-module-nationality-origin-physical-e2e.chum5"
)
OWNER_ID = "local-single-user"
DRAFT_SCHEMA = "chummer.origin_dossier.life_module_draft_checkpoint.v1"
PROVENANCE_SCHEMA = "chummer.origin_dossier.ltd_provenance.v1"
PROVENANCE_STATE = "not-requested"
PRESENTATION_ONLY = "presentation-only"
ACCEPTANCE_SCHEMA = "chummer.origin_dossier.life_module_accepted_decision_receipt.v1"
STEP_SCHEMA = "chummer.origin_dossier.life_module_decision_authority_step.v1"
TURN_SCHEMA = "chummer.origin_dossier.narrative_turn_seed.v1"
ARC_SCHEMA = "chummer.origin_dossier.story_arc_seed.v1"
JOURNEY_ID = "sr5-life-modules-foundation"
STAGE_ID = "nationality"
TERMINAL_STAGE_ID = "nationality-accepted"
ENTRY_RESOURCE_ID = "creation-stage-life-modules"
PAGE_RESOURCE_ID = "origin-life-decision"
LOWER_SHA256 = re.compile(r"^[0-9a-f]{64}$")

CHECKPOINT_FIELDS = {
    "schema", "ownerId", "workspaceId", "workspaceRevision", "projection",
    "timelineChapterDigests", "pendingPreview", "ltdProvenance",
    "boundSeedDigest", "boundDecisionGraphDigest", "boundContentDigest",
    "boundSourceDigest", "boundRulesDigest", "boundRuntimeDigest",
    "boundMechanicsSnapshotDigest", "checkpointDigest", "isUserOwnedDraft",
}
PROVENANCE_FIELDS = {
    "schema", "state", "providerId", "providerModelId",
    "providerRouteReceiptDigest", "proposalDigest", "boundSeedDigest",
    "isVerified", "provenanceDigest", "mechanicsAuthority",
    "affectsMechanics",
}
ARC_FIELDS = {
    "schema", "arcSeedId", "currentTurn", "canonicalLayer",
    "visibleChapters", "allowedCanonicalFactIds", "allowedChoiceIds",
    "toneTags", "seedDigest",
}
TURN_REQUIRED_FIELDS = {
    "schema", "rulesetId", "workspaceId", "workspaceRevision", "ownerId",
    "runnerId", "runnerDisplayName", "locale", "journeyId", "stageId",
    "stageOrder", "turnId", "turnSequence", "visibleStoryMarkdown",
    "decisionPrompt", "legalChoices", "canonicalFacts",
    "acceptedDecisionIds", "previousTurnDigest", "decisionGraphDigest",
    "decisionDigest", "contentDigest", "sourceDigest", "rulesDigest",
    "runtimeDigest", "seedDigest", "storyEndsAtDecisionPoint",
    "mechanicsAuthority", "isTerminal",
}
CHOICE_REQUIRED_FIELDS = {
    "choiceId", "label", "source", "pageReference",
    "decisionCommandDigest", "mechanicsPreview", "mechanicsPreviewDigest",
    "sourceAnchorIds", "blockers", "isLegal", "choiceDigest",
    "withholdsContinuationUntilAccepted",
}
PREVIEW_REQUIRED_FIELDS = {
    "schema", "ownerId", "workspaceId", "workspaceRevision", "turnId",
    "visibleStoryMarkdown", "decisionPrompt", "selectedChoice",
    "ltdProvenance", "boundSeedDigest", "boundDecisionDigest",
    "boundMechanicsSnapshotDigest", "previewDigest",
    "requiresExplicitConfirmation", "includesFutureBranchText",
}
RECEIPT_FIELDS = {
    "Schema", "DecisionId", "ChoiceId", "DecisionCommandDigest",
    "IdempotencyKeyDigest", "PreviousWorkspaceRevision", "WorkspaceRevision",
    "PreviousContentDigest", "ContentDigest", "SourceDigest", "RulesDigest",
    "RuntimeDigest", "PreviousDecisionDigest",
    "PreviousMechanicsSnapshotDigest", "AcceptedDecisionGraphDigest",
    "MechanicsSnapshotDigest", "ConsequenceMarkdown", "CanonicalFacts",
    "ReceiptDigest",
}
NON_CLAIMS = [
    "not part of the Android aggregate",
    "no Google Play or public publication authorization",
    "no provider activation or external AI call",
    "no tablet support",
    "no generic or full editing parity",
    "no Life Module stage beyond Nationality",
]


@dataclass(frozen=True)
class JsonSnapshot:
    payload: dict[str, object]
    raw_sha256: str
    raw_bytes: bytes


@dataclass(frozen=True)
class InitialTurnAuthority:
    checkpoint: JsonSnapshot
    choice: dict[str, object]
    story: str
    prompt: str
    locale: str


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise RuntimeError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def strict_json_bytes(raw: bytes, label: str) -> dict[str, object]:
    try:
        value = json.loads(
            raw.decode("utf-8", errors="strict"),
            object_pairs_hook=reject_duplicate_keys,
            parse_constant=lambda token: (_ for _ in ()).throw(
                RuntimeError(f"{label} contains non-finite number {token}")
            ),
        )
    except (UnicodeError, json.JSONDecodeError, RuntimeError) as error:
        raise RuntimeError(f"{label} is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise RuntimeError(f"{label} must be one JSON object")
    return value


def require_object(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise RuntimeError(f"{label} is not an object")
    return value


def require_array(value: object, label: str) -> list[object]:
    if not isinstance(value, list):
        raise RuntimeError(f"{label} is not an array")
    return value


def require_exact_fields(
    value: dict[str, object], fields: set[str], label: str
) -> None:
    if set(value) != fields:
        raise RuntimeError(
            f"{label} fields are not exact: "
            f"missing={sorted(fields - set(value))!r}, "
            f"extra={sorted(set(value) - fields)!r}"
        )


def require_fields(value: dict[str, object], fields: set[str], label: str) -> None:
    missing = fields - set(value)
    if missing:
        raise RuntimeError(f"{label} is missing fields: {sorted(missing)!r}")


def require_digest(value: object, label: str) -> str:
    if not isinstance(value, str) or LOWER_SHA256.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not canonical lowercase SHA-256")
    return value


def require_nonempty_text(value: object, label: str) -> str:
    if (
        not isinstance(value, str)
        or not value.strip()
        or value != value.strip()
    ):
        raise RuntimeError(f"{label} is not one exact nonempty string")
    return value


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(
        value,
        allow_nan=False,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def fixed_digest_equal(left: object, right: object, label: str) -> None:
    left_value = require_digest(left, f"{label} left")
    right_value = require_digest(right, f"{label} right")
    if not hmac.compare_digest(left_value, right_value):
        raise RuntimeError(f"{label} differs")


def provenance_digest(provenance: dict[str, object]) -> str:
    return canonical_sha256({
        "schema": provenance["schema"],
        "state": provenance["state"],
        "providerId": provenance["providerId"],
        "providerModelId": provenance["providerModelId"],
        "providerRouteReceiptDigest": provenance["providerRouteReceiptDigest"],
        "proposalDigest": provenance["proposalDigest"],
        "boundSeedDigest": provenance["boundSeedDigest"],
        "isVerified": provenance["isVerified"],
    })


def checkpoint_digest(checkpoint: dict[str, object]) -> str:
    projection = require_object(checkpoint["projection"], "Origin projection")
    pending = checkpoint["pendingPreview"]
    pending_digest = ""
    if pending is not None:
        pending_digest = require_digest(
            require_object(pending, "Pending Origin preview")["previewDigest"],
            "Pending Origin preview digest",
        )
    provenance = require_object(checkpoint["ltdProvenance"], "LTD provenance")
    return canonical_sha256({
        "schema": checkpoint["schema"],
        "ownerId": checkpoint["ownerId"],
        "workspaceId": checkpoint["workspaceId"],
        "workspaceRevision": checkpoint["workspaceRevision"],
        "projectionSeedDigest": projection["seedDigest"],
        "timelineChapterDigests": checkpoint["timelineChapterDigests"],
        "pendingPreviewDigest": pending_digest,
        "provenanceDigest": provenance["provenanceDigest"],
        "boundSeedDigest": checkpoint["boundSeedDigest"],
        "boundDecisionGraphDigest": checkpoint["boundDecisionGraphDigest"],
        "boundContentDigest": checkpoint["boundContentDigest"],
        "boundSourceDigest": checkpoint["boundSourceDigest"],
        "boundRulesDigest": checkpoint["boundRulesDigest"],
        "boundRuntimeDigest": checkpoint["boundRuntimeDigest"],
        "boundMechanicsSnapshotDigest": checkpoint[
            "boundMechanicsSnapshotDigest"
        ],
    })


def validate_not_requested_provenance(
    value: object, bound_seed_digest: str
) -> dict[str, object]:
    provenance = require_object(value, "LTD provenance")
    require_exact_fields(provenance, PROVENANCE_FIELDS, "LTD provenance")
    exact = {
        "schema": PROVENANCE_SCHEMA,
        "state": PROVENANCE_STATE,
        "providerId": "",
        "providerModelId": "",
        "providerRouteReceiptDigest": "",
        "proposalDigest": "",
        "boundSeedDigest": bound_seed_digest,
        "isVerified": False,
        "mechanicsAuthority": PRESENTATION_ONLY,
        "affectsMechanics": False,
    }
    for key, expected in exact.items():
        if provenance[key] != expected or type(provenance[key]) is not type(expected):
            raise RuntimeError(f"LTD provenance {key} is not exact NotRequested state")
    observed = require_digest(provenance["provenanceDigest"], "LTD provenance digest")
    if observed != provenance_digest(provenance):
        raise RuntimeError("LTD provenance digest is not canonical")
    return provenance


def validate_choice(value: object) -> dict[str, object]:
    choice = require_object(value, "Life Module legal choice")
    require_fields(choice, CHOICE_REQUIRED_FIELDS, "Life Module legal choice")
    for key in ("choiceId", "label", "source"):
        require_nonempty_text(choice[key], f"Life Module choice {key}")
    for key in (
        "decisionCommandDigest", "mechanicsPreviewDigest", "choiceDigest"
    ):
        require_digest(choice[key], f"Life Module choice {key}")
    anchors = require_array(choice["sourceAnchorIds"], "Life Module source anchors")
    if (
        not anchors
        or any(not isinstance(item, str) or not item.strip() for item in anchors)
        or len(set(anchors)) != len(anchors)
    ):
        raise RuntimeError("Life Module source anchors are not exact and unique")
    if choice["blockers"] != [] or choice["isLegal"] is not True:
        raise RuntimeError("Life Module choice is not one unblocked legal choice")
    if choice["withholdsContinuationUntilAccepted"] is not True:
        raise RuntimeError("Life Module choice leaks continuation before acceptance")
    mechanics = require_object(choice["mechanicsPreview"], "Mechanics preview")
    if mechanics.get("karmaIsExact") is not True:
        raise RuntimeError("Life Module mechanics cost is not exact")
    mechanics_anchors = require_array(
        mechanics.get("sourceAnchorIds"), "Mechanics source anchors"
    )
    if not mechanics_anchors or any(anchor not in anchors for anchor in mechanics_anchors):
        raise RuntimeError("Mechanics source anchors are absent from choice authority")
    if mechanics.get("previewDigest") != choice["mechanicsPreviewDigest"]:
        raise RuntimeError("Choice mechanics preview digest differs from its authority")
    return choice


def validate_initial_checkpoint(
    snapshot: JsonSnapshot,
    workspace_id: str,
    workspace_revision: int,
    payload_sha256: str,
) -> InitialTurnAuthority:
    checkpoint = snapshot.payload
    require_exact_fields(checkpoint, CHECKPOINT_FIELDS, "Origin checkpoint")
    exact = {
        "schema": DRAFT_SCHEMA,
        "ownerId": OWNER_ID,
        "workspaceId": workspace_id,
        "workspaceRevision": workspace_revision,
        "isUserOwnedDraft": True,
    }
    for key, expected in exact.items():
        if checkpoint[key] != expected or type(checkpoint[key]) is not type(expected):
            raise RuntimeError(f"Origin checkpoint {key} is not exact")
    projection = require_object(checkpoint["projection"], "Origin projection")
    require_exact_fields(projection, ARC_FIELDS, "Origin projection")
    if projection["schema"] != ARC_SCHEMA:
        raise RuntimeError("Origin projection schema is not exact")
    projection_seed = require_digest(projection["seedDigest"], "Projection seed")
    fixed_digest_equal(checkpoint["boundSeedDigest"], projection_seed, "Bound seed")
    provenance = validate_not_requested_provenance(
        checkpoint["ltdProvenance"], projection_seed
    )
    if checkpoint["pendingPreview"] is not None:
        raise RuntimeError("Fresh Origin checkpoint unexpectedly contains a preview")
    if checkpoint["timelineChapterDigests"] != []:
        raise RuntimeError("Fresh Nationality turn unexpectedly contains prior chapters")
    turn = require_object(projection["currentTurn"], "Origin current turn")
    require_fields(turn, TURN_REQUIRED_FIELDS, "Origin current turn")
    expected_turn = {
        "schema": TURN_SCHEMA,
        "rulesetId": "sr5",
        "workspaceId": workspace_id,
        "workspaceRevision": workspace_revision,
        "ownerId": OWNER_ID,
        "journeyId": JOURNEY_ID,
        "stageId": STAGE_ID,
        "stageOrder": 1,
        "turnSequence": 1,
        "storyEndsAtDecisionPoint": True,
        "mechanicsAuthority": "accepted-life-module-decisions-only",
        "isTerminal": False,
    }
    for key, expected in expected_turn.items():
        if turn[key] != expected or type(turn[key]) is not type(expected):
            raise RuntimeError(f"Initial Nationality turn {key} is not exact")
    story = require_nonempty_text(turn["visibleStoryMarkdown"], "Visible story")
    prompt = require_nonempty_text(turn["decisionPrompt"], "Decision prompt")
    if not story.endswith(prompt):
        raise RuntimeError("Visible story does not end at the current decision prompt")
    if turn["contentDigest"] != payload_sha256:
        raise RuntimeError("Origin turn is not bound to the imported runner bytes")
    for key in (
        "previousTurnDigest", "decisionGraphDigest", "decisionDigest",
        "contentDigest", "sourceDigest", "rulesDigest", "runtimeDigest",
        "seedDigest",
    ):
        require_digest(turn[key], f"Origin current turn {key}")
    for checkpoint_key, turn_key in (
        ("boundDecisionGraphDigest", "decisionGraphDigest"),
        ("boundContentDigest", "contentDigest"),
        ("boundSourceDigest", "sourceDigest"),
        ("boundRulesDigest", "rulesDigest"),
        ("boundRuntimeDigest", "runtimeDigest"),
    ):
        fixed_digest_equal(checkpoint[checkpoint_key], turn[turn_key], checkpoint_key)
    canonical_layer = require_object(
        projection["canonicalLayer"], "Origin canonical layer"
    )
    fixed_digest_equal(
        checkpoint["boundMechanicsSnapshotDigest"],
        canonical_layer.get("mechanicsSnapshotDigest"),
        "Bound mechanics snapshot",
    )
    choices = require_array(turn["legalChoices"], "Origin legal choices")
    if not choices:
        raise RuntimeError("Initial Nationality turn has no legal choices")
    validated = [validate_choice(item) for item in choices]
    choice_ids = [str(item["choiceId"]) for item in validated]
    if choice_ids != sorted(choice_ids) or len(set(choice_ids)) != len(choice_ids):
        raise RuntimeError("Life Module choices are not uniquely canonical ordered")
    if projection["allowedChoiceIds"] != choice_ids:
        raise RuntimeError("Origin projection choice allowlist differs from legal choices")
    if turn["canonicalFacts"] != [] or turn["acceptedDecisionIds"] != []:
        raise RuntimeError("Initial Nationality turn contains accepted mechanics")
    observed_checkpoint_digest = require_digest(
        checkpoint["checkpointDigest"], "Origin checkpoint digest"
    )
    if observed_checkpoint_digest != checkpoint_digest(checkpoint):
        raise RuntimeError("Origin checkpoint digest is not canonical")
    if provenance["affectsMechanics"] is not False:
        raise RuntimeError("LTD provenance unexpectedly affects mechanics")
    locale = require_nonempty_text(turn["locale"], "Narrative locale")
    return InitialTurnAuthority(snapshot, validated[0], story, prompt, locale)


def validate_prepared_checkpoint(
    initial: InitialTurnAuthority,
    prepared: JsonSnapshot,
) -> dict[str, object]:
    value = prepared.payload
    require_exact_fields(value, CHECKPOINT_FIELDS, "Prepared Origin checkpoint")
    for key in (
        "ownerId", "workspaceId", "workspaceRevision", "projection",
        "timelineChapterDigests", "boundSeedDigest", "boundDecisionGraphDigest",
        "boundContentDigest", "boundSourceDigest", "boundRulesDigest",
        "boundRuntimeDigest", "boundMechanicsSnapshotDigest",
    ):
        if value[key] != initial.checkpoint.payload[key]:
            raise RuntimeError(f"Origin preview changed bound field {key}")
    projection_seed = require_digest(
        require_object(value["projection"], "Prepared projection")["seedDigest"],
        "Prepared projection seed",
    )
    validate_not_requested_provenance(value["ltdProvenance"], projection_seed)
    preview = require_object(value["pendingPreview"], "Pending Origin preview")
    require_fields(preview, PREVIEW_REQUIRED_FIELDS, "Pending Origin preview")
    if preview["requiresExplicitConfirmation"] is not True:
        raise RuntimeError("Origin preview does not require explicit confirmation")
    if preview["includesFutureBranchText"] is not False:
        raise RuntimeError("Origin preview includes future branch text")
    exact = {
        "ownerId": OWNER_ID,
        "workspaceId": value["workspaceId"],
        "workspaceRevision": value["workspaceRevision"],
        "visibleStoryMarkdown": initial.story,
        "decisionPrompt": initial.prompt,
        "boundSeedDigest": value["boundSeedDigest"],
        "boundDecisionDigest": require_object(
            value["projection"], "Prepared projection"
        )["currentTurn"]["decisionDigest"],
        "boundMechanicsSnapshotDigest": value["boundMechanicsSnapshotDigest"],
    }
    for key, expected in exact.items():
        if preview[key] != expected:
            raise RuntimeError(f"Origin preview {key} differs from reviewed turn")
    selected = require_object(preview["selectedChoice"], "Selected Origin choice")
    for key in (
        "choiceId", "label", "source", "pageReference", "mechanicsPreview",
        "sourceAnchorIds", "choiceDigest",
    ):
        if selected.get(key) != initial.choice[key]:
            raise RuntimeError(f"Selected Origin card changed choice field {key}")
    validate_not_requested_provenance(preview["ltdProvenance"], projection_seed)
    require_digest(preview["previewDigest"], "Origin preview digest")
    observed_checkpoint_digest = require_digest(
        value["checkpointDigest"], "Prepared checkpoint digest"
    )
    if observed_checkpoint_digest != checkpoint_digest(value):
        raise RuntimeError("Prepared Origin checkpoint digest is not canonical")
    return preview


def checkpoint_path(workspace_id: str) -> str:
    identity = f"{OWNER_ID}\0{workspace_id}".encode("utf-8")
    name = hashlib.sha256(identity).hexdigest() + ".json"
    return f"files/state/origin-dossier-drafts/{name}"


def read_checkpoint(device: shared.Device, workspace_id: str) -> JsonSnapshot:
    raw = device.run(
        "exec-out", "run-as", shared.PACKAGE, "cat", checkpoint_path(workspace_id)
    ).stdout
    if not isinstance(raw, bytes):
        raise RuntimeError("Origin checkpoint transport did not return raw bytes")
    return JsonSnapshot(
        strict_json_bytes(raw, "Origin checkpoint"),
        hashlib.sha256(raw).hexdigest(),
        raw,
    )


def require_checkpoint_absent(device: shared.Device, workspace_id: str) -> None:
    result = device.run(
        "shell", "run-as", shared.PACKAGE, "test", "!", "-e",
        checkpoint_path(workspace_id),
    )
    if result.returncode != 0:
        raise RuntimeError("Completed Origin draft checkpoint was not deleted")


def read_workspace_record(
    device: shared.Device, workspace_id: str
) -> JsonSnapshot:
    listing = device.shell(
        "run-as", shared.PACKAGE, "find", "files/state", "-type", "f",
        "-name", f"{workspace_id}.json",
    )
    paths = [line.strip() for line in listing.splitlines() if line.strip()]
    if len(paths) != 1:
        raise RuntimeError("Expected one exact app-private workspace record")
    raw = device.run(
        "exec-out", "run-as", shared.PACKAGE, "cat", paths[0]
    ).stdout
    if not isinstance(raw, bytes):
        raise RuntimeError("Workspace record transport did not return raw bytes")
    return JsonSnapshot(
        strict_json_bytes(raw, "Workspace record"),
        hashlib.sha256(raw).hexdigest(),
        raw,
    )


def validate_workspace_record_before(
    snapshot: JsonSnapshot,
    authority: shared.WorkspaceAuthority,
) -> None:
    record = snapshot.payload
    if record.get("ContentRevision") != authority.content_revision:
        raise RuntimeError("Workspace record content revision differs from UI authority")
    envelope = require_object(record.get("Envelope"), "Workspace envelope")
    if envelope.get("Payload") is None:
        raise RuntimeError("Workspace envelope has no character payload")
    payload = str(envelope["Payload"])
    if hashlib.sha256(payload.encode("utf-8")).hexdigest() != authority.payload_sha256:
        raise RuntimeError("Workspace record payload differs from UI authority")
    auxiliary = record.get("AuxiliaryState")
    if auxiliary is not None:
        acceptances = require_object(auxiliary, "Initial auxiliary state").get(
            "LifeModuleDecisionAcceptances"
        )
        if acceptances not in (None, []):
            raise RuntimeError("Initial workspace already contains an Origin acceptance")


def receipt_digest(receipt: dict[str, object]) -> str:
    canonical = dict(receipt)
    canonical["ReceiptDigest"] = ""
    return canonical_sha256(canonical)


def validate_acceptance_record(
    initial_record: JsonSnapshot,
    current_record: JsonSnapshot,
    imported: shared.WorkspaceAuthority,
    authority: InitialTurnAuthority,
) -> dict[str, object]:
    choice = authority.choice
    projection = require_object(
        authority.checkpoint.payload["projection"], "Initial Origin projection"
    )
    turn = require_object(projection["currentTurn"], "Initial Origin turn")
    record = current_record.payload
    if record.get("ContentRevision") != imported.content_revision + 1:
        raise RuntimeError("Origin confirm did not create exactly one successor revision")
    if record.get("Envelope") != initial_record.payload.get("Envelope"):
        raise RuntimeError("Nationality Origin acceptance changed raw character bytes")
    auxiliary = require_object(record.get("AuxiliaryState"), "Applied auxiliary state")
    if auxiliary.get("CharacterCreationFoundationDraft") is None:
        raise RuntimeError("Applied Origin acceptance has no Foundation draft authority")
    acceptances = require_array(
        auxiliary.get("LifeModuleDecisionAcceptances"),
        "Life Module acceptance ledger",
    )
    if len(acceptances) != 1:
        raise RuntimeError("Life Module acceptance mutation was replayed or omitted")
    acceptance = require_object(acceptances[0], "Life Module acceptance")
    if set(acceptance) != {"Receipt", "NextStep"}:
        raise RuntimeError("Life Module acceptance fields are not exact")
    receipt = require_object(acceptance["Receipt"], "Life Module receipt")
    require_exact_fields(receipt, RECEIPT_FIELDS, "Life Module receipt")
    exact_receipt = {
        "Schema": ACCEPTANCE_SCHEMA,
        "ChoiceId": choice["choiceId"],
        "DecisionCommandDigest": choice["decisionCommandDigest"],
        "PreviousWorkspaceRevision": imported.content_revision,
        "WorkspaceRevision": imported.content_revision + 1,
        "PreviousContentDigest": imported.payload_sha256,
        "ContentDigest": imported.payload_sha256,
        "SourceDigest": turn["sourceDigest"],
        "RulesDigest": turn["rulesDigest"],
        "RuntimeDigest": turn["runtimeDigest"],
        "PreviousDecisionDigest": turn["decisionDigest"],
        "PreviousMechanicsSnapshotDigest": authority.checkpoint.payload[
            "boundMechanicsSnapshotDigest"
        ],
    }
    for key, expected in exact_receipt.items():
        if receipt[key] != expected or type(receipt[key]) is not type(expected):
            raise RuntimeError(f"Life Module receipt {key} is not exact")
    for key in (
        "DecisionCommandDigest", "IdempotencyKeyDigest", "PreviousContentDigest",
        "ContentDigest", "SourceDigest", "RulesDigest", "RuntimeDigest",
        "PreviousDecisionDigest", "PreviousMechanicsSnapshotDigest",
        "AcceptedDecisionGraphDigest", "MechanicsSnapshotDigest",
        "ReceiptDigest",
    ):
        require_digest(receipt[key], f"Life Module receipt {key}")
    if receipt["ReceiptDigest"] != receipt_digest(receipt):
        raise RuntimeError("Life Module receipt digest is not canonical")
    if not require_nonempty_text(receipt["DecisionId"], "Origin decision identity"):
        raise RuntimeError("Origin decision identity is empty")
    require_nonempty_text(receipt["ConsequenceMarkdown"], "Origin consequence")
    facts = require_array(receipt["CanonicalFacts"], "Origin canonical facts")
    if not facts:
        raise RuntimeError("Origin receipt contains no canonical accepted fact")
    choice_anchors = set(require_array(choice["sourceAnchorIds"], "Choice anchors"))
    for value in facts:
        fact = require_object(value, "Origin fact")
        fact_anchors = require_array(fact.get("SourceAnchorIds"), "Origin fact anchors")
        if (
            fact.get("AcceptedDecisionId") != receipt["DecisionId"]
            or not fact_anchors
            or any(anchor not in choice_anchors for anchor in fact_anchors)
        ):
            raise RuntimeError("Origin receipt facts are not bound to decision/source")
    next_step = require_object(acceptance["NextStep"], "Origin terminal step")
    terminal_exact = {
        "Schema": STEP_SCHEMA,
        "RulesetId": "sr5",
        "WorkspaceId": imported.workspace_id,
        "WorkspaceRevision": imported.content_revision + 1,
        "JourneyId": JOURNEY_ID,
        "StageId": TERMINAL_STAGE_ID,
        "IsTerminal": True,
        "LegalChoices": [],
    }
    for key, expected in terminal_exact.items():
        if next_step.get(key) != expected:
            raise RuntimeError(f"Terminal Origin step {key} is not exact")
    bindings = {
        "DecisionGraphDigest": "AcceptedDecisionGraphDigest",
        "ContentDigest": "ContentDigest",
        "SourceDigest": "SourceDigest",
        "RulesDigest": "RulesDigest",
        "RuntimeDigest": "RuntimeDigest",
        "MechanicsSnapshotDigest": "MechanicsSnapshotDigest",
    }
    for step_key, receipt_key in bindings.items():
        if next_step.get(step_key) != receipt[receipt_key]:
            raise RuntimeError(f"Terminal Origin step {step_key} differs from receipt")
    return {
        "decisionId": receipt["DecisionId"],
        "choiceId": receipt["ChoiceId"],
        "receiptDigest": receipt["ReceiptDigest"],
        "previousWorkspaceRevision": receipt["PreviousWorkspaceRevision"],
        "workspaceRevision": receipt["WorkspaceRevision"],
        "previousContentDigest": receipt["PreviousContentDigest"],
        "contentDigest": receipt["ContentDigest"],
        "sourceDigest": receipt["SourceDigest"],
        "rulesDigest": receipt["RulesDigest"],
        "runtimeDigest": receipt["RuntimeDigest"],
        "mechanicsSnapshotDigest": receipt["MechanicsSnapshotDigest"],
        "canonicalFactCount": len(facts),
    }


def accessible_value(node: shared.UiNode, label: str) -> str:
    values = [
        node.attributes.get("text", "").strip(),
        node.attributes.get("content-desc", "").strip(),
    ]
    values = [value for value in values if value]
    if len(set(values)) != 1:
        if not values:
            raise RuntimeError(f"{label} has no readable accessibility value")
        if len(values) == 2 and values[0] != values[1]:
            return values[0]
    return values[0]


def require_text_node(
    device: shared.Device,
    resource_id: str,
    expected: str,
    *,
    scroll: bool = False,
) -> str:
    node = device.wait_for_single_exact_resource_id(
        resource_id,
        timeout=120,
        scroll=scroll,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
        evidence_prefix=resource_id,
        surface_name=f"Life Modules {resource_id}",
    )
    observed = accessible_value(node, resource_id)
    if observed != expected:
        raise RuntimeError(
            f"Life Modules {resource_id} text differs: "
            f"expected={expected!r}, observed={observed!r}"
        )
    return observed


BUDGET_PATTERNS = (
    re.compile(
        r"^Life Modules budget: (?P<total>-?[0-9]+(?:\.[0-9]+)?) total, "
        r"(?P<used>-?[0-9]+(?:\.[0-9]+)?) used, "
        r"(?P<remaining>-?[0-9]+(?:\.[0-9]+)?) remaining, unit (?P<unit>\S+)$"
    ),
    re.compile(
        r"^Lebensmodulbudget: (?P<total>-?[0-9]+(?:,[0-9]+)?) gesamt, "
        r"(?P<used>-?[0-9]+(?:,[0-9]+)?) verwendet, "
        r"(?P<remaining>-?[0-9]+(?:,[0-9]+)?) verbleibend, Einheit (?P<unit>\S+)$"
    ),
    re.compile(
        r"^Presupuesto de Módulos de vida: (?P<total>-?[0-9]+(?:,[0-9]+)?) total, "
        r"(?P<used>-?[0-9]+(?:,[0-9]+)?) usado, "
        r"(?P<remaining>-?[0-9]+(?:,[0-9]+)?) restante, unidad (?P<unit>\S+)$"
    ),
)


def parse_budget_semantic(value: str) -> dict[str, object]:
    match = None
    for pattern in BUDGET_PATTERNS:
        match = pattern.fullmatch(value)
        if match is not None:
            break
    if match is None:
        raise RuntimeError("Life Modules budget semantic is not exact DE/EN/ES copy")
    try:
        total = Decimal(match.group("total").replace(",", "."))
        used = Decimal(match.group("used").replace(",", "."))
        remaining = Decimal(match.group("remaining").replace(",", "."))
    except InvalidOperation as error:
        raise RuntimeError("Life Modules budget contains an invalid decimal") from error
    unit = match.group("unit")
    if total < 0 or used < 0 or remaining < 0 or total - used != remaining:
        raise RuntimeError("Life Modules total/used/remaining budget is inconsistent")
    return {
        "total": str(total),
        "used": str(used),
        "remaining": str(remaining),
        "unit": unit,
        "semantic": value,
    }


def require_home_has_no_origin_entry(device: shared.Device) -> dict[str, object]:
    shared.reset_scroll_to_top(device, swipes=16)
    seen: set[str] = set()
    stable = 0
    previous = ""
    for swipe in range(17):
        nodes = device.hierarchy()
        if not nodes:
            raise RuntimeError("Home hierarchy was empty during Origin-entry exclusion proof")
        digest = hashlib.sha256(
            "\0".join(
                sorted(
                    node.attributes.get("resource-id", "")
                    + "|" + node.attributes.get("content-desc", "")
                    + "|" + node.attributes.get("text", "")
                    for node in nodes
                )
            ).encode("utf-8")
        ).hexdigest()
        for node in nodes:
            resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            if resource_id:
                seen.add(resource_id)
        if PAGE_RESOURCE_ID in seen or ENTRY_RESOURCE_ID in seen:
            raise RuntimeError("Origin Life Module entry leaked onto starter Home")
        stable = stable + 1 if digest == previous else 0
        if stable >= 2:
            break
        previous = digest
        if swipe < 16:
            device.swipe_up(distance_ratio=0.18)
    else:
        raise RuntimeError("Starter Home Origin-exclusion scan did not reach a stable end")
    if "home-new-runner" not in seen or "home-open-file" not in seen:
        raise RuntimeError("Origin-exclusion scan was not bound to starter Home")
    return {"resourceIds": sorted(seen), "stableEndObservations": stable + 1}


def open_origin_turn(device: shared.Device) -> None:
    shared.open_creation_dashboard(device)
    device.tap_bidirectional(
        ENTRY_RESOURCE_ID,
        timeout=120,
        backward_scrolls=48,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait_for_single_exact_resource_id(
        PAGE_RESOURCE_ID,
        timeout=180,
        evidence_prefix="life-module-origin-page",
        surface_name="Life Modules Nationality Origin page",
    )


def observe_initial_turn(
    device: shared.Device,
    authority: InitialTurnAuthority,
) -> dict[str, object]:
    require_text_node(device, "origin-life-story", authority.story)
    require_text_node(device, "origin-life-prompt", authority.prompt, scroll=True)
    budget_node = device.wait_exact_resource_id_bidirectional(
        "origin-life-budget",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="origin-life-budget",
        surface_name="Exact Life Modules budget",
    )
    budget = parse_budget_semantic(accessible_value(budget_node, "Origin budget"))
    budget_metric_ids = []
    for metric_id in (
        "origin-life-budget-total",
        "origin-life-budget-used",
        "origin-life-budget-remaining",
    ):
        device.wait_for_single_exact_resource_id(
            metric_id,
            timeout=60,
            scroll=True,
            max_scrolls=12,
            scroll_distance_ratio=0.12,
            evidence_prefix=metric_id,
            surface_name=f"Exact Life Modules budget metric {metric_id}",
        )
        budget_metric_ids.append(metric_id)
    source = str(authority.choice["source"])
    page = str(authority.choice.get("pageReference") or "")
    expected_source = source if not page.strip() else f"{source} · {page}"
    observed_source = require_text_node(
        device, "origin-life-choice-source-0", expected_source, scroll=True
    )
    anchors = [str(value) for value in authority.choice["sourceAnchorIds"]]
    anchor_node = device.wait_exact_resource_id_bidirectional(
        "origin-life-choice-anchors-0",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="origin-choice-anchors",
        surface_name="Exact Life Module choice source anchors",
    )
    anchor_text = accessible_value(anchor_node, "Origin source anchors")
    expected_anchor_tail = ", ".join(anchors)
    if not anchor_text.endswith(expected_anchor_tail):
        raise RuntimeError("Visible Origin source anchors differ from Core choice authority")
    provenance_node = device.wait_exact_resource_id_bidirectional(
        "origin-life-ltd-provenance",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        evidence_prefix="origin-ltd-provenance",
        surface_name="Origin NotRequested provenance",
    )
    provenance_text = accessible_value(provenance_node, "Origin LTD provenance")
    if "Optional narrative extension off" not in provenance_text:
        raise RuntimeError("Origin UI does not expose NotRequested provider provenance")
    return {
        "visibleStory": authority.story,
        "decisionPrompt": authority.prompt,
        "locale": authority.locale,
        "selectedChoiceId": authority.choice["choiceId"],
        "choiceLabel": authority.choice["label"],
        "choiceSource": observed_source,
        "choiceSourceAnchorIds": anchors,
        "choiceSourceAnchorText": anchor_text,
        "budget": budget,
        "budgetMetricResourceIds": budget_metric_ids,
        "providerProvenance": PROVENANCE_STATE,
        "providerDisplay": provenance_text,
        "providerNetworkTransport": "absent-from-bound-origin-call-graph",
    }


def fixture_root(path: Path) -> ET.Element:
    root = ET.parse(path).getroot()
    expected = {
        "alias": FIXTURE_ALIAS,
        "metatype": "Human",
        "buildmethod": "LifeModules",
        "created": "False",
        "gameedition": "SR5",
        "settings": "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
    }
    for key, value in expected.items():
        if root.findtext(key) != value:
            raise RuntimeError(f"Physical Life Modules fixture {key} is not exact")
    return root


def prove_journey(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    home_exclusion = require_home_has_no_origin_entry(device)
    device.tap_single_exact_resource_id(
        "home-open-file",
        timeout=60,
        evidence_prefix="life-module-open-fixture",
        surface_name="Open exact Life Modules fixture",
    )
    shared.select_android_document(device, fixture.name)
    device.wait(FIXTURE_ALIAS, timeout=120)
    shared.wait_for_phone_runner_route(device, created=False, timeout=120)
    imported = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(imported, fixture_sha256)
    initial_record = read_workspace_record(device, imported.workspace_id)
    validate_workspace_record_before(initial_record, imported)

    open_origin_turn(device)
    initial_checkpoint = read_checkpoint(device, imported.workspace_id)
    initial_turn = validate_initial_checkpoint(
        initial_checkpoint,
        imported.workspace_id,
        imported.content_revision,
        imported.payload_sha256,
    )
    visible = observe_initial_turn(device, initial_turn)
    device.capture("life-module-nationality-origin-before-choice")

    device.tap_bidirectional(
        "origin-life-choice-0",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait_for_single_exact_resource_id(
        "origin-life-preview",
        timeout=120,
        scroll=True,
        max_scrolls=48,
        scroll_distance_ratio=0.18,
        evidence_prefix="origin-life-preview",
        surface_name="Life Module explicit preview",
    )
    prepared_checkpoint = read_checkpoint(device, imported.workspace_id)
    preview = validate_prepared_checkpoint(initial_turn, prepared_checkpoint)
    preview_record = read_workspace_record(device, imported.workspace_id)
    if preview_record.raw_bytes != initial_record.raw_bytes:
        raise RuntimeError("Origin preview mutated the workspace before explicit confirm")
    confirm = device.wait_exact_resource_id_bidirectional(
        "origin-life-confirm",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        evidence_prefix="origin-life-confirm",
        surface_name="Life Module explicit confirmation",
        require_tappable=True,
    )
    if confirm.attributes.get("enabled") != "true":
        raise RuntimeError("Life Module explicit confirmation is not enabled")
    device.capture("life-module-nationality-origin-explicit-preview")
    device.shell("input", "tap", *(str(value) for value in confirm.center))
    device.wait_for_single_exact_resource_id(
        "creation-wizard-dashboard",
        timeout=180,
        evidence_prefix="origin-life-confirmed-dashboard",
        surface_name="Creation dashboard after Origin confirmation",
    )

    require_checkpoint_absent(device, imported.workspace_id)
    applied_record = read_workspace_record(device, imported.workspace_id)
    receipt = validate_acceptance_record(
        initial_record, applied_record, imported, initial_turn
    )

    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    same_process_authority = shared.read_workspace_authority(device)
    if same_process_authority.workspace_id != imported.workspace_id:
        raise RuntimeError("Same-process Origin reopen changed workspace identity")
    open_origin_dashboard_record = read_workspace_record(device, imported.workspace_id)
    if open_origin_dashboard_record.raw_bytes != applied_record.raw_bytes:
        raise RuntimeError("Same-process reopen replayed the Origin mutation")
    shared.open_creation_dashboard(device)
    same_process_record = read_workspace_record(device, imported.workspace_id)
    if same_process_record.raw_bytes != applied_record.raw_bytes:
        raise RuntimeError("Creation-dashboard reopen replayed the Origin mutation")
    validate_acceptance_record(
        initial_record, same_process_record, imported, initial_turn
    )
    require_checkpoint_absent(device, imported.workspace_id)
    device.capture("life-module-nationality-origin-same-process-reopen")

    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=False, timeout=180)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    restored_authority = shared.read_workspace_authority(device)
    if restored_authority.workspace_id != imported.workspace_id:
        raise RuntimeError("Restarted Origin proof restored another workspace")
    restored_record = read_workspace_record(device, imported.workspace_id)
    if restored_record.raw_bytes != applied_record.raw_bytes:
        raise RuntimeError("Process restart changed or replayed Origin acceptance bytes")
    validate_acceptance_record(
        initial_record, restored_record, imported, initial_turn
    )
    require_checkpoint_absent(device, imported.workspace_id)
    shared.open_creation_dashboard(device)
    final_record = read_workspace_record(device, imported.workspace_id)
    if final_record.raw_bytes != applied_record.raw_bytes:
        raise RuntimeError("Post-restart dashboard reopen replayed Origin acceptance")
    device.capture("life-module-nationality-origin-new-process-recovery")

    return {
        "scope": {
            "buildMethod": "LifeModules",
            "stage": STAGE_ID,
            "claim": "one SR5 Nationality Origin turn only",
        },
        "starterHomeOriginEntryAbsent": home_exclusion,
        "importAuthority": shared.workspace_authority_json(imported),
        "visibleAuthority": visible,
        "initialCheckpointSha256": initial_checkpoint.raw_sha256,
        "preparedCheckpointSha256": prepared_checkpoint.raw_sha256,
        "previewDigest": preview["previewDigest"],
        "workspaceBeforeSha256": initial_record.raw_sha256,
        "workspaceAfterSha256": applied_record.raw_sha256,
        "sameProcessWorkspaceSha256": same_process_record.raw_sha256,
        "newProcessWorkspaceSha256": restored_record.raw_sha256,
        "foundationAcceptance": receipt,
        "mutationCount": 1,
        "processRestart": {
            "before": list(restart.before_force_stop.process_ids),
            "afterForceStop": list(restart.after_force_stop.process_ids),
            "restarted": list(restart.restarted.process_ids),
        },
    }


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(DISPOSABLE_DEVICE_FLAG, action="store_true")
    parser.add_argument("--creation-runner", type=Path, default=FIXTURE)
    return parser.parse_args(argv)


def execute(args: argparse.Namespace, context: dict[str, object]) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{DISPOSABLE_DEVICE_FLAG} is required because this journey installs "
            "the APK, clears app data, imports a runner and confirms one typed mutation"
        )
    if physical.SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe ASCII grammar")
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    apk = args.apk.resolve()
    fixture = args.creation_runner.resolve()
    if fixture != FIXTURE.resolve():
        raise RuntimeError("Life Modules proof requires the committed governed fixture")
    fixture_root(fixture)

    build_provenance = load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    repositories = require_object(
        build_provenance.get("repositories"), "Build provenance repositories"
    )
    artifact = require_object(build_provenance.get("artifact"), "Build provenance artifact")
    context["buildProvenance"] = build_provenance
    context["releaseEvidenceStatus"] = (
        "source-and-apk-bound-local-build-not-release-attested"
    )
    roots = physical.source_repository_roots(
        android_root=android_root, workspace_root=workspace_root
    )
    physical.validate_external_output_path(
        args.receipt,
        label="Receipt path",
        repository_roots=roots,
        expect_directory=False,
    )
    physical.validate_external_output_path(
        args.evidence,
        label="Evidence path",
        repository_roots=roots,
        expect_directory=True,
    )
    physical.validate_output_layout(receipt=args.receipt, evidence=args.evidence)

    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "physicalAuthorityDriverSha256": Path(physical.__file__).resolve(),
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "originPageSha256": android_root / "src/Chummer.Android/Native/OriginDossierLifeModuleDecisionPage.cs",
        "originRuntimeSha256": android_root / "src/Chummer.Android/Native/OriginDossierLifeModulePhoneRuntime.cs",
        "originStoreSha256": android_root / "src/Chummer.Android/Native/OriginDossierLifeModuleDraftStore.cs",
        "compositionSha256": android_root / "src/Chummer.Android/MauiProgram.cs",
        "originPresentationSha256": presentation_root / "Chummer.Presentation/OriginBooks/OriginDossierLifeModuleInteraction.cs",
        "foundationAuthoritySha256": core_root / "Chummer.Application/LifeModules/CharacterCreationFoundationLifeModuleDecisionAuthority.cs",
        "originServiceSha256": core_root / "Chummer.Application/LifeModules/LifeModuleOriginDossierService.cs",
        "originInteractionSha256": core_root / "Chummer.Application/LifeModules/LifeModuleOriginDossierInteractionService.cs",
        "originContractsSha256": core_root / "Chummer.Contracts/LifeModules/OriginDossierContracts.cs",
        "foundationApplySha256": core_root / "Chummer.Application/Characters/CharacterCreationFoundationDraftApplyAuthority.cs",
        "workspaceStoreSha256": core_root / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "lifeModulesCatalogSha256": core_root / "Chummer/data/lifemodules.xml",
        "settingsCatalogSha256": core_root / "Chummer/data/settings.xml",
        "fixtureSha256": fixture,
        "driverSha256": driver,
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Life Modules proof source graph is incomplete: {missing!r}")
    source_before = physical.source_graph_snapshot(
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
        expected_apk_sha256=str(artifact["sha256"]),
        expected_android_revision=str(
            require_object(repositories["android"], "Android identity")["commit"]
        ),
        expected_core_revision=str(
            require_object(repositories["core"], "Core identity")["commit"]
        ),
        expected_presentation_revision=str(
            require_object(repositories["presentation"], "Presentation identity")["commit"]
        ),
        source_paths=source_paths,
    )
    context["sourceGraphAuthority"] = source_before
    fixture_sha256 = str(
        require_object(source_before["sourceFileSha256"], "Source hashes")[
            "fixtureSha256"
        ]
    )
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    remote_fixture = f"/sdcard/Download/{fixture.name}"
    cleanup = {remote_fixture: False, shared.ADB_FILE_HIERARCHY_REMOTE_PATH: False}
    errors: list[str] = []
    journey: dict[str, object] | None = None
    observation: dict[str, object] | None = None
    verified_remote = ""
    device_validated = False
    try:
        device.require_transport_stability(expected_api_level="36")
        observation = physical.android_device_observation(device)
        if observation["serial"] != args.serial:
            raise RuntimeError("Physical observation differs from authorized ADB serial")
        context["deviceObservation"] = observation
        context["deviceAuthorization"] = {
            "mode": "explicit-command-line-opt-in",
            "flag": DISPOSABLE_DEVICE_FLAG,
            "serial": args.serial,
            "scope": JOURNEY,
            "destructiveActions": ["install-apk", "clear-app-data", "import-fixture", "confirm-one-origin-choice"],
        }
        device_validated = True
        for remote in cleanup:
            physical.remove_remote_temporary_file(device, remote)
        device.install_verified(apk, str(artifact["sha256"]), "--no-streaming", "-r")
        verified_remote = device.push_verified(fixture, remote_fixture, fixture_sha256)
        journey = prove_journey(device, fixture, fixture_sha256)
    except Exception as error:  # noqa: BLE001 - every failure belongs in receipt
        errors.append(f"journey failed: {type(error).__name__}: {error}")
    finally:
        if device_validated:
            for remote in cleanup:
                try:
                    physical.remove_remote_temporary_file(device, remote)
                    cleanup[remote] = True
                except Exception as error:  # noqa: BLE001 - cleanup is proof
                    errors.append(
                        f"cleanup failed for {remote}: {type(error).__name__}: {error}"
                    )
        context["adbTransport"] = device.transport_summary()
        try:
            source_after = physical.source_graph_snapshot(
                android_root=android_root,
                core_root=core_root,
                presentation_root=presentation_root,
                apk=apk,
                expected_apk_sha256=str(artifact["sha256"]),
                expected_android_revision=str(repositories["android"]["commit"]),
                expected_core_revision=str(repositories["core"]["commit"]),
                expected_presentation_revision=str(repositories["presentation"]["commit"]),
                source_paths=source_paths,
            )
            if source_after != source_before:
                errors.append("source/APK authority changed during execution")
        except Exception as error:  # noqa: BLE001 - TOCTOU check fails closed
            errors.append(
                f"source/APK authority recheck failed: {type(error).__name__}: {error}"
            )
    if errors:
        raise RuntimeError("; ".join(errors))
    if journey is None or observation is None or not all(cleanup.values()):
        raise RuntimeError("Physical journey, device observation, or cleanup is incomplete")
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "device-pass-source-bound",
        "executionStatus": "pass",
        "releaseEvidenceStatus": context["releaseEvidenceStatus"],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": JOURNEY,
        "apiLevel": observation["apiLevel"],
        "abi": observation["abi"],
        "package": shared.PACKAGE,
        "apkSha256": source_before["apkSha256"],
        "buildProvenance": build_provenance,
        "sourceGraphAuthority": source_before,
        "sourceGraphRecheckedAfterRun": True,
        "deviceAuthorization": context["deviceAuthorization"],
        "deviceObservation": observation,
        "fixtureSha256": fixture_sha256,
        "verifiedRemoteFixtureSha256": verified_remote,
        "remoteTemporaryFilesDeleted": cleanup,
        "adbTransport": context["adbTransport"],
        "authorityProofStages": journey,
        "gateRegistered": False,
        "aggregateJourneyCountContribution": 0,
        "publicationAuthorized": False,
        "releaseClaim": "none",
        "providerNetworkCallCount": 0,
        "providerNetworkAuthority": "no-provider-transport-in-bound-origin-call-graph",
        "nonClaims": NON_CLAIMS,
    }


def failure_receipt(
    args: argparse.Namespace,
    error: Exception,
    context: dict[str, object],
) -> dict[str, object]:
    return {
        **context,
        "schema": RECEIPT_SCHEMA,
        "status": "fail",
        "executionStatus": "fail",
        "releaseEvidenceStatus": context.get(
            "releaseEvidenceStatus", "manifest-not-verified"
        ),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": JOURNEY,
        "serial": args.serial,
        "gateRegistered": False,
        "aggregateJourneyCountContribution": 0,
        "publicationAuthorized": False,
        "releaseClaim": "none",
        "providerNetworkCallCount": 0,
        "providerNetworkAuthority": "no-provider-transport-in-bound-origin-call-graph",
        "nonClaims": NON_CLAIMS,
        "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
    }


def run_main(argv: list[str] | None = None) -> int:
    raw_args = list(sys.argv[1:] if argv is None else argv)
    if any(value in {"-h", "--help"} for value in raw_args):
        try:
            parse_args(["--help"])
        except SystemExit as error:
            return int(error.code or 0)
        return 0
    try:
        receipt_path = physical.locate_explicit_receipt(raw_args)
        physical.validate_external_output_path(
            receipt_path,
            label="Receipt path",
            repository_roots=physical.preparse_repository_roots(raw_args),
            expect_directory=False,
        )
        physical.prepare_receipt_target(receipt_path)
    except Exception as error:  # noqa: BLE001 - unsafe receipt receives no write
        print(f"Cannot prepare explicit receipt target: {error}", file=sys.stderr)
        return 2
    try:
        args = parse_args(raw_args)
    except SystemExit as error:
        return int(error.code or 0)
    if args.receipt != receipt_path:
        print("Parsed receipt path differs from pre-parsed target", file=sys.stderr)
        return 2
    context: dict[str, object] = {}
    try:
        receipt = execute(args, context)
    except Exception as error:  # noqa: BLE001 - stale pass never survives
        receipt = failure_receipt(args, error, context)
        physical.write_receipt_atomically(receipt_path, receipt)
        print(f"Physical Life Modules Origin E2E failed: {error}", file=sys.stderr)
        return 1
    physical.write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


def main(argv: list[str] | None = None) -> int:
    return run_main(argv)


if __name__ == "__main__":
    raise SystemExit(main())
