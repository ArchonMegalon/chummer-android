from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path
import sys

import pytest


TESTS = Path(__file__).resolve().parent
sys.path.insert(0, str(TESTS))
import run_api36_sr5_life_module_nationality_origin_physical_e2e as driver  # noqa: E402


def digest(label: str) -> str:
    return hashlib.sha256(label.encode("utf-8")).hexdigest()


def provenance(seed: str) -> dict[str, object]:
    value: dict[str, object] = {
        "schema": driver.PROVENANCE_SCHEMA,
        "state": driver.PROVENANCE_STATE,
        "providerId": "",
        "providerModelId": "",
        "providerRouteReceiptDigest": "",
        "proposalDigest": "",
        "boundSeedDigest": seed,
        "isVerified": False,
        "provenanceDigest": "",
        "mechanicsAuthority": driver.PRESENTATION_ONLY,
        "affectsMechanics": False,
    }
    value["provenanceDigest"] = driver.provenance_digest(value)
    return value


def legal_choice() -> dict[str, object]:
    preview_digest = digest("mechanics-preview")
    return {
        "choiceId": digest("choice"),
        "label": "Street origin",
        "source": "RF",
        "pageReference": "66",
        "decisionCommandDigest": digest("decision-command"),
        "mechanicsPreview": {
            "karmaCost": 15,
            "karmaRaw": "15",
            "karmaIsExact": True,
            "items": [],
            "pendingFollowUpIds": [],
            "sourceAnchorIds": ["lifemodules.xml#module:street-origin"],
            "previewDigest": preview_digest,
        },
        "mechanicsPreviewDigest": preview_digest,
        "sourceAnchorIds": ["lifemodules.xml#module:street-origin"],
        "blockers": [],
        "isLegal": True,
        "choiceDigest": digest("choice-digest"),
        "withholdsContinuationUntilAccepted": True,
    }


def initial_checkpoint(
    *, workspace_id: str = "workspaceorigin", revision: int = 1
) -> driver.JsonSnapshot:
    choice = legal_choice()
    projection_seed = digest("projection-seed")
    prompt = "Which origin shapes your runner?"
    turn = {
        "schema": driver.TURN_SCHEMA,
        "rulesetId": "sr5",
        "workspaceId": workspace_id,
        "workspaceRevision": revision,
        "ownerId": driver.OWNER_ID,
        "runnerId": workspace_id,
        "runnerDisplayName": driver.FIXTURE_ALIAS,
        "locale": "en-US",
        "journeyId": driver.JOURNEY_ID,
        "stageId": driver.STAGE_ID,
        "stageOrder": 1,
        "turnId": "turn-1",
        "turnSequence": 1,
        "visibleStoryMarkdown": "Your origin is not decided yet. " + prompt,
        "decisionPrompt": prompt,
        "legalChoices": [choice],
        "canonicalFacts": [],
        "acceptedDecisionIds": [],
        "previousTurnDigest": digest("turn-root"),
        "decisionGraphDigest": digest("decision-graph"),
        "decisionDigest": digest("decision"),
        "contentDigest": digest("fixture"),
        "sourceDigest": digest("source"),
        "rulesDigest": digest("rules"),
        "runtimeDigest": digest("runtime"),
        "seedDigest": digest("turn-seed"),
        "storyEndsAtDecisionPoint": True,
        "mechanicsAuthority": "accepted-life-module-decisions-only",
        "isTerminal": False,
    }
    projection = {
        "schema": driver.ARC_SCHEMA,
        "arcSeedId": "arc-1",
        "currentTurn": turn,
        "canonicalLayer": {
            "mechanicsSnapshotDigest": digest("mechanics"),
        },
        "visibleChapters": [],
        "allowedCanonicalFactIds": [],
        "allowedChoiceIds": [choice["choiceId"]],
        "toneTags": [],
        "seedDigest": projection_seed,
    }
    value: dict[str, object] = {
        "schema": driver.DRAFT_SCHEMA,
        "ownerId": driver.OWNER_ID,
        "workspaceId": workspace_id,
        "workspaceRevision": revision,
        "projection": projection,
        "timelineChapterDigests": [],
        "pendingPreview": None,
        "ltdProvenance": provenance(projection_seed),
        "boundSeedDigest": projection_seed,
        "boundDecisionGraphDigest": turn["decisionGraphDigest"],
        "boundContentDigest": turn["contentDigest"],
        "boundSourceDigest": turn["sourceDigest"],
        "boundRulesDigest": turn["rulesDigest"],
        "boundRuntimeDigest": turn["runtimeDigest"],
        "boundMechanicsSnapshotDigest": digest("mechanics"),
        "checkpointDigest": "",
        "isUserOwnedDraft": True,
    }
    value["checkpointDigest"] = driver.checkpoint_digest(value)
    raw = json.dumps(value, separators=(",", ":")).encode("utf-8")
    return driver.JsonSnapshot(value, hashlib.sha256(raw).hexdigest(), raw)


def prepared_checkpoint(
    initial: driver.JsonSnapshot,
) -> driver.JsonSnapshot:
    value = copy.deepcopy(initial.payload)
    projection = value["projection"]
    assert isinstance(projection, dict)
    turn = projection["currentTurn"]
    assert isinstance(turn, dict)
    choice = turn["legalChoices"][0]
    assert isinstance(choice, dict)
    provenance_value = value["ltdProvenance"]
    value["pendingPreview"] = {
        "schema": "chummer.origin_dossier.life_module_decision_preview.v1",
        "ownerId": driver.OWNER_ID,
        "workspaceId": value["workspaceId"],
        "workspaceRevision": value["workspaceRevision"],
        "turnId": turn["turnId"],
        "visibleStoryMarkdown": turn["visibleStoryMarkdown"],
        "decisionPrompt": turn["decisionPrompt"],
        "selectedChoice": {
            "choiceId": choice["choiceId"],
            "label": choice["label"],
            "source": choice["source"],
            "pageReference": choice["pageReference"],
            "mechanicsPreview": choice["mechanicsPreview"],
            "sourceAnchorIds": choice["sourceAnchorIds"],
            "choiceDigest": choice["choiceDigest"],
            "cardDigest": digest("card"),
        },
        "ltdProvenance": copy.deepcopy(provenance_value),
        "boundSeedDigest": value["boundSeedDigest"],
        "boundDecisionDigest": turn["decisionDigest"],
        "boundMechanicsSnapshotDigest": value["boundMechanicsSnapshotDigest"],
        "previewDigest": digest("prepared-preview"),
        "requiresExplicitConfirmation": True,
        "includesFutureBranchText": False,
    }
    value["checkpointDigest"] = driver.checkpoint_digest(value)
    raw = json.dumps(value, separators=(",", ":")).encode("utf-8")
    return driver.JsonSnapshot(value, hashlib.sha256(raw).hexdigest(), raw)


def imported_authority() -> driver.shared.WorkspaceAuthority:
    return driver.shared.WorkspaceAuthority(
        "workspaceorigin", 1, 1, digest("fixture"), digest("document")
    )


def before_record() -> driver.JsonSnapshot:
    value = {
        "Format": "Xml",
        "RecordSchemaVersion": 2,
        "Envelope": {
            "RulesetId": "sr5",
            "SchemaVersion": 1,
            "PayloadKind": "workspace",
            "Payload": "fixture-payload",
        },
        "ContentRevision": 1,
        "SavedRevision": 1,
    }
    raw = json.dumps(value, separators=(",", ":")).encode()
    return driver.JsonSnapshot(value, hashlib.sha256(raw).hexdigest(), raw)


def applied_record(choice: dict[str, object]) -> driver.JsonSnapshot:
    imported = imported_authority()
    receipt: dict[str, object] = {
        "Schema": driver.ACCEPTANCE_SCHEMA,
        "DecisionId": "decision-1",
        "ChoiceId": choice["choiceId"],
        "DecisionCommandDigest": choice["decisionCommandDigest"],
        "IdempotencyKeyDigest": digest("idempotency"),
        "PreviousWorkspaceRevision": 1,
        "WorkspaceRevision": 2,
        "PreviousContentDigest": imported.payload_sha256,
        "ContentDigest": imported.payload_sha256,
        "SourceDigest": digest("source"),
        "RulesDigest": digest("rules"),
        "RuntimeDigest": digest("runtime"),
        "PreviousDecisionDigest": digest("decision"),
        "PreviousMechanicsSnapshotDigest": digest("mechanics"),
        "AcceptedDecisionGraphDigest": digest("accepted-graph"),
        "MechanicsSnapshotDigest": digest("mechanics-after"),
        "ConsequenceMarkdown": "The streets taught the runner to survive.",
        "CanonicalFacts": [{
            "FactId": "life-module:street-origin",
            "FactKind": "accepted-life-module",
            "LocalizedSummary": "Street origin",
            "AcceptedDecisionId": "decision-1",
            "SourceAnchorIds": ["lifemodules.xml#module:street-origin"],
            "FactDigest": digest("fact"),
        }],
        "ReceiptDigest": "",
    }
    receipt["ReceiptDigest"] = driver.receipt_digest(receipt)
    next_step = {
        "Schema": driver.STEP_SCHEMA,
        "RulesetId": "sr5",
        "WorkspaceId": imported.workspace_id,
        "WorkspaceRevision": 2,
        "JourneyId": driver.JOURNEY_ID,
        "StageId": driver.TERMINAL_STAGE_ID,
        "IsTerminal": True,
        "LegalChoices": [],
        "DecisionGraphDigest": receipt["AcceptedDecisionGraphDigest"],
        "ContentDigest": receipt["ContentDigest"],
        "SourceDigest": receipt["SourceDigest"],
        "RulesDigest": receipt["RulesDigest"],
        "RuntimeDigest": receipt["RuntimeDigest"],
        "MechanicsSnapshotDigest": receipt["MechanicsSnapshotDigest"],
    }
    initial = before_record().payload
    value = {
        **initial,
        "ContentRevision": 2,
        "AuxiliaryState": {
            "CharacterCreationFoundationDraft": {"DraftDigest": digest("draft")},
            "LifeModuleDecisionAcceptances": [{
                "Receipt": receipt,
                "NextStep": next_step,
            }],
            "IsEmpty": False,
        },
    }
    raw = json.dumps(value, separators=(",", ":")).encode()
    return driver.JsonSnapshot(value, hashlib.sha256(raw).hexdigest(), raw)


def valid_initial() -> tuple[driver.JsonSnapshot, driver.InitialTurnAuthority]:
    snapshot = initial_checkpoint()
    authority = driver.validate_initial_checkpoint(
        snapshot, "workspaceorigin", 1, digest("fixture")
    )
    return snapshot, authority


def test_initial_turn_requires_life_modules_story_choice_sources_and_not_requested():
    snapshot, authority = valid_initial()

    assert authority.choice["label"] == "Street origin"
    assert authority.story.endswith(authority.prompt)
    assert authority.locale == "en-US"
    assert snapshot.payload["ltdProvenance"]["state"] == "not-requested"


@pytest.mark.parametrize(
    ("field", "value"),
    (
        ("state", "verified-proposal"),
        ("providerId", "external-provider"),
        ("providerModelId", "remote-model"),
        ("providerRouteReceiptDigest", digest("remote-route")),
        ("proposalDigest", digest("proposal")),
        ("isVerified", True),
        ("affectsMechanics", True),
    ),
)
def test_initial_turn_rejects_any_provider_or_mechanics_provenance(field, value):
    snapshot = initial_checkpoint()
    snapshot.payload["ltdProvenance"][field] = value
    snapshot.payload["checkpointDigest"] = driver.checkpoint_digest(snapshot.payload)

    with pytest.raises(RuntimeError, match="LTD provenance"):
        driver.validate_initial_checkpoint(
            snapshot, "workspaceorigin", 1, digest("fixture")
        )


def test_initial_turn_rejects_story_past_the_decision():
    snapshot = initial_checkpoint()
    turn = snapshot.payload["projection"]["currentTurn"]
    turn["visibleStoryMarkdown"] += " A hidden future branch."
    snapshot.payload["checkpointDigest"] = driver.checkpoint_digest(snapshot.payload)

    with pytest.raises(RuntimeError, match="does not end"):
        driver.validate_initial_checkpoint(
            snapshot, "workspaceorigin", 1, digest("fixture")
        )


@pytest.mark.parametrize(
    "mutation",
    (
        lambda choice: choice.update(isLegal=False),
        lambda choice: choice.update(blockers=["blocked"]),
        lambda choice: choice.update(sourceAnchorIds=[]),
        lambda choice: choice["mechanicsPreview"].update(karmaIsExact=False),
        lambda choice: choice.update(withholdsContinuationUntilAccepted=False),
    ),
)
def test_initial_turn_rejects_non_authoritative_choice(mutation):
    snapshot = initial_checkpoint()
    choice = snapshot.payload["projection"]["currentTurn"]["legalChoices"][0]
    mutation(choice)
    snapshot.payload["checkpointDigest"] = driver.checkpoint_digest(snapshot.payload)

    with pytest.raises(RuntimeError):
        driver.validate_initial_checkpoint(
            snapshot, "workspaceorigin", 1, digest("fixture")
        )


def test_prepared_preview_preserves_turn_and_requires_explicit_confirmation():
    snapshot, authority = valid_initial()
    prepared = prepared_checkpoint(snapshot)

    preview = driver.validate_prepared_checkpoint(authority, prepared)

    assert preview["requiresExplicitConfirmation"] is True
    assert preview["includesFutureBranchText"] is False
    assert preview["selectedChoice"]["choiceId"] == authority.choice["choiceId"]


@pytest.mark.parametrize(
    ("path", "value"),
    (
        (("boundSourceDigest",), digest("stale-source")),
        (("pendingPreview", "requiresExplicitConfirmation"), False),
        (("pendingPreview", "includesFutureBranchText"), True),
        (("pendingPreview", "selectedChoice", "choiceId"), "invented-choice"),
    ),
)
def test_prepared_preview_rejects_stale_or_unreviewed_authority(path, value):
    snapshot, authority = valid_initial()
    prepared = prepared_checkpoint(snapshot)
    target = prepared.payload
    for key in path[:-1]:
        target = target[key]
    target[path[-1]] = value
    prepared.payload["checkpointDigest"] = driver.checkpoint_digest(prepared.payload)

    with pytest.raises(RuntimeError):
        driver.validate_prepared_checkpoint(authority, prepared)


def test_acceptance_is_one_foundation_owned_revision_and_canonical_receipt():
    _, authority = valid_initial()
    initial = before_record()
    initial.payload["Envelope"]["Payload"] = "fixture-payload"
    imported = imported_authority()
    imported = driver.shared.WorkspaceAuthority(
        imported.workspace_id,
        imported.content_revision,
        imported.saved_revision,
        hashlib.sha256(b"fixture-payload").hexdigest(),
        imported.document_sha256,
    )
    applied = applied_record(authority.choice)
    applied.payload["Envelope"] = copy.deepcopy(initial.payload["Envelope"])
    receipt = applied.payload["AuxiliaryState"]["LifeModuleDecisionAcceptances"][0]["Receipt"]
    receipt["PreviousContentDigest"] = imported.payload_sha256
    receipt["ContentDigest"] = imported.payload_sha256
    receipt["ReceiptDigest"] = driver.receipt_digest(receipt)
    next_step = applied.payload["AuxiliaryState"]["LifeModuleDecisionAcceptances"][0]["NextStep"]
    next_step["ContentDigest"] = imported.payload_sha256

    projection = driver.validate_acceptance_record(
        initial, applied, imported, authority
    )

    assert projection["workspaceRevision"] == 2
    assert projection["choiceId"] == authority.choice["choiceId"]
    assert projection["canonicalFactCount"] == 1


def test_acceptance_rejects_mutation_replay_and_receipt_tamper():
    _, authority = valid_initial()
    initial = before_record()
    imported = imported_authority()
    applied = applied_record(authority.choice)
    initial.payload["Envelope"] = copy.deepcopy(applied.payload["Envelope"])

    acceptance = applied.payload["AuxiliaryState"]["LifeModuleDecisionAcceptances"][0]
    applied.payload["AuxiliaryState"]["LifeModuleDecisionAcceptances"].append(
        copy.deepcopy(acceptance)
    )
    with pytest.raises(RuntimeError, match="replayed"):
        driver.validate_acceptance_record(initial, applied, imported, authority)

    applied = applied_record(authority.choice)
    initial.payload["Envelope"] = copy.deepcopy(applied.payload["Envelope"])
    receipt = applied.payload["AuxiliaryState"]["LifeModuleDecisionAcceptances"][0]["Receipt"]
    receipt["ReceiptDigest"] = digest("tampered")
    with pytest.raises(RuntimeError, match="not canonical"):
        driver.validate_acceptance_record(initial, applied, imported, authority)


@pytest.mark.parametrize("mutation", ("source-digest", "foreign-fact-anchor"))
def test_acceptance_rejects_receipt_detached_from_reviewed_turn(mutation):
    _, authority = valid_initial()
    initial = before_record()
    imported = imported_authority()
    applied = applied_record(authority.choice)
    initial.payload["Envelope"] = copy.deepcopy(applied.payload["Envelope"])
    receipt = applied.payload["AuxiliaryState"]["LifeModuleDecisionAcceptances"][0]["Receipt"]
    if mutation == "source-digest":
        receipt["SourceDigest"] = digest("foreign-source")
    else:
        receipt["CanonicalFacts"][0]["SourceAnchorIds"] = ["foreign.xml#module:other"]
    receipt["ReceiptDigest"] = driver.receipt_digest(receipt)

    with pytest.raises(RuntimeError):
        driver.validate_acceptance_record(initial, applied, imported, authority)


@pytest.mark.parametrize(
    "semantic",
    (
        "Life Modules budget: 100 total, 15 used, 85 remaining, unit karma",
        "Lebensmodulbudget: 100 gesamt, 15 verwendet, 85 verbleibend, Einheit karma",
        "Presupuesto de Módulos de vida: 100 total, 15 usado, 85 restante, unidad karma",
    ),
)
def test_budget_semantics_accept_exact_supported_language_authority(semantic):
    parsed = driver.parse_budget_semantic(semantic)
    assert parsed == {
        "total": "100",
        "used": "15",
        "remaining": "85",
        "unit": "karma",
        "semantic": semantic,
    }


@pytest.mark.parametrize(
    "semantic",
    (
        "Life Modules budget: 100 total, 15 used, 84 remaining, unit karma",
        "Life Modules budget: unknown",
        "Life Modules budget: -1 total, 0 used, -1 remaining, unit karma",
    ),
)
def test_budget_semantics_reject_inconsistent_or_untyped_values(semantic):
    with pytest.raises(RuntimeError):
        driver.parse_budget_semantic(semantic)


def test_duplicate_json_key_is_rejected():
    with pytest.raises(RuntimeError, match="strict UTF-8 JSON"):
        driver.strict_json_bytes(b'{"schema":"a","schema":"b"}', "hostile")


def test_fixture_is_exact_sr5_life_modules_creation_runner():
    root = driver.fixture_root(driver.FIXTURE)
    assert root.findtext("buildmethod") == "LifeModules"
    assert root.findtext("created") == "False"


def test_execution_requires_explicit_disposable_device_authorization_first():
    args = argparse.Namespace(allow_destructive_disposable_device=False)
    with pytest.raises(RuntimeError, match=driver.DISPOSABLE_DEVICE_FLAG):
        driver.execute(args, {})


def test_driver_is_non_gating_and_has_no_provider_network_client():
    source = Path(driver.__file__).read_text(encoding="utf-8")
    for forbidden in (
        "import requests", "from requests", "import urllib", "from urllib",
        "import socket", "http://", "https://",
    ):
        assert forbidden not in source
    assert '"gateRegistered": False' in source
    assert '"aggregateJourneyCountContribution": 0' in source
    assert '"publicationAuthorized": False' in source
    assert '"providerNetworkCallCount": 0' in source
    assert '"origin-life-budget-total"' in source
    assert '"origin-life-budget-used"' in source
    assert '"origin-life-budget-remaining"' in source
    assert "workflow_dispatch" not in source
    assert "OriginDossierLifeModulePhoneRuntime.cs" in source
    assert "CharacterCreationFoundationLifeModuleDecisionAuthority.cs" in source


def test_failure_receipt_cannot_widen_release_or_aggregate_claims():
    args = argparse.Namespace(serial="physical-device")
    receipt = driver.failure_receipt(args, RuntimeError("boom"), {
        "status": "pass",
        "gateRegistered": True,
        "aggregateJourneyCountContribution": 1,
        "publicationAuthorized": True,
        "releaseClaim": "play-ready",
        "providerNetworkCallCount": 7,
        "nonClaims": [],
    })
    assert receipt["status"] == "fail"
    assert receipt["gateRegistered"] is False
    assert receipt["aggregateJourneyCountContribution"] == 0
    assert receipt["publicationAuthorized"] is False
    assert receipt["releaseClaim"] == "none"
    assert receipt["providerNetworkCallCount"] == 0
    assert receipt["nonClaims"] == driver.NON_CLAIMS
