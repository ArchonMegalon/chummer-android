#!/usr/bin/env python3
"""Exercise native runner editing on an already-booted API 36 emulator."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


PACKAGE = "com.myexternalbrain.chummer"
MAIN_ACTION = "android.intent.action.MAIN"
LAUNCHER_CATEGORY = "android.intent.category.LAUNCHER"
E2E_AUTHORITY_EXTRA = "com.myexternalbrain.chummer.extra.E2E_AUTHORITY"
WORKSPACE_AUTHORITY_RESOURCE_IDS = (
    "home-e2e-workspace-id",
    "home-e2e-content-revision",
    "home-e2e-saved-revision",
    "home-e2e-payload-sha256",
    "home-e2e-document-sha256",
)
PHONE_SHELL_DESTINATION_IDS = (
    "phone-destination-runners",
    "phone-destination-runner",
    "phone-destination-archive",
    "phone-destination-more",
)
PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE = {
    "en": ("Runners", "Runner", "Stories", "More"),
    "de": ("Runner", "Runner", "Geschichten", "Mehr"),
    "es": ("Runners", "Runner", "Historias", "Más"),
}
SUPPORTED_PHONE_UI_LANGUAGES = ("en", "de", "es")
PHONE_UI_LOCALE_PROPERTIES = ("persist.sys.locale", "ro.product.locale")
PHONE_UI_LOCALE_EVIDENCE_SCHEMA = "chummer.android.phone-ui-locale-evidence/v1"
# Backward-compatible conceptual labels for evidence and callers. Native tab binding below
# accepts only one complete supported-language tuple and binds route identity by position.
PHONE_SHELL_DESTINATION_LABELS = PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE["en"]
PHONE_SHELL_DESTINATION_MAPPING = {
    "phone-destination-runners": "Runners",
    "phone-destination-runner": "Runner",
    "phone-destination-archive": "Stories",
    "phone-destination-more": "More",
}
PHONE_SHELL_FORBIDDEN_DESTINATION_LABELS = ("Play", "Table", "Campaign")
PHONE_SHELL_FORBIDDEN_SUPPORT_LABELS = (
    "Rook",
    "Ask Rook",
    "Tough Tongue",
    "Open Tough Tongue support",
    "All actions",
)
PHONE_SHELL_FORBIDDEN_ROUTE_RESOURCE_IDS = (
    "phone-play-unavailable",
    "phone-table-unavailable",
    "creation-rook-conversation",
)
PHONE_SHELL_FORBIDDEN_LAUNCHER_RESOURCE_IDS = (
    "rook-launch",
    "more-all-actions",
    "live-support",
    "avatar-support",
)
PHONE_SHELL_FORBIDDEN_LAUNCHER_ID_PREFIXES = (
    "build-ghost-",
    "build_ghost_",
    "buildghost-",
    "rook-",
    "rook_",
    "tough-tongue-",
    "tough_tongue_",
    "toughtongue-",
)
BOUNDS = re.compile(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]")
DISPLAY_SIZE = re.compile(r"(?:Physical|Override) size:\s*(\d+)x(\d+)")
COMPONENT = re.compile(
    r"(?P<package>[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)/"
    r"(?P<activity>\.?[A-Za-z0-9_$]+(?:\.[A-Za-z0-9_$]+)*)"
)
PROCESS_ID = re.compile(r"[1-9][0-9]*")
SHA256_TEXT = re.compile(r"[0-9a-f]{64}")
CANONICAL_COLLECTION_ITEM_RESOURCE_ID = re.compile(
    r"^collection-item-(?P<kind>[a-z0-9-]+)-"
    r"(?P<item_id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$"
)
BODY_TOTAL_DESCRIPTION = re.compile(
    r"^Body\.\s+(?:Selected\s+·\s+)?(?P<total>[0-9]+)(?:\s+·|$)"
)
MAX_LAUNCH_EVIDENCE_CHARACTERS = 1_000_000
ADB_TRANSPORT_EVENT_SCHEMA = "chummer.android.adb-transport-event/v1"
ADB_TRANSPORT_PREFLIGHT_SCHEMA = "chummer.android.adb-transport-preflight/v1"
ADB_TRANSPORT_SUMMARY_SCHEMA = "chummer.android.adb-transport-summary/v1"
DURABLE_SAVE_OUTCOME_FAILURE_SCHEMA = (
    "chummer.android.durable-save-outcome-failure/v1"
)
ADB_READ_ONLY_MAX_ATTEMPTS = 3
ADB_READ_ONLY_RETRY_DELAY_SECONDS = 1.0
ADB_SWIPE_RECONCILIATION_REQUIRED_CONSECUTIVE = 2
ADB_SWIPE_RECONCILIATION_MAX_OBSERVATIONS = 3
ADB_SWIPE_RECONCILIATION_DELAY_SECONDS = 0.5
ADB_READ_ONLY_HIERARCHY_ARGUMENTS = (
    "exec-out",
    "uiautomator",
    "dump",
    "--compressed",
    "/dev/tty",
)
DOCUMENTS_UI_PACKAGE = "com.google.android.documentsui"
DOCUMENTS_UI_DRAWER_MARKER = "Open from"
DOCUMENTS_UI_DOWNLOADS_ROOT = "Downloads"
DOCUMENTS_UI_DOWNLOADS_DESTINATION = "Files in Downloads"
DOCUMENTS_UI_MAX_DOWNLOADS_TAPS = 3
DOCUMENTS_UI_DOWNLOADS_RETRY_SETTLE_SECONDS = 2.25
DOCUMENTS_UI_POLL_DELAY_SECONDS = 0.75
ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS = (
    "logcat",
    "-d",
    "-t",
    "50",
    "-s",
    "ChummerBootstrap:I",
    "*:S",
)
ADB_CREATION_BOOTSTRAP_LOGCAT_CLEAR_ARGUMENTS = ("logcat", "-c")
ADB_PREFLIGHT_REQUIRED_CONSECUTIVE = 3
ADB_PREFLIGHT_MAX_OBSERVATIONS = 7
ADB_PREFLIGHT_OBSERVATION_DELAY_SECONDS = 1.0
MAX_ADB_TRANSPORT_EVENTS = 64
MAX_ADB_FAILURE_DETAIL_CHARACTERS = 4000
SAFE_READ_ONLY_REMOTE_PATH = re.compile(r"^/[A-Za-z0-9._/:-]{1,511}$")
SAFE_ANDROID_PROPERTY = re.compile(r"^[A-Za-z0-9._-]{1,255}$")


def _bounded_adb_detail(value: object) -> str:
    if value is None:
        return ""
    if isinstance(value, bytes):
        rendered = value.decode("utf-8", errors="replace")
    else:
        rendered = str(value)
    return rendered[:MAX_ADB_FAILURE_DETAIL_CHARACTERS]


def _write_new_json_receipt(path: Path, receipt: dict[str, object]) -> None:
    """Create one fresh receipt durably; stale/racing paths fail closed."""
    encoded = json.dumps(receipt, indent=2) + "\n"
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
        stream.write(encoded)
        stream.flush()
        os.fsync(stream.fileno())


def _adb_arguments_sha256(arguments: tuple[str, ...]) -> str:
    return hashlib.sha256("\0".join(arguments).encode("utf-8")).hexdigest()


def _adb_arguments_evidence(
    arguments: tuple[str, ...],
    command_policy: str,
) -> list[str]:
    if command_policy == "read-only-retryable":
        return list(arguments)
    visible = 2 if arguments[:1] == ("shell",) else 1
    prefix = list(arguments[:visible])
    hidden = len(arguments) - len(prefix)
    if hidden > 0:
        prefix.append(f"<{hidden} redacted argument(s)>")
    return prefix


def _adb_failure_detail(error: BaseException) -> str:
    return "\n".join(
        part
        for part in (
            _bounded_adb_detail(getattr(error, "stdout", "")),
            _bounded_adb_detail(getattr(error, "stderr", "")),
            _bounded_adb_detail(error),
        )
        if part
    )


def classify_adb_failure(error: BaseException) -> tuple[str, bool]:
    """Classify only transport failures that are safe to recognize mechanically.

    The boolean says whether a fresh *read-only* observation may be attempted.
    It never authorizes replay of the command that failed.
    """
    if isinstance(error, subprocess.TimeoutExpired):
        return ("timeout-unknown-outcome", True)
    detail = _adb_failure_detail(error).casefold()
    if re.search(r"device\s+['\"][^'\"]+['\"]\s+not found", detail):
        return ("device-missing", True)
    classifications = (
        ("device-offline", True, ("device offline", "device is offline")),
        (
            "device-missing",
            True,
            (
                "device not found",
                "no devices/emulators found",
                "no device found",
                "device disconnected",
            ),
        ),
        (
            "transport-closed",
            True,
            (
                "transport is closed",
                "transport closed",
                "error: closed",
                "connection reset by peer",
                "connection aborted",
                "broken pipe",
                "failed to read response from server",
                "failed to read command",
                "protocol fault",
                "connection refused",
                "failed to connect to",
            ),
        ),
        (
            "daemon-unavailable",
            True,
            (
                "cannot connect to daemon",
                "failed to start daemon",
                "server is out of date",
            ),
        ),
        (
            "device-unauthorized",
            False,
            ("device unauthorized", "device is unauthorized"),
        ),
    )
    for classification, retryable_read_only, markers in classifications:
        if any(marker in detail for marker in markers):
            return (classification, retryable_read_only)
    return ("unclassified-adb-failure", False)


def adb_classification_authority(classification: str) -> str:
    if classification in {
        "device-offline",
        "device-missing",
        "transport-closed",
        "daemon-unavailable",
    }:
        return "recognized-transient-transport-marker"
    if classification == "device-unauthorized":
        return "recognized-nonretryable-transport-marker"
    if classification == "timeout-unknown-outcome":
        return "timeout-with-unknown-command-outcome"
    return "unclassified-fail-closed"


def adb_command_retry_policy(arguments: tuple[str, ...]) -> tuple[str, str]:
    """Return a fail-closed replay policy for one exact ADB argument vector."""
    if arguments == ("get-state",):
        return ("read-only-retryable", "exact adb transport-state observation")
    if arguments == ("exec-out", "screencap", "-p"):
        return ("read-only-retryable", "exact framebuffer observation")
    if arguments == ADB_READ_ONLY_HIERARCHY_ARGUMENTS:
        return (
            "read-only-retryable",
            "exact accessibility-hierarchy observation without app mutation",
        )
    if (
        len(arguments) == 3
        and arguments[:2] == ("exec-out", "cat")
        and SAFE_READ_ONLY_REMOTE_PATH.fullmatch(arguments[2]) is not None
    ):
        return ("read-only-retryable", "exact remote-file byte observation")
    if arguments == ("logcat", "-d", "-t", "500"):
        return ("read-only-retryable", "bounded logcat dump observation")
    if arguments == ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS:
        return (
            "read-only-retryable",
            "bounded exact-tag creation-bootstrap timing observation",
        )
    if arguments[:1] != ("shell",):
        return (
            "non-replayable",
            "install/push/pull/unknown adb operations are never replayed",
        )

    shell_arguments = arguments[1:]
    if (
        len(shell_arguments) == 2
        and shell_arguments[0] == "getprop"
        and SAFE_ANDROID_PROPERTY.fullmatch(shell_arguments[1]) is not None
    ):
        return ("read-only-retryable", "exact Android property observation")
    if shell_arguments == ("wm", "size"):
        return ("read-only-retryable", "exact display-size observation")
    if shell_arguments == ("pidof", PACKAGE):
        return ("read-only-retryable", "exact package process-id observation")
    if (
        len(shell_arguments) == 2
        and shell_arguments[0] in {"cat", "sha256sum"}
        and SAFE_READ_ONLY_REMOTE_PATH.fullmatch(shell_arguments[1]) is not None
    ):
        return ("read-only-retryable", "exact remote-file observation")
    if (
        len(shell_arguments) == 4
        and shell_arguments[:3] == ("test", "!", "-e")
        and SAFE_READ_ONLY_REMOTE_PATH.fullmatch(shell_arguments[3]) is not None
    ):
        return ("read-only-retryable", "exact remote-path absence observation")
    read_only_dumpsys = (
        ("dumpsys", "input_method"),
        ("dumpsys", "activity", "activities"),
        ("dumpsys", "activity", "lastanr"),
        ("dumpsys", "activity", "processes"),
        ("dumpsys", "activity", "exit-info", PACKAGE),
        ("dumpsys", "window", "windows"),
    )
    if shell_arguments in read_only_dumpsys:
        return ("read-only-retryable", "exact dumpsys observation")
    if shell_arguments == ("ls", "-la", "/data/anr"):
        return ("read-only-retryable", "exact ANR-directory observation")
    if shell_arguments == (
        "logcat",
        "-d",
        "-b",
        "all",
        "-v",
        "threadtime",
        "-t",
        "4000",
    ):
        return ("read-only-retryable", "bounded logcat dump observation")
    return (
        "non-replayable",
        "shell mutation or ambiguous shell command is never replayed",
    )


class AdbTransportError(RuntimeError):
    """Fail-closed ADB command-outcome error with its exact evidence receipt."""

    def __init__(self, receipt: dict[str, object], evidence_path: Path) -> None:
        self.receipt = receipt
        self.evidence_path = evidence_path
        classification = receipt["classification"]
        policy = receipt["commandPolicy"]
        replay = receipt["replay"]
        super().__init__(
            f"ADB command outcome classified as {classification!r} under {policy!r}; "
            f"automaticReplayPerformed={replay['performed']!r}, "
            f"replaySuppressed={replay['suppressed']!r}; evidence={evidence_path}"
        )


class AdbOperationDeadlineExceeded(RuntimeError):
    """Raised before an ADB invocation when its caller-owned deadline expired."""


def _remaining_operation_timeout(
    *,
    deadline: float | None,
    maximum: float,
) -> float:
    if deadline is None:
        return maximum
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise AdbOperationDeadlineExceeded(
            "ADB operation deadline expired before command invocation"
        )
    return min(maximum, remaining)


class AdbTransportPreflightError(RuntimeError):
    """Raised before any mutation when the transport cannot remain stable."""

    def __init__(self, receipt: dict[str, object], evidence_path: Path) -> None:
        self.receipt = receipt
        self.evidence_path = evidence_path
        super().__init__(
            "ADB transport preflight did not reach the required consecutive "
            f"API-36 observations; evidence={evidence_path}"
        )


@dataclass(frozen=True)
class UiNode:
    attributes: dict[str, str]

    @property
    def bounds(self) -> tuple[int, int, int, int]:
        match = BOUNDS.fullmatch(self.attributes.get("bounds", ""))
        if match is None:
            raise RuntimeError(f"Node has no tappable bounds: {self.attributes}")
        return tuple(int(value) for value in match.groups())

    @property
    def center(self) -> tuple[int, int]:
        left, top, right, bottom = self.bounds
        return ((left + right) // 2, (top + bottom) // 2)


@dataclass(frozen=True)
class LaunchState:
    process_ids: tuple[str, ...]
    resumed_component: str | None
    activity_dump: str


@dataclass(frozen=True)
class ProcessRestartProof:
    before_force_stop: LaunchState
    after_force_stop: LaunchState
    restarted: LaunchState


@dataclass(frozen=True)
class WorkspaceAuthority:
    workspace_id: str
    content_revision: int
    saved_revision: int
    payload_sha256: str
    document_sha256: str


@dataclass(frozen=True)
class FullEditingFixtureContract:
    initial_body_total: int
    improved_body_total: int
    improvement_cost: int
    initial_karma: int
    remaining_karma: int
    next_improvement_cost: int


@dataclass(frozen=True)
class PhoneUiLocaleBinding:
    locale_tag: str
    language: str
    authority_property: str


def supported_phone_ui_language(locale_tag: str) -> str:
    """Resolve one exact DE/EN/ES phone language or fail closed.

    Physical localization proof never silently treats an unsupported/empty
    device locale as English.  Regional tags and Android's underscore form are
    accepted, but only their canonical primary language is used for UI labels.
    """
    normalized = locale_tag.strip().replace("_", "-")
    if re.fullmatch(r"[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*", normalized) is None:
        raise RuntimeError(f"Phone UI locale is not a canonical locale tag: {locale_tag!r}")
    language = normalized.split("-", 1)[0].casefold()
    if language not in SUPPORTED_PHONE_UI_LANGUAGES:
        raise RuntimeError(
            f"Phone UI locale {locale_tag!r} is outside the exact DE/EN/ES proof set"
        )
    return language


def resolve_localized_ui_labels(
    *,
    contract_id: str,
    locale_tag: str,
    observed_labels: tuple[str, ...],
    labels_by_language: dict[str, tuple[str, ...]],
) -> dict[str, object]:
    """Bind one ordered label surface to exactly one detected phone language."""
    if not contract_id.strip():
        raise ValueError("A localized UI label contract requires a stable contract id")
    if tuple(labels_by_language) != SUPPORTED_PHONE_UI_LANGUAGES:
        raise RuntimeError(
            f"Localized UI contract {contract_id!r} must define ordered en/de/es labels"
        )
    language = supported_phone_ui_language(locale_tag)
    arities = {len(labels) for labels in labels_by_language.values()}
    if len(arities) != 1 or not arities or next(iter(arities)) == 0:
        raise RuntimeError(
            f"Localized UI contract {contract_id!r} has inconsistent/empty label tuples"
        )
    if any(not label.strip() for labels in labels_by_language.values() for label in labels):
        raise RuntimeError(f"Localized UI contract {contract_id!r} has a blank label")
    matching_languages = tuple(
        candidate_language
        for candidate_language, expected in labels_by_language.items()
        if observed_labels == expected
    )
    if matching_languages != (language,):
        raise RuntimeError(
            f"Localized UI contract {contract_id!r} observed {observed_labels!r} for "
            f"phone locale {locale_tag!r}; expected exactly {labels_by_language[language]!r} "
            f"and one unambiguous language, matched={matching_languages!r}"
        )
    return {
        "contractId": contract_id,
        "localeTag": locale_tag,
        "language": language,
        "observedLabels": list(observed_labels),
        "expectedLabels": list(labels_by_language[language]),
        "matchingLanguages": list(matching_languages),
    }


class ProductAnrDetected(RuntimeError):
    """Raised when Android reports that the Chummer process is not responding."""


class Device:
    def __init__(self, adb: Path, serial: str, evidence: Path) -> None:
        self.adb = adb
        self.serial = serial
        self.evidence = evidence
        self._display_size: tuple[int, int] | None = None
        self._transport_event_index = 0
        self._transport_events: list[dict[str, object]] = []
        self._transport_preflight: dict[str, object] | None = None
        self._mutation_blocker: dict[str, object] | None = None
        self._phone_ui_locale_binding: PhoneUiLocaleBinding | None = None
        self.evidence.mkdir(parents=True, exist_ok=True)
        stale_transport_evidence = [
            path
            for path in (
                self.evidence / "adb-transport-preflight.json",
                *(
                    self.evidence / f"adb-transport-event-{index:04d}.json"
                    for index in range(1, MAX_ADB_TRANSPORT_EVENTS + 1)
                ),
            )
            if path.exists() or path.is_symlink()
        ]
        if stale_transport_evidence:
            raise RuntimeError(
                "ADB transport evidence target contains stale receipts; use a fresh "
                f"evidence directory: {[str(path) for path in stale_transport_evidence]!r}"
            )

    def _invoke_once(
        self,
        arguments: tuple[str, ...],
        *,
        timeout: float,
        text: bool,
        check: bool,
    ) -> subprocess.CompletedProcess:
        command = [str(self.adb), "-s", self.serial, *arguments]
        return subprocess.run(
            command,
            check=check,
            capture_output=True,
            text=text,
            timeout=timeout,
        )

    def _write_transport_event(
        self,
        *,
        arguments: tuple[str, ...],
        command_policy: str,
        policy_reason: str,
        classification: str,
        retryable_classification: bool,
        attempt: int,
        maximum_attempts: int,
        status: str,
        error: BaseException,
        replay_performed: bool,
        replay_suppressed: bool,
        command_invocation_performed: bool = True,
        blocked_by: dict[str, object] | None = None,
    ) -> tuple[dict[str, object], Path]:
        if self._transport_event_index >= MAX_ADB_TRANSPORT_EVENTS:
            raise RuntimeError(
                "ADB transport event bound exhausted; refusing further device commands"
            ) from error
        self._transport_event_index += 1
        filename = f"adb-transport-event-{self._transport_event_index:04d}.json"
        receipt: dict[str, object] = {
            "schema": ADB_TRANSPORT_EVENT_SCHEMA,
            "status": status,
            "serial": self.serial,
            "classification": classification,
            "classificationAuthority": adb_classification_authority(classification),
            "retryableTransportClassification": retryable_classification,
            "commandPolicy": command_policy,
            "policyReason": policy_reason,
            "adbArguments": _adb_arguments_evidence(arguments, command_policy),
            "adbArgumentsSha256": _adb_arguments_sha256(arguments),
            "attempt": attempt,
            "maximumAttempts": maximum_attempts,
            "commandInvocationPerformed": command_invocation_performed,
            "outcomeMutationAuthority": (
                "none-read-only-command"
                if command_policy == "read-only-retryable"
                else "unknown-fail-closed"
            ),
            "replay": {
                "eligible": (
                    command_policy == "read-only-retryable"
                    and retryable_classification
                ),
                "performed": replay_performed,
                "scheduled": status == "retrying-read-only",
                "suppressed": replay_suppressed,
            },
            "failure": {
                "type": type(error).__name__,
                "returnCode": getattr(error, "returncode", None),
                "stdout": _bounded_adb_detail(getattr(error, "stdout", "")),
                "stderr": _bounded_adb_detail(getattr(error, "stderr", "")),
            },
            "evidenceFile": filename,
        }
        if blocked_by is not None:
            receipt["blockedBy"] = blocked_by
        path = self.evidence / filename
        _write_new_json_receipt(path, receipt)
        self._transport_events.append(receipt)
        return receipt, path

    def _write_recovered_transport_event(
        self,
        *,
        arguments: tuple[str, ...],
        attempts: int,
    ) -> None:
        if self._transport_event_index >= MAX_ADB_TRANSPORT_EVENTS:
            raise RuntimeError(
                "ADB transport event bound exhausted; refusing further device commands"
            )
        self._transport_event_index += 1
        filename = f"adb-transport-event-{self._transport_event_index:04d}.json"
        receipt: dict[str, object] = {
            "schema": ADB_TRANSPORT_EVENT_SCHEMA,
            "status": "recovered-read-only",
            "serial": self.serial,
            "classification": "transport-recovered",
            "classificationAuthority": "fresh-read-only-command-succeeded",
            "retryableTransportClassification": True,
            "commandPolicy": "read-only-retryable",
            "policyReason": adb_command_retry_policy(arguments)[1],
            "adbArguments": list(arguments),
            "adbArgumentsSha256": _adb_arguments_sha256(arguments),
            "attempt": attempts,
            "maximumAttempts": ADB_READ_ONLY_MAX_ATTEMPTS,
            "commandInvocationPerformed": True,
            "outcomeMutationAuthority": "none-read-only-command",
            "replay": {
                "eligible": True,
                "performed": True,
                "scheduled": False,
                "suppressed": False,
            },
            "failure": None,
            "evidenceFile": filename,
        }
        _write_new_json_receipt(self.evidence / filename, receipt)
        self._transport_events.append(receipt)

    def _write_swipe_reconciliation_event(
        self,
        *,
        failed: AdbTransportError,
        arguments: tuple[str, ...],
        observation_count: int,
        hierarchy_sha256: str,
    ) -> None:
        if self._transport_event_index >= MAX_ADB_TRANSPORT_EVENTS:
            raise RuntimeError(
                "ADB transport event bound exhausted; refusing swipe reconciliation"
            )
        self._transport_event_index += 1
        filename = f"adb-transport-event-{self._transport_event_index:04d}.json"
        receipt: dict[str, object] = {
            "schema": ADB_TRANSPORT_EVENT_SCHEMA,
            "status": "reconciled-unknown-swipe",
            "serial": self.serial,
            "classification": "timeout-unknown-outcome",
            "classificationAuthority": (
                "bounded-consecutive-read-only-hierarchy-observations"
            ),
            "retryableTransportClassification": True,
            "commandPolicy": "non-replayable",
            "policyReason": "swipe was never replayed; current viewport became authority",
            "adbArguments": _adb_arguments_evidence(arguments, "non-replayable"),
            "adbArgumentsSha256": _adb_arguments_sha256(arguments),
            "attempt": 1,
            "maximumAttempts": 1,
            "commandInvocationPerformed": False,
            "outcomeMutationAuthority": "current-viewport-observed-no-replay",
            "replay": {
                "eligible": False,
                "performed": False,
                "scheduled": False,
                "suppressed": True,
            },
            "failure": None,
            "reconcilesEvidenceFile": failed.receipt["evidenceFile"],
            "readOnlyObservation": {
                "arguments": list(ADB_READ_ONLY_HIERARCHY_ARGUMENTS),
                "consecutiveMatching": ADB_SWIPE_RECONCILIATION_REQUIRED_CONSECUTIVE,
                "observationsPerformed": observation_count,
                "hierarchySha256": hierarchy_sha256,
            },
            "evidenceFile": filename,
        }
        _write_new_json_receipt(self.evidence / filename, receipt)
        self._transport_events.append(receipt)

    def run(
        self,
        *arguments: str,
        timeout: float = 120,
        text: bool = True,
        check: bool = True,
        deadline: float | None = None,
    ) -> subprocess.CompletedProcess:
        adb_arguments = tuple(arguments)
        command_policy, policy_reason = adb_command_retry_policy(adb_arguments)
        package_process_observation = adb_arguments == ("shell", "pidof", PACKAGE)
        if command_policy != "read-only-retryable" and self._mutation_blocker is not None:
            blocker = dict(self._mutation_blocker)
            suppression = RuntimeError(
                "A prior mutating or ambiguous command has an unknown outcome; "
                "further mutations are prohibited"
            )
            receipt, path = self._write_transport_event(
                arguments=adb_arguments,
                command_policy=command_policy,
                policy_reason=policy_reason,
                classification="prior-mutation-outcome-unknown",
                retryable_classification=False,
                attempt=0,
                maximum_attempts=1,
                status="fail",
                error=suppression,
                replay_performed=False,
                replay_suppressed=True,
                command_invocation_performed=False,
                blocked_by=blocker,
            )
            raise AdbTransportError(receipt, path) from suppression
        maximum_attempts = (
            ADB_READ_ONLY_MAX_ATTEMPTS
            if command_policy == "read-only-retryable"
            else 1
        )
        for attempt in range(1, maximum_attempts + 1):
            try:
                result = self._invoke_once(
                    adb_arguments,
                    timeout=_remaining_operation_timeout(
                        deadline=deadline,
                        maximum=timeout,
                    ),
                    text=text,
                    # Android pidof uses exit 1 with no output for the exact,
                    # expected observation "this package has no process".  Run
                    # only that command unchecked so its result can be bound
                    # below; every other command retains subprocess check=True.
                    check=check and not package_process_observation,
                )
                if package_process_observation and check and result.returncode != 0:
                    process_absent = (
                        result.returncode == 1
                        and not _bounded_adb_detail(result.stdout).strip()
                        and not _bounded_adb_detail(result.stderr).strip()
                    )
                    if not process_absent:
                        raise subprocess.CalledProcessError(
                            result.returncode,
                            result.args,
                            output=result.stdout,
                            stderr=result.stderr,
                        )
                if adb_arguments == ("get-state",):
                    observed_state = _bounded_adb_detail(result.stdout).strip()
                    if observed_state != "device":
                        raise subprocess.CalledProcessError(
                            result.returncode or 1,
                            result.args,
                            output=result.stdout,
                            stderr=f"device {observed_state or 'missing'}",
                        )
                if not check and result.returncode != 0:
                    synthetic_error = subprocess.CalledProcessError(
                        result.returncode,
                        result.args,
                        output=result.stdout,
                        stderr=result.stderr,
                    )
                    classification, _retryable = classify_adb_failure(synthetic_error)
                    if (
                        command_policy != "read-only-retryable"
                        or classification != "unclassified-adb-failure"
                    ):
                        raise synthetic_error
                if attempt > 1:
                    self._write_recovered_transport_event(
                        arguments=adb_arguments,
                        attempts=attempt,
                    )
                return result
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                classification, retryable = classify_adb_failure(error)
                may_retry = (
                    command_policy == "read-only-retryable"
                    and retryable
                    and attempt < maximum_attempts
                    and (deadline is None or time.monotonic() < deadline)
                )
                receipt, path = self._write_transport_event(
                    arguments=adb_arguments,
                    command_policy=command_policy,
                    policy_reason=policy_reason,
                    classification=classification,
                    retryable_classification=retryable,
                    attempt=attempt,
                    maximum_attempts=maximum_attempts,
                    status="retrying-read-only" if may_retry else "fail",
                    error=error,
                    replay_performed=attempt > 1,
                    replay_suppressed=not may_retry,
                )
                if command_policy != "read-only-retryable":
                    self._mutation_blocker = {
                        "classification": classification,
                        "adbArgumentsSha256": receipt["adbArgumentsSha256"],
                        "evidenceFile": receipt["evidenceFile"],
                    }
                if not may_retry:
                    raise AdbTransportError(receipt, path) from error
                retry_delay = ADB_READ_ONLY_RETRY_DELAY_SECONDS
                if deadline is not None:
                    retry_delay = min(
                        retry_delay,
                        max(0.0, deadline - time.monotonic()),
                    )
                if retry_delay <= 0:
                    raise AdbTransportError(receipt, path) from error
                time.sleep(retry_delay)
        raise AssertionError("bounded ADB retry loop exhausted without a terminal result")

    def require_transport_stability(
        self,
        *,
        expected_api_level: str = "36",
        required_consecutive: int = ADB_PREFLIGHT_REQUIRED_CONSECUTIVE,
        max_observations: int = ADB_PREFLIGHT_MAX_OBSERVATIONS,
        delay_seconds: float = ADB_PREFLIGHT_OBSERVATION_DELAY_SECONDS,
    ) -> dict[str, object]:
        """Require consecutive fresh ADB+shell observations before any mutation."""
        if required_consecutive < 2:
            raise ValueError("ADB preflight requires at least two consecutive observations")
        if max_observations < required_consecutive:
            raise ValueError("ADB preflight observation bound is too small")
        observations: list[dict[str, object]] = []
        consecutive = 0
        terminal_error: BaseException | None = None
        for index in range(1, max_observations + 1):
            observation: dict[str, object] = {"index": index}
            try:
                state = self._invoke_once(
                    ("get-state",),
                    timeout=15,
                    text=True,
                    check=True,
                ).stdout.strip()
                api_level = self._invoke_once(
                    ("shell", "getprop", "ro.build.version.sdk"),
                    timeout=15,
                    text=True,
                    check=True,
                ).stdout.strip()
                observation.update(
                    {
                        "status": "stable",
                        "getState": state,
                        "apiLevel": api_level,
                    }
                )
                if state != "device":
                    raise RuntimeError(f"unexpected adb get-state value {state!r}")
                if api_level != expected_api_level:
                    raise RuntimeError(
                        f"unexpected Android API level {api_level!r}; "
                        f"expected {expected_api_level!r}"
                    )
                consecutive += 1
                observations.append(observation)
                if consecutive >= required_consecutive:
                    receipt: dict[str, object] = {
                        "schema": ADB_TRANSPORT_PREFLIGHT_SCHEMA,
                        "status": "pass",
                        "serial": self.serial,
                        "expectedApiLevel": expected_api_level,
                        "requiredConsecutiveObservations": required_consecutive,
                        "maximumObservations": max_observations,
                        "observationDelaySeconds": delay_seconds,
                        "observationsPerformed": index,
                        "consecutiveStableObservations": consecutive,
                        "mutationCommandsIssued": 0,
                        "recoveryPolicy": "bounded-read-only-observation-retry",
                        "recoveryMechanism": (
                            "fresh-adb-invocation-no-reconnect-command"
                        ),
                        "observations": observations,
                    }
                    path = self.evidence / "adb-transport-preflight.json"
                    _write_new_json_receipt(path, receipt)
                    self._transport_preflight = receipt
                    return receipt
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                terminal_error = error
                classification, retryable = classify_adb_failure(error)
                observation.update(
                    {
                        "status": "transport-failure",
                        "classification": classification,
                        "classificationAuthority": adb_classification_authority(
                            classification
                        ),
                        "retryableReadOnlyObservation": retryable,
                        "failure": {
                            "type": type(error).__name__,
                            "returnCode": getattr(error, "returncode", None),
                            "stdout": _bounded_adb_detail(getattr(error, "stdout", "")),
                            "stderr": _bounded_adb_detail(getattr(error, "stderr", "")),
                        },
                    }
                )
                observations.append(observation)
                consecutive = 0
                if not retryable:
                    break
            except RuntimeError as error:
                terminal_error = error
                observation.update(
                    {
                        "status": "invalid-observation",
                        "classification": "preflight-authority-mismatch",
                        "retryableReadOnlyObservation": False,
                        "failure": {
                            "type": type(error).__name__,
                            "message": str(error),
                        },
                    }
                )
                observations.append(observation)
                consecutive = 0
                break
            if index < max_observations:
                time.sleep(delay_seconds)

        receipt = {
            "schema": ADB_TRANSPORT_PREFLIGHT_SCHEMA,
            "status": "fail",
            "serial": self.serial,
            "expectedApiLevel": expected_api_level,
            "requiredConsecutiveObservations": required_consecutive,
            "maximumObservations": max_observations,
            "observationDelaySeconds": delay_seconds,
            "observationsPerformed": len(observations),
            "consecutiveStableObservations": consecutive,
            "mutationCommandsIssued": 0,
            "recoveryPolicy": "bounded-read-only-observation-retry",
            "recoveryMechanism": "fresh-adb-invocation-no-reconnect-command",
            "observations": observations,
        }
        path = self.evidence / "adb-transport-preflight.json"
        _write_new_json_receipt(path, receipt)
        self._transport_preflight = receipt
        failure = AdbTransportPreflightError(receipt, path)
        if terminal_error is None:
            raise failure
        raise failure from terminal_error

    def transport_summary(self) -> dict[str, object]:
        reconciled = {
            event["reconcilesEvidenceFile"]
            for event in self._transport_events
            if event["status"] == "reconciled-unknown-swipe"
        }
        terminal_failures = [
            event
            for event in self._transport_events
            if event["status"] == "fail"
            and event["evidenceFile"] not in reconciled
        ]
        if terminal_failures or (
            self._transport_preflight is not None
            and self._transport_preflight["status"] == "fail"
        ):
            status = "fail"
        elif (
            self._transport_preflight is not None
            and self._transport_preflight["status"] == "pass"
        ):
            status = "pass"
        else:
            status = "not-started"
        return {
            "schema": ADB_TRANSPORT_SUMMARY_SCHEMA,
            "status": status,
            "preflight": self._transport_preflight,
            "eventCount": len(self._transport_events),
            "terminalFailureCount": len(terminal_failures),
            "events": list(self._transport_events),
            "readOnlyMaximumAttempts": ADB_READ_ONLY_MAX_ATTEMPTS,
            "readOnlyRetryDelaySeconds": ADB_READ_ONLY_RETRY_DELAY_SECONDS,
            "preflightObservationDelaySeconds": (
                ADB_PREFLIGHT_OBSERVATION_DELAY_SECONDS
            ),
            "explicitAdbReconnectCommandAllowed": False,
            "nonReplayableCommandMaximumAttempts": 1,
        }

    def install_verified(
        self,
        apk: Path,
        expected_sha256: str,
        *install_arguments: str,
    ) -> None:
        if SHA256_TEXT.fullmatch(expected_sha256) is None:
            raise RuntimeError(f"Invalid expected APK SHA-256: {expected_sha256!r}")
        resolved = apk.resolve()
        before = sha256(resolved)
        if before != expected_sha256:
            raise RuntimeError(
                "APK digest changed before the one-shot install: "
                f"expected {expected_sha256}, got {before}"
            )
        command_error: BaseException | None = None
        try:
            self.run(
                "install",
                *install_arguments,
                str(resolved),
                timeout=300,
            )
        except BaseException as error:
            command_error = error
        after = sha256(resolved)
        if after != expected_sha256:
            raise RuntimeError(
                "APK digest changed across the one-shot install; install outcome is "
                f"not reusable: expected {expected_sha256}, got {after}"
            ) from command_error
        if command_error is not None:
            raise command_error

    def shell(
        self,
        *arguments: str,
        timeout: float = 120,
        deadline: float | None = None,
    ) -> str:
        if deadline is None:
            return self.run("shell", *arguments, timeout=timeout).stdout.strip()
        return self.run(
            "shell",
            *arguments,
            timeout=timeout,
            deadline=deadline,
        ).stdout.strip()

    def push(self, local_path: Path, remote_path: str) -> None:
        self.run("push", str(local_path.resolve()), remote_path, timeout=120)

    def push_verified(
        self,
        local_path: Path,
        remote_path: str,
        expected_sha256: str,
    ) -> str:
        if SHA256_TEXT.fullmatch(expected_sha256) is None:
            raise RuntimeError(f"Invalid expected fixture SHA-256: {expected_sha256!r}")
        resolved = local_path.resolve()
        before = sha256(resolved)
        if before != expected_sha256:
            raise RuntimeError(
                "Fixture digest changed before the one-shot push: "
                f"expected {expected_sha256}, got {before}"
            )
        command_error: BaseException | None = None
        try:
            self.push(resolved, remote_path)
        except BaseException as error:
            command_error = error
        after = sha256(resolved)
        if after != expected_sha256:
            raise RuntimeError(
                "Fixture digest changed across the one-shot push; push outcome is "
                f"not reusable: expected {expected_sha256}, got {after}"
            ) from command_error
        if command_error is not None:
            raise command_error
        output = self.shell("sha256sum", remote_path)
        fields = output.split()
        actual = fields[0].lower() if fields else ""
        if SHA256_TEXT.fullmatch(actual) is None or actual != expected_sha256:
            raise RuntimeError(
                f"Fixture transport digest mismatch for {remote_path!r}: "
                f"expected {expected_sha256}, got {actual or 'unavailable'}"
            )
        return actual

    def hierarchy(self, *, deadline: float | None = None) -> list[UiNode]:
        """Read one hierarchy while sharing an optional caller-owned deadline."""
        try:
            if deadline is None:
                dump_output = self.shell(
                    "uiautomator",
                    "dump",
                    "--compressed",
                    "/sdcard/chummer-editing-window.xml",
                )
            else:
                dump_output = self.shell(
                    "uiautomator",
                    "dump",
                    "--compressed",
                    "/sdcard/chummer-editing-window.xml",
                    timeout=_remaining_operation_timeout(
                        deadline=deadline,
                        maximum=120,
                    ),
                    deadline=deadline,
                )
                _remaining_operation_timeout(deadline=deadline, maximum=120)
            normalized_dump_output = dump_output.lower()
            if not any(
                marker in normalized_dump_output
                for marker in ("hierarchy dumped", "hierchary dumped")
            ):
                (self.evidence / "last-invalid-hierarchy.txt").write_text(
                    dump_output or "uiautomator returned no dump status",
                    encoding="utf-8",
                )
                return []
            if deadline is None:
                xml = self.run(
                    "exec-out", "cat", "/sdcard/chummer-editing-window.xml"
                ).stdout
            else:
                xml = self.run(
                    "exec-out",
                    "cat",
                    "/sdcard/chummer-editing-window.xml",
                    timeout=_remaining_operation_timeout(
                        deadline=deadline,
                        maximum=120,
                    ),
                    deadline=deadline,
                ).stdout
                _remaining_operation_timeout(deadline=deadline, maximum=120)
        except AdbOperationDeadlineExceeded:
            return []
        except subprocess.CalledProcessError as error:
            detail = "\n".join(
                part for part in (str(error), error.stdout, error.stderr) if part
            )
            (self.evidence / "last-invalid-hierarchy.txt").write_text(
                detail,
                encoding="utf-8",
            )
            return []

        return Device._parse_hierarchy(self, xml, "last-invalid-hierarchy.txt")

    def read_only_hierarchy(self) -> list[UiNode]:
        """Observe accessibility state without writing a device-side dump file."""
        xml = self.run(*ADB_READ_ONLY_HIERARCHY_ARGUMENTS, timeout=30).stdout
        return Device._parse_hierarchy(
            self,
            xml,
            "last-read-only-invalid-hierarchy.txt",
        )

    def _parse_hierarchy(self, xml: str, diagnostic_name: str) -> list[UiNode]:
        hierarchy_start = xml.find("<hierarchy")
        if hierarchy_start < 0:
            (self.evidence / diagnostic_name).write_text(
                xml or "uiautomator returned an empty hierarchy",
                encoding="utf-8",
            )
            return []
        hierarchy_end = xml.rfind("</hierarchy>")
        payload = (
            xml[hierarchy_start:hierarchy_end + len("</hierarchy>")]
            if hierarchy_end >= hierarchy_start
            else xml[hierarchy_start:]
        )
        try:
            root = ET.fromstring(payload)
        except ET.ParseError as error:
            (self.evidence / diagnostic_name).write_text(
                f"{error}\n{xml}",
                encoding="utf-8",
            )
            return []
        return [UiNode(dict(node.attrib)) for node in root.iter("node")]

    @staticmethod
    def _hierarchy_sha256(nodes: list[UiNode]) -> str:
        canonical = json.dumps(
            [sorted(node.attributes.items()) for node in nodes],
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        ).encode("utf-8")
        return hashlib.sha256(canonical).hexdigest()

    def _reconcile_unknown_swipe(
        self,
        failed: AdbTransportError,
        arguments: tuple[str, ...],
    ) -> bool:
        receipt = failed.receipt
        blocker = self._mutation_blocker
        if (
            receipt.get("classification") != "timeout-unknown-outcome"
            or receipt.get("commandPolicy") != "non-replayable"
            or receipt.get("adbArgumentsSha256") != _adb_arguments_sha256(arguments)
            or blocker is None
            or blocker.get("evidenceFile") != receipt.get("evidenceFile")
        ):
            return False
        previous_sha256: str | None = None
        consecutive = 0
        for observation in range(1, ADB_SWIPE_RECONCILIATION_MAX_OBSERVATIONS + 1):
            try:
                nodes = self.read_only_hierarchy()
            except AdbTransportError:
                return False
            if not nodes:
                return False
            observed_sha256 = self._hierarchy_sha256(nodes)
            consecutive = consecutive + 1 if observed_sha256 == previous_sha256 else 1
            previous_sha256 = observed_sha256
            if consecutive >= ADB_SWIPE_RECONCILIATION_REQUIRED_CONSECUTIVE:
                self._write_swipe_reconciliation_event(
                    failed=failed,
                    arguments=arguments,
                    observation_count=observation,
                    hierarchy_sha256=observed_sha256,
                )
                self._mutation_blocker = None
                return True
            if observation < ADB_SWIPE_RECONCILIATION_MAX_OBSERVATIONS:
                time.sleep(ADB_SWIPE_RECONCILIATION_DELAY_SECONDS)
        return False

    @staticmethod
    def _matches(node: UiNode, selector: str) -> bool:
        attributes = node.attributes
        resource_id = attributes.get("resource-id", "").rsplit("/", 1)[-1]
        values = {
            attributes.get("text", ""),
            attributes.get("content-desc", ""),
            resource_id,
        }
        return selector in values or any(value.startswith(selector) for value in values if value)

    @staticmethod
    def _scroll_x_ratio(selector: str) -> float:
        if selector.startswith(
            (
                "tablet-build-tab-",
                "tablet-build-action-",
                "tablet-quick-",
                "tablet-origin-dossier",
            )
        ):
            return 0.15
        if selector.startswith(
            (
                "tablet-inspector-",
                "tablet-field-",
                "tablet-contact-",
                "tablet-toggle-",
                "tablet-linked-",
                "tablet-attribute-base-",
                "tablet-attribute-karma-",
                "tablet-attribute-save-",
                "tablet-attribute-improve-",
                "tablet-attribute-burn-",
            )
        ):
            return 0.82
        if selector.startswith("tablet-attribute-"):
            return 0.375
        return 0.5

    def find(self, selector: str, *, field_after_label: str | None = None) -> UiNode | None:
        nodes = self.hierarchy()
        matches = [node for node in nodes if self._matches(node, selector)]
        if matches:
            return next(
                (node for node in matches if node.attributes.get("clickable") == "true"),
                matches[0],
            )

        if field_after_label is not None:
            label_index = next(
                (index for index, node in enumerate(nodes) if node.attributes.get("text") == field_after_label),
                -1,
            )
            if label_index >= 0:
                for node in nodes[label_index + 1 :]:
                    class_name = node.attributes.get("class", "")
                    if node.attributes.get("focusable") == "true" or any(
                        token in class_name for token in ("EditText", "Spinner")
                    ):
                        return node
        return None

    def find_exact_resource_id(self, selector: str) -> UiNode | None:
        matches = [
            node
            for node in self.hierarchy()
            if node.attributes.get("resource-id", "").rsplit("/", 1)[-1] == selector
        ]
        if not matches:
            return None
        return next(
            (node for node in matches if node.attributes.get("clickable") == "true"),
            matches[0],
        )

    def wait_for_single_exact_resource_id(
        self,
        selector: str,
        *,
        timeout: int = 45,
        scroll: bool = False,
        max_scrolls: int = 6,
        scroll_distance_ratio: float = 0.52,
        evidence_prefix: str = "workspace-authority",
        surface_name: str = "Workspace authority accessibility node",
    ) -> UiNode:
        """Return exactly one accessibility node with an exact resource id.

        Exact evidence must never use the driver's permissive text/prefix
        selector. A missing node means the surface was not published; duplicate
        nodes make the rendered proof ambiguous. Both conditions fail closed.
        """
        deadline = time.monotonic() + timeout
        scrolls = 0
        while time.monotonic() < deadline:
            nodes = self.hierarchy()
            if not nodes:
                # A failed/empty UIAutomator dump is not evidence that the target is
                # outside the viewport.  Advancing here can move a short row through
                # the viewport without ever observing it.
                time.sleep(0.75)
                continue
            matches = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(matches) == 1:
                return matches[0]
            if len(matches) > 1:
                self.capture(f"{evidence_prefix}-cardinality-invalid")
                raise RuntimeError(
                    f"{surface_name} "
                    f"{selector!r} has cardinality {len(matches)}; expected exactly one"
                )
            if self.dismiss_system_ui_anr(nodes):
                time.sleep(2)
                continue
            if scroll and scrolls < max_scrolls:
                self.swipe_up(
                    x_ratio=self._scroll_x_ratio(selector),
                    distance_ratio=scroll_distance_ratio,
                )
                scrolls += 1
            time.sleep(0.75)
        self.capture(f"{evidence_prefix}-unavailable")
        raise RuntimeError(
            f"Timed out waiting for exactly one {surface_name.lower()} {selector!r}"
        )

    def wait_for_single_exact_accessibility_value(
        self,
        selector: str,
        *,
        timeout: int = 45,
        evidence_prefix: str,
        surface_name: str,
    ) -> UiNode:
        """Bind one exact resource-id/content-desc value without prefix fallback."""
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            matches = [
                node
                for node in self.hierarchy()
                if selector
                in {
                    node.attributes.get("resource-id", "").rsplit("/", 1)[-1],
                    node.attributes.get("content-desc", ""),
                }
            ]
            if len(matches) == 1:
                return matches[0]
            if len(matches) > 1:
                self.capture(f"{evidence_prefix}-cardinality-invalid")
                raise RuntimeError(
                    f"{surface_name} {selector!r} has cardinality {len(matches)}; "
                    "expected exactly one"
                )
            if self.dismiss_system_ui_anr():
                time.sleep(2)
                continue
            time.sleep(0.75)
        self.capture(f"{evidence_prefix}-unavailable")
        raise RuntimeError(
            f"Timed out waiting for exactly one {surface_name.lower()} {selector!r}"
        )

    def wait_for_single_exact_text(
        self,
        selector: str,
        *,
        timeout: int = 45,
        scroll: bool = False,
        max_scrolls: int = 6,
        scroll_distance_ratio: float = 0.52,
        evidence_prefix: str = "exact-text",
        surface_name: str = "UI text node",
    ) -> UiNode:
        """Bind exactly one text node without prefix or alternate-attribute fallback."""
        deadline = time.monotonic() + timeout
        scrolls = 0
        while time.monotonic() < deadline:
            nodes = self.hierarchy()
            if not nodes:
                time.sleep(0.75)
                continue
            self.dismiss_system_ui_anr(nodes)
            matches = [
                node
                for node in nodes
                if node.attributes.get("text", "") == selector
            ]
            if len(matches) == 1:
                return matches[0]
            if len(matches) > 1:
                self.capture(f"{evidence_prefix}-cardinality-invalid")
                raise RuntimeError(
                    f"{surface_name} {selector!r} has cardinality {len(matches)}; "
                    "expected exactly one"
                )
            if scroll and scrolls < max_scrolls:
                self.swipe_up(
                    x_ratio=self._scroll_x_ratio(selector),
                    distance_ratio=scroll_distance_ratio,
                )
                scrolls += 1
            time.sleep(0.75)
        self.capture(f"{evidence_prefix}-unavailable")
        raise RuntimeError(
            f"Timed out waiting for exactly one {surface_name.lower()} {selector!r}"
        )

    def wait(
        self,
        selector: str,
        *,
        timeout: int = 45,
        scroll: bool = False,
        max_scrolls: int = 6,
        scroll_distance_ratio: float = 0.52,
    ) -> UiNode:
        deadline = time.monotonic() + timeout
        scrolls = 0
        while time.monotonic() < deadline:
            node = self.find(selector)
            if node is not None:
                return node
            if self.dismiss_system_ui_anr():
                time.sleep(5)
                if scroll and scrolls < max_scrolls:
                    self.swipe_up(
                        x_ratio=self._scroll_x_ratio(selector),
                        distance_ratio=scroll_distance_ratio,
                    )
                    scrolls += 1
                    time.sleep(1)
                continue
            if scroll and scrolls < max_scrolls:
                self.swipe_up(
                    x_ratio=self._scroll_x_ratio(selector),
                    distance_ratio=scroll_distance_ratio,
                )
                scrolls += 1
            time.sleep(0.75)
        self.capture("failure")
        raise RuntimeError(f"Timed out waiting for UI node {selector!r}")

    def dismiss_system_ui_anr(self, nodes: list[UiNode] | None = None) -> bool:
        wait_button = (
            self.find("aerr_wait")
            if nodes is None
            else next((node for node in nodes if self._matches(node, "aerr_wait")), None)
        )
        if wait_button is None:
            return False
        self.capture_product_anr_evidence()
        raise ProductAnrDetected(
            "Android reported that Chummer is not responding; captured product-ANR "
            "diagnostics and refused to dismiss the dialog as success"
        )

    def capture_product_anr_evidence(self) -> None:
        """Capture bounded diagnostics without mutating or dismissing the ANR dialog."""
        try:
            screenshot = self.run(
                "exec-out",
                "screencap",
                "-p",
                timeout=15,
                text=False,
            ).stdout
            (self.evidence / "product-anr.png").write_bytes(screenshot)
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
            _write_launch_evidence(self, "product-anr-screenshot-error.txt", error)

        process_ids = tuple(
            token
            for token in _safe_shell(self, "pidof", PACKAGE, timeout=15).split()
            if PROCESS_ID.fullmatch(token)
        )
        _write_launch_evidence(
            self,
            "product-anr-process-ids.txt",
            "\n".join(process_ids) or "process id unavailable",
        )

        diagnostics = (
            (
                "product-anr-lastanr.txt",
                ("dumpsys", "activity", "lastanr"),
            ),
            (
                "product-anr-processes.txt",
                ("dumpsys", "activity", "processes"),
            ),
            (
                "product-anr-exit-info.txt",
                ("dumpsys", "activity", "exit-info", PACKAGE),
            ),
            (
                "product-anr-windows.txt",
                ("dumpsys", "window", "windows"),
            ),
            (
                "product-anr-data-anr.txt",
                ("ls", "-la", "/data/anr"),
            ),
            (
                "product-anr-logcat.txt",
                ("logcat", "-d", "-b", "all", "-v", "threadtime", "-t", "4000"),
            ),
        )
        for name, arguments in diagnostics:
            _write_launch_evidence(
                self,
                name,
                _safe_shell(self, *arguments, timeout=15),
            )

    def tap(
        self,
        selector: str,
        *,
        scroll: bool = False,
        timeout: int = 45,
        max_scrolls: int = 6,
        scroll_distance_ratio: float = 0.52,
        text_leading_offset: int = 0,
        exact_resource_id: bool = False,
    ) -> None:
        deadline = time.monotonic() + timeout
        scrolls = 0
        node = None
        while time.monotonic() < deadline:
            candidate = (
                self.find_exact_resource_id(selector)
                if exact_resource_id
                else self.find(selector)
            )
            if candidate is not None and self.node_has_tappable_bounds(candidate):
                node = candidate
                break
            if self.dismiss_system_ui_anr():
                time.sleep(2)
                continue
            if scroll and scrolls < max_scrolls:
                self.swipe_up(
                    x_ratio=self._scroll_x_ratio(selector),
                    distance_ratio=scroll_distance_ratio,
                )
                scrolls += 1
            time.sleep(0.75)
        if node is None:
            self.capture("failure")
            raise RuntimeError(f"Timed out waiting for tappable UI node {selector!r}")
        x, y = node.center
        if text_leading_offset > 0 and node.attributes.get("text"):
            match = BOUNDS.fullmatch(node.attributes.get("bounds", ""))
            if match is not None:
                x = max(1, int(match.group(1)) - text_leading_offset)
        self.shell("input", "tap", str(x), str(y))

    def tap_single_exact_resource_id(
        self,
        selector: str,
        *,
        timeout: int = 45,
        scroll: bool = False,
        max_scrolls: int = 6,
        scroll_distance_ratio: float = 0.52,
        evidence_prefix: str = "exact-resource-tap",
        surface_name: str = "Exact resource-id control",
    ) -> None:
        """Tap one cardinality-checked resource ID without text/prefix fallback."""
        if scroll:
            node = self.wait_exact_resource_id_bidirectional(
                selector,
                timeout=timeout,
                backward_scrolls=0,
                forward_scrolls=max_scrolls,
                scroll_distance_ratio=scroll_distance_ratio,
                evidence_prefix=evidence_prefix,
                surface_name=surface_name,
                require_tappable=True,
            )
        else:
            node = self.wait_for_single_exact_resource_id(
                selector,
                timeout=timeout,
                evidence_prefix=evidence_prefix,
                surface_name=surface_name,
            )
        if (
            node.attributes.get("enabled") != "true"
            or node.attributes.get("clickable") != "true"
            or not self.node_has_tappable_bounds(node)
        ):
            self.capture(f"{evidence_prefix}-bounds-invalid")
            raise RuntimeError(
                f"{surface_name} {selector!r} is not enabled, clickable, and tappable"
            )
        x, y = node.center
        self.shell("input", "tap", str(x), str(y))

    def wait_exact_resource_id_bidirectional(
        self,
        selector: str,
        *,
        timeout: int = 90,
        backward_scrolls: int = 24,
        forward_scrolls: int = 24,
        scroll_distance_ratio: float = 0.22,
        evidence_prefix: str = "exact-resource-bidirectional",
        surface_name: str = "Exact resource-id control",
        require_tappable: bool = True,
    ) -> UiNode:
        """Reset to the top, then scan one exact ID without blind swipes.

        Empty hierarchy reads are transient acquisition failures, not proof that the
        row is elsewhere.  They never advance the viewport.  Small forward gestures
        keep overlap between observations; if a rendered exact node is clipped above
        the viewport, one bounded reverse gesture recovers it. Interactive callers
        retain the default tappability gate; read-only authority cards can explicitly
        request cardinality-checked visible-node acquisition instead.
        """
        x_ratio = self._scroll_x_ratio(selector)
        for _ in range(backward_scrolls):
            self.swipe_down(
                x_ratio=x_ratio,
                distance_ratio=scroll_distance_ratio,
            )
            time.sleep(0.2)
        if backward_scrolls > 0:
            time.sleep(0.75)

        deadline = time.monotonic() + timeout
        forward = 0
        backtracks = 0
        while time.monotonic() < deadline:
            nodes = self.hierarchy()
            if not nodes:
                time.sleep(0.75)
                continue

            matches = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(matches) > 1:
                self.capture(f"{evidence_prefix}-cardinality-invalid")
                raise RuntimeError(
                    f"{surface_name} {selector!r} has cardinality {len(matches)}; "
                    "expected exactly one"
                )
            if len(matches) == 1:
                node = matches[0]
                visible_bounds = self.node_has_tappable_bounds(node)
                if not require_tappable and visible_bounds:
                    return node
                if (
                    node.attributes.get("enabled") == "true"
                    and node.attributes.get("clickable") == "true"
                    and visible_bounds
                ):
                    return node

                bounds = BOUNDS.fullmatch(node.attributes.get("bounds", ""))
                if bounds is None:
                    self.capture(f"{evidence_prefix}-bounds-invalid")
                    raise RuntimeError(
                        f"{surface_name} {selector!r} exposed invalid bounds"
                    )
                _, top, _, bottom = (int(value) for value in bounds.groups())
                _, height = self.display_size()
                center_y = (top + bottom) // 2
                clipped = bottom - top <= 8
                clipped_above = top < 0 or (clipped and center_y < height // 2)
                clipped_below = (
                    bottom > height
                    or center_y >= height * 0.96
                    or (clipped and center_y >= height // 2)
                )
                if clipped_above and forward > 0 and backtracks < forward_scrolls:
                    self.swipe_down(
                        x_ratio=x_ratio,
                        distance_ratio=scroll_distance_ratio,
                    )
                    forward -= 1
                    backtracks += 1
                    time.sleep(0.75)
                    continue
                if clipped_below and forward < forward_scrolls:
                    self.swipe_up(
                        x_ratio=x_ratio,
                        distance_ratio=scroll_distance_ratio,
                    )
                    forward += 1
                    time.sleep(0.75)
                    continue

                if require_tappable:
                    self.capture(f"{evidence_prefix}-not-tappable")
                    raise RuntimeError(
                        f"{surface_name} {selector!r} was not enabled, clickable, and tappable"
                    )
                self.capture(f"{evidence_prefix}-not-readable")
                raise RuntimeError(
                    f"{surface_name} {selector!r} was not fully visible for read-only acquisition"
                )

            if self.dismiss_system_ui_anr(nodes):
                time.sleep(2)
                continue
            if forward >= forward_scrolls:
                break
            self.swipe_up(
                x_ratio=x_ratio,
                distance_ratio=scroll_distance_ratio,
            )
            forward += 1
            time.sleep(0.75)
        self.capture(f"{evidence_prefix}-unavailable")
        qualifier = "tappable " if require_tappable else "visible "
        raise RuntimeError(
            f"Timed out waiting for exactly one {qualifier}{surface_name.lower()} {selector!r} "
            "after a bounded bidirectional search"
        )

    def tap_exact_resource_id_bidirectional(
        self,
        selector: str,
        *,
        timeout: int = 90,
        backward_scrolls: int = 24,
        forward_scrolls: int = 24,
        scroll_distance_ratio: float = 0.22,
        evidence_prefix: str = "exact-resource-bidirectional-tap",
        surface_name: str = "Exact resource-id control",
    ) -> None:
        """Tap the exact cardinality-checked node from one observed hierarchy."""
        node = self.wait_exact_resource_id_bidirectional(
            selector,
            timeout=timeout,
            backward_scrolls=backward_scrolls,
            forward_scrolls=forward_scrolls,
            scroll_distance_ratio=scroll_distance_ratio,
            evidence_prefix=evidence_prefix,
            surface_name=surface_name,
        )
        x, y = node.center
        self.shell("input", "tap", str(x), str(y))

    def tap_bidirectional(
        self,
        selector: str,
        *,
        timeout: int = 90,
        backward_scrolls: int = 24,
        forward_scrolls: int = 24,
        scroll_distance_ratio: float = 0.22,
        exact_resource_id: bool = False,
    ) -> None:
        """Reset a preserved list position, then scan forward with bounded dumps."""
        # A uiautomator hierarchy dump can take several seconds on the full Build
        # page. Resetting to the known top does not need a dump between gestures;
        # spending the search deadline on those probes can prevent the forward
        # phase from ever starting on a long runner dossier.
        x_ratio = self._scroll_x_ratio(selector)
        for _ in range(backward_scrolls):
            self.swipe_down(
                x_ratio=x_ratio,
                distance_ratio=scroll_distance_ratio,
            )
            time.sleep(0.2)
        if backward_scrolls > 0:
            time.sleep(0.75)

        deadline = time.monotonic() + timeout
        forward = 0
        while time.monotonic() < deadline:
            candidate = (
                self.find_exact_resource_id(selector)
                if exact_resource_id
                else self.find(selector)
            )
            if candidate is not None and self.node_has_tappable_bounds(candidate):
                x, y = candidate.center
                self.shell("input", "tap", str(x), str(y))
                return
            if self.dismiss_system_ui_anr():
                time.sleep(2)
                continue
            if forward < forward_scrolls:
                self.swipe_up(
                    x_ratio=x_ratio,
                    distance_ratio=scroll_distance_ratio,
                )
                forward += 1
            else:
                break
            time.sleep(0.75)
        self.capture("failure")
        raise RuntimeError(
            f"Timed out waiting for tappable UI node {selector!r} "
            "after a bounded bidirectional search"
        )

    def node_has_tappable_bounds(
        self,
        node: UiNode,
        *,
        deadline: float | None = None,
    ) -> bool:
        match = BOUNDS.fullmatch(node.attributes.get("bounds", ""))
        if match is None:
            return False
        left, top, right, bottom = (int(value) for value in match.groups())
        width, height = (
            self.display_size()
            if deadline is None
            else self.display_size(deadline=deadline)
        )
        center_y = (top + bottom) // 2
        return (
            right - left > 8
            and bottom - top > 8
            and 0 <= left < right <= width
            and 0 <= top < bottom <= height
            and center_y < height * 0.96
        )

    def tap_until_visible(
        self,
        selector: str,
        target: str,
        *,
        timeout: int = 45,
        scroll: bool = False,
        max_scrolls: int = 12,
        scroll_distance_ratio: float = 0.22,
    ) -> UiNode:
        deadline = time.monotonic() + timeout
        scrolls = 0
        while time.monotonic() < deadline:
            target_node = self.find(target)
            if target_node is not None:
                return target_node
            if self.dismiss_system_ui_anr():
                time.sleep(2)
                continue
            source_node = self.find(selector)
            if source_node is not None:
                x, y = source_node.center
                self.shell("input", "tap", str(x), str(y))
            elif scroll and scrolls < max_scrolls:
                self.swipe_up(
                    x_ratio=self._scroll_x_ratio(selector),
                    distance_ratio=scroll_distance_ratio,
                )
                scrolls += 1
            time.sleep(1.25)
        self.capture("failure")
        raise RuntimeError(
            f"Timed out waiting for UI node {target!r} after tapping {selector!r}"
        )

    def tap_exact_resource_id_until_exact_resource_id(
        self,
        selector: str,
        target: str,
        *,
        timeout: int = 45,
        evidence_prefix: str = "exact-resource-transition",
        source_name: str = "Exact source control",
        target_name: str = "Exact target control",
        target_scroll_surface: str | None = None,
        max_target_scrolls: int = 0,
        target_scroll_distance_ratio: float = 0.22,
    ) -> UiNode:
        """Open a route without depending on localized visible text.

        Both ends use exact resource-id cardinality.  The source is reacquired
        from the current hierarchy before every tap; a duplicate source/target
        or stale/non-tappable source fails closed.
        """
        deadline = time.monotonic() + timeout
        target_scrolls = 0
        while time.monotonic() < deadline:
            nodes = self.hierarchy()
            if not nodes:
                time.sleep(0.75)
                continue
            targets = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == target
            ]
            if len(targets) == 1:
                return targets[0]
            if len(targets) > 1:
                self.capture(f"{evidence_prefix}-target-cardinality-invalid")
                raise RuntimeError(
                    f"{target_name} {target!r} has cardinality {len(targets)}; expected one"
                )
            scroll_surfaces = (
                [
                    node
                    for node in nodes
                    if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                    == target_scroll_surface
                ]
                if target_scroll_surface is not None
                else []
            )
            if len(scroll_surfaces) > 1:
                self.capture(f"{evidence_prefix}-scroll-surface-cardinality-invalid")
                raise RuntimeError(
                    f"Target scroll surface {target_scroll_surface!r} has cardinality "
                    f"{len(scroll_surfaces)}; expected at most one"
                )
            if len(scroll_surfaces) == 1:
                surface = scroll_surfaces[0]
                if not self.node_has_tappable_bounds(surface):
                    self.capture(f"{evidence_prefix}-scroll-surface-bounds-invalid")
                    raise RuntimeError(
                        f"Target scroll surface {target_scroll_surface!r} did not expose "
                        "exact on-screen bounds"
                    )
                if target_scrolls >= max_target_scrolls:
                    break
                left, _, right, _ = surface.bounds
                display_width, _ = self.display_size()
                self.swipe_up(
                    x_ratio=((left + right) / 2) / display_width,
                    distance_ratio=target_scroll_distance_ratio,
                )
                target_scrolls += 1
                time.sleep(0.75)
                continue
            sources = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(sources) > 1:
                self.capture(f"{evidence_prefix}-source-cardinality-invalid")
                raise RuntimeError(
                    f"{source_name} {selector!r} has cardinality {len(sources)}; expected one"
                )
            if len(sources) == 1:
                source = sources[0]
                if not self.node_has_tappable_bounds(source):
                    self.capture(f"{evidence_prefix}-source-not-tappable")
                    raise RuntimeError(
                        f"{source_name} {selector!r} did not expose exact tappable bounds"
                    )
                self.shell("input", "tap", *(str(value) for value in source.center))
            if self.dismiss_system_ui_anr(nodes):
                time.sleep(2)
                continue
            time.sleep(1.25)
        self.capture(f"{evidence_prefix}-target-unavailable")
        raise RuntimeError(
            f"Timed out waiting for exact {target_name.lower()} {target!r} after "
            f"tapping exact {source_name.lower()} {selector!r}"
        )

    def set_text(
        self,
        selector: str,
        label: str,
        value: str,
        *,
        scroll: bool = False,
        max_scrolls: int = 7,
        scroll_distance_ratio: float = 0.52,
    ) -> None:
        node = None
        attempts = 0
        max_attempts = max_scrolls + 1 if scroll else 1
        while node is None and attempts < max_attempts:
            candidate = self.find(selector, field_after_label=label)
            node = candidate if candidate is not None and self.input_node_is_tappable(candidate) else None
            if node is None and scroll and attempts < max_scrolls:
                self.swipe_up(
                    x_ratio=self._scroll_x_ratio(selector),
                    distance_ratio=scroll_distance_ratio,
                )
                time.sleep(0.75)
            attempts += 1
        if node is None:
            self.capture("missing-field")
            raise RuntimeError(f"Could not find field {selector!r} after {label!r}")
        focused = None
        for _ in range(3):
            x, y = node.center
            self.shell("input", "tap", str(x), str(y))
            time.sleep(0.5)
            focused = self.find(selector)
            if focused is not None and focused.attributes.get("focused") == "true":
                break
            if self.keyboard_visible():
                self.dismiss_keyboard()
            candidate = self.find(selector, field_after_label=label)
            if candidate is not None and self.input_node_is_tappable(candidate):
                node = candidate
        if focused is None or focused.attributes.get("focused") != "true":
            self.capture("field-focus-failed")
            raise RuntimeError(f"Field {selector!r} did not receive focus")
        self.shell("input", "keycombination", "113", "29")
        time.sleep(0.25)
        if value:
            self.shell("input", "text", value.replace(" ", "%s"))
        else:
            self.shell("input", "keyevent", "67")
        time.sleep(0.25)
        updated = self.find(selector)
        if updated is None or updated.attributes.get("text") != value:
            self.capture("field-value-failed")
            actual = None if updated is None else updated.attributes.get("text")
            raise RuntimeError(
                f"Field {selector!r} did not receive {value!r}; rendered {actual!r}"
            )
        self.dismiss_keyboard()

    def input_node_is_tappable(self, node: UiNode) -> bool:
        match = BOUNDS.fullmatch(node.attributes.get("bounds", ""))
        if match is None:
            return False
        left, top, right, bottom = (int(value) for value in match.groups())
        _, height = self.display_size()
        center_y = (top + bottom) // 2
        return right - left > 8 and bottom - top > 8 and 0 <= top < bottom and center_y < height * 0.88

    def keyboard_visible(self) -> bool:
        state = self.shell("dumpsys", "input_method")
        return "mInputShown=true" in state or re.search(
            r"mImeWindowVis=(?:0x)?[1-9a-fA-F]",
            state,
        ) is not None

    def dismiss_keyboard(self) -> None:
        if not self.keyboard_visible():
            return
        self.shell("input", "keyevent", "111")
        time.sleep(0.5)
        if not self.keyboard_visible():
            time.sleep(0.75)
            return
        width, height = self.display_size()
        self.shell(
            "input",
            "tap",
            str(int(round(width * 0.15))),
            str(height - max(24, int(round(height * 0.021)))),
        )
        time.sleep(0.5)
        if self.keyboard_visible():
            self.capture("keyboard-dismiss-failed")
            raise RuntimeError("Android IME dismiss control did not hide the keyboard")
        time.sleep(0.75)

    def assert_text(self, expected: str, *, timeout: int = 10) -> None:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            nodes = self.hierarchy()
            if any(node.attributes.get("text") == expected for node in nodes):
                return
            time.sleep(0.5)
        self.capture("missing-text")
        raise RuntimeError(f"Expected persisted text {expected!r} was not rendered")

    def back(self) -> None:
        node = self.find("Navigate up")
        if node is not None:
            x, y = node.center
            self.shell("input", "tap", str(x), str(y))
            time.sleep(1)
            return
        self.shell("input", "keyevent", "4")
        time.sleep(1)

    def display_size(self, *, deadline: float | None = None) -> tuple[int, int]:
        if self._display_size is None:
            if deadline is None:
                output = self.shell("wm", "size")
            else:
                output = self.shell(
                    "wm",
                    "size",
                    timeout=_remaining_operation_timeout(
                        deadline=deadline,
                        maximum=120,
                    ),
                    deadline=deadline,
                )
            sizes = DISPLAY_SIZE.findall(output)
            self._display_size = (
                (int(sizes[-1][0]), int(sizes[-1][1]))
                if sizes
                else (1080, 2400)
            )
        return self._display_size

    def swipe_up(
        self,
        *,
        x_ratio: float = 0.5,
        distance_ratio: float = 0.52,
    ) -> None:
        width, height = self.display_size()
        x = int(round(width * x_ratio))
        start_y = int(round(height * 0.82))
        end_y = int(round(height * max(0.10, 0.82 - distance_ratio)))
        arguments = (
            "input",
            "swipe",
            str(x),
            str(start_y),
            str(x),
            str(end_y),
            "300",
        )
        try:
            self.shell(*arguments, timeout=15)
        except AdbTransportError as error:
            if not self._reconcile_unknown_swipe(
                error,
                ("shell", *arguments),
            ):
                raise

    def swipe_down(
        self,
        *,
        x_ratio: float = 0.5,
        distance_ratio: float = 0.52,
    ) -> None:
        width, height = self.display_size()
        x = int(round(width * x_ratio))
        start_y = int(round(height * 0.30))
        end_y = int(round(height * min(0.90, 0.30 + distance_ratio)))
        arguments = (
            "input",
            "swipe",
            str(x),
            str(start_y),
            str(x),
            str(end_y),
            "300",
        )
        try:
            self.shell(*arguments, timeout=15)
        except AdbTransportError as error:
            if not self._reconcile_unknown_swipe(
                error,
                ("shell", *arguments),
            ):
                raise

    def open_navigation_drawer(self) -> None:
        for selector in ("Open navigation drawer", "Navigate up", "Show navigation menu"):
            node = self.find(selector)
            if node is not None:
                x, y = node.center
                self.shell("input", "tap", str(x), str(y))
                return
        self.shell("input", "tap", "48", "96")

    def capture(self, name: str, *, deadline: float | None = None) -> None:
        if deadline is not None:
            def deadline_run(
                *arguments: str,
                text: bool = True,
            ) -> subprocess.CompletedProcess | None:
                try:
                    return self.run(
                        *arguments,
                        timeout=_remaining_operation_timeout(
                            deadline=deadline,
                            maximum=120,
                        ),
                        text=text,
                        deadline=deadline,
                    )
                except Exception:
                    # Diagnostic collection is subordinate to the semantic error
                    # which requested it. Never replace that primary failure.
                    return None

            screenshot_result = deadline_run(
                "exec-out",
                "screencap",
                "-p",
                text=False,
            )
            if time.monotonic() >= deadline:
                return
            if screenshot_result is not None:
                try:
                    (self.evidence / f"{name}.png").write_bytes(
                        screenshot_result.stdout
                    )
                except (OSError, TypeError):
                    pass

            hierarchy_result = deadline_run(
                "exec-out",
                "cat",
                "/sdcard/chummer-editing-window.xml",
            )
            if time.monotonic() >= deadline:
                return
            if hierarchy_result is not None:
                try:
                    (self.evidence / f"{name}.xml").write_text(
                        hierarchy_result.stdout,
                        encoding="utf-8",
                    )
                except (OSError, TypeError):
                    pass

            logcat_result = deadline_run("logcat", "-d", "-t", "500")
            if time.monotonic() >= deadline:
                return
            if logcat_result is not None:
                try:
                    (self.evidence / f"{name}-logcat.txt").write_text(
                        logcat_result.stdout,
                        encoding="utf-8",
                    )
                except (OSError, TypeError):
                    pass
            return

        # Preserve the no-deadline diagnostic path and its exact ADB call shape.
        try:
            screenshot = self.run("exec-out", "screencap", "-p", text=False).stdout
            (self.evidence / f"{name}.png").write_bytes(screenshot)
        except subprocess.CalledProcessError as error:
            (self.evidence / f"{name}-screenshot-error.txt").write_text(
                str(error),
                encoding="utf-8",
            )
        try:
            hierarchy = self.run(
                "exec-out", "cat", "/sdcard/chummer-editing-window.xml"
            ).stdout
            (self.evidence / f"{name}.xml").write_text(hierarchy, encoding="utf-8")
        except subprocess.CalledProcessError:
            pass
        try:
            logcat = self.run("logcat", "-d", "-t", "500").stdout
            (self.evidence / f"{name}-logcat.txt").write_text(
                logcat,
                encoding="utf-8",
            )
        except subprocess.CalledProcessError:
            pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_unchanged_local_inputs(
    captured_sha256: dict[Path, str],
    *,
    label: str,
) -> None:
    changed: dict[str, dict[str, str]] = {}
    for path, expected in captured_sha256.items():
        actual = sha256(path)
        if actual != expected:
            changed[str(path)] = {"expected": expected, "actual": actual}
    if changed:
        raise RuntimeError(f"{label} changed during physical proof execution: {changed!r}")


def authorize_remote_cleanup_once(remote: dict[str, object]) -> bool:
    """Authorize one cleanup mutation, never a replay after an unknown outcome."""
    if remote.get("cleanupAttempted") is True:
        remote["cleanupReplaySuppressed"] = True
        return False
    if (
        remote.get("precleanAttempted") is True
        and remote.get("precleaned") is not True
    ):
        remote["cleanupReplaySuppressed"] = True
        return False
    remote["cleanupAttempted"] = True
    return True


def validate_full_editing_fixture(path: Path) -> FullEditingFixtureContract:
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as error:
        raise RuntimeError(f"Full-editing fixture is not valid XML: {path}") from error
    if root.tag != "character":
        raise RuntimeError("Full-editing fixture must use <character> as its root")
    if root.findtext("created") != "True":
        raise RuntimeError("Full-editing fixture must be an exact created=True career runner")
    if root.findtext("alias") != "FullEditingE2E":
        raise RuntimeError("Full-editing fixture must use alias FullEditingE2E")

    bodies = [
        attribute
        for attribute in root.findall("./attributes/attribute")
        if attribute.findtext("name") == "BOD"
    ]
    if len(bodies) != 1:
        raise RuntimeError("Full-editing fixture must contain exactly one source-valid BOD attribute")
    body = bodies[0]
    if [child.tag for child in body] != [
        "name",
        "metatypemin",
        "metatypemax",
        "metatypeaugmax",
        "base",
        "karma",
        "metatypecategory",
        "totalvalue",
    ]:
        raise RuntimeError("Full-editing fixture BOD must use canonical Chummer5 field order")

    def required_int(parent: ET.Element, name: str) -> int:
        raw = parent.findtext(name)
        try:
            return int(raw) if raw is not None else int("")
        except ValueError as error:
            raise RuntimeError(
                f"Full-editing fixture requires integer <{name}> for BOD"
            ) from error

    base = required_int(body, "base")
    karma_value = required_int(body, "karma")
    total = required_int(body, "totalvalue")
    minimum = required_int(body, "metatypemin")
    maximum = required_int(body, "metatypemax")
    augmented_maximum = required_int(body, "metatypeaugmax")
    available_karma = required_int(root, "karma")
    if body.findtext("metatypecategory") != "Standard":
        raise RuntimeError(
            "Full-editing fixture BOD must use Chummer5 metatypecategory Standard"
        )
    if (base, karma_value, total, minimum, maximum, augmented_maximum) != (
        1,
        0,
        1,
        1,
        6,
        10,
    ):
        raise RuntimeError(
            "Full-editing fixture BOD must be exact: base=1, karma=0, "
            "totalvalue=1, min=1, max=6, augmax=10"
        )
    if not minimum <= total < augmented_maximum:
        raise RuntimeError("Full-editing fixture BOD must have career improvement headroom")
    improvement_cost = (total + 1) * 5
    if available_karma < improvement_cost:
        raise RuntimeError(
            "Full-editing fixture does not have enough Karma for the BOD improvement"
        )
    return FullEditingFixtureContract(
        initial_body_total=total,
        improved_body_total=total + 1,
        improvement_cost=improvement_cost,
        initial_karma=available_karma,
        remaining_karma=available_karma - improvement_cost,
        next_improvement_cost=(total + 2) * 5,
    )


def _authority_value(device: Device, automation_id: str) -> str:
    if automation_id not in WORKSPACE_AUTHORITY_RESOURCE_IDS:
        raise RuntimeError(
            f"Unknown workspace authority accessibility id {automation_id!r}"
        )
    node = device.wait_for_single_exact_resource_id(
        automation_id,
        timeout=90,
        scroll=True,
        max_scrolls=12,
    )
    value = node.attributes.get("text", "").strip()
    if not value:
        device.capture("workspace-authority-empty")
        raise RuntimeError(f"Workspace authority node {automation_id!r} is empty")
    return value


def _read_workspace_authority_once(device: Device) -> WorkspaceAuthority:
    workspace_id = _authority_value(device, "home-e2e-workspace-id")
    try:
        content_revision = int(_authority_value(device, "home-e2e-content-revision"))
        saved_revision = int(_authority_value(device, "home-e2e-saved-revision"))
    except ValueError as error:
        device.capture("workspace-authority-revision-invalid")
        raise RuntimeError("Workspace authority revisions are not integers") from error
    payload_sha256 = _authority_value(device, "home-e2e-payload-sha256")
    document_sha256 = _authority_value(device, "home-e2e-document-sha256")
    if not workspace_id or content_revision <= 0 or saved_revision < 0:
        raise RuntimeError("Workspace authority identity or revisions are invalid")
    if SHA256_TEXT.fullmatch(payload_sha256) is None:
        raise RuntimeError("Workspace authority payload SHA-256 is not canonical")
    if SHA256_TEXT.fullmatch(document_sha256) is None:
        raise RuntimeError("Workspace authority document SHA-256 is not canonical")
    return WorkspaceAuthority(
        workspace_id,
        content_revision,
        saved_revision,
        payload_sha256,
        document_sha256,
    )


def read_workspace_authority(device: Device) -> WorkspaceAuthority:
    reset_scroll_to_top(device, swipes=12)
    first = _read_workspace_authority_once(device)
    reset_scroll_to_top(device, swipes=12)
    verified = _read_workspace_authority_once(device)
    if verified != first:
        device.capture("workspace-authority-surface-changed")
        raise RuntimeError(
            "Workspace authority accessibility surface changed during verification: "
            f"first={first!r}, verified={verified!r}"
        )
    return verified


def read_phone_workspace_authority(device: Device) -> WorkspaceAuthority:
    """Navigate to the phone Runners proof surface before reading authority."""
    tap_phone_destination(device, "phone-destination-runners")
    wait_for_phone_runners(device)
    return read_workspace_authority(device)


def require_import_authority(
    authority: WorkspaceAuthority,
    expected_payload_sha256: str,
    previous_workspace_id: str | None = None,
) -> None:
    if authority.payload_sha256 != expected_payload_sha256:
        raise RuntimeError(
            "Imported workspace payload does not match the exact verified fixture bytes: "
            f"expected {expected_payload_sha256}, got {authority.payload_sha256}"
        )
    if previous_workspace_id is not None and authority.workspace_id == previous_workspace_id:
        raise RuntimeError("Fixture import did not activate a new target workspace")


def require_saved_authority(authority: WorkspaceAuthority) -> None:
    if authority.content_revision != authority.saved_revision:
        raise RuntimeError(
            "Workspace authority is not durably checkpointed: "
            f"content revision {authority.content_revision}, "
            f"saved revision {authority.saved_revision}"
        )


def require_restored_authority(
    persisted: WorkspaceAuthority,
    restored: WorkspaceAuthority,
) -> None:
    require_saved_authority(restored)
    if restored != persisted:
        raise RuntimeError(
            "Fresh-process workspace authority does not match the exact saved document: "
            f"before={persisted!r}, after={restored!r}"
        )


def workspace_authority_json(authority: WorkspaceAuthority) -> dict[str, object]:
    return {
        "workspaceId": authority.workspace_id,
        "contentRevision": authority.content_revision,
        "savedRevision": authority.saved_revision,
        "payloadSha256": authority.payload_sha256,
        "documentSha256": authority.document_sha256,
    }


def optional_workspace_authority_json(
    authority: WorkspaceAuthority | None,
) -> dict[str, object] | None:
    return None if authority is None else workspace_authority_json(authority)


def _node_has_canonical_resource_id(node: UiNode, selector: str) -> bool:
    return (
        node.attributes.get("package") == PACKAGE
        and node.attributes.get("resource-id") == f"{PACKAGE}:id/{selector}"
    )


def _phone_runner_route_from_nodes(
    device: Device,
    nodes: list[UiNode],
    *,
    created: bool | None,
    require_tappable_bounds: bool,
) -> UiNode | None:
    expected_routes = {"phone-runner-create", "phone-runner-sheet"}
    desired_route = (
        None
        if created is None
        else "phone-runner-sheet" if created else "phone-runner-create"
    )
    matches = [
        (
            node.attributes["resource-id"].rsplit("/", 1)[-1],
            node,
        )
        for node in nodes
        if any(
            _node_has_canonical_resource_id(node, route_id)
            for route_id in expected_routes
        )
    ]
    if len(matches) == 1:
        observed_route, node = matches[0]
        if desired_route is not None and observed_route != desired_route:
            device.capture("phone-runner-route-lifecycle-mismatch")
            raise RuntimeError(
                f"Final phone runner route was {observed_route!r}; "
                f"expected sole root {desired_route!r}"
            )
        expected_label = (
            "CREATION RUNNER"
            if observed_route == "phone-runner-create"
            else "CAREER RUNNER"
        )
        if (
            node.attributes.get("class") != "android.widget.TextView"
            or node.attributes.get("enabled") != "true"
            or node.attributes.get("clickable") != "false"
            or node.attributes.get("focusable") != "false"
            or node.attributes.get("text") != expected_label
        ):
            device.capture("phone-runner-route-structure-invalid")
            raise RuntimeError(
                "Exact phone runner lifecycle marker did not expose its pinned "
                "noninteractive native role and label"
            )
        if require_tappable_bounds and not device.node_has_tappable_bounds(node):
            device.capture("phone-runner-route-structure-invalid")
            raise RuntimeError(
                "Exact phone runner lifecycle marker was not visible with its "
                "pinned native role after the root viewport reset"
            )
        return node
    if len(matches) > 1:
        device.capture("phone-runner-route-cardinality-invalid")
        raise RuntimeError(
            "Final phone runner route exposed both creation and career roots"
        )
    return None


def wait_for_phone_runner_route(
    device: Device,
    *,
    created: bool | None = None,
    timeout: int = 90,
) -> UiNode:
    return return_to_phone_runner_root(
        device,
        created=created,
        timeout=timeout,
    )


def _node_exposes_exact_accessibility_label(node: UiNode, label: str) -> bool:
    for attribute in ("text", "content-desc"):
        value = node.attributes.get(attribute, "")
        if value == label or value.startswith(f"{label},"):
            return True
    return False


def _is_forbidden_launcher_resource_id(resource_id: str) -> bool:
    normalized = resource_id.casefold()
    return normalized in PHONE_SHELL_FORBIDDEN_LAUNCHER_RESOURCE_IDS or any(
        normalized.startswith(prefix)
        for prefix in PHONE_SHELL_FORBIDDEN_LAUNCHER_ID_PREFIXES
    )


def _native_phone_bottom_tab_bounds_are_plausible(
    node: UiNode,
    *,
    display_width: int,
    display_height: int,
) -> bool:
    """Identify only native Shell tab-shaped nodes in the phone bottom band."""
    if (
        node.attributes.get("package") != PACKAGE
        or node.attributes.get("class") != "android.widget.FrameLayout"
    ):
        return False
    try:
        left, top, right, bottom = node.bounds
    except RuntimeError:
        return False
    return (
        0 <= left < right <= display_width
        and int(display_height * 0.80) <= top < bottom <= display_height
        and bottom >= int(display_height * 0.90)
        and bottom - top <= int(display_height * 0.20)
    )


def detect_phone_ui_locale(device: Device) -> PhoneUiLocaleBinding:
    """Read and cache the exact system locale that governs this proof run."""
    if device._phone_ui_locale_binding is not None:
        return device._phone_ui_locale_binding
    observed: list[tuple[str, str]] = []
    for property_name in PHONE_UI_LOCALE_PROPERTIES:
        value = device.shell("getprop", property_name).strip()
        observed.append((property_name, value))
        if not value:
            continue
        binding = PhoneUiLocaleBinding(
            locale_tag=value,
            language=supported_phone_ui_language(value),
            authority_property=property_name,
        )
        device._phone_ui_locale_binding = binding
        return binding
    raise RuntimeError(
        "Phone UI locale was unavailable from the bounded Android property set: "
        f"{observed!r}"
    )


def bind_phone_shell_destinations(
    device: Device,
    nodes: list[UiNode] | None = None,
) -> tuple[tuple[str, UiNode], ...]:
    """Bind the pinned MAUI Android bottom bar without trusting text aliases.

    MAUI's generated Android Shell tabs expose an empty resource-id on the
    pinned API-36 build. Their native type, exact accessible name, geometry,
    order, focus/click state and one selected destination form the fail-closed
    identity instead. Any non-empty resource-id fails this pinned contract.
    """
    hierarchy = device.hierarchy() if nodes is None else nodes
    display_width, display_height = device.display_size()
    candidates = [
        node
        for node in hierarchy
        if node.attributes.get("content-desc", "").strip()
        and node.attributes.get("enabled") == "true"
        and node.attributes.get("focusable") == "true"
        and node.attributes.get("selected") in ("true", "false")
        and node.attributes.get("clickable") in ("true", "false")
        and _native_phone_bottom_tab_bounds_are_plausible(
            node,
            display_width=display_width,
            display_height=display_height,
        )
    ]
    if len(candidates) != len(PHONE_SHELL_DESTINATION_IDS):
        raise RuntimeError(
            "Native phone bottom bar has "
            f"{len(candidates)} recognized destinations; expected exactly "
            f"{len(PHONE_SHELL_DESTINATION_IDS)}"
        )

    ordered = sorted(candidates, key=lambda node: node.bounds[0])
    labels = tuple(node.attributes.get("content-desc", "") for node in ordered)
    matching_languages = [
        language
        for language, expected_labels in PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE.items()
        if labels == expected_labels
    ]
    if len(matching_languages) != 1:
        raise RuntimeError(
            f"Native phone bottom bar order is {labels!r}; expected exactly one "
            f"supported DE/EN/ES tuple {PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE!r}"
        )
    locale_binding = getattr(device, "_phone_ui_locale_binding", None)
    if isinstance(locale_binding, PhoneUiLocaleBinding):
        resolve_localized_ui_labels(
            contract_id="phone-shell-destinations",
            locale_tag=locale_binding.locale_tag,
            observed_labels=labels,
            labels_by_language=PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE,
        )

    bounds = [node.bounds for node in ordered]
    top = bounds[0][1]
    bottom = bounds[0][3]
    expected_edges = [
        index * display_width // len(ordered)
        for index in range(len(ordered) + 1)
    ]
    expected_bounds = [
        (expected_edges[index], top, expected_edges[index + 1], bottom)
        for index in range(len(ordered))
    ]
    if bounds != expected_bounds:
        raise RuntimeError(
            "Native phone bottom bar geometry is not "
            f"{len(ordered)} exact contiguous segments: "
            f"observed {bounds!r}, expected {expected_bounds!r}"
        )

    selected_labels: list[str] = []
    result: list[tuple[str, UiNode]] = []
    for expected_resource_id, label, node in zip(
        PHONE_SHELL_DESTINATION_IDS,
        labels,
        ordered,
        strict=True,
    ):
        actual_resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
        selected = node.attributes.get("selected") == "true"
        clickable = node.attributes.get("clickable") == "true"
        if actual_resource_id:
            raise RuntimeError(
                f"Native phone destination {label!r} must expose the pinned empty "
                f"resource-id, not {actual_resource_id!r}"
            )
        if (
            node.attributes.get("enabled") != "true"
            or node.attributes.get("focusable") != "true"
            or clickable == selected
        ):
            raise RuntimeError(
                f"Native phone destination {label!r} has invalid enabled/focus/"
                "clickable/selected semantics"
            )
        if selected:
            selected_labels.append(PHONE_SHELL_DESTINATION_MAPPING[expected_resource_id])
        result.append((expected_resource_id, node))
    if len(selected_labels) != 1:
        raise RuntimeError(
            "Native phone bottom bar must expose exactly one selected destination; "
            f"observed {selected_labels!r}"
        )
    return tuple(result)


def _phone_shell_destination_signature(
    destinations: tuple[tuple[str, UiNode], ...],
) -> tuple[tuple[object, ...], ...]:
    return tuple(
        (
            resource_id,
            node.attributes.get("content-desc", ""),
            node.attributes.get("resource-id", ""),
            node.attributes.get("selected", ""),
            node.attributes.get("clickable", ""),
            node.attributes.get("enabled", ""),
            node.attributes.get("focusable", ""),
            node.bounds,
        )
        for resource_id, node in destinations
    )


def wait_for_phone_shell_destination_snapshot(
    device: Device,
    *,
    timeout: int,
    evidence_prefix: str,
    selected_label: str | None = None,
    required_route_resource_id: str | None = None,
) -> tuple[list[UiNode], tuple[tuple[str, UiNode], ...]]:
    deadline = time.monotonic() + timeout
    last_error = "native phone bottom bar was absent"
    previous_signature: tuple[tuple[object, ...], ...] | None = None
    while time.monotonic() < deadline:
        binding_failed = False
        hierarchy = device.hierarchy()
        route_signature: tuple[object, ...] = ()
        if required_route_resource_id is not None:
            route_matches = [
                node
                for node in hierarchy
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == required_route_resource_id
            ]
            if len(route_matches) > 1:
                device.capture(f"{evidence_prefix}-route-cardinality-invalid")
                raise RuntimeError(
                    f"Required phone route {required_route_resource_id!r} has "
                    f"cardinality {len(route_matches)}; expected one"
                )
            if not route_matches:
                last_error = (
                    f"required phone route {required_route_resource_id!r} was absent"
                )
                previous_signature = None
                time.sleep(0.75)
                continue
            route = route_matches[0]
            route_signature = (
                required_route_resource_id,
                route.attributes.get("class", ""),
                route.attributes.get("enabled", ""),
                route.attributes.get("bounds", ""),
            )
        try:
            destinations = bind_phone_shell_destinations(device, hierarchy)
            selected = [
                PHONE_SHELL_DESTINATION_MAPPING[resource_id]
                for resource_id, node in destinations
                if node.attributes.get("selected") == "true"
            ]
            signature = (
                *_phone_shell_destination_signature(destinations),
                route_signature,
            )
            if selected_label is not None and selected != [selected_label]:
                last_error = (
                    f"selected destination remained {selected!r}; "
                    f"expected {selected_label!r}"
                )
                previous_signature = None
            elif signature == previous_signature:
                return hierarchy, destinations
            else:
                last_error = "native phone bottom bar has not remained stable for two dumps"
                previous_signature = signature
        except RuntimeError as error:
            last_error = str(error)
            previous_signature = None
            binding_failed = True
        if binding_failed and device.dismiss_system_ui_anr():
            time.sleep(2)
            continue
        time.sleep(0.75)
    device.capture(f"{evidence_prefix}-invalid")
    raise RuntimeError(
        f"Timed out waiting for structurally bound phone destinations: {last_error}"
    )


def wait_for_phone_shell_destinations(
    device: Device,
    *,
    timeout: int,
    evidence_prefix: str,
    selected_label: str | None = None,
) -> tuple[tuple[str, UiNode], ...]:
    _, destinations = wait_for_phone_shell_destination_snapshot(
        device,
        timeout=timeout,
        evidence_prefix=evidence_prefix,
        selected_label=selected_label,
    )
    return destinations


def record_phone_ui_locale_evidence(
    device: Device,
    *,
    evidence_prefix: str,
    timeout: int = 45,
    required_route_resource_id: str | None = None,
) -> dict[str, object]:
    """Bind system locale and native no-ID shell labels in one durable receipt."""
    binding = detect_phone_ui_locale(device)
    _, destinations = wait_for_phone_shell_destination_snapshot(
        device,
        timeout=timeout,
        evidence_prefix=evidence_prefix,
        required_route_resource_id=required_route_resource_id,
    )
    observed_labels = tuple(
        node.attributes.get("content-desc", "") for _, node in destinations
    )
    label_binding = resolve_localized_ui_labels(
        contract_id="phone-shell-destinations",
        locale_tag=binding.locale_tag,
        observed_labels=observed_labels,
        labels_by_language=PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE,
    )
    receipt: dict[str, object] = {
        "schema": PHONE_UI_LOCALE_EVIDENCE_SCHEMA,
        "status": "pass",
        "serial": device.serial,
        "authorityProperty": binding.authority_property,
        **label_binding,
        "destinationResourceIds": list(PHONE_SHELL_DESTINATION_IDS),
        "nativeResourceIdPosture": "empty-pinned-maui-api36",
    }
    if required_route_resource_id is not None:
        receipt["boundRouteResourceId"] = required_route_resource_id
    _write_new_json_receipt(
        device.evidence / f"{evidence_prefix}-phone-ui-locale.json",
        receipt,
    )
    return receipt


def wait_for_phone_runners(device: Device, *, timeout: int = 90) -> UiNode:
    return device.wait_for_single_exact_resource_id(
        "phone-runners",
        timeout=timeout,
        evidence_prefix="phone-runners-route",
        surface_name="Phone runners route",
    )


def tap_phone_destination(
    device: Device,
    resource_id: str,
    *,
    timeout: int = 45,
) -> None:
    if resource_id not in PHONE_SHELL_DESTINATION_MAPPING:
        raise ValueError(f"Unknown phone shell destination {resource_id!r}")
    label = PHONE_SHELL_DESTINATION_MAPPING[resource_id]
    destinations = wait_for_phone_shell_destinations(
        device,
        timeout=timeout,
        evidence_prefix=f"{resource_id}-tap-bind",
    )
    selected_before_tap = next(
        candidate_id
        for candidate_id, node in destinations
        if node.attributes.get("selected") == "true"
    )
    if selected_before_tap == resource_id:
        return
    selected_label_before_tap = PHONE_SHELL_DESTINATION_MAPPING[selected_before_tap]
    fresh_destinations = wait_for_phone_shell_destinations(
        device,
        timeout=timeout,
        evidence_prefix=f"{resource_id}-tap-reacquire",
        selected_label=selected_label_before_tap,
    )
    if _phone_shell_destination_signature(fresh_destinations) != (
        _phone_shell_destination_signature(destinations)
    ):
        device.capture(f"{resource_id}-tap-stale")
        raise RuntimeError(
            "Native phone bottom bar changed between stable binding and tap; "
            "refusing stale coordinates"
        )
    node = next(
        node for candidate_id, node in fresh_destinations if candidate_id == resource_id
    )
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))
    wait_for_phone_shell_destinations(
        device,
        timeout=timeout,
        evidence_prefix=f"{resource_id}-tap-select",
        selected_label=label,
    )


def assert_phone_shell_surface(
    device: Device,
    *,
    route_resource_id: str,
    evidence_prefix: str,
) -> dict[str, object]:
    """Observe the live hierarchy and fail unless the phone shell is exactly the proven set."""
    device.wait_for_single_exact_resource_id(
        route_resource_id,
        timeout=90,
        evidence_prefix=evidence_prefix,
        surface_name="Phone shell route",
    )
    try:
        nodes, destinations = wait_for_phone_shell_destination_snapshot(
            device,
            timeout=45,
            evidence_prefix=evidence_prefix,
        )
    except RuntimeError as error:
        observation = {
            "routeResourceId": route_resource_id,
            "structuralError": str(error),
        }
        (device.evidence / f"{evidence_prefix}-observation.json").write_text(
            json.dumps(observation, indent=2) + "\n",
            encoding="utf-8",
        )
        raise RuntimeError(
            "Phone shell did not expose the exact supported four-destination set or exposed a "
            f"postponed surface: {observation!r}"
        ) from error
    resource_ids = [
        node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
        for node in nodes
    ]
    observed_destination_mapping = {
        resource_id: [PHONE_SHELL_DESTINATION_MAPPING[resource_id]]
        for resource_id, _ in destinations
    }
    observed_destination_ids = sorted(observed_destination_mapping)
    observed_destination_labels = sorted(
        label for labels in observed_destination_mapping.values() for label in labels
    )
    display_width, display_height = device.display_size()
    recognized_navigation_nodes = [
        (resource_id, node)
        for resource_id, node in zip(resource_ids, nodes, strict=True)
        if resource_id.startswith("phone-destination-")
        or resource_id.startswith("tablet-destination-")
        or resource_id in PHONE_SHELL_FORBIDDEN_ROUTE_RESOURCE_IDS
        or _is_forbidden_launcher_resource_id(resource_id)
        or _native_phone_bottom_tab_bounds_are_plausible(
            node,
            display_width=display_width,
            display_height=display_height,
        )
    ]
    forbidden_destination_labels = sorted(
        label
        for label in PHONE_SHELL_FORBIDDEN_DESTINATION_LABELS
        if any(
            _node_exposes_exact_accessibility_label(node, label)
            for _, node in recognized_navigation_nodes
        )
    )
    forbidden_support_labels = sorted(
        label
        for label in PHONE_SHELL_FORBIDDEN_SUPPORT_LABELS
        if any(
            (
                resource_id in PHONE_SHELL_FORBIDDEN_ROUTE_RESOURCE_IDS
                or _is_forbidden_launcher_resource_id(resource_id)
            )
            and _node_exposes_exact_accessibility_label(node, label)
            for resource_id, node in recognized_navigation_nodes
        )
    )
    forbidden_route_ids = sorted(
        resource_id
        for resource_id, node in zip(resource_ids, nodes, strict=True)
        if (
            (
                resource_id.startswith("phone-destination-")
                and resource_id not in PHONE_SHELL_DESTINATION_IDS
            )
            or resource_id.startswith("tablet-destination-")
            or resource_id in PHONE_SHELL_FORBIDDEN_ROUTE_RESOURCE_IDS
            or _is_forbidden_launcher_resource_id(resource_id)
        )
    )
    observation = {
        "routeResourceId": route_resource_id,
        "destinationResourceIds": observed_destination_ids,
        "destinationLabels": observed_destination_labels,
        "destinationMapping": observed_destination_mapping,
        "nativeDestinationLabels": [
            node.attributes.get("content-desc", "")
            for _, node in destinations
        ],
        "nativeDestinationLanguage": next(
            language
            for language, labels in PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE.items()
            if labels
            == tuple(
                node.attributes.get("content-desc", "")
                for _, node in destinations
            )
        ),
        "selectedDestination": next(
            PHONE_SHELL_DESTINATION_MAPPING[resource_id]
            for resource_id, node in destinations
            if node.attributes.get("selected") == "true"
        ),
        "nativeDestinationResourceIds": {
            PHONE_SHELL_DESTINATION_MAPPING[resource_id]:
                node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            for resource_id, node in destinations
        },
        "nativeDestinationBounds": {
            PHONE_SHELL_DESTINATION_MAPPING[resource_id]: node.attributes.get("bounds", "")
            for resource_id, node in destinations
        },
        "forbiddenDestinationLabels": forbidden_destination_labels,
        "forbiddenSupportLabels": forbidden_support_labels,
        "forbiddenRouteResourceIds": forbidden_route_ids,
    }
    expected_ids = sorted(PHONE_SHELL_DESTINATION_IDS)
    expected_labels = sorted(PHONE_SHELL_DESTINATION_LABELS)
    expected_mapping = {
        resource_id: [label]
        for resource_id, label in PHONE_SHELL_DESTINATION_MAPPING.items()
    }
    if (
        observed_destination_ids != expected_ids
        or observed_destination_labels != expected_labels
        or observed_destination_mapping != expected_mapping
        or forbidden_destination_labels
        or forbidden_support_labels
        or forbidden_route_ids
    ):
        (device.evidence / f"{evidence_prefix}-observation.json").write_text(
            json.dumps(observation, indent=2) + "\n",
            encoding="utf-8",
        )
        device.capture(f"{evidence_prefix}-invalid")
        raise RuntimeError(
            "Phone shell did not expose the exact supported DE/EN/ES four-destination set "
            "or exposed a "
            f"postponed surface: {observation!r}"
        )
    (device.evidence / f"{evidence_prefix}-observation.json").write_text(
        json.dumps(observation, indent=2) + "\n",
        encoding="utf-8",
    )
    return observation


def save_and_read_workspace_authority(
    device: Device,
    profile: str,
) -> WorkspaceAuthority:
    if profile != "phone":
        raise RuntimeError("The API 36 beta authority gate is phone-only; tablet proof is deferred")
    tap_phone_destination(device, "phone-destination-runners")
    wait_for_phone_runners(device)
    open_build(device, profile)
    save_runner_and_wait_for_durable_notice(device)
    tap_phone_destination(device, "phone-destination-runners")
    wait_for_phone_runners(device)
    authority = read_workspace_authority(device)
    require_saved_authority(authority)
    return authority


def open_build(device: Device, profile: str) -> None:
    if profile == "tablet":
        device.open_navigation_drawer()
        device.tap("Build")
        device.wait("tablet-build-layout", timeout=45)
        return
    tap_phone_destination(device, "phone-destination-runner")
    return_to_phone_runner_root(device)


def reset_scroll_to_top(
    device: Device,
    *,
    x_ratio: float = 0.5,
    swipes: int = 2,
) -> None:
    for _ in range(swipes):
        device.swipe_down(x_ratio=x_ratio)
        time.sleep(0.2)
    if swipes > 0:
        time.sleep(0.75)


def return_to_phone_runner_root(
    device: Device,
    *,
    created: bool | None = None,
    timeout: int = 90,
    max_back_steps: int = 8,
) -> UiNode:
    """Unwind a preserved Shell Build stack and prove its exact root.

    Selecting an already-selected Shell destination does not pop MAUI's nested
    navigation stack. Require the immutable BuildPage marker, its fixed Save
    toolbar, and the exact lifecycle route marker. Reset the preserved viewport
    only when that exact route marker is clipped. Otherwise activate only the
    platform's exact ``Navigate up`` control. This keeps recovery bounded and
    prevents a stale collection editor from being mistaken for the Build root.
    """
    deadline = time.monotonic() + timeout
    back_steps = 0
    viewport_reset = False
    while time.monotonic() < deadline:
        nodes = device.hierarchy()
        if not nodes:
            if device.dismiss_system_ui_anr():
                time.sleep(2)
            else:
                time.sleep(0.75)
            continue

        route = _phone_runner_route_from_nodes(
            device,
            nodes,
            created=created,
            require_tappable_bounds=viewport_reset,
        )
        page_matches = [
            node
            for node in nodes
            if _node_has_canonical_resource_id(node, "phone-runner-page")
            and node.attributes.get("class") == "android.view.ViewGroup"
            and node.attributes.get("enabled") == "true"
        ]
        if len(page_matches) > 1:
            device.capture("phone-runner-root-page-cardinality-invalid")
            raise RuntimeError(
                "Phone runner root exposed more than one exact phone-runner-page marker"
            )
        toolbar_matches = [
            node
            for node in nodes
            if node.attributes.get("package") == PACKAGE
            and node.attributes.get("resource-id", "") == ""
            and node.attributes.get("content-desc") == "build-save-runner"
            and node.attributes.get("class") == "android.widget.Button"
            and node.attributes.get("enabled") == "true"
            and node.attributes.get("clickable") == "true"
            and node.attributes.get("focusable") == "true"
        ]
        if len(toolbar_matches) > 1:
            device.capture("phone-runner-root-toolbar-cardinality-invalid")
            raise RuntimeError(
                "Phone runner root exposed more than one exact build-save-runner toolbar"
            )
        root_authority = (
            len(page_matches) == 1
            and len(toolbar_matches) == 1
            and device.node_has_tappable_bounds(page_matches[0])
            and device.node_has_tappable_bounds(toolbar_matches[0])
        )
        if root_authority:
            if route is not None and device.node_has_tappable_bounds(route):
                return route
            if not viewport_reset:
                # The lifecycle marker is the first child of BuildPage's
                # ScrollView and can be clipped by a preserved deep offset.
                # Only an exact immutable page plus root-only toolbar authorizes
                # the reset; the route marker must then become visible before return.
                rewind_surface_to_stable_start(
                    device,
                    evidence_prefix="phone-runner-root",
                )
                viewport_reset = True
                continue
            # Never treat the toolbar alone as final route authority.
            time.sleep(0.75)
            continue

        if back_steps >= max_back_steps:
            device.capture("phone-runner-root-unwind-exhausted")
            raise RuntimeError(
                "Phone runner root remained unavailable after "
                f"{max_back_steps} exact Navigate up activations"
            )

        navigate_up = [
            node
            for node in nodes
            if node.attributes.get("package") == PACKAGE
            and node.attributes.get("resource-id", "") == ""
            and node.attributes.get("content-desc") == "Navigate up"
            and node.attributes.get("class") == "android.widget.ImageButton"
            and node.attributes.get("enabled") == "true"
            and node.attributes.get("clickable") == "true"
            and node.attributes.get("focusable") == "true"
        ]
        if len(navigate_up) > 1:
            device.capture("phone-runner-root-navigation-cardinality-invalid")
            raise RuntimeError(
                "Phone runner stack exposed more than one exact Navigate up control"
            )
        if len(navigate_up) == 1 and device.node_has_tappable_bounds(navigate_up[0]):
            x, y = navigate_up[0].center
            device.shell("input", "tap", str(x), str(y))
            back_steps += 1
            viewport_reset = False
            time.sleep(0.75)
            continue

        if device.dismiss_system_ui_anr():
            time.sleep(2)
            continue
        time.sleep(0.75)

    device.capture("phone-runner-root-unavailable")
    raise RuntimeError(
        "Timed out proving the exact phone runner root and build-save-runner toolbar"
    )


def open_creation_dashboard(
    device: Device,
    profile: str = "phone",
    *,
    open_build_route: bool = True,
    toolbar_timeout: int = 90,
    dashboard_timeout: int = 90,
    reset_swipes: int = 48,
) -> UiNode:
    """Open and bind the phone creation dashboard without viewport assumptions.

    MAUI preserves the inner Build ScrollView position across route visits. The
    page-level dashboard resource can therefore exist thousands of pixels above
    UIAutomator's visible hierarchy. Bind the fixed toolbar first, reset that
    inner viewport, then require one exact dashboard resource id. Callers that
    must prove an automatic handoff or Back route can disable the explicit Build
    tap while retaining the same fail-closed binding sequence.
    """
    if profile != "phone":
        raise RuntimeError(
            "The creation dashboard API 36 proof is phone-only; tablet proof is deferred"
        )
    if open_build_route:
        open_build(device, profile)
    device.wait_for_single_exact_accessibility_value(
        "build-save-runner",
        timeout=toolbar_timeout,
        evidence_prefix="creation-dashboard-toolbar",
        surface_name="Creation dashboard toolbar accessibility node",
    )
    reset_scroll_to_top(device, swipes=reset_swipes)
    return device.wait_for_single_exact_resource_id(
        "creation-wizard-dashboard",
        timeout=dashboard_timeout,
        evidence_prefix="creation-dashboard",
        surface_name="Creation dashboard resource node",
    )


def open_origin_dossier(device: Device, profile: str) -> None:
    selector = "tablet-origin-dossier" if profile == "tablet" else "build-origin-dossier"
    if profile == "phone":
        reset_scroll_to_top(device, swipes=12)
    device.tap(
        selector,
        scroll=True,
        timeout=60,
        max_scrolls=16,
        scroll_distance_ratio=0.22,
    )


def tap_collection_item(device: Device, selector: str) -> None:
    """Select a collection card without skipping it or tapping its child label."""
    device.tap(
        selector,
        scroll=True,
        timeout=60,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
        text_leading_offset=18,
    )


COLLECTION_ROUTE_SCAN_MAX_SCROLLS = 32
COLLECTION_ROUTE_SCAN_DISTANCE_RATIO = 0.52
COLLECTION_ROUTE_SCAN_STABLE_REPEATS = 2


@dataclass(frozen=True)
class CollectionRouteInventory:
    route_viewports: dict[str, int]
    bottom_movement_swipes: int


def rewind_surface_to_stable_start(
    device: Device,
    *,
    evidence_prefix: str,
) -> int:
    """Prove a scroll surface's start through fresh post-gesture snapshots."""
    previous_sha256: str | None = None
    unchanged = 0
    swipes = 0
    consecutive_empty_reads = 0
    while swipes <= COLLECTION_ROUTE_SCAN_MAX_SCROLLS:
        nodes = device.hierarchy()
        if not nodes:
            consecutive_empty_reads += 1
            if consecutive_empty_reads > 3:
                device.capture(f"{evidence_prefix}-stable-start-empty-hierarchy")
                raise RuntimeError(
                    "Collection route stable-start proof exhausted empty hierarchies"
                )
            time.sleep(0.75)
            continue
        consecutive_empty_reads = 0
        hierarchy_sha256 = Device._hierarchy_sha256(nodes)
        unchanged = unchanged + 1 if hierarchy_sha256 == previous_sha256 else 0
        previous_sha256 = hierarchy_sha256
        if unchanged >= COLLECTION_ROUTE_SCAN_STABLE_REPEATS:
            return swipes - COLLECTION_ROUTE_SCAN_STABLE_REPEATS
        if swipes >= COLLECTION_ROUTE_SCAN_MAX_SCROLLS:
            break
        device.swipe_down(
            x_ratio=0.5,
            distance_ratio=COLLECTION_ROUTE_SCAN_DISTANCE_RATIO,
        )
        swipes += 1
        time.sleep(0.2)
    device.capture(f"{evidence_prefix}-stable-start-unproven")
    raise RuntimeError(
        "Collection route surface did not prove its stable start within the exact bound"
    )


def scan_collection_route_inventory(
    device: Device,
    *,
    kind: str,
    evidence_prefix: str,
) -> CollectionRouteInventory:
    """Inventory typed collection routes from one proven start to stable end."""
    rewind_surface_to_stable_start(device, evidence_prefix=evidence_prefix)
    positions: dict[str, int] = {}
    semantics: dict[str, tuple[str, ...]] = {}
    previous_sha256: str | None = None
    unchanged = 0
    swipes = 0
    consecutive_empty_reads = 0
    while swipes <= COLLECTION_ROUTE_SCAN_MAX_SCROLLS:
        nodes = device.hierarchy()
        if not nodes:
            consecutive_empty_reads += 1
            if consecutive_empty_reads > 3:
                device.capture(f"{evidence_prefix}-empty-hierarchy")
                raise RuntimeError(
                    "Collection route inventory exhausted empty hierarchies"
                )
            time.sleep(0.75)
            continue
        consecutive_empty_reads = 0

        viewport_routes: set[str] = set()
        for node in nodes:
            resource_id = node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            match = CANONICAL_COLLECTION_ITEM_RESOURCE_ID.fullmatch(resource_id)
            if match is None or match.group("kind") != kind:
                continue
            if resource_id in viewport_routes:
                device.capture(f"{evidence_prefix}-route-cardinality-invalid")
                raise RuntimeError(
                    f"Typed {kind} collection route {resource_id!r} was duplicated in one viewport"
                )
            viewport_routes.add(resource_id)
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
            prior = semantics.setdefault(resource_id, signature)
            if signature != prior:
                device.capture(f"{evidence_prefix}-route-semantics-drift")
                raise RuntimeError(
                    f"Typed {kind} collection route {resource_id!r} changed semantics"
                )
            if (
                node.attributes.get("enabled") != "true"
                or node.attributes.get("clickable") != "true"
            ):
                device.capture(f"{evidence_prefix}-route-not-enabled")
                raise RuntimeError(
                    f"Typed {kind} collection route {resource_id!r} was not enabled and clickable"
                )
            if device.node_has_tappable_bounds(node):
                positions.setdefault(resource_id, swipes)

        hierarchy_sha256 = Device._hierarchy_sha256(nodes)
        unchanged = unchanged + 1 if hierarchy_sha256 == previous_sha256 else 0
        previous_sha256 = hierarchy_sha256
        if unchanged >= COLLECTION_ROUTE_SCAN_STABLE_REPEATS:
            return CollectionRouteInventory(
                route_viewports=dict(positions),
                bottom_movement_swipes=swipes - COLLECTION_ROUTE_SCAN_STABLE_REPEATS,
            )
        if swipes >= COLLECTION_ROUTE_SCAN_MAX_SCROLLS:
            break
        device.swipe_up(
            x_ratio=0.5,
            distance_ratio=COLLECTION_ROUTE_SCAN_DISTANCE_RATIO,
        )
        swipes += 1
        time.sleep(0.2)
    device.capture(f"{evidence_prefix}-stable-end-unproven")
    raise RuntimeError(
        "Collection route inventory did not prove a stable end within the exact bound"
    )


def tap_typed_collection_route(
    device: Device,
    *,
    inventory: CollectionRouteInventory,
    route_id: str,
    evidence_prefix: str,
) -> str:
    """Reacquire one scan-proven typed route, tap it, and bind its exact editor."""
    match = CANONICAL_COLLECTION_ITEM_RESOURCE_ID.fullmatch(route_id)
    if match is None or route_id not in inventory.route_viewports:
        raise RuntimeError(f"Unknown scan-proven typed collection route {route_id!r}")
    target_viewport = inventory.route_viewports[route_id]
    if (
        isinstance(target_viewport, bool)
        or not isinstance(target_viewport, int)
        or target_viewport < 0
        or target_viewport > inventory.bottom_movement_swipes
    ):
        device.capture(f"{evidence_prefix}-viewport-invalid")
        raise RuntimeError("Typed collection route produced an invalid measured viewport")

    # Inventory finishes at the stable bottom. Gesture distances are not
    # reversible on Android ScrollView surfaces because clamping and settling
    # can make N downward gestures land above or below the viewport discovered
    # by N upward gestures. Rewind to the independently proven start and
    # reacquire the exact scan-proven resource ID in the same direction used
    # by the inventory instead of relying on inverse gesture arithmetic.
    rewind_surface_to_stable_start(
        device,
        evidence_prefix=f"{evidence_prefix}-route-reacquire",
    )
    previous_sha256: str | None = None
    unchanged = 0
    swipes = 0
    consecutive_empty_reads = 0
    node: UiNode | None = None
    saw_clipped_route = False
    while swipes <= COLLECTION_ROUTE_SCAN_MAX_SCROLLS:
        nodes = device.hierarchy()
        if not nodes:
            consecutive_empty_reads += 1
            if consecutive_empty_reads > 3:
                device.capture(f"{evidence_prefix}-fresh-empty-hierarchy")
                raise RuntimeError(
                    "Typed collection route reacquisition exhausted empty hierarchies"
                )
            time.sleep(0.75)
            continue
        consecutive_empty_reads = 0

        matches = [
            candidate
            for candidate in nodes
            if candidate.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            == route_id
        ]
        if len(matches) > 1:
            device.capture(f"{evidence_prefix}-fresh-cardinality-invalid")
            raise RuntimeError(
                f"Typed collection route {route_id!r} has fresh cardinality {len(matches)}"
            )
        if len(matches) == 1:
            candidate = matches[0]
            if (
                candidate.attributes.get("enabled") != "true"
                or candidate.attributes.get("clickable") != "true"
            ):
                device.capture(f"{evidence_prefix}-fresh-not-tappable")
                raise RuntimeError(
                    f"Typed collection route {route_id!r} was not freshly tappable"
                )
            if device.node_has_tappable_bounds(candidate):
                node = candidate
                break
            # A ScrollView hierarchy may expose the exact enabled/clickable
            # route while its bounds are still clipped just outside the
            # tappable viewport. Advance in the same bounded forward search;
            # never tap the clipped node or fall back to its text.
            saw_clipped_route = True

        hierarchy_sha256 = Device._hierarchy_sha256(nodes)
        unchanged = unchanged + 1 if hierarchy_sha256 == previous_sha256 else 0
        previous_sha256 = hierarchy_sha256
        if unchanged >= COLLECTION_ROUTE_SCAN_STABLE_REPEATS:
            if saw_clipped_route:
                device.capture(f"{evidence_prefix}-fresh-not-tappable")
                raise RuntimeError(
                    f"Typed collection route {route_id!r} never became freshly tappable"
                )
            device.capture(f"{evidence_prefix}-fresh-route-missing")
            raise RuntimeError(
                f"Typed collection route {route_id!r} was not found before the proven stable end"
            )
        if swipes >= COLLECTION_ROUTE_SCAN_MAX_SCROLLS:
            break
        device.swipe_up(
            x_ratio=0.5,
            distance_ratio=COLLECTION_ROUTE_SCAN_DISTANCE_RATIO,
        )
        swipes += 1
        time.sleep(0.2)
    if node is None:
        if saw_clipped_route:
            device.capture(f"{evidence_prefix}-fresh-not-tappable")
            raise RuntimeError(
                f"Typed collection route {route_id!r} never became freshly tappable"
            )
        device.capture(f"{evidence_prefix}-fresh-route-missing")
        raise RuntimeError(
            f"Typed collection route {route_id!r} was not found within the exact bound"
        )
    device.shell("input", "tap", *(str(value) for value in node.center))

    editor_id = (
        f"collection-editor-{match.group('kind')}-{match.group('item_id')}"
    )
    device.wait_for_single_exact_resource_id(
        editor_id,
        timeout=60,
        evidence_prefix=f"{evidence_prefix}-editor",
        surface_name="Typed collection editor route",
    )
    rewind_surface_to_stable_start(
        device,
        evidence_prefix=f"{evidence_prefix}-editor",
    )
    editor_nodes = device.hierarchy()
    editor_matches = [
        candidate
        for candidate in editor_nodes
        if candidate.attributes.get("resource-id", "").rsplit("/", 1)[-1]
        == editor_id
    ]
    if len(editor_matches) != 1:
        device.capture(f"{evidence_prefix}-editor-cardinality-invalid")
        raise RuntimeError(
            f"Typed collection editor {editor_id!r} has cardinality {len(editor_matches)}"
        )
    return match.group("item_id")


def reset_collection_editor_to_top(device: Device, profile: str) -> None:
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
    )


PHONE_BUILD_SECTION_ORDER = ("attributes", "combat", "gear", "relationships")
PHONE_BUILD_SECTIONS = frozenset(PHONE_BUILD_SECTION_ORDER)
PHONE_BUILD_SECTION_SCAN_MAX_SCROLLS = 32
# Sample overlapping root viewports.  A half-screen gesture can move a short
# section route from below the tappable viewport to above it without ever
# exposing the exact resource-id at tappable bounds (Combat is short enough to
# trigger that on the API 36 phone profile).  Keep inventory traversal aligned
# with the bounded fresh-route reacquisition quantum so every intervening typed
# route receives a measured viewport without accepting clipped semantics.
PHONE_BUILD_SECTION_SCAN_DISTANCE_RATIO = 0.22
PHONE_BUILD_SECTION_SCAN_STABLE_REPEATS = 2
PHONE_BUILD_SECTION_REACQUIRE_DISTANCE_RATIO = 0.22


@dataclass(frozen=True)
class PhoneBuildSectionInventory:
    viewport_by_section: dict[str, int]
    bottom_movement_swipes: int


def scan_phone_build_section_inventory(device: Device) -> PhoneBuildSectionInventory:
    """Inventory every canonical section route across one measured root traversal."""
    positions: dict[str, int] = {}
    semantics: dict[str, tuple[str, ...]] = {}
    previous_hierarchy_sha256: str | None = None
    unchanged = 0
    swipes = 0
    consecutive_empty_reads = 0
    while swipes <= PHONE_BUILD_SECTION_SCAN_MAX_SCROLLS:
        # The direct ``/dev/tty`` UIAutomator stream can replay the pre-swipe
        # viewport on API 36.  Section inventory is scroll-dependent, so use
        # the canonical dump-file hierarchy for every measured viewport.
        nodes = device.hierarchy()
        if not nodes:
            consecutive_empty_reads += 1
            if consecutive_empty_reads > 3:
                device.capture("phone-build-section-inventory-empty-hierarchy")
                raise RuntimeError(
                    "Phone Build section inventory exhausted transient empty hierarchy reads"
                )
            time.sleep(0.75)
            continue
        consecutive_empty_reads = 0

        for section in PHONE_BUILD_SECTION_ORDER:
            selector = f"build-section-tab-{section}"
            matches = [
                node
                for node in nodes
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(matches) > 1:
                device.capture(f"{section}-section-route-cardinality-invalid")
                raise RuntimeError(
                    f"{section.title()} section route {selector!r} has cardinality "
                    f"{len(matches)} in one root viewport"
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
            prior = semantics.setdefault(section, signature)
            if signature != prior:
                device.capture(f"{section}-section-route-drift")
                raise RuntimeError(
                    f"{section.title()} section route changed semantics during root inventory"
                )
            if (
                node.attributes.get("enabled") != "true"
                or node.attributes.get("clickable") != "true"
            ):
                device.capture(f"{section}-section-route-not-enabled")
                raise RuntimeError(
                    f"{section.title()} section route was not enabled and clickable"
                )
            if device.node_has_tappable_bounds(node):
                positions.setdefault(section, swipes)

        hierarchy_sha256 = Device._hierarchy_sha256(nodes)
        unchanged = (
            unchanged + 1
            if hierarchy_sha256 == previous_hierarchy_sha256
            else 0
        )
        previous_hierarchy_sha256 = hierarchy_sha256
        if unchanged >= PHONE_BUILD_SECTION_SCAN_STABLE_REPEATS:
            missing = [
                section
                for section in PHONE_BUILD_SECTION_ORDER
                if section not in positions
            ]
            if missing:
                device.capture("phone-build-section-inventory-incomplete")
                raise RuntimeError(
                    "Phone Build root reached its stable end without one tappable exact "
                    f"route for every section; missing={missing!r}"
                )
            return PhoneBuildSectionInventory(
                viewport_by_section=dict(positions),
                bottom_movement_swipes=(
                    swipes - PHONE_BUILD_SECTION_SCAN_STABLE_REPEATS
                ),
            )
        if swipes >= PHONE_BUILD_SECTION_SCAN_MAX_SCROLLS:
            break
        device.swipe_up(
            x_ratio=0.5,
            distance_ratio=PHONE_BUILD_SECTION_SCAN_DISTANCE_RATIO,
        )
        swipes += 1
        time.sleep(0.75)

    device.capture("phone-build-section-inventory-end-unproven")
    raise RuntimeError(
        "Phone Build section inventory did not prove the outer root surface end "
        f"within {PHONE_BUILD_SECTION_SCAN_MAX_SCROLLS} gestures"
    )


def tap_phone_build_section(device: Device, section: str) -> None:
    """Activate one canonical phone Build section from the proven Build root.

    Section routes do not exist on nested collection pages.  Bind the immutable
    root page, toolbar, and exactly one lifecycle marker before searching for the
    requested exact resource ID.  ``created=None`` is intentional: this helper is
    shared by the created full-editing journey and the uncreated contact/pet
    journey, while the root binder still requires one unambiguous lifecycle
    marker.  The bound root is already at its measured top viewport, so a blind
    downward reset would only add latency and could move the search off authority.
    """
    if section not in PHONE_BUILD_SECTIONS:
        raise RuntimeError(f"Unsupported canonical phone Build section {section!r}")
    return_to_phone_runner_root(device, created=None)
    inventory = scan_phone_build_section_inventory(device)
    target_viewport = inventory.viewport_by_section[section]
    reverse_swipes = inventory.bottom_movement_swipes - target_viewport
    if reverse_swipes < 0:
        device.capture(f"{section}-section-route-viewport-invalid")
        raise RuntimeError(
            f"{section.title()} section route inventory produced an invalid viewport delta"
        )
    for _ in range(reverse_swipes):
        device.swipe_down(
            x_ratio=0.5,
            distance_ratio=PHONE_BUILD_SECTION_SCAN_DISTANCE_RATIO,
        )
        time.sleep(0.2)
    if reverse_swipes > 0:
        time.sleep(0.75)

    selector = f"build-section-tab-{section}"
    node = acquire_fresh_phone_build_section_route(
        device,
        section=section,
        selector=selector,
        measured_target_viewport=target_viewport,
    )
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))


def acquire_fresh_phone_build_section_route(
    device: Device,
    *,
    section: str,
    selector: str,
    measured_target_viewport: int,
) -> UiNode:
    """Bind an exact section node after every gesture/state transition.

    The measured reverse delta remains the fast path, but it is never accepted
    without a fresh hierarchy.  If that viewport no longer contains the exact
    route (MAUI can preserve a different offset after navigation), invalidate
    the measurement, prove the current root's stable start, then use bounded
    overlapping forward snapshots.  Text, stale nodes, and blind extra taps are
    never fallbacks.
    """
    if measured_target_viewport < 0:
        raise RuntimeError("Measured phone Build section viewport is invalid")

    def exact_from(nodes: list[UiNode]) -> UiNode | None:
        matches = [
            node
            for node in nodes
            if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
            == selector
        ]
        if len(matches) > 1:
            device.capture(f"{section}-section-route-fresh-cardinality-invalid")
            raise RuntimeError(
                f"Measured {section.title()} section viewport exposed cardinality "
                f"{len(matches)} for exact route {selector!r}; expected at most one"
            )
        if len(matches) != 1:
            return None
        node = matches[0]
        if (
            node.attributes.get("enabled") != "true"
            or node.attributes.get("clickable") != "true"
        ):
            device.capture(f"{section}-section-route-fresh-not-enabled")
            raise RuntimeError(
                f"Measured {section.title()} section route was not freshly enabled and clickable"
            )
        return node if device.node_has_tappable_bounds(node) else None

    measured = exact_from(device.hierarchy())
    if measured is not None:
        return measured

    # A zero-cardinality or clipped measured viewport invalidates that
    # observation.  Rebind the exact root scroll origin before any new search.
    rewind_surface_to_stable_start(
        device,
        evidence_prefix=f"{section}-section-route-reacquire",
    )
    max_forward_swipes = min(
        PHONE_BUILD_SECTION_SCAN_MAX_SCROLLS * 3,
        max(8, (measured_target_viewport + 2) * 3),
    )
    for forward_swipes in range(max_forward_swipes + 1):
        node = exact_from(device.hierarchy())
        if node is not None:
            return node
        if forward_swipes >= max_forward_swipes:
            break
        device.swipe_up(
            x_ratio=0.5,
            distance_ratio=PHONE_BUILD_SECTION_REACQUIRE_DISTANCE_RATIO,
        )
        time.sleep(0.2)
    device.capture(f"{section}-section-route-fresh-cardinality-invalid")
    raise RuntimeError(
        f"Measured {section.title()} section viewport exposed cardinality 0 for "
        f"exact route {selector!r} after bounded fresh reacquisition"
    )


def open_attribute_section(
    device: Device,
    profile: str,
    attribute_token: str = "body",
) -> None:
    if profile == "tablet":
        reset_scroll_to_top(device, x_ratio=0.15, swipes=24)
        device.tap("tablet-build-tab-tab-attributes", scroll=True)
        reset_scroll_to_top(device, x_ratio=0.375)
        device.wait(f"tablet-attribute-{attribute_token}", timeout=45)
        device.tap(f"tablet-attribute-{attribute_token}")
        return
    tap_phone_build_section(device, "attributes")
    reset_scroll_to_top(device)
    device.wait(
        f"attribute-{attribute_token}",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )


def read_body_total(device: Device, profile: str) -> int:
    selector = "tablet-attribute-bod" if profile == "tablet" else "attribute-bod"
    node = device.wait(
        selector,
        timeout=90,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    description = node.attributes.get("content-desc", "").strip()
    match = BODY_TOTAL_DESCRIPTION.match(description)
    if match is None:
        device.capture(f"{profile}-body-total-unavailable")
        raise RuntimeError(
            f"BOD row did not expose an authoritative Body total: {description!r}"
        )
    return int(match.group("total"))


def wait_exact_text(
    device: Device,
    expected: str,
    *,
    timeout: int,
) -> None:
    node = device.wait(
        expected,
        timeout=timeout,
        scroll=True,
        max_scrolls=12,
        scroll_distance_ratio=0.22,
    )
    if node.attributes.get("text") != expected:
        device.capture("career-attribute-text-mismatch")
        raise RuntimeError(
            f"Expected exact career attribute text {expected!r}, "
            f"got {node.attributes.get('text', '')!r}"
        )


def assert_body_total(device: Device, profile: str, expected: int) -> None:
    open_attribute_section(device, profile, "bod")
    actual = read_body_total(device, profile)
    if actual != expected:
        device.capture(f"{profile}-body-total-not-persisted")
        raise RuntimeError(
            f"Career Body total did not persist in the {profile} editor; "
            f"expected {expected}, got {actual}"
        )


def improve_body_in_career(
    device: Device,
    profile: str,
    contract: FullEditingFixtureContract,
) -> None:
    open_attribute_section(device, profile, "bod")
    before = read_body_total(device, profile)
    if before != contract.initial_body_total:
        device.capture(f"{profile}-body-total-before-improvement-invalid")
        raise RuntimeError(
            "Imported career BOD did not match its validated fixture total; "
            f"expected {contract.initial_body_total}, got {before}"
        )
    if profile == "phone":
        device.tap("attribute-bod", scroll=True)
        improve_selector = "attribute-improve-bod"
    else:
        improve_selector = "tablet-attribute-improve-bod"
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=6,
    )
    wait_exact_text(
        device,
        f"Available Karma: {contract.initial_karma}",
        timeout=45,
    )
    wait_exact_text(
        device,
        f"Improve · {contract.improvement_cost} Karma",
        timeout=45,
    )
    device.wait(improve_selector, timeout=45, scroll=True)
    device.tap(improve_selector, scroll=True)
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=6,
    )
    wait_exact_text(
        device,
        f"Available Karma: {contract.remaining_karma}",
        timeout=90,
    )
    wait_exact_text(
        device,
        f"Improve · {contract.next_improvement_cost} Karma",
        timeout=90,
    )
    if profile == "phone":
        device.back()
    after = read_body_total(device, profile)
    if after != contract.improved_body_total:
        device.capture(f"{profile}-body-total-after-improvement-invalid")
        raise RuntimeError(
            "Career BOD improvement did not produce the expected total; "
            f"expected {contract.improved_body_total}, got {after}"
        )


def open_condition_monitor_section(device: Device, profile: str) -> None:
    if profile == "tablet":
        reset_scroll_to_top(device, x_ratio=0.15, swipes=24)
        device.tap(
            "tablet-build-tab-tab-combat",
            scroll=True,
            timeout=120,
            max_scrolls=24,
            scroll_distance_ratio=0.22,
        )
        device.tap(
            "tablet-build-action-tab-combat-conditionmonitor",
            timeout=120,
            scroll=True,
            max_scrolls=24,
            scroll_distance_ratio=0.22,
        )
        device.wait(
            "tablet-condition-track-physical",
            timeout=120,
            scroll=True,
            max_scrolls=24,
            scroll_distance_ratio=0.22,
        )
        return
    tap_phone_build_section(device, "combat")
    device.tap(
        "build-action-tab-combat-conditionmonitor",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.wait(
        "condition-monitor-physical",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )


def condition_picker_selector(profile: str, track: str) -> str:
    prefix = "tablet-condition" if profile == "tablet" else "condition-monitor"
    return f"{prefix}-filled-{track}"


def edit_condition_damage(
    device: Device,
    profile: str,
    track: str,
    value: int,
) -> None:
    open_condition_monitor_section(device, profile)
    if profile == "tablet":
        device.tap(f"tablet-condition-track-{track}", scroll=True)
    else:
        device.tap(f"condition-monitor-{track}", scroll=True)
        device.wait(f"condition-monitor-editor-{track}", timeout=45)

    picker = condition_picker_selector(profile, track)
    device.tap(picker, scroll=True)
    device.tap(str(value), scroll=True)
    save = (
        f"tablet-condition-save-{track}"
        if profile == "tablet"
        else f"condition-monitor-save-{track}"
    )
    device.tap(save, scroll=True)
    time.sleep(1)
    actual = selected_text(device, picker, "Filled boxes", scroll=True)
    if actual != str(value):
        device.capture(f"{profile}-{track}-damage-not-applied")
        raise RuntimeError(
            f"{track.title()} damage did not apply in the {profile} editor; "
            f"expected {value}, got {actual!r}"
        )
    if profile == "phone":
        device.back()
        device.back()


def assert_condition_damage(
    device: Device,
    profile: str,
    track: str,
    expected: int,
) -> None:
    open_condition_monitor_section(device, profile)
    if profile == "tablet":
        device.tap(f"tablet-condition-track-{track}", scroll=True)
    else:
        device.tap(f"condition-monitor-{track}", scroll=True)
        device.wait(f"condition-monitor-editor-{track}", timeout=45)

    picker = condition_picker_selector(profile, track)
    actual = selected_text(device, picker, "Filled boxes", scroll=True)
    if actual != str(expected):
        device.capture(f"{profile}-{track}-damage-not-persisted")
        raise RuntimeError(
            f"{track.title()} damage did not persist in the {profile} editor; "
            f"expected {expected}, got {actual!r}"
        )
    if profile == "phone":
        device.back()
        device.back()


def open_gear_section(device: Device, profile: str) -> None:
    if profile == "tablet":
        reset_scroll_to_top(device, x_ratio=0.15, swipes=24)
        device.tap("tablet-build-tab-tab-gear", scroll=True)
        reset_scroll_to_top(device, x_ratio=0.375, swipes=12)
        device.tap("tablet-build-action-tab-gear-gear", scroll=True)
        device.wait(
            "tablet-quick-gear-add",
            timeout=180,
            scroll=True,
            max_scrolls=48,
            scroll_distance_ratio=0.22,
        )
        return
    tap_phone_build_section(device, "gear")
    device.wait_exact_resource_id_bidirectional(
        "section-quick-gear-add",
        timeout=180,
        backward_scrolls=24,
        forward_scrolls=48,
        scroll_distance_ratio=0.22,
    )


def open_contact_section(
    device: Device,
    profile: str,
    *,
    expected_item: str | None = None,
) -> None:
    if profile == "tablet":
        reset_scroll_to_top(device, x_ratio=0.15, swipes=24)
        device.tap("tablet-build-tab-tab-relationships", scroll=True)
        time.sleep(5)
        reset_scroll_to_top(device, x_ratio=0.375, swipes=12)
        if expected_item is not None:
            device.tap(
                "tablet-build-action-tab-relationships-contacts",
                scroll=True,
                timeout=180,
                max_scrolls=48,
                scroll_distance_ratio=0.22,
            )
            time.sleep(2)
            device.wait(
                expected_item,
                timeout=60,
                scroll=True,
                max_scrolls=8,
                scroll_distance_ratio=0.22,
            )
            return
        device.tap("tablet-build-action-tab-relationships-contacts", scroll=True)
        device.wait(
            "tablet-quick-contact-add",
            timeout=180,
            scroll=True,
            max_scrolls=48,
            scroll_distance_ratio=0.22,
        )
        return
    _open_phone_relationship_collection(
        device,
        action_selector="build-action-tab-relationships-contacts",
        quick_add_selector="section-quick-contact-add",
        expected_item=expected_item,
    )


def open_pet_section(
    device: Device,
    profile: str,
    *,
    expected_item: str | None = None,
) -> None:
    if profile == "tablet":
        reset_scroll_to_top(device, x_ratio=0.15, swipes=24)
        device.tap("tablet-build-tab-tab-relationships", scroll=True)
        time.sleep(5)
        reset_scroll_to_top(device, x_ratio=0.375, swipes=12)
        if expected_item is not None:
            device.tap(
                "tablet-build-action-tab-relationships-pets",
                scroll=True,
                timeout=180,
                max_scrolls=48,
                scroll_distance_ratio=0.22,
            )
            time.sleep(2)
            device.wait(
                expected_item,
                timeout=60,
                scroll=True,
                max_scrolls=8,
                scroll_distance_ratio=0.22,
            )
            return
        device.tap("tablet-build-action-tab-relationships-pets", scroll=True)
        device.wait(
            "tablet-quick-contact-add",
            timeout=180,
            scroll=True,
            max_scrolls=48,
            scroll_distance_ratio=0.22,
        )
        return
    _open_phone_relationship_collection(
        device,
        action_selector="build-action-tab-relationships-pets",
        quick_add_selector="section-quick-contact-add",
        expected_item=expected_item,
    )


def _open_phone_relationship_collection(
    device: Device,
    *,
    action_selector: str,
    quick_add_selector: str,
    expected_item: str | None,
) -> None:
    tap_phone_build_section(device, "relationships")
    time.sleep(5)
    device.tap_exact_resource_id_bidirectional(
        action_selector,
        timeout=180,
        backward_scrolls=24,
        forward_scrolls=48,
        scroll_distance_ratio=0.22,
        evidence_prefix="relationships-collection-route",
        surface_name="Relationships collection route",
    )
    time.sleep(2)
    if expected_item is not None:
        reset_scroll_to_top(device, swipes=24)
        device.wait(
            expected_item,
            timeout=60,
            scroll=True,
            max_scrolls=24,
            scroll_distance_ratio=0.22,
        )
        return
    empty_marker = "No entries yet. Use an action above to add one."
    reset_scroll_to_top(device, swipes=24)
    marker_node = device.wait(
        empty_marker,
        timeout=60,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    if marker_node.attributes.get("text") != empty_marker:
        device.capture("relationship-collection-empty-marker-mismatch")
        raise RuntimeError(
            "Relationship collection action did not activate its exact empty state; "
            f"expected {empty_marker!r}, got {marker_node.attributes.get('text', '')!r}"
        )
    device.wait_exact_resource_id_bidirectional(
        quick_add_selector,
        timeout=180,
        backward_scrolls=24,
        forward_scrolls=48,
        scroll_distance_ratio=0.22,
    )


def ensure_checked(device: Device, selector: str, expected: bool = True) -> None:
    node = device.wait(
        selector,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    checked = node.attributes.get("checked") == "true"
    if checked != expected:
        device.tap(
            selector,
            scroll=True,
            max_scrolls=20,
            scroll_distance_ratio=0.22,
        )
        time.sleep(0.5)
        updated = device.wait(
            selector,
            scroll=True,
            max_scrolls=20,
            scroll_distance_ratio=0.22,
        )
        if (updated.attributes.get("checked") == "true") != expected:
            device.capture("toggle-state-failed")
            raise RuntimeError(f"Toggle {selector!r} did not change to {expected}")


def assert_toggle_state(
    device: Device,
    selector: str,
    *,
    checked: bool,
    enabled: bool | None = None,
    capture: str = "toggle-state-unexpected",
) -> None:
    node = device.wait(
        selector,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    expected_checked = "true" if checked else "false"
    expected_enabled = None if enabled is None else ("true" if enabled else "false")
    actual_checked = node.attributes.get("checked")
    actual_enabled = node.attributes.get("enabled")
    if actual_checked != expected_checked or (
        expected_enabled is not None and actual_enabled != expected_enabled
    ):
        device.capture(capture)
        raise RuntimeError(
            f"Toggle {selector!r} state mismatch: expected checked={checked}"
            + ("" if enabled is None else f", enabled={enabled}")
            + f"; got checked={actual_checked!r}, enabled={actual_enabled!r}"
        )


def selected_text(device: Device, selector: str, label: str, *, scroll: bool = False) -> str:
    node = None
    attempts = 0
    while node is None and attempts < (20 if scroll else 1):
        node = device.find(selector)
        if node is None and scroll:
            device.swipe_up(
                x_ratio=device._scroll_x_ratio(selector),
                distance_ratio=0.22,
            )
            time.sleep(0.75)
        attempts += 1
    if node is None:
        device.capture("missing-contact-value")
        raise RuntimeError(f"Could not read contact field {selector!r}")
    return node.attributes.get("text", "")


def assert_linked_identity(device: Device, profile: str, kind: str) -> None:
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
    )
    prefix = "tablet" if profile == "tablet" else "collection"
    expected = [
        (f"{prefix}-field-name", "Name", "NeonFoxE2E"),
        (f"{prefix}-field-metatype", "Metatype", "Elf (Dryad)"),
    ]
    if kind == "contact":
        expected.extend(
            [
                (f"{prefix}-field-gender", "Gender", "NonbinaryE2E"),
                (f"{prefix}-field-age", "Age", "29"),
            ]
        )
    for selector, label, value in expected:
        actual = selected_text(device, selector, label, scroll=True)
        node = device.find(selector)
        enabled = None if node is None else node.attributes.get("enabled")
        if actual != value or enabled != "false":
            device.capture(f"{profile}-{kind}-linked-identity-failed")
            raise RuntimeError(
                f"Linked {kind} identity {label!r} was not projected read-only: "
                f"expected {value!r}, got {actual!r}, enabled={enabled!r}"
            )


def _documents_ui_exact_nodes(nodes: list[UiNode], value: str) -> list[UiNode]:
    """Return exact DocumentsUI nodes without the driver's prefix fallback."""
    return [
        node
        for node in nodes
        if node.attributes.get("package") == DOCUMENTS_UI_PACKAGE
        and value
        in {
            node.attributes.get("text", ""),
            node.attributes.get("content-desc", ""),
            node.attributes.get("resource-id", "").rsplit("/", 1)[-1],
        }
    ]


def _capture_documents_ui_before_deadline(
    device: Device,
    name: str,
    *,
    deadline: float,
) -> bool:
    """Start transition diagnostics only while the caller's budget remains."""
    if time.monotonic() >= deadline:
        return False
    try:
        device.capture(name, deadline=deadline)
    except Exception:
        # Evidence is best-effort and must never mask the caller's semantic error.
        return False
    return time.monotonic() < deadline


def _documents_ui_downloads_state(
    device: Device,
    nodes: list[UiNode],
    *,
    deadline: float,
) -> str:
    drawer_markers = _documents_ui_exact_nodes(nodes, DOCUMENTS_UI_DRAWER_MARKER)
    destinations = _documents_ui_exact_nodes(
        nodes,
        DOCUMENTS_UI_DOWNLOADS_DESTINATION,
    )
    if len(drawer_markers) > 1 or len(destinations) > 1:
        _capture_documents_ui_before_deadline(
            device,
            "documentsui-downloads-transition-cardinality-invalid",
            deadline=deadline,
        )
        raise RuntimeError(
            "DocumentsUI Downloads transition exposed ambiguous exact drawer or "
            "destination authority"
        )

    wrong_destinations = sorted(
        {
            value
            for node in nodes
            if node.attributes.get("package") == DOCUMENTS_UI_PACKAGE
            for value in (
                node.attributes.get("text", ""),
                node.attributes.get("content-desc", ""),
            )
            if value.startswith("Files in ")
            and value != DOCUMENTS_UI_DOWNLOADS_DESTINATION
        }
    )
    if wrong_destinations:
        _capture_documents_ui_before_deadline(
            device,
            "documentsui-downloads-wrong-destination",
            deadline=deadline,
        )
        raise RuntimeError(
            "DocumentsUI opened a root other than the exact Downloads destination: "
            f"{wrong_destinations!r}"
        )
    if drawer_markers and destinations:
        _capture_documents_ui_before_deadline(
            device,
            "documentsui-downloads-transition-state-ambiguous",
            deadline=deadline,
        )
        raise RuntimeError(
            "DocumentsUI simultaneously exposed the roots drawer and Downloads "
            "destination"
        )
    if destinations:
        return "destination"
    if drawer_markers:
        return "drawer"
    return "pending"


def _exact_enabled_documents_ui_downloads_row(
    device: Device,
    nodes: list[UiNode],
    *,
    deadline: float,
) -> UiNode:
    matches = [
        node
        for node in _documents_ui_exact_nodes(nodes, DOCUMENTS_UI_DOWNLOADS_ROOT)
        if node.attributes.get("resource-id", "").rsplit("/", 1)[-1] == "title"
    ]
    if len(matches) != 1:
        _capture_documents_ui_before_deadline(
            device,
            "documentsui-downloads-row-cardinality-invalid",
            deadline=deadline,
        )
        raise RuntimeError(
            "DocumentsUI Downloads root row cardinality was "
            f"{len(matches)}; expected exactly one"
        )
    node = matches[0]
    if (
        node.attributes.get("enabled") != "true"
        or not device.node_has_tappable_bounds(node, deadline=deadline)
    ):
        _capture_documents_ui_before_deadline(
            device,
            "documentsui-downloads-row-not-enabled-tappable",
            deadline=deadline,
        )
        raise RuntimeError(
            "The exact DocumentsUI Downloads root row is not enabled and tappable"
        )
    return node


def _documents_ui_observation_before_deadline(
    device: Device,
    *,
    deadline: float,
) -> list[UiNode] | None:
    if time.monotonic() >= deadline:
        return None
    try:
        nodes = device.hierarchy(deadline=deadline)
    except AdbOperationDeadlineExceeded:
        return None
    # A hierarchy observation is blocking. Its result cannot authorize a later
    # mutation if it completed after the caller-owned transition deadline.
    if time.monotonic() >= deadline:
        return None
    return nodes


def _documents_ui_sleep_before_deadline(deadline: float) -> bool:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        return False
    time.sleep(min(DOCUMENTS_UI_POLL_DELAY_SECONDS, remaining))
    return time.monotonic() < deadline


def select_documents_ui_downloads_root(device: Device, *, timeout: int = 45) -> None:
    """Select Downloads with at most two fresh retaps under one deadline."""
    deadline = time.monotonic() + timeout
    last_state = "pending"
    for tap_attempt in range(DOCUMENTS_UI_MAX_DOWNLOADS_TAPS):
        nodes: list[UiNode] = []
        while True:
            observed = _documents_ui_observation_before_deadline(
                device,
                deadline=deadline,
            )
            if observed is None:
                break
            nodes = observed
            if not nodes:
                if not _documents_ui_sleep_before_deadline(deadline):
                    break
                continue
            last_state = _documents_ui_downloads_state(
                device,
                nodes,
                deadline=deadline,
            )
            if last_state == "destination":
                return
            if last_state == "drawer":
                break
            if not _documents_ui_sleep_before_deadline(deadline):
                break
        if not nodes or time.monotonic() >= deadline:
            break

        row = _exact_enabled_documents_ui_downloads_row(
            device,
            nodes,
            deadline=deadline,
        )
        x, y = row.center
        # Bounds/display acquisition may itself block. Recheck immediately before
        # issuing the non-replayable tap and pass the same deadline into ADB.
        tap_timeout = _remaining_operation_timeout(
            deadline=deadline,
            maximum=120,
        )
        device.shell(
            "input",
            "tap",
            str(x),
            str(y),
            timeout=tap_timeout,
            deadline=deadline,
        )

        retry_at = min(
            deadline,
            time.monotonic() + DOCUMENTS_UI_DOWNLOADS_RETRY_SETTLE_SECONDS,
        )
        while True:
            observed = _documents_ui_observation_before_deadline(
                device,
                deadline=deadline,
            )
            if observed is None:
                break
            nodes = observed
            if not nodes:
                if not _documents_ui_sleep_before_deadline(deadline):
                    break
                continue
            last_state = _documents_ui_downloads_state(
                device,
                nodes,
                deadline=deadline,
            )
            if last_state == "destination":
                return
            if (
                last_state == "drawer"
                and tap_attempt + 1 < DOCUMENTS_UI_MAX_DOWNLOADS_TAPS
                and time.monotonic() >= retry_at
            ):
                # The prior tap had no observable effect. Reacquire the exact row
                # from a fresh hierarchy before issuing the next bounded retap.
                break
            if not _documents_ui_sleep_before_deadline(deadline):
                break

    _capture_documents_ui_before_deadline(
        device,
        "documentsui-downloads-transition-unavailable",
        deadline=deadline,
    )
    if last_state == "drawer":
        raise RuntimeError(
            "DocumentsUI roots drawer remained open after "
            f"{DOCUMENTS_UI_MAX_DOWNLOADS_TAPS} exact Downloads taps"
        )
    raise RuntimeError(
        "Timed out waiting for the exact DocumentsUI Downloads destination"
    )


def select_android_document(device: Device, filename: str) -> None:
    roots_drawer_open = (
        device.find("Recent") is not None
        and device.find("Documents") is not None
    )
    if roots_drawer_open:
        width, height = device.display_size()
        device.shell(
            "input",
            "tap",
            str(int(round(width * 0.75))),
            str(int(round(height * 0.5))),
        )
        time.sleep(0.75)

    if device.find(filename) is None:
        device.wait("Show roots", timeout=45)
        device.tap("Show roots")
        time.sleep(0.75)
        select_documents_ui_downloads_root(device, timeout=45)
        device.wait(filename, timeout=45, scroll=True)
    device.tap(filename, scroll=True)


def normalize_component(value: str) -> str | None:
    match = COMPONENT.fullmatch(value.strip())
    if match is None:
        return None
    package = match.group("package")
    activity = match.group("activity")
    if activity.startswith("."):
        activity = f"{package}{activity}"
    return f"{package}/{activity}"


def launcher_component(device: Device) -> str:
    package_paths = device.shell("pm", "path", "--user", "current", PACKAGE)
    installed_paths = [
        line.removeprefix("package:").strip()
        for line in package_paths.splitlines()
        if line.startswith("package:") and line.removeprefix("package:").strip()
    ]
    if not installed_paths:
        raise RuntimeError(f"The exact E2E package is not installed: {PACKAGE}")

    output = device.shell(
        "cmd",
        "package",
        "resolve-activity",
        "--brief",
        "--user",
        "current",
        "-a",
        MAIN_ACTION,
        "-c",
        LAUNCHER_CATEGORY,
        "-p",
        PACKAGE,
    )
    components = {
        normalized
        for line in output.splitlines()
        if (normalized := normalize_component(line)) is not None
        and normalized.startswith(f"{PACKAGE}/")
    }
    if len(components) != 1:
        raise RuntimeError(
            "Expected exactly one installed launcher activity for "
            f"{PACKAGE}, got {sorted(components)!r}; resolver={output!r}"
        )
    return next(iter(components))


def resumed_activity(activity_dump: str) -> str | None:
    for line in activity_dump.splitlines():
        if "ResumedActivity" not in line and "topResumedActivity" not in line:
            continue
        matches = [normalize_component(match.group(0)) for match in COMPONENT.finditer(line)]
        components = [component for component in matches if component is not None]
        if components:
            return components[-1]
    return None


def _bounded_evidence(value: object) -> str:
    if value is None:
        return ""
    if isinstance(value, bytes):
        rendered = value.decode("utf-8", errors="replace")
    else:
        rendered = str(value)
    if len(rendered) <= MAX_LAUNCH_EVIDENCE_CHARACTERS:
        return rendered
    return rendered[:MAX_LAUNCH_EVIDENCE_CHARACTERS] + "\n[launch evidence truncated]\n"


def _write_launch_evidence(device: Device, name: str, value: object) -> None:
    device.evidence.mkdir(parents=True, exist_ok=True)
    (device.evidence / name).write_text(_bounded_evidence(value), encoding="utf-8")


def _safe_shell(device: Device, *arguments: str, timeout: int = 30) -> str:
    try:
        return device.shell(*arguments, timeout=timeout)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        return "\n".join(
            part
            for part in (
                f"command failed: {error}",
                _bounded_evidence(getattr(error, "stdout", "")),
                _bounded_evidence(getattr(error, "stderr", "")),
            )
            if part
        )


def current_launch_state(device: Device) -> LaunchState:
    try:
        process_output = device.shell("pidof", PACKAGE, timeout=15)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
        process_output = ""
    process_ids = tuple(
        token for token in process_output.split() if PROCESS_ID.fullmatch(token)
    )
    activity_dump = _safe_shell(device, "dumpsys", "activity", "activities")
    return LaunchState(
        process_ids=process_ids,
        resumed_component=resumed_activity(activity_dump),
        activity_dump=activity_dump,
    )


def _launch_state_json(state: LaunchState) -> dict[str, object]:
    return {
        "processIds": list(state.process_ids),
        "resumedComponent": state.resumed_component,
    }


def capture_unknown_durable_save_outcome(
    device: Device,
    *,
    reason: str,
    expected: LaunchState,
    observed: LaunchState,
) -> None:
    """Capture the first unknown post-save state without attempting recovery.

    A save tap is non-idempotent from the driver's point of view. Once it has
    been sent, a process or foreground transition must be diagnosed in place;
    relaunching the task or replaying the tap could turn an unknown commit into
    a second mutation and could hide a product crash.
    """
    _write_new_json_receipt(
        device.evidence / "durable-save-outcome-failure.json",
        {
            "schema": DURABLE_SAVE_OUTCOME_FAILURE_SCHEMA,
            "status": "fail-closed",
            "reason": reason,
            "expected": _launch_state_json(expected),
            "observed": _launch_state_json(observed),
            "saveTapReplayAttempted": False,
            "foregroundRecoveryAttempted": False,
            "outcomeAuthority": "unknown-no-replay",
        },
    )
    def diagnostic_shell(*arguments: str) -> str:
        try:
            return _safe_shell(device, *arguments)
        except Exception as error:
            # Diagnostic transport loss must not replace the semantic unknown-
            # outcome failure which caused this capture.
            return f"diagnostic command failed: {_bounded_evidence(error)}"

    # Log buffers come first: later UIAutomator/dumpsys diagnostics can be noisy
    # enough to evict the short-lived crash event that this lane exists to retain.
    for buffer_name, arguments in (
        (
            "all",
            ("logcat", "-d", "-b", "all", "-v", "threadtime", "-t", "4000"),
        ),
        ("events", ("logcat", "-d", "-b", "events", "-v", "threadtime")),
        ("crash", ("logcat", "-d", "-b", "crash", "-v", "threadtime")),
    ):
        try:
            result = device.run(*arguments, timeout=60, check=False)
            output = _bounded_evidence(result.stdout)
            if result.stderr:
                output = (
                    f"{output}\n[logcat stderr]\n"
                    f"{_bounded_evidence(result.stderr)}"
                )
        except Exception as error:
            output = (
                f"logcat {buffer_name} capture failed: {error}\n"
                f"{_bounded_evidence(getattr(error, 'stdout', ''))}\n"
                f"{_bounded_evidence(getattr(error, 'stderr', ''))}"
            )
        _write_launch_evidence(
            device,
            f"durable-save-outcome-logcat-{buffer_name}.txt",
            output,
        )

    diagnostics = (
        ("durable-save-outcome-activity.txt", observed.activity_dump),
        (
            "durable-save-outcome-exit-info.txt",
            diagnostic_shell("dumpsys", "activity", "exit-info", PACKAGE),
        ),
        (
            "durable-save-outcome-lastanr.txt",
            diagnostic_shell("dumpsys", "activity", "lastanr"),
        ),
        (
            "durable-save-outcome-processes.txt",
            diagnostic_shell("dumpsys", "activity", "processes"),
        ),
        (
            "durable-save-outcome-window.txt",
            diagnostic_shell("dumpsys", "window", "windows"),
        ),
    )
    for name, value in diagnostics:
        _write_launch_evidence(device, name, value)
    try:
        fresh_hierarchy = device.run(
            *ADB_READ_ONLY_HIERARCHY_ARGUMENTS,
            timeout=30,
        ).stdout
        _write_launch_evidence(
            device,
            "durable-save-outcome-fresh-hierarchy.xml",
            fresh_hierarchy,
        )
    except Exception as error:
        _write_launch_evidence(
            device,
            "durable-save-outcome-fresh-hierarchy-error.txt",
            f"fresh read-only hierarchy capture failed: {_bounded_evidence(error)}",
        )
    try:
        device.capture("durable-save-outcome-failure")
    except Exception as error:
        _write_launch_evidence(
            device,
            "durable-save-outcome-capture-error.txt",
            error,
        )


def _is_exact_durable_save_toolbar(device: Device, node: UiNode) -> bool:
    return (
        node.attributes.get("resource-id") == ""
        and node.attributes.get("package") == PACKAGE
        and node.attributes.get("class") == "android.widget.Button"
        and node.attributes.get("content-desc") == "build-save-runner"
        and node.attributes.get("enabled") == "true"
        and node.attributes.get("clickable") == "true"
        and node.attributes.get("focusable") == "true"
        and device.node_has_tappable_bounds(node)
    )


def _launch_state_after_unknown_observation(
    device: Device,
    error: BaseException,
) -> LaunchState:
    """Best-effort read-only state for an already-unknown save outcome."""
    try:
        return current_launch_state(device)
    except Exception as observation_error:
        return LaunchState(
            (),
            None,
            "post-save state observation failed: "
            f"{type(error).__name__}: {_bounded_evidence(error)}; "
            "follow-up observation failed: "
            f"{type(observation_error).__name__}: "
            f"{_bounded_evidence(observation_error)}",
        )


def save_runner_and_wait_for_durable_notice(
    device: Device,
    *,
    timeout: float = 90,
) -> UiNode:
    """Issue one exact save and observe its outcome without further UI input.

    The durable notice lives in the always-visible toolbar, so scrolling is
    neither required nor safe after the mutation. Every post-tap operation is
    read-only. A disappeared/replaced process or a different resumed activity
    is an unknown save outcome and fails immediately with crash/exit evidence.
    """
    if timeout <= 0:
        raise ValueError("Durable save observation timeout must be positive")

    toolbar = device.wait_for_single_exact_accessibility_value(
        "build-save-runner",
        timeout=45,
        evidence_prefix="durable-save-toolbar",
        surface_name="Durable save toolbar control",
    )
    if (
        not _is_exact_durable_save_toolbar(device, toolbar)
        or toolbar.attributes.get("text") != "Save"
    ):
        device.capture("durable-save-toolbar-invalid")
        raise RuntimeError(
            "The exact durable save toolbar control does not have the required "
            "tappable Chummer button topology and exact pre-save text 'Save'"
        )

    expected = current_launch_state(device)
    if (
        len(expected.process_ids) != 1
        or expected.resumed_component is None
        or not expected.resumed_component.startswith(f"{PACKAGE}/")
    ):
        device.capture("durable-save-precondition-invalid")
        raise RuntimeError(
            "Chummer did not have exactly one PID and the exact foreground "
            "component before the save tap"
        )

    x, y = toolbar.center
    # Exactly one non-replayable mutation. Device.shell already suppresses
    # replay when an ADB outcome is unknown.
    try:
        device.shell("input", "tap", str(x), str(y))
    except AdbTransportError as error:
        observed = _launch_state_after_unknown_observation(device, error)
        capture_unknown_durable_save_outcome(
            device,
            reason="save-tap-transport-outcome-unknown",
            expected=expected,
            observed=observed,
        )
        raise RuntimeError(
            "Durable save outcome is unknown after the non-replayable save tap "
            "transport failed; no save replay was attempted"
        ) from error

    deadline = time.monotonic() + timeout
    observed = expected
    while time.monotonic() < deadline:
        try:
            observed = current_launch_state(device)
        except AdbTransportError as error:
            observed = _launch_state_after_unknown_observation(device, error)
            capture_unknown_durable_save_outcome(
                device,
                reason="post-save-launch-state-observation-failed",
                expected=expected,
                observed=observed,
            )
            raise RuntimeError(
                "Durable save outcome is unknown because post-save process/"
                "foreground observation failed; no save replay was attempted"
            ) from error
        if (
            observed.process_ids != expected.process_ids
            or observed.resumed_component != expected.resumed_component
        ):
            capture_unknown_durable_save_outcome(
                device,
                reason="process-or-foreground-authority-changed",
                expected=expected,
                observed=observed,
            )
            raise RuntimeError(
                "Durable save outcome is unknown because Chummer lost its exact "
                "process/foreground authority; no save replay or foreground "
                "recovery was attempted"
            )

        try:
            nodes = device.read_only_hierarchy()
            if nodes:
                device.dismiss_system_ui_anr(nodes)
        except (AdbTransportError, ProductAnrDetected) as error:
            capture_unknown_durable_save_outcome(
                device,
                reason=(
                    "post-save-hierarchy-observation-failed"
                    if isinstance(error, AdbTransportError)
                    else "post-save-product-anr-detected"
                ),
                expected=expected,
                observed=observed,
            )
            raise RuntimeError(
                "Durable save outcome is unknown because post-save hierarchy/"
                "ANR observation failed; no save replay was attempted"
            ) from error
        if not nodes:
            time.sleep(0.75)
            continue
        matches = [
            node
            for node in nodes
            if node.attributes.get("content-desc") == "build-save-runner"
            and node.attributes.get("package") == PACKAGE
        ]
        if len(matches) > 1:
            capture_unknown_durable_save_outcome(
                device,
                reason="durable-save-toolbar-cardinality-invalid",
                expected=expected,
                observed=observed,
            )
            raise RuntimeError(
                "Durable save toolbar has ambiguous post-mutation cardinality; "
                "the save outcome remains unknown"
            )
        if len(matches) == 1:
            match = matches[0]
            try:
                exact_toolbar = _is_exact_durable_save_toolbar(device, match)
            except AdbTransportError as error:
                capture_unknown_durable_save_outcome(
                    device,
                    reason="post-save-toolbar-observation-failed",
                    expected=expected,
                    observed=observed,
                )
                raise RuntimeError(
                    "Durable save outcome is unknown because exact post-save "
                    "toolbar observation failed; no save replay was attempted"
                ) from error
            if not exact_toolbar:
                capture_unknown_durable_save_outcome(
                    device,
                    reason="durable-save-toolbar-topology-invalid",
                    expected=expected,
                    observed=observed,
                )
                raise RuntimeError(
                    "Durable save toolbar changed to an invalid post-mutation "
                    "topology; the save outcome remains unknown"
                )
            if match.attributes.get("text") == "Saved.":
                try:
                    confirmed = current_launch_state(device)
                except AdbTransportError as error:
                    confirmed = _launch_state_after_unknown_observation(
                        device,
                        error,
                    )
                    capture_unknown_durable_save_outcome(
                        device,
                        reason="post-save-success-authority-observation-failed",
                        expected=expected,
                        observed=confirmed,
                    )
                    raise RuntimeError(
                        "Durable save outcome is unknown because final process/"
                        "foreground confirmation failed; no save replay was attempted"
                    ) from error
                if (
                    confirmed.process_ids != expected.process_ids
                    or confirmed.resumed_component != expected.resumed_component
                ):
                    capture_unknown_durable_save_outcome(
                        device,
                        reason="post-save-success-authority-changed",
                        expected=expected,
                        observed=confirmed,
                    )
                    raise RuntimeError(
                        "Durable save notice coincided with changed process/foreground "
                        "authority; the outcome remains unknown"
                    )
                return match
        time.sleep(0.75)

    capture_unknown_durable_save_outcome(
        device,
        reason="durable-save-notice-timeout",
        expected=expected,
        observed=observed,
    )
    raise RuntimeError(
        "Timed out observing the exact durable save notice without replaying "
        "the save or sending post-mutation UI input"
    )


def _package_crash_is_visible(logcat: str) -> bool:
    package_lines = [line for line in logcat.splitlines() if PACKAGE in line]
    return any(
        marker in line
        for line in package_lines
        for marker in (
            "FATAL EXCEPTION",
            "Fatal signal",
            "Force finishing activity",
            "ProcessRecord",
            "has died",
        )
    ) or (f"Process: {PACKAGE}" in logcat and "FATAL EXCEPTION" in logcat)


def capture_launch_diagnostics(
    device: Device,
    attempt: int,
    component: str,
    start_result: subprocess.CompletedProcess | None,
    start_error: BaseException | None,
    state: LaunchState,
) -> str:
    prefix = f"launch-attempt-{attempt}"
    _write_launch_evidence(
        device,
        f"{prefix}-contract.txt",
        "\n".join(
            (
                f"package={PACKAGE}",
                f"component={component}",
                f"process_ids={' '.join(state.process_ids)}",
                f"resumed_component={state.resumed_component or ''}",
            )
        )
        + "\n",
    )
    _write_launch_evidence(
        device,
        f"{prefix}-am-start.stdout.txt",
        (
            getattr(start_error, "stdout", "")
            if start_result is None
            else start_result.stdout
        ),
    )
    error_text = "" if start_error is None else repr(start_error)
    if start_result is None and getattr(start_error, "stderr", None):
        error_text = (
            f"{error_text}\n{_bounded_evidence(getattr(start_error, 'stderr', ''))}"
        ).strip()
    if start_result is not None and start_result.stderr:
        error_text = f"{error_text}\n{_bounded_evidence(start_result.stderr)}".strip()
    _write_launch_evidence(device, f"{prefix}-am-start.stderr.txt", error_text)
    _write_launch_evidence(device, f"{prefix}-activity.txt", state.activity_dump)
    _write_launch_evidence(
        device,
        f"{prefix}-window.txt",
        _safe_shell(device, "dumpsys", "window", "windows"),
    )
    _write_launch_evidence(
        device,
        f"{prefix}-exit-info.txt",
        _safe_shell(device, "dumpsys", "activity", "exit-info", PACKAGE),
    )
    diagnostic_logs: list[str] = []
    try:
        logcat_result = device.run(
            "logcat",
            "-d",
            "-b",
            "all",
            "-v",
            "threadtime",
            "-t",
            "4000",
            timeout=60,
            check=False,
        )
        logcat = _bounded_evidence(logcat_result.stdout)
        if logcat_result.stderr:
            logcat = f"{logcat}\n[logcat stderr]\n{_bounded_evidence(logcat_result.stderr)}"
    except subprocess.TimeoutExpired as error:
        logcat = f"logcat capture timed out: {error}\n{_bounded_evidence(error.stdout)}"
    _write_launch_evidence(device, f"{prefix}-logcat.txt", logcat)
    diagnostic_logs.append(logcat)
    for buffer_name in ("events", "crash"):
        try:
            buffer_result = device.run(
                "logcat",
                "-d",
                "-b",
                buffer_name,
                "-v",
                "threadtime",
                timeout=60,
                check=False,
            )
            buffer_log = _bounded_evidence(buffer_result.stdout)
            if buffer_result.stderr:
                buffer_log = (
                    f"{buffer_log}\n[logcat stderr]\n"
                    f"{_bounded_evidence(buffer_result.stderr)}"
                )
        except subprocess.TimeoutExpired as error:
            buffer_log = (
                f"logcat {buffer_name} capture timed out: {error}\n"
                f"{_bounded_evidence(error.stdout)}"
            )
        _write_launch_evidence(
            device,
            f"{prefix}-logcat-{buffer_name}.txt",
            buffer_log,
        )
        diagnostic_logs.append(buffer_log)
    device.capture(f"{prefix}-failure")
    return "\n".join(diagnostic_logs)


def _start_output_matches_component(output: str, component: str) -> bool:
    lines = [line.strip() for line in output.splitlines()]
    statuses = [line.partition(":")[2].strip() for line in lines if line.startswith("Status:")]
    activities = [
        normalize_component(line.partition(":")[2].strip())
        for line in lines
        if line.startswith("Activity:")
    ]
    return statuses == ["ok"] and activities == [component]


def workspace_authority_start_arguments(component: str) -> tuple[str, ...]:
    """Build the only supported runtime authority opt-in intent.

    Android's ``--es`` creates a String extra. MainActivity intentionally reads
    a Boolean extra and therefore rejects that lookalike. Keeping ``--ez`` in
    one reusable builder prevents read-only continuations from silently
    disabling the fail-closed authority surface.
    """
    normalized = normalize_component(component)
    if normalized != component:
        raise RuntimeError(
            f"Workspace authority launch component is not canonical: {component!r}"
        )
    return (
        "shell",
        "am",
        "start",
        "--user",
        "current",
        "-W",
        "-a",
        MAIN_ACTION,
        "-c",
        LAUNCHER_CATEGORY,
        "--ez",
        E2E_AUTHORITY_EXTRA,
        "true",
        "-n",
        component,
    )


def _wait_for_resumed_component(
    device: Device,
    component: str,
    timeout: float,
) -> tuple[LaunchState, bool]:
    deadline = time.monotonic() + timeout
    saw_process = False
    while True:
        state = current_launch_state(device)
        if state.process_ids:
            saw_process = True
        if state.process_ids and state.resumed_component == component:
            return state, saw_process
        if saw_process and not state.process_ids:
            return state, saw_process
        if time.monotonic() >= deadline:
            return state, saw_process
        time.sleep(0.5)


def launch_app(device: Device, attempts: int = 3, resume_timeout: float = 20) -> LaunchState:
    if attempts < 1 or resume_timeout < 0:
        raise ValueError("Launch attempts must be positive and resume timeout nonnegative")
    component = launcher_component(device)
    for attempt in range(1, attempts + 1):
        device.run("logcat", "-c", timeout=30, check=False)
        start_result: subprocess.CompletedProcess | None = None
        start_error: BaseException | None = None
        try:
            # The journey owns its clear/force-stop lifecycle boundary. On API 36,
            # combining that stop with this start via `-S` can return Status: ok and
            # LaunchState: UNKNOWN without ever scheduling the package process.
            start_result = device.run(
                *workspace_authority_start_arguments(component),
                timeout=60,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            start_error = error

        start_stdout = (
            _bounded_evidence(getattr(start_error, "stdout", ""))
            if start_result is None
            else start_result.stdout
        )
        start_stderr = (
            "\n".join(
                part
                for part in (
                    repr(start_error),
                    _bounded_evidence(getattr(start_error, "stderr", "")),
                )
                if part
            )
            if start_result is None
            else start_result.stderr
        )
        _write_launch_evidence(
            device,
            f"launch-attempt-{attempt}-am-start.stdout.txt",
            start_stdout,
        )
        _write_launch_evidence(
            device,
            f"launch-attempt-{attempt}-am-start.stderr.txt",
            start_stderr,
        )

        command_succeeded = (
            start_result is not None
            and start_result.returncode == 0
            and _start_output_matches_component(start_result.stdout, component)
        )
        state, saw_process = _wait_for_resumed_component(
            device,
            component,
            resume_timeout,
        )
        wait_timed_out = isinstance(start_error, subprocess.TimeoutExpired)
        if (command_succeeded or wait_timed_out) \
            and state.process_ids \
            and state.resumed_component == component:
            _write_launch_evidence(
                device,
                f"launch-attempt-{attempt}-verified.txt",
                "\n".join(
                    (
                        f"package={PACKAGE}",
                        f"component={component}",
                        f"process_ids={' '.join(state.process_ids)}",
                        f"resumed_component={state.resumed_component}",
                    )
                )
                + "\n",
            )
            return state

        logcat = capture_launch_diagnostics(
            device,
            attempt,
            component,
            start_result,
            start_error,
            state,
        )
        if saw_process or _package_crash_is_visible(logcat):
            raise RuntimeError(
                "Chummer started a process but did not remain the exact resumed activity; "
                f"component={component!r}, process_ids={state.process_ids!r}, "
                f"resumed={state.resumed_component!r}"
            )
        if command_succeeded:
            raise RuntimeError(
                "Android reported a successful Chummer launch without an exact process/resumed "
                f"activity match; component={component!r}, resumed={state.resumed_component!r}"
            )
        if attempt == attempts:
            raise RuntimeError(
                "Android could not launch the exact Chummer component after "
                f"{attempts} attempts; component={component!r}"
            )
        time.sleep(3)


def force_stop_and_launch_new_process(
    device: Device,
    previous: LaunchState,
) -> ProcessRestartProof:
    if not previous.process_ids:
        raise RuntimeError("Process-restart proof requires an initial Chummer PID")

    before_force_stop = current_launch_state(device)
    if before_force_stop.process_ids != previous.process_ids \
        or before_force_stop.resumed_component != previous.resumed_component:
        device.capture("process-restart-precondition-changed")
        raise RuntimeError(
            "Chummer launch identity changed before the owned force-stop boundary: "
            f"launch_process_ids={previous.process_ids!r}, "
            f"live_process_ids={before_force_stop.process_ids!r}, "
            f"launch_resumed={previous.resumed_component!r}, "
            f"live_resumed={before_force_stop.resumed_component!r}"
        )

    device.shell("am", "force-stop", PACKAGE)
    after_force_stop = current_launch_state(device)
    if after_force_stop.process_ids:
        device.capture("process-restart-force-stop-not-empty")
        raise RuntimeError(
            "Chummer package PID set remained non-empty after force-stop: "
            + " ".join(after_force_stop.process_ids)
        )

    restarted = launch_app(device)
    reused = sorted(set(before_force_stop.process_ids).intersection(restarted.process_ids))
    if reused:
        device.capture("process-restart-pid-reused")
        raise RuntimeError(
            "Chummer process restart reused an existing PID instead of proving a new process: "
            + " ".join(reused)
        )

    _write_launch_evidence(
        device,
        "process-restart-verified.txt",
        "\n".join(
            (
                f"pre_force_stop_process_ids={' '.join(before_force_stop.process_ids)}",
                f"pre_force_stop_resumed_component={before_force_stop.resumed_component or ''}",
                f"post_force_stop_process_ids={' '.join(after_force_stop.process_ids)}",
                f"restart_process_ids={' '.join(restarted.process_ids)}",
                f"restart_resumed_component={restarted.resumed_component or ''}",
            )
        )
        + "\n",
    )
    return ProcessRestartProof(before_force_stop, after_force_stop, restarted)


def prepare_full_editing_runner(
    device: Device,
    profile: str,
    completed_runner_name: str,
    completed_runner_alias: str,
    completed_runner_sha256: str,
) -> WorkspaceAuthority | None:
    device.tap_exact_resource_id_until_exact_resource_id(
        "home-new-runner",
        "dialog-action-create-character",
        evidence_prefix="new-runner-build-method-dialog",
        source_name="New runner control",
        target_name="Create-character build-method action",
        target_scroll_surface="dialog-surface",
        max_target_scrolls=16,
    )
    device.tap_single_exact_resource_id(
        "dialog-action-create-character",
        timeout=45,
        scroll=True,
        max_scrolls=12,
        scroll_distance_ratio=0.22,
        evidence_prefix="new-runner-create-character-action",
        surface_name="Create-character build-method action",
    )

    creation_authority: WorkspaceAuthority | None = None
    if profile == "phone":
        # The authoritative bootstrap routes directly to the real Creation
        # Wizard. The retired legacy metatype dialog is not part of this journey.
        wait_for_phone_runner_route(device, created=False)
        # The unrestricted editor must remain unavailable until creation is complete.
        open_creation_dashboard(
            device,
            profile,
            open_build_route=False,
        )
        device.capture("new-runner-creation-wizard")
        # Importing another dossier is correctly blocked while the current workspace
        # is dirty. Persist this incomplete creation draft without claiming that the
        # creation workflow itself has completed, then switch to the signed fixture.
        save_runner_and_wait_for_durable_notice(device)
        tap_phone_destination(device, "phone-destination-runners")
        wait_for_phone_runners(device)
    else:
        # Tablet remains a standalone deferred journey and is not launched by the
        # authoritative phone beta lane.
        device.wait("Continue building", timeout=90)

    device.wait("home-open-file", timeout=90)
    if profile == "phone":
        creation_authority = read_workspace_authority(device)
        require_saved_authority(creation_authority)
    device.tap("home-open-file")
    select_android_document(device, completed_runner_name)
    # Bind the transition to the selected career fixture and its final phone route.
    # Picker dismissal, import failure, or a stale prior Profile cannot satisfy this.
    device.wait(completed_runner_alias, timeout=90)
    if profile == "phone":
        wait_for_phone_runner_route(device, created=True)
        tap_phone_destination(device, "phone-destination-runners")
        wait_for_phone_runners(device)
        imported_authority = read_workspace_authority(device)
        require_import_authority(
            imported_authority,
            completed_runner_sha256,
            creation_authority.workspace_id if creation_authority is not None else None,
        )
        return imported_authority
    device.wait("tablet-build-layout", timeout=90)
    return None


def attach_linked_runner(
    device: Device,
    profile: str,
    kind: str,
    original_name: str,
    *,
    validate_invalid: bool = False,
) -> None:
    tap_collection_item(device, original_name)
    attach_selector = "tablet-linked-attach" if profile == "tablet" else "collection-linked-attach-"
    status_selector = "tablet-linked-status" if profile == "tablet" else "collection-linked-status-"
    if validate_invalid:
        device.tap(attach_selector, scroll=True)
        select_android_document(device, "invalid-linked-runner-e2e.chum5")
        device.wait("Select a valid Chummer5 .chum5 or .chum5lz runner document.", timeout=45)
        device.tap("OK")

    device.tap(attach_selector, scroll=True)
    select_android_document(device, "linked-runner-e2e.chum5")
    device.wait(status_selector, timeout=60, scroll=True)
    assert_linked_identity(device, profile, kind)
    if profile == "phone":
        device.back()


def assert_link_persisted_then_remove(
    device: Device,
    profile: str,
    kind: str,
    original_name: str,
) -> None:
    opener = open_contact_section if kind == "contact" else open_pet_section
    opener(device, profile, expected_item="NeonFoxE2E")
    device.wait("NeonFoxE2E", timeout=60, scroll=True)
    tap_collection_item(device, "NeonFoxE2E")
    assert_linked_identity(device, profile, kind)
    remove_selector = "tablet-linked-remove" if profile == "tablet" else "collection-linked-remove-"
    status_selector = "tablet-linked-status" if profile == "tablet" else "collection-linked-status-"
    device.tap(remove_selector, scroll=True)
    device.wait("Remove linked runner?", timeout=30)
    device.tap("Remove link")
    name_selector = "tablet-field-name" if profile == "tablet" else "collection-field-name"
    time.sleep(0.75)
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
    )
    if device.find(name_selector) is None:
        device.wait(original_name, timeout=60, scroll=True)
        tap_collection_item(device, original_name)
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
    )
    name_node = device.wait(name_selector, scroll=True)
    restored = name_node.attributes.get("text", "")
    if restored != original_name or name_node.attributes.get("enabled") != "true":
        device.capture(f"{profile}-{kind}-unlink-restore-failed")
        raise RuntimeError(
            f"Unlink did not restore editable {kind} identity: "
            f"expected {original_name!r}, got {restored!r}, enabled={name_node.attributes.get('enabled')!r}"
        )
    device.wait(status_selector, timeout=60, scroll=True)
    if profile == "phone":
        device.back()


def add_and_edit_gear(device: Device, profile: str) -> str | None:
    open_gear_section(device, profile)
    original_routes: frozenset[str] = frozenset()
    if profile == "phone":
        before = scan_collection_route_inventory(
            device,
            kind="gear",
            evidence_prefix="gear-before-add",
        )
        original_routes = frozenset(before.route_viewports)
        rewind_surface_to_stable_start(device, evidence_prefix="gear-before-add-action")
        device.tap_single_exact_resource_id(
            "section-quick-gear-add",
            timeout=60,
            evidence_prefix="gear-add-action",
            surface_name="Exact gear add action",
        )
    else:
        device.tap("tablet-quick-gear-add", scroll=True)
    device.set_text(
        "dialog-field-uigearname",
        "Gear Name",
        "Ares Predator V",
        scroll=True,
        max_scrolls=32,
        scroll_distance_ratio=0.28,
    )
    device.tap(
        "dialog-action-add",
        scroll=True,
        timeout=180,
        max_scrolls=48,
        scroll_distance_ratio=0.28,
    )
    if profile == "tablet":
        reset_scroll_to_top(device, x_ratio=0.375, swipes=6)
        tap_collection_item(device, "Ares Predator V")
        device.wait("tablet-inspector-save", timeout=60, scroll=True)
        reset_scroll_to_top(device, x_ratio=0.82, swipes=12)
        device.set_text("tablet-field-customname", "Custom Name", "GearProofE2E")
        device.tap("tablet-inspector-save", scroll=True)
        reset_scroll_to_top(device, x_ratio=0.82, swipes=12)
        saved_custom_name = selected_text(
            device,
            "tablet-field-customname",
            "Custom Name",
            scroll=True,
        )
        if saved_custom_name != "GearProofE2E":
            device.capture("tablet-gear-custom-name-not-saved")
            raise RuntimeError(
                "Gear Custom Name was not saved in the tablet inspector: "
                f"expected 'GearProofE2E', got {saved_custom_name!r}"
            )
        return None

    after = scan_collection_route_inventory(
        device,
        kind="gear",
        evidence_prefix="gear-after-add",
    )
    current_routes = frozenset(after.route_viewports)
    added_routes = current_routes - original_routes
    missing_routes = original_routes - current_routes
    if len(added_routes) != 1 or missing_routes:
        device.capture("gear-new-route-delta-invalid")
        raise RuntimeError(
            "Gear add did not materialize exactly one new typed route while preserving "
            f"the baseline: added={sorted(added_routes)!r}, missing={sorted(missing_routes)!r}"
        )
    new_route = next(iter(added_routes))
    item_id = tap_typed_collection_route(
        device,
        inventory=after,
        route_id=new_route,
        evidence_prefix="gear-new-route",
    )
    custom_name_id = f"collection-field-customname-{item_id}"
    save_id = f"collection-save-{item_id}"
    device.set_text(custom_name_id, "Custom Name", "GearProofE2E")
    device.tap_exact_resource_id_bidirectional(
        save_id,
        timeout=90,
        backward_scrolls=0,
        forward_scrolls=32,
        scroll_distance_ratio=0.22,
        evidence_prefix="gear-save",
        surface_name="Exact typed gear save action",
    )
    rewind_surface_to_stable_start(device, evidence_prefix="gear-saved-editor")
    device.assert_text("GearProofE2E")
    device.back()
    return item_id


def add_contact_from_dialog(device: Device, profile: str, name: str, role: str) -> None:
    quick_add = "tablet-quick-contact-add" if profile == "tablet" else "section-quick-contact-add"
    device.tap(quick_add, scroll=True)
    device.wait(
        "dialog-action-add",
        timeout=180,
        scroll=True,
        max_scrolls=48,
        scroll_distance_ratio=0.28,
    )
    reset_scroll_to_top(device, x_ratio=0.375 if profile == "tablet" else 0.5, swipes=24)
    device.set_text("dialog-field-uicontactname", "Contact Name", name, scroll=True)
    device.set_text("dialog-field-uicontactrole", "Role", role, scroll=True)
    device.tap("dialog-action-add", scroll=True)
    device.wait(name, timeout=60, scroll=True)


def add_and_edit_contact(
    device: Device,
    profile: str,
    *,
    create_items: bool = True,
    connection_maximum: int = 6,
    free_editable: bool = True,
) -> None:
    open_contact_section(
        device,
        profile,
        expected_item=None if create_items else "ContactE2E",
    )
    if create_items:
        add_contact_from_dialog(device, profile, "ContactDeleteE2E", "DeleteRoleE2E")
        add_contact_from_dialog(device, profile, "ContactE2E", "InitialRoleE2E")
    tap_collection_item(device, "ContactE2E")
    reset_collection_editor_to_top(device, profile)

    prefix = "tablet" if profile == "tablet" else "collection"
    fields = (
        (f"{prefix}-field-name", "Name", "ContactPersistedE2E"),
        (f"{prefix}-field-notes", "Notes", "ContactNotesE2E"),
        (f"{prefix}-field-role", "Role", "FixerE2E"),
        (f"{prefix}-field-location", "Location", "ViennaE2E"),
        (f"{prefix}-field-metatype", "Metatype", "ElfE2E"),
        (f"{prefix}-field-gender", "Gender", "NonbinaryE2E"),
        (f"{prefix}-field-age", "Age", "42"),
        (f"{prefix}-field-contacttype", "Contact Type", "ProfessionalE2E"),
        (f"{prefix}-field-preferredpayment", "Preferred Payment", "CredstickE2E"),
        (f"{prefix}-field-hobbiesvice", "Hobbies Vice", "UrbanExplorerE2E"),
        (f"{prefix}-field-personallife", "Personal Life", "PrivateE2E"),
        (f"{prefix}-field-groupname", "Group Name", "NightMarketE2E"),
    )
    for selector, label, value in fields:
        device.set_text(
            selector,
            label,
            value,
            scroll=True,
            max_scrolls=20,
            scroll_distance_ratio=0.22,
        )

    connection_selector = (
        "tablet-contact-connection" if profile == "tablet" else "collection-contact-connection-"
    )
    loyalty_selector = "tablet-contact-loyalty" if profile == "tablet" else "collection-contact-loyalty-"
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
    )
    device.set_text(
        connection_selector,
        f"Connection · 1–{connection_maximum}",
        str(connection_maximum + 1),
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.tap("tablet-inspector-save" if profile == "tablet" else "Save changes", scroll=True)
    device.wait("Invalid Connection", timeout=30)
    device.tap("OK")
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
    )
    device.set_text(
        connection_selector,
        f"Connection · 1–{connection_maximum}",
        str(connection_maximum),
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )
    device.set_text(
        loyalty_selector,
        "Loyalty · 1–6",
        "5",
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.22,
    )

    save = "tablet-inspector-save" if profile == "tablet" else "Save changes"
    device.tap(save, scroll=True)
    time.sleep(1)
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
    )
    toggle_prefix = "tablet-toggle" if profile == "tablet" else "collection-toggle"
    editable_toggles = ("group", "family", "blackmail")
    for toggle in editable_toggles:
        ensure_checked(device, f"{toggle_prefix}-{toggle}")
    if free_editable:
        assert_toggle_state(
            device,
            f"{toggle_prefix}-free",
            checked=False,
            enabled=True,
            capture=f"{profile}-creation-contact-free-authority-invalid",
        )
    else:
        assert_toggle_state(
            device,
            f"{toggle_prefix}-free",
            checked=False,
            enabled=False,
            capture=f"{profile}-career-contact-free-authority-invalid",
        )
    device.tap(save, scroll=True)
    time.sleep(5)

    if profile == "phone":
        device.back()
        reset_scroll_to_top(device, swipes=12)
        device.wait("ContactPersistedE2E", timeout=60, scroll=True)

    tap_collection_item(device, "ContactDeleteE2E")
    device.tap("tablet-inspector-delete" if profile == "tablet" else "collection-delete-", scroll=True)
    device.wait("Delete item?", timeout=30)
    device.tap("Delete")
    time.sleep(1)
    if device.find("ContactDeleteE2E") is not None:
        device.capture(f"{profile}-contact-delete-failed")
        raise RuntimeError("Deleted contact remains visible")


def assert_contact_persisted(
    device: Device,
    profile: str,
    *,
    connection_maximum: int = 6,
    free_editable: bool = True,
) -> None:
    open_contact_section(device, profile, expected_item="ContactPersistedE2E")
    device.wait("ContactPersistedE2E", timeout=60, scroll=True)
    if device.find("ContactDeleteE2E") is not None:
        device.capture(f"{profile}-contact-delete-not-persisted")
        raise RuntimeError("Deleted contact returned after process restart")
    tap_collection_item(device, "ContactPersistedE2E")
    reset_collection_editor_to_top(device, profile)
    prefix = "tablet" if profile == "tablet" else "collection"
    expected_fields = (
        (f"{prefix}-field-name", "Name", "ContactPersistedE2E"),
        (f"{prefix}-field-notes", "Notes", "ContactNotesE2E"),
        (f"{prefix}-field-role", "Role", "FixerE2E"),
        (f"{prefix}-field-location", "Location", "ViennaE2E"),
        (f"{prefix}-field-metatype", "Metatype", "ElfE2E"),
        (f"{prefix}-field-gender", "Gender", "NonbinaryE2E"),
        (f"{prefix}-field-age", "Age", "42"),
        (f"{prefix}-field-contacttype", "Contact Type", "ProfessionalE2E"),
        (f"{prefix}-field-preferredpayment", "Preferred Payment", "CredstickE2E"),
        (f"{prefix}-field-hobbiesvice", "Hobbies Vice", "UrbanExplorerE2E"),
        (f"{prefix}-field-personallife", "Personal Life", "PrivateE2E"),
        (f"{prefix}-field-groupname", "Group Name", "NightMarketE2E"),
    )
    for selector, label, expected in expected_fields:
        actual = selected_text(device, selector, label, scroll=True)
        if actual != expected:
            device.capture(f"{profile}-contact-not-persisted")
            raise RuntimeError(
                f"Contact field {label!r} did not persist in the {profile} editor: "
                f"expected {expected!r}, got {actual!r}"
            )

    connection_selector = (
        "tablet-contact-connection" if profile == "tablet" else "collection-contact-connection-"
    )
    expected_connection = str(connection_maximum)
    actual_connection = selected_text(
        device,
        connection_selector,
        f"Connection · 1–{connection_maximum}",
        scroll=True,
    )
    if actual_connection != expected_connection:
        device.capture(
            f"{profile}-contact-connection-{expected_connection}-not-persisted"
        )
        raise RuntimeError(
            "Contact Connection did not persist at the active runner bound: "
            f"expected {expected_connection!r}, got {actual_connection!r}"
        )
    toggle_prefix = "tablet-toggle" if profile == "tablet" else "collection-toggle"
    reset_collection_editor_to_top(device, profile)
    for toggle in ("group", "family", "blackmail"):
        assert_toggle_state(
            device,
            f"{toggle_prefix}-{toggle}",
            checked=True,
            capture=f"{profile}-contact-{toggle}-not-persisted",
        )
    assert_toggle_state(
        device,
        f"{toggle_prefix}-free",
        checked=False,
        enabled=free_editable,
        capture=(
            f"{profile}-creation-contact-free-authority-not-persisted"
            if free_editable
            else f"{profile}-career-contact-free-authority-not-persisted"
        ),
    )
    if profile == "phone":
        device.back()


def edit_creation_free_contact(device: Device, profile: str) -> None:
    name = "ContactFreePersistedE2E"
    open_contact_section(device, profile, expected_item=name)
    device.wait(name, timeout=60, scroll=True)
    tap_collection_item(device, name)
    reset_collection_editor_to_top(device, profile)
    toggle_prefix = "tablet-toggle" if profile == "tablet" else "collection-toggle"
    assert_toggle_state(
        device,
        f"{toggle_prefix}-group",
        checked=False,
        capture=f"{profile}-creation-free-contact-group-precondition-invalid",
    )
    assert_toggle_state(
        device,
        f"{toggle_prefix}-free",
        checked=False,
        enabled=True,
        capture=f"{profile}-creation-free-contact-authority-invalid",
    )
    ensure_checked(device, f"{toggle_prefix}-free")
    assert_toggle_state(
        device,
        f"{toggle_prefix}-group",
        checked=False,
        capture=f"{profile}-creation-free-contact-group-coupled",
    )
    assert_toggle_state(
        device,
        f"{toggle_prefix}-free",
        checked=True,
        enabled=True,
        capture=f"{profile}-creation-free-contact-edit-failed",
    )
    device.tap(
        "tablet-inspector-save" if profile == "tablet" else "Save changes",
        scroll=True,
    )
    time.sleep(5)
    if profile == "phone":
        device.back()
        reset_scroll_to_top(device, swipes=12)
        device.wait(name, timeout=60, scroll=True)


def assert_creation_free_contact_persisted(device: Device, profile: str) -> None:
    name = "ContactFreePersistedE2E"
    open_contact_section(device, profile, expected_item=name)
    device.wait(name, timeout=60, scroll=True)
    tap_collection_item(device, name)
    reset_collection_editor_to_top(device, profile)
    toggle_prefix = "tablet-toggle" if profile == "tablet" else "collection-toggle"
    for toggle in ("group", "family", "blackmail"):
        assert_toggle_state(
            device,
            f"{toggle_prefix}-{toggle}",
            checked=False,
            capture=f"{profile}-creation-free-contact-{toggle}-not-isolated",
        )
    assert_toggle_state(
        device,
        f"{toggle_prefix}-free",
        checked=True,
        enabled=True,
        capture=f"{profile}-creation-free-contact-not-persisted",
    )
    if profile == "phone":
        device.back()


def add_and_edit_pet(device: Device, profile: str, *, create_items: bool = True) -> None:
    open_pet_section(
        device,
        profile,
        expected_item=None if create_items else "PetE2E",
    )
    if create_items:
        add_contact_from_dialog(device, profile, "PetDeleteE2E", "Companion")
        add_contact_from_dialog(device, profile, "PetE2E", "Companion")
    tap_collection_item(device, "PetE2E")
    reset_collection_editor_to_top(device, profile)

    prefix = "tablet" if profile == "tablet" else "collection"
    name_selector = f"{prefix}-field-name"
    save = "tablet-inspector-save" if profile == "tablet" else "Save changes"
    device.set_text(name_selector, "Name", "", scroll=True)
    device.tap(save, scroll=True)
    device.wait("Name required", timeout=30)
    device.tap("OK")
    device.set_text(name_selector, "Name", "PetPersistedE2E", scroll=True)
    device.set_text(f"{prefix}-field-metatype", "Metatype", "HellHoundE2E", scroll=True)
    device.set_text(f"{prefix}-field-notes", "Notes", "PetNotesE2E", scroll=True)
    device.tap(save, scroll=True)
    time.sleep(5)

    if profile == "phone":
        device.back()
        reset_scroll_to_top(device, swipes=12)
        device.wait("PetPersistedE2E", timeout=60, scroll=True)

    tap_collection_item(device, "PetDeleteE2E")
    device.tap("tablet-inspector-delete" if profile == "tablet" else "collection-delete-", scroll=True)
    device.wait("Delete item?", timeout=30)
    device.tap("Delete")
    time.sleep(1)
    if device.find("PetDeleteE2E") is not None:
        device.capture(f"{profile}-pet-delete-failed")
        raise RuntimeError("Deleted pet remains visible")


def assert_pet_persisted(device: Device, profile: str) -> None:
    open_pet_section(device, profile, expected_item="PetPersistedE2E")
    device.wait("PetPersistedE2E", timeout=60, scroll=True)
    if device.find("PetDeleteE2E") is not None:
        device.capture(f"{profile}-pet-delete-not-persisted")
        raise RuntimeError("Deleted pet returned after process restart")
    tap_collection_item(device, "PetPersistedE2E")
    reset_collection_editor_to_top(device, profile)
    prefix = "tablet" if profile == "tablet" else "collection"
    expected_fields = (
        (f"{prefix}-field-name", "Name", "PetPersistedE2E"),
        (f"{prefix}-field-metatype", "Metatype", "HellHoundE2E"),
        (f"{prefix}-field-notes", "Notes", "PetNotesE2E"),
    )
    for selector, label, expected in expected_fields:
        actual = selected_text(device, selector, label, scroll=True)
        if actual != expected:
            device.capture(f"{profile}-pet-not-persisted")
            raise RuntimeError(
                f"Pet field {label!r} did not persist in the {profile} editor: "
                f"expected {expected!r}, got {actual!r}"
            )
    if profile == "phone":
        device.back()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", default="emulator-5554")
    parser.add_argument("--profile", choices=("phone", "tablet"), required=True)
    parser.add_argument(
        "--journey",
        choices=("full", "condition-monitor", "contact-pet"),
        default="full",
    )
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument(
        "--linked-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "linked-runner-e2e.chum5",
    )
    parser.add_argument(
        "--invalid-linked-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "invalid-linked-runner-e2e.chum5",
    )
    parser.add_argument(
        "--full-editing-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "career-full-editing-e2e.chum5",
    )
    parser.add_argument(
        "--condition-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "career-condition-monitor-e2e.chum5",
    )
    parser.add_argument(
        "--contact-pet-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "creation-contact-pet-e2e.chum5",
    )
    args = parser.parse_args()

    full_editing_contract = (
        validate_full_editing_fixture(args.full_editing_runner.resolve())
        if args.journey == "full"
        else None
    )

    fixture_inputs = (
        (args.linked_runner.resolve(), "/sdcard/Download/linked-runner-e2e.chum5"),
        (
            args.invalid_linked_runner.resolve(),
            "/sdcard/Download/invalid-linked-runner-e2e.chum5",
        ),
        (
            args.full_editing_runner.resolve(),
            "/sdcard/Download/career-full-editing-e2e.chum5",
        ),
        (
            args.condition_runner.resolve(),
            "/sdcard/Download/career-condition-monitor-e2e.chum5",
        ),
        (
            args.contact_pet_runner.resolve(),
            "/sdcard/Download/creation-contact-pet-e2e.chum5",
        ),
    )
    fixture_sha256 = {local_path: sha256(local_path) for local_path, _ in fixture_inputs}
    apk_path = args.apk.resolve()
    apk_sha256 = sha256(apk_path)
    local_input_sha256 = {apk_path: apk_sha256}
    local_input_sha256.update(fixture_sha256)
    full_editing_runner_sha256 = fixture_sha256[args.full_editing_runner.resolve()]
    condition_runner_sha256 = fixture_sha256[args.condition_runner.resolve()]
    contact_pet_runner_sha256 = fixture_sha256[args.contact_pet_runner.resolve()]

    device = Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    device.require_transport_stability(expected_api_level="36")
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Editing E2E requires API 36, got {api!r}")

    device.install_verified(
        apk_path,
        apk_sha256,
        "--no-incremental",
        "-r",
    )
    device.shell("pm", "clear", PACKAGE)
    transport_receipt: list[dict[str, str]] = []
    verified_remote_sha256: dict[Path, str] = {}
    for local_path, remote_path in fixture_inputs:
        captured_sha256 = fixture_sha256[local_path]
        remote_sha256 = device.push_verified(local_path, remote_path, captured_sha256)
        verified_remote_sha256[local_path] = remote_sha256
        transport_receipt.append(
            {
                "localPath": str(local_path),
                "remotePath": remote_path,
                "capturedLocalSha256": captured_sha256,
                "verifiedRemoteSha256": remote_sha256,
            }
        )
    (device.evidence / "fixture-transport-receipt.json").write_text(
        json.dumps(
            {
                "schema": "chummer.android.fixture-transport/v1",
                "status": "pass",
                "fixtures": transport_receipt,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    initial_launch_state = launch_app(device)
    if args.profile == "phone":
        wait_for_phone_runners(device)
        record_phone_ui_locale_evidence(
            device,
            evidence_prefix="full-editing",
        )
    else:
        device.wait("Your runners", timeout=90)

    imported_authority: WorkspaceAuthority | None
    if args.journey in {"condition-monitor", "contact-pet"}:
        device.tap("home-open-file")
        fixture_name = (
            "creation-contact-pet-e2e.chum5"
            if args.journey == "contact-pet"
            else "career-condition-monitor-e2e.chum5"
        )
        fixture_alias = (
            "ContactPetE2E" if args.journey == "contact-pet" else "ConditionMonitorE2E"
        )
        expected_fixture_sha256 = (
            contact_pet_runner_sha256
            if args.journey == "contact-pet"
            else condition_runner_sha256
        )
        select_android_document(device, fixture_name)
        device.wait(fixture_alias, timeout=90)
        if args.profile == "phone":
            wait_for_phone_runner_route(
                device,
                created=args.journey != "contact-pet",
            )
            tap_phone_destination(device, "phone-destination-runners")
            wait_for_phone_runners(device)
            imported_authority = read_workspace_authority(device)
            require_import_authority(imported_authority, expected_fixture_sha256)
        else:
            device.wait("tablet-build-layout", timeout=90)
            imported_authority = None
    else:
        imported_authority = prepare_full_editing_runner(
            device,
            args.profile,
            "career-full-editing-e2e.chum5",
            "FullEditingE2E",
            full_editing_runner_sha256,
        )

    open_build(device, args.profile)
    if args.journey == "contact-pet":
        add_and_edit_contact(device, args.profile, create_items=False)
        if args.profile == "phone":
            device.back()
        edit_creation_free_contact(device, args.profile)
        if args.profile == "phone":
            device.back()
        add_and_edit_pet(device, args.profile, create_items=False)
        persisted_authority = (
            save_and_read_workspace_authority(device, args.profile)
            if args.profile == "phone"
            else None
        )
        device.capture("contact-pet-persisted")

        restart_proof = force_stop_and_launch_new_process(
            device,
            initial_launch_state,
        )
        if args.profile == "phone":
            wait_for_phone_runner_route(device, created=False)
            tap_phone_destination(device, "phone-destination-runners")
            wait_for_phone_runners(device)
        else:
            device.wait("Continue building", timeout=90)
        restored_authority = (
            read_workspace_authority(device) if args.profile == "phone" else None
        )
        if persisted_authority is not None and restored_authority is not None:
            require_restored_authority(persisted_authority, restored_authority)
        open_build(device, args.profile)
        assert_contact_persisted(device, args.profile)
        if args.profile == "phone":
            device.back()
        assert_creation_free_contact_persisted(device, args.profile)
        if args.profile == "phone":
            device.back()
        assert_pet_persisted(device, args.profile)
        device.capture("contact-pet-after-restart")

        require_unchanged_local_inputs(
            local_input_sha256,
            label="APK or fixture authority",
        )
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "serial": args.serial,
            "profile": args.profile,
            "journey": args.journey,
            "apiLevel": int(api),
            "apk": str(apk_path),
            "apkSha256": apk_sha256,
            "adbTransport": device.transport_summary(),
            "driverSha256": sha256(Path(__file__).resolve()),
            "inputFixture": str(args.contact_pet_runner.resolve()),
            "inputFixtureSha256": contact_pet_runner_sha256,
            "verifiedRemoteInputFixtureSha256": verified_remote_sha256[
                args.contact_pet_runner.resolve()
            ],
            "importAuthority": optional_workspace_authority_json(imported_authority),
            "preRestartAuthority": optional_workspace_authority_json(persisted_authority),
            "postRestartAuthority": optional_workspace_authority_json(restored_authority),
            "authorityProofStages": {
                "status": (
                    "pass" if args.profile == "phone" else "not-claimed-tablet-deferred"
                ),
                "import": {
                    "frozenFixtureSha256": contact_pet_runner_sha256,
                    "verifiedRemoteFixtureSha256": verified_remote_sha256[
                        args.contact_pet_runner.resolve()
                    ],
                    "workspace": optional_workspace_authority_json(imported_authority),
                },
                "preRestartSaved": optional_workspace_authority_json(persisted_authority),
                "postRestartRestored": optional_workspace_authority_json(restored_authority),
            },
            "initialLaunchProcessIds": list(initial_launch_state.process_ids),
            "initialLaunchResumedComponent": initial_launch_state.resumed_component,
            "preForceStopProcessIds": list(restart_proof.before_force_stop.process_ids),
            "preForceStopResumedComponent": restart_proof.before_force_stop.resumed_component,
            "postForceStopProcessIds": list(restart_proof.after_force_stop.process_ids),
            "restartProcessIds": list(restart_proof.restarted.process_ids),
            "restartResumedComponent": restart_proof.restarted.resumed_component,
            "journeys": {
                "creationRunnerImport": "pass",
                "contactInvalidBoundsRejected": "pass",
                "contactEditPersisted": "pass",
                "creationContactFreeIsolatedPersisted": "pass",
                "contactDeletePersisted": "pass",
                "processRestartContactPersistence": "pass",
                "petInvalidNameRejected": "pass",
                "petEditPersisted": "pass",
                "petDeletePersisted": "pass",
                "processRestartPetPersistence": "pass",
            },
        }
        args.receipt.parent.mkdir(parents=True, exist_ok=True)
        args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(receipt, indent=2))
        return 0

    if args.journey == "condition-monitor":
        edit_condition_damage(device, args.profile, "physical", 2)
        edit_condition_damage(device, args.profile, "stun", 1)
        assert_condition_damage(device, args.profile, "physical", 2)
        assert_condition_damage(device, args.profile, "stun", 1)
        persisted_authority = (
            save_and_read_workspace_authority(device, args.profile)
            if args.profile == "phone"
            else None
        )
        device.capture("condition-monitor-persisted")

        restart_proof = force_stop_and_launch_new_process(
            device,
            initial_launch_state,
        )
        if args.profile == "phone":
            wait_for_phone_runner_route(device, created=True)
            tap_phone_destination(device, "phone-destination-runners")
            wait_for_phone_runners(device)
        else:
            device.wait("Continue building", timeout=90)
        restored_authority = (
            read_workspace_authority(device) if args.profile == "phone" else None
        )
        if persisted_authority is not None and restored_authority is not None:
            require_restored_authority(persisted_authority, restored_authority)
        open_build(device, args.profile)
        assert_condition_damage(device, args.profile, "physical", 2)
        assert_condition_damage(device, args.profile, "stun", 1)
        device.capture("condition-monitor-after-restart")

        require_unchanged_local_inputs(
            local_input_sha256,
            label="APK or fixture authority",
        )
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "serial": args.serial,
            "profile": args.profile,
            "journey": args.journey,
            "apiLevel": int(api),
            "apk": str(apk_path),
            "apkSha256": apk_sha256,
            "adbTransport": device.transport_summary(),
            "driverSha256": sha256(Path(__file__).resolve()),
            "inputFixture": str(args.condition_runner.resolve()),
            "inputFixtureSha256": condition_runner_sha256,
            "verifiedRemoteInputFixtureSha256": verified_remote_sha256[
                args.condition_runner.resolve()
            ],
            "importAuthority": optional_workspace_authority_json(imported_authority),
            "preRestartAuthority": optional_workspace_authority_json(persisted_authority),
            "postRestartAuthority": optional_workspace_authority_json(restored_authority),
            "authorityProofStages": {
                "status": (
                    "pass" if args.profile == "phone" else "not-claimed-tablet-deferred"
                ),
                "import": {
                    "frozenFixtureSha256": condition_runner_sha256,
                    "verifiedRemoteFixtureSha256": verified_remote_sha256[
                        args.condition_runner.resolve()
                    ],
                    "workspace": optional_workspace_authority_json(imported_authority),
                },
                "preRestartSaved": optional_workspace_authority_json(persisted_authority),
                "postRestartRestored": optional_workspace_authority_json(restored_authority),
            },
            "initialLaunchProcessIds": list(initial_launch_state.process_ids),
            "initialLaunchResumedComponent": initial_launch_state.resumed_component,
            "preForceStopProcessIds": list(restart_proof.before_force_stop.process_ids),
            "preForceStopResumedComponent": restart_proof.before_force_stop.resumed_component,
            "postForceStopProcessIds": list(restart_proof.after_force_stop.process_ids),
            "restartProcessIds": list(restart_proof.restarted.process_ids),
            "restartResumedComponent": restart_proof.restarted.resumed_component,
            "journeys": {
                "careerRunnerImport": "pass",
                "physicalConditionDamageEditPersisted": "pass",
                "stunConditionDamageEditPersisted": "pass",
                "processRestartConditionDamagePersistence": "pass",
            },
        }
        args.receipt.parent.mkdir(parents=True, exist_ok=True)
        args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(receipt, indent=2))
        return 0

    open_origin_dossier(device, args.profile)
    device.tap("origin-dossier-identity")
    device.set_text("origin-alias", "Alias", "LatchkeyE2E")
    device.tap("origin-dossier-identity-save", scroll=True)
    device.assert_text("LatchkeyE2E")
    device.back()
    device.tap("origin-dossier-story")
    device.set_text("origin-concept", "Concept", "NativeE2E")
    device.tap("origin-dossier-story-save", scroll=True)
    device.assert_text("NativeE2E")
    device.back()
    device.back()

    if full_editing_contract is None:
        raise RuntimeError("Full journey requires a validated full-editing fixture")
    improve_body_in_career(device, args.profile, full_editing_contract)
    if args.profile == "phone":
        device.back()

    added_gear_item_id = add_and_edit_gear(device, args.profile)
    if args.profile == "phone":
        device.back()
    add_and_edit_contact(
        device,
        args.profile,
        connection_maximum=12,
        free_editable=False,
    )
    attach_linked_runner(
        device,
        args.profile,
        "contact",
        "ContactPersistedE2E",
        validate_invalid=True,
    )
    if args.profile == "phone":
        device.back()
    add_and_edit_pet(device, args.profile)
    attach_linked_runner(device, args.profile, "pet", "PetPersistedE2E")
    if args.profile == "phone":
        device.back()
    assert_body_total(device, args.profile, full_editing_contract.improved_body_total)
    if args.profile == "phone":
        device.back()
    persisted_authority = (
        save_and_read_workspace_authority(device, args.profile)
        if args.profile == "phone"
        else None
    )
    device.capture("editing-persisted")

    restart_proof = force_stop_and_launch_new_process(
        device,
        initial_launch_state,
    )
    if args.profile == "phone":
        wait_for_phone_runner_route(device, created=True)
        tap_phone_destination(device, "phone-destination-runners")
        wait_for_phone_runners(device)
    else:
        device.wait("Continue building", timeout=90)
    restored_authority = (
        read_workspace_authority(device) if args.profile == "phone" else None
    )
    if persisted_authority is not None and restored_authority is not None:
        require_restored_authority(persisted_authority, restored_authority)
    open_build(device, args.profile)
    open_origin_dossier(device, args.profile)
    device.tap("origin-dossier-identity")
    device.assert_text("LatchkeyE2E")
    device.back()
    device.tap("origin-dossier-story")
    device.assert_text("NativeE2E")
    device.back()
    device.back()
    assert_body_total(device, args.profile, full_editing_contract.improved_body_total)
    if args.profile == "phone":
        device.back()
    open_gear_section(device, args.profile)
    if args.profile == "phone":
        if added_gear_item_id is None:
            raise RuntimeError("Phone gear proof lost its exact typed item identity")
        restored_inventory = scan_collection_route_inventory(
            device,
            kind="gear",
            evidence_prefix="gear-after-restart",
        )
        restored_route = f"collection-item-gear-{added_gear_item_id}"
        tap_typed_collection_route(
            device,
            inventory=restored_inventory,
            route_id=restored_route,
            evidence_prefix="gear-after-restart",
        )
        gear_field = f"collection-field-customname-{added_gear_item_id}"
    else:
        reset_scroll_to_top(device, x_ratio=0.375, swipes=6)
        tap_collection_item(device, "Ares Predator V")
        gear_field = "tablet-field-customname"
    persisted_custom_name = selected_text(device, gear_field, "Custom Name", scroll=True)
    if persisted_custom_name != "GearProofE2E":
        device.capture(f"{args.profile}-gear-custom-name-not-persisted")
        raise RuntimeError(
            "Gear Custom Name did not persist after process restart: "
            f"expected 'GearProofE2E', got {persisted_custom_name!r}"
        )
    if args.profile == "phone":
        device.back()
        device.back()
    assert_link_persisted_then_remove(
        device,
        args.profile,
        "contact",
        "ContactPersistedE2E",
    )
    if args.profile == "phone":
        device.back()
    assert_contact_persisted(
        device,
        args.profile,
        connection_maximum=12,
        free_editable=False,
    )
    if args.profile == "phone":
        device.back()
    assert_link_persisted_then_remove(
        device,
        args.profile,
        "pet",
        "PetPersistedE2E",
    )
    if args.profile == "phone":
        device.back()
    assert_pet_persisted(device, args.profile)
    device.capture("editing-after-restart")

    require_unchanged_local_inputs(
        local_input_sha256,
        label="APK or fixture authority",
    )
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": args.profile,
        "journey": args.journey,
        "apiLevel": int(api),
        "apk": str(apk_path),
        "apkSha256": apk_sha256,
        "adbTransport": device.transport_summary(),
        "driverSha256": sha256(Path(__file__).resolve()),
        "inputFixture": str(args.full_editing_runner.resolve()),
        "inputFixtureSha256": full_editing_runner_sha256,
        "verifiedRemoteInputFixtureSha256": verified_remote_sha256[
            args.full_editing_runner.resolve()
        ],
        "importAuthority": optional_workspace_authority_json(imported_authority),
        "preRestartAuthority": optional_workspace_authority_json(persisted_authority),
        "postRestartAuthority": optional_workspace_authority_json(restored_authority),
        "authorityProofStages": {
            "status": (
                "pass" if args.profile == "phone" else "not-claimed-tablet-deferred"
            ),
            "import": {
                "frozenFixtureSha256": full_editing_runner_sha256,
                "verifiedRemoteFixtureSha256": verified_remote_sha256[
                    args.full_editing_runner.resolve()
                ],
                "workspace": optional_workspace_authority_json(imported_authority),
            },
            "preRestartSaved": optional_workspace_authority_json(persisted_authority),
            "postRestartRestored": optional_workspace_authority_json(restored_authority),
        },
        "initialLaunchProcessIds": list(initial_launch_state.process_ids),
        "initialLaunchResumedComponent": initial_launch_state.resumed_component,
        "preForceStopProcessIds": list(restart_proof.before_force_stop.process_ids),
        "preForceStopResumedComponent": restart_proof.before_force_stop.resumed_component,
        "postForceStopProcessIds": list(restart_proof.after_force_stop.process_ids),
        "restartProcessIds": list(restart_proof.restarted.process_ids),
        "restartResumedComponent": restart_proof.restarted.resumed_component,
        "careerAttributeTransition": {
            "attribute": "BOD",
            "initialTotal": full_editing_contract.initial_body_total,
            "improvedTotal": full_editing_contract.improved_body_total,
            "improvementCost": full_editing_contract.improvement_cost,
            "initialKarma": full_editing_contract.initial_karma,
            "remainingKarma": full_editing_contract.remaining_karma,
            "nextImprovementCost": full_editing_contract.next_improvement_cost,
        },
        "journeys": {
            "newRunnerCreationWorkflowStarted": "pass",
            "newRunnerCreationDraftSaved": (
                "pass" if args.profile == "phone" else "not-claimed-tablet-deferred"
            ),
            "newRunnerCreationCompletion": "not-claimed",
            "phoneCreationWizardDashboard": (
                "pass" if args.profile == "phone" else "not-applicable-tablet-deferred"
            ),
            "careerRunnerImport": "pass",
            "careerRunnerAliasActivated": "FullEditingE2E",
            "originIdentityEditPersisted": "pass",
            "originStoryEditPersisted": "pass",
            "careerAttributeImprovePersisted": "pass",
            "collectionCustomNameEditPersisted": "pass",
            "contactInvalidBoundsRejected": "pass",
            "contactEditPersisted": "pass",
            "careerContactFreeReadOnlyAuthority": "pass",
            "contactDeletePersisted": "pass",
            "processRestartContactPersistence": "pass",
            "petInvalidNameRejected": "pass",
            "petEditPersisted": "pass",
            "petDeletePersisted": "pass",
            "processRestartPetPersistence": "pass",
            "linkedRunnerInvalidDocumentRejected": "pass",
            "contactLinkedRunnerAttachPersisted": "pass",
            "contactLinkedRunnerRemoveRestoredIdentity": "pass",
            "petLinkedRunnerAttachPersisted": "pass",
            "petLinkedRunnerRemoveRestoredIdentity": "pass",
            "processRestartPersistence": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"editing E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
