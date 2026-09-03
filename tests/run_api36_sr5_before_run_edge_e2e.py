#!/usr/bin/env python3
"""Prove one exact SR5 Before Run Edge action on a hosted API 36 phone.

This hosted x86_64 lane reuses the same typed transaction proof as the physical
phone journey.  It proves only one Edge-use delta through review, confirmation,
receipt recovery, acknowledgement, and process restart.  It does not authorize
Full Editing, tablet readiness, publication, or any other table action.
"""

from __future__ import annotations

import argparse
from dataclasses import replace
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import api36_proof_state as proof_state
import run_api36_editing_e2e as shared
import run_api36_sr5_before_run_edge_physical_e2e as lane


RECEIPT_SCHEMA = "chummer.android.sr5-before-run-edge-e2e/v1"
JOURNEY = "before-run-edge"
HOSTED_SPEC = replace(
    lane.SPEC,
    receipt_schema=RECEIPT_SCHEMA,
    journey=JOURNEY,
    excluded_scope=(*lane.SPEC.excluded_scope, "full editing", "publication"),
)


def require_hosted_device(device: shared.Device) -> dict[str, object]:
    """Require the disposable hosted API-36 x86_64 emulator lane exactly."""
    api_level = device.shell("getprop", "ro.build.version.sdk")
    if api_level != "36":
        raise RuntimeError(f"Before Run hosted E2E requires API 36, got {api_level!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(
            "Before Run hosted E2E requires the x86_64 phone lane, "
            f"got {abi!r}"
        )
    emulator = device.shell("getprop", "ro.kernel.qemu")
    if emulator != "1":
        raise RuntimeError(
            "Before Run hosted E2E requires the disposable emulator authority"
        )
    return {
        "apiLevel": int(api_level),
        "abi": abi,
        "emulator": True,
    }


def source_paths(
    *,
    driver: Path,
    android_root: Path,
    workspace_root: Path,
    fixture: Path,
) -> dict[str, Path]:
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    return {
        "driverSha256": driver,
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "sharedBeforeRunLaneDriverSha256": Path(lane.__file__).resolve(),
        "careerWizardPageSha256": android_root
        / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs",
        "tableWizardPageSha256": android_root
        / "src/Chummer.Android/Native/Sr5TableWizardPage.cs",
        "tableWizardModelSha256": android_root
        / "src/Chummer.Android/Native/Sr5TableWizardPhoneModel.cs",
        "tableWizardTransactionSha256": android_root
        / "src/Chummer.Android/Native/Sr5TableWizardTypedTransaction.cs",
        "tableWizardAuthoritySha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionSr5TableWizardPhoneAuthority.cs",
        "runnerCoordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "api36ProofStateSha256": android_root
        / "src/Chummer.Android/Proof/Api36ProofState.cs",
        "api36ProofPublisherSha256": android_root
        / "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs",
        "api36ProofReaderSha256": android_root / "tests/api36_proof_state.py",
        "workspaceStoreSha256": core_root
        / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "fixtureSha256": fixture,
        **lane.before_run_source_paths(core_root, presentation_root),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--career-runner", type=Path, default=HOSTED_SPEC.fixture)
    args = parser.parse_args(argv)

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    apk = args.apk.resolve()
    fixture = args.career_runner.resolve()
    bound_sources = source_paths(
        driver=driver,
        android_root=android_root,
        workspace_root=workspace_root,
        fixture=fixture,
    )
    missing = [str(path) for path in bound_sources.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Before Run hosted source graph is incomplete: {missing!r}")

    lane.require_before_run_fixture(ET.parse(fixture).getroot())
    fixture_sha256 = shared.sha256(fixture)
    apk_sha256 = shared.sha256(apk)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    device.require_transport_stability(expected_api_level="36")
    observation = require_hosted_device(device)
    device.install_verified(apk, apk_sha256, "--no-streaming", "-r")
    remote_fixture_path = f"/sdcard/Download/{fixture.name}"
    verified_remote_fixture_sha256 = device.push_verified(
        fixture,
        remote_fixture_path,
        fixture_sha256,
    )
    provider_index = device.index_download_for_documents_ui(
        remote_fixture_path,
        fixture_sha256,
        fixture.stat().st_size,
    )
    proof_build_id = (
        f"hosted-{os.environ['GITHUB_RUN_ID']}-"
        f"{os.environ['CHUMMER_E2E_APK_ARTIFACT_ATTEMPT']}"
    )
    proof_expectation = proof_state.expected_build(
        android_root,
        android_root / "eng/api36-sr5-wizard-gate-authority.json",
        proof_build_id,
    )
    authority = lane.prove_lane(
        device,
        HOSTED_SPEC,
        fixture,
        fixture_sha256,
        assert_before=lane.assert_before_state,
        assert_after=lane.assert_after_state,
        proof_expectation=proof_expectation,
    )
    receipt = {
        "schema": RECEIPT_SCHEMA,
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": JOURNEY,
        **observation,
        "package": shared.PACKAGE,
        "apk": str(apk),
        "apkSha256": apk_sha256,
        **{key: shared.sha256(path) for key, path in bound_sources.items()},
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote_fixture_sha256,
        "documentsUiProviderIndex": provider_index,
        "authorityProofStages": authority,
        "scope": authority["scope"],
        "publicationAuthorized": False,
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
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Before Run hosted E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
