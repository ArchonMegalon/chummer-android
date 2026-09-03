#!/usr/bin/env python3
"""API-36 phone proof for the authoritative Priority/Sum-to-Ten prerequisite.

The source remains an unexecuted contract until CI or an operator runs it against a reviewed APK.
A successfully completed invocation emits a pass receipt bound to that APK and this driver.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
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
TALENT_GRANT_PREVIEW_PLAN_DIGEST_ID = (
    "creation-prerequisite-preview-talent-grant-plan-digest"
)
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
PROGRESS_SCHEMA = "chummer.android.creation-prerequisite-progress/v5"
PROGRESS_FILE_NAME = "creation-prerequisite-progress.json"
PROGRESS_EVENTS_FILE_NAME = "creation-prerequisite-progress.jsonl"
CREATION_BOOTSTRAP_TIMING_PREFIX = "CHUMMER_CREATION_BOOTSTRAP_TIMING "
CREATION_BOOTSTRAP_TIMING_FILE_NAME = "creation-bootstrap-timing.json"
CREATION_BOOTSTRAP_LOGCAT_FILE_NAME = "creation-bootstrap-timing-logcat.txt"
CREATION_BOOTSTRAP_TIMING_LINE = re.compile(
    rf"^{re.escape(CREATION_BOOTSTRAP_TIMING_PREFIX)}(?P<payload>\{{.*\}})$"
)
CREATION_BOOTSTRAP_LOGCAT_MAIN_DIVIDER = "--------- beginning of main"
CREATION_DASHBOARD_READY_PREFIX = "CHUMMER_CREATION_DASHBOARD_READY "
CREATION_DASHBOARD_READY_SCHEMA = (
    "chummer.android.creation-dashboard-route-ready/v1"
)
CREATION_DASHBOARD_READY_LINE = re.compile(
    rf"^{re.escape(CREATION_DASHBOARD_READY_PREFIX)}(?P<payload>\{{.*\}})$"
)
CREATION_DASHBOARD_READY_LOG_FILE_NAME = (
    "creation-dashboard-route-ready-logcat.txt"
)
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
# This is the wall-clock budget for the exhaustive external UIAutomator proof,
# not the product's Creation transaction SLO.  The proof deliberately performs
# repeated fresh file-backed hierarchy observations across mutable catalogs;
# transaction and render timing remain independently bounded below.  Resources
# adds preview/receipt, same-process persistence, cross-domain rebinding, and
# new-process persistence to the original prerequisite journey.  Those are now
# distinct bounded phases instead of one artificial 150-second phase.  The
# external proof cap therefore covers the complete expanded evidence surface;
# it does not widen any product transaction or rendering SLO.
TOTAL_PERFORMANCE_TARGET_MS = 45 * 60 * 1000
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
    # and the visible-dashboard proof. Each semantic surface and the measured
    # method restore has its own strict bound under the exhaustive proof
    # aggregate target; no authority field or stable-end proof is removed.
    "dashboard-authority-inventory": 30_000,
    "advanced-editor-gate-inventory": 90_000,
    # Hosted run 33680699208 proved every preceding phase and the product UI,
    # then safely recovered the first post-tap file-backed hierarchy through
    # one fresh read-only retry plus metadata/content/stat reconciliation. The
    # former 60-second slice expired before the required origin and stable-end
    # traversal could begin. The resolved origin is already reused as viewport
    # zero, so retain every fail-closed observation and give this external
    # tap/origin/scan proof one bounded retry reserve. The independently
    # authoritative product transaction and 45-minute journey SLOs are unchanged.
    "prerequisite-authority-inventory": 120_000,
    "priority-ranks": 150_000,
    "typed-authority-options": 150_000,
    # The grant proof ends by reacquiring viewport zero after the exhaustive
    # option scan. Run 33423780713 observed 11 hierarchy reads plus 10 reverse
    # swipes before the final single, non-replayable file-backed dump reached
    # the former 150-second boundary. Keep the dump single-shot and preserve
    # fail-closed reconciliation, but reserve 30 seconds for that external
    # observer. The whole-journey cap remains independently authoritative.
    "talent-active-skill-grant": 180_000,
    "talent-active-skill-preservation": 150_000,
    "talent-active-skill-reset": 150_000,
    "talent-active-skill-reselection": 150_000,
    # Closing the restored Talent grant is a separate route transition: it
    # taps the exact completion authority, returns through Talent, then
    # reacquires the prerequisite selection ID. Two 45-second route waits and
    # the 60-second selection lookup retain 30 seconds of bounded ADB/boundary
    # overhead. Do not charge this distinct commit/return work to the already-
    # validated explicit reselection phase.
    "talent-active-grant-completion": 180_000,
    "talent-active-preview": 150_000,
    # Returning from the deeply scanned preview preserves the prerequisite
    # page's bottom offset. Reacquiring the Talent row, opening its typed route,
    # and selecting the exact replacement authority is therefore a distinct
    # bounded transition, not part of the already-complete preview proof.
    "talent-skill-group-selection": 150_000,
    # Skill-group grants use the same exhaustive scan/reacquisition topology
    # and therefore retain the same bounded observer reserve.
    "talent-skill-group-grant": 180_000,
    "talent-skill-group-preservation": 150_000,
    "talent-skill-group-reset": 150_000,
    "talent-skill-group-reselection": 150_000,
    # Skill-group completion performs the same distinct commit/return and
    # SelectionId reacquisition as active-skill completion. It must not borrow
    # the tail of the already-proven choose/preserve/reset/reselect sequence.
    "talent-skill-group-grant-completion": 180_000,
    # External proof phase SLO/cap; not a sum-of-operation maxima or an
    # entitlement beyond TOTAL_PERFORMANCE_TARGET_MS.
    "preview-confirm": 360_000,
    "same-process-reopen": 90_000,
    # Heritage and Talent are two independent restored catalogs. The selected
    # Talent grant lives on a third pushed route and must retain its own stable
    # start/end and cardinality proof instead of consuming the catalog tail.
    # Same-process and process-restart restoration call the same exact grant
    # route proof: stable grouped scan, two read-only route observations and
    # the file-backed hierarchy retry/reconciliation fence. Hosted run
    # 33622199489 completed the grouped scan in 14.819 seconds, then proved a
    # timeout/null-root plus a fresh transport retry could consume the former
    # 60-second slice before one valid reconciled hierarchy was available.
    # Give both restoration modes the same bounded observer reserve; this does
    # not alter the independently authoritative whole-journey cap.
    "same-process-authority-options": 120_000,
    "same-process-restored-talent-grant": 90_000,
    # The initial Resources authority inventory already establishes the exact
    # scroll topology. Its zero-conversion option is reacquired from that
    # measured topology, never through a second bidirectional whole-page scan.
    # Exact run 33637265813 measured a 36.253-second stable scan followed by
    # 75.486 seconds of reserved, read-only hierarchy recovery before the
    # measured reverse traversal could finish. Preserve the per-viewport fresh
    # observation invariant and its retry/reconciliation fence with a bounded
    # 180-second phase; the independent 45-minute journey cap is unchanged.
    "resources-initial-authority": 180_000,
    # Preview and explicit confirmation contain two independent stable-end
    # authority scans plus one non-replayable write.  Keep that mutation in its
    # own phase so a later observer traversal can never consume its deadline.
    "resources-preview-confirm": 240_000,
    "resources-same-process-reopen": 120_000,
    "resources-prerequisite-rebind": 180_000,
    # Exact run 33683746719 proved the new process, exact PID transition, and
    # exact resumed MainActivity, then observed four transient empty roots in
    # the persisted prerequisite traversal before its fail-closed capture
    # recovered a valid 48-node Chummer hierarchy. Give this cold-process
    # restoration the bounded observer reserve needed to finish that exact
    # scan, without changing the independent 45-minute whole-journey cap.
    "process-restart-reopen": 240_000,
    "process-restart-authority-options": 120_000,
    "process-restart-restored-talent-grant": 90_000,
    "process-restart-resources": 120_000,
}
PHASE_ORDER = tuple(PHASE_BUDGET_MS)
PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS = 3.0
PREVIEW_ROUTE_PROOF_TIMEOUT_SECONDS = 75.0
ZERO_GESTURE_ROUTE_PROOF_TIMEOUT_SECONDS = 75
CONFIRMED_STATE_TRANSITION_TIMEOUT_SECONDS = 90.0
CONFIRMED_RECEIPT_BACK_ORIGIN_TIMEOUT_SECONDS = 15.0
CONFIRMED_RECEIPT_TRAVERSAL_RESERVE_SECONDS = 60.0
CONFIRMED_RECEIPT_BACK_REACQUISITION_TIMEOUT_SECONDS = 45.0
CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_EMPTY_HIERARCHIES = 3
CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_SYSTEM_UI_DISMISSALS = 3
CONFIRMED_RECEIPT_BACK_RECOVERY_DELAY_SECONDS = 0.2
CONFIRMED_RECEIPT_PROOF_TIMEOUT_SECONDS = (
    CONFIRMED_STATE_TRANSITION_TIMEOUT_SECONDS
    + CONFIRMED_RECEIPT_TRAVERSAL_RESERVE_SECONDS
)
PRE_BACK_ROUTE_LOG_CLEAR_TIMEOUT_SECONDS = 3.0
POST_CONFIRM_DASHBOARD_READY_TIMEOUT_SECONDS = 30.0
POST_CONFIRM_DASHBOARD_READY_READ_ATTEMPT_MAX_SECONDS = 5.0
POST_CONFIRM_DASHBOARD_READY_POLL_DELAY_SECONDS = 0.25
POST_CONFIRM_DASHBOARD_DUMP_ATTEMPT_MAX_SECONDS = 30.0
POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS = 75.0
# The post-confirm dashboard is observed from its current viewport, not rewound.
# Exact-head run 33534835752 required eight genuine forward movements to reach
# the bottom after Core opened the Attributes prerequisite. Stable-end proof
# then needs two additional clamped gestures. Keep this route-specific bound
# exact so the observer can prove the end without widening the normal dashboard
# inventory or replaying the already-consumed Back action.
POST_CONFIRM_DASHBOARD_SCAN_MAX_SCROLLS = 10
CONFIRMED_RECEIPT_BACK_DOWNSTREAM_RESERVE_SECONDS = (
    PRE_BACK_ROUTE_LOG_CLEAR_TIMEOUT_SECONDS
    + PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
    + POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS
)
CONFIRM_DOWNSTREAM_RESERVE_SECONDS = (
    CONFIRMED_RECEIPT_PROOF_TIMEOUT_SECONDS
    + PRE_BACK_ROUTE_LOG_CLEAR_TIMEOUT_SECONDS
    + PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
    + POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS
)
DASHBOARD_SCAN_GESTURE_RATIO = 0.60
DASHBOARD_SCAN_MAX_SCROLLS = 18
PROCESS_RESTART_METHOD_MAX_EMPTY_HIERARCHY_READS = 4
RESOURCES_SURFACE_MAX_CONSECUTIVE_EMPTY_READS = 3
PROCESS_RESTART_RESOURCES_SCAN_ID = (
    "creation-resources-process-restart-persisted-authority"
)
# Runs 33692970619 and 33695613418 both reached the exact restored Resources
# surface before UIAutomator emitted six transient empty hierarchies. Permit
# only that observed burst; a seventh empty read and the phase deadline remain
# hard failures.
PROCESS_RESTART_RESOURCES_MAX_CONSECUTIVE_EMPTY_READS = 6
PROCESS_RESTART_PERSISTED_PREREQUISITE_SCAN_ID = (
    "process-restart-persisted-prerequisite-authority"
)
PROCESS_RESTART_PERSISTED_PREREQUISITE_MAX_CONSECUTIVE_EMPTY_READS = 4
CREATION_METHOD_REACQUISITION_PHASE_AUTHORITY = {
    "advanced-editor-gate-inventory": (
        PHASE_BUDGET_MS["advanced-editor-gate-inventory"],
        3,
    ),
    "same-process-reopen": (
        PHASE_BUDGET_MS["same-process-reopen"],
        3,
    ),
    "resources-prerequisite-rebind": (
        PHASE_BUDGET_MS["resources-prerequisite-rebind"],
        3,
    ),
    "process-restart-reopen": (
        PHASE_BUDGET_MS["process-restart-reopen"],
        PROCESS_RESTART_METHOD_MAX_EMPTY_HIERARCHY_READS,
    ),
}
CREATION_METHOD_REACQUISITION_SCAN_ID = (
    "creation-stage-method-ready-reacquisition"
)
CREATION_METHOD_REACQUISITION_DIRECTION = "down"
TALENT_GRANT_SCAN_GESTURE_RATIO = 0.60
TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO = 0.22
TALENT_GRANT_REACQUISITION_MAX_SCROLLS = 40
TALENT_GRANT_OPTION_RECOVERY_MAX_SCROLLS = 40
TALENT_GRANT_REACQUISITION_STABLE_REPEATS = 2
MAX_STABLE_START_REVERSE_SWIPES = 40
# Forward and reverse Android ScrollView gestures are not exact inverses when
# the forward scan reaches a clamped page end.  One overlapping reverse
# gesture is therefore allowed after the exact measured delta.  Every gesture
# is still followed by a fresh exact hierarchy and the bound cannot expand.
PRIORITY_CATEGORY_REACQUISITION_OVERLAP_SWIPES = 1
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


def require_phase_deadline(
    deadline: float | None,
    *,
    operation: str,
) -> None:
    if deadline is not None and time.monotonic() >= deadline:
        raise RuntimeError(
            f"Advanced-editor phase deadline expired during {operation}"
        )


def sleep_before_phase_deadline(
    seconds: float,
    *,
    deadline: float | None,
    operation: str,
) -> None:
    if seconds <= 0:
        require_phase_deadline(deadline, operation=operation)
        return
    if deadline is not None and deadline - time.monotonic() < seconds:
        raise RuntimeError(
            f"Advanced-editor phase deadline cannot accommodate {operation}"
        )
    time.sleep(seconds)
    require_phase_deadline(deadline, operation=operation)


def persistent_action_deadline(
    phase_deadline: float,
    *,
    action_timeout_seconds: float,
    proof_timeout_seconds: float,
    operation: str,
) -> float:
    """Lease one action only when its immediate bounded proof still fits."""
    if (
        not math.isfinite(phase_deadline)
        or not math.isfinite(action_timeout_seconds)
        or not math.isfinite(proof_timeout_seconds)
        or action_timeout_seconds <= 0
        or proof_timeout_seconds <= 0
    ):
        raise ValueError("Persistent action bounds must be finite and positive")
    now = time.monotonic()
    required = action_timeout_seconds + proof_timeout_seconds
    remaining = phase_deadline - now
    if remaining < required:
        raise RuntimeError(
            f"Preview-confirm phase has {remaining:.3f}s remaining before {operation}; "
            f"requires the exact {required:.3f}s action-plus-proof lease"
        )
    return min(phase_deadline, now + action_timeout_seconds)


def immediate_proof_deadline(
    phase_deadline: float,
    proof_timeout_seconds: float,
    *,
    operation: str,
) -> float:
    if not math.isfinite(proof_timeout_seconds) or proof_timeout_seconds <= 0:
        raise ValueError("Immediate proof timeout must be finite and positive")
    require_phase_deadline(phase_deadline, operation=operation)
    return min(phase_deadline, time.monotonic() + proof_timeout_seconds)


def fresh_hierarchy_timed(
    device: shared.Device,
    durations_ms: list[int],
    *,
    deadline: float | None = None,
    dump_attempt_max_seconds: float | None = None,
    allow_direct_reconciliation: bool = True,
) -> list[shared.UiNode]:
    """Acquire a post-gesture hierarchy through UIAutomator's dump file.

    API 36 can return an older viewport from the direct ``/dev/tty`` stream
    immediately after a swipe even though the rendered frame has moved.  The
    normal dump-to-file plus read path is slower, but it is the authority for
    every scroll-dependent inventory.  Busy-state polling deliberately keeps
    using :func:`read_only_hierarchy_timed` because it never changes viewport.
    """
    if type(allow_direct_reconciliation) is not bool:
        raise ValueError("Direct hierarchy reconciliation policy must be boolean")
    if dump_attempt_max_seconds is not None and (
        isinstance(dump_attempt_max_seconds, bool)
        or not isinstance(dump_attempt_max_seconds, (int, float))
        or not math.isfinite(dump_attempt_max_seconds)
        or dump_attempt_max_seconds <= 0
    ):
        raise ValueError("Hierarchy dump attempt bound must be finite and positive")
    require_phase_deadline(deadline, operation="fresh hierarchy acquisition")
    started = time.perf_counter()
    try:
        hierarchy_options: dict[str, object] = {}
        if deadline is not None:
            hierarchy_options["deadline"] = deadline
        if dump_attempt_max_seconds is not None:
            hierarchy_options["dump_attempt_max_seconds"] = (
                dump_attempt_max_seconds
            )
        if not allow_direct_reconciliation:
            hierarchy_options["allow_direct_reconciliation"] = False
        nodes = device.hierarchy(**hierarchy_options)
    finally:
        durations_ms.append(round((time.perf_counter() - started) * 1000))
    require_phase_deadline(deadline, operation="fresh hierarchy acquisition")
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


def classify_creation_dashboard_ready_logcat(
    logcat: str,
) -> tuple[list[str], list[re.Match[str]], int, list[str]]:
    """Classify one exact route-ready marker and its optional main divider."""
    raw_lines = logcat.splitlines()
    exact_matches: list[re.Match[str]] = []
    invalid_lines: list[str] = []
    divider_count = 0
    marker_seen = False
    for line in raw_lines:
        match = CREATION_DASHBOARD_READY_LINE.fullmatch(line)
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


def wait_for_creation_dashboard_ready_log(
    device: shared.Device,
    *,
    expected_content_revision: int,
    expected_saved_revision: int,
    deadline: float,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
) -> dict[str, object]:
    """Gate UIAutomator on one fresh, laid-out post-Back dashboard marker.

    The main log is cleared immediately before the one-shot Back action.  This
    marker is therefore only transition readiness.  Snapshot reads are safe to
    repeat and never replay the Back action; the following same-snapshot
    accessibility proof remains the route and cardinality authority.
    """
    if (
        type(expected_content_revision) is not int
        or expected_content_revision <= 0
        or type(expected_saved_revision) is not int
        or expected_saved_revision < 0
    ):
        raise ValueError("Dashboard-ready marker requires exact nonnegative revisions")
    started = time.monotonic()
    marker_deadline = min(
        deadline,
        started + POST_CONFIRM_DASHBOARD_READY_TIMEOUT_SECONDS,
    )
    read_durations_ms: list[int] = []
    empty_snapshot_count = 0
    logcat = ""
    exact_matches: list[re.Match[str]] = []
    divider_count = 0
    invalid_lines: list[str] = []
    raw_lines: list[str] = []
    while time.monotonic() < marker_deadline:
        read_started = time.perf_counter()
        try:
            result = device.run(
                *shared.ADB_CREATION_DASHBOARD_READY_LOGCAT_ARGUMENTS,
                timeout=shared._remaining_operation_timeout(
                    deadline=marker_deadline,
                    maximum=(
                        POST_CONFIRM_DASHBOARD_READY_READ_ATTEMPT_MAX_SECONDS
                    ),
                ),
                deadline=marker_deadline,
            )
        finally:
            read_durations_ms.append(
                round((time.perf_counter() - read_started) * 1000)
            )
        logcat = str(result.stdout)
        raw_lines, exact_matches, divider_count, invalid_lines = (
            classify_creation_dashboard_ready_logcat(logcat)
        )
        if invalid_lines or len(exact_matches) > 1:
            break
        if len(exact_matches) == 1:
            break
        empty_snapshot_count += 1
        if (
            time.monotonic() + POST_CONFIRM_DASHBOARD_READY_POLL_DELAY_SECONDS
            >= marker_deadline
        ):
            break
        sleep_before_phase_deadline(
            POST_CONFIRM_DASHBOARD_READY_POLL_DELAY_SECONDS,
            deadline=marker_deadline,
            operation="Creation dashboard route-ready snapshot poll",
        )
    device.evidence.mkdir(parents=True, exist_ok=True)
    (device.evidence / CREATION_DASHBOARD_READY_LOG_FILE_NAME).write_text(
        logcat,
        encoding="utf-8",
    )
    if len(exact_matches) != 1 or invalid_lines:
        if not exact_matches and not invalid_lines:
            raise RuntimeError(
                "Timed out waiting for the exact post-Back Creation "
                "dashboard-ready marker"
            )
        raise RuntimeError(
            "Expected one exact post-Back Creation dashboard-ready marker with "
            "only an optional leading main-buffer divider: "
            f"raw={len(raw_lines)}, exact={len(exact_matches)}, "
            f"dividers={divider_count}, invalid={len(invalid_lines)}"
        )
    try:
        payload = json.loads(exact_matches[0].group("payload"))
    except json.JSONDecodeError as error:
        raise RuntimeError("Creation dashboard-ready marker contained invalid JSON") from error
    if not isinstance(payload, dict):
        raise RuntimeError("Creation dashboard-ready marker payload was not an object")
    expected_fields = {
        "schema",
        "routeAutomationId",
        "dashboardAutomationId",
        "workspaceId",
        "contentRevision",
        "savedRevision",
        "contentDigest",
        "sourceDigest",
        "runtimeFingerprint",
        "buildMethod",
        "snapshotDigest",
        "characterCreated",
        "authorityReady",
    }
    if set(payload) != expected_fields:
        raise RuntimeError(
            "Creation dashboard-ready marker field set differed: "
            f"expected={sorted(expected_fields)!r}, actual={sorted(payload)!r}"
        )
    invalid_scalar_types = [
        field
        for field, expected_type in (
            ("contentRevision", int),
            ("savedRevision", int),
            ("characterCreated", bool),
            ("authorityReady", bool),
        )
        if type(payload.get(field)) is not expected_type
    ]
    if invalid_scalar_types:
        raise RuntimeError(
            "Creation dashboard-ready marker scalar types differed: "
            f"{invalid_scalar_types!r}"
        )
    required_literals: dict[str, object] = {
        "schema": CREATION_DASHBOARD_READY_SCHEMA,
        "routeAutomationId": "phone-runner-create",
        "dashboardAutomationId": "creation-wizard-dashboard",
        "contentRevision": expected_content_revision,
        "savedRevision": expected_saved_revision,
        # Core owns this case-sensitive typed identity. Presentation must not
        # normalize it into a route/display token.
        "buildMethod": "Priority",
        # The frozen Creation projection intentionally has no whole-wizard
        # runtime authority before Magic/Resonance supplies one. Requiring the
        # exact empty value proves the marker did not promote a domain-specific
        # digest or invent a global runtime fingerprint.
        "runtimeFingerprint": "",
        "characterCreated": False,
        "authorityReady": True,
    }
    differing = {
        field: (expected, payload.get(field))
        for field, expected in required_literals.items()
        if payload.get(field) != expected
    }
    digest_fields = (
        "contentDigest",
        "sourceDigest",
        "snapshotDigest",
    )
    invalid_digests = [
        field
        for field in digest_fields
        if not isinstance(payload.get(field), str)
        or CANONICAL_AUTHORITY_DIGEST.fullmatch(str(payload[field])) is None
    ]
    workspace_id = payload.get("workspaceId")
    if (
        differing
        or invalid_digests
        or not isinstance(workspace_id, str)
        or not workspace_id.strip()
        or len(workspace_id) > 128
    ):
        raise RuntimeError(
            "Creation dashboard-ready marker was stale or malformed: "
            f"differing={differing!r}, invalidDigests={invalid_digests!r}"
        )
    elapsed_ms = round((time.monotonic() - started) * 1000)
    scan = {
        "scanId": "post-confirm-dashboard-route-ready-log",
        "status": "resolved",
        "observationMode": "fresh-cleared-main-log-snapshot-poll",
        "logcatReadCount": len(read_durations_ms),
        "emptySnapshotCount": empty_snapshot_count,
        "logcatElapsedMs": sum(read_durations_ms),
        "maximumLogcatReadMs": max(read_durations_ms, default=0),
        "readAttemptMaxMs": round(
            POST_CONFIRM_DASHBOARD_READY_READ_ATTEMPT_MAX_SECONDS * 1000
        ),
        "pollDelayMs": round(
            POST_CONFIRM_DASHBOARD_READY_POLL_DELAY_SECONDS * 1000
        ),
        "expectedContentRevision": expected_content_revision,
        "observedContentRevision": int(payload["contentRevision"]),
        "expectedSavedRevision": expected_saved_revision,
        "observedSavedRevision": int(payload["savedRevision"]),
        "workspaceId": workspace_id,
        "snapshotDigest": str(payload["snapshotDigest"]),
        "deadlineEnforced": True,
        "maximumElapsedMs": round(
            POST_CONFIRM_DASHBOARD_READY_TIMEOUT_SECONDS * 1000
        ),
        "elapsedMs": elapsed_ms,
    }
    if scan_observer is not None:
        scan_observer(scan)
    return payload


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


def require_reusable_scan_origin(
    origin: PriorityRankOrigin,
    *,
    max_reverse_swipes: int = 8,
) -> None:
    if (
        type(max_reverse_swipes) is not int
        or max_reverse_swipes < 0
        or max_reverse_swipes > MAX_STABLE_START_REVERSE_SWIPES
        or not origin.nodes
        or type(origin.reverse_swipes) is not int
        or type(origin.elapsed_ms) is not int
        or type(origin.empty_hierarchy_reads) is not int
        or not origin.hierarchy_durations_ms
        or any(type(value) is not int for value in origin.hierarchy_durations_ms)
        or origin.reverse_swipes < 0
        or origin.reverse_swipes > max_reverse_swipes
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
    deadline: float | None = None,
) -> PriorityRankOrigin:
    """Prove a measured page start and retain its fresh final hierarchy.

    The baseline and every post-gesture observation use the file-backed fresh
    hierarchy path. The returned origin carries that acquisition timing exactly
    once for the composed forward-scan receipt; no separate scan is recorded.
    """
    if (
        not scan_id
        or max_reverse_swipes < stable_repeats
        or max_reverse_swipes > MAX_STABLE_START_REVERSE_SWIPES
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
        nodes = fresh_hierarchy_timed(
            device,
            hierarchy_durations_ms,
            deadline=deadline,
        )
        if not nodes:
            consecutive_empty_reads += 1
            empty_hierarchy_reads += 1
            if consecutive_empty_reads > max_consecutive_empty_reads:
                if deadline is None:
                    device.capture(f"{scan_id}-empty-hierarchy-exhausted")
                else:
                    device.capture(
                        f"{scan_id}-empty-hierarchy-exhausted",
                        deadline=deadline,
                    )
                raise RuntimeError(
                    f"Accessibility reverse scan {scan_id!r} exhausted transient empty "
                    "hierarchy reads"
                )
            sleep_before_phase_deadline(
                0.75,
                deadline=deadline,
                operation="stable-start empty-hierarchy wait",
            )
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
            require_reusable_scan_origin(
                origin,
                max_reverse_swipes=max_reverse_swipes,
            )
            return origin
        if reverse_swipes >= max_reverse_swipes:
            break
        if deadline is None:
            device.swipe_down(distance_ratio=distance_ratio)
        else:
            device.swipe_down(
                distance_ratio=distance_ratio,
                deadline=deadline,
            )
        reverse_swipes += 1
        if delay_seconds > 0:
            sleep_before_phase_deadline(
                delay_seconds,
                deadline=deadline,
                operation="stable-start post-swipe wait",
            )

    if deadline is None:
        device.capture(f"{scan_id}-stable-start-unproven")
    else:
        device.capture(f"{scan_id}-stable-start-unproven", deadline=deadline)
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
    initial_observation_max_reverse_swipes: int = 8,
    stable_repeats: int = 2,
    max_consecutive_empty_reads: int = 3,
    delay_seconds: float = 0.2,
    observer: Callable[[dict[str, object]], None] | None = None,
    deadline: float | None = None,
    hierarchy_dump_attempt_max_seconds: float | None = None,
    allow_direct_hierarchy_reconciliation: bool = True,
    allow_direct_swipe_reconciliation: bool = True,
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
        or type(initial_observation_max_reverse_swipes) is not int
        or initial_observation_max_reverse_swipes < 0
        or initial_observation_max_reverse_swipes > MAX_STABLE_START_REVERSE_SWIPES
        or type(allow_direct_hierarchy_reconciliation) is not bool
        or type(allow_direct_swipe_reconciliation) is not bool
        or (
            hierarchy_dump_attempt_max_seconds is not None
            and (
                isinstance(hierarchy_dump_attempt_max_seconds, bool)
                or not isinstance(
                    hierarchy_dump_attempt_max_seconds,
                    (int, float),
                )
                or not math.isfinite(hierarchy_dump_attempt_max_seconds)
                or hierarchy_dump_attempt_max_seconds <= 0
            )
        )
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
        require_reusable_scan_origin(
            initial_observation,
            max_reverse_swipes=initial_observation_max_reverse_swipes,
        )
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
            nodes = fresh_hierarchy_timed(
                device,
                hierarchy_durations_ms,
                deadline=deadline,
                dump_attempt_max_seconds=(
                    hierarchy_dump_attempt_max_seconds
                ),
                allow_direct_reconciliation=(
                    allow_direct_hierarchy_reconciliation
                ),
            )
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
                if deadline is None:
                    device.capture(f"{scan_id}-empty-hierarchy-exhausted")
                else:
                    device.capture(
                        f"{scan_id}-empty-hierarchy-exhausted",
                        deadline=deadline,
                    )
                raise RuntimeError(
                    f"Accessibility scan {scan_id!r} exhausted transient empty hierarchy reads"
                )
            sleep_before_phase_deadline(
                0.75,
                deadline=deadline,
                operation="stable-end empty-hierarchy wait",
            )
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
        if deadline is None:
            swipe_options: dict[str, object] = {
                "distance_ratio": distance_ratio,
            }
        else:
            swipe_options = {
                "distance_ratio": distance_ratio,
                "deadline": deadline,
            }
        if not allow_direct_swipe_reconciliation:
            swipe_options["allow_direct_reconciliation"] = False
        device.swipe_up(**swipe_options)
        swipes += 1
        if delay_seconds > 0:
            sleep_before_phase_deadline(
                delay_seconds,
                deadline=deadline,
                operation="stable-end post-swipe wait",
            )
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
    if deadline is None:
        device.capture(f"{scan_id}-stable-end-unproven")
    else:
        device.capture(f"{scan_id}-stable-end-unproven", deadline=deadline)
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
    initial_observation_max_reverse_swipes: int = 8,
    max_consecutive_empty_reads: int = 3,
    delay_seconds: float = 0.2,
    observer: Callable[[dict[str, object]], None] | None = None,
    deadline: float | None = None,
    hierarchy_dump_attempt_max_seconds: float | None = None,
    allow_direct_hierarchy_reconciliation: bool = True,
    allow_direct_swipe_reconciliation: bool = True,
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
        initial_observation_max_reverse_swipes=(
            initial_observation_max_reverse_swipes
        ),
        max_consecutive_empty_reads=max_consecutive_empty_reads,
        delay_seconds=delay_seconds,
        observer=record,
        deadline=deadline,
        hierarchy_dump_attempt_max_seconds=(
            hierarchy_dump_attempt_max_seconds
        ),
        allow_direct_hierarchy_reconciliation=(
            allow_direct_hierarchy_reconciliation
        ),
        allow_direct_swipe_reconciliation=allow_direct_swipe_reconciliation,
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
    deadline: float | None = None,
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
        nodes = fresh_hierarchy_timed(
            device,
            hierarchy_durations_ms,
            deadline=deadline,
        )
        if not nodes:
            sleep_before_phase_deadline(
                0.75,
                deadline=deadline,
                operation="stable-start empty-hierarchy wait",
            )
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
        if deadline is None:
            device.swipe_down(distance_ratio=distance_ratio)
        else:
            device.swipe_down(
                distance_ratio=distance_ratio,
                deadline=deadline,
            )
        swipes += 1
        sleep_before_phase_deadline(
            0.2,
            deadline=deadline,
            operation="stable-start post-swipe wait",
        )
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
    if deadline is None:
        device.capture(f"{scan_id}-stable-start-unproven")
    else:
        device.capture(f"{scan_id}-stable-start-unproven", deadline=deadline)
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
    """Bound exact-node reacquisition by the forward scan's measured delta.

    Callers must observe a fresh hierarchy before and after every reverse
    gesture, using the same gesture ratio that established the scan topology.
    A node already at the scan-proven tappable target authorizes no gesture,
    and no blind pre-move or unmeasured fixed reset is authorized.
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
    deadline: float | None = None,
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

    def capture(name: str) -> None:
        if deadline is None:
            device.capture(name)
        else:
            device.capture(name, deadline=deadline)

    reverse_swipes = 0
    empty_hierarchy_reads = 0
    system_ui_dismissals = 0
    while reverse_swipes <= max_swipes:
        nodes = fresh_hierarchy_timed(device, [], deadline=deadline)
        if not nodes:
            empty_hierarchy_reads += 1
            if empty_hierarchy_reads > max_empty_hierarchy_reads:
                capture(f"{evidence_prefix}-empty-hierarchy-exhausted")
                raise RuntimeError(
                    f"{surface_name} exhausted its separate transient empty-hierarchy "
                    f"budget of {max_empty_hierarchy_reads} reads"
                )
            sleep_before_phase_deadline(
                0.75,
                deadline=deadline,
                operation=f"{surface_name} empty-hierarchy wait",
            )
            continue
        matches = [
            node
            for node in nodes
            if _exact_resource_id(node) == selector
        ]
        if len(matches) > 1:
            capture(f"{evidence_prefix}-cardinality-invalid")
            raise RuntimeError(
                f"{surface_name} {selector!r} has cardinality {len(matches)}; expected one"
            )
        if len(matches) == 1:
            node = matches[0]
            visible = (
                device.node_has_tappable_bounds(node)
                if deadline is None
                else device.node_has_tappable_bounds(node, deadline=deadline)
            )
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
                if deadline is None:
                    device.swipe_down(distance_ratio=distance_ratio)
                else:
                    device.swipe_down(
                        distance_ratio=distance_ratio,
                        deadline=deadline,
                    )
                reverse_swipes += 1
                sleep_before_phase_deadline(
                    0.2,
                    deadline=deadline,
                    operation=f"{surface_name} post-swipe wait",
                )
                continue
            capture(f"{evidence_prefix}-not-tappable")
            raise RuntimeError(
                f"{surface_name} {selector!r} was not visible"
                + (", enabled, and clickable" if require_tappable else "")
            )
        dismissed = (
            device.dismiss_system_ui_anr(nodes)
            if deadline is None
            else device.dismiss_system_ui_anr(nodes, deadline=deadline)
        )
        if dismissed:
            system_ui_dismissals += 1
            if system_ui_dismissals > max_system_ui_dismissals:
                capture(f"{evidence_prefix}-system-ui-exhausted")
                raise RuntimeError(
                    f"{surface_name} exhausted its separate system-UI dismissal "
                    f"budget of {max_system_ui_dismissals}"
                )
            sleep_before_phase_deadline(
                2,
                deadline=deadline,
                operation=f"{surface_name} system-UI wait",
            )
            continue
        if reverse_swipes >= max_swipes:
            break
        if deadline is None:
            device.swipe_down(distance_ratio=distance_ratio)
        else:
            device.swipe_down(
                distance_ratio=distance_ratio,
                deadline=deadline,
            )
        reverse_swipes += 1
        sleep_before_phase_deadline(
            0.2,
            deadline=deadline,
            operation=f"{surface_name} post-swipe wait",
        )
    capture(f"{evidence_prefix}-unavailable")
    raise RuntimeError(
        f"Timed out reversing to exact {surface_name.lower()} {selector!r} "
        f"within the scan-proven {max_swipes}-swipe bound"
    )


def require_creation_method_reacquisition_receipt(
    scan: dict[str, object],
    *,
    expected_phase_id: str | None = None,
    phase_elapsed_ms: int | None = None,
    require_deadline: bool = False,
) -> None:
    """Validate one resolved, bounded dashboard-method restoration receipt."""
    effective_phase_id = expected_phase_id or "advanced-editor-gate-inventory"
    if (
        type(effective_phase_id) is not str
        or effective_phase_id
        not in CREATION_METHOD_REACQUISITION_PHASE_AUTHORITY
    ):
        raise ValueError("Creation method receipt phase authority is invalid")
    (
        expected_phase_budget_ms,
        expected_max_empty_hierarchy_reads,
    ) = CREATION_METHOD_REACQUISITION_PHASE_AUTHORITY[effective_phase_id]
    required_literals: dict[str, object] = {
        "scanId": CREATION_METHOD_REACQUISITION_SCAN_ID,
        "status": "resolved",
        "direction": CREATION_METHOD_REACQUISITION_DIRECTION,
        "distanceRatio": DASHBOARD_SCAN_GESTURE_RATIO,
        "configuredMaxScrolls": DASHBOARD_SCAN_MAX_SCROLLS,
        "stableRepeats": 2,
        "maximumEmptyHierarchyReads": expected_max_empty_hierarchy_reads,
        "maximumSystemUiDismissals": 3,
        "phaseBudgetMs": expected_phase_budget_ms,
    }
    if expected_phase_id is not None:
        required_literals["phaseId"] = expected_phase_id
    differing = {
        field: (expected, scan.get(field))
        for field, expected in required_literals.items()
        if scan.get(field) != expected
    }
    if differing:
        raise RuntimeError(
            "Creation method reacquisition receipt literal authority differs: "
            f"{differing!r}"
        )
    deadline_enforced = scan.get("deadlineEnforced")
    if type(deadline_enforced) is not bool or (
        require_deadline and deadline_enforced is not True
    ):
        raise RuntimeError(
            "Creation method reacquisition receipt omitted its active deadline authority"
        )
    integer_fields = (
        "screens",
        "swipes",
        "emptyHierarchyReads",
        "systemUiDismissals",
        "hierarchyReadCount",
        "hierarchyElapsedMs",
        "maximumHierarchyReadMs",
        "elapsedMs",
    )
    invalid = [
        field
        for field in integer_fields
        if type(scan.get(field)) is not int or int(scan[field]) < 0
    ]
    if invalid:
        raise RuntimeError(
            "Creation method reacquisition receipt timing/count data differs: "
            f"{invalid!r}"
        )
    value = {field: int(scan[field]) for field in integer_fields}
    read_rounding_ms = (value["hierarchyReadCount"] + 1) // 2
    mandatory_wait_ms = (
        value["swipes"] * 200
        + value["emptyHierarchyReads"] * 750
        + value["systemUiDismissals"] * 2_000
    )
    maximum_lower_bound = (
        (
            value["hierarchyElapsedMs"]
            + value["hierarchyReadCount"]
            - 1
        )
        // value["hierarchyReadCount"]
        if value["hierarchyReadCount"] > 0
        else 0
    )
    relationships_hold = (
        1 <= value["screens"]
        and 0 <= value["swipes"] <= DASHBOARD_SCAN_MAX_SCROLLS
        and value["emptyHierarchyReads"] <= expected_max_empty_hierarchy_reads
        and value["systemUiDismissals"] <= 3
        and value["hierarchyReadCount"]
        == value["screens"] + value["emptyHierarchyReads"]
        and value["screens"]
        == value["swipes"] + value["systemUiDismissals"] + 1
        and value["hierarchyReadCount"] > 0
        and value["maximumHierarchyReadMs"] >= maximum_lower_bound
        and value["maximumHierarchyReadMs"] <= value["hierarchyElapsedMs"]
        and value["hierarchyElapsedMs"]
        <= value["elapsedMs"] + read_rounding_ms
        and (
            deadline_enforced is not True
            or value["hierarchyElapsedMs"] + mandatory_wait_ms
            <= value["elapsedMs"] + read_rounding_ms + 1
        )
        and value["elapsedMs"]
        <= expected_phase_budget_ms
        and (
            phase_elapsed_ms is None
            or (
                type(phase_elapsed_ms) is int
                and value["elapsedMs"] <= phase_elapsed_ms
            )
        )
    )
    if not relationships_hold:
        raise RuntimeError(
            "Creation method reacquisition receipt did not reconcile its gestures, "
            "screens, hierarchy reads, or phase timing"
        )


def require_one_advanced_editor_method_reacquisition(
    scans: list[dict[str, object]],
    *,
    phase_elapsed_ms: int,
) -> None:
    matches = [
        scan
        for scan in scans
        if scan.get("scanId") == CREATION_METHOD_REACQUISITION_SCAN_ID
    ]
    if len(matches) != 1:
        raise RuntimeError(
            "Advanced-editor phase requires exactly one creation method "
            f"reacquisition receipt; found {len(matches)}"
        )
    require_creation_method_reacquisition_receipt(
        matches[0],
        expected_phase_id="advanced-editor-gate-inventory",
        phase_elapsed_ms=phase_elapsed_ms,
        require_deadline=True,
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
        boundary = time.monotonic()
        first_phase = self._active_id is None and not self.phases
        self._close_active("pass", ended_at=boundary)
        self._active_id = phase_id
        self._active_started = self.started if first_phase else boundary
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
        bound_scan = {**scan, "phaseId": self._active_id}
        if (
            bound_scan.get("scanId") == CREATION_METHOD_REACQUISITION_SCAN_ID
            and bound_scan.get("status") == "resolved"
        ):
            require_creation_method_reacquisition_receipt(
                bound_scan,
                expected_phase_id="advanced-editor-gate-inventory",
                require_deadline=True,
            )
        self.scans.append(bound_scan)
        self._write("running")

    def active_phase_deadline(self, phase_id: str | None = None) -> float:
        expected_phase = self._active_id if phase_id is None else phase_id
        if (
            expected_phase is None
            or self._active_id != expected_phase
            or self._finished
        ):
            raise RuntimeError(
                f"Cannot obtain deadline for inactive progress phase {expected_phase!r}"
            )
        phase_deadline = self._active_started + (
            PHASE_BUDGET_MS[expected_phase] / 1000
        )
        total_deadline = self.started + (TOTAL_PERFORMANCE_TARGET_MS / 1000)
        return min(phase_deadline, total_deadline)

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
        finished_at = time.monotonic()
        self._close_active("pass", ended_at=finished_at)
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
        snapshot = self.snapshot("timing-complete", observed_at=finished_at)
        total_elapsed_ms = snapshot.get("totalElapsedMs")
        if type(total_elapsed_ms) is not int or total_elapsed_ms < 0:
            raise RuntimeError("Prerequisite progress total elapsed time is invalid")
        if abs(sum(phase_elapsed_ms) - total_elapsed_ms) > TIMING_ROUNDING_TOLERANCE_MS:
            raise RuntimeError(
                "Prerequisite progress phase elapsed time does not reconcile with "
                "its contiguous total: "
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

    def snapshot(
        self,
        status: str,
        *,
        observed_at: float | None = None,
    ) -> dict[str, object]:
        observed = time.monotonic() if observed_at is None else observed_at
        total_elapsed = round((observed - self.started) * 1000)
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

    def _close_active(self, status: str, *, ended_at: float | None = None) -> None:
        if self._active_id is None:
            return
        ended = time.monotonic() if ended_at is None else ended_at
        elapsed = round((ended - self._active_started) * 1000)
        budget = PHASE_BUDGET_MS[self._active_id]
        if status == "pass" and self._active_id == "advanced-editor-gate-inventory":
            require_one_advanced_editor_method_reacquisition(
                self.scans,
                phase_elapsed_ms=elapsed,
            )
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


def node_text(
    device: shared.Device,
    selector: str,
    *,
    scroll: bool = False,
    deadline: float | None = None,
) -> str:
    if deadline is None:
        node = device.wait(selector, timeout=60, scroll=scroll, max_scrolls=22)
    else:
        node = device.wait_for_single_exact_resource_id(
            selector,
            timeout=60,
            scroll=scroll,
            max_scrolls=22,
            scroll_distance_ratio=0.52,
            evidence_prefix=f"{selector}-deadline-read",
            surface_name="Deadline-bound Creation authority",
            deadline=deadline,
        )
        _require_canonical_chummer_resource_id(
            device,
            node,
            selector,
            evidence_prefix=f"{selector}-deadline-read",
            surface_name="Deadline-bound Creation authority",
            deadline=deadline,
        )
    return node.attributes.get("text") or node.attributes.get("content-desc") or ""


def _capture_with_phase_deadline(
    device: shared.Device,
    name: str,
    *,
    deadline: float,
) -> None:
    device.capture(name, deadline=deadline)


def _require_canonical_chummer_resource_id(
    device: shared.Device,
    node: shared.UiNode,
    selector: str,
    *,
    evidence_prefix: str,
    surface_name: str,
    deadline: float | None,
) -> None:
    if (
        node.attributes.get("package") == shared.PACKAGE
        and node.attributes.get("resource-id")
        == f"{shared.PACKAGE}:id/{selector}"
    ):
        return
    if deadline is None:
        device.capture(f"{evidence_prefix}-identity-invalid")
    else:
        _capture_with_phase_deadline(
            device,
            f"{evidence_prefix}-identity-invalid",
            deadline=deadline,
        )
    raise RuntimeError(
        f"{surface_name} {selector!r} did not expose the canonical Chummer resource identity"
    )


def acquire_exact_attributes_category_authority(
    device: shared.Device,
    *,
    evidence_prefix: str,
    deadline: float,
) -> tuple[shared.UiNode, str]:
    """Observe current first, then recover one exact Attributes row within 4 gestures."""
    selector = "creation-prerequisite-category-attributes"
    direction: str | None = None
    gestures = 0
    while gestures <= 4:
        require_phase_deadline(deadline, operation="Attributes category acquisition")
        nodes = device.hierarchy(deadline=deadline)
        matches = [node for node in nodes if _exact_resource_id(node) == selector]
        if len(matches) > 1:
            _capture_with_phase_deadline(
                device,
                f"{evidence_prefix}-cardinality-invalid",
                deadline=deadline,
            )
            raise RuntimeError(
                f"Typed Attributes category row has cardinality {len(matches)}; expected one"
            )
        if len(matches) == 1:
            node = matches[0]
            _require_canonical_chummer_resource_id(
                device,
                node,
                selector,
                evidence_prefix=evidence_prefix,
                surface_name="Typed Attributes category row",
                deadline=deadline,
            )
            if (
                node.attributes.get("enabled") != "true"
                or node.attributes.get("clickable") != "true"
            ):
                _capture_with_phase_deadline(
                    device,
                    f"{evidence_prefix}-not-tappable",
                    deadline=deadline,
                )
                raise RuntimeError(
                    "Typed Attributes category row was not enabled and clickable"
                )
            if device.node_has_tappable_bounds(node, deadline=deadline):
                value = (
                    node.attributes.get("text")
                    or node.attributes.get("content-desc")
                    or ""
                )
                if not value.strip():
                    _capture_with_phase_deadline(
                        device,
                        f"{evidence_prefix}-authority-blank",
                        deadline=deadline,
                    )
                    raise RuntimeError(
                        "Typed Attributes category row exposed blank authority"
                    )
                return node, value
            bounds = re.fullmatch(
                r"\[(-?[0-9]+),(-?[0-9]+)\]\[(-?[0-9]+),(-?[0-9]+)\]",
                node.attributes.get("bounds", ""),
            )
            if bounds is None:
                _capture_with_phase_deadline(
                    device,
                    f"{evidence_prefix}-bounds-invalid",
                    deadline=deadline,
                )
                raise RuntimeError("Typed Attributes category row exposed invalid bounds")
            _, top, _, bottom = (int(value) for value in bounds.groups())
            if direction is None:
                direction = "reverse" if bottom <= 0 or top < 0 else "forward"
        elif direction is None:
            # The hosted failure retained a bottom viewport with the exact row
            # wholly above it. A missing current observation therefore gets one
            # bounded reverse probe before any forward recovery.
            direction = "reverse"
        elif direction == "reverse":
            direction = "forward"
        if gestures >= 4:
            break
        if direction == "reverse":
            device.swipe_down(distance_ratio=0.22, deadline=deadline)
        else:
            device.swipe_up(distance_ratio=0.22, deadline=deadline)
        gestures += 1
        sleep_before_phase_deadline(
            0.2,
            deadline=deadline,
            operation="Attributes category post-gesture observation",
        )
    _capture_with_phase_deadline(
        device,
        f"{evidence_prefix}-unavailable",
        deadline=deadline,
    )
    raise RuntimeError(
        "Typed Attributes category row was not tappable within the current-first "
        "four-gesture recovery bound"
    )


def require_exact_zero_gesture_route(
    device: shared.Device,
    selector: str,
    *,
    evidence_prefix: str,
    surface_name: str,
    deadline: float,
) -> shared.UiNode:
    """Prove one exact route without authorizing a gesture or product action."""
    node = device.wait_for_single_exact_resource_id(
        selector,
        timeout=ZERO_GESTURE_ROUTE_PROOF_TIMEOUT_SECONDS,
        scroll=False,
        max_scrolls=0,
        evidence_prefix=evidence_prefix,
        surface_name=surface_name,
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        node,
        selector,
        evidence_prefix=evidence_prefix,
        surface_name=surface_name,
        deadline=deadline,
    )
    return node


def require_exact_attributes_post_back_observation(
    device: shared.Device,
    expected_authority: str,
    *,
    deadline: float,
) -> None:
    """Fine-scan the restored route through its exact disabled Attributes row."""
    selectors = (
        "creation-prerequisite-page",
        "creation-prerequisite-category-attributes",
        "creation-prerequisite-attributes-disabled",
    )
    screens = scan_forward_until_stable(
        device,
        scan_id="creation-prerequisite-attributes-post-back",
        max_scrolls=12,
        distance_ratio=0.22,
        stable_repeats=2,
        max_consecutive_empty_reads=3,
        delay_seconds=0.0,
        deadline=deadline,
    )
    values, viewports, _ = collect_exact_contiguous_authority_values(
        device,
        screens,
        selectors,
        evidence_prefix="creation-prerequisite-attributes-post-back",
        require_nonblank=frozenset(
            {"creation-prerequisite-category-attributes"}
        ),
        deadline=deadline,
    )
    if (
        viewports["creation-prerequisite-page"][0] != 0
        or viewports["creation-prerequisite-category-attributes"][0] != 0
        or values["creation-prerequisite-category-attributes"]
        != expected_authority
    ):
        _capture_with_phase_deadline(
            device,
            "creation-prerequisite-attributes-authority-changed",
            deadline=deadline,
        )
        raise RuntimeError(
            "Back navigation did not restore the byte-for-byte typed Attribute "
            "rank selection at the exact current route"
        )


def require_exact_attributes_category_round_trip(
    device: shared.Device,
    *,
    deadline: float,
) -> str:
    """Open Attributes and return through one exact, non-replayable navigation path."""
    node, before = acquire_exact_attributes_category_authority(
        device,
        evidence_prefix="creation-prerequisite-attributes-before",
        deadline=deadline,
    )
    x, y = node.center
    device.shell(
        "input",
        "tap",
        str(x),
        str(y),
        timeout=shared._remaining_operation_timeout(
            deadline=deadline,
            maximum=120,
        ),
        deadline=deadline,
    )
    require_exact_zero_gesture_route(
        device,
        "creation-prerequisite-category-page",
        evidence_prefix="creation-prerequisite-attributes-category-route",
        surface_name="Attributes category detail route",
        deadline=deadline,
    )
    device.back(deadline=deadline)
    require_exact_attributes_post_back_observation(
        device,
        before,
        deadline=deadline,
    )
    return before


def open_exact_prerequisite_preview(
    device: shared.Device,
    *,
    deadline: float,
) -> None:
    """Acquire and tap one exact Preview action, then prove its exact route."""
    selector = "creation-prerequisite-prepare-preview"
    node = device.wait_exact_resource_id_bidirectional(
        selector,
        timeout=60,
        backward_scrolls=0,
        forward_scrolls=4,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-prepare-preview",
        surface_name="Creation prerequisite Preview action",
        require_tappable=True,
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        node,
        selector,
        evidence_prefix="creation-prerequisite-prepare-preview",
        surface_name="Creation prerequisite Preview action",
        deadline=deadline,
    )
    if (
        node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node, deadline=deadline)
    ):
        _capture_with_phase_deadline(
            device,
            "creation-prerequisite-prepare-preview-not-tappable",
            deadline=deadline,
        )
        raise RuntimeError(
            "Creation prerequisite Preview action was not enabled, clickable, and tappable"
        )
    action_deadline = persistent_action_deadline(
        deadline,
        action_timeout_seconds=PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS,
        proof_timeout_seconds=PREVIEW_ROUTE_PROOF_TIMEOUT_SECONDS,
        operation="the exact Prepare Preview action",
    )
    x, y = node.center
    device.shell(
        "input",
        "tap",
        str(x),
        str(y),
        timeout=shared._remaining_operation_timeout(
            deadline=action_deadline,
            maximum=PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS,
        ),
        deadline=action_deadline,
    )
    proof_deadline = immediate_proof_deadline(
        deadline,
        PREVIEW_ROUTE_PROOF_TIMEOUT_SECONDS,
        operation="the exact Preview route proof",
    )
    require_exact_zero_gesture_route(
        device,
        "creation-prerequisite-preview-page",
        evidence_prefix="creation-prerequisite-preview-route",
        surface_name="Creation prerequisite Preview route",
        deadline=proof_deadline,
    )


def read_exact_skill_group_talent_selection_id(
    device: shared.Device,
    *,
    deadline: float,
) -> str:
    """Read one exact post-grant SelectionId without a product action or fallback."""
    node = device.wait_exact_resource_id_bidirectional(
        "creation-prerequisite-talent-selection-id",
        timeout=60,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-skill-group-talent-selection-id",
        surface_name="Skill-group Talent SelectionId authority",
        require_tappable=False,
        deadline=deadline,
    )
    value = (
        node.attributes.get("text")
        or node.attributes.get("content-desc")
        or ""
    ).strip()
    if not value:
        raise RuntimeError(
            "Skill-group Talent SelectionId authority did not expose an exact value"
        )
    return value


class CreationDashboardScanProof(NamedTuple):
    binding: str
    method_detail: str
    swipes: int
    method_viewport: int


def require_marker_bound_post_confirm_dashboard(
    marker: dict[str, object],
    dashboard: CreationDashboardScanProof,
) -> None:
    """Bind the app readiness marker to the exact visible dashboard snapshot."""
    content_revision = marker.get("contentRevision")
    snapshot_digest = marker.get("snapshotDigest")
    if (
        type(content_revision) is not int
        or content_revision <= 0
        or not isinstance(snapshot_digest, str)
        or CANONICAL_AUTHORITY_DIGEST.fullmatch(snapshot_digest) is None
    ):
        raise RuntimeError(
            "Post-confirm dashboard marker did not expose canonical binding authority"
        )
    expected_binding = (
        f"Revision {content_revision} · snapshot {snapshot_digest[:12]}"
    )
    if dashboard.binding != expected_binding:
        raise RuntimeError(
            "Post-confirm dashboard did not expose the marker-bound revision and "
            "snapshot digest: "
            f"expected={expected_binding!r}, actual={dashboard.binding!r}"
        )


def wait_for_compact_dashboard_origin(
    device: shared.Device,
    *,
    scan_id: str,
    deadline: float,
) -> list[shared.UiNode]:
    """Observe the post-Back dashboard transition without another action.

    The exact Back tap is persistent and must never be replayed.  API 36 may
    briefly exposes the outgoing receipt viewport while MAUI publishes the
    dashboard.  The caller first waits for the fresh, revision-bound,
    post-layout app marker, then takes exactly one fresh file-backed hierarchy.
    Both canonical dashboard identities must share that one snapshot. This
    route-specific observer deliberately disables the shared
    direct ``/dev/tty`` reconciliation path: an ambiguous file dump may inspect
    only its pre-cleared owned file and must never invoke another UIAutomator
    process. Duplicate identities remain an immediate fail-closed error.
    """
    selectors = ("phone-runner-create", "creation-wizard-dashboard")
    require_phase_deadline(deadline, operation="compact dashboard transition")
    current_nodes = device.hierarchy(
        deadline=deadline,
        dump_attempt_max_seconds=(
            POST_CONFIRM_DASHBOARD_DUMP_ATTEMPT_MAX_SECONDS
        ),
        allow_direct_reconciliation=False,
    )
    cardinalities = {
        selector: [
            node
            for node in current_nodes
            if _exact_resource_id(node) == selector
        ]
        for selector in selectors
    }
    for selector, exact in cardinalities.items():
        if len(exact) > 1:
            _capture_with_phase_deadline(
                device,
                f"{scan_id}-{selector}-current-cardinality-invalid",
                deadline=deadline,
            )
            raise RuntimeError(
                f"Compact dashboard current origin exposed {len(exact)} exact "
                f"{selector!r} nodes"
            )
    missing = [
        selector for selector in selectors if len(cardinalities[selector]) != 1
    ]
    if missing:
        _capture_with_phase_deadline(
            device,
            f"{scan_id}-current-transition-unavailable",
            deadline=deadline,
        )
        raise RuntimeError(
            "The single post-marker dashboard snapshot did not expose both exact "
            f"route identities; missing={missing!r}"
        )
    for selector in selectors:
        _require_canonical_chummer_resource_id(
            device,
            cardinalities[selector][0],
            selector,
            evidence_prefix=f"{scan_id}-{selector}-current",
            surface_name="Compact dashboard current origin",
            deadline=deadline,
        )
    return current_nodes


def assert_uncreated_advanced_editor_gated(
    device: shared.Device,
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str = "advanced-editor-gate",
    deadline: float | None = None,
    compact_current: bool = False,
) -> CreationDashboardScanProof:
    """Scan the dashboard once for forbidden controls and reusable authority."""
    def capture(name: str) -> None:
        if deadline is None:
            device.capture(name)
        else:
            device.capture(name, deadline=deadline)

    def visible(node: shared.UiNode) -> bool:
        return (
            device.node_has_tappable_bounds(node)
            if deadline is None
            else device.node_has_tappable_bounds(node, deadline=deadline)
        )

    forbidden = (
        "Actions",
        "build-origin-dossier",
        "build-free-sprite-conversion",
        "build-career-create-expense",
        "creation-wizard-attributes",
        "attribute-save-",
    )
    if compact_current:
        if deadline is None:
            raise ValueError("Compact dashboard proof requires one absolute deadline")
        current_nodes = wait_for_compact_dashboard_origin(
            device,
            scan_id=scan_id,
            deadline=deadline,
        )
        scan_origin = PriorityRankOrigin(
            current_nodes,
            0,
            0,
            (0,),
            0,
        )
        max_scrolls = POST_CONFIRM_DASHBOARD_SCAN_MAX_SCROLLS
    else:
        scan_origin = acquire_stable_start_origin(
            device,
            scan_id=f"{scan_id}-origin",
            max_reverse_swipes=8,
            distance_ratio=DASHBOARD_SCAN_GESTURE_RATIO,
            stable_repeats=2,
            max_consecutive_empty_reads=3,
            delay_seconds=0.0,
            deadline=deadline,
        )
        max_scrolls = DASHBOARD_SCAN_MAX_SCROLLS
    scan_options: dict[str, object] = {}
    if compact_current:
        scan_options.update(
            hierarchy_dump_attempt_max_seconds=(
                POST_CONFIRM_DASHBOARD_DUMP_ATTEMPT_MAX_SECONDS
            ),
            allow_direct_hierarchy_reconciliation=False,
            allow_direct_swipe_reconciliation=False,
        )
    scan = scan_forward_with_receipt(
        device,
        scan_id=scan_id,
        max_scrolls=max_scrolls,
        distance_ratio=DASHBOARD_SCAN_GESTURE_RATIO,
        initial_observation=scan_origin,
        delay_seconds=0.0,
        observer=scan_observer,
        deadline=deadline,
        **scan_options,
    )
    bindings: set[str] = set()
    method_states: set[tuple[str, str, str]] = set()
    method_viewports: set[int] = set()
    exact_dashboard_resource_ids = (
        "phone-runner-create",
        "creation-wizard-dashboard",
    )
    exact_dashboard_ids = (*exact_dashboard_resource_ids, "build-save-runner")
    dashboard_states: dict[str, set[tuple[str, str]]] = {
        selector: set() for selector in exact_dashboard_ids
    }
    dashboard_visible: set[str] = set()
    for viewport_index, nodes in enumerate(scan.screens):
        for selector in forbidden:
            if any(shared.Device._matches(node, selector) for node in nodes):
                capture(f"wizard-forbidden-{selector}")
                raise RuntimeError(
                    "Creation dashboard exposed a Career/advanced-editor control while "
                    f"the authoritative runner is still uncreated: {selector!r}"
                )
        for selector in exact_dashboard_resource_ids if deadline is not None else ():
            matches = [node for node in nodes if _exact_resource_id(node) == selector]
            if len(matches) > 1:
                capture(f"{scan_id}-{selector}-cardinality-invalid")
                raise RuntimeError(
                    f"Creation dashboard {selector!r} has cardinality {len(matches)}"
                )
            if len(matches) == 1:
                node = matches[0]
                if (
                    node.attributes.get("package") != shared.PACKAGE
                    or node.attributes.get("resource-id")
                    != f"{shared.PACKAGE}:id/{selector}"
                ):
                    capture(f"{scan_id}-{selector}-identity-invalid")
                    raise RuntimeError(
                        f"Creation dashboard {selector!r} was not canonical"
                    )
                dashboard_states[selector].add(
                    (
                        node.attributes.get("enabled", ""),
                        node.attributes.get("clickable", ""),
                    )
                )
                if visible(node):
                    dashboard_visible.add(selector)
        if deadline is not None:
            selector = "build-save-runner"
            matches = [
                node
                for node in nodes
                if selector
                in {
                    _exact_resource_id(node),
                    node.attributes.get("content-desc", ""),
                }
            ]
            if len(matches) > 1:
                capture(f"{scan_id}-{selector}-cardinality-invalid")
                raise RuntimeError(
                    f"Creation dashboard {selector!r} has cardinality {len(matches)}"
                )
            if len(matches) == 1:
                node = matches[0]
                if (
                    node.attributes.get("resource-id") != ""
                    or node.attributes.get("package") != shared.PACKAGE
                    or node.attributes.get("class") != "android.widget.Button"
                    or node.attributes.get("content-desc") != selector
                    or node.attributes.get("focusable") != "true"
                ):
                    capture(f"{scan_id}-{selector}-identity-invalid")
                    raise RuntimeError(
                        f"Creation dashboard {selector!r} was not the canonical "
                        "native toolbar accessibility node"
                    )
                dashboard_states[selector].add(
                    (
                        node.attributes.get("enabled", ""),
                        node.attributes.get("clickable", ""),
                    )
                )
                if visible(node):
                    dashboard_visible.add(selector)
        for selector in ("creation-wizard-binding", "creation-stage-method"):
            matches = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(matches) > 1:
                capture(f"{scan_id}-{selector}-cardinality-invalid")
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
                        if visible(matches[0]):
                            method_viewports.add(min(viewport_index, scan.swipes))
                        method_states.add((
                            value,
                            matches[0].attributes.get("enabled", ""),
                            matches[0].attributes.get("clickable", ""),
                        ))
    invalid_dashboard = {
        selector: sorted(states)
        for selector, states in dashboard_states.items()
        if len(states) != 1
    }
    toolbar_state = dashboard_states["build-save-runner"]
    if (
        len(bindings) != 1
        or len(method_states) != 1
        or not method_viewports
    ):
        capture(f"{scan_id}-authority-incomplete")
        raise RuntimeError(
            "Creation dashboard stable scan did not expose one binding and one "
            "tappable method authority: "
            f"bindings={sorted(bindings)!r}, methods={sorted(method_states)!r}, "
            f"methodViewports={sorted(method_viewports)!r}"
        )
    if deadline is not None and (
        invalid_dashboard
        or "creation-wizard-dashboard" not in dashboard_visible
        or "build-save-runner" not in dashboard_visible
        or toolbar_state != {("true", "true")}
    ):
        capture(f"{scan_id}-authority-incomplete")
        raise RuntimeError(
            "Creation dashboard stable scan did not expose its canonical route, root, "
            "toolbar, binding, and method row: "
            f"bindings={sorted(bindings)!r}, methods={sorted(method_states)!r}, "
            f"methodViewports={sorted(method_viewports)!r}, "
            f"dashboardStates={invalid_dashboard!r}, visible={sorted(dashboard_visible)!r}"
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


def canonical_digest(
    device: shared.Device,
    selector: str,
    *,
    scroll: bool = False,
    deadline: float | None = None,
) -> str:
    value = node_text(device, selector, scroll=scroll, deadline=deadline).strip()
    if CANONICAL_AUTHORITY_DIGEST.fullmatch(value) is None:
        raise RuntimeError(f"{selector} did not expose one canonical digest: {value!r}")
    return value


def canonical_auxiliary_state_digest(
    device: shared.Device,
    selector: str,
    *,
    scroll: bool = False,
    deadline: float | None = None,
) -> str:
    value = node_text(device, selector, scroll=scroll, deadline=deadline).strip()
    if CANONICAL_AUXILIARY_STATE_DIGEST.fullmatch(value) is None:
        raise RuntimeError(
            f"{selector} did not expose one canonical auxiliary-state digest: {value!r}"
        )
    return value


def nonnegative_integer(
    device: shared.Device,
    selector: str,
    *,
    scroll: bool = False,
    deadline: float | None = None,
) -> int:
    value = node_text(device, selector, scroll=scroll, deadline=deadline).strip()
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
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    stable_repeats: int = 2,
    max_system_ui_dismissals: int = 3,
    phase_id: str = "advanced-editor-gate-inventory",
    deadline: float | None = None,
) -> tuple[shared.UiNode, str, int]:
    """Observe back to the exact method without assuming swipe-count symmetry.

    Android's equal-distance up/down gestures do not establish an invertible
    MAUI ScrollView coordinate.  Observe every reverse endpoint and stop only
    on the exact ready method, a separately proven stable start, or an explicit
    safety bound.
    """
    if (
        type(max_swipes) is not int
        or max_swipes < 0
        or max_swipes > DASHBOARD_SCAN_MAX_SCROLLS
        or type(stable_repeats) is not int
        or stable_repeats < 1
        or type(max_system_ui_dismissals) is not int
        or max_system_ui_dismissals < 0
        or type(phase_id) is not str
        or phase_id not in CREATION_METHOD_REACQUISITION_PHASE_AUTHORITY
    ):
        raise ValueError(
            "Creation method restoration requires exact gesture, stable-start, "
            "empty-hierarchy, and system-UI bounds"
        )
    phase_budget_ms, max_empty_hierarchy_reads = (
        CREATION_METHOD_REACQUISITION_PHASE_AUTHORITY[phase_id]
    )

    started = time.monotonic()
    hierarchy_durations_ms: list[int] = []
    reverse_swipes = 0
    screens = 0
    empty_hierarchy_reads = 0
    system_ui_dismissals = 0
    previous_signature: tuple[tuple[str, ...], ...] | None = None
    unchanged_post_gesture = 0
    pending_post_gesture = False

    def capture(name: str) -> None:
        if deadline is None:
            device.capture(name)
        else:
            device.capture(name, deadline=deadline)

    def record(status: str) -> None:
        receipt: dict[str, object] = {
            "scanId": CREATION_METHOD_REACQUISITION_SCAN_ID,
            "status": status,
            "direction": CREATION_METHOD_REACQUISITION_DIRECTION,
            "distanceRatio": DASHBOARD_SCAN_GESTURE_RATIO,
            "screens": screens,
            "swipes": reverse_swipes,
            "configuredMaxScrolls": max_swipes,
            "stableRepeats": stable_repeats,
            "emptyHierarchyReads": empty_hierarchy_reads,
            "maximumEmptyHierarchyReads": max_empty_hierarchy_reads,
            "systemUiDismissals": system_ui_dismissals,
            "maximumSystemUiDismissals": max_system_ui_dismissals,
            "deadlineEnforced": deadline is not None,
            "phaseId": phase_id,
            "phaseBudgetMs": phase_budget_ms,
            **hierarchy_timing_fields(hierarchy_durations_ms),
            "elapsedMs": round((time.monotonic() - started) * 1000),
        }
        if status == "resolved":
            require_creation_method_reacquisition_receipt(
                receipt,
                expected_phase_id=phase_id,
            )
        if scan_observer is not None:
            scan_observer(receipt)

    def reject_not_tappable() -> None:
        record("not-tappable")
        capture("creation-stage-method-ready-not-tappable")
        raise RuntimeError(
            "Measured ready creation method navigation 'creation-stage-method' "
            "was not visible, enabled, and clickable"
        )

    while reverse_swipes <= max_swipes:
        nodes = fresh_hierarchy_timed(
            device,
            hierarchy_durations_ms,
            deadline=deadline,
        )
        if not nodes:
            empty_hierarchy_reads += 1
            if empty_hierarchy_reads > max_empty_hierarchy_reads:
                record("reverse-empty-hierarchy-exhausted")
                capture("creation-stage-method-ready-empty-hierarchy-exhausted")
                raise RuntimeError(
                    "Measured ready creation method navigation exhausted its separate "
                    f"transient empty-hierarchy budget of {max_empty_hierarchy_reads} reads"
                )
            sleep_before_phase_deadline(
                0.75,
                deadline=deadline,
                operation="method-reacquisition empty-hierarchy wait",
            )
            continue
        screens += 1
        matches = [
            node
            for node in nodes
            if _exact_resource_id(node) == "creation-stage-method"
        ]
        if len(matches) > 1:
            record("cardinality-invalid")
            capture("creation-stage-method-ready-cardinality-invalid")
            raise RuntimeError(
                "Measured ready creation method navigation 'creation-stage-method' "
                f"has cardinality {len(matches)}; expected one"
            )
        if len(matches) == 1:
            node = matches[0]
            visible = (
                device.node_has_tappable_bounds(node)
                if deadline is None
                else device.node_has_tappable_bounds(node, deadline=deadline)
            )
            interactive = (
                node.attributes.get("enabled") == "true"
                and node.attributes.get("clickable") == "true"
            )
            if visible and interactive:
                try:
                    detail = require_creation_method_navigation(node, ready=True)
                except RuntimeError:
                    record("not-ready")
                    capture("creation-stage-method-ready-not-ready")
                    raise
                if detail != expected_detail:
                    record("detail-changed")
                    capture("creation-stage-method-changed-after-dashboard-scan")
                    raise RuntimeError(
                        "Creation method authority changed between the stable dashboard "
                        f"scan and tap: scan={expected_detail!r}, tap={detail!r}"
                    )
                record("resolved")
                return node, detail, reverse_swipes
            if not interactive:
                reject_not_tappable()

        dismissed = (
            device.dismiss_system_ui_anr(nodes)
            if deadline is None
            else device.dismiss_system_ui_anr(nodes, deadline=deadline)
        )
        if dismissed:
            system_ui_dismissals += 1
            if system_ui_dismissals > max_system_ui_dismissals:
                record("reverse-system-ui-exhausted")
                capture("creation-stage-method-ready-system-ui-exhausted")
                raise RuntimeError(
                    "Measured ready creation method navigation exhausted its separate "
                    f"system-UI dismissal budget of {max_system_ui_dismissals}"
                )
            sleep_before_phase_deadline(
                2,
                deadline=deadline,
                operation="method-reacquisition system-UI wait",
            )
            continue

        signature = accessibility_signature(nodes)
        if pending_post_gesture:
            unchanged_post_gesture = (
                unchanged_post_gesture + 1
                if previous_signature is not None and signature == previous_signature
                else 0
            )
            pending_post_gesture = False
        previous_signature = signature
        if unchanged_post_gesture >= stable_repeats:
            if matches:
                reject_not_tappable()
            record("stable-start-without-method")
            capture("creation-stage-method-ready-stable-start-without-method")
            raise RuntimeError(
                "Creation dashboard proved a stable start without the exact ready "
                "creation-stage-method"
            )
        if reverse_swipes >= max_swipes:
            if matches:
                reject_not_tappable()
            break
        if deadline is None:
            device.swipe_down(distance_ratio=DASHBOARD_SCAN_GESTURE_RATIO)
        else:
            device.swipe_down(
                distance_ratio=DASHBOARD_SCAN_GESTURE_RATIO,
                deadline=deadline,
            )
        reverse_swipes += 1
        pending_post_gesture = True
        sleep_before_phase_deadline(
            0.2,
            deadline=deadline,
            operation="method-reacquisition post-swipe wait",
        )

    record("reverse-bound-exhausted")
    capture("creation-stage-method-ready-unavailable")
    raise RuntimeError(
        "Timed out reversing to exact measured ready creation method navigation "
        f"'creation-stage-method' within the dashboard scan bound of {max_swipes} swipes"
    )


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


class PersistedPrerequisiteAuthorityRead(NamedTuple):
    authority: dict[str, object]
    selection_ids: dict[str, str]
    attributes_authority: str
    navigation: dict[str, object]
    attributes_ready: bool


def read_persisted_prerequisite_authority(
    device: shared.Device,
    *,
    initial_observation: PriorityRankOrigin,
    deadline: float,
    scan_observer: Callable[[dict[str, object]], None] | None,
    scan_id: str,
    max_consecutive_empty_reads: int = 3,
) -> PersistedPrerequisiteAuthorityRead:
    proof = scan_persisted_prerequisite_authority(
        device,
        initial_observation=initial_observation,
        deadline=deadline,
        scan_observer=scan_observer,
        scan_id=scan_id,
        max_consecutive_empty_reads=max_consecutive_empty_reads,
    )
    values = proof.values
    binding = require_prerequisite_binding(
        values["creation-prerequisite-binding"]
    )
    snapshot_digest = values["creation-prerequisite-snapshot-digest"]
    binding_digests = {
        "rawCharacterXml": values[
            "creation-prerequisite-raw-character-xml-digest"
        ],
        "auxiliaryState": values[
            "creation-prerequisite-auxiliary-state-digest"
        ],
        "authority": values["creation-prerequisite-authority-digest"],
    }
    require_binding_matches_canonical_digests(
        binding,
        snapshot_digest,
        binding_digests["authority"],
    )
    authority = {
        "binding": binding,
        "snapshotDigest": snapshot_digest,
        "bindingDigests": binding_digests,
        "draftDigest": values["creation-prerequisite-pending-draft-digest"],
    }
    return PersistedPrerequisiteAuthorityRead(
        authority=authority,
        selection_ids={
            category: values[f"creation-prerequisite-{category}-selection-id"]
            for category in ("heritage", "talent")
        },
        attributes_authority=values[
            "creation-prerequisite-category-attributes"
        ],
        navigation={
            "currentViewport": proof.swipes,
            "selectionViewports": dict(proof.selection_viewports),
        },
        attributes_ready=(
            values["creation-prerequisite-attributes-ready"] == "present"
        ),
    )


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


def require_resources_confirmation_authority_transition(
    confirmed_prerequisite_binding_digests: dict[str, str],
    confirmed_prerequisite_revisions: dict[str, int],
    confirmed_prerequisite_draft_digest: str,
    resources_before: dict[str, object],
    resources_after: dict[str, object],
) -> dict[str, str]:
    """Bind one Resources mutation without conflating domain authorities.

    Prerequisite and Resources authority digests are independently typed Core
    authorities.  They are intentionally not comparable to each other.  The
    workspace revisions plus content and auxiliary-state digests are the
    cross-domain binding: Resources must start from the exact confirmed
    content/saved revisions and prerequisite state, preserve raw character XML
    and the prerequisite draft, advance auxiliary state, and preserve its own
    Resources authority.  Re-entering Prerequisite must then expose the new
    workspace binding while preserving the confirmed Prerequisite authority.
    """
    before_raw = str(resources_before.get("rawCharacterXmlDigest", ""))
    before_auxiliary = str(resources_before.get("auxiliaryStateDigest", ""))
    before_resources_authority = str(resources_before.get("authorityDigest", ""))
    before_content_revision = int(resources_before.get("contentRevision", -1))
    before_saved_revision = int(resources_before.get("savedRevision", -1))
    before_prerequisite_draft = str(
        resources_before.get("prerequisiteDraftDigest", "")
    )
    after_raw = str(resources_after.get("rawCharacterXmlDigest", ""))
    after_auxiliary = str(resources_after.get("auxiliaryStateDigest", ""))
    after_resources_authority = str(resources_after.get("authorityDigest", ""))
    after_prerequisite_draft = str(
        resources_after.get("prerequisiteDraftDigest", "")
    )

    if (
        before_content_revision
        != confirmed_prerequisite_revisions["contentRevision"]
        or before_saved_revision
        != confirmed_prerequisite_revisions["savedRevision"]
        or before_raw != confirmed_prerequisite_binding_digests["rawCharacterXml"]
        or before_auxiliary
        != confirmed_prerequisite_binding_digests["auxiliaryState"]
        or before_prerequisite_draft != confirmed_prerequisite_draft_digest
    ):
        raise RuntimeError(
            "Resources did not start from the confirmed prerequisite workspace binding: "
            f"prerequisite={confirmed_prerequisite_binding_digests!r}, "
            f"resourcesBefore={resources_before!r}"
        )
    if (
        after_raw != before_raw
        or after_resources_authority != before_resources_authority
        or after_prerequisite_draft != before_prerequisite_draft
        or after_auxiliary == before_auxiliary
    ):
        raise RuntimeError(
            "Resources confirmation changed raw XML, Resources authority, or prerequisite "
            "draft, or failed to advance auxiliary state: "
            f"before={resources_before!r}, after={resources_after!r}"
        )

    return {
        "rawCharacterXml": after_raw,
        "auxiliaryState": after_auxiliary,
        "authority": confirmed_prerequisite_binding_digests["authority"],
    }


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


PERSISTED_PREREQUISITE_AUTHORITY_SELECTORS = (
    "creation-prerequisite-binding",
    "creation-prerequisite-method",
    "creation-prerequisite-karma-budget",
    "creation-prerequisite-snapshot-digest",
    "creation-prerequisite-raw-character-xml-digest",
    "creation-prerequisite-auxiliary-state-digest",
    "creation-prerequisite-authority-digest",
    "creation-prerequisite-pending-draft",
    "creation-prerequisite-pending-draft-digest",
    "creation-prerequisite-heritage-selection-id",
    "creation-prerequisite-talent-selection-id",
    "creation-prerequisite-category-attributes",
    "creation-prerequisite-attributes-ready",
)
PERSISTED_PREREQUISITE_PRESENCE_ONLY_SELECTORS = frozenset(
    {
        "creation-prerequisite-pending-draft",
        "creation-prerequisite-attributes-ready",
    }
)


class PersistedPrerequisiteAuthorityScanProof(NamedTuple):
    values: dict[str, str]
    swipes: int
    selection_viewports: dict[str, int]


def wait_for_prerequisite_scan_origin(
    device: shared.Device,
    *,
    timeout: float = 60.0,
    max_reverse_swipes: int = 8,
    distance_ratio: float = 0.68,
    deadline: float | None = None,
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
    operation_deadline = time.monotonic() + timeout
    if deadline is not None:
        operation_deadline = min(operation_deadline, deadline)
    reverse_swipes = 0
    empty_hierarchy_reads = 0
    hierarchy_durations_ms: list[int] = []
    while time.monotonic() < operation_deadline:
        nodes = fresh_hierarchy_timed(
            device,
            hierarchy_durations_ms,
            deadline=operation_deadline,
        )
        if not nodes:
            empty_hierarchy_reads += 1
            sleep_before_phase_deadline(
                0.75,
                deadline=operation_deadline,
                operation="prerequisite scan-origin empty-hierarchy wait",
            )
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
            device.capture(
                "creation-prerequisite-scan-origin-cardinality-invalid",
                deadline=operation_deadline,
            )
            raise RuntimeError(
                "Creation prerequisite scan origin was ambiguous: "
                f"{ambiguous!r}"
            )
        if len(matches[route_selector]) == 1 and all(
            len(matches[selector]) == 1 for selector in top_selectors
        ):
            for selector in (route_selector, *top_selectors):
                _require_canonical_chummer_resource_id(
                    device,
                    matches[selector][0],
                    selector,
                    evidence_prefix="creation-prerequisite-scan-origin",
                    surface_name="Creation prerequisite scan origin",
                    deadline=operation_deadline,
                )
            return PriorityRankOrigin(
                nodes=nodes,
                reverse_swipes=reverse_swipes,
                elapsed_ms=round((time.monotonic() - started) * 1000),
                hierarchy_durations_ms=tuple(hierarchy_durations_ms),
                empty_hierarchy_reads=empty_hierarchy_reads,
            )
        if device.dismiss_system_ui_anr(nodes, deadline=operation_deadline):
            sleep_before_phase_deadline(
                2,
                deadline=operation_deadline,
                operation="prerequisite scan-origin system-UI wait",
            )
            continue
        if len(matches[route_selector]) == 1 and reverse_swipes < max_reverse_swipes:
            device.swipe_down(
                distance_ratio=distance_ratio,
                deadline=operation_deadline,
            )
            reverse_swipes += 1
            continue
        sleep_before_phase_deadline(
            0.25,
            deadline=operation_deadline,
            operation="prerequisite scan-origin retry wait",
        )
    device.capture(
        "creation-prerequisite-scan-origin-unavailable",
        deadline=operation_deadline,
    )
    raise RuntimeError(
        "Timed out acquiring the exact prerequisite route and both top authority "
        f"anchors within {max_reverse_swipes} reverse swipes"
    )


def scan_prerequisite_authority(
    device: shared.Device,
    *,
    initial_observation: PriorityRankOrigin,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    deadline: float | None = None,
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
        deadline=deadline,
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


def scan_persisted_prerequisite_authority(
    device: shared.Device,
    *,
    initial_observation: PriorityRankOrigin,
    deadline: float,
    scan_observer: Callable[[dict[str, object]], None] | None,
    scan_id: str,
    max_consecutive_empty_reads: int = 3,
) -> PersistedPrerequisiteAuthorityScanProof:
    """Read the complete resumed root authority in one deadline-bound traversal."""
    if not scan_id:
        raise ValueError("Persisted prerequisite authority requires a named scan")

    def capture(name: str) -> None:
        device.capture(name, deadline=deadline)

    scan = scan_forward_with_receipt(
        device,
        scan_id=scan_id,
        max_scrolls=22,
        distance_ratio=0.22,
        initial_observation=initial_observation,
        delay_seconds=0.0,
        observer=scan_observer,
        deadline=deadline,
        max_consecutive_empty_reads=max_consecutive_empty_reads,
    )
    observed: dict[str, set[str]] = {
        selector: set()
        for selector in PERSISTED_PREREQUISITE_AUTHORITY_SELECTORS
        if selector not in PERSISTED_PREREQUISITE_PRESENCE_ONLY_SELECTORS
    }
    value_signatures: dict[str, set[tuple[str, ...]]] = {
        selector: set() for selector in observed
    }
    presence_signatures: dict[str, set[tuple[str, ...]]] = {
        selector: set()
        for selector in PERSISTED_PREREQUISITE_PRESENCE_ONLY_SELECTORS
    }
    selection_viewports: dict[str, set[int]] = {
        category: set() for category in ("heritage", "talent")
    }
    selection_semantics: dict[str, tuple[str, ...]] = {}
    for viewport_index, nodes in enumerate(scan.screens):
        for selector in PERSISTED_PREREQUISITE_AUTHORITY_SELECTORS:
            matches = [
                node for node in nodes if _exact_resource_id(node) == selector
            ]
            if len(matches) > 1:
                capture(f"{scan_id}-{selector}-cardinality-invalid")
                raise RuntimeError(
                    f"Persisted prerequisite authority {selector!r} has cardinality "
                    f"{len(matches)} in one viewport"
                )
            if len(matches) != 1:
                continue
            node = matches[0]
            _require_canonical_chummer_resource_id(
                device,
                node,
                selector,
                evidence_prefix=f"{scan_id}-{selector}",
                surface_name="Persisted prerequisite authority",
                deadline=deadline,
            )
            if selector in PERSISTED_PREREQUISITE_PRESENCE_ONLY_SELECTORS:
                presence_signatures[selector].add(
                    tuple(
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
                )
                continue
            value = (
                node.attributes.get("text")
                or node.attributes.get("content-desc")
                or ""
            ).strip()
            observed[selector].add(value)
            value_signatures[selector].add(
                tuple(
                    node.attributes.get(key, "")
                    for key in (
                        "resource-id",
                        "package",
                        "class",
                        "content-desc",
                        "text",
                        "enabled",
                        "clickable",
                        "focusable",
                    )
                )
            )

        for category in ("heritage", "talent"):
            selector = f"creation-prerequisite-{category}-selection"
            matches = [
                node for node in nodes if _exact_resource_id(node) == selector
            ]
            if len(matches) > 1:
                capture(f"{scan_id}-{category}-selection-cardinality-invalid")
                raise RuntimeError(
                    f"Persisted {category} selection row has cardinality "
                    f"{len(matches)} in one viewport"
                )
            if len(matches) != 1:
                continue
            node = matches[0]
            _require_canonical_chummer_resource_id(
                device,
                node,
                selector,
                evidence_prefix=f"{scan_id}-{category}-selection",
                surface_name=f"Persisted {category} selection row",
                deadline=deadline,
            )
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
            previous = selection_semantics.setdefault(category, signature)
            if signature != previous:
                capture(f"{scan_id}-{category}-selection-drift")
                raise RuntimeError(
                    f"Persisted {category} selection row changed semantics during "
                    "the authority scan"
                )
            tappable = device.node_has_tappable_bounds(node, deadline=deadline)
            if (
                node.attributes.get("enabled") != "true"
                or node.attributes.get("clickable") != "true"
                or not tappable
            ):
                # A row can be present in an overlap viewport while clipped or
                # offscreen.  It is evidence of identity/semantics only, never
                # navigation authority.  Continue the measured scan so a later
                # viewport can provide the one genuinely tappable occurrence;
                # invalid_navigation below still fails closed if none does.
                continue
            selection_viewports[category].add(min(viewport_index, scan.swipes))

    invalid_values = {
        selector: sorted(values)
        for selector, values in observed.items()
        if len(values) != 1 or "" in values
    }
    invalid_presence = {
        selector: len(signatures)
        for selector, signatures in presence_signatures.items()
        if len(signatures) != 1
    }
    invalid_semantics = {
        selector: len(signatures)
        for selector, signatures in value_signatures.items()
        if len(signatures) != 1
    }
    invalid_navigation = {
        category: sorted(viewports)
        for category, viewports in selection_viewports.items()
        if not viewports
    }
    if (
        invalid_values
        or invalid_presence
        or invalid_semantics
        or invalid_navigation
    ):
        capture(f"{scan_id}-invalid")
        raise RuntimeError(
            "Persisted prerequisite authority scan was incomplete or changed while "
            f"scrolling: values={invalid_values!r}, presence={invalid_presence!r}, "
            f"semantics={invalid_semantics!r}, navigation={invalid_navigation!r}"
        )
    values = {selector: next(iter(entries)) for selector, entries in observed.items()}
    for selector in (
        "creation-prerequisite-snapshot-digest",
        "creation-prerequisite-raw-character-xml-digest",
        "creation-prerequisite-authority-digest",
        "creation-prerequisite-pending-draft-digest",
    ):
        if CANONICAL_AUTHORITY_DIGEST.fullmatch(values[selector]) is None:
            capture(f"{scan_id}-{selector}-not-canonical")
            raise RuntimeError(
                f"Persisted prerequisite authority {selector!r} was not canonical: "
                f"{values[selector]!r}"
            )
    auxiliary = values["creation-prerequisite-auxiliary-state-digest"]
    if CANONICAL_AUXILIARY_STATE_DIGEST.fullmatch(auxiliary) is None:
        capture(f"{scan_id}-auxiliary-state-digest-not-canonical")
        raise RuntimeError(
            "Persisted prerequisite auxiliary-state digest was not canonical: "
            f"{auxiliary!r}"
        )
    binding = require_prerequisite_binding(
        values["creation-prerequisite-binding"]
    )
    require_binding_matches_canonical_digests(
        binding,
        values["creation-prerequisite-snapshot-digest"],
        values["creation-prerequisite-authority-digest"],
    )
    for selector in PERSISTED_PREREQUISITE_PRESENCE_ONLY_SELECTORS:
        values[selector] = "present"
    measured_selection_viewports = {
        category: max(viewports)
        for category, viewports in selection_viewports.items()
    }
    if (
        measured_selection_viewports["heritage"]
        > measured_selection_viewports["talent"]
    ):
        capture(f"{scan_id}-selection-order-invalid")
        raise RuntimeError(
            "Persisted Heritage and Talent selection rows changed canonical order"
        )
    return PersistedPrerequisiteAuthorityScanProof(
        values=values,
        swipes=scan.swipes,
        selection_viewports=measured_selection_viewports,
    )


def acquire_measured_priority_category_row(
    device: shared.Device,
    category: str,
    navigation: dict[str, object],
) -> shared.UiNode:
    """Reacquire one exact category from the digest-bound ordered inventory.

    The first category reverses the authority scan's exact measured 0.22-height
    delta plus one overlap gesture, checking a fresh hierarchy before and after
    every gesture.  Android end-clamping can make the forward and reverse
    gesture distances differ by one viewport; the overlap is fixed, explicit,
    and cannot expand into a blind reset. Later categories are below the
    freshly verified prior row, so they retain their bounded forward-only
    snapshots. A rank selection can change row height; no pre-navigation node
    or blind absolute viewport is reused.
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
    selector = f"creation-prerequisite-category-{category}"

    if last_category is None:
        if category != CATEGORIES[0] or target_viewport > current_viewport:
            raise RuntimeError(
                "Priority category navigation did not start with the first ordered row"
            )
        reverse_bound = (
            measured_reverse_reacquisition_bound(
                current_viewport,
                target_viewport,
                maximum_viewport=22,
            )
            + PRIORITY_CATEGORY_REACQUISITION_OVERLAP_SWIPES
        )
        node, _ = rewind_to_exact_resource_id(
            device,
            selector,
            max_swipes=reverse_bound,
            distance_ratio=0.22,
            evidence_prefix=f"creation-prerequisite-{category}-category-row",
            surface_name=f"Measured {category} priority category row",
            require_tappable=True,
            max_empty_hierarchy_reads=3,
            max_system_ui_dismissals=3,
        )
        navigation["currentViewport"] = target_viewport
        return node
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
    duplicate_singleton_ids: set[str] = set()
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
        screen_singleton_ids: list[str] = []
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


def open_prerequisite(
    device: shared.Device,
    *,
    ready_method_node: shared.UiNode | None = None,
    deadline: float | None = None,
) -> PriorityRankOrigin:
    if ready_method_node is None:
        ready_method_node = device.wait_exact_resource_id_bidirectional(
            "creation-stage-method",
            timeout=180,
            backward_scrolls=DASHBOARD_SCAN_MAX_SCROLLS,
            forward_scrolls=DASHBOARD_SCAN_MAX_SCROLLS,
            scroll_distance_ratio=DASHBOARD_SCAN_GESTURE_RATIO,
            evidence_prefix="creation-stage-method-open",
            surface_name="Creation method navigation",
            require_tappable=True,
            deadline=deadline,
        )
    tappable = (
        device.node_has_tappable_bounds(ready_method_node)
        if deadline is None
        else device.node_has_tappable_bounds(ready_method_node, deadline=deadline)
    )
    if (
        _exact_resource_id(ready_method_node) != "creation-stage-method"
        or not tappable
    ):
        if deadline is None:
            device.capture("creation-stage-method-resume-node-invalid")
        else:
            device.capture(
                "creation-stage-method-resume-node-invalid",
                deadline=deadline,
            )
        raise RuntimeError(
            "Creation prerequisite resume did not retain one exact tappable "
            "creation-stage-method node"
        )
    _require_canonical_chummer_resource_id(
        device,
        ready_method_node,
        "creation-stage-method",
        evidence_prefix="creation-stage-method-resume",
        surface_name="Creation prerequisite resume method",
        deadline=deadline,
    )
    require_creation_method_navigation(ready_method_node, ready=True)
    x, y = ready_method_node.center
    if deadline is None:
        device.shell("input", "tap", str(x), str(y))
    else:
        device.shell(
            "input",
            "tap",
            str(x),
            str(y),
            timeout=shared._remaining_operation_timeout(
                deadline=deadline,
                maximum=15,
            ),
            deadline=deadline,
        )
    # This single origin acquisition proves the pushed route plus the exact
    # method and binding anchors. Its fresh hierarchy is reused by the complete
    # root inventory; there are no blind resets or independent card searches.
    return wait_for_prerequisite_scan_origin(
        device,
        deadline=deadline,
    )


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
    deadline: float | None = None,
    route_node: shared.UiNode | None = None,
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
    if route_node is None:
        route_node = device.wait_for_single_exact_resource_id(
            "creation-prerequisite-talent-grant-page",
            timeout=45,
            evidence_prefix="creation-prerequisite-talent-grant-route",
            surface_name=f"{expected_kind} Talent grant route",
            deadline=deadline,
        )
    _require_canonical_chummer_resource_id(
        device,
        route_node,
        "creation-prerequisite-talent-grant-page",
        evidence_prefix="creation-prerequisite-talent-grant-route",
        surface_name=f"{expected_kind} Talent grant route",
        deadline=deadline,
    )
    scan_token = scan_id or (
        "talent-grant-cardinality-"
        + re.sub(r"[^a-z0-9]+", "-", expected_kind.casefold()).strip("-")
    )
    origin = acquire_stable_start_origin(
        device,
        scan_id=f"{scan_token}-start",
        max_reverse_swipes=max_scrolls,
        distance_ratio=TALENT_GRANT_SCAN_GESTURE_RATIO,
        deadline=deadline,
    )
    scan = scan_forward_with_receipt(
        device,
        scan_id=scan_token,
        max_scrolls=max_scrolls,
        distance_ratio=TALENT_GRANT_SCAN_GESTURE_RATIO,
        initial_observation=origin,
        initial_observation_max_reverse_swipes=max_scrolls,
        observer=scan_observer,
        deadline=deadline,
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
    duplicate_singleton_ids: set[str] = set()
    seen_authority = False
    seen_digest = False
    seen_completion = False
    resource_viewports: dict[str, set[int]] = {}
    option_identity_values: dict[str, set[tuple[str, ...]]] = {}
    option_slot_ordinals: dict[str, set[int]] = {}
    invalid_slot_option_ids: set[str] = set()

    for viewport_index, nodes in enumerate(scan.screens):
        screen_ids: list[str] = []
        screen_singleton_ids: list[str] = []
        for node in nodes:
            resource_id = _exact_resource_id(node)
            values = _accessible_values(node)
            is_grant_authority_child = (
                resource_id.startswith(option_prefix)
                or resource_id.startswith(opposite_prefix)
                or resource_id
                in {
                    "creation-prerequisite-talent-grant-authority",
                    "creation-prerequisite-talent-grant-digest",
                    "creation-prerequisite-talent-grant-complete",
                }
            )
            if is_grant_authority_child:
                _require_canonical_chummer_resource_id(
                    device,
                    node,
                    resource_id,
                    evidence_prefix=f"{scan_token}-{resource_id}",
                    surface_name="Talent grant child authority",
                    deadline=deadline,
                )
            if (
                navigation_out is not None
                and resource_id
                and (
                    device.node_has_tappable_bounds(node)
                    if deadline is None
                    else device.node_has_tappable_bounds(node, deadline=deadline)
                )
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
                screen_singleton_ids.append(resource_id)
                for value in values:
                    authority_counts.update(
                        (
                            int(match.group("selected")),
                            int(match.group("required")),
                            match.group("kind"),
                        )
                        for match in TALENT_GRANT_REQUIRED.finditer(value)
                    )
            if resource_id == "creation-prerequisite-talent-grant-digest":
                seen_digest = True
                screen_singleton_ids.append(resource_id)
                grant_digests.update(
                    value for value in values if CANONICAL_AUTHORITY_DIGEST.fullmatch(value)
                )
            if resource_id == "creation-prerequisite-talent-grant-complete":
                seen_completion = True
                screen_singleton_ids.append(resource_id)
                completion_states.add(node.attributes.get("enabled") == "true")
        duplicate_ids.update(
            resource_id
            for resource_id in set(screen_ids)
            if screen_ids.count(resource_id) > 1
        )
        duplicate_singleton_ids.update(
            resource_id
            for resource_id in set(screen_singleton_ids)
            if screen_singleton_ids.count(resource_id) > 1
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
        or duplicate_singleton_ids
        or contradictions
        or ambiguous_option_details
        or len(authority_counts) != 1
        or len(grant_digests) != 1
        or len(completion_states) != 1
    ):
        if deadline is None:
            device.capture("creation-prerequisite-talent-grant-authority-invalid")
        else:
            device.capture(
                "creation-prerequisite-talent-grant-authority-invalid",
                deadline=deadline,
            )
        raise RuntimeError(
            "Talent grant prompt authority was ambiguous: "
            f"kind={expected_kind!r}, authority={seen_authority}, digest={seen_digest}, "
            f"completion={seen_completion}, counts={sorted(authority_counts)!r}, "
            f"digests={sorted(grant_digests)!r}, malformed={sorted(malformed_option_ids)!r}, "
            f"opposite={sorted(opposite_option_ids)!r}, duplicates={sorted(duplicate_ids)!r}, "
            f"singletonDuplicates={sorted(duplicate_singleton_ids)!r}, "
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
        if deadline is None:
            device.capture("creation-prerequisite-talent-grant-cardinality-invalid")
        else:
            device.capture(
                "creation-prerequisite-talent-grant-cardinality-invalid",
                deadline=deadline,
            )
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


def choose_navigation_local_talent_options(
    available_option_ids: tuple[str, ...],
    required_count: int,
    navigation: dict[str, object],
) -> tuple[tuple[str, ...], tuple[str, ...]]:
    """Choose valid options nearest the measured completion authority.

    The full option catalog remains deterministic selection authority. Only
    enabled options observed with validated tappable bounds are eligible for
    this physical mutation, because a clipped-only row has no measured tap
    authority. The returned selection is canonical-ID sorted for all
    subsequent digest/state comparisons; the tap order is navigation-local.
    """
    if (
        not available_option_ids
        or len(available_option_ids) != len(set(available_option_ids))
        or type(required_count) is not int
        or required_count <= 0
        or len(available_option_ids) < required_count
    ):
        raise ValueError(
            "Talent navigation-local selection requires distinct sufficient options"
        )
    end_viewport = navigation.get("endViewport")
    if type(end_viewport) is not int:
        raise RuntimeError("Talent grant inventory emitted no exact end viewport")
    _validated_talent_navigation_end(navigation, end_viewport)
    viewports = navigation.get("resourceViewports")
    if not isinstance(viewports, dict):
        raise RuntimeError("Talent grant inventory emitted no measured resource viewports")
    measured_option_ids = tuple(
        resource_id
        for resource_id in available_option_ids
        if resource_id in viewports
    )
    if len(measured_option_ids) < required_count:
        raise RuntimeError(
            "Talent grant inventory exposed too few enabled options with measured "
            "tappable viewports: "
            f"required={required_count}, measured={measured_option_ids!r}, "
            f"available={available_option_ids!r}"
        )
    completion_viewport = _measured_resource_viewport(
        navigation,
        "creation-prerequisite-talent-grant-complete",
    )
    option_viewports = {
        resource_id: _measured_resource_viewport(navigation, resource_id)
        for resource_id in measured_option_ids
    }
    selected = tuple(
        sorted(
            sorted(
                measured_option_ids,
                key=lambda resource_id: (
                    abs(option_viewports[resource_id] - completion_viewport),
                    abs(option_viewports[resource_id] - end_viewport),
                    resource_id,
                ),
            )[:required_count]
        )
    )
    tap_order = tuple(
        sorted(
            selected,
            key=lambda resource_id: (
                abs(option_viewports[resource_id] - end_viewport),
                -option_viewports[resource_id],
                resource_id,
            ),
        )
    )
    return selected, tap_order


def reacquire_exact_talent_state_group(
    device: shared.Device,
    resource_ids: tuple[str, ...],
    current_viewport: int,
    target_viewport: int,
    scan_end_viewport: int,
    *,
    evidence_prefix: str,
    max_empty_hierarchy_reads: int = 3,
    max_system_ui_dismissals: int = 3,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    deadline: float | None = None,
) -> TalentStateGroupSnapshot:
    """Reacquire one exact state group under boundary-proven hard ceilings.

    Inventory viewports are immutable ordering coordinates, not reversible
    physical distances after a MAUI row-state refresh.  The measured ordering
    therefore chooses only the primary direction. Exact Talent option groups
    use overlapping gestures in both the primary and boundary-proven recovery
    directions so a virtualized row cannot be skipped by a coarse gesture;
    authority, digest, completion, and zero-delta groups retain the coarse
    primary traversal and never receive recovery. Every gesture is followed
    by a fresh hierarchy. Primary and recovery gestures and transient retries
    have separate hard ceilings while sharing the active phase deadline.
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
    primary_direction = (
        "forward"
        if measured_delta > 0
        else "reverse"
        if measured_delta < 0
        else "none"
    )
    primary_bound = (
        TALENT_GRANT_REACQUISITION_MAX_SCROLLS if measured_delta else 0
    )
    recovery_eligible = bool(measured_delta) and all(
        any(
            _is_exact_tokenized_resource_id(resource_id, prefix)
            for prefix in TALENT_GRANT_OPTION_PREFIX.values()
        )
        for resource_id in resource_ids
    )
    recovery_direction = (
        "reverse"
        if recovery_eligible and primary_direction == "forward"
        else "forward"
        if recovery_eligible and primary_direction == "reverse"
        else "none"
    )
    recovery_bound = (
        TALENT_GRANT_OPTION_RECOVERY_MAX_SCROLLS if recovery_eligible else 0
    )
    primary_distance_ratio = (
        TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO
        if recovery_eligible
        else TALENT_GRANT_SCAN_GESTURE_RATIO
    )
    stage = "primary"
    primary_swipes = 0
    recovery_swipes = 0
    primary_empty_hierarchy_reads = 0
    recovery_empty_hierarchy_reads = 0
    primary_system_ui_dismissals = 0
    recovery_system_ui_dismissals = 0
    primary_screens = 0
    recovery_screens = 0
    hierarchy_durations_ms: list[int] = []
    previous_signature: tuple[tuple[str, ...], ...] | None = None
    unchanged_signatures = 0
    primary_stable_boundary_proven = False
    recovery_stable_boundary_proven = False
    recovery_used = False
    terminal_receipt_emitted = False
    started = time.monotonic()

    def receipt(status: str) -> dict[str, object]:
        total_swipes = primary_swipes + recovery_swipes
        total_screens = primary_screens + recovery_screens
        total_empty_reads = (
            primary_empty_hierarchy_reads + recovery_empty_hierarchy_reads
        )
        total_system_ui_dismissals = (
            primary_system_ui_dismissals + recovery_system_ui_dismissals
        )
        return {
            "scanId": f"{evidence_prefix}-reacquisition",
            "status": status,
            "navigationMode": (
                "measured-direction-stable-boundary-overlap-recovery"
            ),
            "direction": primary_direction,
            "distanceRatio": primary_distance_ratio,
            "startingViewport": current_viewport,
            "targetViewport": target_viewport,
            "normalizedTargetViewport": target_viewport,
            "measuredDelta": abs(measured_delta),
            "configuredMaxScrolls": primary_bound,
            "catalogMovementExtent": scan_end_viewport,
            "stableRepeats": TALENT_GRANT_REACQUISITION_STABLE_REPEATS,
            # This compatibility field remains terminal-boundary authority.
            # Successful recovery has a proven primary boundary but no proven
            # terminal boundary.
            "stableBoundaryProven": (
                recovery_stable_boundary_proven
                if recovery_used
                else primary_stable_boundary_proven and not recovery_eligible
            ),
            "primaryDirection": primary_direction,
            "primaryDistanceRatio": primary_distance_ratio,
            "primaryConfiguredMaxScrolls": primary_bound,
            "primaryStableBoundaryProven": primary_stable_boundary_proven,
            "primaryScreens": primary_screens,
            "primarySwipes": primary_swipes,
            "primaryEmptyHierarchyReads": primary_empty_hierarchy_reads,
            "primarySystemUiDismissals": primary_system_ui_dismissals,
            "recoveryEligible": recovery_eligible,
            "recoveryUsed": recovery_used,
            "recoveryDirection": recovery_direction,
            "recoveryDistanceRatio": TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO,
            "recoveryConfiguredMaxScrolls": recovery_bound,
            "recoveryStableBoundaryProven": recovery_stable_boundary_proven,
            "recoveryScreens": recovery_screens,
            "recoverySwipes": recovery_swipes,
            "recoveryEmptyHierarchyReads": recovery_empty_hierarchy_reads,
            "recoverySystemUiDismissals": recovery_system_ui_dismissals,
            "deadlineEnforced": deadline is not None,
            "exactResourceIds": list(resource_ids),
            "screens": total_screens,
            "swipes": total_swipes,
            "emptyHierarchyReads": total_empty_reads,
            "systemUiDismissals": total_system_ui_dismissals,
            "maximumEmptyHierarchyReads": max_empty_hierarchy_reads,
            "maximumSystemUiDismissals": max_system_ui_dismissals,
            **hierarchy_timing_fields(hierarchy_durations_ms),
            "elapsedMs": round((time.monotonic() - started) * 1000),
        }

    def emit(status: str) -> None:
        nonlocal terminal_receipt_emitted
        if terminal_receipt_emitted:
            return
        terminal_receipt_emitted = True
        if scan_observer is not None:
            scan_observer(receipt(status))

    def unresolved_status(default: str) -> str:
        return (
            "deadline-unresolved"
            if deadline is not None and time.monotonic() >= deadline
            else default
        )

    def capture(name: str) -> None:
        if deadline is None:
            device.capture(name)
        else:
            device.capture(name, deadline=deadline)

    while True:
        try:
            nodes = fresh_hierarchy_timed(
                device,
                hierarchy_durations_ms,
                deadline=deadline,
            )
        except Exception:
            emit(unresolved_status("hierarchy-or-transport-unresolved"))
            raise
        if not nodes:
            if stage == "primary":
                primary_empty_hierarchy_reads += 1
                stage_empty_reads = primary_empty_hierarchy_reads
            else:
                recovery_empty_hierarchy_reads += 1
                stage_empty_reads = recovery_empty_hierarchy_reads
            if stage_empty_reads > max_empty_hierarchy_reads:
                emit("empty-hierarchy-exhausted")
                capture(f"{evidence_prefix}-empty-hierarchy-exhausted")
                raise RuntimeError(
                    f"Grouped Talent state {stage} scan exhausted its separate "
                    f"transient empty-hierarchy budget of "
                    f"{max_empty_hierarchy_reads} reads"
                )
            try:
                sleep_before_phase_deadline(
                    0.75,
                    deadline=deadline,
                    operation="Talent reacquisition empty-hierarchy wait",
                )
            except Exception:
                emit(unresolved_status("empty-hierarchy-wait-unresolved"))
                raise
            continue
        if stage == "primary":
            primary_screens += 1
        else:
            recovery_screens += 1
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
            emit("cardinality-invalid")
            capture(f"{evidence_prefix}-cardinality-invalid")
            if len(duplicates) == 1:
                resource_id, cardinality = next(iter(duplicates.items()))
                detail = f"{resource_id!r} has cardinality {cardinality}"
            else:
                detail = repr(duplicates)
            raise RuntimeError(
                "Grouped Talent state exact resource cardinality was ambiguous: "
                f"{detail}"
            )
        unavailable = tuple(
            resource_id
            for resource_id, candidates in matches.items()
            if not candidates
            or not (
                device.node_has_tappable_bounds(candidates[0])
                if deadline is None
                else device.node_has_tappable_bounds(
                    candidates[0],
                    deadline=deadline,
                )
            )
        )
        if not unavailable:
            emit("resolved")
            return TalentStateGroupSnapshot(
                nodes=nodes,
                resources={
                    resource_id: candidates[0]
                    for resource_id, candidates in matches.items()
                },
                # Normalize the physical observation back to the immutable
                # inventory coordinate.  Gesture count is deliberately not a
                # coordinate after the native scroll surface has refreshed.
                logical_viewport=target_viewport,
                reacquisition_direction=primary_direction,
                reacquisition_swipes=primary_swipes + recovery_swipes,
            )
        try:
            system_ui_dismissed = (
                device.dismiss_system_ui_anr(nodes)
                if deadline is None
                else device.dismiss_system_ui_anr(nodes, deadline=deadline)
            )
        except Exception:
            emit("system-ui-check-failed")
            raise
        if system_ui_dismissed:
            if stage == "primary":
                primary_system_ui_dismissals += 1
                stage_system_ui_dismissals = primary_system_ui_dismissals
            else:
                recovery_system_ui_dismissals += 1
                stage_system_ui_dismissals = recovery_system_ui_dismissals
            if stage_system_ui_dismissals > max_system_ui_dismissals:
                emit("system-ui-exhausted")
                capture(f"{evidence_prefix}-system-ui-exhausted")
                raise RuntimeError(
                    f"Grouped Talent state {stage} scan exhausted its separate "
                    f"system-UI dismissal budget of {max_system_ui_dismissals}"
                )
            try:
                sleep_before_phase_deadline(
                    2,
                    deadline=deadline,
                    operation="Talent reacquisition system-UI wait",
                )
            except Exception:
                emit(unresolved_status("system-ui-wait-unresolved"))
                raise
            continue
        signature = accessibility_signature(nodes)
        unchanged_signatures = (
            unchanged_signatures + 1
            if previous_signature is not None and signature == previous_signature
            else 0
        )
        previous_signature = signature
        if unchanged_signatures >= TALENT_GRANT_REACQUISITION_STABLE_REPEATS:
            if stage == "primary":
                primary_stable_boundary_proven = True
                if recovery_eligible:
                    recovery_used = True
                    stage = "recovery"
                    previous_signature = None
                    unchanged_signatures = 0
                else:
                    emit("stable-boundary-unresolved")
                    capture(f"{evidence_prefix}-stable-boundary-unresolved")
                    raise RuntimeError(
                        "Grouped Talent state reached a stable physical boundary "
                        "without an authorized option recovery and without "
                        f"reacquiring exact resources {unavailable!r}"
                    )
            else:
                recovery_stable_boundary_proven = True
                emit("recovery-stable-boundary-unresolved")
                capture(
                    f"{evidence_prefix}-recovery-stable-boundary-unresolved"
                )
                raise RuntimeError(
                    "Grouped Talent option recovery reached the opposite stable "
                    f"physical boundary without exact resources {unavailable!r}"
                )
        if stage == "primary" and primary_direction == "none":
            emit("zero-delta-unresolved")
            capture(f"{evidence_prefix}-zero-delta-unresolved")
            raise RuntimeError(
                "Grouped Talent state changed physical geometry at one logical "
                f"viewport without a safe directional hint: {unavailable!r}"
            )
        active_direction = (
            primary_direction if stage == "primary" else recovery_direction
        )
        active_distance_ratio = (
            primary_distance_ratio
            if stage == "primary"
            else TALENT_GRANT_OPTION_RECOVERY_GESTURE_RATIO
        )
        stage_swipes = primary_swipes if stage == "primary" else recovery_swipes
        stage_bound = primary_bound if stage == "primary" else recovery_bound
        if stage_swipes >= stage_bound:
            break
        try:
            if active_direction == "reverse":
                if deadline is None:
                    device.swipe_down(distance_ratio=active_distance_ratio)
                else:
                    device.swipe_down(
                        distance_ratio=active_distance_ratio,
                        deadline=deadline,
                    )
            elif active_direction == "forward":
                if deadline is None:
                    device.swipe_up(distance_ratio=active_distance_ratio)
                else:
                    device.swipe_up(
                        distance_ratio=active_distance_ratio,
                        deadline=deadline,
                    )
        except Exception:
            emit(unresolved_status("gesture-or-transport-unresolved"))
            raise
        if stage == "primary":
            primary_swipes += 1
        else:
            recovery_swipes += 1
        try:
            sleep_before_phase_deadline(
                0.2,
                deadline=deadline,
                operation="Talent reacquisition post-swipe wait",
            )
        except Exception:
            emit(unresolved_status("post-swipe-wait-unresolved"))
            raise

    hard_bound_stage = stage
    emit(f"{hard_bound_stage}-hard-bound-unresolved")
    capture(f"{evidence_prefix}-unavailable")
    raise RuntimeError(
        "Grouped Talent state could not reacquire exact resources "
        f"{unavailable!r} within the boundary-checked {stage_bound}-swipe "
        f"{active_direction} {hard_bound_stage} hard bound"
    )


def tap_exact_measured_talent_resource(
    device: shared.Device,
    resource_id: str,
    navigation: dict[str, object],
    current_viewport: int,
    *,
    evidence_prefix: str,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    deadline: float | None = None,
) -> int:
    """Move to a measured viewport, reacquire one exact node, then tap it."""
    def capture(name: str) -> None:
        if deadline is None:
            device.capture(name)
        else:
            device.capture(name, deadline=deadline)

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
        scan_observer=scan_observer,
        deadline=deadline,
    )
    node = snapshot.resources[resource_id]
    if any(
        _is_exact_tokenized_resource_id(resource_id, prefix)
        for prefix in TALENT_GRANT_OPTION_PREFIX.values()
    ):
        if not _talent_option_has_exact_dynamic_slot(node):
            capture(f"{evidence_prefix}-slot-state-invalid")
            raise RuntimeError(
                f"Measured Talent resource {resource_id!r} exposed an invalid "
                "exact slot decorator"
            )
        expected_detail = _measured_talent_resource_detail(navigation, resource_id)
        if _talent_option_identity_values(node) != expected_detail:
            capture(f"{evidence_prefix}-detail-drift")
            raise RuntimeError(
                f"Measured Talent resource {resource_id!r} changed exact option detail"
            )
    node_is_tappable = (
        device.node_has_tappable_bounds(node)
        if deadline is None
        else device.node_has_tappable_bounds(node, deadline=deadline)
    )
    if (
        node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not node_is_tappable
    ):
        capture(f"{evidence_prefix}-not-tappable")
        raise RuntimeError(f"Measured Talent resource {resource_id!r} was not tappable")
    if deadline is None:
        device.shell("input", "tap", *(str(value) for value in node.center))
    else:
        device.shell(
            "input",
            "tap",
            *(str(value) for value in node.center),
            deadline=deadline,
        )
    return snapshot.logical_viewport


def _nearest_talent_group_viewport(
    current_viewport: int,
    remaining_viewports: set[int],
) -> int:
    if type(current_viewport) is not int or current_viewport < 0:
        raise ValueError("Current Talent viewport must be a nonnegative exact integer")
    if (
        not remaining_viewports
        or any(type(viewport) is not int or viewport < 0 for viewport in remaining_viewports)
    ):
        raise ValueError("Remaining Talent viewports must be nonempty nonnegative integers")
    return min(
        remaining_viewports,
        key=lambda candidate: (abs(candidate - current_viewport), candidate),
    )


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
    preferred_final_resource_id: str | None = None,
    evidence_prefix: str,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    deadline: float | None = None,
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
    if (
        preferred_final_resource_id is not None
        and preferred_final_resource_id not in required_ids
    ):
        raise RuntimeError(
            "Grouped Talent preferred final resource is not part of the exact "
            "required state"
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
    remaining_viewports = set(grouped)
    preferred_final_viewport = (
        _measured_resource_viewport(navigation, preferred_final_resource_id)
        if preferred_final_resource_id is not None
        else None
    )
    while remaining_viewports:
        # Greedy nearest-current traversal is deterministic (lower viewport is
        # the tie-break).  When the immediately following mutation targets one
        # of these exact resources, reserve its viewport for the final group so
        # the mutation can use a fresh zero-delta hierarchy instead of crossing
        # the same option and later performing a boundary recovery.
        eligible_viewports = remaining_viewports
        if (
            preferred_final_viewport in remaining_viewports
            and len(remaining_viewports) > 1
        ):
            eligible_viewports = remaining_viewports - {preferred_final_viewport}
        viewport = _nearest_talent_group_viewport(
            current_viewport,
            eligible_viewports,
        )
        remaining_viewports.remove(viewport)
        snapshot = reacquire_exact_talent_state_group(
            device,
            tuple(grouped[viewport]),
            current_viewport,
            viewport,
            scan_end_viewport,
            evidence_prefix=f"{evidence_prefix}-viewport-{viewport}",
            scan_observer=scan_observer,
            deadline=deadline,
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


def wait_for_exact_talent_option_transition(
    device: shared.Device,
    resource_id: str,
    expected_detail: tuple[str, ...],
    expected_slot: int | None,
    *,
    evidence_prefix: str,
    deadline: float | None = None,
    timeout_seconds: float = 45,
) -> shared.UiNode:
    """Synchronize one mutation on its changed option and exact route.

    A route marker alone can still belong to the pre-tap frame.  This waits for
    the canonical route and mutated option in one fresh hierarchy, proving the
    immutable detail and exact selected-slot decorator before grouped scans.
    """
    if (
        not resource_id
        or not expected_detail
        or any(not value for value in expected_detail)
        or (
            expected_slot is not None
            and (type(expected_slot) is not int or expected_slot <= 0)
        )
        or isinstance(timeout_seconds, bool)
        or not isinstance(timeout_seconds, (int, float))
        or not math.isfinite(timeout_seconds)
        or timeout_seconds <= 0
    ):
        raise ValueError("Exact Talent transition authority is required")
    operation_deadline = min(
        deadline if deadline is not None else math.inf,
        time.monotonic() + timeout_seconds,
    )
    hierarchy_durations_ms: list[int] = []
    route_id = "creation-prerequisite-talent-grant-page"
    while True:
        nodes = fresh_hierarchy_timed(
            device,
            hierarchy_durations_ms,
            deadline=operation_deadline,
        )
        route_matches = [
            node for node in nodes if _exact_resource_id(node) == route_id
        ]
        option_matches = [
            node for node in nodes if _exact_resource_id(node) == resource_id
        ]
        if len(route_matches) > 1 or len(option_matches) > 1:
            device.capture(
                f"{evidence_prefix}-cardinality-invalid",
                deadline=operation_deadline,
            )
            raise RuntimeError(
                "Talent transition exposed ambiguous route or option cardinality"
            )
        if len(route_matches) == 1 and len(option_matches) == 1:
            route = route_matches[0]
            option = option_matches[0]
            _require_canonical_chummer_resource_id(
                device,
                route,
                route_id,
                evidence_prefix=f"{evidence_prefix}-route",
                surface_name="Mutated Talent grant route",
                deadline=operation_deadline,
            )
            _require_canonical_chummer_resource_id(
                device,
                option,
                resource_id,
                evidence_prefix=f"{evidence_prefix}-option",
                surface_name="Mutated Talent grant option",
                deadline=operation_deadline,
            )
            if _talent_option_identity_values(option) != expected_detail:
                device.capture(
                    f"{evidence_prefix}-detail-drift",
                    deadline=operation_deadline,
                )
                raise RuntimeError(
                    f"Talent transition changed exact option detail for {resource_id!r}"
                )
            if (
                _talent_option_matches_exact_slot(option, expected_slot)
                and option.attributes.get("enabled") == "true"
                and option.attributes.get("clickable") == "true"
                and device.node_has_tappable_bounds(
                    option,
                    deadline=operation_deadline,
                )
            ):
                return option
        sleep_before_phase_deadline(
            0.2,
            deadline=operation_deadline,
            operation="Talent option transition synchronization",
        )


def choose_and_prove_talent_grant(
    device: shared.Device,
    expected_kind: str,
    talent_option_id: str,
    talent_option_navigation: dict[str, object],
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id_prefix: str,
    phase_deadline_provider: Callable[[], float] | None = None,
    continuation_phase_advances: tuple[
        Callable[[], None],
        Callable[[], None],
        Callable[[], None],
    ]
    | None = None,
) -> TalentGrantChoiceProof:
    if continuation_phase_advances is not None and (
        len(continuation_phase_advances) != 3
        or any(not callable(advance) for advance in continuation_phase_advances)
    ):
        raise ValueError("Talent grant continuation phase advances must be callable")
    navigation: dict[str, object] = {}

    def active_deadline() -> float | None:
        return phase_deadline_provider() if phase_deadline_provider is not None else None

    initial = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=f"{scan_id_prefix}-initial",
        navigation_out=navigation,
        deadline=active_deadline(),
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
    chosen, tap_order = choose_navigation_local_talent_options(
        available,
        initial.required_count,
        navigation,
    )
    selected_slots = {
        resource_id: ordinal
        for ordinal, resource_id in enumerate(tap_order, start=1)
    }
    current_viewport = int(navigation["endViewport"])
    for resource_id in tap_order:
        current_viewport = tap_exact_measured_talent_resource(
            device,
            resource_id,
            navigation,
            current_viewport,
            evidence_prefix=f"{scan_id_prefix}-choose-{resource_id}",
            scan_observer=scan_observer,
            deadline=active_deadline(),
        )
        wait_for_exact_talent_option_transition(
            device,
            resource_id,
            _measured_talent_resource_detail(navigation, resource_id),
            selected_slots[resource_id],
            evidence_prefix=f"{scan_id_prefix}-choice-refresh",
            deadline=active_deadline(),
        )
    complete_state, current_viewport = read_talent_grant_grouped_state(
        device,
        expected_kind,
        initial,
        navigation,
        current_viewport,
        expected_selected_option_ids=tap_order,
        expected_completion_enabled=True,
        evidence_prefix=f"{scan_id_prefix}-complete",
        scan_observer=scan_observer,
        deadline=active_deadline(),
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
    if continuation_phase_advances is not None:
        continuation_phase_advances[0]()

    # A native Back followed by the exact same Talent option must preserve the
    # in-memory typed choices.  Toggling one selected row off and on again then
    # proves explicit reset/reselection without any implicit default.
    reset_id = chosen[0]
    preservation_deadline = active_deadline()
    device.back(deadline=preservation_deadline)
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix=f"{scan_id_prefix}-back-to-talent",
        surface_name="Talent detail route after grant Back",
        deadline=preservation_deadline,
    )
    talent_viewport = int(talent_option_navigation["endViewport"])
    tap_exact_measured_talent_resource(
        device,
        talent_option_id,
        talent_option_navigation,
        talent_viewport,
        evidence_prefix=f"{scan_id_prefix}-reenter-talent-option",
        scan_observer=scan_observer,
        deadline=active_deadline(),
    )
    preserved_route = device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix=f"{scan_id_prefix}-preserved-route",
        surface_name=f"Preserved {expected_kind} Talent grant route",
        deadline=preservation_deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        preserved_route,
        "creation-prerequisite-talent-grant-page",
        evidence_prefix=f"{scan_id_prefix}-preserved-route",
        surface_name=f"Preserved {expected_kind} Talent grant route",
        deadline=preservation_deadline,
    )
    # A newly pushed grant page owns a fresh ScrollView at its native start.
    # The exact top route therefore rebinds physical state to catalog viewport
    # zero before measured grouped reacquisition; the old page's terminal
    # logical viewport must never be reused across this navigation boundary.
    current_viewport = 0
    preserved_state, current_viewport = read_talent_grant_grouped_state(
        device,
        expected_kind,
        initial,
        navigation,
        current_viewport,
        expected_selected_option_ids=tap_order,
        expected_completion_enabled=True,
        preferred_final_resource_id=reset_id,
        evidence_prefix=f"{scan_id_prefix}-preserved",
        scan_observer=scan_observer,
        deadline=active_deadline(),
    )
    if preserved_state != complete_state:
        device.capture(f"{scan_id_prefix}-back-preservation-mismatch")
        raise RuntimeError(
            f"{expected_kind} native Back/re-enter did not preserve the exact draft: "
            f"complete={complete_state!r}, preserved={preserved_state!r}"
        )
    if continuation_phase_advances is not None:
        continuation_phase_advances[1]()

    current_viewport = tap_exact_measured_talent_resource(
        device,
        reset_id,
        navigation,
        current_viewport,
        evidence_prefix=f"{scan_id_prefix}-explicit-reset-tap",
        scan_observer=scan_observer,
        deadline=active_deadline(),
    )
    wait_for_exact_talent_option_transition(
        device,
        reset_id,
        _measured_talent_resource_detail(navigation, reset_id),
        None,
        evidence_prefix=f"{scan_id_prefix}-explicit-reset-route",
        deadline=active_deadline(),
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
        preferred_final_resource_id=reset_id,
        evidence_prefix=f"{scan_id_prefix}-explicit-reset",
        scan_observer=scan_observer,
        deadline=active_deadline(),
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
    if continuation_phase_advances is not None:
        continuation_phase_advances[2]()
    current_viewport = tap_exact_measured_talent_resource(
        device,
        reset_id,
        navigation,
        current_viewport,
        evidence_prefix=f"{scan_id_prefix}-explicit-reselect-tap",
        scan_observer=scan_observer,
        deadline=active_deadline(),
    )
    wait_for_exact_talent_option_transition(
        device,
        reset_id,
        _measured_talent_resource_detail(navigation, reset_id),
        initial.required_count,
        evidence_prefix=f"{scan_id_prefix}-explicit-reselect-route",
        deadline=active_deadline(),
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
        scan_observer=scan_observer,
        deadline=active_deadline(),
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
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    deadline: float | None = None,
) -> None:
    tap_exact_measured_talent_resource(
        device,
        "creation-prerequisite-talent-grant-complete",
        navigation,
        current_viewport,
        evidence_prefix="creation-prerequisite-talent-grant-complete",
        scan_observer=scan_observer,
        deadline=deadline,
    )
    if deadline is None:
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
        return

    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-talent-after-grant",
        surface_name="Talent detail route after exact grant completion",
        deadline=deadline,
    )
    device.back(deadline=deadline)
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-after-talent-grant",
        surface_name="Creation prerequisite route after Talent grant completion",
        deadline=deadline,
    )


def collect_exact_contiguous_authority_values(
    device: shared.Device,
    screens: list[list[shared.UiNode]],
    selectors: tuple[str, ...],
    *,
    evidence_prefix: str,
    require_nonblank: frozenset[str],
    deadline: float,
) -> tuple[dict[str, str], dict[str, tuple[int, ...]], dict[str, tuple[str, str]]]:
    """Collect one stable value/state per exact ID across overlapping screens."""
    values: dict[str, set[str]] = {selector: set() for selector in selectors}
    states: dict[str, set[tuple[str, str]]] = {
        selector: set() for selector in selectors
    }
    viewports: dict[str, list[int]] = {selector: [] for selector in selectors}
    for viewport_index, nodes in enumerate(screens):
        for selector in selectors:
            matches = [node for node in nodes if _exact_resource_id(node) == selector]
            if len(matches) > 1:
                _capture_with_phase_deadline(
                    device,
                    f"{evidence_prefix}-cardinality-invalid",
                    deadline=deadline,
                )
                raise RuntimeError(
                    f"{evidence_prefix} {selector!r} has cardinality {len(matches)} "
                    f"in viewport {viewport_index}; expected one"
                )
            if not matches:
                continue
            node = matches[0]
            _require_canonical_chummer_resource_id(
                device,
                node,
                selector,
                evidence_prefix=evidence_prefix,
                surface_name="Composite exact authority scan",
                deadline=deadline,
            )
            value = (
                node.attributes.get("text")
                or node.attributes.get("content-desc")
                or ""
            )
            if selector in require_nonblank and not value.strip():
                _capture_with_phase_deadline(
                    device,
                    f"{evidence_prefix}-value-blank",
                    deadline=deadline,
                )
                raise RuntimeError(
                    f"{evidence_prefix} {selector!r} exposed blank authority"
                )
            values[selector].add(value)
            states[selector].add(
                (
                    node.attributes.get("enabled", ""),
                    node.attributes.get("clickable", ""),
                )
            )
            viewports[selector].append(viewport_index)

    missing = sorted(selector for selector, entries in viewports.items() if not entries)
    drift = sorted(
        selector
        for selector in selectors
        if len(values[selector]) != 1 or len(states[selector]) != 1
    )
    reappeared = sorted(
        selector
        for selector, entries in viewports.items()
        if any(current != previous + 1 for previous, current in zip(entries, entries[1:]))
    )
    if missing or drift or reappeared:
        _capture_with_phase_deadline(
            device,
            f"{evidence_prefix}-authority-invalid",
            deadline=deadline,
        )
        raise RuntimeError(
            f"{evidence_prefix} exact authority was incomplete or unstable: "
            f"missing={missing!r}, drift={drift!r}, reappeared={reappeared!r}"
        )
    return (
        {selector: next(iter(values[selector])) for selector in selectors},
        {selector: tuple(viewports[selector]) for selector in selectors},
        {selector: next(iter(states[selector])) for selector in selectors},
    )


def require_exact_preview_talent_grant_plan(
    device: shared.Device,
    expected_kind: str,
    selected_option_ids: tuple[str, ...],
    *,
    max_scrolls: int = 40,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str,
    deadline: float | None = None,
    proof_out: dict[str, object] | None = None,
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
    # The digest row is only 100 px high.  A coarse generic wait can move it
    # from below to above the viewport between hierarchy observations.  Prove
    # a stable start and use the same fine-grained, overlapping full-page scan
    # for both the digest and every projected grant row.  This is one read-only
    # authority traversal: it neither retries nor replays a product action.
    origin = acquire_stable_start_origin(
        device,
        scan_id=f"{scan_id}-origin",
        max_reverse_swipes=8 if proof_out is not None else max_scrolls,
        distance_ratio=0.60 if proof_out is not None else 0.22,
        stable_repeats=2,
        max_consecutive_empty_reads=3,
        delay_seconds=0.0,
        deadline=deadline,
    )
    screens = scan_forward_until_stable(
        device,
        scan_id=scan_id,
        max_scrolls=max_scrolls,
        distance_ratio=0.30 if proof_out is not None else 0.22,
        initial_observation=origin,
        initial_observation_max_reverse_swipes=(
            8 if proof_out is not None else max_scrolls
        ),
        observer=scan_observer,
        deadline=deadline,
    )
    observed_ids: set[str] = set()
    opposite_ids: set[str] = set()
    malformed_ids: set[str] = set()
    duplicate_ids: set[str] = set()
    plan_digest_values: set[str] = set()
    malformed_plan_digests: set[str] = set()
    duplicate_plan_digest_viewports: list[int] = []
    plan_digest_viewports: list[int] = []
    grant_viewports: dict[str, list[int]] = {}
    for viewport_index, nodes in enumerate(screens):
        plan_digest_nodes = [
            node
            for node in nodes
            if _exact_resource_id(node) == TALENT_GRANT_PREVIEW_PLAN_DIGEST_ID
        ]
        if plan_digest_nodes:
            plan_digest_viewports.append(viewport_index)
        if len(plan_digest_nodes) > 1:
            duplicate_plan_digest_viewports.append(viewport_index)
        for node in plan_digest_nodes:
            value = (
                node.attributes.get("text")
                or node.attributes.get("content-desc")
                or ""
            ).strip()
            if CANONICAL_AUTHORITY_DIGEST.fullmatch(value) is None:
                malformed_plan_digests.add(value)
            else:
                plan_digest_values.add(value)
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
        for resource_id in set(screen_ids):
            if _is_exact_tokenized_resource_id(resource_id, preview_prefix):
                grant_viewports.setdefault(resource_id, []).append(viewport_index)
        duplicate_ids.update(
            resource_id
            for resource_id in set(screen_ids)
            if screen_ids.count(resource_id) > 1
        )
    separated_plan_digest = any(
        current != previous + 1
        for previous, current in zip(
            plan_digest_viewports,
            plan_digest_viewports[1:],
        )
    )
    separated_grant_ids = sorted(
        resource_id
        for resource_id, viewports in grant_viewports.items()
        if any(
            current != previous + 1
            for previous, current in zip(viewports, viewports[1:])
        )
    )
    if (
        len(plan_digest_values) != 1
        or malformed_plan_digests
        or duplicate_plan_digest_viewports
        or separated_plan_digest
        or separated_grant_ids
        or malformed_ids
        or opposite_ids
        or duplicate_ids
        or observed_ids != expected_ids
    ):
        if deadline is None:
            device.capture(f"{scan_id}-mismatch")
        else:
            _capture_with_phase_deadline(
                device,
                f"{scan_id}-mismatch",
                deadline=deadline,
            )
        raise RuntimeError(
            f"{expected_kind} preview grant plan was not exact: expected={sorted(expected_ids)!r}, "
            f"observed={sorted(observed_ids)!r}, opposite={sorted(opposite_ids)!r}, "
            f"malformed={sorted(malformed_ids)!r}, duplicates={sorted(duplicate_ids)!r}, "
            f"planDigestValues={sorted(plan_digest_values)!r}, "
            f"malformedPlanDigests={sorted(malformed_plan_digests)!r}, "
            f"duplicatePlanDigestViewports={duplicate_plan_digest_viewports!r}, "
            f"planDigestViewports={plan_digest_viewports!r}, "
            f"separatedGrantIds={separated_grant_ids!r}, "
            f"grantViewports={grant_viewports!r}"
        )
    plan_digest = next(iter(plan_digest_values))
    if proof_out is not None:
        if deadline is None:
            raise ValueError("Rich Preview authority requires one absolute phase deadline")
        assignment_selectors = tuple(
            f"creation-prerequisite-preview-assignment-{category}"
            for category in CATEGORIES
        )
        rich_selectors = (
            "creation-prerequisite-preview-page",
            "creation-prerequisite-preview-binding",
            "creation-prerequisite-preview-digest",
            "creation-prerequisite-preview-raw-character-xml-digest",
            "creation-prerequisite-preview-auxiliary-state-digest",
            "creation-prerequisite-preview-authority-digest",
            *assignment_selectors,
            "creation-prerequisite-preview-heritage",
            "creation-prerequisite-preview-talent",
            "creation-prerequisite-preview-karma-budget",
            "creation-prerequisite-preview-attributes-ready",
            "creation-prerequisite-confirm",
        )
        rich_values, rich_viewports, rich_states = (
            collect_exact_contiguous_authority_values(
                device,
                screens,
                rich_selectors,
                evidence_prefix=f"{scan_id}-rich-preview",
                require_nonblank=frozenset(
                    {
                        "creation-prerequisite-preview-digest",
                        "creation-prerequisite-preview-binding",
                        "creation-prerequisite-preview-raw-character-xml-digest",
                        "creation-prerequisite-preview-auxiliary-state-digest",
                        "creation-prerequisite-preview-authority-digest",
                        "creation-prerequisite-preview-karma-budget",
                        "creation-prerequisite-confirm",
                    }
                ),
                deadline=deadline,
            )
        )
        canonical_selectors = (
            "creation-prerequisite-preview-digest",
            "creation-prerequisite-preview-raw-character-xml-digest",
            "creation-prerequisite-preview-authority-digest",
        )
        malformed = [
            selector
            for selector in canonical_selectors
            if CANONICAL_AUTHORITY_DIGEST.fullmatch(
                rich_values[selector].strip()
            )
            is None
        ]
        auxiliary_selector = (
            "creation-prerequisite-preview-auxiliary-state-digest"
        )
        if CANONICAL_AUXILIARY_STATE_DIGEST.fullmatch(
            rich_values[auxiliary_selector].strip()
        ) is None:
            malformed.append(auxiliary_selector)
        stale_receipt_ids = sorted(
            {
                _exact_resource_id(node)
                for nodes in screens
                for node in nodes
                if _exact_resource_id(node) == "creation-prerequisite-confirmed"
                or _exact_resource_id(node).startswith("creation-prerequisite-receipt-")
                or _exact_resource_id(node)
                == "creation-prerequisite-confirm-receipt"
            }
        )
        absent_preview_ids = (
            "creation-prerequisite-preview-sum-to-ten",
            "creation-prerequisite-preview-blockers",
            "creation-prerequisite-preview-attributes-disabled",
        )
        unexpected_conditional_ids = sorted(
            {
                _exact_resource_id(node)
                for nodes in screens
                for node in nodes
                if _exact_resource_id(node) in absent_preview_ids
            }
        )
        confirm_selector = "creation-prerequisite-confirm"
        confirm_state = rich_states[confirm_selector]
        confirm_viewports = rich_viewports[confirm_selector]
        observed_assignment_order: list[str] = []
        assignment_prefix = "creation-prerequisite-preview-assignment-"
        for nodes in screens:
            for node in nodes:
                resource_id = _exact_resource_id(node)
                if (
                    resource_id.startswith(assignment_prefix)
                    and resource_id not in observed_assignment_order
                ):
                    observed_assignment_order.append(resource_id)
        terminal_confirm_nodes = [
            node
            for node in screens[-1]
            if _exact_resource_id(node) == confirm_selector
        ]
        terminal_confirm = (
            terminal_confirm_nodes[0]
            if len(terminal_confirm_nodes) == 1
            else None
        )
        terminal_confirm_tappable = (
            terminal_confirm is not None
            and device.node_has_tappable_bounds(
                terminal_confirm,
                deadline=deadline,
            )
        )
        if (
            malformed
            or stale_receipt_ids
            or unexpected_conditional_ids
            or tuple(observed_assignment_order) != assignment_selectors
            or confirm_state != ("true", "true")
            or confirm_viewports[-1] != len(screens) - 1
            or not terminal_confirm_tappable
        ):
            _capture_with_phase_deadline(
                device,
                f"{scan_id}-rich-preview-invalid",
                deadline=deadline,
            )
            raise RuntimeError(
                "Rich Preview authority was malformed, stale, or did not retain the "
                "exact enabled Confirm action at the measured terminal viewport: "
                f"malformed={malformed!r}, stale={stale_receipt_ids!r}, "
                f"unexpectedConditional={unexpected_conditional_ids!r}, "
                f"assignmentOrder={observed_assignment_order!r}, "
                f"confirmState={confirm_state!r}, confirmViewports={confirm_viewports!r}, "
                f"terminalConfirmTappable={terminal_confirm_tappable!r}"
            )
        proof_out.clear()
        immutable_selectors = (
            *rich_selectors[:-1],
            TALENT_GRANT_PREVIEW_PLAN_DIGEST_ID,
            *tuple(sorted(expected_ids)),
        )
        proof_out.update(
            {
                "previewDigest": rich_values[
                    "creation-prerequisite-preview-digest"
                ].strip(),
                "bindingDigests": {
                    "rawCharacterXml": rich_values[
                        "creation-prerequisite-preview-raw-character-xml-digest"
                    ].strip(),
                    "auxiliaryState": rich_values[auxiliary_selector].strip(),
                    "authority": rich_values[
                        "creation-prerequisite-preview-authority-digest"
                    ].strip(),
                },
                "confirmAuthority": rich_values[confirm_selector],
                "confirmNode": terminal_confirm,
                "confirmViewport": confirm_viewports[-1],
                "terminalViewport": len(screens) - 1,
                "assignmentIds": assignment_selectors,
                "grantIds": tuple(sorted(expected_ids)),
                "absentPreviewIds": absent_preview_ids,
                "planDigest": plan_digest,
                "immutableAuthorities": {
                    selector: (
                        plan_digest
                        if selector == TALENT_GRANT_PREVIEW_PLAN_DIGEST_ID
                        else rich_values.get(selector, next(
                            (
                                node.attributes.get("text")
                                or node.attributes.get("content-desc")
                                or ""
                                for nodes in screens
                                for node in nodes
                                if _exact_resource_id(node) == selector
                            ),
                            "",
                        ))
                    )
                    for selector in immutable_selectors
                },
                "immutableStates": {
                    selector: rich_states.get(
                        selector,
                        next(
                            (
                                (
                                    node.attributes.get("enabled", ""),
                                    node.attributes.get("clickable", ""),
                                )
                                for nodes in screens
                                for node in nodes
                                if _exact_resource_id(node) == selector
                            ),
                            ("", ""),
                        ),
                    )
                    for selector in immutable_selectors
                },
            }
        )
    return plan_digest


def tap_exact_current_preview_confirm(
    device: shared.Device,
    proof: dict[str, object],
    *,
    deadline: float,
) -> float:
    """Tap the scan-retained terminal Confirm once and return its proof deadline."""
    selector = "creation-prerequisite-confirm"
    node = proof.get("confirmNode")
    if (
        proof.get("confirmViewport") != proof.get("terminalViewport")
        or not isinstance(proof.get("confirmAuthority"), str)
        or not str(proof["confirmAuthority"]).strip()
        or not isinstance(node, shared.UiNode)
    ):
        raise RuntimeError("Preview scan emitted no exact terminal Confirm authority")
    retained = node
    node = device.wait_for_single_exact_resource_id(
        selector,
        timeout=45,
        scroll=False,
        max_scrolls=0,
        evidence_prefix="creation-prerequisite-confirm-current",
        surface_name="Measured current Preview Confirm action",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        node,
        selector,
        evidence_prefix="creation-prerequisite-confirm-current",
        surface_name="Measured current Preview Confirm action",
        deadline=deadline,
    )
    value = node.attributes.get("text") or node.attributes.get("content-desc") or ""
    retained_value = (
        retained.attributes.get("text")
        or retained.attributes.get("content-desc")
        or ""
    )
    if (
        value != proof["confirmAuthority"]
        or retained_value != value
        or node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node, deadline=deadline)
    ):
        _capture_with_phase_deadline(
            device,
            "creation-prerequisite-confirm-current-invalid",
            deadline=deadline,
        )
        raise RuntimeError(
            "Scan-retained Preview Confirm authority drifted or was not tappable"
        )
    action_deadline = persistent_action_deadline(
        deadline,
        action_timeout_seconds=PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS,
        proof_timeout_seconds=CONFIRM_DOWNSTREAM_RESERVE_SECONDS,
        operation="the exact Preview Confirm action",
    )
    x, y = node.center
    device.shell(
        "input",
        "tap",
        str(x),
        str(y),
        timeout=shared._remaining_operation_timeout(
            deadline=action_deadline,
            maximum=PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS,
        ),
        deadline=action_deadline,
    )
    return immediate_proof_deadline(
        deadline,
        CONFIRMED_RECEIPT_PROOF_TIMEOUT_SECONDS,
        operation="the composite confirmed-receipt proof",
    )


def read_exact_confirmed_receipt(
    device: shared.Device,
    *,
    preview_proof: dict[str, object],
    scan_observer: Callable[[dict[str, object]], None] | None,
    deadline: float,
) -> dict[str, object]:
    """Read one compact bottom-oriented receipt scan without leaving its route."""
    scan_id = "creation-prerequisite-confirmed-receipt"
    receipt_selectors = (
        "creation-prerequisite-preview-page",
        "creation-prerequisite-confirmed",
        "creation-prerequisite-confirm-receipt",
        "creation-prerequisite-receipt-content-revision",
        "creation-prerequisite-receipt-saved-revision",
        "creation-prerequisite-receipt-draft-revision",
        "creation-prerequisite-receipt-draft-digest",
        "creation-prerequisite-receipt-raw-character-xml-digest",
        "creation-prerequisite-receipt-auxiliary-state-digest",
        "creation-prerequisite-receipt-authority-digest",
        "creation-prerequisite-back-to-build",
    )
    immutable_authorities = preview_proof.get("immutableAuthorities")
    immutable_states = preview_proof.get("immutableStates")
    assignment_ids = preview_proof.get("assignmentIds")
    grant_ids = preview_proof.get("grantIds")
    absent_preview_ids = preview_proof.get("absentPreviewIds")
    if (
        not isinstance(immutable_authorities, dict)
        or not immutable_authorities
        or not isinstance(immutable_states, dict)
        or set(immutable_states) != set(immutable_authorities)
        or not isinstance(assignment_ids, tuple)
        or not isinstance(grant_ids, tuple)
        or absent_preview_ids != (
            "creation-prerequisite-preview-sum-to-ten",
            "creation-prerequisite-preview-blockers",
            "creation-prerequisite-preview-attributes-disabled",
        )
    ):
        raise RuntimeError("Preview proof emitted no reusable immutable authority")
    immutable_selectors = tuple(str(value) for value in immutable_authorities)
    selectors = tuple(dict.fromkeys((*receipt_selectors, *immutable_selectors)))
    expected = frozenset(selectors)
    confirmed_transition = device.wait_for_single_exact_resource_id(
        "creation-prerequisite-confirmed",
        timeout=CONFIRMED_STATE_TRANSITION_TIMEOUT_SECONDS,
        scroll=False,
        max_scrolls=0,
        evidence_prefix=f"{scan_id}-transition",
        surface_name="Confirmed prerequisite state transition",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        confirmed_transition,
        "creation-prerequisite-confirmed",
        evidence_prefix=f"{scan_id}-transition",
        surface_name="Confirmed prerequisite state transition",
        deadline=deadline,
    )
    back_origin = device.wait_exact_resource_id_bidirectional(
        "creation-prerequisite-back-to-build",
        timeout=CONFIRMED_RECEIPT_BACK_ORIGIN_TIMEOUT_SECONDS,
        backward_scrolls=0,
        forward_scrolls=4,
        scroll_distance_ratio=0.30,
        evidence_prefix=f"{scan_id}-back-origin",
        surface_name="Confirmed receipt Back read origin",
        require_tappable=False,
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        back_origin,
        "creation-prerequisite-back-to-build",
        evidence_prefix=f"{scan_id}-back-origin",
        surface_name="Confirmed receipt Back read origin",
        deadline=deadline,
    )
    screens: list[list[shared.UiNode]] = []
    observed: set[str] = set()
    reverse_swipes = 0
    empty_reads = 0
    started = time.monotonic()
    while reverse_swipes <= 12:
        require_phase_deadline(deadline, operation="confirmed-receipt hierarchy")
        nodes = device.hierarchy(deadline=deadline)
        if not nodes:
            empty_reads += 1
            if empty_reads > 3:
                break
            sleep_before_phase_deadline(
                0.2,
                deadline=deadline,
                operation="confirmed-receipt empty hierarchy",
            )
            continue
        empty_reads = 0
        screens.append(nodes)
        observed.update(
            resource_id
            for node in nodes
            if (resource_id := _exact_resource_id(node)) in expected
        )
        if observed == expected:
            break
        if reverse_swipes >= 12:
            break
        device.swipe_down(distance_ratio=0.30, deadline=deadline)
        reverse_swipes += 1
        sleep_before_phase_deadline(
            0.2,
            deadline=deadline,
            operation="confirmed-receipt reverse observation",
        )
    if observed != expected:
        _capture_with_phase_deadline(
            device,
            f"{scan_id}-required-authority-missing",
            deadline=deadline,
        )
        raise RuntimeError(
            "Confirmed receipt compact scan did not expose every required exact "
            f"authority within 12 reverse swipes: missing={sorted(expected - observed)!r}"
        )
    if scan_observer is not None:
        scan_observer(
            {
                "scanId": scan_id,
                "status": "required-authority-complete",
                "screens": len(screens),
                "swipes": reverse_swipes,
                "configuredMaxScrolls": 12,
                "distanceRatio": 0.30,
                "direction": "reverse-from-current-confirmed-bottom",
                "deadlineEnforced": True,
                "elapsedMs": round((time.monotonic() - started) * 1000),
            }
        )
    value_selectors = frozenset(receipt_selectors) - {
        "creation-prerequisite-preview-page",
        "creation-prerequisite-confirmed",
    }
    values, viewports, states = collect_exact_contiguous_authority_values(
        device,
        screens,
        selectors,
        evidence_prefix=scan_id,
        require_nonblank=value_selectors,
        deadline=deadline,
    )
    stale_confirm = any(
        _exact_resource_id(node) == "creation-prerequisite-confirm"
        for nodes in screens
        for node in nodes
    )
    expected_preview_ids = frozenset(immutable_selectors)
    unknown_preview_ids = sorted(
        {
            resource_id
            for nodes in screens
            for node in nodes
            if (
                resource_id := _exact_resource_id(node)
            ).startswith("creation-prerequisite-preview-")
            and resource_id not in expected_preview_ids
            and resource_id not in absent_preview_ids
        }
    )
    unexpected_conditional_ids = sorted(
        {
            _exact_resource_id(node)
            for nodes in screens
            for node in nodes
            if _exact_resource_id(node) in absent_preview_ids
        }
    )
    retained_assignment_order: list[str] = []
    for nodes in reversed(screens):
        for node in nodes:
            resource_id = _exact_resource_id(node)
            if (
                resource_id in assignment_ids
                and resource_id not in retained_assignment_order
            ):
                retained_assignment_order.append(resource_id)
    immutable_drift = sorted(
        selector
        for selector in immutable_selectors
        if values[selector] != immutable_authorities[selector]
        or states[selector] != tuple(immutable_states[selector])
    )
    receipt_text = values["creation-prerequisite-confirm-receipt"]
    revisions: dict[str, int] = {}
    malformed: list[str] = []
    for name, selector in (
        ("contentRevision", "creation-prerequisite-receipt-content-revision"),
        ("savedRevision", "creation-prerequisite-receipt-saved-revision"),
        ("draftRevision", "creation-prerequisite-receipt-draft-revision"),
    ):
        value = values[selector].strip()
        if re.fullmatch(r"[0-9]+", value) is None:
            malformed.append(selector)
        else:
            revisions[name] = int(value)
    digest_selectors = {
        "draft": "creation-prerequisite-receipt-draft-digest",
        "rawCharacterXml": (
            "creation-prerequisite-receipt-raw-character-xml-digest"
        ),
        "auxiliaryState": (
            "creation-prerequisite-receipt-auxiliary-state-digest"
        ),
        "authority": "creation-prerequisite-receipt-authority-digest",
    }
    digests = {
        name: values[selector].strip()
        for name, selector in digest_selectors.items()
    }
    for name in ("draft", "rawCharacterXml", "authority"):
        if CANONICAL_AUTHORITY_DIGEST.fullmatch(digests[name]) is None:
            malformed.append(digest_selectors[name])
    if CANONICAL_AUXILIARY_STATE_DIGEST.fullmatch(
        digests["auxiliaryState"]
    ) is None:
        malformed.append(digest_selectors["auxiliaryState"])
    back_selector = "creation-prerequisite-back-to-build"
    if (
        stale_confirm
        or unknown_preview_ids
        or unexpected_conditional_ids
        or immutable_drift
        or tuple(retained_assignment_order) != assignment_ids
        or malformed
        or re.search(r"(?:^|[. ·])false(?:$|[. ·])", receipt_text.casefold())
        is None
        or states[back_selector] != ("true", "true")
        or viewports[back_selector][0] != 0
    ):
        _capture_with_phase_deadline(
            device,
            f"{scan_id}-invalid",
            deadline=deadline,
        )
        raise RuntimeError(
            "Confirmed receipt was stale, malformed, or lacked one terminal Back action: "
            f"staleConfirm={stale_confirm!r}, unknownPreviewIds={unknown_preview_ids!r}, "
            f"unexpectedConditional={unexpected_conditional_ids!r}, "
            f"immutableDrift={immutable_drift!r}, "
            f"assignmentOrder={retained_assignment_order!r}, malformed={malformed!r}, "
            f"backState={states[back_selector]!r}, "
            f"backViewports={viewports[back_selector]!r}"
        )

    # Restore the measured current/bottom viewport without another authority
    # traversal. The exact Back action is freshly reacquired after this bounded
    # read-only restoration and is never replayed.
    for _ in range(reverse_swipes):
        device.swipe_up(distance_ratio=0.30, deadline=deadline)
        sleep_before_phase_deadline(
            0.2,
            deadline=deadline,
            operation="confirmed-receipt measured bottom restoration",
        )

    return {
        "receiptText": receipt_text,
        "revisions": revisions,
        "draftDigest": digests["draft"],
        "bindingDigests": {
            "rawCharacterXml": digests["rawCharacterXml"],
            "auxiliaryState": digests["auxiliaryState"],
            "authority": digests["authority"],
        },
        "backAuthority": values[back_selector],
        "backViewport": viewports[back_selector][0],
        "currentViewport": 0,
        # Inverse Android scroll gestures are not mathematically reversible at
        # a clamped page boundary.  Bind any fresh Back reacquisition to no
        # more forward movement than the exact reverse traversal which proved
        # this receipt; the data-changing Back action itself remains one-shot.
        "backRecoveryMaxForwardScrolls": reverse_swipes,
    }


def reacquire_exact_confirmed_receipt_back(
    device: shared.Device,
    *,
    max_forward_scrolls: int,
    expected_authority: str,
    scan_observer: Callable[[dict[str, object]], None] | None,
    deadline: float,
) -> tuple[shared.UiNode, dict[str, object]]:
    """Freshly recover Back within only the receipt scan's measured extent."""
    if (
        type(max_forward_scrolls) is not int
        or max_forward_scrolls < 0
        or max_forward_scrolls > 12
    ):
        raise ValueError(
            "Confirmed-receipt Back recovery requires its exact 0..12 measured bound"
        )
    selector = "creation-prerequisite-back-to-build"
    started = time.monotonic()
    maximum_elapsed_ms = max(
        0,
        min(
            round(CONFIRMED_RECEIPT_BACK_REACQUISITION_TIMEOUT_SECONDS * 1000),
            math.floor((deadline - started) * 1000),
        ),
    )
    forward_swipes = 0
    screens = 0
    empty_hierarchies = 0
    system_ui_dismissals = 0
    hierarchy_durations_ms: list[int] = []
    terminal_receipt_emitted = False

    def emit(status: str, *, failure_reason: str | None = None) -> dict[str, object]:
        nonlocal terminal_receipt_emitted
        scan: dict[str, object] = {
            "scanId": "creation-prerequisite-confirmed-receipt-back-reacquisition",
            "status": status,
            "screens": screens,
            "swipes": forward_swipes,
            "configuredMaxScrolls": max_forward_scrolls,
            "emptyHierarchyReads": empty_hierarchies,
            "maximumEmptyHierarchyReads": (
                CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_EMPTY_HIERARCHIES
            ),
            "systemUiDismissals": system_ui_dismissals,
            "maximumSystemUiDismissals": (
                CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_SYSTEM_UI_DISMISSALS
            ),
            "distanceRatio": 0.30,
            "direction": "forward-from-measured-restored-bottom",
            "deadlineEnforced": True,
            "maximumElapsedMs": maximum_elapsed_ms,
            "downstreamReserveMs": round(
                CONFIRMED_RECEIPT_BACK_DOWNSTREAM_RESERVE_SECONDS * 1000
            ),
            **hierarchy_timing_fields(hierarchy_durations_ms),
            "elapsedMs": round((time.monotonic() - started) * 1000),
        }
        if failure_reason is not None:
            scan["failureReason"] = failure_reason
        if terminal_receipt_emitted:
            return scan
        # Set the cardinality guard before the external observer can raise.
        # A partially written evidence sink must never trigger a contradictory
        # second terminal receipt from the enclosing fail-closed exception path.
        terminal_receipt_emitted = True
        if scan_observer is not None:
            scan_observer(scan)
        return scan

    try:
        require_phase_deadline(
            deadline,
            operation="confirmed-receipt Back reacquisition lease",
        )
        while forward_swipes <= max_forward_scrolls:
            require_phase_deadline(
                deadline,
                operation="confirmed-receipt Back reacquisition",
            )
            hierarchy_started = time.perf_counter()
            try:
                nodes = device.hierarchy(deadline=deadline)
            finally:
                hierarchy_durations_ms.append(
                    round((time.perf_counter() - hierarchy_started) * 1000)
                )
            if not nodes:
                empty_hierarchies += 1
                if (
                    empty_hierarchies
                    > CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_EMPTY_HIERARCHIES
                ):
                    break
                sleep_before_phase_deadline(
                    CONFIRMED_RECEIPT_BACK_RECOVERY_DELAY_SECONDS,
                    deadline=deadline,
                    operation="confirmed-receipt Back empty hierarchy",
                )
                continue
            screens += 1
            matches = [
                node for node in nodes if _exact_resource_id(node) == selector
            ]
            if len(matches) > 1:
                _capture_with_phase_deadline(
                    device,
                    "creation-prerequisite-confirmed-receipt-back-current-cardinality-invalid",
                    deadline=deadline,
                )
                raise RuntimeError(
                    "Fresh confirmed-receipt Back reacquisition exposed duplicate exact nodes"
                )
            if len(matches) == 1:
                node = matches[0]
                _require_canonical_chummer_resource_id(
                    device,
                    node,
                    selector,
                    evidence_prefix="creation-prerequisite-confirmed-receipt-back-current",
                    surface_name="Measured current confirmed-receipt Back action",
                    deadline=deadline,
                )
                value = (
                    node.attributes.get("text")
                    or node.attributes.get("content-desc")
                    or ""
                )
                if value != expected_authority:
                    raise RuntimeError(
                        "Fresh confirmed-receipt Back authority drifted"
                    )
                if (
                    node.attributes.get("enabled") != "true"
                    or node.attributes.get("clickable") != "true"
                ):
                    _capture_with_phase_deadline(
                        device,
                        "creation-prerequisite-confirmed-receipt-back-current-disabled",
                        deadline=deadline,
                    )
                    raise RuntimeError(
                        "Fresh confirmed-receipt Back authority was not enabled and clickable"
                    )
                if device.node_has_tappable_bounds(
                    node,
                    deadline=deadline,
                ):
                    return node, emit("resolved")
            if device.dismiss_system_ui_anr(
                nodes,
                deadline=deadline,
            ):
                system_ui_dismissals += 1
                if (
                    system_ui_dismissals
                    > CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_SYSTEM_UI_DISMISSALS
                ):
                    break
                sleep_before_phase_deadline(
                    2.0,
                    deadline=deadline,
                    operation="confirmed-receipt Back system-UI dismissal",
                )
                continue
            if forward_swipes >= max_forward_scrolls:
                break
            device.swipe_up(
                distance_ratio=0.30,
                deadline=deadline,
            )
            forward_swipes += 1
            sleep_before_phase_deadline(
                CONFIRMED_RECEIPT_BACK_RECOVERY_DELAY_SECONDS,
                deadline=deadline,
                operation="confirmed-receipt Back forward observation",
            )

        _capture_with_phase_deadline(
            device,
            "creation-prerequisite-confirmed-receipt-back-current-unavailable",
            deadline=deadline,
        )
        raise RuntimeError(
            "Fresh confirmed-receipt Back authority was unavailable within its "
            f"scan-proven {max_forward_scrolls}-swipe recovery bound"
        )
    except Exception as exc:
        emit("failed", failure_reason=type(exc).__name__)
        raise


def tap_exact_confirmed_receipt_back(
    device: shared.Device,
    receipt: dict[str, object],
    *,
    scan_observer: Callable[[dict[str, object]], None] | None,
    deadline: float,
) -> float:
    """Freshly reacquire one measured Back action and lease dashboard proof time."""
    selector = "creation-prerequisite-back-to-build"
    if (
        receipt.get("backViewport") != receipt.get("currentViewport")
        or not isinstance(receipt.get("backAuthority"), str)
        or not str(receipt["backAuthority"]).strip()
        or type(receipt.get("backRecoveryMaxForwardScrolls")) is not int
    ):
        raise RuntimeError("Receipt scan emitted no exact terminal Back authority")
    lease_started = time.monotonic()
    remaining = deadline - lease_started
    if remaining <= CONFIRMED_RECEIPT_BACK_DOWNSTREAM_RESERVE_SECONDS:
        if scan_observer is not None:
            scan_observer({
                "scanId": "creation-prerequisite-confirmed-receipt-back-reacquisition",
                "status": "failed",
                "failureReason": "InsufficientDownstreamReserve",
                "screens": 0,
                "swipes": 0,
                "configuredMaxScrolls": int(
                    receipt["backRecoveryMaxForwardScrolls"]
                ),
                "emptyHierarchyReads": 0,
                "maximumEmptyHierarchyReads": (
                    CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_EMPTY_HIERARCHIES
                ),
                "systemUiDismissals": 0,
                "maximumSystemUiDismissals": (
                    CONFIRMED_RECEIPT_BACK_RECOVERY_MAX_SYSTEM_UI_DISMISSALS
                ),
                "distanceRatio": 0.30,
                "direction": "forward-from-measured-restored-bottom",
                "deadlineEnforced": True,
                "maximumElapsedMs": 0,
                "downstreamReserveMs": round(
                    CONFIRMED_RECEIPT_BACK_DOWNSTREAM_RESERVE_SECONDS * 1000
                ),
                "hierarchyReadCount": 0,
                "hierarchyElapsedMs": 0,
                "maximumHierarchyReadMs": 0,
                "elapsedMs": 0,
            })
        raise RuntimeError(
            "Preview-confirm phase cannot preserve the exact confirmed-receipt "
            "Back action-plus-dashboard reserve"
        )
    reacquisition_deadline = min(
        deadline - CONFIRMED_RECEIPT_BACK_DOWNSTREAM_RESERVE_SECONDS,
        lease_started + CONFIRMED_RECEIPT_BACK_REACQUISITION_TIMEOUT_SECONDS,
    )
    node, reacquisition = reacquire_exact_confirmed_receipt_back(
        device,
        max_forward_scrolls=int(receipt["backRecoveryMaxForwardScrolls"]),
        expected_authority=str(receipt["backAuthority"]),
        scan_observer=scan_observer,
        deadline=reacquisition_deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        node,
        selector,
        evidence_prefix="creation-prerequisite-confirmed-receipt-back-current",
        surface_name="Measured current confirmed-receipt Back action",
        deadline=deadline,
    )
    value = node.attributes.get("text") or node.attributes.get("content-desc") or ""
    if (
        value != receipt["backAuthority"]
        or node.attributes.get("enabled") != "true"
        or node.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(node, deadline=deadline)
    ):
        _capture_with_phase_deadline(
            device,
            "creation-prerequisite-confirmed-receipt-back-current-invalid",
            deadline=deadline,
        )
        raise RuntimeError("Fresh confirmed-receipt Back authority drifted or was not tappable")
    clear_deadline = persistent_action_deadline(
        deadline,
        action_timeout_seconds=PRE_BACK_ROUTE_LOG_CLEAR_TIMEOUT_SECONDS,
        proof_timeout_seconds=(
            PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS
            + POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS
        ),
        operation="the pre-Back Creation dashboard route-log freshness barrier",
    )
    device.run(
        *shared.ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS,
        timeout=shared._remaining_operation_timeout(
            deadline=clear_deadline,
            maximum=PRE_BACK_ROUTE_LOG_CLEAR_TIMEOUT_SECONDS,
        ),
        deadline=clear_deadline,
    )
    action_deadline = persistent_action_deadline(
        deadline,
        action_timeout_seconds=PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS,
        proof_timeout_seconds=POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS,
        operation="the exact confirmed-receipt Back action",
    )
    x, y = node.center
    device.shell(
        "input",
        "tap",
        str(x),
        str(y),
        timeout=shared._remaining_operation_timeout(
            deadline=action_deadline,
            maximum=PERSISTENT_PREVIEW_ACTION_TIMEOUT_SECONDS,
        ),
        deadline=action_deadline,
    )
    return immediate_proof_deadline(
        deadline,
        POST_CONFIRM_DASHBOARD_PROOF_TIMEOUT_SECONDS,
        operation="the post-confirm dashboard proof",
    )


def open_talent_selection_after_preview(device: shared.Device) -> None:
    """Open the exact Talent route from a preview-preserved bottom viewport.

    A generic forward-only ``tap(..., scroll=True)`` cannot reach the Talent
    row after Back restores the prerequisite page at its bottom. Use the
    shared exact-cardinality bidirectional acquisition and perform one tap
    only. If acquisition or the resulting route proof fails, propagate the
    failure without retrying the product-state transition.
    """
    device.tap_exact_resource_id_bidirectional(
        "creation-prerequisite-talent-selection",
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-talent-selection-after-preview",
        surface_name="Talent selection row after active-skill preview",
    )
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix="creation-prerequisite-talent-route-after-preview",
        surface_name="Talent route after active-skill preview",
    )


def require_restored_talent_grant(
    device: shared.Device,
    talent_option_id: str,
    talent_option_node: shared.UiNode,
    expected_kind: str,
    expected_grant_digest: str,
    expected_selected_option_ids: tuple[str, ...],
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str,
    deadline: float,
) -> TalentGrantSurface:
    _require_canonical_chummer_resource_id(
        device,
        talent_option_node,
        talent_option_id,
        evidence_prefix=f"{scan_id}-talent-option",
        surface_name="Restored selected Talent authority option",
        deadline=deadline,
    )
    current_ids = exact_current_authority_option_ids(
        [talent_option_node],
        "creation-prerequisite-talent-option-",
        lambda node: device.node_has_tappable_bounds(node, deadline=deadline),
    )
    if current_ids != [talent_option_id]:
        device.capture(f"{scan_id}-talent-option-invalid", deadline=deadline)
        raise RuntimeError(
            "Restored selected Talent authority option drifted before grant entry: "
            f"expected={talent_option_id!r}, actual={current_ids!r}"
        )
    device.shell(
        "input",
        "tap",
        *(str(value) for value in talent_option_node.center),
        timeout=shared._remaining_operation_timeout(
            deadline=deadline,
            maximum=15,
        ),
        deadline=deadline,
    )
    grant_route = device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-grant-page",
        timeout=45,
        evidence_prefix=f"{scan_id}-talent-grant-route",
        surface_name="Restored Talent grant route",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        grant_route,
        "creation-prerequisite-talent-grant-page",
        evidence_prefix=f"{scan_id}-talent-grant-route",
        surface_name="Restored Talent grant route",
        deadline=deadline,
    )
    surface = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=scan_id,
        deadline=deadline,
        route_node=grant_route,
    )
    if (
        surface.grant_digest != expected_grant_digest
        or surface.selected_option_ids != expected_selected_option_ids
        or not surface.completion_enabled
    ):
        device.capture(f"{scan_id}-restored-grant-mismatch", deadline=deadline)
        raise RuntimeError(
            "Persisted Talent grant was not restored exactly: "
            f"expectedDigest={expected_grant_digest!r}, "
            f"actualDigest={surface.grant_digest!r}, "
            f"expectedIds={expected_selected_option_ids!r}, "
            f"actualIds={surface.selected_option_ids!r}"
        )
    device.back(deadline=deadline)
    talent_route = device.wait_for_single_exact_resource_id(
        "creation-prerequisite-talent-page",
        timeout=45,
        evidence_prefix=f"{scan_id}-back-to-talent",
        surface_name="Talent detail after restored grant proof",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        talent_route,
        "creation-prerequisite-talent-page",
        evidence_prefix=f"{scan_id}-back-to-talent",
        surface_name="Talent detail after restored grant proof",
        deadline=deadline,
    )
    device.back(deadline=deadline)
    prerequisite_route = device.wait_for_single_exact_resource_id(
        "creation-prerequisite-page",
        timeout=45,
        evidence_prefix=f"{scan_id}-back-to-prerequisite",
        surface_name="Prerequisite route after restored grant proof",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        prerequisite_route,
        "creation-prerequisite-page",
        evidence_prefix=f"{scan_id}-back-to-prerequisite",
        surface_name="Prerequisite route after restored grant proof",
        deadline=deadline,
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


class RestoredAuthorityOptionProof(NamedTuple):
    selected_node: shared.UiNode | None
    root_viewport: int


def require_exact_restored_authority_option(
    device: shared.Device,
    category: str,
    expected_resource_id: str,
    expected_selection_id: str,
    *,
    root_navigation: dict[str, object],
    observed_selection_id: str,
    previous_root_category: str | None,
    retain_selected_node: bool,
    deadline: float,
    max_scrolls: int = 40,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str | None = None,
) -> RestoredAuthorityOptionProof:
    selection_viewports = root_navigation.get("selectionViewports")
    current_viewport = root_navigation.get("currentViewport")
    if (
        category not in ("heritage", "talent")
        or not isinstance(selection_viewports, dict)
        or set(selection_viewports) != {"heritage", "talent"}
        or type(current_viewport) is not int
        or current_viewport < 0
        or any(
            type(selection_viewports.get(value)) is not int
            or int(selection_viewports[value]) < 0
            for value in ("heritage", "talent")
        )
        or previous_root_category not in (None, "heritage")
        or category == "heritage" and previous_root_category is not None
        or category == "talent" and previous_root_category != "heritage"
    ):
        raise RuntimeError("Restored authority option has no exact measured root navigation")
    if observed_selection_id != expected_selection_id:
        device.capture(
            f"creation-prerequisite-{category}-selection-id-mismatch",
            deadline=deadline,
        )
        raise RuntimeError(
            f"Restored {category} SelectionId changed: "
            f"expected={expected_selection_id!r}, actual={observed_selection_id!r}"
        )

    target_viewport = int(selection_viewports[category])
    if category == "heritage":
        reverse_bound = measured_reverse_reacquisition_bound(
            current_viewport,
            target_viewport,
            maximum_viewport=22,
        ) + PRIORITY_CATEGORY_REACQUISITION_OVERLAP_SWIPES
        selection_row, _ = rewind_to_exact_resource_id(
            device,
            f"creation-prerequisite-{category}-selection",
            max_swipes=reverse_bound,
            distance_ratio=0.22,
            evidence_prefix=f"restored-{category}-selection",
            surface_name=f"Restored {category} selection",
            require_tappable=True,
            deadline=deadline,
        )
    else:
        previous_viewport = int(selection_viewports["heritage"])
        forward_bound = max(
            0,
            target_viewport - previous_viewport
            + PRIORITY_CATEGORY_REACQUISITION_OVERLAP_SWIPES,
        )
        selection_row = device.wait_exact_resource_id_bidirectional(
            f"creation-prerequisite-{category}-selection",
            timeout=90,
            backward_scrolls=0,
            forward_scrolls=forward_bound,
            scroll_distance_ratio=0.22,
            evidence_prefix=f"restored-{category}-selection",
            surface_name=f"Restored {category} selection",
            deadline=deadline,
        )
    _require_canonical_chummer_resource_id(
        device,
        selection_row,
        f"creation-prerequisite-{category}-selection",
        evidence_prefix=f"restored-{category}-selection",
        surface_name=f"Restored {category} selection",
        deadline=deadline,
    )
    x, y = selection_row.center
    device.shell(
        "input",
        "tap",
        str(x),
        str(y),
        timeout=shared._remaining_operation_timeout(
            deadline=deadline,
            maximum=15,
        ),
        deadline=deadline,
    )
    route_node = device.wait_for_single_exact_resource_id(
        f"creation-prerequisite-{category}-page",
        timeout=45,
        evidence_prefix=f"restored-{category}-route",
        surface_name=f"Restored {category} detail route",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        route_node,
        f"creation-prerequisite-{category}-page",
        evidence_prefix=f"restored-{category}-route",
        surface_name=f"Restored {category} detail route",
        deadline=deadline,
    )
    prefix = f"creation-prerequisite-{category}-option-"
    candidate_ids: set[str] = set()
    candidate_viewports: dict[str, set[int]] = {}
    duplicate_resource_id = False
    exact_scan_id = scan_id or f"restored-authority-option-{category}"
    origin = acquire_stable_start_origin(
        device,
        scan_id=f"{exact_scan_id}-start",
        max_reverse_swipes=max_scrolls,
        distance_ratio=0.68,
        deadline=deadline,
    )
    scan = scan_forward_with_receipt(
        device,
        scan_id=exact_scan_id,
        max_scrolls=max_scrolls,
        distance_ratio=0.22,
        initial_observation=origin,
        initial_observation_max_reverse_swipes=max_scrolls,
        delay_seconds=0.0,
        observer=scan_observer,
        deadline=deadline,
    )
    for viewport_index, nodes in enumerate(scan.screens):
        for node in nodes:
            resource_id = _exact_resource_id(node)
            if resource_id.startswith(prefix):
                _require_canonical_chummer_resource_id(
                    device,
                    node,
                    resource_id,
                    evidence_prefix=f"{exact_scan_id}-{resource_id}",
                    surface_name=f"Restored {category} authority option",
                    deadline=deadline,
                )
        screen_ids = exact_current_authority_option_ids(
            nodes,
            prefix,
            lambda node: device.node_has_tappable_bounds(node, deadline=deadline),
        )
        if len(screen_ids) != len(set(screen_ids)):
            duplicate_resource_id = True
        candidate_ids.update(screen_ids)
        for resource_id in screen_ids:
            candidate_viewports.setdefault(resource_id, set()).add(
                min(viewport_index, scan.swipes)
            )
    try:
        assert_exact_restored_authority_option_ids(
            candidate_ids,
            expected_resource_id,
            duplicate_resource_id=duplicate_resource_id,
        )
    except RuntimeError:
        device.capture(
            f"creation-prerequisite-{category}-restored-option-mismatch",
            deadline=deadline,
        )
        raise
    selected_node: shared.UiNode | None = None
    if retain_selected_node:
        selected_viewports = candidate_viewports.get(expected_resource_id, set())
        if not selected_viewports:
            raise RuntimeError(
                f"Restored {category} authority option emitted no measured viewport"
            )
        reverse_bound = measured_reverse_reacquisition_bound(
            scan.swipes,
            max(selected_viewports),
            maximum_viewport=max_scrolls,
        ) + PRIORITY_CATEGORY_REACQUISITION_OVERLAP_SWIPES
        selected_node, _ = rewind_to_exact_resource_id(
            device,
            expected_resource_id,
            max_swipes=reverse_bound,
            distance_ratio=0.22,
            evidence_prefix=f"restored-{category}-selected-option",
            surface_name=f"Restored {category} selected authority option",
            require_tappable=True,
            deadline=deadline,
        )
    else:
        device.back(deadline=deadline)
        root_node = device.wait_for_single_exact_resource_id(
            "creation-prerequisite-page",
            timeout=45,
            evidence_prefix=f"restored-{category}-back-to-prerequisite",
            surface_name=f"Prerequisite route after restored {category} proof",
            deadline=deadline,
        )
        _require_canonical_chummer_resource_id(
            device,
            root_node,
            "creation-prerequisite-page",
            evidence_prefix=f"restored-{category}-back-to-prerequisite",
            surface_name=f"Prerequisite route after restored {category} proof",
            deadline=deadline,
        )
    return RestoredAuthorityOptionProof(
        selected_node=selected_node,
        root_viewport=target_viewport,
    )


class ResourcesSurfaceScanProof(NamedTuple):
    nodes: dict[str, shared.UiNode]
    swipes: int
    selector_viewports: dict[str, int]
    terminal_tappable_nodes: dict[str, shared.UiNode]


def scan_deadline_bound_resources_surface(
    device: shared.Device,
    required_selectors: tuple[str, ...],
    *,
    scan_id: str,
    deadline: float,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    required_terminal_selectors: tuple[str, ...] = (),
    tappable_selectors: tuple[str, ...] = (),
    return_scan_proof: bool = False,
) -> dict[str, shared.UiNode] | ResourcesSurfaceScanProof:
    """Read one Resources surface once and reject identity/cardinality drift."""
    if (
        not required_selectors
        or len(required_selectors) != len(set(required_selectors))
        or len(required_terminal_selectors) != len(set(required_terminal_selectors))
        or not set(required_terminal_selectors).issubset(required_selectors)
        or len(tappable_selectors) != len(set(tappable_selectors))
        or not set(tappable_selectors).issubset(required_selectors)
        or type(return_scan_proof) is not bool
    ):
        raise ValueError("Resources surface scan requires distinct exact selectors")
    max_consecutive_empty_reads = (
        PROCESS_RESTART_RESOURCES_MAX_CONSECUTIVE_EMPTY_READS
        if scan_id == PROCESS_RESTART_RESOURCES_SCAN_ID
        else RESOURCES_SURFACE_MAX_CONSECUTIVE_EMPTY_READS
    )
    origin = acquire_stable_start_origin(
        device,
        scan_id=f"{scan_id}-start",
        max_reverse_swipes=22,
        distance_ratio=0.68,
        deadline=deadline,
    )
    scan = scan_forward_with_receipt(
        device,
        scan_id=scan_id,
        max_scrolls=22,
        distance_ratio=0.22,
        initial_observation=origin,
        initial_observation_max_reverse_swipes=22,
        delay_seconds=0.0,
        max_consecutive_empty_reads=max_consecutive_empty_reads,
        observer=scan_observer,
        deadline=deadline,
    )
    required = set(required_selectors)
    representatives: dict[str, shared.UiNode] = {}
    signatures: dict[str, set[tuple[str, ...]]] = {
        selector: set() for selector in required_selectors
    }
    selector_viewports: dict[str, set[int]] = {
        selector: set() for selector in required_selectors
    }
    duplicate_ids: set[str] = set()
    terminal_tappable_nodes: dict[str, shared.UiNode] = {}
    for viewport_index, nodes in enumerate(scan.screens):
        measured_viewport = min(viewport_index, scan.swipes)
        is_freshest_terminal_viewport = viewport_index == len(scan.screens) - 1
        screen_ids: list[str] = []
        for node in nodes:
            resource_id = _exact_resource_id(node)
            if resource_id not in required:
                continue
            _require_canonical_chummer_resource_id(
                device,
                node,
                resource_id,
                evidence_prefix=f"{scan_id}-{resource_id}",
                surface_name="Creation Resources authority",
                deadline=deadline,
            )
            screen_ids.append(resource_id)
            interactive = (
                resource_id not in tappable_selectors
                or (
                    node.attributes.get("enabled") == "true"
                    and node.attributes.get("clickable") == "true"
                    and device.node_has_tappable_bounds(node, deadline=deadline)
                )
            )
            if not interactive:
                continue
            # Keep the freshest occurrence from the latest observed viewport;
            # action rows near the stable end may then be tapped without a
            # second whole-surface search.
            representatives[resource_id] = node
            selector_viewports[resource_id].add(measured_viewport)
            signatures[resource_id].add(
                (
                    node.attributes.get("text", ""),
                    node.attributes.get("content-desc", ""),
                    node.attributes.get("enabled", ""),
                    node.attributes.get("clickable", ""),
                    node.attributes.get("class", ""),
                )
            )
            if is_freshest_terminal_viewport and resource_id in tappable_selectors:
                # The stable-end scan returned this exact node from its final,
                # unchanged hierarchy.  Preserve that provenance separately:
                # callers may reuse it without another hierarchy read only
                # after the scan's global identity/cardinality checks pass.
                terminal_tappable_nodes[resource_id] = node
        duplicate_ids.update(
            resource_id
            for resource_id in set(screen_ids)
            if screen_ids.count(resource_id) > 1
        )
    missing = sorted(required - set(representatives))
    terminal_ids = {
        _exact_resource_id(node)
        for node in (scan.screens[-1] if scan.screens else [])
        if _exact_resource_id(node) in required
    }
    missing_terminal = sorted(set(required_terminal_selectors) - terminal_ids)
    drifted = sorted(
        selector for selector, values in signatures.items() if len(values) != 1
    )
    if missing or missing_terminal or duplicate_ids or drifted:
        device.capture(f"{scan_id}-authority-invalid", deadline=deadline)
        raise RuntimeError(
            "Creation Resources surface authority was incomplete or ambiguous: "
            f"missing={missing!r}, terminalMissing={missing_terminal!r}, "
            f"duplicates={sorted(duplicate_ids)!r}, "
            f"drifted={drifted!r}"
        )
    if return_scan_proof:
        return ResourcesSurfaceScanProof(
            nodes=representatives,
            swipes=scan.swipes,
            selector_viewports={
                selector: max(viewports)
                for selector, viewports in selector_viewports.items()
                if viewports
            },
            terminal_tappable_nodes=terminal_tappable_nodes,
        )
    return representatives


def required_resources_text(
    nodes: dict[str, shared.UiNode],
    selector: str,
) -> str:
    node = nodes.get(selector)
    value = (
        ""
        if node is None
        else node.attributes.get("text")
        or node.attributes.get("content-desc")
        or ""
    ).strip()
    if not value:
        raise RuntimeError(f"{selector} did not expose one exact Resources value")
    return value


def required_resources_integer(
    nodes: dict[str, shared.UiNode],
    selector: str,
) -> int:
    value = required_resources_text(nodes, selector)
    if re.fullmatch(r"[0-9]+", value) is None:
        raise RuntimeError(f"{selector} did not expose one nonnegative integer: {value!r}")
    return int(value)


def required_resources_digest(
    nodes: dict[str, shared.UiNode],
    selector: str,
) -> str:
    value = required_resources_text(nodes, selector)
    if CANONICAL_AUTHORITY_DIGEST.fullmatch(value) is None:
        raise RuntimeError(f"{selector} did not expose one canonical digest: {value!r}")
    return value


def required_resources_auxiliary_digest(
    nodes: dict[str, shared.UiNode],
    selector: str,
) -> str:
    value = required_resources_text(nodes, selector)
    if CANONICAL_AUXILIARY_STATE_DIGEST.fullmatch(value) is None:
        raise RuntimeError(
            f"{selector} did not expose one canonical auxiliary-state digest: {value!r}"
        )
    return value


def open_resources(
    device: shared.Device,
    *,
    deadline: float | None = None,
    observed_dashboard: shared.UiNode | None = None,
    authority_scan_owns_origin: bool = False,
) -> None:
    """Open Resources without duplicating an already-proven viewport boundary.

    The deadline-bound Creation journey can pass the exact dashboard node that
    ``open_creation_dashboard`` just observed.  In that mode the dashboard is
    already at its product-owned appearance origin, so Resources acquisition is
    forward-only.  When the caller immediately performs the exhaustive
    Resources authority scan, that scan owns the fresh stable-start proof and
    this transition must not spend a second fixed reset first.
    """
    if authority_scan_owns_origin and observed_dashboard is None:
        raise ValueError(
            "Resources authority-scan origin ownership requires an observed dashboard"
        )
    if observed_dashboard is not None:
        _require_canonical_chummer_resource_id(
            device,
            observed_dashboard,
            "creation-wizard-dashboard",
            evidence_prefix="creation-resources-observed-dashboard",
            surface_name="Observed Creation dashboard origin",
            deadline=deadline,
        )
        visible = (
            device.node_has_tappable_bounds(observed_dashboard)
            if deadline is None
            else device.node_has_tappable_bounds(
                observed_dashboard,
                deadline=deadline,
            )
        )
        if not visible:
            if deadline is None:
                device.capture("creation-resources-observed-dashboard-not-visible")
            else:
                device.capture(
                    "creation-resources-observed-dashboard-not-visible",
                    deadline=deadline,
                )
            raise RuntimeError(
                "Observed Creation dashboard origin was not visible before Resources navigation"
            )
    if deadline is None:
        row = device.wait_exact_resource_id_bidirectional(
            "creation-stage-resources",
            timeout=180,
            backward_scrolls=0 if observed_dashboard is not None else 22,
            forward_scrolls=22,
            scroll_distance_ratio=0.22,
            evidence_prefix="creation-resources-stage",
            surface_name="Core-authoritative Resources stage",
        )
        if (
            row.attributes.get("enabled") != "true"
            or row.attributes.get("clickable") != "true"
        ):
            device.capture("creation-resources-stage-disabled")
            raise RuntimeError("Core-authoritative Resources stage was not enabled")
        device.shell("input", "tap", *(str(value) for value in row.center))
        device.wait("creation-resources-page", timeout=60)
        if not authority_scan_owns_origin:
            shared.reset_scroll_to_top(device, swipes=22)
        return
    row = device.wait_exact_resource_id_bidirectional(
        "creation-stage-resources",
        timeout=180,
        backward_scrolls=0 if observed_dashboard is not None else 22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-resources-stage",
        surface_name="Core-authoritative Resources stage",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        row,
        "creation-stage-resources",
        evidence_prefix="creation-resources-stage",
        surface_name="Core-authoritative Resources stage",
        deadline=deadline,
    )
    if row.attributes.get("enabled") != "true" or row.attributes.get("clickable") != "true":
        device.capture("creation-resources-stage-disabled", deadline=deadline)
        raise RuntimeError("Core-authoritative Resources stage was not enabled")
    if deadline is None:
        device.shell("input", "tap", *(str(value) for value in row.center))
    else:
        device.shell(
            "input",
            "tap",
            *(str(value) for value in row.center),
            timeout=shared._remaining_operation_timeout(deadline=deadline, maximum=15),
            deadline=deadline,
        )
    route = device.wait_for_single_exact_resource_id(
        "creation-resources-page",
        timeout=60,
        evidence_prefix="creation-resources-route",
        surface_name="Creation Resources route",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        route,
        "creation-resources-page",
        evidence_prefix="creation-resources-route",
        surface_name="Creation Resources route",
        deadline=deadline,
    )
    if not authority_scan_owns_origin:
        shared.reset_scroll_to_top(device, swipes=22, deadline=deadline)


def reopen_resources(
    device: shared.Device,
    *,
    deadline: float,
) -> None:
    reopen = device.wait_exact_resource_id_bidirectional(
        "creation-resources-reopen",
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-resources-reopen",
        surface_name="Persisted Resources reopen action",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        reopen,
        "creation-resources-reopen",
        evidence_prefix="creation-resources-reopen",
        surface_name="Persisted Resources reopen action",
        deadline=deadline,
    )
    if (
        reopen.attributes.get("enabled") != "true"
        or reopen.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(reopen, deadline=deadline)
    ):
        device.capture("creation-resources-reopen-invalid", deadline=deadline)
        raise RuntimeError("Persisted Resources reopen action was not exactly tappable")
    device.shell(
        "input",
        "tap",
        *(str(value) for value in reopen.center),
        timeout=shared._remaining_operation_timeout(deadline=deadline, maximum=15),
        deadline=deadline,
    )
    route = device.wait_for_single_exact_resource_id(
        "creation-resources-page",
        timeout=60,
        evidence_prefix="creation-resources-reopen-route",
        surface_name="Reopened Creation Resources route",
        deadline=deadline,
    )
    _require_canonical_chummer_resource_id(
        device,
        route,
        "creation-resources-page",
        evidence_prefix="creation-resources-reopen-route",
        surface_name="Reopened Creation Resources route",
        deadline=deadline,
    )


RESOURCES_BINDING_AUTHORITY_SELECTORS = (
    "creation-resources-page",
    "creation-resources-binding-content-revision",
    "creation-resources-binding-saved-revision",
    "creation-resources-binding-snapshot-digest",
    "creation-resources-binding-raw-character-xml-digest",
    "creation-resources-binding-auxiliary-state-digest",
    "creation-resources-binding-prerequisite-draft-digest",
    "creation-resources-authority-digest",
    "creation-resources-budget-priority-nuyen",
    "creation-resources-budget-total-starting-nuyen",
)
RESOURCES_ZERO_CONVERSION_OPTION_ID = "creation-resources-option-karma-0"


def resources_authority_from_nodes(
    nodes: dict[str, shared.UiNode],
) -> dict[str, object]:
    authority = {
        "contentRevision": required_resources_integer(
            nodes,
            "creation-resources-binding-content-revision",
        ),
        "savedRevision": required_resources_integer(
            nodes,
            "creation-resources-binding-saved-revision",
        ),
        "snapshotDigest": required_resources_digest(
            nodes,
            "creation-resources-binding-snapshot-digest",
        ),
        "rawCharacterXmlDigest": required_resources_digest(
            nodes,
            "creation-resources-binding-raw-character-xml-digest",
        ),
        "auxiliaryStateDigest": required_resources_auxiliary_digest(
            nodes,
            "creation-resources-binding-auxiliary-state-digest",
        ),
        "prerequisiteDraftDigest": required_resources_digest(
            nodes,
            "creation-resources-binding-prerequisite-draft-digest",
        ),
        "authorityDigest": required_resources_digest(
            nodes,
            "creation-resources-authority-digest",
        ),
        "priorityNuyen": required_resources_integer(
            nodes,
            "creation-resources-budget-priority-nuyen",
        ),
        "totalStartingNuyen": required_resources_integer(
            nodes,
            "creation-resources-budget-total-starting-nuyen",
        ),
    }
    if authority["contentRevision"] <= 0:
        raise RuntimeError(f"Resources authority revision was invalid: {authority!r}")
    if authority["priorityNuyen"] != 50_000 or authority["totalStartingNuyen"] != 50_000:
        raise RuntimeError(
            "Priority Resources rank D did not project the canonical 50,000 "
            f"nuyen grant: {authority!r}"
        )
    return authority


def read_resources_binding_with_zero_option(
    device: shared.Device,
    *,
    deadline: float,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str = "creation-resources-binding-authority",
) -> tuple[dict[str, object], shared.UiNode]:
    """Read binding authority once and locate its exact zero-conversion row.

    The exhaustive stable-end scan measures every viewport.  Its freshest
    terminal node can be reused directly when exact identity, cardinality and
    tappability were proven by that scan.  Otherwise reacquisition is bounded
    by the measured topology and cannot fall back to a second bidirectional
    whole-page search or replay any mutation.
    """
    scan = scan_deadline_bound_resources_surface(
        device,
        (*RESOURCES_BINDING_AUTHORITY_SELECTORS, RESOURCES_ZERO_CONVERSION_OPTION_ID),
        scan_id=scan_id,
        deadline=deadline,
        scan_observer=scan_observer,
        tappable_selectors=(RESOURCES_ZERO_CONVERSION_OPTION_ID,),
        return_scan_proof=True,
    )
    if not isinstance(scan, ResourcesSurfaceScanProof):
        raise RuntimeError("Resources binding scan did not return measured topology")
    option_viewport = scan.selector_viewports.get(RESOURCES_ZERO_CONVERSION_OPTION_ID)
    if option_viewport is None:
        raise RuntimeError("Resources binding scan did not locate the zero-conversion option")
    scanned_option = scan.nodes[RESOURCES_ZERO_CONVERSION_OPTION_ID]
    option_identity_keys = ("text", "content-desc", "enabled", "clickable", "class")
    scanned_identity = tuple(
        scanned_option.attributes.get(key, "") for key in option_identity_keys
    )
    terminal_option = scan.terminal_tappable_nodes.get(
        RESOURCES_ZERO_CONVERSION_OPTION_ID
    )
    if terminal_option is not None:
        terminal_identity = tuple(
            terminal_option.attributes.get(key, "") for key in option_identity_keys
        )
        if option_viewport != scan.swipes or terminal_identity != scanned_identity:
            device.capture(
                "creation-resources-option-karma-0-terminal-reuse-drift",
                deadline=deadline,
            )
            raise RuntimeError(
                "Measured zero-conversion Resources option changed between scan "
                "and terminal reuse"
            )
        option = terminal_option
    else:
        reverse_bound = measured_reverse_reacquisition_bound(
            scan.swipes,
            option_viewport,
            maximum_viewport=22,
        ) + 2
        option, _ = rewind_to_exact_resource_id(
            device,
            RESOURCES_ZERO_CONVERSION_OPTION_ID,
            max_swipes=reverse_bound,
            distance_ratio=0.22,
            evidence_prefix="creation-resources-option-karma-0-measured-reacquisition",
            surface_name="Exact zero-conversion Resources option",
            require_tappable=True,
            deadline=deadline,
        )
        reacquired_identity = tuple(
            option.attributes.get(key, "") for key in option_identity_keys
        )
        if reacquired_identity != scanned_identity:
            device.capture(
                "creation-resources-option-karma-0-measured-reacquisition-drift",
                deadline=deadline,
            )
            raise RuntimeError(
                "Measured zero-conversion Resources option changed between scan and tap"
            )
    return resources_authority_from_nodes(scan.nodes), option


def read_resources_binding(
    device: shared.Device,
    *,
    deadline: float | None = None,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str = "creation-resources-binding-authority",
) -> dict[str, object]:
    if deadline is not None:
        nodes = scan_deadline_bound_resources_surface(
            device,
            RESOURCES_BINDING_AUTHORITY_SELECTORS,
            scan_id=scan_id,
            deadline=deadline,
            scan_observer=scan_observer,
        )
        if isinstance(nodes, ResourcesSurfaceScanProof):
            raise RuntimeError("Resources binding scan unexpectedly returned topology")
        return resources_authority_from_nodes(nodes)

    def integer(selector: str) -> int:
        return nonnegative_integer(device, selector, scroll=True)

    def digest(selector: str) -> str:
        return canonical_digest(device, selector, scroll=True)

    def auxiliary_digest(selector: str) -> str:
        return canonical_auxiliary_state_digest(
            device,
            selector,
            scroll=True,
        )

    authority = {
        "contentRevision": integer("creation-resources-binding-content-revision"),
        "savedRevision": integer("creation-resources-binding-saved-revision"),
        "snapshotDigest": digest("creation-resources-binding-snapshot-digest"),
        "rawCharacterXmlDigest": digest(
            "creation-resources-binding-raw-character-xml-digest"
        ),
        "auxiliaryStateDigest": auxiliary_digest(
            "creation-resources-binding-auxiliary-state-digest"
        ),
        "prerequisiteDraftDigest": digest(
            "creation-resources-binding-prerequisite-draft-digest"
        ),
        "authorityDigest": digest("creation-resources-authority-digest"),
        "priorityNuyen": integer("creation-resources-budget-priority-nuyen"),
        "totalStartingNuyen": integer(
            "creation-resources-budget-total-starting-nuyen"
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
    *,
    prelocated_option: shared.UiNode | None = None,
    deadline: float | None = None,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id_prefix: str = "creation-resources-confirm",
) -> dict[str, object]:
    def capture(name: str) -> None:
        if deadline is None:
            device.capture(name)
        else:
            device.capture(name, deadline=deadline)

    def text(selector: str) -> str:
        if deadline is None:
            return node_text(device, selector, scroll=True)
        return node_text(device, selector, scroll=True, deadline=deadline)

    def integer(selector: str) -> int:
        if deadline is None:
            return nonnegative_integer(device, selector, scroll=True)
        return nonnegative_integer(
            device,
            selector,
            scroll=True,
            deadline=deadline,
        )

    def digest(selector: str) -> str:
        if deadline is None:
            return canonical_digest(device, selector, scroll=True)
        return canonical_digest(
            device,
            selector,
            scroll=True,
            deadline=deadline,
        )

    option_id = RESOURCES_ZERO_CONVERSION_OPTION_ID
    if prelocated_option is None:
        option_options: dict[str, object] = {
            "timeout": 90,
            "backward_scrolls": 22,
            "forward_scrolls": 22,
            "scroll_distance_ratio": 0.22,
            "evidence_prefix": "creation-resources-option-karma-0",
            "surface_name": "Exact zero-conversion Resources option",
        }
        if deadline is not None:
            option_options["deadline"] = deadline
        option = device.wait_exact_resource_id_bidirectional(
            option_id,
            **option_options,
        )
    else:
        option = prelocated_option
    _require_canonical_chummer_resource_id(
        device,
        option,
        option_id,
        evidence_prefix="creation-resources-option-karma-0",
        surface_name="Exact zero-conversion Resources option",
        deadline=deadline,
    )
    detail = option.attributes.get("content-desc", "")
    option_is_tappable = (
        True
        if deadline is None
        else device.node_has_tappable_bounds(option, deadline=deadline)
    )
    if (
        option.attributes.get("enabled") != "true"
        or option.attributes.get("clickable") != "true"
        or not option_is_tappable
        or "0 Karma" not in detail
        or "50,000" not in detail
    ):
        capture("creation-resources-option-karma-0-invalid")
        raise RuntimeError(
            "Exact zero-conversion Resources option did not expose the 50,000 nuyen grant: "
            f"{detail!r}"
        )
    if deadline is None:
        device.shell("input", "tap", *(str(value) for value in option.center))
    else:
        device.shell(
            "input",
            "tap",
            *(str(value) for value in option.center),
            timeout=shared._remaining_operation_timeout(deadline=deadline, maximum=15),
            deadline=deadline,
        )
    if deadline is None:
        preview_route = device.wait("creation-resources-preview-page", timeout=60)
        shared.reset_scroll_to_top(device, swipes=22)
        preview_nodes: dict[str, shared.UiNode] | None = None
    else:
        preview_route = device.wait_for_single_exact_resource_id(
            "creation-resources-preview-page",
            timeout=60,
            evidence_prefix="creation-resources-preview-route",
            surface_name="Creation Resources preview route",
            deadline=deadline,
        )
        _require_canonical_chummer_resource_id(
            device,
            preview_route,
            "creation-resources-preview-page",
            evidence_prefix="creation-resources-preview-route",
            surface_name="Creation Resources preview route",
            deadline=deadline,
        )
        preview_nodes = scan_deadline_bound_resources_surface(
            device,
            (
                "creation-resources-preview-page",
                "creation-resources-preview-option-id",
                "creation-resources-preview-priority-grant",
                "creation-resources-preview-total-starting-nuyen",
                "creation-resources-preview-digest",
                "creation-resources-confirm",
            ),
            scan_id=f"{scan_id_prefix}-preview-authority",
            deadline=deadline,
            scan_observer=scan_observer,
            required_terminal_selectors=("creation-resources-confirm",),
        )

    preview = (
        {
            "optionId": text("creation-resources-preview-option-id").strip(),
            "priorityGrant": integer("creation-resources-preview-priority-grant"),
            "totalStartingNuyen": integer(
                "creation-resources-preview-total-starting-nuyen"
            ),
            "previewDigest": digest("creation-resources-preview-digest"),
        }
        if preview_nodes is None
        else {
            "optionId": required_resources_text(
                preview_nodes,
                "creation-resources-preview-option-id",
            ),
            "priorityGrant": required_resources_integer(
                preview_nodes,
                "creation-resources-preview-priority-grant",
            ),
            "totalStartingNuyen": required_resources_integer(
                preview_nodes,
                "creation-resources-preview-total-starting-nuyen",
            ),
            "previewDigest": required_resources_digest(
                preview_nodes,
                "creation-resources-preview-digest",
            ),
        }
    )
    if (
        preview["optionId"] != "karma:0"
        or preview["priorityGrant"] != 50_000
        or preview["totalStartingNuyen"] != 50_000
    ):
        capture("creation-resources-preview-authority-mismatch")
        raise RuntimeError(f"Resources preview changed the exact rank-D grant: {preview!r}")

    if preview_nodes is None:
        confirm = device.wait_exact_resource_id_bidirectional(
            "creation-resources-confirm",
            timeout=90,
            backward_scrolls=22,
            forward_scrolls=22,
            scroll_distance_ratio=0.22,
            evidence_prefix="creation-resources-explicit-confirm",
            surface_name="Explicit Resources confirmation",
        )
    else:
        confirm = preview_nodes["creation-resources-confirm"]
    _require_canonical_chummer_resource_id(
        device,
        confirm,
        "creation-resources-confirm",
        evidence_prefix="creation-resources-explicit-confirm",
        surface_name="Explicit Resources confirmation",
        deadline=deadline,
    )
    confirm_is_tappable = (
        True
        if deadline is None
        else device.node_has_tappable_bounds(confirm, deadline=deadline)
    )
    if (
        confirm.attributes.get("enabled") != "true"
        or confirm.attributes.get("clickable") != "true"
        or not confirm_is_tappable
    ):
        capture("creation-resources-confirm-disabled")
        raise RuntimeError("Exact Resources preview was not explicitly confirmable")
    if deadline is None:
        device.shell("input", "tap", *(str(value) for value in confirm.center))
    else:
        device.shell(
            "input",
            "tap",
            *(str(value) for value in confirm.center),
            timeout=shared._remaining_operation_timeout(deadline=deadline, maximum=15),
            deadline=deadline,
        )
    if deadline is None:
        receipt_route = device.wait(
            "creation-resources-confirm-receipt",
            timeout=90,
            scroll=True,
            max_scrolls=22,
        )
        shared.reset_scroll_to_top(device, swipes=22)
        receipt_nodes: dict[str, shared.UiNode] | None = None
    else:
        receipt_route = device.wait_for_single_exact_resource_id(
            "creation-resources-confirm-receipt",
            timeout=90,
            scroll=True,
            max_scrolls=22,
            evidence_prefix="creation-resources-confirm-receipt",
            surface_name="Creation Resources confirmation receipt",
            deadline=deadline,
        )
        _require_canonical_chummer_resource_id(
            device,
            receipt_route,
            "creation-resources-confirm-receipt",
            evidence_prefix="creation-resources-confirm-receipt",
            surface_name="Creation Resources confirmation receipt",
            deadline=deadline,
        )
        receipt_nodes = scan_deadline_bound_resources_surface(
            device,
            (
                "creation-resources-confirm-receipt",
                "creation-resources-receipt-option-id",
                "creation-resources-receipt-workspace-revision",
                "creation-resources-receipt-saved-revision",
                "creation-resources-receipt-draft-revision",
                "creation-resources-receipt-total-starting-nuyen",
                "creation-resources-receipt-preview-digest",
                "creation-resources-receipt-draft-digest",
                "creation-resources-receipt-digest",
            ),
            scan_id=f"{scan_id_prefix}-receipt-authority",
            deadline=deadline,
            scan_observer=scan_observer,
        )

    receipt = (
        {
            "optionId": text("creation-resources-receipt-option-id").strip(),
            "workspaceRevision": integer(
                "creation-resources-receipt-workspace-revision"
            ),
            "savedRevision": integer("creation-resources-receipt-saved-revision"),
            "draftRevision": integer("creation-resources-receipt-draft-revision"),
            "totalStartingNuyen": integer(
                "creation-resources-receipt-total-starting-nuyen"
            ),
            "previewDigest": digest("creation-resources-receipt-preview-digest"),
            "draftDigest": digest("creation-resources-receipt-draft-digest"),
            "receiptDigest": digest("creation-resources-receipt-digest"),
        }
        if receipt_nodes is None
        else {
            "optionId": required_resources_text(
                receipt_nodes,
                "creation-resources-receipt-option-id",
            ),
            "workspaceRevision": required_resources_integer(
                receipt_nodes,
                "creation-resources-receipt-workspace-revision",
            ),
            "savedRevision": required_resources_integer(
                receipt_nodes,
                "creation-resources-receipt-saved-revision",
            ),
            "draftRevision": required_resources_integer(
                receipt_nodes,
                "creation-resources-receipt-draft-revision",
            ),
            "totalStartingNuyen": required_resources_integer(
                receipt_nodes,
                "creation-resources-receipt-total-starting-nuyen",
            ),
            "previewDigest": required_resources_digest(
                receipt_nodes,
                "creation-resources-receipt-preview-digest",
            ),
            "draftDigest": required_resources_digest(
                receipt_nodes,
                "creation-resources-receipt-draft-digest",
            ),
            "receiptDigest": required_resources_digest(
                receipt_nodes,
                "creation-resources-receipt-digest",
            ),
        }
    )
    if (
        receipt["optionId"] != "karma:0"
        or receipt["workspaceRevision"] != before["contentRevision"] + 1
        or receipt["savedRevision"] != before["savedRevision"] + 1
        or receipt["draftRevision"] <= 0
        or receipt["totalStartingNuyen"] != 50_000
        or receipt["previewDigest"] != preview["previewDigest"]
    ):
        capture("creation-resources-receipt-authority-mismatch")
        raise RuntimeError(
            "Resources receipt was not bound to the exact preview and next workspace revision: "
            f"before={before!r}, preview={preview!r}, receipt={receipt!r}"
        )
    return {"preview": preview, "receipt": receipt}


def read_persisted_resources_authority(
    device: shared.Device,
    expected_receipt: dict[str, object],
    *,
    deadline: float | None = None,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str = "creation-resources-persisted-authority",
) -> dict[str, object]:
    if deadline is not None:
        nodes = scan_deadline_bound_resources_surface(
            device,
            (
                "creation-resources-page",
                "creation-resources-binding-content-revision",
                "creation-resources-binding-saved-revision",
                "creation-resources-binding-snapshot-digest",
                "creation-resources-binding-raw-character-xml-digest",
                "creation-resources-binding-auxiliary-state-digest",
                "creation-resources-binding-prerequisite-draft-digest",
                "creation-resources-authority-digest",
                "creation-resources-budget-priority-nuyen",
                "creation-resources-budget-total-starting-nuyen",
                "creation-resources-saved-option-id",
                "creation-resources-saved-draft-revision",
                "creation-resources-saved-draft-digest",
            ),
            scan_id=scan_id,
            deadline=deadline,
            scan_observer=scan_observer,
        )
        binding = {
            "contentRevision": required_resources_integer(
                nodes,
                "creation-resources-binding-content-revision",
            ),
            "savedRevision": required_resources_integer(
                nodes,
                "creation-resources-binding-saved-revision",
            ),
            "snapshotDigest": required_resources_digest(
                nodes,
                "creation-resources-binding-snapshot-digest",
            ),
            "rawCharacterXmlDigest": required_resources_digest(
                nodes,
                "creation-resources-binding-raw-character-xml-digest",
            ),
            "auxiliaryStateDigest": required_resources_auxiliary_digest(
                nodes,
                "creation-resources-binding-auxiliary-state-digest",
            ),
            "prerequisiteDraftDigest": required_resources_digest(
                nodes,
                "creation-resources-binding-prerequisite-draft-digest",
            ),
            "authorityDigest": required_resources_digest(
                nodes,
                "creation-resources-authority-digest",
            ),
            "priorityNuyen": required_resources_integer(
                nodes,
                "creation-resources-budget-priority-nuyen",
            ),
            "totalStartingNuyen": required_resources_integer(
                nodes,
                "creation-resources-budget-total-starting-nuyen",
            ),
        }
        if binding["contentRevision"] <= 0:
            raise RuntimeError(f"Resources authority revision was invalid: {binding!r}")
        if (
            binding["priorityNuyen"] != 50_000
            or binding["totalStartingNuyen"] != 50_000
        ):
            raise RuntimeError(
                "Priority Resources rank D did not project the canonical 50,000 "
                f"nuyen grant: {binding!r}"
            )
        saved = {
            "optionId": required_resources_text(
                nodes,
                "creation-resources-saved-option-id",
            ),
            "draftRevision": required_resources_integer(
                nodes,
                "creation-resources-saved-draft-revision",
            ),
            "draftDigest": required_resources_digest(
                nodes,
                "creation-resources-saved-draft-digest",
            ),
        }
        if (
            binding["contentRevision"] != expected_receipt["workspaceRevision"]
            or binding["savedRevision"] != expected_receipt["savedRevision"]
            or saved["optionId"] != expected_receipt["optionId"]
            or saved["draftRevision"] != expected_receipt["draftRevision"]
            or saved["draftDigest"] != expected_receipt["draftDigest"]
        ):
            device.capture(
                "creation-resources-persisted-authority-mismatch",
                deadline=deadline,
            )
            raise RuntimeError(
                "Persisted Resources authority changed across reopen/restart: "
                f"expected={expected_receipt!r}, binding={binding!r}, saved={saved!r}"
            )
        return {"binding": binding, "savedDraft": saved}

    def capture(name: str) -> None:
        if deadline is None:
            device.capture(name)
        else:
            device.capture(name, deadline=deadline)

    def text(selector: str) -> str:
        if deadline is None:
            return node_text(device, selector, scroll=True)
        return node_text(device, selector, scroll=True, deadline=deadline)

    def integer(selector: str) -> int:
        if deadline is None:
            return nonnegative_integer(device, selector, scroll=True)
        return nonnegative_integer(
            device,
            selector,
            scroll=True,
            deadline=deadline,
        )

    def digest(selector: str) -> str:
        if deadline is None:
            return canonical_digest(device, selector, scroll=True)
        return canonical_digest(
            device,
            selector,
            scroll=True,
            deadline=deadline,
        )

    binding = (
        read_resources_binding(device)
        if deadline is None
        else read_resources_binding(device, deadline=deadline)
    )
    saved = {
        "optionId": text("creation-resources-saved-option-id").strip(),
        "draftRevision": integer("creation-resources-saved-draft-revision"),
        "draftDigest": digest("creation-resources-saved-draft-digest"),
    }
    if (
        binding["contentRevision"] != expected_receipt["workspaceRevision"]
        or binding["savedRevision"] != expected_receipt["savedRevision"]
        or saved["optionId"] != expected_receipt["optionId"]
        or saved["draftRevision"] != expected_receipt["draftRevision"]
        or saved["draftDigest"] != expected_receipt["draftDigest"]
    ):
        capture("creation-resources-persisted-authority-mismatch")
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
    progress.advance("dashboard-authority-inventory")
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
    progress.advance("advanced-editor-gate-inventory")
    advanced_editor_deadline = progress.active_phase_deadline(
        "advanced-editor-gate-inventory"
    )
    dashboard_scan = assert_uncreated_advanced_editor_gated(
        device,
        scan_observer=progress.record_scan,
        scan_id="advanced-editor-gate-initial",
        deadline=advanced_editor_deadline,
    )
    dashboard_binding = dashboard_scan.binding
    method_node, method_detail, _ = reacquire_exact_ready_creation_method(
        device,
        expected_detail=dashboard_scan.method_detail,
        max_swipes=DASHBOARD_SCAN_MAX_SCROLLS,
        scan_observer=progress.record_scan,
        deadline=advanced_editor_deadline,
    )
    ready_navigation = {
        "detail": method_detail,
        "clickable": True,
        "enabled": True,
        "authorityProjectionWaited": authority_projection_waited,
    }
    device.capture(
        "creation-priority-core-bootstrap-ready",
        deadline=advanced_editor_deadline,
    )

    progress.advance("prerequisite-authority-inventory")
    prerequisite_deadline = progress.active_phase_deadline(
        "prerequisite-authority-inventory"
    )
    device.shell(
        "input",
        "tap",
        *(str(value) for value in method_node.center),
        timeout=shared._remaining_operation_timeout(
            deadline=prerequisite_deadline,
            maximum=15,
        ),
        deadline=prerequisite_deadline,
    )
    prerequisite_origin = wait_for_prerequisite_scan_origin(
        device,
        deadline=prerequisite_deadline,
    )
    prerequisite_scan = scan_prerequisite_authority(
        device,
        initial_observation=prerequisite_origin,
        scan_observer=progress.record_scan,
        deadline=prerequisite_deadline,
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
        max_scrolls=12,
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

    typed_authority_deadline = progress.active_phase_deadline(
        "typed-authority-options"
    )
    device.tap_exact_resource_id_bidirectional(
        "creation-prerequisite-talent-selection",
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-talent-selection",
        surface_name="Typed Talent selection row",
        deadline=typed_authority_deadline,
    )
    device.wait_exact_resource_id_bidirectional(
        "creation-prerequisite-talent-page",
        timeout=45,
        backward_scrolls=0,
        forward_scrolls=0,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-talent-route",
        surface_name="Typed Talent detail route",
        require_tappable=False,
        deadline=typed_authority_deadline,
    )
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
        phase_deadline_provider=progress.active_phase_deadline,
        continuation_phase_advances=(
            lambda: progress.advance("talent-active-skill-preservation"),
            lambda: progress.advance("talent-active-skill-reset"),
            lambda: progress.advance("talent-active-skill-reselection"),
        ),
    )
    progress.advance("talent-active-grant-completion")
    active_grant = active_grant_proof.receipt
    active_selected_option_ids = tuple(active_grant["selectedOptionAutomationIds"])
    active_grant_completion_deadline = progress.active_phase_deadline(
        "talent-active-grant-completion"
    )
    complete_talent_grant_to_prerequisite(
        device,
        active_grant_proof.navigation,
        active_grant_proof.current_viewport,
        scan_observer=progress.record_scan,
        deadline=active_grant_completion_deadline,
    )
    active_talent_selection_node = device.wait_exact_resource_id_bidirectional(
        "creation-prerequisite-talent-selection-id",
        timeout=60,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-active-talent-selection-id",
        surface_name="Active-skill Talent SelectionId authority",
        require_tappable=False,
        deadline=active_grant_completion_deadline,
    )
    active_talent_selection_id = (
        active_talent_selection_node.attributes.get("text")
        or active_talent_selection_node.attributes.get("content-desc")
        or ""
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
        deadline=progress.active_phase_deadline("talent-active-preview"),
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
    progress.advance("talent-skill-group-selection")
    open_talent_selection_after_preview(device)
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
        phase_deadline_provider=progress.active_phase_deadline,
        continuation_phase_advances=(
            lambda: progress.advance("talent-skill-group-preservation"),
            lambda: progress.advance("talent-skill-group-reset"),
            lambda: progress.advance("talent-skill-group-reselection"),
        ),
    )
    skill_group_grant = skill_group_grant_proof.receipt
    skill_group_selected_option_ids = tuple(
        skill_group_grant["selectedOptionAutomationIds"]
    )
    progress.advance("talent-skill-group-grant-completion")
    skill_group_grant_completion_deadline = progress.active_phase_deadline(
        "talent-skill-group-grant-completion"
    )
    complete_talent_grant_to_prerequisite(
        device,
        skill_group_grant_proof.navigation,
        skill_group_grant_proof.current_viewport,
        scan_observer=progress.record_scan,
        deadline=skill_group_grant_completion_deadline,
    )
    typed_selection_ids["talent"] = read_exact_skill_group_talent_selection_id(
        device,
        deadline=skill_group_grant_completion_deadline,
    )
    if (
        not typed_selection_ids["talent"]
        or typed_selection_ids["talent"] == active_talent_selection_id
    ):
        raise RuntimeError(
            "Switching from active-skill to skill-group Talent did not bind a distinct "
            "Core SelectionId"
        )

    progress.advance("preview-confirm")
    preview_confirm_deadline = progress.active_phase_deadline("preview-confirm")
    attributes_before = require_exact_attributes_category_round_trip(
        device,
        deadline=preview_confirm_deadline,
    )

    open_exact_prerequisite_preview(
        device,
        deadline=preview_confirm_deadline,
    )
    preview_proof: dict[str, object] = {}
    skill_group_plan_digest = require_exact_preview_talent_grant_plan(
        device,
        "Skill groups",
        skill_group_selected_option_ids,
        max_scrolls=12,
        scan_observer=progress.record_scan,
        scan_id="talent-skill-group-preview-plan",
        deadline=preview_confirm_deadline,
        proof_out=preview_proof,
    )
    preview_digest = str(preview_proof["previewDigest"])
    preview_binding_digests = dict(preview_proof["bindingDigests"])
    if preview_binding_digests != prerequisite_digests:
        raise RuntimeError(
            "Core preview binding changed from the selected prerequisite snapshot: "
            f"before={prerequisite_digests!r}, preview={preview_binding_digests!r}"
        )

    receipt_deadline = tap_exact_current_preview_confirm(
        device,
        preview_proof,
        deadline=preview_confirm_deadline,
    )
    confirmed_receipt = read_exact_confirmed_receipt(
        device,
        preview_proof=preview_proof,
        scan_observer=progress.record_scan,
        deadline=receipt_deadline,
    )
    receipt_text = str(confirmed_receipt["receiptText"])
    confirmed_revisions = dict(confirmed_receipt["revisions"])
    confirmed_draft_digest = str(confirmed_receipt["draftDigest"])
    confirmed_binding_digests = dict(confirmed_receipt["bindingDigests"])
    if confirmed_revisions["contentRevision"] <= 0 or confirmed_revisions["draftRevision"] <= 0:
        raise RuntimeError(f"Prerequisite receipt revisions are invalid: {confirmed_revisions!r}")
    if confirmed_binding_digests["rawCharacterXml"] != preview_binding_digests["rawCharacterXml"]:
        raise RuntimeError("Auxiliary draft confirmation changed raw character XML authority")
    if confirmed_binding_digests["authority"] != preview_binding_digests["authority"]:
        raise RuntimeError("Auxiliary draft confirmation changed rules authority")
    if confirmed_binding_digests["auxiliaryState"] == preview_binding_digests["auxiliaryState"]:
        raise RuntimeError("Auxiliary draft confirmation did not change auxiliary-state authority")
    dashboard_deadline = tap_exact_confirmed_receipt_back(
        device,
        confirmed_receipt,
        scan_observer=progress.record_scan,
        deadline=preview_confirm_deadline,
    )
    dashboard_ready_marker = wait_for_creation_dashboard_ready_log(
        device,
        expected_content_revision=confirmed_revisions["contentRevision"],
        expected_saved_revision=confirmed_revisions["savedRevision"],
        deadline=dashboard_deadline,
        scan_observer=progress.record_scan,
    )
    post_confirm_dashboard = assert_uncreated_advanced_editor_gated(
        device,
        scan_observer=progress.record_scan,
        scan_id="advanced-editor-gate-post-confirm",
        deadline=dashboard_deadline,
        compact_current=True,
    )
    require_marker_bound_post_confirm_dashboard(
        dashboard_ready_marker,
        post_confirm_dashboard,
    )
    if post_confirm_dashboard.binding == dashboard_binding:
        raise RuntimeError("Atomic prerequisite confirmation did not refresh the wizard revision")

    # Prove the prerequisite receipt before the Resources write legitimately advances
    # the shared auxiliary-state revision.
    progress.advance("same-process-reopen")
    same_process_deadline = progress.active_phase_deadline("same-process-reopen")
    resumed_method_node, _, _ = reacquire_exact_ready_creation_method(
        device,
        expected_detail=post_confirm_dashboard.method_detail,
        max_swipes=DASHBOARD_SCAN_MAX_SCROLLS,
        phase_id="same-process-reopen",
        deadline=same_process_deadline,
    )
    resumed_origin = open_prerequisite(
        device,
        ready_method_node=resumed_method_node,
        deadline=same_process_deadline,
    )
    resumed_authority = read_persisted_prerequisite_authority(
        device,
        initial_observation=resumed_origin,
        deadline=same_process_deadline,
        scan_observer=progress.record_scan,
        scan_id="same-process-persisted-prerequisite-authority",
    )
    assert_persisted_prerequisite_authority(
        resumed_authority.authority,
        confirmed_draft_digest,
        confirmed_binding_digests,
        confirmed_revisions["contentRevision"],
        confirmed_revisions["savedRevision"],
    )
    progress.advance("same-process-authority-options")
    same_process_options_deadline = progress.active_phase_deadline(
        "same-process-authority-options"
    )
    require_exact_restored_authority_option(
        device,
        "heritage",
        typed_selections["heritage"],
        typed_selection_ids["heritage"],
        root_navigation=resumed_authority.navigation,
        observed_selection_id=resumed_authority.selection_ids["heritage"],
        previous_root_category=None,
        retain_selected_node=False,
        deadline=same_process_options_deadline,
        scan_observer=progress.record_scan,
        scan_id="same-process-restored-authority-option-heritage",
    )
    resumed_talent_option = require_exact_restored_authority_option(
        device,
        "talent",
        typed_selections["talent"],
        typed_selection_ids["talent"],
        root_navigation=resumed_authority.navigation,
        observed_selection_id=resumed_authority.selection_ids["talent"],
        previous_root_category="heritage",
        retain_selected_node=True,
        deadline=same_process_options_deadline,
        scan_observer=progress.record_scan,
        scan_id="same-process-restored-authority-option-talent",
    )
    if resumed_talent_option.selected_node is None:
        raise RuntimeError("Same-process Talent proof retained no selected option node")
    progress.advance("same-process-restored-talent-grant")
    same_process_grant_deadline = progress.active_phase_deadline(
        "same-process-restored-talent-grant"
    )
    resumed_talent_grant = require_restored_talent_grant(
        device,
        typed_selections["talent"],
        resumed_talent_option.selected_node,
        "Skill groups",
        str(skill_group_grant["grantDigest"]),
        skill_group_selected_option_ids,
        scan_observer=progress.record_scan,
        scan_id="same-process-restored-talent-skill-group-grant",
        deadline=same_process_grant_deadline,
    )
    resumed_attributes = resumed_authority.attributes_authority
    if resumed_attributes != attributes_before:
        raise RuntimeError(
            "Confirmed prerequisite draft did not resume the exact localized Attribute "
            f"rank row: before={attributes_before!r}, resumed={resumed_attributes!r}"
        )
    if not resumed_authority.attributes_ready:
        raise RuntimeError("Confirmed prerequisite draft did not restore Attributes readiness")

    progress.advance("resources-initial-authority")
    resources_initial_deadline = progress.active_phase_deadline(
        "resources-initial-authority"
    )
    device.back(deadline=resources_initial_deadline)
    resources_dashboard = shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=0,
        deadline=resources_initial_deadline,
    )
    open_resources(
        device,
        deadline=resources_initial_deadline,
        observed_dashboard=resources_dashboard,
        authority_scan_owns_origin=True,
    )
    resources_before, resources_zero_option = read_resources_binding_with_zero_option(
        device,
        deadline=resources_initial_deadline,
        scan_observer=progress.record_scan,
        scan_id="creation-resources-initial-binding-authority",
    )

    progress.advance("resources-preview-confirm")
    resources_confirm_deadline = progress.active_phase_deadline(
        "resources-preview-confirm"
    )
    resources_confirmation = select_and_confirm_resources(
        device,
        resources_before,
        prelocated_option=resources_zero_option,
        deadline=resources_confirm_deadline,
        scan_observer=progress.record_scan,
        scan_id_prefix="creation-resources-initial-confirm",
    )
    resources_receipt = resources_confirmation["receipt"]
    device.capture("creation-resources-confirmed", deadline=resources_confirm_deadline)

    progress.advance("resources-same-process-reopen")
    resources_reopen_deadline = progress.active_phase_deadline(
        "resources-same-process-reopen"
    )
    reopen_resources(device, deadline=resources_reopen_deadline)
    resources_same_process = read_persisted_resources_authority(
        device,
        resources_receipt,
        deadline=resources_reopen_deadline,
        scan_observer=progress.record_scan,
        scan_id="creation-resources-same-process-persisted-authority",
    )

    progress.advance("resources-prerequisite-rebind")
    resources_rebind_deadline = progress.active_phase_deadline(
        "resources-prerequisite-rebind"
    )
    device.back(deadline=resources_rebind_deadline)
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=22,
        deadline=resources_rebind_deadline,
    )
    post_resources_method_node, _, _ = reacquire_exact_ready_creation_method(
        device,
        expected_detail=post_confirm_dashboard.method_detail,
        max_swipes=DASHBOARD_SCAN_MAX_SCROLLS,
        phase_id="resources-prerequisite-rebind",
        deadline=resources_rebind_deadline,
    )
    post_resources_origin = open_prerequisite(
        device,
        ready_method_node=post_resources_method_node,
        deadline=resources_rebind_deadline,
    )
    post_resources_prerequisite_authority = read_persisted_prerequisite_authority(
        device,
        initial_observation=post_resources_origin,
        deadline=resources_rebind_deadline,
        scan_observer=progress.record_scan,
        scan_id="post-resources-persisted-prerequisite-authority",
    )
    resources_binding = resources_same_process.get("binding")
    if not isinstance(resources_binding, dict):
        raise RuntimeError("Reopened Resources binding receipt is absent")
    post_resources_prerequisite_binding_digests = (
        require_resources_confirmation_authority_transition(
            confirmed_binding_digests,
            confirmed_revisions,
            confirmed_draft_digest,
            resources_before,
            resources_binding,
        )
    )
    assert_persisted_prerequisite_authority(
        post_resources_prerequisite_authority.authority,
        confirmed_draft_digest,
        post_resources_prerequisite_binding_digests,
        int(resources_receipt["workspaceRevision"]),
        int(resources_receipt["savedRevision"]),
    )

    progress.advance("process-restart-reopen")
    process_restart_deadline = progress.active_phase_deadline(
        "process-restart-reopen"
    )
    restart = shared.force_stop_and_launch_new_process(
        device,
        initial_launch,
        deadline=process_restart_deadline,
    )
    shared.wait_for_phone_runner_route(
        device,
        created=False,
        deadline=process_restart_deadline,
    )
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        reset_swipes=22,
        deadline=process_restart_deadline,
    )
    process_restart_dashboard = assert_uncreated_advanced_editor_gated(
        device,
        scan_observer=progress.record_scan,
        scan_id="advanced-editor-gate-process-restart",
        deadline=process_restart_deadline,
    )
    restarted_method_node, _, _ = reacquire_exact_ready_creation_method(
        device,
        expected_detail=process_restart_dashboard.method_detail,
        max_swipes=DASHBOARD_SCAN_MAX_SCROLLS,
        phase_id="process-restart-reopen",
        deadline=process_restart_deadline,
    )
    restarted_origin = open_prerequisite(
        device,
        ready_method_node=restarted_method_node,
        deadline=process_restart_deadline,
    )
    restarted_authority = read_persisted_prerequisite_authority(
        device,
        initial_observation=restarted_origin,
        deadline=process_restart_deadline,
        scan_observer=progress.record_scan,
        scan_id=PROCESS_RESTART_PERSISTED_PREREQUISITE_SCAN_ID,
        max_consecutive_empty_reads=(
            PROCESS_RESTART_PERSISTED_PREREQUISITE_MAX_CONSECUTIVE_EMPTY_READS
        ),
    )
    assert_persisted_prerequisite_authority(
        restarted_authority.authority,
        confirmed_draft_digest,
        post_resources_prerequisite_binding_digests,
        int(resources_receipt["workspaceRevision"]),
        int(resources_receipt["savedRevision"]),
    )
    progress.advance("process-restart-authority-options")
    process_restart_options_deadline = progress.active_phase_deadline(
        "process-restart-authority-options"
    )
    require_exact_restored_authority_option(
        device,
        "heritage",
        typed_selections["heritage"],
        typed_selection_ids["heritage"],
        root_navigation=restarted_authority.navigation,
        observed_selection_id=restarted_authority.selection_ids["heritage"],
        previous_root_category=None,
        retain_selected_node=False,
        deadline=process_restart_options_deadline,
        scan_observer=progress.record_scan,
        scan_id="process-restart-restored-authority-option-heritage",
    )
    restarted_talent_option = require_exact_restored_authority_option(
        device,
        "talent",
        typed_selections["talent"],
        typed_selection_ids["talent"],
        root_navigation=restarted_authority.navigation,
        observed_selection_id=restarted_authority.selection_ids["talent"],
        previous_root_category="heritage",
        retain_selected_node=True,
        deadline=process_restart_options_deadline,
        scan_observer=progress.record_scan,
        scan_id="process-restart-restored-authority-option-talent",
    )
    if restarted_talent_option.selected_node is None:
        raise RuntimeError("Process-restart Talent proof retained no selected option node")
    progress.advance("process-restart-restored-talent-grant")
    process_restart_grant_deadline = progress.active_phase_deadline(
        "process-restart-restored-talent-grant"
    )
    restarted_talent_grant = require_restored_talent_grant(
        device,
        typed_selections["talent"],
        restarted_talent_option.selected_node,
        "Skill groups",
        str(skill_group_grant["grantDigest"]),
        skill_group_selected_option_ids,
        scan_observer=progress.record_scan,
        scan_id="process-restart-restored-talent-skill-group-grant",
        deadline=process_restart_grant_deadline,
    )
    if (
        restarted_authority.attributes_authority != attributes_before
        or not restarted_authority.attributes_ready
    ):
        raise RuntimeError(
            "Process-restart prerequisite authority did not restore the exact "
            "Attributes row and readiness"
        )
    progress.advance("process-restart-resources")
    process_restart_resources_deadline = progress.active_phase_deadline(
        "process-restart-resources"
    )
    device.back(deadline=process_restart_resources_deadline)
    process_restart_resources_dashboard = shared.open_creation_dashboard(
        device,
        open_build_route=False,
        dashboard_timeout=60,
        reset_swipes=0,
        deadline=process_restart_resources_deadline,
    )
    open_resources(
        device,
        deadline=process_restart_resources_deadline,
        observed_dashboard=process_restart_resources_dashboard,
        authority_scan_owns_origin=True,
    )
    resources_restarted = read_persisted_resources_authority(
        device,
        resources_receipt,
        deadline=process_restart_resources_deadline,
        scan_observer=progress.record_scan,
        scan_id=PROCESS_RESTART_RESOURCES_SCAN_ID,
    )
    if resources_restarted != resources_same_process:
        raise RuntimeError(
            "Resources authority changed across the exact process restart: "
            f"sameProcess={resources_same_process!r}, restarted={resources_restarted!r}"
        )
    device.capture(
        "creation-prerequisite-process-restart",
        deadline=process_restart_resources_deadline,
    )

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
            "sameSessionPersistedAuthority": resumed_authority.authority,
            "restartedPersistedAuthority": restarted_authority.authority,
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
            "prerequisiteAuthorityAfterResources": (
                post_resources_prerequisite_authority.authority
            ),
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
