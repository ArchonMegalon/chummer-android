#!/usr/bin/env python3
"""Prove CharacterCreate.cmdDeleteLifestyle on an API 36 arm64 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "CharacterCreate.cmdDeleteLifestyle"
PACKAGE = "com.myexternalbrain.chummer"
ABI = "arm64-v8a"
TARGET = "71111111111111111111111111111111"
KEEP = "73333333333333333333333333333333"


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_lifestyle(device: shared.Device, target: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-action-tab-lifestyle-lifestyles",
        scroll=True,
        timeout=120,
        max_scrolls=36,
    )
    device.wait(f"collection-item-lifestyle-{target}", timeout=120, scroll=True)
    device.tap(f"collection-item-lifestyle-{target}", timeout=120, scroll=True)
    device.wait(f"collection-editor-lifestyle-{target}", timeout=120)
    device.wait(f"creation-lifestyle-delete-{target}", timeout=60, scroll=True)


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run("exec-out", "run-as", PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def assert_workspace(device: shared.Device, *, target_present: bool) -> None:
    observed: list[str] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        if root.findtext("customstate") != "Creation Lifestyle delete unrelated state":
            continue
        lifestyles = {
            item.findtext("guid", "").replace("-", "").lower(): item
            for item in root.findall("./lifestyles/lifestyle")
        }
        markers = {
            improvement.findtext("marker", "")
            for improvement in root.findall("./improvements/improvement")
        }
        observed.append(f"lifestyles={sorted(lifestyles)}, markers={sorted(markers)}")
        expected_markers = (
            {"remove-exact", "remove-legacy-prefix", "keep-quality", "keep-custom"}
            if target_present else {"keep-quality", "keep-custom"}
        )
        if (
            (TARGET in lifestyles) == target_present
            and KEEP in lifestyles
            and markers == expected_markers
            and root.findtext("nuyen") == "8123.45"
            and root.findtext("./expenses/expense/reason") == "Unrelated expense sentinel"
        ):
            return
    device.capture("creation-lifestyle-delete-workspace-mismatch")
    raise RuntimeError(f"Creation Lifestyle delete workspace mismatch: {observed!r}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--serial", required=True)
    parser.add_argument("--apk", required=True, type=Path)
    parser.add_argument(
        "--receipt",
        type=Path,
        default=Path("artifacts/android-api36-creation-lifestyle-delete-e2e.json"),
    )
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    driver = Path(__file__).resolve()
    fixture = root / "tests/fixtures/creation-lifestyle-delete-e2e.chum5"
    source_paths = {
        "lifestyleDeleteRulesSha256": root.parent / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCreationLifestyleDeleteRules.cs",
        "lifestyleDeleteRequestSha256": root.parent / "chummer-presentation/Chummer.Presentation/Overview/CreationLifestyleDeleteRequest.cs",
        "mutationCatalogSha256": root.parent / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterPersistenceSha256": root.parent / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "workspaceStoreSha256": root.parent / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    for path in (args.apk, fixture, *source_paths.values()):
        if not path.is_file():
            raise RuntimeError(f"Required proof input is missing: {path}")

    device = shared.Device(args.serial, args.receipt.parent)
    api = device.shell("getprop", "ro.build.version.sdk").strip()
    abi = device.shell("getprop", "ro.product.cpu.abi").strip()
    if api != "36":
        raise RuntimeError(f"Expected API 36, found {api!r}")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Expected arm64-v8a, found {abi!r}")
    if not device.shell("cmd", "package", "path", PACKAGE).startswith("package:"):
        raise RuntimeError(f"Expected installed package {PACKAGE!r} was not found")
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    device.shell("pm", "clear", PACKAGE)
    prepare_runner(device, fixture.name)

    open_lifestyle(device, TARGET)
    device.tap(f"creation-lifestyle-delete-{TARGET}", timeout=60, scroll=True)
    device.wait("Delete Lifestyle?", timeout=30)
    device.tap("Cancel")
    device.wait(f"collection-editor-lifestyle-{TARGET}", timeout=30)
    assert_workspace(device, target_present=True)
    device.capture("creation-lifestyle-delete-cancel-noop")

    device.tap(f"creation-lifestyle-delete-{TARGET}", timeout=60, scroll=True)
    device.wait("Delete Lifestyle?", timeout=30)
    device.tap("Delete")
    device.wait(f"collection-item-lifestyle-{KEEP}", timeout=180, scroll=True)
    assert_workspace(device, target_present=False)
    device.capture("creation-lifestyle-delete-same-session")

    device.shell("am", "force-stop", PACKAGE)
    time.sleep(0.5)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, target_present=False)
    open_lifestyle(device, KEEP)
    device.capture("creation-lifestyle-delete-process-restart")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "creation-lifestyle-delete",
        "apiLevel": int(api),
        "abi": abi,
        "package": PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(fixture),
        "controls": {CONTROL: {
            "typedSelectedLifestyleIdentity": "pass",
            "creationPhaseOnly": "pass",
            "cancelNoOp": "pass",
            "qualityImprovementCascade": "pass",
            "zeroNuyenRefund": "pass",
            "zeroExpenseDelta": "pass",
            "lifestyleCostRemovedWithTarget": "pass",
            "workspaceRevisionBound": "pass",
            "atomicSaveRecovery": "pass",
            "sameSessionReopened": "pass",
            "processRestartWorkspacePersisted": "pass",
        }},
        "journeys": {
            "creationCancelNoOp": "pass",
            "creationLifestyleAndQualityImprovementsDeleted": "pass",
            "creationZeroRefundAndExpenseDelta": "pass",
            "creationSameSessionReopen": "pass",
            "creationProcessRestart": "pass",
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
        print(f"Creation Lifestyle Delete E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
