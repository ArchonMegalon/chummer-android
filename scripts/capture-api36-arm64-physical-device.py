#!/usr/bin/env python3
"""Capture one stable non-emulator API-36 ARM64 Android device observation."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import os
from pathlib import Path
import subprocess
import sys
from typing import Callable

sys.path.insert(0, str(Path(__file__).resolve().parent))
from api36_arm64_physical_contract import (  # noqa: E402
    ABI,
    DEVICE_SCHEMA,
    SERIAL,
    validate_device_payload,
    write_json_exclusive,
)


PROPERTIES = (
    "ro.boot.qemu", "ro.boot.verifiedbootstate", "ro.build.fingerprint",
    "ro.build.id", "ro.build.version.security_patch", "ro.build.version.sdk",
    "ro.hardware", "ro.kernel.qemu", "ro.product.cpu.abi",
    "ro.product.cpu.abilist", "ro.product.device", "ro.product.manufacturer",
    "ro.product.model", "ro.product.name",
)


def adb_runner(adb: Path, serial: str, *arguments: str) -> str:
    result = subprocess.run(
        [str(adb), "-s", serial, *arguments], check=True, capture_output=True,
        text=True, timeout=30,
    )
    return result.stdout.strip()


def capture_payload(
    adb: Path, serial: str, *,
    runner: Callable[..., str] = adb_runner,
    captured_at_utc: str | None = None,
) -> dict[str, object]:
    if (
        not adb.is_absolute() or adb.resolve(strict=True) != adb
        or adb.is_symlink() or not adb.is_file() or not os.access(adb, os.X_OK)
    ):
        raise ValueError("ADB executable must be one absolute canonical regular file")
    if SERIAL.fullmatch(serial) is None:
        raise ValueError("ADB serial does not match the safe exact grammar")
    observations: list[dict[str, str]] = []
    for _attempt in range(2):
        if runner(adb, serial, "get-state") != "device":
            raise ValueError("the selected ADB transport is not ready")
        observations.append({
            name: runner(adb, serial, "shell", "getprop", name)
            for name in PROPERTIES
        })
    if observations[0] != observations[1]:
        raise ValueError("physical device properties changed during stable capture")
    properties = observations[0]
    abi_list = properties["ro.product.cpu.abilist"].split(",")
    payload = {
        "schema": DEVICE_SCHEMA, "status": "pass",
        "classification": "physical_api36_arm64_non_emulator",
        "publicationAuthorized": False, "serial": serial,
        "serialSha256": hashlib.sha256(serial.encode("utf-8")).hexdigest(),
        "apiLevel": int(properties["ro.build.version.sdk"]),
        "abi": properties["ro.product.cpu.abi"], "abiList": abi_list,
        "properties": properties,
        "observationNature": "stable-twice non-cryptographic adb/getprop observation",
        "capturedAtUtc": captured_at_utc
        or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    validate_device_payload(payload)
    return payload


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    repository_root = Path(__file__).resolve().parents[1]
    try:
        payload = capture_payload(args.adb, args.serial)
        write_json_exclusive(args.output, payload, repository_root)
    except Exception as error:  # noqa: BLE001 - fail-closed CLI boundary
        print(f"physical_device_capture=blocked error={type(error).__name__}:{error}", file=sys.stderr)
        return 1
    print(
        f"physical_device_capture=pass publication_authorized=false "
        f"serial_sha256={payload['serialSha256']} output={args.output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
