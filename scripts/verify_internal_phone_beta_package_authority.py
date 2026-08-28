#!/usr/bin/env python3
"""Authenticate Android's current, package-only Presentation dependency graph."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import stat
import subprocess
from pathlib import Path
from typing import Any, Mapping


CONTRACT = "chummer.android.internal-phone-beta-package-authority/v2"
RECEIPT_CONTRACT = "chummer6-ui.fresh-package-plane-verification"
LOCK_CONTRACT = "chummer6-ui.fresh-package-plane-lock"
CACHE_CONTRACT = "chummer6-ui.owner-package-artifact-cache/v1"
EXPECTED_PRESENTATION_COMMIT = "5beaccc912f914c8ff4ae262509ed4d13b84bf75"
EXPECTED_PRESENTATION_TREE = "3741b4a314540a7a88a3532524429a8cd358743b"
EXPECTED_PRESENTATION_REPOSITORY = "https://github.com/ArchonMegalon/chummer6-ui.git"
EXPECTED_COMPATIBILITY_REPOSITORY = "https://github.com/ArchonMegalon/chummer6-ui-kit.git"
EXPECTED_LOCK_PATH = "config/package-plane.lock.json"
EXPECTED_LOCK_SHA256 = "c24b07d27b249dfe073ecd664b88d0b1d1b723bd6cd97c82dbaf7e8e7874977d"
EXPECTED_LOCK_SIZE = 54833
EXPECTED_LOCK_BLOB = "63b19db9f9be9d4e96d23ad2b2dea80811329a92"
EXPECTED_RECEIPT_SHA256 = "3fb8b1913fd3a975e8ec038f2799ca0bedad557e1350c84956861ea6fdff7d08"
EXPECTED_RECEIPT_SIZE = 46803
EXPECTED_CACHE_KEY = "408008a4928f00e08e380ce588a99eea189fbfceed3fc5a2faf6f0baaf8d3c7b"
EXPECTED_CACHE_MANIFEST_SHA256 = "e65cea39593f7156c0d4302c0aa882fd7b963574c78280f2883e3cc14bc37cf6"
EXPECTED_CACHE_MANIFEST_SIZE = 13705
EXPECTED_PACKAGE_COUNT = 18
EXPECTED_SOURCE_GRAPH = {
    "corePackageRecipeCommit": "3260ac73714d8b001a3599d6776196e394dc6c35",
    "coreRuntimeSourceCommit": "febd698752e195dceef79fbc3f83dc971564fe00",
    "hubProducerCommit": "8cc22cb6fdf9bdf2af3c390125f7a88de90700b3",
    "registryCommit": "af9a7e19c3bf331e96411dfb8f9e7820a98cab29",
    "uiKitCommit": "d51ecd99cf72098d4adc8db0192bff7bf9fd8e61",
}
EXPECTED_ANDROID_LOCKS = (
    (
        "src/Chummer.Android/Chummer.Android.csproj",
        "src/Chummer.Android/packages.lock.json",
        "c4d3bccece5ee750cc71aaead9cf1d65423b7b21038b6cb237f43bf092270d22",
        70375,
    ),
    (
        "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj",
        "tests/Chummer.Android.Native.CompileCheck/packages.lock.json",
        "f0040f4ff8b968899519c8e24af09e28c680ff0484569abfc2319f18df7cce78",
        16178,
    ),
)
CORE_VERSION = "0.0.0-packageplane.candidate.shfebd698752e19"
HUB_VERSION = "0.1.0-packageplane.candidate.sh66c418a5004f"
CAMPAIGN_VERSION = "0.1.0-preview"
UI_KIT_VERSION = "0.1.0-preview"
EXPECTED_COMPILE_PACKAGES = {
    "Chummer.Application": CORE_VERSION,
    "Chummer.Campaign.Contracts": CAMPAIGN_VERSION,
    "Chummer.Engine.Contracts": CORE_VERSION,
    "Chummer.Hub.Registry.Contracts": HUB_VERSION,
    "Chummer.Infrastructure": CORE_VERSION,
    "Chummer.Play.Contracts": HUB_VERSION,
    "Chummer.Rulesets.Hosting": CORE_VERSION,
    "Chummer.Rulesets.Sr4": CORE_VERSION,
    "Chummer.Rulesets.Sr5": CORE_VERSION,
    "Chummer.Rulesets.Sr6": CORE_VERSION,
    "Chummer.Run.Contracts": HUB_VERSION,
    "Chummer.Ui.Kit": UI_KIT_VERSION,
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def strict_json(path: Path, label: str) -> dict[str, Any]:
    def reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{label} contains duplicate key {key!r}")
            result[key] = value
        return result

    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=reject_duplicates,
            parse_constant=lambda item: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite number {item}")
            ),
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read {label}: {error}") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one JSON object")
    return value


def git(root: Path, *arguments: str) -> str:
    return subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def require_private_regular_file(path: Path, label: str) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_file():
        raise ValueError(f"{label} must be an absolute non-symlinked regular file")
    resolved = path.resolve(strict=True)
    if resolved != path:
        raise ValueError(f"{label} must use its canonical path")
    if stat.S_IMODE(path.stat().st_mode) & 0o077:
        raise ValueError(f"{label} must be owner-only")
    if path.stat().st_uid != os.getuid():
        raise ValueError(f"{label} must be owned by the current user")
    return resolved


def validate_manifest(path: Path) -> dict[str, Any]:
    manifest = strict_json(path, "internal phone-beta authority")
    expected_keys = {
        "contractName", "authorityClass", "authorityState", "publicationAuthorized",
        "presentationSource", "packagePlaneLock", "verificationReceipt",
        "artifactCache", "sourceGraph", "dependencyMode", "sdkAuthority",
        "headlessRuntimeBinding", "androidConsumerLocks", "doesNotAssert",
    }
    if set(manifest) != expected_keys or manifest.get("contractName") != CONTRACT:
        raise ValueError("internal phone-beta authority schema is not exact")
    if manifest.get("authorityClass") != "internal_phone_beta_only":
        raise ValueError("internal authority class drifted")
    if manifest.get("authorityState") != "current_graph_verified":
        raise ValueError("internal authority is not the current verified graph")
    if manifest.get("publicationAuthorized") is not False:
        raise ValueError("internal phone-beta authority cannot authorize publication")
    if manifest.get("presentationSource") != {
        "commit": EXPECTED_PRESENTATION_COMMIT,
        "tree": EXPECTED_PRESENTATION_TREE,
        "repository": EXPECTED_PRESENTATION_REPOSITORY,
        "compatibilityCheckoutRepository": EXPECTED_COMPATIBILITY_REPOSITORY,
    }:
        raise ValueError("Presentation current graph binding is not exact")
    if manifest.get("packagePlaneLock") != {
        "path": EXPECTED_LOCK_PATH,
        "contractName": LOCK_CONTRACT,
        "contractVersion": 11,
        "sha256": EXPECTED_LOCK_SHA256,
        "sizeBytes": EXPECTED_LOCK_SIZE,
        "gitBlob": EXPECTED_LOCK_BLOB,
    }:
        raise ValueError("Presentation package-plane lock binding is not exact")
    if manifest.get("verificationReceipt") != {
        "contractName": RECEIPT_CONTRACT,
        "contractVersion": 11,
        "sha256": EXPECTED_RECEIPT_SHA256,
        "sizeBytes": EXPECTED_RECEIPT_SIZE,
        "status": "passed",
    }:
        raise ValueError("Presentation verification receipt binding is not exact")
    if manifest.get("artifactCache") != {
        "contractName": CACHE_CONTRACT,
        "cacheKey": EXPECTED_CACHE_KEY,
        "manifestFileName": "owner-package-cache.json",
        "manifestSha256": EXPECTED_CACHE_MANIFEST_SHA256,
        "manifestSizeBytes": EXPECTED_CACHE_MANIFEST_SIZE,
        "packageCount": EXPECTED_PACKAGE_COUNT,
    }:
        raise ValueError("Presentation artifact-cache binding is not exact")
    if manifest.get("sourceGraph") != EXPECTED_SOURCE_GRAPH:
        raise ValueError("current Core/Hub/Registry/UI Kit source graph is not exact")
    if manifest.get("dependencyMode") != {
        "packageOnly": True,
        "restoreLockedMode": True,
        "sourceCheckoutsPresent": False,
        "siblingsAllowed": False,
    }:
        raise ValueError("internal dependency mode is not package-only, locked, and sibling-free")
    if manifest.get("headlessRuntimeBinding") != {
        "project": "Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
        "androidEntryPoint": "AddChummerLocalRuntimeClient",
        "role": "android-headless-runtime-dependency",
        "includesAvaloniaUi": False,
        "includesBlazorUi": False,
        "desktopReleaseGate": False,
    }:
        raise ValueError("headless runtime binding is not exact")
    expected_locks = [
        {"project": project, "path": lock, "sha256": digest, "sizeBytes": size}
        for project, lock, digest, size in EXPECTED_ANDROID_LOCKS
    ]
    if manifest.get("androidConsumerLocks") != expected_locks:
        raise ValueError("Android consumer lock bindings are not exact")
    expected_nonclaims = [
        "api36_device_execution", "google_play_upload", "public_release_readiness",
        "publication_authority", "tablet_readiness",
    ]
    if manifest.get("doesNotAssert") != expected_nonclaims:
        raise ValueError("internal authority non-claims are not exact")
    return manifest


def validate_presentation_repository(root: Path) -> None:
    if not root.is_absolute() or root.is_symlink() or not root.is_dir() or root.resolve() != root:
        raise ValueError("Presentation root must be one canonical non-symlinked directory")
    if git(root, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Presentation current graph repository is dirty")
    if git(root, "rev-parse", "HEAD") != EXPECTED_PRESENTATION_COMMIT:
        raise ValueError("Presentation current graph commit drifted")
    if git(root, "rev-parse", "HEAD^{tree}") != EXPECTED_PRESENTATION_TREE:
        raise ValueError("Presentation current graph tree drifted")
    if git(root, "remote", "get-url", "origin") != EXPECTED_COMPATIBILITY_REPOSITORY:
        raise ValueError("Presentation repository authority drifted")
    authority = root / EXPECTED_LOCK_PATH
    if authority.is_symlink() or not authority.is_file():
        raise ValueError("Presentation package-plane lock is unavailable")
    if authority.stat().st_size != EXPECTED_LOCK_SIZE or sha256(authority) != EXPECTED_LOCK_SHA256:
        raise ValueError("Presentation package-plane lock bytes drifted")
    if git(root, "rev-parse", f"HEAD:{EXPECTED_LOCK_PATH}") != EXPECTED_LOCK_BLOB:
        raise ValueError("Presentation package-plane lock Git blob drifted")


def validate_android_sdk_authority(android_root: Path, manifest: Mapping[str, Any]) -> None:
    sdk = manifest.get("sdkAuthority")
    if not isinstance(sdk, dict):
        raise ValueError("Android SDK authority is missing")
    policy = sdk.get("androidGlobalPolicy")
    workflow = sdk.get("releaseWorkflow")
    if not isinstance(policy, dict) or not isinstance(workflow, dict):
        raise ValueError("Android SDK authority is malformed")
    global_json = android_root / str(policy.get("path"))
    workflow_path = android_root / str(workflow.get("path"))
    if sha256(global_json) != policy.get("sha256") or sha256(workflow_path) != workflow.get("sha256"):
        raise ValueError("Android SDK authority bytes drifted")
    if sdk.get("packageProofSdkVersion") != "10.0.103":
        raise ValueError("package proof SDK authority drifted")
    if sdk.get("selectedAndroidConsumerSdkVersion") != "10.0.111":
        raise ValueError("Android consumer SDK authority drifted")
    if workflow_path.read_text(encoding="utf-8").count("dotnet-version: 10.0.111") != 1:
        raise ValueError("Android release workflow SDK selection drifted")
    for project, relative, digest, size in EXPECTED_ANDROID_LOCKS:
        lock = android_root / relative
        if lock.is_symlink() or not lock.is_file() or lock.stat().st_size != size or sha256(lock) != digest:
            raise ValueError(f"Android consumer lock bytes drifted: {project}")


def validate_receipt(receipt_path: Path) -> dict[str, Any]:
    receipt_path = require_private_regular_file(receipt_path, "UI current-graph receipt")
    if receipt_path.stat().st_size != EXPECTED_RECEIPT_SIZE or sha256(receipt_path) != EXPECTED_RECEIPT_SHA256:
        raise ValueError("UI current-graph receipt bytes are not exact")
    receipt = strict_json(receipt_path, "UI current-graph receipt")
    if receipt.get("contractName") != RECEIPT_CONTRACT or receipt.get("contractVersion") != 11:
        raise ValueError("UI current-graph receipt contract drifted")
    if receipt.get("status") != "passed" or receipt.get("mode") != "integration":
        raise ValueError("UI current-graph receipt did not pass integration mode")
    if receipt.get("consumerCommit") != EXPECTED_PRESENTATION_COMMIT:
        raise ValueError("UI current-graph receipt consumer drifted")
    if receipt.get("localCompatibilityTree") is not False or receipt.get("packageCacheWasFresh") is not True:
        raise ValueError("UI current-graph receipt used a local tree or stale cache")
    lock = receipt.get("consumerPackagePlaneLock")
    if lock != {"path": EXPECTED_LOCK_PATH, "sha256": EXPECTED_LOCK_SHA256, "sizeBytes": EXPECTED_LOCK_SIZE}:
        raise ValueError("UI current-graph receipt package lock drifted")
    cache = receipt.get("ownerPackageArtifactCache")
    if not isinstance(cache, dict):
        raise ValueError("UI current-graph receipt cache binding is missing")
    if cache.get("contract") != CACHE_CONTRACT or cache.get("cacheKey") != EXPECTED_CACHE_KEY:
        raise ValueError("UI current-graph receipt cache key drifted")
    if cache.get("packageCount") != EXPECTED_PACKAGE_COUNT or cache.get("status") != "passed" or cache.get("used") is not True:
        raise ValueError("UI current-graph receipt cache status drifted")
    if cache.get("manifest") != {
        "path": "owner-package-cache.json",
        "sha256": EXPECTED_CACHE_MANIFEST_SHA256,
        "sizeBytes": EXPECTED_CACHE_MANIFEST_SIZE,
    }:
        raise ValueError("UI current-graph receipt cache manifest drifted")
    cached_packages = cache.get("packages")
    if not isinstance(cached_packages, list) or len(cached_packages) != EXPECTED_PACKAGE_COUNT:
        raise ValueError("UI current-graph receipt cached package inventory is not exact")
    return receipt


def validate_package_feed(feed: Path) -> dict[str, Any]:
    if not feed.is_absolute() or feed.is_symlink() or not feed.is_dir() or feed.resolve() != feed:
        raise ValueError("current package feed must be one canonical non-symlinked directory")
    manifest_path = feed.parent / "owner-package-cache.json"
    if manifest_path.is_symlink() or not manifest_path.is_file():
        raise ValueError("current package cache manifest is unavailable")
    if manifest_path.stat().st_size != EXPECTED_CACHE_MANIFEST_SIZE or sha256(manifest_path) != EXPECTED_CACHE_MANIFEST_SHA256:
        raise ValueError("current package cache manifest bytes drifted")
    cache = strict_json(manifest_path, "current package cache manifest")
    if cache.get("contract") != CACHE_CONTRACT or cache.get("cacheKey") != EXPECTED_CACHE_KEY:
        raise ValueError("current package cache authority drifted")
    rows = cache.get("packages")
    if not isinstance(rows, list) or len(rows) != EXPECTED_PACKAGE_COUNT:
        raise ValueError("current package cache must bind exactly eighteen packages")
    expected: dict[str, tuple[str, int]] = {}
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("current package cache row is malformed")
        name = row.get("fileName")
        digest = row.get("sha256")
        size = row.get("sizeBytes")
        if not isinstance(name, str) or not isinstance(digest, str) or not isinstance(size, int):
            raise ValueError("current package cache row fields are malformed")
        if name in expected:
            raise ValueError("current package cache contains duplicate filenames")
        expected[name] = (digest, size)
    actual = {path.name: path for path in feed.iterdir() if path.is_file()}
    if set(actual) != set(expected):
        raise ValueError("current package feed does not match the exact eighteen-package cache")
    for name, path in actual.items():
        digest, size = expected[name]
        if path.is_symlink() or path.stat().st_size != size or sha256(path) != digest:
            raise ValueError(f"current package bytes drifted: {name}")
    return cache


def build_binding(manifest: Mapping[str, Any]) -> dict[str, Any]:
    return dict(manifest)


def write_exclusive(path: Path, payload: Mapping[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0), 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
        json.dump(payload, stream, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    repo_root = Path(__file__).resolve().parents[1]
    parser.add_argument("--manifest", type=Path, default=repo_root / "eng/internal-phone-beta-package-authority.json")
    parser.add_argument("--presentation-root", type=Path, required=True)
    parser.add_argument("--android-root", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--package-feed", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        manifest = validate_manifest(args.manifest)
        validate_android_sdk_authority(args.android_root, manifest)
        validate_presentation_repository(args.presentation_root)
        validate_receipt(args.receipt)
        validate_package_feed(args.package_feed)
        binding = build_binding(manifest)
        if args.output is not None:
            write_exclusive(args.output, binding)
        print(json.dumps({
            "contractName": CONTRACT,
            "status": "pass",
            "authorityClass": "internal_phone_beta_only",
            "publicationAuthorized": False,
            "receiptSha256": EXPECTED_RECEIPT_SHA256,
            "packagePinCount": EXPECTED_PACKAGE_COUNT,
            "ownerPackagePinCount": 6,
            "doesNotAssert": manifest["doesNotAssert"],
        }, sort_keys=True))
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(json.dumps({
            "contractName": CONTRACT,
            "status": "blocked",
            "publicationAuthorized": False,
            "error": str(error),
        }, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
