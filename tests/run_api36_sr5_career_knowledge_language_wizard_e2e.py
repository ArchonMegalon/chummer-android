#!/usr/bin/env python3
"""Run fail-closed SR5 Knowledge/Language proof on a physical API 36 ARM64 phone.

This driver intentionally rejects emulators and x86 transports. It is source proof
until somebody runs it with an explicit disposable-device acknowledgement and an
APK digest; its mere presence is never a hosted-x86 or physical-device result.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared
import run_api36_sr5_career_active_skill_wizard_e2e as physical
from api36_physical_build_provenance import load_and_verify_manifest


CHECKPOINT_KEY = "sr5.career.knowledge-language.draft.v1"
MUTATION_OWNER_KEY = "sr5.career.mutation-owner.v1"
CHOOSE_ROUTE = "sr5-career/advancement/knowledge-language/choose"
REVIEW_ROUTE = "sr5-career/advancement/knowledge-language/review"
RECEIPT_ROUTE = "sr5-career/advancement/knowledge-language/receipt"
ALLOW_MUTATION_FLAG = "--allow-destructive-disposable-device"
LOWER_SHA256 = re.compile(r"^[0-9a-f]{64}$")
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
        / "fixtures/career-knowledge-language-advance-e2e.chum5",
    )
    return parser.parse_args(argv)


def atomic_json(path: Path, value: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", dir=path.parent, delete=False
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = Path(stream.name)
    temporary.replace(path)


def physical_device_observation(device: shared.Device) -> dict[str, object]:
    if device.run("get-state").stdout.strip() != "device":
        raise RuntimeError("The requested ADB transport is not ready")
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Physical proof requires Android API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Physical proof requires arm64-v8a, got {abi!r}")
    qemu = device.shell("getprop", "ro.kernel.qemu")
    hardware = device.shell("getprop", "ro.hardware")
    characteristics = device.shell("getprop", "ro.build.characteristics")
    if (
        device.serial.startswith("emulator-")
        or qemu == "1"
        or "emulator" in characteristics.lower()
        or any(token in hardware.lower() for token in ("goldfish", "ranchu", "cuttlefish"))
    ):
        raise RuntimeError("The requested transport is an emulator, not a physical phone")
    return {
        "classification": "observed-non-emulator-arm64-api36",
        "evidenceNature": "non-cryptographic adb/getprop observation",
        "serial": device.serial,
        "apiLevel": int(api),
        "abi": abi,
        "qemu": qemu,
        "hardware": hardware,
        "characteristics": characteristics,
        "manufacturer": device.shell("getprop", "ro.product.manufacturer"),
        "model": device.shell("getprop", "ro.product.model"),
        "buildFingerprint": device.shell("getprop", "ro.build.fingerprint"),
    }


def wait_route(device: shared.Device, route: str, timeout: int = 120) -> shared.UiNode:
    return device.wait_for_single_exact_accessibility_value(
        route,
        timeout=timeout,
        evidence_prefix="sr5-career-knowledge-language-route",
        surface_name="SR5 Career Knowledge/Language route",
    )


def tap_route(device: shared.Device, route: str) -> None:
    node = wait_route(device, route)
    if not device.node_has_tappable_bounds(node):
        raise RuntimeError(f"Exact SR5 Career route is not tappable: {route}")
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))


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
        "sr5-career-action-knowledge-language",
        timeout=90,
        evidence_prefix="sr5-career-knowledge-language-action",
        surface_name="SR5 Career Knowledge/Language action",
    )
    wait_route(device, CHOOSE_ROUTE)


def read_serialized_preference(device: shared.Device, key: str) -> list[str]:
    listing = device.shell(
        "run-as", shared.PACKAGE, "find", "shared_prefs", "-type", "f", "-name", "*.xml"
    )
    serialized_values: list[str] = []
    for preference_file in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run(
            "exec-out", "run-as", shared.PACKAGE, "cat", preference_file
        ).stdout
        root = ET.fromstring(raw)
        serialized_values.extend(
            node.text or ""
            for node in root.findall("string")
            if node.get("name") == key
        )
    return serialized_values


def read_checkpoint(
    device: shared.Device, *, required: bool = True
) -> tuple[str, dict[str, object]] | None:
    serialized_values = read_serialized_preference(device, CHECKPOINT_KEY)
    if not serialized_values:
        if required:
            raise RuntimeError("The durable Knowledge/Language checkpoint is missing")
        return None
    if len(serialized_values) != 1 or not serialized_values[0]:
        raise RuntimeError("Knowledge/Language checkpoint cardinality is not exact")
    serialized = serialized_values[0]
    payload = json.loads(serialized)
    if not isinstance(payload, dict):
        raise RuntimeError("Knowledge/Language checkpoint is not a JSON object")
    return hashlib.sha256(serialized.encode()).hexdigest(), payload


def validate_checkpoint(
    checkpoint: tuple[str, dict[str, object]],
    *,
    phase: int,
    version: int,
    workspace: shared.WorkspaceAuthority,
) -> None:
    _, payload = checkpoint
    if payload.get("SchemaVersion") != 1:
        raise RuntimeError("Checkpoint schema is not exact")
    if payload.get("Version") != version or payload.get("Phase") != phase:
        raise RuntimeError("Checkpoint phase/version CAS is not exact")
    if payload.get("RouteId") != REVIEW_ROUTE:
        raise RuntimeError("Checkpoint review route is not exact")
    draft = payload.get("Draft")
    if not isinstance(draft, dict):
        raise RuntimeError("Checkpoint draft is missing")
    if draft.get("ExpectedContentRevision") != workspace.content_revision:
        raise RuntimeError("Checkpoint lost workspace content-revision CAS")
    workspace_id = draft.get("WorkspaceId")
    if not isinstance(workspace_id, dict) or workspace_id.get("Value") != workspace.workspace_id:
        raise RuntimeError("Checkpoint belongs to another workspace")
    quote = draft.get("Quote")
    plan = draft.get("Plan")
    action = draft.get("ActionPlan")
    if not all(isinstance(value, dict) for value in (quote, plan, action)):
        raise RuntimeError("Checkpoint lost typed quote, plan, or action")
    identity = quote.get("Identity")
    if not isinstance(identity, dict) or identity.get("SourceSkillId") is not None:
        raise RuntimeError("Custom Knowledge identity did not retain nullable source authority")
    for quote_field, plan_field in (
        ("CharacterRevision", "ExpectedCharacterRevision"),
        ("LogicalRevision", "ExpectedLogicalRevision"),
        ("SourceRevision", "ExpectedSourceRevision"),
        ("RuleDigest", "ExpectedRuleDigest"),
    ):
        value = quote.get(quote_field)
        if not isinstance(value, str) or LOWER_SHA256.fullmatch(value) is None:
            raise RuntimeError(f"Quote {quote_field} is not canonical SHA-256")
        if plan.get(plan_field) != value:
            raise RuntimeError(f"Plan does not CAS-bind {quote_field}")
    if action.get("Kind") != 4 or action.get("DomainIdentity") != (
        "22222222-2222-2222-2222-222222222222:custom"
    ):
        raise RuntimeError("Generic action lost specialized Knowledge identity/kind")
    idempotency_key = payload.get("IdempotencyKey")
    if not isinstance(idempotency_key, str) or LOWER_SHA256.fullmatch(idempotency_key) is None:
        raise RuntimeError("Checkpoint idempotency key is not SHA-256 shaped")
    if action.get("IdempotencyKey") != idempotency_key:
        raise RuntimeError("Action and checkpoint idempotency bindings differ")


def import_fixture(
    device: shared.Device, fixture: Path, fixture_sha256: str
) -> tuple[shared.LaunchState, shared.WorkspaceAuthority]:
    launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    shared.record_phone_ui_locale_evidence(
        device,
        evidence_prefix="sr5-career-knowledge-language",
    )
    device.tap("home-open-file")
    shared.select_android_document(device, fixture.name)
    device.wait("CareerKnowledgeLanguageAdvanceE2E", timeout=120)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    workspace = shared.read_phone_workspace_authority(device)
    shared.require_import_authority(workspace, fixture_sha256)
    return launch, workspace


def prove(device: shared.Device, fixture: Path, fixture_sha256: str) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = import_fixture(device, fixture, fixture_sha256)
    open_choose(device)
    picker = device.wait_for_single_exact_resource_id(
        "sr5-career-knowledge-skill-picker",
        timeout=60,
        evidence_prefix="sr5-career-knowledge-language-picker",
        surface_name="Knowledge/Language picker",
    )
    if "Matrix Security · Academic · 3 → 4 · 4 Karma" not in (
        picker.attributes.get("text") or ""
    ):
        raise RuntimeError("The exact custom Knowledge quote was not rendered")
    device.capture("sr5-career-knowledge-language-choose")
    device.tap_single_exact_resource_id(
        "sr5-career-knowledge-skill-review",
        timeout=60,
        evidence_prefix="sr5-career-knowledge-language-review",
        surface_name="Knowledge/Language review control",
    )
    wait_route(device, REVIEW_ROUTE)
    reviewed = read_checkpoint(device)
    if reviewed is None:
        raise RuntimeError("Reviewed checkpoint disappeared")
    validate_checkpoint(reviewed, phase=0, version=1, workspace=imported)

    reviewed_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(imported, restored)
    if read_checkpoint(device) != reviewed:
        raise RuntimeError("Reviewed checkpoint bytes changed across restart")
    open_choose(device)
    device.tap_bidirectional(
        "sr5-career-knowledge-skill-resume",
        timeout=90,
        backward_scrolls=20,
        forward_scrolls=20,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    wait_route(device, REVIEW_ROUTE)
    device.tap_bidirectional(
        "sr5-career-knowledge-skill-apply",
        timeout=120,
        backward_scrolls=20,
        forward_scrolls=20,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    wait_route(device, RECEIPT_ROUTE, timeout=180)
    applied = read_checkpoint(device)
    if applied is None:
        raise RuntimeError("Applied checkpoint disappeared")
    validate_checkpoint(applied, phase=2, version=3, workspace=imported)
    if read_serialized_preference(device, MUTATION_OWNER_KEY):
        raise RuntimeError("Resolved Knowledge journal retained its shared mutation owner")
    if applied[1].get("IdempotencyKey") != reviewed[1].get("IdempotencyKey"):
        raise RuntimeError("Applied checkpoint changed action idempotency")
    device.capture("sr5-career-knowledge-language-receipt")

    applied_restart = shared.force_stop_and_launch_new_process(
        device, reviewed_restart.restarted
    )
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    open_choose(device)
    wait_route(device, RECEIPT_ROUTE, timeout=180)
    if read_checkpoint(device) != applied:
        raise RuntimeError("Applied checkpoint bytes changed during receipt recovery")
    device.tap_bidirectional(
        "sr5-career-knowledge-skill-receipt-acknowledge",
        timeout=120,
        backward_scrolls=30,
        forward_scrolls=30,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    time.sleep(1)
    if read_checkpoint(device, required=False) is not None:
        raise RuntimeError("Acknowledged checkpoint was not durably removed")
    if read_serialized_preference(device, MUTATION_OWNER_KEY):
        raise RuntimeError("Acknowledgement left a shared mutation owner")

    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    saved = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(saved)
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Knowledge advancement changed workspace identity")
    if saved.content_revision != imported.content_revision + 1:
        raise RuntimeError("Knowledge advancement did not save one successor revision")
    if saved.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("Knowledge advancement did not change the payload")
    return {
        "import": shared.workspace_authority_json(imported),
        "saved": shared.workspace_authority_json(saved),
        "reviewedCheckpointSha256": reviewed[0],
        "appliedCheckpointSha256": applied[0],
        "restartProcessIds": [
            list(reviewed_restart.restarted.process_ids),
            list(applied_restart.restarted.process_ids),
        ],
    }


def execute(args: argparse.Namespace) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{ALLOW_MUTATION_FLAG} is required because this proof installs, clears, imports, and mutates"
        )
    if SAFE_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe grammar")
    apk = args.apk.resolve()
    fixture = args.career_runner.resolve()
    android_root = Path(__file__).resolve().parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    build_provenance = load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    artifact = build_provenance["artifact"]
    if not isinstance(artifact, dict):
        raise RuntimeError("Verified build-provenance artifact is malformed")
    expected_apk_sha256 = str(artifact["sha256"])
    fixture_sha256 = shared.sha256(fixture)
    args.evidence.mkdir(parents=True, exist_ok=True)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    device.require_transport_stability(expected_api_level="36")
    observation = physical_device_observation(device)
    remote_fixture = f"/sdcard/Download/{physical.safe_fixture_basename(fixture)}"
    physical.remove_remote_temporary_file(device, remote_fixture)
    try:
        device.install_verified(
            apk,
            expected_apk_sha256,
            "--no-streaming",
            "-r",
        )
        device.push_verified(fixture, remote_fixture, fixture_sha256)
        journey = prove(device, fixture, fixture_sha256)
    finally:
        physical.remove_remote_temporary_file(device, remote_fixture)
    post_run_provenance = load_and_verify_manifest(
        args.build_provenance_manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    if post_run_provenance != build_provenance:
        raise RuntimeError("Source/APK provenance changed during physical execution")
    return {
        "schema": "chummer.android.sr5-career-knowledge-language-physical-e2e/v1",
        "status": "device-pass-source-bound",
        "executionStatus": "pass",
        "releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested",
        "hostedX86Claim": False,
        "proofClass": "observed-physical-arm64-api36",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "package": shared.PACKAGE,
        "apkSha256": expected_apk_sha256,
        "buildProvenance": build_provenance,
        "sourceGraphRecheckedAfterRun": True,
        "fixtureSha256": fixture_sha256,
        "deviceObservation": observation,
        "adbTransport": device.transport_summary(),
        "authorityProofStages": journey,
    }


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        receipt = execute(args)
    except Exception as error:  # noqa: BLE001 - failed proof must leave a receipt
        receipt = {
            "schema": "chummer.android.sr5-career-knowledge-language-physical-e2e/v1",
            "status": "fail",
            "executionStatus": "fail",
            "releaseEvidenceStatus": "manifest-not-verified-or-journey-failed",
            "buildProvenanceManifest": str(args.build_provenance_manifest),
            "hostedX86Claim": False,
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "failure": {"type": type(error).__name__, "message": str(error)[:4000]},
            "adbTransportFailure": getattr(error, "receipt", None),
        }
        atomic_json(args.receipt.resolve(), receipt)
        print(f"Physical Knowledge/Language proof failed: {error}", file=sys.stderr)
        return 1
    atomic_json(args.receipt.resolve(), receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
