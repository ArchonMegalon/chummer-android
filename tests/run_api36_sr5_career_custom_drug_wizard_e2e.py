#!/usr/bin/env python3
"""Exercise the typed SR5 Career Custom Drug wizard on hosted API 36.

This is deliberately a non-gating diagnostic journey.  It proves the real
wizard route and its exact Core-owned recipe mutation; it grants no aggregate,
publication, Play, tablet, generic-editing, or later-quantity-purchase claim.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_editing_e2e as shared


RECEIPT_SCHEMA = "chummer.android.sr5-career-custom-drug-wizard-e2e/v1"
JOURNEY = "sr5-career-custom-drug-wizard"
CHECKPOINT_SCHEMA = "chummer.android.sr5-career-custom-drug-recipe.checkpoint.v1"
CHECKPOINT_KEY_PREFIX = "chummer.android.sr5-career-custom-drug-recipe.v1."
RECIPE_NAME = "Nightwatch API36"
GRADE_ID = "b00a075a-2c2c-4816-bb4a-0d0a33d06370"
GRADE_NAME = "Standard"
FOUNDATION_ID = "33ae6b1c-62f6-4824-967d-0e2b37c7d1b9"
FOUNDATION_NAME = "Tank"
FOUNDATION_LEVEL = 0
EMPTY_GUID = "00000000-0000-0000-0000-000000000000"
LOWER_SHA256 = re.compile(r"^[0-9a-f]{64}$")
IDEMPOTENCY_KEY = re.compile(r"^custom-drug-recipe:[0-9a-f]{32}$")

CHECKPOINT_FIELDS = {
    "SchemaId", "WorkspaceId", "BoundContentRevision",
    "BoundCharacterDigest", "BoundCatalogDigest", "BoundRulesDigest",
    "Selection", "Phase", "Command", "Receipt",
}
SELECTION_FIELDS = {
    "Name", "GradeId", "Quantity", "Stolen", "FreeCost",
    "MarkupPercent", "Components",
}
COMPONENT_SELECTION_FIELDS = {"ComponentId", "Level"}
COMMAND_FIELDS = {
    "ExpectedContentRevision", "ExpectedCharacterDigest",
    "ExpectedCatalogDigest", "ExpectedRulesDigest", "ExpectedQuoteDigest",
    "IdempotencyKey", "Selection", "NewDrugInstanceId",
    "NewComponentInstanceIds",
}
RECEIPT_FIELDS = {
    "PreviousContentRevision", "ContentRevision", "PreviousCharacterDigest",
    "CharacterDigest", "CatalogDigest", "RulesDigest", "QuoteDigest",
    "CommandDigest", "IdempotencyKeyDigest", "DrugInstanceId",
    "ComponentInstanceIds", "DrugXmlDigest", "ReceiptDigest",
}
FIXTURE = (
    Path(__file__).resolve().parent
    / "fixtures/sr5-career-custom-drug-wizard-e2e.chum5"
)


@dataclass(frozen=True)
class CheckpointSnapshot:
    payload: dict[str, object]
    serialized_sha256: str


def object_without_duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise RuntimeError(f"Duplicate JSON key in custom-drug checkpoint: {key}")
        result[key] = value
    return result


def require_object(value: object, label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise RuntimeError(f"{label} is not an object")
    return value


def require_exact_fields(value: dict[str, object], fields: set[str], label: str) -> None:
    if set(value) != fields:
        raise RuntimeError(
            f"{label} fields are not exact: expected={sorted(fields)!r}, "
            f"actual={sorted(value)!r}"
        )


def require_digest(value: object, label: str) -> str:
    if not isinstance(value, str) or LOWER_SHA256.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not canonical lowercase SHA-256")
    return value


def require_guid(value: object, label: str, *, allow_empty: bool = False) -> str:
    if not isinstance(value, str):
        raise RuntimeError(f"{label} is not a GUID string")
    try:
        parsed = uuid.UUID(value)
    except ValueError as error:
        raise RuntimeError(f"{label} is not a GUID") from error
    if str(parsed) != value or (not allow_empty and parsed.int == 0):
        raise RuntimeError(f"{label} is not one canonical GUID")
    return value


def value_wrapper(value: object, label: str, *, allow_empty: bool = False) -> str:
    wrapper = require_object(value, label)
    require_exact_fields(wrapper, {"Value"}, label)
    return require_guid(wrapper["Value"], f"{label}.Value", allow_empty=allow_empty)


def dotnet_scalar(value: object) -> str:
    if value is True:
        return "True"
    if value is False:
        return "False"
    return str(value)


def selection_canonical(selection: dict[str, object]) -> str:
    components = selection["Components"]
    if not isinstance(components, list):
        raise RuntimeError("Custom-drug components are not an array")
    rows: list[tuple[str, int]] = []
    for index, candidate in enumerate(components):
        component = require_object(candidate, f"Selection component {index}")
        require_exact_fields(
            component, COMPONENT_SELECTION_FIELDS, f"Selection component {index}"
        )
        component_id = value_wrapper(
            component["ComponentId"], f"Selection component {index} identity"
        )
        level = component["Level"]
        if type(level) is not int:
            raise RuntimeError("Custom-drug component level is not an integer")
        rows.append((component_id, level))
    rows.sort()
    result = "".join(
        (
            str(selection["Name"]).strip() + "\n",
            value_wrapper(selection["GradeId"], "Selection grade") + "\n",
            dotnet_scalar(selection["Quantity"]) + "\n",
            dotnet_scalar(selection["Stolen"]) + "\n",
            dotnet_scalar(selection["FreeCost"]) + "\n",
            dotnet_scalar(selection["MarkupPercent"]) + "\n",
        )
    )
    return result + "".join(f"{identity}|{level}\n" for identity, level in rows)


def command_digest(command: dict[str, object]) -> str:
    component_instances = command["NewComponentInstanceIds"]
    if not isinstance(component_instances, list):
        raise RuntimeError("New component instance identities are not an array")
    canonical = "".join(
        (
            "custom-drug-recipe-command-v2\n",
            f'{command["ExpectedContentRevision"]}\n',
            f'{command["ExpectedCharacterDigest"]}\n',
            f'{command["ExpectedCatalogDigest"]}\n',
            f'{command["ExpectedRulesDigest"]}\n',
            f'{command["ExpectedQuoteDigest"]}\n',
            f'{command["IdempotencyKey"]}\n',
            value_wrapper(command["NewDrugInstanceId"], "New drug identity") + "\n",
            selection_canonical(
                require_object(command["Selection"], "Command selection")
            ),
            "".join(
                f"{require_guid(value, 'New component identity')}\n"
                for value in component_instances
            ),
        )
    )
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def receipt_digest(receipt: dict[str, object]) -> str:
    component_instances = receipt["ComponentInstanceIds"]
    if not isinstance(component_instances, list):
        raise RuntimeError("Receipt component identities are not an array")
    canonical = "".join(
        (
            "custom-drug-recipe-receipt-v2\n",
            f'{receipt["PreviousContentRevision"]}\n',
            f'{receipt["ContentRevision"]}\n',
            f'{receipt["PreviousCharacterDigest"]}\n',
            f'{receipt["CharacterDigest"]}\n',
            f'{receipt["CatalogDigest"]}\n',
            f'{receipt["RulesDigest"]}\n',
            f'{receipt["QuoteDigest"]}\n',
            f'{receipt["CommandDigest"]}\n',
            f'{receipt["IdempotencyKeyDigest"]}\n',
            value_wrapper(receipt["DrugInstanceId"], "Receipt drug identity") + "\n",
            f'{receipt["DrugXmlDigest"]}\n',
            "".join(
                f"{require_guid(value, 'Receipt component identity')}\n"
                for value in component_instances
            ),
        )
    )
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def validate_selection(value: object) -> dict[str, object]:
    selection = require_object(value, "Custom-drug selection")
    require_exact_fields(selection, SELECTION_FIELDS, "Custom-drug selection")
    if selection["Name"] != RECIPE_NAME:
        raise RuntimeError("Custom-drug selection name is not exact")
    if value_wrapper(selection["GradeId"], "Selection grade") != GRADE_ID:
        raise RuntimeError("Custom-drug grade identity is not exact")
    exact_scalars = {
        "Quantity": 1,
        "Stolen": False,
        "FreeCost": False,
        "MarkupPercent": 0,
    }
    for key, expected in exact_scalars.items():
        if selection[key] != expected or type(selection[key]) is not type(expected):
            raise RuntimeError(f"Custom-drug selection {key} is not exact")
    components = selection["Components"]
    if not isinstance(components, list) or len(components) != 1:
        raise RuntimeError("Custom-drug selection must contain exactly one component")
    component = require_object(components[0], "Custom-drug Foundation selection")
    require_exact_fields(
        component, COMPONENT_SELECTION_FIELDS, "Custom-drug Foundation selection"
    )
    if value_wrapper(component["ComponentId"], "Foundation identity") != FOUNDATION_ID:
        raise RuntimeError("Custom-drug Foundation identity is not exact")
    if component["Level"] != FOUNDATION_LEVEL or type(component["Level"]) is not int:
        raise RuntimeError("Custom-drug Foundation level is not exact")
    return selection


def validate_checkpoint(
    checkpoint: dict[str, object],
    *,
    workspace_id: str,
    initial: shared.WorkspaceAuthority,
    persisted: shared.WorkspaceAuthority | None,
    phase: int,
) -> tuple[dict[str, object], dict[str, object] | None]:
    require_exact_fields(checkpoint, CHECKPOINT_FIELDS, "Custom-drug checkpoint")
    if checkpoint["SchemaId"] != CHECKPOINT_SCHEMA:
        raise RuntimeError("Custom-drug checkpoint schema is not exact")
    workspace_value = require_object(checkpoint["WorkspaceId"], "Checkpoint workspace")
    require_exact_fields(workspace_value, {"Value"}, "Checkpoint workspace")
    if workspace_value["Value"] != workspace_id:
        raise RuntimeError("Custom-drug checkpoint belongs to another workspace")
    if checkpoint["Phase"] != phase or type(checkpoint["Phase"]) is not int:
        raise RuntimeError("Custom-drug checkpoint phase is not exact")
    selection = validate_selection(checkpoint["Selection"])
    command = require_object(checkpoint["Command"], "Custom-drug command")
    require_exact_fields(command, COMMAND_FIELDS, "Custom-drug command")
    if command["Selection"] != selection:
        raise RuntimeError("Reviewed command does not bind the exact selected recipe")
    if (
        command["ExpectedContentRevision"] != initial.content_revision
        or type(command["ExpectedContentRevision"]) is not int
    ):
        raise RuntimeError("Custom-drug command does not bind the imported revision")
    if command["ExpectedCharacterDigest"] != initial.payload_sha256:
        raise RuntimeError("Custom-drug command does not bind the imported XML digest")
    for key in ("ExpectedCharacterDigest", "ExpectedCatalogDigest", "ExpectedRulesDigest", "ExpectedQuoteDigest"):
        require_digest(command[key], f"Command {key}")
    for checkpoint_key in (
        "BoundCharacterDigest",
        "BoundCatalogDigest",
        "BoundRulesDigest",
    ):
        require_digest(checkpoint[checkpoint_key], f"Checkpoint {checkpoint_key}")
    if checkpoint["BoundCatalogDigest"] != command["ExpectedCatalogDigest"]:
        raise RuntimeError("Checkpoint catalog digest differs from the reviewed command")
    if checkpoint["BoundRulesDigest"] != command["ExpectedRulesDigest"]:
        raise RuntimeError("Checkpoint rules digest differs from the reviewed command")
    idempotency = command["IdempotencyKey"]
    if not isinstance(idempotency, str) or IDEMPOTENCY_KEY.fullmatch(idempotency) is None:
        raise RuntimeError("Custom-drug idempotency key is not exact")
    drug_id = value_wrapper(command["NewDrugInstanceId"], "New drug identity")
    component_ids = command["NewComponentInstanceIds"]
    if not isinstance(component_ids, list) or len(component_ids) != 1:
        raise RuntimeError("Custom-drug command must create one component identity")
    component_id = require_guid(component_ids[0], "New component identity")
    if len({drug_id, component_id, GRADE_ID, FOUNDATION_ID}) != 4:
        raise RuntimeError("Custom-drug command identities are not distinct")

    receipt_value = checkpoint["Receipt"]
    if phase == 1:
        if persisted is not None or receipt_value is not None:
            raise RuntimeError("Reviewed custom-drug checkpoint unexpectedly has a receipt")
        if (
            checkpoint["BoundContentRevision"] != initial.content_revision
            or type(checkpoint["BoundContentRevision"]) is not int
        ):
            raise RuntimeError("Reviewed checkpoint revision is not exact")
        if checkpoint["BoundCharacterDigest"] != initial.payload_sha256:
            raise RuntimeError("Reviewed checkpoint character digest is not exact")
        return command, None

    if phase != 3 or persisted is None:
        raise RuntimeError("Only reviewed and applied custom-drug checkpoints are supported")
    if (
        checkpoint["BoundContentRevision"] != persisted.content_revision
        or type(checkpoint["BoundContentRevision"]) is not int
    ):
        raise RuntimeError("Applied checkpoint revision is not the saved revision")
    if checkpoint["BoundCharacterDigest"] != persisted.payload_sha256:
        raise RuntimeError("Applied checkpoint character digest is not the saved XML digest")
    for checkpoint_key, command_key in (
        ("BoundCatalogDigest", "ExpectedCatalogDigest"),
        ("BoundRulesDigest", "ExpectedRulesDigest"),
    ):
        if checkpoint[checkpoint_key] != command[command_key]:
            raise RuntimeError(f"Applied checkpoint {checkpoint_key} drifted")

    receipt = require_object(receipt_value, "Custom-drug receipt")
    require_exact_fields(receipt, RECEIPT_FIELDS, "Custom-drug receipt")
    if (
        receipt["PreviousContentRevision"] != initial.content_revision
        or type(receipt["PreviousContentRevision"]) is not int
    ):
        raise RuntimeError("Receipt previous revision is not exact")
    if (
        receipt["ContentRevision"] != persisted.content_revision
        or type(receipt["ContentRevision"]) is not int
    ):
        raise RuntimeError("Receipt saved revision is not exact")
    exact_bindings = {
        "PreviousCharacterDigest": initial.payload_sha256,
        "CharacterDigest": persisted.payload_sha256,
        "CatalogDigest": command["ExpectedCatalogDigest"],
        "RulesDigest": command["ExpectedRulesDigest"],
        "QuoteDigest": command["ExpectedQuoteDigest"],
        "CommandDigest": command_digest(command),
        "IdempotencyKeyDigest": hashlib.sha256(idempotency.encode("utf-8")).hexdigest(),
    }
    for key, expected in exact_bindings.items():
        if receipt[key] != expected:
            raise RuntimeError(f"Custom-drug receipt {key} is not exact")
        require_digest(receipt[key], f"Receipt {key}")
    if value_wrapper(receipt["DrugInstanceId"], "Receipt drug identity") != drug_id:
        raise RuntimeError("Receipt drug identity differs from the reviewed command")
    if receipt["ComponentInstanceIds"] != component_ids:
        raise RuntimeError("Receipt component identities differ from the reviewed command")
    require_digest(receipt["DrugXmlDigest"], "Receipt drug XML digest")
    if receipt["ReceiptDigest"] != receipt_digest(receipt):
        raise RuntimeError("Custom-drug receipt digest is not canonical")
    return command, receipt


def read_checkpoint(
    device: shared.Device,
    workspace_id: str,
) -> CheckpointSnapshot:
    listing = device.shell(
        "run-as", shared.PACKAGE, "find", "shared_prefs", "-type", "f", "-name", "*.xml"
    )
    matches: list[tuple[str, str]] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as error:
            raise RuntimeError(f"Shared-preference file is malformed: {path}") from error
        matches.extend(
            (element.get("name", ""), element.text or "")
            for element in root.findall("string")
            if element.get("name", "").startswith(CHECKPOINT_KEY_PREFIX)
        )
    expected_key = CHECKPOINT_KEY_PREFIX + hashlib.sha256(
        workspace_id.encode("utf-8")
    ).hexdigest()
    if len(matches) != 1 or matches[0][0] != expected_key or not matches[0][1]:
        raise RuntimeError("Expected one exact workspace-bound custom-drug checkpoint")
    serialized = matches[0][1]
    try:
        payload = json.loads(serialized, object_pairs_hook=object_without_duplicates)
    except (json.JSONDecodeError, RuntimeError) as error:
        raise RuntimeError("Custom-drug checkpoint is not strict JSON") from error
    if not isinstance(payload, dict):
        raise RuntimeError("Custom-drug checkpoint is not an object")
    return CheckpointSnapshot(
        payload,
        hashlib.sha256(serialized.encode("utf-8")).hexdigest(),
    )


def workspace_payload(device: shared.Device, authority: shared.WorkspaceAuthority) -> str:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    matches: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
        try:
            record = json.loads(raw, object_pairs_hook=object_without_duplicates)
        except (json.JSONDecodeError, RuntimeError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if (
            isinstance(payload, str)
            and hashlib.sha256(payload.encode("utf-8")).hexdigest()
            == authority.payload_sha256
        ):
            matches.append(payload)
    if len(matches) != 1:
        raise RuntimeError("Saved custom-drug XML is not uniquely bound to workspace authority")
    return matches[0]


def assert_imported_xml(payload: str) -> None:
    root = ET.fromstring(payload)
    if root.findtext("alias") != "CareerCustomDrugWizardE2E":
        raise RuntimeError("Imported custom-drug fixture identity is not exact")
    if root.findtext("created") != "True" or root.findtext("gameedition") != "SR5":
        raise RuntimeError("Imported custom-drug fixture is not a created SR5 runner")
    if root.findtext("settings") != "67e25032-2a4e-42ca-97fa-69f7f608236c":
        raise RuntimeError("Imported custom-drug fixture lacks the exact CF source profile")
    if root.findtext("nuyen") != "10000" or root.findall("./expenses/expense"):
        raise RuntimeError("Imported custom-drug fixture has unexpected commerce state")
    if len(root.findall("./drugs")) != 1 or root.findall("./drugs/drug"):
        raise RuntimeError("Imported custom-drug container is not uniquely empty")
    sentinel = root.find("./customstate/sentinel")
    if (
        sentinel is None
        or sentinel.get("guid") != "custom-drug-unrelated-state"
        or sentinel.text != "keep-nested-structure"
    ):
        raise RuntimeError("Imported custom-drug unrelated sentinel is not exact")


def assert_persisted_xml(
    payload: str,
    command: dict[str, object],
    receipt: dict[str, object],
) -> dict[str, object]:
    root = ET.fromstring(payload)
    drug_id = value_wrapper(command["NewDrugInstanceId"], "Saved drug identity")
    component_id = require_guid(
        require_object(receipt, "Saved receipt")["ComponentInstanceIds"][0],
        "Saved component identity",
    )
    drugs = root.findall("./drugs/drug")
    matches = [drug for drug in drugs if drug.findtext("guid") == drug_id]
    if len(drugs) != 1 or len(matches) != 1:
        raise RuntimeError("Saved XML does not contain exactly the reviewed drug identity")
    drug = matches[0]
    expected_drug_scalars = {
        "sourceid": EMPTY_GUID,
        "guid": drug_id,
        "name": RECIPE_NAME,
        "category": "Custom Drug",
        "quantity": "1",
        "availability": "0",
        "grade": GRADE_NAME,
        "sortorder": "0",
        "stolen": "False",
        "source": "",
        "page": "",
        "notes": "",
        "notesColor": "Chocolate",
    }
    for key, expected in expected_drug_scalars.items():
        if drug.findtext(key, default="") != expected:
            raise RuntimeError(f"Saved custom-drug field {key} is not exact")
    components = drug.findall("./drugcomponents/drugcomponent")
    if len(components) != 1:
        raise RuntimeError("Saved custom drug does not have exactly one component")
    component = components[0]
    expected_component_scalars = {
        "sourceid": FOUNDATION_ID,
        "guid": component_id,
        "name": FOUNDATION_NAME,
        "category": "Foundation",
        "availability": "+4R",
        "cost": "75",
        "level": "0",
        "limit": "1",
        "rating": "6",
        "threshold": "2",
        "source": "CF",
        "page": "190",
    }
    for key, expected in expected_component_scalars.items():
        if component.findtext(key, default="") != expected:
            raise RuntimeError(f"Saved custom-drug component field {key} is not exact")
    effect = component.find("./effects/effect")
    if effect is None or len(component.findall("./effects/effect")) != 1:
        raise RuntimeError("Saved custom-drug Foundation effects are not exact")
    attributes = {
        item.findtext("name"): item.findtext("value")
        for item in effect.findall("attribute")
    }
    qualities = [
        (item.text, item.get("rating")) for item in effect.findall("quality")
    ]
    if effect.findtext("level") != "0" or attributes != {
        "BOD": "2",
        "CHA": "-2",
        "WIL": "1",
    }:
        raise RuntimeError("Saved custom-drug Foundation attribute effects are not exact")
    if qualities != [("High Pain Tolerance", "3")]:
        raise RuntimeError("Saved custom-drug Foundation quality effect is not exact")
    if root.findtext("nuyen") != "10000" or root.findall("./expenses/expense"):
        raise RuntimeError("Free initial dose changed Nuyen or created an expense")
    sentinel = root.find("./customstate/sentinel")
    if (
        sentinel is None
        or sentinel.get("guid") != "custom-drug-unrelated-state"
        or sentinel.text != "keep-nested-structure"
    ):
        raise RuntimeError("Custom-drug mutation changed unrelated XML")

    target = re.compile(
        rf"<drug><sourceid>{EMPTY_GUID}</sourceid><guid>{re.escape(drug_id)}</guid>.*?</drug>"
    ).search(payload)
    if target is None:
        raise RuntimeError("Could not bind the exact serialized custom-drug XML bytes")
    drug_xml_digest = hashlib.sha256(target.group(0).encode("utf-8")).hexdigest()
    if receipt["DrugXmlDigest"] != drug_xml_digest:
        raise RuntimeError("Receipt does not bind the exact serialized custom-drug XML")
    return {
        "drugInstanceId": drug_id,
        "componentInstanceIds": [component_id],
        "drugXmlDigest": drug_xml_digest,
    }


def open_recipe(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=16)
    device.tap_bidirectional(
        "build-career-commerce",
        timeout=120,
        backward_scrolls=16,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait_for_single_exact_resource_id(
        "sr5-career-commerce-hub",
        timeout=90,
        evidence_prefix="custom-drug-commerce-hub",
        surface_name="SR5 Career commerce hub",
    )
    device.tap_single_exact_resource_id(
        "sr5-career-custom-drug-recipe",
        timeout=90,
        evidence_prefix="custom-drug-route",
        surface_name="SR5 Career custom-drug route",
    )
    device.wait_for_single_exact_resource_id(
        "sr5-career-custom-drug-recipe-page",
        timeout=120,
        evidence_prefix="custom-drug-page",
        surface_name="SR5 Career custom-drug page",
    )


def choose_recipe(device: shared.Device) -> None:
    device.tap_bidirectional(
        "career-custom-drug-grade-route",
        timeout=90,
        backward_scrolls=16,
        forward_scrolls=16,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait_for_single_exact_resource_id(
        "sr5-career-custom-drug-grade-page",
        timeout=60,
        evidence_prefix="custom-drug-grade-page",
        surface_name="Custom-drug grade page",
    )
    device.tap_single_exact_resource_id(
        "career-custom-drug-grade-" + GRADE_ID.replace("-", ""),
        timeout=60,
        evidence_prefix="custom-drug-grade",
        surface_name="Standard custom-drug grade",
    )
    device.tap_bidirectional(
        "career-custom-drug-components-route",
        timeout=90,
        backward_scrolls=16,
        forward_scrolls=16,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait_for_single_exact_resource_id(
        "sr5-career-custom-drug-component-page",
        timeout=60,
        evidence_prefix="custom-drug-component-page",
        surface_name="Custom-drug component page",
    )
    device.tap_bidirectional(
        "career-custom-drug-component-"
        + FOUNDATION_ID.replace("-", "")
        + f"-{FOUNDATION_LEVEL}",
        timeout=90,
        backward_scrolls=12,
        forward_scrolls=36,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.set_text(
        "career-custom-drug-recipe-name",
        "Enter a bounded recipe name",
        RECIPE_NAME,
        scroll=True,
    )
    device.tap_bidirectional(
        "career-custom-drug-recipe-update",
        timeout=90,
        backward_scrolls=18,
        forward_scrolls=18,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait_for_single_exact_resource_id(
        "career-custom-drug-quote",
        timeout=90,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="custom-drug-quote",
        surface_name="Exact Core custom-drug quote",
    )


def same_process_reopen(device: shared.Device) -> None:
    device.back()
    device.wait_for_single_exact_resource_id(
        "sr5-career-commerce-hub",
        timeout=60,
        evidence_prefix="custom-drug-same-process-hub",
        surface_name="SR5 Career commerce hub after receipt",
    )
    device.tap_single_exact_resource_id(
        "sr5-career-custom-drug-recipe",
        timeout=60,
        evidence_prefix="custom-drug-same-process-reopen",
        surface_name="Custom-drug same-process reopen route",
    )
    device.wait_for_single_exact_resource_id(
        "career-custom-drug-receipt",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="custom-drug-same-process-receipt",
        surface_name="Custom-drug same-process receipt",
    )


def prove_journey(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture.name)
    device.wait("CareerCustomDrugWizardE2E", timeout=120)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    imported = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(imported, fixture_sha256)
    shared.require_saved_authority(imported)
    assert_imported_xml(workspace_payload(device, imported))

    open_recipe(device)
    choose_recipe(device)
    device.tap_bidirectional(
        "career-custom-drug-review",
        timeout=90,
        backward_scrolls=18,
        forward_scrolls=18,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait_for_single_exact_resource_id(
        "career-custom-drug-review-diff",
        timeout=90,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="custom-drug-review-diff",
        surface_name="Custom-drug exact review diff",
    )
    reviewed = read_checkpoint(device, imported.workspace_id)
    reviewed_command, _ = validate_checkpoint(
        reviewed.payload,
        workspace_id=imported.workspace_id,
        initial=imported,
        persisted=None,
        phase=1,
    )
    reviewed_drug_id = value_wrapper(
        reviewed_command["NewDrugInstanceId"], "Reviewed drug identity"
    )
    device.wait(reviewed_drug_id, timeout=60, scroll=True, max_scrolls=24)
    device.capture("custom-drug-reviewed")

    device.tap_bidirectional(
        "career-custom-drug-confirm",
        timeout=120,
        backward_scrolls=18,
        forward_scrolls=18,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    device.wait("Confirm exact custom-drug recipe", timeout=30)
    device.tap("Confirm")
    device.wait_for_single_exact_resource_id(
        "career-custom-drug-receipt",
        timeout=180,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="custom-drug-applied-receipt",
        surface_name="Custom-drug applied receipt",
    )
    device.wait(reviewed_drug_id, timeout=60, scroll=True, max_scrolls=24)
    same_process_checkpoint = read_checkpoint(device, imported.workspace_id)
    same_process_reopen(device)
    reopened_checkpoint = read_checkpoint(device, imported.workspace_id)
    if reopened_checkpoint != same_process_checkpoint:
        raise RuntimeError("Custom-drug receipt bytes changed across same-process reopen")

    persisted = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(persisted)
    if persisted.workspace_id != imported.workspace_id:
        raise RuntimeError("Custom-drug wizard changed workspace identity")
    if persisted.content_revision != imported.content_revision + 1:
        raise RuntimeError("Custom-drug wizard did not save one atomic successor revision")
    applied_command, applied_receipt = validate_checkpoint(
        same_process_checkpoint.payload,
        workspace_id=imported.workspace_id,
        initial=imported,
        persisted=persisted,
        phase=3,
    )
    if applied_command != reviewed_command or applied_receipt is None:
        raise RuntimeError("Applied receipt does not bind the exact reviewed command")
    xml_projection = assert_persisted_xml(
        workspace_payload(device, persisted), applied_command, applied_receipt
    )

    restarted = shared.force_stop_and_launch_new_process(device, launch)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(persisted, restored)
    open_recipe(device)
    device.wait_for_single_exact_resource_id(
        "career-custom-drug-receipt",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.18,
        evidence_prefix="custom-drug-restarted-receipt",
        surface_name="Custom-drug restarted receipt",
    )
    device.wait(reviewed_drug_id, timeout=60, scroll=True, max_scrolls=24)
    restarted_checkpoint = read_checkpoint(device, imported.workspace_id)
    if restarted_checkpoint != same_process_checkpoint:
        raise RuntimeError("Custom-drug checkpoint bytes changed across process restart")
    restarted_command, restarted_receipt = validate_checkpoint(
        restarted_checkpoint.payload,
        workspace_id=imported.workspace_id,
        initial=imported,
        persisted=restored,
        phase=3,
    )
    if restarted_command != reviewed_command or restarted_receipt != applied_receipt:
        raise RuntimeError("Restarted custom-drug receipt differs from reviewed authority")
    restarted_xml_projection = assert_persisted_xml(
        workspace_payload(device, restored), restarted_command, restarted_receipt
    )
    if restarted_xml_projection != xml_projection:
        raise RuntimeError("Custom-drug XML identity projection changed after restart")
    device.capture("custom-drug-restarted-exact-receipt")

    return {
        "importAuthority": shared.workspace_authority_json(imported),
        "persistedAuthority": shared.workspace_authority_json(persisted),
        "restoredAuthority": shared.workspace_authority_json(restored),
        "reviewedCheckpoint": reviewed.payload,
        "reviewedCheckpointSha256": reviewed.serialized_sha256,
        "appliedCheckpoint": same_process_checkpoint.payload,
        "appliedCheckpointSha256": same_process_checkpoint.serialized_sha256,
        "sameProcessReopenCheckpointSha256": reopened_checkpoint.serialized_sha256,
        "processRestartCheckpointSha256": restarted_checkpoint.serialized_sha256,
        "xmlProjection": xml_projection,
        "processRestart": {
            "before": list(restarted.before_force_stop.process_ids),
            "afterForceStop": list(restarted.after_force_stop.process_ids),
            "restarted": list(restarted.restarted.process_ids),
        },
    }


def write_receipt(path: Path, value: dict[str, object]) -> None:
    path = path.resolve(strict=False)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and (path.is_symlink() or not path.is_file()):
        raise RuntimeError("Receipt target must be a regular non-symlink file")
    encoded = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(encoded)
        stream.flush()
    temporary.replace(path)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--career-runner", type=Path, default=FIXTURE)
    return parser.parse_args(argv)


def execute(args: argparse.Namespace) -> dict[str, object]:
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    apk = args.apk.resolve()
    fixture = args.career_runner.resolve()
    if fixture != FIXTURE.resolve():
        raise RuntimeError("Custom-drug proof requires the committed governed fixture")
    build_provenance = load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "recipePageSha256": android_root / "src/Chummer.Android/Native/Sr5CareerCustomDrugRecipePages.cs",
        "recipeModelSha256": android_root / "src/Chummer.Android/Native/Sr5CareerCustomDrugRecipeModel.cs",
        "recipeServiceSha256": android_root / "src/Chummer.Android/Native/Sr5CareerCustomDrugRecipeService.cs",
        "recipeStoreSha256": android_root / "src/Chummer.Android/Native/AndroidSr5CareerCustomDrugStores.cs",
        "commerceHubSha256": android_root / "src/Chummer.Android/Native/Sr5CareerCommercePages.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "customDrugRulesSha256": core_root / "Chummer.Contracts/Characters/CharacterCustomDrugRules.cs",
        "customDrugAuthoritySha256": core_root / "Chummer.Infrastructure/Xml/FileSystemCharacterCustomDrugAuthority.cs",
        "sourceResolverSha256": core_root / "Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "workspaceStoreSha256": core_root / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "drugCatalogSha256": core_root / "Chummer/data/drugcomponents.xml",
        "settingsCatalogSha256": core_root / "Chummer/data/settings.xml",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Custom-drug E2E source graph is incomplete: {missing!r}")
    fixture_sha256 = shared.sha256(fixture)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if api != "36" or abi != "x86_64":
        raise RuntimeError(
            f"Custom-drug E2E requires hosted API 36 x86_64, got API {api!r} ABI {abi!r}"
        )
    apk_sha256 = shared.sha256(apk)
    device.install_verified(apk, apk_sha256, "--no-streaming", "-r")
    verified_remote = device.push_verified(
        fixture, f"/sdcard/Download/{fixture.name}", fixture_sha256
    )
    journey = prove_journey(device, fixture, fixture_sha256)
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": JOURNEY,
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apkSha256": apk_sha256,
        "driverSha256": shared.sha256(driver),
        "fixtureSha256": fixture_sha256,
        "verifiedRemoteFixtureSha256": verified_remote,
        "buildProvenanceManifestSha256": shared.sha256(
            args.build_provenance_manifest.resolve()
        ),
        "buildProvenance": build_provenance,
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "authorityProofStages": journey,
        "gateRegistered": False,
        "aggregateJourneyCountContribution": 0,
        "publicationAuthorized": False,
        "releaseClaim": "none",
        "nonClaims": [
            "not part of the seven-journey Android aggregate",
            "no Google Play or public release authorization",
            "no generic or full editing parity",
            "no tablet support",
            "no later custom-drug quantity purchase",
            "no Creation custom-drug finalization",
        ],
    }


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    receipt_path = args.receipt
    try:
        receipt = execute(args)
    except (RuntimeError, OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        write_receipt(
            receipt_path,
            {
                "schema": RECEIPT_SCHEMA,
                "status": "fail",
                "executionStatus": "fail",
                "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                "profile": "phone",
                "journey": JOURNEY,
                "gateRegistered": False,
                "aggregateJourneyCountContribution": 0,
                "publicationAuthorized": False,
                "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
            },
        )
        print(f"SR5 Career Custom Drug E2E failed: {error}", file=sys.stderr)
        return 1
    write_receipt(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
