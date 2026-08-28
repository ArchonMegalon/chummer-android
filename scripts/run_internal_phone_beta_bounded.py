#!/usr/bin/env python3
"""Run one phone-beta proof command with durable bounds and process-group teardown."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import signal
import subprocess
import time
from pathlib import Path


CONTRACT = "chummer.android.internal-phone-beta-command-journal/v1"


def append_journal(path: Path, row: dict[str, object]) -> None:
    flags = os.O_WRONLY | os.O_CREAT | os.O_APPEND | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags, 0o600)
    try:
        with os.fdopen(descriptor, "a", encoding="utf-8") as stream:
            stream.write(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
            stream.flush()
            os.fsync(stream.fileno())
    finally:
        if descriptor >= 0:
            try:
                os.close(descriptor)
            except OSError:
                pass


def terminate_group(process: subprocess.Popen[bytes]) -> None:
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    try:
        process.wait(timeout=5)
        return
    except subprocess.TimeoutExpired:
        pass
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        return
    process.wait(timeout=5)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--journal", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--phase", required=True)
    parser.add_argument("--timeout-seconds", type=float, default=900.0)
    parser.add_argument("--deadline-epoch", type=float, required=True)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command and args.command[0] == "--" else args.command
    if not command or args.timeout_seconds <= 0:
        parser.error("one command and a positive timeout are required")
    if not args.journal.is_absolute() or args.journal.is_symlink():
        parser.error("journal must be an absolute non-symlink path")
    if args.journal.exists() and (not args.journal.is_file() or args.journal.stat().st_mode & 0o077):
        parser.error("existing journal must be an owner-only regular file")
    if not args.output.is_absolute() or args.output.is_symlink() or args.output.exists():
        parser.error("output must be an absent absolute non-symlink path")
    for path, label in ((args.journal, "journal"), (args.output, "output")):
        if not path.parent.is_dir() or path.parent.is_symlink():
            parser.error(f"{label} parent must be a real directory")
    now = time.time()
    remaining = args.deadline_epoch - now
    if remaining <= 0:
        append_journal(args.journal, {
            "contractName": CONTRACT, "phase": args.phase, "event": "blocked",
            "reason": "total-deadline-expired", "publicationAuthorized": False,
        })
        return 124
    timeout = min(args.timeout_seconds, remaining)
    started = time.monotonic()
    append_journal(args.journal, {
        "contractName": CONTRACT, "phase": args.phase, "event": "started",
        "command": command, "timeoutSeconds": timeout,
        "processGroupTermination": True, "publicationAuthorized": False,
    })
    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        start_new_session=True,
    )
    timed_out = False
    try:
        output, _ = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        timed_out = True
        terminate_group(process)
        output = process.stdout.read() if process.stdout is not None else b""
    elapsed = time.monotonic() - started
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(args.output, flags, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(output)
        stream.flush()
        os.fsync(stream.fileno())
    digest = hashlib.sha256(output).hexdigest()
    exit_code = 124 if timed_out else int(process.returncode)
    append_journal(args.journal, {
        "contractName": CONTRACT, "phase": args.phase, "event": "finished",
        "exitCode": exit_code, "timedOut": timed_out,
        "elapsedSeconds": round(elapsed, 6), "outputSha256": digest,
        "processGroupTermination": True, "publicationAuthorized": False,
    })
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
