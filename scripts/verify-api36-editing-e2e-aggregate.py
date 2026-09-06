#!/usr/bin/env python3
"""Require the exact API-36 phone journey set bound to one APK authority."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

from api36_wizard_gate_contract import (  # noqa: E402
    AGGREGATE_SCHEMA,
    AUTHORITY_CLASS,
    DEFAULT_CONTRACT,
    PROOF_SCOPE,
    contract_binding,
    journey_map,
)
from api36_proof_environment_authority import (  # noqa: E402
    BUILD_SCHEMA as BUILD_ENVIRONMENT_SCHEMA,
    DEFAULT_POLICY as DEFAULT_ENVIRONMENT_POLICY,
    JOURNEY_SCHEMA as JOURNEY_ENVIRONMENT_SCHEMA,
    StableFile as EnvironmentStableFile,
    canonical_sha256 as environment_canonical_sha256,
    load_policy as load_environment_policy,
    parse_emulator_live_observation,
    policy_binding as environment_policy_binding,
    validate_receipt as validate_environment_receipt,
)


SHA256 = re.compile(r"^[0-9a-f]{64}$")
ARTIFACT_DIGEST = re.compile(r"^(?:sha256:)?[0-9a-f]{64}$")
POSITIVE_INTEGER = re.compile(r"^[1-9][0-9]*$")
JOURNEYS = {
    matrix_journey: driver_and_schema[0]
    for matrix_journey, driver_and_schema in journey_map().items()
}
CREATION_PROGRESS_SCHEMA = "chummer.android.creation-prerequisite-progress/v5"
CREATION_TOTAL_TARGET_MS = 45 * 60 * 1000
CREATION_METHOD_REACQUISITION_SCAN_ID = (
    "creation-stage-method-ready-reacquisition"
)
CREATION_METHOD_REACQUISITION_PHASES = (
    "advanced-editor-gate-inventory",
    "resources-prerequisite-rebind",
)
CREATION_METHOD_REACQUISITION_MAX_SCROLLS = 18
CREATION_METHOD_ONE_SHOT_SCHEMA = (
    "chummer.android.creation-method-one-shot/v1"
)
CREATION_METHOD_ONE_SHOT_DIGEST_DOMAIN = (
    "canonical-accessibility-signature-json"
)
CONFIRMED_RECEIPT_SCAN_ID = "creation-prerequisite-confirmed-receipt"
CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID = (
    "creation-prerequisite-confirmed-receipt-back-reacquisition"
)
POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID = (
    "post-confirm-dashboard-route-ready-log"
)
CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_SCROLLS = 12
CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_EMPTY_HIERARCHIES = 3
CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_SYSTEM_UI_DISMISSALS = 3
CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_ELAPSED_MS = 45_000
CONFIRMED_RECEIPT_BACK_REACQUISITION_DOWNSTREAM_RESERVE_MS = 81_000
CONFIRMED_RECEIPT_BACK_REACQUISITION_DELAY_MS = 200
POST_CONFIRM_DASHBOARD_ROUTE_READY_MAX_ELAPSED_MS = 30_000
POST_CONFIRM_DASHBOARD_ROUTE_READY_READ_ATTEMPT_MAX_MS = 5_000
POST_CONFIRM_DASHBOARD_ROUTE_READY_POLL_DELAY_MS = 250
TALENT_REACQUISITION_MAX_SCROLLS = 40
TALENT_OPTION_RECOVERY_MAX_SCROLLS = 40
TALENT_COMPLETION_REFLOW_MAX_SCROLLS = 4
TALENT_REACQUISITION_STABLE_REPEATS = 2
TALENT_REACQUISITION_DISTANCE_RATIO = 0.60
TALENT_OPTION_RECOVERY_DISTANCE_RATIO = 0.22
TALENT_OPTION_PREFIXES = (
    "creation-prerequisite-talent-active-skill-option-",
    "creation-prerequisite-talent-skill-group-option-",
)
TALENT_REACQUISITION_PHASES = (
    "talent-active-skill-grant",
    "talent-active-skill-preservation",
    "talent-active-skill-reset",
    "talent-active-skill-reselection",
    "talent-active-grant-completion",
    "talent-skill-group-grant",
    "talent-skill-group-preservation",
    "talent-skill-group-reset",
    "talent-skill-group-reselection",
    "talent-skill-group-grant-completion",
)
CREATION_METHOD_REACQUISITION_DIRECTION = "down"
CREATION_METHOD_REACQUISITION_DISTANCE_RATIO = 0.60
CREATION_PHASE_BUDGETS_MS = {
    "device-preflight-install": 180_000,
    "initial-navigation": 60_000,
    "initial-authority": 90_000,
    "dashboard-proof": 30_000,
    "dashboard-authority-inventory": 30_000,
    "advanced-editor-gate-inventory": 90_000,
    # Run 33680699208 exhausted the former external observer slice only after
    # a safe hierarchy retry and strict owned-file reconciliation. Preserve the
    # complete tap/origin/stable-scan proof under one exact bounded phase cap.
    "prerequisite-authority-inventory": 120_000,
    "priority-ranks": 150_000,
    "typed-authority-options": 150_000,
    "talent-active-skill-grant": 180_000,
    "talent-active-skill-preservation": 150_000,
    "talent-active-skill-reset": 150_000,
    "talent-active-skill-reselection": 150_000,
    "talent-active-grant-completion": 180_000,
    "talent-active-preview": 150_000,
    "talent-skill-group-selection": 150_000,
    "talent-skill-group-grant": 180_000,
    "talent-skill-group-preservation": 150_000,
    "talent-skill-group-reset": 150_000,
    "talent-skill-group-reselection": 150_000,
    "talent-skill-group-grant-completion": 180_000,
    "preview-confirm": 360_000,
    "same-process-reopen": 90_000,
    "same-process-authority-options": 120_000,
    "same-process-restored-talent-grant": 90_000,
    # Run 33637265813 proved the product route and exact zero-Karma authority,
    # then exhausted the observer lease while preserving file-backed retry and
    # reconciliation. Keep the per-viewport proof intact and bind only this
    # evidence-backed phase to 180 seconds; the 45-minute total remains fixed.
    "resources-initial-authority": 180_000,
    "resources-preview-confirm": 240_000,
    "resources-same-process-reopen": 120_000,
    "resources-prerequisite-rebind": 180_000,
    "process-restart-reopen": 240_000,
    "process-restart-authority-options": 120_000,
    "process-restart-restored-talent-grant": 90_000,
    "process-restart-resources": 120_000,
}
CREATION_MILESTONES = (
    ("app-cold-start-complete", "initial-navigation"),
    ("phone-shell-locale-complete", "initial-navigation"),
    ("dialog-acquisition-complete", "initial-navigation"),
    ("create-bootstrap-transaction-complete", "initial-authority"),
    ("dashboard-render-complete", "dashboard-proof"),
)
CREATION_TIMING_ROUNDING_TOLERANCE_MS = (
    len(CREATION_PHASE_BUDGETS_MS) + 1
) // 2
STARTED_FIELDS = {
    "profile",
    "matrix_journey",
    "driver_journey",
    "gate_contract_sha256",
    "artifact_id",
    "artifact_digest",
    "artifact_name",
    "artifact_attempt",
    "apk_sha256",
}


def require_creation_method_reacquisition_scans(
    timing: dict[str, Any],
    *,
    phase_elapsed_by_id: dict[str, int],
) -> None:
    """Require one fully validated method scan in each authorized phase."""
    scans = timing.get("scans")
    if not isinstance(scans, list):
        raise ValueError("creation prerequisite scan timing evidence is missing")
    matches = [
        scan
        for scan in scans
        if isinstance(scan, dict)
        and scan.get("scanId") == CREATION_METHOD_REACQUISITION_SCAN_ID
    ]
    if len(matches) != len(CREATION_METHOD_REACQUISITION_PHASES):
        raise ValueError(
            "creation method reacquisition scan cardinality differs: "
            f"expected={len(CREATION_METHOD_REACQUISITION_PHASES)}, "
            f"actual={len(matches)}"
        )
    observed_phases = tuple(scan.get("phaseId") for scan in matches)
    unknown_phases = [
        phase_id
        for phase_id in observed_phases
        if phase_id not in CREATION_METHOD_REACQUISITION_PHASES
    ]
    if unknown_phases:
        raise ValueError(
            "creation method reacquisition scan phase whitelist differs: "
            f"unknown={unknown_phases!r}"
        )
    phase_matches = {
        phase_id: [
            scan for scan in matches if scan.get("phaseId") == phase_id
        ]
        for phase_id in CREATION_METHOD_REACQUISITION_PHASES
    }
    incorrect_cardinality = {
        phase_id: len(phase_scans)
        for phase_id, phase_scans in phase_matches.items()
        if len(phase_scans) != 1
    }
    if incorrect_cardinality:
        raise ValueError(
            "creation method reacquisition per-phase cardinality differs: "
            f"expected=1, actual={incorrect_cardinality!r}"
        )
    if observed_phases != CREATION_METHOD_REACQUISITION_PHASES:
        raise ValueError(
            "creation method reacquisition scan phase order differs: "
            f"expected={CREATION_METHOD_REACQUISITION_PHASES!r}, "
            f"actual={observed_phases!r}"
        )

    for phase_id in CREATION_METHOD_REACQUISITION_PHASES:
        scan = phase_matches[phase_id][0]
        required_literals: dict[str, Any] = {
            "status": "resolved",
            "phaseId": phase_id,
            "direction": CREATION_METHOD_REACQUISITION_DIRECTION,
            "distanceRatio": CREATION_METHOD_REACQUISITION_DISTANCE_RATIO,
            "configuredMaxScrolls": CREATION_METHOD_REACQUISITION_MAX_SCROLLS,
            "stableRepeats": 2,
            "maximumEmptyHierarchyReads": 3,
            "maximumSystemUiDismissals": 3,
            "phaseBudgetMs": CREATION_PHASE_BUDGETS_MS[phase_id],
        }
        differing = {
            field: (expected, scan.get(field))
            for field, expected in required_literals.items()
            if scan.get(field) != expected
        }
        if differing:
            raise ValueError(
                "creation method reacquisition scan authority differs: "
                f"phase={phase_id!r}, differing={differing!r}"
            )
        if type(scan.get("deadlineEnforced")) is not bool or scan.get(
            "deadlineEnforced"
        ) is not True:
            raise ValueError(
                "creation method reacquisition scan authority differs: "
                f"phase={phase_id!r}, deadlineEnforced must be the JSON boolean true"
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
            raise ValueError(
                "creation method reacquisition scan timing/count data differs: "
                f"phase={phase_id!r}, fields={invalid!r}"
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
        if not (
            1 <= value["screens"]
            and 0 <= value["swipes"] <= CREATION_METHOD_REACQUISITION_MAX_SCROLLS
            and value["emptyHierarchyReads"] <= 3
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
            and value["hierarchyElapsedMs"] + mandatory_wait_ms
            <= value["elapsedMs"] + read_rounding_ms + 1
            and value["elapsedMs"] <= phase_elapsed_by_id[phase_id]
            and value["elapsedMs"] <= CREATION_PHASE_BUDGETS_MS[phase_id]
        ):
            raise ValueError(
                "creation method reacquisition scan did not reconcile gestures, "
                "screens, hierarchy reads, or phase timing: "
                f"phase={phase_id!r}"
            )


def require_creation_method_one_shot_opening(timing: dict[str, Any]) -> None:
    """Require fresh geometry and one non-replayed tap before route observation."""
    scans = timing.get("scans")
    if not isinstance(scans, list):
        raise ValueError("creation prerequisite scan timing evidence is missing")
    matches = [
        scan
        for scan in scans
        if isinstance(scan, dict)
        and scan.get("scanId") == "creation-prerequisite-scan-origin"
        and scan.get("phaseId") == "prerequisite-authority-inventory"
        and isinstance(scan.get("openingAction"), dict)
    ]
    if len(matches) != 1:
        raise ValueError(
            "creation method one-shot opening cardinality differs: "
            f"expected=1, actual={len(matches)}"
        )
    scan = matches[0]
    action = scan["openingAction"]
    pre_tap = action.get("preTap")
    tap = action.get("tap")
    first_post_tap = action.get("firstPostTap")
    if (
        scan.get("status") != "resolved"
        or not isinstance(pre_tap, dict)
        or not isinstance(tap, dict)
        or not isinstance(first_post_tap, dict)
    ):
        raise ValueError("creation method one-shot opening evidence is incomplete")
    required_literals = {
        "schema": CREATION_METHOD_ONE_SHOT_SCHEMA,
        "status": "first-post-tap-observed",
        "selector": "creation-stage-method",
        "fullResourceId": (
            "com.myexternalbrain.chummer:id/creation-stage-method"
        ),
        "diagnosticCapture": "creation-priority-core-bootstrap-ready",
        "tapReplayPerformed": False,
        "fallbackTapPerformed": False,
    }
    differing = {
        field: (expected, action.get(field))
        for field, expected in required_literals.items()
        if action.get(field) != expected
    }
    if differing:
        raise ValueError(
            f"creation method one-shot opening authority differs: {differing!r}"
        )
    bounds_match = re.fullmatch(
        r"\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]",
        str(pre_tap.get("bounds", "")),
    )
    center = pre_tap.get("center")
    coordinates = tap.get("coordinates")
    if bounds_match is None:
        raise ValueError("creation method one-shot bounds are invalid")
    left, top, right, bottom = (
        int(value) for value in bounds_match.groups()
    )
    expected_center = {"x": (left + right) // 2, "y": (top + bottom) // 2}
    if (
        left >= right
        or top >= bottom
        or not isinstance(center, dict)
        or center != expected_center
        or not isinstance(coordinates, dict)
        or coordinates != expected_center
        or tap.get("command") != "input tap"
        or type(tap.get("count")) is not int
        or tap.get("count") != 1
        or pre_tap.get("enabled") is not True
        or pre_tap.get("clickable") is not True
        or type(pre_tap.get("nodeCount")) is not int
        or int(pre_tap["nodeCount"]) <= 0
        or pre_tap.get("hierarchyReadCount") != 1
        or type(pre_tap.get("hierarchyElapsedMs")) is not int
        or int(pre_tap["hierarchyElapsedMs"]) < 0
        or pre_tap.get("hierarchyDigestDomain")
        != CREATION_METHOD_ONE_SHOT_DIGEST_DOMAIN
        or not isinstance(pre_tap.get("hierarchyDigest"), str)
        or re.fullmatch(
            r"sha256:[0-9a-f]{64}",
            str(pre_tap["hierarchyDigest"]),
        )
        is None
        or not isinstance(pre_tap.get("detail"), str)
        or not str(pre_tap["detail"]).strip()
    ):
        raise ValueError(
            "creation method one-shot target geometry or tap authority differs"
        )
    cardinalities = (
        first_post_tap.get("routeCardinality"),
        first_post_tap.get("methodCardinality"),
        first_post_tap.get("bindingCardinality"),
    )
    if (
        first_post_tap.get("hierarchyDigestDomain")
        != CREATION_METHOD_ONE_SHOT_DIGEST_DOMAIN
        or not isinstance(first_post_tap.get("hierarchyDigest"), str)
        or re.fullmatch(
            r"sha256:[0-9a-f]{64}",
            str(first_post_tap["hierarchyDigest"]),
        )
        is None
        or type(first_post_tap.get("nodeCount")) is not int
        or int(first_post_tap["nodeCount"]) < 0
        or any(type(value) is not int or value not in {0, 1} for value in cardinalities)
        or type(first_post_tap.get("routeResolved")) is not bool
        or first_post_tap["routeResolved"]
        is not all(value == 1 for value in cardinalities)
    ):
        raise ValueError(
            "creation method one-shot first post-tap route authority differs"
        )
    timestamps: list[datetime] = []
    for field, container in (
        ("observedAtUtc", pre_tap),
        ("issuedAtUtc", tap),
        ("observedAtUtc", first_post_tap),
    ):
        value = container.get(field)
        if not isinstance(value, str):
            raise ValueError("creation method one-shot timestamps are missing")
        try:
            parsed = datetime.fromisoformat(value)
        except ValueError as error:
            raise ValueError(
                "creation method one-shot timestamp is invalid"
            ) from error
        if parsed.utcoffset() != timezone.utc.utcoffset(parsed):
            raise ValueError("creation method one-shot timestamp is not UTC")
        timestamps.append(parsed)
    if timestamps != sorted(timestamps):
        raise ValueError("creation method one-shot timestamps are not monotonic")


def require_confirmed_receipt_back_reacquisition_scan(
    timing: dict[str, Any],
    *,
    preview_phase_elapsed_ms: int,
) -> None:
    scans = timing.get("scans")
    if not isinstance(scans, list):
        raise ValueError("creation prerequisite scan timing evidence is missing")
    receipt_matches = [
        scan
        for scan in scans
        if isinstance(scan, dict)
        and scan.get("scanId") == CONFIRMED_RECEIPT_SCAN_ID
    ]
    if len(receipt_matches) != 1:
        raise ValueError(
            "confirmed-receipt authority scan cardinality differs: "
            f"expected=1, actual={len(receipt_matches)}"
        )
    receipt_scan = receipt_matches[0]
    receipt_required_literals: dict[str, Any] = {
        "status": "required-authority-complete",
        "phaseId": "preview-confirm",
    }
    receipt_differing = {
        field: (expected, receipt_scan.get(field))
        for field, expected in receipt_required_literals.items()
        if receipt_scan.get(field) != expected
    }
    if receipt_differing:
        raise ValueError(
            "confirmed-receipt scan authority differs: "
            f"{receipt_differing!r}"
        )
    if (
        type(receipt_scan.get("swipes")) is not int
        or int(receipt_scan["swipes"]) < 0
    ):
        raise ValueError(
            "confirmed-receipt authority scan swipes must be a nonnegative integer"
        )
    receipt_swipes = int(receipt_scan["swipes"])
    if receipt_swipes > 12:
        raise ValueError(
            "confirmed-receipt authority scan swipes exceeded its measured bound"
        )
    matches = [
        scan
        for scan in scans
        if isinstance(scan, dict)
        and scan.get("scanId")
        == CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
    ]
    if len(matches) != 1:
        raise ValueError(
            "confirmed-receipt Back reacquisition scan cardinality differs: "
            f"expected=1, actual={len(matches)}"
        )
    scan = matches[0]
    required_literals: dict[str, Any] = {
        "status": "resolved",
        "phaseId": "preview-confirm",
        "direction": "forward-from-measured-restored-bottom",
        "distanceRatio": 0.30,
        "deadlineEnforced": True,
        "maximumEmptyHierarchyReads": (
            CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_EMPTY_HIERARCHIES
        ),
        "maximumSystemUiDismissals": (
            CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_SYSTEM_UI_DISMISSALS
        ),
        "downstreamReserveMs": (
            CONFIRMED_RECEIPT_BACK_REACQUISITION_DOWNSTREAM_RESERVE_MS
        ),
    }
    differing = {
        field: (expected, scan.get(field))
        for field, expected in required_literals.items()
        if scan.get(field) != expected
    }
    if differing:
        raise ValueError(
            "confirmed-receipt Back reacquisition authority differs: "
            f"{differing!r}"
        )
    if type(scan.get("deadlineEnforced")) is not bool or scan.get(
        "deadlineEnforced"
    ) is not True:
        raise ValueError(
            "confirmed-receipt Back reacquisition authority differs: "
            "deadlineEnforced must be the JSON boolean true"
        )
    integer_fields = (
        "screens",
        "swipes",
        "configuredMaxScrolls",
        "emptyHierarchyReads",
        "systemUiDismissals",
        "hierarchyReadCount",
        "hierarchyElapsedMs",
        "maximumHierarchyReadMs",
        "maximumElapsedMs",
        "elapsedMs",
    )
    invalid = [
        field
        for field in integer_fields
        if type(scan.get(field)) is not int or int(scan[field]) < 0
    ]
    if invalid:
        raise ValueError(
            "confirmed-receipt Back reacquisition timing/count data differs: "
            f"{invalid!r}"
        )
    value = {field: int(scan[field]) for field in integer_fields}
    if value["configuredMaxScrolls"] != receipt_swipes:
        raise ValueError(
            "confirmed-receipt Back configuredMaxScrolls differs from the "
            "original confirmed-receipt swipes"
        )
    read_rounding_ms = (value["hierarchyReadCount"] + 1) // 2
    mandatory_wait_ms = (
        value["swipes"] * CONFIRMED_RECEIPT_BACK_REACQUISITION_DELAY_MS
        + value["emptyHierarchyReads"]
        * CONFIRMED_RECEIPT_BACK_REACQUISITION_DELAY_MS
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
    if not (
        0
        <= value["configuredMaxScrolls"]
        <= CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_SCROLLS
        and value["configuredMaxScrolls"] == receipt_swipes
        and 0 <= value["swipes"] <= value["configuredMaxScrolls"]
        and 1 <= value["screens"]
        and value["emptyHierarchyReads"]
        <= CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_EMPTY_HIERARCHIES
        and value["systemUiDismissals"]
        <= CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_SYSTEM_UI_DISMISSALS
        and value["hierarchyReadCount"]
        == value["screens"] + value["emptyHierarchyReads"]
        and value["screens"]
        == value["swipes"] + value["systemUiDismissals"] + 1
        and value["hierarchyReadCount"] > 0
        and value["maximumHierarchyReadMs"] >= maximum_lower_bound
        and value["maximumHierarchyReadMs"] <= value["hierarchyElapsedMs"]
        and value["hierarchyElapsedMs"]
        <= value["elapsedMs"] + read_rounding_ms
        and value["hierarchyElapsedMs"] + mandatory_wait_ms
        <= value["elapsedMs"] + read_rounding_ms + 1
        and 0 < value["maximumElapsedMs"]
        <= CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_ELAPSED_MS
        and value["elapsedMs"] <= value["maximumElapsedMs"] + 1
        and value["elapsedMs"] <= preview_phase_elapsed_ms
    ):
        raise ValueError(
            "confirmed-receipt Back reacquisition scan did not reconcile its "
            "measured bound, gestures, hierarchy reads, or timing"
        )


def require_post_confirm_dashboard_route_ready_scan(
    timing: dict[str, Any],
    *,
    preview_phase_elapsed_ms: int,
    confirmed_content_revision: int,
    confirmed_saved_revision: int,
) -> None:
    scans = timing.get("scans")
    if not isinstance(scans, list):
        raise ValueError("creation prerequisite scan timing evidence is missing")
    matches = [
        scan
        for scan in scans
        if isinstance(scan, dict)
        and scan.get("scanId") == POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
    ]
    if len(matches) != 1:
        raise ValueError(
            "post-confirm dashboard route-ready scan cardinality differs: "
            f"expected=1, actual={len(matches)}"
        )
    scan = matches[0]
    required_literals: dict[str, Any] = {
        "status": "resolved",
        "phaseId": "preview-confirm",
        "observationMode": "fresh-cleared-main-log-snapshot-poll",
        "readAttemptMaxMs": (
            POST_CONFIRM_DASHBOARD_ROUTE_READY_READ_ATTEMPT_MAX_MS
        ),
        "pollDelayMs": POST_CONFIRM_DASHBOARD_ROUTE_READY_POLL_DELAY_MS,
        "deadlineEnforced": True,
        "maximumElapsedMs": POST_CONFIRM_DASHBOARD_ROUTE_READY_MAX_ELAPSED_MS,
    }
    differing = {
        field: (expected, scan.get(field))
        for field, expected in required_literals.items()
        if scan.get(field) != expected
    }
    if differing:
        raise ValueError(
            "post-confirm dashboard route-ready authority differs: "
            f"{differing!r}"
        )
    if type(scan.get("deadlineEnforced")) is not bool:
        raise ValueError(
            "post-confirm dashboard route-ready deadlineEnforced must be boolean"
        )
    integer_fields = (
        "logcatReadCount",
        "emptySnapshotCount",
        "logcatElapsedMs",
        "maximumLogcatReadMs",
        "readAttemptMaxMs",
        "pollDelayMs",
        "expectedContentRevision",
        "observedContentRevision",
        "expectedSavedRevision",
        "observedSavedRevision",
        "maximumElapsedMs",
        "elapsedMs",
    )
    invalid = [
        field
        for field in integer_fields
        if type(scan.get(field)) is not int or int(scan[field]) < 0
    ]
    if invalid:
        raise ValueError(
            "post-confirm dashboard route-ready timing/revision data differs: "
            f"{invalid!r}"
        )
    value = {field: int(scan[field]) for field in integer_fields}
    read_rounding_ms = (value["logcatReadCount"] + 1) // 2
    maximum_lower_bound = (
        (
            value["logcatElapsedMs"]
            + value["logcatReadCount"]
            - 1
        )
        // value["logcatReadCount"]
        if value["logcatReadCount"] > 0
        else 0
    )
    mandatory_wait_ms = (
        value["emptySnapshotCount"] * value["pollDelayMs"]
    )
    workspace_id = scan.get("workspaceId")
    snapshot_digest = scan.get("snapshotDigest")
    if not (
        value["expectedContentRevision"] > 0
        and value["expectedContentRevision"] == confirmed_content_revision
        and value["observedContentRevision"]
        == value["expectedContentRevision"]
        and value["expectedSavedRevision"] == confirmed_saved_revision
        and value["observedSavedRevision"] == value["expectedSavedRevision"]
        and value["logcatReadCount"] >= 1
        and value["emptySnapshotCount"] == value["logcatReadCount"] - 1
        and value["maximumLogcatReadMs"] >= maximum_lower_bound
        and value["maximumLogcatReadMs"] <= value["logcatElapsedMs"]
        and value["logcatElapsedMs"]
        <= value["elapsedMs"] + read_rounding_ms
        and value["logcatElapsedMs"] + mandatory_wait_ms
        <= value["elapsedMs"] + read_rounding_ms + 1
        and value["elapsedMs"] <= value["maximumElapsedMs"] + 1
        and value["elapsedMs"] <= preview_phase_elapsed_ms
        and isinstance(workspace_id, str)
        and 0 < len(workspace_id.strip()) <= 128
        and isinstance(snapshot_digest, str)
        and ARTIFACT_DIGEST.fullmatch(snapshot_digest) is not None
    ):
        raise ValueError(
            "post-confirm dashboard route-ready scan did not reconcile its "
            "revision, digest, workspace, or timing authority"
        )


def require_talent_reacquisition_scans(
    timing: dict[str, Any],
    *,
    phase_elapsed_by_id: dict[str, int],
) -> None:
    scans = timing.get("scans")
    if not isinstance(scans, list):
        raise ValueError("creation prerequisite scan timing evidence is missing")
    matches = [
        scan
        for scan in scans
        if isinstance(scan, dict)
        and str(scan.get("scanId", "")).endswith("-reacquisition")
        and "exactResourceIds" in scan
    ]
    observed_phases: set[str] = set()
    elapsed_by_phase: dict[str, int] = {}
    scan_count_by_phase: dict[str, int] = {}
    for scan in matches:
        phase_id = scan.get("phaseId")
        resource_ids = scan.get("exactResourceIds")
        completion_reflow_used = scan.get("completionReflowUsed", False)
        completion_reflow_bound = scan.get("completionReflowMaxScrolls", 0)
        completion_reflow_viewport = scan.get("completionReflowViewport")
        completion_reflow_display = scan.get("completionReflowDisplaySize")
        integer_fields = (
            "startingViewport",
            "targetViewport",
            "normalizedTargetViewport",
            "measuredDelta",
            "configuredMaxScrolls",
            "catalogMovementExtent",
            "screens",
            "swipes",
            "emptyHierarchyReads",
            "systemUiDismissals",
            "maximumEmptyHierarchyReads",
            "maximumSystemUiDismissals",
            "hierarchyReadCount",
            "hierarchyElapsedMs",
            "maximumHierarchyReadMs",
            "elapsedMs",
            "primaryConfiguredMaxScrolls",
            "primaryScreens",
            "primarySwipes",
            "primaryEmptyHierarchyReads",
            "primarySystemUiDismissals",
            "recoveryConfiguredMaxScrolls",
            "recoveryScreens",
            "recoverySwipes",
            "recoveryEmptyHierarchyReads",
            "recoverySystemUiDismissals",
        )
        if (
            scan.get("status") != "resolved"
            or phase_id not in TALENT_REACQUISITION_PHASES
            or scan.get("navigationMode")
            != "measured-direction-stable-boundary-overlap-recovery"
            or scan.get("direction") not in {"forward", "reverse", "none"}
            or scan.get("primaryDirection") not in {"forward", "reverse", "none"}
            or scan.get("recoveryDirection") not in {"forward", "reverse", "none"}
            or scan.get("recoveryDistanceRatio")
            != TALENT_OPTION_RECOVERY_DISTANCE_RATIO
            or scan.get("stableRepeats")
            != TALENT_REACQUISITION_STABLE_REPEATS
            or type(scan.get("stableBoundaryProven")) is not bool
            or scan.get("stableBoundaryProven") is not False
            or type(scan.get("primaryStableBoundaryProven")) is not bool
            or type(scan.get("recoveryStableBoundaryProven")) is not bool
            or scan.get("recoveryStableBoundaryProven") is not False
            or type(scan.get("recoveryEligible")) is not bool
            or type(scan.get("recoveryUsed")) is not bool
            or type(completion_reflow_used) is not bool
            or type(completion_reflow_bound) is not int
            or completion_reflow_bound not in (0, TALENT_COMPLETION_REFLOW_MAX_SCROLLS)
            or type(scan.get("deadlineEnforced")) is not bool
            or scan.get("deadlineEnforced") is not True
            or not isinstance(resource_ids, list)
            or not resource_ids
            or any(not isinstance(value, str) or not value for value in resource_ids)
            or len(resource_ids) != len(set(resource_ids))
            or any(
                type(scan.get(field)) is not int or int(scan[field]) < 0
                for field in integer_fields
            )
        ):
            raise ValueError("Talent reacquisition scan identity or typed data differs")
        value = {field: int(scan[field]) for field in integer_fields}

        def exact_talent_option(resource_id: str) -> bool:
            return any(
                resource_id.startswith(prefix)
                and re.fullmatch(
                    r"[a-z0-9](?:[a-z0-9-]*[a-z0-9])?",
                    resource_id[len(prefix) :],
                ) is not None
                for prefix in TALENT_OPTION_PREFIXES
            )

        completion_id = "creation-prerequisite-talent-grant-complete"
        if completion_reflow_bound and not (
            value["measuredDelta"] == 0
            and completion_id in resource_ids
            and len(resource_ids) >= 2
            and all(
                resource_id == completion_id or exact_talent_option(resource_id)
                for resource_id in resource_ids
            )
        ):
            raise ValueError("Talent reacquisition completion reflow group differs")
        if completion_reflow_used:
            if not (
                completion_reflow_bound == TALENT_COMPLETION_REFLOW_MAX_SCROLLS
                and 1 <= value["primarySwipes"] <= completion_reflow_bound
                and isinstance(completion_reflow_viewport, (list, tuple))
                and len(completion_reflow_viewport) == 4
                and all(type(coordinate) is int for coordinate in completion_reflow_viewport)
                and isinstance(completion_reflow_display, (list, tuple))
                and len(completion_reflow_display) == 2
                and all(type(dimension) is int for dimension in completion_reflow_display)
            ):
                raise ValueError("Talent reacquisition completion reflow typed geometry differs")
            left, top, right, bottom = completion_reflow_viewport
            width, height = completion_reflow_display
            if not (
                0 <= left < right <= width
                and 0 <= top < bottom <= height
                and left < round(width * 0.5) < right
                and top < round(height * (0.82 - TALENT_OPTION_RECOVERY_DISTANCE_RATIO))
                < round(height * 0.82) < bottom
            ):
                raise ValueError("Talent reacquisition completion reflow gesture leaves viewport")
        elif completion_reflow_viewport is not None or completion_reflow_display is not None:
            raise ValueError("Talent reacquisition unused completion reflow claims geometry")
        expected_direction = (
            "forward"
            if completion_reflow_used or value["targetViewport"] > value["startingViewport"]
            else "reverse"
            if value["targetViewport"] < value["startingViewport"]
            else "none"
        )
        expected_bound = (
            TALENT_COMPLETION_REFLOW_MAX_SCROLLS
            if completion_reflow_used
            else TALENT_REACQUISITION_MAX_SCROLLS
            if value["measuredDelta"] > 0
            else 0
        )
        expected_recovery_eligible = value["measuredDelta"] > 0 and all(
            exact_talent_option(resource_id) for resource_id in resource_ids
        )
        expected_recovery_direction = (
            "reverse"
            if expected_recovery_eligible and expected_direction == "forward"
            else "forward"
            if expected_recovery_eligible and expected_direction == "reverse"
            else "none"
        )
        expected_recovery_bound = (
            TALENT_OPTION_RECOVERY_MAX_SCROLLS
            if expected_recovery_eligible
            else 0
        )
        expected_primary_distance_ratio = (
            TALENT_OPTION_RECOVERY_DISTANCE_RATIO
            if expected_recovery_eligible or completion_reflow_used
            else TALENT_REACQUISITION_DISTANCE_RATIO
        )
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
        if not (
            value["startingViewport"] <= value["catalogMovementExtent"] <= 40
            and value["targetViewport"] <= value["catalogMovementExtent"]
            and value["normalizedTargetViewport"] == value["targetViewport"]
            and value["measuredDelta"]
            == abs(value["targetViewport"] - value["startingViewport"])
            and scan.get("direction") == expected_direction
            and scan.get("primaryDirection") == expected_direction
            and scan.get("distanceRatio") == expected_primary_distance_ratio
            and scan.get("primaryDistanceRatio")
            == expected_primary_distance_ratio
            and value["configuredMaxScrolls"] == expected_bound
            and value["primaryConfiguredMaxScrolls"] == expected_bound
            and value["primarySwipes"] <= value["primaryConfiguredMaxScrolls"]
            and scan.get("recoveryEligible") is expected_recovery_eligible
            and scan.get("recoveryDirection") == expected_recovery_direction
            and value["recoveryConfiguredMaxScrolls"] == expected_recovery_bound
            and value["recoverySwipes"]
            <= value["recoveryConfiguredMaxScrolls"]
            and (
                scan.get("recoveryUsed") is False
                or (
                    expected_recovery_eligible
                    and scan.get("primaryStableBoundaryProven") is True
                    and value["primarySwipes"]
                    >= TALENT_REACQUISITION_STABLE_REPEATS
                    and value["primaryScreens"]
                    >= TALENT_REACQUISITION_STABLE_REPEATS + 1
                    and value["recoverySwipes"] >= 1
                    and value["recoveryScreens"] >= 1
                )
            )
            and (
                scan.get("recoveryUsed") is True
                or (
                    scan.get("primaryStableBoundaryProven") is False
                    and value["recoveryScreens"] == 0
                    and value["recoverySwipes"] == 0
                    and value["recoveryEmptyHierarchyReads"] == 0
                    and value["recoverySystemUiDismissals"] == 0
                )
            )
            and value["swipes"]
            == value["primarySwipes"] + value["recoverySwipes"]
            and value["screens"]
            == value["primaryScreens"] + value["recoveryScreens"]
            and value["emptyHierarchyReads"]
            == value["primaryEmptyHierarchyReads"]
            + value["recoveryEmptyHierarchyReads"]
            and value["systemUiDismissals"]
            == value["primarySystemUiDismissals"]
            + value["recoverySystemUiDismissals"]
            and value["primaryScreens"]
            == value["primarySwipes"]
            + value["primarySystemUiDismissals"]
            + 1
            and (
                (
                    scan.get("recoveryUsed") is True
                    and value["recoveryScreens"]
                    == value["recoverySwipes"]
                    + value["recoverySystemUiDismissals"]
                )
                or (
                    scan.get("recoveryUsed") is False
                    and value["recoveryScreens"] == 0
                )
            )
            and value["screens"] >= 1
            and value["hierarchyReadCount"]
            == value["screens"] + value["emptyHierarchyReads"]
            and value["screens"]
            == value["swipes"] + value["systemUiDismissals"] + 1
            and value["primaryEmptyHierarchyReads"]
            <= value["maximumEmptyHierarchyReads"] == 3
            and value["recoveryEmptyHierarchyReads"]
            <= value["maximumEmptyHierarchyReads"]
            and value["primarySystemUiDismissals"]
            <= value["maximumSystemUiDismissals"] == 3
            and value["recoverySystemUiDismissals"]
            <= value["maximumSystemUiDismissals"]
            and value["hierarchyReadCount"] > 0
            and value["maximumHierarchyReadMs"] >= maximum_lower_bound
            and value["maximumHierarchyReadMs"] <= value["hierarchyElapsedMs"]
            and value["hierarchyElapsedMs"]
            <= value["elapsedMs"] + read_rounding_ms
            and value["hierarchyElapsedMs"] + mandatory_wait_ms
            <= value["elapsedMs"] + read_rounding_ms + 1
            and value["elapsedMs"] <= phase_elapsed_by_id[str(phase_id)]
            and value["elapsedMs"]
            <= CREATION_PHASE_BUDGETS_MS[str(phase_id)]
        ):
            raise ValueError(
                "Talent reacquisition scan did not reconcile catalog geometry, "
                "hierarchy reads, retries, or phase timing"
            )
        observed_phases.add(str(phase_id))
        elapsed_by_phase[str(phase_id)] = (
            elapsed_by_phase.get(str(phase_id), 0) + value["elapsedMs"]
        )
        scan_count_by_phase[str(phase_id)] = (
            scan_count_by_phase.get(str(phase_id), 0) + 1
        )
    if observed_phases != set(TALENT_REACQUISITION_PHASES):
        raise ValueError(
            "Talent reacquisition scan phase coverage differs: "
            f"expected={sorted(TALENT_REACQUISITION_PHASES)!r}, "
            f"actual={sorted(observed_phases)!r}"
        )
    overcommitted = {
        phase_id: {
            "scanElapsedMs": elapsed_by_phase[phase_id],
            "phaseElapsedMs": phase_elapsed_by_id[phase_id],
        }
        for phase_id in TALENT_REACQUISITION_PHASES
        if elapsed_by_phase[phase_id]
        > phase_elapsed_by_id[phase_id]
        + (scan_count_by_phase[phase_id] + 1) // 2
    }
    if overcommitted:
        raise ValueError(
            "Talent reacquisition scan elapsed partitions exceed their phases: "
            f"{overcommitted!r}"
        )


def object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json_object(path: Path) -> dict[str, Any]:
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"expected one regular JSON receipt: {path}")
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream, object_pairs_hook=object_without_duplicates)
    if not isinstance(value, dict):
        raise ValueError(f"JSON receipt root is not an object: {path}")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def expected_artifact_directory(journey: str, run_id: str) -> str:
    return f"chummer-android-api36-phone-{journey}-evidence-{run_id}"


def read_execution_started(path: Path) -> dict[str, str]:
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"execution-started evidence is missing: {path}")
    result: dict[str, str] = {}
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        key, separator, value = raw_line.partition("=")
        if not separator or not key or not value or key in result:
            raise ValueError(f"execution-started evidence is ambiguous: {path}")
        result[key] = value
    if set(result) != STARTED_FIELDS:
        raise ValueError(
            f"execution-started fields differ: expected={sorted(STARTED_FIELDS)!r}, "
            f"actual={sorted(result)!r}"
        )
    return result


def require_portable_receipt_seal(
    receipt: EnvironmentStableFile,
    seal: EnvironmentStableFile,
    *,
    expected_name: str = "receipt.json",
) -> str:
    try:
        seal_text = seal.data.decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        raise ValueError(f"journey receipt seal is not UTF-8: {seal.path}") from error
    fields = seal_text.strip().split()
    if len(fields) != 2 or fields[1] != expected_name or not SHA256.fullmatch(fields[0]):
        raise ValueError(f"journey receipt seal is not canonical: {seal.path}")
    actual = receipt.sha256
    if actual != fields[0]:
        raise ValueError(f"journey receipt seal mismatch: {receipt.path}")
    return actual


def canonical_authority(
    *,
    run_id: str,
    artifact_id: str,
    artifact_digest: str,
    artifact_name: str,
    artifact_attempt: str,
    apk_sha256: str,
) -> dict[str, Any]:
    if not POSITIVE_INTEGER.fullmatch(run_id):
        raise ValueError("run id is not a positive integer")
    if not POSITIVE_INTEGER.fullmatch(artifact_id):
        raise ValueError("artifact id is not a positive integer")
    if not POSITIVE_INTEGER.fullmatch(artifact_attempt):
        raise ValueError("artifact attempt is not a positive integer")
    if not ARTIFACT_DIGEST.fullmatch(artifact_digest):
        raise ValueError("artifact digest is not canonical SHA-256")
    if not SHA256.fullmatch(apk_sha256):
        raise ValueError("APK SHA-256 is not canonical")
    expected_name = f"chummer-android-api36-x64-debug-{run_id}-{artifact_attempt}"
    if artifact_name != expected_name:
        raise ValueError("artifact name is not bound to the expected run and attempt")
    return {
        "schema": "chummer.android.api36-apk-authority/v1",
        "runId": int(run_id),
        "artifactId": artifact_id,
        "artifactDigest": artifact_digest,
        "artifactName": artifact_name,
        "artifactAttempt": int(artifact_attempt),
        "apkSha256": apk_sha256,
    }


def require_creation_timing_within_budget(receipt: dict[str, Any]) -> None:
    journeys = receipt.get("journeys")
    confirmed_revisions = (
        journeys.get("confirmedRevisions")
        if isinstance(journeys, dict)
        else None
    )
    if not isinstance(confirmed_revisions, dict):
        raise ValueError("creation prerequisite confirmed revisions are missing")
    confirmed_content_revision = confirmed_revisions.get("contentRevision")
    confirmed_saved_revision = confirmed_revisions.get("savedRevision")
    if (
        type(confirmed_content_revision) is not int
        or confirmed_content_revision <= 0
        or type(confirmed_saved_revision) is not int
        or confirmed_saved_revision < 0
    ):
        raise ValueError("creation prerequisite confirmed revisions are invalid")
    timing = receipt.get("timing")
    if not isinstance(timing, dict):
        raise ValueError("creation prerequisite timing evidence is missing")
    if timing.get("schema") != CREATION_PROGRESS_SCHEMA:
        raise ValueError("creation prerequisite timing schema differs")
    if timing.get("status") != "timing-complete" or timing.get("clock") != "time.monotonic":
        raise ValueError("creation prerequisite timing is not complete and monotonic")
    if timing.get("configuredTotalTargetMs") != CREATION_TOTAL_TARGET_MS:
        raise ValueError("creation prerequisite total timing target differs")
    total_elapsed = timing.get("totalElapsedMs")
    if type(total_elapsed) is not int or total_elapsed < 0:
        raise ValueError("creation prerequisite total elapsed time is invalid")
    if (
        timing.get("withinConfiguredTotalTarget") is not True
        or total_elapsed > CREATION_TOTAL_TARGET_MS
    ):
        raise ValueError("creation prerequisite total timing target was exceeded")
    if timing.get("phaseBudgetsMs") != CREATION_PHASE_BUDGETS_MS:
        raise ValueError("creation prerequisite phase timing budgets differ")
    phases = timing.get("phases")
    if not isinstance(phases, list) or len(phases) != len(CREATION_PHASE_BUDGETS_MS):
        raise ValueError("creation prerequisite timing phase cardinality differs")
    for ordinal, (phase_id, budget_ms) in enumerate(
        CREATION_PHASE_BUDGETS_MS.items(),
        start=1,
    ):
        phase = phases[ordinal - 1]
        if not isinstance(phase, dict):
            raise ValueError("creation prerequisite timing phase is not an object")
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
            raise ValueError(
                f"creation prerequisite phase timing is outside budget: {phase_id}"
            )
    phase_elapsed_values = [int(phase["elapsedMs"]) for phase in phases]
    phase_elapsed_sum = sum(phase_elapsed_values)
    if abs(phase_elapsed_sum - total_elapsed) > CREATION_TIMING_ROUNDING_TOLERANCE_MS:
        raise ValueError(
            "creation prerequisite phase elapsed sum does not reconcile with "
            "total elapsed time"
        )
    milestones = timing.get("milestones")
    if not isinstance(milestones, list) or len(milestones) != len(CREATION_MILESTONES):
        raise ValueError("creation prerequisite milestone cardinality differs")
    phase_elapsed_by_id = {
        str(phase["phaseId"]): int(phase["elapsedMs"])
        for phase in phases
    }
    previous_phase_elapsed: dict[str, int] = {}
    previous_total_elapsed = -1
    for ordinal, (milestone_id, phase_id) in enumerate(CREATION_MILESTONES, start=1):
        milestone = milestones[ordinal - 1]
        if not isinstance(milestone, dict):
            raise ValueError("creation prerequisite milestone is not an object")
        if (
            milestone.get("milestoneId") != milestone_id
            or milestone.get("phaseId") != phase_id
            or type(milestone.get("ordinal")) is not int
            or milestone.get("ordinal") != ordinal
        ):
            raise ValueError(
                f"creation prerequisite milestone identity differs: {milestone_id}"
            )
        phase_elapsed_ms = milestone.get("phaseElapsedMs")
        segment_elapsed_ms = milestone.get("segmentElapsedMs")
        milestone_total_elapsed_ms = milestone.get("totalElapsedMs")
        phase_index = tuple(CREATION_PHASE_BUDGETS_MS).index(phase_id)
        minimum_total_elapsed_ms = (
            sum(phase_elapsed_values[:phase_index])
            + (phase_elapsed_ms if type(phase_elapsed_ms) is int else 0)
        )
        if (
            type(phase_elapsed_ms) is not int
            or type(segment_elapsed_ms) is not int
            or type(milestone_total_elapsed_ms) is not int
            or phase_elapsed_ms < 0
            or phase_elapsed_ms > phase_elapsed_by_id[phase_id]
            or segment_elapsed_ms < 0
            or segment_elapsed_ms
            != phase_elapsed_ms - previous_phase_elapsed.get(phase_id, 0)
            or milestone_total_elapsed_ms < previous_total_elapsed
            or milestone_total_elapsed_ms > total_elapsed
            or milestone_total_elapsed_ms + CREATION_TIMING_ROUNDING_TOLERANCE_MS
            < minimum_total_elapsed_ms
        ):
            raise ValueError(
                f"creation prerequisite milestone timing differs: {milestone_id}"
            )
        previous_phase_elapsed[phase_id] = phase_elapsed_ms
        previous_total_elapsed = milestone_total_elapsed_ms
    require_creation_method_reacquisition_scans(
        timing,
        phase_elapsed_by_id=phase_elapsed_by_id,
    )
    require_creation_method_one_shot_opening(timing)
    require_confirmed_receipt_back_reacquisition_scan(
        timing,
        preview_phase_elapsed_ms=phase_elapsed_by_id["preview-confirm"],
    )
    require_post_confirm_dashboard_route_ready_scan(
        timing,
        preview_phase_elapsed_ms=phase_elapsed_by_id["preview-confirm"],
        confirmed_content_revision=confirmed_content_revision,
        confirmed_saved_revision=confirmed_saved_revision,
    )
    require_talent_reacquisition_scans(
        timing,
        phase_elapsed_by_id=phase_elapsed_by_id,
    )


def validate_aggregate(
    evidence_root: Path,
    *,
    build_environment_receipt_path: Path,
    x64_apk_path: Path,
    arm64_apk_path: Path,
    hosted_candidate_path: Path,
    workflow_path: Path,
    environment_policy_path: Path = DEFAULT_ENVIRONMENT_POLICY,
    run_id: str,
    run_attempt: str,
    build_result: str,
    matrix_result: str,
    artifact_id: str,
    artifact_digest: str,
    artifact_name: str,
    artifact_attempt: str,
    apk_sha256: str,
    gate_contract_path: Path = DEFAULT_CONTRACT,
) -> dict[str, Any]:
    if build_result != "success":
        raise ValueError(f"build job did not succeed: {build_result!r}")
    if matrix_result != "success":
        raise ValueError(f"phone journey matrix did not succeed: {matrix_result!r}")
    if not POSITIVE_INTEGER.fullmatch(run_attempt):
        raise ValueError("run attempt must be one positive integer")
    if evidence_root.is_symlink() or not evidence_root.is_dir():
        raise ValueError("journey evidence root is not one regular directory")

    gate_authority = contract_binding(gate_contract_path)
    environment_policy_snapshot = EnvironmentStableFile(
        environment_policy_path,
        "API-36 proof environment policy",
    )
    environment_policy = load_environment_policy(environment_policy_snapshot)
    expected_environment_policy = environment_policy_binding(
        environment_policy_snapshot
    )
    build_environment_snapshot = EnvironmentStableFile(
        build_environment_receipt_path,
        "API-36 build environment receipt",
    )
    build_environment_seal_snapshot = EnvironmentStableFile(
        build_environment_receipt_path.with_name(
            f"{build_environment_receipt_path.name}.sha256"
        ),
        "API-36 build environment receipt seal",
    )
    build_environment_receipt_sha256 = require_portable_receipt_seal(
        build_environment_snapshot,
        build_environment_seal_snapshot,
        expected_name=build_environment_receipt_path.name,
    )
    if build_environment_receipt_sha256 != build_environment_snapshot.sha256:
        raise ValueError("build environment receipt seal differs")
    build_environment = build_environment_snapshot.json()
    x64_apk_snapshot = EnvironmentStableFile(x64_apk_path, "x64 APK")
    arm64_apk_snapshot = EnvironmentStableFile(arm64_apk_path, "ARM64 APK")
    hosted_candidate_snapshot = EnvironmentStableFile(
        hosted_candidate_path,
        "hosted ARM64 candidate",
    )
    workflow_snapshot = EnvironmentStableFile(workflow_path, "API-36 workflow")
    validate_environment_receipt(build_environment, environment_policy)
    if (
        build_environment.get("schema") != BUILD_ENVIRONMENT_SCHEMA
        or build_environment.get("receiptRole") != "build"
        or build_environment.get("policyAuthority") != expected_environment_policy
        or build_environment.get("gateAuthority") != gate_authority
        or build_environment.get("publicationAuthorized") is not False
    ):
        raise ValueError("build environment authority differs")
    authority = canonical_authority(
        run_id=run_id,
        artifact_id=artifact_id,
        artifact_digest=artifact_digest,
        artifact_name=artifact_name,
        artifact_attempt=artifact_attempt,
        apk_sha256=apk_sha256,
    )
    expected_build_subject = {
        "x64Apk": {
            "sha256": x64_apk_snapshot.sha256,
            "sizeBytes": x64_apk_snapshot.size,
        },
        "arm64Apk": {
            "sha256": arm64_apk_snapshot.sha256,
            "sizeBytes": arm64_apk_snapshot.size,
        },
        "hostedCandidate": {
            "schema": "chummer.android.api36-arm64-hosted-debug-candidate/v1",
            "sha256": hosted_candidate_snapshot.sha256,
            "sizeBytes": hosted_candidate_snapshot.size,
        },
        "workflow": {
            "sha256": workflow_snapshot.sha256,
            "sizeBytes": workflow_snapshot.size,
        },
    }
    build_subject = build_environment.get("subjectAuthority")
    if (
        build_subject != expected_build_subject
        or x64_apk_snapshot.sha256 != apk_sha256
    ):
        raise ValueError("build environment does not bind the exact build inputs")
    expected_directories = {
        expected_artifact_directory(journey, run_id): journey for journey in JOURNEYS
    }
    actual_entries = list(evidence_root.iterdir())
    if any(entry.is_symlink() or not entry.is_dir() for entry in actual_entries):
        raise ValueError("journey evidence root contains a non-directory or link")
    actual_names = {entry.name for entry in actual_entries}
    if actual_names != set(expected_directories):
        raise ValueError(
            "journey evidence artifact cardinality/name mismatch: "
            f"expected={sorted(expected_directories)!r}, actual={sorted(actual_names)!r}"
        )

    receipt_paths: list[Path] = []
    environment_receipt_paths: list[Path] = []
    environment_seal_paths: list[Path] = []
    emulator_observation_paths: list[Path] = []
    for directory in actual_entries:
        for root, directories, files in os.walk(directory, followlinks=False):
            root_path = Path(root)
            if any((root_path / child).is_symlink() for child in directories):
                raise ValueError("journey evidence contains a directory symlink")
            if any((root_path / child).is_symlink() for child in files):
                raise ValueError("journey evidence contains a file symlink")
            receipt_paths.extend(root_path / child for child in files if child == "receipt.json")
            environment_receipt_paths.extend(
                root_path / child
                for child in files
                if child == "environment-receipt.json"
            )
            environment_seal_paths.extend(
                root_path / child
                for child in files
                if child == "environment-receipt.json.sha256"
            )
            emulator_observation_paths.extend(
                root_path / child
                for child in files
                if child == "emulator-live-observation.json"
            )
    expected_receipt_paths = {
        evidence_root / directory / "receipt.json" for directory in expected_directories
    }
    if len(receipt_paths) != len(JOURNEYS) or set(receipt_paths) != expected_receipt_paths:
        raise ValueError(
            f"exactly {len(JOURNEYS)} top-level named journey receipts are required; "
            f"found={sorted(str(path) for path in receipt_paths)!r}"
        )
    expected_environment_receipt_paths = {
        evidence_root / directory / "environment-receipt.json"
        for directory in expected_directories
    }
    expected_environment_seal_paths = {
        evidence_root / directory / "environment-receipt.json.sha256"
        for directory in expected_directories
    }
    expected_emulator_observation_paths = {
        evidence_root / directory / "emulator-live-observation.json"
        for directory in expected_directories
    }
    if (
        len(environment_receipt_paths) != len(JOURNEYS)
        or set(environment_receipt_paths) != expected_environment_receipt_paths
        or len(environment_seal_paths) != len(JOURNEYS)
        or set(environment_seal_paths) != expected_environment_seal_paths
        or len(emulator_observation_paths) != len(JOURNEYS)
        or set(emulator_observation_paths) != expected_emulator_observation_paths
    ):
        raise ValueError(
            "exactly one top-level environment receipt, seal, and emulator "
            "live observation are required "
            f"for each of the {len(JOURNEYS)} journeys"
        )

    aggregate_journeys: dict[str, Any] = {}
    aggregate_environments: dict[str, Any] = {}
    journey_snapshots: list[EnvironmentStableFile] = []
    journey_compatibility_sha256: str | None = None
    x64_apk_size = x64_apk_snapshot.size
    if type(x64_apk_size) is not int or x64_apk_size <= 0:
        raise ValueError("build environment x64 APK size differs")
    for directory_name, journey in expected_directories.items():
        driver_journey = JOURNEYS[journey]
        directory = evidence_root / directory_name
        receipt_path = directory / "receipt.json"
        receipt_snapshot = EnvironmentStableFile(
            receipt_path,
            f"{journey} finalized journey receipt",
        )
        receipt_seal_snapshot = EnvironmentStableFile(
            directory / "receipt.json.sha256",
            f"{journey} finalized journey receipt seal",
        )
        receipt = receipt_snapshot.json()
        receipt_sha256 = require_portable_receipt_seal(
            receipt_snapshot,
            receipt_seal_snapshot,
        )
        started = read_execution_started(directory / "execution-started.txt")
        expected_started = {
            "profile": "phone",
            "matrix_journey": journey,
            "driver_journey": driver_journey,
            "gate_contract_sha256": gate_authority["contractSha256"],
            "artifact_id": artifact_id,
            "artifact_digest": artifact_digest,
            "artifact_name": artifact_name,
            "artifact_attempt": artifact_attempt,
            "apk_sha256": apk_sha256,
        }
        if started != expected_started:
            raise ValueError(f"execution-started authority differs for {journey}")
        if receipt.get("status") != "pass":
            raise ValueError(f"journey receipt is not passing: {journey}")
        if "executionStatus" in receipt and receipt["executionStatus"] != "pass":
            raise ValueError(f"journey execution did not pass: {journey}")
        if receipt.get("profile") != "phone":
            raise ValueError(f"journey receipt is not phone-only: {journey}")
        if receipt.get("publicationAuthorized", False) is not False:
            raise ValueError(f"journey receipt cannot authorize publication: {journey}")
        if receipt.get("matrixJourney") != journey:
            raise ValueError(f"matrix journey receipt binding differs: {journey}")
        if receipt.get("driverJourney") != driver_journey:
            raise ValueError(f"driver journey receipt binding differs: {journey}")
        if receipt.get("gateAuthority") != gate_authority:
            raise ValueError(f"wizard gate authority differs: {journey}")
        if receipt.get("apkSha256") != apk_sha256:
            raise ValueError(f"APK SHA-256 differs: {journey}")
        if receipt.get("artifactAuthority") != authority:
            raise ValueError(f"artifact authority differs: {journey}")
        if journey == "creation-prerequisite":
            require_creation_timing_within_budget(receipt)
        environment_path = directory / "environment-receipt.json"
        environment_snapshot = EnvironmentStableFile(
            environment_path,
            f"{journey} environment receipt",
        )
        environment_seal_snapshot = EnvironmentStableFile(
            directory / "environment-receipt.json.sha256",
            f"{journey} environment receipt seal",
        )
        environment = environment_snapshot.json()
        environment_receipt_sha256 = require_portable_receipt_seal(
            environment_snapshot,
            environment_seal_snapshot,
            expected_name="environment-receipt.json",
        )
        validate_environment_receipt(environment, environment_policy)
        emulator_observation_snapshot = EnvironmentStableFile(
            directory / "emulator-live-observation.json",
            f"{journey} emulator live observation",
        )
        expected_emulator = parse_emulator_live_observation(
            emulator_observation_snapshot
        )
        emulator_execution = expected_emulator["liveObservation"]["execution"]
        expected_execution = {
            "runId": int(run_id),
            "runAttempt": int(run_attempt),
            "matrixJourney": journey,
        }
        if (
            environment["environment"]["androidSdk"]["emulator"]
            != expected_emulator
            or emulator_execution != expected_execution
        ):
            raise ValueError(f"emulator live observation differs: {journey}")
        expected_environment_subject = {
            "matrixJourney": journey,
            "driverJourney": driver_journey,
            "receiptSchema": receipt["schema"],
            "journeyReceiptSha256": receipt_sha256,
            "journeyReceiptSizeBytes": receipt_snapshot.size,
            "apkSha256": apk_sha256,
            "apkSizeBytes": x64_apk_size,
            "artifactAuthoritySha256": environment_canonical_sha256(authority),
        }
        if (
            environment.get("schema") != JOURNEY_ENVIRONMENT_SCHEMA
            or environment.get("receiptRole") != "journey"
            or environment.get("policyAuthority") != expected_environment_policy
            or environment.get("gateAuthority") != gate_authority
            or environment.get("subjectAuthority") != expected_environment_subject
            or environment.get("publicationAuthorized") is not False
        ):
            raise ValueError(f"journey environment authority differs: {journey}")
        compatibility_sha256 = environment["compatibilitySha256"]
        if journey_compatibility_sha256 is None:
            journey_compatibility_sha256 = compatibility_sha256
        elif compatibility_sha256 != journey_compatibility_sha256:
            raise ValueError("journey environment compatibility differs")
        aggregate_journeys[journey] = {
            "status": "pass",
            "driverJourney": driver_journey,
            "receiptSha256": receipt_sha256,
        }
        aggregate_environments[journey] = {
            "receiptSha256": environment_receipt_sha256,
            "environmentSha256": environment["environmentSha256"],
            "compatibilitySha256": compatibility_sha256,
            "emulatorLiveObservationSha256": emulator_observation_snapshot.sha256,
        }
        journey_snapshots.extend(
            (
                receipt_snapshot,
                receipt_seal_snapshot,
                environment_snapshot,
                environment_seal_snapshot,
                emulator_observation_snapshot,
            )
        )

    aggregate = {
        "schema": AGGREGATE_SCHEMA,
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "authorityClass": AUTHORITY_CLASS,
        "proofScope": PROOF_SCOPE,
        "publicationAuthorized": False,
        "gateAuthority": gate_authority,
        "artifactAuthority": authority,
        "environmentAuthority": {
            "policyAuthority": expected_environment_policy,
            "build": {
                "receiptSha256": build_environment_receipt_sha256,
                "environmentSha256": build_environment["environmentSha256"],
                "compatibilitySha256": build_environment["compatibilitySha256"],
            },
            "journeyCompatibilitySha256": journey_compatibility_sha256,
            "journeys": aggregate_environments,
        },
        "requiredJourneyCount": len(JOURNEYS),
        "requiredJourneys": list(JOURNEYS),
        "journeyCount": len(JOURNEYS),
        "journeys": aggregate_journeys,
    }
    environment_policy_snapshot.recheck()
    build_environment_snapshot.recheck()
    build_environment_seal_snapshot.recheck()
    x64_apk_snapshot.recheck()
    arm64_apk_snapshot.recheck()
    hosted_candidate_snapshot.recheck()
    workflow_snapshot.recheck()
    for snapshot in journey_snapshots:
        snapshot.recheck()
    validate_aggregate_receipt(aggregate, gate_authority)
    return aggregate


def validate_aggregate_receipt(
    value: dict[str, Any],
    gate_authority: dict[str, Any],
) -> None:
    expected_fields = {
        "schema",
        "status",
        "generatedAtUtc",
        "authorityClass",
        "proofScope",
        "publicationAuthorized",
        "gateAuthority",
        "artifactAuthority",
        "environmentAuthority",
        "requiredJourneyCount",
        "requiredJourneys",
        "journeyCount",
        "journeys",
    }
    if set(value) != expected_fields:
        raise ValueError("wizard aggregate schema fields differ")
    if value.get("schema") != AGGREGATE_SCHEMA:
        raise ValueError("wizard aggregate schema is stale or unsupported")
    if (
        value.get("status") != "pass"
        or value.get("authorityClass") != AUTHORITY_CLASS
        or value.get("proofScope") != PROOF_SCOPE
        or value.get("publicationAuthorized") is not False
        or value.get("gateAuthority") != gate_authority
    ):
        raise ValueError("wizard aggregate authority or publication posture differs")
    required = list(JOURNEYS)
    if (
        value.get("requiredJourneyCount") != len(required)
        or value.get("journeyCount") != len(required)
        or value.get("requiredJourneys") != required
    ):
        raise ValueError(
            f"wizard aggregate must contain exactly {len(required)} required journeys"
        )
    journeys = value.get("journeys")
    if not isinstance(journeys, dict) or set(journeys) != set(required):
        raise ValueError("wizard aggregate journey set differs")
    if "full-editing" in required or "full-editing" in journeys:
        raise ValueError("Full Editing cannot satisfy wizard aggregate authority")
    environment = value.get("environmentAuthority")
    if not isinstance(environment, dict) or set(environment) != {
        "policyAuthority",
        "build",
        "journeyCompatibilitySha256",
        "journeys",
    }:
        raise ValueError("wizard aggregate environment authority differs")
    if (
        not isinstance(environment["policyAuthority"], dict)
        or not isinstance(environment["build"], dict)
        or set(environment["build"])
        != {"receiptSha256", "environmentSha256", "compatibilitySha256"}
        or not isinstance(environment["journeys"], dict)
        or set(environment["journeys"]) != set(required)
        or not isinstance(environment["journeyCompatibilitySha256"], str)
        or SHA256.fullmatch(environment["journeyCompatibilitySha256"]) is None
    ):
        raise ValueError("wizard aggregate environment binding differs")
    if any(
        not isinstance(environment["build"][field], str)
        or SHA256.fullmatch(environment["build"][field]) is None
        for field in environment["build"]
    ):
        raise ValueError("wizard aggregate build environment member differs")
    for binding in environment["journeys"].values():
        if (
            not isinstance(binding, dict)
            or set(binding)
            != {
                "receiptSha256",
                "environmentSha256",
                "compatibilitySha256",
                "emulatorLiveObservationSha256",
            }
            or any(
                not isinstance(binding[field], str)
                or SHA256.fullmatch(binding[field]) is None
                for field in binding
            )
        ):
            raise ValueError("wizard aggregate environment member differs")
    if any(
        binding["compatibilitySha256"]
        != environment["journeyCompatibilitySha256"]
        for binding in environment["journeys"].values()
    ):
        raise ValueError("wizard aggregate journey environments are not compatible")


def write_atomically(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_symlink() or (path.exists() and not path.is_file()):
        raise ValueError("aggregate receipt path is not a regular file target")
    encoded = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=path.parent,
            prefix=f".{path.name}.",
            delete=False,
        ) as stream:
            temporary_name = stream.name
            os.fchmod(stream.fileno(), 0o644)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_name, path)
        temporary_name = None
    finally:
        if temporary_name is not None:
            Path(temporary_name).unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence-root", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--gate-contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument(
        "--environment-policy",
        type=Path,
        default=DEFAULT_ENVIRONMENT_POLICY,
    )
    parser.add_argument(
        "--build-environment-receipt",
        type=Path,
        required=True,
    )
    parser.add_argument("--x64-apk", type=Path, required=True)
    parser.add_argument("--arm64-apk", type=Path, required=True)
    parser.add_argument("--hosted-candidate", type=Path, required=True)
    parser.add_argument("--workflow", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--build-result", required=True)
    parser.add_argument("--matrix-result", required=True)
    parser.add_argument("--artifact-id", required=True)
    parser.add_argument("--artifact-digest", required=True)
    parser.add_argument("--artifact-name", required=True)
    parser.add_argument("--artifact-attempt", required=True)
    parser.add_argument("--apk-sha256", required=True)
    args = parser.parse_args()

    evidence_root = args.evidence_root.absolute()
    if evidence_root.is_symlink():
        raise ValueError("journey evidence root must not be a symlink")
    receipt_path = args.receipt.absolute()
    if receipt_path.is_symlink():
        raise ValueError("aggregate receipt path must not be a symlink")

    aggregate = validate_aggregate(
        evidence_root,
        build_environment_receipt_path=args.build_environment_receipt,
        x64_apk_path=args.x64_apk,
        arm64_apk_path=args.arm64_apk,
        hosted_candidate_path=args.hosted_candidate,
        workflow_path=args.workflow,
        environment_policy_path=args.environment_policy,
        run_id=args.run_id,
        run_attempt=args.run_attempt,
        build_result=args.build_result,
        matrix_result=args.matrix_result,
        artifact_id=args.artifact_id,
        artifact_digest=args.artifact_digest,
        artifact_name=args.artifact_name,
        artifact_attempt=args.artifact_attempt,
        apk_sha256=args.apk_sha256,
        gate_contract_path=args.gate_contract,
    )
    write_atomically(receipt_path, aggregate)
    print(
        "api36_phone_evidence_aggregate=pass "
        f"scope={PROOF_SCOPE} journeys={len(JOURNEYS)} "
        f"artifact_id={args.artifact_id} apk_sha256={args.apk_sha256} "
        "publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"api36 phone evidence aggregate failed: {error}")
        raise SystemExit(1) from error
