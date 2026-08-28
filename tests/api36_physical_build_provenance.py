#!/usr/bin/env python3
"""Fail-closed provenance for an internal API-36 ARM64 build candidate."""

from __future__ import annotations

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
import tempfile
from typing import Callable, Mapping
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
FULL_PROJECT_LOCK_SHA256 = "9037d4afc11dd8661dfbcccbc67a9f814d110fb17cf985cf215268e12ae3583e"
FULL_PROJECT_LOCK_SIZE = 72165
PRODUCTION_PRESENTATION_COMMIT = "3a5ca054e1ce126a02dec4199dc92233dfee8804"
PRODUCTION_PRESENTATION_TREE = "25def23deef40822e3ff89549cc509e01c149ed4"
CORE_CONTENT_REVISION = "2fb2ae9bb48e5a1a6b25a174ba88008ce995fcd5"
DOTNET_SDK_VERSION = "10.0.111"
TARGET_FRAMEWORK = "net10.0-android36.0"
RUNTIME_IDENTIFIER = "android-arm64"
CONFIGURATION = "Debug"

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


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_strict_json(path: Path, label: str) -> dict[str, object]:
    require_regular(path, label)
    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
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


def binding(path: Path) -> dict[str, object]:
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
) -> dict[str, object]:
    require_regular(receipt_path, "W5 compile receipt")
    require_directory(evidence_directory, "W5 evidence directory")
    if file_sha256(receipt_path) != W5_RECEIPT_SHA256:
        raise ValueError("W5 compile receipt digest is not the authorized PASS receipt")
    payload = load_strict_json(receipt_path, "W5 compile receipt")
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
    verified = verifier(receipt_path, evidence_directory)
    if verified.get("status") != "pass" or verified.get("verifiedReceiptStatus") != "pass":
        raise ValueError("W5 compile receipt did not pass its committed verifier")
    return payload


def validate_source_graph(path: Path, android_identity: Mapping[str, str]) -> dict[str, object]:
    graph = load_strict_json(path, "release source graph")
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


def validate_package_authority(
    path: Path, *, committed_path: Path, w5_receipt: Mapping[str, object],
) -> dict[str, object]:
    require_regular(path, "internal package authority")
    require_regular(committed_path, "committed internal package authority")
    digest = file_sha256(path)
    if digest != W5_AUTHORITY_BINDING_SHA256 or digest != w5_receipt.get("authorityBindingSha256"):
        raise ValueError("internal package authority digest is not W5-bound")
    if path.read_bytes() != committed_path.read_bytes():
        raise ValueError("internal package authority differs from the committed authority")
    payload = load_strict_json(path, "internal package authority")
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
) -> dict[str, object]:
    receipt = load_strict_json(path, "Core content receipt")
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
        require_regular(apk, "ARM64 APK")
        if (
            receipt.get("apkVerified") is not True
            or receipt.get("apkSha256") != file_sha256(apk)
            or receipt.get("apkCanonicalFileCount") != count
        ):
            raise ValueError("post-build Core content receipt does not bind the complete APK content")
    if source_binding is not None:
        for field in ("coreRevision", "bundleDigest", "manifestSha256", "canonicalFileCount", "canonicalByteCount"):
            if receipt.get(field) != source_binding.get(field):
                raise ValueError(f"pre/post Core content receipt mismatch: {field}")
    return receipt


def validate_full_project_lock(path: Path) -> dict[str, object]:
    lock = load_strict_json(path, "full-project package lock")
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


def validate_assets(path: Path, *, package_authority: Mapping[str, object]) -> dict[str, object]:
    assets = load_strict_json(path, "full-project restore assets")
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


def apk_abis(path: Path) -> list[str]:
    require_regular(path, "ARM64 APK")
    try:
        with zipfile.ZipFile(path) as archive:
            abis = sorted({
                parts[1] for name in archive.namelist()
                if len(parts := name.split("/")) >= 3 and parts[0] == "lib" and parts[1]
            })
    except (OSError, zipfile.BadZipFile) as error:
        raise ValueError("ARM64 artifact is not a readable APK") from error
    if abis != ["arm64-v8a"]:
        raise ValueError(f"APK ABI closure must be exactly arm64-v8a: {abis!r}")
    return abis


def validate_execution_evidence(
    source_graph_log: Path, content_source_log: Path, build_inputs_log: Path,
    restore_log: Path, build_log: Path, content_apk_log: Path,
    source_graph_seal_log: Path, command_journal: Path,
) -> None:
    for path, label in (
        (source_graph_log, "source graph intake log"),
        (content_source_log, "Core content intake log"),
        (build_inputs_log, "W5 build inputs log"),
        (restore_log, "locked restore log"),
        (build_log, "full MAUI build log"),
        (content_apk_log, "APK content verification log"),
        (source_graph_seal_log, "post-build source graph seal log"),
        (command_journal, "bounded command journal"),
    ):
        require_regular(path, label)
    restore = restore_log.read_text(encoding="utf-8")
    if (
        not ("Restored " in restore or "All projects are up-to-date for restore." in restore)
        or re.search(r"\b(?:warning|error)\b", restore, re.IGNORECASE)
    ):
        raise ValueError("locked restore evidence does not prove a clean pass")
    build = build_log.read_text(encoding="utf-8")
    if not (
        "Build succeeded." in build
        and re.search(r"\b0 Warning\(s\)", build)
        and re.search(r"\b0 Error\(s\)", build)
    ):
        raise ValueError("full MAUI build evidence does not prove warnings=0/errors=0")
    rows: list[dict[str, object]] = []
    for index, line in enumerate(command_journal.read_bytes().splitlines(), start=1):
        try:
            row = json.loads(line.decode("utf-8"), object_pairs_hook=reject_duplicate_keys)
        except (UnicodeError, json.JSONDecodeError) as error:
            raise ValueError(f"bounded command journal row {index} is invalid") from error
        if not isinstance(row, dict):
            raise ValueError(f"bounded command journal row {index} is not an object")
        rows.append(row)
    expected = (
        ("source-graph-intake", source_graph_log),
        ("core-content-intake", content_source_log),
        ("w5-build-input-intake", build_inputs_log),
        ("locked-full-restore", restore_log),
        ("serialized-full-maui-build", build_log),
        ("apk-content-verification", content_apk_log),
        ("post-build-source-graph-seal", source_graph_seal_log),
    )
    if len(rows) != len(expected) * 2:
        raise ValueError("bounded command journal row count is not exact")
    for index, (phase, output) in enumerate(expected):
        started, row = rows[index * 2:index * 2 + 2]
        if (
            started.get("event") != "started" or started.get("phase") != phase
            or started.get("processGroupTermination") is not True
            or started.get("publicationAuthorized") is not False
        ):
            raise ValueError("bounded command journal started phase order is not exact")
        if (
            row.get("event") != "finished" or row.get("phase") != phase
            or row.get("outputSha256") != file_sha256(output)
            or row.get("publicationAuthorized") is not False
            or row.get("exitCode") != 0 or row.get("timedOut") is not False
            or row.get("processGroupTermination") is not True
            or not isinstance(row.get("termination"), dict)
            or row["termination"].get("groupAbsent") is not True
            or row["termination"].get("sigtermSent") is not False
            or row["termination"].get("sigkillSent") is not False
        ):
            raise ValueError("bounded command journal contains a failed or terminated phase")


def authenticate_inputs(
    *, android_root: Path, presentation_root: Path, core_content_root: Path,
    w5_receipt_path: Path,
    w5_evidence_directory: Path,
    source_graph_path: Path, package_authority_path: Path,
    content_source_receipt_path: Path, full_project_lock_path: Path,
    w5_verifier: Callable[[Path, Path], Mapping[str, object]] = _verify_w5_external,
    content_verifier: Callable[[Path, Path], list[str]] = _verify_core_content_external,
) -> dict[str, object]:
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
    ancestor = subprocess.run(
        ["git", "-C", os.fspath(android_root), "merge-base", "--is-ancestor", W5_ANDROID_COMMIT, "HEAD"],
        check=False, capture_output=True, timeout=30,
    )
    if ancestor.returncode != 0:
        raise ValueError("current Android candidate does not descend from the W5 proof source")
    changed = set(filter(None, _git(android_root, "diff", "--name-only", f"{W5_ANDROID_COMMIT}..HEAD").splitlines()))
    if not changed.issubset(ALLOWED_POST_W5_PATHS):
        raise ValueError(f"Android product source changed after W5 proof: {sorted(changed - ALLOWED_POST_W5_PATHS)}")
    w5 = validate_w5_receipt(w5_receipt_path, w5_evidence_directory, verifier=w5_verifier)
    graph = validate_source_graph(source_graph_path, android_identity)
    expected_generator = {
        "path": "scripts/verify_release_source_graph.py",
        "sha256": file_sha256(android_root / "scripts/verify_release_source_graph.py"),
        "size_bytes": (android_root / "scripts/verify_release_source_graph.py").stat().st_size,
    }
    if graph.get("generator") != expected_generator:
        raise ValueError("release source graph generator bytes do not match current Android source")
    authority = validate_package_authority(
        package_authority_path,
        committed_path=android_root / "eng/internal-phone-beta-package-authority.json",
        w5_receipt=w5,
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
    content = validate_content_receipt(content_source_receipt_path, apk=None)
    content_manifest_path = android_root / "src/Chummer.Android/Content/chummer-content-manifest.json"
    content_manifest = load_strict_json(content_manifest_path, "committed Core content manifest")
    files = content_manifest.get("files")
    if not isinstance(files, list):
        raise ValueError("committed Core content manifest file inventory is invalid")
    expected_content = {
        "coreRevision": content_manifest.get("coreRevision"),
        "bundleDigest": content_manifest.get("bundleDigest"),
        "manifestSha256": file_sha256(content_manifest_path),
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
        file_sha256(full_project_lock_path) != FULL_PROJECT_LOCK_SHA256
        or full_project_lock_path.stat().st_size != FULL_PROJECT_LOCK_SIZE
    ):
        raise ValueError("full-project package lock bytes are not exact")
    validate_full_project_lock(full_project_lock_path)
    return {
        "androidIdentity": android_identity,
        "presentationBuildIdentity": presentation_identity,
        "coreContentIdentity": core_content_identity,
        "w5": w5, "sourceGraph": graph,
        "packageAuthority": authority, "contentSource": content,
    }


def create_manifest(
    *, android_root: Path, presentation_root: Path, core_content_root: Path,
    apk: Path, w5_receipt_path: Path,
    w5_evidence_directory: Path, source_graph_path: Path,
    package_authority_path: Path, content_source_receipt_path: Path,
    content_apk_receipt_path: Path, full_project_lock_path: Path,
    assets_path: Path, source_graph_log_path: Path, content_source_log_path: Path,
    build_inputs_log_path: Path, restore_log_path: Path, build_log_path: Path,
    content_apk_log_path: Path, source_graph_seal_log_path: Path,
    command_journal_path: Path,
    android_sdk_packages_path: Path, java_version: str,
    dotnet_version: str, generated_at_utc: str | None = None,
    w5_verifier: Callable[[Path, Path], Mapping[str, object]] = _verify_w5_external,
    content_verifier: Callable[[Path, Path], list[str]] = _verify_core_content_external,
) -> dict[str, object]:
    facts = authenticate_inputs(
        android_root=android_root, presentation_root=presentation_root,
        core_content_root=core_content_root,
        w5_receipt_path=w5_receipt_path,
        w5_evidence_directory=w5_evidence_directory,
        source_graph_path=source_graph_path,
        package_authority_path=package_authority_path,
        content_source_receipt_path=content_source_receipt_path,
        full_project_lock_path=full_project_lock_path, w5_verifier=w5_verifier,
        content_verifier=content_verifier,
    )
    apk = apk.resolve(strict=True)
    abis = apk_abis(apk)
    content_apk = validate_content_receipt(
        content_apk_receipt_path, apk=apk, source_binding=facts["contentSource"],
    )
    validate_assets(assets_path, package_authority=facts["packageAuthority"])
    validate_execution_evidence(
        source_graph_log_path, content_source_log_path, build_inputs_log_path,
        restore_log_path, build_log_path, content_apk_log_path,
        source_graph_seal_log_path, command_journal_path,
    )
    require_regular(android_sdk_packages_path, "Android SDK package inventory")
    if dotnet_version != DOTNET_SDK_VERSION:
        raise ValueError("full MAUI build SDK selection drifted")
    if not isinstance(java_version, str) or not java_version.strip() or "\n" in java_version.strip("\n"):
        raise ValueError("Java toolchain identity must be one non-empty line")

    graph = facts["sourceGraph"]
    authority = facts["packageAuthority"]
    authority_payload: dict[str, object] = {
        "schema": SCHEMA, "status": "pass", "authorityClass": AUTHORITY_CLASS,
        "publicationAuthorized": False, "proofScope": PROOF_SCOPE,
        "dependencyMode": "locked_w5_packages_no_owner_siblings",
        "sourceGraph": {
            **binding(source_graph_path), "contractName": SOURCE_GRAPH_CONTRACT,
            "repositories": graph["repositories"],
        },
        "w5CompileProof": {
            **binding(w5_receipt_path), "contractName": W5_CONTRACT, "status": "pass",
            "androidCommit": W5_ANDROID_COMMIT, "androidTree": W5_ANDROID_TREE,
        },
        "presentationBuildSource": {
            **facts["presentationBuildIdentity"],
            "authorityClass": "W4.1_internal_package_authority_source",
            "productionSource": False,
            "publicationAuthorized": False,
        },
        "packageAuthority": {
            **binding(package_authority_path), "contractName": PACKAGE_AUTHORITY_CONTRACT,
            "packagePins": authority["packagePins"],
            "ownerPackagePins": authority["ownerPackagePins"],
        },
        "content": {
            "sourceReceipt": binding(content_source_receipt_path),
            "apkReceipt": binding(content_apk_receipt_path),
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
            "fullProjectLock": binding(full_project_lock_path),
            "projectAssets": binding(assets_path),
        },
        "executionEvidence": {
            "sourceGraphLog": binding(source_graph_log_path),
            "contentSourceLog": binding(content_source_log_path),
            "buildInputsLog": binding(build_inputs_log_path),
            "restoreLog": binding(restore_log_path),
            "buildLog": binding(build_log_path),
            "contentApkLog": binding(content_apk_log_path),
            "sourceGraphSealLog": binding(source_graph_seal_log_path),
            "commandJournal": binding(command_journal_path),
            "boundedProcessGroups": True,
            "warnings": 0,
            "errors": 0,
        },
        "toolchain": {
            "dotnetSdkVersion": DOTNET_SDK_VERSION,
            "javaVersion": java_version.strip(),
            "androidSdkPackages": binding(android_sdk_packages_path),
            "targetFramework": TARGET_FRAMEWORK, "targetSdkVersion": 36,
            "runtimeIdentifier": RUNTIME_IDENTIFIER, "configuration": CONFIGURATION,
            "serializedBuild": True,
        },
        "artifact": {
            "basename": apk.name, "sha256": file_sha256(apk),
            "sizeBytes": apk.stat().st_size, "package": PACKAGE, "abis": abis,
            "apiLevel": 36, "configuration": CONFIGURATION,
            "runtimeIdentifier": RUNTIME_IDENTIFIER, "targetFramework": TARGET_FRAMEWORK,
            "fullMauiArtifact": True, "installed": False,
        },
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
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
