#!/usr/bin/env python3
"""Compatibility entrypoint for the typed-Core SR5 Priority phone proof.

The historical driver used the retired metatype/priority continuation dialog.
The authoritative physical journey now lives in
``run_api36_creation_prerequisite_e2e`` and exercises the real prerequisite
wizard. ``select_option`` and ``workspace_payloads`` remain public because the
separate Karma driver uses those generic, fail-closed helpers.
"""

from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_creation_prerequisite_e2e as prerequisite
import run_api36_editing_e2e as shared


def find_exact(device: shared.Device, selector: str) -> shared.UiNode | None:
    matches: list[shared.UiNode] = []
    for node in device.hierarchy():
        attributes = node.attributes
        resource_id = attributes.get("resource-id", "").rsplit("/", 1)[-1]
        if selector in {resource_id, attributes.get("content-desc", "")}:
            matches.append(node)
    return next(
        (node for node in matches if node.attributes.get("clickable") == "true"),
        matches[0] if matches else None,
    )


def tap_exact_field(
    device: shared.Device,
    selector: str,
    *,
    scroll: bool,
) -> None:
    if scroll:
        node = device.wait_exact_resource_id_bidirectional(
            selector,
            timeout=90,
            backward_scrolls=18,
            forward_scrolls=18,
            scroll_distance_ratio=0.22,
            evidence_prefix=f"priority-field-{selector}",
            surface_name="Priority dialog field",
        )
        x, y = node.center
        device.shell("input", "tap", str(x), str(y))
        return

    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        node = find_exact(device, selector)
        if node is not None and device.node_has_tappable_bounds(node):
            x, y = node.center
            device.shell("input", "tap", str(x), str(y))
            return
        if device.dismiss_system_ui_anr():
            time.sleep(5)
            continue
        time.sleep(0.75)
    device.capture(f"missing-exact-{selector}")
    raise RuntimeError(f"Timed out waiting for exact UI field {selector!r}")


def tap_exact_option(device: shared.Device, option_label: str) -> None:
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        option = next(
            (
                node
                for node in device.hierarchy()
                if node.attributes.get("text") == option_label
                and node.attributes.get("class", "").endswith("CheckedTextView")
            ),
            None,
        )
        if option is not None and device.node_has_tappable_bounds(option):
            x, y = option.center
            device.shell("input", "tap", str(x), str(y))
            return
        time.sleep(0.5)
    device.capture(f"missing-option-{option_label}")
    raise RuntimeError(f"Timed out waiting for exact picker option {option_label!r}")


def select_option(
    device: shared.Device,
    selector: str,
    option_label: str,
    *,
    scroll: bool = True,
) -> None:
    """Select one exact Android picker value; retained for the Karma driver."""
    tap_exact_field(device, selector, scroll=scroll)
    tap_exact_option(device, option_label)
    shared.reset_scroll_to_top(device, swipes=16)
    deadline = time.monotonic() + 90
    scrolls = 0
    while time.monotonic() < deadline:
        field = find_exact(device, selector)
        if field is not None and field.attributes.get("text") == option_label:
            time.sleep(0.75)
            return
        if device.dismiss_system_ui_anr():
            time.sleep(5)
            continue
        if field is None and scroll and scrolls < 18:
            device.swipe_up(distance_ratio=0.22)
            scrolls += 1
        time.sleep(0.5)
    device.capture(f"picker-{selector}-not-applied")
    raise RuntimeError(f"Picker {selector!r} did not retain {option_label!r}")


def workspace_payloads(device: shared.Device) -> list[str]:
    """Read candidate workspace XML payloads; retained for the Karma driver."""
    listing = device.shell(
        "run-as",
        shared.PACKAGE,
        "find",
        "files/state",
        "-type",
        "f",
    )
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        try:
            raw = device.run(
                "exec-out",
                "run-as",
                shared.PACKAGE,
                "cat",
                path,
            ).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def main(argv: list[str] | None = None) -> int:
    """Run the canonical direct-bootstrap prerequisite physical journey."""
    return prerequisite.main(argv)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"typed-Core Priority prerequisite e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
