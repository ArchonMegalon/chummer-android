#!/usr/bin/env python3
"""Run one source/APK-bound Chummer journey on an attached physical API-36 phone.

This is an operator entry point, not a device farm and not release attestation.  It
never runs ``adb connect`` and never retries a mutating command.  The selected
journey remains the authority for its detailed edit/save/reopen/restart claims.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
from typing import Callable


SCHEMA = "chummer.android.api36-physical-phone-proof-session/v1"
PASS_STATUS = "device-pass-source-bound"
PASS_EXECUTION_STATUS = "pass"
SOURCE_BOUND_STATUS = "source-and-apk-bound-local-build-not-release-attested"
SAFE_SERIAL = re.compile(r"^[A-Za-z0-9._:-]{1,255}$")
PROCESS_ID = re.compile(r"^[1-9][0-9]*$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
EMULATOR_HARDWARE_MARKERS = ("goldfish", "ranchu", "cuttlefish")
MAX_FAILURE_CHARACTERS = 4000

JOURNEYS = {
    "sr5-career-active-skill": "run_api36_sr5_career_active_skill_wizard_e2e.py",
    "sr5-career-attribute": "run_api36_sr5_career_attribute_wizard_e2e.py",
    "sr5-career-knowledge-language": "run_api36_sr5_career_knowledge_language_wizard_e2e.py",
    "sr5-career-quality": "run_api36_sr5_career_quality_wizard_e2e.py",
    "sr5-creation-contacts": "run_api36_sr5_creation_contacts_e2e.py",
    "sr5-creation-lifestyles": "run_api36_sr5_creation_lifestyles_e2e.py",
}


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(
        value,
        allow_nan=False,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def strict_json(path: Path) -> dict[str, object]:
    def object_without_duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
        result: dict[str, object] = {}
        for key, value in pairs:
            if key in result:
                raise RuntimeError(f"Duplicate JSON key in {path.name}: {key}")
            result[key] = value
        return result

    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=object_without_duplicates,
        )
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise RuntimeError(f"Receipt is not strict UTF-8 JSON: {path}") from error
    if not isinstance(value, dict):
        raise RuntimeError(f"Receipt is not a JSON object: {path}")
    return value


def write_new_json(path: Path, value: dict[str, object]) -> None:
    encoded = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(encoded)
        stream.flush()
        os.fsync(stream.fileno())


def _is_relative_to(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
    except ValueError:
        return False
    return True


def prepare_fresh_output_root(path: Path, protected_roots: tuple[Path, ...]) -> Path:
    if not path.is_absolute():
        raise RuntimeError("Physical proof output root must be absolute")
    if path.exists() or path.is_symlink():
        raise RuntimeError("Physical proof output root already exists; stale evidence is forbidden")
    resolved_parent = path.parent.resolve(strict=True)
    resolved = resolved_parent / path.name
    if any(_is_relative_to(resolved, root.resolve()) for root in protected_roots):
        raise RuntimeError("Physical proof output must remain outside every source repository")
    resolved.mkdir(mode=0o700)
    return resolved


def _require_regular_file(path: Path, label: str, *, executable: bool = False) -> Path:
    if not path.is_absolute():
        raise RuntimeError(f"{label} must be an absolute path")
    try:
        supplied_mode = os.lstat(path).st_mode
    except FileNotFoundError as error:
        raise RuntimeError(f"{label} does not exist: {path}") from error
    if stat.S_ISLNK(supplied_mode) or not stat.S_ISREG(supplied_mode):
        raise RuntimeError(f"{label} must be one regular non-symlink file")
    resolved = path.resolve(strict=True)
    if executable and not os.access(resolved, os.X_OK):
        raise RuntimeError(f"{label} is not executable")
    return resolved


def _adb_text(adb: Path, serial: str, *arguments: str) -> str:
    try:
        result = subprocess.run(
            [str(adb), "-s", serial, *arguments],
            check=True,
            capture_output=True,
            text=True,
            timeout=15,
        )
    except (OSError, subprocess.SubprocessError) as error:
        message = str(error).replace(serial, "<serial>")[:MAX_FAILURE_CHARACTERS]
        raise RuntimeError(f"ADB read-only preflight failed: {message}") from error
    return result.stdout.strip()


def observe_physical_api36_phone(
    adb: Path,
    serial: str,
    invoke: Callable[..., str] = _adb_text,
) -> dict[str, object]:
    if SAFE_SERIAL.fullmatch(serial) is None:
        raise RuntimeError("ADB serial does not match the bounded safe grammar")
    if invoke(adb, serial, "get-state") != "device":
        raise RuntimeError("The requested ADB transport is not in device state")
    api_level = invoke(adb, serial, "shell", "getprop", "ro.build.version.sdk")
    abi = invoke(adb, serial, "shell", "getprop", "ro.product.cpu.abi")
    abi_list = invoke(adb, serial, "shell", "getprop", "ro.product.cpu.abilist")
    qemu = invoke(adb, serial, "shell", "getprop", "ro.kernel.qemu")
    hardware = invoke(adb, serial, "shell", "getprop", "ro.hardware")
    characteristics = invoke(
        adb, serial, "shell", "getprop", "ro.build.characteristics"
    )
    if api_level != "36":
        raise RuntimeError(f"Physical proof requires exact API 36, got {api_level!r}")
    if abi != "arm64-v8a" or "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError("Physical proof requires an exact arm64-v8a primary ABI")
    emulator_markers = (
        serial.startswith("emulator-")
        or qemu == "1"
        or "emulator" in characteristics.casefold()
        or any(marker in hardware.casefold() for marker in EMULATOR_HARDWARE_MARKERS)
    )
    if emulator_markers:
        raise RuntimeError("The requested transport has an emulator marker")
    return {
        "classification": "observed-non-emulator-arm64-api36",
        "evidenceNature": "non-cryptographic adb/getprop observations",
        "serialSha256": hashlib.sha256(serial.encode("utf-8")).hexdigest(),
        "apiLevel": 36,
        "abi": abi,
        "abiList": abi_list,
        "qemu": qemu,
        "hardware": hardware,
        "characteristics": characteristics,
        "manufacturer": invoke(
            adb, serial, "shell", "getprop", "ro.product.manufacturer"
        ),
        "model": invoke(adb, serial, "shell", "getprop", "ro.product.model"),
        "buildFingerprintSha256": hashlib.sha256(
            invoke(
                adb, serial, "shell", "getprop", "ro.build.fingerprint"
            ).encode("utf-8")
        ).hexdigest(),
    }


def _values_for_key(value: object, key: str) -> list[object]:
    found: list[object] = []
    if isinstance(value, dict):
        for candidate, child in value.items():
            if candidate == key:
                found.append(child)
            found.extend(_values_for_key(child, key))
    elif isinstance(value, list):
        for child in value:
            found.extend(_values_for_key(child, key))
    return found


def validate_passing_journey_receipt(
    receipt: dict[str, object],
    provenance: dict[str, object],
) -> dict[str, object]:
    if receipt.get("status") != PASS_STATUS or receipt.get("executionStatus") != PASS_EXECUTION_STATUS:
        raise RuntimeError("Physical journey receipt does not report an exact passing execution")
    if receipt.get("releaseEvidenceStatus") != SOURCE_BOUND_STATUS:
        raise RuntimeError("Physical journey receipt widened or lost its release-truth posture")
    if receipt.get("buildProvenance") != provenance:
        raise RuntimeError("Physical journey receipt is not bound to the supplied provenance")
    artifact = provenance.get("artifact")
    if not isinstance(artifact, dict) or SHA256.fullmatch(str(artifact.get("sha256", ""))) is None:
        raise RuntimeError("Verified build provenance has no exact APK SHA-256")
    if receipt.get("apkSha256") != artifact["sha256"]:
        raise RuntimeError("Physical journey receipt APK digest differs from provenance")

    restart_values = _values_for_key(receipt, "restartProcessIds")
    process_chains: list[list[str]] = []
    for value in restart_values:
        if not isinstance(value, list) or not value:
            raise RuntimeError("Physical journey contains an empty restart process chain")
        candidates = value if all(isinstance(item, str) for item in value) else value
        for candidate in candidates:
            chain = [candidate] if isinstance(candidate, str) else candidate
            if not isinstance(chain, list) or not chain or any(
                not isinstance(pid, str) or PROCESS_ID.fullmatch(pid) is None for pid in chain
            ):
                raise RuntimeError("Physical journey has a malformed restart process identity")
            process_chains.append(list(chain))
    if not process_chains:
        raise RuntimeError("Physical journey did not provide a process-restart identity chain")
    return {
        "restartEvidencePresent": True,
        "restartProcessObservationCount": len(process_chains),
    }


def load_provenance_module(android_root: Path):
    module_path = android_root / "tests/api36_physical_build_provenance.py"
    spec = importlib.util.spec_from_file_location("api36_physical_build_provenance", module_path)
    if spec is None or spec.loader is None:
        raise RuntimeError("Cannot load the physical build-provenance authority")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--journey", choices=tuple(JOURNEYS), required=True)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--build-provenance-manifest", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--allow-destructive-disposable-device", action="store_true")
    return parser.parse_args(argv)


def execute(args: argparse.Namespace) -> tuple[int, dict[str, object]]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            "--allow-destructive-disposable-device is required; the proof installs, clears, imports, and mutates"
        )
    android_root = Path(__file__).resolve().parents[1]
    workspace_root = args.workspace_root.resolve(strict=True)
    core_root = (workspace_root / "chummer-core-engine").resolve(strict=True)
    presentation_root = (workspace_root / "chummer-presentation").resolve(strict=True)
    output_root = prepare_fresh_output_root(
        args.output_root,
        (android_root, core_root, presentation_root),
    )
    adb = _require_regular_file(args.adb, "ADB", executable=True)
    apk = _require_regular_file(args.apk, "ARM64 APK")
    manifest = _require_regular_file(
        args.build_provenance_manifest, "Build-provenance manifest"
    )
    provenance_module = load_provenance_module(android_root)
    provenance = provenance_module.load_and_verify_manifest(
        manifest,
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
    )
    observation = observe_physical_api36_phone(adb, args.serial)
    driver = _require_regular_file(
        android_root / "tests" / JOURNEYS[args.journey], "Physical journey driver"
    )
    receipt_path = output_root / "journey-receipt.json"
    evidence_path = output_root / "journey-evidence"
    command = [
        sys.executable,
        str(driver),
        "--adb",
        str(adb),
        "--apk",
        str(apk),
        "--build-provenance-manifest",
        str(manifest),
        "--serial",
        args.serial,
        "--workspace-root",
        str(workspace_root),
        "--evidence",
        str(evidence_path),
        "--receipt",
        str(receipt_path),
        "--allow-destructive-disposable-device",
    ]
    result = subprocess.run(command, check=False, timeout=45 * 60)
    if not receipt_path.is_file():
        raise RuntimeError(
            f"Physical journey exited {result.returncode} without a receipt"
        )
    journey_receipt = strict_json(receipt_path)
    if result.returncode != 0:
        raise RuntimeError(f"Physical journey failed with exit code {result.returncode}")
    restart_summary = validate_passing_journey_receipt(journey_receipt, provenance)
    session = {
        "schema": SCHEMA,
        "status": "pass",
        "executionStatus": "pass",
        "releaseEvidenceEligible": False,
        "releaseEvidenceStatus": SOURCE_BOUND_STATUS,
        "proofScope": "one-selected-journey-not-exhaustive-parity",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "journey": args.journey,
        "driver": {
            "path": f"tests/{driver.name}",
            "sha256": file_sha256(driver),
        },
        "buildProvenanceSha256": file_sha256(manifest),
        "buildAuthoritySha256": provenance.get("authoritySha256"),
        "apkSha256": provenance["artifact"]["sha256"],
        "deviceObservation": observation,
        "journeyReceipt": {
            "path": receipt_path.name,
            "sha256": file_sha256(receipt_path),
            **restart_summary,
        },
    }
    session["sessionSha256"] = canonical_sha256(session)
    write_new_json(output_root / "session-receipt.json", session)
    return (0, session)


def main(argv: list[str] | None = None) -> int:
    raw = list(sys.argv[1:] if argv is None else argv)
    try:
        args = parse_args(raw)
        status, session = execute(args)
    except Exception as error:  # noqa: BLE001 - operator failure remains fail closed
        serial = ""
        if "--serial" in raw and raw.index("--serial") + 1 < len(raw):
            serial = raw[raw.index("--serial") + 1]
        message = str(error).replace(serial, "<serial>")[:MAX_FAILURE_CHARACTERS]
        print(f"physical API-36 proof failed: {message}", file=sys.stderr)
        return 3
    print(json.dumps(session, indent=2, sort_keys=True))
    return status


if __name__ == "__main__":
    raise SystemExit(main())
