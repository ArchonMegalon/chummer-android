#!/usr/bin/env python3
"""Exercise native runner editing on an already-booted API 36 emulator."""

from __future__ import annotations

import argparse
import hashlib
import json
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
BOUNDS = re.compile(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]")
DISPLAY_SIZE = re.compile(r"(?:Physical|Override) size:\s*(\d+)x(\d+)")
COMPONENT = re.compile(
    r"(?P<package>[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)/"
    r"(?P<activity>\.?[A-Za-z0-9_$]+(?:\.[A-Za-z0-9_$]+)*)"
)
PROCESS_ID = re.compile(r"[1-9][0-9]*")
SHA256_TEXT = re.compile(r"[0-9a-f]{64}")
BODY_TOTAL_DESCRIPTION = re.compile(
    r"^Body\.\s+(?:Selected\s+·\s+)?(?P<total>[0-9]+)(?:\s+·|$)"
)
MAX_LAUNCH_EVIDENCE_CHARACTERS = 1_000_000


@dataclass(frozen=True)
class UiNode:
    attributes: dict[str, str]

    @property
    def center(self) -> tuple[int, int]:
        match = BOUNDS.fullmatch(self.attributes.get("bounds", ""))
        if match is None:
            raise RuntimeError(f"Node has no tappable bounds: {self.attributes}")
        left, top, right, bottom = (int(value) for value in match.groups())
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


class Device:
    def __init__(self, adb: Path, serial: str, evidence: Path) -> None:
        self.adb = adb
        self.serial = serial
        self.evidence = evidence
        self._display_size: tuple[int, int] | None = None
        self.evidence.mkdir(parents=True, exist_ok=True)

    def run(
        self,
        *arguments: str,
        timeout: int = 120,
        text: bool = True,
        check: bool = True,
    ) -> subprocess.CompletedProcess:
        command = [str(self.adb), "-s", self.serial, *arguments]
        return subprocess.run(
            command,
            check=check,
            capture_output=True,
            text=text,
            timeout=timeout,
        )

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        return self.run("shell", *arguments, timeout=timeout).stdout.strip()

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
        self.push(local_path, remote_path)
        output = self.shell("sha256sum", remote_path)
        fields = output.split()
        actual = fields[0].lower() if fields else ""
        if SHA256_TEXT.fullmatch(actual) is None or actual != expected_sha256:
            raise RuntimeError(
                f"Fixture transport digest mismatch for {remote_path!r}: "
                f"expected {expected_sha256}, got {actual or 'unavailable'}"
            )
        return actual

    def hierarchy(self) -> list[UiNode]:
        try:
            dump_output = self.shell(
                "uiautomator",
                "dump",
                "--compressed",
                "/sdcard/chummer-editing-window.xml",
            )
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
            xml = self.run(
                "exec-out", "cat", "/sdcard/chummer-editing-window.xml"
            ).stdout
        except subprocess.CalledProcessError as error:
            detail = "\n".join(
                part for part in (str(error), error.stdout, error.stderr) if part
            )
            (self.evidence / "last-invalid-hierarchy.txt").write_text(
                detail,
                encoding="utf-8",
            )
            return []

        hierarchy_start = xml.find("<hierarchy")
        if hierarchy_start < 0:
            (self.evidence / "last-invalid-hierarchy.txt").write_text(
                xml or "uiautomator returned an empty hierarchy",
                encoding="utf-8",
            )
            return []
        try:
            root = ET.fromstring(xml[hierarchy_start:])
        except ET.ParseError as error:
            (self.evidence / "last-invalid-hierarchy.txt").write_text(
                f"{error}\n{xml}",
                encoding="utf-8",
            )
            return []
        return [UiNode(dict(node.attrib)) for node in root.iter("node")]

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
    ) -> UiNode:
        """Return exactly one accessibility node with an exact resource id.

        Authority evidence must never use the driver's permissive text/prefix
        selector. A missing node means no authority was published; duplicate
        nodes make the rendered proof ambiguous. Both conditions fail closed.
        """
        deadline = time.monotonic() + timeout
        scrolls = 0
        while time.monotonic() < deadline:
            matches = [
                node
                for node in self.hierarchy()
                if node.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ]
            if len(matches) == 1:
                return matches[0]
            if len(matches) > 1:
                self.capture("workspace-authority-cardinality-invalid")
                raise RuntimeError(
                    "Workspace authority accessibility node "
                    f"{selector!r} has cardinality {len(matches)}; expected exactly one"
                )
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
        self.capture("workspace-authority-unavailable")
        raise RuntimeError(
            "Timed out waiting for exactly one workspace authority accessibility "
            f"node {selector!r}"
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

    def dismiss_system_ui_anr(self) -> bool:
        wait_button = self.find("aerr_wait")
        if wait_button is None:
            return False
        x, y = wait_button.center
        self.shell("input", "tap", str(x), str(y))
        return True

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

    def wait_exact_resource_id_bidirectional(
        self,
        selector: str,
        *,
        timeout: int = 90,
        backward_scrolls: int = 24,
        forward_scrolls: int = 24,
        scroll_distance_ratio: float = 0.22,
    ) -> UiNode:
        """Reset a refreshed page to its top, then scan forward for one exact ID."""
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
            node = self.find_exact_resource_id(selector)
            if node is not None and self.node_has_tappable_bounds(node):
                return node
            if self.dismiss_system_ui_anr():
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
        self.capture("failure")
        raise RuntimeError(
            f"Timed out waiting for exact UI resource {selector!r} "
            "after a bounded bidirectional search"
        )

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

    def node_has_tappable_bounds(self, node: UiNode) -> bool:
        match = BOUNDS.fullmatch(node.attributes.get("bounds", ""))
        if match is None:
            return False
        left, top, right, bottom = (int(value) for value in match.groups())
        width, height = self.display_size()
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

    def display_size(self) -> tuple[int, int]:
        if self._display_size is None:
            output = self.shell("wm", "size")
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
        self.shell(
            "input",
            "swipe",
            str(x),
            str(start_y),
            str(x),
            str(end_y),
            "300",
        )

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
        self.shell(
            "input",
            "swipe",
            str(x),
            str(start_y),
            str(x),
            str(end_y),
            "300",
        )

    def open_navigation_drawer(self) -> None:
        for selector in ("Open navigation drawer", "Navigate up", "Show navigation menu"):
            node = self.find(selector)
            if node is not None:
                x, y = node.center
                self.shell("input", "tap", str(x), str(y))
                return
        self.shell("input", "tap", "48", "96")

    def capture(self, name: str) -> None:
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


def save_and_read_workspace_authority(
    device: Device,
    profile: str,
) -> WorkspaceAuthority:
    if profile != "phone":
        raise RuntimeError("The API 36 beta authority gate is phone-only; tablet proof is deferred")
    device.tap("Home")
    device.wait("Continue building", timeout=90)
    open_build(device, profile)
    device.tap("build-save-runner")
    device.wait(
        "Saved.",
        timeout=90,
        scroll=True,
        max_scrolls=48,
        scroll_distance_ratio=0.22,
    )
    device.tap("Home")
    device.wait("Continue building", timeout=90)
    authority = read_workspace_authority(device)
    require_saved_authority(authority)
    return authority


def open_build(device: Device, profile: str) -> None:
    if profile == "tablet":
        device.open_navigation_drawer()
        device.tap("Build")
        device.wait("tablet-build-layout", timeout=45)
        return
    device.tap("Build")


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


def reset_collection_editor_to_top(device: Device, profile: str) -> None:
    reset_scroll_to_top(
        device,
        x_ratio=0.82 if profile == "tablet" else 0.5,
        swipes=12,
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
    device.tap_bidirectional(
        "build-section-tab-attributes",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.22,
    )
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
    device.tap(
        "build-section-tab-combat",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
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
    device.tap_bidirectional(
        "build-section-tab-gear",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.22,
        exact_resource_id=True,
    )
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
    device.tap_bidirectional(
        "build-section-tab-relationships",
        timeout=120,
        backward_scrolls=24,
        forward_scrolls=24,
        scroll_distance_ratio=0.22,
        exact_resource_id=True,
    )
    time.sleep(5)
    device.tap_bidirectional(
        action_selector,
        timeout=180,
        backward_scrolls=24,
        forward_scrolls=48,
        scroll_distance_ratio=0.22,
        exact_resource_id=True,
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
        device.wait("Downloads", timeout=45)
        device.tap("Downloads")
        time.sleep(0.75)
        device.wait("Files in Downloads", timeout=45)
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
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True)
    device.wait("dialog-action-complete-new-character-workflow", timeout=45, scroll=True)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True)

    creation_authority: WorkspaceAuthority | None = None
    if profile == "phone":
        # A new creation-mode runner now routes directly to the fail-closed wizard.
        # The unrestricted editor must remain unavailable until creation is complete.
        device.wait("creation-wizard-dashboard", timeout=90)
        device.capture("new-runner-creation-wizard")
        # Importing another dossier is correctly blocked while the current workspace
        # is dirty. Persist this incomplete creation draft without claiming that the
        # creation workflow itself has completed, then switch to the signed fixture.
        device.tap("build-save-runner")
        device.wait(
            "Saved.",
            timeout=90,
            scroll=True,
            max_scrolls=48,
            scroll_distance_ratio=0.22,
        )
        device.tap("Home")
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
    # Bind the transition to the selected career fixture. A picker dismissal or a
    # guarded no-op can leave a generic Continue button on the prior workspace.
    device.wait(completed_runner_alias, timeout=90)
    device.wait("Continue building", timeout=90)
    if profile == "phone":
        imported_authority = read_workspace_authority(device)
        require_import_authority(
            imported_authority,
            completed_runner_sha256,
            creation_authority.workspace_id if creation_authority is not None else None,
        )
        return imported_authority
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


def add_and_edit_gear(device: Device, profile: str) -> None:
    open_gear_section(device, profile)
    device.tap("tablet-quick-gear-add" if profile == "tablet" else "section-quick-gear-add", scroll=True)
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
    reset_scroll_to_top(
        device,
        x_ratio=0.375 if profile == "tablet" else 0.5,
        swipes=6,
    )
    tap_collection_item(device, "Ares Predator V")

    if profile == "tablet":
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
        return

    device.set_text("collection-field-customname", "Custom Name", "GearProofE2E")
    device.tap("Save changes", scroll=True)
    reset_scroll_to_top(device, swipes=6)
    device.assert_text("GearProofE2E")
    device.back()


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
    full_editing_runner_sha256 = fixture_sha256[args.full_editing_runner.resolve()]
    condition_runner_sha256 = fixture_sha256[args.condition_runner.resolve()]
    contact_pet_runner_sha256 = fixture_sha256[args.contact_pet_runner.resolve()]

    device = Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Editing E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-incremental", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
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
        device.wait("Continue building", timeout=90)
        if args.profile == "phone":
            imported_authority = read_workspace_authority(device)
            require_import_authority(imported_authority, expected_fixture_sha256)
        else:
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

        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "serial": args.serial,
            "profile": args.profile,
            "journey": args.journey,
            "apiLevel": int(api),
            "apk": str(args.apk.resolve()),
            "apkSha256": sha256(args.apk.resolve()),
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

        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "serial": args.serial,
            "profile": args.profile,
            "journey": args.journey,
            "apiLevel": int(api),
            "apk": str(args.apk.resolve()),
            "apkSha256": sha256(args.apk.resolve()),
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

    add_and_edit_gear(device, args.profile)
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
    reset_scroll_to_top(
        device,
        x_ratio=0.375 if args.profile == "tablet" else 0.5,
        swipes=6,
    )
    tap_collection_item(device, "Ares Predator V")
    gear_field = "tablet-field-customname" if args.profile == "tablet" else "collection-field-customname"
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

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": args.profile,
        "journey": args.journey,
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
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
