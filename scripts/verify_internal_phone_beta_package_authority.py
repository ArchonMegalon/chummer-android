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
LOCK_TOP_LEVEL_KEYS = {
    "approvedPackageSources", "canonicalOwnerFeed", "consumer", "contractName",
    "contractVersion", "coreRuntimeFeed", "currentOwnerContractFeed",
    "externalPackages", "owners", "packages", "sdkArchive", "sdkVersion",
    "uiOwnerFeed",
}
RECEIPT_TOP_LEVEL_KEYS = {
    "buildExecutions", "buildProjects", "canonicalOwnerFeed",
    "childExecutableAuthority", "consumerCommit", "consumerPackagePlaneLock",
    "contractName", "contractVersion", "coreRuntimeFeed",
    "creationInitialAuthorityTimingContract", "currentOwnerContractFeed",
    "focusedCareerAdvanceTestExecution", "focusedOverviewTestExecution",
    "generatedAt", "localCompatibilityTree", "mode", "nugetConfigSha256",
    "ownerPackageArtifactCache", "ownerSources", "packageCacheWasFresh",
    "packageFeedInventorySha256", "packageInventory", "packageSources",
    "sdkArchiveSha512", "sdkVersion", "sourceInventory", "status",
    "stubPackagesAllowed", "testExecutions", "testProjects", "uiOwnerFeed",
}
EXPECTED_PRESENTATION_COMMIT = "1438978f6f883be321c62de69165c9216e10e011"
EXPECTED_PRESENTATION_TREE = "d1ae70610a1c4f43cfa8386db22d6f55e620fa6e"
EXPECTED_PRESENTATION_REPOSITORY = "https://github.com/ArchonMegalon/chummer6-ui.git"
EXPECTED_LOCK_PATH = "config/package-plane.lock.json"
EXPECTED_LOCK_SHA256 = "42e01c93a863882022cf156d86674cda1fbaecba7b9a1112323a27e42dd73a61"
EXPECTED_LOCK_SIZE = 54835
EXPECTED_LOCK_BLOB = "2cfcd24e9d7a2320a1844a2d5e0b831e1245e000"
EXPECTED_RECEIPT_SHA256 = "940d4cf0d77bb371e50b1cb3fb566089843a945c097b73a122522db1a673b547"
EXPECTED_RECEIPT_SIZE = 46808
EXPECTED_CACHE_KEY = "547f8e6670d2306207f85b0d2aa2f45c5f8e506b25dcba6db20f3485f7eb2b02"
EXPECTED_CACHE_MANIFEST_SHA256 = "b31e6f2b1903d9cab0cfe550c2892b9bb0ffc1183bbb8bb2eab4289b1710b09c"
EXPECTED_CACHE_MANIFEST_SIZE = 13707
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
        "2c6b273ed9eb11db0c3820ebb7e8434ccea6471e7ac2db38763a0aa08db294d9",
        70376,
    ),
    (
        "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj",
        "tests/Chummer.Android.Native.CompileCheck/packages.lock.json",
        "efbb131ff76a9d1e4d7ec86c5d6b50614587ff51a36316ede5a1d24c5800efd2",
        16179,
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


def require_exact_object(
    value: Any,
    label: str,
    expected_keys: set[str],
) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != expected_keys:
        raise ValueError(f"{label} schema is not exact")
    return value


def require_string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError(f"{label} must be one non-empty string")
    return value


def package_rows_by_id(
    value: Any,
    label: str,
    expected_row_keys: set[str],
) -> dict[str, dict[str, Any]]:
    if not isinstance(value, list) or not value:
        raise ValueError(f"{label} must contain package rows")
    result: dict[str, dict[str, Any]] = {}
    for value_index, row_value in enumerate(value):
        row = require_exact_object(
            row_value,
            f"{label} row {value_index}",
            expected_row_keys,
        )
        package_id = require_string(row.get("packageId"), f"{label} packageId")
        if package_id in result:
            raise ValueError(f"{label} contains duplicate packageId {package_id!r}")
        result[package_id] = row
    return result


def receipt_package_rows(value: Any, label: str) -> list[dict[str, Any]]:
    if not isinstance(value, list) or not value:
        raise ValueError(f"{label} must contain package rows")
    rows: list[dict[str, Any]] = []
    names: set[str] = set()
    for value_index, row_value in enumerate(value):
        row = require_exact_object(
            row_value,
            f"{label} row {value_index}",
            {"fileName", "sha256", "sizeBytes"},
        )
        file_name = require_string(row.get("fileName"), f"{label} fileName")
        digest = require_string(row.get("sha256"), f"{label} sha256")
        size = row.get("sizeBytes")
        if len(digest) != 64 or not isinstance(size, int) or isinstance(size, bool) or size <= 0:
            raise ValueError(f"{label} package bytes are malformed")
        if file_name in names:
            raise ValueError(f"{label} contains duplicate filename {file_name!r}")
        names.add(file_name)
        rows.append(row)
    return rows


def package_byte_projection(rows: Mapping[str, Mapping[str, Any]]) -> list[dict[str, Any]]:
    return sorted([
        {
            "fileName": row["fileName"],
            "sha256": row["sha256"],
            "sizeBytes": row["sizeBytes"],
        }
        for row in rows.values()
    ], key=lambda row: row["fileName"])


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


def validate_package_plane_lock(lock: Mapping[str, Any]) -> dict[str, Any]:
    lock = require_exact_object(lock, "Presentation package-plane lock", LOCK_TOP_LEVEL_KEYS)
    if lock.get("contractName") != LOCK_CONTRACT or lock.get("contractVersion") != 11:
        raise ValueError("Presentation package-plane lock contract drifted")

    sdk_version = require_string(lock.get("sdkVersion"), "Presentation package proof SDK")
    sdk_archive = require_exact_object(
        lock.get("sdkArchive"),
        "Presentation SDK archive",
        {"fileName", "rid", "sha512", "source", "version"},
    )
    if sdk_archive.get("version") != sdk_version:
        raise ValueError("Presentation package proof SDK archive drifted")

    core = require_exact_object(
        lock.get("coreRuntimeFeed"),
        "Presentation Core runtime feed",
        {
            "inventoryContract", "inventoryFileName", "inventorySha256",
            "lockContract", "lockFileName", "lockSha256", "packageRecipeCommit",
            "packageVersion", "packages", "receiptContract", "receiptFileName",
            "receiptSha256", "repository", "runtimeSourceCommit",
        },
    )
    core_rows = package_rows_by_id(
        core.get("packages"),
        "Presentation Core runtime feed",
        {"commit", "fileName", "packageId", "project", "repository", "sha256", "sizeBytes", "version"},
    )
    core_runtime = require_string(core.get("runtimeSourceCommit"), "Core runtime source commit")
    core_recipe = require_string(core.get("packageRecipeCommit"), "Core package recipe commit")
    core_version = require_string(core.get("packageVersion"), "Core package version")
    if any(
        row.get("commit") != core_runtime or row.get("version") != core_version
        for row in core_rows.values()
    ):
        raise ValueError("Presentation Core runtime package rows drifted")
    if core_version != CORE_VERSION:
        raise ValueError("Android Core compile version is not derived from the UI lock")

    canonical = require_exact_object(
        lock.get("canonicalOwnerFeed"),
        "Presentation canonical Hub feed",
        {
            "inventoryContract", "inventoryFileName", "inventorySha256",
            "lockContract", "lockPath", "lockSha256", "packageVersion", "packages",
            "producerCommit", "producerDirectory", "producerPath", "producerRepository",
            "producerSha256", "receiptContract", "receiptFileName", "receiptSha256",
        },
    )
    canonical_rows = package_rows_by_id(
        canonical.get("packages"),
        "Presentation canonical Hub feed",
        {"commit", "fileName", "packageId", "project", "repository", "sha256", "sizeBytes", "version"},
    )
    hub_producer = require_string(canonical.get("producerCommit"), "Hub producer commit")
    hub_version = require_string(canonical.get("packageVersion"), "Hub package version")
    for package_id in ("Chummer.Play.Contracts", "Chummer.Run.Contracts"):
        row = canonical_rows.get(package_id)
        if row is None or row.get("version") != hub_version:
            raise ValueError("Presentation Hub package rows drifted")
    if hub_version != HUB_VERSION:
        raise ValueError("Android Hub compile version is not derived from the UI lock")
    registry_row = canonical_rows.get("Chummer.Hub.Registry.Contracts")
    if registry_row is None or registry_row.get("version") != hub_version:
        raise ValueError("Presentation Registry package row is missing or drifted")
    registry_commit = require_string(registry_row.get("commit"), "Registry package commit")
    run_registry_row = canonical_rows.get("Chummer.Run.Registry")
    if run_registry_row is None or run_registry_row.get("commit") != registry_commit:
        raise ValueError("Presentation Registry package commits disagree")

    ui_owner = require_exact_object(
        lock.get("uiOwnerFeed"),
        "Presentation UI owner feed",
        {
            "dependencyAuthorityCacheKey", "inventoryContract", "inventoryFileName",
            "inventorySha256", "packageRecipeCommit", "packageRecipeSha256", "packages",
            "producerLockFileName", "producerLockPath", "producerLockSha256",
            "receiptContract", "receiptFileName", "receiptSha256", "sdkVersion",
        },
    )
    ui_owner_rows = package_rows_by_id(
        ui_owner.get("packages"),
        "Presentation UI owner feed",
        {
            "commit", "fileName", "ownerDirectory", "packageId", "project",
            "projectSha256", "repository", "sha256", "sizeBytes", "sourceTree", "version",
        },
    )
    if ui_owner.get("sdkVersion") != sdk_version:
        raise ValueError("Presentation UI owner package SDK drifted")
    ui_kit_row = ui_owner_rows.get("Chummer.Ui.Kit")
    campaign_row = ui_owner_rows.get("Chummer.Campaign.Contracts")
    if ui_kit_row is None or ui_kit_row.get("version") != UI_KIT_VERSION:
        raise ValueError("Presentation UI Kit package row is missing or drifted")
    if campaign_row is None or campaign_row.get("version") != CAMPAIGN_VERSION:
        raise ValueError("Presentation Campaign package row is missing or drifted")
    ui_kit_commit = require_string(ui_kit_row.get("commit"), "UI Kit package commit")

    return {
        "packageProofSdkVersion": sdk_version,
        "sourceGraph": {
            "corePackageRecipeCommit": core_recipe,
            "coreRuntimeSourceCommit": core_runtime,
            "hubProducerCommit": hub_producer,
            "registryCommit": registry_commit,
            "uiKitCommit": ui_kit_commit,
        },
        "coreRuntimeFeed": {
            "inventoryContract": core["inventoryContract"],
            "inventorySha256": core["inventorySha256"],
            "lockContract": core["lockContract"],
            "lockSha256": core["lockSha256"],
            "packageRecipeCommit": core_recipe,
            "packages": package_byte_projection(core_rows),
            "receiptContract": core["receiptContract"],
            "receiptSha256": core["receiptSha256"],
            "runtimeSourceCommit": core_runtime,
        },
        "canonicalOwnerFeed": {
            "inventoryContract": canonical["inventoryContract"],
            "inventorySha256": canonical["inventorySha256"],
            "lockContract": canonical["lockContract"],
            "lockSha256": canonical["lockSha256"],
            "packages": package_byte_projection(canonical_rows),
            "producerCommit": hub_producer,
            "producerPath": canonical["producerPath"],
            "producerRepository": canonical["producerRepository"],
            "producerSha256": canonical["producerSha256"],
            "receiptContract": canonical["receiptContract"],
            "receiptSha256": canonical["receiptSha256"],
        },
        "uiOwnerFeed": {
            "dependencyAuthorityCacheKey": ui_owner["dependencyAuthorityCacheKey"],
            "inventoryContract": ui_owner["inventoryContract"],
            "inventorySha256": ui_owner["inventorySha256"],
            "packageRecipeCommit": ui_owner["packageRecipeCommit"],
            "packageRecipeSha256": ui_owner["packageRecipeSha256"],
            "packages": package_byte_projection(ui_owner_rows),
            "producerLockSha256": ui_owner["producerLockSha256"],
            "receiptContract": ui_owner["receiptContract"],
            "receiptSha256": ui_owner["receiptSha256"],
            "sdkVersion": ui_owner["sdkVersion"],
        },
    }


def validate_presentation_repository(root: Path) -> dict[str, Any]:
    if not root.is_absolute() or root.is_symlink() or not root.is_dir() or root.resolve() != root:
        raise ValueError("Presentation root must be one canonical non-symlinked directory")
    if git(root, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Presentation current graph repository is dirty")
    if git(root, "rev-parse", "HEAD") != EXPECTED_PRESENTATION_COMMIT:
        raise ValueError("Presentation current graph commit drifted")
    if git(root, "rev-parse", "HEAD^{tree}") != EXPECTED_PRESENTATION_TREE:
        raise ValueError("Presentation current graph tree drifted")
    if git(root, "remote", "get-url", "origin") != EXPECTED_PRESENTATION_REPOSITORY:
        raise ValueError("Presentation repository authority drifted")
    authority = root / EXPECTED_LOCK_PATH
    if authority.is_symlink() or not authority.is_file():
        raise ValueError("Presentation package-plane lock is unavailable")
    if authority.stat().st_size != EXPECTED_LOCK_SIZE or sha256(authority) != EXPECTED_LOCK_SHA256:
        raise ValueError("Presentation package-plane lock bytes drifted")
    if git(root, "rev-parse", f"HEAD:{EXPECTED_LOCK_PATH}") != EXPECTED_LOCK_BLOB:
        raise ValueError("Presentation package-plane lock Git blob drifted")
    return validate_package_plane_lock(strict_json(authority, "Presentation package-plane lock"))


def validate_android_sdk_authority(
    android_root: Path,
    manifest: Mapping[str, Any],
    package_proof_sdk_version: str | None = None,
) -> None:
    sdk = manifest.get("sdkAuthority")
    sdk = require_exact_object(
        sdk,
        "Android SDK authority",
        {
            "packageProofSdkVersion", "androidGlobalPolicy", "releaseWorkflow",
            "selectedAndroidConsumerSdkVersion",
        },
    )
    policy = require_exact_object(
        sdk.get("androidGlobalPolicy"),
        "Android global SDK policy",
        {"path", "sha256", "version", "rollForward", "allowPrerelease"},
    )
    workflow = require_exact_object(
        sdk.get("releaseWorkflow"),
        "Android release workflow SDK authority",
        {"path", "sha256", "dotnetVersion"},
    )
    if policy.get("path") != "global.json" or workflow.get("path") != ".github/workflows/preview9-arm64-aab.yml":
        raise ValueError("Android SDK authority paths drifted")
    global_json = android_root / "global.json"
    workflow_path = android_root / ".github/workflows/preview9-arm64-aab.yml"
    if global_json.is_symlink() or workflow_path.is_symlink():
        raise ValueError("Android SDK authority cannot use symlinked inputs")
    if sha256(global_json) != policy.get("sha256") or sha256(workflow_path) != workflow.get("sha256"):
        raise ValueError("Android SDK authority bytes drifted")
    global_payload = require_exact_object(
        strict_json(global_json, "Android global SDK policy"),
        "Android global SDK policy",
        {"sdk"},
    )
    selected_policy = require_exact_object(
        global_payload.get("sdk"),
        "Android global SDK selection",
        {"version", "rollForward", "allowPrerelease"},
    )
    if {
        "version": policy.get("version"),
        "rollForward": policy.get("rollForward"),
        "allowPrerelease": policy.get("allowPrerelease"),
    } != selected_policy:
        raise ValueError("Android global SDK policy claims drifted from global.json")
    if package_proof_sdk_version is None:
        package_proof_sdk_version = require_string(
            sdk.get("packageProofSdkVersion"),
            "package proof SDK authority",
        )
    if sdk.get("packageProofSdkVersion") != package_proof_sdk_version:
        raise ValueError("package proof SDK authority drifted")
    selected_consumer_sdk = require_string(
        workflow.get("dotnetVersion"),
        "Android release workflow SDK selection",
    )
    if sdk.get("selectedAndroidConsumerSdkVersion") != selected_consumer_sdk:
        raise ValueError("Android consumer SDK authority drifted")
    if workflow_path.read_text(encoding="utf-8").count(f"dotnet-version: {selected_consumer_sdk}") != 1:
        raise ValueError("Android release workflow SDK selection drifted")
    for project, relative, digest, size in EXPECTED_ANDROID_LOCKS:
        lock = android_root / relative
        if lock.is_symlink() or not lock.is_file() or lock.stat().st_size != size or sha256(lock) != digest:
            raise ValueError(f"Android consumer lock bytes drifted: {project}")


def validate_receipt(receipt_path: Path) -> dict[str, Any]:
    receipt_path = require_private_regular_file(receipt_path, "UI current-graph receipt")
    if receipt_path.stat().st_size != EXPECTED_RECEIPT_SIZE or sha256(receipt_path) != EXPECTED_RECEIPT_SHA256:
        raise ValueError("UI current-graph receipt bytes are not exact")
    receipt = require_exact_object(
        strict_json(receipt_path, "UI current-graph receipt"),
        "UI current-graph receipt",
        RECEIPT_TOP_LEVEL_KEYS,
    )
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


def validate_bound_authority_claims(
    manifest: Mapping[str, Any],
    package_authority: Mapping[str, Any],
    receipt: Mapping[str, Any],
) -> None:
    source_graph = package_authority.get("sourceGraph")
    package_sdk = package_authority.get("packageProofSdkVersion")
    if manifest.get("sourceGraph") != source_graph:
        raise ValueError("Android source graph is not derived from the bound UI package lock")
    sdk = manifest.get("sdkAuthority")
    if not isinstance(sdk, dict) or sdk.get("packageProofSdkVersion") != package_sdk:
        raise ValueError("Android package proof SDK is not derived from the bound UI package lock")
    if receipt.get("sdkVersion") != package_sdk:
        raise ValueError("UI receipt package proof SDK disagrees with the bound UI package lock")

    receipt_core = require_exact_object(
        receipt.get("coreRuntimeFeed"),
        "UI receipt Core runtime feed",
        {
            "inventoryContract", "inventorySha256", "lockContract", "lockSha256",
            "packageCount", "packageRecipeCommit", "packages", "receiptContract",
            "receiptSha256", "runtimeSourceCommit", "selectedForCanonicalFullFeed", "status",
        },
    )
    receipt_core_rows = sorted(
        receipt_package_rows(receipt_core.get("packages"), "UI receipt Core runtime feed"),
        key=lambda row: row["fileName"],
    )
    expected_core = package_authority.get("coreRuntimeFeed")
    if not isinstance(expected_core, dict):
        raise ValueError("bound UI package lock Core projection is missing")
    if {
        "inventoryContract": receipt_core.get("inventoryContract"),
        "inventorySha256": receipt_core.get("inventorySha256"),
        "lockContract": receipt_core.get("lockContract"),
        "lockSha256": receipt_core.get("lockSha256"),
        "packageRecipeCommit": receipt_core.get("packageRecipeCommit"),
        "packages": receipt_core_rows,
        "receiptContract": receipt_core.get("receiptContract"),
        "receiptSha256": receipt_core.get("receiptSha256"),
        "runtimeSourceCommit": receipt_core.get("runtimeSourceCommit"),
    } != expected_core:
        raise ValueError("UI receipt Core authority disagrees with the bound UI package lock")
    if (
        receipt_core.get("packageCount") != len(receipt_core_rows)
        or receipt_core.get("selectedForCanonicalFullFeed") is not True
        or receipt_core.get("status") != "passed"
    ):
        raise ValueError("UI receipt Core authority status drifted")

    receipt_hub = require_exact_object(
        receipt.get("canonicalOwnerFeed"),
        "UI receipt canonical Hub feed",
        {
            "inventoryContract", "inventorySha256", "lockContract", "lockSha256",
            "packageCount", "packages", "producerCommit", "producerPath",
            "producerRepository", "producerSha256", "projectLockFilesEnforced",
            "receiptContract", "receiptSha256", "status",
        },
    )
    receipt_hub_rows = sorted(
        receipt_package_rows(receipt_hub.get("packages"), "UI receipt canonical Hub feed"),
        key=lambda row: row["fileName"],
    )
    expected_hub = package_authority.get("canonicalOwnerFeed")
    if not isinstance(expected_hub, dict):
        raise ValueError("bound UI package lock Hub projection is missing")
    if {
        "inventoryContract": receipt_hub.get("inventoryContract"),
        "inventorySha256": receipt_hub.get("inventorySha256"),
        "lockContract": receipt_hub.get("lockContract"),
        "lockSha256": receipt_hub.get("lockSha256"),
        "packages": receipt_hub_rows,
        "producerCommit": receipt_hub.get("producerCommit"),
        "producerPath": receipt_hub.get("producerPath"),
        "producerRepository": receipt_hub.get("producerRepository"),
        "producerSha256": receipt_hub.get("producerSha256"),
        "receiptContract": receipt_hub.get("receiptContract"),
        "receiptSha256": receipt_hub.get("receiptSha256"),
    } != expected_hub:
        raise ValueError("UI receipt Hub authority disagrees with the bound UI package lock")
    if (
        receipt_hub.get("packageCount") != len(receipt_hub_rows)
        or receipt_hub.get("projectLockFilesEnforced") is not True
        or receipt_hub.get("status") != "passed"
    ):
        raise ValueError("UI receipt Hub authority status drifted")

    receipt_ui = require_exact_object(
        receipt.get("uiOwnerFeed"),
        "UI receipt owner feed",
        {
            "dependencyAuthorityCacheKey", "inventoryContract", "inventorySha256",
            "packageCount", "packageRecipeCommit", "packageRecipeSha256", "packages",
            "producerLockSha256", "receiptContract", "receiptSha256", "sdkVersion", "status",
        },
    )
    receipt_ui_rows = sorted(
        receipt_package_rows(receipt_ui.get("packages"), "UI receipt owner feed"),
        key=lambda row: row["fileName"],
    )
    expected_ui = package_authority.get("uiOwnerFeed")
    if not isinstance(expected_ui, dict):
        raise ValueError("bound UI package lock owner projection is missing")
    if {
        "dependencyAuthorityCacheKey": receipt_ui.get("dependencyAuthorityCacheKey"),
        "inventoryContract": receipt_ui.get("inventoryContract"),
        "inventorySha256": receipt_ui.get("inventorySha256"),
        "packageRecipeCommit": receipt_ui.get("packageRecipeCommit"),
        "packageRecipeSha256": receipt_ui.get("packageRecipeSha256"),
        "packages": receipt_ui_rows,
        "producerLockSha256": receipt_ui.get("producerLockSha256"),
        "receiptContract": receipt_ui.get("receiptContract"),
        "receiptSha256": receipt_ui.get("receiptSha256"),
        "sdkVersion": receipt_ui.get("sdkVersion"),
    } != expected_ui:
        raise ValueError("UI receipt owner authority disagrees with the bound UI package lock")
    if (
        receipt_ui.get("packageCount") != len(receipt_ui_rows)
        or receipt_ui.get("status") != "passed"
    ):
        raise ValueError("UI receipt owner authority status drifted")


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
        package_authority = validate_presentation_repository(args.presentation_root)
        receipt = validate_receipt(args.receipt)
        validate_bound_authority_claims(manifest, package_authority, receipt)
        validate_android_sdk_authority(
            args.android_root,
            manifest,
            require_string(
                package_authority.get("packageProofSdkVersion"),
                "bound package proof SDK authority",
            ),
        )
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
