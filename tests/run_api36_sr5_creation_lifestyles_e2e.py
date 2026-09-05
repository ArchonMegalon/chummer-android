#!/usr/bin/env python3
"""Prove the dedicated SR5 creation Lifestyles wizard on a physical API 36 phone."""

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
import time
import xml.etree.ElementTree as ET

import api36_physical_build_provenance as build_provenance
import run_api36_editing_e2e as shared
import run_api36_sr5_career_active_skill_wizard_e2e as physical
import run_api36_sr5_creation_contacts_e2e as contacts


RECEIPT_SCHEMA = "chummer.android.sr5-creation-lifestyles-physical-e2e/v1"
DISPOSABLE_DEVICE_FLAG = "--allow-destructive-disposable-device"
STAGE_ID = "creation-stage-contacts-lifestyles"
CATALOG_PREFIX = "creation-lifestyle-catalog-"
LIFESTYLE_ITEM_PREFIX = "creation-lifestyle-item-"
CREATED_NAME = "LifestyleE2EWizard"
CREATED_CITY = "ViennaE2E"
CREATED_DISTRICT = "InnereStadtE2E"
CREATED_INCREMENTS = "2"
CANONICAL_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
SHORT_DIGEST = re.compile(r"^(?:sha256:)?[0-9a-f]{12,19}$")
CANONICAL_GUID = re.compile(
    r"^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"
)
LIST_BINDING = re.compile(
    r"^Revision (?P<content>[0-9]+) · saved (?P<saved>[0-9]+) · "
    r"snapshot (?P<snapshot>[^ ]+) · source (?P<source>[^ ]+)$"
)
EDIT_BINDING = re.compile(
    r"^Revision (?P<content>[0-9]+) · snapshot (?P<snapshot>[^ ]+) · "
    r"Lifestyle (?P<lifestyle>[0-9a-f-]{36})$"
)
PREVIEW_BINDING = re.compile(
    r"^Revision (?P<content>[0-9]+) · saved (?P<saved>[0-9]+) · "
    r"Create (?P<lifestyle>[0-9a-f-]{36})$"
)
RECEIPT_ID = re.compile(r"^creation-lifestyle-[0-9a-f]{24}$")
BLOCKER_IDS = (
    "creation-lifestyles-unavailable",
    "creation-lifestyles-blockers",
    "creation-lifestyle-edit-blockers",
    "creation-lifestyle-not-found",
    "creation-lifestyle-draft-stale",
    "creation-lifestyle-preview-blockers",
    "creation-lifestyle-confirm-blockers",
)
PREVIEW_RECEIPT_IDS = (
    "creation-lifestyle-receipt-id",
    "creation-lifestyle-receipt-digest",
    "creation-lifestyle-receipt-content-before",
    "creation-lifestyle-receipt-content-after",
    "creation-lifestyle-receipt-source",
    "creation-lifestyle-receipt-rules",
    "creation-lifestyle-receipt-runtime",
)


resource_id = contacts.resource_id
node_value = contacts.node_value
scan_exact_resources = contacts.scan_exact_resources


@dataclass(frozen=True)
class CatalogOptionEvidence:
    resource_id: str
    label: str


@dataclass(frozen=True)
class LifestyleReceiptProjection:
    receipt_id: str
    receipt_digest: str
    content_before: str
    content_after: str
    source_digest: str
    rules_digest: str
    runtime_digest: str


def require_control_state(
    device: shared.Device,
    selector: str,
    *,
    enabled: bool,
    checked: bool | None = None,
) -> shared.UiNode:
    node = device.wait_exact_resource_id_bidirectional(
        selector,
        timeout=120,
        backward_scrolls=32,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        evidence_prefix=f"lifestyles-control-{selector}",
        surface_name="Creation Lifestyles control",
        require_tappable=False,
    )
    expected_enabled = "true" if enabled else "false"
    if node.attributes.get("enabled") != expected_enabled:
        device.capture(f"lifestyles-control-{selector}-enabled-invalid")
        raise RuntimeError(
            f"Control {selector!r} enabled state differs: expected "
            f"{expected_enabled}, got {node.attributes.get('enabled')!r}"
        )
    if checked is not None and node.attributes.get("checked") != (
        "true" if checked else "false"
    ):
        device.capture(f"lifestyles-control-{selector}-checked-invalid")
        raise RuntimeError(f"Control {selector!r} checked state differs")
    if enabled and (
        node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node)
    ):
        device.capture(f"lifestyles-control-{selector}-not-tappable")
        raise RuntimeError(f"Enabled control {selector!r} is not clickable and tappable")
    return node


def tap_enabled_control(device: shared.Device, selector: str) -> None:
    node = require_control_state(device, selector, enabled=True)
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))


def set_exact_text(device: shared.Device, selector: str, value: str) -> None:
    node = require_control_state(device, selector, enabled=True)
    if node.attributes.get("focusable") != "true":
        raise RuntimeError(f"Text field {selector!r} is not focusable")
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))
    time.sleep(0.4)
    focused = device.find_exact_resource_id(selector)
    if focused is None or focused.attributes.get("focused") != "true":
        device.capture(f"lifestyles-field-{selector}-focus-failed")
        raise RuntimeError(f"Text field {selector!r} did not receive focus")
    device.shell("input", "keycombination", "113", "29")
    device.shell("input", "text", value.replace(" ", "%s"))
    time.sleep(0.35)
    updated = device.find_exact_resource_id(selector)
    if updated is None or updated.attributes.get("text") != value:
        device.capture(f"lifestyles-field-{selector}-value-failed")
        raise RuntimeError(f"Text field {selector!r} did not render the exact value")
    device.dismiss_keyboard()


def labeled_value(attributes: dict[str, str], title: str, label: str) -> str:
    rendered = node_value(attributes, label)
    prefix = f"{title} · "
    if not rendered.startswith(prefix) or len(rendered) == len(prefix):
        raise RuntimeError(f"{label} is not bound to the exact {title!r} label")
    return rendered[len(prefix) :]


def canonical_digest(value: str, label: str) -> str:
    if CANONICAL_DIGEST.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not one canonical sha256: digest")
    return value


def parse_list_binding(value: str) -> dict[str, object]:
    match = LIST_BINDING.fullmatch(value)
    if match is None:
        raise RuntimeError("Creation Lifestyles list binding is malformed")
    parsed: dict[str, object] = {
        "contentRevision": int(match.group("content")),
        "savedRevision": int(match.group("saved")),
        "snapshot": match.group("snapshot"),
        "source": match.group("source"),
    }
    for field in ("snapshot", "source"):
        if SHORT_DIGEST.fullmatch(str(parsed[field])) is None:
            raise RuntimeError(f"Creation Lifestyles {field} short digest is malformed")
    return parsed


def parse_edit_binding(value: str, expected_lifestyle_id: str | None = None) -> dict[str, object]:
    match = EDIT_BINDING.fullmatch(value)
    if match is None:
        raise RuntimeError("Creation Lifestyle edit binding is malformed")
    lifestyle_id = match.group("lifestyle")
    if CANONICAL_GUID.fullmatch(lifestyle_id) is None:
        raise RuntimeError("Creation Lifestyle edit identity is not canonical")
    if expected_lifestyle_id is not None and lifestyle_id != expected_lifestyle_id:
        raise RuntimeError("Creation Lifestyle edit identity drifted")
    snapshot = match.group("snapshot")
    if SHORT_DIGEST.fullmatch(snapshot) is None:
        raise RuntimeError("Creation Lifestyle edit snapshot digest is malformed")
    return {
        "contentRevision": int(match.group("content")),
        "snapshot": snapshot,
        "lifestyleId": lifestyle_id,
    }


def parse_preview_binding(value: str, expected_lifestyle_id: str) -> dict[str, object]:
    match = PREVIEW_BINDING.fullmatch(value)
    if match is None or match.group("lifestyle") != expected_lifestyle_id:
        raise RuntimeError("Creation Lifestyle preview binding is malformed or identity-drifted")
    return {
        "contentRevision": int(match.group("content")),
        "savedRevision": int(match.group("saved")),
        "lifestyleId": match.group("lifestyle"),
    }


def receipt_projection(values: dict[str, dict[str, str]]) -> LifestyleReceiptProjection:
    rendered = {
        "receipt_id": labeled_value(
            values["creation-lifestyle-receipt-id"],
            "Receipt ID",
            "creation-lifestyle-receipt-id",
        ),
        "receipt_digest": labeled_value(
            values["creation-lifestyle-receipt-digest"],
            "Receipt digest",
            "creation-lifestyle-receipt-digest",
        ),
        "content_before": labeled_value(
            values["creation-lifestyle-receipt-content-before"],
            "Content before",
            "creation-lifestyle-receipt-content-before",
        ),
        "content_after": labeled_value(
            values["creation-lifestyle-receipt-content-after"],
            "Content after",
            "creation-lifestyle-receipt-content-after",
        ),
        "source_digest": labeled_value(
            values["creation-lifestyle-receipt-source"],
            "Source",
            "creation-lifestyle-receipt-source",
        ),
        "rules_digest": labeled_value(
            values["creation-lifestyle-receipt-rules"],
            "Rules",
            "creation-lifestyle-receipt-rules",
        ),
        "runtime_digest": labeled_value(
            values["creation-lifestyle-receipt-runtime"],
            "Runtime",
            "creation-lifestyle-receipt-runtime",
        ),
    }
    if RECEIPT_ID.fullmatch(rendered["receipt_id"]) is None:
        raise RuntimeError("Creation Lifestyle receipt ID is malformed")
    for field in (
        "receipt_digest",
        "content_before",
        "content_after",
        "source_digest",
        "rules_digest",
        "runtime_digest",
    ):
        canonical_digest(rendered[field], field.replace("_", " "))
    return LifestyleReceiptProjection(**rendered)


def validate_receipt_projection(
    receipt: LifestyleReceiptProjection,
    *,
    imported: shared.WorkspaceAuthority,
    saved: shared.WorkspaceAuthority,
) -> None:
    successor = imported.content_revision + 1
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Creation Lifestyle mutation changed workspace identity")
    if not (
        saved.content_revision == successor
        and saved.saved_revision == successor
    ):
        raise RuntimeError("Lifestyle receipt does not lead to one exact saved successor")
    if receipt.content_before != f"sha256:{imported.payload_sha256}":
        raise RuntimeError("Lifestyle receipt content-before is not bound to the import payload")
    if receipt.content_after != f"sha256:{saved.payload_sha256}":
        raise RuntimeError("Lifestyle receipt content-after is not bound to the saved payload")
    if receipt.content_before == receipt.content_after:
        raise RuntimeError("Creation Lifestyle receipt proves no payload change")


def collect_catalog_options(
    device: shared.Device,
    *,
    max_scrolls: int = 48,
) -> list[CatalogOptionEvidence]:
    """Collect the exact enabled Core catalog rows and reject ambiguous/blocker surfaces."""
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    observed: dict[str, CatalogOptionEvidence] = {}
    previous_signature: tuple[tuple[str, str, str], ...] | None = None
    stable_signatures = 0
    for scroll in range(max_scrolls + 1):
        nodes = device.hierarchy()
        by_id: dict[str, list[shared.UiNode]] = {}
        for node in nodes:
            identifier = resource_id(node)
            if identifier in BLOCKER_IDS or identifier.startswith(CATALOG_PREFIX):
                by_id.setdefault(identifier, []).append(node)
        for identifier, matches in by_id.items():
            if len(matches) != 1:
                device.capture("creation-lifestyles-catalog-cardinality-invalid")
                raise RuntimeError(
                    "Creation Lifestyle catalog resource "
                    f"{identifier!r} has cardinality {len(matches)}"
                )
            node = matches[0]
            if identifier in BLOCKER_IDS:
                device.capture("creation-lifestyles-catalog-blocker-visible")
                raise RuntimeError(f"Fail-closed Lifestyle blocker is visible: {identifier}")
            if node.attributes.get("enabled") != "true":
                continue
            if (
                node.attributes.get("clickable") != "true"
                or not device.node_has_tappable_bounds(node)
            ):
                device.capture("creation-lifestyles-catalog-option-not-tappable")
                raise RuntimeError(f"Enabled catalog option {identifier!r} is not tappable")
            label = node_value(
                node.attributes,
                f"Creation Lifestyle catalog option {identifier!r}",
            )
            candidate = CatalogOptionEvidence(identifier, label)
            if identifier in observed and observed[identifier] != candidate:
                device.capture("creation-lifestyles-catalog-option-drift")
                raise RuntimeError(f"Catalog option {identifier!r} changed while scanning")
            observed[identifier] = candidate
        signature = tuple(
            sorted(
                (
                    resource_id(node),
                    node.attributes.get("enabled", ""),
                    node.attributes.get("bounds", ""),
                )
                for node in nodes
                if resource_id(node).startswith(CATALOG_PREFIX)
            )
        )
        stable_signatures = stable_signatures + 1 if signature == previous_signature else 0
        previous_signature = signature
        if observed and stable_signatures >= 1:
            return sorted(observed.values(), key=lambda option: option.resource_id)
        if scroll < max_scrolls:
            device.swipe_up(distance_ratio=0.18)
            time.sleep(0.45)
    device.capture("creation-lifestyles-catalog-option-missing")
    raise RuntimeError("No exact enabled Creation Lifestyle catalog option was available")


def workspace_payloads(device: shared.Device) -> list[str]:
    return contacts.workspace_payloads(device)


def root_for_authority(device: shared.Device, authority: shared.WorkspaceAuthority) -> ET.Element:
    matches = [
        payload
        for payload in workspace_payloads(device)
        if hashlib.sha256(payload.encode("utf-8")).hexdigest() == authority.payload_sha256
    ]
    if len(matches) != 1:
        device.capture("creation-lifestyles-authority-payload-ambiguous")
        raise RuntimeError(
            f"Expected one Lifestyle payload bound to authority, got {len(matches)}"
        )
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != "CreationLifestylesE2E" or root.findtext("created") != "False":
        raise RuntimeError("Workspace authority selected a different or career runner")
    return root


def assert_fixture_state(
    root: ET.Element,
    *,
    lifestyle_id: str | None,
) -> None:
    if root.findtext("customstate") != "Creation Lifestyles unrelated state":
        raise RuntimeError("Creation Lifestyle mutation changed unknown root state")
    expected_root = {
        "karma": "35",
        "nuyen": "10000",
        "startingnuyen": "10000",
        "buildmethod": "Priority",
        "gameedition": "SR5",
    }
    for field, expected in expected_root.items():
        if root.findtext(field) != expected:
            raise RuntimeError(f"Creation Lifestyle mutation changed root field {field!r}")
    contacts_found = root.findall("./contacts/contact")
    if len(contacts_found) != 1:
        raise RuntimeError("Creation Lifestyle mutation changed the Contact sibling set")
    contact = contacts_found[0]
    if (
        contact.findtext("guid") != "50d92979-524d-4cb5-898e-196771e3c786"
        or contact.findtext("name") != "LifestyleSiblingContactE2E"
        or contact.findtext("role") != "UnrelatedStateSentinel"
    ):
        raise RuntimeError("Creation Lifestyle mutation changed the unrelated Contact")
    lifestyles = root.findall("./lifestyles/lifestyle")
    if lifestyle_id is None:
        if lifestyles:
            raise RuntimeError("Fresh Creation Lifestyles fixture is not empty")
        return
    matches = [item for item in lifestyles if item.findtext("guid") == lifestyle_id]
    if len(matches) != 1 or len(lifestyles) != 1:
        raise RuntimeError("Saved Lifestyle identity is missing, duplicated, or has a sibling")
    lifestyle = matches[0]
    expected_lifestyle = {
        "name": CREATED_NAME,
        "months": CREATED_INCREMENTS,
        "percentage": "100",
        "roommates": "0",
        "city": CREATED_CITY,
        "district": CREATED_DISTRICT,
        "type": "Standard",
        "increment": "Month",
    }
    for field, expected in expected_lifestyle.items():
        if lifestyle.findtext(field) != expected:
            raise RuntimeError(f"Saved Lifestyle field {field!r} differs from the exact draft")
    source_id = (lifestyle.findtext("sourceid") or "").lower()
    if CANONICAL_GUID.fullmatch(source_id) is None:
        raise RuntimeError("Saved Lifestyle is not bound to one canonical source identity")
    try:
        cost = int(lifestyle.findtext("cost") or "0")
    except ValueError as error:
        raise RuntimeError("Saved Lifestyle cost is not an integer") from error
    if cost <= 0 or not lifestyle.findtext("baselifestyle"):
        raise RuntimeError("Saved Lifestyle lacks exact catalog economics")


def prepare_runner(
    device: shared.Device,
    fixture_name: str,
    fixture_sha256: str,
) -> tuple[shared.LaunchState, shared.WorkspaceAuthority]:
    launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap_single_exact_resource_id(
        "home-open-file",
        timeout=60,
        evidence_prefix="creation-lifestyles-open-file",
        surface_name="Open runner file control",
    )
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, created=False, timeout=180)
    authority = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    shared.require_saved_authority(authority)
    assert_fixture_state(root_for_authority(device, authority), lifestyle_id=None)
    return launch, authority


def open_lifestyles(
    device: shared.Device,
    *,
    expected_lifestyle_id: str | None,
    open_build_route: bool = True,
) -> dict[str, object]:
    shared.open_creation_dashboard(device, open_build_route=open_build_route)
    tap_enabled_control(device, STAGE_ID)
    device.wait_for_single_exact_resource_id(
        "creation-contacts-page",
        timeout=120,
        evidence_prefix="creation-lifestyles-contacts-page",
        surface_name="Creation Contacts/Lifestyles page",
    )
    tap_enabled_control(device, "creation-contacts-open-lifestyles")
    device.wait_for_single_exact_resource_id(
        "creation-lifestyles-page",
        timeout=120,
        evidence_prefix="creation-lifestyles-page",
        surface_name="Creation Lifestyles page",
    )
    expected_item = (
        "creation-lifestyles-empty"
        if expected_lifestyle_id is None
        else LIFESTYLE_ITEM_PREFIX + expected_lifestyle_id.replace("-", "")
    )
    values = scan_exact_resources(
        device,
        (
            "creation-lifestyles-binding",
            "creation-lifestyles-budget",
            "creation-lifestyles-authority",
            expected_item,
        ),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-lifestyles-list",
    )
    return parse_list_binding(
        node_value(values["creation-lifestyles-binding"], "Lifestyles list binding")
    )


def open_editor_from_catalog(
    device: shared.Device,
) -> tuple[CatalogOptionEvidence, dict[str, object]]:
    options = collect_catalog_options(device)
    selected = options[0]
    tap_enabled_control(device, selected.resource_id)
    return selected, read_editor(device)


def read_editor(
    device: shared.Device,
    expected_lifestyle_id: str | None = None,
) -> dict[str, object]:
    device.wait_for_single_exact_resource_id(
        "creation-lifestyle-edit-page",
        timeout=120,
        evidence_prefix="creation-lifestyle-edit-page",
        surface_name="Creation Lifestyle edit page",
    )
    values = scan_exact_resources(
        device,
        (
            "creation-lifestyle-edit-binding",
            "creation-lifestyle-base-option",
            "creation-lifestyle-name",
            "creation-lifestyle-increments",
            "creation-lifestyle-city",
            "creation-lifestyle-district",
            "creation-lifestyle-preview",
        ),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-lifestyle-edit",
    )
    return parse_edit_binding(
        node_value(values["creation-lifestyle-edit-binding"], "Lifestyle edit binding"),
        expected_lifestyle_id,
    )


def assert_editor_values(device: shared.Device) -> None:
    expected = {
        "creation-lifestyle-name": CREATED_NAME,
        "creation-lifestyle-increments": CREATED_INCREMENTS,
        "creation-lifestyle-city": CREATED_CITY,
        "creation-lifestyle-district": CREATED_DISTRICT,
    }
    for selector, value in expected.items():
        node = device.wait_exact_resource_id_bidirectional(
            selector,
            timeout=120,
            backward_scrolls=32,
            forward_scrolls=48,
            scroll_distance_ratio=0.18,
            evidence_prefix=f"creation-lifestyle-readback-{selector}",
            surface_name="Creation Lifestyle field readback",
            require_tappable=False,
        )
        if node.attributes.get("text") != value:
            device.capture(f"creation-lifestyle-readback-{selector}-mismatch")
            raise RuntimeError(f"Reopened Lifestyle field {selector!r} differs")


def assert_reopened_lifestyle(
    device: shared.Device,
    *,
    lifestyle_id: str,
    expected_revision: int,
    open_build_route: bool = True,
) -> dict[str, object]:
    listing = open_lifestyles(
        device,
        expected_lifestyle_id=lifestyle_id,
        open_build_route=open_build_route,
    )
    if (
        listing["contentRevision"] != expected_revision
        or listing["savedRevision"] != expected_revision
    ):
        raise RuntimeError("Reopened Lifestyle list is not bound to the saved revision")
    tap_enabled_control(device, LIFESTYLE_ITEM_PREFIX + lifestyle_id.replace("-", ""))
    editor = read_editor(device, lifestyle_id)
    if editor["contentRevision"] != expected_revision:
        raise RuntimeError("Reopened Lifestyle editor is not bound to the saved revision")
    assert_editor_values(device)
    return {"listBinding": listing, "editBinding": editor}


def prove_lifestyles_journey(
    device: shared.Device,
    fixture_name: str,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture_name, fixture_sha256)
    before_list = open_lifestyles(device, expected_lifestyle_id=None)
    if (
        before_list["contentRevision"] != imported.content_revision
        or before_list["savedRevision"] != imported.saved_revision
    ):
        raise RuntimeError("Initial Lifestyle list differs from imported workspace authority")
    selected_catalog, before_edit = open_editor_from_catalog(device)
    lifestyle_id = str(before_edit["lifestyleId"])
    if before_edit["contentRevision"] != imported.content_revision:
        raise RuntimeError("Creation Lifestyle editor differs from imported workspace authority")
    require_control_state(device, "creation-lifestyle-preview", enabled=True)
    set_exact_text(device, "creation-lifestyle-name", CREATED_NAME)
    set_exact_text(device, "creation-lifestyle-increments", CREATED_INCREMENTS)
    set_exact_text(device, "creation-lifestyle-city", CREATED_CITY)
    set_exact_text(device, "creation-lifestyle-district", CREATED_DISTRICT)
    assert_editor_values(device)
    tap_enabled_control(device, "creation-lifestyle-preview")
    device.wait_for_single_exact_resource_id(
        "creation-lifestyle-preview-page",
        timeout=120,
        evidence_prefix="creation-lifestyle-preview-page",
        surface_name="Creation Lifestyle preview page",
    )
    preview_values = scan_exact_resources(
        device,
        (
            "creation-lifestyle-preview-binding",
            "creation-lifestyle-preview-digest",
            "creation-lifestyle-plan-digest",
            "creation-lifestyle-preview-target",
            "creation-lifestyle-preview-budget-before",
            "creation-lifestyle-preview-budget-after",
            "creation-lifestyle-write-1-create",
            "creation-lifestyle-preview-preservation",
            "creation-lifestyle-explicit-confirm",
            "creation-lifestyle-confirm",
        ),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-lifestyle-preview",
    )
    preview_binding = parse_preview_binding(
        node_value(preview_values["creation-lifestyle-preview-binding"], "preview binding"),
        lifestyle_id,
    )
    if (
        preview_binding["contentRevision"] != imported.content_revision
        or preview_binding["savedRevision"] != imported.saved_revision
    ):
        raise RuntimeError("Lifestyle preview is not bound to imported workspace authority")
    preview_digest = canonical_digest(
        labeled_value(
            preview_values["creation-lifestyle-preview-digest"],
            "Preview digest",
            "preview digest",
        ),
        "preview digest",
    )
    plan_digest = canonical_digest(
        labeled_value(
            preview_values["creation-lifestyle-plan-digest"],
            "Atomic plan digest",
            "plan digest",
        ),
        "plan digest",
    )
    require_control_state(device, "creation-lifestyle-confirm", enabled=False)
    checkbox = require_control_state(
        device,
        "creation-lifestyle-explicit-confirm",
        enabled=True,
        checked=False,
    )
    x, y = checkbox.center
    device.shell("input", "tap", str(x), str(y))
    require_control_state(
        device,
        "creation-lifestyle-explicit-confirm",
        enabled=True,
        checked=True,
    )
    tap_enabled_control(device, "creation-lifestyle-confirm")
    device.wait_for_single_exact_resource_id(
        "creation-lifestyle-confirmed",
        timeout=180,
        evidence_prefix="creation-lifestyle-confirmed",
        surface_name="Creation Lifestyle confirmation result",
    )
    receipt_values = scan_exact_resources(
        device,
        (
            "creation-lifestyle-confirm-receipt",
            *PREVIEW_RECEIPT_IDS,
            "creation-lifestyle-back-to-build",
        ),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-lifestyle-receipt",
    )
    receipt = receipt_projection(receipt_values)
    tap_enabled_control(device, "creation-lifestyle-back-to-build")
    shared.open_creation_dashboard(device, open_build_route=False)
    saved = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(saved)
    validate_receipt_projection(receipt, imported=imported, saved=saved)
    assert_fixture_state(root_for_authority(device, saved), lifestyle_id=lifestyle_id)
    same_session = assert_reopened_lifestyle(
        device,
        lifestyle_id=lifestyle_id,
        expected_revision=saved.content_revision,
    )
    device.capture("creation-lifestyles-same-session-reopen")

    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=False, timeout=180)
    restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(saved, restored)
    assert_fixture_state(root_for_authority(device, restored), lifestyle_id=lifestyle_id)
    after_restart = assert_reopened_lifestyle(
        device,
        lifestyle_id=lifestyle_id,
        expected_revision=restored.content_revision,
    )
    device.capture("creation-lifestyles-process-restart-reopen")
    return {
        "lifestyleId": lifestyle_id,
        "selectedCatalogOption": selected_catalog.__dict__,
        "importedAuthority": shared.workspace_authority_json(imported),
        "savedAuthority": shared.workspace_authority_json(saved),
        "restoredAuthority": shared.workspace_authority_json(restored),
        "initialListBinding": before_list,
        "initialEditBinding": before_edit,
        "previewBinding": preview_binding,
        "previewDigest": preview_digest,
        "planDigest": plan_digest,
        "receipt": receipt.__dict__,
        "sameSession": same_session,
        "afterProcessRestart": after_restart,
        "restartProcessIds": list(restart.restarted.process_ids),
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
    parser.add_argument(
        "--creation-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures/creation-lifestyles-e2e.chum5",
    )
    return parser.parse_args(argv)


def source_roots(android_root: Path, workspace_root: Path) -> tuple[Path, ...]:
    return physical.source_repository_roots(
        android_root=android_root,
        workspace_root=workspace_root,
    )


def execute(args: argparse.Namespace, context: dict[str, object]) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{DISPOSABLE_DEVICE_FLAG} is required because the journey installs, "
            "clears, imports, and edits"
        )
    if physical.SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe ASCII grammar")
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    apk = args.apk.resolve()
    provenance = build_provenance.load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    repositories = provenance["repositories"]
    artifact = provenance["artifact"]
    if not isinstance(repositories, dict) or not isinstance(artifact, dict):
        raise RuntimeError("Verified build provenance is malformed")
    roots = source_roots(android_root, workspace_root)
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
    context["releaseEvidenceStatus"] = "source-and-apk-bound-local-build-not-release-attested"
    context["buildProvenance"] = provenance

    fixture = args.creation_runner.resolve()
    fixture_name = physical.safe_fixture_basename(fixture)
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "contactsHelperSha256": Path(contacts.__file__).resolve(),
        "physicalHelperSha256": Path(physical.__file__).resolve(),
        "buildProvenanceHelperSha256": Path(build_provenance.__file__).resolve(),
        "driverSha256": driver,
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "contactsPageSha256": android_root / "src/Chummer.Android/Native/CreationContactsPage.cs",
        "lifestylesPageSha256": android_root
        / "src/Chummer.Android/Native/CreationLifestylesPage.cs",
        "lifestyleEditPageSha256": android_root
        / "src/Chummer.Android/Native/CreationLifestyleEditPage.cs",
        "lifestylePreviewPageSha256": android_root
        / "src/Chummer.Android/Native/CreationLifestylePreviewPage.cs",
        "lifestylesDraftSha256": android_root
        / "src/Chummer.Android/Native/CreationLifestylesPhoneDraft.cs",
        "runnerCoordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "lifestylesContractSha256": core_root
        / "Chummer.Contracts/Characters/CharacterCreationLifestylesModels.cs",
        "lifestylesRulesSha256": core_root
        / "Chummer.Contracts/Characters/CharacterCreationLifestylesRules.cs",
        "lifestylesServiceSha256": core_root
        / "Chummer.Application/Characters/CharacterCreationLifestylesService.cs",
        "lifestylesLedgerSha256": core_root
        / "Chummer.Application/Characters/CharacterCreationLifestyleReceiptLedgerIntegrity.cs",
        "lifestylesPresenterSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterCreationLifestylesInteractionPresenter.cs",
        "creationFixtureSha256": fixture,
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Creation Lifestyles source graph is incomplete: {missing!r}")
    source_before = physical.source_graph_snapshot(
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
    context["sourceGraphAuthority"] = source_before
    assert_fixture_state(ET.parse(fixture).getroot(), lifestyle_id=None)
    source_digests = source_before["sourceFileSha256"]
    if not isinstance(source_digests, dict):
        raise RuntimeError("Creation Lifestyles source digest map is malformed")
    fixture_sha256 = str(source_digests["creationFixtureSha256"])
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence)
    remote_fixture = f"/sdcard/Download/{fixture_name}"
    remote_files = [
        {
            "path": remote_fixture,
            "precleanAttempted": False,
            "precleaned": False,
            "cleanupAttempted": False,
            "cleanupReplaySuppressed": False,
            "deletedAndVerified": False,
        },
        {
            "path": shared.ADB_FILE_HIERARCHY_REMOTE_PATH,
            "precleanAttempted": False,
            "precleaned": False,
            "cleanupAttempted": False,
            "cleanupReplaySuppressed": False,
            "deletedAndVerified": False,
        },
    ]
    context["remoteTemporaryFiles"] = remote_files
    errors: list[str] = []
    journey: dict[str, object] | None = None
    observation: dict[str, object] | None = None
    transport_validated = False
    verified_remote_fixture = ""
    try:
        device.require_transport_stability(expected_api_level="36")
        observation = physical.android_device_observation(device)
        context["deviceObservation"] = observation
        transport_validated = True
        for remote in remote_files:
            remote["precleanAttempted"] = True
            physical.remove_remote_temporary_file(device, str(remote["path"]))
            remote["precleaned"] = True
        device.install_verified(apk, str(artifact["sha256"]), "--no-streaming", "-r")
        verified_remote_fixture = device.push_verified(
            fixture,
            remote_fixture,
            fixture_sha256,
        )
        journey = prove_lifestyles_journey(device, fixture_name, fixture_sha256)
    except Exception as error:  # noqa: BLE001 - receipt records runtime failure
        errors.append(f"journey failed: {type(error).__name__}: {error}")
    finally:
        if transport_validated:
            for remote in remote_files:
                if not shared.authorize_remote_cleanup_once(remote):
                    errors.append(
                        "remote cleanup replay suppressed after an earlier/unknown "
                        f"mutation outcome for {remote['path']}"
                    )
                    continue
                try:
                    physical.remove_remote_temporary_file(device, str(remote["path"]))
                    remote["deletedAndVerified"] = True
                except Exception as error:  # noqa: BLE001 - cleanup is proof
                    errors.append(
                        f"remote cleanup failed: {remote['path']}: "
                        f"{type(error).__name__}: {error}"
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
                errors.append("source/APK authority changed during device execution")
        except Exception as error:  # noqa: BLE001 - TOCTOU check fails closed
            errors.append(
                f"source/APK authority recheck failed: {type(error).__name__}: {error}"
            )
    if errors:
        raise RuntimeError("; ".join(errors))
    if journey is None or observation is None or not all(
        item["deletedAndVerified"] for item in remote_files
    ):
        raise RuntimeError("Journey, device observation, or remote cleanup proof is incomplete")
    if verified_remote_fixture != fixture_sha256:
        raise RuntimeError("Verified remote Lifestyle fixture differs from the source fixture")
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "device-pass-source-bound",
        "executionStatus": "pass",
        "releaseEvidenceStatus": context["releaseEvidenceStatus"],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "sr5-priority-creation-lifestyles-physical",
        "serial": args.serial,
        "apiLevel": observation["apiLevel"],
        "abi": observation["abi"],
        "deviceObservation": observation,
        "package": shared.PACKAGE,
        "buildProvenance": provenance,
        "sourceGraphAuthority": source_before,
        "sourceGraphRecheckedAfterRun": True,
        "verifiedRemoteCreationFixtureSha256": verified_remote_fixture,
        "remoteTemporaryFiles": remote_files,
        "adbTransport": context["adbTransport"],
        "authorityProofStages": journey,
        "journeys": {
            "dedicatedLifestylesCatalog": "pass",
            "typedCreateConfiguration": "pass",
            "disabledConfirmationUntilExplicitReview": "pass",
            "exactTypedPreviewAndAtomicReceipt": "pass",
            "sameSessionSavedReopen": "pass",
            "newProcessSavedReopen": "pass",
        },
    }


def failure_receipt(
    args: argparse.Namespace,
    error: Exception,
    context: dict[str, object],
) -> dict[str, object]:
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "fail",
        "executionStatus": "fail",
        "releaseEvidenceStatus": context.get(
            "releaseEvidenceStatus",
            "manifest-not-verified",
        ),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "sr5-priority-creation-lifestyles-physical",
        "serial": args.serial,
        "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
        **context,
    }


def argument_failure_receipt(exit_code: int) -> dict[str, object]:
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "fail",
        "executionStatus": "fail",
        "releaseEvidenceStatus": "manifest-not-verified",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "sr5-priority-creation-lifestyles-physical",
        "failure": {
            "type": "ArgumentParseError",
            "message": f"argparse exited {exit_code}",
        },
    }


def preparse_repository_roots(argv: list[str]) -> tuple[Path, ...]:
    roots = [physical.git_toplevel(Path(__file__).resolve().parents[1])]
    values: list[str] = []
    for index, argument in enumerate(argv):
        if argument == "--workspace-root" and index + 1 < len(argv):
            values.append(argv[index + 1])
        elif argument.startswith("--workspace-root="):
            values.append(argument.partition("=")[2])
    if len(values) != 1 or not values[0] or "\x00" in values[0]:
        return tuple(roots)
    workspace = Path(values[0])
    if not workspace.is_absolute() or str(workspace) != values[0]:
        return tuple(roots)
    for repository in (
        workspace / "chummer-core-engine",
        workspace / "chummer-presentation",
    ):
        try:
            roots.append(physical.git_toplevel(repository))
        except (OSError, RuntimeError, subprocess.SubprocessError):
            continue
    return tuple(roots)


def main(argv: list[str] | None = None) -> int:
    raw = list(sys.argv[1:] if argv is None else argv)
    if any(value in {"-h", "--help"} for value in raw):
        try:
            parse_args(["--help"])
        except SystemExit as error:
            return int(error.code or 0)
        return 0
    try:
        receipt_path = physical.locate_explicit_receipt(raw)
        physical.validate_external_output_path(
            receipt_path,
            label="Receipt path",
            repository_roots=preparse_repository_roots(raw),
            expect_directory=False,
        )
        physical.prepare_receipt_target(receipt_path)
    except Exception as error:  # noqa: BLE001 - unsafe receipt gets no write
        print(f"Cannot prepare explicit receipt target: {error}", file=sys.stderr)
        return 2
    try:
        args = parse_args(raw)
    except SystemExit as error:
        code = int(error.code or 0)
        if code:
            physical.write_receipt_atomically(receipt_path, argument_failure_receipt(code))
        return code
    context: dict[str, object] = {}
    try:
        roots = source_roots(
            Path(__file__).resolve().parents[1],
            args.workspace_root.resolve(),
        )
        physical.validate_external_output_path(
            receipt_path,
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
        physical.validate_output_layout(receipt=receipt_path, evidence=args.evidence)
        receipt = execute(args, context)
    except Exception as error:  # noqa: BLE001 - stale pass becomes fail
        receipt = failure_receipt(args, error, context)
        physical.write_receipt_atomically(receipt_path, receipt)
        print(f"Physical SR5 Creation Lifestyles E2E failed: {error}", file=sys.stderr)
        return 1
    physical.write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
