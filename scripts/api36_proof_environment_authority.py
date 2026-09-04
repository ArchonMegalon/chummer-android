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
BUILD_SCHEMA = "chummer.android.api36-build-environment-receipt/v2"
JOURNEY_SCHEMA = "chummer.android.api36-journey-environment-receipt/v2"
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
EMULATOR_VERSION_HEADER = re.compile(
    r"^(?:INFO\s+\|\s+)?Android emulator version "
    r"(?P<version>[0-9]+(?:\.[0-9]+){2,3}) "
    r"\(build_id (?P<build_id>[1-9][0-9]*)\) "
    r"\(CL:(?:N/A|[0-9]+)\)$",
)
EMULATOR_NUMERIC_VERSION = re.compile(r"^[0-9]+(?:\.[0-9]+){2,3}$")
EMULATOR_LIVE_OBSERVATION_SCHEMA = (
    "chummer.android.api36-emulator-live-observation/v1"
)
EMULATOR_LIVE_LOG_MAX_PREFIX_BYTES = 64 * 1024
EMULATOR_LIVE_LOG_NAME = "chummer-api36-emulator-live.log"
EMULATOR_LAUNCHER_RELATIVE_PATH = "emulator/emulator"
EMULATOR_AVD_NAME = "test"
EMULATOR_SERIAL = "emulator-5554"
EMULATOR_PORT = 5554
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


def stable_virtual_file_bytes(
    path: Path,
    label: str,
    *,
    maximum_bytes: int = 64 * 1024,
) -> bytes:
    """Double-read a bounded virtual regular file whose stat size may be zero."""
    candidate = path.absolute()
    try:
        resolved = candidate.resolve(strict=True)
    except (OSError, RuntimeError) as error:
        raise ValueError(f"{label} is missing or has an unsafe path") from error
    if resolved != candidate or candidate.is_symlink() or maximum_bytes <= 0:
        raise ValueError(f"{label} path must contain no symlink component")

    def read_once() -> bytes:
        flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
        descriptor = os.open(candidate, flags)
        try:
            metadata = os.fstat(descriptor)
            if not stat.S_ISREG(metadata.st_mode):
                raise ValueError(f"{label} must be one virtual regular file")
            chunks: list[bytes] = []
            remaining = maximum_bytes + 1
            while remaining > 0:
                chunk = os.read(descriptor, min(8192, remaining))
                if not chunk:
                    break
                chunks.append(chunk)
                remaining -= len(chunk)
        finally:
            os.close(descriptor)
        result = b"".join(chunks)
        if not result or len(result) > maximum_bytes:
            raise ValueError(f"{label} is empty or exceeds its bounded read")
        return result

    first = read_once()
    second = read_once()
    if first != second:
        raise ValueError(f"{label} changed between bounded reads")
    return first


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


def parse_emulator_version_prefix(prefix: bytes) -> dict[str, Any]:
    """Parse one exact official emulator header from a bounded stable prefix."""
    if not prefix or len(prefix) > EMULATOR_LIVE_LOG_MAX_PREFIX_BYTES:
        raise ValueError("emulator live-log prefix is empty or oversized")
    try:
        text = prefix.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ValueError("emulator live-log prefix is not UTF-8") from error
    matches: list[tuple[re.Match[str], bytes]] = []
    for line in text.splitlines():
        match = EMULATOR_VERSION_HEADER.fullmatch(line)
        if match is not None:
            matches.append((match, line.encode("utf-8")))
        elif "android emulator version" in line.lower():
            raise ValueError("emulator live-log prefix contains a malformed header")
    if len(matches) != 1:
        raise ValueError("emulator live-log prefix must contain exactly one official header")
    match, official_line = matches[0]
    return {
        "version": match.group("version"),
        "buildId": int(match.group("build_id")),
        "officialLineSha256": hashlib.sha256(official_line).hexdigest(),
    }


def capture_stable_growing_log_prefix(path: Path) -> tuple[bytes, dict[str, Any]]:
    """Capture a stable prefix while allowing append-only growth after emulator boot."""
    candidate = path.absolute()
    try:
        resolved = candidate.resolve(strict=True)
    except (OSError, RuntimeError) as error:
        raise ValueError("emulator live log is missing or has an unsafe path") from error
    if resolved != candidate or candidate.is_symlink():
        raise ValueError("emulator live-log path must contain no symlink component")
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(candidate, flags)
    except OSError as error:
        raise ValueError("emulator live log cannot be opened safely") from error

    def stable_identity(metadata: os.stat_result) -> tuple[int, ...]:
        return (
            metadata.st_dev,
            metadata.st_ino,
            metadata.st_uid,
            stat.S_IMODE(metadata.st_mode),
            metadata.st_nlink,
        )

    try:
        before = os.fstat(descriptor)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_uid != os.geteuid()
            or stat.S_IMODE(before.st_mode) != 0o600
            or before.st_nlink != 1
            or before.st_size <= 0
        ):
            raise ValueError("emulator live-log identity differs")
        prefix_size = min(before.st_size, EMULATOR_LIVE_LOG_MAX_PREFIX_BYTES)
        first = os.pread(descriptor, prefix_size, 0)
        middle = os.fstat(descriptor)
        second = os.pread(descriptor, prefix_size, 0)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    if (
        len(first) != prefix_size
        or first != second
        or stable_identity(before) != stable_identity(middle)
        or stable_identity(before) != stable_identity(after)
        or middle.st_size < before.st_size
        or after.st_size < middle.st_size
    ):
        raise ValueError("emulator live-log prefix or identity changed during capture")
    try:
        final = os.lstat(candidate)
    except OSError as error:
        raise ValueError("emulator live log disappeared after capture") from error
    if not stat.S_ISREG(final.st_mode) or stable_identity(final) != stable_identity(before):
        raise ValueError("emulator live-log path identity changed during capture")
    return first, {
        "device": before.st_dev,
        "inode": before.st_ino,
        "ownerUid": before.st_uid,
        "mode": "0600",
        "linkCount": before.st_nlink,
    }


def build_emulator_live_observation(
    *,
    live_log_path: Path,
    run_id: int,
    run_attempt: int,
    matrix_journey: str,
) -> dict[str, Any]:
    if (
        type(run_id) is not int
        or run_id <= 0
        or type(run_attempt) is not int
        or run_attempt <= 0
        or matrix_journey not in journey_map()
    ):
        raise ValueError("emulator live observation execution binding differs")
    prefix, identity = capture_stable_growing_log_prefix(live_log_path)
    parsed = parse_emulator_version_prefix(prefix)
    value = {
        "schema": EMULATOR_LIVE_OBSERVATION_SCHEMA,
        "status": "observed",
        "publicationAuthorized": False,
        "execution": {
            "runId": run_id,
            "runAttempt": run_attempt,
            "matrixJourney": matrix_journey,
        },
        "launch": {
            "launcherRelativePath": EMULATOR_LAUNCHER_RELATIVE_PATH,
            "avdName": EMULATOR_AVD_NAME,
            "emulatorSerial": EMULATOR_SERIAL,
            "emulatorPort": EMULATOR_PORT,
        },
        "emulator": parsed,
        "prefix": {
            "sha256": hashlib.sha256(prefix).hexdigest(),
            "sizeBytes": len(prefix),
        },
        "liveLogIdentity": identity,
        "authoritySha256": None,
    }
    value["authoritySha256"] = canonical_sha256(value)
    validate_emulator_live_observation(value)
    return value


def validate_emulator_live_observation(value: dict[str, Any]) -> dict[str, Any]:
    require_exact_fields(
        value,
        {
            "schema",
            "status",
            "publicationAuthorized",
            "execution",
            "launch",
            "emulator",
            "prefix",
            "liveLogIdentity",
            "authoritySha256",
        },
        "emulator live observation",
    )
    if (
        value["schema"] != EMULATOR_LIVE_OBSERVATION_SCHEMA
        or value["status"] != "observed"
        or value["publicationAuthorized"] is not False
    ):
        raise ValueError("emulator live-observation authority differs")
    execution = value["execution"]
    require_exact_fields(execution, {"runId", "runAttempt", "matrixJourney"}, "emulator execution")
    if (
        type(execution["runId"]) is not int
        or execution["runId"] <= 0
        or type(execution["runAttempt"]) is not int
        or execution["runAttempt"] <= 0
        or execution["matrixJourney"] not in journey_map()
    ):
        raise ValueError("emulator live observation execution binding differs")
    launch = value["launch"]
    require_exact_fields(
        launch,
        {"launcherRelativePath", "avdName", "emulatorSerial", "emulatorPort"},
        "emulator launch",
    )
    if launch != {
        "launcherRelativePath": EMULATOR_LAUNCHER_RELATIVE_PATH,
        "avdName": EMULATOR_AVD_NAME,
        "emulatorSerial": EMULATOR_SERIAL,
        "emulatorPort": EMULATOR_PORT,
    }:
        raise ValueError("emulator launch authority differs")
    emulator = value["emulator"]
    require_exact_fields(emulator, {"version", "buildId", "officialLineSha256"}, "emulator version")
    if (
        EMULATOR_NUMERIC_VERSION.fullmatch(str(emulator["version"])) is None
        or type(emulator["buildId"]) is not int
        or emulator["buildId"] <= 0
        or SHA256.fullmatch(str(emulator["officialLineSha256"])) is None
    ):
        raise ValueError("emulator version authority differs")
    prefix = value["prefix"]
    require_exact_fields(prefix, {"sha256", "sizeBytes"}, "emulator prefix")
    if (
        SHA256.fullmatch(str(prefix["sha256"])) is None
        or type(prefix["sizeBytes"]) is not int
        or not 1 <= prefix["sizeBytes"] <= EMULATOR_LIVE_LOG_MAX_PREFIX_BYTES
    ):
        raise ValueError("emulator live-log prefix authority differs")
    identity = value["liveLogIdentity"]
    require_exact_fields(identity, {"device", "inode", "ownerUid", "mode", "linkCount"}, "emulator live-log identity")
    if (
        any(type(identity[field]) is not int or identity[field] < 0 for field in ("device", "inode", "ownerUid"))
        or identity["mode"] != "0600"
        or identity["linkCount"] != 1
    ):
        raise ValueError("emulator live-log identity authority differs")
    if (
        SHA256.fullmatch(str(value["authoritySha256"])) is None
        or value["authoritySha256"]
        != canonical_sha256({**value, "authoritySha256": None})
    ):
        raise ValueError("emulator live observation digest differs")
    return value


def parse_emulator_live_observation(snapshot: StableFile) -> dict[str, Any]:
    if snapshot.size <= 0 or snapshot.size > EMULATOR_LIVE_LOG_MAX_PREFIX_BYTES:
        raise ValueError("emulator live-observation sidecar is empty or oversized")
    value = validate_emulator_live_observation(snapshot.json())
    return {
        "available": True,
        "version": value["emulator"]["version"],
        "buildId": value["emulator"]["buildId"],
        "versionOutputSha256": canonical_sha256(
            {
                "version": value["emulator"]["version"],
                "buildId": value["emulator"]["buildId"],
            }
        ),
        "liveObservation": {
            "schema": value["schema"],
            "sha256": snapshot.sha256,
            "sizeBytes": snapshot.size,
            "authoritySha256": value["authoritySha256"],
            "officialLineSha256": value["emulator"]["officialLineSha256"],
            "prefixSha256": value["prefix"]["sha256"],
            "prefixSizeBytes": value["prefix"]["sizeBytes"],
            "execution": value["execution"],
            "launch": value["launch"],
        },
    }


def emulator_versions_match(package_version: str, observed_version: str) -> bool:
    """Allow equality or one side having exactly one additional trailing .0."""
    if (
        EMULATOR_NUMERIC_VERSION.fullmatch(package_version) is None
        or EMULATOR_NUMERIC_VERSION.fullmatch(observed_version) is None
    ):
        return False

    return (
        package_version == observed_version
        or f"{package_version}.0" == observed_version
        or f"{observed_version}.0" == package_version
    )


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
    try:
        completed = subprocess.run(
            list(command),
            check=True,
            capture_output=True,
            text=True,
            timeout=timeout,
            env=allowed_environment,
        )
    except subprocess.CalledProcessError as error:
        def bounded_diagnostic(value: object) -> str:
            raw = value if isinstance(value, str) else ""
            safe = "".join(
                character
                if character in {"\n", "\r", "\t"} or character.isprintable()
                else "?"
                for character in raw
            )
            encoded = safe.encode("utf-8")
            if len(encoded) > 4096:
                safe = encoded[:4096].decode("utf-8", errors="replace") + "...[truncated]"
            return safe

        raise RuntimeError(
            f"exact tool command failed with exit code {error.returncode}; "
            f"stdout={bounded_diagnostic(error.stdout)!r}; "
            f"stderr={bounded_diagnostic(error.stderr)!r}"
        ) from None
    output = (completed.stdout + "\n" + completed.stderr).strip()
    if not output or len(output.encode("utf-8")) > 4 * 1024 * 1024:
        raise ValueError(f"command emitted empty or oversized output: {command[0]}")
    return output


def _canonical_sdk_root(android_sdk_root: Path) -> Path:
    if not android_sdk_root.is_absolute() or android_sdk_root.is_symlink():
        raise ValueError("Android SDK root must be an absolute canonical directory")
    try:
        resolved = android_sdk_root.resolve(strict=True)
    except (OSError, RuntimeError) as error:
        raise ValueError("Android SDK root is missing or unsafe") from error
    if resolved != android_sdk_root or not android_sdk_root.is_dir():
        raise ValueError("Android SDK root must be an absolute canonical directory")
    return android_sdk_root


def _inside_sdk(path: Path, android_sdk_root: Path, label: str) -> None:
    try:
        path.relative_to(android_sdk_root)
    except ValueError as error:
        raise ValueError(f"{label} symlink escapes the Android SDK root") from error


def sdk_executable(
    android_sdk_root: Path,
    relative_path: str,
    label: str,
    *,
    required: bool,
    allow_internal_file_symlink: bool = False,
) -> Path | None:
    """Resolve one exact SDK executable without consulting PATH.

    SDK directories must be canonical. The emulator's exact file may be an
    SDK-internal symlink chain because hosted Android packages use that layout;
    every hop and the final regular executable must remain below the SDK root.
    After validating the complete chain, return the exact public SDK launcher
    path rather than its final target. Android's emulator launcher derives its
    runtime layout from that public path; invoking the internal target directly
    is not equivalent and may exit before printing its version.
    """
    root = _canonical_sdk_root(android_sdk_root)
    expected = root / relative_path
    _inside_sdk(expected, root, label)
    if not expected.exists() and not expected.is_symlink():
        if required:
            raise ValueError(f"{label} is missing from its exact Android SDK path")
        return None

    current = expected
    visited: set[Path] = set()
    while True:
        if current in visited or len(visited) >= 16:
            raise ValueError(f"{label} has an unsafe or cyclic SDK symlink chain")
        visited.add(current)
        try:
            canonical_parent = current.parent.resolve(strict=True)
        except (OSError, RuntimeError) as error:
            raise ValueError(f"{label} has an unsafe SDK parent") from error
        if canonical_parent != current.parent:
            raise ValueError(f"{label} has a symlinked SDK directory component")
        try:
            metadata = os.lstat(current)
        except OSError as error:
            raise ValueError(f"{label} is missing from its exact Android SDK path") from error
        if stat.S_ISLNK(metadata.st_mode):
            if not allow_internal_file_symlink:
                raise ValueError(f"{label} must not be a symlink")
            raw_target = Path(os.readlink(current))
            target = raw_target if raw_target.is_absolute() else current.parent / raw_target
            current = Path(os.path.abspath(target))
            _inside_sdk(current, root, label)
            continue
        if not stat.S_ISREG(metadata.st_mode) or not os.access(current, os.X_OK):
            raise ValueError(
                f"{label} is not one executable regular file under Android SDK root"
            )
        return expected


def collect_environment(
    android_sdk_root: Path,
    environment: Mapping[str, str],
    *,
    emulator_required: bool = True,
    emulator_live_observation: StableFile | None = None,
    command_runner: Callable[[Sequence[str]], str] = _run,
    kvm_path: Path = Path("/dev/kvm"),
    kvm_module_path: Path = Path("/sys/module/kvm"),
    proc_version_path: Path = Path("/proc/version"),
    uname_provider: Callable[[], Any] = platform.uname,
) -> dict[str, Any]:
    if type(emulator_required) is not bool:
        raise ValueError("emulator-required posture must be boolean")
    if not emulator_required and emulator_live_observation is not None:
        raise ValueError("build environment must not accept an emulator observation")
    android_sdk_root = _canonical_sdk_root(android_sdk_root)
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

    sdkmanager = sdk_executable(
        android_sdk_root,
        "cmdline-tools/latest/bin/sdkmanager",
        "sdkmanager",
        required=True,
    )
    adb = sdk_executable(
        android_sdk_root,
        "platform-tools/adb",
        "adb",
        required=True,
    )
    emulator = (
        sdk_executable(
            android_sdk_root,
            "emulator/emulator",
            "emulator",
            required=True,
            allow_internal_file_symlink=True,
        )
        if emulator_required
        else None
    )
    assert sdkmanager is not None and adb is not None
    sdkmanager_output = command_runner((str(sdkmanager), "--list_installed"))
    adb_output = command_runner((str(adb), "version"))
    installed_packages = parse_sdkmanager_inventory(sdkmanager_output)
    if emulator is None:
        emulator_observation = {
            "available": False,
            "version": None,
            "buildId": None,
            "versionOutputSha256": canonical_sha256({"available": False}),
            "liveObservation": None,
        }
    else:
        if emulator_live_observation is None:
            raise ValueError("journey emulator live-observation sidecar is required")
        emulator_observation = parse_emulator_live_observation(
            emulator_live_observation
        )
        emulator_package = next(
            (row for row in installed_packages if row["package"] == "emulator"),
            None,
        )
        if emulator_package is None or not emulator_versions_match(
            emulator_package["version"], emulator_observation["version"]
        ):
            raise ValueError(
                "observed emulator version differs from sdkmanager package authority"
            )

    java_match = JAVA_VERSION.search(java_output)
    javac_match = JAVAC_VERSION.search(javac_output)
    rid_match = DOTNET_RID.search(dotnet_info_output)
    adb_protocol = ADB_PROTOCOL_VERSION.search(adb_output)
    adb_package = ADB_PACKAGE_VERSION.search(adb_output)
    if None in (java_match, javac_match, rid_match, adb_protocol, adb_package):
        raise ValueError("one or more hosted tool versions could not be parsed")
    dotnet_version = dotnet_version_output.strip()
    _safe_token(dotnet_version, "dotnet SDK version")
    uname = uname_provider()
    proc_version = stable_virtual_file_bytes(proc_version_path, "kernel version")
    try:
        kvm_stat = kvm_path.stat()
    except FileNotFoundError:
        kvm_stat = None
    return {
        "runnerImage": runner,
        "java": {
            "runtimeVersion": java_match.group("version"),
            "compilerVersion": javac_match.group("version"),
            "versionOutputSha256": canonical_sha256(
                {"runtimeVersion": java_match.group("version")}
            ),
            "compilerOutputSha256": canonical_sha256(
                {"compilerVersion": javac_match.group("version")}
            ),
        },
        "dotnet": {
            "sdkVersion": dotnet_version,
            "runtimeIdentifier": rid_match.group("rid"),
            "infoOutputSha256": canonical_sha256(
                {
                    "sdkVersion": dotnet_version,
                    "runtimeIdentifier": rid_match.group("rid"),
                }
            ),
        },
        "androidSdk": {
            "installedPackages": installed_packages,
            "inventoryOutputSha256": canonical_sha256(installed_packages),
            "adb": {
                "protocolVersion": adb_protocol.group("version"),
                "packageVersion": adb_package.group("version"),
                "versionOutputSha256": canonical_sha256(
                    {
                        "protocolVersion": adb_protocol.group("version"),
                        "packageVersion": adb_package.group("version"),
                    }
                ),
            },
            "emulator": emulator_observation,
        },
        "kernel": {
            "system": uname.system,
            "release": uname.release,
            "machine": uname.machine,
            "procVersionSha256": hashlib.sha256(proc_version).hexdigest(),
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
    if (
        java["versionOutputSha256"]
        != canonical_sha256({"runtimeVersion": java["runtimeVersion"]})
        or java["compilerOutputSha256"]
        != canonical_sha256({"compilerVersion": java["compilerVersion"]})
    ):
        raise ValueError("Java canonical output digest differs")

    dotnet = observation["dotnet"]
    require_exact_fields(dotnet, {"sdkVersion", "runtimeIdentifier", "infoOutputSha256"}, "dotnet observation")
    if dotnet["sdkVersion"] != policy["requiredDotnetSdkVersion"]:
        raise ValueError("dotnet SDK version differs from proof policy")
    _safe_token(dotnet["runtimeIdentifier"], "dotnet runtime identifier")
    if dotnet["infoOutputSha256"] != canonical_sha256(
        {
            "sdkVersion": dotnet["sdkVersion"],
            "runtimeIdentifier": dotnet["runtimeIdentifier"],
        }
    ):
        raise ValueError("dotnet canonical output digest differs")

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
    if packages != [by_name[key] for key in sorted(by_name)]:
        raise ValueError("Android installed package inventory is not canonical")
    if android["inventoryOutputSha256"] != canonical_sha256(packages):
        raise ValueError("Android SDK canonical inventory digest differs")
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
        {
            "available",
            "version",
            "buildId",
            "versionOutputSha256",
            "liveObservation",
        },
        "emulator observation",
    )
    for field in ("protocolVersion", "packageVersion"):
        _safe_token(android["adb"][field], f"ADB {field}")
    emulator_required = "emulator" in role_policy["requiredAndroidPackages"]
    emulator_observation = android["emulator"]
    if type(emulator_observation["available"]) is not bool:
        raise ValueError("emulator availability posture must be boolean")
    if not emulator_required and emulator_observation["available"]:
        raise ValueError("build environment must record emulator as unavailable")
    if emulator_observation["available"]:
        _safe_token(emulator_observation["version"], "emulator version")
        if (
            type(emulator_observation["buildId"]) is not int
            or emulator_observation["buildId"] <= 0
            or not isinstance(emulator_observation["liveObservation"], dict)
            or "emulator" not in by_name
            or not emulator_versions_match(
                by_name["emulator"]["version"], emulator_observation["version"]
            )
        ):
            raise ValueError("emulator observation authority differs")
        if emulator_observation["versionOutputSha256"] != canonical_sha256(
            {
                "version": emulator_observation["version"],
                "buildId": emulator_observation["buildId"],
            }
        ):
            raise ValueError("emulator canonical output digest differs")
        live_observation = emulator_observation["liveObservation"]
        require_exact_fields(
            live_observation,
            {
                "schema",
                "sha256",
                "sizeBytes",
                "authoritySha256",
                "officialLineSha256",
                "prefixSha256",
                "prefixSizeBytes",
                "execution",
                "launch",
            },
            "emulator live-observation binding",
        )
        if (
            live_observation["schema"] != EMULATOR_LIVE_OBSERVATION_SCHEMA
            or type(live_observation["sizeBytes"]) is not int
            or not 1 <= live_observation["sizeBytes"] <= EMULATOR_LIVE_LOG_MAX_PREFIX_BYTES
            or type(live_observation["prefixSizeBytes"]) is not int
            or not 1
            <= live_observation["prefixSizeBytes"]
            <= EMULATOR_LIVE_LOG_MAX_PREFIX_BYTES
            or not isinstance(live_observation["execution"], dict)
            or not isinstance(live_observation["launch"], dict)
        ):
            raise ValueError("emulator live-observation binding differs")
        require_exact_fields(
            live_observation["execution"],
            {"runId", "runAttempt", "matrixJourney"},
            "emulator live execution binding",
        )
        if (
            type(live_observation["execution"]["runId"]) is not int
            or live_observation["execution"]["runId"] <= 0
            or type(live_observation["execution"]["runAttempt"]) is not int
            or live_observation["execution"]["runAttempt"] <= 0
            or live_observation["execution"]["matrixJourney"] not in journey_map()
        ):
            raise ValueError("emulator live execution binding differs")
        require_exact_fields(
            live_observation["launch"],
            {"launcherRelativePath", "avdName", "emulatorSerial", "emulatorPort"},
            "emulator live launch binding",
        )
        if live_observation["launch"] != {
            "launcherRelativePath": EMULATOR_LAUNCHER_RELATIVE_PATH,
            "avdName": EMULATOR_AVD_NAME,
            "emulatorSerial": EMULATOR_SERIAL,
            "emulatorPort": EMULATOR_PORT,
        }:
            raise ValueError("emulator live launch binding differs")
    elif (
        emulator_required
        or emulator_observation["version"] is not None
        or emulator_observation["buildId"] is not None
        or emulator_observation["versionOutputSha256"]
        != canonical_sha256({"available": False})
        or emulator_observation["liveObservation"] is not None
    ):
        raise ValueError("required emulator observation is unavailable or inconsistent")
    if android["adb"]["versionOutputSha256"] != canonical_sha256(
        {
            "protocolVersion": android["adb"]["protocolVersion"],
            "packageVersion": android["adb"]["packageVersion"],
        }
    ):
        raise ValueError("ADB canonical output digest differs")

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
    compatibility = {
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
        "kernel": {
            field: observation["kernel"][field]
            for field in ("system", "machine")
        },
    }
    if role == "journey":
        compatibility["emulator"] = {
            field: observation["androidSdk"]["emulator"][field]
            for field in ("available", "version", "buildId")
        }
        live_observation = observation["androidSdk"]["emulator"]["liveObservation"]
        compatibility["emulatorLiveAuthority"] = {
            "schema": live_observation["schema"],
            "officialLineSha256": live_observation["officialLineSha256"],
            "launch": live_observation["launch"],
        }
        compatibility["kvm"] = dict(observation["kvm"])
    return compatibility


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
    if role == "journey" and validated["androidSdk"]["emulator"][
        "liveObservation"
    ]["execution"]["matrixJourney"] != validated_subject["matrixJourney"]:
        raise ValueError("emulator live observation journey differs")
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
    if role == "journey" and receipt["environment"]["androidSdk"]["emulator"][
        "liveObservation"
    ]["execution"]["matrixJourney"] != receipt["subjectAuthority"]["matrixJourney"]:
        raise ValueError("emulator live observation journey differs")
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
