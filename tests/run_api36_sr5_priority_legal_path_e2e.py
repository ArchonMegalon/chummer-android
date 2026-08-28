#!/usr/bin/env python3
"""Development-only API-36 observation of one typed SR5 Priority runner.

This driver starts from a runner whose individual typed drafts were prepared by
the dedicated phone pages.  It does not invent allocations, seed auxiliary
state, or bypass a disabled stage.  A pass observes exact route/page identity,
persisted draft readback, whole-build Core readiness, explicit finalization,
and a fresh Career reopen.  It is intentionally not wired into a workflow,
release matrix, finalizer, device-proof claim, or completion claim.  It does
not install or bind an APK, so its receipt cannot identify installed bytes.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


SCHEMA = "chummer.android.sr5-priority-legal-path-development-observation/v1"
IDENTITY_CONTRACT_BLOCKER = "creation-identity-draft-contract-unavailable"


@dataclass(frozen=True)
class LegalPathStage:
    step_id: str
    route_id: str
    page_id: str | None
    authority_id: str | None
    required_by_finalizer: bool
    expected_clickable: bool = True


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
        "basics",
        "method",
        "foundation",
        "attributes",
        "qualities",
        "skills",
        "magic-resonance",
        "resources",
        "contacts-lifestyles",
        "identity-story",
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
    if any(stage.page_id is not None or stage.authority_id is not None for stage in stages if not stage.expected_clickable):
        raise RuntimeError("A blocked SR5 Priority stage must not name a fallback page or authority")


def open_exact_stage(device: shared.Device, stage: LegalPathStage) -> dict[str, str | bool]:
    row = device.wait_exact_resource_id_bidirectional(
        stage.route_id,
        timeout=90,
        backward_scrolls=30,
        forward_scrolls=30,
        scroll_distance_ratio=0.22,
        evidence_prefix=f"sr5-priority-{stage.step_id}-route",
        surface_name=f"SR5 Priority {stage.step_id} stage",
    )
    enabled = row.attributes.get("enabled") == "true"
    clickable = row.attributes.get("clickable") == "true"
    if not stage.expected_clickable:
        reason = row.attributes.get("content-desc") or row.attributes.get("text") or ""
        if enabled or clickable:
            raise RuntimeError(
                f"Typed SR5 Priority stage {stage.step_id!r} exposed an unsupported mutation route"
            )
        if IDENTITY_CONTRACT_BLOCKER not in reason:
            raise RuntimeError(
                f"Typed SR5 Priority stage {stage.step_id!r} did not expose its exact contract blocker"
            )
        device.capture(f"sr5-priority-{stage.step_id}-blocked")
        return {
            "stepId": stage.step_id,
            "routeId": stage.route_id,
            "requiredByCurrentFinalizer": stage.required_by_finalizer,
            "routeStatus": "typed-contract-unavailable",
            "authorityVisible": False,
        }
    if not enabled or not clickable:
        reason = row.attributes.get("content-desc") or row.attributes.get("text") or "no disable reason"
        device.capture(f"sr5-priority-{stage.step_id}-blocked")
        raise RuntimeError(
            f"Typed SR5 Priority stage {stage.step_id!r} is blocked; no fallback is allowed: {reason}"
        )
    if stage.page_id is None or stage.authority_id is None:
        raise RuntimeError(f"Clickable SR5 Priority stage {stage.step_id!r} has no typed page authority")
    device.shell("input", "tap", *(str(value) for value in row.center))
    device.wait_for_single_exact_resource_id(
        stage.page_id,
        timeout=60,
        evidence_prefix=f"sr5-priority-{stage.step_id}-page",
        surface_name=f"SR5 Priority {stage.step_id} page",
    )
    authority = device.wait_exact_resource_id_bidirectional(
        stage.authority_id,
        timeout=60,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.22,
        evidence_prefix=f"sr5-priority-{stage.step_id}-authority",
        surface_name=f"SR5 Priority {stage.step_id} authority",
    )
    device.capture(f"sr5-priority-{stage.step_id}-authority")
    result = {
        "stepId": stage.step_id,
        "routeId": stage.route_id,
        "pageId": stage.page_id,
        "authorityId": stage.authority_id,
        "requiredByCurrentFinalizer": stage.required_by_finalizer,
        "authorityVisible": authority is not None,
    }
    if stage.step_id == "basics":
        device.wait_exact_resource_id_bidirectional(
            "creation-basics-sourcebooks-contract-unavailable",
            timeout=60,
            backward_scrolls=24,
            forward_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-sourcebooks-contract-unavailable",
            surface_name="Fail-closed creation sourcebook dependency",
        )
        result["sourcebookMutation"] = "typed-contract-unavailable"
    elif stage.step_id == "resources":
        gear = device.wait_exact_resource_id_bidirectional(
            "creation-resources-open-gear",
            timeout=60,
            backward_scrolls=24,
            forward_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-gear-route",
            surface_name="Typed creation Gear route",
        )
        if gear.attributes.get("enabled") != "true" or gear.attributes.get("clickable") != "true":
            raise RuntimeError("Finalizer-required typed Gear draft route is blocked")
        device.shell("input", "tap", *(str(value) for value in gear.center))
        device.wait_for_single_exact_resource_id(
            "creation-gear-page",
            timeout=60,
            evidence_prefix="sr5-priority-gear-page",
            surface_name="Typed creation Gear page",
        )
        device.wait_exact_resource_id_bidirectional(
            "creation-gear-saved-draft-revision",
            timeout=60,
            backward_scrolls=24,
            forward_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-gear-saved-draft",
            surface_name="Persisted typed Gear draft",
        )
        result["gearDraft"] = "persisted"
        device.back()
        device.wait_for_single_exact_resource_id(
            "creation-resources-page",
            timeout=60,
            evidence_prefix="sr5-priority-gear-return",
            surface_name="Typed creation Resources page",
        )
    elif stage.step_id == "contacts-lifestyles":
        lifestyles = device.wait_exact_resource_id_bidirectional(
            "creation-contacts-open-lifestyles",
            timeout=60,
            backward_scrolls=24,
            forward_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-lifestyles-route",
            surface_name="Typed creation Lifestyles route",
        )
        if lifestyles.attributes.get("enabled") != "true" or lifestyles.attributes.get("clickable") != "true":
            raise RuntimeError("Typed creation Lifestyles route is blocked")
        device.shell("input", "tap", *(str(value) for value in lifestyles.center))
        device.wait_for_single_exact_resource_id(
            "creation-lifestyles-page",
            timeout=60,
            evidence_prefix="sr5-priority-lifestyles-page",
            surface_name="Typed creation Lifestyles page",
        )
        device.wait_exact_resource_id_bidirectional(
            "creation-lifestyles-authority",
            timeout=60,
            backward_scrolls=24,
            forward_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="sr5-priority-lifestyles-authority",
            surface_name="Typed creation Lifestyles authority",
        )
        result["lifestylesAuthority"] = "visible"
        device.back()
        device.wait_for_single_exact_resource_id(
            "creation-contacts-page",
            timeout=60,
            evidence_prefix="sr5-priority-lifestyles-return",
            surface_name="Typed creation Contacts page",
        )
    device.back()
    device.wait_for_single_exact_resource_id(
        "creation-wizard-dashboard",
        timeout=60,
        evidence_prefix=f"sr5-priority-{stage.step_id}-return",
        surface_name="Creation wizard dashboard",
    )
    return result


def finalize_exact_build(device: shared.Device) -> dict[str, str]:
    device.wait_exact_resource_id_bidirectional(
        "creation-finalization-authority-ready",
        timeout=90,
        backward_scrolls=40,
        forward_scrolls=40,
        scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-ready",
        surface_name="Core whole-build finalization readiness",
    )
    review = device.wait_exact_resource_id_bidirectional(
        "creation-finalization-open-review",
        timeout=90,
        backward_scrolls=40,
        forward_scrolls=40,
        scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-review",
        surface_name="Explicit finalization review action",
    )
    if review.attributes.get("enabled") != "true" or review.attributes.get("clickable") != "true":
        raise RuntimeError("Core-ready finalization review action is not enabled")
    device.shell("input", "tap", *(str(value) for value in review.center))
    device.wait_for_single_exact_resource_id(
        "creation-finalization-page",
        timeout=90,
        evidence_prefix="sr5-priority-finalization-page",
        surface_name="Sealed creation finalization review",
    )
    for identity in (
        "creation-finalization-binding",
        "creation-finalization-costs",
        "creation-finalization-atomic-boundary",
    ):
        device.wait_exact_resource_id_bidirectional(
            identity,
            timeout=60,
            backward_scrolls=30,
            forward_scrolls=30,
            scroll_distance_ratio=0.22,
            evidence_prefix=identity,
            surface_name="Sealed creation finalization review",
        )
    confirm = device.wait_exact_resource_id_bidirectional(
        "creation-finalization-confirm",
        timeout=60,
        backward_scrolls=30,
        forward_scrolls=30,
        scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-confirm",
        surface_name="Explicit atomic creation confirmation",
    )
    if confirm.attributes.get("enabled") != "true" or confirm.attributes.get("clickable") != "true":
        raise RuntimeError("Sealed whole-build plan is not explicitly confirmable")
    device.shell("input", "tap", *(str(value) for value in confirm.center))
    device.wait_for_single_exact_resource_id(
        "creation-finalization-receipt-page",
        timeout=120,
        evidence_prefix="sr5-priority-finalization-receipt-page",
        surface_name="Durable creation receipt",
    )
    device.wait_exact_resource_id_bidirectional(
        "creation-finalization-receipt",
        timeout=60,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-finalization-receipt",
        surface_name="Durable creation receipt",
    )
    reopen = device.wait_exact_resource_id_bidirectional(
        "creation-finalization-career-reopen",
        timeout=60,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.22,
        evidence_prefix="sr5-priority-career-reopen",
        surface_name="Fresh Career reopen verification",
    )
    if "Career mode" not in (reopen.attributes.get("text") or reopen.attributes.get("content-desc") or ""):
        raise RuntimeError("Finalization receipt did not expose a fresh Career reopen result")
    device.capture("sr5-priority-finalized-career-reopened")
    return {
        "review": "sealed-core-plan",
        "confirmation": "explicit-atomic",
        "receipt": "durable",
        "careerReopen": "verified",
    }


def execute(args: argparse.Namespace) -> int:
    validate_stage_catalog()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"SR5 Priority legal-path observation requires API 36, got {api!r}")
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=90)
    device.wait_for_single_exact_resource_id(
        "creation-wizard-dashboard",
        timeout=90,
        evidence_prefix="sr5-priority-dashboard",
        surface_name="Creation wizard dashboard",
    )
    stages = [open_exact_stage(device, stage) for stage in LEGAL_PATH_STAGES]
    finalization = finalize_exact_build(device)
    receipt = {
        "schema": SCHEMA,
        "status": "development-observation",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "apiLevel": int(api),
        "driverSha256": shared.sha256(Path(__file__).resolve()),
        "stages": stages,
        "finalization": finalization,
        "deviceProof": False,
        "installedArtifactBound": False,
        "releaseAuthority": False,
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.receipt.with_name(f".{args.receipt.name}.tmp")
    temporary.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    temporary.replace(args.receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    return execute(parser.parse_args(argv))


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"SR5 Priority legal-path development observation failed: {error}", flush=True)
        raise SystemExit(1) from error
