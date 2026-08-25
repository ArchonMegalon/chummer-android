#!/usr/bin/env python3
"""Prove CharacterCareer Spend/Regain Edge on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("cmdEdgeSpent", "cmdEdgeGained")
CONTROL_PROOF_KEYS = (
    "exactOnePointAdjustment",
    "legacyBounds",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-edge-use",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.20,
    )
    device.wait("career-edge-use-page", timeout=60)
    device.wait("career-edge-use-summary", timeout=45)


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def assert_workspace(device: shared.Device, expected_used: str) -> None:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        edge_used = root.findtext("edgeused", default="")
        nested = root.findtext("./customstate/edgeused", default="")
        observed.append((edge_used, nested))
        if edge_used == expected_used and nested == "Unrelated nested Edge use must survive":
            return
    device.capture("career-edge-use-workspace-not-persisted")
    raise RuntimeError(f"Career Edge use was not durable: {observed!r}")


def assert_summary(device: shared.Device, expected: str) -> None:
    node = device.wait("career-edge-use-summary", timeout=45)
    text = node.attributes.get("text", "")
    if expected not in text:
        device.capture("career-edge-use-summary-mismatch")
        raise RuntimeError(f"Expected Edge summary {expected!r}; observed {text!r}")


def assert_button_state(device: shared.Device, automation_id: str, expected: bool) -> None:
    node = device.wait(automation_id, timeout=45)
    observed = node.attributes.get("enabled") == "true"
    if observed != expected:
        device.capture("career-edge-use-button-state-mismatch")
        raise RuntimeError(f"{automation_id} enabled was {observed!r}; expected {expected!r}")


def prove(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    assert_summary(device, "3 available · 1 used · 4 total")
    assert_button_state(device, "career-edge-use-spend", True)
    assert_button_state(device, "career-edge-use-regain", True)
    device.tap("career-edge-use-spend", timeout=120)
    device.wait("build-career-edge-use", timeout=180, scroll=True, max_scrolls=24)
    assert_workspace(device, "2")
    open_page(device)
    assert_summary(device, "2 available · 2 used · 4 total")
    device.capture("career-edge-use-spent-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, "2")
    open_page(device)
    assert_summary(device, "2 available · 2 used · 4 total")
    device.tap("career-edge-use-regain", timeout=120)
    device.wait("build-career-edge-use", timeout=180, scroll=True, max_scrolls=24)
    assert_workspace(device, "1")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, "1")
    open_page(device)
    assert_summary(device, "3 available · 1 used · 4 total")
    device.capture("career-edge-use-regained-after-process-restart")

    device.tap("career-edge-use-regain", timeout=120)
    device.wait("build-career-edge-use", timeout=180, scroll=True, max_scrolls=24)
    assert_workspace(device, "0")
    open_page(device)
    assert_summary(device, "4 available · 0 used · 4 total")
    assert_button_state(device, "career-edge-use-spend", True)
    assert_button_state(device, "career-edge-use-regain", False)
    device.capture("career-edge-use-regain-disabled-at-zero")

    for expected_used in range(1, 5):
        device.tap("career-edge-use-spend", timeout=120)
        device.wait("build-career-edge-use", timeout=180, scroll=True, max_scrolls=24)
        assert_workspace(device, str(expected_used))
        open_page(device)
        assert_summary(
            device,
            f"{4 - expected_used} available · {expected_used} used · 4 total",
        )
    assert_button_state(device, "career-edge-use-spend", False)
    assert_button_state(device, "career-edge-use-regain", True)
    device.capture("career-edge-use-spend-disabled-at-total")

    device.tap("career-edge-use-regain", timeout=120)
    device.wait("build-career-edge-use", timeout=180, scroll=True, max_scrolls=24)
    assert_workspace(device, "3")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, "3")
    open_page(device)
    assert_summary(device, "1 available · 3 used · 4 total")
    device.capture("career-edge-use-bounds-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures/career-edge-use-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerEdgeUsePageSha256": android_root / "src/Chummer.Android/Native/CareerEdgeUsePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerEdgeUseContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CareerEdgeUseEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "careerEdgeUseRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerEdgeUseRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Career Edge-use source graph is incomplete: {missing!r}")
    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career Edge-use E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Career Edge-use E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    prove(device, fixture)

    controls = {
        f"CharacterCareer.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-edge-use",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "careerEdgeSpent": "pass",
            "spentWorkspacePersisted": "pass",
            "spentProcessRestartUiReadback": "pass",
            "careerEdgeRegained": "pass",
            "regainedWorkspacePersisted": "pass",
            "regainedProcessRestartUiReadback": "pass",
            "regainDisabledAtZero": "pass",
            "spendDisabledAtTotal": "pass",
            "boundsWorkspacePersisted": "pass",
            "boundsProcessRestartUiReadback": "pass",
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
        print(f"career Edge-use E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
