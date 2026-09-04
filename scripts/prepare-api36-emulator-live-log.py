#!/usr/bin/env python3
"""Precreate the one private API-36 emulator stdout/stderr log target."""

from __future__ import annotations

import os
import stat
import sys
from pathlib import Path


LIVE_LOG_NAME = "chummer-api36-emulator-live.log"


def exact_runner_temp() -> Path:
    raw = os.environ.get("RUNNER_TEMP")
    if not raw:
        raise ValueError("RUNNER_TEMP is required")
    candidate = Path(raw)
    if not candidate.is_absolute() or candidate != Path(os.path.abspath(candidate)):
        raise ValueError("RUNNER_TEMP must be absolute and normalized")
    try:
        resolved = candidate.resolve(strict=True)
    except (OSError, RuntimeError) as error:
        raise ValueError("RUNNER_TEMP is missing or unsafe") from error
    if resolved != candidate or candidate.is_symlink() or not candidate.is_dir():
        raise ValueError("RUNNER_TEMP must contain no symlink component")
    return candidate


def create_live_log() -> Path:
    target = exact_runner_temp() / LIVE_LOG_NAME
    flags = (
        os.O_WRONLY
        | os.O_CREAT
        | os.O_EXCL
        | getattr(os, "O_CLOEXEC", 0)
        | getattr(os, "O_NOFOLLOW", 0)
    )
    try:
        descriptor = os.open(target, flags, 0o600)
    except OSError as error:
        raise ValueError("emulator live-log target already exists or is unsafe") from error
    try:
        os.fchmod(descriptor, 0o600)
        metadata = os.fstat(descriptor)
        if (
            not stat.S_ISREG(metadata.st_mode)
            or stat.S_IMODE(metadata.st_mode) != 0o600
            or metadata.st_uid != os.geteuid()
            or metadata.st_nlink != 1
            or metadata.st_size != 0
        ):
            raise ValueError("emulator live-log target identity differs")
        os.fsync(descriptor)
    except Exception:
        os.close(descriptor)
        target.unlink(missing_ok=True)
        raise
    os.close(descriptor)
    final = os.lstat(target)
    if (
        final.st_dev != metadata.st_dev
        or final.st_ino != metadata.st_ino
        or not stat.S_ISREG(final.st_mode)
        or stat.S_IMODE(final.st_mode) != 0o600
        or final.st_uid != os.geteuid()
        or final.st_nlink != 1
        or final.st_size != 0
    ):
        target.unlink(missing_ok=True)
        raise ValueError("emulator live-log target changed after creation")
    return target


if __name__ == "__main__":
    try:
        create_live_log()
    except (OSError, ValueError) as error:
        print(f"API-36 emulator live-log preparation failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
