#!/usr/bin/env python3
"""Seal the launched API-36 emulator's stable stdout/stderr prefix."""

from __future__ import annotations

import argparse
import json
import os
import stat
import sys
import tempfile
from pathlib import Path

from api36_proof_environment_authority import (
    EMULATOR_LIVE_LOG_NAME,
    build_emulator_live_observation,
)


def exact_new_output(path: Path) -> Path:
    candidate = path.absolute()
    if not path.is_absolute() or candidate != path:
        raise ValueError("emulator observation output must be absolute and normalized")
    try:
        parent = candidate.parent.resolve(strict=True)
    except (OSError, RuntimeError) as error:
        raise ValueError("emulator observation output parent is missing or unsafe") from error
    if parent != candidate.parent or candidate.parent.is_symlink():
        raise ValueError("emulator observation output parent must contain no symlink")
    parent_metadata = os.lstat(candidate.parent)
    if (
        not stat.S_ISDIR(parent_metadata.st_mode)
        or parent_metadata.st_uid != os.geteuid()
        or stat.S_IMODE(parent_metadata.st_mode) != 0o700
    ):
        raise ValueError("emulator observation output parent must be private and owned")
    if candidate.exists() or candidate.is_symlink():
        raise ValueError("emulator observation output already exists")
    return candidate


def write_new_private(path: Path, value: dict[str, object]) -> None:
    target = exact_new_output(path)
    encoded = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary: str | None = None
    try:
        descriptor, temporary = tempfile.mkstemp(
            dir=target.parent,
            prefix=f".{target.name}.",
        )
        with os.fdopen(descriptor, "wb") as stream:
            os.fchmod(stream.fileno(), 0o600)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        metadata = os.lstat(temporary)
        if (
            not stat.S_ISREG(metadata.st_mode)
            or stat.S_IMODE(metadata.st_mode) != 0o600
            or metadata.st_uid != os.geteuid()
            or metadata.st_nlink != 1
        ):
            raise ValueError("emulator observation temporary identity differs")
        os.link(temporary, target, follow_symlinks=False)
        final = os.lstat(target)
        if (
            not stat.S_ISREG(final.st_mode)
            or final.st_dev != metadata.st_dev
            or final.st_ino != metadata.st_ino
            or stat.S_IMODE(final.st_mode) != 0o600
            or final.st_uid != os.geteuid()
            or final.st_nlink != 2
        ):
            target.unlink(missing_ok=True)
            raise ValueError("emulator observation publication identity differs")
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)
    published = os.lstat(target)
    if (
        not stat.S_ISREG(published.st_mode)
        or published.st_dev != metadata.st_dev
        or published.st_ino != metadata.st_ino
        or stat.S_IMODE(published.st_mode) != 0o600
        or published.st_uid != os.geteuid()
        or published.st_nlink != 1
    ):
        target.unlink(missing_ok=True)
        raise ValueError("emulator observation publication identity differs")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--live-log", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--run-attempt", type=int, required=True)
    parser.add_argument("--matrix-journey", required=True)
    args = parser.parse_args(argv)
    runner_temp = os.environ.get("RUNNER_TEMP")
    if not runner_temp:
        raise ValueError("RUNNER_TEMP is required")
    expected_live_log = Path(runner_temp) / EMULATOR_LIVE_LOG_NAME
    if (
        not args.live_log.is_absolute()
        or args.live_log != args.live_log.absolute()
        or args.live_log != expected_live_log
    ):
        raise ValueError("emulator live log is not the exact RUNNER_TEMP target")
    value = build_emulator_live_observation(
        live_log_path=args.live_log,
        run_id=args.run_id,
        run_attempt=args.run_attempt,
        matrix_journey=args.matrix_journey,
    )
    write_new_private(args.output, value)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"API-36 emulator live observation failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
