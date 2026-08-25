#!/usr/bin/env python3
"""Prove exact Career Notoriety persistence on the isolated ARM64 package."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

import run_api36_career_notoriety_e2e as notoriety
import run_api36_editing_e2e as shared


PACKAGE = "com.myexternalbrain.chummer.codexproof.arm64"
PRESERVED_PACKAGES = (
    "com.myexternalbrain.chummer",
    "com.myexternalbrain.chummer.codexproof",
)
EXPECTED_ABI = "arm64-v8a"
SHA256 = re.compile(r"[0-9a-f]{64}")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def package_snapshot(device: shared.Device, package: str) -> dict[str, str]:
    dump = device.shell("dumpsys", "package", package)
    if f"Package [{package}]" not in dump:
        raise RuntimeError(f"Required package {package!r} is not installed")

    def one(pattern: str, label: str) -> str:
        matches = re.findall(pattern, dump, flags=re.MULTILINE)
        if len(matches) != 1:
            raise RuntimeError(
                f"Package {package!r} exposed {len(matches)} exact {label} values"
            )
        return matches[0]

    return {
        "package": package,
        "versionCode": one(r"^\s*versionCode=([^\s]+)", "versionCode"),
        "versionName": one(r"^\s*versionName=(.+)$", "versionName"),
        "primaryCpuAbi": one(r"^\s*primaryCpuAbi=(.+)$", "primaryCpuAbi"),
        "lastUpdateTime": one(r"^\s*lastUpdateTime=(.+)$", "lastUpdateTime"),
    }


def installed_apk_snapshot(
    device: shared.Device,
    package: str,
    expected_sha256: str,
) -> dict[str, str]:
    if SHA256.fullmatch(expected_sha256) is None:
        raise RuntimeError(f"Invalid expected APK SHA-256: {expected_sha256!r}")

    paths = [
        line.removeprefix("package:").strip()
        for line in device.shell("pm", "path", package).splitlines()
        if line.startswith("package:")
    ]
    if len(paths) != 1:
        raise RuntimeError(
            f"Installed package {package!r} exposed {len(paths)} base APK paths"
        )
    path = paths[0]
    if not path.startswith("/data/app/") or not path.endswith("/base.apk"):
        raise RuntimeError(f"Unsafe installed APK path for {package!r}: {path!r}")

    fields = device.shell("sha256sum", path, timeout=300).split()
    actual_sha256 = fields[0].lower() if fields else ""
    if SHA256.fullmatch(actual_sha256) is None or actual_sha256 != expected_sha256:
        raise RuntimeError(
            f"Installed APK digest mismatch for {package!r}: "
            f"expected {expected_sha256}, got {actual_sha256 or 'unavailable'}"
        )
    return {"package": package, "path": path, "sha256": actual_sha256}


def require_provenance(provenance: dict[str, object], apk_sha256: str) -> None:
    expected = {
        "schema": "chummer.android.physical-apk/v1",
        "status": "pass",
        "packageId": PACKAGE,
        "abi": EXPECTED_ABI,
        "runtimeIdentifier": "android-arm64",
        "distributionLane": "isolated-physical-proof",
        "apkSha256": apk_sha256,
        "playBetaSigningClaim": False,
        "stableUpdateSigningClaim": False,
    }
    mismatches = {
        key: {"expected": value, "actual": provenance.get(key)}
        for key, value in expected.items()
        if provenance.get(key) != value
    }
    if mismatches:
        raise RuntimeError(f"Physical APK provenance mismatch: {mismatches!r}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--provenance", type=Path, required=True)
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent
        / "fixtures/career-notoriety-e2e.chum5",
    )
    args = parser.parse_args()

    apk = args.apk.resolve()
    provenance_path = args.provenance.resolve()
    fixture = args.career_runner.resolve()
    apk_sha256 = sha256(apk)
    provenance = json.loads(provenance_path.read_text(encoding="utf-8"))
    require_provenance(provenance, apk_sha256)

    root = notoriety.ET.parse(fixture).getroot()
    notoriety.require_canonical_import_fixture(root)
    notoriety.assert_before(root)
    fixture_sha256 = sha256(fixture)

    shared.PACKAGE = PACKAGE
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    model = device.shell("getprop", "ro.product.model")
    if api != "36":
        raise RuntimeError(f"Physical proof requires API 36, got {api!r}")
    if abi != EXPECTED_ABI:
        raise RuntimeError(f"Physical proof requires {EXPECTED_ABI}, got {abi!r}")

    before = {
        package: package_snapshot(device, package)
        for package in (*PRESERVED_PACKAGES, PACKAGE)
    }
    installed_apk_before = installed_apk_snapshot(device, PACKAGE, apk_sha256)
    target = before[PACKAGE]
    if target["versionCode"] != "10" or target["primaryCpuAbi"] != EXPECTED_ABI:
        raise RuntimeError(f"Installed physical package does not match the proof lane: {target!r}")

    verified_remote_fixture_sha256 = device.push_verified(
        fixture,
        f"/sdcard/Download/{fixture.name}",
        fixture_sha256,
    )
    journey = notoriety.prove_notoriety_edit(device, fixture, fixture_sha256)

    after = {
        package: package_snapshot(device, package)
        for package in (*PRESERVED_PACKAGES, PACKAGE)
    }
    installed_apk_after = installed_apk_snapshot(device, PACKAGE, apk_sha256)
    if before != after:
        raise RuntimeError("Physical proof changed an installed package boundary")
    if installed_apk_before != installed_apk_after:
        raise RuntimeError("Physical proof changed the installed APK authority")

    receipt = {
        "schema": "chummer.android.physical-editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "model": model,
        "profile": "phone",
        "journey": "career-notoriety",
        "apiLevel": int(api),
        "abi": abi,
        "package": PACKAGE,
        "apkSha256": apk_sha256,
        "provenanceSha256": sha256(provenance_path),
        "provenance": provenance,
        "driverSha256": sha256(Path(__file__).resolve()),
        "sharedDriverSha256": sha256(Path(shared.__file__).resolve()),
        "journeyDriverSha256": sha256(Path(notoriety.__file__).resolve()),
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote_fixture_sha256,
        "installedPackagesBefore": before,
        "installedPackagesAfter": after,
        "installedApkBefore": installed_apk_before,
        "installedApkAfter": installed_apk_after,
        "controlCount": 1,
        "controls": {
            notoriety.CONTROL: {
                key: "pass" for key in notoriety.CONTROL_PROOF_KEYS
            }
        },
        "authorityProofStages": journey,
        "journeys": {
            "exactNotorietyEdit": "pass",
            "sameSessionReopen": "pass",
            "newProcessRestart": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Physical Career Notoriety E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
