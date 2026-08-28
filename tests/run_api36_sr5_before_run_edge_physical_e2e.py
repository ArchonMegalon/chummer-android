#!/usr/bin/env python3
"""Prove one exact SR5 Before Run Edge action on a physical API 36 ARM64 phone.

Scope is deliberately narrow: this journey proves only the existing typed
``SpendEdge`` leaf.  It does not claim loadout, healing, preparation purchase,
contact, commitment, tablet, Play, After Run, or unrestricted editing parity.
"""

from __future__ import annotations

import argparse
import base64
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
import uuid
import xml.etree.ElementTree as ET

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_editing_e2e as shared
import run_api36_sr5_career_attribute_wizard_e2e as physical


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
    source_paths={},
)


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


def length_prefixed_hash(*values: object) -> str:
    canonical = "".join(f"{len(str(value))}:{value};" for value in values)
    return "sha256:" + hashlib.sha256(canonical.encode("utf-8")).hexdigest()


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
    require_receipt: bool,
) -> dict[str, object] | None:
    if set(transaction) != JOURNAL_FIELDS:
        raise RuntimeError("Durable table transaction fields are not exact")
    if transaction["SchemaVersion"] != 1 or type(transaction["SchemaVersion"]) is not int:
        raise RuntimeError("Durable table transaction schema is not exact")
    if transaction["Phase"] != phase or type(transaction["Phase"]) is not int:
        raise RuntimeError("Durable table transaction phase is not exact")
    if not isinstance(transaction["Version"], int) or transaction["Version"] <= 0:
        raise RuntimeError("Durable table transaction CAS version is invalid")
    canonical_guid(transaction["OwnerId"], "Transaction owner")
    canonical_guid(transaction["TransactionId"], "Transaction identity")
    idempotency = typed_digest(transaction["IdempotencyKey"], "Idempotency key")
    expected_postcondition = typed_digest(
        transaction["ExpectedPostconditionDigest"], "Expected postcondition"
    )
    typed_digest(transaction["JournalDigest"], "Journal digest")

    review = require_object(transaction["Review"], "Typed table review")
    quote = require_object(transaction["Quote"], "Typed table quote")
    identity = require_object(quote.get("Identity"), "Typed action identity")
    selected = require_object(review.get("SelectedAction"), "Reviewed action identity")
    if identity != selected:
        raise RuntimeError("Durable review and quote do not bind the same typed action")
    if review.get("WorkspaceId") != workspace_id:
        raise RuntimeError("Durable review workspace identity is not exact")
    if review.get("WorkspaceRevision") != expected_revision:
        raise RuntimeError("Durable review workspace revision is not exact")
    if review.get("Lane") != spec.lane_value:
        raise RuntimeError("Durable review lane is not exact")
    typed_digest(review.get("SnapshotDigest"), "Reviewed snapshot digest")
    if identity.get("Kind") != spec.action_kind:
        raise RuntimeError("Durable review action kind exceeds this journey scope")
    action_id = identity.get("ActionId")
    if not isinstance(action_id, str) or not action_id:
        raise RuntimeError("Durable action identity is empty")
    action_digest = typed_digest(identity.get("ActionDigest"), "Action digest")
    typed_digest(identity.get("TargetRevision"), "Action target revision")

    receipt_value = transaction["Receipt"]
    if not require_receipt:
        if receipt_value is not None:
            raise RuntimeError("Reviewed transaction unexpectedly contains a receipt")
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
        if receipt.get(field) != expected:
            raise RuntimeError(f"Applied receipt {field} is not exact")
    actual_digest = typed_digest(receipt.get("ReceiptDigest"), "Receipt digest")
    if actual_digest != expected_receipt_digest(receipt):
        raise RuntimeError("Applied receipt digest is not canonical")
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


def assert_before_state(root: ET.Element) -> None:
    require_before_run_fixture(root)


def assert_after_state(root: ET.Element, _before_authority: object) -> None:
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


def prepare_runner(
    device: shared.Device,
    spec: LaneSpec,
    fixture_name: str,
    fixture_sha256: str,
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
    authority = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def open_lane(device: shared.Device, spec: LaneSpec) -> None:
    physical.open_choose(device)
    physical.tap_exact_route(device, "sr5-career/table", timeout=90)
    physical.wait_exact_route(device, "sr5-career/table", timeout=90)
    device.tap_single_exact_resource_id(
        spec.action_route,
        timeout=90,
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


def acknowledge_alert(device: shared.Device) -> None:
    device.tap("OK", timeout=180)


def prove_lane(
    device: shared.Device,
    spec: LaneSpec,
    fixture: Path,
    fixture_sha256: str,
    *,
    assert_before,
    assert_after,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(
        device, spec, fixture.name, fixture_sha256
    )
    before_authority = assert_before(
        root_for_authority(device, imported, spec.fixture_alias)
    )

    open_lane(device, spec)
    action_automation_id = tap_unique_typed_action(device, spec)
    device.wait("sr5-table-wizard-quote", timeout=90)
    device.tap_single_exact_resource_id(
        "sr5-table-wizard-open-review",
        timeout=90,
        evidence_prefix=f"sr5-{spec.lane}-open-review",
        surface_name=f"SR5 {spec.lane} durable review control",
    )
    physical.wait_exact_route(device, spec.review_route, timeout=90)
    reviewed = read_transaction(device, spec.checkpoint_key)
    if reviewed is None:
        raise RuntimeError("Reviewed table transaction unexpectedly disappeared")
    validate_transaction(
        reviewed.payload,
        spec=spec,
        workspace_id=imported.workspace_id,
        expected_revision=imported.content_revision,
        phase=0,
        require_receipt=False,
    )
    device.capture(f"sr5-{spec.lane}-durable-review")

    review_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    restored_before_apply = shared.read_phone_workspace_authority(device)
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
    device.capture(f"sr5-{spec.lane}-resumed-review")
    device.tap_single_exact_resource_id(
        "sr5-table-wizard-confirm",
        timeout=120,
        evidence_prefix=f"sr5-{spec.lane}-confirm",
        surface_name=f"SR5 {spec.lane} exact typed apply control",
    )
    acknowledge_alert(device)
    device.wait("sr5-table-wizard-receipt", timeout=180)
    applied = read_transaction(device, spec.checkpoint_key)
    if applied is None:
        raise RuntimeError("Applied table transaction unexpectedly disappeared")
    receipt = validate_transaction(
        applied.payload,
        spec=spec,
        workspace_id=imported.workspace_id,
        expected_revision=imported.content_revision,
        phase=2,
        require_receipt=True,
    )
    if receipt is None:
        raise RuntimeError("Applied table receipt is missing")
    require_same_review(reviewed.payload, applied.payload)

    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    saved = shared.read_phone_workspace_authority(device)
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
    recovered = read_transaction(device, spec.checkpoint_key)
    if recovered != applied:
        raise RuntimeError("Recovered receipt bytes differ after process restart")
    recovered_receipt = validate_transaction(
        recovered.payload,
        spec=spec,
        workspace_id=imported.workspace_id,
        expected_revision=imported.content_revision,
        phase=2,
        require_receipt=True,
    )
    if recovered_receipt != receipt:
        raise RuntimeError("Recovered receipt identity differs after restart")
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
    final_saved = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(saved, final_saved)
    assert_after(
        root_for_authority(device, final_saved, spec.fixture_alias),
        before_authority,
    )
    if read_transaction(device, spec.checkpoint_key, required=False) is not None:
        raise RuntimeError("Acknowledged receipt deletion did not survive restart")
    open_lane(device, spec)
    successor_action = tap_unique_typed_action(device, spec)
    device.wait("sr5-table-wizard-quote", timeout=90)
    device.capture(f"sr5-{spec.lane}-saved-successor-reopened")
    return {
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
        "successorActionAutomationId": successor_action,
        "reviewedTransactionSha256": reviewed.serialized_sha256,
        "appliedTransactionSha256": applied.serialized_sha256,
        "receipt": receipt,
        "restartProcessIds": [
            list(review_restart.restarted.process_ids),
            list(receipt_restart.restarted.process_ids),
            list(final_restart.restarted.process_ids),
        ],
    }


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
