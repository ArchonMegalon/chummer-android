#!/usr/bin/env python3
"""Authenticate or materialize an internal API-36 ARM64 build candidate."""

from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import time


REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT))

from tests.api36_physical_build_provenance import (  # noqa: E402
    authenticate_inputs,
    COMMAND_JOURNAL_CONTRACT,
    create_manifest,
    ENVIRONMENT_ALLOWLIST,
    PHASE_NAMES,
    reject_duplicate_keys,
    write_manifest,
)


def add_common(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--android-root", type=Path, required=True)
    parser.add_argument("--presentation-root", type=Path, required=True)
    parser.add_argument("--core-content-root", type=Path, required=True)
    parser.add_argument("--w5-receipt", type=Path, required=True)
    parser.add_argument("--w5-evidence-directory", type=Path, required=True)
    parser.add_argument("--source-graph", type=Path, required=True)
    parser.add_argument("--package-authority", type=Path, required=True)
    parser.add_argument("--release-package-authority-v2", type=Path, required=True)
    parser.add_argument("--content-source-receipt", type=Path, required=True)
    parser.add_argument("--full-project-lock", type=Path, required=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    check = subparsers.add_parser("check-inputs")
    add_common(check)
    bounded = subparsers.add_parser("run-bounded")
    bounded.add_argument("--journal", type=Path, required=True)
    bounded.add_argument("--raw-journal", type=Path, required=True)
    bounded.add_argument("--output", type=Path, required=True)
    bounded.add_argument("--phase", required=True)
    bounded.add_argument("--timeout-seconds", type=float, required=True)
    bounded.add_argument("--deadline-epoch", type=float, required=True)
    bounded.add_argument("--working-directory", type=Path, required=True)
    bounded.add_argument("--environment", action="append", default=[])
    bounded.add_argument("argv", nargs=argparse.REMAINDER)
    workloads = subparsers.add_parser("capture-workloads")
    workloads.add_argument("--dotnet", type=Path, required=True)
    workloads.add_argument("--output", type=Path, required=True)
    materialize = subparsers.add_parser("materialize")
    add_common(materialize)
    materialize.add_argument("--apk", type=Path, required=True)
    materialize.add_argument("--content-apk-receipt", type=Path, required=True)
    materialize.add_argument("--assets", type=Path, required=True)
    materialize.add_argument("--toolchain-log", type=Path, required=True)
    materialize.add_argument("--source-graph-log", type=Path, required=True)
    materialize.add_argument("--content-source-log", type=Path, required=True)
    materialize.add_argument("--build-inputs-log", type=Path, required=True)
    materialize.add_argument("--restore-log", type=Path, required=True)
    materialize.add_argument("--build-log", type=Path, required=True)
    materialize.add_argument("--content-apk-log", type=Path, required=True)
    materialize.add_argument("--source-graph-seal-log", type=Path, required=True)
    materialize.add_argument("--command-journal", type=Path, required=True)
    materialize.add_argument("--raw-command-journal", type=Path, required=True)
    materialize.add_argument("--android-sdk-packages", type=Path, required=True)
    materialize.add_argument("--dotnet-workloads", type=Path, required=True)
    materialize.add_argument("--java-path", type=Path, required=True)
    materialize.add_argument("--javac-path", type=Path, required=True)
    materialize.add_argument("--dotnet-path", type=Path, required=True)
    materialize.add_argument("--python-path", type=Path, required=True)
    materialize.add_argument("--release-workspace-root", type=Path, required=True)
    materialize.add_argument("--package-feed", type=Path, required=True)
    materialize.add_argument("--offline-feed", type=Path, required=True)
    materialize.add_argument("--nuget-packages", type=Path, required=True)
    materialize.add_argument("--android-build-tools-version", required=True)
    materialize.add_argument("--dotnet-version", required=True)
    materialize.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "capture-workloads":
        if (
            not args.dotnet.is_absolute() or args.dotnet.is_symlink()
            or args.dotnet.resolve(strict=True) != args.dotnet
            or not os.access(args.dotnet, os.X_OK)
            or not args.output.is_absolute() or args.output.exists() or args.output.is_symlink()
            or not args.output.parent.is_dir() or args.output.parent.is_symlink()
            or args.output.parent.resolve(strict=True) != args.output.parent
        ):
            raise ValueError("workload capture paths are not exact regular/exclusive paths")
        completed = subprocess.run(
            [os.fspath(args.dotnet), "workload", "list", "--machine-readable"],
            check=False, capture_output=True, text=True, timeout=60,
        )
        lines = [line for line in completed.stdout.splitlines() if line.strip()]
        if completed.returncode != 0 or not lines:
            raise ValueError("dotnet workload inventory command failed")
        payload = json.loads(lines[-1], object_pairs_hook=reject_duplicate_keys)
        encoded = (json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n").encode()
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
        descriptor = os.open(args.output, flags, 0o600)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        print("dotnet_workload_inventory=pass")
        return 0
    if args.command == "run-bounded":
        argv = args.argv[1:] if args.argv and args.argv[0] == "--" else args.argv
        environment: dict[str, str] = {}
        for value in args.environment:
            key, separator, item = value.partition("=")
            if not separator or key in environment:
                parser.error("--environment entries must be unique KEY=VALUE pairs")
            environment[key] = item
        if set(environment) != ENVIRONMENT_ALLOWLIST or not all(environment.values()):
            parser.error("bounded environment does not match the exact allowlist")
        if not argv:
            parser.error("run-bounded requires one command after --")
        if (
            args.phase not in PHASE_NAMES or not math.isfinite(args.timeout_seconds)
            or not math.isfinite(args.deadline_epoch) or args.timeout_seconds <= 0
            or args.deadline_epoch <= time.time()
        ):
            parser.error("run-bounded phase/timeout is outside the exact contract")
        if (
            not args.working_directory.is_absolute() or args.working_directory.is_symlink()
            or args.working_directory.resolve(strict=True) != args.working_directory
            or not args.journal.is_absolute() or args.journal.is_symlink()
            or not args.raw_journal.is_absolute() or args.raw_journal.is_symlink()
            or not args.output.is_absolute() or args.output.is_symlink() or args.output.exists()
        ):
            parser.error("run-bounded paths must be absolute canonical non-symlinks")
        for path in (args.journal, args.raw_journal, args.output):
            if not path.parent.is_dir() or path.parent.is_symlink() or path.parent.resolve(strict=True) != path.parent:
                parser.error("run-bounded output parents must be real canonical directories")
        if args.journal.exists() and (
            not args.journal.is_file() or args.journal.stat().st_mode & 0o077
        ):
            parser.error("canonical journal must remain an owner-only regular file")
        started_epoch = time.time()
        runner = REPO_ROOT / "scripts/run_internal_phone_beta_bounded.py"
        command = [
            sys.executable, os.fspath(runner), "--journal", os.fspath(args.raw_journal),
            "--output", os.fspath(args.output), "--phase", args.phase,
            "--timeout-seconds", str(args.timeout_seconds),
            "--deadline-epoch", str(args.deadline_epoch), "--", *argv,
        ]
        completed = subprocess.run(
            command, check=False, cwd=args.working_directory, env=environment,
        )
        raw_lines = args.raw_journal.read_bytes().splitlines()
        if len(raw_lines) < 2:
            raise ValueError("bounded runner did not emit a complete command pair")
        raw_rows = [
            json.loads(line.decode("utf-8"), object_pairs_hook=reject_duplicate_keys)
            for line in raw_lines[-2:]
        ]
        raw_started, raw_finished = raw_rows
        common = {
            "contractName": COMMAND_JOURNAL_CONTRACT, "phase": args.phase,
            "argv": argv, "workingDirectory": os.fspath(args.working_directory),
            "environment": environment,
            "timeoutSeconds": raw_started["timeoutSeconds"],
            "deadlineEpoch": args.deadline_epoch, "startedEpoch": started_epoch,
            "outputPath": os.fspath(args.output), "processGroupTermination": True,
            "publicationAuthorized": False,
        }
        rows = [
            {**common, "event": "started"},
            {
                **common, "event": "finished",
                "elapsedSeconds": raw_finished["elapsedSeconds"],
                "exitCode": raw_finished["exitCode"],
                "timedOut": raw_finished["timedOut"],
                "outputSha256": raw_finished["outputSha256"],
                "termination": raw_finished["termination"],
            },
        ]
        flags = os.O_WRONLY | os.O_CREAT | os.O_APPEND | getattr(os, "O_NOFOLLOW", 0)
        descriptor = os.open(args.journal, flags, 0o600)
        with os.fdopen(descriptor, "a", encoding="utf-8") as stream:
            for row in rows:
                stream.write(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
            stream.flush()
            os.fsync(stream.fileno())
        return completed.returncode
    common = {
        "android_root": args.android_root,
        "presentation_root": args.presentation_root,
        "core_content_root": args.core_content_root,
        "w5_receipt_path": args.w5_receipt,
        "w5_evidence_directory": args.w5_evidence_directory,
        "source_graph_path": args.source_graph,
        "package_authority_path": args.package_authority,
        "release_package_authority_v2_path": args.release_package_authority_v2,
        "content_source_receipt_path": args.content_source_receipt,
        "full_project_lock_path": args.full_project_lock,
    }
    if args.command == "check-inputs":
        authenticate_inputs(**common)
        print("api36_physical_build_inputs=pass publication_authorized=false")
        return 0
    manifest = create_manifest(
        **common,
        apk=args.apk,
        content_apk_receipt_path=args.content_apk_receipt,
        assets_path=args.assets,
        toolchain_log_path=args.toolchain_log,
        source_graph_log_path=args.source_graph_log,
        content_source_log_path=args.content_source_log,
        build_inputs_log_path=args.build_inputs_log,
        restore_log_path=args.restore_log,
        build_log_path=args.build_log,
        content_apk_log_path=args.content_apk_log,
        source_graph_seal_log_path=args.source_graph_seal_log,
        command_journal_path=args.command_journal,
        raw_command_journal_path=args.raw_command_journal,
        android_sdk_packages_path=args.android_sdk_packages,
        dotnet_workloads_path=args.dotnet_workloads,
        java_path=args.java_path, javac_path=args.javac_path,
        dotnet_path=args.dotnet_path, python_path=args.python_path,
        release_workspace_root=args.release_workspace_root,
        package_feed_path=args.package_feed, offline_feed_path=args.offline_feed,
        nuget_packages_path=args.nuget_packages,
        android_build_tools_version=args.android_build_tools_version,
        dotnet_version=args.dotnet_version,
    )
    write_manifest(args.output, manifest)
    print(
        f"api36_physical_build_provenance=pass publication_authorized=false "
        f"output={args.output} authority_sha256={manifest['authoritySha256']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
