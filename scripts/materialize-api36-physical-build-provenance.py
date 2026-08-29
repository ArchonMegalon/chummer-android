#!/usr/bin/env python3
"""Authenticate or materialize an internal API-36 ARM64 build candidate."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time


REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT))

from tests.api36_physical_build_provenance import (  # noqa: E402
    authenticate_inputs,
    COMMAND_JOURNAL_CONTRACT,
    create_manifest,
    DELEGATE_COMMAND_JOURNAL_CONTRACT,
    ENVIRONMENT_ALLOWLIST,
    PER_PHASE_TIMEOUT_SECONDS,
    PHASE_NAMES,
    RAW_COMMAND_JOURNAL_CONTRACT,
    reject_duplicate_keys,
    SnapshotRegistry,
    TOTAL_DEADLINE_SECONDS,
    validate_toolchain,
    write_manifest,
)


def write_exclusive(path: Path, data: bytes, label: str) -> None:
    if (
        not path.is_absolute() or path.exists() or path.is_symlink()
        or not path.parent.is_dir() or path.parent.is_symlink()
        or path.parent.resolve(strict=True) != path.parent
    ):
        raise ValueError(f"{label} output path is not exclusive/canonical")
    descriptor = os.open(
        path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0), 0o600,
    )
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())


def append_rows(path: Path, rows: list[dict[str, object]], label: str) -> None:
    if (
        not path.is_absolute() or path.is_symlink()
        or not path.parent.is_dir() or path.parent.is_symlink()
        or path.parent.resolve(strict=True) != path.parent
        or (path.exists() and (not path.is_file() or path.stat().st_mode & 0o077))
    ):
        raise ValueError(f"{label} path is not an owner-only canonical regular file")
    descriptor = os.open(
        path, os.O_WRONLY | os.O_CREAT | os.O_APPEND | getattr(os, "O_NOFOLLOW", 0), 0o600,
    )
    with os.fdopen(descriptor, "a", encoding="utf-8") as stream:
        for row in rows:
            stream.write(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
        stream.flush()
        os.fsync(stream.fileno())


def load_journal_pair(path: Path, label: str) -> tuple[dict[str, object], dict[str, object]]:
    lines = path.read_bytes().splitlines()
    if len(lines) < 2:
        raise ValueError(f"{label} did not emit a complete command pair")
    rows: list[dict[str, object]] = []
    for line in lines[-2:]:
        value = json.loads(line.decode("utf-8"), object_pairs_hook=reject_duplicate_keys)
        if not isinstance(value, dict):
            raise ValueError(f"{label} row must be an object")
        rows.append(value)
    return rows[0], rows[1]


def stable_regular_bytes(path: Path, label: str) -> tuple[bytes, tuple[int, ...]]:
    if not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path:
        raise ValueError(f"{label} path is not canonical/non-symlinked")
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError(f"{label} must be a regular file")
        chunks: list[bytes] = []
        while chunk := os.read(descriptor, 1024 * 1024):
            chunks.append(chunk)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    identity = (
        before.st_dev, before.st_ino, before.st_mode, before.st_size,
        before.st_mtime_ns, before.st_ctime_ns,
    )
    if identity != (
        after.st_dev, after.st_ino, after.st_mode, after.st_size,
        after.st_mtime_ns, after.st_ctime_ns,
    ):
        raise ValueError(f"{label} changed during stable capture")
    data = b"".join(chunks)
    if len(data) != before.st_size:
        raise ValueError(f"{label} size changed during stable capture")
    return data, identity


def add_common(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--android-root", type=Path, required=True)
    parser.add_argument("--presentation-root", type=Path, required=True)
    parser.add_argument("--core-content-root", type=Path, required=True)
    parser.add_argument("--ui-authority-receipt", type=Path, required=True)
    parser.add_argument("--package-feed", type=Path, required=True)
    parser.add_argument("--package-authority", type=Path, required=True)
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
    bounded.add_argument("--delegate-journal", type=Path, required=True)
    bounded.add_argument("--output", type=Path, required=True)
    bounded.add_argument("--phase", required=True)
    bounded.add_argument("--timeout-seconds", type=float, required=True)
    bounded.add_argument("--deadline-epoch", type=float, required=True)
    bounded.add_argument("--invocation-started-epoch", type=float, required=True)
    bounded.add_argument("--working-directory", type=Path, required=True)
    bounded.add_argument("--environment", action="append", default=[])
    bounded.add_argument("argv", nargs=argparse.REMAINDER)
    workloads = subparsers.add_parser("capture-workloads")
    workloads.add_argument("--dotnet", type=Path, required=True)
    workloads.add_argument("--android-workload-manifest", type=Path, required=True)
    workloads.add_argument("--maui-workload-manifest", type=Path, required=True)
    workloads.add_argument("--android-sdk-packages", type=Path, required=True)
    workloads.add_argument("--android-sdk-root", type=Path, required=True)
    workloads.add_argument("--java", type=Path, required=True)
    workloads.add_argument("--javac", type=Path, required=True)
    workloads.add_argument("--jarsigner", type=Path, required=True)
    workloads.add_argument("--apksigner", type=Path, required=True)
    workloads.add_argument("--output", type=Path, required=True)
    signing = subparsers.add_parser("verify-apk-signing")
    signing.add_argument("--apk", type=Path, required=True)
    signing.add_argument("--apksigner", type=Path, required=True)
    signing.add_argument("--jarsigner", type=Path, required=True)
    signing.add_argument("--receipt", type=Path, required=True)
    signing.add_argument("--apksigner-log", type=Path, required=True)
    signing.add_argument("--jarsigner-log", type=Path, required=True)
    materialize = subparsers.add_parser("materialize")
    add_common(materialize)
    materialize.add_argument("--apk", type=Path, required=True)
    materialize.add_argument("--content-apk-receipt", type=Path, required=True)
    materialize.add_argument("--assets", type=Path, required=True)
    materialize.add_argument("--toolchain-log", type=Path, required=True)
    materialize.add_argument("--package-authority-log", type=Path, required=True)
    materialize.add_argument("--package-authority-binding", type=Path, required=True)
    materialize.add_argument("--content-source-log", type=Path, required=True)
    materialize.add_argument("--build-inputs-log", type=Path, required=True)
    materialize.add_argument("--restore-log", type=Path, required=True)
    materialize.add_argument("--build-log", type=Path, required=True)
    materialize.add_argument("--signing-phase-log", type=Path, required=True)
    materialize.add_argument("--apksigner-log", type=Path, required=True)
    materialize.add_argument("--jarsigner-log", type=Path, required=True)
    materialize.add_argument("--signing-receipt", type=Path, required=True)
    materialize.add_argument("--content-apk-log", type=Path, required=True)
    materialize.add_argument("--package-authority-seal-log", type=Path, required=True)
    materialize.add_argument("--package-authority-seal", type=Path, required=True)
    materialize.add_argument("--command-journal", type=Path, required=True)
    materialize.add_argument("--raw-command-journal", type=Path, required=True)
    materialize.add_argument("--delegate-command-journal", type=Path, required=True)
    materialize.add_argument("--android-sdk-packages", type=Path, required=True)
    materialize.add_argument("--android-sdk-root", type=Path, required=True)
    materialize.add_argument("--android-workload-manifest", type=Path, required=True)
    materialize.add_argument("--maui-workload-manifest", type=Path, required=True)
    materialize.add_argument("--dotnet-workloads", type=Path, required=True)
    materialize.add_argument("--java-path", type=Path, required=True)
    materialize.add_argument("--javac-path", type=Path, required=True)
    materialize.add_argument("--jarsigner-path", type=Path, required=True)
    materialize.add_argument("--apksigner-path", type=Path, required=True)
    materialize.add_argument("--dotnet-path", type=Path, required=True)
    materialize.add_argument("--python-path", type=Path, required=True)
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
        for manifest_path, label in (
            (args.android_workload_manifest, "Android workload manifest"),
            (args.maui_workload_manifest, "MAUI workload manifest"),
        ):
            if (
                not manifest_path.is_absolute() or manifest_path.is_symlink()
                or manifest_path.resolve(strict=True) != manifest_path or not manifest_path.is_file()
            ):
                raise ValueError(f"{label} path is not exact")
        completed = subprocess.run(
            [os.fspath(args.dotnet), "workload", "list", "--machine-readable"],
            check=False, capture_output=True, text=True, timeout=60,
        )
        lines = [line for line in completed.stdout.splitlines() if line.strip()]
        if completed.returncode != 0 or not lines:
            raise ValueError("dotnet workload inventory command failed")
        machine = json.loads(lines[-1], object_pairs_hook=reject_duplicate_keys)
        if machine != {"installed": ["maui-android"], "updateAvailable": []}:
            raise ValueError("dotnet machine-readable workload set is not exact")
        human = subprocess.run(
            [os.fspath(args.dotnet), "workload", "list"], check=False,
            capture_output=True, text=True, timeout=60,
        )
        runtimes = subprocess.run(
            [os.fspath(args.dotnet), "--list-runtimes"], check=False,
            capture_output=True, text=True, timeout=60,
        )
        if human.returncode != 0 or runtimes.returncode != 0:
            raise ValueError("dotnet workload/runtime identity command failed")
        if (
            "Workload version: 10.0.110.1" not in human.stdout
            or re.search(r"(?m)^maui-android\s+10\.0\.20/10\.0\.100(?:\s|$)", human.stdout) is None
            or re.search(r"(?m)^Microsoft\.NETCore\.App 10\.0\.11 \[", runtimes.stdout) is None
        ):
            raise ValueError("dotnet workload/runtime identities are not exact")
        payload = {
            **machine, "workloadSetVersion": "10.0.110.1",
            "manifestVersions": {
                "maui-android": "10.0.20/10.0.100",
                "microsoft.net.sdk.android": "36.1.69",
            },
            "runtimeVersion": "10.0.11",
        }
        encoded = (json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n").encode()
        write_exclusive(args.output, encoded, ".NET workload inventory")
        validate_toolchain(
            java_path=args.java, javac_path=args.javac, jarsigner_path=args.jarsigner,
            apksigner_path=args.apksigner, dotnet_path=args.dotnet,
            dotnet_workloads_path=args.output,
            android_sdk_packages_path=args.android_sdk_packages,
            android_sdk_root=args.android_sdk_root,
            android_workload_manifest_path=args.android_workload_manifest,
            maui_workload_manifest_path=args.maui_workload_manifest,
            android_build_tools_version="36.0.0", dotnet_version="10.0.111",
            snapshots=SnapshotRegistry(),
        )
        print("dotnet_workload_inventory=pass")
        return 0
    if args.command == "verify-apk-signing":
        for path, label in (
            (args.apk, "APK"), (args.apksigner, "apksigner"), (args.jarsigner, "jarsigner"),
        ):
            if (
                not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path
                or not path.is_file()
            ):
                raise ValueError(f"{label} is not an exact regular file")
        authenticated = {
            path: stable_regular_bytes(path, label)
            for path, label in (
                (args.apk, "APK"), (args.apksigner, "apksigner"),
                (args.jarsigner, "jarsigner"),
            )
        }
        apksigner = subprocess.run(
            [os.fspath(args.apksigner), "verify", "--verbose", "--print-certs", "--Werr", os.fspath(args.apk)],
            check=False, capture_output=True, timeout=300,
        )
        write_exclusive(args.apksigner_log, apksigner.stdout + apksigner.stderr, "apksigner log")
        if apksigner.returncode != 0:
            raise ValueError("apksigner structural verification failed")
        jarsigner = subprocess.run(
            [os.fspath(args.jarsigner), "-verify", "-certs", os.fspath(args.apk)],
            check=False, capture_output=True, timeout=300,
        )
        write_exclusive(args.jarsigner_log, jarsigner.stdout + jarsigner.stderr, "jarsigner log")
        if jarsigner.returncode != 0:
            raise ValueError("jarsigner structural verification failed")
        try:
            apksigner_text = (apksigner.stdout + apksigner.stderr).decode("utf-8")
            jarsigner_text = (jarsigner.stdout + jarsigner.stderr).decode("utf-8")
        except UnicodeError as error:
            raise ValueError("APK signing verification output is not UTF-8") from error
        certs = re.findall(r"Signer #1 certificate SHA-256 digest:\s*([0-9A-Fa-f]{64})", apksigner_text)
        schemes = sorted({
            int(match.group(1)) for match in re.finditer(
                r"Verified using v([1-4]) scheme(?: \([^)]*\))?: true", apksigner_text,
            )
        })
        if len(certs) != 1 or not set(schemes).intersection({2, 3, 4}) or "jar verified" not in jarsigner_text.lower():
            raise ValueError("APK does not carry one structurally verified modern signing identity")
        for path, (original_bytes, original_identity) in authenticated.items():
            current_bytes, current_identity = stable_regular_bytes(path, path.name)
            if current_bytes != original_bytes or current_identity != original_identity:
                raise ValueError("APK signing input changed before receipt seal")
        receipt = {
            "contractName": "chummer.android.apk-signing-verification/v1", "status": "pass",
            "apkSha256": hashlib.sha256(authenticated[args.apk][0]).hexdigest(),
            "certificateSha256": certs[0].lower(), "verifiedSchemes": schemes,
            "apksignerSha256": hashlib.sha256(authenticated[args.apksigner][0]).hexdigest(),
            "jarsignerSha256": hashlib.sha256(authenticated[args.jarsigner][0]).hexdigest(),
            "apksignerOutputSha256": hashlib.sha256((apksigner.stdout + apksigner.stderr)).hexdigest(),
            "jarsignerOutputSha256": hashlib.sha256((jarsigner.stdout + jarsigner.stderr)).hexdigest(),
            "warningsAsErrors": True, "publicationAuthorized": False,
        }
        write_exclusive(
            args.receipt, (json.dumps(receipt, sort_keys=True, separators=(",", ":")) + "\n").encode(),
            "APK signing receipt",
        )
        print("apk_signing_verification=pass publication_authorized=false")
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
            or not math.isfinite(args.deadline_epoch) or not math.isfinite(args.invocation_started_epoch)
            or args.timeout_seconds != PER_PHASE_TIMEOUT_SECONDS
            or args.deadline_epoch - args.invocation_started_epoch != TOTAL_DEADLINE_SECONDS
            or args.deadline_epoch - time.time() < PER_PHASE_TIMEOUT_SECONDS
        ):
            parser.error("run-bounded phase/timeout/deadline is outside the script-owned exact contract")
        if (
            not args.working_directory.is_absolute() or args.working_directory.is_symlink()
            or args.working_directory.resolve(strict=True) != args.working_directory
            or not args.journal.is_absolute() or args.journal.is_symlink()
            or not args.raw_journal.is_absolute() or args.raw_journal.is_symlink()
            or not args.delegate_journal.is_absolute() or args.delegate_journal.is_symlink()
            or not args.output.is_absolute() or args.output.is_symlink() or args.output.exists()
        ):
            parser.error("run-bounded paths must be absolute canonical non-symlinks")
        for path in (args.journal, args.raw_journal, args.delegate_journal, args.output):
            if not path.parent.is_dir() or path.parent.is_symlink() or path.parent.resolve(strict=True) != path.parent:
                parser.error("run-bounded output parents must be real canonical directories")
        for path in (args.journal, args.raw_journal, args.delegate_journal):
            if path.exists() and (not path.is_file() or path.stat().st_mode & 0o077):
                parser.error("all journals must remain owner-only regular files")
        started_epoch = time.time()
        runner = REPO_ROOT / "scripts/run_internal_phone_beta_bounded.py"
        command = [
            sys.executable, os.fspath(runner), "--journal", os.fspath(args.delegate_journal),
            "--output", os.fspath(args.output), "--phase", args.phase,
            "--timeout-seconds", str(args.timeout_seconds),
            "--deadline-epoch", str(args.deadline_epoch), "--", *argv,
        ]
        completed = subprocess.run(
            command, check=False, cwd=args.working_directory, env=environment,
        )
        delegated_started, delegated_finished = load_journal_pair(
            args.delegate_journal, "delegate bounded runner",
        )
        if (
            delegated_started.get("contractName") != DELEGATE_COMMAND_JOURNAL_CONTRACT
            or delegated_started.get("phase") != args.phase
            or delegated_started.get("event") != "started"
            or delegated_started.get("command") != argv
            or delegated_started.get("timeoutSeconds") != PER_PHASE_TIMEOUT_SECONDS
            or delegated_finished.get("contractName") != DELEGATE_COMMAND_JOURNAL_CONTRACT
            or delegated_finished.get("phase") != args.phase
            or delegated_finished.get("event") != "finished"
        ):
            raise ValueError("delegate bounded runner journal did not bind exact phase/timeout/command")
        raw_common = {
            "contractName": RAW_COMMAND_JOURNAL_CONTRACT, "phase": args.phase,
            "command": argv, "timeoutSeconds": PER_PHASE_TIMEOUT_SECONDS,
            "deadlineEpoch": args.deadline_epoch,
            "invocationStartedEpoch": args.invocation_started_epoch,
            "totalDeadlineSeconds": TOTAL_DEADLINE_SECONDS,
            "processGroupTermination": True, "publicationAuthorized": False,
        }
        raw_rows = [
            {**raw_common, "event": "started"},
            {
                **raw_common, "event": "finished",
                **{key: delegated_finished[key] for key in (
                    "elapsedSeconds", "exitCode", "timedOut", "outputSha256", "termination",
                )},
            },
        ]
        append_rows(args.raw_journal, raw_rows, "raw bounded runner journal")
        raw_started, raw_finished = raw_rows
        common = {
            "contractName": COMMAND_JOURNAL_CONTRACT, "phase": args.phase,
            "argv": argv, "workingDirectory": os.fspath(args.working_directory),
            "environment": environment,
            "timeoutSeconds": raw_started["timeoutSeconds"],
            "deadlineEpoch": args.deadline_epoch, "startedEpoch": started_epoch,
            "invocationStartedEpoch": args.invocation_started_epoch,
            "totalDeadlineSeconds": TOTAL_DEADLINE_SECONDS,
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
        append_rows(args.journal, rows, "canonical bounded command journal")
        return completed.returncode
    common = {
        "android_root": args.android_root,
        "presentation_root": args.presentation_root,
        "core_content_root": args.core_content_root,
        "ui_authority_receipt_path": args.ui_authority_receipt,
        "package_feed_path": args.package_feed,
        "package_authority_path": args.package_authority,
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
        package_authority_log_path=args.package_authority_log,
        package_authority_binding_path=args.package_authority_binding,
        content_source_log_path=args.content_source_log,
        build_inputs_log_path=args.build_inputs_log,
        restore_log_path=args.restore_log,
        build_log_path=args.build_log,
        signing_phase_log_path=args.signing_phase_log,
        apksigner_log_path=args.apksigner_log,
        jarsigner_log_path=args.jarsigner_log,
        signing_receipt_path=args.signing_receipt,
        content_apk_log_path=args.content_apk_log,
        package_authority_seal_log_path=args.package_authority_seal_log,
        package_authority_seal_path=args.package_authority_seal,
        command_journal_path=args.command_journal,
        raw_command_journal_path=args.raw_command_journal,
        delegate_command_journal_path=args.delegate_command_journal,
        android_sdk_packages_path=args.android_sdk_packages,
        android_sdk_root=args.android_sdk_root,
        android_workload_manifest_path=args.android_workload_manifest,
        maui_workload_manifest_path=args.maui_workload_manifest,
        dotnet_workloads_path=args.dotnet_workloads,
        java_path=args.java_path, javac_path=args.javac_path,
        jarsigner_path=args.jarsigner_path, apksigner_path=args.apksigner_path,
        dotnet_path=args.dotnet_path, python_path=args.python_path,
        offline_feed_path=args.offline_feed,
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
