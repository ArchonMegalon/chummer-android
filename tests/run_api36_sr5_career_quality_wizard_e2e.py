#!/usr/bin/env python3
"""Prove the SR5 Career Quality wizard on a physical ARM64 API-36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sys
import time
import xml.etree.ElementTree as ET

from api36_physical_build_provenance import load_and_verify_manifest
import run_api36_editing_e2e as shared
import run_api36_sr5_career_active_skill_wizard_e2e as physical


SCHEMA = "chummer.android.sr5-career-quality-physical-e2e/v1"
CHECKPOINT_KEY = "sr5.career.quality.draft.v1"
MUTATION_OWNER_KEY = "sr5.career.mutation-owner.v1"
CHOOSE_ROUTE = "sr5-career/advancement/quality/choose"
REVIEW_ROUTE = "sr5-career/advancement/quality/review"
RECEIPT_ROUTE = "sr5-career/advancement/quality/receipt"
ALLOW_MUTATION_FLAG = "--allow-destructive-disposable-device"
SHA256 = re.compile(r"^[0-9a-f]{64}$")
SAFE_SERIAL = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument(ALLOW_MUTATION_FLAG, action="store_true")
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent
        / "fixtures/career-quality-level-e2e.chum5",
    )
    return parser.parse_args(argv)


def strict_json(serialized: str) -> dict[str, object]:
    value = json.loads(serialized, object_pairs_hook=physical.object_without_duplicates)
    if not isinstance(value, dict):
        raise RuntimeError("Quality checkpoint is not one JSON object")
    return value


def read_preference(device: shared.Device, key: str) -> list[str]:
    listing = device.shell(
        "run-as", shared.PACKAGE, "find", "shared_prefs", "-type", "f", "-name", "*.xml"
    )
    values: list[str] = []
    for preference_file in (line.strip() for line in listing.splitlines() if line.strip()):
        root = ET.fromstring(
            device.run("exec-out", "run-as", shared.PACKAGE, "cat", preference_file).stdout
        )
        values.extend(
            node.text or ""
            for node in root.findall("string")
            if node.get("name") == key
        )
    return values


def read_checkpoint(
    device: shared.Device, *, required: bool = True
) -> tuple[str, dict[str, object]] | None:
    values = read_preference(device, CHECKPOINT_KEY)
    if not values:
        if required:
            raise RuntimeError("Durable Quality checkpoint is missing")
        return None
    if len(values) != 1 or not values[0]:
        raise RuntimeError("Quality checkpoint cardinality is not exact")
    return hashlib.sha256(values[0].encode("utf-8")).hexdigest(), strict_json(values[0])


def validate_checkpoint(
    checkpoint: tuple[str, dict[str, object]],
    *,
    workspace: shared.WorkspaceAuthority,
    phase: int,
    version: int,
) -> None:
    _, payload = checkpoint
    if set(payload) != {
        "SchemaVersion", "Version", "RouteId", "Phase", "Draft", "IdempotencyKey"
    }:
        raise RuntimeError("Quality checkpoint fields are not exact")
    if (
        payload["SchemaVersion"] != 1
        or payload["Version"] != version
        or payload["RouteId"] != REVIEW_ROUTE
        or payload["Phase"] != phase
    ):
        raise RuntimeError("Quality checkpoint schema/phase/version/route is not exact")
    idempotency = payload["IdempotencyKey"]
    if not isinstance(idempotency, str) or SHA256.fullmatch(idempotency) is None:
        raise RuntimeError("Quality checkpoint idempotency key is not SHA-256")
    draft = payload["Draft"]
    if not isinstance(draft, dict):
        raise RuntimeError("Quality checkpoint draft is missing")
    review = draft.get("Review")
    action = draft.get("ActionPlan")
    if not isinstance(review, dict) or not isinstance(action, dict):
        raise RuntimeError("Quality checkpoint lost its typed review/action")
    review_draft = review.get("Draft")
    if not isinstance(review_draft, dict):
        raise RuntimeError("Quality checkpoint lost its Presentation draft")
    workspace_id = review_draft.get("WorkspaceId")
    if not isinstance(workspace_id, dict) or workspace_id.get("Value") != workspace.workspace_id:
        raise RuntimeError("Quality checkpoint belongs to another workspace")
    if (
        review_draft.get("ExpectedWorkspaceRevision") != workspace.content_revision
        or review_draft.get("ExpectedSavedRevision") != workspace.saved_revision
    ):
        raise RuntimeError("Quality checkpoint lost workspace/saved revision CAS")
    if action.get("Kind") != 3 or action.get("RouteId") != REVIEW_ROUTE:
        raise RuntimeError("Generic action lost specialized Quality kind/route")
    if action.get("IdempotencyKey") != idempotency:
        raise RuntimeError("Quality action/checkpoint idempotency differs")


def wait_route(device: shared.Device, route: str) -> None:
    device.wait_for_single_exact_accessibility_value(
        route,
        timeout=180,
        evidence_prefix="sr5-career-quality-route",
        surface_name="SR5 Career Quality route",
    )


def tap_route(device: shared.Device, route: str) -> None:
    node = device.wait_for_single_exact_accessibility_value(
        route,
        timeout=120,
        evidence_prefix="sr5-career-quality-route",
        surface_name="SR5 Career Quality route",
    )
    if not device.node_has_tappable_bounds(node):
        raise RuntimeError(f"Quality route is not tappable: {route}")
    device.shell("input", "tap", *(str(value) for value in node.center))


def open_choose(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=16)
    device.tap_bidirectional(
        "build-sr5-career-wizard",
        timeout=120,
        backward_scrolls=16,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    wait_route(device, "sr5-career")
    tap_route(device, "sr5-career/advancement")
    wait_route(device, "sr5-career/advancement")
    device.tap_single_exact_resource_id(
        "sr5-career-action-quality",
        timeout=90,
        evidence_prefix="sr5-career-quality-action",
        surface_name="SR5 Career Quality action",
    )
    wait_route(device, CHOOSE_ROUTE)


def import_fixture(
    device: shared.Device, fixture: Path, fixture_sha256: str
) -> tuple[shared.LaunchState, shared.WorkspaceAuthority]:
    launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    shared.record_phone_ui_locale_evidence(
        device,
        evidence_prefix="sr5-career-quality",
    )
    device.tap("home-open-file")
    shared.select_android_document(device, fixture.name)
    device.wait("CareerQualityLevelE2E", timeout=120)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    authority = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def prove(device: shared.Device, fixture: Path, fixture_sha256: str) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = import_fixture(device, fixture, fixture_sha256)
    open_choose(device)
    picker = device.wait_for_single_exact_resource_id(
        "sr5-career-quality-picker",
        timeout=90,
        evidence_prefix="sr5-career-quality-picker",
        surface_name="SR5 Career Quality picker",
    )
    if not (picker.attributes.get("text") or "").strip():
        raise RuntimeError("Quality wizard rendered no exact typed candidate")
    device.capture("sr5-career-quality-choose")
    device.tap_single_exact_resource_id(
        "sr5-career-quality-review",
        timeout=90,
        evidence_prefix="sr5-career-quality-review",
        surface_name="SR5 Career Quality review",
    )
    wait_route(device, REVIEW_ROUTE)
    reviewed = read_checkpoint(device)
    if reviewed is None:
        raise RuntimeError("Reviewed Quality checkpoint disappeared")
    validate_checkpoint(reviewed, workspace=imported, phase=0, version=1)

    reviewed_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(imported, restored)
    if read_checkpoint(device) != reviewed:
        raise RuntimeError("Reviewed Quality checkpoint changed across restart")
    open_choose(device)
    device.tap_bidirectional(
        "sr5-career-quality-resume",
        timeout=120,
        backward_scrolls=20,
        forward_scrolls=20,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    wait_route(device, REVIEW_ROUTE)
    device.tap_bidirectional(
        "sr5-career-quality-apply",
        timeout=180,
        backward_scrolls=30,
        forward_scrolls=30,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    wait_route(device, RECEIPT_ROUTE)
    applied = read_checkpoint(device)
    if applied is None:
        raise RuntimeError("Applied Quality checkpoint disappeared")
    validate_checkpoint(applied, workspace=imported, phase=2, version=3)
    if applied[1]["Draft"] != reviewed[1]["Draft"] or applied[1]["IdempotencyKey"] != reviewed[1]["IdempotencyKey"]:
        raise RuntimeError("Applied Quality checkpoint differs from reviewed action")
    if read_preference(device, MUTATION_OWNER_KEY):
        raise RuntimeError("Resolved Quality transaction retained shared mutation owner")
    device.capture("sr5-career-quality-receipt")

    applied_restart = shared.force_stop_and_launch_new_process(
        device, reviewed_restart.restarted
    )
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    open_choose(device)
    wait_route(device, RECEIPT_ROUTE)
    if read_checkpoint(device) != applied:
        raise RuntimeError("Applied Quality checkpoint changed across restart")
    device.tap_bidirectional(
        "sr5-career-quality-receipt-acknowledge",
        timeout=120,
        backward_scrolls=30,
        forward_scrolls=30,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    time.sleep(1)
    if read_checkpoint(device, required=False) is not None:
        raise RuntimeError("Acknowledged Quality checkpoint remains durable")
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    saved = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(saved)
    if (
        saved.workspace_id != imported.workspace_id
        or saved.content_revision != imported.content_revision + 1
        or saved.payload_sha256 == imported.payload_sha256
    ):
        raise RuntimeError("Quality transaction did not save one changed successor revision")

    final_restart = shared.force_stop_and_launch_new_process(
        device, applied_restart.restarted
    )
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    final_saved = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(saved, final_saved)
    if read_checkpoint(device, required=False) is not None:
        raise RuntimeError("Quality checkpoint deletion did not survive final restart")
    return {
        "import": shared.workspace_authority_json(imported),
        "saved": shared.workspace_authority_json(saved),
        "finalSaved": shared.workspace_authority_json(final_saved),
        "reviewedCheckpointSha256": reviewed[0],
        "appliedCheckpointSha256": applied[0],
        "restartProcessIds": [
            list(reviewed_restart.restarted.process_ids),
            list(applied_restart.restarted.process_ids),
            list(final_restart.restarted.process_ids),
        ],
    }


def execute(args: argparse.Namespace) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(f"{ALLOW_MUTATION_FLAG} is required")
    if SAFE_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe grammar")
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    repositories = physical.source_repository_roots(
        android_root=android_root, workspace_root=workspace_root
    )
    physical.validate_external_output_path(
        args.receipt, label="Receipt path", repository_roots=repositories, expect_directory=False
    )
    physical.validate_external_output_path(
        args.evidence, label="Evidence path", repository_roots=repositories, expect_directory=True
    )
    physical.validate_output_layout(receipt=args.receipt, evidence=args.evidence)
    apk = args.apk.resolve()
    provenance = load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    fixture = args.career_runner.resolve()
    fixture_sha256 = shared.sha256(fixture)
    args.evidence.mkdir(parents=True, exist_ok=True)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    device.require_transport_stability(expected_api_level="36")
    observation = physical.android_device_observation(device)
    artifact = provenance.get("artifact")
    if not isinstance(artifact, dict):
        raise RuntimeError("Verified build-provenance artifact is malformed")
    expected_apk_sha256 = str(artifact.get("sha256", ""))
    device.install_verified(
        apk,
        expected_apk_sha256,
        "--no-streaming",
        "-r",
    )
    remote_fixture = f"/sdcard/Download/{physical.safe_fixture_basename(fixture)}"
    physical.remove_remote_temporary_file(device, remote_fixture)
    try:
        device.push_verified(fixture, remote_fixture, fixture_sha256)
        journey = prove(device, fixture, fixture_sha256)
    finally:
        physical.remove_remote_temporary_file(device, remote_fixture)
    if load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    ) != provenance:
        raise RuntimeError("Source/APK provenance changed during Quality proof")
    return {
        "schema": SCHEMA,
        "status": "device-pass-source-bound",
        "executionStatus": "pass",
        "releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "package": shared.PACKAGE,
        "apkSha256": provenance["artifact"]["sha256"],
        "buildProvenance": provenance,
        "deviceObservation": observation,
        "adbTransport": device.transport_summary(),
        "authorityProofStages": journey,
    }


def main(argv: list[str] | None = None) -> int:
    raw = list(sys.argv[1:] if argv is None else argv)
    if any(value in {"-h", "--help"} for value in raw):
        try:
            parse_args(["--help"])
        except SystemExit as error:
            return int(error.code or 0)
        return 0
    receipt_path: Path | None = None
    try:
        receipt_path = physical.locate_explicit_receipt(raw)
        physical.prepare_receipt_target(receipt_path)
        args = parse_args(raw)
        receipt = execute(args)
    except Exception as error:  # noqa: BLE001 - failed proof must leave a receipt
        receipt = {
            "schema": SCHEMA,
            "status": "fail",
            "executionStatus": "fail",
            "releaseEvidenceStatus": "manifest-not-verified-or-journey-failed",
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
            "adbTransportFailure": getattr(error, "receipt", None),
        }
        try:
            if receipt_path is None:
                raise RuntimeError("No safe explicit receipt target was available")
            physical.write_receipt_atomically(receipt_path, receipt)
        except Exception:
            pass
        print(f"Physical Quality proof failed: {error}", file=sys.stderr)
        return 1
    physical.write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
