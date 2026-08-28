#!/usr/bin/env python3
"""Fail-closed provenance for an internal API-36 ARM64 build candidate."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import importlib.util
import io
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys
import tempfile
from typing import Callable, Mapping
import xml.etree.ElementTree as ET
import zipfile


SCHEMA = "chummer.android.api36-arm64-physical-build-provenance/v2"
AUTHORITY_CLASS = "internal_phone_beta_physical_candidate_only"
PROOF_SCOPE = "full_maui_arm64_apk_build_only"
PACKAGE = "com.myexternalbrain.chummer"
SOURCE_GRAPH_CONTRACT = "chummer.android.release-source-graph/v2"
W5_CONTRACT = "chummer.android.internal-phone-beta-native-compile/v1"
PACKAGE_AUTHORITY_CONTRACT = "chummer.android.internal-phone-beta-package-authority/v1"
CONTENT_CONTRACT = "chummer.android.content-bundle/v1"

W5_RECEIPT_SHA256 = "10346e9900b5c871222f13a1a9eeb5e1961e678d8329f6aca7272461694c5993"
W5_ANDROID_COMMIT = "ac41e30eb6d33433a69b1c7d15628f46ce403c0b"
W5_ANDROID_TREE = "55a9f350a8ad5c3beba37b7d7f97509ae468cf21"
W5_LOCK_SHA256 = "64454d5420e2a5430a046d392c6eea2ca41d9105c1667f2b8a66e1f61064cccc"
W5_AUTHORITY_BINDING_SHA256 = "7f7ab5b827f69eee79addcc5cd47204d9cfa7387acd06c6894329088b6bae839"
W5_PRESENTATION_COMMIT = "a8a317aff534dc5fd47f2db1bc39466799021990"
W5_PRESENTATION_TREE = "f8214243280030de5d134351f39ea4b23afbe394"
W41_PRESENTATION_LOCK_SHA256 = "568fd2c602494329d19fbe8d9a2c83a4c2e82754b50e31141b192c1af7ccf964"
W41_DESKTOP_LOCK_SHA256 = "202a29a35b4768c3306349ee40a34d8f23ada97c0b0ef11e104763b5ff9cc60e"
FULL_PROJECT_LOCK_SHA256 = "9037d4afc11dd8661dfbcccbc67a9f814d110fb17cf985cf215268e12ae3583e"
FULL_PROJECT_LOCK_SIZE = 72165
PRODUCTION_PRESENTATION_COMMIT = "3a5ca054e1ce126a02dec4199dc92233dfee8804"
PRODUCTION_PRESENTATION_TREE = "25def23deef40822e3ff89549cc509e01c149ed4"
CORE_CONTENT_REVISION = "2fb2ae9bb48e5a1a6b25a174ba88008ce995fcd5"
DOTNET_SDK_VERSION = "10.0.111"
TARGET_FRAMEWORK = "net10.0-android36.0"
RUNTIME_IDENTIFIER = "android-arm64"
CONFIGURATION = "Debug"
ANDROID_SDK_ROOT_AUTHORITY = Path("/home/tibor/.cache/chummer-android-toolchain/android-sdk")
JDK_ROOT_AUTHORITY = Path("/home/tibor/.cache/chummer-android-toolchain/microsoft-jdk")
DOTNET_HOST_AUTHORITY = Path("/usr/lib/dotnet/dotnet")
DOTNET_CLI_HOME_AUTHORITY = Path("/home/tibor")
ANDROID_WORKLOAD_MANIFEST_AUTHORITY = Path(
    "/home/tibor/.dotnet/sdk-manifests/10.0.100/microsoft.net.sdk.android/36.1.69/WorkloadManifest.json"
)
MAUI_WORKLOAD_MANIFEST_AUTHORITY = Path(
    "/home/tibor/.dotnet/sdk-manifests/10.0.100/microsoft.net.sdk.maui/10.0.20/WorkloadManifest.json"
)
WORKLOAD_SET_VERSION = "10.0.110.1"
MAUI_ANDROID_MANIFEST_VERSION = "10.0.20/10.0.100"
ANDROID_WORKLOAD_MANIFEST_VERSION = "36.1.69"
DOTNET_RUNTIME_VERSION = "10.0.11"
TOOLCHAIN_SHA256_AUTHORITY = {
    "dotnet": "1c13be7f10008294dfd25f0fc0cd7c88e26d3dbaf8e16019af6c5bb53dd0259d",
    "jdk_release": "6bd25f1446259442ae9cfdd1d9d7b6094aa7e3cf05bcbddb842e2f2b5facac4c",
    "java": "2878f3c82270ae7f2bc0c94dbde65718a5a97387ed3ad4b1ce9047948f8b401e",
    "javac": "899fa6dab44db00429d59959cb2ca53169ad4393841dbbae14a0debcdb9fe2a8",
    "jarsigner": "07e52b7729ed7355c280f6766970b8d5dc9942e741ed5af0330cfc09699eb548",
    "keytool": "7bb11637313a640810ec568ffb7e12d90e423c8c81356fc0416d7547047fa144",
    "platform_package": "2110f8ec9c213a77e287e4e92d89e28dd770e4377c24350758cbddebb75de9f3",
    "android_jar": "d9eb9da824d9e247a352f570f01e1169e725b2954bca9e283a71786c59b59f9a",
    "build_tools_package": "a1d29ea87385aa2b8997c7f65968e0c52e8efb4f73ed4cf1df54df808acde6b8",
    "apksigner": "b47549e373b895ce6ca620d0c7887e674d9615ffa837a86ac601dcfd04adb0f0",
    "apksigner_jar": "3716d9311e55d2b0918a2fd9d54ba9e406c5f6abeea700b287f11259bc163dec",
    "aapt2": "1a6a396b9cd071f7040071fdd108718cb98c3c9f4960044f373b288993d19eb7",
    "zipalign": "c5f559e946de5a9e7d58792181db20383b228877812136bc469d97ae00a43b0a",
    "platform_tools_package": "b7253bc2352e6bd5fdc2aa5da4f452ee4c3b6bdc93f20a87d39ee680a91af97c",
    "adb": "372d800c04c3272729afade8a85d95a70fb1c7e74062d9ab17a92eb7b618096c",
    "android_workload_manifest": "e520a5f491b933774ed06c48e8adf3a6878ad8a6cd320180a3395080cf362644",
    "maui_workload_manifest": "e2506ea1897fca4cf528fa2e950d3267477e28e5253f1e7781520058742ced10",
}

REPOSITORY_NAMES = (
    "chummer-android", "chummer6-ui", "chummer6-core", "chummer6-ui-kit",
    "chummer6-hub", "chummer6-hub-registry", "chummer6-media-factory",
    "chummer6-design",
)
REPOSITORY_ROLES = (
    "app", "runtime", "runtime", "runtime", "contracts_and_validation",
    "contracts", "contracts", "validation",
)
REPOSITORY_URLS = (
    "https://github.com/ArchonMegalon/chummer-android.git",
    "https://github.com/ArchonMegalon/chummer6-ui.git",
    "https://github.com/ArchonMegalon/chummer6-core.git",
    "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
    "https://github.com/ArchonMegalon/chummer6-hub.git",
    "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
    "https://github.com/ArchonMegalon/chummer6-media-factory.git",
    "https://github.com/ArchonMegalon/chummer6-design.git",
)
RELEASE_WORKSPACE_PATHS = (
    ("chummer-android",),
    ("chummer-presentation",),
    ("chummer-core-engine",),
    ("chummer-ui-kit",),
    ("chummer.run-services",),
    ("chummer-hub-registry",),
    ("fleet", "repos", "chummer-media-factory"),
    ("chummer-design",),
)
SOURCE_GRAPH_DOES_NOT_ASSERT = (
    "google_play_upload", "google_play_processing", "tester_installation",
    "production_rollout", "presentation_package_authority",
)
CORE_PACKAGE_IDS = (
    "Chummer.Application", "Chummer.Infrastructure", "Chummer.Rulesets.Hosting",
    "Chummer.Rulesets.Sr4", "Chummer.Rulesets.Sr5", "Chummer.Rulesets.Sr6",
)
OWNER_PACKAGE_SPECS = (
    ("Chummer.Campaign.Contracts", "chummer6-hub"),
    ("Chummer.Play.Contracts", "chummer6-hub"),
    ("Chummer.Run.Contracts", "chummer6-hub"),
    ("Chummer.Run.Hub.Contracts", "chummer6-hub"),
    ("Chummer.Run.Hub", "chummer6-hub"),
    ("Chummer.Hub.Registry.Contracts", "chummer6-hub-registry"),
    ("Chummer.Ui.Kit", "chummer6-ui-kit"),
)
OWNER_PACKAGE_IDS = tuple(row[0] for row in OWNER_PACKAGE_SPECS)
EXPECTED_PROJECT_LIBRARIES = (
    "Chummer.Desktop.Runtime/1.0.0", "Chummer.Presentation/1.0.0",
)
ALLOWED_POST_W5_PATHS = frozenset({
    "scripts/build-api36-physical-candidate.sh",
    "scripts/materialize-api36-physical-build-provenance.py",
    "tests/api36_physical_build_provenance.py",
    "tests/test_api36_physical_build_provenance.py",
    "src/Chummer.Android/packages.lock.json",
})
DOES_NOT_ASSERT = (
    "apk_install", "api36_device_execution", "physical_journey_pass",
    "google_play_upload", "google_play_processing", "tester_installation",
    "public_release_readiness", "publication_authority", "tablet_readiness",
)
SHA40_PATTERN = re.compile(r"[0-9a-f]{40}")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
VERSION_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?")
JAVA_VERSION_PATTERN = re.compile(r'^(?:openjdk|java) version "(?P<version>[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)"(?:\s.*)?$')
JAVAC_VERSION_PATTERN = re.compile(r'^javac (?P<version>[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)$')
COMMAND_JOURNAL_CONTRACT = "chummer.android.api36-bounded-command-journal/v1"
RAW_COMMAND_JOURNAL_CONTRACT = "chummer.android.api36-raw-command-journal/v1"
DELEGATE_COMMAND_JOURNAL_CONTRACT = "chummer.android.internal-phone-beta-command-journal/v1"
PER_PHASE_TIMEOUT_SECONDS = 1800.0
TOTAL_DEADLINE_SECONDS = 7200.0
EXPECTED_APK_BASENAME = f"{PACKAGE}-Signed.apk"
PHASE_NAMES = (
    "toolchain-intake", "source-graph-intake", "core-content-intake", "w5-build-input-intake",
    "locked-full-restore", "serialized-full-maui-build",
    "apk-signature-verification", "apk-content-verification",
    "post-build-source-graph-seal",
)
ENVIRONMENT_ALLOWLIST = frozenset({
    "ANDROID_HOME", "ANDROID_SDK_ROOT", "DOTNET_CLI_HOME", "DOTNET_CLI_TELEMETRY_OPTOUT",
    "DOTNET_CLI_USE_MSBUILD_SERVER", "HOME", "JAVA_HOME", "LANG", "LC_ALL",
    "MSBUILDDISABLENODEREUSE", "NUGET_PACKAGES", "PATH", "TMPDIR", "DOTNET_ROOT",
    "CHUMMER_RELEASE_WORKSPACE_ROOT",
})


@dataclass(frozen=True)
class StableFileSnapshot:
    path: Path
    label: str
    data: bytes
    sha256: str
    size: int
    device: int
    inode: int
    mode: int
    modified_ns: int
    changed_ns: int

    def binding(self) -> dict[str, object]:
        return {"sha256": self.sha256, "sizeBytes": self.size}


class SnapshotRegistry:
    """Capture regular files through one descriptor and reject later byte/identity drift."""

    def __init__(self) -> None:
        self._snapshots: dict[Path, StableFileSnapshot] = {}

    @staticmethod
    def _canonical(path: Path, label: str) -> Path:
        if not path.is_absolute():
            raise ValueError(f"{label} must be an absolute path")
        try:
            resolved = path.resolve(strict=True)
        except (FileNotFoundError, RuntimeError, OSError) as error:
            raise ValueError(f"{label} is missing or has an unsafe path") from error
        if resolved != path:
            raise ValueError(f"{label} path must contain no symlink component")
        return path

    @staticmethod
    def _read_descriptor(path: Path, label: str) -> StableFileSnapshot:
        SnapshotRegistry._canonical(path, label)
        flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
        try:
            descriptor = os.open(path, flags)
        except OSError as error:
            raise ValueError(f"{label} cannot be opened as a stable regular file") from error
        try:
            before = os.fstat(descriptor)
            if not stat.S_ISREG(before.st_mode):
                raise ValueError(f"{label} must be a regular file")
            chunks: list[bytes] = []
            while chunk := os.read(descriptor, 1024 * 1024):
                chunks.append(chunk)
            after = os.fstat(descriptor)
        finally:
            os.close(descriptor)
        identity_before = (
            before.st_dev, before.st_ino, before.st_mode, before.st_size,
            before.st_mtime_ns, before.st_ctime_ns,
        )
        identity_after = (
            after.st_dev, after.st_ino, after.st_mode, after.st_size,
            after.st_mtime_ns, after.st_ctime_ns,
        )
        if identity_before != identity_after:
            raise ValueError(f"{label} changed while it was being captured")
        data = b"".join(chunks)
        if len(data) != before.st_size:
            raise ValueError(f"{label} size changed while it was being captured")
        return StableFileSnapshot(
            path=path, label=label, data=data, sha256=hashlib.sha256(data).hexdigest(),
            size=len(data), device=before.st_dev, inode=before.st_ino,
            mode=before.st_mode, modified_ns=before.st_mtime_ns,
            changed_ns=before.st_ctime_ns,
        )

    def capture(self, path: Path, label: str) -> StableFileSnapshot:
        existing = self._snapshots.get(path)
        if existing is not None:
            return existing
        snapshot = self._read_descriptor(path, label)
        self._snapshots[path] = snapshot
        return snapshot

    def recheck_all(self) -> None:
        for original in self._snapshots.values():
            current = self._read_descriptor(original.path, original.label)
            if (
                current.sha256, current.size, current.device, current.inode,
                current.mode, current.modified_ns, current.changed_ns,
            ) != (
                original.sha256, original.size, original.device, original.inode,
                original.mode, original.modified_ns, original.changed_ns,
            ):
                raise ValueError(f"{original.label} changed before provenance seal")


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_strict_json_bytes(data: bytes, label: str) -> dict[str, object]:
    try:
        value = json.loads(
            data.decode("utf-8"),
            object_pairs_hook=reject_duplicate_keys,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite number {token}")
            ),
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one JSON object")
    return value


def load_strict_json(
    path: Path, label: str, snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    if snapshots is None:
        require_regular(path, label)
        data = path.read_bytes()
    else:
        data = snapshots.capture(path, label).data
    return load_strict_json_bytes(data, label)


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(
        value, allow_nan=False, ensure_ascii=False, separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_regular(path: Path, label: str) -> None:
    if not path.is_absolute():
        raise ValueError(f"{label} must be an absolute path")
    try:
        mode = path.lstat().st_mode
    except FileNotFoundError as error:
        raise ValueError(f"{label} is missing") from error
    if not stat.S_ISREG(mode) or path.is_symlink():
        raise ValueError(f"{label} must be a non-symlink regular file")
    if path.resolve(strict=True) != path:
        raise ValueError(f"{label} path must be canonical")


def require_directory(path: Path, label: str) -> None:
    if not path.is_absolute():
        raise ValueError(f"{label} must be an absolute path")
    try:
        mode = path.lstat().st_mode
    except FileNotFoundError as error:
        raise ValueError(f"{label} is missing") from error
    if not stat.S_ISDIR(mode) or path.is_symlink():
        raise ValueError(f"{label} must be a non-symlink directory")
    if path.resolve(strict=True) != path:
        raise ValueError(f"{label} path must be canonical")


def require_exact_keys(value: object, expected: set[str], label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be an object")
    if set(value) != expected:
        raise ValueError(
            f"{label} keys are not exact; missing={sorted(expected - set(value))}, "
            f"extra={sorted(set(value) - expected)}"
        )
    return value


def require_sha(value: object, label: str, *, length: int = 64) -> str:
    pattern = SHA256_PATTERN if length == 64 else SHA40_PATTERN
    if not isinstance(value, str) or pattern.fullmatch(value) is None:
        raise ValueError(f"{label} must be lowercase {length}-character hex")
    return value


def binding(path: Path, snapshots: SnapshotRegistry | None = None) -> dict[str, object]:
    if snapshots is not None:
        return snapshots.capture(path, str(path)).binding()
    require_regular(path, str(path))
    return {"sha256": file_sha256(path), "sizeBytes": path.stat().st_size}


def _git(root: Path, *arguments: str) -> str:
    return subprocess.run(
        ["git", "-C", os.fspath(root), *arguments], check=True,
        capture_output=True, text=True, timeout=30,
    ).stdout.strip()


def repository_identity(
    root: Path, *, label: str = "Android repository", require_clean: bool = True,
) -> dict[str, str]:
    require_directory(root, label)
    if Path(_git(root, "rev-parse", "--show-toplevel")) != root:
        raise ValueError(f"{label} root is not exact")
    if require_clean and _git(root, "status", "--porcelain=v1", "--untracked-files=all"):
        raise ValueError(f"{label} is dirty")
    return {
        "commit": require_sha(_git(root, "rev-parse", "HEAD"), "Android commit", length=40),
        "tree": require_sha(_git(root, "rev-parse", "HEAD^{tree}"), "Android tree", length=40),
    }


def validate_release_workspace_authority(
    release_workspace_root: Path, android_root: Path, graph: Mapping[str, object],
) -> dict[str, dict[str, str]]:
    require_directory(release_workspace_root, "release workspace authority root")
    repository_rows = graph.get("repositories")
    if not isinstance(repository_rows, list) or len(repository_rows) != len(REPOSITORY_NAMES):
        raise ValueError("release workspace authority requires the exact eight-repository graph")
    expected_android_root = release_workspace_root.joinpath(*RELEASE_WORKSPACE_PATHS[0])
    if android_root != expected_android_root:
        raise ValueError("Android root is not the exact release workspace chummer-android checkout")
    identities: dict[str, dict[str, str]] = {}
    row_keys = {"name", "role", "commit", "tree", "tree_sha256", "repository"}
    for index, (expected_name, expected_role, expected_url, relative_parts, row) in enumerate(
        zip(
            REPOSITORY_NAMES, REPOSITORY_ROLES, REPOSITORY_URLS,
            RELEASE_WORKSPACE_PATHS, repository_rows, strict=True,
        )
    ):
        row = require_exact_keys(row, row_keys, f"release workspace repository row {index}")
        if (
            row.get("name") != expected_name or row.get("role") != expected_role
            or row.get("repository") != expected_url
        ):
            raise ValueError("release workspace repository order/role/remote authority is not exact")
        require_sha(row.get("commit"), f"release workspace {expected_name} commit", length=40)
        require_sha(row.get("tree"), f"release workspace {expected_name} tree", length=40)
        require_sha(row.get("tree_sha256"), f"release workspace {expected_name} tree inventory")
        root = release_workspace_root.joinpath(*relative_parts)
        identity = repository_identity(root, label=f"release workspace {expected_name}")
        if identity != {"commit": row.get("commit"), "tree": row.get("tree")}:
            raise ValueError(f"release workspace repository identity drifted: {expected_name}")
        tree_listing = subprocess.run(
            ["git", "-C", os.fspath(root), "ls-tree", "-r", "-z", "--full-tree", "HEAD"],
            check=True, capture_output=True, timeout=30,
        ).stdout
        if hashlib.sha256(tree_listing).hexdigest() != row["tree_sha256"]:
            raise ValueError(f"release workspace tree inventory drifted: {expected_name}")
        try:
            remote = _git(root, "remote", "get-url", "origin")
        except subprocess.CalledProcessError as error:
            raise ValueError(f"release workspace repository remote is missing: {expected_name}") from error
        if remote != expected_url:
            raise ValueError(f"release workspace repository remote drifted: {expected_name}")
        identities[expected_name] = {**identity, "repository": remote}
    return identities


def _verify_w5_external(receipt: Path, evidence_directory: Path) -> Mapping[str, object]:
    verifier_path = Path(__file__).resolve().parents[1] / "scripts/verify_internal_phone_beta_compile_receipt.py"
    spec = importlib.util.spec_from_file_location("w5_compile_receipt_verifier", verifier_path)
    if spec is None or spec.loader is None:
        raise ValueError("W5 compile verifier cannot be loaded")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    try:
        spec.loader.exec_module(module)
    finally:
        sys.modules.pop(spec.name, None)
    return module.verify_receipt(receipt, evidence_directory, None)


def _verify_core_content_external(android_root: Path, core_root: Path) -> list[str]:
    verifier_path = android_root / "scripts/verify_android_content_bundle.py"
    spec = importlib.util.spec_from_file_location("api36_core_content_verifier", verifier_path)
    if spec is None or spec.loader is None:
        raise ValueError("Core content verifier cannot be loaded")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    try:
        spec.loader.exec_module(module)
    finally:
        sys.modules.pop(spec.name, None)
    manifest = load_strict_json(
        android_root / "src/Chummer.Android/Content/chummer-content-manifest.json",
        "committed Core content manifest",
    )
    return [
        *module.verify_project_contract(android_root),
        *module.verify_manifest_against_source(manifest, core_root),
    ]


def validate_w5_receipt(
    receipt_path: Path,
    evidence_directory: Path,
    *,
    verifier: Callable[[Path, Path], Mapping[str, object]] = _verify_w5_external,
    snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    receipt_snapshot = snapshots.capture(receipt_path, "W5 compile receipt") if snapshots else None
    require_regular(receipt_path, "W5 compile receipt")
    require_directory(evidence_directory, "W5 evidence directory")
    if (receipt_snapshot.sha256 if receipt_snapshot else file_sha256(receipt_path)) != W5_RECEIPT_SHA256:
        raise ValueError("W5 compile receipt digest is not the authorized PASS receipt")
    payload = load_strict_json(receipt_path, "W5 compile receipt", snapshots)
    exact = {
        "contractName": W5_CONTRACT, "status": "pass",
        "authorityClass": "internal_phone_beta_only", "publicationAuthorized": False,
        "dependencyMode": "locked_package_no_siblings", "packageOnly": True,
        "restoreLockedMode": True, "sourceCheckoutsPresent": False,
        "siblingsAllowed": False, "serializedBuild": True,
        "sdkVersion": DOTNET_SDK_VERSION, "androidCommit": W5_ANDROID_COMMIT,
        "androidTree": W5_ANDROID_TREE,
        "presentationCommit": W5_PRESENTATION_COMMIT,
        "presentationTree": W5_PRESENTATION_TREE,
        "authorityBindingSha256": W5_AUTHORITY_BINDING_SHA256,
        "lockSha256": W5_LOCK_SHA256,
        "proofScope": "Native.CompileCheck_dependency_only",
        "fullMauiBuild": False, "coreDataLangContentVerified": False,
    }
    for field, expected in exact.items():
        if payload.get(field) != expected:
            raise ValueError(f"W5 compile receipt authoritative field mismatch: {field}")
    evidence_rows = payload.get("evidence")
    if evidence_rows is not None:
        if not isinstance(evidence_rows, list) or not evidence_rows:
            raise ValueError("W5 evidence inventory is invalid")
        names: list[str] = []
        for index, row in enumerate(evidence_rows):
            row = require_exact_keys(row, {"path", "sha256", "sizeBytes"}, f"W5 evidence row {index}")
            name = row.get("path")
            if (
                not isinstance(name, str) or not name or name in names
                or Path(name).name != name or name in {".", ".."}
            ):
                raise ValueError("W5 evidence inventory path is not exact")
            names.append(name)
            if (
                not isinstance(row.get("sha256"), str)
                or SHA256_PATTERN.fullmatch(row["sha256"]) is None
                or type(row.get("sizeBytes")) is not int or row["sizeBytes"] < 0
            ):
                raise ValueError("W5 evidence digest/size types are invalid")
            evidence_snapshot = snapshots.capture(
                evidence_directory / name, f"W5 evidence {name}",
            ) if snapshots else None
            actual = evidence_snapshot.binding() if evidence_snapshot else binding(evidence_directory / name)
            if actual != {"sha256": row.get("sha256"), "sizeBytes": row.get("sizeBytes")}:
                raise ValueError(f"W5 evidence row does not bind bytes: {name}")
        with os.scandir(evidence_directory) as iterator:
            entries = list(iterator)
        if any(entry.is_symlink() or not entry.is_file(follow_symlinks=False) for entry in entries):
            raise ValueError("W5 evidence directory contains a non-regular entry")
        actual_names = sorted(entry.name for entry in entries)
        if actual_names != sorted(names):
            raise ValueError("W5 evidence directory inventory is not exact")
    verified = verifier(receipt_path, evidence_directory)
    if verified.get("status") != "pass" or verified.get("verifiedReceiptStatus") != "pass":
        raise ValueError("W5 compile receipt did not pass its committed verifier")
    return payload


def validate_source_graph(
    path: Path, android_identity: Mapping[str, str], snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    graph = load_strict_json(path, "release source graph", snapshots)
    require_exact_keys(graph, {
        "contractName", "generatedAtUtc", "authorityState", "publicationAuthorized",
        "generator", "repositories", "packagePins", "ownerPackagePins",
        "dependencyClosure", "presentationSource", "doesNotAssert",
    }, "release source graph")
    if (graph.get("contractName"), graph.get("authorityState"), graph.get("publicationAuthorized")) != (
        SOURCE_GRAPH_CONTRACT, "local_review_required", False,
    ):
        raise ValueError("release source graph authority posture is not exact")
    if (
        not isinstance(graph.get("generatedAtUtc"), str)
        or not graph["generatedAtUtc"].endswith("Z")
        or graph.get("doesNotAssert") != list(SOURCE_GRAPH_DOES_NOT_ASSERT)
    ):
        raise ValueError("release source graph review boundary is not exact")
    generator = require_exact_keys(
        graph.get("generator"), {"path", "sha256", "size_bytes"}, "source graph generator",
    )
    if generator.get("path") != "scripts/verify_release_source_graph.py":
        raise ValueError("release source graph generator path is not canonical")
    require_sha(generator.get("sha256"), "source graph generator digest")
    if not isinstance(generator.get("size_bytes"), int) or isinstance(generator.get("size_bytes"), bool) or generator["size_bytes"] <= 0:
        raise ValueError("release source graph generator size is invalid")

    rows = graph.get("repositories")
    if not isinstance(rows, list) or len(rows) != len(REPOSITORY_NAMES):
        raise ValueError("release source graph must contain exactly eight repositories")
    repositories: dict[str, dict[str, object]] = {}
    for expected_name, expected_role, expected_url, row in zip(
        REPOSITORY_NAMES, REPOSITORY_ROLES, REPOSITORY_URLS, rows, strict=True,
    ):
        row = require_exact_keys(
            row, {"name", "role", "commit", "tree", "tree_sha256", "repository"},
            f"source repository {expected_name}",
        )
        if row.get("name") != expected_name:
            raise ValueError("release source graph repository order/set is not exact")
        require_sha(row.get("commit"), f"{expected_name} commit", length=40)
        require_sha(row.get("tree"), f"{expected_name} tree", length=40)
        require_sha(row.get("tree_sha256"), f"{expected_name} tree inventory")
        if row.get("role") != expected_role or row.get("repository") != expected_url:
            raise ValueError(f"{expected_name} repository authority is invalid")
        repositories[expected_name] = row
    android = repositories["chummer-android"]
    if (android.get("commit"), android.get("tree")) != (
        android_identity["commit"], android_identity["tree"],
    ):
        raise ValueError("release source graph does not bind the current clean Android source")
    presentation_repository = repositories["chummer6-ui"]
    if (
        presentation_repository.get("commit") != PRODUCTION_PRESENTATION_COMMIT
        or presentation_repository.get("tree") != PRODUCTION_PRESENTATION_TREE
    ):
        raise ValueError("release source graph reviewed Presentation source row is not exact")

    package_rows = graph.get("packagePins")
    if not isinstance(package_rows, list) or len(package_rows) != len(CORE_PACKAGE_IDS):
        raise ValueError("release source graph must contain the exact six Core package pins")
    core_commit = repositories["chummer6-core"]["commit"]
    for expected_id, row in zip(CORE_PACKAGE_IDS, package_rows, strict=True):
        row = require_exact_keys(
            row, {"package_id", "version", "sha256", "repository", "commit"},
            f"Core package pin {expected_id}",
        )
        if (row.get("package_id"), row.get("repository"), row.get("commit")) != (
            expected_id, "chummer6-core", core_commit,
        ):
            raise ValueError("release source graph Core package pins are not exact and ordered")
        if not isinstance(row.get("version"), str) or VERSION_PATTERN.fullmatch(row["version"]) is None:
            raise ValueError(f"Core package version is invalid: {expected_id}")
        require_sha(row.get("sha256"), f"Core package digest {expected_id}")

    owner_rows = graph.get("ownerPackagePins")
    if not isinstance(owner_rows, list) or len(owner_rows) != len(OWNER_PACKAGE_SPECS):
        raise ValueError("release source graph must contain the exact seven owner package pins")
    owner_fields = {
        "package_id", "version", "sha256", "size_bytes", "owner_repository",
        "source_commit", "source_tree", "authority_receipt_sha256",
        "package_inventory_sha256", "package_plane_lock_sha256", "dependency_mode",
    }
    for (expected_id, expected_owner), row in zip(OWNER_PACKAGE_SPECS, owner_rows, strict=True):
        row = require_exact_keys(row, owner_fields, f"owner package pin {expected_id}")
        source = repositories[expected_owner]
        if (
            row.get("package_id") != expected_id
            or row.get("owner_repository") != expected_owner
            or row.get("source_commit") != source["commit"]
            or row.get("source_tree") != source["tree"]
            or row.get("dependency_mode") != "locked_package"
        ):
            raise ValueError("release source graph owner package authority is not exact and ordered")
        if not isinstance(row.get("version"), str) or VERSION_PATTERN.fullmatch(row["version"]) is None:
            raise ValueError(f"owner package version is invalid: {expected_id}")
        for field in ("sha256", "authority_receipt_sha256", "package_inventory_sha256", "package_plane_lock_sha256"):
            require_sha(row.get(field), f"owner package {field}: {expected_id}")
        if not isinstance(row.get("size_bytes"), int) or isinstance(row.get("size_bytes"), bool) or row["size_bytes"] <= 0:
            raise ValueError(f"owner package size is invalid: {expected_id}")

    closure = graph.get("dependencyClosure")
    if not isinstance(closure, list) or len(closure) != len(OWNER_PACKAGE_IDS):
        raise ValueError("release source graph owner dependency closure is incomplete")
    for expected_id, row in zip(OWNER_PACKAGE_IDS, closure, strict=True):
        row = require_exact_keys(row, {"package_id", "dependencies"}, f"closure {expected_id}")
        dependencies = row.get("dependencies")
        if row.get("package_id") != expected_id or not isinstance(dependencies, list) or dependencies != sorted(set(dependencies)):
            raise ValueError("release source graph owner dependency closure is not canonical")
        if expected_id == "Chummer.Run.Contracts" and "Chummer.Play.Contracts" not in dependencies:
            raise ValueError("release source graph is missing transitive Chummer.Play.Contracts")

    presentation = require_exact_keys(
        graph.get("presentationSource"),
        {"repository", "commit", "tree", "source_path", "authority_state", "publication_authorized", "dependency_mode"},
        "presentation source binding",
    )
    if presentation != {
        "repository": "chummer6-ui", "commit": PRODUCTION_PRESENTATION_COMMIT,
        "tree": PRODUCTION_PRESENTATION_TREE, "source_path": "chummer-presentation",
        "authority_state": "local_review_required", "publication_authorized": False,
        "dependency_mode": "source_compatibility",
    }:
        raise ValueError("release source graph production Presentation binding is not exact")
    return graph


def validate_release_package_authority_v2(
    path: Path, *, graph: Mapping[str, object], release_workspace_root: Path,
    snapshots: SnapshotRegistry,
) -> dict[str, object]:
    payload = load_strict_json(path, "v2 release package authority", snapshots)
    require_exact_keys(
        payload, {"contractName", "packagePins", "ownerPackagePins", "dependencyClosure"},
        "v2 release package authority",
    )
    if payload.get("contractName") != "chummer.android.release-package-authority/v2":
        raise ValueError("v2 release package authority contract is not exact")
    package_rows = payload.get("packagePins")
    if not isinstance(package_rows, list) or len(package_rows) != len(CORE_PACKAGE_IDS):
        raise ValueError("v2 release authority Core package graph is not exact")
    for index, (expected_id, raw, canonical) in enumerate(
        zip(CORE_PACKAGE_IDS, package_rows, graph["packagePins"], strict=True),
    ):
        raw = require_exact_keys(
            raw, {"package_id", "version", "sha256", "repository", "commit"},
            f"v2 Core package authority row {index}",
        )
        if raw != canonical or raw.get("package_id") != expected_id:
            raise ValueError("v2 release authority Core package graph differs from source graph")

    repository_roots = {
        "chummer6-hub": release_workspace_root / "chummer.run-services",
        "chummer6-hub-registry": release_workspace_root / "chummer-hub-registry",
        "chummer6-ui-kit": release_workspace_root / "chummer-ui-kit",
    }
    owner_rows = payload.get("ownerPackagePins")
    if not isinstance(owner_rows, list) or len(owner_rows) != len(OWNER_PACKAGE_SPECS):
        raise ValueError("v2 release authority owner package graph is not exact")
    raw_owner_fields = {
        "package_id", "version", "sha256", "size_bytes", "owner_repository",
        "source_commit", "source_tree", "authority_receipt", "package_inventory",
        "package_plane_lock", "dependency_mode",
    }
    for index, ((expected_id, expected_owner), raw, canonical) in enumerate(
        zip(OWNER_PACKAGE_SPECS, owner_rows, graph["ownerPackagePins"], strict=True),
    ):
        raw = require_exact_keys(raw, raw_owner_fields, f"v2 owner package authority row {index}")
        if (
            raw.get("package_id") != expected_id or raw.get("owner_repository") != expected_owner
            or any(raw.get(field) != canonical.get(field) for field in (
                "package_id", "version", "sha256", "size_bytes", "owner_repository",
                "source_commit", "source_tree", "dependency_mode",
            ))
        ):
            raise ValueError("v2 release authority owner package graph differs from source graph")
        owner_root = repository_roots[expected_owner]
        require_directory(owner_root, f"v2 authority owner repository {expected_owner}")
        for raw_field, graph_field in (
            ("authority_receipt", "authority_receipt_sha256"),
            ("package_inventory", "package_inventory_sha256"),
            ("package_plane_lock", "package_plane_lock_sha256"),
        ):
            contained = require_exact_keys(
                raw.get(raw_field), {"path", "sha256"}, f"{expected_id}.{raw_field}",
            )
            relative = contained.get("path")
            if not isinstance(relative, str) or not relative or relative != relative.strip():
                raise ValueError("v2 authority binding path is not canonical")
            posix = PurePosixPath(relative)
            if posix.is_absolute() or ".." in posix.parts or "\\" in relative:
                raise ValueError("v2 authority binding path escapes owner repository")
            bound_path = owner_root.joinpath(*posix.parts)
            snapshot = snapshots.capture(bound_path, f"{expected_id}.{raw_field}")
            if (
                contained.get("sha256") != snapshot.sha256
                or contained.get("sha256") != canonical.get(graph_field)
            ):
                raise ValueError("v2 authority binding digest differs from authenticated graph/bytes")

    closure = payload.get("dependencyClosure")
    if closure != graph.get("dependencyClosure"):
        raise ValueError("v2 release authority dependency graph differs from source graph")
    return payload


def validate_package_authority(
    path: Path, *, committed_path: Path, w5_receipt: Mapping[str, object],
    snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    require_regular(path, "internal package authority")
    require_regular(committed_path, "committed internal package authority")
    source_snapshot = snapshots.capture(path, "internal package authority") if snapshots else None
    committed_snapshot = snapshots.capture(committed_path, "committed internal package authority") if snapshots else None
    digest = source_snapshot.sha256 if source_snapshot else file_sha256(path)
    if digest != W5_AUTHORITY_BINDING_SHA256 or digest != w5_receipt.get("authorityBindingSha256"):
        raise ValueError("internal package authority digest is not W5-bound")
    if (
        source_snapshot.data if source_snapshot else path.read_bytes()
    ) != (
        committed_snapshot.data if committed_snapshot else committed_path.read_bytes()
    ):
        raise ValueError("internal package authority differs from the committed authority")
    payload = load_strict_json(path, "internal package authority", snapshots)
    if (
        payload.get("contractName") != PACKAGE_AUTHORITY_CONTRACT
        or payload.get("authorityClass") != "internal_phone_beta_only"
        or payload.get("authorityState") != "independently_audited"
        or payload.get("publicationAuthorized") is not False
        or payload.get("dependencyMode") != {
            "packageOnly": True, "restoreLockedMode": True,
            "sourceCheckoutsPresent": False, "siblingsAllowed": False,
        }
    ):
        raise ValueError("internal package authority posture is not exact")
    package_rows = payload.get("packagePins")
    owner_rows = payload.get("ownerPackagePins")
    if not isinstance(package_rows, list) or [row.get("package_id") for row in package_rows if isinstance(row, dict)] != list(CORE_PACKAGE_IDS):
        raise ValueError("internal package authority Core pins are not exact")
    if not isinstance(owner_rows, list) or [row.get("package_id") for row in owner_rows if isinstance(row, dict)] != list(OWNER_PACKAGE_IDS):
        raise ValueError("internal package authority owner pins are not exact")
    return payload


def validate_content_receipt(
    path: Path, *, apk: Path | None, source_binding: Mapping[str, object] | None = None,
    snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    receipt = load_strict_json(path, "Core content receipt", snapshots)
    require_exact_keys(receipt, {
        "status", "schema", "coreRevision", "bundleDigest", "manifestSha256",
        "apkSha256", "canonicalFileCount", "canonicalByteCount",
        "apkCanonicalFileCount", "apkVerified", "issues",
    }, "Core content receipt")
    if (
        receipt.get("status") != "pass" or receipt.get("schema") != CONTENT_CONTRACT
        or receipt.get("coreRevision") != CORE_CONTENT_REVISION or receipt.get("issues") != []
    ):
        raise ValueError("Core content receipt did not authenticate canonical content")
    require_sha(receipt.get("bundleDigest"), "Core content bundle digest")
    require_sha(receipt.get("manifestSha256"), "Core content manifest digest")
    count = receipt.get("canonicalFileCount")
    byte_count = receipt.get("canonicalByteCount")
    if not isinstance(count, int) or isinstance(count, bool) or count <= 0:
        raise ValueError("Core content canonical file count is invalid")
    if not isinstance(byte_count, int) or isinstance(byte_count, bool) or byte_count <= 0:
        raise ValueError("Core content canonical byte count is invalid")
    if apk is None:
        if receipt.get("apkVerified") is not False or receipt.get("apkSha256") is not None or receipt.get("apkCanonicalFileCount") != 0:
            raise ValueError("pre-build Core content receipt contains an APK success claim")
    else:
        apk_snapshot = snapshots.capture(apk, "ARM64 APK") if snapshots else None
        require_regular(apk, "ARM64 APK")
        if (
            receipt.get("apkVerified") is not True
            or receipt.get("apkSha256") != (apk_snapshot.sha256 if apk_snapshot else file_sha256(apk))
            or receipt.get("apkCanonicalFileCount") != count
        ):
            raise ValueError("post-build Core content receipt does not bind the complete APK content")
    if source_binding is not None:
        for field in ("coreRevision", "bundleDigest", "manifestSha256", "canonicalFileCount", "canonicalByteCount"):
            if receipt.get(field) != source_binding.get(field):
                raise ValueError(f"pre/post Core content receipt mismatch: {field}")
    return receipt


def validate_full_project_lock(
    path: Path, snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    lock = load_strict_json(path, "full-project package lock", snapshots)
    require_exact_keys(lock, {"version", "dependencies"}, "full-project package lock")
    if lock.get("version") != 1:
        raise ValueError("full-project package lock version must be 1")
    frameworks = lock.get("dependencies")
    rid_framework = f"{TARGET_FRAMEWORK}/{RUNTIME_IDENTIFIER}"
    if not isinstance(frameworks, dict) or tuple(frameworks) != (TARGET_FRAMEWORK, rid_framework):
        raise ValueError("full-project package lock framework is not exact")
    if frameworks[rid_framework] != {}:
        raise ValueError("full-project ARM64 lock target must be present and canonical")
    packages = frameworks[TARGET_FRAMEWORK]
    if not isinstance(packages, dict):
        raise ValueError("full-project package lock package table is invalid")
    required_direct = {
        "Microsoft.Maui.Controls": "10.0.20",
        "Microsoft.Extensions.Logging.Debug": "10.0.0",
        "Xamarin.Google.Android.Play.App.Update": "2.1.0.19",
        "Xamarin.Google.Android.Play.Review": "2.0.2.9",
        "Xamarin.AndroidX.Activity.Ktx": "1.13.0.1",
        "Xamarin.AndroidX.Collection.Ktx": "1.6.0.1",
        "Xamarin.AndroidX.Fragment.Ktx": "1.8.9.4",
        "Xamarin.AndroidX.Lifecycle.LiveData": "2.11.0.1",
        "Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx": "2.11.0.1",
        "Xamarin.AndroidX.Lifecycle.Process": "2.11.0.1",
        "Xamarin.AndroidX.Lifecycle.Runtime.Ktx": "2.11.0.1",
        "Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android": "2.11.0.1",
        "Xamarin.AndroidX.Lifecycle.ViewModel.Ktx": "2.11.0.1",
        "Xamarin.AndroidX.SavedState.SavedState.Ktx": "1.5.0.1",
    }
    for package_id, version in required_direct.items():
        row = packages.get(package_id)
        if not isinstance(row, dict) or row.get("type") != "Direct" or row.get("requested") != f"[{version}, )" or row.get("resolved") != version:
            raise ValueError(f"full-project direct package pin is invalid: {package_id}")
        if not isinstance(row.get("contentHash"), str) or not row["contentHash"]:
            raise ValueError(f"full-project content hash is invalid: {package_id}")
    for package_id in (*CORE_PACKAGE_IDS, *OWNER_PACKAGE_IDS):
        row = packages.get(package_id)
        if not isinstance(row, dict) or row.get("type") != "Transitive":
            raise ValueError(f"full-project W5 package closure is missing: {package_id}")
        if not isinstance(row.get("contentHash"), str) or not row["contentHash"]:
            raise ValueError(f"full-project content hash is invalid: {package_id}")
    return lock


def validate_assets(
    path: Path, *, package_authority: Mapping[str, object],
    snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    assets = load_strict_json(path, "full-project restore assets", snapshots)
    libraries = assets.get("libraries")
    if not isinstance(libraries, dict):
        raise ValueError("full-project restore assets libraries are invalid")
    projects = tuple(sorted(
        name for name, row in libraries.items()
        if isinstance(row, dict) and row.get("type") == "project"
    ))
    if projects != EXPECTED_PROJECT_LIBRARIES:
        raise ValueError("full-project restore assets project closure is not exact")
    expected_versions = {
        row["package_id"]: row["version"]
        for group in (package_authority["packagePins"], package_authority["ownerPackagePins"])
        for row in group
    }
    for package_id, version in expected_versions.items():
        row = libraries.get(f"{package_id}/{version}")
        if not isinstance(row, dict) or row.get("type") != "package":
            raise ValueError(f"full-project restore assets package closure mismatch: {package_id}")
    return assets


def validate_apk_output_directory(
    path: Path, snapshots: SnapshotRegistry | None = None,
) -> StableFileSnapshot | None:
    if path.name != EXPECTED_APK_BASENAME:
        raise ValueError("APK basename is not the expected signed package")
    require_directory(path.parent, "APK output directory")
    apk_entries: list[str] = []
    with os.scandir(path.parent) as entries:
        for entry in entries:
            if entry.is_symlink():
                raise ValueError("APK output directory contains a symlink")
            if entry.name.lower().endswith(".apk"):
                if not entry.is_file(follow_symlinks=False):
                    raise ValueError("APK output directory contains a non-regular APK")
                apk_entries.append(entry.name)
    if apk_entries != [EXPECTED_APK_BASENAME]:
        raise ValueError(f"APK output inventory must contain exactly one signed ARM64 APK: {sorted(apk_entries)!r}")
    return snapshots.capture(path, "signed ARM64 APK") if snapshots else None


def apk_abis(path: Path, snapshots: SnapshotRegistry | None = None) -> list[str]:
    snapshot = snapshots.capture(path, "ARM64 APK") if snapshots else None
    require_regular(path, "ARM64 APK")
    try:
        with zipfile.ZipFile(io.BytesIO(snapshot.data) if snapshot else path) as archive:
            abis = sorted({
                parts[1] for name in archive.namelist()
                if len(parts := name.split("/")) >= 3 and parts[0] == "lib" and parts[1]
            })
    except (OSError, zipfile.BadZipFile) as error:
        raise ValueError("ARM64 artifact is not a readable APK") from error
    if abis != ["arm64-v8a"]:
        raise ValueError(f"APK ABI closure must be exactly arm64-v8a: {abis!r}")
    return abis


def _probe_version(executable: Path, label: str) -> str:
    completed = subprocess.run(
        [os.fspath(executable), "-version"], check=False, capture_output=True,
        text=True, timeout=20, env={"LC_ALL": "C", "PATH": os.environ.get("PATH", "")},
    )
    output = "\n".join(part for part in (completed.stdout, completed.stderr) if part).strip()
    lines = [line.strip() for line in output.splitlines() if line.strip()]
    if completed.returncode != 0 or not lines:
        raise ValueError(f"{label} identity command failed")
    return lines[0]


def require_native_executable(
    path: Path, label: str, snapshots: SnapshotRegistry,
) -> StableFileSnapshot:
    snapshot = snapshots.capture(path, label)
    if snapshot.mode & 0o111 == 0:
        raise ValueError(f"{label} must be executable")
    native_magics = (
        b"\x7fELF", b"MZ", b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe",
        b"\xfe\xed\xfa\xcf", b"\xfe\xed\xfa\xce",
    )
    if not any(snapshot.data.startswith(magic) for magic in native_magics):
        raise ValueError(f"{label} must be a native binary, not a script or shim")
    return snapshot


def require_authorized_toolchain_digest(snapshot: StableFileSnapshot, authority_name: str) -> None:
    expected = TOOLCHAIN_SHA256_AUTHORITY.get(authority_name)
    if expected is None or snapshot.sha256 != expected:
        raise ValueError(f"toolchain bytes are not release-authorized: {authority_name}")




def _android_package_identity(data: bytes, label: str) -> tuple[str, str]:
    try:
        root = ET.fromstring(data)
    except ET.ParseError as error:
        raise ValueError(f"{label} is not well-formed XML") from error
    packages = [element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == "localPackage"]
    if len(packages) != 1:
        raise ValueError(f"{label} must contain exactly one localPackage")
    package = packages[0]
    package_path = package.get("path")
    if (
        not isinstance(package_path, str) or not package_path
        or re.fullmatch(r"[A-Za-z0-9._+-]+(?:;[A-Za-z0-9._+-]+)*", package_path) is None
    ):
        raise ValueError(f"{label} localPackage path is invalid")
    revisions = [element for element in package if element.tag.rsplit("}", 1)[-1] == "revision"]
    if len(revisions) != 1:
        raise ValueError(f"{label} must contain one revision")
    values: dict[str, int] = {}
    for child in revisions[0]:
        name = child.tag.rsplit("}", 1)[-1]
        if name not in {"major", "minor", "micro"} or name in values:
            raise ValueError(f"{label} revision is not canonical")
        if child.text is None or re.fullmatch(r"0|[1-9][0-9]*", child.text) is None:
            raise ValueError(f"{label} revision value is invalid")
        values[name] = int(child.text)
    if "major" not in values:
        raise ValueError(f"{label} revision has no major version")
    version = ".".join(str(values.get(name, 0)) for name in ("major", "minor", "micro"))
    return package_path, version


def _load_workload_manifest(
    path: Path, *, expected_version: str, label: str, snapshots: SnapshotRegistry,
) -> dict[str, object]:
    if path.parent.name != expected_version:
        raise ValueError(f"{label} path does not select the expected manifest version")
    payload = load_strict_json(path, label, snapshots)
    version = payload.get("version")
    if version is not None and version != expected_version:
        raise ValueError(f"{label} payload version is not exact")
    return payload


def validate_toolchain(
    *, java_path: Path, javac_path: Path, dotnet_path: Path,
    jarsigner_path: Path, apksigner_path: Path,
    dotnet_workloads_path: Path, android_sdk_packages_path: Path,
    android_sdk_root: Path, android_workload_manifest_path: Path,
    maui_workload_manifest_path: Path,
    android_build_tools_version: str, dotnet_version: str,
    snapshots: SnapshotRegistry,
) -> dict[str, object]:
    if dotnet_version != DOTNET_SDK_VERSION:
        raise ValueError("full MAUI build SDK selection drifted")
    if dotnet_path != DOTNET_HOST_AUTHORITY:
        raise ValueError(".NET host path is not the release-authorized host")
    if android_sdk_root != ANDROID_SDK_ROOT_AUTHORITY:
        raise ValueError("Android SDK root is not the release-authorized root")
    if (
        android_workload_manifest_path != ANDROID_WORKLOAD_MANIFEST_AUTHORITY
        or maui_workload_manifest_path != MAUI_WORKLOAD_MANIFEST_AUTHORITY
    ):
        raise ValueError("workload manifest paths are not release-authorized")
    if android_build_tools_version != "36.0.0":
        raise ValueError("Android build-tools selection is not exact")

    dotnet_snapshot = require_native_executable(dotnet_path, ".NET host", snapshots)
    java_snapshot = require_native_executable(java_path, "Java runtime", snapshots)
    javac_snapshot = require_native_executable(javac_path, "Java compiler", snapshots)
    jarsigner_snapshot = require_native_executable(jarsigner_path, "JDK jarsigner", snapshots)
    keytool_snapshot = require_native_executable(
        JDK_ROOT_AUTHORITY / "bin/keytool", "JDK keytool", snapshots,
    )
    for snapshot, authority_name in (
        (dotnet_snapshot, "dotnet"), (java_snapshot, "java"), (javac_snapshot, "javac"),
        (jarsigner_snapshot, "jarsigner"), (keytool_snapshot, "keytool"),
    ):
        require_authorized_toolchain_digest(snapshot, authority_name)
    if (
        java_path != JDK_ROOT_AUTHORITY / "bin/java"
        or javac_path != JDK_ROOT_AUTHORITY / "bin/javac"
        or jarsigner_path != JDK_ROOT_AUTHORITY / "bin/jarsigner"
    ):
        raise ValueError("Java tools are not exact sibling native binaries from the authorized JDK")
    java_line = _probe_version(java_path, "Java runtime")
    javac_line = _probe_version(javac_path, "Java compiler")
    java_match = JAVA_VERSION_PATTERN.fullmatch(java_line)
    javac_match = JAVAC_VERSION_PATTERN.fullmatch(javac_line)
    if java_match is None or javac_match is None or java_match.group("version") != javac_match.group("version"):
        raise ValueError("Java runtime/compiler version output is not canonical and identical")
    if java_match.group("version") != "17.0.14":
        raise ValueError("JDK version is not release-authorized")
    release_snapshot = snapshots.capture(JDK_ROOT_AUTHORITY / "release", "JDK release identity")
    require_authorized_toolchain_digest(release_snapshot, "jdk_release")
    try:
        release_text = release_snapshot.data.decode("utf-8")
    except UnicodeError as error:
        raise ValueError("JDK release identity is not UTF-8") from error
    release_fields: dict[str, str] = {}
    for line in release_text.splitlines():
        if not line:
            continue
        key, separator, value = line.partition("=")
        if (
            not separator or key in release_fields
            or re.fullmatch(r"[A-Z][A-Z0-9_]*", key) is None
            or len(value) < 2 or not value.startswith('"') or not value.endswith('"')
        ):
            raise ValueError("JDK release identity is not canonical")
        release_fields[key] = value[1:-1]
    if (
        release_fields.get("JAVA_VERSION") != "17.0.14"
        or "Microsoft" not in release_fields.get("IMPLEMENTOR", "")
    ):
        raise ValueError("JDK release identity is not Microsoft OpenJDK 17.0.14")

    workloads = load_strict_json(dotnet_workloads_path, ".NET workload inventory", snapshots)
    require_exact_keys(workloads, {
        "installed", "updateAvailable", "workloadSetVersion", "manifestVersions",
        "runtimeVersion",
    }, ".NET workload inventory")
    if workloads != {
        "installed": ["maui-android"], "updateAvailable": [],
        "workloadSetVersion": WORKLOAD_SET_VERSION,
        "manifestVersions": {
            "maui-android": MAUI_ANDROID_MANIFEST_VERSION,
            "microsoft.net.sdk.android": ANDROID_WORKLOAD_MANIFEST_VERSION,
        },
        "runtimeVersion": DOTNET_RUNTIME_VERSION,
    }:
        raise ValueError(".NET workload inventory is not the authorized exact set")
    android_manifest = _load_workload_manifest(
        android_workload_manifest_path, expected_version=ANDROID_WORKLOAD_MANIFEST_VERSION,
        label="Android workload manifest", snapshots=snapshots,
    )
    maui_manifest = _load_workload_manifest(
        maui_workload_manifest_path, expected_version="10.0.20",
        label="MAUI workload manifest", snapshots=snapshots,
    )
    require_authorized_toolchain_digest(
        snapshots.capture(android_workload_manifest_path, "Android workload manifest"),
        "android_workload_manifest",
    )
    require_authorized_toolchain_digest(
        snapshots.capture(maui_workload_manifest_path, "MAUI workload manifest"),
        "maui_workload_manifest",
    )

    inventory_snapshot = snapshots.capture(android_sdk_packages_path, "Android SDK selected package inventory")
    try:
        inventory_root = ET.fromstring(inventory_snapshot.data)
    except ET.ParseError as error:
        raise ValueError("Android SDK selected package inventory is not XML") from error
    inventory_packages = [
        element for element in inventory_root.iter()
        if element.tag.rsplit("}", 1)[-1] == "localPackage"
    ]
    if (
        inventory_root.tag.rsplit("}", 1)[-1] != "repository"
        or inventory_root.attrib or list(inventory_root) != inventory_packages
        or len(inventory_packages) != 3
    ):
        raise ValueError("Android SDK selected package inventory must contain exactly three packages")
    combined: dict[str, str] = {}
    for package in inventory_packages:
        package_children = list(package)
        if (
            set(package.attrib) != {"path"} or len(package_children) != 1
            or package_children[0].tag.rsplit("}", 1)[-1] != "revision"
            or package_children[0].attrib
            or [child.tag.rsplit("}", 1)[-1] for child in package_children[0]]
            != ["major", "minor", "micro"]
            or any(child.attrib or list(child) for child in package_children[0])
        ):
            raise ValueError("Android SDK selected package inventory schema is not canonical")
        wrapper = ET.Element("repository")
        wrapper.append(ET.fromstring(ET.tostring(package, encoding="utf-8")))
        package_id, revision = _android_package_identity(
            ET.tostring(wrapper, encoding="utf-8"), "Android SDK selected package",
        )
        if package_id in combined:
            raise ValueError("Android SDK selected package inventory contains duplicates")
        combined[package_id] = revision
    expected_packages = {
        "platforms;android-36": "2.0.0", "build-tools;36.0.0": "36.0.0",
        "platform-tools": "36.0.0",
    }
    if combined != expected_packages:
        raise ValueError("Android SDK selected package inventory is not exact")
    actual_package_files = {
        "platforms;android-36": android_sdk_root / "platforms/android-36/package.xml",
        "build-tools;36.0.0": android_sdk_root / "build-tools/36.0.0/package.xml",
        "platform-tools": android_sdk_root / "platform-tools/package.xml",
    }
    actual_packages: dict[str, dict[str, object]] = {}
    package_authority_names = {
        "platforms;android-36": "platform_package",
        "build-tools;36.0.0": "build_tools_package",
        "platform-tools": "platform_tools_package",
    }
    for expected_id, package_path in actual_package_files.items():
        package_snapshot = snapshots.capture(package_path, f"Android SDK {expected_id} package identity")
        identity, revision = _android_package_identity(package_snapshot.data, f"Android SDK {expected_id} package identity")
        if identity != expected_id or revision != expected_packages[expected_id]:
            raise ValueError(f"installed Android SDK package identity drifted: {expected_id}")
        require_authorized_toolchain_digest(package_snapshot, package_authority_names[expected_id])
        actual_packages[expected_id] = {**package_snapshot.binding(), "revision": revision}

    platform_root = android_sdk_root / "platforms/android-36"
    build_tools_root = android_sdk_root / "build-tools/36.0.0"
    platform_tools_root = android_sdk_root / "platform-tools"
    if apksigner_path != build_tools_root / "apksigner":
        raise ValueError("apksigner path is not the exact selected Android build-tools wrapper")
    android_jar = snapshots.capture(platform_root / "android.jar", "API36 android.jar")
    aapt2 = require_native_executable(build_tools_root / "aapt2", "Android aapt2", snapshots)
    zipalign = require_native_executable(build_tools_root / "zipalign", "Android zipalign", snapshots)
    adb = require_native_executable(platform_tools_root / "adb", "Android platform-tools adb", snapshots)
    apksigner_snapshot = snapshots.capture(apksigner_path, "Android apksigner wrapper")
    if apksigner_snapshot.mode & 0o111 == 0:
        raise ValueError("Android apksigner wrapper must be executable")
    apksigner_jar = snapshots.capture(build_tools_root / "lib/apksigner.jar", "Android apksigner jar")
    for snapshot, authority_name in (
        (android_jar, "android_jar"), (aapt2, "aapt2"), (zipalign, "zipalign"),
        (adb, "adb"), (apksigner_snapshot, "apksigner"),
        (apksigner_jar, "apksigner_jar"),
    ):
        require_authorized_toolchain_digest(snapshot, authority_name)
    return {
        "dotnetSdkVersion": DOTNET_SDK_VERSION,
        "dotnetRuntimeVersion": DOTNET_RUNTIME_VERSION,
        "workloadSetVersion": WORKLOAD_SET_VERSION,
        "dotnetHost": dotnet_snapshot.binding(),
        "dotnetWorkloads": {
            **snapshots.capture(dotnet_workloads_path, ".NET workload inventory").binding(),
            **workloads,
        },
        "workloadManifests": {
            "android": {**snapshots.capture(android_workload_manifest_path, "Android workload manifest").binding(), "version": ANDROID_WORKLOAD_MANIFEST_VERSION},
            "maui": {**snapshots.capture(maui_workload_manifest_path, "MAUI workload manifest").binding(), "version": "10.0.20"},
        },
        "java": {**java_snapshot.binding(), "version": "17.0.14", "versionLine": java_line},
        "javac": {**javac_snapshot.binding(), "version": "17.0.14", "versionLine": javac_line},
        "jarsigner": jarsigner_snapshot.binding(), "keytool": keytool_snapshot.binding(),
        "jdkRelease": {**release_snapshot.binding(), "fields": release_fields},
        "androidSdk": {
            "root": os.fspath(android_sdk_root), "selectedInventory": inventory_snapshot.binding(),
            "installedPackages": actual_packages, "androidJar": android_jar.binding(),
            "aapt2": aapt2.binding(), "zipalign": zipalign.binding(), "adb": adb.binding(),
            "apksigner": apksigner_snapshot.binding(), "apksignerJar": apksigner_jar.binding(),
        },
        "androidBuildToolsVersion": "36.0.0", "androidPlatformLabel": "Android 16",
        "targetFramework": TARGET_FRAMEWORK, "targetSdkVersion": 36,
        "runtimeIdentifier": RUNTIME_IDENTIFIER, "configuration": CONFIGURATION,
        "serializedBuild": True,
    }


def validate_apk_signing(
    *, receipt_path: Path, apksigner_log_path: Path, jarsigner_log_path: Path,
    apk: Path, apksigner_path: Path, jarsigner_path: Path,
    snapshots: SnapshotRegistry,
) -> dict[str, object]:
    receipt = load_strict_json(receipt_path, "APK signing receipt", snapshots)
    require_exact_keys(receipt, {
        "contractName", "status", "apkSha256", "certificateSha256",
        "verifiedSchemes", "apksignerSha256", "jarsignerSha256",
        "apksignerOutputSha256", "jarsignerOutputSha256", "warningsAsErrors",
        "publicationAuthorized",
    }, "APK signing receipt")
    apk_snapshot = snapshots.capture(apk, "signed ARM64 APK")
    apksigner_snapshot = snapshots.capture(apksigner_path, "Android apksigner wrapper")
    jarsigner_snapshot = snapshots.capture(jarsigner_path, "JDK jarsigner")
    apksigner_log = snapshots.capture(apksigner_log_path, "apksigner verification log")
    jarsigner_log = snapshots.capture(jarsigner_log_path, "jarsigner verification log")
    schemes = receipt.get("verifiedSchemes")
    certificate = receipt.get("certificateSha256")
    if (
        receipt.get("contractName") != "chummer.android.apk-signing-verification/v1"
        or receipt.get("status") != "pass" or receipt.get("publicationAuthorized") is not False
        or receipt.get("warningsAsErrors") is not True
        or receipt.get("apkSha256") != apk_snapshot.sha256
        or receipt.get("apksignerSha256") != apksigner_snapshot.sha256
        or receipt.get("jarsignerSha256") != jarsigner_snapshot.sha256
        or receipt.get("apksignerOutputSha256") != apksigner_log.sha256
        or receipt.get("jarsignerOutputSha256") != jarsigner_log.sha256
        or not isinstance(certificate, str) or SHA256_PATTERN.fullmatch(certificate) is None
        or not isinstance(schemes, list) or schemes != sorted(set(schemes))
        or not all(type(value) is int and value in (1, 2, 3, 4) for value in schemes)
        or not set(schemes).intersection({2, 3, 4})
    ):
        raise ValueError("APK signing receipt does not bind a structural modern signature")
    try:
        apksigner_text = apksigner_log.data.decode("utf-8")
        jarsigner_text = jarsigner_log.data.decode("utf-8")
    except UnicodeError as error:
        raise ValueError("APK signing verification logs must be UTF-8") from error
    for scheme in schemes:
        if f"Verified using v{scheme} scheme" not in apksigner_text:
            raise ValueError("apksigner log does not bind every claimed signing scheme")
    if (
        f"Signer #1 certificate SHA-256 digest: {certificate}" not in apksigner_text
        or "jar verified" not in jarsigner_text.lower()
    ):
        raise ValueError("APK signing logs do not bind certificate and verification success")
    return receipt


def _require_argv_value(argv: list[str], option: str, expected: str) -> None:
    try:
        index = argv.index(option)
    except ValueError as error:
        raise ValueError(f"bounded command is missing {option}") from error
    if index + 1 >= len(argv) or argv[index + 1] != expected:
        raise ValueError(f"bounded command {option} value is not exact")


def validate_phase_argv(
    phase: str, argv: object, *, android_root: Path, apk: Path,
    source_graph_path: Path, content_source_receipt_path: Path,
    content_apk_receipt_path: Path, android_build_tools_version: str,
    python_path: Path, dotnet_path: Path, presentation_root: Path,
    core_content_root: Path, release_workspace_root: Path,
    release_package_authority_v2_path: Path, package_authority_path: Path,
    w5_receipt_path: Path, w5_evidence_directory: Path,
    full_project_lock_path: Path, package_feed_path: Path,
    offline_feed_path: Path, nuget_packages_path: Path,
    dotnet_workloads_path: Path,
    android_sdk_packages_path: Path, android_sdk_root: Path,
    android_workload_manifest_path: Path, maui_workload_manifest_path: Path,
    java_path: Path,
    apksigner_path: Path, jarsigner_path: Path,
    signing_receipt_path: Path, apksigner_log_path: Path, jarsigner_log_path: Path,
) -> list[str]:
    if not isinstance(argv, list) or not argv or not all(isinstance(value, str) and value for value in argv):
        raise ValueError("bounded command argv must be a non-empty string array")
    executable = Path(argv[0])
    require_regular(executable, f"{phase} executable")
    if executable.stat().st_mode & 0o111 == 0:
        raise ValueError(f"{phase} executable is not executable")
    project = os.fspath(android_root / "src/Chummer.Android/Chummer.Android.csproj")
    content_manifest = os.fspath(android_root / "src/Chummer.Android/Content/chummer-content-manifest.json")
    materializer = os.fspath(android_root / "scripts/materialize-api36-physical-build-provenance.py")
    graph_verifier = os.fspath(android_root / "scripts/verify_release_source_graph.py")
    content_verifier = os.fspath(android_root / "scripts/verify_android_content_bundle.py")
    package_args = [
        f"-p:ChummerPresentationRoot={presentation_root}",
        f"-p:ChummerCoreEngineRoot={core_content_root}",
        "-p:ChummerDesktopRuntimeIdentifiers=",
        "-p:ChummerUseLocalCompatibilityTree=false",
        "-p:ChummerUseLockedOwnerContractPackages=true",
        "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
        "-p:NuGetAudit=false",
        f"-p:AndroidSdkDirectory={android_sdk_root}",
        f"-p:AndroidSdkBuildToolsVersion={android_build_tools_version}",
        f"-p:JavaSdkDirectory={java_path.parent.parent}",
        "-p:ChummerContractsPackageVersion=0.1.0-packageplane.breaking.shb04ff26f6d538.auth91a48eed5b819",
        "-p:ChummerCoreRuntimePackageVersion=0.1.0-packageplane.breaking.shb04ff26f6d538.auth91a48eed5b819",
        "-p:ChummerCampaignContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
        "-p:ChummerRunContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
        "-p:ChummerRunHubContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
        "-p:ChummerRunHubPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
        "-p:ChummerHubRegistryContractsPackageVersion=0.1.0-packageplane.candidate.sh66c418a5004f",
        "-p:ChummerUiKitPackageVersion=0.1.0-packageplane.android.shd51ecd99cf720",
    ]
    if phase == "source-graph-intake":
        expected = [
            os.fspath(python_path), graph_verifier, "--android-root", os.fspath(android_root),
            "--workspace-root", os.fspath(release_workspace_root),
            "--package-authority", os.fspath(release_package_authority_v2_path),
            "--verify-existing", os.fspath(source_graph_path),
        ]
    elif phase == "toolchain-intake":
        expected = [
            os.fspath(python_path), materializer, "capture-workloads", "--dotnet",
            os.fspath(dotnet_path), "--android-workload-manifest",
            os.fspath(android_workload_manifest_path), "--maui-workload-manifest",
            os.fspath(maui_workload_manifest_path), "--android-sdk-packages",
            os.fspath(android_sdk_packages_path), "--android-sdk-root", os.fspath(android_sdk_root),
            "--java", os.fspath(java_path), "--javac", os.fspath(java_path.parent / "javac"),
            "--jarsigner", os.fspath(jarsigner_path), "--apksigner", os.fspath(apksigner_path),
            "--output", os.fspath(dotnet_workloads_path),
        ]
    elif phase == "core-content-intake":
        expected = [
            os.fspath(python_path), content_verifier, "--repo-root", os.fspath(android_root),
            "--core-root", os.fspath(core_content_root), "--manifest", content_manifest,
            "--receipt", os.fspath(content_source_receipt_path), "--check",
        ]
    elif phase == "w5-build-input-intake":
        expected = [
            os.fspath(python_path), materializer, "check-inputs", "--android-root", os.fspath(android_root),
            "--presentation-root", os.fspath(presentation_root), "--core-content-root", os.fspath(core_content_root),
            "--w5-receipt", os.fspath(w5_receipt_path), "--w5-evidence-directory", os.fspath(w5_evidence_directory),
            "--source-graph", os.fspath(source_graph_path), "--package-authority", os.fspath(package_authority_path),
            "--release-package-authority-v2", os.fspath(release_package_authority_v2_path),
            "--release-workspace-root", os.fspath(release_workspace_root),
            "--content-source-receipt", os.fspath(content_source_receipt_path),
            "--full-project-lock", os.fspath(full_project_lock_path),
        ]
    elif phase == "locked-full-restore":
        expected = [
            os.fspath(dotnet_path), "restore", project, "--locked-mode", "--disable-parallel",
            "--no-http-cache", "--packages", os.fspath(nuget_packages_path),
            "--source", os.fspath(package_feed_path), "--source", os.fspath(offline_feed_path),
            *package_args,
        ]
    elif phase == "serialized-full-maui-build":
        expected = [
            os.fspath(dotnet_path), "build", project, "--configuration", CONFIGURATION,
            "--framework", TARGET_FRAMEWORK, "--runtime", RUNTIME_IDENTIFIER,
            "--no-restore", "--warnaserror", "-m:1", "-nr:false",
            "--disable-build-servers", "-p:UseSharedCompilation=false",
            "-p:BuildInParallel=false", "-p:AndroidPackageFormats=apk", *package_args,
        ]
    elif phase == "apk-signature-verification":
        expected = [
            os.fspath(python_path), materializer, "verify-apk-signing",
            "--apk", os.fspath(apk), "--apksigner", os.fspath(apksigner_path),
            "--jarsigner", os.fspath(jarsigner_path), "--receipt", os.fspath(signing_receipt_path),
            "--apksigner-log", os.fspath(apksigner_log_path),
            "--jarsigner-log", os.fspath(jarsigner_log_path),
        ]
    elif phase == "apk-content-verification":
        expected = [
            os.fspath(python_path), content_verifier, "--repo-root", os.fspath(android_root),
            "--core-root", os.fspath(core_content_root), "--manifest", content_manifest,
            "--apk", os.fspath(apk), "--receipt", os.fspath(content_apk_receipt_path), "--check",
        ]
    elif phase == "post-build-source-graph-seal":
        expected = [
            os.fspath(python_path), graph_verifier, "--android-root", os.fspath(android_root),
            "--workspace-root", os.fspath(release_workspace_root), "--package-authority",
            os.fspath(release_package_authority_v2_path), "--verify-existing", os.fspath(source_graph_path),
        ]
    else:
        raise ValueError("bounded command phase is outside the exact build contract")
    if argv != expected:
        raise ValueError(f"{phase} argv is not exact")
    return argv


def validate_execution_evidence(
    toolchain_log: Path, source_graph_log: Path, content_source_log: Path, build_inputs_log: Path,
    restore_log: Path, build_log: Path, signing_phase_log: Path, content_apk_log: Path,
    source_graph_seal_log: Path, command_journal: Path, raw_command_journal: Path,
    delegate_command_journal: Path,
    *, android_root: Path, apk: Path, source_graph_path: Path,
    content_source_receipt_path: Path, content_apk_receipt_path: Path,
    android_build_tools_version: str, snapshots: SnapshotRegistry,
    python_path: Path, dotnet_path: Path, java_path: Path,
    android_sdk_packages_path: Path, android_sdk_root: Path,
    android_workload_manifest_path: Path, maui_workload_manifest_path: Path,
    dotnet_workloads_path: Path,
    presentation_root: Path, core_content_root: Path,
    release_workspace_root: Path, release_package_authority_v2_path: Path,
    package_authority_path: Path, w5_receipt_path: Path,
    w5_evidence_directory: Path, full_project_lock_path: Path,
    package_feed_path: Path, offline_feed_path: Path, nuget_packages_path: Path,
    apksigner_path: Path, jarsigner_path: Path, signing_receipt_path: Path,
    apksigner_log_path: Path, jarsigner_log_path: Path,
) -> None:
    for path, label in (
        (toolchain_log, "toolchain intake log"),
        (source_graph_log, "source graph intake log"),
        (content_source_log, "Core content intake log"),
        (build_inputs_log, "W5 build inputs log"),
        (restore_log, "locked restore log"),
        (build_log, "full MAUI build log"),
        (signing_phase_log, "APK signature verification phase log"),
        (content_apk_log, "APK content verification log"),
        (source_graph_seal_log, "post-build source graph seal log"),
        (command_journal, "bounded command journal"),
        (raw_command_journal, "raw bounded-runner journal"),
        (delegate_command_journal, "delegate bounded-runner journal"),
    ):
        if snapshots.capture(path, label).size <= 0:
            raise ValueError(f"{label} must not be empty")
    try:
        restore = snapshots.capture(restore_log, "locked restore log").data.decode("utf-8")
        build = snapshots.capture(build_log, "full MAUI build log").data.decode("utf-8")
    except UnicodeError as error:
        raise ValueError("build evidence logs must be UTF-8") from error
    if (
        not ("Restored " in restore or "All projects are up-to-date for restore." in restore)
        or re.search(r"\b(?:warning|error)\b", restore, re.IGNORECASE)
    ):
        raise ValueError("locked restore evidence does not prove a clean pass")
    if not (
        "Build succeeded." in build
        and re.search(r"\b0 Warning\(s\)", build)
        and re.search(r"\b0 Error\(s\)", build)
    ):
        raise ValueError("full MAUI build evidence does not prove warnings=0/errors=0")
    rows: list[dict[str, object]] = []
    for index, line in enumerate(snapshots.capture(command_journal, "bounded command journal").data.splitlines(), start=1):
        try:
            row = json.loads(
                line.decode("utf-8"), object_pairs_hook=reject_duplicate_keys,
                parse_constant=lambda token: (_ for _ in ()).throw(
                    ValueError(f"bounded command journal contains {token}")
                ),
            )
        except (UnicodeError, json.JSONDecodeError) as error:
            raise ValueError(f"bounded command journal row {index} is invalid") from error
        if not isinstance(row, dict):
            raise ValueError(f"bounded command journal row {index} is not an object")
        rows.append(row)
    expected = (
        ("toolchain-intake", toolchain_log),
        ("source-graph-intake", source_graph_log),
        ("core-content-intake", content_source_log),
        ("w5-build-input-intake", build_inputs_log),
        ("locked-full-restore", restore_log),
        ("serialized-full-maui-build", build_log),
        ("apk-signature-verification", signing_phase_log),
        ("apk-content-verification", content_apk_log),
        ("post-build-source-graph-seal", source_graph_seal_log),
    )
    if len(rows) != len(expected) * 2:
        raise ValueError("bounded command journal row count is not exact")
    common_invocation_epoch: float | int | None = None
    common_deadline_epoch: float | int | None = None
    for index, (phase, output) in enumerate(expected):
        started, finished = rows[index * 2:index * 2 + 2]
        common_keys = {
            "contractName", "phase", "event", "argv", "workingDirectory",
            "environment", "timeoutSeconds", "deadlineEpoch", "startedEpoch",
            "invocationStartedEpoch", "totalDeadlineSeconds",
            "outputPath", "processGroupTermination", "publicationAuthorized",
        }
        require_exact_keys(started, common_keys, f"bounded journal {phase} started")
        require_exact_keys(finished, common_keys | {
            "elapsedSeconds", "exitCode", "timedOut", "outputSha256", "termination",
        }, f"bounded journal {phase} finished")
        if (
            started.get("contractName") != COMMAND_JOURNAL_CONTRACT
            or started.get("event") != "started" or started.get("phase") != phase
            or started.get("workingDirectory") != os.fspath(android_root)
            or started.get("outputPath") != os.fspath(output)
            or started.get("processGroupTermination") is not True
            or started.get("publicationAuthorized") is not False
        ):
            raise ValueError("bounded command journal started phase order/context is not exact")
        environment = started.get("environment")
        if (
            not isinstance(environment, dict) or set(environment) != ENVIRONMENT_ALLOWLIST
            or not all(isinstance(value, str) and value for value in environment.values())
        ):
            raise ValueError("bounded command environment allowlist is not exact")
        expected_environment = {
            "ANDROID_HOME": os.fspath(android_sdk_root),
            "ANDROID_SDK_ROOT": os.fspath(android_sdk_root),
            "DOTNET_CLI_HOME": os.fspath(DOTNET_CLI_HOME_AUTHORITY),
            "DOTNET_ROOT": os.fspath(dotnet_path.parent),
            "HOME": os.fspath(DOTNET_CLI_HOME_AUTHORITY),
            "JAVA_HOME": os.fspath(java_path.parent.parent),
            "NUGET_PACKAGES": os.fspath(nuget_packages_path),
            "CHUMMER_RELEASE_WORKSPACE_ROOT": os.fspath(release_workspace_root),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
            "MSBUILDDISABLENODEREUSE": "1", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
            "PATH": f"{java_path.parent}:{dotnet_path.parent}:/usr/bin:/bin",
            "TMPDIR": "/tmp",
        }
        if environment != expected_environment:
            raise ValueError("bounded command environment values are not exact")
        timeout = started.get("timeoutSeconds")
        deadline = started.get("deadlineEpoch")
        epoch = started.get("startedEpoch")
        invocation_epoch = started.get("invocationStartedEpoch")
        total_deadline = started.get("totalDeadlineSeconds")
        if common_invocation_epoch is None:
            common_invocation_epoch = invocation_epoch  # type: ignore[assignment]
            common_deadline_epoch = deadline  # type: ignore[assignment]
        if (
            not isinstance(timeout, (int, float)) or isinstance(timeout, bool) or timeout <= 0
            or not isinstance(deadline, (int, float)) or isinstance(deadline, bool)
            or not isinstance(epoch, (int, float)) or isinstance(epoch, bool)
            or not isinstance(invocation_epoch, (int, float)) or isinstance(invocation_epoch, bool)
            or not isinstance(total_deadline, (int, float)) or isinstance(total_deadline, bool)
            or not all(math.isfinite(value) for value in (
                timeout, deadline, epoch, invocation_epoch, total_deadline,
            ))
            or timeout != PER_PHASE_TIMEOUT_SECONDS
            or total_deadline != TOTAL_DEADLINE_SECONDS
            or deadline - invocation_epoch != TOTAL_DEADLINE_SECONDS
            or invocation_epoch != common_invocation_epoch or deadline != common_deadline_epoch
            or epoch < invocation_epoch or deadline - epoch < PER_PHASE_TIMEOUT_SECONDS
        ):
            raise ValueError("bounded command script-owned timeout/deadline facts are invalid")
        argv = validate_phase_argv(
            phase, started.get("argv"), android_root=android_root, apk=apk,
            source_graph_path=source_graph_path,
            content_source_receipt_path=content_source_receipt_path,
            content_apk_receipt_path=content_apk_receipt_path,
            android_build_tools_version=android_build_tools_version,
            python_path=python_path, dotnet_path=dotnet_path,
            presentation_root=presentation_root, core_content_root=core_content_root,
            release_workspace_root=release_workspace_root,
            release_package_authority_v2_path=release_package_authority_v2_path,
            package_authority_path=package_authority_path,
            w5_receipt_path=w5_receipt_path, w5_evidence_directory=w5_evidence_directory,
            full_project_lock_path=full_project_lock_path,
            package_feed_path=package_feed_path, offline_feed_path=offline_feed_path,
            nuget_packages_path=nuget_packages_path,
            dotnet_workloads_path=dotnet_workloads_path,
            android_sdk_packages_path=android_sdk_packages_path, java_path=java_path,
            android_sdk_root=android_sdk_root,
            android_workload_manifest_path=android_workload_manifest_path,
            maui_workload_manifest_path=maui_workload_manifest_path,
            apksigner_path=apksigner_path, jarsigner_path=jarsigner_path,
            signing_receipt_path=signing_receipt_path,
            apksigner_log_path=apksigner_log_path, jarsigner_log_path=jarsigner_log_path,
        )
        for field in common_keys - {"event"}:
            if finished.get(field) != started.get(field):
                raise ValueError(f"bounded command finished row drifted: {phase}/{field}")
        elapsed = finished.get("elapsedSeconds")
        termination = finished.get("termination")
        if (
            finished.get("contractName") != COMMAND_JOURNAL_CONTRACT
            or finished.get("event") != "finished" or finished.get("phase") != phase
            or finished.get("argv") != argv
            or finished.get("outputSha256") != snapshots.capture(output, f"{phase} output").sha256
            or finished.get("publicationAuthorized") is not False
            or type(finished.get("exitCode")) is not int or finished.get("exitCode") != 0
            or finished.get("timedOut") is not False
            or not isinstance(elapsed, (int, float)) or isinstance(elapsed, bool)
            or not math.isfinite(elapsed) or elapsed < 0 or elapsed > timeout + 1.0
            or finished.get("processGroupTermination") is not True
            or require_exact_keys(
                termination, {"groupAbsent", "sigtermSent", "sigkillSent"},
                f"bounded journal {phase} termination",
            ) != {"groupAbsent": True, "sigtermSent": False, "sigkillSent": False}
            or not all(type(value) is bool for value in termination.values())
        ):
            raise ValueError("bounded command journal contains a failed or terminated phase")

    raw_rows: list[dict[str, object]] = []
    for index, line in enumerate(
        snapshots.capture(raw_command_journal, "raw bounded-runner journal").data.splitlines(), start=1,
    ):
        try:
            raw_row = json.loads(
                line.decode("utf-8"), object_pairs_hook=reject_duplicate_keys,
                parse_constant=lambda token: (_ for _ in ()).throw(
                    ValueError(f"raw bounded-runner journal contains {token}")
                ),
            )
        except (UnicodeError, json.JSONDecodeError) as error:
            raise ValueError(f"raw bounded-runner journal row {index} is invalid") from error
        if not isinstance(raw_row, dict):
            raise ValueError("raw bounded-runner journal row is not an object")
        raw_rows.append(raw_row)
    if len(raw_rows) != len(rows):
        raise ValueError("raw/canonical bounded journal row count differs")
    for index, (phase, output) in enumerate(expected):
        canonical_started, canonical_finished = rows[index * 2:index * 2 + 2]
        raw_started, raw_finished = raw_rows[index * 2:index * 2 + 2]
        require_exact_keys(raw_started, {
            "contractName", "phase", "event", "command", "timeoutSeconds",
            "deadlineEpoch", "invocationStartedEpoch", "totalDeadlineSeconds",
            "processGroupTermination", "publicationAuthorized",
        }, f"raw bounded journal {phase} started")
        require_exact_keys(raw_finished, {
            "contractName", "phase", "event", "exitCode", "timedOut",
            "elapsedSeconds", "outputSha256", "termination",
            "command",
            "timeoutSeconds", "deadlineEpoch", "invocationStartedEpoch", "totalDeadlineSeconds",
            "processGroupTermination", "publicationAuthorized",
        }, f"raw bounded journal {phase} finished")
        if (
            raw_started != {
                "contractName": RAW_COMMAND_JOURNAL_CONTRACT, "phase": phase,
                "event": "started", "command": canonical_started["argv"],
                "timeoutSeconds": canonical_started["timeoutSeconds"],
                "deadlineEpoch": canonical_started["deadlineEpoch"],
                "invocationStartedEpoch": canonical_started["invocationStartedEpoch"],
                "totalDeadlineSeconds": TOTAL_DEADLINE_SECONDS,
                "processGroupTermination": True, "publicationAuthorized": False,
            }
            or raw_finished != {
                "contractName": RAW_COMMAND_JOURNAL_CONTRACT, "phase": phase,
                "event": "finished", "exitCode": canonical_finished["exitCode"],
                "command": canonical_started["argv"],
                "timedOut": canonical_finished["timedOut"],
                "elapsedSeconds": canonical_finished["elapsedSeconds"],
                "timeoutSeconds": canonical_started["timeoutSeconds"],
                "deadlineEpoch": canonical_started["deadlineEpoch"],
                "invocationStartedEpoch": canonical_started["invocationStartedEpoch"],
                "totalDeadlineSeconds": TOTAL_DEADLINE_SECONDS,
                "outputSha256": snapshots.capture(output, f"{phase} output").sha256,
                "termination": canonical_finished["termination"],
                "processGroupTermination": True, "publicationAuthorized": False,
            }
        ):
            raise ValueError("raw bounded-runner journal does not cross-bind canonical execution")

    delegate_rows: list[dict[str, object]] = []
    for line in snapshots.capture(
        delegate_command_journal, "delegate bounded-runner journal",
    ).data.splitlines():
        delegate_rows.append(json.loads(
            line.decode("utf-8"), object_pairs_hook=reject_duplicate_keys,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"delegate bounded-runner journal contains {token}")
            ),
        ))
    if len(delegate_rows) != len(rows):
        raise ValueError("delegate/raw/canonical bounded journal row count differs")
    for index, (phase, output) in enumerate(expected):
        raw_started, raw_finished = raw_rows[index * 2:index * 2 + 2]
        delegated_started, delegated_finished = delegate_rows[index * 2:index * 2 + 2]
        require_exact_keys(delegated_started, {
            "contractName", "phase", "event", "command", "timeoutSeconds",
            "processGroupTermination", "publicationAuthorized",
        }, f"delegate bounded journal {phase} started")
        require_exact_keys(delegated_finished, {
            "contractName", "phase", "event", "exitCode", "timedOut",
            "elapsedSeconds", "outputSha256", "termination",
            "processGroupTermination", "publicationAuthorized",
        }, f"delegate bounded journal {phase} finished")
        if (
            delegated_started != {
                "contractName": DELEGATE_COMMAND_JOURNAL_CONTRACT, "phase": phase,
                "event": "started", "command": raw_started["command"],
                "timeoutSeconds": PER_PHASE_TIMEOUT_SECONDS,
                "processGroupTermination": True, "publicationAuthorized": False,
            }
            or delegated_finished != {
                key: value for key, value in raw_finished.items()
                if key not in {"command", "timeoutSeconds", "deadlineEpoch", "invocationStartedEpoch", "totalDeadlineSeconds"}
            } | {"contractName": DELEGATE_COMMAND_JOURNAL_CONTRACT}
        ):
            raise ValueError("delegate bounded-runner journal does not cross-bind raw execution")


def authenticate_inputs(
    *, android_root: Path, presentation_root: Path, core_content_root: Path,
    w5_receipt_path: Path,
    w5_evidence_directory: Path,
    source_graph_path: Path, package_authority_path: Path,
    release_package_authority_v2_path: Path,
    release_workspace_root: Path,
    content_source_receipt_path: Path, full_project_lock_path: Path,
    w5_verifier: Callable[[Path, Path], Mapping[str, object]] = _verify_w5_external,
    content_verifier: Callable[[Path, Path], list[str]] = _verify_core_content_external,
    release_workspace_verifier: Callable[
        [Path, Path, Mapping[str, object]], Mapping[str, object]
    ] = validate_release_workspace_authority,
    snapshots: SnapshotRegistry | None = None,
) -> dict[str, object]:
    snapshots = snapshots or SnapshotRegistry()
    android_identity = repository_identity(android_root)
    presentation_identity = repository_identity(
        presentation_root, label="W5 Presentation build source",
    )
    if presentation_identity != {
        "commit": W5_PRESENTATION_COMMIT, "tree": W5_PRESENTATION_TREE,
    }:
        raise ValueError("W5 Presentation build source identity is not exact")
    core_content_identity = repository_identity(
        core_content_root, label="Core content source",
    )
    if core_content_identity["commit"] != CORE_CONTENT_REVISION:
        raise ValueError("Core content source revision is not exact")
    presentation_lock = snapshots.capture(
        presentation_root / "Chummer.Presentation/packages.lock.json",
        "W4.1 Presentation package lock",
    )
    desktop_lock = snapshots.capture(
        presentation_root / "Chummer.Desktop.Runtime/packages.lock.json",
        "W4.1 Desktop.Runtime package lock",
    )
    if presentation_lock.sha256 != W41_PRESENTATION_LOCK_SHA256:
        raise ValueError("W4.1 Presentation package lock is not exact")
    if desktop_lock.sha256 != W41_DESKTOP_LOCK_SHA256:
        raise ValueError("W4.1 Desktop.Runtime package lock is not exact")
    ancestor = subprocess.run(
        ["git", "-C", os.fspath(android_root), "merge-base", "--is-ancestor", W5_ANDROID_COMMIT, "HEAD"],
        check=False, capture_output=True, timeout=30,
    )
    if ancestor.returncode != 0:
        raise ValueError("current Android candidate does not descend from the W5 proof source")
    changed = set(filter(None, _git(android_root, "diff", "--name-only", f"{W5_ANDROID_COMMIT}..HEAD").splitlines()))
    if not changed.issubset(ALLOWED_POST_W5_PATHS):
        raise ValueError(f"Android product source changed after W5 proof: {sorted(changed - ALLOWED_POST_W5_PATHS)}")
    w5 = validate_w5_receipt(
        w5_receipt_path, w5_evidence_directory, verifier=w5_verifier,
        snapshots=snapshots,
    )
    graph = validate_source_graph(source_graph_path, android_identity, snapshots)
    release_workspace_authority = release_workspace_verifier(
        release_workspace_root, android_root, graph,
    )
    release_authority_v2_payload = validate_release_package_authority_v2(
        release_package_authority_v2_path, graph=graph,
        release_workspace_root=release_workspace_root, snapshots=snapshots,
    )
    release_authority_v2 = snapshots.capture(
        release_package_authority_v2_path, "v2 release package authority",
    )
    generator_snapshot = snapshots.capture(
        android_root / "scripts/verify_release_source_graph.py",
        "release source graph verifier",
    )
    expected_generator = {
        "path": "scripts/verify_release_source_graph.py",
        "sha256": generator_snapshot.sha256,
        "size_bytes": generator_snapshot.size,
    }
    if graph.get("generator") != expected_generator:
        raise ValueError("release source graph generator bytes do not match current Android source")
    authority = validate_package_authority(
        package_authority_path,
        committed_path=android_root / "eng/internal-phone-beta-package-authority.json",
        w5_receipt=w5, snapshots=snapshots,
    )
    for graph_row, authority_row in zip(
        graph["packagePins"], authority["packagePins"], strict=True,
    ):
        for field in ("package_id", "version", "sha256"):
            if graph_row.get(field) != authority_row.get(field):
                raise ValueError(f"Core package authority cross-binding mismatch: {field}")
    for graph_row, authority_row in zip(
        graph["ownerPackagePins"], authority["ownerPackagePins"], strict=True,
    ):
        for field in ("package_id", "version", "sha256", "size_bytes"):
            if graph_row.get(field) != authority_row.get(field):
                raise ValueError(f"owner package authority cross-binding mismatch: {field}")
    content = validate_content_receipt(content_source_receipt_path, apk=None, snapshots=snapshots)
    content_manifest_path = android_root / "src/Chummer.Android/Content/chummer-content-manifest.json"
    content_manifest_snapshot = snapshots.capture(content_manifest_path, "committed Core content manifest")
    content_manifest = load_strict_json(content_manifest_path, "committed Core content manifest", snapshots)
    files = content_manifest.get("files")
    if not isinstance(files, list):
        raise ValueError("committed Core content manifest file inventory is invalid")
    expected_content = {
        "coreRevision": content_manifest.get("coreRevision"),
        "bundleDigest": content_manifest.get("bundleDigest"),
        "manifestSha256": content_manifest_snapshot.sha256,
        "canonicalFileCount": len(files),
        "canonicalByteCount": sum(
            row.get("size", 0) for row in files
            if isinstance(row, dict) and isinstance(row.get("size"), int)
        ),
    }
    for field, expected in expected_content.items():
        if content.get(field) != expected:
            raise ValueError(f"Core content receipt does not bind committed manifest: {field}")
    content_issues = content_verifier(android_root, core_content_root)
    if content_issues:
        raise ValueError(f"Core data/lang content verifier blocked: {content_issues}")
    expected_lock_path = android_root / "src/Chummer.Android/packages.lock.json"
    if full_project_lock_path != expected_lock_path:
        raise ValueError("full-project package lock path is not canonical")
    if (
        snapshots.capture(full_project_lock_path, "full-project package lock").sha256 != FULL_PROJECT_LOCK_SHA256
        or snapshots.capture(full_project_lock_path, "full-project package lock").size != FULL_PROJECT_LOCK_SIZE
    ):
        raise ValueError("full-project package lock bytes are not exact")
    validate_full_project_lock(full_project_lock_path, snapshots)
    return {
        "androidIdentity": android_identity,
        "presentationBuildIdentity": presentation_identity,
        "coreContentIdentity": core_content_identity,
        "w5": w5, "sourceGraph": graph,
        "packageAuthority": authority, "contentSource": content,
        "presentationLock": presentation_lock.binding(),
        "desktopLock": desktop_lock.binding(),
        "releasePackageAuthorityV2": release_authority_v2.binding(),
        "releasePackageAuthorityV2Payload": release_authority_v2_payload,
        "releaseWorkspaceAuthority": release_workspace_authority,
        "snapshots": snapshots,
    }


def create_manifest(
    *, android_root: Path, presentation_root: Path, core_content_root: Path,
    apk: Path, w5_receipt_path: Path,
    w5_evidence_directory: Path, source_graph_path: Path,
    package_authority_path: Path, release_package_authority_v2_path: Path,
    content_source_receipt_path: Path,
    content_apk_receipt_path: Path, full_project_lock_path: Path,
    assets_path: Path, toolchain_log_path: Path,
    source_graph_log_path: Path, content_source_log_path: Path,
    build_inputs_log_path: Path, restore_log_path: Path, build_log_path: Path,
    signing_phase_log_path: Path, apksigner_log_path: Path, jarsigner_log_path: Path,
    signing_receipt_path: Path, content_apk_log_path: Path,
    source_graph_seal_log_path: Path, command_journal_path: Path,
    raw_command_journal_path: Path, delegate_command_journal_path: Path,
    android_sdk_packages_path: Path, android_sdk_root: Path,
    android_workload_manifest_path: Path, maui_workload_manifest_path: Path,
    dotnet_workloads_path: Path,
    java_path: Path, javac_path: Path, jarsigner_path: Path,
    apksigner_path: Path, dotnet_path: Path,
    python_path: Path, release_workspace_root: Path,
    package_feed_path: Path, offline_feed_path: Path, nuget_packages_path: Path,
    android_build_tools_version: str, dotnet_version: str,
    generated_at_utc: str | None = None,
    w5_verifier: Callable[[Path, Path], Mapping[str, object]] = _verify_w5_external,
    content_verifier: Callable[[Path, Path], list[str]] = _verify_core_content_external,
    release_workspace_verifier: Callable[
        [Path, Path, Mapping[str, object]], Mapping[str, object]
    ] = validate_release_workspace_authority,
    before_final_recheck: Callable[[], None] | None = None,
) -> dict[str, object]:
    snapshots = SnapshotRegistry()
    for path, label in (
        (release_workspace_root, "release workspace"),
        (package_feed_path, "W5 package feed"),
        (offline_feed_path, "offline package feed"),
        (nuget_packages_path, "NuGet package cache"),
    ):
        require_directory(path, label)
    facts = authenticate_inputs(
        android_root=android_root, presentation_root=presentation_root,
        core_content_root=core_content_root,
        w5_receipt_path=w5_receipt_path,
        w5_evidence_directory=w5_evidence_directory,
        source_graph_path=source_graph_path,
        package_authority_path=package_authority_path,
        release_package_authority_v2_path=release_package_authority_v2_path,
        release_workspace_root=release_workspace_root,
        content_source_receipt_path=content_source_receipt_path,
        full_project_lock_path=full_project_lock_path, w5_verifier=w5_verifier,
        content_verifier=content_verifier,
        release_workspace_verifier=release_workspace_verifier, snapshots=snapshots,
    )
    validate_apk_output_directory(apk, snapshots)
    abis = apk_abis(apk, snapshots)
    content_apk = validate_content_receipt(
        content_apk_receipt_path, apk=apk, source_binding=facts["contentSource"],
        snapshots=snapshots,
    )
    validate_assets(
        assets_path, package_authority=facts["packageAuthority"], snapshots=snapshots,
    )
    toolchain = validate_toolchain(
        java_path=java_path, javac_path=javac_path, dotnet_path=dotnet_path,
        jarsigner_path=jarsigner_path, apksigner_path=apksigner_path,
        dotnet_workloads_path=dotnet_workloads_path,
        android_sdk_packages_path=android_sdk_packages_path,
        android_sdk_root=android_sdk_root,
        android_workload_manifest_path=android_workload_manifest_path,
        maui_workload_manifest_path=maui_workload_manifest_path,
        android_build_tools_version=android_build_tools_version,
        dotnet_version=dotnet_version, snapshots=snapshots,
    )
    signing = validate_apk_signing(
        receipt_path=signing_receipt_path, apksigner_log_path=apksigner_log_path,
        jarsigner_log_path=jarsigner_log_path, apk=apk,
        apksigner_path=apksigner_path, jarsigner_path=jarsigner_path,
        snapshots=snapshots,
    )
    validate_execution_evidence(
        toolchain_log_path, source_graph_log_path, content_source_log_path, build_inputs_log_path,
        restore_log_path, build_log_path, signing_phase_log_path, content_apk_log_path,
        source_graph_seal_log_path, command_journal_path, raw_command_journal_path,
        delegate_command_journal_path,
        android_root=android_root, apk=apk, source_graph_path=source_graph_path,
        content_source_receipt_path=content_source_receipt_path,
        content_apk_receipt_path=content_apk_receipt_path,
        android_build_tools_version=android_build_tools_version,
        snapshots=snapshots,
        python_path=python_path, dotnet_path=dotnet_path, java_path=java_path,
        android_sdk_packages_path=android_sdk_packages_path,
        android_sdk_root=android_sdk_root,
        android_workload_manifest_path=android_workload_manifest_path,
        maui_workload_manifest_path=maui_workload_manifest_path,
        dotnet_workloads_path=dotnet_workloads_path,
        presentation_root=presentation_root, core_content_root=core_content_root,
        release_workspace_root=release_workspace_root,
        release_package_authority_v2_path=release_package_authority_v2_path,
        package_authority_path=package_authority_path,
        w5_receipt_path=w5_receipt_path, w5_evidence_directory=w5_evidence_directory,
        full_project_lock_path=full_project_lock_path,
        package_feed_path=package_feed_path, offline_feed_path=offline_feed_path,
        nuget_packages_path=nuget_packages_path,
        apksigner_path=apksigner_path, jarsigner_path=jarsigner_path,
        signing_receipt_path=signing_receipt_path,
        apksigner_log_path=apksigner_log_path, jarsigner_log_path=jarsigner_log_path,
    )

    graph = facts["sourceGraph"]
    authority = facts["packageAuthority"]
    authority_payload: dict[str, object] = {
        "schema": SCHEMA, "status": "pass", "authorityClass": AUTHORITY_CLASS,
        "publicationAuthorized": False, "proofScope": PROOF_SCOPE,
        "dependencyMode": "locked_w5_packages_no_owner_siblings",
        "sourceGraph": {
            **binding(source_graph_path, snapshots), "contractName": SOURCE_GRAPH_CONTRACT,
            "repositories": graph["repositories"],
            "packageAuthority": facts["releasePackageAuthorityV2"],
            "packageAuthorityContract": "chummer.android.release-package-authority/v2",
            "packageAuthorityPublicationAuthorized": False,
        },
        "w5CompileProof": {
            **binding(w5_receipt_path, snapshots), "contractName": W5_CONTRACT, "status": "pass",
            "androidCommit": W5_ANDROID_COMMIT, "androidTree": W5_ANDROID_TREE,
        },
        "presentationBuildSource": {
            **facts["presentationBuildIdentity"],
            "authorityClass": "W4.1_internal_package_authority_source",
            "productionSource": False,
            "publicationAuthorized": False,
            "presentationLock": facts["presentationLock"],
            "desktopRuntimeLock": facts["desktopLock"],
        },
        "packageAuthority": {
            **binding(package_authority_path, snapshots), "contractName": PACKAGE_AUTHORITY_CONTRACT,
            "packagePins": authority["packagePins"],
            "ownerPackagePins": authority["ownerPackagePins"],
        },
        "content": {
            "sourceReceipt": binding(content_source_receipt_path, snapshots),
            "apkReceipt": binding(content_apk_receipt_path, snapshots),
            "coreRevision": content_apk["coreRevision"],
            "bundleDigest": content_apk["bundleDigest"],
            "manifestSha256": content_apk["manifestSha256"],
            "canonicalFileCount": content_apk["canonicalFileCount"],
            "canonicalByteCount": content_apk["canonicalByteCount"],
            "sourceRepository": facts["coreContentIdentity"],
        },
        "restore": {
            "lockedMode": True, "networkSourcesAllowed": False,
            "ownerSourceFallbackAllowed": False,
            "fullProjectLock": binding(full_project_lock_path, snapshots),
            "projectAssets": binding(assets_path, snapshots),
        },
        "executionEvidence": {
            "toolchainLog": binding(toolchain_log_path, snapshots),
            "sourceGraphLog": binding(source_graph_log_path, snapshots),
            "contentSourceLog": binding(content_source_log_path, snapshots),
            "buildInputsLog": binding(build_inputs_log_path, snapshots),
            "restoreLog": binding(restore_log_path, snapshots),
            "buildLog": binding(build_log_path, snapshots),
            "signingPhaseLog": binding(signing_phase_log_path, snapshots),
            "apksignerLog": binding(apksigner_log_path, snapshots),
            "jarsignerLog": binding(jarsigner_log_path, snapshots),
            "signingReceipt": binding(signing_receipt_path, snapshots),
            "contentApkLog": binding(content_apk_log_path, snapshots),
            "sourceGraphSealLog": binding(source_graph_seal_log_path, snapshots),
            "commandJournal": binding(command_journal_path, snapshots),
            "rawCommandJournal": binding(raw_command_journal_path, snapshots),
            "delegateCommandJournal": binding(delegate_command_journal_path, snapshots),
            "boundedProcessGroups": True,
            "warnings": 0,
            "errors": 0,
        },
        "toolchain": toolchain,
        "artifact": {
            "basename": apk.name, **snapshots.capture(apk, "signed ARM64 APK").binding(),
            "package": PACKAGE, "abis": abis,
            "apiLevel": 36, "configuration": CONFIGURATION,
            "runtimeIdentifier": RUNTIME_IDENTIFIER, "targetFramework": TARGET_FRAMEWORK,
            "fullMauiArtifact": True, "installed": False,
            "signing": {
                "certificateSha256": signing["certificateSha256"],
                "verifiedSchemes": signing["verifiedSchemes"],
                "receipt": binding(signing_receipt_path, snapshots),
            },
        },
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    if before_final_recheck is not None:
        before_final_recheck()
    validate_apk_output_directory(apk, snapshots)
    snapshots.recheck_all()
    final_android = repository_identity(android_root)
    final_presentation = repository_identity(
        presentation_root, label="W5 Presentation build source",
    )
    final_core = repository_identity(core_content_root, label="Core content source")
    if final_android != facts["androidIdentity"]:
        raise ValueError("Android source identity changed before provenance seal")
    if final_presentation != facts["presentationBuildIdentity"]:
        raise ValueError("W5 Presentation source identity changed before provenance seal")
    if final_core != facts["coreContentIdentity"]:
        raise ValueError("Core content source identity changed before provenance seal")
    final_release_workspace = release_workspace_verifier(
        release_workspace_root, android_root, facts["sourceGraph"],
    )
    if final_release_workspace != facts["releaseWorkspaceAuthority"]:
        raise ValueError("release workspace authority changed before provenance seal")
    return {
        **authority_payload,
        "authoritySha256": canonical_sha256(authority_payload),
        "generatedAtUtc": generated_at_utc or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }


def write_manifest(path: Path, manifest: Mapping[str, object]) -> None:
    if not path.is_absolute():
        raise ValueError("provenance output path must be absolute")
    require_directory(path.parent, "provenance output parent")
    if path.exists() or path.is_symlink():
        raise ValueError("provenance output must be absent")
    encoded = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile("wb", dir=path.parent, delete=False) as stream:
            temporary = stream.name
            os.fchmod(stream.fileno(), 0o600)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path)
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def load_and_verify_manifest(manifest_path: Path, **create_arguments: object) -> dict[str, object]:
    manifest = load_strict_json(manifest_path, "API36 build provenance")
    require_exact_keys(manifest, {
        "schema", "status", "authorityClass", "publicationAuthorized", "proofScope",
        "dependencyMode", "sourceGraph", "w5CompileProof", "presentationBuildSource", "packageAuthority",
        "content", "restore", "executionEvidence", "toolchain", "artifact", "doesNotAssert",
        "authoritySha256", "generatedAtUtc",
    }, "API36 build provenance")
    generated = manifest.get("generatedAtUtc")
    if not isinstance(generated, str) or not generated.endswith("Z"):
        raise ValueError("API36 build provenance timestamp is not canonical UTC")
    expected = create_manifest(generated_at_utc=generated, **create_arguments)
    if manifest != expected:
        raise ValueError("API36 build provenance differs from authenticated current inputs")
    return manifest
