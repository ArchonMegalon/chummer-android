#!/usr/bin/env python3
"""API-36 phone proof for the authoritative Priority/Sum-to-Ten prerequisite.

The source remains an unexecuted contract until CI or an operator runs it against a reviewed APK.
A successfully completed invocation emits a pass receipt bound to that APK and this driver.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import time
from collections.abc import Callable
from datetime import datetime, timezone
from pathlib import Path
from typing import NamedTuple

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


CATEGORIES = ("heritage", "talent", "attributes", "skills", "resources")
PRIORITY_PROOF_RANKS = {
    "heritage": "e",
    "talent": "b",
    "attributes": "a",
    "skills": "c",
    "resources": "d",
}
PRIORITY_RANK_LABEL_BY_LANGUAGE = {
    "en": "Rank",
    "de": "Rang",
    "es": "Rango",
}
PRIORITY_KARMA_LABELS_BY_LANGUAGE = {
    "en": ("Total", "Used", "Remaining"),
    "de": ("Gesamt", "Verwendet", "Verbleibend"),
    "es": ("Total", "Usado", "Restante"),
}
ACTIVE_SKILL_TALENT_LABEL = "Adept - 6 Magic"
SKILL_GROUP_TALENT_LABEL = "Aspected Magician - 5 Magic"
TALENT_GRANT_KINDS = ("Active skills", "Skill groups")
TALENT_GRANT_OPTION_PREFIX = {
    "Active skills": "creation-prerequisite-talent-active-skill-option-",
    "Skill groups": "creation-prerequisite-talent-skill-group-option-",
}
TALENT_GRANT_PREVIEW_PREFIX = {
    "Active skills": "creation-prerequisite-preview-talent-active-skill-",
    "Skill groups": "creation-prerequisite-preview-talent-skill-group-",
}
TALENT_GRANT_REQUIRED = re.compile(
    r"(?<![0-9])(?P<selected>[0-9]+) / (?P<required>[0-9]+) "
    r"(?P<kind>Active skills|Skill groups)(?=$|[. ·])"
)
TALENT_SELECTED_SLOT_DECORATOR = re.compile(
    r"(?P<separator>\. )Selected slot (?P<slot>[1-9][0-9]*) · "
)
CREATION_KARMA_AUTHORITY_BLOCKER = "creation-karma-authority-required"
STANDARD_PRIORITY_SETTINGS_ID = "223a11ff-80e0-428b-89a9-6ef1c243b8b6"
SHORT_AUTHORITY_BINDING = re.compile(
    r"^Revision (?P<revision>[1-9][0-9]*) · saved (?P<saved>[0-9]+) · "
    r"snapshot (?P<snapshot>[0-9a-f]{12}) · authority (?P<authority>[0-9a-f]{12})$"
)
CANONICAL_AUTHORITY_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
CANONICAL_AUXILIARY_STATE_DIGEST = re.compile(r"^[0-9a-f]{64}$")
CREATION_AUTHORITY_PENDING_TIMEOUT_PREFIX = (
    "creation-dashboard-authority-loading-pending-timeout"
)
CREATION_AUTHORITY_PENDING_TIMEOUT_MANIFEST = (
    f"{CREATION_AUTHORITY_PENDING_TIMEOUT_PREFIX}-manifest.json"
)
CREATION_AUTHORITY_PENDING_TIMEOUT_HIERARCHY = (
    "/sdcard/chummer-creation-authority-pending-timeout.xml"
)
CREATION_AUTHORITY_PENDING_TIMEOUT_TEXT_LIMIT = 1_000_000
PROGRESS_SCHEMA = "chummer.android.creation-prerequisite-progress/v2"
PROGRESS_FILE_NAME = "creation-prerequisite-progress.json"
PROGRESS_EVENTS_FILE_NAME = "creation-prerequisite-progress.jsonl"
CREATION_BOOTSTRAP_TIMING_PREFIX = "CHUMMER_CREATION_BOOTSTRAP_TIMING "
CREATION_BOOTSTRAP_TIMING_FILE_NAME = "creation-bootstrap-timing.json"
CREATION_BOOTSTRAP_LOGCAT_FILE_NAME = "creation-bootstrap-timing-logcat.txt"
CREATION_BOOTSTRAP_TIMING_LINE = re.compile(
    rf"^{re.escape(CREATION_BOOTSTRAP_TIMING_PREFIX)}(?P<payload>\{{.*\}})$"
)
CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER = "--------- beginning of main"
ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS = (
    "logcat",
    "-b",
    "main",
    "-v",
    "raw",
    "-T",
    "1",
    "-m",
    "1",
    "-e",
    r"^CHUMMER_CREATION_BOOTSTRAP_TIMING \{",
    "-s",
    "ChummerBootstrap:I",
    "*:S",
)
ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS = (
    "logcat",
    "-d",
    "-b",
    "main",
    "-v",
    "raw",
    "-s",
    "ChummerBootstrap:I",
    "*:S",
)
TOTAL_PERFORMANCE_TARGET_MS = 15 * 60 * 1000
INITIAL_NAVIGATION_MILESTONE_ORDER = (
    "app-cold-start-complete",
    "phone-shell-locale-complete",
    "dialog-acquisition-complete",
)
INITIAL_AUTHORITY_MILESTONE_ORDER = (
    "create-bootstrap-transaction-complete",
)
DASHBOARD_PROOF_MILESTONE_ORDER = (
    "dashboard-render-complete",
)
INITIAL_MILESTONE_ORDER = (
    *INITIAL_NAVIGATION_MILESTONE_ORDER,
    *INITIAL_AUTHORITY_MILESTONE_ORDER,
    *DASHBOARD_PROOF_MILESTONE_ORDER,
)
INITIAL_MILESTONE_PHASES = (
    *(["initial-navigation"] * len(INITIAL_NAVIGATION_MILESTONE_ORDER)),
    *(["initial-authority"] * len(INITIAL_AUTHORITY_MILESTONE_ORDER)),
    *(["dashboard-proof"] * len(DASHBOARD_PROOF_MILESTONE_ORDER)),
)
PHASE_BUDGET_MS = {
    "device-preflight-install": 180_000,
    # Cold start, locale evidence, and navigation to the explicit action remain
    # bounded without being charged to the product transaction.
    "initial-navigation": 60_000,
    "initial-authority": 90_000,
    # UIAutomator is an external observer. Its exact visible-dashboard proof is
    # independently bounded after the product has emitted the validated
    # workspace-publication and shell-sync timing receipt.
    "dashboard-proof": 30_000,
    # Exhaustive scroll inventories remain outside both the product transaction
    # and the visible-dashboard proof. They retain their own strict bound and
    # the unchanged 15-minute aggregate target; no authority field or stable-end
    # proof is removed.
    "authority-inventory": 90_000,
    "priority-ranks": 150_000,
    "typed-authority-options": 150_000,
    "talent-active-skill-grant": 150_000,
    "talent-active-preview": 150_000,
    "talent-skill-group-grant": 150_000,
    "preview-confirm": 150_000,
    "same-process-reopen": 90_000,
    "resources-preview-confirm": 150_000,
    "process-restart-reopen": 90_000,
}
PHASE_ORDER = tuple(PHASE_BUDGET_MS)
# Every phase and the aggregate clock are independently rounded to the nearest
# millisecond. Their worst-case opposing rounding errors are therefore
# ``(phase count + aggregate clock) / 2`` milliseconds. This reconciliation
# allowance is never a performance-budget allowance.
TIMING_ROUNDING_TOLERANCE_MS = (len(PHASE_ORDER) + 1) // 2


def accessibility_signature(
    nodes: list[shared.UiNode],
) -> tuple[tuple[str, ...], ...]:
    """Return one order-independent, duplicate-preserving viewport signature."""
    keys = (
        "resource-id",
        "class",
        "text",
        "content-desc",
        "enabled",
        "clickable",
        "checked",
        "bounds",
    )
    return tuple(sorted(tuple(node.attributes.get(key, "") for key in keys) for node in nodes))


def read_only_hierarchy_timed(
    device: shared.Device,
    durations_ms: list[int],
) -> list[shared.UiNode]:
    """Read the same UIAutomator XML in one non-mutating ADB round trip."""
    started = time.perf_counter()
    # Real ``Device`` instances always take the one-command read-only path.
    # Minimal contract fakes predating that API may expose only ``hierarchy``;
    # retaining that structural fallback keeps pure tests usable without
    # allowing the production driver to regress to a device-side dump file.
    reader = getattr(type(device), "read_only_hierarchy", None)
    nodes = device.read_only_hierarchy() if callable(reader) else device.hierarchy()
    durations_ms.append(round((time.perf_counter() - started) * 1000))
    return nodes


def fresh_hierarchy_timed(
    device: shared.Device,
    durations_ms: list[int],
) -> list[shared.UiNode]:
    """Acquire a post-gesture hierarchy through UIAutomator's dump file.

    API 36 can return an older viewport from the direct ``/dev/tty`` stream
    immediately after a swipe even though the rendered frame has moved.  The
    normal dump-to-file plus read path is slower, but it is the authority for
    every scroll-dependent inventory.  Busy-state polling deliberately keeps
    using :func:`read_only_hierarchy_timed` because it never changes viewport.
    """
    started = time.perf_counter()
    nodes = device.hierarchy()
    durations_ms.append(round((time.perf_counter() - started) * 1000))
    return nodes


def capture_creation_bootstrap_timing(
    device: shared.Device,
    *,
    logcat: str | None = None,
) -> dict[str, object]:
    """Capture the product-emitted, exact create/load/shell timing partition."""
    if logcat is None:
        result = device.run(
            *ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
            timeout=30,
        )
        logcat = str(result.stdout)
    device.evidence.mkdir(parents=True, exist_ok=True)
    (device.evidence / CREATION_BOOTSTRAP_LOGCAT_FILE_NAME).write_text(
        logcat,
        encoding="utf-8",
    )
    raw_lines, exact_matches, divider_count, invalid_lines = (
        classify_creation_bootstrap_logcat(logcat)
    )
    if len(exact_matches) != 1 or invalid_lines:
        device.capture("creation-bootstrap-timing-cardinality-invalid")
        raise RuntimeError(
            "Expected exactly one exact create bootstrap timing line with at most "
            "one canonical main-buffer divider before it, "
            f"found raw={len(raw_lines)}, exact={len(exact_matches)}, "
            f"dividers={divider_count}, invalid={len(invalid_lines)}"
        )
    try:
        timing = json.loads(exact_matches[0].group("payload"))
    except json.JSONDecodeError as error:
        device.capture("creation-bootstrap-timing-json-invalid")
        raise RuntimeError(
            "Creation bootstrap timing log contained invalid JSON"
        ) from error
    if not isinstance(timing, dict):
        raise RuntimeError("Creation bootstrap timing payload was not an object")
    required_literal = {
        "schema": "chummer.android.creation-bootstrap-timing/v1",
        "actionId": "create_character",
        "loadStartObserved": True,
        "workspaceStatePublished": True,
        "exactPublishedWorkspace": True,
        "reusedPresenterShellSync": True,
        "androidFullShellSyncMs": -1,
    }
    for key, expected in required_literal.items():
        if timing.get(key) != expected:
            device.capture("creation-bootstrap-timing-authority-invalid")
            raise RuntimeError(
                f"Creation bootstrap timing field {key!r} was not exact: "
                f"expected={expected!r}, actual={timing.get(key)!r}"
            )
    duration_fields = (
        "coreCreateMs",
        "presenterLoadMs",
        "presenterNavigationAndShellMs",
        "activeSectionMs",
        "androidRetainedRefreshMs",
        "processPendingOutputsMs",
        "totalMs",
    )
    for field in duration_fields:
        value = timing.get(field)
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise RuntimeError(
                f"Creation bootstrap timing field {field!r} was not a nonnegative integer"
            )
    partition_total = sum(int(timing[field]) for field in duration_fields[:-1])
    if abs(partition_total - int(timing["totalMs"])) > len(duration_fields) - 1:
        raise RuntimeError(
            "Creation bootstrap timing segments did not partition the product transaction: "
            f"segments={partition_total}, total={timing['totalMs']}"
        )
    timing_path = device.evidence / CREATION_BOOTSTRAP_TIMING_FILE_NAME
    timing_path.write_text(
        json.dumps(timing, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return timing


def classify_creation_bootstrap_logcat(
    logcat: str,
) -> tuple[
    list[str],
    list[re.Match[str]],
    int,
    list[str],
]:
    """Classify the narrow raw-logcat framing around one main-buffer marker.

    ``logcat -v raw`` still writes the canonical buffer divider before the
    first matching entry.  Both local commands are pinned to ``-b main``, so
    the only non-marker line allowed is one exact main-buffer divider before
    the marker.  Duplicate/late dividers and every other line remain invalid.
    """
    raw_lines = logcat.splitlines()
    exact_matches: list[re.Match[str]] = []
    invalid_lines: list[str] = []
    divider_count = 0
    marker_seen = False
    for line in raw_lines:
        match = CREATION_BOOTSTRAP_TIMING_LINE.fullmatch(line)
        if match is not None:
            exact_matches.append(match)
            marker_seen = True
        elif (
            line == CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER
            and divider_count == 0
            and not marker_seen
        ):
            divider_count = 1
        else:
            invalid_lines.append(line)
    return raw_lines, exact_matches, divider_count, invalid_lines


def wait_for_creation_bootstrap_timing_log(
    device: shared.Device,
    *,
    timeout: float = 90.0,
    observation_out: dict[str, object] | None = None,
) -> str:
    """Wait once for the post-action marker, then snapshot its full cardinality.

    A repeated ``logcat -d`` loop scans the growing device log buffer on every
    observation and competes with the creation loaders on a small hosted
    emulator.  The exact-tag stream blocks in one ADB process and exits after
    its first exact marker.  One subsequent bounded dump remains authoritative
    for duplicate/forged receipt rejection in
    :func:`capture_creation_bootstrap_timing`.
    """
    if timeout <= 0:
        raise ValueError("Creation bootstrap timing wait requires a positive timeout")
    deadline = time.monotonic() + timeout
    started = time.monotonic()
    read_durations_ms: list[int] = []
    last_logcat = ""

    def record(status: str) -> None:
        if observation_out is not None:
            observation_out.update({
                "scanId": "creation-bootstrap-timing-log-poll",
                "status": status,
                "logcatReadCount": len(read_durations_ms),
                "logcatElapsedMs": sum(read_durations_ms),
                "maximumLogcatReadMs": max(read_durations_ms, default=0),
                "observationMode": "single-bounded-stream-plus-snapshot",
                "streamLogcatReadCount": min(1, len(read_durations_ms)),
                "snapshotLogcatReadCount": max(0, len(read_durations_ms) - 1),
                "elapsedMs": round((time.monotonic() - started) * 1000),
            })

    def observe(
        arguments: tuple[str, ...],
        *,
        command_timeout: float,
        command_deadline: float | None = None,
    ) -> subprocess.CompletedProcess:
        read_started = time.perf_counter()
        try:
            options: dict[str, object] = {"timeout": command_timeout}
            if command_deadline is not None:
                options["deadline"] = command_deadline
            return device.run(*arguments, **options)
        finally:
            read_durations_ms.append(
                round((time.perf_counter() - read_started) * 1000)
            )

    try:
        streamed = observe(
            ADB_CREATION_BOOTSTRAP_LOGCAT_WAIT_ARGUMENTS,
            command_timeout=timeout,
            command_deadline=deadline,
        )
    except shared.AdbTransportError as error:
        if error.receipt.get("classification") != "timeout-unknown-outcome":
            raise
        failure = error.receipt.get("failure")
        if isinstance(failure, dict):
            last_logcat = str(failure.get("stdout", ""))
    except subprocess.TimeoutExpired as error:
        # Pure source-contract fakes may expose the subprocess timeout directly;
        # the real Device converts it to the exact fail-closed transport receipt
        # handled above.
        stdout = error.stdout
        if isinstance(stdout, bytes):
            last_logcat = stdout.decode("utf-8", errors="replace")
        elif stdout is not None:
            last_logcat = str(stdout)
    else:
        last_logcat = str(streamed.stdout)
        _, stream_exact, _, stream_invalid = classify_creation_bootstrap_logcat(
            last_logcat
        )
        if (
            len(stream_exact) == 1
            and not stream_invalid
            and time.monotonic() < deadline
        ):
            try:
                snapshot = observe(
                    ADB_CREATION_BOOTSTRAP_LOGCAT_SNAPSHOT_ARGUMENTS,
                    command_timeout=30,
                    command_deadline=deadline,
                )
            except shared.AdbTransportError as error:
                if error.receipt.get("classification") != "timeout-unknown-outcome":
                    raise
                failure = error.receipt.get("failure")
                if isinstance(failure, dict):
                    last_logcat = str(failure.get("stdout", ""))
            except subprocess.TimeoutExpired as error:
                stdout = error.stdout
                if isinstance(stdout, bytes):
                    last_logcat = stdout.decode("utf-8", errors="replace")
                elif stdout is not None:
                    last_logcat = str(stdout)
            else:
                last_logcat = str(snapshot.stdout)
                _, snapshot_exact, _, snapshot_invalid = (
                    classify_creation_bootstrap_logcat(last_logcat)
                )
                if (
                    snapshot_exact
                    and not snapshot_invalid
                    and time.monotonic() < deadline
                ):
                    record("resolved")
                    device.evidence.mkdir(parents=True, exist_ok=True)
                    (device.evidence / CREATION_BOOTSTRAP_LOGCAT_FILE_NAME).write_text(
                        last_logcat,
                        encoding="utf-8",
                    )
                    return last_logcat

    record("timeout")
    device.evidence.mkdir(parents=True, exist_ok=True)
    (device.evidence / CREATION_BOOTSTRAP_LOGCAT_FILE_NAME).write_text(
        last_logcat,
        encoding="utf-8",
    )
    device.capture("creation-bootstrap-timing-log-timeout")
    raise RuntimeError(
        "Timed out waiting for the exact post-action creation bootstrap timing marker"
    )


def clear_creation_bootstrap_timing_log(device: shared.Device) -> None:
    """Remove stale log authority immediately before the create mutation.

    The clear is deliberately a one-shot, non-replayable ADB command.  A marker
    observed by the following exact-tag poll must therefore have been emitted by
    the create action under proof, never by an earlier app process or journey.
    """
    device.run(*shared.ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS, timeout=30)


def hierarchy_timing_fields(durations_ms: list[int]) -> dict[str, int]:
    return {
        "hierarchyReadCount": len(durations_ms),
        "hierarchyElapsedMs": sum(durations_ms),
        "maximumHierarchyReadMs": max(durations_ms, default=0),
    }


COMPOSED_SCAN_TIMING_FIELDS = (
    "originElapsedMs",
    "originReverseSwipes",
    "originEmptyHierarchyReads",
    "originHierarchyReadCount",
    "originHierarchyElapsedMs",
    "originMaximumHierarchyReadMs",
    "traversalElapsedMs",
    "traversalEmptyHierarchyReads",
    "emptyHierarchyReads",
    "totalNavigationSwipes",
    "hierarchyReadCount",
    "hierarchyElapsedMs",
    "maximumHierarchyReadMs",
    "elapsedMs",
    "swipes",
)
COMPOSED_SCAN_TIMING_TRIGGER_FIELDS = (
    "reusedInitialScreen",
    "originElapsedMs",
    "originReverseSwipes",
    "originEmptyHierarchyReads",
    "originHierarchyReadCount",
    "originHierarchyElapsedMs",
    "originMaximumHierarchyReadMs",
    "traversalElapsedMs",
    "traversalEmptyHierarchyReads",
    "totalNavigationSwipes",
)
COMPOSED_SCAN_FORWARD_STATUSES = frozenset({
    "stable-end",
    "bound-exhausted",
    "empty-hierarchy-exhausted",
})


def require_composed_scan_timing(scan: dict[str, object]) -> None:
    """Fail closed unless a reused-origin scan's clocks reconcile exactly.

    Each hierarchy duration and the enclosing monotonic intervals are rounded
    independently.  The per-part read-count tolerance below accounts only for
    those opposing half-millisecond rounding errors; it is not a phase-budget
    allowance.  Receipts without composed origin/traversal fields are separate
    polling observations and remain outside this contract.
    """
    composed = (
        scan.get("status") in COMPOSED_SCAN_FORWARD_STATUSES
        or any(field in scan for field in COMPOSED_SCAN_TIMING_TRIGGER_FIELDS)
    )
    if not composed:
        return
    missing = [field for field in COMPOSED_SCAN_TIMING_FIELDS if field not in scan]
    if missing:
        raise RuntimeError(
            f"Composed accessibility scan timing omitted fields: {missing!r}"
        )
    invalid = [
        field
        for field in COMPOSED_SCAN_TIMING_FIELDS
        if type(scan[field]) is not int or int(scan[field]) < 0
    ]
    if invalid:
        raise RuntimeError(
            f"Composed accessibility scan timing was not nonnegative integer data: {invalid!r}"
        )

    value = {field: int(scan[field]) for field in COMPOSED_SCAN_TIMING_FIELDS}
    reused = scan.get("reusedInitialScreen")
    if type(reused) is not bool:
        raise RuntimeError("Composed accessibility scan timing omitted its reuse decision")
    traversal_reads = (
        value["hierarchyReadCount"] - value["originHierarchyReadCount"]
    )
    traversal_hierarchy_ms = (
        value["hierarchyElapsedMs"] - value["originHierarchyElapsedMs"]
    )
    origin_rounding_ms = (value["originHierarchyReadCount"] + 1) // 2
    traversal_rounding_ms = (traversal_reads + 1) // 2
    origin_maximum_lower_bound = (
        (
            value["originHierarchyElapsedMs"]
            + value["originHierarchyReadCount"]
            - 1
        )
        // value["originHierarchyReadCount"]
        if value["originHierarchyReadCount"] > 0
        else 0
    )
    traversal_maximum_lower_bound = (
        (traversal_hierarchy_ms + traversal_reads - 1) // traversal_reads
        if traversal_reads > 0
        else 0
    )
    relationships_hold = (
        traversal_reads >= 0
        and traversal_hierarchy_ms >= 0
        and value["elapsedMs"]
        == value["originElapsedMs"] + value["traversalElapsedMs"]
        and value["emptyHierarchyReads"]
        == value["originEmptyHierarchyReads"]
        + value["traversalEmptyHierarchyReads"]
        and value["totalNavigationSwipes"]
        == value["originReverseSwipes"] + value["swipes"]
        and value["originEmptyHierarchyReads"]
        <= value["originHierarchyReadCount"]
        and value["traversalEmptyHierarchyReads"] <= traversal_reads
        and value["originHierarchyElapsedMs"]
        <= value["originElapsedMs"] + origin_rounding_ms
        and traversal_hierarchy_ms
        <= value["traversalElapsedMs"] + traversal_rounding_ms
        and value["originMaximumHierarchyReadMs"]
        <= value["maximumHierarchyReadMs"]
        and value["maximumHierarchyReadMs"] <= value["hierarchyElapsedMs"]
        and value["originMaximumHierarchyReadMs"]
        <= value["originHierarchyElapsedMs"]
        and (
            (
                value["originHierarchyReadCount"] > 0
                and value["originMaximumHierarchyReadMs"]
                >= origin_maximum_lower_bound
            )
            or (
                value["originHierarchyReadCount"] == 0
                and value["originHierarchyElapsedMs"] == 0
                and value["originMaximumHierarchyReadMs"] == 0
            )
        )
        and (
            (
                traversal_reads > 0
                and value["maximumHierarchyReadMs"]
                >= traversal_maximum_lower_bound
            )
            or (traversal_reads == 0 and traversal_hierarchy_ms == 0)
        )
        and (
            value["hierarchyReadCount"] > 0
            or (
                value["hierarchyReadCount"] == 0
                and value["hierarchyElapsedMs"] == 0
                and value["maximumHierarchyReadMs"] == 0
            )
        )
        and value["maximumHierarchyReadMs"]
        <= max(
            value["originMaximumHierarchyReadMs"],
            traversal_hierarchy_ms,
        )
        and (
            reused
            and value["originHierarchyReadCount"]
            >= value["originEmptyHierarchyReads"]
            + value["originReverseSwipes"]
            + 1
            or not reused
            and all(
                value[field] == 0
                for field in (
                    "originElapsedMs",
                    "originReverseSwipes",
                    "originEmptyHierarchyReads",
                    "originHierarchyReadCount",
                    "originHierarchyElapsedMs",
                    "originMaximumHierarchyReadMs",
                )
            )
        )
    )
    if not relationships_hold:
        raise RuntimeError(
            "Composed accessibility scan timing did not reconcile its origin, "
            "traversal, hierarchy, empty-read, and navigation partitions"
        )


class PriorityRankOrigin(NamedTuple):
    nodes: list[shared.UiNode]
    reverse_swipes: int
    elapsed_ms: int
    hierarchy_durations_ms: tuple[int, ...]
    empty_hierarchy_reads: int


def require_reusable_scan_origin(origin: PriorityRankOrigin) -> None:
    if (
        not origin.nodes
        or type(origin.reverse_swipes) is not int
        or type(origin.elapsed_ms) is not int
        or type(origin.empty_hierarchy_reads) is not int
        or not origin.hierarchy_durations_ms
        or any(type(value) is not int for value in origin.hierarchy_durations_ms)
        or origin.reverse_swipes < 0
        or origin.reverse_swipes > 8
        or origin.elapsed_ms < 0
        or origin.empty_hierarchy_reads < 0
        or any(value < 0 for value in origin.hierarchy_durations_ms)
        or len(origin.hierarchy_durations_ms)
        < origin.empty_hierarchy_reads + origin.reverse_swipes + 1
        or origin.empty_hierarchy_reads > len(origin.hierarchy_durations_ms)
        or origin.elapsed_ms + (len(origin.hierarchy_durations_ms) + 1) // 2
        < sum(origin.hierarchy_durations_ms)
    ):
        raise ValueError("A reused initial scan observation must carry exact nonnegative timing")


def acquire_stable_start_origin(
    device: shared.Device,
    *,
    scan_id: str,
    max_reverse_swipes: int,
    distance_ratio: float,
    stable_repeats: int = 2,
    max_consecutive_empty_reads: int = 3,
    delay_seconds: float = 0.0,
) -> PriorityRankOrigin:
    """Prove a measured page start and retain its fresh final hierarchy.

    The baseline and every post-gesture observation use the file-backed fresh
    hierarchy path. The returned origin carries that acquisition timing exactly
    once for the composed forward-scan receipt; no separate scan is recorded.
    """
    if (
        not scan_id
        or max_reverse_swipes < stable_repeats
        or max_reverse_swipes > 8
        or stable_repeats < 1
        or max_consecutive_empty_reads < 0
        or delay_seconds < 0
    ):
        raise ValueError(
            "A named stable-start scan with bounded swipes, repeats, and empty reads is required"
        )
    started = time.monotonic()
    hierarchy_durations_ms: list[int] = []
    empty_hierarchy_reads = 0
    previous: tuple[tuple[str, ...], ...] | None = None
    unchanged = 0
    reverse_swipes = 0
    consecutive_empty_reads = 0

    while reverse_swipes <= max_reverse_swipes:
        nodes = fresh_hierarchy_timed(device, hierarchy_durations_ms)
        if not nodes:
            consecutive_empty_reads += 1
            empty_hierarchy_reads += 1
            if consecutive_empty_reads > max_consecutive_empty_reads:
                device.capture(f"{scan_id}-empty-hierarchy-exhausted")
                raise RuntimeError(
                    f"Accessibility reverse scan {scan_id!r} exhausted transient empty "
                    "hierarchy reads"
                )
            time.sleep(0.75)
            continue
        consecutive_empty_reads = 0
        signature = accessibility_signature(nodes)
        unchanged = unchanged + 1 if previous is not None and signature == previous else 0
        previous = signature
        if unchanged >= stable_repeats:
            elapsed_ms = round((time.monotonic() - started) * 1000)
            origin = PriorityRankOrigin(
                nodes=nodes,
                reverse_swipes=reverse_swipes,
                elapsed_ms=elapsed_ms,
                hierarchy_durations_ms=tuple(hierarchy_durations_ms),
                empty_hierarchy_reads=empty_hierarchy_reads,
            )
            require_reusable_scan_origin(origin)
            return origin
        if reverse_swipes >= max_reverse_swipes:
            break
        device.swipe_down(distance_ratio=distance_ratio)
        reverse_swipes += 1
        if delay_seconds > 0:
            time.sleep(delay_seconds)

    device.capture(f"{scan_id}-stable-start-unproven")
    raise RuntimeError(
        f"Accessibility reverse scan {scan_id!r} did not prove a stable page start "
        f"within {max_reverse_swipes} swipes"
    )


def scan_forward_until_stable(
    device: shared.Device,
    *,
    scan_id: str,
    max_scrolls: int,
    distance_ratio: float,
    initial_observation: PriorityRankOrigin | None = None,
    stable_repeats: int = 2,
    max_consecutive_empty_reads: int = 3,
    delay_seconds: float = 0.2,
    observer: Callable[[dict[str, object]], None] | None = None,
) -> list[list[shared.UiNode]]:
    """Scan through the exact stable page end instead of spending the full bound.

    Two unchanged, full accessibility signatures after forward swipes prove that
    the native viewport stopped moving. Exhausting the configured bound without
    that proof fails closed, so early termination cannot hide a later duplicate.
    """
    if (
        not scan_id
        or max_scrolls < stable_repeats
        or stable_repeats < 1
        or max_consecutive_empty_reads < 0
    ):
        raise ValueError("A named scan with enough scroll budget for stable-end proof is required")
    started = time.monotonic()
    screens: list[list[shared.UiNode]] = []
    previous: tuple[tuple[str, ...], ...] | None = None
    unchanged = 0
    swipes = 0
    consecutive_empty_reads = 0
    total_empty_reads = 0
    hierarchy_durations_ms: list[int] = []
    if initial_observation is not None:
        require_reusable_scan_origin(initial_observation)
    reused_initial_screen = initial_observation is not None
    pending_initial_screen = (
        initial_observation.nodes if initial_observation is not None else None
    )
    origin_durations_ms = list(
        initial_observation.hierarchy_durations_ms
        if initial_observation is not None
        else ()
    )
    origin_elapsed_ms = (
        initial_observation.elapsed_ms if initial_observation is not None else 0
    )
    origin_reverse_swipes = (
        initial_observation.reverse_swipes if initial_observation is not None else 0
    )
    origin_empty_hierarchy_reads = (
        initial_observation.empty_hierarchy_reads
        if initial_observation is not None
        else 0
    )

    def timing_receipt() -> dict[str, int]:
        traversal_elapsed_ms = round((time.monotonic() - started) * 1000)
        combined_durations = [*origin_durations_ms, *hierarchy_durations_ms]
        return {
            "originElapsedMs": origin_elapsed_ms,
            "originReverseSwipes": origin_reverse_swipes,
            "originEmptyHierarchyReads": origin_empty_hierarchy_reads,
            "originHierarchyReadCount": len(origin_durations_ms),
            "originHierarchyElapsedMs": sum(origin_durations_ms),
            "originMaximumHierarchyReadMs": max(origin_durations_ms, default=0),
            "traversalElapsedMs": traversal_elapsed_ms,
            "traversalEmptyHierarchyReads": total_empty_reads,
            "emptyHierarchyReads": origin_empty_hierarchy_reads + total_empty_reads,
            "totalNavigationSwipes": origin_reverse_swipes + swipes,
            **hierarchy_timing_fields(combined_durations),
            "elapsedMs": origin_elapsed_ms + traversal_elapsed_ms,
        }
    while swipes <= max_scrolls:
        if pending_initial_screen is not None:
            nodes = pending_initial_screen
            pending_initial_screen = None
        else:
            nodes = fresh_hierarchy_timed(device, hierarchy_durations_ms)
        if not nodes:
            consecutive_empty_reads += 1
            total_empty_reads += 1
            if consecutive_empty_reads > max_consecutive_empty_reads:
                result = {
                    "scanId": scan_id,
                    "status": "empty-hierarchy-exhausted",
                    "screens": len(screens),
                    "swipes": swipes,
                    "configuredMaxScrolls": max_scrolls,
                    "stableRepeats": stable_repeats,
                    "emptyHierarchyReads": total_empty_reads,
                    "maximumConsecutiveEmptyReads": max_consecutive_empty_reads,
                    "reusedInitialScreen": reused_initial_screen,
                    **timing_receipt(),
                }
                if observer is not None:
                    observer(result)
                device.capture(f"{scan_id}-empty-hierarchy-exhausted")
                raise RuntimeError(
                    f"Accessibility scan {scan_id!r} exhausted transient empty hierarchy reads"
                )
            time.sleep(0.75)
            continue
        consecutive_empty_reads = 0
        screens.append(nodes)
        signature = accessibility_signature(nodes)
        unchanged = unchanged + 1 if previous is not None and signature == previous else 0
        previous = signature
        if unchanged >= stable_repeats:
            result = {
                "scanId": scan_id,
                "status": "stable-end",
                "screens": len(screens),
                "swipes": swipes,
                "configuredMaxScrolls": max_scrolls,
                "stableRepeats": stable_repeats,
                "emptyHierarchyReads": total_empty_reads,
                "maximumConsecutiveEmptyReads": max_consecutive_empty_reads,
                "reusedInitialScreen": reused_initial_screen,
                **timing_receipt(),
            }
            if observer is not None:
                observer(result)
            return screens
        if swipes >= max_scrolls:
            break
        device.swipe_up(distance_ratio=distance_ratio)
        swipes += 1
        if delay_seconds > 0:
            time.sleep(delay_seconds)
    result = {
        "scanId": scan_id,
        "status": "bound-exhausted",
        "screens": len(screens),
        "swipes": swipes,
        "configuredMaxScrolls": max_scrolls,
        "stableRepeats": stable_repeats,
        "emptyHierarchyReads": total_empty_reads,
        "maximumConsecutiveEmptyReads": max_consecutive_empty_reads,
        "reusedInitialScreen": reused_initial_screen,
        **timing_receipt(),
    }
    if observer is not None:
        observer(result)
    device.capture(f"{scan_id}-stable-end-unproven")
    raise RuntimeError(
        f"Accessibility scan {scan_id!r} did not prove a stable page end within "
        f"{max_scrolls} forward swipes"
    )


class StableViewportScan(NamedTuple):
    screens: list[list[shared.UiNode]]
    swipes: int


def scan_forward_with_receipt(
    device: shared.Device,
    *,
    scan_id: str,
    max_scrolls: int,
    distance_ratio: float,
    initial_observation: PriorityRankOrigin | None = None,
    delay_seconds: float = 0.2,
    observer: Callable[[dict[str, object]], None] | None = None,
) -> StableViewportScan:
    """Return the stable scan's actual viewport delta without another dump."""
    receipt: dict[str, object] = {}

    def record(value: dict[str, object]) -> None:
        receipt.update(value)
        if observer is not None:
            observer(value)

    screens = scan_forward_until_stable(
        device,
        scan_id=scan_id,
        max_scrolls=max_scrolls,
        distance_ratio=distance_ratio,
        initial_observation=initial_observation,
        delay_seconds=delay_seconds,
        observer=record,
    )
    swipes = receipt.get("swipes")
    stable_repeats = receipt.get("stableRepeats")
    if (
        receipt.get("status") != "stable-end"
        or type(swipes) is not int
        or type(stable_repeats) is not int
        or swipes < stable_repeats
    ):
        raise RuntimeError(f"Accessibility scan {scan_id!r} emitted no stable swipe receipt")
    # Stable-end proof deliberately spends ``stable_repeats`` gestures against
    # the clamped page end. They are evidence, not viewport movement. Callers
    # restoring a measured node must reverse only the successful movement delta.
    return StableViewportScan(screens, swipes - stable_repeats)


def rewind_to_stable_start(
    device: shared.Device,
    *,
    scan_id: str,
    max_scrolls: int,
    distance_ratio: float,
    stable_repeats: int = 2,
    observer: Callable[[dict[str, object]], None] | None = None,
) -> int:
    """Prove the page start and return only successful reverse movement."""
    if not scan_id or max_scrolls < stable_repeats or stable_repeats < 1:
        raise ValueError("A named reverse scan with a stable-start budget is required")
    started = time.monotonic()
    previous: tuple[tuple[str, ...], ...] | None = None
    unchanged = 0
    swipes = 0
    screens = 0
    hierarchy_durations_ms: list[int] = []
    while swipes <= max_scrolls:
        nodes = fresh_hierarchy_timed(device, hierarchy_durations_ms)
        if not nodes:
            time.sleep(0.75)
            continue
        screens += 1
        signature = accessibility_signature(nodes)
        unchanged = unchanged + 1 if previous is not None and signature == previous else 0
        previous = signature
        if unchanged >= stable_repeats:
            result = {
                "scanId": scan_id,
                "status": "stable-start",
                "screens": screens,
                "swipes": swipes,
                "movementSwipes": swipes - stable_repeats,
                "configuredMaxScrolls": max_scrolls,
                "stableRepeats": stable_repeats,
                **hierarchy_timing_fields(hierarchy_durations_ms),
                "elapsedMs": round((time.monotonic() - started) * 1000),
            }
            if observer is not None:
                observer(result)
            return swipes - stable_repeats
        if swipes >= max_scrolls:
            break
        device.swipe_down(distance_ratio=distance_ratio)
        swipes += 1
        time.sleep(0.2)
    result = {
        "scanId": scan_id,
        "status": "stable-start-bound-exhausted",
        "screens": screens,
        "swipes": swipes,
        "configuredMaxScrolls": max_scrolls,
        "stableRepeats": stable_repeats,
        **hierarchy_timing_fields(hierarchy_durations_ms),
        "elapsedMs": round((time.monotonic() - started) * 1000),
    }
    if observer is not None:
        observer(result)
    device.capture(f"{scan_id}-stable-start-unproven")
    raise RuntimeError(
        f"Accessibility reverse scan {scan_id!r} did not prove a stable page start "
        f"within {max_scrolls} swipes"
    )


def move_between_measured_viewports(
    device: shared.Device,
    current_viewport: int,
    target_viewport: int,
    *,
    distance_ratio: float = 0.68,
    delay_seconds: float = 0.2,
) -> int:
    """Move an exact scan-proven delta without hierarchy churn between endpoints."""
    if current_viewport < 0 or target_viewport < 0:
        raise ValueError("Measured viewport indexes must be nonnegative")
    if target_viewport < current_viewport:
        for _ in range(current_viewport - target_viewport):
            device.swipe_down(distance_ratio=distance_ratio)
            if delay_seconds > 0:
                time.sleep(delay_seconds)
    else:
        for _ in range(target_viewport - current_viewport):
            device.swipe_up(distance_ratio=distance_ratio)
            if delay_seconds > 0:
                time.sleep(delay_seconds)
    return target_viewport


def measured_reverse_reacquisition_bound(
    current_viewport: int,
    target_viewport: int,
    *,
    maximum_viewport: int = 18,
) -> int:
    """Bound exact-node compensation by the forward scan's measured delta.

    ``swipe_down`` is capped by the device viewport and therefore does not
    exactly invert a same-ratio ``swipe_up``.  After the fast measured move,
    allow at most the same proven delta again while checking a fresh hierarchy
    after every smaller reverse gesture. A node already at the scan-proven
    tappable target authorizes no gesture, and no unmeasured fixed reset is
    authorized.
    """
    if (
        type(current_viewport) is not int
        or type(target_viewport) is not int
        or current_viewport < 0
        or target_viewport < 0
        or target_viewport > current_viewport
        or type(maximum_viewport) is not int
        or maximum_viewport < 0
        or current_viewport > maximum_viewport
    ):
        raise ValueError(
            "Exact-node reacquisition requires integer viewports ordered within "
            f"0..{maximum_viewport!r}"
        )
    return current_viewport - target_viewport


def wait_for_priority_rank_origin(
    device: shared.Device,
    category: str,
    *,
    timeout: float = 45.0,
    max_reverse_swipes: int = 8,
    distance_ratio: float = 0.68,
) -> PriorityRankOrigin:
    """Bind the pushed category route and its exact Rank-A origin together.

    The old physical proof dumped the same unchanged viewport three times: once
    for the route marker, once while rewinding to Rank A, and once as the first
    cardinality-scan screen.  This combined acquisition retains exact route and
    Rank-A cardinality, uses only fresh post-gesture hierarchies, and returns the
    authoritative viewport for direct reuse by the stable-end scan.
    """
    if category not in CATEGORIES or timeout <= 0 or max_reverse_swipes < 0:
        raise ValueError("A supported category and bounded rank-origin search are required")
    route_selector = "creation-prerequisite-category-page"
    rank_selector = f"creation-prerequisite-rank-{category}-a"
    started = time.monotonic()
    deadline = time.monotonic() + timeout
    reverse_swipes = 0
    empty_hierarchy_reads = 0
    hierarchy_durations_ms: list[int] = []
    while time.monotonic() < deadline:
        nodes = fresh_hierarchy_timed(device, hierarchy_durations_ms)
        if not nodes:
            empty_hierarchy_reads += 1
            time.sleep(0.75)
            continue
        route_matches = [
            node for node in nodes if _exact_resource_id(node) == route_selector
        ]
        if len(route_matches) > 1:
            device.capture(f"creation-prerequisite-{category}-category-route-cardinality-invalid")
            raise RuntimeError(
                f"{category} priority category route {route_selector!r} has "
                f"cardinality {len(route_matches)}"
            )
        rank_matches = [
            node for node in nodes if _exact_resource_id(node) == rank_selector
        ]
        if len(rank_matches) > 1:
            device.capture(f"creation-prerequisite-{category}-rank-origin-cardinality-invalid")
            raise RuntimeError(
                f"{category} rank scan origin {rank_selector!r} has cardinality "
                f"{len(rank_matches)}"
            )
        if len(route_matches) == 1 and len(rank_matches) == 1:
            node = rank_matches[0]
            if not device.node_has_tappable_bounds(node):
                device.capture(f"creation-prerequisite-{category}-rank-origin-not-visible")
                raise RuntimeError(
                    f"{category} rank scan origin {rank_selector!r} was not visible"
                )
            return PriorityRankOrigin(
                nodes=nodes,
                reverse_swipes=reverse_swipes,
                elapsed_ms=round((time.monotonic() - started) * 1000),
                hierarchy_durations_ms=tuple(hierarchy_durations_ms),
                empty_hierarchy_reads=empty_hierarchy_reads,
            )
        if device.dismiss_system_ui_anr(nodes):
            time.sleep(2)
            continue
        if len(route_matches) == 1 and reverse_swipes < max_reverse_swipes:
            # ``input swipe`` is synchronous and the immediately following
            # dump is the post-gesture authority; no blind fixed sleep is
            # needed between those two bounded operations.
            device.swipe_down(distance_ratio=distance_ratio)
            reverse_swipes += 1
            continue
        time.sleep(0.25)
    device.capture(f"creation-prerequisite-{category}-rank-origin-unavailable")
    raise RuntimeError(
        f"Timed out acquiring exact {category} priority category route and "
        f"rank origin {rank_selector!r} within {max_reverse_swipes} reverse swipes"
    )


def rewind_to_exact_resource_id(
    device: shared.Device,
    selector: str,
    *,
    max_swipes: int,
    distance_ratio: float,
    evidence_prefix: str,
    surface_name: str,
    require_tappable: bool,
    max_empty_hierarchy_reads: int = 3,
    max_system_ui_dismissals: int = 3,
) -> tuple[shared.UiNode, int]:
    """Reverse only as far as needed, observing one hierarchy per viewport."""
    if (
        type(max_swipes) is not int
        or max_swipes < 0
        or type(max_empty_hierarchy_reads) is not int
        or max_empty_hierarchy_reads < 0
        or type(max_system_ui_dismissals) is not int
        or max_system_ui_dismissals < 0
    ):
        raise ValueError(
            "Exact integer gesture, empty-hierarchy, and system-UI bounds are required"
        )
    reverse_swipes = 0
    empty_hierarchy_reads = 0
    system_ui_dismissals = 0
    while reverse_swipes <= max_swipes:
        nodes = fresh_hierarchy_timed(device, [])
        if not nodes:
            empty_hierarchy_reads += 1
            if empty_hierarchy_reads > max_empty_hierarchy_reads:
                device.capture(f"{evidence_prefix}-empty-hierarchy-exhausted")
                raise RuntimeError(
                    f"{surface_name} exhausted its separate transient empty-hierarchy "
                    f"budget of {max_empty_hierarchy_reads} reads"
                )
            time.sleep(0.75)
            continue
        matches = [
            node
            for node in nodes
            if _exact_resource_id(node) == selector
        ]
        if len(matches) > 1:
            device.capture(f"{evidence_prefix}-cardinality-invalid")
            raise RuntimeError(
                f"{surface_name} {selector!r} has cardinality {len(matches)}; expected one"
            )
        if len(matches) == 1:
            node = matches[0]
            visible = device.node_has_tappable_bounds(node)
            interactive = (
                node.attributes.get("enabled") == "true"
                and node.attributes.get("clickable") == "true"
            )
            if visible and (interactive or not require_tappable):
                return node, reverse_swipes
            if (
                not visible
                and (interactive or not require_tappable)
                and reverse_swipes < max_swipes
            ):
                device.swipe_down(distance_ratio=distance_ratio)
                reverse_swipes += 1
                time.sleep(0.2)
                continue
            device.capture(f"{evidence_prefix}-not-tappable")
            raise RuntimeError(
                f"{surface_name} {selector!r} was not visible"
                + (", enabled, and clickable" if require_tappable else "")
            )
        if device.dismiss_system_ui_anr(nodes):
            system_ui_dismissals += 1
            if system_ui_dismissals > max_system_ui_dismissals:
                device.capture(f"{evidence_prefix}-system-ui-exhausted")
                raise RuntimeError(
                    f"{surface_name} exhausted its separate system-UI dismissal "
                    f"budget of {max_system_ui_dismissals}"
                )
            time.sleep(2)
            continue
        if reverse_swipes >= max_swipes:
            break
        device.swipe_down(distance_ratio=distance_ratio)
        reverse_swipes += 1
        time.sleep(0.2)
    device.capture(f"{evidence_prefix}-unavailable")
    raise RuntimeError(
        f"Timed out reversing to exact {surface_name.lower()} {selector!r} "
        f"within the scan-proven {max_swipes}-swipe bound"
    )


class ProgressRecorder:
    """Deterministic phase events plus atomic timing evidence for a physical run."""

    def __init__(self, evidence_root: Path) -> None:
        self.evidence_path = evidence_root.resolve() / PROGRESS_FILE_NAME
        self.events_path = evidence_root.resolve() / PROGRESS_EVENTS_FILE_NAME
        self.started = time.monotonic()
        self.phases: list[dict[str, object]] = []
        self.scans: list[dict[str, object]] = []
        self.milestones: list[dict[str, object]] = []
        self.events: list[dict[str, object]] = []
        self._active_id: str | None = None
        self._active_started = 0.0
        self._finished = False

    def advance(self, phase_id: str) -> None:
        if phase_id not in PHASE_BUDGET_MS:
            raise RuntimeError(f"Unknown prerequisite progress phase {phase_id!r}")
        expected_index = len(self.phases) + (1 if self._active_id is not None else 0)
        expected = PHASE_ORDER[expected_index] if expected_index < len(PHASE_ORDER) else None
        if self._finished or phase_id != expected:
            raise RuntimeError(
                f"Expected prerequisite progress phase {expected!r}, got {phase_id!r}"
            )
        self._close_active("pass")
        self._active_id = phase_id
        self._active_started = time.monotonic()
        self._emit({
            "schema": PROGRESS_SCHEMA,
            "event": "phase-start",
            "ordinal": len(self.phases) + 1,
            "phaseId": phase_id,
        })
        self._write("running")

    def record_scan(self, scan: dict[str, object]) -> None:
        if self._active_id is None or self._finished:
            raise RuntimeError("Scan timing was recorded outside an active progress phase")
        require_composed_scan_timing(scan)
        self.scans.append({**scan, "phaseId": self._active_id})
        self._write("running")

    def record_initial_milestone(self, milestone_id: str) -> None:
        """Emit ordered navigation, product, and observer timing segments."""
        expected_index = len(self.milestones)
        expected = (
            INITIAL_MILESTONE_ORDER[expected_index]
            if expected_index < len(INITIAL_MILESTONE_ORDER)
            else None
        )
        navigation_end = len(INITIAL_NAVIGATION_MILESTONE_ORDER)
        authority_end = navigation_end + len(INITIAL_AUTHORITY_MILESTONE_ORDER)
        expected_phase = (
            "initial-navigation"
            if expected_index < navigation_end
            else "initial-authority"
            if expected_index < authority_end
            else "dashboard-proof"
        )
        if self._active_id != expected_phase or self._finished:
            raise RuntimeError(
                f"Initial milestone {milestone_id!r} was recorded outside its "
                f"active phase {expected_phase!r}"
            )
        if milestone_id != expected:
            raise RuntimeError(
                f"Expected initial milestone {expected!r}, got {milestone_id!r}"
            )
        phase_elapsed = round((time.monotonic() - self._active_started) * 1000)
        total_elapsed = round((time.monotonic() - self.started) * 1000)
        previous = self.milestones[-1] if self.milestones else None
        previous_elapsed = (
            int(previous["phaseElapsedMs"])
            if previous is not None and previous["phaseId"] == self._active_id
            else 0
        )
        milestone = {
            "milestoneId": milestone_id,
            "phaseId": self._active_id,
            "ordinal": expected_index + 1,
            "phaseElapsedMs": phase_elapsed,
            "segmentElapsedMs": phase_elapsed - previous_elapsed,
            "totalElapsedMs": total_elapsed,
        }
        self.milestones.append(milestone)
        self._emit({
            "schema": PROGRESS_SCHEMA,
            "event": "phase-milestone",
            **milestone,
        })
        self._write("running")

    def finish(self) -> dict[str, object]:
        if self._finished:
            raise RuntimeError("Prerequisite progress was already finalized")
        self._close_active("pass")
        completed = tuple(phase.get("phaseId") for phase in self.phases)
        if completed != PHASE_ORDER or len(self.phases) != len(PHASE_ORDER):
            raise RuntimeError(
                f"Prerequisite progress is incomplete: expected={PHASE_ORDER!r}, "
                f"actual={completed!r}"
            )
        phase_elapsed_ms: list[int] = []
        for ordinal, (phase_id, budget_ms) in enumerate(PHASE_BUDGET_MS.items(), start=1):
            phase = self.phases[ordinal - 1]
            elapsed_ms = phase.get("elapsedMs")
            if (
                type(phase.get("ordinal")) is not int
                or phase.get("ordinal") != ordinal
                or phase.get("phaseId") != phase_id
                or phase.get("status") != "pass"
                or type(phase.get("budgetMs")) is not int
                or phase.get("budgetMs") != budget_ms
                or phase.get("withinBudget") is not True
                or type(elapsed_ms) is not int
                or elapsed_ms < 0
                or elapsed_ms > budget_ms
            ):
                raise RuntimeError(
                    f"Prerequisite progress phase evidence differs: {phase_id!r}"
                )
            phase_elapsed_ms.append(elapsed_ms)
        snapshot = self.snapshot("timing-complete")
        total_elapsed_ms = snapshot.get("totalElapsedMs")
        if type(total_elapsed_ms) is not int or total_elapsed_ms < 0:
            raise RuntimeError("Prerequisite progress total elapsed time is invalid")
        if sum(phase_elapsed_ms) > total_elapsed_ms + TIMING_ROUNDING_TOLERANCE_MS:
            raise RuntimeError(
                "Prerequisite progress phase elapsed time exceeds its contiguous total: "
                f"phaseSumMs={sum(phase_elapsed_ms)}, totalElapsedMs={total_elapsed_ms}, "
                f"roundingToleranceMs={TIMING_ROUNDING_TOLERANCE_MS}"
            )
        actual_milestones = tuple(
            (
                milestone.get("milestoneId"),
                milestone.get("phaseId"),
                milestone.get("ordinal"),
            )
            for milestone in self.milestones
        )
        expected_milestones = tuple(
            (milestone_id, phase_id, ordinal)
            for ordinal, (milestone_id, phase_id) in enumerate(
                zip(INITIAL_MILESTONE_ORDER, INITIAL_MILESTONE_PHASES, strict=True),
                start=1,
            )
        )
        if actual_milestones != expected_milestones or any(
            type(milestone.get("ordinal")) is not int
            for milestone in self.milestones
        ):
            raise RuntimeError(
                "Prerequisite progress milestone evidence differs: "
                f"expected={expected_milestones!r}, actual={actual_milestones!r}"
            )
        phase_index_by_id = {
            phase_id: index
            for index, phase_id in enumerate(PHASE_ORDER)
        }
        previous_phase_elapsed: dict[str, int] = {}
        previous_total_elapsed = -1
        for milestone, (milestone_id, phase_id, _) in zip(
            self.milestones,
            expected_milestones,
            strict=True,
        ):
            phase_elapsed = milestone.get("phaseElapsedMs")
            segment_elapsed = milestone.get("segmentElapsedMs")
            milestone_total_elapsed = milestone.get("totalElapsedMs")
            preceding_elapsed = sum(
                phase_elapsed_ms[:phase_index_by_id[phase_id]]
            )
            minimum_total_elapsed = preceding_elapsed + (
                phase_elapsed if type(phase_elapsed) is int else 0
            )
            if (
                type(phase_elapsed) is not int
                or type(segment_elapsed) is not int
                or type(milestone_total_elapsed) is not int
                or phase_elapsed < 0
                or phase_elapsed > phase_elapsed_ms[phase_index_by_id[phase_id]]
                or segment_elapsed < 0
                or segment_elapsed
                != phase_elapsed - previous_phase_elapsed.get(phase_id, 0)
                or milestone_total_elapsed < previous_total_elapsed
                or milestone_total_elapsed > total_elapsed_ms
                or milestone_total_elapsed + TIMING_ROUNDING_TOLERANCE_MS
                < minimum_total_elapsed
            ):
                raise RuntimeError(
                    f"Prerequisite progress milestone timing differs: {milestone_id!r}"
                )
            previous_phase_elapsed[phase_id] = phase_elapsed
            previous_total_elapsed = milestone_total_elapsed
        over_budget = tuple(
            str(phase["phaseId"])
            for phase in self.phases
            if phase.get("withinBudget") is not True
            or phase.get("elapsedMs", 0) > phase.get("budgetMs", -1)
        )
        if over_budget or snapshot["withinConfiguredTotalTarget"] is not True:
            raise RuntimeError(
                "Prerequisite progress exceeded its explicit timing budget: "
                f"phases={over_budget!r}, totalElapsedMs={snapshot['totalElapsedMs']}, "
                f"configuredTotalTargetMs={snapshot['configuredTotalTargetMs']}"
            )
        self._finished = True
        self._atomic_write(snapshot)
        self._emit({
            "schema": PROGRESS_SCHEMA,
            "event": "timing-complete",
            "phaseCount": len(self.phases),
            "scanCount": len(self.scans),
            "totalElapsedMs": snapshot["totalElapsedMs"],
        })
        return snapshot

    def fail(self, error: BaseException) -> None:
        if self._finished:
            return
        self._close_active("fail")
        self._finished = True
        snapshot = self.snapshot("fail")
        snapshot["failureType"] = type(error).__name__
        self._atomic_write(snapshot)
        self._emit({
            "schema": PROGRESS_SCHEMA,
            "event": "timing-failed",
            "failureType": type(error).__name__,
            "totalElapsedMs": snapshot["totalElapsedMs"],
        })

    def snapshot(self, status: str) -> dict[str, object]:
        total_elapsed = round((time.monotonic() - self.started) * 1000)
        return {
            "schema": PROGRESS_SCHEMA,
            "status": status,
            "clock": "time.monotonic",
            "configuredTotalTargetMs": TOTAL_PERFORMANCE_TARGET_MS,
            "totalElapsedMs": total_elapsed,
            "withinConfiguredTotalTarget": total_elapsed <= TOTAL_PERFORMANCE_TARGET_MS,
            "phaseBudgetsMs": dict(PHASE_BUDGET_MS),
            "phases": list(self.phases),
            "scans": list(self.scans),
            "milestones": list(self.milestones),
        }

    def _close_active(self, status: str) -> None:
        if self._active_id is None:
            return
        elapsed = round((time.monotonic() - self._active_started) * 1000)
        budget = PHASE_BUDGET_MS[self._active_id]
        phase = {
            "ordinal": len(self.phases) + 1,
            "phaseId": self._active_id,
            "status": status,
            "elapsedMs": elapsed,
            "budgetMs": budget,
            "withinBudget": elapsed <= budget,
        }
        self.phases.append(phase)
        self._emit({"schema": PROGRESS_SCHEMA, "event": "phase-complete", **phase})
        self._active_id = None
        self._active_started = 0.0
        if status == "pass" and phase["withinBudget"] is not True:
            raise RuntimeError(
                "Prerequisite progress exceeded its explicit phase timing budget: "
                f"phase={phase['phaseId']!r}, elapsedMs={phase['elapsedMs']}, "
                f"budgetMs={phase['budgetMs']}"
            )

    def _write(self, status: str) -> None:
        self._atomic_write(self.snapshot(status))

    def _atomic_write(self, payload: dict[str, object]) -> None:
        self.evidence_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.evidence_path.with_name(f".{self.evidence_path.name}.tmp")
        temporary.write_text(
            json.dumps(payload, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        temporary.replace(self.evidence_path)

    def _emit(self, payload: dict[str, object]) -> None:
        self.events.append(dict(payload))
        self.events_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.events_path.with_name(f".{self.events_path.name}.tmp")
        temporary.write_text(
            "".join(
                json.dumps(event, sort_keys=True, separators=(",", ":")) + "\n"
                for event in self.events
            ),
            encoding="utf-8",
        )
        temporary.replace(self.events_path)
        print(json.dumps(payload, sort_keys=True), flush=True)


class TalentGrantSurface(NamedTuple):
    kind: str
    selected_count: int
    required_count: int
    grant_digest: str
    option_ids: tuple[str, ...]
    enabled_option_ids: tuple[str, ...]
    selected_option_ids: tuple[str, ...]
    completion_enabled: bool


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def canonical_json_sha256(payload: dict[str, object]) -> str:
    canonical = json.dumps(
        payload,
        ensure_ascii=True,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def node_text(device: shared.Device, selector: str, *, scroll: bool = False) -> str:
    node = device.wait(selector, timeout=60, scroll=scroll, max_scrolls=22)
    return node.attributes.get("text") or node.attributes.get("content-desc") or ""


class CreationDashboardScanProof(NamedTuple):
    binding: str
    method_detail: str
    swipes: int
    method_viewport: int


def assert_uncreated_advanced_editor_gated(
    device: shared.Device,
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str = "advanced-editor-gate",
) -> CreationDashboardScanProof:
    """Scan the dashboard once for forbidden controls and reusable authority."""
    forbidden = (
        "Actions",
        "build-origin-dossier",
        "build-free-sprite-conversion",
        "build-career-create-expense",
        "creation-wizard-attributes",
        "attribute-save-",
    )
    scan_origin = acquire_stable_start_origin(
        device,
        scan_id=f"{scan_id}-origin",
        max_reverse_swipes=8,
        distance_ratio=0.68,
        stable_repeats=2,
        max_consecutive_empty_reads=3,
        delay_seconds=0.0,
    )
    scan = scan_forward_with_receipt(
        device,
        scan_id=scan_id,
        max_scrolls=18,
        distance_ratio=0.68,
        initial_observation=scan_origin,
        delay_seconds=0.0,
        observer=scan_observer,
    )
    bindings: set[str] = set()
    method_states: set[tuple[str, str, str]] = set()
    method_viewports: set[int] = set()
    for viewport_index, nodes in enumerate(scan.screens):
        for selector in forbidden:
            if any(shared.Device._matches(node, selector) for node in nodes):
                device.capture(f"wizard-forbidden-{selector}")
                raise RuntimeError(
                    "Creation dashboard exposed a Career/advanced-editor control while "
                    f"the authoritative runner is still uncreated: {selector!r}"
                )
        for selector in ("creation-wizard-binding", "creation-stage-method"):
            matches = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(matches) > 1:
                device.capture(f"{scan_id}-{selector}-cardinality-invalid")
                raise RuntimeError(
                    f"Creation dashboard {selector!r} has cardinality {len(matches)}"
                )
            if len(matches) == 1:
                value = (
                    matches[0].attributes.get("text")
                    or matches[0].attributes.get("content-desc")
                    or ""
                ).strip()
                if value:
                    if selector == "creation-wizard-binding":
                        bindings.add(value)
                    else:
                        if device.node_has_tappable_bounds(matches[0]):
                            method_viewports.add(min(viewport_index, scan.swipes))
                        method_states.add((
                            value,
                            matches[0].attributes.get("enabled", ""),
                            matches[0].attributes.get("clickable", ""),
                        ))
    if len(bindings) != 1 or len(method_states) != 1 or not method_viewports:
        device.capture(f"{scan_id}-authority-incomplete")
        raise RuntimeError(
            "Creation dashboard stable scan did not expose one binding and one method row: "
            f"bindings={sorted(bindings)!r}, methods={sorted(method_states)!r}, "
            f"methodViewports={sorted(method_viewports)!r}"
        )
    method_detail, method_enabled, method_clickable = next(iter(method_states))
    require_creation_method_navigation(
        shared.UiNode(
            {
                "content-desc": method_detail,
                "enabled": method_enabled,
                "clickable": method_clickable,
            }
        ),
        ready=True,
    )
    return CreationDashboardScanProof(
        binding=next(iter(bindings)),
        method_detail=method_detail,
        swipes=scan.swipes,
        method_viewport=max(method_viewports),
    )


def canonical_digest(device: shared.Device, selector: str, *, scroll: bool = False) -> str:
    value = node_text(device, selector, scroll=scroll).strip()
    if CANONICAL_AUTHORITY_DIGEST.fullmatch(value) is None:
        raise RuntimeError(f"{selector} did not expose one canonical digest: {value!r}")
    return value


def canonical_auxiliary_state_digest(
    device: shared.Device,
    selector: str,
    *,
    scroll: bool = False,
) -> str:
    value = node_text(device, selector, scroll=scroll).strip()
    if CANONICAL_AUXILIARY_STATE_DIGEST.fullmatch(value) is None:
        raise RuntimeError(
            f"{selector} did not expose one canonical auxiliary-state digest: {value!r}"
        )
    return value


def nonnegative_integer(device: shared.Device, selector: str, *, scroll: bool = False) -> int:
    value = node_text(device, selector, scroll=scroll).strip()
    if re.fullmatch(r"[0-9]+", value) is None:
        raise RuntimeError(f"{selector} did not expose one nonnegative integer: {value!r}")
    return int(value)


def require_new_character_dialog_transition(
    device: shared.Device,
    *,
    timeout: int = 120,
    observation_out: dict[str, object] | None = None,
    resolved_viewport_out: list[PriorityRankOrigin] | None = None,
    fresh_first: bool = False,
) -> list[shared.UiNode]:
    """Require the production modal to publish either Build or one exact error."""
    deadline = time.monotonic() + timeout
    selectors = ("dialog-surface", "dialog-error", "build-save-runner")
    started = time.monotonic()
    hierarchy_durations_ms: list[int] = []
    empty_hierarchy_reads = 0
    first_observation = True

    def record_observation(status: str) -> None:
        if observation_out is not None:
            observation_out.update({
                "scanId": "dialog-transition-poll",
                "status": status,
                "emptyHierarchyReads": empty_hierarchy_reads,
                **hierarchy_timing_fields(hierarchy_durations_ms),
                "elapsedMs": round((time.monotonic() - started) * 1000),
            })

    while time.monotonic() < deadline:
        if first_observation and fresh_first:
            nodes = fresh_hierarchy_timed(device, hierarchy_durations_ms)
        else:
            nodes = read_only_hierarchy_timed(device, hierarchy_durations_ms)
        first_observation = False
        if not nodes:
            empty_hierarchy_reads += 1
        matches = {
            selector: [
                node
                for node in nodes
                if selector
                in {
                    node.attributes.get("resource-id", "").rsplit("/", 1)[-1],
                    node.attributes.get("content-desc", ""),
                }
            ]
            for selector in selectors
        }
        ambiguous = {
            selector: len(candidates)
            for selector, candidates in matches.items()
            if len(candidates) > 1
        }
        if ambiguous:
            record_observation("cardinality-invalid")
            device.capture("creation-priority-dialog-transition-cardinality-invalid")
            raise RuntimeError(
                f"New-character modal transition was ambiguous: {ambiguous!r}"
            )
        if len(matches["dialog-error"]) == 1:
            record_observation("product-error")
            error = matches["dialog-error"][0]
            message = (
                error.attributes.get("text")
                or error.attributes.get("content-desc")
                or "unknown product import error"
            ).strip()
            device.capture("creation-priority-dialog-product-error")
            raise RuntimeError(
                f"New-character production import kept the modal open: {message}"
            )
        if len(matches["build-save-runner"]) == 1:
            if matches["dialog-surface"]:
                record_observation("route-overlap")
                device.capture("creation-priority-dialog-route-overlap")
                raise RuntimeError(
                    "New-character modal and Build toolbar were published together"
                )
            if resolved_viewport_out is not None:
                resolved_viewport_out.append(
                    PriorityRankOrigin(
                        nodes=nodes,
                        reverse_swipes=0,
                        elapsed_ms=hierarchy_durations_ms[-1],
                        hierarchy_durations_ms=(hierarchy_durations_ms[-1],),
                        empty_hierarchy_reads=0,
                    )
                )
            record_observation("resolved")
            return nodes
        if device.dismiss_system_ui_anr(nodes):
            time.sleep(2)
            continue
        time.sleep(0.75)

    record_observation("timeout")
    device.capture("creation-priority-dialog-transition-unavailable")
    raise RuntimeError(
        "New-character production modal published neither one exact error nor the Build route"
    )


def require_initial_creation_dashboard_snapshot(
    device: shared.Device,
    nodes: list[shared.UiNode],
) -> None:
    """Bind the automatic create-to-dashboard handoff from its one fresh snapshot."""
    selectors = (
        "phone-runner-page",
        "phone-runner-create",
        "creation-wizard-dashboard",
    )
    matches = {
        selector: [
            node
            for node in nodes
            if node.attributes.get("resource-id", "").rsplit("/", 1)[-1] == selector
        ]
        for selector in selectors
    }
    ambiguous = {
        selector: len(candidates)
        for selector, candidates in matches.items()
        if len(candidates) != 1
    }
    if ambiguous:
        device.capture("creation-priority-dashboard-handoff-cardinality-invalid")
        raise RuntimeError(
            "New-character production handoff did not publish one exact creation dashboard: "
            f"{ambiguous!r}"
        )
    page = matches["phone-runner-page"][0]
    route = matches["phone-runner-create"][0]
    dashboard = matches["creation-wizard-dashboard"][0]
    if (
        page.attributes.get("class") != "android.view.ViewGroup"
        or page.attributes.get("enabled") != "true"
        or route.attributes.get("class") != "android.widget.TextView"
        or route.attributes.get("text") != "CREATION RUNNER"
        or route.attributes.get("enabled") != "true"
        or route.attributes.get("clickable") != "false"
        or dashboard.attributes.get("enabled") != "true"
    ):
        device.capture("creation-priority-dashboard-handoff-structure-invalid")
        raise RuntimeError(
            "New-character production handoff changed its exact native creation-dashboard structure"
        )


def require_creation_method_navigation(
    node: shared.UiNode,
    *,
    ready: bool,
) -> str:
    description = (
        node.attributes.get("content-desc")
        or node.attributes.get("text")
        or ""
    )
    clickable = node.attributes.get("clickable") == "true"
    enabled = node.attributes.get("enabled") == "true"
    if ready:
        if not clickable or not enabled or CREATION_KARMA_AUTHORITY_BLOCKER in description:
            raise RuntimeError(
                "Priority-created runner did not enable the method navigation row: "
                f"clickable={clickable}, enabled={enabled}, detail={description!r}"
            )
    elif enabled or CREATION_KARMA_AUTHORITY_BLOCKER not in description:
        raise RuntimeError(
            "Fresh runner did not remain fail-closed without Creation Karma authority: "
            f"clickable={clickable}, enabled={enabled}, detail={description!r}"
        )
    return description


def reacquire_exact_ready_creation_method(
    device: shared.Device,
    *,
    expected_detail: str,
    max_swipes: int,
) -> tuple[shared.UiNode, str, int]:
    """Reacquire and revalidate the exact method before any physical tap."""
    node, reverse_swipes = rewind_to_exact_resource_id(
        device,
        "creation-stage-method",
        max_swipes=max_swipes,
        distance_ratio=0.22,
        evidence_prefix="creation-stage-method-ready",
        surface_name="Measured ready creation method navigation",
        require_tappable=True,
        max_empty_hierarchy_reads=3,
        max_system_ui_dismissals=3,
    )
    detail = require_creation_method_navigation(node, ready=True)
    if detail != expected_detail:
        device.capture("creation-stage-method-changed-after-dashboard-scan")
        raise RuntimeError(
            "Creation method authority changed between the stable dashboard scan and tap: "
            f"scan={expected_detail!r}, tap={detail!r}"
        )
    return node, detail, reverse_swipes


def _pending_timeout_text(value: object) -> str:
    if value is None:
        return ""
    if isinstance(value, bytes):
        rendered = value.decode("utf-8", errors="replace")
    else:
        rendered = str(value)
    if len(rendered) <= CREATION_AUTHORITY_PENDING_TIMEOUT_TEXT_LIMIT:
        return rendered
    return (
        rendered[:CREATION_AUTHORITY_PENDING_TIMEOUT_TEXT_LIMIT]
        + "\n[pending-timeout diagnostic truncated]\n"
    )


def _pending_timeout_error(error: BaseException) -> str:
    return "\n".join(
        part
        for part in (
            f"command failed: {error}",
            _pending_timeout_text(getattr(error, "stdout", "")),
            _pending_timeout_text(getattr(error, "stderr", "")),
        )
        if part
    )


def _write_pending_timeout_artifact(
    device: shared.Device,
    name: str,
    value: str | bytes,
    *,
    status: str,
) -> dict[str, object]:
    payload = (
        value
        if isinstance(value, bytes)
        else _pending_timeout_text(value).encode("utf-8")
    )
    path = device.evidence / name
    path.write_bytes(payload)
    return {
        "name": name,
        "sha256": hashlib.sha256(payload).hexdigest(),
        "sizeBytes": len(payload),
        "status": status,
    }


def _safe_pending_timeout_shell(
    device: shared.Device,
    *arguments: str,
) -> tuple[str, str]:
    try:
        return device.shell(*arguments, timeout=15), "captured"
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        return _pending_timeout_error(error), "command-error"


def _capture_per_process_timeout_diagnostic(
    device: shared.Device,
    name: str,
    process_ids: tuple[str, ...],
    command: Callable[[str], tuple[str, ...]],
) -> dict[str, object]:
    if not process_ids:
        return _write_pending_timeout_artifact(
            device,
            name,
            "status=not-attempted\nreason=no-valid-package-process-id\n",
            status="not-attempted",
        )

    sections: list[str] = []
    statuses: list[str] = []
    for process_id in process_ids:
        arguments = command(process_id)
        output, status = _safe_pending_timeout_shell(device, *arguments)
        statuses.append(status)
        sections.append(
            "\n".join(
                (
                    f"pid={process_id}",
                    f"command={' '.join(arguments)}",
                    f"status={status}",
                    output,
                )
            ).rstrip()
        )
    aggregate_status = (
        "captured"
        if all(status == "captured" for status in statuses)
        else "command-error"
        if all(status == "command-error" for status in statuses)
        else "partial"
    )
    return _write_pending_timeout_artifact(
        device,
        name,
        "\n\n".join(sections) + "\n",
        status=aggregate_status,
    )


def capture_creation_authority_pending_timeout_diagnostics(
    device: shared.Device,
    *,
    timeout: float,
) -> dict[str, object]:
    """Capture a bounded pending-timeout bundle without declaring a product ANR."""
    prefix = CREATION_AUTHORITY_PENDING_TIMEOUT_PREFIX
    device.evidence.mkdir(parents=True, exist_ok=True)
    artifacts: list[dict[str, object]] = []

    process_output, process_status = _safe_pending_timeout_shell(
        device,
        "pidof",
        shared.PACKAGE,
    )
    process_ids = (
        tuple(
            sorted(
                {
                    token
                    for token in process_output.split()
                    if shared.PROCESS_ID.fullmatch(token)
                },
                key=int,
            )
        )
        if process_status == "captured"
        else ()
    )
    if process_status == "captured" and not process_ids:
        process_status = "unavailable"
    artifacts.append(
        _write_pending_timeout_artifact(
            device,
            f"{prefix}-process-ids.txt",
            "\n".join(
                (
                    f"package={shared.PACKAGE}",
                    f"status={process_status}",
                    f"process_ids={' '.join(process_ids)}",
                    f"pidof_output={process_output}",
                )
            )
            + "\n",
            status=process_status,
        )
    )
    artifacts.append(
        _capture_per_process_timeout_diagnostic(
            device,
            f"{prefix}-managed-thread-signal.txt",
            process_ids,
            lambda process_id: ("kill", "-3", process_id),
        )
    )
    artifacts.append(
        _capture_per_process_timeout_diagnostic(
            device,
            f"{prefix}-native-backtrace.txt",
            process_ids,
            lambda process_id: ("debuggerd", "-b", process_id),
        )
    )
    if process_ids:
        # Give Android's runtime logger one bounded beat to publish SIGQUIT output
        # before the fixed post-signal logcat snapshot is taken.
        time.sleep(0.75)

    for suffix, arguments in (
        ("activity-activities.txt", ("dumpsys", "activity", "activities")),
        ("activity-processes.txt", ("dumpsys", "activity", "processes")),
        ("window-windows.txt", ("dumpsys", "window", "windows")),
    ):
        output, status = _safe_pending_timeout_shell(device, *arguments)
        artifacts.append(
            _write_pending_timeout_artifact(
                device,
                f"{prefix}-{suffix}",
                output,
                status=status,
            )
        )

    fresh_dump_output, fresh_dump_status = _safe_pending_timeout_shell(
        device,
        "uiautomator",
        "dump",
        "--compressed",
        CREATION_AUTHORITY_PENDING_TIMEOUT_HIERARCHY,
    )
    hierarchy_errors = [
        f"fresh_dump_status={fresh_dump_status}",
        f"fresh_dump_output={fresh_dump_output}",
    ]
    hierarchy_payload: str | None = None
    hierarchy_status = "command-error"
    hierarchy_source = ""
    normalized_dump_output = fresh_dump_output.casefold()
    fresh_dump_succeeded = fresh_dump_status == "captured" and any(
        marker in normalized_dump_output
        for marker in ("hierarchy dumped", "hierchary dumped")
    )
    hierarchy_paths = (
        (
            CREATION_AUTHORITY_PENDING_TIMEOUT_HIERARCHY,
            "/sdcard/chummer-editing-window.xml",
        )
        if fresh_dump_succeeded
        else ("/sdcard/chummer-editing-window.xml",)
    )
    for hierarchy_path in hierarchy_paths:
        try:
            result = device.run(
                "exec-out",
                "cat",
                hierarchy_path,
                timeout=15,
            )
            candidate = _pending_timeout_text(result.stdout)
            if "<hierarchy" not in candidate:
                hierarchy_errors.append(
                    f"{hierarchy_path}: hierarchy root was absent"
                )
                continue
            hierarchy_payload = candidate
            hierarchy_source = hierarchy_path
            hierarchy_status = (
                "captured"
                if hierarchy_path == CREATION_AUTHORITY_PENDING_TIMEOUT_HIERARCHY
                else "fallback-captured"
            )
            break
        except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
            hierarchy_errors.append(f"{hierarchy_path}: {_pending_timeout_error(error)}")
    if hierarchy_payload is not None:
        artifacts.append(
            _write_pending_timeout_artifact(
                device,
                f"{prefix}-hierarchy.xml",
                hierarchy_payload,
                status=hierarchy_status,
            )
        )
    else:
        artifacts.append(
            _write_pending_timeout_artifact(
                device,
                f"{prefix}-hierarchy-error.txt",
                "\n".join(hierarchy_errors) + "\n",
                status="command-error",
            )
        )

    try:
        screenshot = device.run(
            "exec-out",
            "screencap",
            "-p",
            timeout=15,
            text=False,
        ).stdout
        if not isinstance(screenshot, bytes) or not screenshot.startswith(
            b"\x89PNG\r\n\x1a\n"
        ):
            raise RuntimeError("screencap returned no PNG image")
        artifacts.append(
            _write_pending_timeout_artifact(
                device,
                f"{prefix}-screenshot.png",
                screenshot,
                status="captured",
            )
        )
    except (OSError, RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        artifacts.append(
            _write_pending_timeout_artifact(
                device,
                f"{prefix}-screenshot-error.txt",
                _pending_timeout_error(error),
                status="command-error",
            )
        )

    logcat, logcat_status = _safe_pending_timeout_shell(
        device,
        "logcat",
        "-d",
        "-b",
        "all",
        "-v",
        "threadtime",
        "-t",
        "4000",
    )
    artifacts.append(
        _write_pending_timeout_artifact(
            device,
            f"{prefix}-logcat.txt",
            logcat,
            status=logcat_status,
        )
    )

    manifest = {
        "schemaVersion": 1,
        "diagnosticKind": "creation-dashboard-authority-pending-timeout",
        "selector": "creation-dashboard-authority-loading",
        "boundedWaitSeconds": timeout,
        "package": shared.PACKAGE,
        "processIds": list(process_ids),
        "hierarchySource": hierarchy_source,
        "artifacts": artifacts,
    }
    (device.evidence / CREATION_AUTHORITY_PENDING_TIMEOUT_MANIFEST).write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return manifest


def wait_creation_dashboard_authority(
    device: shared.Device,
    *,
    timeout: float = 30.0,
    observation_out: dict[str, object] | None = None,
    initial_observation: PriorityRankOrigin | None = None,
    resolved_viewport_out: list[PriorityRankOrigin] | None = None,
    poll_delay_seconds: float = 0.5,
) -> bool:
    """Wait for the explicitly asynchronous authority projection, never for a guessed row state."""
    if timeout <= 0 or poll_delay_seconds < 0:
        raise ValueError(
            "Creation authority polling requires a positive timeout and nonnegative delay"
        )
    deadline = time.monotonic() + timeout
    saw_loading = False
    started = time.monotonic()
    hierarchy_durations_ms: list[int] = []
    empty_hierarchy_reads = 0
    pending_initial_observation = initial_observation

    def record_observation(status: str) -> None:
        if observation_out is not None:
            observation_out.update({
                "scanId": "dashboard-authority-poll",
                "status": status,
                "emptyHierarchyReads": empty_hierarchy_reads,
                **hierarchy_timing_fields(hierarchy_durations_ms),
                "elapsedMs": round((time.monotonic() - started) * 1000),
            })

    def raise_pending_timeout() -> None:
        record_observation("timeout")
        try:
            capture_creation_authority_pending_timeout_diagnostics(
                device,
                timeout=timeout,
            )
        except Exception as error:
            # Evidence collection is deliberately best-effort. It must never
            # turn the product's bounded pending timeout into a false success
            # or replace the stable timeout contract with a tooling exception.
            try:
                _write_pending_timeout_artifact(
                    device,
                    f"{CREATION_AUTHORITY_PENDING_TIMEOUT_PREFIX}-collection-error.txt",
                    _pending_timeout_error(error),
                    status="collection-error",
                )
            except Exception:
                pass
        raise RuntimeError(
            "Creation dashboard authority projection remained pending past the bounded wait"
        )

    while True:
        if pending_initial_observation is not None:
            current_observation = pending_initial_observation
            pending_initial_observation = None
            nodes = current_observation.nodes
        else:
            nodes = read_only_hierarchy_timed(device, hierarchy_durations_ms)
            current_observation = PriorityRankOrigin(
                nodes=nodes,
                reverse_swipes=0,
                elapsed_ms=hierarchy_durations_ms[-1],
                hierarchy_durations_ms=(hierarchy_durations_ms[-1],),
                empty_hierarchy_reads=0 if nodes else 1,
            )
        if not nodes:
            empty_hierarchy_reads += 1
            if time.monotonic() >= deadline:
                raise_pending_timeout()
            time.sleep(0.75)
            continue
        matches = {
            selector: [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            for selector in (
                "creation-dashboard-authority-failed",
                "creation-dashboard-authority-loading",
            )
        }
        ambiguous = {
            selector: len(candidates)
            for selector, candidates in matches.items()
            if len(candidates) > 1
        }
        if ambiguous:
            record_observation("cardinality-invalid")
            device.capture("creation-dashboard-authority-cardinality-invalid")
            raise RuntimeError(
                f"Creation dashboard authority state was ambiguous: {ambiguous!r}"
            )
        if matches["creation-dashboard-authority-failed"]:
            record_observation("product-failed")
            device.capture("creation-dashboard-authority-failed")
            raise RuntimeError(
                "Creation dashboard reported an explicit authority projection failure"
            )
        if not matches["creation-dashboard-authority-loading"]:
            if resolved_viewport_out is not None:
                resolved_viewport_out.append(current_observation)
            record_observation("resolved")
            return saw_loading
        saw_loading = True
        if time.monotonic() >= deadline:
            raise_pending_timeout()
        if poll_delay_seconds > 0:
            time.sleep(poll_delay_seconds)


def wait_creation_method_navigation(
    device: shared.Device,
    *,
    ready: bool,
    max_scrolls: int = 22,
) -> dict[str, object]:
    authority_projection_waited = wait_creation_dashboard_authority(device)
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    for scroll_index in range(max_scrolls + 1):
        node = device.find("creation-stage-method")
        if node is not None:
            detail = require_creation_method_navigation(node, ready=ready)
            before_tap = {
                "detail": detail,
                "clickable": node.attributes.get("clickable") == "true",
                "enabled": node.attributes.get("enabled") == "true",
            }
            if not ready:
                # UIAutomator reports a MAUI Button with a Clicked handler as clickable even while
                # IsEnabled=false. Prove the product gate itself: a physical tap must remain on the
                # dashboard and must not open the prerequisite route.
                x, y = node.center
                device.shell("input", "tap", str(x), str(y))
                time.sleep(1.25)
                if device.find("creation-prerequisite-page") is not None:
                    device.capture("creation-method-navigation-opened-without-authority")
                    raise RuntimeError(
                        "Disabled creation method navigation opened without Creation Karma authority"
                    )
                blocked_after = device.find("creation-stage-method")
                if blocked_after is None:
                    device.capture("creation-method-navigation-row-missing-after-blocked-tap")
                    raise RuntimeError(
                        "Creation method row disappeared after its disabled no-authority tap"
                    )
                after_tap = {
                    "detail": require_creation_method_navigation(blocked_after, ready=False),
                    "clickable": blocked_after.attributes.get("clickable") == "true",
                    "enabled": blocked_after.attributes.get("enabled") == "true",
                }
                if after_tap != before_tap:
                    device.capture("creation-method-navigation-changed-after-blocked-tap")
                    raise RuntimeError(
                        "Creation method no-authority state changed after its disabled tap: "
                        f"before={before_tap!r}, after={after_tap!r}"
                    )
                device.capture("creation-method-navigation-remained-blocked")
                # Rebind the fixed toolbar and exact dashboard resource after resetting the
                # preserved inner Build viewport. Prefix lookalikes or duplicate nodes fail closed.
                shared.open_creation_dashboard(
                    device,
                    open_build_route=False,
                    dashboard_timeout=30,
                    reset_swipes=max_scrolls,
                )
                return {
                    **before_tap,
                    "authorityProjectionWaited": authority_projection_waited,
                    "afterTap": after_tap,
                    "tapRemainedOnDashboard": True,
                }
            return {
                **before_tap,
                "authorityProjectionWaited": authority_projection_waited,
            }
        if scroll_index < max_scrolls:
            device.swipe_up(distance_ratio=0.22)
            time.sleep(0.75)
    device.capture("creation-method-navigation-missing")
    raise RuntimeError("Creation method navigation row is absent from the phone wizard")


def require_prerequisite_binding(value: str) -> dict[str, object]:
    match = SHORT_AUTHORITY_BINDING.fullmatch(value)
    if match is None:
        raise RuntimeError(
            "Creation prerequisite binding did not expose exact revision, snapshot, and authority "
            f"digests: {value!r}"
        )
    revision = int(match.group("revision"))
    saved = int(match.group("saved"))
    if saved > revision:
        raise RuntimeError(
            f"Creation prerequisite binding saved revision exceeds content revision: {value!r}"
        )
    return {
        "contentRevision": revision,
        "savedRevision": saved,
        "snapshotDigestPrefix": match.group("snapshot"),
        "authorityDigestPrefix": match.group("authority"),
    }


def require_binding_matches_canonical_digests(
    binding: dict[str, object],
    snapshot_digest: str,
    authority_digest: str,
) -> None:
    if CANONICAL_AUTHORITY_DIGEST.fullmatch(snapshot_digest) is None:
        raise RuntimeError(
            f"Creation prerequisite snapshot digest is not canonical: {snapshot_digest!r}"
        )
    if CANONICAL_AUTHORITY_DIGEST.fullmatch(authority_digest) is None:
        raise RuntimeError(
            f"Creation prerequisite authority digest is not canonical: {authority_digest!r}"
        )
    expected_snapshot_prefix = snapshot_digest.removeprefix("sha256:")[:12]
    expected_authority_prefix = authority_digest.removeprefix("sha256:")[:12]
    if binding.get("snapshotDigestPrefix") != expected_snapshot_prefix:
        raise RuntimeError(
            "Creation prerequisite binding snapshot prefix does not match its full canonical "
            f"digest: binding={binding!r}, snapshot={snapshot_digest!r}"
        )
    if binding.get("authorityDigestPrefix") != expected_authority_prefix:
        raise RuntimeError(
            "Creation prerequisite binding authority prefix does not match its full canonical "
            f"digest: binding={binding!r}, authority={authority_digest!r}"
        )


def read_persisted_prerequisite_authority(device: shared.Device) -> dict[str, object]:
    shared.reset_scroll_to_top(device, swipes=22)
    binding = require_prerequisite_binding(
        node_text(device, "creation-prerequisite-binding", scroll=True)
    )
    snapshot_digest = canonical_digest(
        device,
        "creation-prerequisite-snapshot-digest",
        scroll=True,
    )
    binding_digests = {
        "rawCharacterXml": canonical_digest(
            device,
            "creation-prerequisite-raw-character-xml-digest",
            scroll=True,
        ),
        "auxiliaryState": canonical_auxiliary_state_digest(
            device,
            "creation-prerequisite-auxiliary-state-digest",
            scroll=True,
        ),
        "authority": canonical_digest(
            device,
            "creation-prerequisite-authority-digest",
            scroll=True,
        ),
    }
    require_binding_matches_canonical_digests(
        binding,
        snapshot_digest,
        binding_digests["authority"],
    )
    device.wait(
        "creation-prerequisite-pending-draft",
        timeout=60,
        scroll=True,
        max_scrolls=22,
    )
    return {
        "binding": binding,
        "snapshotDigest": snapshot_digest,
        "bindingDigests": binding_digests,
        "draftDigest": canonical_digest(
            device,
            "creation-prerequisite-pending-draft-digest",
            scroll=True,
        ),
    }


def assert_persisted_prerequisite_authority(
    actual: dict[str, object],
    expected_draft_digest: str,
    expected_binding_digests: dict[str, str],
    expected_content_revision: int,
    expected_saved_revision: int,
) -> None:
    if actual.get("draftDigest") != expected_draft_digest:
        raise RuntimeError("Persisted prerequisite DraftDigest changed across re-entry")
    if actual.get("bindingDigests") != expected_binding_digests:
        raise RuntimeError("Persisted prerequisite binding digests changed across re-entry")
    binding = actual.get("binding")
    if not isinstance(binding, dict):
        raise RuntimeError("Persisted prerequisite binding receipt is absent")
    if binding.get("contentRevision") != expected_content_revision:
        raise RuntimeError("Persisted prerequisite content revision changed across re-entry")
    if binding.get("savedRevision") != expected_saved_revision:
        raise RuntimeError("Persisted prerequisite saved revision changed across re-entry")


def read_source_authority_digests(device: shared.Device) -> list[str]:
    """Read source authority by stable IDs; localized labels are not authority."""
    digests: set[str] = set()
    for selector in (
        "creation-prerequisite-authority-digest",
        "creation-prerequisite-profile-inputs-digest",
        "creation-prerequisite-priorities-xml-digest",
    ):
        shared.reset_scroll_to_top(device, swipes=22)
        node = device.wait_for_single_exact_resource_id(
            selector,
            timeout=90,
            scroll=True,
            max_scrolls=22,
            scroll_distance_ratio=0.22,
            evidence_prefix=f"{selector}-source-authority",
            surface_name="Creation prerequisite source-authority digest",
        )
        value = (
            node.attributes.get("text")
            or node.attributes.get("content-desc")
            or ""
        ).strip()
        if CANONICAL_AUTHORITY_DIGEST.fullmatch(value) is None:
            device.capture(f"{selector}-not-canonical")
            raise RuntimeError(
                f"Creation prerequisite source authority {selector!r} did not "
                f"expose one canonical digest: {value!r}"
            )
        digests.add(value)
    if len(digests) != 3:
        device.capture("creation-prerequisite-source-authority-incomplete")
        raise RuntimeError(
            "Creation prerequisite source authority did not expose three distinct "
            f"exact digest values: {sorted(digests)!r}"
        )
    return sorted(digests)


PREREQUISITE_AUTHORITY_SELECTORS = (
    "creation-prerequisite-binding",
    "creation-prerequisite-method",
    "creation-prerequisite-karma-budget",
    "creation-prerequisite-snapshot-digest",
    "creation-prerequisite-raw-character-xml-digest",
    "creation-prerequisite-auxiliary-state-digest",
    "creation-prerequisite-authority-digest",
    "creation-prerequisite-profile-inputs-digest",
    "creation-prerequisite-priorities-xml-digest",
)


class PrerequisiteAuthorityScanProof(NamedTuple):
    values: dict[str, str]
    swipes: int
    category_viewports: dict[str, int]


def wait_for_prerequisite_scan_origin(
    device: shared.Device,
    *,
    timeout: float = 60.0,
    max_reverse_swipes: int = 8,
    distance_ratio: float = 0.68,
) -> PriorityRankOrigin:
    """Acquire and retain the exact top viewport of the pushed prerequisite page.

    A pushed MAUI page can inherit the prior inner-scroll offset.  The former
    proof therefore issued eight blind reverse gestures, waited, and then paid
    for a second hierarchy read at the start of the stable scan.  This bounded
    acquisition instead reverses only while the exact prerequisite route is
    present but its two top authority anchors are absent.  The hierarchy that
    proves those anchors is returned for direct reuse as the scan's first
    viewport; duplicate route or anchor nodes still fail closed.
    """
    if timeout <= 0 or max_reverse_swipes < 0:
        raise ValueError("A positive timeout and nonnegative reverse bound are required")
    route_selector = "creation-prerequisite-page"
    top_selectors = (
        "creation-prerequisite-method",
        "creation-prerequisite-binding",
    )
    started = time.monotonic()
    deadline = time.monotonic() + timeout
    reverse_swipes = 0
    empty_hierarchy_reads = 0
    hierarchy_durations_ms: list[int] = []
    while time.monotonic() < deadline:
        nodes = fresh_hierarchy_timed(device, hierarchy_durations_ms)
        if not nodes:
            empty_hierarchy_reads += 1
            time.sleep(0.75)
            continue
        matches = {
            selector: [
                node for node in nodes if _exact_resource_id(node) == selector
            ]
            for selector in (route_selector, *top_selectors)
        }
        ambiguous = {
            selector: len(candidates)
            for selector, candidates in matches.items()
            if len(candidates) > 1
        }
        if ambiguous:
            device.capture("creation-prerequisite-scan-origin-cardinality-invalid")
            raise RuntimeError(
                "Creation prerequisite scan origin was ambiguous: "
                f"{ambiguous!r}"
            )
        if len(matches[route_selector]) == 1 and all(
            len(matches[selector]) == 1 for selector in top_selectors
        ):
            return PriorityRankOrigin(
                nodes=nodes,
                reverse_swipes=reverse_swipes,
                elapsed_ms=round((time.monotonic() - started) * 1000),
                hierarchy_durations_ms=tuple(hierarchy_durations_ms),
                empty_hierarchy_reads=empty_hierarchy_reads,
            )
        if device.dismiss_system_ui_anr(nodes):
            time.sleep(2)
            continue
        if len(matches[route_selector]) == 1 and reverse_swipes < max_reverse_swipes:
            device.swipe_down(distance_ratio=distance_ratio)
            reverse_swipes += 1
            continue
        time.sleep(0.25)
    device.capture("creation-prerequisite-scan-origin-unavailable")
    raise RuntimeError(
        "Timed out acquiring the exact prerequisite route and both top authority "
        f"anchors within {max_reverse_swipes} reverse swipes"
    )


def scan_prerequisite_authority(
    device: shared.Device,
    *,
    initial_observation: PriorityRankOrigin,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
) -> PrerequisiteAuthorityScanProof:
    """Read every initial prerequisite authority field in one stable traversal."""
    # The origin has already proved the exact pushed route and its two top
    # authority anchors. Reuse that fresh hierarchy instead of dumping it a
    # second time. Keep the overlap-heavy 0.22-height gesture: Heritage and
    # Talent are short intermediate rows, and widening this step would trade
    # physical evidence coverage for speed. The real reduction comes from
    # eliminating duplicate observations and blind navigation, not weaker
    # sampling.
    scan = scan_forward_with_receipt(
        device,
        scan_id="prerequisite-authority-initial",
        max_scrolls=22,
        distance_ratio=0.22,
        initial_observation=initial_observation,
        delay_seconds=0.0,
        observer=scan_observer,
    )
    observed: dict[str, set[str]] = {
        selector: set() for selector in PREREQUISITE_AUTHORITY_SELECTORS
    }
    category_viewports: dict[str, set[int]] = {
        category: set() for category in CATEGORIES
    }
    category_semantics: dict[str, tuple[str, ...]] = {}
    for viewport_index, nodes in enumerate(scan.screens):
        for selector in PREREQUISITE_AUTHORITY_SELECTORS:
            matches = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(matches) > 1:
                device.capture(f"{selector}-cardinality-invalid")
                raise RuntimeError(
                    f"Creation prerequisite authority {selector!r} has cardinality "
                    f"{len(matches)} in one viewport"
                )
            if len(matches) == 1:
                value = (
                    matches[0].attributes.get("text")
                    or matches[0].attributes.get("content-desc")
                    or ""
                ).strip()
                if value:
                    observed[selector].add(value)
        for category in CATEGORIES:
            selector = f"creation-prerequisite-category-{category}"
            matches = [
                node
                for node in nodes
                if _exact_resource_id(node) == selector
            ]
            if len(matches) > 1:
                device.capture(f"creation-prerequisite-{category}-inventory-cardinality-invalid")
                raise RuntimeError(
                    f"Creation prerequisite category inventory {selector!r} has "
                    f"cardinality {len(matches)} in one viewport"
                )
            if len(matches) != 1:
                continue
            node = matches[0]
            signature = tuple(
                node.attributes.get(key, "")
                for key in (
                    "resource-id",
                    "class",
                    "content-desc",
                    "text",
                    "enabled",
                    "clickable",
                    "focusable",
                )
            )
            prior = category_semantics.setdefault(category, signature)
            if signature != prior:
                device.capture(f"creation-prerequisite-{category}-inventory-drift")
                raise RuntimeError(
                    f"Creation prerequisite category {category!r} changed semantics "
                    "during the digest-bound authority scan"
                )
            if (
                node.attributes.get("enabled") != "true"
                or node.attributes.get("clickable") != "true"
            ):
                device.capture(f"creation-prerequisite-{category}-inventory-disabled")
                raise RuntimeError(
                    f"Creation prerequisite category {category!r} was not enabled and clickable"
                )
            if device.node_has_tappable_bounds(node):
                category_viewports[category].add(min(viewport_index, scan.swipes))
    invalid = {
        selector: sorted(values)
        for selector, values in observed.items()
        if len(values) != 1
    }
    if invalid:
        device.capture("creation-prerequisite-authority-scan-invalid")
        raise RuntimeError(
            "Creation prerequisite authority scan was incomplete or changed while scrolling: "
            f"{invalid!r}"
        )
    values = {selector: next(iter(entries)) for selector, entries in observed.items()}
    for selector in (
        "creation-prerequisite-snapshot-digest",
        "creation-prerequisite-raw-character-xml-digest",
        "creation-prerequisite-authority-digest",
        "creation-prerequisite-profile-inputs-digest",
        "creation-prerequisite-priorities-xml-digest",
    ):
        if CANONICAL_AUTHORITY_DIGEST.fullmatch(values[selector]) is None:
            raise RuntimeError(
                f"{selector} did not expose one canonical digest: {values[selector]!r}"
            )
    auxiliary = values["creation-prerequisite-auxiliary-state-digest"]
    if CANONICAL_AUXILIARY_STATE_DIGEST.fullmatch(auxiliary) is None:
        raise RuntimeError(
            "creation-prerequisite-auxiliary-state-digest did not expose one "
            f"canonical auxiliary-state digest: {auxiliary!r}"
        )
    source_digests = {
        values["creation-prerequisite-authority-digest"],
        values["creation-prerequisite-profile-inputs-digest"],
        values["creation-prerequisite-priorities-xml-digest"],
    }
    if len(source_digests) != 3:
        raise RuntimeError(
            "Creation prerequisite source authority did not expose three distinct digests"
        )
    invalid_categories = {
        category: sorted(viewports)
        for category, viewports in category_viewports.items()
        if not viewports
    }
    if invalid_categories:
        device.capture("creation-prerequisite-category-inventory-incomplete")
        raise RuntimeError(
            "Digest-bound prerequisite authority scan omitted an exact tappable "
            f"priority category: {invalid_categories!r}"
        )
    measured_categories = {
        category: max(category_viewports[category])
        for category in CATEGORIES
    }
    ordered_viewports = [measured_categories[category] for category in CATEGORIES]
    if ordered_viewports != sorted(ordered_viewports):
        device.capture("creation-prerequisite-category-inventory-order-invalid")
        raise RuntimeError(
            "Digest-bound prerequisite category inventory changed canonical row order: "
            f"{measured_categories!r}"
        )
    return PrerequisiteAuthorityScanProof(
        values=values,
        swipes=scan.swipes,
        category_viewports=measured_categories,
    )


def acquire_measured_priority_category_row(
    device: shared.Device,
    category: str,
    navigation: dict[str, object],
) -> shared.UiNode:
    """Reacquire one exact category from the digest-bound ordered inventory.

    The first category restores only the already measured viewport delta from
    the authority scan's stable end.  Later categories are below the freshly
    verified prior row, so they use bounded forward-only snapshots.  A rank
    selection can change row height; no pre-navigation node or blind absolute
    viewport is reused after that state transition.
    """
    viewports = navigation.get("viewportByCategory")
    current_viewport = navigation.get("currentViewport")
    last_category = navigation.get("lastCategory")
    current_nodes = navigation.get("currentNodes")
    if (
        not isinstance(viewports, dict)
        or set(viewports) != set(CATEGORIES)
        or type(current_viewport) is not int
        or current_viewport < 0
        or last_category is not None and last_category not in CATEGORIES
        or current_nodes is not None
        and (
            not isinstance(current_nodes, list)
            or not current_nodes
            or not all(isinstance(node, shared.UiNode) for node in current_nodes)
        )
    ):
        raise RuntimeError("Priority category navigation has no complete measured authority")
    target_viewport = viewports.get(category)
    if type(target_viewport) is not int or target_viewport < 0:
        raise RuntimeError(f"Priority category {category!r} has no measured viewport")

    if last_category is None:
        if category != CATEGORIES[0] or target_viewport > current_viewport:
            raise RuntimeError(
                "Priority category navigation did not start with the first ordered row"
            )
        move_between_measured_viewports(
            device,
            current_viewport,
            target_viewport,
            distance_ratio=0.22,
        )
        max_forward_swipes = 0
    else:
        last_index = CATEGORIES.index(last_category)
        if last_index + 1 >= len(CATEGORIES) or CATEGORIES[last_index + 1] != category:
            raise RuntimeError(
                f"Priority category navigation skipped canonical row order after {last_category!r}"
            )
        prior_viewport = viewports[last_category]
        if type(prior_viewport) is not int or target_viewport < prior_viewport:
            raise RuntimeError("Priority category inventory order changed after navigation")
        # Initial overlapping scans can place adjacent rows in the same viewport.
        # After a selection mutates row height, use overlapping 0.22 gestures
        # with a small bound derived from the measured initial separation.
        max_forward_swipes = max(4, (target_viewport - prior_viewport + 2) * 4)

    selector = f"creation-prerequisite-category-{category}"
    for forward_swipes in range(max_forward_swipes + 1):
        # The prior selection already acquired and cardinality-checked this
        # exact refreshed parent viewport.  Reuse it once rather than issuing
        # an identical UIAutomator dump before the next bounded forward step.
        if forward_swipes == 0 and last_category is not None and current_nodes is not None:
            nodes = current_nodes
        else:
            nodes = fresh_hierarchy_timed(device, [])
        matches = [node for node in nodes if _exact_resource_id(node) == selector]
        if len(matches) > 1:
            device.capture(f"creation-prerequisite-{category}-category-row-cardinality-invalid")
            raise RuntimeError(
                f"Measured {category} priority category row {selector!r} has "
                f"cardinality {len(matches)}"
            )
        if len(matches) == 1:
            node = matches[0]
            if (
                node.attributes.get("enabled") != "true"
                or node.attributes.get("clickable") != "true"
            ):
                device.capture(f"creation-prerequisite-{category}-category-row-disabled")
                raise RuntimeError(
                    f"Measured {category} priority category row was not enabled and clickable"
                )
            if device.node_has_tappable_bounds(node):
                navigation["currentViewport"] = target_viewport
                navigation["currentNodes"] = nodes
                return node
        if forward_swipes >= max_forward_swipes:
            break
        device.swipe_up(distance_ratio=0.22)
        # The next operation is a fresh UIAutomator dump, which synchronizes
        # the post-gesture viewport.  Avoid an additional blind fixed delay.
    device.capture(f"creation-prerequisite-{category}-category-row-unavailable")
    raise RuntimeError(
        f"Timed out acquiring exact ordered {category} priority category row "
        f"{selector!r} within {max_forward_swipes} forward swipes"
    )


def tap_prescribed_exact_enabled_priority_rank(
    device: shared.Device,
    category: str,
    *,
    expected_rank: str | None = None,
    initial_observation: PriorityRankOrigin | None = None,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
) -> str:
    """Tap one prescribed exact, enabled A-E rank after a cardinality scan."""
    prescribed_rank = expected_rank or PRIORITY_PROOF_RANKS.get(category, "")
    if re.fullmatch(r"[a-e]", prescribed_rank) is None:
        raise RuntimeError(
            f"No exact legal Priority proof rank was prescribed for {category!r}: "
            f"{prescribed_rank!r}"
        )
    prefix = f"creation-prerequisite-rank-{category}-"
    expected_ids = {f"{prefix}{rank}" for rank in "abcde"}
    selected_resource_id = f"{prefix}{prescribed_rank}"
    observed_ids: set[str] = set()
    candidates: set[str] = set()
    selected_viewports: list[int] = []
    invalid_ids: set[str] = set()
    duplicate_ids: set[str] = set()
    if initial_observation is None:
        rewind_to_exact_resource_id(
            device,
            f"{prefix}a",
            max_swipes=8,
            distance_ratio=0.68,
            evidence_prefix=f"creation-prerequisite-{category}-rank-origin",
            surface_name=f"{category} rank scan origin",
            require_tappable=False,
        )
    scan = scan_forward_with_receipt(
        device,
        scan_id=f"rank-cardinality-{category}",
        max_scrolls=8,
        distance_ratio=0.68,
        initial_observation=initial_observation,
        delay_seconds=0.0,
        observer=scan_observer,
    )
    for viewport_index, nodes in enumerate(scan.screens):
        screen_ids: list[str] = []
        for node in nodes:
            resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            if not resource_id.startswith("creation-prerequisite-rank-"):
                continue
            screen_ids.append(resource_id)
            if resource_id not in expected_ids:
                invalid_ids.add(resource_id)
                continue
            rank_token = resource_id[len(prefix) :]
            if re.fullmatch(r"[a-e]", rank_token) is None:
                invalid_ids.add(resource_id)
                continue
            observed_ids.add(resource_id)
            if (
                node.attributes.get("enabled") == "true"
                and node.attributes.get("clickable") == "true"
                and device.node_has_tappable_bounds(node)
            ):
                candidates.add(resource_id)
                if resource_id == selected_resource_id:
                    selected_viewports.append(viewport_index)
        duplicate_ids.update(
            resource_id
            for resource_id in set(screen_ids)
            if screen_ids.count(resource_id) > 1
        )
    if (
        invalid_ids
        or duplicate_ids
        or observed_ids != expected_ids
        or selected_resource_id not in candidates
        or not selected_viewports
    ):
        device.capture(f"creation-prerequisite-{category}-rank-cardinality-invalid")
        raise RuntimeError(
            f"Exact {category} rank scan was invalid: candidates={sorted(candidates)!r}, "
            f"observedIds={sorted(observed_ids)!r}, expectedIds={sorted(expected_ids)!r}, "
            f"invalidIds={sorted(invalid_ids)!r}, duplicateIds={sorted(duplicate_ids)!r}"
        )

    selected_viewport = max(selected_viewports)
    reverse_swipes = max(0, scan.swipes - selected_viewport)
    for _ in range(reverse_swipes):
        device.swipe_down(distance_ratio=0.68)
    nodes = device.hierarchy()
    exact_selected = [
        node
        for node in nodes
        if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
        == selected_resource_id
    ]
    if len(exact_selected) != 1:
        device.capture(f"creation-prerequisite-{category}-rank-option-cardinality-invalid")
        raise RuntimeError(
            f"Enabled {category} rank option {selected_resource_id!r} has cardinality "
            f"{len(exact_selected)} after reversing the exact scan delta"
        )
    node = exact_selected[0]
    if (
        node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node)
    ):
        device.capture(f"creation-prerequisite-{category}-rank-option-not-tappable")
        raise RuntimeError(
            f"Exact {category} rank option {selected_resource_id!r} was not enabled and tappable"
        )
    device.shell("input", "tap", *(str(value) for value in node.center))
    return selected_resource_id


def select_priority_rank(
    device: shared.Device,
    category: str,
    *,
    category_navigation: dict[str, object],
    expected_rank: str | None = None,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
) -> str:
    """Select one exact projected rank and prove that the parent draft refreshed.

    The first row is restored from the digest-bound authority inventory; every
    later row is reacquired forward from the freshly verified prior row.  A bare
    wait for the parent page is insufficient because navigation can briefly
    expose the parent's accessibility marker before its refreshed category row
    is ready.
    """
    if category not in CATEGORIES:
        raise RuntimeError(f"Unsupported prerequisite category {category!r}")

    category_selector = f"creation-prerequisite-category-{category}"
    category_row = acquire_measured_priority_category_row(
        device,
        category,
        category_navigation,
    )
    category_navigation.pop("currentNodes", None)
    device.shell("input", "tap", *(str(value) for value in category_row.center))
    rank_origin = wait_for_priority_rank_origin(
        device,
        category,
        timeout=45,
        max_reverse_swipes=8,
        distance_ratio=0.68,
    )
    selected_resource_id = tap_prescribed_exact_enabled_priority_rank(
        device,
        category,
        expected_rank=expected_rank,
        initial_observation=rank_origin,
        scan_observer=scan_observer,
    )

    expected_prefix = f"creation-prerequisite-rank-{category}-"
    if not selected_resource_id.startswith(expected_prefix):
        device.capture(f"creation-prerequisite-{category}-rank-id-invalid")
        raise RuntimeError(
            f"Selected {category} rank did not expose its exact resource ID: "
            f"{selected_resource_id!r}"
        )
    rank_token = selected_resource_id[len(expected_prefix) :]
    if re.fullmatch(r"[a-e]", rank_token) is None:
        device.capture(f"creation-prerequisite-{category}-rank-id-invalid")
        raise RuntimeError(
            f"Selected {category} rank resource ID carried an invalid SR5 rank: "
            f"{selected_resource_id!r}"
        )

    deadline = time.monotonic() + 45
    row: shared.UiNode | None = None
    while time.monotonic() < deadline:
        nodes = device.hierarchy()
        if not nodes:
            time.sleep(0.75)
            continue
        matches = {
            selector: [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            for selector in (
                "creation-prerequisite-category-page",
                "creation-prerequisite-page",
                category_selector,
            )
        }
        ambiguous = {
            selector: len(candidates)
            for selector, candidates in matches.items()
            if len(candidates) > 1
        }
        if ambiguous:
            device.capture(f"creation-prerequisite-{category}-pop-cardinality-invalid")
            raise RuntimeError(
                f"{category} rank selection published ambiguous parent state: {ambiguous!r}"
            )
        if (
            not matches["creation-prerequisite-category-page"]
            and len(matches["creation-prerequisite-page"]) == 1
            and len(matches[category_selector]) == 1
        ):
            row = matches[category_selector][0]
            break
        if device.dismiss_system_ui_anr(nodes):
            time.sleep(2)
            continue
        time.sleep(0.25)
    if row is None:
        device.capture(f"creation-prerequisite-{category}-category-pop-timeout")
        raise RuntimeError(
            f"{category} rank selection did not publish one refreshed parent row"
        )
    detail = row.attributes.get("content-desc", "")
    expected_rank = rank_token.upper()
    locale_binding = getattr(device, "_phone_ui_locale_binding", None)
    language = (
        locale_binding.language
        if isinstance(locale_binding, shared.PhoneUiLocaleBinding)
        else "en"
    )
    rank_label = PRIORITY_RANK_LABEL_BY_LANGUAGE[language]
    if (
        row.attributes.get("enabled") != "true"
        or row.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(row)
        or re.search(
            rf"(?:^|[. ·]){re.escape(rank_label)} {re.escape(expected_rank)}(?:$|[. ·])",
            detail,
        ) is None
    ):
        device.capture(f"creation-prerequisite-{category}-draft-not-refreshed")
        raise RuntimeError(
            f"Selected {category} rank {expected_rank!r} was not projected by the "
            f"refreshed phone draft row: {detail!r}"
        )
    category_navigation["lastCategory"] = category
    category_navigation["currentNodes"] = nodes
    return selected_resource_id


def open_prerequisite(device: shared.Device) -> None:
    device.tap_bidirectional(
        "creation-stage-method",
        timeout=180,
        backward_scrolls=0,
        forward_scrolls=8,
        scroll_distance_ratio=0.68,
        exact_resource_id=True,
    )
    device.wait("creation-prerequisite-page", timeout=60)
    # Android can carry the deeply scrolled Build viewport into this newly pushed page.
    # Bind the route first, then establish the native page origin before reading top cards.
    shared.reset_scroll_to_top(device, swipes=8)
    device.wait("creation-prerequisite-karma-budget", timeout=60, scroll=True, max_scrolls=22)
    # A full-height Android swipe can jump from the tall Karma card directly to
    # the category list and never expose the shorter method card to UIAutomator.
    # Use bounded, small bidirectional steps for this exact authority surface.
    device.wait_exact_resource_id_bidirectional(
        "creation-prerequisite-method",
        timeout=90,
        backward_scrolls=0,
        forward_scrolls=4,
        scroll_distance_ratio=0.18,
        evidence_prefix="creation-prerequisite-method",
        surface_name="Creation prerequisite build-method authority",
        require_tappable=False,
    )
    # Both authority cards can push the binding above UIAutomator's visible hierarchy.
    # Leave this route at a deterministic origin so every caller can read the binding
    # without depending on the height of the cards it just verified.
    shared.reset_scroll_to_top(device, swipes=4)


def tap_enabled_authority_option(
    device: shared.Device,
    prefix: str,
    required_label: str,
    *,
    max_scrolls: int = 40,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    navigation_out: dict[str, object] | None = None,
) -> str:
    candidate_ids: set[str] = set()
    candidate_viewports: dict[str, set[int]] = {}
    duplicate_resource_id = False
    scan_token = re.sub(r"[^a-z0-9]+", "-", required_label.casefold()).strip("-")
    rewind_to_stable_start(
        device,
        scan_id=f"authority-option-start-{scan_token}",
        max_scrolls=max_scrolls,
        distance_ratio=0.68,
        observer=scan_observer,
    )
    scan = scan_forward_with_receipt(
        device,
        scan_id="authority-option-cardinality-" + scan_token,
        max_scrolls=max_scrolls,
        distance_ratio=0.68,
        observer=scan_observer,
    )
    for viewport_index, nodes in enumerate(scan.screens):
        screen_ids = exact_enabled_authority_option_ids(
            nodes,
            prefix,
            required_label,
            device.node_has_tappable_bounds,
        )
        if len(screen_ids) != len(set(screen_ids)):
            duplicate_resource_id = True
        candidate_ids.update(screen_ids)
        for resource_id in screen_ids:
            candidate_viewports.setdefault(resource_id, set()).add(
                min(viewport_index, scan.swipes)
            )
    if duplicate_resource_id or len(candidate_ids) != 1:
        device.capture(
            "invalid-authority-option-cardinality-"
            + re.sub(r"[^a-z0-9]+", "-", required_label.casefold()).strip("-")
        )
        raise RuntimeError(
            "Expected exactly one enabled authoritative option for "
            f"prefix={prefix!r}, label={required_label!r}; "
            f"found {len(candidate_ids)} unique candidates"
        )
    resource_id = next(iter(candidate_ids))
    target_viewport = max(candidate_viewports[resource_id])
    move_between_measured_viewports(device, scan.swipes, target_viewport)
    nodes = device.hierarchy()
    exact = [node for node in nodes if _exact_resource_id(node) == resource_id]
    if len(exact) != 1:
        device.capture("creation-prerequisite-authority-option-cardinality-invalid")
        raise RuntimeError(
            f"Enabled authority option {required_label!r} changed cardinality to "
            f"{len(exact)} in its measured viewport"
        )
    node = exact[0]
    if (
        node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node)
    ):
        device.capture("creation-prerequisite-authority-option-not-tappable")
        raise RuntimeError(
            f"Enabled authority option {required_label!r} was not tappable in its fresh snapshot"
        )
    device.shell("input", "tap", *(str(value) for value in node.center))
    if navigation_out is not None:
        navigation_out.clear()
        navigation_out.update(
            {
                "endViewport": target_viewport,
                "resourceViewports": {resource_id: target_viewport},
            }
        )
    return resource_id


def exact_enabled_authority_option_ids(
    nodes: list[shared.UiNode],
    prefix: str,
    required_label: str,
    is_tappable: Callable[[shared.UiNode], bool],
) -> list[str]:
    if not prefix or not required_label.strip():
        return []
    expected = required_label.strip().casefold()
    candidates: list[str] = []
    for node in nodes:
        resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
        accessible_values = (
            node.attributes.get("text", "").strip().casefold(),
            node.attributes.get("content-desc", "").strip().casefold(),
        )
        exact_label = any(
            value == expected
            or value.startswith(expected + ". ")
            or value.startswith(expected + " · ")
            for value in accessible_values
        )
        if (
            resource_id.startswith(prefix)
            and exact_label
            and node.attributes.get("enabled") == "true"
            and node.attributes.get("clickable") == "true"
            and is_tappable(node)
        ):
            candidates.append(resource_id)
    return candidates


def _exact_resource_id(node: shared.UiNode) -> str:
    return node.attributes.get("resource-id", "").rsplit("/", 1)[-1]


def _accessible_values(node: shared.UiNode) -> tuple[str, ...]:
    return tuple(
        value.strip()
        for value in (
            node.attributes.get("text", ""),
            node.attributes.get("content-desc", ""),
        )
        if value.strip()
    )


def _talent_option_identity_and_slots(
    node: shared.UiNode,
) -> tuple[tuple[str, ...], tuple[int, ...], tuple[bool, ...]]:
    """Separate immutable detail while preserving every exact slot decorator."""
    identities: list[str] = []
    slots: list[int] = []
    slots_at_first_separator: list[bool] = []
    for accessible_value in _accessible_values(node):
        value = accessible_value.removeprefix("✓ ")
        matches = list(TALENT_SELECTED_SLOT_DECORATOR.finditer(value))
        first_separator = value.find(". ")
        slots.extend(int(match.group("slot")) for match in matches)
        slots_at_first_separator.extend(
            match.start() == first_separator
            for match in matches
        )
        for match in reversed(matches):
            value = (
                value[: match.start()]
                + match.group("separator")
                + value[match.end() :]
            )
        identities.append(value)
    return tuple(identities), tuple(slots), tuple(slots_at_first_separator)


def _talent_option_identity_values(node: shared.UiNode) -> tuple[str, ...]:
    return _talent_option_identity_and_slots(node)[0]


def _talent_option_slot_ordinals(node: shared.UiNode) -> tuple[int, ...]:
    return _talent_option_identity_and_slots(node)[1]


def _talent_option_has_exact_dynamic_slot(node: shared.UiNode) -> bool:
    """Require the product's sole dynamic slot decorator in its exact position."""
    _, slots, slots_at_first_separator = _talent_option_identity_and_slots(node)
    selected = any(value.startswith("✓ ") for value in _accessible_values(node))
    if selected:
        return len(slots) == 1 and slots_at_first_separator == (True,)
    return not slots and not slots_at_first_separator


def _talent_option_matches_exact_slot(
    node: shared.UiNode,
    expected_slot: int | None,
) -> bool:
    if not _talent_option_has_exact_dynamic_slot(node):
        return False
    selected = any(value.startswith("✓ ") for value in _accessible_values(node))
    if expected_slot is None:
        return not selected
    return selected and _talent_option_slot_ordinals(node) == (expected_slot,)


def _is_exact_tokenized_resource_id(resource_id: str, prefix: str) -> bool:
    if not resource_id.startswith(prefix):
        return False
    suffix = resource_id[len(prefix) :]
    return re.fullmatch(r"[a-z0-9](?:[a-z0-9-]*[a-z0-9])?", suffix) is not None


def read_talent_grant_surface(
    device: shared.Device,
    expected_kind: str,
    *,
    max_scrolls: int = 40,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str | None = None,
    navigation_out: dict[str, object] | None = None,
) -> TalentGrantSurface:
    """Read one complete, exact Core-projected Talent grant prompt.

    The scan accepts overlapping viewports but rejects duplicate IDs within a
    viewport, malformed tokenized selectors, contradictory selected state, or
    more than one authority count/digest/completion state.  This makes a future
    UI projection change fail closed instead of silently selecting a convenient
    first row.
    """
    if expected_kind not in TALENT_GRANT_KINDS:
        raise RuntimeError(f"Unsupported Talent grant kind {expected_kind!r}")
    option_prefix = TALENT_GRANT_OPTION_PREFIX[expected_kind]
    opposite_prefix = TALENT_GRANT_OPTION_PREFIX[
        next(kind for kind in TALENT_GRANT_KINDS if kind != expected_kind)
    ]
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-talent-grant-route",
        surface_name=f"{expected_kind} Talent grant route",
    )
    scan_token = scan_id or (
        "talent-grant-cardinality-"
        + re.sub(r"[^a-z0-9]+", "-", expected_kind.casefold()).strip("-")
    )
    rewind_to_stable_start(
        device,
        scan_id=f"{scan_token}-start",
        max_scrolls=max_scrolls,
        distance_ratio=0.68,
        observer=scan_observer,
    )
    scan = scan_forward_with_receipt(
        device,
        scan_id=scan_token,
        max_scrolls=max_scrolls,
        distance_ratio=0.68,
        observer=scan_observer,
    )

    option_ids: set[str] = set()
    enabled_option_ids: set[str] = set()
    selected_option_ids: set[str] = set()
    explicitly_unselected_option_ids: set[str] = set()
    malformed_option_ids: set[str] = set()
    opposite_option_ids: set[str] = set()
    duplicate_ids: set[str] = set()
    authority_counts: set[tuple[int, int, str]] = set()
    grant_digests: set[str] = set()
    completion_states: set[bool] = set()
    seen_authority = False
    seen_digest = False
    seen_completion = False
    resource_viewports: dict[str, set[int]] = {}
    option_identity_values: dict[str, set[tuple[str, ...]]] = {}
    option_slot_ordinals: dict[str, set[int]] = {}
    invalid_slot_option_ids: set[str] = set()

    for viewport_index, nodes in enumerate(scan.screens):
        screen_ids: list[str] = []
        for node in nodes:
            resource_id = _exact_resource_id(node)
            values = _accessible_values(node)
            if (
                navigation_out is not None
                and resource_id
                and device.node_has_tappable_bounds(node)
            ):
                resource_viewports.setdefault(resource_id, set()).add(
                    min(viewport_index, scan.swipes)
                )
            if resource_id.startswith(opposite_prefix):
                opposite_option_ids.add(resource_id)
            if resource_id.startswith(option_prefix):
                screen_ids.append(resource_id)
                if not _is_exact_tokenized_resource_id(resource_id, option_prefix):
                    malformed_option_ids.add(resource_id)
                    continue
                option_ids.add(resource_id)
                option_identity_values.setdefault(resource_id, set()).add(
                    _talent_option_identity_values(node)
                )
                option_slot_ordinals.setdefault(resource_id, set()).update(
                    _talent_option_slot_ordinals(node)
                )
                is_selected = any(value.startswith("✓ ") for value in values)
                if not _talent_option_has_exact_dynamic_slot(node):
                    invalid_slot_option_ids.add(resource_id)
                if is_selected:
                    selected_option_ids.add(resource_id)
                else:
                    explicitly_unselected_option_ids.add(resource_id)
                if (
                    node.attributes.get("enabled") == "true"
                    and node.attributes.get("clickable") == "true"
                ):
                    enabled_option_ids.add(resource_id)
            if resource_id == "creation-prerequisite-talent-grant-authority":
                seen_authority = True
            if resource_id == "creation-prerequisite-talent-grant-digest":
                seen_digest = True
                grant_digests.update(
                    value for value in values if CANONICAL_AUTHORITY_DIGEST.fullmatch(value)
                )
            if resource_id == "creation-prerequisite-talent-grant-complete":
                seen_completion = True
                completion_states.add(node.attributes.get("enabled") == "true")
            for value in values:
                authority_counts.update(
                    (
                        int(match.group("selected")),
                        int(match.group("required")),
                        match.group("kind"),
                    )
                    for match in TALENT_GRANT_REQUIRED.finditer(value)
                )
        duplicate_ids.update(
            resource_id
            for resource_id in set(screen_ids)
            if screen_ids.count(resource_id) > 1
        )

    contradictions = selected_option_ids & explicitly_unselected_option_ids
    ambiguous_option_details = {
        resource_id
        for resource_id in option_ids
        if option_identity_values.get(resource_id) in (None, {()})
        or len(option_identity_values.get(resource_id, set())) != 1
    }
    if (
        not seen_authority
        or not seen_digest
        or not seen_completion
        or malformed_option_ids
        or opposite_option_ids
        or duplicate_ids
        or contradictions
        or ambiguous_option_details
        or len(authority_counts) != 1
        or len(grant_digests) != 1
        or len(completion_states) != 1
    ):
        device.capture("creation-prerequisite-talent-grant-authority-invalid")
        raise RuntimeError(
            "Talent grant prompt authority was ambiguous: "
            f"kind={expected_kind!r}, authority={seen_authority}, digest={seen_digest}, "
            f"completion={seen_completion}, counts={sorted(authority_counts)!r}, "
            f"digests={sorted(grant_digests)!r}, malformed={sorted(malformed_option_ids)!r}, "
            f"opposite={sorted(opposite_option_ids)!r}, duplicates={sorted(duplicate_ids)!r}, "
            f"contradictions={sorted(contradictions)!r}, "
            f"ambiguousDetails={sorted(ambiguous_option_details)!r}"
        )

    selected_count, required_count, observed_kind = next(iter(authority_counts))
    completion_enabled = next(iter(completion_states))
    selected_slot_ordinals = {
        slot
        for resource_id in selected_option_ids
        for slot in option_slot_ordinals.get(resource_id, set())
    }
    if (
        observed_kind != expected_kind
        or required_count < 1
        or selected_count > required_count
        or len(option_ids) < required_count
        or len(enabled_option_ids) < selected_count
        or not selected_option_ids.issubset(enabled_option_ids)
        or selected_count != len(selected_option_ids)
        or invalid_slot_option_ids
        or selected_slot_ordinals != set(range(1, selected_count + 1))
        or completion_enabled != (selected_count == required_count)
    ):
        device.capture("creation-prerequisite-talent-grant-cardinality-invalid")
        raise RuntimeError(
            "Talent grant prompt count did not match its exact option state: "
            f"expectedKind={expected_kind!r}, observedKind={observed_kind!r}, "
            f"selected={selected_count}, required={required_count}, "
            f"options={sorted(option_ids)!r}, enabled={sorted(enabled_option_ids)!r}, "
            f"selectedIds={sorted(selected_option_ids)!r}, "
            f"selectedSlots={sorted(selected_slot_ordinals)!r}, "
            f"invalidSlotIds={sorted(invalid_slot_option_ids)!r}, "
            f"completionEnabled={completion_enabled}"
        )
    surface = TalentGrantSurface(
        kind=observed_kind,
        selected_count=selected_count,
        required_count=required_count,
        grant_digest=next(iter(grant_digests)),
        option_ids=tuple(sorted(option_ids)),
        enabled_option_ids=tuple(sorted(enabled_option_ids)),
        selected_option_ids=tuple(sorted(selected_option_ids)),
        completion_enabled=completion_enabled,
    )
    if navigation_out is not None:
        navigation_out.clear()
        navigation_out.update(
            {
                "endViewport": scan.swipes,
                "resourceViewports": {
                    resource_id: max(viewports)
                    for resource_id, viewports in resource_viewports.items()
                },
                "resourceDetails": {
                    resource_id: next(iter(option_identity_values[resource_id]))
                    for resource_id in sorted(option_ids)
                },
            }
        )
    return surface


class TalentGrantMutableState(NamedTuple):
    selected_count: int
    selected_option_ids: tuple[str, ...]
    completion_enabled: bool


class TalentStateGroupSnapshot(NamedTuple):
    nodes: list[shared.UiNode]
    resources: dict[str, shared.UiNode]
    logical_viewport: int
    reacquisition_direction: str
    reacquisition_swipes: int


def _measured_resource_viewport(
    navigation: dict[str, object],
    resource_id: str,
) -> int:
    viewports = navigation.get("resourceViewports")
    if not isinstance(viewports, dict):
        raise RuntimeError("Talent grant inventory emitted no measured resource viewports")
    viewport = viewports.get(resource_id)
    if type(viewport) is not int or viewport < 0:
        raise RuntimeError(
            f"Talent grant inventory emitted no measured viewport for {resource_id!r}"
        )
    return viewport


def _measured_talent_resource_detail(
    navigation: dict[str, object],
    resource_id: str,
) -> tuple[str, ...]:
    details = navigation.get("resourceDetails")
    if not isinstance(details, dict):
        raise RuntimeError("Talent grant inventory emitted no exact resource details")
    detail = details.get(resource_id)
    if (
        not isinstance(detail, tuple)
        or not detail
        or any(not isinstance(value, str) or not value for value in detail)
    ):
        raise RuntimeError(
            f"Talent grant inventory emitted no exact detail for {resource_id!r}"
        )
    return detail


def _validated_talent_navigation_end(
    navigation: dict[str, object],
    current_viewport: int,
) -> int:
    end_viewport = navigation.get("endViewport")
    viewports = navigation.get("resourceViewports")
    if (
        type(end_viewport) is not int
        or end_viewport < 0
        or end_viewport > 40
        or type(current_viewport) is not int
        or current_viewport < 0
        or current_viewport > end_viewport
        or not isinstance(viewports, dict)
        or any(
            type(viewport) is not int
            or viewport < 0
            or viewport > end_viewport
            for viewport in viewports.values()
        )
    ):
        raise RuntimeError(
            "Talent grant inventory navigation is not an exact bounded scan topology"
        )
    return end_viewport


def reacquire_exact_talent_state_group(
    device: shared.Device,
    resource_ids: tuple[str, ...],
    current_viewport: int,
    target_viewport: int,
    scan_end_viewport: int,
    *,
    evidence_prefix: str,
    measured_distance_ratio: float = 0.68,
    reacquisition_distance_ratio: float = 0.22,
    max_empty_hierarchy_reads: int = 3,
    max_system_ui_dismissals: int = 3,
) -> TalentStateGroupSnapshot:
    """Reacquire one scan-proven state group after a bounded measured move.

    A measured move can land on a different physical offset after a refreshed
    list or a prior small-gesture compensation.  The initial measured move
    therefore remains the fast path, while a missing target authorizes at most
    the same absolute measured delta again in the requested direction.  Each
    compensation gesture is followed by a fresh dump.  Empty hierarchies and
    dismissed system UI retain independent retry budgets and never consume
    that geometric bound.
    """
    if (
        not resource_ids
        or len(resource_ids) != len(set(resource_ids))
        or any(not resource_id for resource_id in resource_ids)
        or type(max_empty_hierarchy_reads) is not int
        or max_empty_hierarchy_reads < 0
        or type(max_system_ui_dismissals) is not int
        or max_system_ui_dismissals < 0
    ):
        raise ValueError(
            "Exact Talent group IDs and separate nonnegative retry bounds are required"
        )
    if (
        type(scan_end_viewport) is not int
        or scan_end_viewport < 0
        or scan_end_viewport > 40
        or type(current_viewport) is not int
        or current_viewport < 0
        or current_viewport > scan_end_viewport
        or type(target_viewport) is not int
        or target_viewport < 0
        or target_viewport > scan_end_viewport
    ):
        raise ValueError("Talent group viewports must belong to the scan topology")

    measured_delta = target_viewport - current_viewport
    reacquisition_direction = (
        "forward"
        if measured_delta > 0
        else "reverse"
        if measured_delta < 0
        else "none"
    )
    reacquisition_bound = abs(measured_delta)
    move_between_measured_viewports(
        device,
        current_viewport,
        target_viewport,
        distance_ratio=measured_distance_ratio,
        delay_seconds=0.0,
    )
    reacquisition_swipes = 0
    empty_hierarchy_reads = 0
    system_ui_dismissals = 0
    while reacquisition_swipes <= reacquisition_bound:
        nodes = fresh_hierarchy_timed(device, [])
        if not nodes:
            empty_hierarchy_reads += 1
            if empty_hierarchy_reads > max_empty_hierarchy_reads:
                device.capture(f"{evidence_prefix}-empty-hierarchy-exhausted")
                raise RuntimeError(
                    "Grouped Talent state exhausted its separate transient "
                    f"empty-hierarchy budget of {max_empty_hierarchy_reads} reads"
                )
            time.sleep(0.75)
            continue
        matches = {
            resource_id: [
                node for node in nodes if _exact_resource_id(node) == resource_id
            ]
            for resource_id in resource_ids
        }
        duplicates = {
            resource_id: len(candidates)
            for resource_id, candidates in matches.items()
            if len(candidates) > 1
        }
        if duplicates:
            device.capture(f"{evidence_prefix}-cardinality-invalid")
            if len(duplicates) == 1:
                resource_id, cardinality = next(iter(duplicates.items()))
                detail = f"{resource_id!r} has cardinality {cardinality}"
            else:
                detail = repr(duplicates)
            raise RuntimeError(
                "Grouped Talent state exact resource cardinality was ambiguous: "
                f"{detail}"
            )
        missing = tuple(
            resource_id
            for resource_id, candidates in matches.items()
            if not candidates
        )
        if not missing:
            return TalentStateGroupSnapshot(
                nodes=nodes,
                resources={
                    resource_id: candidates[0]
                    for resource_id, candidates in matches.items()
                },
                logical_viewport=target_viewport,
                reacquisition_direction=reacquisition_direction,
                reacquisition_swipes=reacquisition_swipes,
            )
        if device.dismiss_system_ui_anr(nodes):
            system_ui_dismissals += 1
            if system_ui_dismissals > max_system_ui_dismissals:
                device.capture(f"{evidence_prefix}-system-ui-exhausted")
                raise RuntimeError(
                    "Grouped Talent state exhausted its separate system-UI dismissal "
                    f"budget of {max_system_ui_dismissals}"
                )
            time.sleep(2)
            continue
        if reacquisition_swipes >= reacquisition_bound:
            break
        if reacquisition_direction == "reverse":
            device.swipe_down(distance_ratio=reacquisition_distance_ratio)
        elif reacquisition_direction == "forward":
            device.swipe_up(distance_ratio=reacquisition_distance_ratio)
        else:
            break
        reacquisition_swipes += 1

    device.capture(f"{evidence_prefix}-unavailable")
    raise RuntimeError(
        "Grouped Talent state could not reacquire exact resources "
        f"{missing!r} within the scan-proven {reacquisition_bound}-swipe "
        f"{reacquisition_direction} compensation bound"
    )


def tap_exact_measured_talent_resource(
    device: shared.Device,
    resource_id: str,
    navigation: dict[str, object],
    current_viewport: int,
    *,
    evidence_prefix: str,
) -> int:
    """Move to a measured viewport, reacquire one exact node, then tap it."""
    target_viewport = _measured_resource_viewport(navigation, resource_id)
    scan_end_viewport = _validated_talent_navigation_end(
        navigation,
        current_viewport,
    )
    snapshot = reacquire_exact_talent_state_group(
        device,
        (resource_id,),
        current_viewport,
        target_viewport,
        scan_end_viewport,
        evidence_prefix=evidence_prefix,
    )
    node = snapshot.resources[resource_id]
    if any(
        _is_exact_tokenized_resource_id(resource_id, prefix)
        for prefix in TALENT_GRANT_OPTION_PREFIX.values()
    ):
        if not _talent_option_has_exact_dynamic_slot(node):
            device.capture(f"{evidence_prefix}-slot-state-invalid")
            raise RuntimeError(
                f"Measured Talent resource {resource_id!r} exposed an invalid "
                "exact slot decorator"
            )
        expected_detail = _measured_talent_resource_detail(navigation, resource_id)
        if _talent_option_identity_values(node) != expected_detail:
            device.capture(f"{evidence_prefix}-detail-drift")
            raise RuntimeError(
                f"Measured Talent resource {resource_id!r} changed exact option detail"
            )
    if (
        node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node)
    ):
        device.capture(f"{evidence_prefix}-not-tappable")
        raise RuntimeError(f"Measured Talent resource {resource_id!r} was not tappable")
    device.shell("input", "tap", *(str(value) for value in node.center))
    return snapshot.logical_viewport


def read_talent_grant_grouped_state(
    device: shared.Device,
    expected_kind: str,
    baseline: TalentGrantSurface,
    navigation: dict[str, object],
    current_viewport: int,
    *,
    expected_selected_option_ids: tuple[str, ...],
    expected_unselected_option_ids: tuple[str, ...] = (),
    expected_completion_enabled: bool,
    evidence_prefix: str,
) -> tuple[TalentGrantMutableState, int]:
    """Read fresh exact state groups without rescanning the immutable catalog."""
    expected_selected_order = tuple(expected_selected_option_ids)
    expected_selected = tuple(sorted(expected_selected_order))
    expected_unselected = tuple(sorted(expected_unselected_option_ids))
    if (
        len(expected_selected_order) != len(set(expected_selected_order))
        or len(expected_unselected) != len(set(expected_unselected))
        or bool(set(expected_selected) & set(expected_unselected))
        or not set(expected_selected).issubset(baseline.option_ids)
        or not set(expected_unselected).issubset(baseline.option_ids)
    ):
        raise RuntimeError("Grouped Talent state expected IDs are not a valid catalog partition")
    expected_selected_slots = {
        resource_id: ordinal
        for ordinal, resource_id in enumerate(expected_selected_order, start=1)
    }
    authority_id = "creation-prerequisite-talent-grant-authority"
    digest_id = "creation-prerequisite-talent-grant-digest"
    completion_id = "creation-prerequisite-talent-grant-complete"
    required_ids = (
        authority_id,
        digest_id,
        *expected_selected,
        *expected_unselected,
        completion_id,
    )
    scan_end_viewport = _validated_talent_navigation_end(
        navigation,
        current_viewport,
    )
    expected_option_details = {
        resource_id: _measured_talent_resource_detail(navigation, resource_id)
        for resource_id in (*expected_selected, *expected_unselected)
    }
    grouped: dict[int, list[str]] = {}
    for resource_id in required_ids:
        grouped.setdefault(
            _measured_resource_viewport(navigation, resource_id),
            [],
        ).append(resource_id)

    observed: dict[str, shared.UiNode] = {}
    for viewport in sorted(grouped):
        snapshot = reacquire_exact_talent_state_group(
            device,
            tuple(grouped[viewport]),
            current_viewport,
            viewport,
            scan_end_viewport,
            evidence_prefix=f"{evidence_prefix}-viewport-{viewport}",
        )
        observed.update(snapshot.resources)
        current_viewport = snapshot.logical_viewport

    authority_values = _accessible_values(observed[authority_id])
    counts = {
        (
            int(match.group("selected")),
            int(match.group("required")),
            match.group("kind"),
        )
        for value in authority_values
        for match in TALENT_GRANT_REQUIRED.finditer(value)
    }
    digest_values = set(_accessible_values(observed[digest_id]))
    if len(counts) != 1 or digest_values != {baseline.grant_digest}:
        device.capture(f"{evidence_prefix}-authority-drift")
        raise RuntimeError(
            "Grouped Talent state changed immutable authority: "
            f"counts={sorted(counts)!r}, digests={sorted(digest_values)!r}"
        )
    selected_count, required_count, observed_kind = next(iter(counts))
    if observed_kind != expected_kind or required_count != baseline.required_count:
        device.capture(f"{evidence_prefix}-kind-count-drift")
        raise RuntimeError(
            "Grouped Talent state changed kind or required count: "
            f"kind={observed_kind!r}, required={required_count}"
        )

    for resource_id in expected_selected:
        node = observed[resource_id]
        if (
            _talent_option_identity_values(node)
            != expected_option_details[resource_id]
        ):
            device.capture(f"{evidence_prefix}-{resource_id}-detail-drift")
            raise RuntimeError(
                f"Grouped Talent state changed exact option detail for {resource_id!r}"
            )
        if (
            not _talent_option_matches_exact_slot(
                node,
                expected_selected_slots[resource_id],
            )
            or node.attributes.get("enabled") != "true"
            or node.attributes.get("clickable") != "true"
            or not device.node_has_tappable_bounds(node)
        ):
            device.capture(f"{evidence_prefix}-{resource_id}-selected-state-invalid")
            raise RuntimeError(
                f"Grouped Talent state did not expose enabled exact selection {resource_id!r}"
            )
    for resource_id in expected_unselected:
        node = observed[resource_id]
        if (
            _talent_option_identity_values(node)
            != expected_option_details[resource_id]
        ):
            device.capture(f"{evidence_prefix}-{resource_id}-detail-drift")
            raise RuntimeError(
                f"Grouped Talent state changed exact option detail for {resource_id!r}"
            )
        if (
            not _talent_option_matches_exact_slot(node, None)
            or node.attributes.get("enabled") != "true"
            or node.attributes.get("clickable") != "true"
            or not device.node_has_tappable_bounds(node)
        ):
            device.capture(f"{evidence_prefix}-{resource_id}-unselected-state-invalid")
            raise RuntimeError(
                f"Grouped Talent state did not expose enabled exact unselection {resource_id!r}"
            )

    completion = observed[completion_id]
    completion_enabled = completion.attributes.get("enabled") == "true"
    if (
        selected_count != len(expected_selected)
        or completion_enabled != expected_completion_enabled
        or completion_enabled != (selected_count == required_count)
    ):
        device.capture(f"{evidence_prefix}-selection-count-invalid")
        raise RuntimeError(
            "Grouped Talent state did not match exact selection/completion parity: "
            f"selected={selected_count}, expected={len(expected_selected)}, "
            f"required={required_count}, completion={completion_enabled}"
        )
    return (
        TalentGrantMutableState(
            selected_count=selected_count,
            selected_option_ids=expected_selected,
            completion_enabled=completion_enabled,
        ),
        current_viewport,
    )


def tap_exact_talent_grant_option(
    device: shared.Device,
    expected_kind: str,
    resource_id: str,
    *,
    max_scrolls: int = 40,
) -> None:
    if expected_kind not in TALENT_GRANT_OPTION_PREFIX:
        raise RuntimeError(f"Unsupported Talent grant kind {expected_kind!r}")
    prefix = TALENT_GRANT_OPTION_PREFIX[expected_kind]
    if not _is_exact_tokenized_resource_id(resource_id, prefix):
        raise RuntimeError(
            f"Malformed exact {expected_kind} Talent grant option ID {resource_id!r}"
        )
    node = device.wait_exact_resource_id_bidirectional(
        resource_id,
        timeout=90,
        backward_scrolls=max_scrolls,
        forward_scrolls=max_scrolls,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-talent-grant-option",
        surface_name=f"Exact {expected_kind} Talent grant option",
    )
    if (
        node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node)
    ):
        device.capture("creation-prerequisite-talent-grant-option-not-tappable")
        raise RuntimeError(
            f"Exact {expected_kind} Talent grant option {resource_id!r} was not tappable"
        )
    device.shell("input", "tap", *(str(value) for value in node.center))
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-talent-grant-refresh",
        surface_name=f"Refreshed {expected_kind} Talent grant route",
    )


def tap_exact_talent_option(
    device: shared.Device,
    resource_id: str,
    *,
    max_scrolls: int = 40,
) -> None:
    prefix = "creation-prerequisite-talent-option-"
    if not _is_exact_tokenized_resource_id(resource_id, prefix):
        raise RuntimeError(f"Malformed exact Talent option ID {resource_id!r}")
    node = device.wait_exact_resource_id_bidirectional(
        resource_id,
        timeout=90,
        backward_scrolls=max_scrolls,
        forward_scrolls=max_scrolls,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-talent-option",
        surface_name="Exact selected Talent option",
    )
    if (
        node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node)
    ):
        device.capture("creation-prerequisite-talent-option-not-tappable")
        raise RuntimeError(f"Exact Talent option {resource_id!r} was not tappable")
    device.shell("input", "tap", *(str(value) for value in node.center))
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-talent-grant-route",
        surface_name="Selected Talent grant route",
    )


class TalentGrantChoiceProof(NamedTuple):
    receipt: dict[str, object]
    navigation: dict[str, object]
    current_viewport: int


def choose_and_prove_talent_grant(
    device: shared.Device,
    expected_kind: str,
    talent_option_id: str,
    talent_option_navigation: dict[str, object],
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id_prefix: str,
) -> TalentGrantChoiceProof:
    navigation: dict[str, object] = {}
    initial = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=f"{scan_id_prefix}-initial",
        navigation_out=navigation,
    )
    if initial.selected_count != 0 or initial.completion_enabled:
        device.capture(f"{scan_id_prefix}-initial-selection-not-empty")
        raise RuntimeError(
            f"Fresh {expected_kind} Talent prompt retained stale grant selections: {initial!r}"
        )
    device.capture(f"{scan_id_prefix}-initial-zero")
    available = tuple(
        resource_id
        for resource_id in initial.enabled_option_ids
        if resource_id not in initial.selected_option_ids
    )
    if len(available) < initial.required_count:
        raise RuntimeError(
            f"{expected_kind} Talent prompt exposed too few enabled exact choices: "
            f"required={initial.required_count}, available={available!r}"
        )
    chosen = tuple(sorted(available)[: initial.required_count])
    current_viewport = int(navigation["endViewport"])
    for resource_id in chosen:
        current_viewport = tap_exact_measured_talent_resource(
            device,
            resource_id,
            navigation,
            current_viewport,
            evidence_prefix=f"{scan_id_prefix}-choose-{resource_id}",
        )
        device.wait_for_single_exact_resource_id(
            "creation-prerequisite-talent-grant-page",
            timeout=45,
            evidence_prefix=f"{scan_id_prefix}-choice-refresh",
            surface_name=f"Refreshed {expected_kind} Talent grant route",
        )
    complete_state, current_viewport = read_talent_grant_grouped_state(
        device,
        expected_kind,
        initial,
        navigation,
        current_viewport,
        expected_selected_option_ids=chosen,
        expected_completion_enabled=True,
        evidence_prefix=f"{scan_id_prefix}-complete",
    )
    if (
        complete_state.selected_option_ids != chosen
        or complete_state.selected_count != initial.required_count
        or not complete_state.completion_enabled
    ):
        device.capture(f"{scan_id_prefix}-selection-mismatch")
        raise RuntimeError(
            f"{expected_kind} exact selection/capacity changed authority: "
            f"initial={initial!r}, complete={complete_state!r}, chosen={chosen!r}"
        )

    # A native Back followed by the exact same Talent option must preserve the
    # in-memory typed choices.  Toggling one selected row off and on again then
    # proves explicit reset/reselection without any implicit default.
    device.back()
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix=f"{scan_id_prefix}-back-to-talent",
        surface_name="Talent detail route after grant Back",
    )
    talent_viewport = int(talent_option_navigation["endViewport"])
    tap_exact_measured_talent_resource(
        device,
        talent_option_id,
        talent_option_navigation,
        talent_viewport,
        evidence_prefix=f"{scan_id_prefix}-reenter-talent-option",
    )
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix=f"{scan_id_prefix}-preserved-route",
        surface_name=f"Preserved {expected_kind} Talent grant route",
    )
    preserved_state, current_viewport = read_talent_grant_grouped_state(
        device,
        expected_kind,
        initial,
        navigation,
        current_viewport,
        expected_selected_option_ids=chosen,
        expected_completion_enabled=True,
        evidence_prefix=f"{scan_id_prefix}-preserved",
    )
    if preserved_state != complete_state:
        device.capture(f"{scan_id_prefix}-back-preservation-mismatch")
        raise RuntimeError(
            f"{expected_kind} native Back/re-enter did not preserve the exact draft: "
            f"complete={complete_state!r}, preserved={preserved_state!r}"
        )

    reset_id = chosen[0]
    current_viewport = tap_exact_measured_talent_resource(
        device,
        reset_id,
        navigation,
        current_viewport,
        evidence_prefix=f"{scan_id_prefix}-explicit-reset-tap",
    )
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix=f"{scan_id_prefix}-explicit-reset-route",
        surface_name=f"Reset {expected_kind} Talent grant route",
    )
    expected_after_reset = tuple(resource_id for resource_id in chosen if resource_id != reset_id)
    incomplete_state, current_viewport = read_talent_grant_grouped_state(
        device,
        expected_kind,
        initial,
        navigation,
        current_viewport,
        expected_selected_option_ids=expected_after_reset,
        expected_unselected_option_ids=(reset_id,),
        expected_completion_enabled=False,
        evidence_prefix=f"{scan_id_prefix}-explicit-reset",
    )
    if (
        incomplete_state.selected_option_ids != expected_after_reset
        or incomplete_state.completion_enabled
        or incomplete_state.selected_count != initial.required_count - 1
    ):
        device.capture(f"{scan_id_prefix}-explicit-reset-mismatch")
        raise RuntimeError(
            f"{expected_kind} explicit deselection did not reopen the exact prompt: "
            f"{incomplete_state!r}"
        )
    current_viewport = tap_exact_measured_talent_resource(
        device,
        reset_id,
        navigation,
        current_viewport,
        evidence_prefix=f"{scan_id_prefix}-explicit-reselect-tap",
    )
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix=f"{scan_id_prefix}-explicit-reselect-route",
        surface_name=f"Reselected {expected_kind} Talent grant route",
    )
    restored_state, current_viewport = read_talent_grant_grouped_state(
        device,
        expected_kind,
        initial,
        navigation,
        current_viewport,
        expected_selected_option_ids=(*expected_after_reset, reset_id),
        expected_completion_enabled=True,
        evidence_prefix=f"{scan_id_prefix}-explicit-reselect",
    )
    if restored_state != complete_state:
        device.capture(f"{scan_id_prefix}-explicit-reselect-mismatch")
        raise RuntimeError(
            f"{expected_kind} explicit reselection did not restore exact authority: "
            f"expected={complete_state!r}, actual={restored_state!r}"
        )
    device.capture(f"{scan_id_prefix}-complete-exact")
    return TalentGrantChoiceProof(
        receipt={
            "kind": expected_kind,
            "talentOptionAutomationId": talent_option_id,
            "grantDigest": initial.grant_digest,
            "requiredCount": initial.required_count,
            "allOptionAutomationIds": list(initial.option_ids),
            "selectedOptionAutomationIds": list(restored_state.selected_option_ids),
            "backPreservedSelection": True,
            "explicitDeselectReselect": True,
        },
        navigation=navigation,
        current_viewport=current_viewport,
    )


def complete_talent_grant_to_prerequisite(
    device: shared.Device,
    navigation: dict[str, object],
    current_viewport: int,
) -> None:
    tap_exact_measured_talent_resource(
        device,
        "creation-prerequisite-talent-grant-complete",
        navigation,
        current_viewport,
        evidence_prefix="creation-prerequisite-talent-grant-complete",
    )
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-talent-after-grant",
        surface_name="Talent detail route after exact grant completion",
    )
    device.back()
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-after-talent-grant",
        surface_name="Creation prerequisite route after Talent grant completion",
    )


def require_exact_preview_talent_grant_plan(
    device: shared.Device,
    expected_kind: str,
    selected_option_ids: tuple[str, ...],
    *,
    max_scrolls: int = 40,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str,
) -> str:
    option_prefix = TALENT_GRANT_OPTION_PREFIX[expected_kind]
    preview_prefix = TALENT_GRANT_PREVIEW_PREFIX[expected_kind]
    opposite_prefix = TALENT_GRANT_PREVIEW_PREFIX[
        next(kind for kind in TALENT_GRANT_KINDS if kind != expected_kind)
    ]
    expected_ids = {
        preview_prefix + resource_id[len(option_prefix) :]
        for resource_id in selected_option_ids
        if _is_exact_tokenized_resource_id(resource_id, option_prefix)
    }
    if len(expected_ids) != len(selected_option_ids) or not expected_ids:
        raise RuntimeError(
            f"Expected {expected_kind} preview IDs were not exact: {selected_option_ids!r}"
        )
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    plan_digest = canonical_digest(
        device,
        "creation-prerequisite-preview-talent-grant-plan-digest",
        scroll=True,
    )
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    screens = scan_forward_until_stable(
        device,
        scan_id=scan_id,
        max_scrolls=max_scrolls,
        distance_ratio=0.22,
        observer=scan_observer,
    )
    observed_ids: set[str] = set()
    opposite_ids: set[str] = set()
    malformed_ids: set[str] = set()
    duplicate_ids: set[str] = set()
    for nodes in screens:
        screen_ids: list[str] = []
        for node in nodes:
            resource_id = _exact_resource_id(node)
            if resource_id.startswith(opposite_prefix):
                opposite_ids.add(resource_id)
            if not resource_id.startswith(preview_prefix):
                continue
            screen_ids.append(resource_id)
            if _is_exact_tokenized_resource_id(resource_id, preview_prefix):
                observed_ids.add(resource_id)
            else:
                malformed_ids.add(resource_id)
        duplicate_ids.update(
            resource_id
            for resource_id in set(screen_ids)
            if screen_ids.count(resource_id) > 1
        )
    if malformed_ids or opposite_ids or duplicate_ids or observed_ids != expected_ids:
        device.capture(f"{scan_id}-mismatch")
        raise RuntimeError(
            f"{expected_kind} preview grant plan was not exact: expected={sorted(expected_ids)!r}, "
            f"observed={sorted(observed_ids)!r}, opposite={sorted(opposite_ids)!r}, "
            f"malformed={sorted(malformed_ids)!r}, duplicates={sorted(duplicate_ids)!r}"
        )
    return plan_digest


def require_restored_talent_grant(
    device: shared.Device,
    talent_option_id: str,
    expected_kind: str,
    expected_grant_digest: str,
    expected_selected_option_ids: tuple[str, ...],
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str,
) -> TalentGrantSurface:
    row = device.wait_exact_resource_id_bidirectional(
        "creation-prerequisite-talent-selection",
        timeout=90,
        backward_scrolls=40,
        forward_scrolls=40,
        scroll_distance_ratio=0.22,
        evidence_prefix=f"{scan_id}-talent-selection",
        surface_name="Restored Talent selection row",
    )
    device.shell("input", "tap", *(str(value) for value in row.center))
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix=f"{scan_id}-talent-route",
        surface_name="Restored Talent detail route",
    )
    tap_exact_talent_option(device, talent_option_id)
    surface = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=scan_id,
    )
    if (
        surface.grant_digest != expected_grant_digest
        or surface.selected_option_ids != expected_selected_option_ids
        or not surface.completion_enabled
    ):
        device.capture(f"{scan_id}-restored-grant-mismatch")
        raise RuntimeError(
            "Persisted Talent grant was not restored exactly: "
            f"expectedDigest={expected_grant_digest!r}, "
            f"actualDigest={surface.grant_digest!r}, "
            f"expectedIds={expected_selected_option_ids!r}, "
            f"actualIds={surface.selected_option_ids!r}"
        )
    device.back()
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix=f"{scan_id}-back-to-talent",
        surface_name="Talent detail after restored grant proof",
    )
    device.back()
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-page",
        timeout=45,
        evidence_prefix=f"{scan_id}-back-to-prerequisite",
        surface_name="Prerequisite route after restored grant proof",
    )
    return surface


def exact_current_authority_option_ids(
    nodes: list[shared.UiNode],
    prefix: str,
    is_tappable: Callable[[shared.UiNode], bool],
) -> list[str]:
    candidates: list[str] = []
    for node in nodes:
        resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
        accessible_values = (
            node.attributes.get("text", "").casefold(),
            node.attributes.get("content-desc", "").casefold(),
        )
        if (
            resource_id.startswith(prefix)
            and any("current typed draft selection" in value for value in accessible_values)
            and node.attributes.get("enabled") == "true"
            and node.attributes.get("clickable") == "true"
            and is_tappable(node)
        ):
            candidates.append(resource_id)
    return candidates


def assert_exact_restored_authority_option_ids(
    candidate_ids: set[str],
    expected_resource_id: str,
    *,
    duplicate_resource_id: bool,
) -> None:
    if duplicate_resource_id or candidate_ids != {expected_resource_id}:
        raise RuntimeError(
            "Restored Core draft did not mark exactly the selected authority option: "
            f"expected={expected_resource_id!r}, candidates={sorted(candidate_ids)!r}, "
            f"duplicateResourceId={duplicate_resource_id}"
        )


def require_exact_restored_authority_option(
    device: shared.Device,
    category: str,
    expected_resource_id: str,
    expected_selection_id: str,
    *,
    max_scrolls: int = 40,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str | None = None,
) -> None:
    # Persisted-draft authority is read from the bottom of the page immediately
    # before this check. Re-establish the page origin before resolving the
    # selection id, which is intentionally a forward-only exact lookup.
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    restored_selection_id = node_text(
        device,
        f"creation-prerequisite-{category}-selection-id",
        scroll=True,
    ).strip()
    if restored_selection_id != expected_selection_id:
        device.capture(f"creation-prerequisite-{category}-selection-id-mismatch")
        raise RuntimeError(
            f"Restored {category} SelectionId changed: "
            f"expected={expected_selection_id!r}, actual={restored_selection_id!r}"
        )

    selection_row = device.wait_exact_resource_id_bidirectional(
        f"creation-prerequisite-{category}-selection",
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix=f"restored-{category}-selection",
        surface_name=f"Restored {category} selection",
    )
    x, y = selection_row.center
    device.shell("input", "tap", str(x), str(y))
    device.wait(f"creation-prerequisite-{category}-page", timeout=45)
    prefix = f"creation-prerequisite-{category}-option-"
    candidate_ids: set[str] = set()
    duplicate_resource_id = False
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    screens = scan_forward_until_stable(
        device,
        scan_id=scan_id or f"restored-authority-option-{category}",
        max_scrolls=max_scrolls,
        distance_ratio=0.22,
        observer=scan_observer,
    )
    for nodes in screens:
        screen_ids = exact_current_authority_option_ids(
            nodes,
            prefix,
            device.node_has_tappable_bounds,
        )
        if len(screen_ids) != len(set(screen_ids)):
            duplicate_resource_id = True
        candidate_ids.update(screen_ids)
    try:
        assert_exact_restored_authority_option_ids(
            candidate_ids,
            expected_resource_id,
            duplicate_resource_id=duplicate_resource_id,
        )
    except RuntimeError:
        device.capture(f"creation-prerequisite-{category}-restored-option-mismatch")
        raise
    device.back()
    device.wait("creation-prerequisite-page", timeout=45)


def open_resources(device: shared.Device) -> None:
    row = device.wait_exact_resource_id_bidirectional(
        "creation-stage-resources",
        timeout=180,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-resources-stage",
        surface_name="Core-authoritative Resources stage",
    )
    if row.attributes.get("enabled") != "true" or row.attributes.get("clickable") != "true":
        device.capture("creation-resources-stage-disabled")
        raise RuntimeError("Core-authoritative Resources stage was not enabled")
    device.shell("input", "tap", *(str(value) for value in row.center))
    device.wait("creation-resources-page", timeout=60)
    shared.reset_scroll_to_top(device, swipes=22)


def read_resources_binding(device: shared.Device) -> dict[str, object]:
    authority = {
        "contentRevision": nonnegative_integer(
            device,
            "creation-resources-binding-content-revision",
            scroll=True,
        ),
        "savedRevision": nonnegative_integer(
            device,
            "creation-resources-binding-saved-revision",
            scroll=True,
        ),
        "snapshotDigest": canonical_digest(
            device,
            "creation-resources-binding-snapshot-digest",
            scroll=True,
        ),
        "rawCharacterXmlDigest": canonical_digest(
            device,
            "creation-resources-binding-raw-character-xml-digest",
            scroll=True,
        ),
        "auxiliaryStateDigest": canonical_auxiliary_state_digest(
            device,
            "creation-resources-binding-auxiliary-state-digest",
            scroll=True,
        ),
        "prerequisiteDraftDigest": canonical_digest(
            device,
            "creation-resources-binding-prerequisite-draft-digest",
            scroll=True,
        ),
        "authorityDigest": canonical_digest(
            device,
            "creation-resources-authority-digest",
            scroll=True,
        ),
        "priorityNuyen": nonnegative_integer(
            device,
            "creation-resources-budget-priority-nuyen",
            scroll=True,
        ),
        "totalStartingNuyen": nonnegative_integer(
            device,
            "creation-resources-budget-total-starting-nuyen",
            scroll=True,
        ),
    }
    if authority["contentRevision"] <= 0:
        raise RuntimeError(f"Resources authority revision was invalid: {authority!r}")
    if authority["priorityNuyen"] != 50_000 or authority["totalStartingNuyen"] != 50_000:
        raise RuntimeError(
            "Priority Resources rank D did not project the canonical 50,000 nuyen grant: "
            f"{authority!r}"
        )
    return authority


def select_and_confirm_resources(
    device: shared.Device,
    before: dict[str, object],
) -> dict[str, object]:
    option_id = "creation-resources-option-karma-0"
    option = device.wait_exact_resource_id_bidirectional(
        option_id,
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-resources-option-karma-0",
        surface_name="Exact zero-conversion Resources option",
    )
    detail = option.attributes.get("content-desc", "")
    if (
        option.attributes.get("enabled") != "true"
        or option.attributes.get("clickable") != "true"
        or "0 Karma" not in detail
        or "50,000" not in detail
    ):
        device.capture("creation-resources-option-karma-0-invalid")
        raise RuntimeError(
            "Exact zero-conversion Resources option did not expose the 50,000 nuyen grant: "
            f"{detail!r}"
        )
    device.shell("input", "tap", *(str(value) for value in option.center))
    device.wait("creation-resources-preview-page", timeout=60)
    shared.reset_scroll_to_top(device, swipes=22)

    preview = {
        "optionId": node_text(
            device,
            "creation-resources-preview-option-id",
            scroll=True,
        ).strip(),
        "priorityGrant": nonnegative_integer(
            device,
            "creation-resources-preview-priority-grant",
            scroll=True,
        ),
        "totalStartingNuyen": nonnegative_integer(
            device,
            "creation-resources-preview-total-starting-nuyen",
            scroll=True,
        ),
        "previewDigest": canonical_digest(
            device,
            "creation-resources-preview-digest",
            scroll=True,
        ),
    }
    if (
        preview["optionId"] != "karma:0"
        or preview["priorityGrant"] != 50_000
        or preview["totalStartingNuyen"] != 50_000
    ):
        device.capture("creation-resources-preview-authority-mismatch")
        raise RuntimeError(f"Resources preview changed the exact rank-D grant: {preview!r}")

    confirm = device.wait_exact_resource_id_bidirectional(
        "creation-resources-confirm",
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-resources-explicit-confirm",
        surface_name="Explicit Resources confirmation",
    )
    if confirm.attributes.get("enabled") != "true" or confirm.attributes.get("clickable") != "true":
        device.capture("creation-resources-confirm-disabled")
        raise RuntimeError("Exact Resources preview was not explicitly confirmable")
    device.shell("input", "tap", *(str(value) for value in confirm.center))
    device.wait("creation-resources-confirm-receipt", timeout=90, scroll=True, max_scrolls=22)
    shared.reset_scroll_to_top(device, swipes=22)

    receipt = {
        "optionId": node_text(device, "creation-resources-receipt-option-id", scroll=True).strip(),
        "workspaceRevision": nonnegative_integer(
            device,
            "creation-resources-receipt-workspace-revision",
            scroll=True,
        ),
        "savedRevision": nonnegative_integer(
            device,
            "creation-resources-receipt-saved-revision",
            scroll=True,
        ),
        "draftRevision": nonnegative_integer(
            device,
            "creation-resources-receipt-draft-revision",
            scroll=True,
        ),
        "totalStartingNuyen": nonnegative_integer(
            device,
            "creation-resources-receipt-total-starting-nuyen",
            scroll=True,
        ),
        "previewDigest": canonical_digest(
            device,
            "creation-resources-receipt-preview-digest",
            scroll=True,
        ),
        "draftDigest": canonical_digest(
            device,
            "creation-resources-receipt-draft-digest",
            scroll=True,
        ),
        "receiptDigest": canonical_digest(
            device,
            "creation-resources-receipt-digest",
            scroll=True,
        ),
    }
    if (
        receipt["optionId"] != "karma:0"
        or receipt["workspaceRevision"] != before["contentRevision"] + 1
        or receipt["savedRevision"] != before["savedRevision"] + 1
        or receipt["draftRevision"] <= 0
        or receipt["totalStartingNuyen"] != 50_000
        or receipt["previewDigest"] != preview["previewDigest"]
    ):
        device.capture("creation-resources-receipt-authority-mismatch")
        raise RuntimeError(
            "Resources receipt was not bound to the exact preview and next workspace revision: "
            f"before={before!r}, preview={preview!r}, receipt={receipt!r}"
        )
    return {"preview": preview, "receipt": receipt}


def read_persisted_resources_authority(
    device: shared.Device,
    expected_receipt: dict[str, object],
) -> dict[str, object]:
    binding = read_resources_binding(device)
    saved = {
        "optionId": node_text(device, "creation-resources-saved-option-id", scroll=True).strip(),
        "draftRevision": nonnegative_integer(
            device,
            "creation-resources-saved-draft-revision",
            scroll=True,
        ),
        "draftDigest": canonical_digest(
            device,
            "creation-resources-saved-draft-digest",
            scroll=True,
        ),
    }
    if (
        binding["contentRevision"] != expected_receipt["workspaceRevision"]
        or binding["savedRevision"] != expected_receipt["savedRevision"]
        or saved["optionId"] != expected_receipt["optionId"]
        or saved["draftRevision"] != expected_receipt["draftRevision"]
        or saved["draftDigest"] != expected_receipt["draftDigest"]
    ):
        device.capture("creation-resources-persisted-authority-mismatch")
        raise RuntimeError(
            "Persisted Resources authority changed across reopen/restart: "
            f"expected={expected_receipt!r}, binding={binding!r}, saved={saved!r}"
        )
    return {"binding": binding, "savedDraft": saved}


def execute(args: argparse.Namespace, progress: ProgressRecorder) -> int:
    progress.advance("device-preflight-install")
    driver_path = Path(__file__).resolve()
    shared_path = Path(shared.__file__).resolve()
    priority_compatibility_path = driver_path.with_name(
        "run_api36_new_character_priority_e2e.py"
    )
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Creation prerequisite E2E requires API 36, got {api!r}")

    subprocess.run(
        [
            str(args.adb),
            "-s",
            args.serial,
            "install",
            "--no-streaming",
            "-r",
            str(args.apk.resolve()),
        ],
        check=True,
        timeout=300,
    )
    device.shell("pm", "clear", shared.PACKAGE)
    progress.advance("initial-navigation")
    initial_launch = shared.launch_app(device)
    progress.record_initial_milestone("app-cold-start-complete")
    phone_ui_locale = shared.record_phone_ui_locale_evidence(
        device,
        evidence_prefix="creation-prerequisite",
        required_route_resource_id="phone-runners",
    )
    progress.record_initial_milestone("phone-shell-locale-complete")
    create_character = device.tap_exact_resource_id_until_exact_resource_id(
        "home-new-runner",
        "dialog-action-create-character",
        evidence_prefix="new-runner-build-method-dialog",
        source_name="New runner control",
        target_name="Create-character build-method action",
        target_scroll_surface="dialog-surface",
        max_target_scrolls=16,
    )
    progress.record_initial_milestone("dialog-acquisition-complete")
    if (
        create_character.attributes.get("enabled") != "true"
        or create_character.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(create_character)
    ):
        device.capture("dialog-action-create-character-not-tappable")
        raise RuntimeError(
            "Exact create-character dialog action was not visible, enabled, and clickable"
        )
    progress.advance("initial-authority")
    clear_creation_bootstrap_timing_log(device)
    device.shell(
        "input",
        "tap",
        *(str(value) for value in create_character.center),
    )
    bootstrap_log_observation: dict[str, object] = {}
    bootstrap_logcat = wait_for_creation_bootstrap_timing_log(
        device,
        observation_out=bootstrap_log_observation,
    )
    progress.record_scan(bootstrap_log_observation)
    creation_bootstrap_timing = capture_creation_bootstrap_timing(
        device,
        logcat=bootstrap_logcat,
    )
    progress.record_initial_milestone("create-bootstrap-transaction-complete")
    progress.advance("dashboard-proof")
    transition_observation: dict[str, object] = {}
    transition_viewport: list[PriorityRankOrigin] = []
    transition_nodes = require_new_character_dialog_transition(
        device,
        timeout=30,
        observation_out=transition_observation,
        resolved_viewport_out=transition_viewport,
        fresh_first=True,
    )
    progress.record_scan(transition_observation)
    require_initial_creation_dashboard_snapshot(device, transition_nodes)
    if len(transition_viewport) != 1:
        raise RuntimeError(
            "Creation dashboard transition did not retain one exact resolved viewport"
        )
    progress.record_initial_milestone("dashboard-render-complete")
    progress.advance("authority-inventory")
    dashboard_authority_observation: dict[str, object] = {}
    resolved_dashboard_viewport: list[PriorityRankOrigin] = []
    authority_projection_waited = wait_creation_dashboard_authority(
        device,
        observation_out=dashboard_authority_observation,
        initial_observation=transition_viewport[0],
        resolved_viewport_out=resolved_dashboard_viewport,
        poll_delay_seconds=0.0,
    )
    progress.record_scan(dashboard_authority_observation)
    if len(resolved_dashboard_viewport) != 1:
        raise RuntimeError(
            "Creation dashboard authority wait did not retain one exact resolved viewport"
        )
    dashboard_scan = assert_uncreated_advanced_editor_gated(
        device,
        scan_observer=progress.record_scan,
        scan_id="advanced-editor-gate-initial",
    )
    dashboard_binding = dashboard_scan.binding
    method_reverse_swipe_bound = measured_reverse_reacquisition_bound(
        dashboard_scan.swipes,
        dashboard_scan.method_viewport,
    )
    move_between_measured_viewports(
        device,
        dashboard_scan.swipes,
        dashboard_scan.method_viewport,
        delay_seconds=0.0,
    )
    method_node, method_detail, _ = reacquire_exact_ready_creation_method(
        device,
        expected_detail=dashboard_scan.method_detail,
        max_swipes=method_reverse_swipe_bound,
    )
    ready_navigation = {
        "detail": method_detail,
        "clickable": True,
        "enabled": True,
        "authorityProjectionWaited": authority_projection_waited,
    }
    device.capture("creation-priority-core-bootstrap-ready")

    device.shell("input", "tap", *(str(value) for value in method_node.center))
    prerequisite_origin = wait_for_prerequisite_scan_origin(device)
    prerequisite_scan = scan_prerequisite_authority(
        device,
        initial_observation=prerequisite_origin,
        scan_observer=progress.record_scan,
    )
    prerequisite_values = prerequisite_scan.values
    prerequisite_binding = prerequisite_values["creation-prerequisite-binding"]
    prerequisite_binding_authority = require_prerequisite_binding(prerequisite_binding)
    prerequisite_snapshot_digest = prerequisite_values[
        "creation-prerequisite-snapshot-digest"
    ]
    prerequisite_digests = {
        "rawCharacterXml": prerequisite_values[
            "creation-prerequisite-raw-character-xml-digest"
        ],
        "auxiliaryState": prerequisite_values[
            "creation-prerequisite-auxiliary-state-digest"
        ],
        "authority": prerequisite_values[
            "creation-prerequisite-authority-digest"
        ],
    }
    require_binding_matches_canonical_digests(
        prerequisite_binding_authority,
        prerequisite_snapshot_digest,
        prerequisite_digests["authority"],
    )
    karma = prerequisite_values["creation-prerequisite-karma-budget"]
    karma_labels = PRIORITY_KARMA_LABELS_BY_LANGUAGE[
        str(phone_ui_locale["language"])
    ]
    cursor = 0
    for label in karma_labels:
        position = karma.find(label, cursor)
        if position < 0:
            raise RuntimeError(
                "Global Creation Karma omitted or reordered the exact localized "
                f"{phone_ui_locale['language']!r} label {label!r}: {karma!r}"
            )
        cursor = position + len(label)
    source_authority_digests = sorted(
        {
            prerequisite_values["creation-prerequisite-authority-digest"],
            prerequisite_values["creation-prerequisite-profile-inputs-digest"],
            prerequisite_values["creation-prerequisite-priorities-xml-digest"],
        }
    )

    progress.advance("priority-ranks")
    selected: dict[str, str] = {}
    if tuple(PRIORITY_PROOF_RANKS) != CATEGORIES:
        raise RuntimeError("Priority proof rank allocation does not cover the ordered categories")
    priority_category_navigation: dict[str, object] = {
        "viewportByCategory": prerequisite_scan.category_viewports,
        "currentViewport": prerequisite_scan.swipes,
        "lastCategory": None,
    }
    for category, expected_rank in PRIORITY_PROOF_RANKS.items():
        selected[category] = select_priority_rank(
            device,
            category,
            category_navigation=priority_category_navigation,
            expected_rank=expected_rank,
            scan_observer=progress.record_scan,
        )

    progress.advance("typed-authority-options")
    typed_selections: dict[str, str] = {}
    typed_selection_ids: dict[str, str] = {}
    shared.reset_scroll_to_top(device, swipes=22)
    device.tap(
        "creation-prerequisite-heritage-selection",
        scroll=True,
        max_scrolls=22,
    )
    device.wait("creation-prerequisite-heritage-page", timeout=45)
    typed_selections["heritage"] = tap_enabled_authority_option(
        device,
        "creation-prerequisite-heritage-option-",
        "Human",
        max_scrolls=40,
        scan_observer=progress.record_scan,
    )
    device.wait("creation-prerequisite-page", timeout=45)
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-heritage-selection",
        timeout=60,
        scroll=True,
        max_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-heritage-selection",
        surface_name="Typed Heritage selection row",
    )
    typed_selection_ids["heritage"] = node_text(
        device,
        "creation-prerequisite-heritage-selection-id",
        scroll=True,
    ).strip()
    if not typed_selection_ids["heritage"]:
        raise RuntimeError("Typed heritage SelectionId was not exposed by Core authority")

    device.tap(
        "creation-prerequisite-talent-selection",
        scroll=True,
        max_scrolls=22,
    )
    device.wait("creation-prerequisite-talent-page", timeout=45)
    active_talent_option_navigation: dict[str, object] = {}
    active_talent_option_id = tap_enabled_authority_option(
        device,
        "creation-prerequisite-talent-option-",
        ACTIVE_SKILL_TALENT_LABEL,
        max_scrolls=40,
        scan_observer=progress.record_scan,
        navigation_out=active_talent_option_navigation,
    )
    device.wait("creation-prerequisite-talent-grant-page", timeout=45)
    progress.advance("talent-active-skill-grant")
    active_grant_proof = choose_and_prove_talent_grant(
        device,
        "Active skills",
        active_talent_option_id,
        active_talent_option_navigation,
        scan_observer=progress.record_scan,
        scan_id_prefix="talent-active-skill-grant",
    )
    active_grant = active_grant_proof.receipt
    active_selected_option_ids = tuple(active_grant["selectedOptionAutomationIds"])
    complete_talent_grant_to_prerequisite(
        device,
        active_grant_proof.navigation,
        active_grant_proof.current_viewport,
    )
    active_talent_selection_id = node_text(
        device,
        "creation-prerequisite-talent-selection-id",
        scroll=True,
    ).strip()
    if not active_talent_selection_id:
        raise RuntimeError("Active-skill Talent SelectionId was not exposed by Core authority")

    progress.advance("talent-active-preview")
    device.tap("creation-prerequisite-prepare-preview", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-preview-page", timeout=60)
    active_preview_digest = canonical_digest(
        device,
        "creation-prerequisite-preview-digest",
        scroll=True,
    )
    active_plan_digest = require_exact_preview_talent_grant_plan(
        device,
        "Active skills",
        active_selected_option_ids,
        scan_observer=progress.record_scan,
        scan_id="talent-active-skill-preview-plan",
    )
    device.capture("creation-prerequisite-talent-active-skill-preview")
    device.back()
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-page",
        timeout=45,
        evidence_prefix="talent-active-skill-preview-back",
        surface_name="Prerequisite route after active-skill preview",
    )

    # Changing the selected Talent must clear the prior active-skill slots.  The
    # new skill-group prompt is required to start at zero and is then subjected
    # to the same Back-preservation and explicit deselect/reselect proof.
    device.tap(
        "creation-prerequisite-talent-selection",
        scroll=True,
        max_scrolls=22,
    )
    device.wait("creation-prerequisite-talent-page", timeout=45)
    skill_group_option_navigation: dict[str, object] = {}
    typed_selections["talent"] = tap_enabled_authority_option(
        device,
        "creation-prerequisite-talent-option-",
        SKILL_GROUP_TALENT_LABEL,
        max_scrolls=40,
        scan_observer=progress.record_scan,
        navigation_out=skill_group_option_navigation,
    )
    device.wait("creation-prerequisite-talent-grant-page", timeout=45)
    progress.advance("talent-skill-group-grant")
    skill_group_grant_proof = choose_and_prove_talent_grant(
        device,
        "Skill groups",
        typed_selections["talent"],
        skill_group_option_navigation,
        scan_observer=progress.record_scan,
        scan_id_prefix="talent-skill-group-grant",
    )
    skill_group_grant = skill_group_grant_proof.receipt
    skill_group_selected_option_ids = tuple(
        skill_group_grant["selectedOptionAutomationIds"]
    )
    complete_talent_grant_to_prerequisite(
        device,
        skill_group_grant_proof.navigation,
        skill_group_grant_proof.current_viewport,
    )
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-selection",
        timeout=60,
        scroll=True,
        max_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-talent-selection",
        surface_name="Typed Talent selection row",
    )
    typed_selection_ids["talent"] = node_text(
        device,
        "creation-prerequisite-talent-selection-id",
        scroll=True,
    ).strip()
    if (
        not typed_selection_ids["talent"]
        or typed_selection_ids["talent"] == active_talent_selection_id
    ):
        raise RuntimeError(
            "Switching from active-skill to skill-group Talent did not bind a distinct "
            "Core SelectionId"
        )

    progress.advance("preview-confirm")
    # A plain Back from a category route preserves the exact in-memory typed rank choice.
    attributes_before = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    device.tap("creation-prerequisite-category-attributes", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-category-page", timeout=45)
    device.back()
    attributes_after = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    if attributes_after != attributes_before:
        raise RuntimeError("Back navigation did not restore the typed Attribute rank selection")

    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-attributes-disabled",
        timeout=60,
        scroll=True,
        max_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-attributes-disabled",
        surface_name="Core-disabled Attributes prerequisite row",
    )

    device.tap("creation-prerequisite-prepare-preview", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-preview-page", timeout=60)
    skill_group_plan_digest = require_exact_preview_talent_grant_plan(
        device,
        "Skill groups",
        skill_group_selected_option_ids,
        scan_observer=progress.record_scan,
        scan_id="talent-skill-group-preview-plan",
    )
    # The pushed preview route can inherit the prerequisite page's bottom offset.
    shared.reset_scroll_to_top(device, swipes=22)
    preview_digest = canonical_digest(
        device,
        "creation-prerequisite-preview-digest",
        scroll=True,
    )
    preview_binding_digests = {
        "rawCharacterXml": canonical_digest(
            device,
            "creation-prerequisite-preview-raw-character-xml-digest",
            scroll=True,
        ),
        "auxiliaryState": canonical_auxiliary_state_digest(
            device,
            "creation-prerequisite-preview-auxiliary-state-digest",
            scroll=True,
        ),
        "authority": canonical_digest(
            device,
            "creation-prerequisite-preview-authority-digest",
            scroll=True,
        ),
    }
    if preview_binding_digests != prerequisite_digests:
        raise RuntimeError(
            "Core preview binding changed from the selected prerequisite snapshot: "
            f"before={prerequisite_digests!r}, preview={preview_binding_digests!r}"
        )
    for category in CATEGORIES:
        device.wait(
            f"creation-prerequisite-preview-assignment-{category}",
            timeout=45,
            scroll=True,
            max_scrolls=22,
        )
    device.wait("creation-prerequisite-preview-heritage", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-preview-talent", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-preview-karma-budget", timeout=45, scroll=True, max_scrolls=22)
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-preview-attributes-ready",
        timeout=60,
        scroll=True,
        max_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-preview-attributes-ready",
        surface_name="Core-ready Attributes preview authority",
    )
    device.tap("creation-prerequisite-confirm", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-confirm-receipt", timeout=90, scroll=True, max_scrolls=22)
    # Confirmation replaces the deeply scrolled preview content in place. Read the
    # receipt and its ordered authority fields from a deterministic page origin.
    shared.reset_scroll_to_top(device, swipes=22)
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-confirmed",
        timeout=60,
        scroll=True,
        max_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-confirmed",
        surface_name="Explicit prerequisite confirmation state",
    )
    receipt_text = node_text(device, "creation-prerequisite-confirm-receipt", scroll=True)
    if re.search(r"(?:^|[. ·])false(?:$|[. ·])", receipt_text.casefold()) is None:
        raise RuntimeError(
            "Prerequisite receipt did not expose the invariant "
            f"CharacterDocumentChanged=false value: {receipt_text!r}"
        )
    confirmed_revisions = {
        "contentRevision": nonnegative_integer(
            device,
            "creation-prerequisite-receipt-content-revision",
            scroll=True,
        ),
        "savedRevision": nonnegative_integer(
            device,
            "creation-prerequisite-receipt-saved-revision",
            scroll=True,
        ),
        "draftRevision": nonnegative_integer(
            device,
            "creation-prerequisite-receipt-draft-revision",
            scroll=True,
        ),
    }
    confirmed_draft_digest = canonical_digest(
        device,
        "creation-prerequisite-receipt-draft-digest",
        scroll=True,
    )
    confirmed_binding_digests = {
        "rawCharacterXml": canonical_digest(
            device,
            "creation-prerequisite-receipt-raw-character-xml-digest",
            scroll=True,
        ),
        "auxiliaryState": canonical_auxiliary_state_digest(
            device,
            "creation-prerequisite-receipt-auxiliary-state-digest",
            scroll=True,
        ),
        "authority": canonical_digest(
            device,
            "creation-prerequisite-receipt-authority-digest",
            scroll=True,
        ),
    }
    if confirmed_revisions["contentRevision"] <= 0 or confirmed_revisions["draftRevision"] <= 0:
        raise RuntimeError(f"Prerequisite receipt revisions are invalid: {confirmed_revisions!r}")
    if confirmed_binding_digests["rawCharacterXml"] != preview_binding_digests["rawCharacterXml"]:
        raise RuntimeError("Auxiliary draft confirmation changed raw character XML authority")
    if confirmed_binding_digests["authority"] != preview_binding_digests["authority"]:
        raise RuntimeError("Auxiliary draft confirmation changed rules authority")
    if confirmed_binding_digests["auxiliaryState"] == preview_binding_digests["auxiliaryState"]:
        raise RuntimeError("Auxiliary draft confirmation did not change auxiliary-state authority")
    device.capture("creation-prerequisite-confirmed")
    device.tap("creation-prerequisite-back-to-build", scroll=True, max_scrolls=22)
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=22,
    )
    assert_uncreated_advanced_editor_gated(
        device,
        scan_observer=progress.record_scan,
        scan_id="advanced-editor-gate-post-confirm",
    )
    if node_text(device, "creation-wizard-binding", scroll=True) == dashboard_binding:
        raise RuntimeError("Atomic prerequisite confirmation did not refresh the wizard revision")

    # Prove the prerequisite receipt before the Resources write legitimately advances
    # the shared auxiliary-state revision.
    progress.advance("same-process-reopen")
    open_prerequisite(device)
    resumed_authority = read_persisted_prerequisite_authority(device)
    assert_persisted_prerequisite_authority(
        resumed_authority,
        confirmed_draft_digest,
        confirmed_binding_digests,
        confirmed_revisions["contentRevision"],
        confirmed_revisions["savedRevision"],
    )
    for category in ("heritage", "talent"):
        require_exact_restored_authority_option(
            device,
            category,
            typed_selections[category],
            typed_selection_ids[category],
            scan_observer=progress.record_scan,
            scan_id=f"same-process-restored-authority-option-{category}",
        )
    resumed_talent_grant = require_restored_talent_grant(
        device,
        typed_selections["talent"],
        "Skill groups",
        str(skill_group_grant["grantDigest"]),
        skill_group_selected_option_ids,
        scan_observer=progress.record_scan,
        scan_id="same-process-restored-talent-skill-group-grant",
    )
    resumed_attributes = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    if resumed_attributes != attributes_before:
        raise RuntimeError(
            "Confirmed prerequisite draft did not resume the exact localized Attribute "
            f"rank row: before={attributes_before!r}, resumed={resumed_attributes!r}"
        )

    device.back()
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=22,
    )
    progress.advance("resources-preview-confirm")
    open_resources(device)
    resources_before = read_resources_binding(device)
    resources_confirmation = select_and_confirm_resources(device, resources_before)
    resources_receipt = resources_confirmation["receipt"]
    device.capture("creation-resources-confirmed")
    device.tap("creation-resources-reopen", scroll=True, max_scrolls=22)
    device.wait("creation-resources-page", timeout=60)
    resources_same_process = read_persisted_resources_authority(device, resources_receipt)
    device.back()
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=22,
    )
    open_prerequisite(device)
    post_resources_prerequisite_authority = read_persisted_prerequisite_authority(device)
    resources_binding = resources_same_process.get("binding")
    if not isinstance(resources_binding, dict):
        raise RuntimeError("Reopened Resources binding receipt is absent")
    post_resources_binding_digests = {
        "rawCharacterXml": str(resources_binding.get("rawCharacterXmlDigest", "")),
        "auxiliaryState": str(resources_binding.get("auxiliaryStateDigest", "")),
        "authority": str(resources_binding.get("authorityDigest", "")),
    }
    if (
        post_resources_binding_digests["rawCharacterXml"]
        != confirmed_binding_digests["rawCharacterXml"]
        or post_resources_binding_digests["authority"]
        != confirmed_binding_digests["authority"]
        or post_resources_binding_digests["auxiliaryState"]
        == confirmed_binding_digests["auxiliaryState"]
    ):
        raise RuntimeError(
            "Resources confirmation changed raw/rules authority or failed to advance auxiliary state: "
            f"prerequisite={confirmed_binding_digests!r}, resources={resources_binding!r}"
        )
    assert_persisted_prerequisite_authority(
        post_resources_prerequisite_authority,
        confirmed_draft_digest,
        post_resources_binding_digests,
        int(resources_receipt["workspaceRevision"]),
        int(resources_receipt["savedRevision"]),
    )

    progress.advance("process-restart-reopen")
    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=False)
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        reset_swipes=22,
    )
    assert_uncreated_advanced_editor_gated(
        device,
        scan_observer=progress.record_scan,
        scan_id="advanced-editor-gate-process-restart",
    )
    open_prerequisite(device)
    restarted_authority = read_persisted_prerequisite_authority(device)
    assert_persisted_prerequisite_authority(
        restarted_authority,
        confirmed_draft_digest,
        post_resources_binding_digests,
        int(resources_receipt["workspaceRevision"]),
        int(resources_receipt["savedRevision"]),
    )
    for category in ("heritage", "talent"):
        require_exact_restored_authority_option(
            device,
            category,
            typed_selections[category],
            typed_selection_ids[category],
            scan_observer=progress.record_scan,
            scan_id=f"process-restart-restored-authority-option-{category}",
        )
    restarted_talent_grant = require_restored_talent_grant(
        device,
        typed_selections["talent"],
        "Skill groups",
        str(skill_group_grant["grantDigest"]),
        skill_group_selected_option_ids,
        scan_observer=progress.record_scan,
        scan_id="process-restart-restored-talent-skill-group-grant",
    )
    device.wait("creation-prerequisite-attributes-ready", scroll=True, max_scrolls=22)
    device.back()
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=22,
    )
    open_resources(device)
    resources_restarted = read_persisted_resources_authority(device, resources_receipt)
    device.capture("creation-prerequisite-process-restart")

    timing = progress.finish()
    artifact_binding: dict[str, object] = {
        "schema": "chummer.android.creation-prerequisite-artifact-binding/v1",
        "hashAlgorithm": "sha256",
        "digestDomain": "raw-file-bytes",
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "sharedDriverSha256": sha256(shared_path),
        "priorityCompatibilityDriverSha256": sha256(priority_compatibility_path),
        "progressSnapshotSha256": sha256(progress.evidence_path),
        "progressEventsJsonlSha256": sha256(progress.events_path),
        "creationBootstrapTimingSha256": sha256(
            device.evidence / CREATION_BOOTSTRAP_TIMING_FILE_NAME
        ),
    }
    receipt = {
        "schema": "chummer.android.creation-prerequisite-e2e/v1",
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "phoneUiLocale": phone_ui_locale,
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": artifact_binding["apkSha256"],
        "driverSha256": artifact_binding["driverSha256"],
        "sharedDriverSha256": artifact_binding["sharedDriverSha256"],
        "priorityCompatibilityDriverSha256": artifact_binding[
            "priorityCompatibilityDriverSha256"
        ],
        "artifactBinding": artifact_binding,
        "artifactBindingSha256": canonical_json_sha256(artifact_binding),
        "selectorSemantics": {
            "identity": "full lowercase ASCII tokenized resource ID",
            "ordering": "ascending full resource ID; equivalent to UTF-8 byte order",
            "enabled": "enabled=true and clickable=true; exact row must also have tappable bounds",
            "selected": "accessible text or description begins with U+2713 plus one space",
            "completion": "exact completion resource ID enabled iff selected count equals required count",
        },
        "timing": timing,
        "creationBootstrapTiming": creation_bootstrap_timing,
        "progressEvidence": {
            "snapshot": {
                "path": str(progress.evidence_path),
                "sha256": sha256(progress.evidence_path),
            },
            "atomicJsonl": {
                "path": str(progress.events_path),
                "sha256": sha256(progress.events_path),
                "writeProtocol": "same-directory temporary file then os.replace-compatible Path.replace",
            },
        },
        "journeys": {
            "publicPriorityRunnerBootstrappedByCore": "pass",
            "legacyPriorityContinuationSkipped": "pass",
            "canonicalPrioritySettingsProfileBound": "pass",
            "creationMethodNavigationEnabledByBootstrapAuthority": "pass",
            "canonicalSourceAuthorityDigestsVisible": "pass",
            "priorityOrSumToTenAuthorityLoaded": "pass",
            "globalCreationKarmaExactTotalUsedRemaining": "pass",
            "fiveOrderedTypedCategorySelections": "pass",
            "authorityProjectedRankOptionsOnly": "pass",
            "priorityMultisetOrSumTargetEnforced": "pass",
            "selectedRankAutomationIds": selected,
            "selectedAuthorityOptionAutomationIds": typed_selections,
            "selectedAuthoritySelectionIds": typed_selection_ids,
            "activeSkillTalentSelectionId": active_talent_selection_id,
            "activeSkillTalentGrant": {
                **active_grant,
                "previewDigest": active_preview_digest,
                "previewGrantPlanDigest": active_plan_digest,
            },
            "skillGroupTalentGrant": {
                **skill_group_grant,
                "previewGrantPlanDigest": skill_group_plan_digest,
                "sameProcessRestoredSurface": dict(resumed_talent_grant._asdict()),
                "processRestartRestoredSurface": dict(restarted_talent_grant._asdict()),
            },
            "prerequisiteSnapshotDigest": prerequisite_snapshot_digest,
            "confirmedDraftDigest": confirmed_draft_digest,
            "previewDigest": preview_digest,
            "previewBindingDigests": preview_binding_digests,
            "confirmedBindingDigests": confirmed_binding_digests,
            "confirmedRevisions": confirmed_revisions,
            "sameSessionPersistedAuthority": resumed_authority,
            "restartedPersistedAuthority": restarted_authority,
            "backRestoresDraftSelection": "pass",
            "activeSkillGrantExactSelectorCardinality": "pass",
            "activeSkillGrantBackPreserveAndExplicitReset": "pass",
            "activeSkillGrantPreviewExact": "pass",
            "talentChangeClearsActiveSkillGrantSlots": "pass",
            "skillGroupGrantExactSelectorCardinality": "pass",
            "skillGroupGrantBackPreserveAndExplicitReset": "pass",
            "skillGroupGrantPreviewAndConfirmExact": "pass",
            "skillGroupGrantSameProcessResume": "pass",
            "skillGroupGrantProcessRestartResume": "pass",
            "heritageAndTalentSelectionsProjectedByCore": "pass",
            "previewDigestBeforeExplicitConfirmation": "pass",
            "atomicDraftReceiptVerified": "pass",
            "characterDocumentChangedFalse": "pass",
            "rawAttributeGrantVisible": "pass",
            "metatypeAdjustmentResolvedByCore": "pass",
            "attributesPrerequisiteOpenedByCore": "pass",
            "pendingDraftSameProcessResume": "pass",
            "pendingDraftProcessRestartResume": "pass",
            "resourcesRankDCanonicalGrant": resources_before,
            "resourcesPreviewAndReceipt": resources_confirmation,
            "resourcesSameProcessPersistedAuthority": resources_same_process,
            "prerequisiteAuthorityAfterResources": post_resources_prerequisite_authority,
            "resourcesRestartedPersistedAuthority": resources_restarted,
            "resourcesExplicitConfirmationOnly": "pass",
            "resourcesReceiptRevisionAndDigestBound": "pass",
            "resourcesPendingDraftProcessRestartResume": "pass",
            "buildGhostLaunchPostponedAndAbsent": "pass",
            "advancedEditorNeverExposedWhileCreatedFalse": "pass",
        },
        "restartProcessIds": {
            "beforeForceStop": list(restart.before_force_stop.process_ids),
            "afterForceStop": list(restart.after_force_stop.process_ids),
            "restarted": list(restart.restarted.process_ids),
        },
        "creationKarmaProvisioning": {
            "method": "typed-core-bootstrap-from-production-dialog",
            "buildMethod": "Priority",
            "settingsProfileId": STANDARD_PRIORITY_SETTINGS_ID,
            "dashboardBinding": dashboard_binding,
            "readyNavigation": ready_navigation,
            "prerequisiteBinding": prerequisite_binding_authority,
            "sourceAuthorityDigests": source_authority_digests,
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    temporary_receipt = args.receipt.with_name(f".{args.receipt.name}.tmp")
    temporary_receipt.write_text(
        json.dumps(receipt, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    temporary_receipt.replace(args.receipt)
    print(json.dumps(receipt, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args(argv)
    progress = ProgressRecorder(args.evidence)
    try:
        return execute(args, progress)
    except Exception as error:
        progress.fail(error)
        raise


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"creation prerequisite e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
