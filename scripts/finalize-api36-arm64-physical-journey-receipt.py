#!/usr/bin/env python3
"""Adapt and seal one finalized raw physical journey receipt."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from api36_arm64_physical_contract import (  # noqa: E402
    JOURNEY_ORDER,
    create_journey_seal,
    parse_driver_paths,
    write_json_exclusive,
)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--journey-id", choices=JOURNEY_ORDER, required=True)
    parser.add_argument("--raw-receipt", type=Path, required=True)
    parser.add_argument("--restart-evidence", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--source-graph", type=Path, required=True)
    parser.add_argument("--build-provenance", type=Path, required=True)
    parser.add_argument("--device-observation", type=Path, required=True)
    parser.add_argument("--android-repository", type=Path, required=True)
    parser.add_argument("--driver", action="append", default=[], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    repository_root = Path(__file__).resolve().parents[1]
    try:
        seal = create_journey_seal(
            journey_id=args.journey_id, raw_receipt_path=args.raw_receipt,
            restart_evidence_path=args.restart_evidence, apk_path=args.apk,
            source_graph_path=args.source_graph,
            build_provenance_path=args.build_provenance,
            device_observation_path=args.device_observation,
            repository_root=args.android_repository,
            driver_paths=parse_driver_paths(args.driver),
        )
        write_json_exclusive(args.output, seal, repository_root)
    except Exception as error:  # noqa: BLE001 - fail-closed CLI boundary
        print(
            f"physical_journey_seal=blocked journey={args.journey_id} "
            f"error={type(error).__name__}:{error}", file=sys.stderr,
        )
        return 1
    print(
        f"physical_journey_seal=pass journey={args.journey_id} "
        f"publication_authorized=false authority_sha256={seal['sealAuthoritySha256']} "
        f"output={args.output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
