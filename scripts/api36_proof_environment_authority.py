#!/usr/bin/env python3
"""Fail-closed API-36 hosted build and journey environment authority."""

from __future__ import annotations

import hashlib
import json
import os
import platform
import re
import stat
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Mapping, Sequence

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

from api36_wizard_gate_contract import contract_binding, journey_map


POLICY_SCHEMA = "chummer.android.api36-proof-environment-authority/v2"
BUILD_SCHEMA = "chummer.android.api36-build-environment-receipt/v1"
JOURNEY_SCHEMA = "chummer.android.api36-journey-environment-receipt/v1"
DEFAULT_POLICY = Path(__file__).resolve().parents[1] / "eng/api36-proof-environment-authority.json"
SHA256 = re.compile(r"^[0-9a-f]{64}$")
SAFE_TOKEN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._+;:\-]{0,255}$")
JAVA_VERSION = re.compile(
    r'^(?:openjdk|java) version "(?P<version>[0-9]+(?:\.[0-9]+){1,3})"',
    re.MULTILINE,
)
JAVAC_VERSION = re.compile(
    r"^javac (?P<version>[0-9]+(?:\.[0-9]+){1,3})$",
    re.MULTILINE,
)
DOTNET_RID = re.compile(r"^\s*RID:\s*(?P<rid>[^\s]+)\s*$", re.MULTILINE)
ADB_PROTOCOL_VERSION = re.compile(
    r"^Android Debug Bridge version (?P<version>[0-9.]+)$",
    re.MULTILINE,
)
ADB_PACKAGE_VERSION = re.compile(r"^Version (?P<version>[^\s]+)$", re.MULTILINE)
EMULATOR_VERSION = re.compile(
    r"Android emulator version (?P<version>[0-9.]+)",
    re.IGNORECASE,
)
DOES_NOT_ASSERT = (
    "journey_pass_without_the_bound_journey_receipt",
    "physical_device_execution",
    "full_editing_pass",
    "tablet_readiness",
    "google_play_upload",
    "public_release_readiness",
    "publication_authority",
)


def object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def canonical_json(value: Any) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def canonical_sha256(value: Any) -> str:
    return hashlib.sha256(canonical_json(value)).hexdigest()


class StableFile:
    """Capture one regular file and reject symlinks, identity drift, or byte drift."""

    def __init__(self, path: Path, label: str) -> None:
        candidate = path.absolute()
        try:
            resolved = candidate.resolve(strict=True)
        except (OSError, RuntimeError) as error:
            raise ValueError(f"{label} is missing or has an unsafe path") from error
        if resolved != candidate or candidate.is_symlink():
            raise ValueError(f"{label} path must contain no symlink component")
        flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
        try:
            descriptor = os.open(candidate, flags)
        except OSError as error:
            raise ValueError(f"{label} cannot be opened safely") from error
        try:
            before = os.fstat(descriptor)
            if not stat.S_ISREG(before.st_mode):
                raise ValueError(f"{label} must be one regular file")
            chunks: list[bytes] = []
            while chunk := os.read(descriptor, 1024 * 1024):
                chunks.append(chunk)
            after = os.fstat(descriptor)
        finally:
            os.close(descriptor)
        self.path = candidate
        self.label = label
        self.data = b"".join(chunks)
        self.identity = self._identity(before)
        if self.identity != self._identity(after) or len(self.data) != before.st_size:
            raise ValueError(f"{label} changed while it was captured")
        self.sha256 = hashlib.sha256(self.data).hexdigest()
        self.size = len(self.data)

    @staticmethod
    def _identity(value: os.stat_result) -> tuple[int, ...]:
        return (
            value.st_dev,
            value.st_ino,
            value.st_mode,
            value.st_size,
            value.st_mtime_ns,
            value.st_ctime_ns,
        )

    def recheck(self) -> None:
        current = StableFile(self.path, self.label)
        if (
            current.sha256 != self.sha256
            or current.size != self.size
            or current.identity != self.identity
        ):
            raise ValueError(f"{self.label} changed before receipt seal")

    def json(self) -> dict[str, Any]:
        try:
            value = json.loads(self.data, object_pairs_hook=object_without_duplicates)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError(f"{self.label} is not readable JSON") from error
        if not isinstance(value, dict):
            raise ValueError(f"{self.label} root must be an object")
        return value


def require_exact_fields(value: Any, fields: set[str], label: str) -> None:
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be an object")
    if set(value) != fields:
        raise ValueError(f"{label} fields differ: {sorted(set(value) ^ fields)!r}")


def _safe_token(value: Any, label: str) -> str:
    if not isinstance(value, str) or SAFE_TOKEN.fullmatch(value) is None:
        raise ValueError(f"{label} is missing or unsafe")
    return value


def load_policy(snapshot: StableFile) -> dict[str, Any]:
    value = snapshot.json()
    require_exact_fields(
        value,
        {
            "schema",
            "authorityClass",
            "proofScope",
            "apiLevel",
            "requiredRunner",
            "requiredJavaMajor",
            "requiredDotnetSdkVersion",
            "roles",
            "requireLinuxKernel",
            "publicationAuthorized",
            "doesNotAssert",
        },
        "proof environment policy",
    )
    if value["schema"] != POLICY_SCHEMA or value["apiLevel"] != 36:
        raise ValueError("proof environment policy schema or API level differs")
    if value["publicationAuthorized"] is not False:
        raise ValueError("proof environment policy cannot authorize publication")
    if value["doesNotAssert"] != list(DOES_NOT_ASSERT):
        raise ValueError("proof environment policy exclusions differ")
    require_exact_fields(
        value["requiredRunner"],
        {"runnerOs", "runnerArch", "imageOs", "requireImageVersion"},
        "required runner policy",
    )
    if value["requiredRunner"]["requireImageVersion"] is not True:
        raise ValueError("hosted proof policy must require an image version")
    if type(value["requiredJavaMajor"]) is not int or value["requiredJavaMajor"] <= 0:
        raise ValueError("required Java major must be a positive integer")
    _safe_token(value["requiredDotnetSdkVersion"], "required dotnet SDK version")
    roles = value["roles"]
    if not isinstance(roles, dict) or set(roles) != {"build", "journey"}:
        raise ValueError("environment policy roles differ")
    for role, rule in roles.items():
        if not isinstance(rule, dict):
            raise ValueError(f"{role} environment role is not an object")
        require_exact_fields(
            rule,
            {
                "requiredAndroidPackages",
                "requiredAndroidPackagePrefixes",
                "requireKvmDevice",
            },
            f"{role} environment role",
        )
        for field in ("requiredAndroidPackages", "requiredAndroidPackagePrefixes"):
            rows = rule[field]
            if (
                not isinstance(rows, list)
                or not rows
                or rows != sorted(set(rows))
                or any(not isinstance(item, str) or SAFE_TOKEN.fullmatch(item) is None for item in rows)
            ):
                raise ValueError(f"{role} {field} must be sorted unique safe tokens")
        if type(rule["requireKvmDevice"]) is not bool:
            raise ValueError(f"{role} requireKvmDevice must be boolean")
    if type(value["requireLinuxKernel"]) is not bool:
        raise ValueError("requireLinuxKernel must be boolean")
    return value


def parse_sdkmanager_inventory(output: str) -> list[dict[str, str]]:
    installed: dict[str, dict[str, str]] = {}
    in_installed = False
    for raw_line in output.splitlines():
        line = raw_line.strip()
        if line == "Installed packages:":
            in_installed = True
            continue
        if in_installed and line.endswith("packages:") and line != "Installed packages:":
            break
        if not in_installed or not line or line.startswith("Path") or set(line) <= {"-", "|", " "}:
            continue
        columns = [column.strip() for column in line.split("|")]
        if len(columns) < 2:
            continue
        package, version = columns[:2]
        if SAFE_TOKEN.fullmatch(package) is None or SAFE_TOKEN.fullmatch(version) is None:
            raise ValueError(f"sdkmanager emitted an unsafe package row: {line!r}")
        if package in installed:
            raise ValueError(f"sdkmanager emitted duplicate installed package {package!r}")
        installed[package] = {"package": package, "version": version}
        if len(installed) > 512:
            raise ValueError("sdkmanager installed package inventory is unexpectedly large")
    if not installed:
        raise ValueError("sdkmanager installed package inventory is empty")
    return [installed[key] for key in sorted(installed)]


def _run(command: Sequence[str], *, timeout: int = 60) -> str:
    allowed_environment = {
        key: value
        for key, value in os.environ.items()
        if key
        in {
            "PATH",
            "HOME",
            "JAVA_HOME",
            "DOTNET_ROOT",
            "DOTNET_CLI_HOME",
            "ANDROID_HOME",
            "ANDROID_SDK_ROOT",
        }
    }
    allowed_environment.update({"LANG": "C", "LC_ALL": "C"})
    completed = subprocess.run(
        list(command),
        check=True,
        capture_output=True,
        text=True,
        timeout=timeout,
        env=allowed_environment,
    )
    output = (completed.stdout + "\n" + completed.stderr).strip()
    if not output or len(output.encode("utf-8")) > 4 * 1024 * 1024:
        raise ValueError(f"command emitted empty or oversized output: {command[0]}")
    return output


def collect_environment(
    android_sdk_root: Path,
    environment: Mapping[str, str],
    *,
    command_runner: Callable[[Sequence[str]], str] = _run,
    kvm_path: Path = Path("/dev/kvm"),
    kvm_module_path: Path = Path("/sys/module/kvm"),
    proc_version_path: Path = Path("/proc/version"),
    uname_provider: Callable[[], Any] = platform.uname,
) -> dict[str, Any]:
    runner = {
        "runnerOs": environment.get("RUNNER_OS", ""),
        "runnerArch": environment.get("RUNNER_ARCH", ""),
        "imageOs": environment.get("ImageOS", ""),
        "imageVersion": environment.get("ImageVersion", ""),
    }
    java_output = command_runner(("java", "-version"))
    javac_output = command_runner(("javac", "-version"))
    dotnet_version_output = command_runner(("dotnet", "--version"))
    dotnet_info_output = command_runner(("dotnet", "--info"))

    sdkmanager = android_sdk_root / "cmdline-tools/latest/bin/sdkmanager"
    adb = android_sdk_root / "platform-tools/adb"
    emulator = android_sdk_root / "emulator/emulator"
    for path, label in ((sdkmanager, "sdkmanager"), (adb, "adb"), (emulator, "emulator")):
        if path.is_symlink() or not path.is_file() or not os.access(path, os.X_OK):
            raise ValueError(f"{label} is not one executable regular file under Android SDK root")
    sdkmanager_output = command_runner((str(sdkmanager), "--list_installed"))
    adb_output = command_runner((str(adb), "version"))
    emulator_output = command_runner((str(emulator), "-version"))

    java_match = JAVA_VERSION.search(java_output)
    javac_match = JAVAC_VERSION.search(javac_output)
    rid_match = DOTNET_RID.search(dotnet_info_output)
    adb_protocol = ADB_PROTOCOL_VERSION.search(adb_output)
    adb_package = ADB_PACKAGE_VERSION.search(adb_output)
    emulator_version = EMULATOR_VERSION.search(emulator_output)
    if None in (java_match, javac_match, rid_match, adb_protocol, adb_package, emulator_version):
        raise ValueError("one or more hosted tool versions could not be parsed")
    dotnet_version = dotnet_version_output.strip()
    _safe_token(dotnet_version, "dotnet SDK version")
    uname = uname_provider()
    proc_version = StableFile(proc_version_path, "kernel version")
    try:
        kvm_stat = kvm_path.stat()
    except FileNotFoundError:
        kvm_stat = None
    return {
        "runnerImage": runner,
        "java": {
            "runtimeVersion": java_match.group("version"),
            "compilerVersion": javac_match.group("version"),
            "versionOutputSha256": hashlib.sha256(java_output.encode()).hexdigest(),
            "compilerOutputSha256": hashlib.sha256(javac_output.encode()).hexdigest(),
        },
        "dotnet": {
            "sdkVersion": dotnet_version,
            "runtimeIdentifier": rid_match.group("rid"),
            "infoOutputSha256": hashlib.sha256(dotnet_info_output.encode()).hexdigest(),
        },
        "androidSdk": {
            "installedPackages": parse_sdkmanager_inventory(sdkmanager_output),
            "inventoryOutputSha256": hashlib.sha256(sdkmanager_output.encode()).hexdigest(),
            "adb": {
                "protocolVersion": adb_protocol.group("version"),
                "packageVersion": adb_package.group("version"),
                "versionOutputSha256": hashlib.sha256(adb_output.encode()).hexdigest(),
            },
            "emulator": {
                "version": emulator_version.group("version"),
                "versionOutputSha256": hashlib.sha256(emulator_output.encode()).hexdigest(),
            },
        },
        "kernel": {
            "system": uname.system,
            "release": uname.release,
            "machine": uname.machine,
            "procVersionSha256": proc_version.sha256,
        },
        "kvm": {
            "devicePresent": kvm_stat is not None,
            "characterDevice": bool(kvm_stat is not None and stat.S_ISCHR(kvm_stat.st_mode)),
            "readable": os.access(kvm_path, os.R_OK),
            "writable": os.access(kvm_path, os.W_OK),
            "kernelModulePresent": kvm_module_path.is_dir(),
        },
    }


def validate_environment(
    observation: dict[str, Any],
    policy: dict[str, Any],
    role: str,
) -> dict[str, Any]:
    if role not in {"build", "journey"}:
        raise ValueError("environment receipt role differs")
    require_exact_fields(
        observation,
        {"runnerImage", "java", "dotnet", "androidSdk", "kernel", "kvm"},
        "environment observation",
    )
    runner = observation["runnerImage"]
    require_exact_fields(runner, {"runnerOs", "runnerArch", "imageOs", "imageVersion"}, "runner image")
    required_runner = policy["requiredRunner"]
    for field in ("runnerOs", "runnerArch", "imageOs"):
        if runner[field] != required_runner[field]:
            raise ValueError(f"runner image authority differs for {field}")
    _safe_token(runner["imageVersion"], "runner image version")

    java = observation["java"]
    require_exact_fields(
        java,
        {"runtimeVersion", "compilerVersion", "versionOutputSha256", "compilerOutputSha256"},
        "Java observation",
    )
    for field in ("runtimeVersion", "compilerVersion"):
        _safe_token(java[field], f"Java {field}")
        if java[field].split(".", 1)[0] != str(policy["requiredJavaMajor"]):
            raise ValueError("Java runtime/compiler major differs from proof policy")

    dotnet = observation["dotnet"]
    require_exact_fields(dotnet, {"sdkVersion", "runtimeIdentifier", "infoOutputSha256"}, "dotnet observation")
    if dotnet["sdkVersion"] != policy["requiredDotnetSdkVersion"]:
        raise ValueError("dotnet SDK version differs from proof policy")
    _safe_token(dotnet["runtimeIdentifier"], "dotnet runtime identifier")

    android = observation["androidSdk"]
    require_exact_fields(android, {"installedPackages", "inventoryOutputSha256", "adb", "emulator"}, "Android SDK")
    packages = android["installedPackages"]
    if not isinstance(packages, list) or not packages:
        raise ValueError("Android installed package inventory is missing")
    by_name: dict[str, dict[str, str]] = {}
    for row in packages:
        if not isinstance(row, dict):
            raise ValueError("Android installed package row is not an object")
        require_exact_fields(row, {"package", "version"}, "Android installed package row")
        package = _safe_token(row["package"], "Android package")
        _safe_token(row["version"], "Android package version")
        if package in by_name:
            raise ValueError(f"Android installed package is ambiguous: {package!r}")
        by_name[package] = row
    role_policy = policy["roles"][role]
    missing = sorted(set(role_policy["requiredAndroidPackages"]) - set(by_name))
    if missing:
        raise ValueError(f"required Android packages are missing: {missing!r}")
    for prefix in role_policy["requiredAndroidPackagePrefixes"]:
        if not any(package.startswith(prefix) for package in by_name):
            raise ValueError(f"required Android package family is missing: {prefix!r}")

    require_exact_fields(
        android["adb"],
        {"protocolVersion", "packageVersion", "versionOutputSha256"},
        "ADB observation",
    )
    require_exact_fields(
        android["emulator"],
        {"version", "versionOutputSha256"},
        "emulator observation",
    )
    for field in ("protocolVersion", "packageVersion"):
        _safe_token(android["adb"][field], f"ADB {field}")
    _safe_token(android["emulator"]["version"], "emulator version")

    kernel = observation["kernel"]
    require_exact_fields(kernel, {"system", "release", "machine", "procVersionSha256"}, "kernel observation")
    if policy["requireLinuxKernel"] and kernel["system"] != "Linux":
        raise ValueError("proof environment is not a Linux kernel")
    _safe_token(kernel["release"], "kernel release")
    _safe_token(kernel["machine"], "kernel machine")
    kvm = observation["kvm"]
    require_exact_fields(
        kvm,
        {"devicePresent", "characterDevice", "readable", "writable", "kernelModulePresent"},
        "KVM observation",
    )
    if any(type(value) is not bool for value in kvm.values()):
        raise ValueError("KVM posture must contain only JSON booleans")
    if role_policy["requireKvmDevice"] and not all(kvm.values()):
        raise ValueError("journey proof requires a usable KVM device and module")
    for digest in _digest_values(observation):
        if SHA256.fullmatch(digest) is None:
            raise ValueError("environment command digest is not canonical SHA-256")
    return observation


def _digest_values(value: Any) -> list[str]:
    result: list[str] = []
    if isinstance(value, dict):
        for key, item in value.items():
            if key.endswith("Sha256"):
                if not isinstance(item, str):
                    raise ValueError(f"{key} must be a string")
                result.append(item)
            else:
                result.extend(_digest_values(item))
    elif isinstance(value, list):
        for item in value:
            result.extend(_digest_values(item))
    return result


def compatibility_observation(
    observation: dict[str, Any],
    policy: dict[str, Any],
    role: str,
) -> dict[str, Any]:
    validate_environment(observation, policy, role)
    role_policy = policy["roles"][role]
    packages = {
        row["package"]: row["version"]
        for row in observation["androidSdk"]["installedPackages"]
        if row["package"] in role_policy["requiredAndroidPackages"]
        or any(
            row["package"].startswith(prefix)
            for prefix in role_policy["requiredAndroidPackagePrefixes"]
        )
    }
    return {
        "role": role,
        "runner": {
            field: observation["runnerImage"][field]
            for field in ("runnerOs", "runnerArch", "imageOs")
        },
        "java": {
            field: observation["java"][field]
            for field in ("runtimeVersion", "compilerVersion")
        },
        "dotnet": {
            field: observation["dotnet"][field]
            for field in ("sdkVersion", "runtimeIdentifier")
        },
        "androidPackages": packages,
        "adb": {
            field: observation["androidSdk"]["adb"][field]
            for field in ("protocolVersion", "packageVersion")
        },
        "emulatorVersion": observation["androidSdk"]["emulator"]["version"],
        "kernel": {
            field: observation["kernel"][field]
            for field in ("system", "machine")
        },
        "kvm": dict(observation["kvm"]),
    }


def policy_binding(snapshot: StableFile) -> dict[str, Any]:
    policy = load_policy(snapshot)
    return {
        "schema": policy["schema"],
        "sha256": snapshot.sha256,
        "sizeBytes": snapshot.size,
    }


def validate_subject(role: str, subject: dict[str, Any]) -> dict[str, Any]:
    if role == "journey":
        require_exact_fields(
            subject,
            {
                "matrixJourney",
                "driverJourney",
                "receiptSchema",
                "journeyReceiptSha256",
                "journeyReceiptSizeBytes",
                "apkSha256",
                "apkSizeBytes",
                "artifactAuthoritySha256",
            },
            "journey environment subject",
        )
        expected = journey_map().get(subject["matrixJourney"])
        if expected != (subject["driverJourney"], subject["receiptSchema"]):
            raise ValueError("journey environment subject route differs")
        digest_fields = (
            "journeyReceiptSha256",
            "apkSha256",
            "artifactAuthoritySha256",
        )
        size_fields = ("journeyReceiptSizeBytes", "apkSizeBytes")
    elif role == "build":
        require_exact_fields(
            subject,
            {"x64Apk", "arm64Apk", "hostedCandidate", "workflow"},
            "build environment subject",
        )
        for field in ("x64Apk", "arm64Apk"):
            require_exact_fields(
                subject[field],
                {"sha256", "sizeBytes"},
                f"build environment {field}",
            )
        require_exact_fields(
            subject["hostedCandidate"],
            {"schema", "sha256", "sizeBytes"},
            "build environment hosted candidate",
        )
        if (
            subject["hostedCandidate"]["schema"]
            != "chummer.android.api36-arm64-hosted-debug-candidate/v1"
        ):
            raise ValueError("build environment hosted candidate schema differs")
        require_exact_fields(
            subject["workflow"],
            {"sha256", "sizeBytes"},
            "build environment workflow",
        )
        digest_fields = ()
        size_fields = ()
        for binding in subject.values():
            if SHA256.fullmatch(str(binding["sha256"])) is None:
                raise ValueError("build environment subject digest differs")
            if type(binding["sizeBytes"]) is not int or binding["sizeBytes"] <= 0:
                raise ValueError("build environment subject size differs")
        return subject
    else:
        raise ValueError("environment receipt role differs")
    for field in digest_fields:
        if SHA256.fullmatch(str(subject[field])) is None:
            raise ValueError(f"journey environment {field} differs")
    for field in size_fields:
        if type(subject[field]) is not int or subject[field] <= 0:
            raise ValueError(f"journey environment {field} differs")
    return subject


def base_receipt(
    *,
    role: str,
    policy: dict[str, Any],
    policy_snapshot: StableFile,
    gate_authority: dict[str, Any],
    subject_authority: dict[str, Any],
    observation: dict[str, Any],
) -> dict[str, Any]:
    validated = validate_environment(observation, policy, role)
    validated_subject = validate_subject(role, subject_authority)
    compatibility = compatibility_observation(validated, policy, role)
    receipt = {
        "schema": BUILD_SCHEMA if role == "build" else JOURNEY_SCHEMA,
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "receiptRole": role,
        "authorityClass": policy["authorityClass"],
        "proofScope": policy["proofScope"],
        "publicationAuthorized": False,
        "policyAuthority": policy_binding(policy_snapshot),
        "gateAuthority": gate_authority,
        "subjectAuthority": validated_subject,
        "environment": validated,
        "environmentSha256": canonical_sha256(validated),
        "compatibility": compatibility,
        "compatibilitySha256": canonical_sha256(compatibility),
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    receipt["receiptSha256"] = canonical_sha256({**receipt, "receiptSha256": None})
    validate_receipt(receipt, policy)
    return receipt


def validate_receipt(receipt: dict[str, Any], policy: dict[str, Any]) -> None:
    require_exact_fields(
        receipt,
        {
            "schema",
            "status",
            "generatedAtUtc",
            "receiptRole",
            "authorityClass",
            "proofScope",
            "publicationAuthorized",
            "policyAuthority",
            "gateAuthority",
            "subjectAuthority",
            "environment",
            "environmentSha256",
            "compatibility",
            "compatibilitySha256",
            "doesNotAssert",
            "receiptSha256",
        },
        "environment receipt",
    )
    role = receipt["receiptRole"]
    expected_schema = BUILD_SCHEMA if role == "build" else JOURNEY_SCHEMA if role == "journey" else None
    if expected_schema is None or receipt["schema"] != expected_schema or receipt["status"] != "pass":
        raise ValueError("environment receipt schema, role, or status differs")
    if (
        receipt["authorityClass"] != policy["authorityClass"]
        or receipt["proofScope"] != policy["proofScope"]
        or receipt["publicationAuthorized"] is not False
        or receipt["doesNotAssert"] != list(DOES_NOT_ASSERT)
    ):
        raise ValueError("environment receipt boundary differs")
    generated_at = receipt["generatedAtUtc"]
    if not isinstance(generated_at, str):
        raise ValueError("environment receipt timestamp differs")
    try:
        generated = datetime.fromisoformat(generated_at)
    except ValueError as error:
        raise ValueError("environment receipt timestamp differs") from error
    if generated.tzinfo is None or generated.utcoffset() != timezone.utc.utcoffset(generated):
        raise ValueError("environment receipt timestamp must be UTC")
    validate_environment(receipt["environment"], policy, role)
    expected_compatibility = compatibility_observation(receipt["environment"], policy, role)
    if receipt["environmentSha256"] != canonical_sha256(receipt["environment"]):
        raise ValueError("environment digest differs")
    if (
        receipt["compatibility"] != expected_compatibility
        or receipt["compatibilitySha256"] != canonical_sha256(expected_compatibility)
    ):
        raise ValueError("environment compatibility digest differs")
    expected_policy = receipt["policyAuthority"]
    require_exact_fields(expected_policy, {"schema", "sha256", "sizeBytes"}, "policy authority")
    if (
        expected_policy["schema"] != POLICY_SCHEMA
        or SHA256.fullmatch(str(expected_policy["sha256"])) is None
        or type(expected_policy["sizeBytes"]) is not int
        or expected_policy["sizeBytes"] <= 0
    ):
        raise ValueError("environment policy authority differs")
    if not isinstance(receipt["gateAuthority"], dict):
        raise ValueError("wizard gate authority is missing")
    if not isinstance(receipt["subjectAuthority"], dict):
        raise ValueError("environment subject authority is missing")
    validate_subject(role, receipt["subjectAuthority"])
    if SHA256.fullmatch(str(receipt["receiptSha256"])) is None:
        raise ValueError("environment receipt digest is not canonical")
    if receipt["receiptSha256"] != canonical_sha256({**receipt, "receiptSha256": None}):
        raise ValueError("environment receipt digest differs")


def journey_subject(
    *,
    journey_snapshot: StableFile,
    matrix_journey: str,
    apk_snapshot: StableFile,
    expected_apk_sha256: str,
    gate_authority: dict[str, Any],
) -> dict[str, Any]:
    expected = journey_map().get(matrix_journey)
    if expected is None:
        raise ValueError(f"matrix journey is not approved: {matrix_journey!r}")
    driver_journey, receipt_schema = expected
    journey = journey_snapshot.json()
    if (
        journey.get("schema") != receipt_schema
        or journey.get("status") != "pass"
        or journey.get("matrixJourney") != matrix_journey
        or journey.get("driverJourney") != driver_journey
        or journey.get("publicationAuthorized", False) is not False
        or journey.get("gateAuthority") != gate_authority
    ):
        raise ValueError("journey receipt authority differs")
    if apk_snapshot.sha256 != expected_apk_sha256 or journey.get("apkSha256") != expected_apk_sha256:
        raise ValueError("journey APK authority differs")
    artifact = journey.get("artifactAuthority")
    if not isinstance(artifact, dict) or artifact.get("apkSha256") != expected_apk_sha256:
        raise ValueError("journey artifact authority differs")
    return {
        "matrixJourney": matrix_journey,
        "driverJourney": driver_journey,
        "receiptSchema": receipt_schema,
        "journeyReceiptSha256": journey_snapshot.sha256,
        "journeyReceiptSizeBytes": journey_snapshot.size,
        "apkSha256": apk_snapshot.sha256,
        "apkSizeBytes": apk_snapshot.size,
        "artifactAuthoritySha256": canonical_sha256(artifact),
    }


def build_subject(
    *,
    x64_apk: StableFile,
    arm64_apk: StableFile,
    hosted_candidate: StableFile,
    workflow: StableFile,
) -> dict[str, Any]:
    candidate = hosted_candidate.json()
    if (
        candidate.get("contractName")
        != "chummer.android.api36-arm64-hosted-debug-candidate/v1"
        or candidate.get("status") != "candidate"
        or candidate.get("publicationAuthorized") is not False
        or candidate.get("releaseEligible") is not False
        or candidate.get("releaseAttested") is not False
    ):
        raise ValueError("hosted ARM64 candidate authority differs")
    artifact = candidate.get("artifact")
    if not isinstance(artifact, dict) or artifact.get("sha256") != arm64_apk.sha256:
        raise ValueError("hosted ARM64 candidate does not bind the exact ARM64 APK")
    return {
        "x64Apk": {"sha256": x64_apk.sha256, "sizeBytes": x64_apk.size},
        "arm64Apk": {"sha256": arm64_apk.sha256, "sizeBytes": arm64_apk.size},
        "hostedCandidate": {
            "schema": candidate["contractName"],
            "sha256": hosted_candidate.sha256,
            "sizeBytes": hosted_candidate.size,
        },
        "workflow": {"sha256": workflow.sha256, "sizeBytes": workflow.size},
    }


def write_atomically(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_symlink() or (path.exists() and not path.is_file()):
        raise ValueError("environment receipt output is not a regular file target")
    encoded = json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False).encode() + b"\n"
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=path.parent,
            prefix=f".{path.name}.",
            delete=False,
        ) as stream:
            temporary = stream.name
            os.fchmod(stream.fileno(), 0o644)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        temporary = None
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)
