#!/usr/bin/env python3
"""Materialize fail-closed API-36 build or journey environment receipts."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
from pathlib import Path

from api36_proof_environment_authority import (
    DEFAULT_POLICY,
    StableFile,
    base_receipt,
    build_subject,
    collect_environment,
    journey_subject,
    load_policy,
    write_atomically,
)
from api36_wizard_gate_contract import (
    DEFAULT_CONTRACT as DEFAULT_GATE_CONTRACT,
    contract_binding,
)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="role", required=True)

    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--android-sdk-root", type=Path, required=True)
    common.add_argument("--gate-contract", type=Path, default=DEFAULT_GATE_CONTRACT)
    common.add_argument("--policy", type=Path, default=DEFAULT_POLICY)
    common.add_argument("--output", type=Path, required=True)

    journey = subparsers.add_parser("journey", parents=[common])
    journey.add_argument("--apk", type=Path, required=True)
    journey.add_argument("--expected-apk-sha256", required=True)
    journey.add_argument("--journey-receipt", type=Path, required=True)
    journey.add_argument("--matrix-journey", required=True)

    build = subparsers.add_parser("build", parents=[common])
    build.add_argument("--x64-apk", type=Path, required=True)
    build.add_argument("--arm64-apk", type=Path, required=True)
    build.add_argument("--hosted-candidate", type=Path, required=True)
    build.add_argument("--workflow", type=Path, required=True)

    args = parser.parse_args(argv)
    policy_snapshot = StableFile(args.policy, "proof environment policy")
    gate_snapshot = StableFile(args.gate_contract, "wizard gate contract")
    policy = load_policy(policy_snapshot)
    gate_authority = contract_binding(gate_snapshot.path)
    observation = collect_environment(args.android_sdk_root.absolute(), os.environ)

    snapshots: list[StableFile] = [policy_snapshot, gate_snapshot]
    if args.role == "journey":
        journey_snapshot = StableFile(args.journey_receipt, "finalized journey receipt")
        apk_snapshot = StableFile(args.apk, "x64 journey APK")
        snapshots.extend((journey_snapshot, apk_snapshot))
        subject = journey_subject(
            journey_snapshot=journey_snapshot,
            matrix_journey=args.matrix_journey,
            apk_snapshot=apk_snapshot,
            expected_apk_sha256=args.expected_apk_sha256,
            gate_authority=gate_authority,
        )
    else:
        x64_apk = StableFile(args.x64_apk, "x64 APK")
        arm64_apk = StableFile(args.arm64_apk, "ARM64 APK")
        hosted_candidate = StableFile(args.hosted_candidate, "hosted ARM64 candidate")
        workflow = StableFile(args.workflow, "API-36 workflow")
        snapshots.extend((x64_apk, arm64_apk, hosted_candidate, workflow))
        subject = build_subject(
            x64_apk=x64_apk,
            arm64_apk=arm64_apk,
            hosted_candidate=hosted_candidate,
            workflow=workflow,
        )

    receipt = base_receipt(
        role=args.role,
        policy=policy,
        policy_snapshot=policy_snapshot,
        gate_authority=gate_authority,
        subject_authority=subject,
        observation=observation,
    )
    for snapshot in snapshots:
        snapshot.recheck()
    write_atomically(args.output.absolute(), receipt)
    print(
        f"api36_proof_environment_receipt=pass role={args.role} "
        f"receipt_sha256={receipt['receiptSha256']} publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, json.JSONDecodeError, subprocess.SubprocessError) as error:
        print(f"API-36 proof environment receipt failed: {error}")
        raise SystemExit(1) from error

