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
PROGRESS_SCHEMA = "chummer.android.creation-prerequisite-progress/v1"
PROGRESS_FILE_NAME = "creation-prerequisite-progress.json"
PROGRESS_EVENTS_FILE_NAME = "creation-prerequisite-progress.jsonl"
TOTAL_PERFORMANCE_TARGET_MS = 15 * 60 * 1000
PHASE_BUDGET_MS = {
    "device-preflight-install": 180_000,
    "initial-authority": 90_000,
    "priority-ranks": 150_000,
    "typed-authority-options": 150_000,
    "preview-confirm": 150_000,
    "same-process-reopen": 90_000,
    "resources-preview-confirm": 150_000,
    "process-restart-reopen": 90_000,
}
PHASE_ORDER = tuple(PHASE_BUDGET_MS)


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


def scan_forward_until_stable(
    device: shared.Device,
    *,
    scan_id: str,
    max_scrolls: int,
    distance_ratio: float,
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
    while swipes <= max_scrolls:
        nodes = device.hierarchy()
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
                    "elapsedMs": round((time.monotonic() - started) * 1000),
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
                "elapsedMs": round((time.monotonic() - started) * 1000),
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
        "elapsedMs": round((time.monotonic() - started) * 1000),
    }
    if observer is not None:
        observer(result)
    device.capture(f"{scan_id}-stable-end-unproven")
    raise RuntimeError(
        f"Accessibility scan {scan_id!r} did not prove a stable page end within "
        f"{max_scrolls} forward swipes"
    )


class ProgressRecorder:
    """Deterministic phase events plus atomic timing evidence for a physical run."""

    def __init__(self, evidence_root: Path) -> None:
        self.evidence_path = evidence_root.resolve() / PROGRESS_FILE_NAME
        self.events_path = evidence_root.resolve() / PROGRESS_EVENTS_FILE_NAME
        self.started = time.monotonic()
        self.phases: list[dict[str, object]] = []
        self.scans: list[dict[str, object]] = []
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
        self.scans.append({**scan, "phaseId": self._active_id})
        self._write("running")

    def finish(self) -> dict[str, object]:
        if self._finished:
            raise RuntimeError("Prerequisite progress was already finalized")
        self._close_active("pass")
        completed = tuple(phase["phaseId"] for phase in self.phases)
        if completed != PHASE_ORDER:
            raise RuntimeError(
                f"Prerequisite progress is incomplete: expected={PHASE_ORDER!r}, "
                f"actual={completed!r}"
            )
        self._finished = True
        snapshot = self.snapshot("timing-complete")
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


def assert_uncreated_advanced_editor_gated(
    device: shared.Device,
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id: str = "advanced-editor-gate",
) -> None:
    """Scan the whole creation dashboard for Career/editor escape hatches."""
    forbidden = (
        "Actions",
        "build-origin-dossier",
        "build-free-sprite-conversion",
        "build-career-create-expense",
        "creation-wizard-attributes",
        "attribute-save-",
    )
    screens = scan_forward_until_stable(
        device,
        scan_id=scan_id,
        max_scrolls=18,
        distance_ratio=0.68,
        observer=scan_observer,
    )
    for nodes in screens:
        for selector in forbidden:
            if any(shared.Device._matches(node, selector) for node in nodes):
                device.capture(f"wizard-forbidden-{selector}")
                raise RuntimeError(
                    "Creation dashboard exposed a Career/advanced-editor control while "
                    f"the authoritative runner is still uncreated: {selector!r}"
                )
    shared.reset_scroll_to_top(device, swipes=12)


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
) -> None:
    """Require the production modal to publish either Build or one exact error."""
    deadline = time.monotonic() + timeout
    selectors = ("dialog-surface", "dialog-error", "build-save-runner")
    while time.monotonic() < deadline:
        nodes = device.hierarchy()
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
            device.capture("creation-priority-dialog-transition-cardinality-invalid")
            raise RuntimeError(
                f"New-character modal transition was ambiguous: {ambiguous!r}"
            )
        if len(matches["dialog-error"]) == 1:
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
                device.capture("creation-priority-dialog-route-overlap")
                raise RuntimeError(
                    "New-character modal and Build toolbar were published together"
                )
            return
        if device.dismiss_system_ui_anr():
            time.sleep(2)
            continue
        time.sleep(0.75)

    device.capture("creation-priority-dialog-transition-unavailable")
    raise RuntimeError(
        "New-character production modal published neither one exact error nor the Build route"
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
) -> bool:
    """Wait for the explicitly asynchronous authority projection, never for a guessed row state."""
    deadline = time.monotonic() + timeout
    saw_loading = False
    while True:
        if device.find("creation-dashboard-authority-failed") is not None:
            device.capture("creation-dashboard-authority-failed")
            raise RuntimeError(
                "Creation dashboard reported an explicit authority projection failure"
            )
        loading = device.find("creation-dashboard-authority-loading")
        if loading is None:
            return saw_loading
        saw_loading = True
        if time.monotonic() >= deadline:
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
        time.sleep(0.5)


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


def tap_prescribed_exact_enabled_priority_rank(
    device: shared.Device,
    category: str,
    *,
    expected_rank: str | None = None,
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
    invalid_ids: set[str] = set()
    duplicate_ids: set[str] = set()
    shared.reset_scroll_to_top(device, swipes=22)
    screens = scan_forward_until_stable(
        device,
        scan_id=f"rank-cardinality-{category}",
        max_scrolls=22,
        distance_ratio=0.22,
        observer=scan_observer,
    )
    for nodes in screens:
        screen_ids: list[str] = []
        for node in nodes:
            resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            if not resource_id.startswith(prefix):
                continue
            screen_ids.append(resource_id)
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
    ):
        device.capture(f"creation-prerequisite-{category}-rank-cardinality-invalid")
        raise RuntimeError(
            f"Exact {category} rank scan was invalid: candidates={sorted(candidates)!r}, "
            f"observedIds={sorted(observed_ids)!r}, expectedIds={sorted(expected_ids)!r}, "
            f"invalidIds={sorted(invalid_ids)!r}, duplicateIds={sorted(duplicate_ids)!r}"
        )

    shared.reset_scroll_to_top(device, swipes=22)
    node = device.wait_exact_resource_id_bidirectional(
        selected_resource_id,
        timeout=45,
        backward_scrolls=0,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix=f"creation-prerequisite-{category}-rank-option",
        surface_name=f"Enabled {category} rank option",
    )
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
    expected_rank: str | None = None,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
) -> str:
    """Select one exact projected rank and prove that the parent draft refreshed.

    Source-authority collection intentionally finishes at the bottom of the long
    prerequisite page.  The generic tap helper only scrolls forwards, so every
    category transition must start from a deterministic origin.  A bare wait for
    the parent page is insufficient because navigation can briefly expose the
    parent's accessibility marker before its refreshed category row is ready.
    """
    if category not in CATEGORIES:
        raise RuntimeError(f"Unsupported prerequisite category {category!r}")

    category_selector = f"creation-prerequisite-category-{category}"
    category_row = device.wait_exact_resource_id_bidirectional(
        category_selector,
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix=f"creation-prerequisite-{category}-category-row",
        surface_name=f"{category} priority category row",
    )
    device.shell("input", "tap", *(str(value) for value in category_row.center))
    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-category-page",
        timeout=45,
        evidence_prefix=f"creation-prerequisite-{category}-category-route",
        surface_name=f"{category} priority category route",
    )
    selected_resource_id = tap_prescribed_exact_enabled_priority_rank(
        device,
        category,
        expected_rank=expected_rank,
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
    while time.monotonic() < deadline:
        if device.find_exact_resource_id("creation-prerequisite-category-page") is None:
            break
        if device.dismiss_system_ui_anr():
            time.sleep(2)
            continue
        time.sleep(0.25)
    else:
        device.capture(f"creation-prerequisite-{category}-category-pop-timeout")
        raise RuntimeError(f"{category} rank selection did not leave the category route")

    device.wait_for_single_exact_resource_id(
        "creation-prerequisite-page",
        timeout=45,
        evidence_prefix=f"creation-prerequisite-{category}-parent-route",
        surface_name="Creation prerequisite parent route",
    )
    # PopAsync returns to the same long ScrollView offset.  Reacquire the exact
    # row with the same bounded overlapping scan, then require the selected rank
    # text. This proves the shared in-memory phone draft survived deep navigation.
    row = device.wait_exact_resource_id_bidirectional(
        category_selector,
        timeout=90,
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        evidence_prefix=f"creation-prerequisite-{category}-selected-row",
        surface_name=f"Selected {category} category row",
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
) -> str:
    candidate_ids: set[str] = set()
    duplicate_resource_id = False
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    screens = scan_forward_until_stable(
        device,
        scan_id=(
            "authority-option-cardinality-"
            + re.sub(r"[^a-z0-9]+", "-", required_label.casefold()).strip("-")
        ),
        max_scrolls=max_scrolls,
        distance_ratio=0.22,
        observer=scan_observer,
    )
    for nodes in screens:
        screen_ids = exact_enabled_authority_option_ids(
            nodes,
            prefix,
            required_label,
            device.node_has_tappable_bounds,
        )
        if len(screen_ids) != len(set(screen_ids)):
            duplicate_resource_id = True
        candidate_ids.update(screen_ids)
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
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    node = device.wait_exact_resource_id_bidirectional(
        resource_id,
        timeout=90,
        backward_scrolls=0,
        forward_scrolls=max_scrolls,
        scroll_distance_ratio=0.22,
        evidence_prefix="creation-prerequisite-authority-option",
        surface_name=f"Enabled authority option {required_label}",
    )
    device.shell("input", "tap", *(str(value) for value in node.center))
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
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    screens = scan_forward_until_stable(
        device,
        scan_id=scan_id or (
            "talent-grant-cardinality-"
            + re.sub(r"[^a-z0-9]+", "-", expected_kind.casefold()).strip("-")
        ),
        max_scrolls=max_scrolls,
        distance_ratio=0.22,
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

    for nodes in screens:
        screen_ids: list[str] = []
        for node in nodes:
            resource_id = _exact_resource_id(node)
            values = _accessible_values(node)
            if resource_id.startswith(opposite_prefix):
                opposite_option_ids.add(resource_id)
            if resource_id.startswith(option_prefix):
                screen_ids.append(resource_id)
                if not _is_exact_tokenized_resource_id(resource_id, option_prefix):
                    malformed_option_ids.add(resource_id)
                    continue
                option_ids.add(resource_id)
                is_selected = any(value.startswith("✓ ") for value in values)
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
    if (
        not seen_authority
        or not seen_digest
        or not seen_completion
        or malformed_option_ids
        or opposite_option_ids
        or duplicate_ids
        or contradictions
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
            f"contradictions={sorted(contradictions)!r}"
        )

    selected_count, required_count, observed_kind = next(iter(authority_counts))
    completion_enabled = next(iter(completion_states))
    if (
        observed_kind != expected_kind
        or required_count < 1
        or selected_count > required_count
        or len(option_ids) < required_count
        or len(enabled_option_ids) < selected_count
        or not selected_option_ids.issubset(enabled_option_ids)
        or selected_count != len(selected_option_ids)
        or completion_enabled != (selected_count == required_count)
    ):
        device.capture("creation-prerequisite-talent-grant-cardinality-invalid")
        raise RuntimeError(
            "Talent grant prompt count did not match its exact option state: "
            f"expectedKind={expected_kind!r}, observedKind={observed_kind!r}, "
            f"selected={selected_count}, required={required_count}, "
            f"options={sorted(option_ids)!r}, enabled={sorted(enabled_option_ids)!r}, "
            f"selectedIds={sorted(selected_option_ids)!r}, "
            f"completionEnabled={completion_enabled}"
        )
    return TalentGrantSurface(
        kind=observed_kind,
        selected_count=selected_count,
        required_count=required_count,
        grant_digest=next(iter(grant_digests)),
        option_ids=tuple(sorted(option_ids)),
        enabled_option_ids=tuple(sorted(enabled_option_ids)),
        selected_option_ids=tuple(sorted(selected_option_ids)),
        completion_enabled=completion_enabled,
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


def choose_and_prove_talent_grant(
    device: shared.Device,
    expected_kind: str,
    talent_option_id: str,
    *,
    scan_observer: Callable[[dict[str, object]], None] | None = None,
    scan_id_prefix: str,
) -> dict[str, object]:
    initial = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=f"{scan_id_prefix}-initial",
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
    for resource_id in chosen:
        tap_exact_talent_grant_option(device, expected_kind, resource_id)
    complete = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=f"{scan_id_prefix}-complete",
    )
    if (
        complete.grant_digest != initial.grant_digest
        or complete.option_ids != initial.option_ids
        or complete.selected_option_ids != chosen
        or set(complete.enabled_option_ids) != set(chosen)
        or not complete.completion_enabled
    ):
        device.capture(f"{scan_id_prefix}-selection-mismatch")
        raise RuntimeError(
            f"{expected_kind} exact selection/capacity changed authority: "
            f"initial={initial!r}, complete={complete!r}, chosen={chosen!r}"
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
    tap_exact_talent_option(device, talent_option_id)
    preserved = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=f"{scan_id_prefix}-preserved",
    )
    if preserved != complete:
        device.capture(f"{scan_id_prefix}-back-preservation-mismatch")
        raise RuntimeError(
            f"{expected_kind} native Back/re-enter did not preserve the exact draft: "
            f"complete={complete!r}, preserved={preserved!r}"
        )

    reset_id = chosen[0]
    tap_exact_talent_grant_option(device, expected_kind, reset_id)
    incomplete = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=f"{scan_id_prefix}-explicit-reset",
    )
    expected_after_reset = tuple(resource_id for resource_id in chosen if resource_id != reset_id)
    if (
        incomplete.selected_option_ids != expected_after_reset
        or incomplete.completion_enabled
        or incomplete.selected_count != initial.required_count - 1
    ):
        device.capture(f"{scan_id_prefix}-explicit-reset-mismatch")
        raise RuntimeError(
            f"{expected_kind} explicit deselection did not reopen the exact prompt: {incomplete!r}"
        )
    tap_exact_talent_grant_option(device, expected_kind, reset_id)
    restored = read_talent_grant_surface(
        device,
        expected_kind,
        scan_observer=scan_observer,
        scan_id=f"{scan_id_prefix}-explicit-reselect",
    )
    if restored != complete:
        device.capture(f"{scan_id_prefix}-explicit-reselect-mismatch")
        raise RuntimeError(
            f"{expected_kind} explicit reselection did not restore exact authority: "
            f"expected={complete!r}, actual={restored!r}"
        )
    device.capture(f"{scan_id_prefix}-complete-exact")
    return {
        "kind": expected_kind,
        "talentOptionAutomationId": talent_option_id,
        "grantDigest": restored.grant_digest,
        "requiredCount": restored.required_count,
        "allOptionAutomationIds": list(restored.option_ids),
        "selectedOptionAutomationIds": list(restored.selected_option_ids),
        "backPreservedSelection": True,
        "explicitDeselectReselect": True,
    }


def complete_talent_grant_to_prerequisite(device: shared.Device) -> None:
    device.tap_bidirectional(
        "creation-prerequisite-talent-grant-complete",
        timeout=90,
        backward_scrolls=40,
        forward_scrolls=40,
        scroll_distance_ratio=0.22,
        exact_resource_id=True,
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
    progress.advance("initial-authority")
    initial_launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device)
    phone_ui_locale = shared.record_phone_ui_locale_evidence(
        device,
        evidence_prefix="creation-prerequisite",
    )
    device.tap_exact_resource_id_until_exact_resource_id(
        "home-new-runner",
        "dialog-action-create-character",
        evidence_prefix="new-runner-build-method-dialog",
        source_name="New runner control",
        target_name="Create-character build-method action",
        target_scroll_surface="dialog-surface",
        max_target_scrolls=16,
    )
    device.tap("dialog-action-create-character", scroll=True)
    require_new_character_dialog_transition(device)
    shared.wait_for_phone_runner_route(device, created=False)
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        toolbar_timeout=120,
        dashboard_timeout=30,
        reset_swipes=8,
    )
    assert_uncreated_advanced_editor_gated(
        device,
        scan_observer=progress.record_scan,
        scan_id="advanced-editor-gate-initial",
    )

    dashboard_binding = node_text(device, "creation-wizard-binding", scroll=True)
    ready_navigation = wait_creation_method_navigation(device, ready=True)
    device.capture("creation-priority-core-bootstrap-ready")

    open_prerequisite(device)
    prerequisite_binding = node_text(device, "creation-prerequisite-binding", scroll=True)
    prerequisite_binding_authority = require_prerequisite_binding(prerequisite_binding)
    prerequisite_snapshot_digest = canonical_digest(
        device,
        "creation-prerequisite-snapshot-digest",
        scroll=True,
    )
    prerequisite_digests = {
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
        prerequisite_binding_authority,
        prerequisite_snapshot_digest,
        prerequisite_digests["authority"],
    )
    karma = node_text(device, "creation-prerequisite-karma-budget", scroll=True)
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
    source_authority_digests = read_source_authority_digests(device)

    progress.advance("priority-ranks")
    selected: dict[str, str] = {}
    if tuple(PRIORITY_PROOF_RANKS) != CATEGORIES:
        raise RuntimeError("Priority proof rank allocation does not cover the ordered categories")
    for category, expected_rank in PRIORITY_PROOF_RANKS.items():
        selected[category] = select_priority_rank(
            device,
            category,
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
    active_talent_option_id = tap_enabled_authority_option(
        device,
        "creation-prerequisite-talent-option-",
        ACTIVE_SKILL_TALENT_LABEL,
        max_scrolls=40,
        scan_observer=progress.record_scan,
    )
    device.wait("creation-prerequisite-talent-grant-page", timeout=45)
    active_grant = choose_and_prove_talent_grant(
        device,
        "Active skills",
        active_talent_option_id,
        scan_observer=progress.record_scan,
        scan_id_prefix="talent-active-skill-grant",
    )
    active_selected_option_ids = tuple(active_grant["selectedOptionAutomationIds"])
    complete_talent_grant_to_prerequisite(device)
    active_talent_selection_id = node_text(
        device,
        "creation-prerequisite-talent-selection-id",
        scroll=True,
    ).strip()
    if not active_talent_selection_id:
        raise RuntimeError("Active-skill Talent SelectionId was not exposed by Core authority")

    progress.advance("preview-confirm")
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
    typed_selections["talent"] = tap_enabled_authority_option(
        device,
        "creation-prerequisite-talent-option-",
        SKILL_GROUP_TALENT_LABEL,
        max_scrolls=40,
        scan_observer=progress.record_scan,
    )
    device.wait("creation-prerequisite-talent-grant-page", timeout=45)
    skill_group_grant = choose_and_prove_talent_grant(
        device,
        "Skill groups",
        typed_selections["talent"],
        scan_observer=progress.record_scan,
        scan_id_prefix="talent-skill-group-grant",
    )
    skill_group_selected_option_ids = tuple(
        skill_group_grant["selectedOptionAutomationIds"]
    )
    complete_talent_grant_to_prerequisite(device)
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
