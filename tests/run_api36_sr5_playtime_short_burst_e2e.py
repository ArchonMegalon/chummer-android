#!/usr/bin/env python3
"""Prove one exact SR5 Playtime Short Burst on a hosted API 36 phone.

This journey deliberately reuses the existing typed Playtime transaction and
its fail-closed XML authority.  It proves only one direct-weapon Short Burst;
it does not authorize Full Editing, tablet readiness, or publication.
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
import time
import xml.etree.ElementTree as ET

import api36_proof_state as proof_state
import run_api36_editing_e2e as shared
import run_api36_sr5_before_run_edge_physical_e2e as lane
import run_api36_sr5_playtime_weapon_physical_e2e as playtime


RECEIPT_SCHEMA = "chummer.android.editing-e2e/v1"
JOURNEY = "playtime-short-burst"
CONTROL = "Sr5TableWizard.Playtime.ShortBurst"
PROOF_STAGES = (
    "importExactCareerFixture",
    "persistDurableReview",
    "restartAndResumeReview",
    "applyRepresentativeTypedActionOnce",
    "verifySavedRevisionPlusOne",
    "restartAndRecoverExactReceipt",
    "acknowledgeReceipt",
    "restartAndReopenSavedSuccessor",
)
SPEC = replace(
    playtime.SPEC,
    receipt_schema=RECEIPT_SCHEMA,
    journey=JOURNEY,
    excluded_scope=playtime.SPEC.excluded_scope + ("Full Editing",),
)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--career-runner", type=Path, default=SPEC.fixture)
    return parser.parse_args(argv)


def source_paths(workspace_root: Path) -> dict[str, Path]:
    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    presentation_root = workspace_root / "chummer-presentation"
    core_root = workspace_root / "chummer-core-engine"
    return {
        "driverSha256": driver,
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "typedLaneAuthorityHelperSha256": Path(lane.__file__).resolve(),
        "playtimeShortBurstAuthorityHelperSha256": Path(playtime.__file__).resolve(),
        "fixtureSha256": SPEC.fixture,
        "phoneShellPagesSha256": android_root
        / "src/Chummer.Android/Native/PhoneShellPages.cs",
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
        "careerWeaponRequestSha256": presentation_root
        / "Chummer.Presentation/Overview/CareerWeaponFireRequest.cs",
        "tableWizardSessionSha256": presentation_root
        / "Chummer.Presentation/Overview/Sr5TableWizardSession.cs",
        "presenterMutationSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "careerWeaponRulesSha256": core_root
        / "Chummer.Contracts/Characters/CharacterWeaponFireRules.cs",
        "workspaceStoreSha256": core_root
        / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }


def execute(args: argparse.Namespace) -> dict[str, object]:
    if lane.physical.SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the safe ASCII grammar")
    fixture = args.career_runner.resolve()
    playtime.require_playtime_fixture(ET.parse(fixture).getroot())
    fixture_sha256 = shared.sha256(fixture)

    paths = source_paths(args.workspace_root.resolve())
    # The caller may explicitly select a byte-identical fixture elsewhere.
    paths["fixtureSha256"] = fixture
    missing = [str(path) for path in paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Playtime Short Burst source graph is incomplete: {missing!r}")
    source_hashes = {key: shared.sha256(path) for key, path in paths.items()}

    apk = args.apk.resolve()
    apk_sha256 = shared.sha256(apk)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Playtime Short Burst E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(
            "Playtime Short Burst E2E requires the hosted x86_64 phone lane, "
            f"got {abi!r}"
        )

    device.require_shared_storage_readiness(
        deadline=time.monotonic()
        + shared.ADB_SHARED_STORAGE_PREFLIGHT_MAX_SECONDS
    )
    device.install_verified(apk, apk_sha256, "--no-streaming", "-r")
    provider_registration = device.publish_document_for_documents_ui(
        fixture,
        fixture_sha256,
    )
    proof_build_id = (
        f"hosted-{os.environ['GITHUB_RUN_ID']}-"
        f"{os.environ['CHUMMER_E2E_APK_ARTIFACT_ATTEMPT']}"
    )
    proof_expectation = proof_state.expected_build(
        Path(__file__).resolve().parents[1],
        Path(__file__).resolve().parents[1]
        / "eng/api36-sr5-wizard-gate-authority.json",
        proof_build_id,
    )
    proof = lane.prove_lane(
        device,
        SPEC,
        fixture,
        fixture_sha256,
        assert_before=playtime.assert_before_state,
        assert_after=playtime.assert_after_state,
        proof_expectation=proof_expectation,
    )
    if {key: shared.sha256(path) for key, path in paths.items()} != source_hashes:
        raise RuntimeError("Playtime Short Burst source authority changed during execution")
    if shared.sha256(apk) != apk_sha256:
        raise RuntimeError("Playtime Short Burst APK authority changed during execution")

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
        "apk": str(apk),
        "apkSha256": apk_sha256,
        "sourceFileSha256": source_hashes,
        "sourceGraphRecheckedAfterRun": True,
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": provider_registration["sha256"],
        "documentsUiProviderRegistration": provider_registration,
        "publicationAuthorized": False,
        "controlCount": 1,
        "controls": {CONTROL: "pass"},
        "authorityProofStages": proof,
        "scope": proof["scope"],
        "doesNotAssert": ["Full Editing", "tablet readiness", "publication authority"],
        "journeys": {stage: "pass" for stage in PROOF_STAGES},
    }


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    receipt = execute(args)
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (
        OSError,
        RuntimeError,
        subprocess.CalledProcessError,
        subprocess.TimeoutExpired,
    ) as error:
        print(f"Playtime Short Burst E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
