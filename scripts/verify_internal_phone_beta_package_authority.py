#!/usr/bin/env python3
"""Authenticate the locked package plane consumed by pinned Presentation source."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
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
    "focusedContinuationTestExecution", "focusedOwnerShellTestExecution",
    "focusedExistingOwnerRegressionTestExecutions",
    "generatedAt", "localCompatibilityTree", "mode", "nugetConfigSha256",
    "ownerPackageArtifactCache", "ownerSources", "packageCacheWasFresh",
    "packageFeedInventorySha256", "packageInventory", "packageSources",
    "sdkArchiveSha512", "sdkVersion", "sourceInventory", "status",
    "stubPackagesAllowed", "testExecutions", "testProjects", "uiOwnerFeed",
}
EXPECTED_PRESENTATION_COMMIT = "f0f88fe31db4ab7528340591ac93abec43791a7b"
EXPECTED_PRESENTATION_TREE = "801fccb6c657c6e468f42ee6acf25553be53348d"
EXPECTED_PRESENTATION_REPOSITORY = "https://github.com/ArchonMegalon/chummer6-ui.git"
EXPECTED_LOCK_PATH = "config/package-plane.lock.json"
EXPECTED_LOCK_SHA256 = "54c80a11ef989d7c0c9c2f10dc0a0b28ac9c098e53d7e98c93efee7ccde7df47"
EXPECTED_LOCK_SIZE = 66890
EXPECTED_LOCK_BLOB = "bc22b16fdce93db2a2a0ade1fc6516bb2121595e"
EXPECTED_RECEIPT_SHA256 = "1e90742f36a05d4b1801e3ac393556a949f020f723afbeff4712144c51cfbdc9"
EXPECTED_RECEIPT_SIZE = 71957
EXPECTED_CACHE_KEY = "aacfee609963a1a702738f470c03ea3a328b817299472b2ceb135b85082dac35"
EXPECTED_CACHE_MANIFEST_SHA256 = "3ce42acedc97dfeb5d273048b7de23699f32d214a135f2340df1b347c703f3ed"
EXPECTED_CACHE_MANIFEST_SIZE = 13596
EXPECTED_PACKAGE_COUNT = 18
EXPECTED_CACHE_AUTHORITY_COUNT = 13
EXPECTED_SOURCE_GRAPH = {
    "corePackageRecipeCommit": "2d97ba450de0cb2b558984cc4637f2678a75d26a",
    "coreRuntimeSourceCommit": "181faa98a540294b72e6fe177751fd4308590fae",
    "hubProducerCommit": "6b1b2e03b2768f820b54a77bb474fbdd2fdfdc0e",
    "registryCommit": "af9a7e19c3bf331e96411dfb8f9e7820a98cab29",
    "uiKitCommit": "d51ecd99cf72098d4adc8db0192bff7bf9fd8e61",
}
EXPECTED_RUNTIME_HUB_COMMIT = "6b1b2e03b2768f820b54a77bb474fbdd2fdfdc0e"
RUNTIME_SOURCE_WORKFLOW_PATH = ".github/workflows/api36-editing-e2e.yml"
EXPECTED_ANDROID_LOCKS = (
    (
        "src/Chummer.Android/Chummer.Android.csproj",
        "src/Chummer.Android/packages.lock.json",
        "817d2d49b98681fc69f69ac848eb28f721a51fa5fa7bba04b99787edcfe5b794",
        70263,
    ),
    (
        "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj",
        "tests/Chummer.Android.Native.CompileCheck/packages.lock.json",
        "a0e2e8c01de30e37ca0c14aba5c19321b287d7016b110596444f9326877a54b4",
        16066,
    ),
)
CORE_VERSION = "0.0.0-packageplane.candidate.sh181faa98a5402"
HUB_VERSION = "0.1.1-packageplane.20260910.1"
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
PRODUCT_TEST_PROJECT = "Chummer.Product.UnitTests/Chummer.Product.UnitTests.csproj"
PRODUCT_TEST_ASSEMBLY = "Chummer.Product.UnitTests/bin/Release/net10.0/Chummer.Product.UnitTests.dll"
# Configured execution floors from the reviewed UI producer, not observed totals.
OWNER_TEST_EXECUTIONS = (
    ("focusedContinuationTestExecution", "InProcessWorkspaceContinuationTests", "Chummer.Tests/InProcessWorkspaceContinuationTests.cs", 19),
    ("focusedOwnerShellTestExecution", "InProcessShellOwnerContextTests", "Chummer.Tests/InProcessShellOwnerContextTests.cs", 26),
)
EXISTING_OWNER_TEST_EXECUTIONS = (
    ("InProcessChummerClientRulesetPluginTests", "Chummer.Tests/InProcessChummerClientRulesetPluginTests.cs", 74),
    ("ShellBootstrapDataProviderTests", "Chummer.Tests/Presentation/ShellBootstrapDataProviderTests.cs", 24),
    ("ShellPresenterTests", "Chummer.Tests/Presentation/ShellPresenterTests.cs", 80),
    ("WorkspaceSessionActivationServiceTests", "Chummer.Tests/Presentation/WorkspaceSessionActivationServiceTests.cs", 5),
    ("WorkspaceSessionPresenterTests", "Chummer.Tests/Presentation/WorkspaceSessionPresenterTests.cs", 23),
    ("WorkspaceViewStateStoreTests", "Chummer.Tests/Presentation/WorkspaceViewStateStoreTests.cs", 6),
    ("RestartSafeWorkspacePersistenceTests", "Chummer.Tests/RestartSafeWorkspacePersistenceTests.cs", 1),
    ("WorkspaceOverviewFinalizationOwnerTests", "Chummer.CreationWizard.Presentation.Tests/WorkspaceOverviewFinalizationOwnerTests.cs", 24),
)


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


def validate_runtime_hub_source_checkout(workflow_path: Path) -> dict[str, str]:
    """Bind runtime and package roles independently, even at one exact commit."""
    if workflow_path.is_symlink() or not workflow_path.is_file():
        raise ValueError("API-36 runtime source workflow is unavailable")
    lines = workflow_path.read_text(encoding="utf-8").splitlines()
    repository_line = "repository: ArchonMegalon/chummer6-hub"
    checkout_indexes = [
        index for index, line in enumerate(lines) if line.strip() == repository_line
    ]
    if len(checkout_indexes) != 1:
        raise ValueError("API-36 runtime Hub checkout must occur exactly once")
    index = checkout_indexes[0]
    refs = [
        line.strip().split(":", 1)[1].strip()
        for line in lines[index + 1 : index + 8]
        if line.strip().startswith("ref:")
    ]
    if refs != [EXPECTED_RUNTIME_HUB_COMMIT]:
        raise ValueError("API-36 runtime Hub checkout commit drifted")
    package_producer = EXPECTED_SOURCE_GRAPH["hubProducerCommit"]
    # Both roles may intentionally use the same reviewed source commit. The
    # runtime allowlist above is independent: a different package producer must
    # never implicitly select the runtime checkout or confer API-36 authority.
    return {
        "runtimeSourceCommit": EXPECTED_RUNTIME_HUB_COMMIT,
        "packageProducerCommit": package_producer,
    }


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


def cache_file_name(value: Any, label: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", value):
        raise ValueError(f"{label} must be one canonical filename")
    return value


def cache_byte_identity(digest: Any, size: Any, label: str) -> None:
    if (
        not isinstance(digest, str)
        or not re.fullmatch(r"[0-9a-f]{64}", digest)
        or type(size) is not int
        or size <= 0
    ):
        raise ValueError(f"{label} byte identity is malformed")


def cache_file_inventory(
    path: Path, label: str, *, expected_size: int | None = None, capture: bool = False,
) -> tuple[dict[str, Any], bytes]:
    """Hash the actual regular file, rejecting substitution during the read."""
    def identity(metadata: os.stat_result) -> tuple[int, ...]:
        return (metadata.st_dev, metadata.st_ino, metadata.st_mode, metadata.st_size,
                metadata.st_mtime_ns, metadata.st_ctime_ns)

    try:
        if not path.is_absolute() or path.resolve(strict=True) != path:
            raise ValueError(f"{label} path is not canonical or contains a symlink")
        before = path.lstat()
        if not stat.S_ISREG(before.st_mode):
            raise ValueError(f"{label} must be a regular non-symlinked file")
        descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        try:
            opened = os.fstat(descriptor)
            if identity(opened) != identity(before):
                raise ValueError(f"{label} changed before read")
            if expected_size is not None and opened.st_size != expected_size:
                raise ValueError(f"{label} size differs")
            digest = hashlib.sha256()
            chunks: list[bytes] = []
            read_size = 0
            # One byte beyond the opened size detects growth without following
            # an indefinitely growing file or capturing unbounded manifest data.
            while chunk := os.read(descriptor, min(1024 * 1024, opened.st_size - read_size + 1)):
                read_size += len(chunk)
                if read_size > opened.st_size:
                    raise ValueError(f"{label} grew during read")
                digest.update(chunk)
                if capture:
                    chunks.append(chunk)
            if read_size != opened.st_size:
                raise ValueError(f"{label} shrank during read")
            if identity(os.fstat(descriptor)) != identity(opened):
                raise ValueError(f"{label} changed during read")
        finally:
            os.close(descriptor)
        if identity(path.lstat()) != identity(before):
            raise ValueError(f"{label} changed after read")
    except OSError as error:
        raise ValueError(f"{label} is unavailable: {error}") from error
    return {"sha256": digest.hexdigest(), "sizeBytes": opened.st_size}, b"".join(chunks)


def validate_cold_cache_receipt(value: Any) -> dict[str, Any]:
    cache = require_exact_object(value, "UI cold/non-use cache receipt", {
        "coldProducerFallbackOnCacheMiss", "contract", "status", "used",
    })
    if (
        cache["coldProducerFallbackOnCacheMiss"] is not True
        or cache["contract"] != CACHE_CONTRACT
        or cache["status"] != "not_supplied"
        or cache["used"] is not False
    ):
        raise ValueError("UI cold/non-use cache receipt posture is not exact")
    return cache


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
        raise ValueError(
            "internal package-plane dependency mode is not package-only, locked, and source-fallback-free"
        )
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

    legacy = require_exact_object(
        lock.get("currentOwnerContractFeed"),
        "Presentation legacy owner-contract feed",
        {
            "inventoryContract", "inventoryFileName", "inventorySha256", "lockContract",
            "lockPath", "lockSha256", "ownerDirectory", "packageFeedInventorySha256",
            "packageVersion", "packages", "producerCommit", "producerPath",
            "producerRepository", "producerSha256", "selectedForCoreRuntimeCompatibility",
        },
    )
    legacy_rows = package_rows_by_id(
        legacy.get("packages"),
        "Presentation legacy owner-contract feed",
        {"commit", "fileName", "packageId", "project", "repository", "sha256", "sizeBytes", "version"},
    )
    legacy_version = require_string(legacy.get("packageVersion"), "legacy owner-contract package version")
    if (
        any(row.get("version") != legacy_version for row in legacy_rows.values())
        or legacy.get("selectedForCoreRuntimeCompatibility") is not True
    ):
        raise ValueError("Presentation legacy owner-contract package rows drifted")

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
        "currentOwnerContractFeed": {
            "inventoryContract": legacy["inventoryContract"],
            "inventorySha256": legacy["inventorySha256"],
            "lockContract": legacy["lockContract"],
            "lockSha256": legacy["lockSha256"],
            "packageFeedInventorySha256": legacy["packageFeedInventorySha256"],
            "packageVersion": legacy_version,
            "packages": package_byte_projection(legacy_rows),
            "producerCommit": legacy["producerCommit"],
            "producerPath": legacy["producerPath"],
            "producerRepository": legacy["producerRepository"],
            "producerSha256": legacy["producerSha256"],
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


def validate_owner_test_executions(receipt: Mapping[str, Any]) -> None:
    """Validate recorded invocation identity; do not invent observed test counts.

    The separately pinned passed receipt authenticates execution success. Source
    roots here are provenance only, never filesystem or restore authority.
    """
    executions = receipt.get("testExecutions")
    if receipt.get("testProjects") != [PRODUCT_TEST_PROJECT] or not isinstance(executions, list) or len(executions) != 1:
        raise ValueError("UI owner tests require the one complete Product test execution")
    full = require_exact_object(executions[0], "UI full Product test execution", {
        "coreProjectionContent", "buildInParallel", "compileRunner", "disableBuildServers",
        "maxCpuCount", "minimumExpectedTests", "project", "runner", "sdkVersion",
        "testAssembly", "useSharedCompilation",
    })
    sdk = require_string(receipt.get("sdkVersion"), "UI test SDK")
    if (
        full["project"] != PRODUCT_TEST_PROJECT
        or full["runner"] != "direct-exact-assembly"
        or full["compileRunner"] != "serialized-package-plane-build"
        or full["sdkVersion"] != sdk
        or full["buildInParallel"] is not False
        or full["disableBuildServers"] is not True
        or full["useSharedCompilation"] is not False
        or type(full["maxCpuCount"]) is not int or full["maxCpuCount"] != 1
        or type(full["minimumExpectedTests"]) is not int or full["minimumExpectedTests"] != 771
    ):
        raise ValueError("UI full Product test invocation is not exact")
    assembly = require_exact_object(full["testAssembly"], "UI Product test assembly", {"path", "sha256", "sizeBytes"})
    cache_byte_identity(assembly["sha256"], assembly["sizeBytes"], "UI Product test assembly")
    if assembly["path"] != PRODUCT_TEST_ASSEMBLY:
        raise ValueError("UI Product test assembly path differs")

    content = require_exact_object(full["coreProjectionContent"], "UI test Core projection", {
        "repository", "checkoutCommit", "runtimeSourceCommit", "packageRecipeCommit", "sourceRoot",
        "usage", "contentDirectories", "fileCount", "contentInventorySha256",
    })
    source = EXPECTED_SOURCE_GRAPH["coreRuntimeSourceCommit"]
    recipe = EXPECTED_SOURCE_GRAPH["corePackageRecipeCommit"]
    core_feed = receipt.get("coreRuntimeFeed")
    source_root = content["sourceRoot"]
    if (
        content["repository"] != "https://github.com/ArchonMegalon/chummer6-core.git"
        or content["runtimeSourceCommit"] != source or content["packageRecipeCommit"] != recipe
        or content["checkoutCommit"] not in (source, recipe)
        or not isinstance(core_feed, dict)
        or core_feed.get("runtimeSourceCommit") != source or core_feed.get("packageRecipeCommit") != recipe
        or content["usage"] != "read-only-rule-data-not-project-reference"
        or content["contentDirectories"] != ["Chummer/data", "Chummer/lang", "Chummer/customdata"]
        or not isinstance(source_root, str) or not source_root.startswith("/")
        or "\\" in source_root or "\0" in source_root or source_root == "/"
        or any(part in ("", ".", "..") for part in source_root[1:].split("/"))
        or type(content["fileCount"]) is not int or content["fileCount"] <= 0
        or not isinstance(content["contentInventorySha256"], str)
        or re.fullmatch(r"[0-9a-f]{64}", content["contentInventorySha256"]) is None
    ):
        raise ValueError("UI test Core projection is not bound to the runtime feed")

    inventory = receipt.get("sourceInventory")
    if not isinstance(inventory, list) or not inventory:
        raise ValueError("UI owner test source inventory is missing")
    source_paths: list[str] = []
    for value in inventory:
        row = require_exact_object(value, "UI test source inventory row", {"path", "sha256", "sizeBytes"})
        path = row["path"]
        if (
            not isinstance(path, str) or "\\" in path or "\0" in path
            or any(part in ("", ".", "..") for part in path.split("/"))
            or not isinstance(row["sha256"], str) or re.fullmatch(r"[0-9a-f]{64}", row["sha256"]) is None
            or type(row["sizeBytes"]) is not int or row["sizeBytes"] < 0
        ):
            raise ValueError("UI owner test source inventory row is malformed")
        source_paths.append(path)
    if source_paths != sorted(set(source_paths)) or PRODUCT_TEST_PROJECT not in source_paths:
        raise ValueError("UI owner test source inventory membership is not canonical")

    def validate_execution(value: Any, test_class: str, source_file: str, minimum: int) -> None:
        row = require_exact_object(value, f"UI focused owner test {test_class}", {
            "coreProjectionContent", "filter", "minimumExpectedTests", "project", "reuseFullSuiteBuild",
            "runner", "sdkVersion", "sourceFiles", "testAssembly",
        })
        # Validate types before equality: bool/int and float/int must not alias.
        focused_assembly = require_exact_object(row["testAssembly"], "UI focused test assembly", {"path", "sha256", "sizeBytes"})
        cache_byte_identity(focused_assembly["sha256"], focused_assembly["sizeBytes"], "UI focused test assembly")
        focused_content = require_exact_object(row["coreProjectionContent"], "UI focused Core projection", set(content))
        if (
            row["filter"] != f"FullyQualifiedName~{test_class}"
            or type(row["minimumExpectedTests"]) is not int or row["minimumExpectedTests"] != minimum
            or row["project"] != PRODUCT_TEST_PROJECT or row["reuseFullSuiteBuild"] is not True
            or row["runner"] != "direct-exact-assembly" or row["sdkVersion"] != sdk
            or row["sourceFiles"] != [source_file] or source_file not in source_paths
            or focused_assembly != assembly or focused_content != content
            or type(focused_content["fileCount"]) is not int
        ):
            raise ValueError(f"UI focused owner test {test_class} is not bound to the full suite")

    for key, test_class, source_file, minimum in OWNER_TEST_EXECUTIONS:
        validate_execution(receipt.get(key), test_class, source_file, minimum)
    existing = receipt.get("focusedExistingOwnerRegressionTestExecutions")
    if not isinstance(existing, list) or len(existing) != len(EXISTING_OWNER_TEST_EXECUTIONS):
        raise ValueError("UI existing owner test execution set is not exact")
    for row, (test_class, source_file, minimum) in zip(existing, EXISTING_OWNER_TEST_EXECUTIONS, strict=True):
        validate_execution(row, test_class, source_file, minimum)


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
    if (receipt.get("localCompatibilityTree") is not False
        or receipt.get("packageCacheWasFresh") is not True
        or receipt.get("stubPackagesAllowed") is not False):
        raise ValueError("UI current-graph receipt used a local tree or stale cache")
    lock = receipt.get("consumerPackagePlaneLock")
    if lock != {"path": EXPECTED_LOCK_PATH, "sha256": EXPECTED_LOCK_SHA256, "sizeBytes": EXPECTED_LOCK_SIZE}:
        raise ValueError("UI current-graph receipt package lock drifted")
    # The hosted current-main consumer supplies no owner-package cache. Its
    # cold production receipt must not be replaced with a local copied-cache run.
    # A caller's retained feed is authenticated independently below.
    validate_cold_cache_receipt(receipt.get("ownerPackageArtifactCache"))
    validate_owner_test_executions(receipt)
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
            "status",
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
    } != {
        key: value for key, value in expected_hub.items()
        if key not in {"receiptContract", "receiptSha256"}
    }:
        raise ValueError("UI receipt Hub authority disagrees with the bound UI package lock")
    # Cold hosted production reports package reproduction, not a copied Hub
    # receipt. The latter remains bound by the exact UI lock and the independently
    # authenticated retained cache's hub-receipt.json bytes.
    if (
        receipt_hub.get("packageCount") != len(receipt_hub_rows)
        or receipt_hub.get("projectLockFilesEnforced") is not True
        or receipt_hub.get("status") != "passed"
    ):
        raise ValueError("UI receipt Hub authority status drifted")

    receipt_legacy = require_exact_object(
        receipt.get("currentOwnerContractFeed"),
        "UI receipt legacy owner-contract feed",
        {
            "compatibilityPurpose", "inventoryContract", "inventorySha256",
            "lockContract", "lockSha256", "materializedFeedValidated", "packageCount",
            "packageFeedInventorySha256", "packageVersion", "packages", "producerCommit",
            "producerPath", "producerRepository", "producerSha256",
            "selectedForCanonicalFullFeed", "selectedForCoreRuntimeCompatibility", "status",
        },
    )
    receipt_legacy_rows = sorted(
        receipt_package_rows(receipt_legacy.get("packages"), "UI receipt legacy owner-contract feed"),
        key=lambda row: row["fileName"],
    )
    expected_legacy = package_authority.get("currentOwnerContractFeed")
    if not isinstance(expected_legacy, dict):
        raise ValueError("bound UI package lock legacy owner-contract projection is missing")
    if {
        "inventoryContract": receipt_legacy.get("inventoryContract"),
        "inventorySha256": receipt_legacy.get("inventorySha256"),
        "lockContract": receipt_legacy.get("lockContract"),
        "lockSha256": receipt_legacy.get("lockSha256"),
        "packageFeedInventorySha256": receipt_legacy.get("packageFeedInventorySha256"),
        "packageVersion": receipt_legacy.get("packageVersion"),
        "packages": receipt_legacy_rows,
        "producerCommit": receipt_legacy.get("producerCommit"),
        "producerPath": receipt_legacy.get("producerPath"),
        "producerRepository": receipt_legacy.get("producerRepository"),
        "producerSha256": receipt_legacy.get("producerSha256"),
    } != expected_legacy:
        raise ValueError("UI receipt legacy owner-contract authority disagrees with the bound UI package lock")
    if (
        receipt_legacy.get("compatibilityPurpose") != "exact-core-runtime-transitive-dependencies"
        or receipt_legacy.get("materializedFeedValidated") is not True
        or receipt_legacy.get("packageCount") != len(receipt_legacy_rows)
        or receipt_legacy.get("selectedForCanonicalFullFeed") is not True
        or receipt_legacy.get("selectedForCoreRuntimeCompatibility") is not True
        or receipt_legacy.get("status") != "passed"
    ):
        raise ValueError("UI receipt legacy owner-contract authority status drifted")

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
    if feed.name != "packages" or {entry.name for entry in feed.parent.iterdir()} != {
        "authority", "owner-package-cache.json", "packages",
    }:
        raise ValueError("current package cache root membership is not exact")
    manifest_path = feed.parent / "owner-package-cache.json"
    manifest_inventory, manifest_bytes = cache_file_inventory(
        manifest_path, "current package cache manifest",
        expected_size=EXPECTED_CACHE_MANIFEST_SIZE, capture=True,
    )
    if manifest_inventory["sha256"] != EXPECTED_CACHE_MANIFEST_SHA256:
        raise ValueError("current package cache manifest bytes drifted")
    # Parse exactly the bytes hashed above; a second path read is not authority.
    def reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("current package cache manifest contains duplicate keys")
            result[key] = value
        return result

    cache = require_exact_object(
        json.loads(manifest_bytes, object_pairs_hook=reject_duplicates,
                   parse_constant=lambda _: (_ for _ in ()).throw(ValueError("non-finite cache value"))),
        "current package cache manifest",
        {"authorities", "authorityArtifacts", "cacheKey", "contract", "packages"},
    )
    if cache.get("contract") != CACHE_CONTRACT or cache.get("cacheKey") != EXPECTED_CACHE_KEY:
        raise ValueError("current package cache authority drifted")
    if not isinstance(cache.get("authorities"), dict):
        raise ValueError("current package cache authority projection is malformed")
    authority_artifacts = cache.get("authorityArtifacts")
    if not isinstance(authority_artifacts, list) or len(authority_artifacts) != EXPECTED_CACHE_AUTHORITY_COUNT:
        raise ValueError("current package cache authority artifact inventory is malformed")
    artifact_names: set[str] = set()
    for value_index, row_value in enumerate(authority_artifacts):
        row = require_exact_object(
            row_value,
            f"current package cache authority artifact row {value_index}",
            {"fileName", "sha256"},
        )
        name = cache_file_name(row.get("fileName"), "current package cache authority artifact filename")
        digest = require_string(row.get("sha256"), "current package cache authority artifact sha256")
        if name in artifact_names or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("current package cache authority artifact inventory is not exact")
        artifact_names.add(name)
    authority_root = feed.parent / "authority"
    if (authority_root.is_symlink() or not authority_root.is_dir()
        or authority_root.resolve() != authority_root
        or {path.name for path in authority_root.iterdir()} != artifact_names):
        raise ValueError("current package cache authority files are not exact")
    for row in authority_artifacts:
        inventory, _ = cache_file_inventory(authority_root / row["fileName"], "current cache authority file")
        if inventory["sha256"] != row["sha256"]:
            raise ValueError("current package cache authority bytes drifted")
    rows = cache.get("packages")
    if not isinstance(rows, list) or len(rows) != EXPECTED_PACKAGE_COUNT:
        raise ValueError("current package cache must bind exactly eighteen packages")
    expected: dict[str, tuple[str, int]] = {}
    for value_index, row_value in enumerate(rows):
        row = require_exact_object(
            row_value,
            f"current package cache row {value_index}",
            {"commit", "fileName", "packageId", "plane", "repository", "sha256", "sizeBytes", "version"},
        )
        name = cache_file_name(row.get("fileName"), "current package cache filename")
        digest = row.get("sha256")
        size = row.get("sizeBytes")
        cache_byte_identity(digest, size, "current package cache row")
        if name in expected:
            raise ValueError("current package cache contains duplicate filenames")
        expected[name] = (digest, size)
    actual = {path.name: path for path in feed.iterdir()}
    if set(actual) != set(expected):
        raise ValueError("current package feed does not match the exact eighteen-package cache")
    for name, path in actual.items():
        digest, size = expected[name]
        inventory, _ = cache_file_inventory(path, "current package file", expected_size=size)
        if inventory["sha256"] != digest:
            raise ValueError(f"current package bytes drifted: {name}")
    return cache


def validate_receipt_cache_equivalence(
    receipt: Mapping[str, Any],
    cache: Mapping[str, Any],
    *,
    package_feed: Path,
) -> None:
    # This proves byte equivalence, not that the hosted producer used this cache.
    validate_cold_cache_receipt(receipt.get("ownerPackageArtifactCache"))
    # Reauthenticate the independently pinned manifest and all 18/13 files.
    if validate_package_feed(package_feed) != cache:
        raise ValueError("retained package cache differs from authenticated files")
    cache_rows = cache.get("packages")
    if not isinstance(cache_rows, list) or len(cache_rows) != EXPECTED_PACKAGE_COUNT:
        raise ValueError("retained package cache inventory is unavailable")
    cache_bytes = sorted(
        [
            {
                "fileName": row["fileName"],
                "sha256": row["sha256"],
                "sizeBytes": row["sizeBytes"],
            }
            for row in cache_rows
            if isinstance(row, dict)
        ],
        key=lambda row: row["fileName"],
    )
    if len(cache_bytes) != EXPECTED_PACKAGE_COUNT:
        raise ValueError("retained package cache rows are malformed")

    receipt_owner_rows: list[dict[str, Any]] = []
    for field, label in (
        ("coreRuntimeFeed", "UI receipt Core runtime feed"),
        ("canonicalOwnerFeed", "UI receipt canonical Hub feed"),
        ("currentOwnerContractFeed", "UI receipt legacy owner-contract feed"),
        ("uiOwnerFeed", "UI receipt owner feed"),
    ):
        feed = receipt.get(field)
        if not isinstance(feed, dict):
            raise ValueError(f"{label} is missing")
        receipt_owner_rows.extend(receipt_package_rows(feed.get("packages"), label))
    receipt_owner_rows.sort(key=lambda row: row["fileName"])
    owner_names = [row["fileName"] for row in receipt_owner_rows]
    if len(owner_names) != len(set(owner_names)) or receipt_owner_rows != cache_bytes:
        raise ValueError("UI receipt owner feeds diverge from the retained package cache")

    receipt_inventory = receipt_package_rows(
        receipt.get("packageInventory"),
        "UI receipt package inventory",
    )
    inventory_by_name = {row["fileName"]: row for row in receipt_inventory}
    if len(inventory_by_name) != len(receipt_inventory):
        raise ValueError("UI receipt package inventory contains duplicate filenames")
    if any(inventory_by_name.get(row["fileName"]) != row for row in cache_bytes):
        raise ValueError("UI receipt package inventory diverges from the retained package cache")

    # The cold/non-use receipt has no copied-file inventory. Authority bytes
    # remain bound by the exact separately pinned cache manifest, not by a
    # synthesized claim that the hosted producer copied these files.
    for row in cache["authorityArtifacts"]:
        inventory, _ = cache_file_inventory(
            package_feed.parent / "authority" / row["fileName"], "retained authority file",
        )
        if inventory["sha256"] != row["sha256"]:
            raise ValueError("retained authority file changed after cache validation")


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
        validate_runtime_hub_source_checkout(
            args.android_root / RUNTIME_SOURCE_WORKFLOW_PATH
        )
        cache = validate_package_feed(args.package_feed)
        validate_receipt_cache_equivalence(receipt, cache, package_feed=args.package_feed)
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
