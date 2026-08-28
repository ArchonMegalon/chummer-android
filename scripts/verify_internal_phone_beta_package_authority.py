#!/usr/bin/env python3
"""Authenticate the package-only Presentation plane for the internal phone beta."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import stat
import subprocess
from pathlib import Path, PurePosixPath
from typing import Any, Mapping


CONTRACT = "chummer.android.internal-phone-beta-package-authority/v1"
RECEIPT_CONTRACT = "chummer6-ui.android-presentation-package-verification/v1"
AUTHORITY_CONTRACT = "chummer6-ui.android-presentation-package-authority/v1"
EXPECTED_PRESENTATION_COMMIT = "a8a317aff534dc5fd47f2db1bc39466799021990"
EXPECTED_PRESENTATION_TREE = "f8214243280030de5d134351f39ea4b23afbe394"
EXPECTED_PRESENTATION_REPOSITORY = "https://github.com/ArchonMegalon/chummer6-ui.git"
EXPECTED_PRODUCTION_COMMIT = "3a5ca054e1ce126a02dec4199dc92233dfee8804"
EXPECTED_PRODUCTION_TREE = "25def23deef40822e3ff89549cc509e01c149ed4"
EXPECTED_AUTHORITY_PATH = "config/android-presentation-package-authority.json"
EXPECTED_AUTHORITY_SHA256 = "4dfc8bff234ced999792797b7ac4e5f5dd5d371c1c261bc56b2f6adcfa382c4b"
EXPECTED_AUTHORITY_SIZE = 8207
EXPECTED_AUTHORITY_BLOB = "2a4acfe802a51b275793338fa660b5f69275751b"
EXPECTED_RECEIPT_SHA256 = "aaf2c755ef7233f2b21bc257e306ea5de60ac42125c3c8b47501aa05a3b949dd"
EXPECTED_RECEIPT_SIZE = 129553
EXPECTED_JOURNAL_SHA256 = "4b41a6c2afded5acf83b07a59ac73605faf5037948a0dd0fbed772440ff54bec"
EXPECTED_JOURNAL_SIZE = 38137

CORE_VERSION = "0.1.0-packageplane.breaking.shb04ff26f6d538.auth91a48eed5b819"
HUB_VERSION = "0.1.0-packageplane.android.sh1215f9389779e"
REGISTRY_VERSION = "0.1.0-packageplane.candidate.sh66c418a5004f"
UI_KIT_VERSION = "0.1.0-packageplane.android.shd51ecd99cf720"

EXPECTED_PACKAGES = (
    ("Chummer.Engine.Contracts", CORE_VERSION, "3fb0adcf0b5dfecd8be2493a02da91d3e13ac3e91df1f6ef69ad7351aefff21a", 1200370, "core"),
    ("Chummer.Application", CORE_VERSION, "b1d239637100efefaaa36d87cb4a2029a3e91ec26333f8d3be7036cd868f92dd", 447924, "core"),
    ("Chummer.Rulesets.Hosting", CORE_VERSION, "f707d184da187a0a1f439edb1e7d1fd90d48283d22c67c796ad68a933ad91712", 14404, "core"),
    ("Chummer.Rulesets.Sr5", CORE_VERSION, "d7a5e9d573b787fd5f2097d858015b1d660eeeaa953b7d18137f1287fb8c88db", 31672, "core"),
    ("Chummer.Rulesets.Sr6", CORE_VERSION, "f775f9370b24731341dac8f9371c6bfae7ce2dab46c9ded876bfd971689e65dd", 40943, "core"),
    ("Chummer.Infrastructure", CORE_VERSION, "24338168a2baa5fa057c5d7841227c7e9bf1a65b0b106c156890e8ad5f5cb696", 252466, "core"),
    ("Chummer.Rulesets.Sr4", CORE_VERSION, "8d74e784f0683766d660c6a275d248315f5e9443054f428541d6c9cf3c1de8ba", 33912, "core"),
    ("Chummer.Engine.GmCharacterEdits", CORE_VERSION, "d592cdf8c22898219cd954809269562b379eb7b836d69f94b5400017ffc1387c", 783466, "core"),
    ("Chummer.Play.Contracts", HUB_VERSION, "9bb54360f1d93dfbc897ad0a73c6cddd81d5167d0a808f6cec7939164624c43f", 322542, "hub"),
    ("Chummer.Campaign.Contracts", HUB_VERSION, "46d9ed26b3d1dcefc544544ac797123cff3b789dbc7e1d10e751e7a6f03be0b2", 451361, "hub"),
    ("Chummer.Run.Contracts", HUB_VERSION, "df750cd521ab8cbd41d479aa3dc94173dfdb2196290619fb6452ef8ae1a9cc98", 1819542, "hub"),
    ("Chummer.Run.Hub.Contracts", HUB_VERSION, "cf8efc62d08619433a7d08e0b94ff40a808834f57ac93676d171bfd42f937697", 23493, "hub"),
    ("Chummer.Run.Hub", HUB_VERSION, "cdca62ef686b83481f05aab7898d44f0b26577192be82739710964bb201cdee8", 140964, "hub"),
    ("Chummer.Hub.Registry.Contracts", REGISTRY_VERSION, "2916c9cbfd8da0bc4a13d6a26746ff30ada5e88a593a3e5039d632d58593935d", 524842, "registry"),
    ("Chummer.Ui.Kit", UI_KIT_VERSION, "1cfeb8adb6a0ee9a3e416d9fa6454304870bc30689446afa996fdbd2b5373bf2", 122029, "uiKit"),
)

EXPECTED_CORE_IDS = (
    "Chummer.Application", "Chummer.Infrastructure", "Chummer.Rulesets.Hosting",
    "Chummer.Rulesets.Sr4", "Chummer.Rulesets.Sr5", "Chummer.Rulesets.Sr6",
)
EXPECTED_OWNER_IDS = (
    "Chummer.Campaign.Contracts", "Chummer.Play.Contracts", "Chummer.Run.Contracts",
    "Chummer.Run.Hub.Contracts", "Chummer.Run.Hub",
    "Chummer.Hub.Registry.Contracts", "Chummer.Ui.Kit",
)
EXPECTED_LOCKS = (
    ("Chummer.Presentation/packages.lock.json", "568fd2c602494329d19fbe8d9a2c83a4c2e82754b50e31141b192c1af7ccf964"),
    ("Chummer.Desktop.Runtime/packages.lock.json", "202a29a35b4768c3306349ee40a34d8f23ada97c0b0ef11e104763b5ff9cc60e"),
    ("Chummer.Product.UnitTests/Chummer.Presentation.AndroidActivation.Tests.packages.lock.json", "c7dc75976db581dfed25adfe9a3057cb7c6845d18138c8f4323f4fd4165b5623"),
    ("Chummer.Tests/Presentation/Chummer.Presentation.Sr5CareerWizard.Tests.packages.lock.json", "c79b3b1827c290ed312254f35e116907c16730b2f69b0bb5dfa6417540bc7f86"),
    ("Chummer.Tests/Presentation/Chummer.Presentation.Sr5TableWizard.Tests.packages.lock.json", "27d88df61af8995e2622a33b31cc198acd82d52ddb8c2641d240c9249a30cb51"),
)
EXPECTED_ASSETS = (
    ("Chummer.Presentation/Chummer.Presentation.csproj", "d06ebbeeceec719abfde402c5b483dbd6187dd1d826b95d7bc285a326a6be429"),
    ("Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj", "5bb124c8f9bc4aecc2c05a040e85322ceffaf7f65d3765c2040794839ac39fb7"),
    ("Chummer.Product.UnitTests/Chummer.Presentation.AndroidActivation.Tests.csproj", "d98a5dca62aa99803229eb55bf66914ed1f19f3202bc582500b7e7c2dc69244a"),
    ("Chummer.Tests/Presentation/Chummer.Presentation.Sr5CareerWizard.Tests.csproj", "3d6dd80606ec3ceddce2af29605ed6455867ec7b5ba18684bd8216a163a58df9"),
    ("Chummer.Tests/Presentation/Chummer.Presentation.Sr5TableWizard.Tests.csproj", "5e24342a89b1b567c8567aac79f4373e51be1e138333e3f9e42a51e7b6a51a9b"),
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


def git(root: Path, *arguments: str) -> str:
    return subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=True, capture_output=True, text=True,
    ).stdout.strip()


def git_bytes(root: Path, *arguments: str) -> bytes:
    return subprocess.run(
        ["git", "-C", os.fspath(root), *arguments],
        check=True, capture_output=True,
    ).stdout


def require_private_regular_file(path: Path, label: str) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_file():
        raise ValueError(f"{label} must be an absolute non-symlinked regular file")
    resolved = path.resolve(strict=True)
    if resolved != path:
        raise ValueError(f"{label} must use its canonical path")
    mode = stat.S_IMODE(path.stat().st_mode)
    if mode & 0o077:
        raise ValueError(f"{label} must be owner-only")
    if path.stat().st_uid != os.getuid():
        raise ValueError(f"{label} must be owned by the current user")
    return resolved


def _manifest_package_rows(manifest: Mapping[str, Any]) -> dict[str, dict[str, Any]]:
    core = manifest.get("packagePins")
    owners = manifest.get("ownerPackagePins")
    if not isinstance(core, list) or [row.get("package_id") for row in core if isinstance(row, dict)] != list(EXPECTED_CORE_IDS):
        raise ValueError("internal authority must preserve the exact ordered six Core runtime package pins")
    if not isinstance(owners, list) or [row.get("package_id") for row in owners if isinstance(row, dict)] != list(EXPECTED_OWNER_IDS):
        raise ValueError("internal authority must preserve the exact ordered seven owner package pins")
    if any(not isinstance(row, dict) for row in [*core, *owners]):
        raise ValueError("internal package pins must be JSON objects")
    return {str(row["package_id"]): row for row in [*core, *owners]}


def validate_manifest(path: Path) -> dict[str, Any]:
    manifest = strict_json(path, "internal phone-beta authority")
    expected_keys = {
        "contractName", "authorityClass", "authorityState", "publicationAuthorized",
        "presentationSource", "authority", "verificationReceipt", "dependencyMode",
        "sdkAuthority", "headlessRuntimeBinding", "packagePins", "ownerPackagePins", "lockFiles",
        "androidConsumerLock",
        "doesNotAssert",
    }
    if set(manifest) != expected_keys or manifest.get("contractName") != CONTRACT:
        raise ValueError("internal phone-beta authority schema is not exact")
    if manifest.get("authorityClass") != "internal_phone_beta_only" or manifest.get("authorityState") != "independently_audited":
        raise ValueError("W4.1 authority is not scoped to the independently audited internal phone beta")
    if manifest.get("publicationAuthorized") is not False:
        raise ValueError("internal phone-beta authority cannot authorize publication")
    source = manifest.get("presentationSource")
    if source != {
        "productionCommit": EXPECTED_PRODUCTION_COMMIT,
        "productionTree": EXPECTED_PRODUCTION_TREE,
        "packageAuthorityCommit": EXPECTED_PRESENTATION_COMMIT,
        "packageAuthorityTree": EXPECTED_PRESENTATION_TREE,
        "repository": EXPECTED_PRESENTATION_REPOSITORY,
    }:
        raise ValueError("Presentation production source and internal package authority pins are not exact")
    if manifest.get("authority") != {
        "path": EXPECTED_AUTHORITY_PATH,
        "sha256": EXPECTED_AUTHORITY_SHA256,
        "sizeBytes": EXPECTED_AUTHORITY_SIZE,
        "gitBlob": EXPECTED_AUTHORITY_BLOB,
    }:
        raise ValueError("W4.1 authority file binding is not exact")
    if manifest.get("verificationReceipt") != {
        "sha256": EXPECTED_RECEIPT_SHA256,
        "sizeBytes": EXPECTED_RECEIPT_SIZE,
        "journalSha256": EXPECTED_JOURNAL_SHA256,
        "journalSizeBytes": EXPECTED_JOURNAL_SIZE,
    }:
        raise ValueError("W4.1 receipt binding is not exact")
    if manifest.get("dependencyMode") != {
        "packageOnly": True, "restoreLockedMode": True,
        "sourceCheckoutsPresent": False, "siblingsAllowed": False,
    }:
        raise ValueError("internal phone-beta dependency mode is not package-only, locked, and sibling-free")
    if manifest.get("sdkAuthority") != {
        "packageProofSdkVersion": "10.0.103",
        "androidGlobalPolicy": {
            "path": "global.json",
            "sha256": "a97905ba6c0bbdfec34e2bbf53173d2777a1ea533e2e82aa99e98406395223e3",
            "version": "10.0.110",
            "rollForward": "latestPatch",
            "allowPrerelease": False,
        },
        "releaseWorkflow": {
            "path": ".github/workflows/preview9-arm64-aab.yml",
            "sha256": "173a710ba2a123180b802e003e681b99b7a3681b973b6fe9a5f54fadb06cce3a",
            "dotnetVersion": "10.0.111",
        },
        "selectedAndroidConsumerSdkVersion": "10.0.111",
    }:
        raise ValueError("internal phone-beta producer and Android consumer SDK authority is not exact")
    if manifest.get("headlessRuntimeBinding") != {
        "project": "Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
        "androidEntryPoint": "AddChummerLocalRuntimeClient",
        "role": "android-headless-runtime-dependency",
        "includesAvaloniaUi": False, "includesBlazorUi": False,
        "desktopReleaseGate": False,
    }:
        raise ValueError("internal phone-beta headless runtime binding is not exact")
    rows = _manifest_package_rows(manifest)
    by_id = {row[0]: row for row in EXPECTED_PACKAGES}
    for package_id in (*EXPECTED_CORE_IDS, *EXPECTED_OWNER_IDS):
        expected = by_id[package_id]
        row = rows[package_id]
        expected_fields = {"package_id", "version", "sha256", "size_bytes"}
        expected_owner = package_id in EXPECTED_OWNER_IDS
        if expected_owner:
            expected_fields.add("owner")
        if set(row) != expected_fields:
            raise ValueError(f"internal package pin fields are not exact: {package_id}")
        if row.get("version") != expected[1] or row.get("sha256") != expected[2] or row.get("size_bytes") != expected[3]:
            raise ValueError(f"internal package pin bytes are not exact: {package_id}")
        if expected_owner and row.get("owner") != expected[4]:
            raise ValueError(f"internal package pin owner is not exact: {package_id}")
    locks = manifest.get("lockFiles")
    if not isinstance(locks, list) or [(row.get("path"), row.get("sha256")) for row in locks if isinstance(row, dict)] != list(EXPECTED_LOCKS):
        raise ValueError("internal authority must bind the exact five W4.1 locks")
    if manifest.get("androidConsumerLock") != {
        "project": "tests/Chummer.Android.Native.CompileCheck/Chummer.Android.Native.CompileCheck.csproj",
        "path": "tests/Chummer.Android.Native.CompileCheck/packages.lock.json",
        "sha256": "64454d5420e2a5430a046d392c6eea2ca41d9105c1667f2b8a66e1f61064cccc",
        "sizeBytes": 17968,
    }:
        raise ValueError("Android internal phone-beta consumer lock binding is not exact")
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
        raise ValueError("Presentation W4.1 repository is dirty")
    if git(root, "rev-parse", "HEAD") != EXPECTED_PRESENTATION_COMMIT:
        raise ValueError("Presentation W4.1 commit drifted")
    if git(root, "rev-parse", "HEAD^{tree}") != EXPECTED_PRESENTATION_TREE:
        raise ValueError("Presentation W4.1 tree drifted")
    if git(root, "remote", "get-url", "origin") != EXPECTED_PRESENTATION_REPOSITORY:
        raise ValueError("Presentation W4.1 repository authority drifted")
    authority = root / EXPECTED_AUTHORITY_PATH
    if authority.is_symlink() or not authority.is_file() or authority.stat().st_size != EXPECTED_AUTHORITY_SIZE or sha256(authority) != EXPECTED_AUTHORITY_SHA256:
        raise ValueError("Presentation W4.1 authority file bytes drifted")
    if git(root, "rev-parse", f"HEAD:{EXPECTED_AUTHORITY_PATH}") != EXPECTED_AUTHORITY_BLOB:
        raise ValueError("Presentation W4.1 authority Git blob drifted")
    for relative, digest in EXPECTED_LOCKS:
        lock = root / relative
        if lock.is_symlink() or not lock.is_file() or sha256(lock) != digest:
            raise ValueError(f"Presentation W4.1 lock bytes drifted: {relative}")


def validate_android_sdk_authority(android_root: Path, manifest: Mapping[str, Any]) -> None:
    if not android_root.is_absolute() or android_root.is_symlink() or not android_root.is_dir() or android_root.resolve() != android_root:
        raise ValueError("Android root must be one canonical non-symlinked directory")
    sdk = manifest["sdkAuthority"]
    policy = sdk["androidGlobalPolicy"]
    global_json = android_root / policy["path"]
    if global_json.is_symlink() or not global_json.is_file() or sha256(global_json) != policy["sha256"]:
        raise ValueError("Android global SDK policy bytes drifted")
    if strict_json(global_json, "Android global SDK policy") != {
        "sdk": {
            "version": policy["version"],
            "rollForward": policy["rollForward"],
            "allowPrerelease": policy["allowPrerelease"],
        }
    }:
        raise ValueError("Android global SDK policy semantics drifted")
    workflow = sdk["releaseWorkflow"]
    workflow_path = android_root / workflow["path"]
    if workflow_path.is_symlink() or not workflow_path.is_file() or sha256(workflow_path) != workflow["sha256"]:
        raise ValueError("Android release workflow SDK authority bytes drifted")
    if workflow_path.read_text(encoding="utf-8").count(
        f'dotnet-version: {workflow["dotnetVersion"]}'
    ) != 1:
        raise ValueError("Android release workflow SDK selection drifted")
    if sdk["packageProofSdkVersion"] != "10.0.103" or sdk["selectedAndroidConsumerSdkVersion"] != workflow["dotnetVersion"]:
        raise ValueError("W4.1 producer SDK and Android consumer SDK were not kept as separate exact authorities")
    consumer_lock = manifest["androidConsumerLock"]
    lock_path = android_root / consumer_lock["path"]
    if lock_path.is_symlink() or not lock_path.is_file() or lock_path.stat().st_size != consumer_lock["sizeBytes"] or sha256(lock_path) != consumer_lock["sha256"]:
        raise ValueError("Android internal phone-beta consumer lock bytes drifted")


def validate_package_feed(feed: Path) -> None:
    if not feed.is_absolute() or feed.is_symlink() or not feed.is_dir() or feed.resolve() != feed:
        raise ValueError("internal phone-beta package feed must be one canonical non-symlinked directory")
    expected = {
        f"{package_id}.{version}.nupkg": (digest, size)
        for package_id, version, digest, size, _ in EXPECTED_PACKAGES
    }
    actual = {path.name: path for path in feed.iterdir() if path.is_file()}
    if set(actual) != set(expected):
        raise ValueError("internal phone-beta package feed must contain exactly the fifteen W4.1 NUPKGs")
    for name, path in actual.items():
        if path.is_symlink() or path.stat().st_size != expected[name][1] or sha256(path) != expected[name][0]:
            raise ValueError(f"internal phone-beta package bytes drifted: {name}")


def _expected_receipt_packages() -> list[dict[str, Any]]:
    return [
        {
            "id": package_id,
            "version": version,
            "fileName": f"{package_id}.{version}.nupkg",
            "sha256": digest,
            "sizeBytes": size,
            "owner": owner,
        }
        for package_id, version, digest, size, owner in EXPECTED_PACKAGES
    ]


def _validate_commands(commands: object) -> None:
    if not isinstance(commands, list) or len(commands) != 13:
        raise ValueError("W4.1 receipt must bind exactly thirteen bounded commands")
    restore_count = build_count = executable_count = 0
    for row in commands:
        if not isinstance(row, dict) or set(row) != {"command", "exitCode", "outputSha256", "outputTail"} or row.get("exitCode") != 0:
            raise ValueError("W4.1 receipt contains a failed or malformed command")
        command = row.get("command")
        if not isinstance(command, list) or not command or any(not isinstance(item, str) or not item for item in command):
            raise ValueError("W4.1 receipt command vector is malformed")
        if "restore" in command:
            restore_count += 1
            required = {
                "--locked-mode", "--ignore-failed-sources",
                "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
                "-p:ChummerUseLocalCompatibilityTree=false",
                "-p:ChummerUseLockedOwnerContractPackages=true",
            }
            if not required.issubset(command):
                raise ValueError("W4.1 restore command was not exact locked package-only mode")
        elif "build" in command:
            build_count += 1
            if "--no-restore" not in command or "-m:1" not in command:
                raise ValueError("W4.1 build command was not serialized and no-restore")
        else:
            executable_count += 1
    if (restore_count, build_count, executable_count) != (5, 5, 3):
        raise ValueError("W4.1 command phase counts are not exact")


def _validate_consumer_sources(rows: object, root: Path) -> None:
    if not isinstance(rows, list) or len(rows) != 279:
        raise ValueError("W4.1 receipt must bind exactly 279 consumer sources")
    seen: set[str] = set()
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"path", "gitMode", "gitBlob", "sha256", "sizeBytes"}:
            raise ValueError("W4.1 consumer source row is malformed")
        relative = row.get("path")
        if not isinstance(relative, str) or relative in seen:
            raise ValueError("W4.1 consumer sources are duplicated or noncanonical")
        seen.add(relative)
        posix = PurePosixPath(relative)
        if posix.is_absolute() or ".." in posix.parts or "\\" in relative:
            raise ValueError("W4.1 consumer source path escapes Presentation")
        path = root.joinpath(*posix.parts)
        if path.is_symlink() or not path.is_file():
            raise ValueError(f"W4.1 consumer source is unavailable: {relative}")
        listing = git(root, "ls-tree", "HEAD", "--", relative).split()
        if len(listing) < 3 or listing[0] != row.get("gitMode") or listing[2] != row.get("gitBlob"):
            raise ValueError(f"W4.1 consumer source Git binding drifted: {relative}")
        blob = git_bytes(root, "cat-file", "blob", str(row.get("gitBlob")))
        if len(blob) != row.get("sizeBytes") or hashlib.sha256(blob).hexdigest() != row.get("sha256"):
            raise ValueError(f"W4.1 consumer source blob bytes drifted: {relative}")


def validate_receipt(receipt_path: Path, journal_path: Path, presentation_root: Path) -> dict[str, Any]:
    receipt_path = require_private_regular_file(receipt_path, "W4.1 receipt")
    journal_path = require_private_regular_file(journal_path, "W4.1 journal")
    if receipt_path.stat().st_size != EXPECTED_RECEIPT_SIZE or sha256(receipt_path) != EXPECTED_RECEIPT_SHA256:
        raise ValueError("W4.1 receipt bytes are not exact")
    if journal_path.stat().st_size != EXPECTED_JOURNAL_SIZE or sha256(journal_path) != EXPECTED_JOURNAL_SHA256:
        raise ValueError("W4.1 journal bytes are not exact")
    strict_json(journal_path, "W4.1 journal")
    receipt = strict_json(receipt_path, "W4.1 receipt")
    expected_keys = {
        "contractName", "status", "generatedAtUtc", "presentationCommit",
        "presentationTree", "presentationRepositoryClean", "authoritySha256",
        "authorityGitBlob", "journalSha256", "publicationAuthorized", "sdkVersion",
        "executionBounds", "headlessRuntimeBinding", "externalCacheSeedPackageCount",
        "sourceCheckoutsPresent", "restoreLockedMode", "generatedTestState",
        "packages", "lockFiles", "assets", "commands", "consumerSources",
        "scratchFreeBytesAtStart",
    }
    if set(receipt) != expected_keys or receipt.get("contractName") != RECEIPT_CONTRACT or receipt.get("status") != "pass":
        raise ValueError("W4.1 receipt schema or status is not exact")
    exact_fields = {
        "presentationCommit": EXPECTED_PRESENTATION_COMMIT,
        "presentationTree": EXPECTED_PRESENTATION_TREE,
        "presentationRepositoryClean": True,
        "authoritySha256": EXPECTED_AUTHORITY_SHA256,
        "authorityGitBlob": EXPECTED_AUTHORITY_BLOB,
        "journalSha256": EXPECTED_JOURNAL_SHA256,
        "publicationAuthorized": False,
        "sdkVersion": "10.0.103",
        "sourceCheckoutsPresent": False,
        "restoreLockedMode": True,
        "externalCacheSeedPackageCount": 243,
    }
    for field, expected in exact_fields.items():
        if receipt.get(field) != expected:
            raise ValueError(f"W4.1 receipt field drifted: {field}")
    if receipt.get("executionBounds") != {
        "perCommandSeconds": 900.0,
        "processGroupTermination": True,
        "totalSeconds": 3600.0,
    }:
        raise ValueError("W4.1 verifier execution bounds are not exact")
    if receipt.get("headlessRuntimeBinding") != {
        "project": "Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj",
        "androidEntryPoint": "AddChummerLocalRuntimeClient",
        "role": "android-headless-runtime-dependency",
        "includesAvaloniaUi": False, "includesBlazorUi": False,
        "desktopReleaseGate": False,
    }:
        raise ValueError("W4.1 headless runtime binding drifted")
    if receipt.get("packages") != _expected_receipt_packages():
        raise ValueError("W4.1 receipt package table is not the exact ordered fifteen-row table")
    locks = receipt.get("lockFiles")
    if not isinstance(locks, list) or [(row.get("path"), row.get("sha256")) for row in locks if isinstance(row, dict)] != list(EXPECTED_LOCKS):
        raise ValueError("W4.1 receipt lock table is not exact")
    assets = receipt.get("assets")
    if not isinstance(assets, list) or [(row.get("project"), row.get("assetsSha256")) for row in assets if isinstance(row, dict)] != list(EXPECTED_ASSETS):
        raise ValueError("W4.1 receipt assets table is not exact")
    _validate_commands(receipt.get("commands"))
    _validate_consumer_sources(receipt.get("consumerSources"), presentation_root)
    return receipt


def build_binding(manifest: Mapping[str, Any]) -> dict[str, Any]:
    return {
        "contractName": CONTRACT,
        "authorityClass": "internal_phone_beta_only",
        "authorityState": "independently_audited",
        "publicationAuthorized": False,
        "presentationSource": manifest["presentationSource"],
        "authority": manifest["authority"],
        "verificationReceipt": manifest["verificationReceipt"],
        "dependencyMode": manifest["dependencyMode"],
        "sdkAuthority": manifest["sdkAuthority"],
        "headlessRuntimeBinding": manifest["headlessRuntimeBinding"],
        "packagePins": manifest["packagePins"],
        "ownerPackagePins": manifest["ownerPackagePins"],
        "lockFiles": manifest["lockFiles"],
        "androidConsumerLock": manifest["androidConsumerLock"],
        "doesNotAssert": manifest["doesNotAssert"],
    }


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
    parser.add_argument("--journal", type=Path, required=True)
    parser.add_argument("--package-feed", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        manifest = validate_manifest(args.manifest)
        validate_android_sdk_authority(args.android_root, manifest)
        presentation_root = args.presentation_root
        validate_presentation_repository(presentation_root)
        validate_receipt(args.receipt, args.journal, presentation_root)
        if args.package_feed is not None:
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
            "packagePinCount": len(EXPECTED_CORE_IDS),
            "ownerPackagePinCount": len(EXPECTED_OWNER_IDS),
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
