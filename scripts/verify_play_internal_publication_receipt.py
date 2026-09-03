#!/usr/bin/env python3
"""Verify the recorded Preview.10 Play Internal publication truth.

The receipt records two evidence classes: Play Console browser readback and
local build-sidecar identity. Play does not expose the uploaded AAB digest for
readback, so this verifier must not manufacture artifact-retrieval or
tester-install claims from the browser observation.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit


CONTRACT = "chummer.android.play-internal-publication-receipt/v2"
PACKAGE_ID = "com.myexternalbrain.chummer"
PLAY_APPLICATION_ID = "4975957268242186974"
TRACK_ID = "4700678198570024687"
JOIN_URL = f"https://play.google.com/apps/internaltest/{TRACK_ID}"
VERSION_CODE = 10
VERSION_NAME = "0.1.0-preview.10"
RELEASE_NAME = "10 (0.1.0-preview.10)"
RELEASE_STATUS = "Available to internal testers"
RELEASE_CONSOLE_TIME = "3 Sept 01:04"
RECORDED_AT_UTC = "2026-09-02T23:36:57Z"
SUPPORTED_ANDROID_DEVICES = 13550
EXPECTED_AAB_SHA256 = "964d81b5d4463e0bd1c6de8172a7a12655e982897202b0151dccc69a566aaae1"
EXPECTED_AAB_SIZE = 29197875
EXPECTED_SOURCE_GRAPH_SHA256 = "257ce53d912aea02416a64288029a589324e037464f76883d925b678b7364a24"
SOURCE_GRAPH_CONTRACT = "chummer.android.release-source-graph/v2"
SOURCE_GRAPH_GENERATED_AT_UTC = "2026-09-02T22:17:57.971042Z"
ANDROID_SOURCE_COMMIT = "f276d4af2d936760f6d21871f281b1f7dd50e261"
ANDROID_SOURCE_TREE = "6e03b31cf4b1b85225e6d7134db4cc0482210b5e"
EXPECTED_OBSERVED_FIELDS = {"application", "track", "release", "join_url"}
EXPECTED_NONCLAIMS = {
    "tester_installation",
    "production_rollout",
    "public_release",
    "tablet_support",
    "full_edit_parity",
    "live_rook_rule_authority",
    "play_artifact_digest_readback",
    "play_artifact_byte_retrievability",
}
SHA256 = re.compile(r"[0-9a-f]{64}")
COMMIT = re.compile(r"[0-9a-f]{40}")
MAX_RECEIPT_BYTES = 256 * 1024
MAX_GRAPH_BYTES = 4 * 1024 * 1024
MAX_AAB_BYTES = 512 * 1024 * 1024
TOP_LEVEL_KEYS = {
    "contractName",
    "recordedAtUtc",
    "application",
    "track",
    "release",
    "artifact",
    "browserReadback",
    "artifactLinkage",
    "authorization",
    "doesNotClaim",
}


def _strict_json(raw: bytes, *, label: str) -> dict[str, Any]:
    def pairs(values: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in values:
            if key in result:
                raise ValueError(f"{label} contains duplicate key {key!r}")
            result[key] = value
        return result

    payload = json.loads(
        raw.decode("utf-8"),
        object_pairs_hook=pairs,
        parse_constant=lambda value: (_ for _ in ()).throw(
            ValueError(f"{label} contains non-finite number {value}")
        ),
    )
    if not isinstance(payload, dict):
        raise ValueError(f"{label} must be an object")
    return payload


def _read_regular(path: Path, *, label: str, limit: int) -> bytes:
    if path.is_symlink():
        raise ValueError(f"{label} must not be a symlink")
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags)
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode) or before.st_size > limit:
            raise ValueError(f"{label} must be a bounded regular file")
        chunks: list[bytes] = []
        total = 0
        while total <= limit:
            chunk = os.read(descriptor, min(1024 * 1024, limit + 1 - total))
            if not chunk:
                break
            chunks.append(chunk)
            total += len(chunk)
        after = os.fstat(descriptor)
        identity = lambda row: (row.st_dev, row.st_ino, row.st_size, row.st_mtime_ns, row.st_ctime_ns)
        if identity(before) != identity(after):
            raise ValueError(f"{label} changed while being read")
        raw = b"".join(chunks)
        if len(raw) != before.st_size:
            raise ValueError(f"{label} read was incomplete")
        return raw
    finally:
        os.close(descriptor)


def _require(condition: bool, failures: list[str], message: str) -> None:
    if not condition:
        failures.append(message)


def _object(
    payload: dict[str, Any],
    name: str,
    keys: set[str],
    failures: list[str],
) -> dict[str, Any]:
    value = payload.get(name)
    if not isinstance(value, dict):
        failures.append(f"{name} must be an object")
        return {}
    _require(set(value) == keys, failures, f"{name} fields are not exact")
    return value


def _parse_timestamp(value: object, *, label: str, failures: list[str]) -> None:
    if not isinstance(value, str):
        failures.append(f"{label} is missing")
        return
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        _require(parsed.tzinfo is not None, failures, f"{label} lacks a timezone")
        if parsed.tzinfo is not None:
            _require(
                parsed.astimezone(UTC) <= datetime.now(UTC) + timedelta(minutes=5),
                failures,
                f"{label} is in the future",
            )
    except ValueError:
        failures.append(f"{label} is invalid")


def _validate_join_url(value: object, failures: list[str]) -> None:
    _require(value == JOIN_URL, failures, "join URL is not the exact Internal testing URL")
    if not isinstance(value, str):
        return
    parsed = urlsplit(value)
    _require(parsed.scheme == "https", failures, "join URL is not HTTPS")
    _require(parsed.hostname == "play.google.com", failures, "join URL host is not Google Play")
    _require(parsed.path == f"/apps/internaltest/{TRACK_ID}", failures, "join URL path is not exact")
    _require(
        not parsed.query and not parsed.fragment and not parsed.username,
        failures,
        "join URL contains extra authority",
    )


def verify(
    receipt_path: Path,
    *,
    aab_path: Path | None = None,
    source_graph_path: Path | None = None,
    expected_aab_sha256: str = EXPECTED_AAB_SHA256,
    expected_aab_size: int = EXPECTED_AAB_SIZE,
    expected_source_graph_sha256: str = EXPECTED_SOURCE_GRAPH_SHA256,
) -> dict[str, Any]:
    failures: list[str] = []
    try:
        receipt_raw = _read_regular(receipt_path, label="publication receipt", limit=MAX_RECEIPT_BYTES)
        receipt = _strict_json(receipt_raw, label="publication receipt")
    except (OSError, UnicodeDecodeError, json.JSONDecodeError, RecursionError, ValueError) as exc:
        return {
            "contractName": "chummer.android.play-internal-publication-verification/v1",
            "status": "fail",
            "failures": [
                f"publication receipt is unavailable or invalid: {type(exc).__name__}: {exc}"
            ],
            "byteEvidenceVerified": False,
        }

    _require(set(receipt) == TOP_LEVEL_KEYS, failures, "publication receipt fields are not exact")
    _require(receipt.get("contractName") == CONTRACT, failures, "publication receipt contract is not v2")
    _parse_timestamp(receipt.get("recordedAtUtc"), label="recordedAtUtc", failures=failures)
    _require(receipt.get("recordedAtUtc") == RECORDED_AT_UTC, failures, "recordedAtUtc is not exact")

    application = _object(receipt, "application", {"name", "packageId", "playApplicationId"}, failures)
    _require(application.get("name") == "Chummer", failures, "application name is not Chummer")
    _require(application.get("packageId") == PACKAGE_ID, failures, "package ID is incorrect")
    _require(
        application.get("playApplicationId") == PLAY_APPLICATION_ID,
        failures,
        "Play application ID is incorrect",
    )

    track = _object(receipt, "track", {"name", "consoleName", "trackId", "active", "joinUrl"}, failures)
    _require(track.get("name") == "internal", failures, "Play track is not internal")
    _require(track.get("consoleName") == "Internal testing", failures, "Play track console name is incorrect")
    _require(track.get("trackId") == TRACK_ID, failures, "Play track ID is incorrect")
    _require(track.get("active") is True, failures, "Play Internal track is not active")
    _validate_join_url(track.get("joinUrl"), failures)

    release = _object(
        receipt,
        "release",
        {"name", "versionCode", "versionName", "status", "releasedAt", "supportedAndroidDevices"},
        failures,
    )
    _require(release.get("name") == RELEASE_NAME, failures, "release name is incorrect")
    _require(
        type(release.get("versionCode")) is int and release.get("versionCode") == VERSION_CODE,
        failures,
        "version code is incorrect",
    )
    _require(release.get("versionName") == VERSION_NAME, failures, "version name is incorrect")
    _require(release.get("status") == RELEASE_STATUS, failures, "Play release status is incorrect")
    _require(
        type(release.get("supportedAndroidDevices")) is int
        and release.get("supportedAndroidDevices") == SUPPORTED_ANDROID_DEVICES,
        failures,
        "supported-device count is not exact",
    )
    released_at = release.get("releasedAt")
    if not isinstance(released_at, dict):
        failures.append("releasedAt must be an object")
        released_at = {}
    _require(
        set(released_at) == {"consoleDisplay", "normalizedUtc", "precision", "timeZoneAuthority"},
        failures,
        "releasedAt fields are not exact",
    )
    _require(
        released_at.get("consoleDisplay") == RELEASE_CONSOLE_TIME,
        failures,
        "release console time is incorrect",
    )
    _require(
        released_at.get("normalizedUtc") is None,
        failures,
        "release time invents a normalized UTC value",
    )
    _require(released_at.get("precision") == "console_minute", failures, "release-time precision is incorrect")
    _require(
        released_at.get("timeZoneAuthority") == "not_exposed_by_browser_readback",
        failures,
        "release-time timezone authority is incorrect",
    )

    artifact = _object(
        receipt,
        "artifact",
        {
            "aabFileName",
            "aabSha256",
            "aabSizeBytes",
            "sourceGraphFileName",
            "sourceGraphSha256",
            "sourceGraphContract",
            "sourceGraphGeneratedAtUtc",
            "androidSourceCommit",
            "androidSourceTree",
        },
        failures,
    )
    _require(
        artifact.get("aabFileName") == f"chummer-android-{VERSION_NAME}-upload.aab",
        failures,
        "AAB filename is incorrect",
    )
    _require(bool(SHA256.fullmatch(str(artifact.get("aabSha256") or ""))), failures, "AAB digest is invalid")
    _require(
        artifact.get("aabSha256") == expected_aab_sha256,
        failures,
        "AAB digest is not the exact Preview.10 digest",
    )
    _require(
        type(artifact.get("aabSizeBytes")) is int
        and artifact.get("aabSizeBytes") == expected_aab_size,
        failures,
        "AAB size is incorrect",
    )
    _require(
        artifact.get("sourceGraphFileName") == f"chummer-android-{VERSION_NAME}-source-graph.json",
        failures,
        "source-graph filename is incorrect",
    )
    _require(
        bool(SHA256.fullmatch(str(artifact.get("sourceGraphSha256") or ""))),
        failures,
        "source-graph digest is invalid",
    )
    _require(
        artifact.get("sourceGraphSha256") == expected_source_graph_sha256,
        failures,
        "source-graph digest is not exact",
    )
    _require(
        artifact.get("sourceGraphContract") == SOURCE_GRAPH_CONTRACT,
        failures,
        "source-graph contract is incorrect",
    )
    _parse_timestamp(
        artifact.get("sourceGraphGeneratedAtUtc"),
        label="sourceGraphGeneratedAtUtc",
        failures=failures,
    )
    _require(
        artifact.get("sourceGraphGeneratedAtUtc") == SOURCE_GRAPH_GENERATED_AT_UTC,
        failures,
        "source-graph generation time is not exact",
    )
    _require(
        bool(COMMIT.fullmatch(str(artifact.get("androidSourceCommit") or ""))),
        failures,
        "Android source commit is invalid",
    )
    _require(
        artifact.get("androidSourceCommit") == ANDROID_SOURCE_COMMIT,
        failures,
        "Android source commit is not exact",
    )
    _require(
        bool(COMMIT.fullmatch(str(artifact.get("androidSourceTree") or ""))),
        failures,
        "Android source tree is invalid",
    )
    _require(
        artifact.get("androidSourceTree") == ANDROID_SOURCE_TREE,
        failures,
        "Android source tree is not exact",
    )

    browser = _object(
        receipt,
        "browserReadback",
        {"surface", "status", "fieldsObserved", "credentialOrSessionDataRecorded"},
        failures,
    )
    _require(
        browser.get("surface") == "google_play_console_internal_testing",
        failures,
        "browser surface is incorrect",
    )
    _require(browser.get("status") == "observed", failures, "browser readback was not observed")
    observed = browser.get("fieldsObserved")
    observed_is_strings = isinstance(observed, list) and all(
        isinstance(value, str) for value in observed
    )
    _require(
        observed_is_strings and len(observed) == len(set(observed)),
        failures,
        "browser readback fields are not unique",
    )
    _require(
        observed_is_strings and set(observed) == EXPECTED_OBSERVED_FIELDS,
        failures,
        "browser readback fields are not exact",
    )
    _require(
        browser.get("credentialOrSessionDataRecorded") is False,
        failures,
        "receipt contains credential/session authority",
    )

    linkage = _object(
        receipt,
        "artifactLinkage",
        {
            "basis",
            "localBuildSidecarRecorded",
            "playConsoleExposesArtifactDigest",
            "artifactDownloadedBackFromPlay",
            "sourceGraphRetrievedFromPlay",
        },
        failures,
    )
    _require(
        linkage.get("basis") == "local_build_sidecar_plus_operator_upload_action",
        failures,
        "artifact linkage basis is incorrect",
    )
    _require(linkage.get("localBuildSidecarRecorded") is True, failures, "local build sidecar is not recorded")
    _require(linkage.get("playConsoleExposesArtifactDigest") is False, failures, "receipt claims Play digest readback")
    _require(linkage.get("artifactDownloadedBackFromPlay") is False, failures, "receipt claims AAB retrieval from Play")
    _require(linkage.get("sourceGraphRetrievedFromPlay") is False, failures, "receipt claims source-graph retrieval from Play")

    authorization = _object(
        receipt,
        "authorization",
        {"scope", "productionAuthorized", "testerRosterMutationAuthorized"},
        failures,
    )
    _require(
        authorization.get("scope") == "google_play_internal_testing_only",
        failures,
        "authorization scope is not Internal-only",
    )
    _require(authorization.get("productionAuthorized") is False, failures, "receipt authorizes production")
    _require(
        authorization.get("testerRosterMutationAuthorized") is False,
        failures,
        "receipt authorizes tester-roster mutation",
    )

    nonclaims = receipt.get("doesNotClaim")
    nonclaims_are_strings = isinstance(nonclaims, list) and all(
        isinstance(value, str) for value in nonclaims
    )
    _require(
        nonclaims_are_strings and len(nonclaims) == len(set(nonclaims)),
        failures,
        "nonclaims are not unique",
    )
    _require(
        nonclaims_are_strings and set(nonclaims) == EXPECTED_NONCLAIMS,
        failures,
        "nonclaim set is not exact",
    )

    record_failures = list(failures)
    byte_evidence_verified = False
    if (aab_path is None) != (source_graph_path is None):
        failures.append("AAB and source graph must be supplied together for byte verification")
    elif aab_path is not None and source_graph_path is not None:
        try:
            aab_raw = _read_regular(aab_path, label="AAB", limit=MAX_AAB_BYTES)
            graph_raw = _read_regular(source_graph_path, label="source graph", limit=MAX_GRAPH_BYTES)
            _require(
                hashlib.sha256(aab_raw).hexdigest() == artifact.get("aabSha256"),
                failures,
                "retrieved AAB digest mismatch",
            )
            _require(len(aab_raw) == artifact.get("aabSizeBytes"), failures, "retrieved AAB size mismatch")
            _require(
                hashlib.sha256(graph_raw).hexdigest() == artifact.get("sourceGraphSha256"),
                failures,
                "retrieved source-graph digest mismatch",
            )
            graph = _strict_json(graph_raw, label="source graph")
            _require(
                graph.get("contractName") == SOURCE_GRAPH_CONTRACT,
                failures,
                "retrieved source-graph contract mismatch",
            )
            rows = graph.get("repositories") if isinstance(graph.get("repositories"), list) else []
            android_rows = [
                row for row in rows if isinstance(row, dict) and row.get("name") == "chummer-android"
            ]
            _require(
                len(android_rows) == 1
                and android_rows[0].get("commit") == ANDROID_SOURCE_COMMIT
                and android_rows[0].get("tree") == ANDROID_SOURCE_TREE,
                failures,
                "retrieved source graph does not bind exact Android source",
            )
            byte_evidence_verified = not failures
        except (OSError, UnicodeDecodeError, json.JSONDecodeError, RecursionError, ValueError) as exc:
            failures.append(f"byte evidence is unavailable or invalid: {type(exc).__name__}: {exc}")

    unique_failures = list(dict.fromkeys(failures))
    return {
        "contractName": "chummer.android.play-internal-publication-verification/v1",
        "status": "pass" if not unique_failures else "fail",
        "receiptSha256": hashlib.sha256(receipt_raw).hexdigest(),
        "browserReadbackVerified": not record_failures,
        "byteEvidenceSupplied": aab_path is not None and source_graph_path is not None,
        "byteEvidenceVerified": byte_evidence_verified,
        "artifact": {
            "aabSha256": artifact.get("aabSha256"),
            "sourceGraphSha256": artifact.get("sourceGraphSha256"),
        },
        "authorization": {
            "scope": authorization.get("scope"),
            "productionAuthorized": authorization.get("productionAuthorized"),
        },
        "doesNotClaim": sorted(nonclaims) if nonclaims_are_strings else [],
        "failures": unique_failures,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--receipt",
        type=Path,
        default=Path("play/evidence/preview10-internal-publication.json"),
    )
    parser.add_argument("--aab", type=Path)
    parser.add_argument("--source-graph", type=Path)
    args = parser.parse_args(argv)
    payload = verify(args.receipt, aab_path=args.aab, source_graph_path=args.source_graph)
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
