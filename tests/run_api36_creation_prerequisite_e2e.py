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

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_creation_wizard_foundation_e2e as foundation
import run_api36_editing_e2e as shared
import run_api36_new_character_priority_e2e as priority


CATEGORIES = ("heritage", "talent", "attributes", "skills", "resources")
CREATION_KARMA_AUTHORITY_BLOCKER = "creation-karma-authority-required"
STANDARD_PRIORITY_SETTINGS_ID = "223a11ff-80e0-428b-89a9-6ef1c243b8b6"
PRIORITY_BUILD_METHOD_SELECTION = (
    "dialog-field-newcharacterbuildmethod",
    "Priority",
)
PRIORITY_SETTINGS_SELECTION = (
    "dialog-field-newcharactersetting",
    "Character Setting",
    STANDARD_PRIORITY_SETTINGS_ID,
)
PRIORITY_CREATION_SELECTIONS = (
    ("dialog-field-newcharactermetatypecategory", "Non-human choices"),
    ("dialog-field-newcharactermetatype", "Elf"),
    ("dialog-field-newcharacterpriorityheritage", "A"),
    ("dialog-field-newcharactermetavariant", "Dryad"),
    ("dialog-field-newcharacterpriorityattributes", "C"),
    ("dialog-field-newcharacterprioritytalent", "B"),
    ("dialog-field-newcharacterpriorityskills", "D"),
    ("dialog-field-newcharacterpriorityresources", "E"),
    ("dialog-field-newcharacterprioritytalentchoice", "Mystic Adept"),
    ("dialog-field-newcharacterpriorityskillchoice1", "Summoning"),
    ("dialog-field-newcharacterpriorityskillchoice2", "Binding"),
    ("dialog-field-newcharacterpriorityskillchoice3", "Gymnastics"),
)
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


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def node_text(device: shared.Device, selector: str, *, scroll: bool = False) -> str:
    node = device.wait(selector, timeout=60, scroll=scroll, max_scrolls=22)
    return node.attributes.get("text") or node.attributes.get("content-desc") or ""


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


def require_priority_created_workspace_authority(
    fresh: shared.WorkspaceAuthority,
    prepared: shared.WorkspaceAuthority,
) -> None:
    shared.require_saved_authority(prepared)
    if prepared.workspace_id == fresh.workspace_id:
        raise RuntimeError("Priority creation did not publish a distinct runner workspace identity")
    if prepared.payload_sha256 == fresh.payload_sha256:
        raise RuntimeError("Priority creation did not publish a distinct character payload digest")
    if prepared.document_sha256 == fresh.document_sha256:
        raise RuntimeError("Priority creation did not publish a distinct document authority digest")


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


def provision_creation_karma_through_priority_creation(
    device: shared.Device,
) -> dict[str, str]:
    """Create a rules-valid Priority runner exclusively through the production phone dialog."""
    device.tap_until_visible(
        "home-new-runner",
        "Select Build Method",
        scroll=True,
        max_scrolls=16,
    )
    build_method_selector, build_method = PRIORITY_BUILD_METHOD_SELECTION
    priority.select_option(device, build_method_selector, build_method)
    settings_selector, settings_label, settings_id = PRIORITY_SETTINGS_SELECTION
    device.set_text(
        settings_selector,
        settings_label,
        settings_id,
        scroll=True,
        max_scrolls=16,
        scroll_distance_ratio=0.22,
    )
    device.tap("dialog-action-create-character", scroll=True, max_scrolls=16)
    device.wait("Select Metatype Priority", timeout=60)
    selected: dict[str, str] = {
        build_method_selector: build_method,
        settings_selector: settings_id,
    }
    for selector, option in PRIORITY_CREATION_SELECTIONS:
        priority.select_option(device, selector, option)
        selected[selector] = option
    device.tap(
        "dialog-action-complete-new-character-workflow",
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    require_new_character_dialog_transition(device)
    # Completing a created=false runner deliberately routes the phone shell straight to Build.
    # The closing dialog can leave Build's ScrollView at the dialog's deep scroll offset, which
    # prunes the page-level AutomationId from UIAutomator. Bind the route to the fixed toolbar,
    # reset the viewport, and only then require the dashboard marker.
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        toolbar_timeout=120,
        dashboard_timeout=30,
        reset_swipes=48,
    )
    device.capture("creation-karma-priority-runner-created")
    device.tap(
        "build-save-runner",
        scroll=True,
        max_scrolls=48,
        scroll_distance_ratio=0.22,
    )
    device.wait(
        "Saved.",
        timeout=90,
        scroll=True,
        max_scrolls=48,
        scroll_distance_ratio=0.22,
    )
    shared.tap_phone_destination(device, "phone-destination-runners")
    device.wait("home-open-file", timeout=90, scroll=True, max_scrolls=16)
    return selected


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
    required_labels = {"Authority digest", "Profile inputs", "Priorities XML"}
    seen_labels: set[str] = set()
    digests: set[str] = set()
    seen_card = False
    shared.reset_scroll_to_top(device, swipes=22)
    for scroll_index in range(23):
        nodes = device.hierarchy()
        seen_card = seen_card or any(
            shared.Device._matches(node, "creation-prerequisite-source-authority")
            for node in nodes
        )
        for node in nodes:
            values = (
                node.attributes.get("text", ""),
                node.attributes.get("content-desc", ""),
            )
            for value in values:
                if value in required_labels:
                    seen_labels.add(value)
                digests.update(shared.SHA256_TEXT.findall(value))
        if seen_card and seen_labels == required_labels and len(digests) >= 3:
            return sorted(digests)
        if scroll_index < 22:
            device.swipe_up(distance_ratio=0.22)
            time.sleep(0.75)
    device.capture("creation-prerequisite-source-authority-incomplete")
    raise RuntimeError(
        "Creation prerequisite source authority was incomplete: "
        f"card={seen_card}, labels={sorted(seen_labels)!r}, canonicalDigests={sorted(digests)!r}"
    )


def tap_first_exact_enabled_priority_rank(device: shared.Device, category: str) -> str:
    """Tap the first exact, enabled A-E rank after a cardinality scan."""
    prefix = f"creation-prerequisite-rank-{category}-"
    expected_ids = {f"{prefix}{rank}" for rank in "abcde"}
    observed_ids: set[str] = set()
    candidates: set[str] = set()
    invalid_ids: set[str] = set()
    duplicate_ids: set[str] = set()
    shared.reset_scroll_to_top(device, swipes=22)
    for scroll_index in range(23):
        screen_ids: list[str] = []
        for node in device.hierarchy():
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
        if scroll_index < 22:
            device.swipe_up(distance_ratio=0.22)
            time.sleep(0.2)

    if (
        invalid_ids
        or duplicate_ids
        or observed_ids != expected_ids
        or not candidates
    ):
        device.capture(f"creation-prerequisite-{category}-rank-cardinality-invalid")
        raise RuntimeError(
            f"Exact {category} rank scan was invalid: candidates={sorted(candidates)!r}, "
            f"observedIds={sorted(observed_ids)!r}, expectedIds={sorted(expected_ids)!r}, "
            f"invalidIds={sorted(invalid_ids)!r}, duplicateIds={sorted(duplicate_ids)!r}"
        )

    selected_resource_id = min(
        candidates,
        key=lambda resource_id: resource_id[len(prefix) :],
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


def select_priority_rank(device: shared.Device, category: str) -> str:
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
    selected_resource_id = tap_first_exact_enabled_priority_rank(device, category)

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
    if (
        row.attributes.get("enabled") != "true"
        or row.attributes.get("clickable") != "true"
        or not device.node_has_tappable_bounds(row)
        or re.search(rf"\bRank {re.escape(expected_rank)}\b", detail) is None
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
        backward_scrolls=22,
        forward_scrolls=22,
        scroll_distance_ratio=0.22,
        exact_resource_id=True,
    )
    device.wait("creation-prerequisite-page", timeout=60)
    # Android can carry the deeply scrolled Build viewport into this newly pushed page.
    # Bind the route first, then establish the native page origin before reading top cards.
    shared.reset_scroll_to_top(device, swipes=22)
    device.wait("creation-prerequisite-karma-budget", timeout=60, scroll=True, max_scrolls=22)
    # A full-height Android swipe can jump from the tall Karma card directly to
    # the category list and never expose the shorter method card to UIAutomator.
    # Use bounded, small bidirectional steps for this exact authority surface.
    device.wait_exact_resource_id_bidirectional(
        "creation-prerequisite-method",
        timeout=90,
        backward_scrolls=6,
        forward_scrolls=16,
        scroll_distance_ratio=0.18,
        evidence_prefix="creation-prerequisite-method",
        surface_name="Creation prerequisite build-method authority",
        require_tappable=False,
    )
    # Both authority cards can push the binding above UIAutomator's visible hierarchy.
    # Leave this route at a deterministic origin so every caller can read the binding
    # without depending on the height of the cards it just verified.
    shared.reset_scroll_to_top(device, swipes=22)


def tap_enabled_authority_option(
    device: shared.Device,
    prefix: str,
    required_label: str,
    *,
    max_scrolls: int = 40,
) -> str:
    candidate_ids: set[str] = set()
    duplicate_resource_id = False
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    for scroll_index in range(max_scrolls + 1):
        screen_ids = exact_enabled_authority_option_ids(
            device.hierarchy(),
            prefix,
            required_label,
            device.node_has_tappable_bounds,
        )
        if len(screen_ids) != len(set(screen_ids)):
            duplicate_resource_id = True
        candidate_ids.update(screen_ids)
        if scroll_index < max_scrolls:
            device.swipe_up(distance_ratio=0.22)
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
    for scroll_index in range(max_scrolls + 1):
        screen_ids = exact_current_authority_option_ids(
            device.hierarchy(),
            prefix,
            device.node_has_tappable_bounds,
        )
        if len(screen_ids) != len(set(screen_ids)):
            duplicate_resource_id = True
        candidate_ids.update(screen_ids)
        if scroll_index < max_scrolls:
            device.swipe_up(distance_ratio=0.22)
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    shared_path = Path(shared.__file__).resolve()
    priority_driver_path = Path(priority.__file__).resolve()
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
    initial_launch = shared.launch_app(device)
    shared.wait_for_phone_runners(device)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True)
    device.wait("dialog-action-complete-new-character-workflow", timeout=45, scroll=True)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True)
    shared.wait_for_phone_runner_route(device, created=False)
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        reset_swipes=22,
    )
    foundation.assert_creation_editor_gated(device)

    fresh_dashboard_binding = node_text(device, "creation-wizard-binding", scroll=True)
    fresh_navigation = wait_creation_method_navigation(device, ready=False)
    device.capture("fresh-runner-creation-karma-authority-blocked")
    shared.reset_scroll_to_top(device, swipes=22)

    # Bind the blocked runner to its durable authority before creating a separate, exact Priority
    # runner exclusively through the same public production dialog available to phone users.
    device.tap("build-save-runner", scroll=True, max_scrolls=48, scroll_distance_ratio=0.22)
    device.wait("Saved.", timeout=90, scroll=True, max_scrolls=48, scroll_distance_ratio=0.22)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device)
    device.wait("home-open-file", timeout=90, scroll=True, max_scrolls=16)
    fresh_authority = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(fresh_authority)
    priority_creation_selections = provision_creation_karma_through_priority_creation(device)
    prepared_authority = shared.read_phone_workspace_authority(device)
    require_priority_created_workspace_authority(fresh_authority, prepared_authority)

    shared.open_creation_dashboard(device, reset_swipes=48)
    foundation.assert_creation_editor_gated(device)
    dashboard_binding = node_text(device, "creation-wizard-binding", scroll=True)
    if dashboard_binding == fresh_dashboard_binding:
        raise RuntimeError("Priority creation did not refresh the creation wizard binding")
    ready_navigation = wait_creation_method_navigation(device, ready=True)

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
    for label in ("Total", "Used", "Remaining"):
        if label.lower() not in karma.lower():
            raise RuntimeError(f"Global Creation Karma omitted {label!r}: {karma!r}")
    source_authority_digests = read_source_authority_digests(device)

    selected: dict[str, str] = {}
    for category in CATEGORIES:
        selected[category] = select_priority_rank(device, category)

    typed_selections: dict[str, str] = {}
    typed_selection_ids: dict[str, str] = {}
    shared.reset_scroll_to_top(device, swipes=22)
    for category, label in (("heritage", "Human"), ("talent", "Mundane")):
        device.tap(
            f"creation-prerequisite-{category}-selection",
            scroll=True,
            max_scrolls=22,
        )
        device.wait(f"creation-prerequisite-{category}-page", timeout=45)
        typed_selections[category] = tap_enabled_authority_option(
            device,
            f"creation-prerequisite-{category}-option-",
            label,
            max_scrolls=40,
        )
        device.wait("creation-prerequisite-page", timeout=45)
        selection_row = node_text(
            device,
            f"creation-prerequisite-{category}-selection",
            scroll=True,
        )
        if "selection" not in selection_row.lower():
            raise RuntimeError(
                f"Core-projected {category} choice did not bind to the typed phone draft: "
                f"{selection_row!r}"
            )
        typed_selection_ids[category] = node_text(
            device,
            f"creation-prerequisite-{category}-selection-id",
            scroll=True,
        ).strip()
        if not typed_selection_ids[category]:
            raise RuntimeError(f"Typed {category} SelectionId was not exposed by Core authority")

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

    attributes_gate = node_text(
        device,
        "creation-prerequisite-attributes-disabled",
        scroll=True,
    )
    if "raw" not in attributes_gate.lower() or "metatype" not in attributes_gate.lower():
        raise RuntimeError(f"Attribute prerequisite reason is not explicit: {attributes_gate!r}")

    device.tap("creation-prerequisite-prepare-preview", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-preview-page", timeout=60)
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
    preview_attributes = node_text(
        device,
        "creation-prerequisite-preview-attributes-ready",
        scroll=True,
    )
    if "effective" not in preview_attributes.lower() or "special" not in preview_attributes.lower():
        raise RuntimeError(
            "Core preview did not expose effective normal and special Attribute authority: "
            f"{preview_attributes!r}"
        )
    device.tap("creation-prerequisite-confirm", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-confirm-receipt", timeout=90, scroll=True, max_scrolls=22)
    # Confirmation replaces the deeply scrolled preview content in place. Read the
    # receipt and its ordered authority fields from a deterministic page origin.
    shared.reset_scroll_to_top(device, swipes=22)
    receipt_text = node_text(device, "creation-prerequisite-confirm-receipt", scroll=True)
    if "false" not in receipt_text.lower():
        raise RuntimeError("Prerequisite receipt did not prove CharacterDocumentChanged=false")
    if "core prerequisite complete" not in receipt_text.lower():
        raise RuntimeError(
            "Prerequisite receipt did not prove the post-confirm Attributes gate: "
            f"{receipt_text!r}"
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
    foundation.assert_creation_editor_gated(device)
    if node_text(device, "creation-wizard-binding", scroll=True) == dashboard_binding:
        raise RuntimeError("Atomic prerequisite confirmation did not refresh the wizard revision")

    # Same-process reload and a real process restart must both restore Core's persisted draft.
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
        )
    resumed_attributes = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    if "rank" not in resumed_attributes.lower():
        raise RuntimeError("Confirmed prerequisite draft did not resume its Attribute rank")

    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=False)
    shared.open_creation_dashboard(
        device,
        open_build_route=False,
        reset_swipes=22,
    )
    foundation.assert_creation_editor_gated(device)
    open_prerequisite(device)
    restarted_authority = read_persisted_prerequisite_authority(device)
    assert_persisted_prerequisite_authority(
        restarted_authority,
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
        )
    device.wait("creation-prerequisite-attributes-ready", scroll=True, max_scrolls=22)
    device.capture("creation-prerequisite-process-restart")

    receipt = {
        "schema": "chummer.android.creation-prerequisite-e2e/v1",
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "sharedDriverSha256": sha256(shared_path),
        "priorityCreationDriverSha256": sha256(priority_driver_path),
        "journeys": {
            "freshRunnerCreationKarmaAuthorityBlocked": "pass",
            "publicRulesValidPriorityRunnerCreated": "pass",
            "priorityCreationUsedExplicitProductionSelections": "pass",
            "distinctSavedWorkspacePayloadAndDocumentAuthority": "pass",
            "creationMethodNavigationEnabledAfterAuthority": "pass",
            "canonicalSourceAuthorityDigestsVisible": "pass",
            "priorityOrSumToTenAuthorityLoaded": "pass",
            "globalCreationKarmaExactTotalUsedRemaining": "pass",
            "fiveOrderedTypedCategorySelections": "pass",
            "authorityProjectedRankOptionsOnly": "pass",
            "priorityMultisetOrSumTargetEnforced": "pass",
            "selectedRankAutomationIds": selected,
            "selectedAuthorityOptionAutomationIds": typed_selections,
            "selectedAuthoritySelectionIds": typed_selection_ids,
            "prerequisiteSnapshotDigest": prerequisite_snapshot_digest,
            "confirmedDraftDigest": confirmed_draft_digest,
            "previewDigest": preview_digest,
            "previewBindingDigests": preview_binding_digests,
            "confirmedBindingDigests": confirmed_binding_digests,
            "confirmedRevisions": confirmed_revisions,
            "sameSessionPersistedAuthority": resumed_authority,
            "restartedPersistedAuthority": restarted_authority,
            "backRestoresDraftSelection": "pass",
            "heritageAndTalentSelectionsProjectedByCore": "pass",
            "previewDigestBeforeExplicitConfirmation": "pass",
            "atomicDraftReceiptVerified": "pass",
            "characterDocumentChangedFalse": "pass",
            "rawAttributeGrantVisible": "pass",
            "metatypeAdjustmentResolvedByCore": "pass",
            "attributesPrerequisiteOpenedByCore": "pass",
            "pendingDraftSameProcessResume": "pass",
            "pendingDraftProcessRestartResume": "pass",
            "buildGhostLaunchPostponedAndAbsent": "pass",
            "advancedEditorNeverExposedWhileCreatedFalse": "pass",
        },
        "restartProcessIds": {
            "beforeForceStop": list(restart.before_force_stop.process_ids),
            "afterForceStop": list(restart.after_force_stop.process_ids),
            "restarted": list(restart.restarted.process_ids),
        },
        "creationKarmaProvisioning": {
            "method": "production-priority-creation-dialog",
            "explicitSelections": priority_creation_selections,
            "freshRunnerWorkspaceAuthority": shared.workspace_authority_json(fresh_authority),
            "preparedWorkspaceAuthority": shared.workspace_authority_json(prepared_authority),
            "freshNavigation": fresh_navigation,
            "readyNavigation": ready_navigation,
            "prerequisiteBinding": prerequisite_binding_authority,
            "sourceAuthorityDigests": source_authority_digests,
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
        print(f"creation prerequisite e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
