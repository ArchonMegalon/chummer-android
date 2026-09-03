#!/usr/bin/env python3
"""Prove one exact SR5 Before Run Edge action on a physical API 36 ARM64 phone.

Scope is deliberately narrow: this journey proves only the existing typed
``SpendEdge`` leaf.  It does not claim loadout, healing, preparation purchase,
contact, commitment, tablet, Play, After Run, or unrestricted editing parity.
"""

from __future__ import annotations

import argparse
import base64
import copy
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
from typing import TYPE_CHECKING
import uuid
import xml.etree.ElementTree as ET

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_editing_e2e as shared
import run_api36_sr5_career_attribute_wizard_e2e as physical

if TYPE_CHECKING:
    import api36_proof_state as proof_state


RECEIPT_SCHEMA = "chummer.android.sr5-before-run-edge-physical-e2e/v1"
JOURNEY = "sr5-before-run-edge-physical"
LANE = "before-run"
LANE_VALUE = 0
ACTION_KIND = 0  # Sr5TableWizardActionKind.SpendEdge
CHECKPOINT_KEY = "chummer.android.sr5-before-run.review.v1"
LANE_ROUTE = "sr5-career/before-run"
REVIEW_ROUTE = "sr5-career/before-run/review"
ACTION_ROUTE = "sr5-career-action-before-run"
FIXTURE_ALIAS = "Sr5BeforeRunEdgePhysicalE2E"
LOWER_SHA256 = re.compile(r"^[0-9a-f]{64}$")
TYPED_SHA256 = re.compile(r"^sha256:[0-9a-f]{64}$")
JOURNAL_FIELDS = {
    "SchemaVersion",
    "Version",
    "Phase",
    "OwnerId",
    "TransactionId",
    "IdempotencyKey",
    "Review",
    "Quote",
    "ExpectedPostconditionDigest",
    "Receipt",
    "JournalDigest",
}
RECEIPT_FIELDS = {
    "ContractName",
    "TransactionId",
    "IdempotencyKey",
    "WorkspaceId",
    "ExpectedWorkspaceRevision",
    "AppliedWorkspaceRevision",
    "ActionId",
    "ActionKind",
    "ActionDigest",
    "ExpectedPostconditionDigest",
    "ObservedPostconditionDigest",
    "ReceiptDigest",
}
RECEIPT_CONTRACT = "chummer.android.sr5-table-transaction-receipt/v1"
DISPOSABLE_DEVICE_FLAG = physical.DISPOSABLE_DEVICE_FLAG


@dataclass(frozen=True)
class TransactionSnapshot:
    payload: dict[str, object]
    serialized_sha256: str


@dataclass(frozen=True)
class LaneSpec:
    receipt_schema: str
    journey: str
    lane: str
    lane_value: int
    action_kind: int
    checkpoint_key: str
    lane_route: str
    review_route: str
    action_route: str
    fixture_alias: str
    fixture: Path
    representative_action: str
    excluded_scope: tuple[str, ...]
    expected_action_id: str
    successor_action_count: int
    source_paths: dict[str, Path]


SPEC = LaneSpec(
    receipt_schema=RECEIPT_SCHEMA,
    journey=JOURNEY,
    lane=LANE,
    lane_value=LANE_VALUE,
    action_kind=ACTION_KIND,
    checkpoint_key=CHECKPOINT_KEY,
    lane_route=LANE_ROUTE,
    review_route=REVIEW_ROUTE,
    action_route=ACTION_ROUTE,
    fixture_alias=FIXTURE_ALIAS,
    fixture=Path(__file__).resolve().parent
    / "fixtures/sr5-before-run-edge-physical-e2e.chum5",
    representative_action="Spend exactly one point of Edge use (EdgeUsed 0 -> 1)",
    excluded_scope=(
        "loadout",
        "preparation purchases",
        "healing",
        "contacts",
        "commitments",
        "tablet",
    ),
    expected_action_id="before-run.edge.spend",
    successor_action_count=2,
    source_paths={},
)

CHECKPOINT_FIELDS = {
    "Schema",
    "Lane",
    "WorkspaceId",
    "WorkspaceRevision",
    "SnapshotDigest",
    "SelectedAction",
}
IDENTITY_FIELDS = {
    "ActionId",
    "Lane",
    "Kind",
    "WeaponId",
    "AmmoSlot",
    "AmmoGearId",
    "TargetRevision",
    "FireMode",
    "ActionDigest",
}
QUOTE_FIELDS = {
    "Identity",
    "DisplayName",
    "EdgeUsedBefore",
    "EdgeUsedAfter",
    "WeaponPlan",
}
WEAPON_PLAN_FIELDS = {
    "Mode",
    "RoundsConsumed",
    "NewAmmoRemaining",
    "NewAmmoGearQuantity",
    "DeleteAmmoGear",
    "RequiresPartialConfirmation",
}
EMPTY_GUID = "00000000-0000-0000-0000-000000000000"
PLAYTIME_WEAPON_ID = "f1111111-1111-4111-8111-111111111111"
PLAYTIME_AMMO_GEAR_ID = "f2222222-2222-4222-8222-222222222222"
PLAYTIME_WEAPON_DISPLAY_NAME = "Playtime Short Burst Alpha"


def object_without_duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise RuntimeError(f"Duplicate JSON key in durable transaction: {key}")
        result[key] = value
    return result


def canonical_guid(value: object, label: str) -> str:
    if not isinstance(value, str):
        raise RuntimeError(f"{label} is not a canonical GUID string")
    try:
        parsed = uuid.UUID(value)
    except (AttributeError, ValueError) as error:
        raise RuntimeError(f"{label} is not a GUID") from error
    if parsed.int == 0 or str(parsed) != value:
        raise RuntimeError(f"{label} is not one canonical nonempty GUID")
    return value


def typed_digest(value: object, label: str) -> str:
    if not isinstance(value, str) or TYPED_SHA256.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not canonical typed SHA-256")
    return value


def require_object(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise RuntimeError(f"{label} is not an object")
    return value


def exact_typed_equal(actual: object, expected: object) -> bool:
    if type(actual) is not type(expected):
        return False
    if isinstance(expected, dict):
        return set(actual) == set(expected) and all(  # type: ignore[arg-type]
            exact_typed_equal(actual[key], value)  # type: ignore[index]
            for key, value in expected.items()
        )
    if isinstance(expected, list):
        return len(actual) == len(expected) and all(  # type: ignore[arg-type]
            exact_typed_equal(left, right)
            for left, right in zip(actual, expected, strict=True)  # type: ignore[arg-type]
        )
    return actual == expected


def length_prefixed_hash(*values: object) -> str:
    canonical = "".join(f"{len(str(value))}:{value};" for value in values)
    return "sha256:" + hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def json_bytes(value: object) -> bytes:
    """Match System.Text.Json's compact declared-property serialization for these ASCII DTOs."""
    return json.dumps(
        value,
        ensure_ascii=False,
        separators=(",", ":"),
    ).encode("utf-8")


def bytes_digest(value: bytes) -> str:
    return "sha256:" + hashlib.sha256(value).hexdigest()


def phase_name(phase: int) -> str:
    try:
        return ("Reviewed", "Applying", "Applied")[phase]
    except (IndexError, TypeError) as error:
        raise RuntimeError("Durable transaction phase is outside the typed enum") from error


def canonical_identity_json(identity_value: object) -> dict[str, object]:
    identity = require_object(identity_value, "Journal action identity")
    return {
        "ActionId": identity["ActionId"],
        "Lane": identity["Lane"],
        "Kind": identity["Kind"],
        "WeaponId": identity["WeaponId"],
        "AmmoSlot": identity["AmmoSlot"],
        "AmmoGearId": identity["AmmoGearId"],
        "TargetRevision": identity["TargetRevision"],
        "FireMode": identity["FireMode"],
        "ActionDigest": identity["ActionDigest"],
    }


def canonical_review_json(review_value: object) -> dict[str, object]:
    review = require_object(review_value, "Journal review")
    return {
        "Schema": review["Schema"],
        "Lane": review["Lane"],
        "WorkspaceId": review["WorkspaceId"],
        "WorkspaceRevision": review["WorkspaceRevision"],
        "SnapshotDigest": review["SnapshotDigest"],
        "SelectedAction": canonical_identity_json(review["SelectedAction"]),
    }


def canonical_quote_json(quote_value: object) -> dict[str, object]:
    quote = require_object(quote_value, "Journal quote")
    plan_value = quote["WeaponPlan"]
    plan = None
    if plan_value is not None:
        source = require_object(plan_value, "Journal Weapon plan")
        plan = {
            "Mode": source["Mode"],
            "RoundsConsumed": source["RoundsConsumed"],
            "NewAmmoRemaining": source["NewAmmoRemaining"],
            "NewAmmoGearQuantity": source["NewAmmoGearQuantity"],
            "DeleteAmmoGear": source["DeleteAmmoGear"],
            "RequiresPartialConfirmation": source["RequiresPartialConfirmation"],
        }
    return {
        "Identity": canonical_identity_json(quote["Identity"]),
        "DisplayName": quote["DisplayName"],
        "EdgeUsedBefore": quote["EdgeUsedBefore"],
        "EdgeUsedAfter": quote["EdgeUsedAfter"],
        "WeaponPlan": plan,
    }


def expected_journal_digest(transaction: dict[str, object]) -> str:
    receipt = transaction.get("Receipt")
    receipt_digest = receipt.get("ReceiptDigest", "") if isinstance(receipt, dict) else ""
    return length_prefixed_hash(
        "chummer.android.sr5-table-transaction-journal/v1",
        transaction["SchemaVersion"],
        transaction["Version"],
        phase_name(int(transaction["Phase"])),
        transaction["OwnerId"],
        transaction["TransactionId"],
        transaction["IdempotencyKey"],
        bytes_digest(json_bytes(canonical_review_json(transaction["Review"]))),
        bytes_digest(json_bytes(canonical_quote_json(transaction["Quote"]))),
        transaction["ExpectedPostconditionDigest"],
        receipt_digest,
    )


def expected_weapon_target_revision(ammo: int) -> str:
    payload = "\0".join(
        (
            "career-weapon-fire/v1",
            PLAYTIME_WEAPON_ID,
            "1",
            PLAYTIME_AMMO_GEAR_ID,
            str(ammo),
            str(ammo),
            "ShortBurst",
            "ShortBurst:3",
        )
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def action_automation_id(action_digest: str) -> str:
    """Reproduce Sr5TableWizardPage's digest-derived resource identity."""
    canonical = typed_digest(action_digest, "Successor action digest")
    return "sr5-table-action-" + canonical[7:19]


def expected_successor_action_contracts(
    spec: LaneSpec,
    successor_state: dict[str, object],
) -> dict[str, dict[str, object]]:
    """Derive the exact catalog authority from the saved successor state."""
    if spec.lane == "before-run":
        expected_fields = {"lane", "edgeUsed", "totalEdge"}
        if set(successor_state) != expected_fields:
            raise RuntimeError("Before Run successor state authority fields are not exact")
        if successor_state["lane"] != "before-run":
            raise RuntimeError("Before Run successor state authority has the wrong lane")
        edge_used = successor_state["edgeUsed"]
        total_edge = successor_state["totalEdge"]
        if type(edge_used) is not int or type(total_edge) is not int:
            raise RuntimeError("Before Run successor Edge state is not typed")
        if edge_used != 1 or total_edge != 4:
            raise RuntimeError("Before Run successor Edge state is not the exact saved delta")
        can_spend = edge_used < total_edge
        can_regain = edge_used > 0
        target_revision = length_prefixed_hash(
            "chummer.sr5_table_wizard.edge_target.v1",
            edge_used,
            total_edge,
            int(can_spend),
            int(can_regain),
        )
        result: dict[str, dict[str, object]] = {}
        successor_actions: list[tuple[str, str, int]] = []
        if can_spend:
            successor_actions.append(
                ("before-run.edge.spend", "SpendEdge", edge_used + 1)
            )
        if can_regain:
            successor_actions.append(
                ("before-run.edge.regain", "RegainEdge", edge_used - 1)
            )
        for action_id, kind, after in successor_actions:
            action_digest = length_prefixed_hash(
                "chummer.sr5_table_wizard.edge_action.v1",
                "BeforeRun",
                kind,
                action_id,
                target_revision,
                after,
            )
            result[action_id] = {
                "actionId": action_id,
                "kind": kind,
                "edgeUsedBefore": edge_used,
                "edgeUsedAfter": after,
                "totalEdge": total_edge,
                "targetRevision": target_revision,
                "actionDigest": action_digest,
                "automationId": action_automation_id(action_digest),
            }
        return result

    if spec.lane != "playtime":
        raise RuntimeError(f"Unsupported successor lane authority: {spec.lane}")
    expected_fields = {
        "lane",
        "weaponId",
        "ammoSlot",
        "ammoGearId",
        "displayName",
        "fireMode",
        "roundsConsumed",
        "ammoRemaining",
        "ammoGearQuantity",
    }
    if set(successor_state) != expected_fields:
        raise RuntimeError("Playtime successor state authority fields are not exact")
    expected_values = {
        "lane": "playtime",
        "weaponId": PLAYTIME_WEAPON_ID,
        "ammoSlot": 1,
        "ammoGearId": PLAYTIME_AMMO_GEAR_ID,
        "displayName": PLAYTIME_WEAPON_DISPLAY_NAME,
        "fireMode": "ShortBurst",
        "roundsConsumed": 3,
        "ammoRemaining": 8,
        "ammoGearQuantity": 8,
    }
    if not exact_typed_equal(successor_state, expected_values):
        raise RuntimeError("Playtime successor state is not the exact saved weapon authority")
    ammo_before = int(successor_state["ammoRemaining"])
    ammo_after = 5
    target_revision = expected_weapon_target_revision(ammo_before)
    action_digest = length_prefixed_hash(
        "chummer.sr5_table_wizard.weapon_action.v1",
        "Playtime",
        "playtime.weapon.fire",
        PLAYTIME_WEAPON_ID,
        1,
        PLAYTIME_AMMO_GEAR_ID,
        target_revision,
        PLAYTIME_WEAPON_DISPLAY_NAME,
        "ShortBurst",
        3,
        ammo_after,
        ammo_after,
        0,
        0,
    )
    return {
        "playtime.weapon.fire": {
            "actionId": "playtime.weapon.fire",
            "kind": "FireWeapon",
            "weaponId": PLAYTIME_WEAPON_ID,
            "ammoSlot": 1,
            "ammoGearId": PLAYTIME_AMMO_GEAR_ID,
            "fireMode": "ShortBurst",
            "roundsConsumed": 3,
            "ammoBefore": ammo_before,
            "ammoAfter": ammo_after,
            "targetRevision": target_revision,
            "actionDigest": action_digest,
            "automationId": action_automation_id(action_digest),
        }
    }


def expected_successor_action_ids(
    spec: LaneSpec,
    successor_state: dict[str, object],
) -> frozenset[str]:
    contracts = expected_successor_action_contracts(spec, successor_state)
    identities = frozenset(
        str(contract["automationId"]) for contract in contracts.values()
    )
    if len(identities) != len(contracts):
        raise RuntimeError("Successor action digests collide at the UI automation boundary")
    if len(identities) != spec.successor_action_count:
        raise RuntimeError("Lane successor cardinality disagrees with derived action authority")
    return identities


def expected_action_contract(
    spec: LaneSpec,
    workspace_id: str,
    workspace_revision: int,
) -> dict[str, object]:
    if spec.lane == "before-run":
        target_revision = length_prefixed_hash(
            "chummer.sr5_table_wizard.edge_target.v1",
            0,
            4,
            1,
            0,
        )
        action_digest = length_prefixed_hash(
            "chummer.sr5_table_wizard.edge_action.v1",
            "BeforeRun",
            "SpendEdge",
            spec.expected_action_id,
            target_revision,
            1,
        )
        identity = {
            "ActionId": spec.expected_action_id,
            "Lane": 0,
            "Kind": 0,
            "WeaponId": EMPTY_GUID,
            "AmmoSlot": 0,
            "AmmoGearId": EMPTY_GUID,
            "TargetRevision": target_revision,
            "FireMode": None,
            "ActionDigest": action_digest,
        }
        quote = {
            "Identity": identity,
            "DisplayName": "Spend 1 Edge",
            "EdgeUsedBefore": 0,
            "EdgeUsedAfter": 1,
            "WeaponPlan": None,
        }
        snapshot_digest = length_prefixed_hash(
            "chummer.sr5_table_wizard.snapshot.v1",
            "BeforeRun",
            workspace_id,
            workspace_revision,
            0,
            4,
            1,
            0,
            action_digest,
        )
        postcondition = length_prefixed_hash(
            "chummer.android.sr5-table-transaction-postcondition/v1",
            workspace_id,
            workspace_revision + 1,
            "SpendEdge",
            1,
        )
        return {
            "identity": identity,
            "quote": quote,
            "snapshotDigest": snapshot_digest,
            "postcondition": postcondition,
        }

    target_revision = expected_weapon_target_revision(11)
    weapon_plan = {
        "Mode": 1,
        "RoundsConsumed": 3,
        "NewAmmoRemaining": 8,
        "NewAmmoGearQuantity": 8,
        "DeleteAmmoGear": False,
        "RequiresPartialConfirmation": False,
    }
    action_digest = length_prefixed_hash(
        "chummer.sr5_table_wizard.weapon_action.v1",
        "Playtime",
        spec.expected_action_id,
        PLAYTIME_WEAPON_ID,
        1,
        PLAYTIME_AMMO_GEAR_ID,
        target_revision,
        PLAYTIME_WEAPON_DISPLAY_NAME,
        "ShortBurst",
        3,
        8,
        8,
        0,
        0,
    )
    identity = {
        "ActionId": spec.expected_action_id,
        "Lane": 1,
        "Kind": 2,
        "WeaponId": PLAYTIME_WEAPON_ID,
        "AmmoSlot": 1,
        "AmmoGearId": PLAYTIME_AMMO_GEAR_ID,
        "TargetRevision": target_revision,
        "FireMode": 1,
        "ActionDigest": action_digest,
    }
    quote = {
        "Identity": identity,
        "DisplayName": PLAYTIME_WEAPON_DISPLAY_NAME,
        "EdgeUsedBefore": 0,
        "EdgeUsedAfter": 0,
        "WeaponPlan": weapon_plan,
    }
    snapshot_digest = length_prefixed_hash(
        "chummer.sr5_table_wizard.snapshot.v1",
        "Playtime",
        workspace_id,
        workspace_revision,
        0,
        0,
        0,
        0,
        PLAYTIME_WEAPON_ID,
        1,
        PLAYTIME_AMMO_GEAR_ID,
        PLAYTIME_WEAPON_DISPLAY_NAME,
        target_revision,
        action_digest,
    )
    postcondition = length_prefixed_hash(
        "chummer.android.sr5-table-transaction-postcondition/v1",
        workspace_id,
        workspace_revision + 1,
        "FireWeapon",
        PLAYTIME_WEAPON_ID,
        1,
        PLAYTIME_AMMO_GEAR_ID,
        8,
        8,
    )
    return {
        "identity": identity,
        "quote": quote,
        "snapshotDigest": snapshot_digest,
        "postcondition": postcondition,
    }


def expected_receipt_digest(receipt: dict[str, object]) -> str:
    return length_prefixed_hash(
        "chummer.android.sr5-table-transaction-receipt-digest/v1",
        receipt["ContractName"],
        receipt["TransactionId"],
        receipt["IdempotencyKey"],
        receipt["WorkspaceId"],
        receipt["ExpectedWorkspaceRevision"],
        receipt["AppliedWorkspaceRevision"],
        receipt["ActionId"],
        ("SpendEdge", "RegainEdge", "FireWeapon")[int(receipt["ActionKind"])],
        receipt["ActionDigest"],
        receipt["ExpectedPostconditionDigest"],
        receipt["ObservedPostconditionDigest"],
    )


def read_transaction(
    device: shared.Device,
    checkpoint_key: str,
    *,
    required: bool = True,
) -> TransactionSnapshot | None:
    listing = device.shell(
        "run-as",
        shared.PACKAGE,
        "find",
        "shared_prefs",
        "-type",
        "f",
        "-name",
        "*.xml",
    )
    matches: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run(
            "exec-out", "run-as", shared.PACKAGE, "cat", path
        ).stdout
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as error:
            raise RuntimeError(f"Shared-preference file is malformed: {path}") from error
        matches.extend(
            element.text or ""
            for element in root.findall("string")
            if element.get("name") == checkpoint_key
        )
    if not matches:
        if required:
            raise RuntimeError(f"Durable table transaction is missing: {checkpoint_key}")
        return None
    if len(matches) != 1 or not matches[0]:
        raise RuntimeError(
            f"Expected one durable table transaction, got {len(matches)}"
        )
    try:
        raw_payload = base64.b64decode(matches[0], validate=True)
        if base64.b64encode(raw_payload).decode("ascii") != matches[0]:
            raise RuntimeError("Durable table transaction base64 is not canonical")
        payload = json.loads(
            raw_payload.decode("utf-8"),
            object_pairs_hook=object_without_duplicates,
        )
    except (UnicodeDecodeError, ValueError, json.JSONDecodeError) as error:
        raise RuntimeError("Durable table transaction is not canonical JSON") from error
    if not isinstance(payload, dict):
        raise RuntimeError("Durable table transaction is not an object")
    return TransactionSnapshot(
        payload,
        hashlib.sha256(raw_payload).hexdigest(),
    )


def validate_transaction(
    transaction: dict[str, object],
    *,
    spec: LaneSpec,
    workspace_id: str,
    expected_revision: int,
    phase: int,
    version: int,
    require_receipt: bool,
) -> dict[str, object] | None:
    if set(transaction) != JOURNAL_FIELDS:
        raise RuntimeError("Durable table transaction fields are not exact")
    if transaction["SchemaVersion"] != 1 or type(transaction["SchemaVersion"]) is not int:
        raise RuntimeError("Durable table transaction schema is not exact")
    if transaction["Phase"] != phase or type(transaction["Phase"]) is not int:
        raise RuntimeError("Durable table transaction phase is not exact")
    if transaction["Version"] != version or type(transaction["Version"]) is not int:
        raise RuntimeError("Durable table transaction CAS version is not exact")
    canonical_guid(transaction["OwnerId"], "Transaction owner")
    canonical_guid(transaction["TransactionId"], "Transaction identity")
    idempotency = typed_digest(transaction["IdempotencyKey"], "Idempotency key")

    review = require_object(transaction["Review"], "Typed table review")
    quote = require_object(transaction["Quote"], "Typed table quote")
    if set(review) != CHECKPOINT_FIELDS:
        raise RuntimeError("Typed table review fields are not exact")
    if set(quote) != QUOTE_FIELDS:
        raise RuntimeError("Typed table quote fields are not exact")
    identity = require_object(quote.get("Identity"), "Typed action identity")
    selected = require_object(review.get("SelectedAction"), "Reviewed action identity")
    if set(identity) != IDENTITY_FIELDS or set(selected) != IDENTITY_FIELDS:
        raise RuntimeError("Typed action identity fields are not exact")
    if not exact_typed_equal(identity, selected):
        raise RuntimeError("Durable review and quote do not bind the same typed action")
    if review.get("Schema") != "chummer.sr5_table_wizard.checkpoint.v1":
        raise RuntimeError("Typed table review schema is not exact")
    if review.get("WorkspaceId") != workspace_id:
        raise RuntimeError("Durable review workspace identity is not exact")
    if (
        review.get("WorkspaceRevision") != expected_revision
        or type(review.get("WorkspaceRevision")) is not int
    ):
        raise RuntimeError("Durable review workspace revision is not exact")
    if review.get("Lane") != spec.lane_value or type(review.get("Lane")) is not int:
        raise RuntimeError("Durable review lane is not exact")
    if identity.get("Kind") != spec.action_kind:
        raise RuntimeError("Durable review action kind exceeds this journey scope")
    contract = expected_action_contract(spec, workspace_id, expected_revision)
    expected_identity = require_object(contract["identity"], "Expected action identity")
    expected_quote = require_object(contract["quote"], "Expected typed quote")
    if not exact_typed_equal(identity, expected_identity):
        raise RuntimeError("Durable action identity is not the exact representative action")
    if not exact_typed_equal(selected, expected_identity):
        raise RuntimeError("Reviewed selected action is not the exact representative action")
    if spec.lane == "playtime":
        weapon_plan = require_object(quote.get("WeaponPlan"), "Typed Weapon plan")
        if set(weapon_plan) != WEAPON_PLAN_FIELDS:
            raise RuntimeError("Typed Weapon plan fields are not exact")
    if not exact_typed_equal(quote, expected_quote):
        raise RuntimeError("Durable typed quote or mutation delta is not exact")
    expected_snapshot = str(contract["snapshotDigest"])
    if review.get("SnapshotDigest") != expected_snapshot:
        raise RuntimeError("Reviewed snapshot digest is not exact")
    expected_postcondition = str(contract["postcondition"])
    if transaction["ExpectedPostconditionDigest"] != expected_postcondition:
        raise RuntimeError("Expected postcondition digest is not the exact reviewed delta")
    expected_idempotency = length_prefixed_hash(
        "chummer.android.sr5-table-transaction-idempotency/v1",
        transaction["OwnerId"],
        transaction["TransactionId"],
        workspace_id,
        expected_revision,
        expected_snapshot,
        expected_identity["ActionDigest"],
        expected_postcondition,
    )
    if idempotency != expected_idempotency:
        raise RuntimeError("Transaction idempotency digest is not cross-bound")
    action_id = str(expected_identity["ActionId"])
    action_digest = str(expected_identity["ActionDigest"])

    receipt_value = transaction["Receipt"]
    if not require_receipt:
        if receipt_value is not None:
            raise RuntimeError("Reviewed transaction unexpectedly contains a receipt")
        journal_digest = typed_digest(transaction["JournalDigest"], "Journal digest")
        if journal_digest != expected_journal_digest(transaction):
            raise RuntimeError("Reviewed journal digest is not computed from its exact bytes")
        return None
    if not isinstance(receipt_value, dict) or set(receipt_value) != RECEIPT_FIELDS:
        raise RuntimeError("Applied transaction receipt fields are not exact")
    receipt = receipt_value
    expected_values = {
        "ContractName": RECEIPT_CONTRACT,
        "TransactionId": transaction["TransactionId"],
        "IdempotencyKey": idempotency,
        "WorkspaceId": workspace_id,
        "ExpectedWorkspaceRevision": expected_revision,
        "AppliedWorkspaceRevision": expected_revision + 1,
        "ActionId": action_id,
        "ActionKind": spec.action_kind,
        "ActionDigest": action_digest,
        "ExpectedPostconditionDigest": expected_postcondition,
        "ObservedPostconditionDigest": expected_postcondition,
    }
    for field, expected in expected_values.items():
        if not exact_typed_equal(receipt.get(field), expected):
            raise RuntimeError(f"Applied receipt {field} is not exact")
    actual_digest = typed_digest(receipt.get("ReceiptDigest"), "Receipt digest")
    if actual_digest != expected_receipt_digest(receipt):
        raise RuntimeError("Applied receipt digest is not canonical")
    journal_digest = typed_digest(transaction["JournalDigest"], "Journal digest")
    if journal_digest != expected_journal_digest(transaction):
        raise RuntimeError("Applied journal digest is not computed from its exact bytes")
    return receipt


def require_same_review(
    reviewed: dict[str, object],
    applied: dict[str, object],
) -> None:
    mutable = {"Version", "Phase", "Receipt", "JournalDigest"}
    if (
        {key: value for key, value in reviewed.items() if key not in mutable}
        != {key: value for key, value in applied.items() if key not in mutable}
    ):
        raise RuntimeError("Applied transaction differs from the exact reviewed action")


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        try:
            raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def root_for_authority(
    device: shared.Device,
    authority: shared.WorkspaceAuthority,
    fixture_alias: str,
) -> ET.Element:
    matches = [
        payload
        for payload in workspace_payloads(device)
        if hashlib.sha256(payload.encode("utf-8")).hexdigest()
        == authority.payload_sha256
    ]
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected one payload bound to workspace authority, got {len(matches)}"
        )
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != fixture_alias:
        raise RuntimeError("Workspace authority selected a different Career fixture")
    return root


def require_before_run_fixture(root: ET.Element) -> None:
    expected = {
        "alias": FIXTURE_ALIAS,
        "metatype": "Human",
        "buildmethod": "Priority",
        "created": "True",
        "gameedition": "SR5",
        "edgeused": "0",
    }
    for field, value in expected.items():
        if root.findtext(field) != value:
            raise RuntimeError(f"Before Run fixture <{field}> is not exact")
    edge = [
        value
        for value in root.findall("./attributes/attribute")
        if value.findtext("name") == "EDG"
    ]
    if len(edge) != 1 or edge[0].findtext("totalvalue") != "4":
        raise RuntimeError("Before Run fixture has no exact Edge 4 authority")
    if root.findtext("./customstate/sentinel") != "before-run-unrelated-must-survive":
        raise RuntimeError("Before Run fixture sentinel is missing")


def assert_before_state(root: ET.Element) -> ET.Element:
    require_before_run_fixture(root)
    return copy.deepcopy(root)


def assert_after_state(
    root: ET.Element,
    before_authority: object,
) -> dict[str, object]:
    if not isinstance(before_authority, ET.Element):
        raise RuntimeError("Before Run exact pre-mutation XML authority is missing")
    if root.findtext("edgeused") != "1":
        raise RuntimeError("Before Run did not persist exactly one point of Edge use")
    edge = [
        value
        for value in root.findall("./attributes/attribute")
        if value.findtext("name") == "EDG"
    ]
    if len(edge) != 1 or edge[0].findtext("totalvalue") != "4":
        raise RuntimeError("Before Run changed total Edge authority")
    if root.findtext("./customstate/sentinel") != "before-run-unrelated-must-survive":
        raise RuntimeError("Before Run changed unrelated fixture XML")
    expected = copy.deepcopy(before_authority)
    edge_used = expected.findall("./edgeused")
    if len(edge_used) != 1:
        raise RuntimeError("Before Run pre-mutation XML has ambiguous Edge use")
    edge_used[0].text = "1"
    if ET.tostring(root, encoding="utf-8") != ET.tostring(expected, encoding="utf-8"):
        raise RuntimeError("Before Run changed XML outside the exact EdgeUsed 0 -> 1 delta")
    return {
        "lane": "before-run",
        "edgeUsed": 1,
        "totalEdge": 4,
    }


def prepare_runner(
    device: shared.Device,
    spec: LaneSpec,
    fixture_name: str,
    fixture_sha256: str,
    proof_expectation: proof_state.ProofBuildExpectation | None = None,
    proof_trace: list[dict[str, object]] | None = None,
) -> tuple[shared.LaunchState, shared.WorkspaceAuthority]:
    launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    shared.record_phone_ui_locale_evidence(device, evidence_prefix=spec.lane)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait(spec.fixture_alias, timeout=120)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    authority = (
        read_proof_workspace_authority(
            device,
            proof_expectation,
            proof_trace,
            label="imported-runner",
            page_automation_id="phone-runners",
            stage="runners-ready",
            wizard_lane=None,
        )
        if proof_expectation is not None and proof_trace is not None
        else shared.read_phone_workspace_authority(device)
    )
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def open_lane(device: shared.Device, spec: LaneSpec) -> None:
    physical.open_career_hub(device)
    device.tap_exact_resource_id_bidirectional(
        "sr5-career/table",
        timeout=90,
        backward_scrolls=0,
        forward_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="sr5-career-table-family",
        surface_name="SR5 Career table family route",
    )
    physical.wait_exact_route(device, "sr5-career/table", timeout=90)
    device.tap_exact_resource_id_bidirectional(
        spec.action_route,
        timeout=90,
        backward_scrolls=0,
        forward_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix=f"sr5-{spec.lane}-route",
        surface_name=f"SR5 {spec.lane} typed route",
    )
    physical.wait_exact_route(device, spec.lane_route, timeout=120)


def tap_unique_typed_action(device: shared.Device, spec: LaneSpec) -> str:
    prefix = "sr5-table-action-"
    for attempt in range(31):
        matches: dict[str, shared.UiNode] = {}
        for node in device.hierarchy():
            resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            if resource_id.startswith(prefix):
                matches[resource_id] = node
        if len(matches) > 1:
            device.capture(f"sr5-{spec.lane}-representative-action-ambiguous")
            raise RuntimeError(
                f"{spec.lane} fixture exposed {len(matches)} typed actions; expected one"
            )
        if len(matches) == 1:
            resource_id, node = next(iter(matches.items()))
            if not device.node_has_tappable_bounds(node):
                raise RuntimeError("The one representative typed action is not tappable")
            x, y = node.center
            device.shell("input", "tap", str(x), str(y))
            return resource_id
        if attempt < 30:
            device.swipe_up(distance_ratio=0.18)
            time.sleep(0.35)
    raise RuntimeError(f"No representative typed {spec.lane} action was rendered")


def observe_successor_actions(
    device: shared.Device,
    spec: LaneSpec,
    successor_state: dict[str, object],
) -> list[str]:
    """Read and exactly validate the successor catalog without selecting an action."""
    prefix = "sr5-table-action-"
    expected = expected_successor_action_ids(spec, successor_state)
    observed: set[str] = set()
    shared.reset_scroll_to_top(device, swipes=20)
    for attempt in range(31):
        visible: list[str] = []
        for node in device.hierarchy():
            raw_resource_id = node.attributes.get("resource-id", "")
            if not isinstance(raw_resource_id, str):
                raise RuntimeError("Successor action resource identity is not a string")
            resource_id = raw_resource_id.rsplit("/", 1)[-1]
            if resource_id.startswith(prefix):
                visible.append(resource_id)
        if len(visible) != len(set(visible)):
            device.capture(f"sr5-{spec.lane}-successor-action-duplicate")
            raise RuntimeError("Saved successor exposed a duplicate typed action identity")
        foreign = set(visible) - expected
        if foreign:
            device.capture(f"sr5-{spec.lane}-successor-action-foreign")
            raise RuntimeError(
                "Saved successor exposed foreign digest-derived action identities: "
                + ", ".join(sorted(foreign))
            )
        observed.update(visible)
        if attempt < 30:
            device.swipe_up(distance_ratio=0.18)
            time.sleep(0.35)
    if observed != expected:
        device.capture(f"sr5-{spec.lane}-successor-action-identity")
        missing = expected - observed
        extra = observed - expected
        raise RuntimeError(
            f"Saved {spec.lane} successor action identities are not exact; "
            f"missing={sorted(missing)}, extra={sorted(extra)}"
        )
    return sorted(observed)


def acknowledge_alert(device: shared.Device) -> None:
    device.tap("OK", timeout=180)


def append_proof_state(
    trace: list[dict[str, object]],
    label: str,
    snapshot: proof_state.ProofStateSnapshot,
) -> None:
    payload = snapshot.payload
    process_instance = str(payload["processInstanceId"])
    sequence = int(payload["sequence"])
    previous = trace[-1] if trace else None
    if (
        previous is not None
        and previous["processInstanceId"] == process_instance
        and int(previous["sequence"]) >= sequence
    ):
        raise RuntimeError("API-36 proof-state sequence did not advance in one process")
    trace.append({
        "label": label,
        "schema": payload["schema"],
        "serializedSha256": snapshot.serialized_sha256,
        "sequence": sequence,
        "processId": payload["processId"],
        "processInstanceId": process_instance,
        "surface": payload["surface"],
        "workspace": payload["workspace"],
        "transaction": payload["transaction"],
        "stateDigest": payload["stateDigest"],
    })


def read_proof_state(
    device: shared.Device,
    expectation: proof_state.ProofBuildExpectation,
    trace: list[dict[str, object]],
    *,
    label: str,
    page_automation_id: str,
    stage: str,
    wizard_lane: str | None,
) -> proof_state.ProofStateSnapshot:
    import api36_proof_state as proof_state_runtime

    snapshot = proof_state_runtime.wait_for_state(
        device,
        expected=expectation,
        page_automation_id=page_automation_id,
        stage=stage,
        wizard_lane=wizard_lane,
    )
    append_proof_state(trace, label, snapshot)
    return snapshot


def read_proof_workspace_authority(
    device: shared.Device,
    expectation: proof_state.ProofBuildExpectation,
    trace: list[dict[str, object]],
    *,
    label: str,
    page_automation_id: str,
    stage: str,
    wizard_lane: str | None,
) -> shared.WorkspaceAuthority:
    snapshot = read_proof_state(
        device,
        expectation,
        trace,
        label=label,
        page_automation_id=page_automation_id,
        stage=stage,
        wizard_lane=wizard_lane,
    )
    workspace = snapshot.payload["workspace"]
    if not isinstance(workspace, dict):
        raise RuntimeError("API-36 proof state has no exact workspace authority")
    return shared.WorkspaceAuthority(
        str(workspace["workspaceId"]),
        int(workspace["contentRevision"]),
        int(workspace["savedRevision"]),
        str(workspace["payloadSha256"]),
        str(workspace["documentSha256"]),
    )


def prove_lane(
    device: shared.Device,
    spec: LaneSpec,
    fixture: Path,
    fixture_sha256: str,
    *,
    assert_before,
    assert_after,
    proof_expectation: proof_state.ProofBuildExpectation | None = None,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    proof_trace: list[dict[str, object]] = []
    initial_launch, imported = prepare_runner(
        device,
        spec,
        fixture.name,
        fixture_sha256,
        proof_expectation,
        proof_trace if proof_expectation is not None else None,
    )
    before_authority = assert_before(
        root_for_authority(device, imported, spec.fixture_alias)
    )

    open_lane(device, spec)
    if proof_expectation is not None:
        read_proof_state(
            device,
            proof_expectation,
            proof_trace,
            label="before-run-ready",
            page_automation_id=spec.lane_route,
            stage="ready",
            wizard_lane=spec.lane,
        )
    action_automation_id = tap_unique_typed_action(device, spec)
    device.wait("sr5-table-wizard-quote", timeout=90)
    if proof_expectation is not None:
        read_proof_state(
            device,
            proof_expectation,
            proof_trace,
            label="quote-ready",
            page_automation_id="sr5-table-wizard-quote",
            stage="quote-ready",
            wizard_lane=spec.lane,
        )
    device.tap_single_exact_resource_id(
        "sr5-table-wizard-open-review",
        timeout=90,
        evidence_prefix=f"sr5-{spec.lane}-open-review",
        surface_name=f"SR5 {spec.lane} durable review control",
    )
    physical.wait_exact_route(device, spec.review_route, timeout=90)
    reviewed_proof = (
        read_proof_state(
            device,
            proof_expectation,
            proof_trace,
            label="review-ready",
            page_automation_id=spec.review_route,
            stage="review-ready",
            wizard_lane=spec.lane,
        )
        if proof_expectation is not None
        else None
    )
    reviewed = read_transaction(device, spec.checkpoint_key)
    if reviewed is None:
        raise RuntimeError("Reviewed table transaction unexpectedly disappeared")
    validate_transaction(
        reviewed.payload,
        spec=spec,
        workspace_id=imported.workspace_id,
        expected_revision=imported.content_revision,
        phase=0,
        version=1,
        require_receipt=False,
    )
    if reviewed_proof is not None:
        proof_transaction = reviewed_proof.payload["transaction"]
        if not isinstance(proof_transaction, dict) or (
            proof_transaction["phase"] != "reviewed"
            or proof_transaction["journalDigest"] != reviewed.payload["JournalDigest"]
            or proof_transaction["actionDigest"]
                != reviewed.payload["Quote"]["Identity"]["ActionDigest"]
        ):
            raise RuntimeError("Reviewed proof state does not match the durable transaction")
    device.capture(f"sr5-{spec.lane}-durable-review")

    review_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    restored_before_apply = (
        read_proof_workspace_authority(
            device,
            proof_expectation,
            proof_trace,
            label="review-restart-runner",
            page_automation_id="phone-runners",
            stage="runners-ready",
            wizard_lane=None,
        )
        if proof_expectation is not None
        else shared.read_phone_workspace_authority(device)
    )
    shared.require_restored_authority(imported, restored_before_apply)
    restarted_review = read_transaction(device, spec.checkpoint_key)
    if restarted_review != reviewed:
        raise RuntimeError("Reviewed transaction bytes changed across process restart")
    open_lane(device, spec)
    device.tap_single_exact_resource_id(
        "sr5-table-wizard-resume-review",
        timeout=90,
        evidence_prefix=f"sr5-{spec.lane}-resume-review",
        surface_name=f"SR5 {spec.lane} reviewed transaction resume control",
    )
    physical.wait_exact_route(device, spec.review_route, timeout=90)
    if proof_expectation is not None:
        resumed_proof = read_proof_state(
            device,
            proof_expectation,
            proof_trace,
            label="review-resumed",
            page_automation_id=spec.review_route,
            stage="review-ready",
            wizard_lane=spec.lane,
        )
        resumed_transaction = resumed_proof.payload["transaction"]
        if not isinstance(resumed_transaction, dict) or (
            resumed_transaction["resumeRestored"] is not True
            or resumed_transaction["journalDigest"] != reviewed.payload["JournalDigest"]
        ):
            raise RuntimeError("Restarted proof state did not restore the reviewed transaction")
    device.capture(f"sr5-{spec.lane}-resumed-review")
    device.tap_single_exact_resource_id(
        "sr5-table-wizard-confirm",
        timeout=120,
        evidence_prefix=f"sr5-{spec.lane}-confirm",
        surface_name=f"SR5 {spec.lane} exact typed apply control",
    )
    acknowledge_alert(device)
    device.wait("sr5-table-wizard-receipt", timeout=180)
    applied_proof = (
        read_proof_state(
            device,
            proof_expectation,
            proof_trace,
            label="receipt-ready",
            page_automation_id=spec.lane_route,
            stage="receipt-ready",
            wizard_lane=spec.lane,
        )
        if proof_expectation is not None
        else None
    )
    applied = read_transaction(device, spec.checkpoint_key)
    if applied is None:
        raise RuntimeError("Applied table transaction unexpectedly disappeared")
    receipt = validate_transaction(
        applied.payload,
        spec=spec,
        workspace_id=imported.workspace_id,
        expected_revision=imported.content_revision,
        phase=2,
        version=3,
        require_receipt=True,
    )
    if receipt is None:
        raise RuntimeError("Applied table receipt is missing")
    if applied_proof is not None:
        proof_transaction = applied_proof.payload["transaction"]
        if not isinstance(proof_transaction, dict) or (
            proof_transaction["phase"] != "applied"
            or proof_transaction["journalDigest"] != applied.payload["JournalDigest"]
            or proof_transaction["receiptDigest"] != receipt["ReceiptDigest"]
        ):
            raise RuntimeError("Applied proof state does not match the durable receipt")
    require_same_review(reviewed.payload, applied.payload)

    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    saved = (
        read_proof_workspace_authority(
            device,
            proof_expectation,
            proof_trace,
            label="saved-runner",
            page_automation_id="phone-runners",
            stage="runners-ready",
            wizard_lane=None,
        )
        if proof_expectation is not None
        else shared.read_phone_workspace_authority(device)
    )
    shared.require_saved_authority(saved)
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Typed table apply changed workspace identity")
    if saved.content_revision != imported.content_revision + 1:
        raise RuntimeError("Typed table apply did not save exactly revision +1")
    if saved.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("Typed table apply did not change the payload digest")
    assert_after(
        root_for_authority(device, saved, spec.fixture_alias),
        before_authority,
    )

    receipt_restart = shared.force_stop_and_launch_new_process(
        device, review_restart.restarted
    )
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    open_lane(device, spec)
    device.wait("sr5-table-wizard-receipt", timeout=180)
    if proof_expectation is not None:
        recovered_proof = read_proof_state(
            device,
            proof_expectation,
            proof_trace,
            label="receipt-recovered",
            page_automation_id=spec.lane_route,
            stage="receipt-ready",
            wizard_lane=spec.lane,
        )
    recovered = read_transaction(device, spec.checkpoint_key)
    if recovered != applied:
        raise RuntimeError("Recovered receipt bytes differ after process restart")
    recovered_receipt = validate_transaction(
        recovered.payload,
        spec=spec,
        workspace_id=imported.workspace_id,
        expected_revision=imported.content_revision,
        phase=2,
        version=3,
        require_receipt=True,
    )
    if recovered_receipt != receipt:
        raise RuntimeError("Recovered receipt identity differs after restart")
    if proof_expectation is not None:
        recovered_transaction = recovered_proof.payload["transaction"]
        if not isinstance(recovered_transaction, dict) or (
            recovered_transaction["receiptDigest"] != receipt["ReceiptDigest"]
        ):
            raise RuntimeError("Restarted proof state did not recover the exact receipt")
    device.capture(f"sr5-{spec.lane}-recovered-receipt")
    device.tap_single_exact_resource_id(
        "sr5-table-wizard-receipt-acknowledge",
        timeout=90,
        evidence_prefix=f"sr5-{spec.lane}-acknowledge",
        surface_name=f"SR5 {spec.lane} receipt acknowledgement",
    )
    if read_transaction(device, spec.checkpoint_key, required=False) is not None:
        raise RuntimeError("Acknowledged table receipt was not removed")

    final_restart = shared.force_stop_and_launch_new_process(
        device, receipt_restart.restarted
    )
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    final_saved = (
        read_proof_workspace_authority(
            device,
            proof_expectation,
            proof_trace,
            label="final-restored-runner",
            page_automation_id="phone-runners",
            stage="runners-ready",
            wizard_lane=None,
        )
        if proof_expectation is not None
        else shared.read_phone_workspace_authority(device)
    )
    shared.require_restored_authority(saved, final_saved)
    final_successor_state = assert_after(
        root_for_authority(device, final_saved, spec.fixture_alias),
        before_authority,
    )
    if not isinstance(final_successor_state, dict):
        raise RuntimeError("Saved successor did not emit typed action state authority")
    if read_transaction(device, spec.checkpoint_key, required=False) is not None:
        raise RuntimeError("Acknowledged receipt deletion did not survive restart")
    open_lane(device, spec)
    if proof_expectation is not None:
        read_proof_state(
            device,
            proof_expectation,
            proof_trace,
            label="saved-successor-ready",
            page_automation_id=spec.lane_route,
            stage="ready",
            wizard_lane=spec.lane,
        )
    successor_actions = observe_successor_actions(device, spec, final_successor_state)
    device.capture(f"sr5-{spec.lane}-saved-successor-reopened")
    result = {
        "scope": {
            "representativeAction": spec.representative_action,
            "excluded": list(spec.excluded_scope),
            "claim": "one representative typed action only",
        },
        "import": shared.workspace_authority_json(imported),
        "restoredBeforeApply": shared.workspace_authority_json(restored_before_apply),
        "savedSuccessor": shared.workspace_authority_json(saved),
        "finalRestoredSuccessor": shared.workspace_authority_json(final_saved),
        "actionAutomationId": action_automation_id,
        "successorActionAutomationIds": successor_actions,
        "successorActionAuthority": expected_successor_action_contracts(
            spec,
            final_successor_state,
        ),
        "reviewedTransactionSha256": reviewed.serialized_sha256,
        "appliedTransactionSha256": applied.serialized_sha256,
        "receipt": receipt,
        "restartProcessIds": [
            list(review_restart.restarted.process_ids),
            list(receipt_restart.restarted.process_ids),
            list(final_restart.restarted.process_ids),
        ],
    }
    if proof_expectation is not None:
        require_proof_process_transitions(proof_trace)
        result["api36ProofInstrumentation"] = {
            "schema": proof_trace[0]["schema"],
            "transport": "app-private-run-as-cat-read-only",
            "supplementalOnly": True,
            "blackBoxAssertionsRetained": True,
            "observations": proof_trace,
        }
    return result


def require_proof_process_transitions(trace: list[dict[str, object]]) -> None:
    by_label = {str(item["label"]): str(item["processInstanceId"]) for item in trace}
    expected_labels = {
        "imported-runner", "before-run-ready", "quote-ready", "review-ready",
        "review-restart-runner", "review-resumed", "receipt-ready", "saved-runner",
        "receipt-recovered", "final-restored-runner", "saved-successor-ready",
    }
    if set(by_label) != expected_labels:
        raise RuntimeError("API-36 proof-state process trace is incomplete")
    process_groups = (
        ("imported-runner", "before-run-ready", "quote-ready", "review-ready"),
        ("review-restart-runner", "review-resumed", "receipt-ready", "saved-runner"),
        ("receipt-recovered",),
        ("final-restored-runner", "saved-successor-ready"),
    )
    instances: list[str] = []
    for labels in process_groups:
        values = {by_label[label] for label in labels}
        if len(values) != 1:
            raise RuntimeError("API-36 proof state changed process identity without restart")
        instances.append(next(iter(values)))
    if len(set(instances)) != len(instances):
        raise RuntimeError("API-36 proof state did not rotate process identity after force-stop")


def default_source_paths(
    *,
    driver: Path,
    fixture: Path,
    android_root: Path,
    core_root: Path,
    presentation_root: Path,
    lane_specific: dict[str, Path],
) -> dict[str, Path]:
    return {
        "sharedPhysicalDriverSha256": Path(shared.__file__).resolve(),
        "sharedProvenanceHelperSha256": Path(load_and_verify_manifest.__code__.co_filename).resolve(),
        "careerWizardPageSha256": android_root / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs",
        "tableWizardPageSha256": android_root / "src/Chummer.Android/Native/Sr5TableWizardPage.cs",
        "tableWizardModelSha256": android_root / "src/Chummer.Android/Native/Sr5TableWizardPhoneModel.cs",
        "tableWizardTransactionSha256": android_root / "src/Chummer.Android/Native/Sr5TableWizardTypedTransaction.cs",
        "tableWizardAuthoritySha256": android_root / "src/Chummer.Android/Native/RunnerSessionSr5TableWizardPhoneAuthority.cs",
        "runnerCoordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "workspaceStoreSha256": core_root / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "fixtureSha256": fixture,
        "driverSha256": driver,
        **lane_specific,
    }


def execute_lane(
    args: argparse.Namespace,
    context: dict[str, object],
    *,
    spec: LaneSpec,
    driver: Path,
    fixture_validator,
    before_validator,
    after_validator,
    lane_source_paths,
) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{DISPOSABLE_DEVICE_FLAG} is required because this journey installs the APK, "
            "clears app data, imports a Career runner, and applies one typed mutation"
        )
    if physical.SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe ASCII grammar")
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    apk = args.apk.resolve()
    build_provenance = load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    repositories = require_object(build_provenance["repositories"], "Provenance repositories")
    artifact = require_object(build_provenance["artifact"], "Provenance artifact")
    context["buildProvenance"] = build_provenance
    context["releaseEvidenceStatus"] = "source-and-apk-bound-local-build-not-release-attested"
    repository_roots = physical.source_repository_roots(
        android_root=android_root,
        workspace_root=workspace_root,
    )
    physical.validate_external_output_path(
        args.receipt,
        label="Receipt path",
        repository_roots=repository_roots,
        expect_directory=False,
    )
    physical.validate_external_output_path(
        args.evidence,
        label="Evidence path",
        repository_roots=repository_roots,
        expect_directory=True,
    )
    physical.validate_output_layout(receipt=args.receipt, evidence=args.evidence)

    fixture = args.career_runner.resolve()
    fixture_name = physical.safe_fixture_basename(fixture)
    source_paths = default_source_paths(
        driver=driver,
        fixture=fixture,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        lane_specific=lane_source_paths(core_root, presentation_root),
    )
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"{spec.lane} source graph is incomplete: {missing!r}")
    source_before = physical.source_graph_snapshot(
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
        expected_apk_sha256=str(artifact["sha256"]),
        expected_android_revision=str(require_object(repositories["android"], "Android identity")["commit"]),
        expected_core_revision=str(require_object(repositories["core"], "Core identity")["commit"]),
        expected_presentation_revision=str(require_object(repositories["presentation"], "Presentation identity")["commit"]),
        source_paths=source_paths,
    )
    context["sourceGraphAuthority"] = source_before
    fixture_validator(ET.parse(fixture).getroot())
    source_hashes = require_object(source_before["sourceFileSha256"], "Source file hashes")
    fixture_sha256 = str(source_hashes["fixtureSha256"])
    remote_fixture = f"/sdcard/Download/{fixture_name}"
    temporary = [remote_fixture, "/sdcard/chummer-editing-window.xml"]
    cleanup = {path: False for path in temporary}
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence)
    errors: list[str] = []
    journey: dict[str, object] | None = None
    observation: dict[str, object] | None = None
    verified_remote = ""
    validated_device = False
    try:
        device.require_transport_stability(expected_api_level="36")
        observation = physical.android_device_observation(device)
        context["deviceObservation"] = observation
        validated_device = True
        for path in temporary:
            physical.remove_remote_temporary_file(device, path)
        device.install_verified(apk, str(artifact["sha256"]), "--no-streaming", "-r")
        verified_remote = device.push_verified(fixture, remote_fixture, fixture_sha256)
        journey = prove_lane(
            device,
            spec,
            fixture,
            fixture_sha256,
            assert_before=before_validator,
            assert_after=after_validator,
        )
    except Exception as error:  # noqa: BLE001 - all failures belong in the receipt
        errors.append(f"journey failed: {type(error).__name__}: {error}")
    finally:
        if validated_device:
            for path in temporary:
                try:
                    physical.remove_remote_temporary_file(device, path)
                    cleanup[path] = True
                except Exception as error:  # noqa: BLE001 - cleanup is evidence
                    errors.append(f"cleanup failed for {path}: {type(error).__name__}: {error}")
        context["adbTransport"] = device.transport_summary()
        try:
            source_after = physical.source_graph_snapshot(
                android_root=android_root,
                core_root=core_root,
                presentation_root=presentation_root,
                apk=apk,
                expected_apk_sha256=str(artifact["sha256"]),
                expected_android_revision=str(require_object(repositories["android"], "Android identity")["commit"]),
                expected_core_revision=str(require_object(repositories["core"], "Core identity")["commit"]),
                expected_presentation_revision=str(require_object(repositories["presentation"], "Presentation identity")["commit"]),
                source_paths=source_paths,
            )
            if source_after != source_before:
                errors.append("source/APK authority changed during execution")
        except Exception as error:  # noqa: BLE001 - TOCTOU check fails closed
            errors.append(f"source/APK authority recheck failed: {type(error).__name__}: {error}")
    if errors:
        raise RuntimeError("; ".join(errors))
    if journey is None or observation is None or not all(cleanup.values()):
        raise RuntimeError("Physical journey, device observation, or cleanup proof is incomplete")
    return {
        "schema": spec.receipt_schema,
        "status": "device-pass-source-bound",
        "executionStatus": "pass",
        "releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": spec.journey,
        "apiLevel": observation["apiLevel"],
        "abi": observation["abi"],
        "deviceObservation": observation,
        "buildProvenance": build_provenance,
        "sourceGraphAuthority": source_before,
        "sourceGraphRecheckedAfterRun": True,
        "apkSha256": source_before["apkSha256"],
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote,
        "remoteTemporaryFilesDeleted": cleanup,
        "adbTransport": context["adbTransport"],
        "authorityProofStages": journey,
        "scope": journey["scope"],
        "journeys": {
            "importExactCareerFixture": "pass",
            "persistDurableReview": "pass",
            "restartAndResumeReview": "pass",
            "applyRepresentativeTypedActionOnce": "pass",
            "verifySavedRevisionPlusOne": "pass",
            "restartAndRecoverExactReceipt": "pass",
            "acknowledgeReceipt": "pass",
            "restartAndReopenSavedSuccessor": "pass",
        },
    }


def before_run_source_paths(core_root: Path, presentation_root: Path) -> dict[str, Path]:
    return {
        "careerEdgeRequestSha256": presentation_root
        / "Chummer.Presentation/Overview/CareerEdgeUseEditRequest.cs",
        "careerEdgeRulesSha256": core_root
        / "Chummer.Contracts/Characters/CharacterCareerEdgeUseRules.cs",
        "presenterMutationSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
    }


def parse_args(argv: list[str] | None = None, *, spec: LaneSpec = SPEC) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(DISPOSABLE_DEVICE_FLAG, action="store_true")
    parser.add_argument("--career-runner", type=Path, default=spec.fixture)
    return parser.parse_args(argv)


def failure_receipt(
    args: argparse.Namespace,
    error: Exception,
    context: dict[str, object],
    spec: LaneSpec,
) -> dict[str, object]:
    return {
        "schema": spec.receipt_schema,
        "status": "fail",
        "executionStatus": "fail",
        "releaseEvidenceStatus": context.get("releaseEvidenceStatus", "manifest-not-verified"),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": spec.journey,
        "buildProvenance": context.get("buildProvenance"),
        "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
        **context,
    }


def run_main(
    argv: list[str] | None,
    *,
    spec: LaneSpec,
    driver: Path,
    fixture_validator,
    before_validator,
    after_validator,
    lane_source_paths,
) -> int:
    raw_args = list(sys.argv[1:] if argv is None else argv)
    if any(value in {"-h", "--help"} for value in raw_args):
        try:
            parse_args(["--help"], spec=spec)
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
    except Exception as error:  # noqa: BLE001 - unsafe receipt target gets no write
        print(f"Cannot prepare explicit receipt target: {error}", file=sys.stderr)
        return 2
    try:
        args = parse_args(raw_args, spec=spec)
    except SystemExit as error:
        return int(error.code or 0)
    if args.receipt != receipt_path:
        print("Parsed receipt path differs from pre-parsed target", file=sys.stderr)
        return 2
    context: dict[str, object] = {}
    try:
        receipt = execute_lane(
            args,
            context,
            spec=spec,
            driver=driver,
            fixture_validator=fixture_validator,
            before_validator=before_validator,
            after_validator=after_validator,
            lane_source_paths=lane_source_paths,
        )
    except Exception as error:  # noqa: BLE001 - stale pass must never survive
        receipt = failure_receipt(args, error, context, spec)
        physical.write_receipt_atomically(receipt_path, receipt)
        print(f"Physical {spec.journey} failed: {error}", file=sys.stderr)
        return 1
    physical.write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


def main(argv: list[str] | None = None) -> int:
    return run_main(
        argv,
        spec=SPEC,
        driver=Path(__file__).resolve(),
        fixture_validator=require_before_run_fixture,
        before_validator=assert_before_state,
        after_validator=assert_after_state,
        lane_source_paths=before_run_source_paths,
    )


if __name__ == "__main__":
    raise SystemExit(main())
