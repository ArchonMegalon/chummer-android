#!/usr/bin/env python3
"""Authenticate Design product policy separately from Android runtime sources.

Design is data here: its validator is digest-bound, never imported or trusted to
approve itself. Android independently checks the product-policy intersection.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))
from api36_wizard_gate_contract import (
    AGGREGATE_SCHEMA, CONTRACT_SCHEMA, CONTRACT_RELATIVE_PATH,
    REQUIRED_JOURNEY_SPECS, load_contract, object_without_duplicates,
)

REPO_ROOT = SCRIPT_DIRECTORY.parent
PIN_PATH = Path("eng/design-policy-authority.json")
PIN_SCHEMA = "chummer.android.design-policy-authority/v1"
DESIGN_REPOSITORY = "ArchonMegalon/chummer6-design"
MATRIX_PATH = "products/chummer/ANDROID_PHONE_BETA_SUPPORT_MATRIX.yaml"
VALIDATOR_PATH = "scripts/ai/validate_android_phone_beta_contract.py"
MATRIX_SCHEMA = "chummer.android_phone_beta_support_matrix.v1"
MAX_FILE_BYTES = 2 * 1024 * 1024
SHA40 = re.compile(r"[0-9a-f]{40}\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")


def _read(path: Path) -> bytes:
    if not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path:
        raise ValueError("policy input must be an absolute canonical non-symlink file")
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    try:
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or not 0 < before.st_size <= MAX_FILE_BYTES:
            raise ValueError("policy input is not one bounded regular file")
        data = b""
        while len(data) <= MAX_FILE_BYTES:
            chunk = os.read(fd, min(65536, MAX_FILE_BYTES + 1 - len(data)))
            if not chunk:
                break
            data += chunk
        after = os.fstat(fd)
    finally:
        os.close(fd)
    identity = lambda s: (s.st_dev, s.st_ino, s.st_mode, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
    if identity(before) != identity(after) or len(data) != before.st_size:
        raise ValueError("policy input changed during capture")
    return data


def _json(data: bytes) -> dict:
    value = json.loads(data.decode("utf-8", errors="strict"),
        object_pairs_hook=object_without_duplicates,
        parse_constant=lambda _: (_ for _ in ()).throw(ValueError("non-finite policy JSON")))
    if not isinstance(value, dict):
        raise ValueError("policy must contain one JSON object")
    return value


def _hash(value: object, pattern: re.Pattern, label: str) -> None:
    if not isinstance(value, str) or not pattern.fullmatch(value):
        raise ValueError(f"Design {label} is not a canonical digest")


def load_policy_pin(*, android_root: Path = REPO_ROOT) -> dict:
    pin = _json(_read(android_root / PIN_PATH))
    if set(pin) != {"schema", "design"} or pin.get("schema") != PIN_SCHEMA:
        raise ValueError("Design policy pin schema differs")
    design = pin["design"]
    fields = {"repository", "commit", "tree", "matrix", "validator", "matrixSchema",
              "wizardAggregateSchema", "wizardGate"}
    if not isinstance(design, dict) or set(design) != fields:
        raise ValueError("Design policy authority fields differ")
    if (design["repository"] != DESIGN_REPOSITORY or design["matrixSchema"] != MATRIX_SCHEMA
            or design["wizardAggregateSchema"] != AGGREGATE_SCHEMA):
        raise ValueError("Design repository or policy schema differs")
    for field in ("commit", "tree"):
        _hash(design[field], SHA40, field)
    for field, path in (("matrix", MATRIX_PATH), ("validator", VALIDATOR_PATH)):
        member = design[field]
        if not isinstance(member, dict) or set(member) != {"path", "sha256"} or member["path"] != path:
            raise ValueError(f"Design {field} path or fields differ")
        _hash(member["sha256"], SHA256, field)
    gate_path = android_root / CONTRACT_RELATIVE_PATH
    load_contract(gate_path)
    expected_gate = {"path": CONTRACT_RELATIVE_PATH.as_posix(),
                     "schema": CONTRACT_SCHEMA, "sha256": hashlib.sha256(_read(gate_path)).hexdigest()}
    if design["wizardGate"] != expected_gate:
        raise ValueError("Design pin does not bind the exact Android wizard gate")
    return {"design": design}


def validate_policy_authorities(value: object, *, android_root: Path = REPO_ROOT) -> dict:
    expected = load_policy_pin(android_root=android_root)
    if not isinstance(value, dict) or value != expected:
        raise ValueError("Design policy authorities are missing, stale or substituted")
    return expected


def validate_matrix(matrix: dict, design: dict) -> None:
    if matrix.get("schema") != MATRIX_SCHEMA or matrix.get("owner") != "chummer6-design":
        raise ValueError("Design matrix identity differs")
    evidence = matrix.get("evidenceAuthority")
    if not isinstance(evidence, dict):
        raise ValueError("Design matrix evidence authority missing")
    expected = {
        "implementationRepository": "chummer-android",
        "wizardGateAuthority": "chummer-android/" + CONTRACT_RELATIVE_PATH.as_posix(),
        "wizardGateSchema": CONTRACT_SCHEMA,
        "wizardGateSha256": design["wizardGate"]["sha256"],
        "wizardAggregateSchema": AGGREGATE_SCHEMA,
        "requiredP0Journeys": [row["matrixJourney"] for row in REQUIRED_JOURNEY_SPECS],
    }
    if any(evidence.get(key) != value for key, value in expected.items()) or "rowInventory" in evidence:
        raise ValueError("Design matrix does not authorize the exact seven-journey wizard gate")
    beta = matrix.get("claimTiers", {}).get("phone_beta", {})
    if (matrix.get("status") != "contract_defined_evidence_pending"
            or beta.get("currentEvidenceStatus") != "pending"
            or beta.get("tabletRequired") is not False or beta.get("rookRequired") is not False):
        raise ValueError("Design phone beta claim boundary differs")
    capabilities = matrix.get("capabilities")
    if not isinstance(capabilities, list) or any(not isinstance(row, dict) for row in capabilities):
        raise ValueError("Design capabilities must be an array of objects")
    advanced = [row for row in capabilities if row.get("id") == "advanced_editor"]
    if (len(advanced) != 1 or advanced[0].get("betaPosture") != "postponed_non_blocking"
            or advanced[0].get("visibility") != "not_in_phone_beta"):
        raise ValueError("Design Full Editing must stay outside phone beta")


def _git(root: Path, *args: str) -> bytes:
    env = {"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C", "GIT_NO_REPLACE_OBJECTS": "1",
           "GIT_CONFIG_NOSYSTEM": "1", "GIT_CONFIG_GLOBAL": "/dev/null", "GIT_TERMINAL_PROMPT": "0"}
    result = subprocess.run(["/usr/bin/git", "-c", "core.fsmonitor=false", "-C", str(root), *args],
                            env=env, capture_output=True, timeout=15, check=False)
    if result.returncode != 0 or len(result.stdout) > MAX_FILE_BYTES:
        raise ValueError("cannot authenticate exact Design checkout")
    return result.stdout


def verify_design_checkout(design_root: Path, *, android_root: Path = REPO_ROOT) -> dict:
    if (not design_root.is_absolute() or design_root.is_symlink()
            or design_root.resolve(strict=True) != design_root or not design_root.is_dir()):
        raise ValueError("Design root must be absolute, canonical and non-symlinked")
    expected = load_policy_pin(android_root=android_root)
    if _git(android_root, "rev-parse", "--show-toplevel").strip().decode() != str(android_root):
        raise ValueError("Android policy root is not its own checkout")
    # These are executable Android admission inputs, not arbitrary caller JSON.
    # Compare tracked bytes even when index flags hide a local modification.
    for relative in (PIN_PATH, CONTRACT_RELATIVE_PATH):
        entry = _git(android_root, "ls-tree", "HEAD", "--", relative.as_posix()).decode().strip()
        if not re.fullmatch(r"100(?:644|755) blob [0-9a-f]{40}\t" + re.escape(relative.as_posix()), entry):
            raise ValueError("Android policy input is not a tracked regular blob")
        if _read(android_root / relative) != _git(android_root, "show", f"HEAD:{relative.as_posix()}"):
            raise ValueError("Android policy input differs from its exact HEAD blob")
    design = expected["design"]
    def identity() -> tuple:
        return (_git(design_root, "rev-parse", "HEAD").strip().decode(),
                _git(design_root, "rev-parse", "HEAD^{tree}").strip().decode(),
                _git(design_root, "remote", "get-url", "origin").strip().decode(),
                _git(design_root, "status", "--porcelain=v1", "--untracked-files=no"))
    first = identity()
    remotes = {f"https://github.com/{DESIGN_REPOSITORY}", f"https://github.com/{DESIGN_REPOSITORY}.git",
               f"git@github.com:{DESIGN_REPOSITORY}.git"}
    if first[0] != design["commit"] or first[1] != design["tree"] or first[2] not in remotes or first[3]:
        raise ValueError("Design checkout head/tree/remote/clean-state differs from Android policy pin")
    captured = {}
    for field in ("matrix", "validator"):
        relative = design[field]["path"]
        entry = _git(design_root, "ls-tree", "HEAD", "--", relative).decode().strip()
        if not re.fullmatch(r"100(?:644|755) blob [0-9a-f]{40}\t" + re.escape(relative), entry):
            raise ValueError("Design policy member is not a tracked regular blob")
        raw = _read(design_root / relative)
        if (raw != _git(design_root, "show", f"HEAD:{relative}")
                or hashlib.sha256(raw).hexdigest() != design[field]["sha256"]):
            raise ValueError("Design policy blob bytes differ from pinned authority")
        captured[field] = raw
    validate_matrix(_json(captured["matrix"]), design)
    if first != identity() or expected != load_policy_pin(android_root=android_root):
        raise ValueError("Design policy authority changed during verification")
    for field, raw in captured.items():
        if raw != _read(design_root / design[field]["path"]):
            raise ValueError("Design policy member changed after verification")
    return expected


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--android-root", type=Path, default=REPO_ROOT)
    parser.add_argument("--design-root", type=Path, required=True)
    args = parser.parse_args()
    value = verify_design_checkout(args.design_root.absolute(), android_root=args.android_root.absolute())
    print(json.dumps({"status": "pass", "policyAuthorities": value, "publicationAuthorized": False}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
