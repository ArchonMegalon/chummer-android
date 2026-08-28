#!/usr/bin/env python3
"""Strict contracts for the six-journey API-36 ARM64 physical proof plane."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import re
import stat
import tempfile
from typing import Mapping, Sequence
import zipfile


DEVICE_SCHEMA = "chummer.android.api36-arm64-physical-device/v1"
SEAL_SCHEMA = "chummer.android.api36-arm64-physical-journey-seal/v1"
AGGREGATE_SCHEMA = "chummer.android.api36-arm64-physical-six-journey/v1"
BUILD_PROVENANCE_SCHEMA = "chummer.android.api36-arm64-physical-build-provenance/v2"
SOURCE_GRAPH_SCHEMA = "chummer.android.release-source-graph/v2"
PACKAGE = "com.myexternalbrain.chummer"
TARGET_FRAMEWORK = "net10.0-android36.0"
RUNTIME_IDENTIFIER = "android-arm64"
ABI = "arm64-v8a"
JOURNEY_ORDER = (
    "priority", "career", "before-run", "after-run", "downtime", "playtime",
)
JOURNEY_CONTRACTS = {
    "priority": (
        "chummer.android.sr5-priority-create-physical-e2e/v1",
        "sr5-priority-create-physical",
    ),
    "career": (
        "chummer.android.sr5-career-active-skill-physical-e2e/v1",
        "sr5-career-active-skill-wizard-physical",
    ),
    "before-run": (
        "chummer.android.sr5-before-run-edge-physical-e2e/v1",
        "sr5-before-run-edge-physical",
    ),
    "after-run": (
        "chummer.android.sr5-after-run-settlement-physical-e2e/v1",
        "sr5-after-run-settlement-physical",
    ),
    "downtime": (
        "chummer.android.sr5-downtime-calendar-physical-e2e/v1",
        "sr5-downtime-calendar-physical",
    ),
    "playtime": (
        "chummer.android.sr5-playtime-weapon-physical-e2e/v1",
        "sr5-playtime-weapon-physical",
    ),
}
DOES_NOT_ASSERT = (
    "google_play_upload", "google_play_processing", "tester_distribution",
    "production_rollout", "tablet_journey", "publication_authority",
    "public_release_readiness",
)
SHA256 = re.compile(r"^[0-9a-f]{64}$")
PID = re.compile(r"^[1-9][0-9]*$")
SERIAL = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
COMPONENT = re.compile(r"^com\.myexternalbrain\.chummer/[A-Za-z0-9._$]+$")
VIRTUAL_MARKERS = (
    "aosp_cf_", "cuttlefish", "emulator", "generic", "goldfish", "qemu",
    "ranchu", "sdk_gphone", "vbox", "virtualbox",
)


CAREER_SOURCE_FIELDS = {
    "sharedDriverSha256", "x86LeafDriverSha256", "buildPageSha256",
    "runnerCoordinatorSha256", "careerWizardModelSha256", "careerWizardPageSha256",
    "activeSkillWizardPageSha256", "activeSkillCoordinatorSha256",
    "checkpointStoreSha256", "careerActiveSkillRequestSha256",
    "careerActiveSkillMutationSha256", "presenterPersistenceSha256",
    "careerActiveSkillRulesSha256", "activeSkillSourceResolverSha256",
    "workspaceStoreSha256", "careerFixtureSha256", "driverSha256",
}
RAW_FIELDS = {
    "priority": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "releaseAttested", "publicationAuthorized", "generatedAtUtc", "journey",
        "buildMethod", "profile", "serial", "apiLevel", "abi", "package", "apk",
        "apkSha256", "buildProvenance", "buildProvenanceFile",
        "buildProvenanceRecheckedAfterRun", "buildProvenanceFileRecheckedAfterRun",
        "disposableDeviceAuthorization", "deviceObservation", "adbTransport",
        "physicalDeviceProof", "installedArtifactBound", "draftStateFabricated",
        "identityContractStatus", "authorityProofStages",
    },
    "career": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "buildProvenance", "adbTransport", "generatedAtUtc", "serial", "profile",
        "journey", "apiLevel", "abi", "deviceObservation", "package", "apk",
        "apkSha256", "expectedApkSha256", "apkAbis", "androidSourceRevision",
        "expectedAndroidSourceRevision", "presentationSourceRevision",
        "coreSourceRevision", "sourceGraphAuthority", "postRunSourceGraphAuthoritySha256",
        "sourceGraphRecheckedAfterRun", "verifiedRemoteCareerFixtureSha256",
        "remoteTemporaryFiles", "authorityProofStages", "journeys",
    } | CAREER_SOURCE_FIELDS,
    "before-run": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "sourceGraphRecheckedAfterRun", "apkSha256", "careerFixtureSha256",
        "verifiedRemoteCareerFixtureSha256", "remoteTemporaryFilesDeleted",
        "adbTransport", "authorityProofStages", "scope", "journeys",
    },
    "playtime": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "sourceGraphRecheckedAfterRun", "apkSha256", "careerFixtureSha256",
        "verifiedRemoteCareerFixtureSha256", "remoteTemporaryFilesDeleted",
        "adbTransport", "authorityProofStages", "scope", "journeys",
    },
    "after-run": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "serial", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "postRunSourceGraphAuthoritySha256", "sourceGraphRecheckedAfterRun",
        "apkSha256", "apkAbis", "governedFixtureSha256", "materializedRunnerSha256",
        "verifiedRemoteRunnerSha256", "remoteTemporaryFiles", "authorityProofStages",
        "journeys",
    },
    "downtime": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "serial", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "postRunSourceGraphAuthoritySha256", "sourceGraphRecheckedAfterRun",
        "apkSha256", "apkAbis", "governedFixtureSha256", "careerRunnerSha256",
        "verifiedRemoteRunnerSha256", "remoteTemporaryFiles", "authorityProofStages",
        "journeys",
    },
}


@dataclass(frozen=True)
class BoundBytes:
    path: Path
    data: bytes
    sha256: str
    size_bytes: int

    def json(self) -> dict[str, object]:
        return {
            "basename": self.path.name,
            "sha256": self.sha256,
            "sizeBytes": self.size_bytes,
        }


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def strict_json_bytes(data: bytes, label: str) -> dict[str, object]:
    try:
        value = json.loads(
            data.decode("utf-8"), object_pairs_hook=reject_duplicate_keys,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite number {token}")
            ),
        )
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one JSON object")
    return value


def require_exact_keys(value: object, expected: set[str], label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one object")
    if set(value) != expected:
        raise ValueError(
            f"{label} keys are not exact; missing={sorted(expected - set(value))}, "
            f"extra={sorted(set(value) - expected)}"
        )
    return value


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(
        value, allow_nan=False, ensure_ascii=False, separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def require_utc_timestamp(value: object, label: str, *, canonical_z: bool = False) -> str:
    if not isinstance(value, str) or (canonical_z and not value.endswith("Z")):
        raise ValueError(f"{label} must be one UTC timestamp")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise ValueError(f"{label} must be one UTC timestamp") from error
    if parsed.tzinfo is None or parsed.utcoffset() != timezone.utc.utcoffset(parsed):
        raise ValueError(f"{label} must be one UTC timestamp")
    return value


def _reject_symlink_components(path: Path) -> None:
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        try:
            mode = os.lstat(current).st_mode
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(mode):
            raise ValueError(f"path contains a symlink component: {current}")


def bind_regular(path: Path, label: str) -> BoundBytes:
    if not path.is_absolute() or path.resolve(strict=True) != path:
        raise ValueError(f"{label} path must be absolute and canonical")
    _reject_symlink_components(path)
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags)
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError(f"{label} must be a regular file")
        chunks: list[bytes] = []
        digest = hashlib.sha256()
        size = 0
        while True:
            chunk = os.read(descriptor, 1024 * 1024)
            if not chunk:
                break
            chunks.append(chunk)
            digest.update(chunk)
            size += len(chunk)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    identity = (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
    if identity != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns) or size != after.st_size:
        raise ValueError(f"{label} changed during immutable byte capture")
    return BoundBytes(path, b"".join(chunks), digest.hexdigest(), size)


def require_unchanged(bound: BoundBytes, label: str) -> None:
    if bind_regular(bound.path, label) != bound:
        raise ValueError(f"{label} bytes changed across the authentication boundary")


def validate_external_output(path: Path, repository_root: Path) -> None:
    if not path.is_absolute() or any(part in {".", ".."} for part in path.parts):
        raise ValueError("output path must be absolute and normalized")
    if path.exists() or path.is_symlink():
        raise ValueError("output path must be absent")
    if path.parent.resolve(strict=True) != path.parent or path.parent.is_symlink():
        raise ValueError("output parent must be a canonical real directory")
    _reject_symlink_components(path.parent)
    try:
        path.relative_to(repository_root.resolve(strict=True))
    except ValueError:
        return
    raise ValueError("proof output must remain outside the source worktree")


def write_json_exclusive(path: Path, payload: Mapping[str, object], repository_root: Path) -> None:
    validate_external_output(path, repository_root)
    encoded = (json.dumps(payload, allow_nan=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile("wb", dir=path.parent, prefix=f".{path.name}.", delete=False) as stream:
            temporary = stream.name
            os.fchmod(stream.fileno(), 0o600)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path, follow_symlinks=False)
        directory = os.open(path.parent, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def validate_source_graph(bound: BoundBytes) -> dict[str, object]:
    graph = strict_json_bytes(bound.data, "v2 release source graph")
    require_exact_keys(graph, {
        "contractName", "generatedAtUtc", "authorityState", "publicationAuthorized",
        "generator", "repositories", "packagePins", "ownerPackagePins",
        "dependencyClosure", "presentationSource", "doesNotAssert",
    }, "v2 release source graph")
    if graph.get("contractName") != SOURCE_GRAPH_SCHEMA or graph.get("publicationAuthorized") is not False:
        raise ValueError("source graph contract/publication posture is not exact")
    require_utc_timestamp(graph.get("generatedAtUtc"), "source graph generatedAtUtc", canonical_z=True)
    repositories = graph.get("repositories")
    if not isinstance(repositories, list) or len(repositories) != 8:
        raise ValueError("source graph must bind exactly eight repositories")
    package_pins = graph.get("packagePins")
    owner_pins = graph.get("ownerPackagePins")
    if (
        not isinstance(package_pins, list) or len(package_pins) != 6
        or not isinstance(owner_pins, list) or len(owner_pins) != 7
    ):
        raise ValueError("source graph must bind six Core and seven owner package pins")
    return graph


def validate_build_provenance(bound: BoundBytes, graph: BoundBytes, apk: BoundBytes) -> dict[str, object]:
    value = strict_json_bytes(bound.data, "WP1 build provenance")
    require_exact_keys(value, {
        "schema", "status", "authorityClass", "publicationAuthorized", "proofScope",
        "dependencyMode", "sourceGraph", "w5CompileProof", "presentationBuildSource",
        "packageAuthority", "content", "restore", "executionEvidence", "toolchain",
        "artifact", "doesNotAssert", "authoritySha256", "generatedAtUtc",
    }, "WP1 build provenance")
    if (
        value.get("schema") != BUILD_PROVENANCE_SCHEMA or value.get("status") != "pass"
        or value.get("publicationAuthorized") is not False
        or value.get("proofScope") != "full_maui_arm64_apk_build_only"
        or value.get("authorityClass") != "internal_phone_beta_physical_candidate_only"
        or value.get("dependencyMode") != "locked_w5_packages_no_owner_siblings"
    ):
        raise ValueError("WP1 build provenance pass/scope/publication posture is not exact")
    require_utc_timestamp(value.get("generatedAtUtc"), "WP1 generatedAtUtc", canonical_z=True)
    authority = dict(value)
    authority_sha = authority.pop("authoritySha256", None)
    authority.pop("generatedAtUtc", None)
    if not isinstance(authority_sha, str) or SHA256.fullmatch(authority_sha) is None:
        raise ValueError("WP1 authority digest is not canonical")
    if canonical_sha256(authority) != authority_sha:
        raise ValueError("WP1 authority digest does not authenticate its payload")
    source = require_exact_keys(
        value.get("sourceGraph"), {"sha256", "sizeBytes", "contractName", "repositories"},
        "WP1 source graph binding",
    )
    graph_payload = validate_source_graph(graph)
    if (
        source.get("sha256") != graph.sha256 or source.get("sizeBytes") != graph.size_bytes
        or source.get("contractName") != SOURCE_GRAPH_SCHEMA
        or source.get("repositories") != graph_payload["repositories"]
    ):
        raise ValueError("WP1 build provenance does not bind the supplied v2 source graph bytes")
    presentation_source = value.get("presentationBuildSource")
    if not isinstance(presentation_source, dict) or (
        presentation_source.get("productionSource") is not False
        or presentation_source.get("publicationAuthorized") is not False
    ):
        raise ValueError("WP1 Presentation build source is not internal/non-publication-only")
    artifact = require_exact_keys(value.get("artifact"), {
        "basename", "sha256", "sizeBytes", "package", "abis", "apiLevel",
        "configuration", "runtimeIdentifier", "targetFramework", "fullMauiArtifact", "installed",
    }, "WP1 artifact")
    expected = {
        "basename": apk.path.name, "sha256": apk.sha256, "sizeBytes": apk.size_bytes,
        "package": PACKAGE, "abis": [ABI], "apiLevel": 36, "configuration": "Debug",
        "runtimeIdentifier": RUNTIME_IDENTIFIER, "targetFramework": TARGET_FRAMEWORK,
        "fullMauiArtifact": True, "installed": False,
    }
    if artifact != expected:
        raise ValueError("WP1 artifact does not bind the exact supplied ARM64 APK")
    restore = value.get("restore")
    if not isinstance(restore, dict) or restore.get("lockedMode") is not True or restore.get("networkSourcesAllowed") is not False:
        raise ValueError("WP1 restore posture is not locked and offline")
    return value


def validate_apk(bound: BoundBytes) -> None:
    try:
        with zipfile.ZipFile(io.BytesIO(bound.data)) as archive:
            abis = sorted({
                parts[1] for name in archive.namelist()
                if len(parts := name.split("/")) >= 3 and parts[0] == "lib" and parts[1]
            })
    except (OSError, zipfile.BadZipFile) as error:
        raise ValueError("APK is not a readable ZIP artifact") from error
    if abis != [ABI]:
        raise ValueError(f"APK ABI closure is not exactly {ABI}: {abis!r}")


def validate_device_observation(bound: BoundBytes) -> dict[str, object]:
    value = strict_json_bytes(bound.data, "physical device observation")
    return validate_device_payload(value)


def validate_device_payload(value: object) -> dict[str, object]:
    require_exact_keys(value, {
        "schema", "status", "classification", "publicationAuthorized", "serial",
        "serialSha256", "apiLevel", "abi", "abiList", "properties",
        "observationNature", "capturedAtUtc",
    }, "physical device observation")
    if (
        value.get("schema") != DEVICE_SCHEMA or value.get("status") != "pass"
        or value.get("classification") != "physical_api36_arm64_non_emulator"
        or value.get("publicationAuthorized") is not False
        or value.get("apiLevel") != 36 or value.get("abi") != ABI
    ):
        raise ValueError("physical API36 ARM64 device posture is not exact")
    serial = value.get("serial")
    if not isinstance(serial, str) or SERIAL.fullmatch(serial) is None:
        raise ValueError("physical device serial is invalid")
    if value.get("serialSha256") != hashlib.sha256(serial.encode("utf-8")).hexdigest():
        raise ValueError("physical device serial digest mismatch")
    require_utc_timestamp(value.get("capturedAtUtc"), "device capturedAtUtc", canonical_z=True)
    abi_list = value.get("abiList")
    if not isinstance(abi_list, list) or ABI not in abi_list or any(not isinstance(row, str) for row in abi_list):
        raise ValueError("physical device ABI list is invalid")
    properties = require_exact_keys(value.get("properties"), {
        "ro.boot.qemu", "ro.boot.verifiedbootstate", "ro.build.fingerprint",
        "ro.build.id", "ro.build.version.security_patch", "ro.build.version.sdk",
        "ro.hardware", "ro.kernel.qemu", "ro.product.cpu.abi",
        "ro.product.cpu.abilist", "ro.product.device", "ro.product.manufacturer",
        "ro.product.model", "ro.product.name",
    }, "physical device properties")
    if any(not isinstance(row, str) for row in properties.values()):
        raise ValueError("physical device properties must all be strings")
    if (
        properties["ro.build.version.sdk"] != "36"
        or properties["ro.product.cpu.abi"] != ABI
        or properties["ro.product.cpu.abilist"].split(",") != abi_list
        or not properties["ro.build.fingerprint"]
        or not properties["ro.product.manufacturer"]
        or not properties["ro.product.model"]
    ):
        raise ValueError("physical device properties contradict the API/ABI/device observation")
    virtual = "\n".join(str(properties[key]) for key in properties).lower()
    if (
        serial.lower().startswith(("emulator-", "localhost:", "127.0.0.1:", "::1:"))
        or properties["ro.kernel.qemu"] not in ("", "0")
        or properties["ro.boot.qemu"] not in ("", "0")
        or any(marker in virtual for marker in VIRTUAL_MARKERS)
    ):
        raise ValueError("device observation contains emulator evidence")
    return value


def parse_restart_evidence(bound: BoundBytes) -> dict[str, object]:
    try:
        lines = bound.data.decode("utf-8").splitlines()
    except UnicodeError as error:
        raise ValueError("restart evidence is not UTF-8") from error
    expected = (
        "pre_force_stop_process_ids", "pre_force_stop_resumed_component",
        "post_force_stop_process_ids", "restart_process_ids", "restart_resumed_component",
    )
    if len(lines) != len(expected):
        raise ValueError("restart evidence line count is not exact")
    values: dict[str, str] = {}
    for key, line in zip(expected, lines, strict=True):
        prefix = f"{key}="
        if not line.startswith(prefix):
            raise ValueError("restart evidence field order/set is not exact")
        values[key] = line[len(prefix):]
    before = values[expected[0]].split()
    after = values[expected[2]].split()
    restarted = values[expected[3]].split()
    if not before or not restarted or after:
        raise ValueError("restart evidence requires nonempty before/restarted and empty post-stop PIDs")
    if any(PID.fullmatch(row) is None for row in (*before, *restarted)):
        raise ValueError("restart evidence contains an invalid PID")
    if len(before) != len(set(before)) or len(restarted) != len(set(restarted)):
        raise ValueError("restart evidence contains duplicate PIDs")
    if set(before).intersection(restarted):
        raise ValueError("restart evidence reused a pre-force-stop PID")
    before_component = values[expected[1]]
    restarted_component = values[expected[4]]
    if (
        COMPONENT.fullmatch(before_component) is None
        or restarted_component != before_component
    ):
        raise ValueError("restart evidence resumed component is absent, foreign, or changed")
    authority = {
        "beforeProcessIds": before, "afterForceStopProcessIds": [],
        "restartedProcessIds": restarted, "beforeResumedComponent": before_component,
        "restartedResumedComponent": restarted_component,
        "newPidVerified": True,
    }
    return {**authority, "restartAuthoritySha256": canonical_sha256(authority)}


def _raw_device_matches(raw: Mapping[str, object], device: Mapping[str, object]) -> None:
    observation = raw.get("deviceObservation")
    if not isinstance(observation, dict):
        raise ValueError("raw receipt omitted deviceObservation")
    aliases = {
        "serial": "serial", "apiLevel": "apiLevel", "abi": "abi", "abiList": "abiList",
        "qemu": "ro.kernel.qemu", "bootQemu": "ro.boot.qemu", "hardware": "ro.hardware",
        "buildFingerprint": "ro.build.fingerprint", "buildId": "ro.build.id",
        "securityPatch": "ro.build.version.security_patch",
        "verifiedBootState": "ro.boot.verifiedbootstate", "manufacturer": "ro.product.manufacturer",
        "model": "ro.product.model", "productDevice": "ro.product.device", "productName": "ro.product.name",
    }
    properties = device["properties"]
    for raw_key, device_key in aliases.items():
        if raw_key not in observation:
            continue
        expected = device.get(device_key) if device_key in {"serial", "apiLevel", "abi", "abiList"} else properties[device_key]
        observed = observation[raw_key]
        if raw_key == "abiList" and isinstance(observed, str):
            observed = observed.split(",")
        if observed != expected:
            raise ValueError(f"raw receipt device observation diverged: {raw_key}")
    for mandatory in ("serial", "apiLevel", "abi"):
        if mandatory not in observation:
            raise ValueError(f"raw receipt device observation omitted {mandatory}")


def _raw_restart_pids(journey_id: str, proof: Mapping[str, object]) -> list[str]:
    if journey_id == "priority":
        restart = proof.get("processRestart")
        if not isinstance(restart, dict):
            raise ValueError("Priority raw receipt omitted processRestart")
        before = restart.get("beforeProcessIds")
        after = restart.get("afterForceStopProcessIds")
        restarted = restart.get("restartedProcessIds")
        if not isinstance(before, list) or not isinstance(after, list) or not isinstance(restarted, list):
            raise ValueError("Priority raw restart PID sets are malformed")
        if after or not before or not restarted or set(before).intersection(restarted):
            raise ValueError("Priority raw restart semantics are invalid")
        return [str(row) for row in restarted]
    rows = proof.get("restartProcessIds")
    if not isinstance(rows, list) or len(rows) != 3:
        raise ValueError(f"{journey_id} raw receipt must contain exactly three restarted PID sets")
    normalized: list[list[str]] = []
    for row in rows:
        if not isinstance(row, list) or not row or any(not isinstance(pid, str) or PID.fullmatch(pid) is None for pid in row):
            raise ValueError(f"{journey_id} raw restarted PID set is invalid")
        normalized.append(row)
    flattened = [pid for row in normalized for pid in row]
    if len(flattened) != len(set(flattened)):
        raise ValueError(f"{journey_id} raw receipt reused a restarted PID")
    return normalized[-1]


def validate_raw_receipt(
    journey_id: str, bound: BoundBytes, *, build_provenance: Mapping[str, object],
    build_provenance_binding: BoundBytes, apk: BoundBytes,
    device: Mapping[str, object], restart: Mapping[str, object],
) -> dict[str, object]:
    if journey_id not in JOURNEY_CONTRACTS:
        raise ValueError(f"unknown journey id: {journey_id}")
    value = strict_json_bytes(bound.data, f"{journey_id} raw receipt")
    require_exact_keys(value, RAW_FIELDS[journey_id], f"{journey_id} raw receipt")
    schema, raw_journey = JOURNEY_CONTRACTS[journey_id]
    if (
        value.get("schema") != schema or value.get("journey") != raw_journey
        or value.get("status") != "device-pass-source-bound"
        or value.get("executionStatus") != "pass" or value.get("profile") != "phone"
        or value.get("apiLevel") != 36 or value.get("abi") != ABI
        or value.get("releaseEvidenceStatus") != "source-and-apk-bound-local-build-not-release-attested"
    ):
        raise ValueError(f"{journey_id} raw receipt pass/schema/phone posture is not exact")
    require_utc_timestamp(value.get("generatedAtUtc"), f"{journey_id} generatedAtUtc")
    if value.get("buildProvenance") != build_provenance:
        raise ValueError(f"{journey_id} raw receipt does not bind the exact WP1 provenance")
    if value.get("apkSha256") != apk.sha256:
        raise ValueError(f"{journey_id} raw receipt APK digest mismatch")
    if value.get("package", PACKAGE) != PACKAGE:
        raise ValueError(f"{journey_id} raw receipt package identity mismatch")
    if value.get("apkAbis", [ABI]) != [ABI]:
        raise ValueError(f"{journey_id} raw receipt ABI closure mismatch")
    if value.get("expectedApkSha256", apk.sha256) != apk.sha256:
        raise ValueError(f"{journey_id} raw receipt expected APK digest mismatch")
    if value.get("serial", device["serial"]) != device["serial"]:
        raise ValueError(f"{journey_id} raw receipt serial mismatch")
    provenance_file = value.get("buildProvenanceFile")
    if provenance_file is not None and provenance_file != {
        "sha256": build_provenance_binding.sha256, "size": build_provenance_binding.size_bytes,
    }:
        raise ValueError(f"{journey_id} raw receipt WP1 provenance byte binding mismatch")
    if journey_id == "priority":
        disposable = value.get("disposableDeviceAuthorization")
        if disposable != {
            "authorized": True,
            "flag": "--allow-destructive-disposable-device",
            "serial": device["serial"],
            "scope": "install-apk-and-atomically-finalize-one-pending-runner",
        }:
            raise ValueError("Priority raw receipt lacks exact disposable-device authorization")
        if (
            value.get("releaseAttested") is not False
            or value.get("buildMethod") != "Priority"
            or value.get("apk") != str(apk.path)
            or value.get("buildProvenanceRecheckedAfterRun") is not True
            or value.get("buildProvenanceFileRecheckedAfterRun") is not True
            or value.get("physicalDeviceProof") is not True
            or value.get("installedArtifactBound") is not True
            or value.get("draftStateFabricated") is not False
            or value.get("identityContractStatus") != "typed-contract-unavailable"
        ):
            raise ValueError("Priority raw receipt physical/provenance/non-release posture is not exact")
    if "publicationAuthorized" in value and value["publicationAuthorized"] is not False:
        raise ValueError(f"{journey_id} raw receipt contains a publication claim")
    forbidden_claims = {
        "publicationauthorized", "releaseattested", "productionrollout",
        "googleplayupload", "googleplayprocessing", "testerdistribution",
        "tabletjourney", "publicreleasereadiness",
    }

    def reject_positive_claims(candidate: object) -> None:
        if isinstance(candidate, dict):
            for key, nested in candidate.items():
                normalized = re.sub(r"[^a-z0-9]", "", key.lower())
                if normalized in forbidden_claims and nested not in (
                    False, None, "false", "unclaimed", "not-asserted",
                ):
                    raise ValueError(f"{journey_id} raw receipt contains forbidden claim {key}")
                reject_positive_claims(nested)
        elif isinstance(candidate, list):
            for nested in candidate:
                reject_positive_claims(nested)

    reject_positive_claims(value)
    if value.get("sourceGraphRecheckedAfterRun", True) is not True:
        raise ValueError(f"{journey_id} raw receipt did not recheck its source graph")
    transport = value.get("adbTransport")
    if "adbTransport" in RAW_FIELDS[journey_id] and (
        not isinstance(transport, dict) or transport.get("status") != "pass"
    ):
        raise ValueError(f"{journey_id} raw receipt ADB transport did not pass")
    journeys = value.get("journeys")
    if journeys is not None and (
        not isinstance(journeys, dict) or not journeys
        or any(result != "pass" for result in journeys.values())
    ):
        raise ValueError(f"{journey_id} raw subjourney claims are not all pass")
    proof = value.get("authorityProofStages")
    if not isinstance(proof, dict):
        raise ValueError(f"{journey_id} raw receipt omitted authorityProofStages")
    _raw_device_matches(value, device)
    final_raw_pids = _raw_restart_pids(journey_id, proof)
    if final_raw_pids != restart["restartedProcessIds"]:
        raise ValueError(f"{journey_id} raw final restarted PIDs do not match restart evidence")
    return value


def common_authority(
    *, apk: BoundBytes, graph: BoundBytes, provenance: BoundBytes,
    provenance_payload: Mapping[str, object], device: BoundBytes,
    device_payload: Mapping[str, object],
) -> dict[str, object]:
    return {
        "artifact": {
            **apk.json(), "package": PACKAGE, "targetFramework": TARGET_FRAMEWORK,
            "runtimeIdentifier": RUNTIME_IDENTIFIER, "abi": ABI,
        },
        "sourceGraph": {**graph.json(), "contractName": SOURCE_GRAPH_SCHEMA},
        "buildProvenance": {
            **provenance.json(), "schema": BUILD_PROVENANCE_SCHEMA,
            "authoritySha256": provenance_payload["authoritySha256"],
        },
        "deviceObservation": {
            **device.json(), "schema": DEVICE_SCHEMA,
            "serial": device_payload["serial"],
            "serialSha256": device_payload["serialSha256"],
        },
    }


def capture_authority_inputs(
    *, apk_path: Path, source_graph_path: Path, build_provenance_path: Path,
    device_observation_path: Path,
) -> tuple[BoundBytes, BoundBytes, BoundBytes, dict[str, object], BoundBytes, dict[str, object]]:
    apk, graph, provenance, provenance_payload = capture_build_inputs(
        apk_path=apk_path, source_graph_path=source_graph_path,
        build_provenance_path=build_provenance_path,
    )
    device = bind_regular(device_observation_path, "physical device observation")
    device_payload = validate_device_observation(device)
    return apk, graph, provenance, provenance_payload, device, device_payload


def capture_build_inputs(
    *, apk_path: Path, source_graph_path: Path, build_provenance_path: Path,
) -> tuple[BoundBytes, BoundBytes, BoundBytes, dict[str, object]]:
    apk = bind_regular(apk_path, "ARM64 APK")
    validate_apk(apk)
    graph = bind_regular(source_graph_path, "v2 source graph")
    validate_source_graph(graph)
    provenance = bind_regular(build_provenance_path, "WP1 build provenance")
    provenance_payload = validate_build_provenance(provenance, graph, apk)
    return apk, graph, provenance, provenance_payload


def create_journey_seal(
    *, journey_id: str, raw_receipt_path: Path, restart_evidence_path: Path,
    apk_path: Path, source_graph_path: Path, build_provenance_path: Path,
    device_observation_path: Path, generated_at_utc: str | None = None,
) -> dict[str, object]:
    apk, graph, provenance, provenance_payload, device, device_payload = capture_authority_inputs(
        apk_path=apk_path, source_graph_path=source_graph_path,
        build_provenance_path=build_provenance_path,
        device_observation_path=device_observation_path,
    )
    raw = bind_regular(raw_receipt_path, f"{journey_id} raw receipt")
    restart_file = bind_regular(restart_evidence_path, f"{journey_id} restart evidence")
    restart = parse_restart_evidence(restart_file)
    raw_payload = validate_raw_receipt(
        journey_id, raw, build_provenance=provenance_payload,
        build_provenance_binding=provenance, apk=apk,
        device=device_payload, restart=restart,
    )
    authority = {
        "schema": SEAL_SCHEMA, "status": "pass",
        "authorityClass": "local_physical_api36_arm64_journey_only",
        "publicationAuthorized": False, "profile": "phone",
        "journeyId": journey_id, "journeyOrder": JOURNEY_ORDER.index(journey_id),
        "rawReceipt": {
            **raw.json(), "schema": raw_payload["schema"],
            "journey": raw_payload["journey"], "status": raw_payload["status"],
        },
        "restartEvidence": {**restart_file.json(), **restart},
        **common_authority(
            apk=apk, graph=graph, provenance=provenance,
            provenance_payload=provenance_payload, device=device,
            device_payload=device_payload,
        ),
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    seal = {
        **authority, "sealAuthoritySha256": canonical_sha256(authority),
        "generatedAtUtc": generated_at_utc
        or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    for captured, label in (
        (apk, "ARM64 APK"), (graph, "v2 source graph"),
        (provenance, "WP1 build provenance"), (device, "physical device observation"),
        (raw, f"{journey_id} raw receipt"),
        (restart_file, f"{journey_id} restart evidence"),
    ):
        require_unchanged(captured, label)
    return seal


def load_and_verify_journey_seal(path: Path, **arguments: object) -> tuple[dict[str, object], BoundBytes]:
    bound = bind_regular(path, "journey seal")
    value = strict_json_bytes(bound.data, "journey seal")
    require_exact_keys(value, {
        "schema", "status", "authorityClass", "publicationAuthorized", "profile",
        "journeyId", "journeyOrder", "rawReceipt", "restartEvidence", "artifact",
        "sourceGraph", "buildProvenance", "deviceObservation", "doesNotAssert",
        "sealAuthoritySha256", "generatedAtUtc",
    }, "journey seal")
    generated = require_utc_timestamp(
        value.get("generatedAtUtc"), "journey seal generatedAtUtc", canonical_z=True,
    )
    expected = create_journey_seal(generated_at_utc=generated, **arguments)
    if value != expected:
        raise ValueError("journey seal differs from authenticated current inputs")
    require_unchanged(bound, "journey seal")
    return value, bound


def create_aggregate(
    *, journey_inputs: Sequence[tuple[str, Path, Path, Path]], apk_path: Path,
    source_graph_path: Path, build_provenance_path: Path,
    device_observation_path: Path, generated_at_utc: str | None = None,
) -> dict[str, object]:
    if tuple(row[0] for row in journey_inputs) != JOURNEY_ORDER or len(journey_inputs) != 6:
        raise ValueError("aggregate journey cardinality/order is not exact")
    apk, graph, provenance, provenance_payload, device, device_payload = capture_authority_inputs(
        apk_path=apk_path, source_graph_path=source_graph_path,
        build_provenance_path=build_provenance_path,
        device_observation_path=device_observation_path,
    )
    rows: list[dict[str, object]] = []
    captured_journey_files: list[tuple[BoundBytes, str]] = []
    restarted_pids: set[str] = set()
    for journey_id, raw_path, restart_path, seal_path in journey_inputs:
        seal, seal_bound = load_and_verify_journey_seal(
            seal_path, journey_id=journey_id, raw_receipt_path=raw_path,
            restart_evidence_path=restart_path, apk_path=apk_path,
            source_graph_path=source_graph_path,
            build_provenance_path=build_provenance_path,
            device_observation_path=device_observation_path,
        )
        raw_bound = bind_regular(raw_path, f"{journey_id} raw receipt")
        restart_bound = bind_regular(restart_path, f"{journey_id} restart evidence")
        if raw_bound.json() != {
            key: seal["rawReceipt"][key] for key in ("basename", "sha256", "sizeBytes")
        }:
            raise ValueError(f"{journey_id} aggregate raw receipt binding diverged")
        if restart_bound.json() != {
            key: seal["restartEvidence"][key] for key in ("basename", "sha256", "sizeBytes")
        }:
            raise ValueError(f"{journey_id} aggregate restart binding diverged")
        captured_journey_files.extend((
            (raw_bound, f"{journey_id} raw receipt"),
            (restart_bound, f"{journey_id} restart evidence"),
            (seal_bound, f"{journey_id} journey seal"),
        ))
        journey_restarted = seal["restartEvidence"]["restartedProcessIds"]
        if restarted_pids.intersection(journey_restarted):
            raise ValueError("aggregate reused a restarted PID across physical journeys")
        restarted_pids.update(journey_restarted)
        rows.append({
            "journeyId": journey_id, "journeyOrder": JOURNEY_ORDER.index(journey_id),
            "rawReceipt": seal["rawReceipt"], "restartEvidence": seal["restartEvidence"],
            "receiptSeal": {**seal_bound.json(), "sealAuthoritySha256": seal["sealAuthoritySha256"]},
        })
    authority = {
        "schema": AGGREGATE_SCHEMA, "status": "pass",
        "authorityClass": "local_physical_api36_arm64_six_journey_only",
        "publicationAuthorized": False, "profile": "phone",
        "journeyOrder": list(JOURNEY_ORDER), "journeys": rows,
        **common_authority(
            apk=apk, graph=graph, provenance=provenance,
            provenance_payload=provenance_payload, device=device,
            device_payload=device_payload,
        ),
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    aggregate = {
        **authority, "authoritySha256": canonical_sha256(authority),
        "generatedAtUtc": generated_at_utc
        or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    for captured, label in (
        (apk, "ARM64 APK"), (graph, "v2 source graph"),
        (provenance, "WP1 build provenance"), (device, "physical device observation"),
        *captured_journey_files,
    ):
        require_unchanged(captured, label)
    return aggregate


def load_and_verify_aggregate(path: Path, **arguments: object) -> dict[str, object]:
    bound = bind_regular(path, "six-journey aggregate")
    value = strict_json_bytes(bound.data, "six-journey aggregate")
    require_exact_keys(value, {
        "schema", "status", "authorityClass", "publicationAuthorized", "profile",
        "journeyOrder", "journeys", "artifact", "sourceGraph", "buildProvenance",
        "deviceObservation", "doesNotAssert", "authoritySha256", "generatedAtUtc",
    }, "six-journey aggregate")
    generated = require_utc_timestamp(
        value.get("generatedAtUtc"), "aggregate generatedAtUtc", canonical_z=True,
    )
    expected = create_aggregate(generated_at_utc=generated, **arguments)
    if value != expected:
        raise ValueError("six-journey aggregate differs from authenticated current inputs")
    require_unchanged(bound, "six-journey aggregate")
    return value
