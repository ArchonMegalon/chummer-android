#!/usr/bin/env python3
"""Authenticate or materialize an internal API-36 ARM64 build candidate."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys


REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT))

from tests.api36_physical_build_provenance import (  # noqa: E402
    authenticate_inputs,
    create_manifest,
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
    parser.add_argument("--content-source-receipt", type=Path, required=True)
    parser.add_argument("--full-project-lock", type=Path, required=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    check = subparsers.add_parser("check-inputs")
    add_common(check)
    materialize = subparsers.add_parser("materialize")
    add_common(materialize)
    materialize.add_argument("--apk", type=Path, required=True)
    materialize.add_argument("--content-apk-receipt", type=Path, required=True)
    materialize.add_argument("--assets", type=Path, required=True)
    materialize.add_argument("--source-graph-log", type=Path, required=True)
    materialize.add_argument("--content-source-log", type=Path, required=True)
    materialize.add_argument("--build-inputs-log", type=Path, required=True)
    materialize.add_argument("--restore-log", type=Path, required=True)
    materialize.add_argument("--build-log", type=Path, required=True)
    materialize.add_argument("--content-apk-log", type=Path, required=True)
    materialize.add_argument("--source-graph-seal-log", type=Path, required=True)
    materialize.add_argument("--command-journal", type=Path, required=True)
    materialize.add_argument("--android-sdk-packages", type=Path, required=True)
    materialize.add_argument("--java-version", required=True)
    materialize.add_argument("--dotnet-version", required=True)
    materialize.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    common = {
        "android_root": args.android_root.resolve(),
        "presentation_root": args.presentation_root.resolve(),
        "core_content_root": args.core_content_root.resolve(),
        "w5_receipt_path": args.w5_receipt.resolve(),
        "w5_evidence_directory": args.w5_evidence_directory.resolve(),
        "source_graph_path": args.source_graph.resolve(),
        "package_authority_path": args.package_authority.resolve(),
        "content_source_receipt_path": args.content_source_receipt.resolve(),
        "full_project_lock_path": args.full_project_lock.resolve(),
    }
    if args.command == "check-inputs":
        authenticate_inputs(**common)
        print("api36_physical_build_inputs=pass publication_authorized=false")
        return 0
    manifest = create_manifest(
        **common,
        apk=args.apk.resolve(),
        content_apk_receipt_path=args.content_apk_receipt.resolve(),
        assets_path=args.assets.resolve(),
        source_graph_log_path=args.source_graph_log.resolve(),
        content_source_log_path=args.content_source_log.resolve(),
        build_inputs_log_path=args.build_inputs_log.resolve(),
        restore_log_path=args.restore_log.resolve(),
        build_log_path=args.build_log.resolve(),
        content_apk_log_path=args.content_apk_log.resolve(),
        source_graph_seal_log_path=args.source_graph_seal_log.resolve(),
        command_journal_path=args.command_journal.resolve(),
        android_sdk_packages_path=args.android_sdk_packages.resolve(),
        java_version=args.java_version,
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
