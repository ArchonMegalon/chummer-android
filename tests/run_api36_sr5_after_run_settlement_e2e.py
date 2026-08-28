#!/usr/bin/env python3
"""Prove one governed SR5 After Run settlement on a physical API 36 ARM64 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import html
import json
from pathlib import Path
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_sr5_career_active_skill_wizard_e2e as physical


SCHEMA = "chummer.android.sr5-after-run-settlement-physical-e2e/v1"
FIXTURE_SCHEMA = "chummer.android.sr5-after-run-settlement-fixture/v1"
CHECKPOINT_KEY = "sr5.after-run.settlement.checkpoint.v1"
ENTER_ROUTE = "sr5-career/after-run/settlement/enter"
CHOOSE_ROUTE = "sr5-career/after-run/settlement/choose"
REVIEW_ROUTE = "sr5-career/after-run/settlement/review"
RECEIPT_ROUTE = "sr5-career/after-run/settlement/receipt"
DISPOSABLE_DEVICE_FLAG = physical.DISPOSABLE_DEVICE_FLAG
DEFAULT_FIXTURE = (
    Path(__file__).resolve().parent / "fixtures/sr5-after-run-settlement-e2e.json"
)
FIXTURE_FIELDS = {
    "schema", "runner", "identity", "reward", "consequences", "policy",
    "contacts", "reviews", "expected",
}
RUNNER_FIELDS = {
    "fileName", "name", "alias", "settingsId", "streetCred", "notoriety",
    "publicAwareness", "karma", "nuyen", "heat", "customSentinel",
    "expectedSha256",
}
IDENTITY_FIELDS = {"proposalId", "runId", "characterId"}
REWARD_FIELDS = {
    "runTitle", "completedAtUtc", "karmaAward", "nuyenAward", "receiptSha256",
}
CONSEQUENCE_FIELDS = {
    "currentHeat", "heatDelta", "streetCredDelta", "notorietyDelta",
    "publicAwarenessDelta",
}
POLICY_FIELDS = {
    "maximumHeat", "maximumReputation", "maximumConnection", "maximumLoyalty",
    "karmaPerContactPoint", "allowRunRewardContacts",
    "allowKarmaPurchasedContacts", "calculatePublicAwareness",
}
CONTACT_FIELDS = {
    "contactId", "name", "role", "location", "connection", "loyalty", "kind",
}
REVIEW_FIELDS = {"actorId", "reviewId", "reason"}
EXPECTED_FIELDS = {
    "heatAfter", "streetCredAfter", "notorietyAfter", "publicAwarenessAfter",
    "contactKarmaCost", "karmaAfter", "contactsAdded",
}
CHECKPOINT_FIELDS = {
    "SchemaVersion", "Version", "RouteId", "Phase", "Draft", "Receipt",
    "IdempotencyKey",
}
IDENTITY_JSON_FIELDS = {"ProposalId", "RunId", "CharacterId"}
REWARD_CONTEXT_FIELDS = {
    "ContractName", "Identity", "RunTitle", "CompletedAt", "KarmaAward",
    "NuyenAward", "RewardReceiptDigest", "ContextDigest",
}
CANDIDATE_FIELDS = {"RewardContext", "Binding"}
BINDING_FIELDS = {
    "ContractName", "WorkspaceId", "WorkspaceRevision", "Identity", "Quote",
    "BindingDigest",
}
QUOTE_FIELDS = {
    "Identity", "HeatBefore", "HeatDelta", "HeatAfter", "StreetCredBefore",
    "StreetCredDelta", "StreetCredAfter", "NotorietyBefore", "NotorietyDelta",
    "NotorietyAfter", "PublicAwarenessBefore", "RequestedPublicAwarenessDelta",
    "PublicAwarenessAfter", "KarmaBefore", "ContactKarmaCost", "KarmaAfter",
    "Contacts", "GmReviewDigest", "OwnerReviewDigest", "Prerequisites",
    "CanSettle", "Blocker", "SourceDigest", "CustomDataDigest", "GmPolicyDigest",
    "RuntimeDigest", "LogicalDigest",
}
SETTLED_CONTACT_FIELDS = {
    "ContactId", "Name", "Role", "Location", "Connection", "Loyalty", "Kind",
    "KarmaCost",
}
PREREQUISITE_FIELDS = {"Prerequisite", "Satisfied", "Authority"}
PLAN_FIELDS = {
    "Identity", "TransactionId", "TargetHeat", "TargetStreetCred",
    "TargetNotoriety", "TargetPublicAwareness", "TargetKarma", "ContactKarmaCost",
    "ContactsToAdd", "ExpenseId", "ExpenseAmount", "ExpenseReason",
    "GmReviewDigest", "OwnerReviewDigest", "ExpectedSourceDigest",
    "ExpectedCustomDataDigest", "ExpectedGmPolicyDigest", "ExpectedRuntimeDigest",
    "ExpectedLogicalDigest", "PlanDigest",
}
DRAFT_FIELDS = {"OwnerId", "Candidate", "Plan", "Acknowledgements"}
ACKNOWLEDGEMENT_FIELDS = {
    "RunContextReviewed", "RewardsReviewed", "ConsequencesReviewed",
    "ContactsReviewed", "GmApprovalReviewed", "OwnerApprovalReviewed",
}
RECEIPT_FIELDS = {
    "TransactionId", "Identity", "HeatBefore", "HeatAfter", "StreetCredBefore",
    "StreetCredAfter", "NotorietyBefore", "NotorietyAfter",
    "PublicAwarenessBefore", "PublicAwarenessAfter", "KarmaBefore", "KarmaAfter",
    "ContactKarmaCost", "AddedContacts", "ExpenseId", "ExpenseAmount",
    "ExpenseReason", "GmReviewDigest", "OwnerReviewDigest", "SourceDigest",
    "CustomDataDigest", "GmPolicyDigest", "RuntimeDigest", "LogicalDigestBefore",
    "LogicalDigestAfter", "ReceiptDigest",
}
AFTER_RUN_CORE_CONTRACT = "chummer.core.sr5-after-run-settlement/v1"
AFTER_RUN_QUOTE_CONTRACT = "chummer.core.sr5-after-run-settlement-quote/v1"
AFTER_RUN_REWARD_CONTRACT = "chummer.android.sr5-after-run-reward-context/v1"
AFTER_RUN_MANUAL_CONTRACT = "chummer.android.sr5-manual-after-run-proposal/v1"
AFTER_RUN_ACTION_CONTRACT = "chummer.android.sr5-after-run-settlement-action/v1"
ASCII_TEXT = re.compile(r"^[A-Za-z0-9 .:#_-]{1,128}$")


def _mapping(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise RuntimeError(f"{label} must be one JSON object")
    return value


def _exact_fields(value: dict[str, object], expected: set[str], label: str) -> None:
    if set(value) != expected:
        raise RuntimeError(
            f"{label} fields are not exact: expected={sorted(expected)!r}, "
            f"actual={sorted(value)!r}"
        )


def _integer(value: object, label: str, *, minimum: int = 0) -> int:
    if type(value) is not int or value < minimum:
        raise RuntimeError(f"{label} must be an integer >= {minimum}")
    return value


def _text(value: object, label: str) -> str:
    if not isinstance(value, str) or ASCII_TEXT.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not bounded device-safe text")
    return value


def _sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or physical.LOWER_SHA256.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not canonical lowercase SHA-256")
    return value


def _canonical(*values: object) -> str:
    rendered = [str(value) for value in values]
    return "\0".join(f"{len(value)}:{value}" for value in rendered)


def _canonical_sha256(*values: object) -> str:
    return hashlib.sha256(_canonical(*values).encode("utf-8")).hexdigest()


def _manual_add(values: list[str]) -> str:
    return "".join(f"{len(value)}:{value}\n" for value in values)


def _dotnet_roundtrip_utc(value: str) -> str:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    return parsed.strftime("%Y-%m-%dT%H:%M:%S") + ".0000000+00:00"


def _dotnet_json_utc(value: str) -> str:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    return parsed.strftime("%Y-%m-%dT%H:%M:%S") + "+00:00"


def _identity_json(fixture: dict[str, object]) -> dict[str, str]:
    identity = _mapping(fixture["identity"], "fixture identity")
    return {
        "ProposalId": str(identity["proposalId"]),
        "RunId": str(identity["runId"]),
        "CharacterId": str(identity["characterId"]),
    }


def _identity_text(fixture: dict[str, object]) -> str:
    identity = _mapping(fixture["identity"], "fixture identity")
    return _canonical(
        identity["proposalId"], identity["runId"], identity["characterId"]
    )


def _settled_contacts(fixture: dict[str, object]) -> list[dict[str, object]]:
    policy = _mapping(fixture["policy"], "fixture policy")
    result: list[dict[str, object]] = []
    for contact in sorted(fixture["contacts"], key=lambda item: item["contactId"]):
        purchased = contact["kind"] == "Karma purchase"
        result.append({
            "ContactId": contact["contactId"],
            "Name": contact["name"],
            "Role": contact["role"],
            "Location": contact["location"],
            "Connection": contact["connection"],
            "Loyalty": contact["loyalty"],
            "Kind": 1 if purchased else 0,
            "KarmaCost": (
                (contact["connection"] + contact["loyalty"])
                * policy["karmaPerContactPoint"] if purchased else 0
            ),
        })
    return result


def _contacts_text(contacts: list[dict[str, object]]) -> str:
    return _canonical(*[
        _canonical(
            contact["ContactId"], contact["Name"], contact["Role"],
            contact["Location"], contact["Connection"], contact["Loyalty"],
            "RunReward" if contact["Kind"] == 0 else "KarmaPurchase",
            contact["KarmaCost"],
        )
        for contact in contacts
    ])


def _review_digest(fixture: dict[str, object], role: str) -> str:
    review = _mapping(_mapping(fixture["reviews"], "fixture reviews")[role], role)
    expected_role = "GameMaster" if role == "gm" else "CharacterOwner"
    return _canonical_sha256(
        AFTER_RUN_CORE_CONTRACT, "review", expected_role, review["actorId"],
        review["reviewId"], expected_role, review["actorId"], "Approved",
        review["reason"],
    )


def _manual_proposal_digest(
    fixture: dict[str, object], workspace_id: str, workspace_revision: int,
    character_projection_digest: str,
) -> str:
    identity = _mapping(fixture["identity"], "fixture identity")
    reward = _mapping(fixture["reward"], "fixture reward")
    consequences = _mapping(fixture["consequences"], "fixture consequences")
    policy = _mapping(fixture["policy"], "fixture policy")
    values = [
        AFTER_RUN_MANUAL_CONTRACT, workspace_id, str(workspace_revision),
        character_projection_digest, str(identity["proposalId"]), str(identity["runId"]),
        str(identity["characterId"]), str(reward["runTitle"]),
        _dotnet_roundtrip_utc(str(reward["completedAtUtc"])),
        str(reward["karmaAward"]), str(reward["nuyenAward"]),
        str(reward["receiptSha256"]), str(consequences["currentHeat"]),
        str(consequences["heatDelta"]), str(consequences["streetCredDelta"]),
        str(consequences["notorietyDelta"]), str(consequences["publicAwarenessDelta"]),
        str(policy["maximumHeat"]), str(policy["maximumReputation"]),
        str(policy["maximumConnection"]), str(policy["maximumLoyalty"]),
        str(policy["karmaPerContactPoint"]),
        str(policy["allowRunRewardContacts"]).lower(),
        str(policy["allowKarmaPurchasedContacts"]).lower(),
        str(policy["calculatePublicAwareness"]).lower(),
    ]
    for contact in sorted(fixture["contacts"], key=lambda item: item["contactId"]):
        values.extend([
            str(contact["contactId"]), str(contact["name"]), str(contact["role"]),
            str(contact["location"]), str(contact["connection"]),
            str(contact["loyalty"]),
            "RunReward" if contact["kind"] == "Run reward" else "KarmaPurchase",
        ])
    for role, enum_name in (("gm", "GameMaster"), ("owner", "CharacterOwner")):
        review = fixture["reviews"][role]
        values.extend([
            str(review["reviewId"]), enum_name, str(review["actorId"]), "Approved",
            str(review["reason"]),
        ])
    return hashlib.sha256(_manual_add(values).encode("utf-8")).hexdigest()


def _expected_after_run_authority(
    fixture: dict[str, object], *, workspace_id: str, workspace_revision: int,
    character_projection_digest: str, owner_id: str, transaction_id: str,
    phase: int, version: int,
) -> dict[str, object]:
    identity = _identity_json(fixture)
    identity_text = _identity_text(fixture)
    runner = fixture["runner"]
    reward = fixture["reward"]
    consequences = fixture["consequences"]
    policy = fixture["policy"]
    expected = fixture["expected"]
    contacts = _settled_contacts(fixture)
    contacts_text = _contacts_text(contacts)
    proposal_digest = _manual_proposal_digest(
        fixture, workspace_id, workspace_revision, character_projection_digest
    )
    raw_source = f"{AFTER_RUN_MANUAL_CONTRACT}/source/{proposal_digest}"
    raw_custom = f"{AFTER_RUN_MANUAL_CONTRACT}/custom-data/{proposal_digest}"
    raw_policy = f"{AFTER_RUN_MANUAL_CONTRACT}/gm-policy/{proposal_digest}"
    raw_runtime = f"{AFTER_RUN_MANUAL_CONTRACT}/runtime/{proposal_digest}"
    source_digest = hashlib.sha256(raw_source.encode()).hexdigest()
    custom_digest = hashlib.sha256(raw_custom.encode()).hexdigest()
    runtime_digest = hashlib.sha256(raw_runtime.encode()).hexdigest()
    policy_digest = _canonical_sha256(
        AFTER_RUN_CORE_CONTRACT, "gm-policy", policy["maximumHeat"],
        policy["maximumReputation"], policy["maximumConnection"],
        policy["maximumLoyalty"], policy["karmaPerContactPoint"],
        str(policy["allowRunRewardContacts"]),
        str(policy["allowKarmaPurchasedContacts"]),
        str(policy["calculatePublicAwareness"]), raw_policy,
    )
    gm_digest = _review_digest(fixture, "gm")
    owner_digest = _review_digest(fixture, "owner")
    prerequisite_names = [
        "CareerCharacter", "Sr5Ruleset", "ExactTarget", "ExactProjection",
        "RunCompleted", "ProposalUnsettled", "GmApproved", "OwnerApproved",
        "HeatWithinPolicy", "ReputationWithinPolicy", "ContactsWithinPolicy",
        "SufficientKarma",
    ]
    authorities = [
        "character.created", "ruleset.sr5",
        f"character.internal-id:{identity['CharacterId']}",
        "after-run.projection:exact", f"run.internal-id:{identity['RunId']}",
        f"after-run.proposal-id:{identity['ProposalId']}", "review.game-master",
        "review.character-owner", "gm-policy.maximum-heat",
        "gm-policy.maximum-reputation", "gm-policy.contact-acquisition",
        "character.karma",
    ]
    prerequisites = [
        {"Prerequisite": index, "Satisfied": True, "Authority": authority}
        for index, authority in enumerate(authorities)
    ]
    prerequisites_text = _canonical(*[
        _canonical(name, "True", authority)
        for name, authority in zip(prerequisite_names, authorities, strict=True)
    ])
    logical_digest = _canonical_sha256(
        AFTER_RUN_CORE_CONTRACT, "quote", identity_text,
        consequences["currentHeat"], consequences["heatDelta"], expected["heatAfter"],
        runner["streetCred"], consequences["streetCredDelta"],
        expected["streetCredAfter"], runner["notoriety"],
        consequences["notorietyDelta"], expected["notorietyAfter"],
        runner["publicAwareness"], consequences["publicAwarenessDelta"],
        expected["publicAwarenessAfter"], runner["karma"],
        expected["contactKarmaCost"], expected["karmaAfter"], contacts_text,
        gm_digest, owner_digest, prerequisites_text, "True", "None",
        source_digest, custom_digest, policy_digest, runtime_digest,
    )
    quote = {
        "Identity": identity, "HeatBefore": consequences["currentHeat"],
        "HeatDelta": consequences["heatDelta"], "HeatAfter": expected["heatAfter"],
        "StreetCredBefore": runner["streetCred"],
        "StreetCredDelta": consequences["streetCredDelta"],
        "StreetCredAfter": expected["streetCredAfter"],
        "NotorietyBefore": runner["notoriety"],
        "NotorietyDelta": consequences["notorietyDelta"],
        "NotorietyAfter": expected["notorietyAfter"],
        "PublicAwarenessBefore": runner["publicAwareness"],
        "RequestedPublicAwarenessDelta": consequences["publicAwarenessDelta"],
        "PublicAwarenessAfter": expected["publicAwarenessAfter"],
        "KarmaBefore": runner["karma"], "ContactKarmaCost": expected["contactKarmaCost"],
        "KarmaAfter": expected["karmaAfter"], "Contacts": contacts,
        "GmReviewDigest": gm_digest, "OwnerReviewDigest": owner_digest,
        "Prerequisites": prerequisites, "CanSettle": True, "Blocker": 0,
        "SourceDigest": source_digest, "CustomDataDigest": custom_digest,
        "GmPolicyDigest": policy_digest, "RuntimeDigest": runtime_digest,
        "LogicalDigest": logical_digest,
    }
    binding_digest = _canonical_sha256(
        AFTER_RUN_QUOTE_CONTRACT, workspace_id, workspace_revision, identity_text,
        source_digest, custom_digest, policy_digest, runtime_digest, logical_digest,
    )
    context_digest = hashlib.sha256("\0".join([
        AFTER_RUN_REWARD_CONTRACT, identity["ProposalId"], identity["RunId"],
        identity["CharacterId"], str(reward["runTitle"]),
        _dotnet_roundtrip_utc(str(reward["completedAtUtc"])),
        str(reward["karmaAward"]), str(reward["nuyenAward"]),
        str(reward["receiptSha256"]),
    ]).encode()).hexdigest()
    reward_context = {
        "ContractName": AFTER_RUN_REWARD_CONTRACT, "Identity": identity,
        "RunTitle": reward["runTitle"],
        "CompletedAt": _dotnet_json_utc(str(reward["completedAtUtc"])),
        "KarmaAward": reward["karmaAward"], "NuyenAward": reward["nuyenAward"],
        "RewardReceiptDigest": reward["receiptSha256"], "ContextDigest": context_digest,
    }
    expense_reason = f"After Run contacts ({len(contacts)})"
    plan_digest = _canonical_sha256(
        AFTER_RUN_CORE_CONTRACT, "plan", identity_text, transaction_id,
        expected["heatAfter"], expected["streetCredAfter"],
        expected["notorietyAfter"], expected["publicAwarenessAfter"],
        expected["karmaAfter"], expected["contactKarmaCost"], contacts_text,
        transaction_id, -expected["contactKarmaCost"], expense_reason,
        gm_digest, owner_digest, source_digest, custom_digest, policy_digest,
        runtime_digest, logical_digest,
    )
    plan = {
        "Identity": identity, "TransactionId": transaction_id,
        "TargetHeat": expected["heatAfter"],
        "TargetStreetCred": expected["streetCredAfter"],
        "TargetNotoriety": expected["notorietyAfter"],
        "TargetPublicAwareness": expected["publicAwarenessAfter"],
        "TargetKarma": expected["karmaAfter"],
        "ContactKarmaCost": expected["contactKarmaCost"],
        "ContactsToAdd": contacts, "ExpenseId": transaction_id,
        "ExpenseAmount": -expected["contactKarmaCost"],
        "ExpenseReason": expense_reason, "GmReviewDigest": gm_digest,
        "OwnerReviewDigest": owner_digest, "ExpectedSourceDigest": source_digest,
        "ExpectedCustomDataDigest": custom_digest,
        "ExpectedGmPolicyDigest": policy_digest,
        "ExpectedRuntimeDigest": runtime_digest,
        "ExpectedLogicalDigest": logical_digest, "PlanDigest": plan_digest,
    }
    idempotency = _canonical_sha256(
        AFTER_RUN_ACTION_CONTRACT, owner_id, workspace_id, workspace_revision,
        transaction_id,
        f"{identity['ProposalId']}:{identity['RunId']}:{identity['CharacterId']}",
        AFTER_RUN_QUOTE_CONTRACT, binding_digest, source_digest, custom_digest,
        policy_digest, runtime_digest, logical_digest, gm_digest, owner_digest,
        context_digest, plan_digest,
    )
    draft = {
        "OwnerId": owner_id,
        "Candidate": {"RewardContext": reward_context, "Binding": {
            "ContractName": AFTER_RUN_QUOTE_CONTRACT,
            "WorkspaceId": {"Value": workspace_id},
            "WorkspaceRevision": workspace_revision, "Identity": identity,
            "Quote": quote, "BindingDigest": binding_digest,
        }},
        "Plan": plan,
        "Acknowledgements": {field: True for field in ACKNOWLEDGEMENT_FIELDS},
    }
    receipt: dict[str, object] | None = None
    if phase == 2:
        post_logical = _canonical_sha256(
            AFTER_RUN_CORE_CONTRACT, "settled", identity_text, transaction_id,
            expected["heatAfter"], expected["streetCredAfter"],
            expected["notorietyAfter"], expected["publicAwarenessAfter"],
            expected["karmaAfter"], contacts_text, source_digest, custom_digest,
            policy_digest, runtime_digest,
        )
        receipt = {
            "TransactionId": transaction_id, "Identity": identity,
            "HeatBefore": consequences["currentHeat"], "HeatAfter": expected["heatAfter"],
            "StreetCredBefore": runner["streetCred"],
            "StreetCredAfter": expected["streetCredAfter"],
            "NotorietyBefore": runner["notoriety"],
            "NotorietyAfter": expected["notorietyAfter"],
            "PublicAwarenessBefore": runner["publicAwareness"],
            "PublicAwarenessAfter": expected["publicAwarenessAfter"],
            "KarmaBefore": runner["karma"], "KarmaAfter": expected["karmaAfter"],
            "ContactKarmaCost": expected["contactKarmaCost"],
            "AddedContacts": contacts, "ExpenseId": transaction_id,
            "ExpenseAmount": -expected["contactKarmaCost"],
            "ExpenseReason": expense_reason, "GmReviewDigest": gm_digest,
            "OwnerReviewDigest": owner_digest, "SourceDigest": source_digest,
            "CustomDataDigest": custom_digest, "GmPolicyDigest": policy_digest,
            "RuntimeDigest": runtime_digest, "LogicalDigestBefore": logical_digest,
            "LogicalDigestAfter": post_logical, "ReceiptDigest": "",
        }
        receipt["ReceiptDigest"] = _canonical_sha256(
            AFTER_RUN_CORE_CONTRACT, "receipt", transaction_id, identity_text,
            receipt["HeatBefore"], receipt["HeatAfter"], receipt["StreetCredBefore"],
            receipt["StreetCredAfter"], receipt["NotorietyBefore"],
            receipt["NotorietyAfter"], receipt["PublicAwarenessBefore"],
            receipt["PublicAwarenessAfter"], receipt["KarmaBefore"],
            receipt["KarmaAfter"], receipt["ContactKarmaCost"], contacts_text,
            receipt["ExpenseId"], receipt["ExpenseAmount"], receipt["ExpenseReason"],
            gm_digest, owner_digest, source_digest, custom_digest, policy_digest,
            runtime_digest, logical_digest, post_logical,
        )
    return {
        "SchemaVersion": 1, "Version": version, "RouteId": REVIEW_ROUTE,
        "Phase": phase, "Draft": draft, "Receipt": receipt,
        "IdempotencyKey": idempotency,
    }


def _strict_json(path: Path) -> dict[str, object]:
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 64 * 1024:
        raise RuntimeError("After Run fixture must be one bounded regular file")
    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=physical.object_without_duplicates,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        raise RuntimeError("After Run fixture is not strict UTF-8 JSON") from error
    return _mapping(value, "After Run fixture")


def validate_fixture(value: dict[str, object]) -> dict[str, object]:
    _exact_fields(value, FIXTURE_FIELDS, "After Run fixture")
    if value["schema"] != FIXTURE_SCHEMA:
        raise RuntimeError("After Run fixture schema is not exact")
    runner = _mapping(value["runner"], "runner")
    identity = _mapping(value["identity"], "identity")
    reward = _mapping(value["reward"], "reward")
    consequences = _mapping(value["consequences"], "consequences")
    policy = _mapping(value["policy"], "policy")
    reviews = _mapping(value["reviews"], "reviews")
    expected = _mapping(value["expected"], "expected")
    for actual, fields, label in (
        (runner, RUNNER_FIELDS, "runner"),
        (identity, IDENTITY_FIELDS, "identity"),
        (reward, REWARD_FIELDS, "reward"),
        (consequences, CONSEQUENCE_FIELDS, "consequences"),
        (policy, POLICY_FIELDS, "policy"),
        (reviews, {"gm", "owner"}, "reviews"),
        (expected, EXPECTED_FIELDS, "expected"),
    ):
        _exact_fields(actual, fields, label)
    if runner["fileName"] != "sr5-after-run-settlement-e2e.chum5":
        raise RuntimeError("After Run runner basename is not governed")
    for field in ("name", "alias", "customSentinel"):
        _text(runner[field], f"runner.{field}")
    physical.canonical_guid(runner["settingsId"], "runner settings identity")
    for field in ("streetCred", "notoriety", "publicAwareness", "karma", "nuyen", "heat"):
        _integer(runner[field], f"runner.{field}")
    _sha256(runner["expectedSha256"], "runner.expectedSha256")
    identities = [
        physical.canonical_guid(identity[field], f"identity.{field}")
        for field in sorted(IDENTITY_FIELDS)
    ]
    if len(set(identities)) != 3:
        raise RuntimeError("Proposal, run, and character identities must be distinct")
    _text(reward["runTitle"], "reward.runTitle")
    completed = reward["completedAtUtc"]
    try:
        parsed_completed = datetime.fromisoformat(str(completed).removesuffix("Z") + "+00:00")
    except ValueError as error:
        raise RuntimeError("reward.completedAtUtc is invalid") from error
    if not isinstance(completed, str) or not completed.endswith("Z") or parsed_completed.utcoffset() is None:
        raise RuntimeError("reward.completedAtUtc must be canonical UTC text")
    _integer(reward["karmaAward"], "reward.karmaAward")
    _integer(reward["nuyenAward"], "reward.nuyenAward")
    _sha256(reward["receiptSha256"], "reward.receiptSha256")
    for field in CONSEQUENCE_FIELDS:
        _integer(consequences[field], f"consequences.{field}", minimum=-1000)
    for field in (
        "maximumHeat", "maximumReputation", "maximumConnection", "maximumLoyalty",
        "karmaPerContactPoint",
    ):
        _integer(policy[field], f"policy.{field}")
    for field in (
        "allowRunRewardContacts", "allowKarmaPurchasedContacts",
        "calculatePublicAwareness",
    ):
        if type(policy[field]) is not bool:
            raise RuntimeError(f"policy.{field} must be Boolean")
    contacts = value["contacts"]
    if not isinstance(contacts, list) or len(contacts) != 2:
        raise RuntimeError("After Run fixture must contain two exact contact proposals")
    contact_ids: list[str] = []
    for index, raw_contact in enumerate(contacts):
        contact = _mapping(raw_contact, f"contacts[{index}]")
        _exact_fields(contact, CONTACT_FIELDS, f"contacts[{index}]")
        contact_ids.append(physical.canonical_guid(
            contact["contactId"], f"contacts[{index}].contactId"
        ))
        for field in ("name", "role", "location"):
            _text(contact[field], f"contacts[{index}].{field}")
        _integer(contact["connection"], f"contacts[{index}].connection", minimum=1)
        _integer(contact["loyalty"], f"contacts[{index}].loyalty", minimum=1)
        if contact["kind"] not in {"Run reward", "Karma purchase"}:
            raise RuntimeError("Contact kind is not exact")
    if len(set(contact_ids)) != 2 or {item["kind"] for item in contacts} != {
        "Run reward", "Karma purchase"
    }:
        raise RuntimeError("Fixture contact identities/origins are not exact")
    for role in ("gm", "owner"):
        review = _mapping(reviews[role], f"reviews.{role}")
        _exact_fields(review, REVIEW_FIELDS, f"reviews.{role}")
        _text(review["actorId"], f"reviews.{role}.actorId")
        physical.canonical_guid(review["reviewId"], f"reviews.{role}.reviewId")
        _text(review["reason"], f"reviews.{role}.reason")
    if reviews["gm"]["reviewId"] == reviews["owner"]["reviewId"]:
        raise RuntimeError("GM and owner review identities must be distinct")
    for field in EXPECTED_FIELDS:
        _integer(expected[field], f"expected.{field}")
    calculated = {
        "heatAfter": consequences["currentHeat"] + consequences["heatDelta"],
        "streetCredAfter": runner["streetCred"] + consequences["streetCredDelta"],
        "notorietyAfter": runner["notoriety"] + consequences["notorietyDelta"],
        "publicAwarenessAfter": runner["publicAwareness"] + consequences["publicAwarenessDelta"],
        "contactKarmaCost": sum(
            (item["connection"] + item["loyalty"]) * policy["karmaPerContactPoint"]
            for item in contacts if item["kind"] == "Karma purchase"
        ),
        "contactsAdded": len(contacts),
    }
    calculated["karmaAfter"] = runner["karma"] - calculated["contactKarmaCost"]
    if expected != calculated:
        raise RuntimeError("After Run expected projection is arithmetically incoherent")
    return value


def load_fixture(path: Path = DEFAULT_FIXTURE) -> dict[str, object]:
    return validate_fixture(_strict_json(path.resolve()))


def render_runner_xml(fixture: dict[str, object]) -> bytes:
    runner = _mapping(fixture["runner"], "runner")
    escaped = {key: html.escape(str(value), quote=False) for key, value in runner.items()}
    payload = (
        '<?xml version="1.0" encoding="utf-8"?>\n<character>\n'
        "  <created>True</created>\n"
        f"  <settings>{escaped['settingsId']}</settings>\n"
        "  <customdatadirectorynames />\n"
        f"  <name>{escaped['name']}</name>\n"
        f"  <alias>{escaped['alias']}</alias>\n"
        "  <playername>Local API 36 physical proof</playername>\n"
        "  <metatype>Human</metatype>\n  <buildmethod>Priority</buildmethod>\n"
        "  <createdversion>5.225.0</createdversion>\n  <appversion>5.225.0</appversion>\n"
        "  <gameedition>SR5</gameedition>\n"
        f"  <streetcred>{runner['streetCred']}</streetcred>\n"
        f"  <notoriety>{runner['notoriety']}</notoriety>\n"
        f"  <publicawareness>{runner['publicAwareness']}</publicawareness>\n"
        f"  <karma>{runner['karma']}</karma>\n  <nuyen>{runner['nuyen']}</nuyen>\n"
        f"  <heat>{runner['heat']}</heat>\n  <contacts />\n  <expenses />\n"
        "  <customstate>\n"
        f"    <sentinel>{escaped['customSentinel']}</sentinel>\n"
        "  </customstate>\n</character>\n"
    )
    return payload.encode("utf-8")


def materialize_runner(fixture: dict[str, object], evidence: Path) -> tuple[Path, str]:
    runner = _mapping(fixture["runner"], "runner")
    payload = render_runner_xml(fixture)
    digest = hashlib.sha256(payload).hexdigest()
    if digest != runner["expectedSha256"]:
        raise RuntimeError("Deterministic After Run runner bytes differ from fixture authority")
    target = evidence / str(runner["fileName"])
    physical.reject_symlink_components(target, label="Materialized runner")
    target.parent.mkdir(parents=True, exist_ok=True)
    physical.reject_symlink_components(target, label="Materialized runner")
    if target.exists() or target.is_symlink():
        raise RuntimeError("Materialized runner target is stale; use fresh evidence output")
    target.write_bytes(payload)
    if physical.shared.sha256(target) != digest:
        raise RuntimeError("Materialized After Run runner failed read-back verification")
    return target, digest


def _workspace_payloads(device: physical.shared.Device) -> list[str]:
    listing = device.shell("run-as", physical.shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        try:
            record = json.loads(device.run(
                "exec-out", "run-as", physical.shared.PACKAGE, "cat", path
            ).stdout)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.lstrip().startswith("<"):
            payloads.append(payload)
    return payloads


def root_for_authority(
    device: physical.shared.Device,
    authority: physical.shared.WorkspaceAuthority,
) -> ET.Element:
    matches = [
        payload for payload in _workspace_payloads(device)
        if hashlib.sha256(payload.encode("utf-8")).hexdigest() == authority.payload_sha256
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one exact After Run authority payload, got {len(matches)}")
    return ET.fromstring(matches[0])


def prepare_runner(
    device: physical.shared.Device,
    fixture: Path,
    fixture_sha256: str,
    alias: str,
) -> tuple[physical.shared.LaunchState, physical.shared.WorkspaceAuthority]:
    launch = physical.shared.launch_app(device)
    physical.shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    physical.shared.select_android_document(device, fixture.name)
    device.wait(alias, timeout=120)
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    physical.shared.tap_phone_destination(device, "phone-destination-runners")
    physical.shared.wait_for_phone_runners(device, timeout=120)
    authority = physical.shared.read_phone_workspace_authority(device)
    physical.shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def read_checkpoint(
    device: physical.shared.Device,
    *,
    required: bool = True,
) -> physical.CheckpointSnapshot | None:
    listing = device.shell(
        "run-as", physical.shared.PACKAGE, "find", "shared_prefs", "-type", "f",
        "-name", "*.xml",
    )
    matches: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run("exec-out", "run-as", physical.shared.PACKAGE, "cat", path).stdout
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as error:
            raise RuntimeError(f"Shared-preference file is malformed: {path}") from error
        matches.extend(
            element.text or "" for element in root.findall("string")
            if element.get("name") == CHECKPOINT_KEY
        )
    if not matches:
        if required:
            raise RuntimeError("Durable After Run checkpoint is missing")
        return None
    if len(matches) != 1 or not matches[0]:
        raise RuntimeError(f"Expected one durable After Run checkpoint, got {len(matches)}")
    serialized = matches[0]
    try:
        payload = json.loads(serialized, object_pairs_hook=physical.object_without_duplicates)
    except (json.JSONDecodeError, ValueError) as error:
        raise RuntimeError("Durable After Run checkpoint is not strict JSON") from error
    return physical.CheckpointSnapshot(
        _mapping(payload, "After Run checkpoint"),
        hashlib.sha256(serialized.encode("utf-8")).hexdigest(),
    )


def _workspace_value(value: object, label: str) -> str:
    if isinstance(value, str) and value:
        return value
    if isinstance(value, dict) and set(value) == {"Value"} and isinstance(value["Value"], str):
        return value["Value"]
    raise RuntimeError(f"{label} is not an exact workspace identity")


def _require_exact(actual: object, expected: object, label: str) -> None:
    if isinstance(expected, dict):
        mapped = _mapping(actual, label)
        _exact_fields(mapped, set(expected), label)
        for field, value in expected.items():
            _require_exact(mapped[field], value, f"{label}.{field}")
        return
    if isinstance(expected, list):
        if not isinstance(actual, list) or len(actual) != len(expected):
            raise RuntimeError(f"{label} list shape differs")
        for index, (actual_item, expected_item) in enumerate(zip(actual, expected, strict=True)):
            _require_exact(actual_item, expected_item, f"{label}[{index}]")
        return
    if type(actual) is not type(expected) or actual != expected:
        raise RuntimeError(f"{label} differs from exact product authority")


def validate_checkpoint(
    payload: dict[str, object],
    fixture: dict[str, object],
    *,
    workspace_id: str,
    workspace_revision: int,
    character_projection_digest: str,
    version: int,
    phase: int,
) -> dict[str, str]:
    _exact_fields(payload, CHECKPOINT_FIELDS, "After Run checkpoint")
    draft = _mapping(payload["Draft"], "checkpoint.Draft")
    _exact_fields(draft, DRAFT_FIELDS, "checkpoint.Draft")
    owner_id = physical.canonical_guid(draft["OwnerId"], "draft owner")
    plan = _mapping(draft["Plan"], "checkpoint plan")
    _exact_fields(plan, PLAN_FIELDS, "checkpoint plan")
    transaction_id = physical.canonical_guid(
        plan["TransactionId"], "settlement transaction"
    )
    _sha256(character_projection_digest, "character projection digest")
    if character_projection_digest != fixture["runner"]["expectedSha256"]:
        raise RuntimeError("After Run projection is not the governed runner document")
    expected_payload = _expected_after_run_authority(
        fixture, workspace_id=workspace_id, workspace_revision=workspace_revision,
        character_projection_digest=character_projection_digest, owner_id=owner_id,
        transaction_id=transaction_id, phase=phase, version=version,
    )
    _require_exact(payload, expected_payload, "After Run checkpoint")
    quote = expected_payload["Draft"]["Candidate"]["Binding"]["Quote"]
    receipt = expected_payload["Receipt"]
    return {
        "transactionId": transaction_id,
        "gmReviewDigest": str(quote["GmReviewDigest"]),
        "ownerReviewDigest": str(quote["OwnerReviewDigest"]),
        "receiptDigest": "" if receipt is None else str(receipt["ReceiptDigest"]),
    }


def require_same_draft(reviewed: dict[str, object], applied: dict[str, object]) -> None:
    if reviewed["Draft"] != applied["Draft"] or reviewed["IdempotencyKey"] != applied["IdempotencyKey"]:
        raise RuntimeError("Applied checkpoint does not preserve the exact reviewed draft")


def _set(device: physical.shared.Device, selector: str, label: str, value: object) -> None:
    device.set_text(
        selector, label, str(value), scroll=True, max_scrolls=48,
        scroll_distance_ratio=0.18,
    )


def _tap_exact(device: physical.shared.Device, selector: str) -> None:
    device.tap_bidirectional(
        selector, timeout=120, backward_scrolls=48, forward_scrolls=48,
        scroll_distance_ratio=0.18, exact_resource_id=True,
    )


def open_after_run(device: physical.shared.Device, expected_route: str) -> None:
    physical.shared.open_build(device, "phone")
    physical.shared.reset_scroll_to_top(device, swipes=18)
    _tap_exact(device, "build-sr5-career-wizard")
    physical.wait_exact_route(device, "sr5-career", timeout=90)
    device.tap_single_exact_resource_id(
        "sr5-career-action-after-run", timeout=120,
        evidence_prefix="sr5-after-run-action", surface_name="SR5 After Run action",
    )
    physical.wait_exact_route(device, expected_route, timeout=180)


def enter_manual_proposal(device: physical.shared.Device, fixture: dict[str, object]) -> None:
    identity = fixture["identity"]
    reward = fixture["reward"]
    consequences = fixture["consequences"]
    policy = fixture["policy"]
    reviews = fixture["reviews"]
    for field in (
        ("sr5-after-run-entry-proposal-id", "Proposal UUID", identity["proposalId"]),
        ("sr5-after-run-entry-run-id", "Run UUID", identity["runId"]),
        ("sr5-after-run-entry-character-id", "Character UUID", identity["characterId"]),
        ("sr5-after-run-entry-title", "Run title", reward["runTitle"]),
        ("sr5-after-run-entry-completed-at", "Completed at (ISO 8601)", reward["completedAtUtc"]),
    ):
        _set(device, *field)
    _tap_exact(device, "sr5-after-run-entry-target-owned")
    _tap_exact(device, "sr5-after-run-entry-completed")
    for field in (
        ("sr5-after-run-entry-karma-award", "Karma awarded", reward["karmaAward"]),
        ("sr5-after-run-entry-nuyen-award", "Nuyen awarded", reward["nuyenAward"]),
        ("sr5-after-run-entry-reward-digest", "Reward receipt SHA-256", reward["receiptSha256"]),
        ("sr5-after-run-entry-current-heat", "Current Heat", consequences["currentHeat"]),
        ("sr5-after-run-entry-heat-delta", "Heat delta", consequences["heatDelta"]),
        ("sr5-after-run-entry-street-cred-delta", "Street Cred delta", consequences["streetCredDelta"]),
        ("sr5-after-run-entry-notoriety-delta", "Notoriety delta", consequences["notorietyDelta"]),
        ("sr5-after-run-entry-public-awareness-delta", "Public Awareness delta", consequences["publicAwarenessDelta"]),
        ("sr5-after-run-entry-maximum-heat", "Maximum Heat", policy["maximumHeat"]),
        ("sr5-after-run-entry-maximum-reputation", "Maximum reputation", policy["maximumReputation"]),
        ("sr5-after-run-entry-maximum-connection", "Maximum contact Connection", policy["maximumConnection"]),
        ("sr5-after-run-entry-maximum-loyalty", "Maximum contact Loyalty", policy["maximumLoyalty"]),
        ("sr5-after-run-entry-contact-karma-rate", "Karma per purchased contact point", policy["karmaPerContactPoint"]),
    ):
        _set(device, *field)
    for enabled, selector in (
        (policy["allowRunRewardContacts"], "sr5-after-run-entry-allow-reward-contacts"),
        (policy["allowKarmaPurchasedContacts"], "sr5-after-run-entry-allow-purchased-contacts"),
        (policy["calculatePublicAwareness"], "sr5-after-run-entry-calculate-awareness"),
    ):
        if enabled:
            _tap_exact(device, selector)
    for contact in fixture["contacts"]:
        for field in (
            ("sr5-after-run-entry-contact-id", "Contact UUID", contact["contactId"]),
            ("sr5-after-run-entry-contact-name", "Contact name", contact["name"]),
            ("sr5-after-run-entry-contact-role", "Contact role", contact["role"]),
            ("sr5-after-run-entry-contact-location", "Contact location", contact["location"]),
            ("sr5-after-run-entry-contact-connection", "Connection", contact["connection"]),
            ("sr5-after-run-entry-contact-loyalty", "Loyalty", contact["loyalty"]),
        ):
            _set(device, *field)
        if contact["kind"] == "Karma purchase":
            _tap_exact(device, "sr5-after-run-entry-contact-kind")
            device.tap("Karma purchase", timeout=60)
        _tap_exact(device, "sr5-after-run-entry-contact-add")
    for role, prefix, label in (
        ("gm", "gm", "GM"), ("owner", "owner", "Owner"),
    ):
        review = reviews[role]
        for field in (
            (f"sr5-after-run-entry-{prefix}-actor", f"{label} actor ID", review["actorId"]),
            (f"sr5-after-run-entry-{prefix}-review-id", f"{label} review UUID", review["reviewId"]),
            (f"sr5-after-run-entry-{prefix}-reason", f"{label} review reason / note", review["reason"]),
        ):
            _set(device, *field)
        _tap_exact(device, f"sr5-after-run-entry-{prefix}-approved")
    _tap_exact(device, "sr5-after-run-entry-publish")
    physical.wait_exact_route(device, CHOOSE_ROUTE, timeout=180)


def _assert_initial_runner(root: ET.Element, fixture: dict[str, object]) -> None:
    runner = fixture["runner"]
    expected = {
        "created": "True", "gameedition": "SR5", "alias": runner["alias"],
        "streetcred": str(runner["streetCred"]), "notoriety": str(runner["notoriety"]),
        "publicawareness": str(runner["publicAwareness"]), "karma": str(runner["karma"]),
        "nuyen": str(runner["nuyen"]), "heat": str(runner["heat"]),
    }
    for field, value in expected.items():
        if root.findtext(field) != value:
            raise RuntimeError(f"Initial After Run runner field {field} differs")
    if not _same_xml(root, ET.fromstring(render_runner_xml(fixture))):
        raise RuntimeError("Imported After Run runner differs from governed XML authority")


def _same_xml(actual: ET.Element, expected: ET.Element) -> bool:
    if actual.tag != expected.tag or actual.attrib != expected.attrib:
        return False
    if (actual.text or "") != (expected.text or ""):
        return False
    actual_children = list(actual)
    expected_children = list(expected)
    return len(actual_children) == len(expected_children) and all(
        _same_xml(left, right)
        for left, right in zip(actual_children, expected_children, strict=True)
    )


def _expected_successor_runner(
    fixture: dict[str, object], transaction_id: str, expense_date: str
) -> ET.Element:
    expected = ET.fromstring(render_runner_xml(fixture))
    projection = fixture["expected"]
    for name, value in (
        ("streetcred", projection["streetCredAfter"]),
        ("notoriety", projection["notorietyAfter"]),
        ("publicawareness", projection["publicAwarenessAfter"]),
        ("karma", projection["karmaAfter"]),
    ):
        target = expected.find(name)
        if target is None:
            raise RuntimeError(f"Governed runner omitted {name}")
        target.text = str(value)
    contacts = expected.find("contacts")
    expenses = expected.find("expenses")
    if contacts is None or expenses is None or list(contacts) or list(expenses):
        raise RuntimeError("Governed runner contact/expense base is not exact")
    for contact in _settled_contacts(fixture):
        ET.SubElement(contacts, "contact")
        created = contacts[-1]
        fields = [
            ("name", contact["Name"]), ("role", contact["Role"]),
            ("location", contact["Location"]), ("connection", contact["Connection"]),
            ("loyalty", contact["Loyalty"]), ("metatype", ""), ("gender", ""),
            ("age", ""), ("contacttype", ""), ("preferredpayment", ""),
            ("hobbiesvice", ""), ("personallife", ""), ("type", "Contact"),
            ("file", ""), ("relative", ""), ("notes", ""),
            ("notesColor", "#000000"), ("groupname", ""), ("colour", "-16777216"),
            ("group", "False"), ("family", "False"), ("blackmail", "False"),
            ("free", "True" if contact["Kind"] == 0 else "False"),
            ("groupenabled", "False"), ("guid", contact["ContactId"]),
        ]
        for name, value in fields:
            ET.SubElement(created, name).text = str(value) if value != "" else None
    expense = ET.SubElement(expenses, "expense")
    for name, value in (
        ("guid", transaction_id), ("date", expense_date),
        ("amount", -projection["contactKarmaCost"]),
        ("reason", f"After Run contacts ({projection['contactsAdded']})"),
        ("type", "Karma"), ("refund", "False"),
        ("forcecareervisible", "True"),
    ):
        ET.SubElement(expense, name).text = str(value)
    undo = ET.SubElement(expense, "undo")
    for name, value in (
        ("karmatype", "ManualSubtract"), ("nuyentype", "AddCyberware"),
        ("objectid", ""), ("qty", "0"), ("extra", ""),
    ):
        ET.SubElement(undo, name).text = value or None
    return expected


def _assert_successor_runner(
    root: ET.Element, fixture: dict[str, object], transaction_id: str
) -> None:
    actual_expenses = root.findall("./expenses/expense")
    if len(actual_expenses) != 1:
        raise RuntimeError("After Run successor expense cardinality differs")
    date = actual_expenses[0].findtext("date")
    if not isinstance(date, str):
        raise RuntimeError("After Run expense date is missing")
    try:
        datetime.strptime(date, "%Y-%m-%dT%H:%M:%S")
    except ValueError as error:
        raise RuntimeError("After Run expense date is not product-canonical") from error
    expected = _expected_successor_runner(fixture, transaction_id, date)
    if not _same_xml(root, expected):
        raise RuntimeError(
            "After Run successor contains a non-permitted XML delta or changed record"
        )


def prove_after_run(
    device: physical.shared.Device,
    runner: Path,
    runner_sha256: str,
    fixture: dict[str, object],
) -> dict[str, object]:
    device.shell("pm", "clear", physical.shared.PACKAGE)
    initial_launch, imported = prepare_runner(
        device, runner, runner_sha256, str(fixture["runner"]["alias"])
    )
    _assert_initial_runner(root_for_authority(device, imported), fixture)
    physical.shared.record_phone_ui_locale_evidence(device, evidence_prefix="sr5-after-run")
    open_after_run(device, ENTER_ROUTE)
    enter_manual_proposal(device, fixture)
    context = device.wait_for_single_exact_resource_id(
        "sr5-after-run-proposal-context", timeout=60,
        evidence_prefix="sr5-after-run-context", surface_name="After Run proposal context",
    ).attributes.get("text") or ""
    for identity in (fixture["identity"]["proposalId"], fixture["identity"]["runId"]):
        if identity not in context:
            raise RuntimeError("Published proposal context lost an exact identity")
    _tap_exact(device, "sr5-after-run-open-rewards")
    for route, control in (
        ("sr5-career/after-run/settlement/rewards", "sr5-after-run-acknowledge-rewards"),
        ("sr5-career/after-run/settlement/consequences", "sr5-after-run-acknowledge-consequences"),
        ("sr5-career/after-run/settlement/contacts", "sr5-after-run-acknowledge-contacts"),
        ("sr5-career/after-run/settlement/gm-review", "sr5-after-run-acknowledge-gm-review"),
        ("sr5-career/after-run/settlement/owner-review", "sr5-after-run-acknowledge-owner-review"),
    ):
        physical.wait_exact_route(device, route, timeout=120)
        device.capture(route.rsplit("/", 1)[-1])
        _tap_exact(device, control)
    physical.wait_exact_route(device, REVIEW_ROUTE, timeout=120)
    reviewed = read_checkpoint(device)
    if reviewed is None:
        raise RuntimeError("Reviewed After Run checkpoint disappeared")
    review_projection = validate_checkpoint(
        reviewed.payload, fixture, workspace_id=imported.workspace_id,
        workspace_revision=imported.content_revision,
        character_projection_digest=imported.payload_sha256, version=1, phase=0,
    )
    device.capture("sr5-after-run-durable-review")
    reviewed_restart = physical.shared.force_stop_and_launch_new_process(device, initial_launch)
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    physical.shared.tap_phone_destination(device, "phone-destination-runners")
    physical.shared.wait_for_phone_runners(device, timeout=120)
    restored_before = physical.shared.read_phone_workspace_authority(device)
    physical.shared.require_restored_authority(imported, restored_before)
    if read_checkpoint(device) != reviewed:
        raise RuntimeError("Reviewed After Run checkpoint bytes changed across restart")
    open_after_run(device, CHOOSE_ROUTE)
    _tap_exact(device, "sr5-after-run-resume")
    physical.wait_exact_route(device, REVIEW_ROUTE, timeout=120)
    _tap_exact(device, "sr5-after-run-confirm")
    physical.wait_exact_route(device, RECEIPT_ROUTE, timeout=240)
    applied = read_checkpoint(device)
    if applied is None:
        raise RuntimeError("Applied After Run checkpoint disappeared")
    receipt_projection = validate_checkpoint(
        applied.payload, fixture, workspace_id=imported.workspace_id,
        workspace_revision=imported.content_revision,
        character_projection_digest=imported.payload_sha256, version=3, phase=2,
    )
    require_same_draft(reviewed.payload, applied.payload)
    if any(
        receipt_projection[field] != review_projection[field]
        for field in ("transactionId", "gmReviewDigest", "ownerReviewDigest")
    ):
        raise RuntimeError("Applied receipt changed transaction or review-digest authority")
    device.capture("sr5-after-run-atomic-core-receipt")
    applied_restart = physical.shared.force_stop_and_launch_new_process(
        device, reviewed_restart.restarted
    )
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    open_after_run(device, CHOOSE_ROUTE)
    _tap_exact(device, "sr5-after-run-resolve")
    physical.wait_exact_route(device, RECEIPT_ROUTE, timeout=180)
    if read_checkpoint(device) != applied:
        raise RuntimeError("Applied After Run receipt bytes changed during restart recovery")
    device.capture("sr5-after-run-recovered-core-receipt")
    _tap_exact(device, "sr5-after-run-receipt-acknowledge")
    time.sleep(1)
    if read_checkpoint(device, required=False) is not None:
        raise RuntimeError("Acknowledged After Run checkpoint was not removed")
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    physical.shared.tap_phone_destination(device, "phone-destination-runners")
    physical.shared.wait_for_phone_runners(device, timeout=120)
    saved = physical.shared.read_phone_workspace_authority(device)
    physical.shared.require_saved_authority(saved)
    if saved.workspace_id != imported.workspace_id or saved.content_revision != imported.content_revision + 1:
        raise RuntimeError("After Run settlement did not save one exact successor revision")
    if saved.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("After Run successor did not change the workspace payload digest")
    _assert_successor_runner(
        root_for_authority(device, saved), fixture, review_projection["transactionId"]
    )
    final_restart = physical.shared.force_stop_and_launch_new_process(
        device, applied_restart.restarted
    )
    physical.shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    physical.shared.tap_phone_destination(device, "phone-destination-runners")
    physical.shared.wait_for_phone_runners(device, timeout=120)
    final_saved = physical.shared.read_phone_workspace_authority(device)
    physical.shared.require_restored_authority(saved, final_saved)
    _assert_successor_runner(
        root_for_authority(device, final_saved), fixture,
        review_projection["transactionId"],
    )
    if read_checkpoint(device, required=False) is not None:
        raise RuntimeError("After Run acknowledgement did not survive final restart")
    return {
        "import": physical.shared.workspace_authority_json(imported),
        "restoredBeforeApply": physical.shared.workspace_authority_json(restored_before),
        "savedSuccessor": physical.shared.workspace_authority_json(saved),
        "finalRestartSuccessor": physical.shared.workspace_authority_json(final_saved),
        "reviewedCheckpoint": reviewed.payload,
        "reviewedCheckpointSha256": reviewed.serialized_sha256,
        "appliedCheckpoint": applied.payload,
        "appliedCheckpointSha256": applied.serialized_sha256,
        "transactionAndReviewAuthority": review_projection,
        "restartProcessIds": [
            list(reviewed_restart.restarted.process_ids),
            list(applied_restart.restarted.process_ids),
            list(final_restart.restarted.process_ids),
        ],
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
    parser.add_argument("--fixture", type=Path, default=DEFAULT_FIXTURE)
    return parser.parse_args(argv)


def _remote(path: str, purpose: str) -> dict[str, object]:
    return {
        "path": path, "purpose": purpose, "precleanAttempted": False,
        "precleaned": False, "cleanupAttempted": False,
        "cleanupReplaySuppressed": False, "deletedAndVerified": False,
    }


def execute(args: argparse.Namespace, context: dict[str, object]) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{DISPOSABLE_DEVICE_FLAG} is required because the journey installs the APK, "
            "clears app data, imports a governed runner, and saves one settlement"
        )
    if physical.SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe ASCII grammar")
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    apk = args.apk.resolve()
    fixture_path = args.fixture.resolve()
    if fixture_path != DEFAULT_FIXTURE.resolve():
        raise RuntimeError("Physical After Run proof requires the exact committed governed fixture")
    fixture = load_fixture(fixture_path)
    provenance = load_and_verify_manifest(
        args.build_provenance_manifest, android_root=android_root, core_root=core_root,
        presentation_root=presentation_root, apk=apk,
    )
    repositories = _mapping(provenance.get("repositories"), "build repositories")
    artifact = _mapping(provenance.get("artifact"), "build artifact")
    expected_android = str(_mapping(repositories["android"], "android repository")["commit"])
    expected_core = str(_mapping(repositories["core"], "core repository")["commit"])
    expected_presentation = str(
        _mapping(repositories["presentation"], "presentation repository")["commit"]
    )
    expected_apk = str(artifact["sha256"])
    source_paths = {
        "driverSha256": driver,
        "fixtureSha256": fixture_path,
        "physicalHarnessSha256": Path(physical.__file__).resolve(),
        "sharedDeviceHarnessSha256": Path(physical.shared.__file__).resolve(),
        "buildProvenanceVerifierSha256": Path(load_and_verify_manifest.__code__.co_filename).resolve(),
        "careerWizardPageSha256": android_root / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs",
        "manualProposalPageSha256": android_root / "src/Chummer.Android/Native/Sr5AfterRunManualProposalPage.cs",
        "manualProposalSourceSha256": android_root / "src/Chummer.Android/Native/Sr5AfterRunManualProposalSource.cs",
        "workspaceSnapshotSha256": android_root / "src/Chummer.Android/Native/AndroidAfterRunWorkspaceSnapshotSource.cs",
        "checkpointStoreSha256": android_root / "src/Chummer.Android/Native/Sr5AfterRunSettlementCheckpointStore.cs",
        "settlementCoordinatorSha256": android_root / "src/Chummer.Android/Native/Sr5AfterRunSettlementCoordinator.cs",
        "settlementModelSha256": android_root / "src/Chummer.Android/Native/Sr5AfterRunWizardModel.cs",
        "settlementPageSha256": android_root / "src/Chummer.Android/Native/Sr5AfterRunSettlementWizardPage.cs",
        "runnerCoordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"After Run source graph is incomplete: {missing!r}")
    source_before = physical.source_graph_snapshot(
        android_root=android_root, core_root=core_root, presentation_root=presentation_root,
        apk=apk, expected_apk_sha256=expected_apk,
        expected_android_revision=expected_android, expected_core_revision=expected_core,
        expected_presentation_revision=expected_presentation, source_paths=source_paths,
    )
    context.update({
        "releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested",
        "buildProvenance": provenance, "sourceGraphAuthority": source_before,
    })
    runner, runner_sha256 = materialize_runner(fixture, args.evidence.resolve())
    device = physical.shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    remote_runner = f"/sdcard/Download/{runner.name}"
    remotes = [
        _remote(remote_runner, "temporary governed After Run runner"),
        _remote("/sdcard/chummer-editing-window.xml", "temporary UIAutomator hierarchy dump"),
    ]
    context["remoteTemporaryFiles"] = remotes
    errors: list[str] = []
    journey: dict[str, object] | None = None
    observation: dict[str, object] | None = None
    verified_remote = ""
    device_validated = False
    try:
        device.require_transport_stability(expected_api_level="36")
        observation = physical.android_device_observation(device)
        context["deviceObservation"] = observation
        device_validated = True
        for remote in remotes:
            remote["precleanAttempted"] = True
            physical.remove_remote_temporary_file(device, str(remote["path"]))
            remote["precleaned"] = True
        device.install_verified(apk, expected_apk, "--no-streaming", "-r")
        verified_remote = device.push_verified(runner, remote_runner, runner_sha256)
        journey = prove_after_run(device, runner, runner_sha256, fixture)
    except Exception as error:  # noqa: BLE001 - every runtime failure belongs in receipt
        errors.append(f"journey failed: {type(error).__name__}: {error}")
    finally:
        if device_validated:
            for remote in remotes:
                if not physical.shared.authorize_remote_cleanup_once(remote):
                    errors.append(f"remote cleanup replay suppressed for {remote['path']}")
                    continue
                try:
                    physical.remove_remote_temporary_file(device, str(remote["path"]))
                    remote["deletedAndVerified"] = True
                except Exception as error:  # noqa: BLE001
                    errors.append(f"remote cleanup failed for {remote['path']}: {error}")
        context["adbTransport"] = device.transport_summary()
        try:
            source_after = physical.source_graph_snapshot(
                android_root=android_root, core_root=core_root,
                presentation_root=presentation_root, apk=apk,
                expected_apk_sha256=expected_apk,
                expected_android_revision=expected_android,
                expected_core_revision=expected_core,
                expected_presentation_revision=expected_presentation,
                source_paths=source_paths,
            )
            context["postRunSourceGraphAuthority"] = source_after
            if source_after != source_before:
                errors.append("source/APK authority changed during physical execution")
        except Exception as error:  # noqa: BLE001
            errors.append(f"source/APK authority recheck failed: {error}")
    if errors:
        raise RuntimeError("; ".join(errors))
    if journey is None or observation is None or not all(
        remote["deletedAndVerified"] for remote in remotes
    ):
        raise RuntimeError("After Run journey, device, or cleanup proof is incomplete")
    return {
        "schema": SCHEMA, "status": "device-pass-source-bound", "executionStatus": "pass",
        "releaseEvidenceStatus": context["releaseEvidenceStatus"],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "profile": "phone",
        "journey": "sr5-after-run-settlement-physical", "serial": args.serial,
        "apiLevel": observation["apiLevel"], "abi": observation["abi"],
        "deviceObservation": observation, "buildProvenance": provenance,
        "sourceGraphAuthority": source_before,
        "postRunSourceGraphAuthoritySha256": source_before["authoritySha256"],
        "sourceGraphRecheckedAfterRun": True, "apkSha256": source_before["apkSha256"],
        "apkAbis": source_before["apkAbis"],
        "governedFixtureSha256": source_before["sourceFileSha256"]["fixtureSha256"],
        "materializedRunnerSha256": runner_sha256,
        "verifiedRemoteRunnerSha256": verified_remote,
        "remoteTemporaryFiles": remotes, "authorityProofStages": journey,
        "journeys": {
            "exactProposalRunCharacterIds": "pass",
            "rewardHeatReputationAndContacts": "pass",
            "gmAndOwnerReviewDigests": "pass",
            "durableReviewRestartResume": "pass",
            "atomicCoreReceiptAndSuccessor": "pass",
            "receiptRestartRecovery": "pass",
            "acknowledgementAndFinalRestart": "pass",
        },
    }


def failure_receipt(
    args: argparse.Namespace, error: Exception, context: dict[str, object]
) -> dict[str, object]:
    return {
        "schema": SCHEMA, "status": "fail", "executionStatus": "fail",
        "releaseEvidenceStatus": context.get("releaseEvidenceStatus", "manifest-not-verified"),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone", "journey": "sr5-after-run-settlement-physical",
        "serial": args.serial,
        "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
        **context,
    }


def argument_failure_receipt(exit_code: int) -> dict[str, object]:
    return {
        "schema": SCHEMA, "status": "fail", "executionStatus": "fail",
        "releaseEvidenceStatus": "manifest-not-verified",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone", "journey": "sr5-after-run-settlement-physical",
        "failure": {"type": "ArgumentParseError", "message": f"argparse exited {exit_code}"},
    }


def main(argv: list[str] | None = None) -> int:
    raw_args = list(sys.argv[1:] if argv is None else argv)
    if any(argument in {"-h", "--help"} for argument in raw_args):
        try:
            parse_args(["--help"])
        except SystemExit as error:
            return int(error.code or 0)
        return 0
    try:
        receipt_path = physical.locate_explicit_receipt(raw_args)
        physical.validate_external_output_path(
            receipt_path, label="Receipt path",
            repository_roots=physical.preparse_repository_roots(raw_args),
            expect_directory=False,
        )
        physical.prepare_receipt_target(receipt_path)
    except Exception as error:  # noqa: BLE001
        print(f"Cannot prepare explicit receipt target: {error}", file=sys.stderr)
        return 2
    try:
        args = parse_args(raw_args)
    except SystemExit as error:
        exit_code = int(error.code or 0)
        if exit_code:
            physical.write_receipt_atomically(receipt_path, argument_failure_receipt(exit_code))
        return exit_code
    context: dict[str, object] = {}
    try:
        repositories = physical.source_repository_roots(
            android_root=Path(__file__).resolve().parents[1],
            workspace_root=args.workspace_root.resolve(),
        )
        physical.validate_external_output_path(
            receipt_path, label="Receipt path", repository_roots=repositories,
            expect_directory=False,
        )
        physical.validate_external_output_path(
            args.evidence, label="Evidence path", repository_roots=repositories,
            expect_directory=True,
        )
        physical.validate_output_layout(receipt=receipt_path, evidence=args.evidence)
        receipt = execute(args, context)
    except Exception as error:  # noqa: BLE001
        receipt = failure_receipt(args, error, context)
        physical.write_receipt_atomically(receipt_path, receipt)
        print(f"Physical SR5 After Run E2E failed: {error}", file=sys.stderr)
        return 1
    physical.write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
