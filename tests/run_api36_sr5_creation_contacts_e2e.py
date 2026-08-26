#!/usr/bin/env python3
"""Prove the dedicated SR5 creation Contacts wizard on a physical API 36 phone."""

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


RECEIPT_SCHEMA = "chummer.android.sr5-creation-contacts-physical-e2e/v1"
DISPOSABLE_DEVICE_FLAG = "--allow-destructive-disposable-device"
STAGE_ID = "creation-stage-contacts-lifestyles"
CONTACT_ID = "50d92979-524d-4cb5-898e-196771e3c786"
CONTACT_TOKEN = CONTACT_ID.replace("-", "")
CONTACT_ITEM_ID = f"creation-contact-item-{CONTACT_TOKEN}"
ORIGINAL_NAME = "ContactE2E"
ORIGINAL_ROLE = "InitialRoleE2E"
UPDATED_NAME = "ContactE2EWizard"
UPDATED_ROLE = "FixerE2EWizard"
CANONICAL_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
SHORT_DIGEST = re.compile(r"^(?:sha256:)?[0-9a-f]{12,19}$")
LIST_BINDING = re.compile(
    r"^Revision (?P<content>[0-9]+) · saved (?P<saved>[0-9]+) · "
    r"snapshot (?P<snapshot>[^ ]+) · source (?P<source>[^ ]+)$"
)
EDIT_BINDING = re.compile(
    r"^Revision (?P<content>[0-9]+) · snapshot (?P<snapshot>[^ ]+) · "
    r"Contact (?P<contact>[0-9a-f-]{36}) · authority (?P<authority>[^ ]+)$"
)
PREVIEW_BINDING = re.compile(
    r"^Revision (?P<content>[0-9]+) · saved (?P<saved>[0-9]+) · "
    r"Contact (?P<contact>[0-9a-f-]{36})$"
)
RECEIPT_ID = re.compile(r"^creation-contact-[0-9a-f]{24}$")
BLOCKER_IDS = (
    "creation-contacts-unavailable",
    "creation-contacts-blockers",
    "creation-contact-edit-blockers",
    "creation-contact-fields-incomplete",
    "creation-contact-preview-blockers",
    "creation-contact-confirm-blockers",
)
PREVIEW_RECEIPT_IDS = (
    "creation-contact-receipt-id",
    "creation-contact-receipt-previous-workspace-revision",
    "creation-contact-receipt-workspace-revision",
    "creation-contact-receipt-previous-content-revision",
    "creation-contact-receipt-content-revision",
    "creation-contact-receipt-previous-saved-revision",
    "creation-contact-receipt-saved-revision",
    "creation-contact-receipt-digest",
    "creation-contact-receipt-content-before",
    "creation-contact-receipt-content-after",
    "creation-contact-receipt-idempotency-digest",
    "creation-contact-receipt-command-digest",
)


@dataclass(frozen=True)
class ContactReceiptProjection:
    receipt_id: str
    previous_workspace_revision: int
    workspace_revision: int
    previous_content_revision: int
    content_revision: int
    previous_saved_revision: int
    saved_revision: int
    receipt_digest: str
    content_before: str
    content_after: str
    idempotency_digest: str
    command_digest: str


def resource_id(node: shared.UiNode) -> str:
    return node.attributes.get("resource-id", "").rsplit("/", 1)[-1]


def node_value(attributes: dict[str, str], label: str) -> str:
    text = attributes.get("text", "")
    description = attributes.get("content-desc", "")
    if text and description and text != description:
        raise RuntimeError(f"{label} exposes conflicting text and description")
    value = text or description
    if not value:
        raise RuntimeError(f"{label} exposes no exact value")
    return value


def scan_exact_resources(
    device: shared.Device,
    required: tuple[str, ...],
    *,
    forbidden: tuple[str, ...] = (),
    max_scrolls: int = 48,
    evidence_prefix: str,
) -> dict[str, dict[str, str]]:
    """Collect exact IDs across a bounded page and reject ambiguity/blockers."""
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    observed: dict[str, dict[str, str]] = {}
    previous_signature: tuple[tuple[str, str, str], ...] | None = None
    stable_signatures = 0
    wanted = set(required) | set(forbidden)
    for scroll in range(max_scrolls + 1):
        nodes = device.hierarchy()
        if not nodes:
            time.sleep(0.75)
            continue
        by_id: dict[str, list[shared.UiNode]] = {}
        for node in nodes:
            identifier = resource_id(node)
            if identifier in wanted:
                by_id.setdefault(identifier, []).append(node)
        for identifier, matches in by_id.items():
            if len(matches) != 1:
                device.capture(f"{evidence_prefix}-cardinality-invalid")
                raise RuntimeError(
                    f"Exact resource {identifier!r} has cardinality {len(matches)}"
                )
            if identifier in forbidden:
                device.capture(f"{evidence_prefix}-blocker-visible")
                raise RuntimeError(f"Fail-closed blocker is visible: {identifier}")
            current = dict(matches[0].attributes)
            previous = observed.get(identifier)
            stable_keys = ("text", "content-desc", "enabled", "clickable", "checked")
            if previous is not None and any(
                previous.get(key, "") != current.get(key, "") for key in stable_keys
            ):
                device.capture(f"{evidence_prefix}-resource-drift")
                raise RuntimeError(f"Resource {identifier!r} changed while scanning")
            observed[identifier] = current
        signature = tuple(
            sorted(
                (
                    resource_id(node),
                    node.attributes.get("text", ""),
                    node.attributes.get("bounds", ""),
                )
                for node in nodes
            )
        )
        stable_signatures = stable_signatures + 1 if signature == previous_signature else 0
        previous_signature = signature
        if set(required).issubset(observed) and stable_signatures >= 1:
            return observed
        if scroll < max_scrolls:
            device.swipe_up(distance_ratio=0.18)
            time.sleep(0.45)
    missing = sorted(set(required) - set(observed))
    device.capture(f"{evidence_prefix}-required-missing")
    raise RuntimeError(f"Required exact resources were not all rendered: {missing!r}")


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
        evidence_prefix=f"contacts-control-{selector}",
        surface_name="Creation Contacts control",
        require_tappable=False,
    )
    expected_enabled = "true" if enabled else "false"
    if node.attributes.get("enabled") != expected_enabled:
        device.capture(f"contacts-control-{selector}-enabled-invalid")
        raise RuntimeError(
            f"Control {selector!r} enabled state differs: expected "
            f"{expected_enabled}, got {node.attributes.get('enabled')!r}"
        )
    if checked is not None and node.attributes.get("checked") != ("true" if checked else "false"):
        device.capture(f"contacts-control-{selector}-checked-invalid")
        raise RuntimeError(f"Control {selector!r} checked state differs")
    if enabled and (
        node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node)
    ):
        device.capture(f"contacts-control-{selector}-not-tappable")
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
        device.capture(f"contacts-field-{selector}-focus-failed")
        raise RuntimeError(f"Text field {selector!r} did not receive focus")
    device.shell("input", "keycombination", "113", "29")
    device.shell("input", "text", value.replace(" ", "%s"))
    time.sleep(0.35)
    updated = device.find_exact_resource_id(selector)
    if updated is None or updated.attributes.get("text") != value:
        device.capture(f"contacts-field-{selector}-value-failed")
        raise RuntimeError(f"Text field {selector!r} did not render the exact value")
    device.dismiss_keyboard()


def parse_list_binding(value: str) -> dict[str, object]:
    match = LIST_BINDING.fullmatch(value)
    if match is None:
        raise RuntimeError("Creation Contacts list binding is malformed")
    parsed: dict[str, object] = {
        "contentRevision": int(match.group("content")),
        "savedRevision": int(match.group("saved")),
        "snapshot": match.group("snapshot"),
        "source": match.group("source"),
    }
    for field in ("snapshot", "source"):
        if SHORT_DIGEST.fullmatch(str(parsed[field])) is None:
            raise RuntimeError(f"Creation Contacts {field} short digest is malformed")
    return parsed


def parse_edit_binding(value: str) -> dict[str, object]:
    match = EDIT_BINDING.fullmatch(value)
    if match is None or match.group("contact") != CONTACT_ID:
        raise RuntimeError("Creation Contact edit binding is malformed or identity-drifted")
    for field in ("snapshot", "authority"):
        if SHORT_DIGEST.fullmatch(match.group(field)) is None:
            raise RuntimeError(f"Creation Contact edit {field} digest is malformed")
    return {
        "contentRevision": int(match.group("content")),
        "snapshot": match.group("snapshot"),
        "authority": match.group("authority"),
        "contactId": match.group("contact"),
    }


def parse_preview_binding(value: str) -> dict[str, object]:
    match = PREVIEW_BINDING.fullmatch(value)
    if match is None or match.group("contact") != CONTACT_ID:
        raise RuntimeError("Creation Contact preview binding is malformed or identity-drifted")
    return {
        "contentRevision": int(match.group("content")),
        "savedRevision": int(match.group("saved")),
        "contactId": match.group("contact"),
    }


def canonical_digest(value: str, label: str) -> str:
    if CANONICAL_DIGEST.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not one canonical sha256: digest")
    return value


def exact_nonnegative_integer(value: str, label: str) -> int:
    if re.fullmatch(r"0|[1-9][0-9]*", value) is None:
        raise RuntimeError(f"{label} is not one canonical nonnegative integer")
    return int(value)


def receipt_projection(values: dict[str, dict[str, str]]) -> ContactReceiptProjection:
    rendered = {
        identifier: node_value(values[identifier], identifier)
        for identifier in PREVIEW_RECEIPT_IDS
    }
    receipt_id = rendered["creation-contact-receipt-id"]
    if RECEIPT_ID.fullmatch(receipt_id) is None:
        raise RuntimeError("Creation Contact receipt ID is malformed")
    integer = lambda identifier, label: exact_nonnegative_integer(rendered[identifier], label)
    digest = lambda identifier, label: canonical_digest(rendered[identifier], label)
    return ContactReceiptProjection(
        receipt_id=receipt_id,
        previous_workspace_revision=integer("creation-contact-receipt-previous-workspace-revision", "previous workspace revision"),
        workspace_revision=integer("creation-contact-receipt-workspace-revision", "workspace revision"),
        previous_content_revision=integer("creation-contact-receipt-previous-content-revision", "previous content revision"),
        content_revision=integer("creation-contact-receipt-content-revision", "content revision"),
        previous_saved_revision=integer("creation-contact-receipt-previous-saved-revision", "previous saved revision"),
        saved_revision=integer("creation-contact-receipt-saved-revision", "saved revision"),
        receipt_digest=digest("creation-contact-receipt-digest", "receipt digest"),
        content_before=digest("creation-contact-receipt-content-before", "content-before digest"),
        content_after=digest("creation-contact-receipt-content-after", "content-after digest"),
        idempotency_digest=digest("creation-contact-receipt-idempotency-digest", "idempotency digest"),
        command_digest=digest("creation-contact-receipt-command-digest", "command digest"),
    )


def validate_receipt_projection(
    receipt: ContactReceiptProjection,
    *,
    imported: shared.WorkspaceAuthority,
    saved: shared.WorkspaceAuthority,
) -> None:
    if receipt.previous_workspace_revision != imported.content_revision:
        raise RuntimeError("Receipt previous workspace revision differs from imported authority")
    if receipt.previous_content_revision != imported.content_revision:
        raise RuntimeError("Receipt previous content revision differs from imported authority")
    if receipt.previous_saved_revision != imported.saved_revision:
        raise RuntimeError("Receipt previous saved revision differs from imported authority")
    successor = imported.content_revision + 1
    if not (
        receipt.workspace_revision == successor
        and receipt.content_revision == successor
        and receipt.saved_revision == successor
        and saved.content_revision == successor
        and saved.saved_revision == successor
    ):
        raise RuntimeError("Receipt and workspace do not prove one exact saved successor revision")
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Creation Contact mutation changed workspace identity")
    if receipt.content_before != f"sha256:{imported.payload_sha256}":
        raise RuntimeError("Receipt content-before digest is not bound to imported payload")
    if receipt.content_after != f"sha256:{saved.payload_sha256}":
        raise RuntimeError("Receipt content-after digest is not bound to saved payload")
    if receipt.content_before == receipt.content_after:
        raise RuntimeError("Creation Contact receipt proves no payload change")


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


def root_for_authority(device: shared.Device, authority: shared.WorkspaceAuthority) -> ET.Element:
    matches = [
        payload
        for payload in workspace_payloads(device)
        if hashlib.sha256(payload.encode("utf-8")).hexdigest() == authority.payload_sha256
    ]
    if len(matches) != 1:
        device.capture("creation-contacts-authority-payload-ambiguous")
        raise RuntimeError(f"Expected one payload bound to authority, got {len(matches)}")
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != "ContactPetE2E" or root.findtext("created") != "False":
        raise RuntimeError("Workspace authority selected a different or career runner")
    return root


def target_contact(root: ET.Element) -> ET.Element:
    matches = [
        item
        for item in root.findall("./contacts/contact")
        if item.findtext("guid") == CONTACT_ID
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one stable Contact identity, got {len(matches)}")
    return matches[0]


def assert_contact_xml(root: ET.Element, *, changed: bool) -> None:
    contact = target_contact(root)
    expected_name = UPDATED_NAME if changed else ORIGINAL_NAME
    expected_role = UPDATED_ROLE if changed else ORIGINAL_ROLE
    if contact.findtext("name") != expected_name or contact.findtext("role") != expected_role:
        raise RuntimeError("Target Contact name/role differs from the exact journey state")
    expected_untouched = {
        "location": "Seattle",
        "connection": "3",
        "loyalty": "2",
        "metatype": "Human",
        "group": "False",
        "family": "False",
        "blackmail": "False",
        "free": "False",
        "type": "Contact",
    }
    for field, expected in expected_untouched.items():
        if contact.findtext(field) != expected:
            raise RuntimeError(f"Target Contact unrelated field {field!r} changed")
    siblings = {
        item.findtext("name")
        for item in root.findall("./contacts/contact")
        if item.findtext("guid") != CONTACT_ID
    }
    if siblings != {
        "ContactDeleteE2E",
        "ContactFreePersistedE2E",
        "PetE2E",
        "PetDeleteE2E",
    }:
        raise RuntimeError("Contact/Pet sibling set changed during the Contact edit")
    if root.findtext("karma") != "35" or root.findtext("nuyen") != "8500":
        raise RuntimeError("Creation Contact edit changed unrelated Karma or Nuyen")


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
        evidence_prefix="creation-contacts-open-file",
        surface_name="Open runner file control",
    )
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, created=False, timeout=180)
    authority = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    shared.require_saved_authority(authority)
    assert_contact_xml(root_for_authority(device, authority), changed=False)
    return launch, authority


def open_contacts(device: shared.Device, *, open_build_route: bool = True) -> dict[str, object]:
    shared.open_creation_dashboard(device, open_build_route=open_build_route)
    tap_enabled_control(device, STAGE_ID)
    device.wait_for_single_exact_resource_id(
        "creation-contacts-page",
        timeout=120,
        evidence_prefix="creation-contacts-page",
        surface_name="Creation Contacts page",
    )
    values = scan_exact_resources(
        device,
        ("creation-contacts-binding", CONTACT_ITEM_ID),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-contacts-list",
    )
    return parse_list_binding(node_value(values["creation-contacts-binding"], "list binding"))


def open_target_contact(device: shared.Device) -> dict[str, object]:
    tap_enabled_control(device, CONTACT_ITEM_ID)
    device.wait_for_single_exact_resource_id(
        "creation-contact-edit-page",
        timeout=120,
        evidence_prefix="creation-contact-edit-page",
        surface_name="Creation Contact edit page",
    )
    values = scan_exact_resources(
        device,
        (
            "creation-contact-edit-binding",
            "creation-contact-field-name",
            "creation-contact-field-role",
            "creation-contact-preview",
        ),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-contact-edit",
    )
    return parse_edit_binding(node_value(values["creation-contact-edit-binding"], "edit binding"))


def assert_updated_contact_surface(device: shared.Device, expected_revision: int) -> dict[str, object]:
    binding = open_contacts(device)
    if binding["contentRevision"] != expected_revision or binding["savedRevision"] != expected_revision:
        raise RuntimeError("Reopened Contacts list is not bound to the saved revision")
    edit = open_target_contact(device)
    if edit["contentRevision"] != expected_revision:
        raise RuntimeError("Reopened Contact editor is not bound to the saved revision")
    name = device.wait_exact_resource_id_bidirectional(
        "creation-contact-field-name",
        timeout=90,
        evidence_prefix="creation-contact-name-readback",
        surface_name="Creation Contact name field",
        require_tappable=False,
    )
    role = device.wait_exact_resource_id_bidirectional(
        "creation-contact-field-role",
        timeout=90,
        evidence_prefix="creation-contact-role-readback",
        surface_name="Creation Contact role field",
        require_tappable=False,
    )
    if name.attributes.get("text") != UPDATED_NAME or role.attributes.get("text") != UPDATED_ROLE:
        raise RuntimeError("Reopened Contact fields differ from the exact saved values")
    return {"listBinding": binding, "editBinding": edit}


def prove_contacts_journey(
    device: shared.Device,
    fixture_name: str,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture_name, fixture_sha256)
    before_list = open_contacts(device)
    if before_list["contentRevision"] != imported.content_revision:
        raise RuntimeError("Initial Contacts list revision differs from workspace authority")
    before_edit = open_target_contact(device)
    if before_edit["contentRevision"] != imported.content_revision:
        raise RuntimeError("Initial Contact editor revision differs from workspace authority")
    require_control_state(device, "creation-contact-preview", enabled=False)
    set_exact_text(device, "creation-contact-field-name", UPDATED_NAME)
    set_exact_text(device, "creation-contact-field-role", UPDATED_ROLE)
    tap_enabled_control(device, "creation-contact-preview")
    device.wait_for_single_exact_resource_id(
        "creation-contact-preview-page",
        timeout=120,
        evidence_prefix="creation-contact-preview-page",
        surface_name="Creation Contact preview page",
    )
    preview_values = scan_exact_resources(
        device,
        (
            "creation-contact-preview-binding",
            "creation-contact-preview-digest",
            "creation-contact-plan-digest",
            "creation-contact-preview-target",
            "creation-contact-write-1-name",
            "creation-contact-write-2-role",
            "creation-contact-preview-preservation",
            "creation-contact-explicit-confirm",
            "creation-contact-confirm",
        ),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-contact-preview",
    )
    preview_binding = parse_preview_binding(
        node_value(preview_values["creation-contact-preview-binding"], "preview binding")
    )
    if preview_binding["contentRevision"] != imported.content_revision:
        raise RuntimeError("Preview is not bound to the imported content revision")
    preview_digest = canonical_digest(
        node_value(preview_values["creation-contact-preview-digest"], "preview digest"),
        "preview digest",
    )
    plan_digest = canonical_digest(
        node_value(preview_values["creation-contact-plan-digest"], "plan digest"),
        "plan digest",
    )
    require_control_state(device, "creation-contact-confirm", enabled=False)
    checkbox = require_control_state(
        device,
        "creation-contact-explicit-confirm",
        enabled=True,
        checked=False,
    )
    x, y = checkbox.center
    device.shell("input", "tap", str(x), str(y))
    require_control_state(
        device,
        "creation-contact-explicit-confirm",
        enabled=True,
        checked=True,
    )
    tap_enabled_control(device, "creation-contact-confirm")
    device.wait_for_single_exact_resource_id(
        "creation-contact-confirmed",
        timeout=180,
        evidence_prefix="creation-contact-confirmed",
        surface_name="Creation Contact confirmation result",
    )
    receipt_values = scan_exact_resources(
        device,
        (
            "creation-contact-confirm-receipt",
            *PREVIEW_RECEIPT_IDS,
            "creation-contact-back-to-build",
        ),
        forbidden=BLOCKER_IDS,
        evidence_prefix="creation-contact-receipt",
    )
    receipt = receipt_projection(receipt_values)
    tap_enabled_control(device, "creation-contact-back-to-build")
    shared.open_creation_dashboard(device, open_build_route=False)
    saved = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(saved)
    validate_receipt_projection(receipt, imported=imported, saved=saved)
    assert_contact_xml(root_for_authority(device, saved), changed=True)
    same_session = assert_updated_contact_surface(device, saved.content_revision)
    device.capture("creation-contacts-same-session-reopen")

    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=False, timeout=180)
    restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(saved, restored)
    assert_contact_xml(root_for_authority(device, restored), changed=True)
    after_restart = assert_updated_contact_surface(device, restored.content_revision)
    device.capture("creation-contacts-process-restart-reopen")
    return {
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
        default=Path(__file__).resolve().parent
        / "fixtures/creation-contact-pet-e2e.chum5",
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
    context["releaseEvidenceStatus"] = (
        "source-and-apk-bound-local-build-not-release-attested"
    )
    context["buildProvenance"] = provenance

    fixture = args.creation_runner.resolve()
    fixture_name = physical.safe_fixture_basename(fixture)
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "physicalHelperSha256": Path(physical.__file__).resolve(),
        "buildProvenanceHelperSha256": Path(build_provenance.__file__).resolve(),
        "driverSha256": driver,
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "contactsPageSha256": android_root / "src/Chummer.Android/Native/CreationContactsPage.cs",
        "contactEditPageSha256": android_root / "src/Chummer.Android/Native/CreationContactEditPage.cs",
        "contactPreviewPageSha256": android_root / "src/Chummer.Android/Native/CreationContactPreviewPage.cs",
        "contactDraftSha256": android_root / "src/Chummer.Android/Native/CreationContactsPhoneDraft.cs",
        "runnerCoordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "contactsContractSha256": core_root / "Chummer.Contracts/Characters/CharacterCreationContactsModels.cs",
        "contactsServiceSha256": core_root / "Chummer.Application/Characters/CharacterCreationContactsService.cs",
        "contactsLedgerSha256": core_root / "Chummer.Application/Characters/CharacterCreationContactReceiptLedgerIntegrity.cs",
        "contactsPresenterSha256": presentation_root / "Chummer.Presentation/Overview/CharacterCreationContactsInteractionPresenter.cs",
        "creationFixtureSha256": fixture,
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Creation Contacts source graph is incomplete: {missing!r}")
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
    assert_contact_xml(ET.parse(fixture).getroot(), changed=False)
    source_digests = source_before["sourceFileSha256"]
    if not isinstance(source_digests, dict):
        raise RuntimeError("Creation Contacts source digest map is malformed")
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
            "path": "/sdcard/chummer-editing-window.xml",
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
        device.install_verified(
            apk,
            str(artifact["sha256"]),
            "--no-streaming",
            "-r",
        )
        verified_remote_fixture = device.push_verified(
            fixture,
            remote_fixture,
            fixture_sha256,
        )
        journey = prove_contacts_journey(device, fixture_name, fixture_sha256)
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
    if (
        journey is None
        or observation is None
        or not all(item["deletedAndVerified"] for item in remote_files)
    ):
        raise RuntimeError(
            "Journey, device observation, or remote cleanup proof is incomplete"
        )
    if verified_remote_fixture != fixture_sha256:
        raise RuntimeError(
            "Verified remote Creation Contact fixture digest differs from the source fixture"
        )
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "device-pass-source-bound",
        "executionStatus": "pass",
        "releaseEvidenceStatus": context["releaseEvidenceStatus"],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "sr5-priority-creation-contacts-physical",
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
            "dedicatedContactsStage": "pass",
            "disabledPreviewAndConfirmation": "pass",
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
        "journey": "sr5-priority-creation-contacts-physical",
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
        "journey": "sr5-priority-creation-contacts-physical",
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
            physical.write_receipt_atomically(
                receipt_path,
                argument_failure_receipt(code),
            )
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
        print(f"Physical SR5 Creation Contacts E2E failed: {error}", file=sys.stderr)
        return 1
    physical.write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
