#!/usr/bin/env python3
"""Materialize or verify a next-release Play Internal publication receipt.

This offline contract combines explicit public browser readback with the exact
local AAB, the protected build-sidecar digest of the v3 release source graph,
and a detached-approved two-green receipt.  It never opens Play, accepts
credentials, or grants production/upload/tester-roster authority.
"""

from __future__ import annotations

import argparse
from datetime import UTC, datetime, timedelta
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import stat
from typing import Any
from urllib.parse import urlsplit
import zipfile


CONTRACT = "chummer.android.play-internal-publication-receipt/v4"
VERIFICATION_CONTRACT = "chummer.android.play-internal-publication-verification/v3"
BROWSER_READBACK_CONTRACT = "chummer.android.play-internal-browser-readback/v1"
SOURCE_GRAPH_CONTRACT = "chummer.android.release-source-graph/v3"
PACKAGE_ID = "com.myexternalbrain.chummer"
PLAY_APPLICATION_ID = "4975957268242186974"
TRACK_ID = "4700678198570024687"
JOIN_URL = f"https://play.google.com/apps/internaltest/{TRACK_ID}"
HISTORICAL_VERSION_CODE_FLOOR = 10
EXPECTED_OBSERVED_FIELDS = ("application", "track", "release", "join_url")
EXPECTED_SOURCE_REPOSITORIES = {
    "chummer-android": ("app", "https://github.com/ArchonMegalon/chummer-android.git"),
    "chummer6-ui": ("runtime", "https://github.com/ArchonMegalon/chummer6-ui.git"),
    "chummer6-core": ("runtime", "https://github.com/ArchonMegalon/chummer6-core.git"),
    "chummer6-ui-kit": ("runtime", "https://github.com/ArchonMegalon/chummer6-ui-kit.git"),
    "chummer6-hub": (
        "contracts_and_validation",
        "https://github.com/ArchonMegalon/chummer6-hub.git",
    ),
    "chummer6-hub-registry": (
        "contracts",
        "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
    ),
    "chummer6-media-factory": (
        "contracts",
        "https://github.com/ArchonMegalon/chummer6-media-factory.git",
    ),
    "chummer6-design": (
        "validation",
        "https://github.com/ArchonMegalon/chummer6-design.git",
    ),
}
EXPECTED_RUNTIME_PACKAGES = (
    "Chummer.Application",
    "Chummer.Engine.Contracts",
    "Chummer.Infrastructure",
    "Chummer.Rulesets.Hosting",
    "Chummer.Rulesets.Sr4",
    "Chummer.Rulesets.Sr5",
    "Chummer.Rulesets.Sr6",
)
EXPECTED_OWNER_PACKAGES = (
    "Chummer.Campaign.Contracts",
    "Chummer.Play.Contracts",
    "Chummer.Run.Contracts",
    "Chummer.Hub.Registry.Contracts",
    "Chummer.Ui.Kit",
)
OWNER_REPOSITORY_BY_PACKAGE = {
    "Chummer.Campaign.Contracts": "chummer6-hub",
    "Chummer.Play.Contracts": "chummer6-hub",
    "Chummer.Run.Contracts": "chummer6-hub",
    "Chummer.Hub.Registry.Contracts": "chummer6-hub-registry",
    "Chummer.Ui.Kit": "chummer6-ui-kit",
}
SOURCE_GRAPH_NONCLAIMS = (
    "google_play_upload",
    "google_play_processing",
    "tester_installation",
    "production_rollout",
    "presentation_package_authority",
)
RECEIPT_NONCLAIMS = (
    "production_rollout",
    "public_release",
    "upload_action_authorization",
    "tester_roster_mutation_authority",
    "tester_installation",
    "play_artifact_digest_readback",
    "play_artifact_byte_retrievability",
    "source_graph_retrieval_from_play",
    "exact_local_aab_published_to_play",
    "successful_internal_test_install",
)
MAX_BROWSER_READBACK_AGE = timedelta(hours=24)
SHA40 = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
VERSION_NAME = re.compile(
    r"[0-9]+(?:\.[0-9]+){2}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"
)
PACKAGE_VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?")
CONSOLE_DISPLAY = re.compile(r"[A-Za-z0-9][A-Za-z0-9 .,:()+-]{0,127}")
MAX_BROWSER_BYTES = 64 * 1024
MAX_RECEIPT_BYTES = 256 * 1024
MAX_GRAPH_BYTES = 4 * 1024 * 1024
MAX_AAB_BYTES = 512 * 1024 * 1024
REPO_ROOT = Path(__file__).resolve().parents[1]


def _load_qualification_module() -> Any:
    path = REPO_ROOT / "scripts/verify_api36_two_green_release_eligibility.py"
    specification = importlib.util.spec_from_file_location(
        "next_publication_two_green_release_eligibility", path
    )
    if specification is None or specification.loader is None:
        raise ValueError("cannot load the two-green release eligibility verifier")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


QUALIFICATION = _load_qualification_module()


def canonical_json_bytes(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def strict_json(raw: bytes, *, label: str) -> dict[str, Any]:
    def reject_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{label} contains duplicate key {key!r}")
            result[key] = value
        return result

    try:
        value = json.loads(
            raw.decode("utf-8"),
            object_pairs_hook=reject_duplicates,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite number {token}")
            ),
        )
    except (UnicodeDecodeError, json.JSONDecodeError, RecursionError) as error:
        raise ValueError(f"{label} is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one object")
    return value


def read_regular(
    path: Path,
    *,
    label: str,
    limit: int,
    owner_only: bool,
) -> bytes:
    if not path.is_absolute() or path.is_symlink():
        raise ValueError(f"{label} must be an absolute canonical non-symlink path")
    try:
        if path.resolve(strict=True) != path:
            raise ValueError(f"{label} must be an absolute canonical non-symlink path")
        descriptor = os.open(
            path,
            os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0),
        )
    except OSError as error:
        raise ValueError(f"{label} is unavailable") from error
    try:
        before = os.fstat(descriptor)
        if (
            not stat.S_ISREG(before.st_mode)
            or before.st_size <= 0
            or before.st_size > limit
            or before.st_uid != os.getuid()
            or (owner_only and stat.S_IMODE(before.st_mode) & 0o077)
        ):
            raise ValueError(f"{label} is not an exact bounded owner file")
        chunks: list[bytes] = []
        total = 0
        while total <= limit:
            chunk = os.read(descriptor, min(1024 * 1024, limit + 1 - total))
            if not chunk:
                break
            chunks.append(chunk)
            total += len(chunk)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    identity = lambda row: (
        row.st_dev,
        row.st_ino,
        row.st_mode,
        row.st_uid,
        row.st_size,
        row.st_mtime_ns,
        row.st_ctime_ns,
    )
    if identity(before) != identity(after):
        raise ValueError(f"{label} changed while being read")
    raw = b"".join(chunks)
    if len(raw) != before.st_size:
        raise ValueError(f"{label} read was incomplete")
    return raw


def require_exact_fields(value: object, fields: set[str], label: str) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != fields:
        raise ValueError(f"{label} fields are not exact")
    return value


def require_positive_integer(value: object, label: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        raise ValueError(f"{label} must be a positive integer")
    return value


def require_hex(value: object, pattern: re.Pattern[str], label: str) -> str:
    if not isinstance(value, str) or pattern.fullmatch(value) is None:
        raise ValueError(f"{label} is not canonical lowercase hex")
    return value


def validate_timestamp(value: object, label: str) -> str:
    if not isinstance(value, str) or not value.endswith("Z"):
        raise ValueError(f"{label} must be one UTC RFC3339 timestamp")
    try:
        parsed = datetime.fromisoformat(value.removesuffix("Z") + "+00:00")
    except ValueError as error:
        raise ValueError(f"{label} must be one UTC RFC3339 timestamp") from error
    if parsed.tzinfo is None or parsed.astimezone(UTC) > datetime.now(UTC) + timedelta(minutes=5):
        raise ValueError(f"{label} is in the future or lacks UTC authority")
    return value


def validate_browser_readback(
    value: dict[str, Any], *, require_fresh: bool = False
) -> dict[str, Any]:
    require_exact_fields(
        value,
        {
            "contractName",
            "observedAtUtc",
            "surface",
            "status",
            "application",
            "track",
            "release",
            "fieldsObserved",
            "credentialOrSessionDataRecorded",
        },
        "browser readback",
    )
    if value["contractName"] != BROWSER_READBACK_CONTRACT:
        raise ValueError("browser readback contract is not exact")
    observed_at = validate_timestamp(value["observedAtUtc"], "observedAtUtc")
    observed_instant = datetime.fromisoformat(
        observed_at.removesuffix("Z") + "+00:00"
    ).astimezone(UTC)
    if require_fresh and datetime.now(UTC) - observed_instant > MAX_BROWSER_READBACK_AGE:
        raise ValueError("browser readback is too old to materialize new evidence")
    if value["surface"] != "google_play_console_internal_testing" or value["status"] != "observed":
        raise ValueError("browser readback is not an observed Internal testing surface")
    if value["credentialOrSessionDataRecorded"] is not False:
        raise ValueError("browser readback records credential or session data")
    if value["fieldsObserved"] != list(EXPECTED_OBSERVED_FIELDS):
        raise ValueError("browser readback field inventory is not exact and canonical")

    application = require_exact_fields(
        value["application"], {"name", "packageId", "playApplicationId"}, "application"
    )
    if application["name"] != "Chummer" or application["packageId"] != PACKAGE_ID:
        raise ValueError("browser readback application identity is not Chummer")
    if application["playApplicationId"] != PLAY_APPLICATION_ID:
        raise ValueError("browser readback Play application ID is not exact for Chummer")

    track = require_exact_fields(
        value["track"], {"name", "consoleName", "trackId", "active", "joinUrl"}, "track"
    )
    if (
        track["name"] != "internal"
        or track["consoleName"] != "Internal testing"
        or track["active"] is not True
        or track["trackId"] != TRACK_ID
    ):
        raise ValueError("browser readback track is not exact Internal testing")
    if track["joinUrl"] != JOIN_URL:
        raise ValueError("browser readback join URL does not bind the exact Internal track")
    parsed_join = urlsplit(JOIN_URL)
    if (
        parsed_join.scheme != "https"
        or parsed_join.hostname != "play.google.com"
        or parsed_join.query
        or parsed_join.fragment
        or parsed_join.username
    ):
        raise ValueError("browser readback join URL contains extra authority")

    release = require_exact_fields(
        value["release"],
        {
            "name",
            "versionCode",
            "versionName",
            "status",
            "releasedAt",
            "supportedAndroidDevices",
        },
        "release",
    )
    version_code = require_positive_integer(release["versionCode"], "release.versionCode")
    version_name = release["versionName"]
    if (
        version_code <= HISTORICAL_VERSION_CODE_FLOOR
        or not isinstance(version_name, str)
        or len(version_name) > 128
        or VERSION_NAME.fullmatch(version_name) is None
    ):
        raise ValueError(
            "browser readback release identity is not a canonical post-Preview.10 version"
        )
    if release["name"] != f"{version_code} ({version_name})":
        raise ValueError("browser readback release name does not bind version code and name")
    if release["status"] != "Available to internal testers":
        raise ValueError("browser readback release is not available to Internal testers")
    require_positive_integer(
        release["supportedAndroidDevices"], "release.supportedAndroidDevices"
    )
    released_at = require_exact_fields(
        release["releasedAt"],
        {"consoleDisplay", "normalizedUtc", "precision", "timeZoneAuthority"},
        "release.releasedAt",
    )
    if (
        not isinstance(released_at["consoleDisplay"], str)
        or CONSOLE_DISPLAY.fullmatch(released_at["consoleDisplay"]) is None
        or released_at["normalizedUtc"] is not None
        or released_at["precision"] != "console_minute"
        or released_at["timeZoneAuthority"] != "not_exposed_by_browser_readback"
    ):
        raise ValueError("browser readback release time invents unavailable authority")
    return {
        "contractName": BROWSER_READBACK_CONTRACT,
        "observedAtUtc": observed_at,
        "surface": value["surface"],
        "status": value["status"],
        "application": dict(application),
        "track": dict(track),
        "release": {
            **release,
            "releasedAt": dict(released_at),
        },
        "fieldsObserved": list(EXPECTED_OBSERVED_FIELDS),
        "credentialOrSessionDataRecorded": False,
    }


def validate_source_graph(raw: bytes, *, version_name: str, version_code: int) -> dict[str, Any]:
    graph = strict_json(raw, label="source graph")
    require_exact_fields(
        graph,
        {
            "contractName",
            "generatedAtUtc",
            "authorityState",
            "publicationAuthorized",
            "releaseIdentity",
            "generator",
            "repositories",
            "packagePins",
            "ownerPackagePins",
            "dependencyClosure",
            "presentationSource",
            "doesNotAssert",
        },
        "source graph",
    )
    if (
        graph["contractName"] != SOURCE_GRAPH_CONTRACT
        or graph["authorityState"] != "local_review_required"
        or graph["publicationAuthorized"] is not False
        or graph["doesNotAssert"] != list(SOURCE_GRAPH_NONCLAIMS)
    ):
        raise ValueError("source graph authority boundary is not exact v3 local evidence")
    generated_at = validate_timestamp(graph["generatedAtUtc"], "sourceGraph.generatedAtUtc")
    identity = require_exact_fields(
        graph["releaseIdentity"],
        {
            "packageId",
            "versionName",
            "versionCode",
            "intentAuthority",
            "minimumExclusiveVersionCode",
        },
        "source graph releaseIdentity",
    )
    if identity != {
        "packageId": PACKAGE_ID,
        "versionName": version_name,
        "versionCode": version_code,
        "intentAuthority": "explicit_build_input",
        "minimumExclusiveVersionCode": HISTORICAL_VERSION_CODE_FLOOR,
    }:
        raise ValueError("source graph release identity does not match browser readback")

    generator = require_exact_fields(
        graph["generator"], {"path", "sha256", "size_bytes"}, "source graph generator"
    )
    generator_path = REPO_ROOT / "scripts/verify_release_source_graph.py"
    generator_raw = generator_path.read_bytes()
    if generator != {
        "path": "scripts/verify_release_source_graph.py",
        "sha256": hashlib.sha256(generator_raw).hexdigest(),
        "size_bytes": len(generator_raw),
    }:
        raise ValueError("source graph generator binding does not match this verifier")

    repositories = graph["repositories"]
    if not isinstance(repositories, list) or len(repositories) != len(EXPECTED_SOURCE_REPOSITORIES):
        raise ValueError("source graph repository inventory is not exact")
    if [row.get("name") if isinstance(row, dict) else None for row in repositories] != list(
        EXPECTED_SOURCE_REPOSITORIES
    ):
        raise ValueError("source graph repository order is not canonical")
    by_name: dict[str, dict[str, Any]] = {}
    for row in repositories:
        record = require_exact_fields(
            row,
            {"name", "role", "commit", "tree", "tree_sha256", "repository"},
            "source graph repository",
        )
        name = record["name"]
        if not isinstance(name, str) or name in by_name or name not in EXPECTED_SOURCE_REPOSITORIES:
            raise ValueError("source graph repository inventory is duplicated or unknown")
        expected_role, expected_repository = EXPECTED_SOURCE_REPOSITORIES[name]
        if record["role"] != expected_role or record["repository"] != expected_repository:
            raise ValueError("source graph repository role or origin is not exact")
        require_hex(record["commit"], SHA40, f"{name}.commit")
        require_hex(record["tree"], SHA40, f"{name}.tree")
        require_hex(record["tree_sha256"], SHA256, f"{name}.tree_sha256")
        by_name[name] = record
    if set(by_name) != set(EXPECTED_SOURCE_REPOSITORIES):
        raise ValueError("source graph repository inventory is incomplete")

    package_pins = graph["packagePins"]
    if not isinstance(package_pins, list) or [
        row.get("package_id") if isinstance(row, dict) else None for row in package_pins
    ] != list(EXPECTED_RUNTIME_PACKAGES):
        raise ValueError("source graph runtime package inventory is not exact")
    for row in package_pins:
        pin = require_exact_fields(
            row, {"package_id", "version", "sha256", "repository", "commit"},
            "source graph runtime package pin",
        )
        require_hex(pin["sha256"], SHA256, "runtime package sha256")
        require_hex(pin["commit"], SHA40, "runtime package commit")
        if (
            not isinstance(pin["version"], str)
            or PACKAGE_VERSION.fullmatch(pin["version"]) is None
            or pin["repository"] != "chummer6-core"
            or pin["commit"] != by_name["chummer6-core"]["commit"]
        ):
            raise ValueError("source graph runtime package source authority is not exact")
    if len({pin["sha256"] for pin in package_pins}) != len(package_pins):
        raise ValueError("source graph runtime package digests are not unique")

    owner_pins = graph["ownerPackagePins"]
    if not isinstance(owner_pins, list) or [
        row.get("package_id") if isinstance(row, dict) else None for row in owner_pins
    ] != list(EXPECTED_OWNER_PACKAGES):
        raise ValueError("source graph owner package inventory is not exact")
    owner_fields = {
        "package_id",
        "version",
        "sha256",
        "size_bytes",
        "owner_repository",
        "source_commit",
        "source_tree",
        "source_authority",
        "authority_receipt_sha256",
        "package_inventory_sha256",
        "package_plane_lock_sha256",
        "dependency_mode",
    }
    for row in owner_pins:
        pin = require_exact_fields(row, owner_fields, "source graph owner package pin")
        for field in (
            "sha256",
            "authority_receipt_sha256",
            "package_inventory_sha256",
            "package_plane_lock_sha256",
        ):
            require_hex(pin[field], SHA256, f"owner package {field}")
        require_hex(pin["source_commit"], SHA40, "owner package source_commit")
        require_hex(pin["source_tree"], SHA40, "owner package source_tree")
        require_positive_integer(pin["size_bytes"], "owner package size_bytes")
        if (
            not isinstance(pin["version"], str)
            or PACKAGE_VERSION.fullmatch(pin["version"]) is None
            or pin["owner_repository"] != OWNER_REPOSITORY_BY_PACKAGE[pin["package_id"]]
            or pin["dependency_mode"] != "locked_package"
        ):
            raise ValueError("source graph owner package authority is not exact and locked")
        source_authority = require_exact_fields(
            pin["source_authority"],
            {"owner_head_commit", "owner_head_tree", "relationship", "verification"},
            "owner package source authority",
        )
        require_hex(source_authority["owner_head_commit"], SHA40, "owner head commit")
        require_hex(source_authority["owner_head_tree"], SHA40, "owner head tree")
        owner_source = by_name[pin["owner_repository"]]
        if (
            source_authority["owner_head_commit"] != owner_source["commit"]
            or source_authority["owner_head_tree"] != owner_source["tree"]
            or source_authority["relationship"] != "ancestor_or_equal"
            or source_authority["verification"]
            != "git-merge-base-is-ancestor-without-replace-objects"
        ):
            raise ValueError("owner package source authority is not exact")
    if len({pin["sha256"] for pin in owner_pins}) != len(owner_pins):
        raise ValueError("source graph owner package digests are not unique")

    closure = graph["dependencyClosure"]
    if not isinstance(closure, list) or [
        row.get("package_id") if isinstance(row, dict) else None for row in closure
    ] != list(EXPECTED_OWNER_PACKAGES):
        raise ValueError("source graph dependency closure inventory is not exact")
    for row in closure:
        binding = require_exact_fields(
            row, {"package_id", "dependencies"}, "source graph dependency closure"
        )
        dependencies = binding["dependencies"]
        if (
            not isinstance(dependencies, list)
            or any(not isinstance(item, str) or not item for item in dependencies)
            or dependencies != sorted(set(dependencies))
        ):
            raise ValueError("source graph dependency closure is not canonical")
        if (
            binding["package_id"] == "Chummer.Run.Contracts"
            and "Chummer.Play.Contracts" not in dependencies
        ):
            raise ValueError("source graph dependency closure omits Chummer.Play.Contracts")

    presentation = require_exact_fields(
        graph["presentationSource"],
        {
            "repository",
            "commit",
            "tree",
            "source_path",
            "authority_state",
            "publication_authorized",
            "dependency_mode",
        },
        "source graph presentation source",
    )
    if (
        presentation["repository"] != "chummer6-ui"
        or presentation["source_path"] != "chummer-presentation"
        or presentation["authority_state"] != "local_review_required"
        or presentation["publication_authorized"] is not False
        or presentation["dependency_mode"] != "source_compatibility"
        or presentation["commit"] != by_name["chummer6-ui"]["commit"]
        or presentation["tree"] != by_name["chummer6-ui"]["tree"]
    ):
        raise ValueError("source graph Presentation authority is not exact local source")
    require_hex(presentation["commit"], SHA40, "presentation source commit")
    require_hex(presentation["tree"], SHA40, "presentation source tree")
    return {
        "contractName": SOURCE_GRAPH_CONTRACT,
        "generatedAtUtc": generated_at,
        "androidSourceCommit": by_name["chummer-android"]["commit"],
        "androidSourceTree": by_name["chummer-android"]["tree"],
    }


def validate_aab(raw: bytes) -> None:
    try:
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)) or archive.testzip() is not None:
                raise ValueError("AAB ZIP entries are duplicated or corrupt")
    except (zipfile.BadZipFile, OSError) as error:
        raise ValueError("AAB is not a readable bundle archive") from error
    if (
        "BundleConfig.pb" not in names
        or "base/manifest/AndroidManifest.xml" not in names
        or any(
            name.startswith("/") or "\\" in name or ".." in Path(name).parts
            for name in names
        )
    ):
        raise ValueError("AAB archive structure is not canonical")


def load_artifact_bindings(
    aab_path: Path,
    source_graph_path: Path,
    browser: dict[str, Any],
    *,
    expected_android_source_commit: str,
    expected_aab_sha256: str,
    expected_source_graph_sha256: str,
) -> dict[str, Any]:
    version_name = browser["release"]["versionName"]
    version_code = browser["release"]["versionCode"]
    if aab_path.name != f"chummer-android-{version_name}-upload.aab":
        raise ValueError("AAB filename does not bind browser release identity")
    if source_graph_path.name != f"chummer-android-{version_name}-source-graph.json":
        raise ValueError("source graph filename does not bind browser release identity")
    aab_raw = read_regular(
        aab_path, label="AAB", limit=MAX_AAB_BYTES, owner_only=False
    )
    graph_raw = read_regular(
        source_graph_path, label="source graph", limit=MAX_GRAPH_BYTES, owner_only=True
    )
    validate_aab(aab_raw)
    graph = validate_source_graph(
        graph_raw, version_name=version_name, version_code=version_code
    )
    expected_head = require_hex(
        expected_android_source_commit, SHA40, "expected Android source commit"
    )
    expected_aab = require_hex(expected_aab_sha256, SHA256, "expected AAB sha256")
    expected_graph = require_hex(
        expected_source_graph_sha256,
        SHA256,
        "expected source graph sha256",
    )
    actual_aab = hashlib.sha256(aab_raw).hexdigest()
    actual_graph = hashlib.sha256(graph_raw).hexdigest()
    if graph["androidSourceCommit"] != expected_head:
        raise ValueError("source graph Android commit does not match approved source head")
    if actual_aab != expected_aab:
        raise ValueError("AAB bytes do not match approved AAB sha256")
    graph_time = datetime.fromisoformat(
        graph["generatedAtUtc"].removesuffix("Z") + "+00:00"
    )
    observed_time = datetime.fromisoformat(
        browser["observedAtUtc"].removesuffix("Z") + "+00:00"
    )
    if graph_time > observed_time:
        raise ValueError("source graph was generated after the browser publication observation")
    if actual_graph != expected_graph:
        raise ValueError("source graph bytes do not match approved build-sidecar sha256")
    return {
        "aabFileName": aab_path.name,
        "aabSha256": actual_aab,
        "aabSizeBytes": len(aab_raw),
        "sourceGraphFileName": source_graph_path.name,
        "sourceGraphSha256": actual_graph,
        "sourceGraphSizeBytes": len(graph_raw),
        "sourceGraphContract": graph["contractName"],
        "sourceGraphGeneratedAtUtc": graph["generatedAtUtc"],
        "androidSourceCommit": graph["androidSourceCommit"],
        "androidSourceTree": graph["androidSourceTree"],
    }


def build_receipt(
    browser: dict[str, Any],
    artifact: dict[str, Any],
    two_green_eligibility: dict[str, Any],
) -> dict[str, Any]:
    return {
        "contractName": CONTRACT,
        "recordedAtUtc": browser["observedAtUtc"],
        "evidenceClass": (
            "explicit_internal_browser_readback_plus_exact_qualified_release_outputs"
        ),
        "publicationAuthorized": False,
        "application": dict(browser["application"]),
        "track": dict(browser["track"]),
        "release": {
            **browser["release"],
            "releasedAt": dict(browser["release"]["releasedAt"]),
        },
        "artifact": artifact,
        "twoGreenEligibility": two_green_eligibility,
        "browserReadback": {
            "contractName": BROWSER_READBACK_CONTRACT,
            "surface": browser["surface"],
            "status": browser["status"],
            "fieldsObserved": list(browser["fieldsObserved"]),
            "credentialOrSessionDataRecorded": False,
            "canonicalEvidenceSha256": hashlib.sha256(canonical_json_bytes(browser)).hexdigest(),
        },
        "artifactLinkage": {
            "basis": (
                "two_green_qualified_exact_local_release_outputs_plus_explicit_browser_readback"
            ),
            "localAabBytesVerified": True,
            "localSourceGraphBytesVerified": True,
            "playConsoleExposesArtifactDigest": False,
            "artifactDownloadedBackFromPlay": False,
            "sourceGraphRetrievedFromPlay": False,
        },
        "authorization": {
            "scope": "google_play_internal_testing_evidence_only",
            "publicationAuthorized": False,
            "productionAuthorized": False,
            "uploadActionAuthorized": False,
            "testerRosterMutationAuthorized": False,
        },
        "doesNotClaim": list(RECEIPT_NONCLAIMS),
    }


def browser_from_receipt(receipt: dict[str, Any]) -> dict[str, Any]:
    browser = require_exact_fields(
        receipt.get("browserReadback"),
        {
            "contractName",
            "surface",
            "status",
            "fieldsObserved",
            "credentialOrSessionDataRecorded",
            "canonicalEvidenceSha256",
        },
        "receipt browserReadback",
    )
    return validate_browser_readback(
        {
            "contractName": browser["contractName"],
            "observedAtUtc": receipt.get("recordedAtUtc"),
            "surface": browser["surface"],
            "status": browser["status"],
            "application": receipt.get("application"),
            "track": receipt.get("track"),
            "release": receipt.get("release"),
            "fieldsObserved": browser["fieldsObserved"],
            "credentialOrSessionDataRecorded": browser["credentialOrSessionDataRecorded"],
        }
    )


def verify(
    receipt_path: Path,
    aab_path: Path,
    source_graph_path: Path,
    *,
    expected_android_source_commit: str,
    expected_aab_sha256: str,
    expected_source_graph_sha256: str,
    two_green_receipt_path: Path,
    two_green_approval_path: Path,
) -> dict[str, Any]:
    failures: list[str] = []
    receipt_raw = b""
    artifact: dict[str, Any] = {}
    try:
        receipt_raw = read_regular(
            receipt_path, label="publication receipt", limit=MAX_RECEIPT_BYTES, owner_only=True
        )
        receipt = strict_json(receipt_raw, label="publication receipt")
        require_exact_fields(
            receipt,
            {
                "contractName",
                "recordedAtUtc",
                "evidenceClass",
                "publicationAuthorized",
                "application",
                "track",
                "release",
                "artifact",
                "twoGreenEligibility",
                "browserReadback",
                "artifactLinkage",
                "authorization",
                "doesNotClaim",
            },
            "publication receipt",
        )
        if receipt["contractName"] != CONTRACT:
            raise ValueError("publication receipt contract is not exact v4")
        browser = browser_from_receipt(receipt)
        artifact = load_artifact_bindings(
            aab_path,
            source_graph_path,
            browser,
            expected_android_source_commit=expected_android_source_commit,
            expected_aab_sha256=expected_aab_sha256,
            expected_source_graph_sha256=expected_source_graph_sha256,
        )
        two_green_eligibility = QUALIFICATION.verify_release_eligibility(
            two_green_receipt_path,
            two_green_approval_path,
            android_root=REPO_ROOT,
            expected_version_name=browser["release"]["versionName"],
            expected_version_code=browser["release"]["versionCode"],
            source_graph_path=source_graph_path,
            approval_effective_time=datetime.fromisoformat(
                artifact["sourceGraphGeneratedAtUtc"].removesuffix("Z") + "+00:00"
            ),
        )
        expected = build_receipt(browser, artifact, two_green_eligibility)
        if receipt != expected:
            raise ValueError("publication receipt claims or bindings are not exact")
        if receipt_raw != canonical_json_bytes(receipt):
            raise ValueError("publication receipt bytes are not canonical")
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        failures.append(str(error))
    passed = not failures
    return {
        "contractName": VERIFICATION_CONTRACT,
        "status": "pass" if passed else "fail",
        "publicationAuthorized": False,
        "authorizationScope": "none",
        "evidenceScope": (
            "google_play_internal_testing_observation_only" if passed else "none"
        ),
        "productionAuthorized": False,
        "browserReadbackVerified": passed,
        "localArtifactBytesVerified": passed,
        "receiptSha256": hashlib.sha256(receipt_raw).hexdigest() if receipt_raw else None,
        "artifact": {
            "aabSha256": artifact.get("aabSha256"),
            "sourceGraphSha256": artifact.get("sourceGraphSha256"),
        },
        "failures": failures,
    }


def write_exclusive(path: Path, raw: bytes) -> None:
    if (
        not path.is_absolute()
        or path.exists()
        or path.is_symlink()
        or not path.parent.is_dir()
        or path.parent.is_symlink()
        or path.parent.resolve(strict=True) != path.parent
        or path.parent.stat().st_uid != os.getuid()
        or stat.S_IMODE(path.parent.stat().st_mode) & 0o077
    ):
        raise ValueError("receipt output must be a new file in one canonical owner-only directory")
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        0o600,
    )
    try:
        with os.fdopen(descriptor, "wb") as output:
            output.write(raw)
            output.flush()
            os.fsync(output.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def materialize(
    browser_readback_path: Path,
    aab_path: Path,
    source_graph_path: Path,
    output_path: Path,
    *,
    expected_android_source_commit: str,
    expected_aab_sha256: str,
    expected_source_graph_sha256: str,
    two_green_receipt_path: Path,
    two_green_approval_path: Path,
) -> dict[str, Any]:
    browser_raw = read_regular(
        browser_readback_path,
        label="browser readback",
        limit=MAX_BROWSER_BYTES,
        owner_only=True,
    )
    browser = validate_browser_readback(
        strict_json(browser_raw, label="browser readback"), require_fresh=True
    )
    artifact = load_artifact_bindings(
        aab_path,
        source_graph_path,
        browser,
        expected_android_source_commit=expected_android_source_commit,
        expected_aab_sha256=expected_aab_sha256,
        expected_source_graph_sha256=expected_source_graph_sha256,
    )
    two_green_eligibility = QUALIFICATION.verify_release_eligibility(
        two_green_receipt_path,
        two_green_approval_path,
        android_root=REPO_ROOT,
        expected_version_name=browser["release"]["versionName"],
        expected_version_code=browser["release"]["versionCode"],
        source_graph_path=source_graph_path,
    )
    receipt = build_receipt(browser, artifact, two_green_eligibility)
    write_exclusive(output_path, canonical_json_bytes(receipt))
    try:
        result = verify(
            output_path,
            aab_path,
            source_graph_path,
            expected_android_source_commit=expected_android_source_commit,
            expected_aab_sha256=expected_aab_sha256,
            expected_source_graph_sha256=expected_source_graph_sha256,
            two_green_receipt_path=two_green_receipt_path,
            two_green_approval_path=two_green_approval_path,
        )
        if result["status"] != "pass":
            raise ValueError("new publication receipt failed self-verification")
    except Exception:
        output_path.unlink(missing_ok=True)
        raise
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    materialize_parser = actions.add_parser("materialize")
    materialize_parser.add_argument("--browser-readback", required=True, type=Path)
    materialize_parser.add_argument("--aab", required=True, type=Path)
    materialize_parser.add_argument("--source-graph", required=True, type=Path)
    materialize_parser.add_argument("--output", required=True, type=Path)
    materialize_parser.add_argument("--expected-android-source-commit", required=True)
    materialize_parser.add_argument("--expected-aab-sha256", required=True)
    materialize_parser.add_argument("--expected-source-graph-sha256", required=True)
    materialize_parser.add_argument("--two-green-receipt", required=True, type=Path)
    materialize_parser.add_argument("--two-green-approval", required=True, type=Path)
    verify_parser = actions.add_parser("verify")
    verify_parser.add_argument("--receipt", required=True, type=Path)
    verify_parser.add_argument("--aab", required=True, type=Path)
    verify_parser.add_argument("--source-graph", required=True, type=Path)
    verify_parser.add_argument("--expected-android-source-commit", required=True)
    verify_parser.add_argument("--expected-aab-sha256", required=True)
    verify_parser.add_argument("--expected-source-graph-sha256", required=True)
    verify_parser.add_argument("--two-green-receipt", required=True, type=Path)
    verify_parser.add_argument("--two-green-approval", required=True, type=Path)
    arguments = parser.parse_args(argv)
    try:
        if arguments.action == "materialize":
            result = materialize(
                arguments.browser_readback,
                arguments.aab,
                arguments.source_graph,
                arguments.output,
                expected_android_source_commit=arguments.expected_android_source_commit,
                expected_aab_sha256=arguments.expected_aab_sha256,
                expected_source_graph_sha256=arguments.expected_source_graph_sha256,
                two_green_receipt_path=arguments.two_green_receipt,
                two_green_approval_path=arguments.two_green_approval,
            )
        else:
            result = verify(
                arguments.receipt,
                arguments.aab,
                arguments.source_graph,
                expected_android_source_commit=arguments.expected_android_source_commit,
                expected_aab_sha256=arguments.expected_aab_sha256,
                expected_source_graph_sha256=arguments.expected_source_graph_sha256,
                two_green_receipt_path=arguments.two_green_receipt,
                two_green_approval_path=arguments.two_green_approval,
            )
    except (OSError, ValueError) as error:
        result = {
            "contractName": VERIFICATION_CONTRACT,
            "status": "fail",
            "publicationAuthorized": False,
            "authorizationScope": "none",
            "productionAuthorized": False,
            "failures": [str(error)],
        }
    print(json.dumps(result, sort_keys=True))
    return 0 if result["status"] == "pass" else 2


if __name__ == "__main__":
    raise SystemExit(main())
