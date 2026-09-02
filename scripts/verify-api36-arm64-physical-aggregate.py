#!/usr/bin/env python3
"""Materialize or verify the exact six-journey API-36 ARM64 aggregate."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from api36_arm64_physical_contract import (  # noqa: E402
    JOURNEY_ORDER,
    capture_build_inputs,
    capture_driver_authority,
    create_aggregate,
    load_and_verify_aggregate,
    parse_driver_paths,
    validate_source_graph,
    write_json_exclusive,
)


def parse_bound_rows(values: list[str], label: str) -> dict[str, Path]:
    result: dict[str, Path] = {}
    for value in values:
        journey, separator, raw_path = value.partition("=")
        if not separator or journey not in JOURNEY_ORDER or not raw_path or journey in result:
            raise ValueError(f"{label} must contain each exact journey once as id=/absolute/path")
        path = Path(raw_path)
        if not path.is_absolute():
            raise ValueError(f"{label} path must be absolute: {journey}")
        result[journey] = path
    if tuple(result) != JOURNEY_ORDER:
        raise ValueError(f"{label} cardinality/order must be {JOURNEY_ORDER!r}")
    return result


def add_common(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--source-graph", type=Path, required=True)
    parser.add_argument("--build-provenance", type=Path, required=True)
    parser.add_argument("--device-observation", type=Path, required=True)
    parser.add_argument("--android-repository", type=Path, required=True)
    parser.add_argument("--driver", action="append", default=[], required=True)
    parser.add_argument("--raw-receipt", action="append", default=[], required=True)
    parser.add_argument("--restart-evidence", action="append", default=[], required=True)
    parser.add_argument("--journey-seal", action="append", default=[], required=True)


def arguments(args: argparse.Namespace) -> dict[str, object]:
    receipts = parse_bound_rows(args.raw_receipt, "raw receipt")
    restarts = parse_bound_rows(args.restart_evidence, "restart evidence")
    seals = parse_bound_rows(args.journey_seal, "journey seal")
    return {
        "journey_inputs": [
            (journey, receipts[journey], restarts[journey], seals[journey])
            for journey in JOURNEY_ORDER
        ],
        "apk_path": args.apk, "source_graph_path": args.source_graph,
        "build_provenance_path": args.build_provenance,
        "device_observation_path": args.device_observation,
        "repository_root": args.android_repository,
        "driver_paths": parse_driver_paths(args.driver),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    preflight = subparsers.add_parser("preflight")
    preflight.add_argument("--apk", type=Path, required=True)
    preflight.add_argument("--source-graph", type=Path, required=True)
    preflight.add_argument("--build-provenance", type=Path, required=True)
    preflight.add_argument("--android-repository", type=Path, required=True)
    preflight.add_argument("--driver", action="append", default=[], required=True)
    materialize = subparsers.add_parser("materialize")
    add_common(materialize)
    materialize.add_argument("--output", type=Path, required=True)
    verify = subparsers.add_parser("verify")
    add_common(verify)
    verify.add_argument("--aggregate", type=Path, required=True)
    args = parser.parse_args(argv)
    repository_root = Path(__file__).resolve().parents[1]
    try:
        if args.command == "preflight":
            _apk, graph, _provenance, payload = capture_build_inputs(
                apk_path=args.apk, source_graph_path=args.source_graph,
                build_provenance_path=args.build_provenance,
                repository_root=args.android_repository,
            )
            capture_driver_authority(
                repository_root=args.android_repository,
                driver_paths=parse_driver_paths(args.driver),
                source_graph=validate_source_graph(graph),
            )
            print(
                "physical_build_inputs=pass publication_authorized=false "
                f"authority_sha256={payload['authoritySha256']}"
            )
            return 0
        common = arguments(args)
        if args.command == "materialize":
            payload = create_aggregate(**common)
            write_json_exclusive(args.output, payload, repository_root)
            output = args.output
        else:
            payload = load_and_verify_aggregate(args.aggregate, **common)
            output = args.aggregate
    except Exception as error:  # noqa: BLE001 - fail-closed CLI boundary
        print(f"physical_six_journey_aggregate=blocked error={type(error).__name__}:{error}", file=sys.stderr)
        return 1
    print(
        f"physical_six_journey_aggregate=pass publication_authorized=false "
        f"authority_sha256={payload['authoritySha256']} output={output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
