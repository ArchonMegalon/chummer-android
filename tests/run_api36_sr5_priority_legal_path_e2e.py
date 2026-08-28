#!/usr/bin/env python3
"""Prove one typed SR5 Priority creation path on a physical API-36 phone.

The journey installs one ARM64 APK whose bytes and source repositories are
bound by the shared build-provenance manifest, then observes typed drafts made
through the phone pages. It never seeds or invents creation draft state.
Identity stays explicitly unavailable and is not required by Core's current
finalizer. Success requires a sealed whole-build plan, one atomic confirmation,
a durable receipt, Career reopen, force-stop/new PID, and the exact same saved
workspace/document digest after restart. This is local proof, not release truth.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared
from api36_physical_build_provenance import load_and_verify_manifest


SCHEMA = "chummer.android.sr5-priority-create-physical-e2e/v1"
IDENTITY_CONTRACT_BLOCKER = "creation-identity-draft-contract-unavailable"
DISPOSABLE_DEVICE_FLAG = "--allow-destructive-disposable-device"
SAFE_ADB_SERIAL = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
CANONICAL_SHA256 = re.compile(r"^[0-9a-f]{64}$")
POSITIVE_REVISION = re.compile(r"^[1-9][0-9]*$")
VISIBLE_FINALIZATION_BINDING = re.compile(
    r"^Revision [1-9][0-9]* · plan sha256:[0-9a-f]{11}… · "
    r"preview sha256:[0-9a-f]{11}…$"
)
VIRTUAL_MARKERS = (
    "aosp_cf_", "cuttlefish", "emulator", "generic", "goldfish", "qemu",
    "ranchu", "sdk_gphone", "vbox", "virtualbox",
)


@dataclass(frozen=True)
class LegalPathStage:
    step_id: str
    route_id: str
    page_id: str | None
    authority_id: str | None
    required_by_finalizer: bool
    expected_clickable: bool = True


@dataclass(frozen=True)
class ProvenanceFileIdentity:
    sha256: str
    size: int


@dataclass(frozen=True)
class FinalizationReviewAuthority:
    content_revision: int
    plan_digest: str
    preview_digest: str


@dataclass(frozen=True)
class FinalizationReceiptAuthority:
    previous_content_revision: int
    content_revision: int
    saved_revision: int
    build_method: str
    plan_digest: str
    preview_digest: str
    receipt_digest: str


LEGAL_PATH_STAGES = (
    LegalPathStage("basics", "creation-stage-basics", "creation-basics-page", "creation-basics-authority", False),
    LegalPathStage("method", "creation-stage-method", "creation-prerequisite-page", "creation-prerequisite-pending-draft", True),
    LegalPathStage("foundation", "creation-stage-foundation", "creation-foundation-page", "creation-foundation-pending-draft", False),
    LegalPathStage("attributes", "creation-stage-attributes", "creation-attributes-page", "creation-attributes-pending-draft", True),
    LegalPathStage("qualities", "creation-stage-qualities", "creation-qualities-page", "creation-qualities-binding", True),
    LegalPathStage("skills", "creation-stage-skills", "creation-skills-page", "creation-skills-binding", True),
    LegalPathStage("magic-resonance", "creation-stage-magic-resonance", "creation-magic-resonance-page", "creation-magic-resonance-binding", True),
    LegalPathStage("resources", "creation-stage-resources", "creation-resources-page", "creation-resources-saved-draft", True),
    LegalPathStage("contacts-lifestyles", "creation-stage-contacts-lifestyles", "creation-contacts-page", "creation-contacts-binding", False),
    LegalPathStage("identity-story", "creation-stage-identity-story", None, None, False, False),
)


def validate_stage_catalog(stages: tuple[LegalPathStage, ...] = LEGAL_PATH_STAGES) -> None:
    expected = (
        "basics", "method", "foundation", "attributes", "qualities", "skills",
        "magic-resonance", "resources", "contacts-lifestyles", "identity-story",
    )
    actual = tuple(stage.step_id for stage in stages)
    if actual != expected:
        raise RuntimeError(f"SR5 Priority legal-path order changed: expected={expected!r}, actual={actual!r}")
    route_ids = tuple(stage.route_id for stage in stages)
    if any(not value for value in route_ids) or len(route_ids) != len(set(route_ids)):
        raise RuntimeError("SR5 Priority legal-path route identities are empty or duplicated")
    clickable = tuple(stage for stage in stages if stage.expected_clickable)
    for field in ("page_id", "authority_id"):
        values = tuple(getattr(stage, field) for stage in clickable)
        if any(not value for value in values) or len(values) != len(set(values)):
            raise RuntimeError(f"SR5 Priority legal-path {field} identities are empty or duplicated")
    if any(
        stage.page_id is not None or stage.authority_id is not None
        for stage in stages if not stage.expected_clickable
    ):
        raise RuntimeError("A blocked SR5 Priority stage must not name a fallback page or authority")


def _node_text(node: shared.UiNode) -> str:
    return node.attributes.get("content-desc") or node.attributes.get("text") or ""


def _require_enabled(node: shared.UiNode, label: str) -> None:
    if node.attributes.get("enabled") != "true" or node.attributes.get("clickable") != "true":
        raise RuntimeError(f"{label} is not enabled and clickable")


def _read_exact_authority(
    device: shared.Device,
    selector: str,
    label: str,
) -> str:
    node = device.wait_exact_resource_id_bidirectional(
        selector, timeout=60, backward_scrolls=30, forward_scrolls=30,
        scroll_distance_ratio=0.22, evidence_prefix=selector,
        surface_name=label, require_tappable=False,
    )
    value = _node_text(node).strip()
    if not value:
        raise RuntimeError(f"{label} did not expose an exact machine-readable value")
    return value


def is_canonical_authority_digest(value: str) -> bool:
    return CANONICAL_SHA256.fullmatch(value) is not None and len(set(value)) > 1


def _read_canonical_digest(device: shared.Device, selector: str, label: str) -> str:
    value = _read_exact_authority(device, selector, label)
    if not is_canonical_authority_digest(value):
        raise RuntimeError(f"{label} is not one full lowercase SHA-256 digest: {value!r}")
    return value


def _read_positive_revision(device: shared.Device, selector: str, label: str) -> int:
    value = _read_exact_authority(device, selector, label)
    if POSITIVE_REVISION.fullmatch(value) is None:
        raise RuntimeError(f"{label} is not one exact positive revision: {value!r}")
    return int(value)


def require_priority_build_method(value: str, label: str) -> str:
    if value != "Priority":
        raise RuntimeError(f"{label} must be exactly BuildMethod 'Priority', got {value!r}")
    return value


def cross_bind_finalization_authorities(
    review: FinalizationReviewAuthority,
    receipt: FinalizationReceiptAuthority,
) -> None:
    for label, digest in (
        ("sealed plan digest", review.plan_digest),
        ("finalization preview digest", review.preview_digest),
        ("receipt plan digest", receipt.plan_digest),
        ("receipt preview digest", receipt.preview_digest),
        ("durable receipt digest", receipt.receipt_digest),
    ):
        if not is_canonical_authority_digest(digest):
            raise RuntimeError(f"{label} is not one full lowercase SHA-256 digest: {digest!r}")
    require_priority_build_method(receipt.build_method, "Durable creation receipt")
    if receipt.previous_content_revision != review.content_revision:
        raise RuntimeError("Durable receipt does not bind the reviewed workspace revision")
    if receipt.content_revision != review.content_revision + 1:
        raise RuntimeError("Atomic finalization did not advance the reviewed revision exactly once")
    if receipt.saved_revision != receipt.content_revision:
        raise RuntimeError("Durable receipt content and saved revisions do not match")
    if receipt.plan_digest != review.plan_digest:
        raise RuntimeError("Durable receipt plan digest does not match the sealed reviewed plan")
    if receipt.preview_digest != review.preview_digest:
        raise RuntimeError("Durable receipt preview digest does not match the sealed review")


def provenance_file_identity(path: Path) -> ProvenanceFileIdentity:
    resolved = path.resolve(strict=True)
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(resolved, flags)
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise RuntimeError("Build-provenance manifest is not a regular file")
        digest = hashlib.sha256()
        size = 0
        while True:
            chunk = os.read(descriptor, 1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
            size += len(chunk)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    if (
        before.st_dev != after.st_dev
        or before.st_ino != after.st_ino
        or before.st_size != after.st_size
        or before.st_mtime_ns != after.st_mtime_ns
        or size != after.st_size
    ):
        raise RuntimeError("Build-provenance manifest changed during exact byte capture")
    return ProvenanceFileIdentity(digest.hexdigest(), size)


def provenance_file_identity_json(identity: ProvenanceFileIdentity) -> dict[str, object]:
    return {"sha256": identity.sha256, "size": identity.size}


def open_exact_stage(device: shared.Device, stage: LegalPathStage) -> dict[str, object]:
    row = device.wait_exact_resource_id_bidirectional(
        stage.route_id, timeout=90, backward_scrolls=30, forward_scrolls=30,
        scroll_distance_ratio=0.22, evidence_prefix=f"sr5-priority-{stage.step_id}-route",
        surface_name=f"SR5 Priority {stage.step_id} stage",
    )
    enabled = row.attributes.get("enabled") == "true"
    clickable = row.attributes.get("clickable") == "true"
    if not stage.expected_clickable:
        if enabled or clickable:
            raise RuntimeError(f"Typed SR5 Priority stage {stage.step_id!r} exposed an unsupported mutation route")
        if IDENTITY_CONTRACT_BLOCKER not in _node_text(row):
            raise RuntimeError(f"Typed SR5 Priority stage {stage.step_id!r} did not expose its exact contract blocker")
        device.capture(f"sr5-priority-{stage.step_id}-blocked")
        return {
            "stepId": stage.step_id,
            "routeId": stage.route_id,
            "requiredByCurrentFinalizer": stage.required_by_finalizer,
            "routeStatus": "typed-contract-unavailable",
            "authorityVisible": False,
            "draftFabricated": False,
            "blocker": IDENTITY_CONTRACT_BLOCKER,
        }
    if not enabled or not clickable:
        device.capture(f"sr5-priority-{stage.step_id}-blocked")
        raise RuntimeError(
            f"Typed SR5 Priority stage {stage.step_id!r} is blocked; no fallback is allowed: "
            f"{_node_text(row) or 'no disable reason'}"
        )
    if stage.page_id is None or stage.authority_id is None:
        raise RuntimeError(f"Clickable SR5 Priority stage {stage.step_id!r} has no typed page authority")
    device.shell("input", "tap", *(str(value) for value in row.center))
    device.wait_for_single_exact_resource_id(
        stage.page_id, timeout=60, evidence_prefix=f"sr5-priority-{stage.step_id}-page",
        surface_name=f"SR5 Priority {stage.step_id} page",
    )
    device.wait_exact_resource_id_bidirectional(
        stage.authority_id, timeout=60, backward_scrolls=24, forward_scrolls=24,
        scroll_distance_ratio=0.22, evidence_prefix=f"sr5-priority-{stage.step_id}-authority",
        surface_name=f"SR5 Priority {stage.step_id} authority",
    )
    device.capture(f"sr5-priority-{stage.step_id}-authority")
    result: dict[str, object] = {
        "stepId": stage.step_id, "routeId": stage.route_id, "pageId": stage.page_id,
        "authorityId": stage.authority_id, "requiredByCurrentFinalizer": stage.required_by_finalizer,
        "routeStatus": "typed-authority-visible", "authorityVisible": True, "draftFabricated": False,
    }
    if stage.step_id == "method":
        result["buildMethod"] = require_priority_build_method(
            _read_exact_authority(
                device,
                "creation-prerequisite-build-method-id",
                "Machine-readable creation BuildMethod authority",
            ),
            "Creation prerequisite authority",
        )
    elif stage.step_id == "basics":
        device.wait_exact_resource_id_bidirectional(
            "creation-basics-sourcebooks-contract-unavailable", timeout=60,
            backward_scrolls=24, forward_scrolls=24, scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-sourcebooks-contract-unavailable",
            surface_name="Fail-closed creation sourcebook dependency",
        )
        result["sourcebookMutation"] = "typed-contract-unavailable"
    elif stage.step_id == "resources":
        gear = device.wait_exact_resource_id_bidirectional(
            "creation-resources-open-gear", timeout=60, backward_scrolls=24,
            forward_scrolls=24, scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-gear-route", surface_name="Typed creation Gear route",
        )
        _require_enabled(gear, "Finalizer-required typed Gear draft route")
        device.shell("input", "tap", *(str(value) for value in gear.center))
        device.wait_for_single_exact_resource_id(
            "creation-gear-page", timeout=60, evidence_prefix="sr5-priority-gear-page",
            surface_name="Typed creation Gear page",
        )
        device.wait_exact_resource_id_bidirectional(
            "creation-gear-saved-draft-revision", timeout=60, backward_scrolls=24,
            forward_scrolls=24, scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-gear-saved-draft", surface_name="Persisted typed Gear draft",
        )
        result["gearDraft"] = "persisted-typed-authority"
        device.back()
        device.wait_for_single_exact_resource_id(
            "creation-resources-page", timeout=60, evidence_prefix="sr5-priority-gear-return",
            surface_name="Typed creation Resources page",
        )
    elif stage.step_id == "contacts-lifestyles":
        lifestyles = device.wait_exact_resource_id_bidirectional(
            "creation-contacts-open-lifestyles", timeout=60, backward_scrolls=24,
            forward_scrolls=24, scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-lifestyles-route", surface_name="Typed creation Lifestyles route",
        )
        _require_enabled(lifestyles, "Typed creation Lifestyles route")
        device.shell("input", "tap", *(str(value) for value in lifestyles.center))
        device.wait_for_single_exact_resource_id(
            "creation-lifestyles-page", timeout=60, evidence_prefix="sr5-priority-lifestyles-page",
            surface_name="Typed creation Lifestyles page",
        )
        device.wait_exact_resource_id_bidirectional(
            "creation-lifestyles-authority", timeout=60, backward_scrolls=24,
            forward_scrolls=24, scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-lifestyles-authority", surface_name="Typed creation Lifestyles authority",
        )
        result["lifestylesAuthority"] = "visible"
        device.back()
        device.wait_for_single_exact_resource_id(
            "creation-contacts-page", timeout=60, evidence_prefix="sr5-priority-lifestyles-return",
            surface_name="Typed creation Contacts page",
        )
    device.back()
    device.wait_for_single_exact_resource_id(
        "creation-wizard-dashboard", timeout=60,
        evidence_prefix=f"sr5-priority-{stage.step_id}-return", surface_name="Creation wizard dashboard",
    )
    return result


def finalize_exact_build(device: shared.Device) -> dict[str, object]:
    device.wait_exact_resource_id_bidirectional(
        "creation-finalization-authority-ready", timeout=90, backward_scrolls=40,
        forward_scrolls=40, scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-ready", surface_name="Core whole-build finalization readiness",
    )
    review = device.wait_exact_resource_id_bidirectional(
        "creation-finalization-open-review", timeout=90, backward_scrolls=40,
        forward_scrolls=40, scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-review", surface_name="Explicit finalization review action",
    )
    _require_enabled(review, "Core-ready finalization review action")
    device.shell("input", "tap", *(str(value) for value in review.center))
    device.wait_for_single_exact_resource_id(
        "creation-finalization-page", timeout=90, evidence_prefix="sr5-priority-finalization-page",
        surface_name="Sealed creation finalization review",
    )
    reviewed: dict[str, str] = {}
    for identity in (
        "creation-finalization-binding", "creation-finalization-costs",
        "creation-finalization-atomic-boundary",
    ):
        node = device.wait_exact_resource_id_bidirectional(
            identity, timeout=60, backward_scrolls=30, forward_scrolls=30,
            scroll_distance_ratio=0.22, evidence_prefix=identity,
            surface_name="Sealed creation finalization review",
        )
        reviewed[identity] = _node_text(node)
    review = FinalizationReviewAuthority(
        content_revision=_read_positive_revision(
            device, "creation-finalization-content-revision",
            "Sealed review content revision",
        ),
        plan_digest=_read_canonical_digest(
            device, "creation-finalization-plan-digest", "Sealed whole-build plan digest",
        ),
        preview_digest=_read_canonical_digest(
            device, "creation-finalization-preview-digest", "Sealed finalization preview digest",
        ),
    )
    if VISIBLE_FINALIZATION_BINDING.fullmatch(reviewed["creation-finalization-binding"]) is None:
        raise RuntimeError(
            "Whole-build display must use canonical truncated sha256 markers; "
            "the machine-readable authorities remain the only trusted values"
        )
    confirm = device.wait_exact_resource_id_bidirectional(
        "creation-finalization-confirm", timeout=60, backward_scrolls=30,
        forward_scrolls=30, scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-confirm", surface_name="Explicit atomic creation confirmation",
    )
    _require_enabled(confirm, "Sealed whole-build plan")
    device.shell("input", "tap", *(str(value) for value in confirm.center))
    device.wait_for_single_exact_resource_id(
        "creation-finalization-receipt-page", timeout=120,
        evidence_prefix="sr5-priority-finalization-receipt-page", surface_name="Durable creation receipt",
    )
    device.wait_exact_resource_id_bidirectional(
        "creation-finalization-receipt", timeout=60, backward_scrolls=24,
        forward_scrolls=24, scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-receipt", surface_name="Durable creation receipt",
    )
    receipt = FinalizationReceiptAuthority(
        previous_content_revision=_read_positive_revision(
            device, "creation-finalization-receipt-previous-content-revision",
            "Durable receipt previous content revision",
        ),
        content_revision=_read_positive_revision(
            device, "creation-finalization-receipt-content-revision",
            "Durable receipt content revision",
        ),
        saved_revision=_read_positive_revision(
            device, "creation-finalization-receipt-saved-revision",
            "Durable receipt saved revision",
        ),
        build_method=_read_exact_authority(
            device, "creation-finalization-receipt-build-method",
            "Durable receipt BuildMethod",
        ),
        plan_digest=_read_canonical_digest(
            device, "creation-finalization-receipt-plan-digest",
            "Durable receipt plan digest",
        ),
        preview_digest=_read_canonical_digest(
            device, "creation-finalization-receipt-preview-digest",
            "Durable receipt preview digest",
        ),
        receipt_digest=_read_canonical_digest(
            device, "creation-finalization-receipt-digest",
            "Durable creation receipt digest",
        ),
    )
    cross_bind_finalization_authorities(review, receipt)
    reopen = device.wait_exact_resource_id_bidirectional(
        "creation-finalization-career-reopen", timeout=60, backward_scrolls=24,
        forward_scrolls=24, scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-career-reopen", surface_name="Fresh Career reopen verification",
    )
    if "Career mode" not in _node_text(reopen):
        raise RuntimeError("Finalization receipt did not expose a fresh Career reopen result")
    open_career = device.wait_exact_resource_id_bidirectional(
        "creation-finalization-open-career", timeout=60, backward_scrolls=24,
        forward_scrolls=24, scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-open-career", surface_name="Open finalized Career runner",
    )
    _require_enabled(open_career, "Open finalized Career runner")
    device.capture("sr5-priority-finalization-receipt-durable")
    device.shell("input", "tap", *(str(value) for value in open_career.center))
    return {
        "review": "sealed-core-whole-build-plan", "visibleReviewEvidence": reviewed,
        "sealedPlanAuthority": {
            "contentRevision": review.content_revision,
            "planDigest": review.plan_digest,
            "previewDigest": review.preview_digest,
        },
        "receiptAuthority": {
            "previousContentRevision": receipt.previous_content_revision,
            "contentRevision": receipt.content_revision,
            "savedRevision": receipt.saved_revision,
            "buildMethod": receipt.build_method,
            "planDigest": receipt.plan_digest,
            "previewDigest": receipt.preview_digest,
            "receiptDigest": receipt.receipt_digest,
        },
        "confirmation": "explicit-atomic-once", "receipt": "durable", "careerReopen": "verified",
    }


def require_career_surface(device: shared.Device) -> None:
    shared.wait_for_phone_runner_route(device, created=True, timeout=90)
    shared.open_build(device, "phone")
    career = device.wait_exact_resource_id_bidirectional(
        "build-sr5-career-wizard", timeout=90, backward_scrolls=24,
        forward_scrolls=40, scroll_distance_ratio=0.20,
        evidence_prefix="sr5-priority-career-surface", surface_name="Finalized SR5 Career surface",
    )
    _require_enabled(career, "Finalized SR5 Career wizard")


def read_persisted_creation_receipt_digest(device: shared.Device) -> str:
    return _read_canonical_digest(
        device,
        "phone-workspace-creation-receipt-digest",
        "Persisted workspace creation receipt digest",
    )


def bind_saved_workspace_to_receipt(
    workspace: shared.WorkspaceAuthority,
    receipt: dict[str, object],
) -> None:
    content_revision = receipt.get("contentRevision")
    saved_revision = receipt.get("savedRevision")
    if not isinstance(content_revision, int) or not isinstance(saved_revision, int):
        raise RuntimeError("Finalization result omitted exact receipt revisions")
    if workspace.content_revision != content_revision or workspace.saved_revision != saved_revision:
        raise RuntimeError("Saved Career workspace revisions do not match the durable creation receipt")
    digest = receipt.get("receiptDigest")
    if not isinstance(digest, str) or not is_canonical_authority_digest(digest):
        raise RuntimeError("Finalization result omitted the canonical durable receipt digest")


def prove_priority_journey(device: shared.Device, initial_launch: shared.LaunchState) -> dict[str, object]:
    shared.wait_for_phone_runner_route(device, created=False, timeout=90)
    device.wait_for_single_exact_resource_id(
        "creation-wizard-dashboard", timeout=90, evidence_prefix="sr5-priority-dashboard",
        surface_name="Creation wizard dashboard",
    )
    stages = [open_exact_stage(device, stage) for stage in LEGAL_PATH_STAGES]
    method = next(row for row in stages if row["stepId"] == "method")
    require_priority_build_method(str(method.get("buildMethod", "")), "Observed creation stage")
    identity = next(row for row in stages if row["stepId"] == "identity-story")
    expected_identity = {
        "stepId": "identity-story", "routeId": "creation-stage-identity-story",
        "requiredByCurrentFinalizer": False, "routeStatus": "typed-contract-unavailable",
        "authorityVisible": False, "draftFabricated": False,
        "blocker": IDENTITY_CONTRACT_BLOCKER,
    }
    if identity != expected_identity:
        raise RuntimeError("Identity gap changed or acquired fabricated draft authority")
    finalization = finalize_exact_build(device)
    receipt = finalization.get("receiptAuthority")
    if not isinstance(receipt, dict):
        raise RuntimeError("Finalization did not return one exact durable receipt authority")
    require_career_surface(device)
    persisted = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(persisted)
    bind_saved_workspace_to_receipt(persisted, receipt)
    persisted_receipt_digest = read_persisted_creation_receipt_digest(device)
    if persisted_receipt_digest != receipt["receiptDigest"]:
        raise RuntimeError("Career workspace receipt digest does not match the durable finalization receipt")
    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    require_career_surface(device)
    restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(persisted, restored)
    restored_receipt_digest = read_persisted_creation_receipt_digest(device)
    if restored_receipt_digest != persisted_receipt_digest:
        raise RuntimeError("Creation receipt digest changed after force-stop and new-process reopen")
    device.capture("sr5-priority-finalized-career-restored-new-process")
    return {
        "stages": stages, "identityGap": identity,
        "draftStateAuthority": "typed-phone-pages-preexisting-no-seed-or-fabrication",
        "finalization": finalization,
        "savedCareerWorkspace": shared.workspace_authority_json(persisted),
        "restoredCareerWorkspace": shared.workspace_authority_json(restored),
        "persistedCreationReceiptDigest": persisted_receipt_digest,
        "restoredCreationReceiptDigest": restored_receipt_digest,
        "processRestart": {
            "beforeProcessIds": list(restart.before_force_stop.process_ids),
            "afterForceStopProcessIds": list(restart.after_force_stop.process_ids),
            "restartedProcessIds": list(restart.restarted.process_ids), "newPidVerified": True,
        },
    }


def physical_device_observation(device: shared.Device) -> dict[str, object]:
    if device.run("get-state").stdout.strip() != "device":
        raise RuntimeError("The requested physical Android transport is not ready")
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    qemu = device.shell("getprop", "ro.kernel.qemu")
    boot_qemu = device.shell("getprop", "ro.boot.qemu")
    hardware = device.shell("getprop", "ro.hardware")
    fingerprint = device.shell("getprop", "ro.build.fingerprint")
    product_device = device.shell("getprop", "ro.product.device")
    product_name = device.shell("getprop", "ro.product.name")
    if api != "36":
        raise RuntimeError(f"Physical SR5 Priority proof requires API 36, got {api!r}")
    if abi != "arm64-v8a" or "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError(f"Physical SR5 Priority proof requires arm64-v8a, got {abi!r}")
    serial = device.serial.lower()
    serial_is_virtual_transport = (
        serial.startswith("emulator-")
        or serial.startswith("localhost:")
        or serial.startswith("127.0.0.1:")
        or serial.startswith("::1:")
    )
    virtual_surface = "\n".join((hardware, fingerprint, product_device, product_name)).lower()
    if (
        serial_is_virtual_transport
        or qemu not in ("", "0")
        or boot_qemu not in ("", "0")
        or any(token in virtual_surface for token in VIRTUAL_MARKERS)
    ):
        raise RuntimeError("The requested transport is an emulator, not a physical phone")
    return {
        "classification": "non-emulator-arm64-api36",
        "evidenceNature": "non-cryptographic getprop and adb serial observations",
        "serial": device.serial, "apiLevel": 36, "abi": abi, "abiList": abi_list,
        "qemu": qemu, "bootQemu": boot_qemu,
        "manufacturer": device.shell("getprop", "ro.product.manufacturer"),
        "model": device.shell("getprop", "ro.product.model"), "hardware": hardware,
        "productDevice": product_device, "productName": product_name,
        "buildFingerprint": fingerprint,
        "buildId": device.shell("getprop", "ro.build.id"),
        "securityPatch": device.shell("getprop", "ro.build.version.security_patch"),
        "verifiedBootState": device.shell("getprop", "ro.boot.verifiedbootstate"),
    }


def _reject_symlink_components(path: Path) -> None:
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        try:
            mode = os.lstat(current).st_mode
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(mode):
            raise RuntimeError(f"Receipt path contains a symlink component: {current}")


def validate_output_paths(receipt: Path, evidence: Path, source_roots: tuple[Path, ...]) -> None:
    for path, label in ((receipt, "Receipt"), (evidence, "Evidence")):
        if not path.is_absolute() or any(part in {".", ".."} for part in path.parts):
            raise RuntimeError(f"{label} path must be absolute and normalized")
        _reject_symlink_components(path)
        for source_root in source_roots:
            try:
                path.relative_to(source_root)
            except ValueError:
                continue
            raise RuntimeError(f"{label} path must remain outside source worktrees")
    if receipt == evidence or receipt in evidence.parents or evidence in receipt.parents:
        raise RuntimeError("Receipt and evidence outputs must not overlap")


def write_receipt_durably(path: Path, receipt: dict[str, object]) -> None:
    if path.exists() or path.is_symlink():
        raise RuntimeError("Receipt target already exists; stale evidence is never overwritten")
    path.parent.mkdir(parents=True, exist_ok=True)
    _reject_symlink_components(path)
    encoded = (json.dumps(receipt, allow_nan=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile("wb", dir=path.parent, prefix=f".{path.name}.", delete=False) as stream:
            temporary_name = stream.name
            os.fchmod(stream.fileno(), 0o600)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary_name, path, follow_symlinks=False)
        directory = os.open(path.parent, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if temporary_name is not None:
            Path(temporary_name).unlink(missing_ok=True)


def source_roots(android_root: Path, workspace_root: Path) -> tuple[Path, ...]:
    return (
        android_root.resolve(), (workspace_root / "chummer-core-engine").resolve(),
        (workspace_root / "chummer-presentation").resolve(),
    )


def execute(args: argparse.Namespace, context: dict[str, object]) -> dict[str, object]:
    validate_stage_catalog()
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{DISPOSABLE_DEVICE_FLAG} is required because this journey installs an APK "
            "and atomically finalizes a pending runner on a disposable phone"
        )
    if SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the exact safe grammar")
    context["disposableDeviceAuthorization"] = {
        "authorized": True,
        "flag": DISPOSABLE_DEVICE_FLAG,
        "serial": args.serial,
        "scope": "install-apk-and-atomically-finalize-one-pending-runner",
    }
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    apk = args.apk.resolve()
    validate_output_paths(args.receipt, args.evidence, source_roots(android_root, workspace_root))
    manifest_file_before = provenance_file_identity(args.build_provenance_manifest)
    manifest = load_and_verify_manifest(
        args.build_provenance_manifest, android_root=android_root, core_root=core_root,
        presentation_root=presentation_root, apk=apk,
    )
    manifest_file_after_parse = provenance_file_identity(args.build_provenance_manifest)
    if manifest_file_after_parse != manifest_file_before:
        raise RuntimeError("Build-provenance manifest bytes changed while validating its payload")
    artifact = manifest.get("artifact")
    if not isinstance(artifact, dict) or not isinstance(artifact.get("sha256"), str):
        raise RuntimeError("Verified build-provenance artifact is malformed")
    expected_apk_sha256 = artifact["sha256"]
    context["buildProvenance"] = manifest
    context["buildProvenanceFile"] = provenance_file_identity_json(manifest_file_before)
    context["releaseEvidenceStatus"] = "source-and-apk-bound-local-build-not-release-attested"
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    device.require_transport_stability(expected_api_level="36")
    observation = physical_device_observation(device)
    context["deviceObservation"] = observation
    device.install_verified(apk, expected_apk_sha256, "--no-streaming", "-r")
    initial_launch = shared.launch_app(device)
    journey = prove_priority_journey(device, initial_launch)
    manifest_file_before_recheck = provenance_file_identity(args.build_provenance_manifest)
    if manifest_file_before_recheck != manifest_file_before:
        raise RuntimeError("Build-provenance manifest bytes changed during physical execution")
    manifest_after = load_and_verify_manifest(
        args.build_provenance_manifest, android_root=android_root, core_root=core_root,
        presentation_root=presentation_root, apk=apk,
    )
    manifest_file_after_recheck = provenance_file_identity(args.build_provenance_manifest)
    if manifest_file_after_recheck != manifest_file_before:
        raise RuntimeError("Build-provenance manifest bytes changed during post-run validation")
    if manifest_after != manifest:
        raise RuntimeError("Source/APK build provenance changed during physical execution")
    transport = device.transport_summary()
    if transport.get("status") != "pass":
        raise RuntimeError("ADB transport summary is not a complete pass")
    return {
        "schema": SCHEMA, "status": "device-pass-source-bound", "executionStatus": "pass",
        "releaseEvidenceStatus": context["releaseEvidenceStatus"], "releaseAttested": False,
        "publicationAuthorized": False, "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "journey": "sr5-priority-create-physical", "buildMethod": "Priority",
        "profile": "phone", "serial": args.serial,
        "apiLevel": observation["apiLevel"], "abi": observation["abi"], "package": shared.PACKAGE,
        "apk": str(apk), "apkSha256": expected_apk_sha256, "buildProvenance": manifest,
        "buildProvenanceFile": provenance_file_identity_json(manifest_file_before),
        "buildProvenanceRecheckedAfterRun": True,
        "buildProvenanceFileRecheckedAfterRun": True,
        "disposableDeviceAuthorization": context["disposableDeviceAuthorization"],
        "deviceObservation": observation,
        "adbTransport": transport, "physicalDeviceProof": True, "installedArtifactBound": True,
        "draftStateFabricated": False, "identityContractStatus": "typed-contract-unavailable",
        "authorityProofStages": journey,
    }


def failure_receipt(args: argparse.Namespace, error: Exception, context: dict[str, object]) -> dict[str, object]:
    return {
        "schema": SCHEMA, "status": "fail", "executionStatus": "fail",
        "releaseEvidenceStatus": context.get("releaseEvidenceStatus", "manifest-not-verified"),
        "releaseAttested": False, "publicationAuthorized": False,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "journey": "sr5-priority-create-physical", "profile": "phone", "serial": args.serial,
        "buildProvenanceManifest": str(args.build_provenance_manifest),
        "failure": {"type": type(error).__name__, "message": str(error)[:4000]}, **context,
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
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    driver = Path(__file__).resolve()
    try:
        validate_output_paths(
            args.receipt,
            args.evidence,
            source_roots(driver.parents[1], args.workspace_root.resolve()),
        )
        if args.receipt.exists() or args.receipt.is_symlink():
            raise RuntimeError("Receipt target already exists; stale evidence is never overwritten")
    except Exception as error:  # noqa: BLE001 - unsafe output gets no receipt mutation
        print(f"SR5 Priority output validation failed: {error}", file=sys.stderr)
        return 2
    context: dict[str, object] = {}
    try:
        receipt = execute(args, context)
    except Exception as error:  # noqa: BLE001 - stale/partial pass must never survive
        receipt = failure_receipt(args, error, context)
        try:
            write_receipt_durably(args.receipt, receipt)
        except Exception as receipt_error:  # noqa: BLE001 - unsafe output gets no replacement
            print(f"Cannot write SR5 Priority failure receipt: {receipt_error}", file=sys.stderr)
            return 2
        print(f"Physical SR5 Priority creation E2E failed: {error}", file=sys.stderr)
        return 1
    write_receipt_durably(args.receipt, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
