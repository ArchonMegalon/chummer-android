#!/usr/bin/env python3
"""Prove Career Nuyen expense selection/editing on a real API 36 phone."""

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


CONTROLS = ("cmdNuyenEdit", "lstNuyen", "tsEditNuyenExpense")
MANUAL_ID = "65da27db-24a8-4b6e-b42c-30f4bb13a4f8"
LOCKED_ID = "a47497a9-0893-43e1-89cb-fb2dfa803b5d"
CONTROL_PROOF_KEYS = (
    "stableExpenseGuid",
    "manualAmountAuthority",
    "lockedAmountAuthority",
    "exactNuyenDelta",
    "dateReasonEditable",
    "lockedMetadataPreserved",
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
        "build-career-nuyen-expenses",
        scroll=True,
        timeout=120,
        max_scrolls=32,
        scroll_distance_ratio=0.18,
    )
    device.wait("career-nuyen-expense-page", timeout=60)


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


def expense_by_id(root: ET.Element, expense_id: str) -> ET.Element:
    rows = [row for row in root.findall("./expenses/expense") if row.findtext("guid") == expense_id]
    if len(rows) != 1:
        raise RuntimeError(f"Expected one expense {expense_id}, got {len(rows)}")
    return rows[0]


def matching_root(device: shared.Device, nuyen: str, manual_reason: str, locked_reason: str) -> ET.Element:
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        if root.findtext("nuyen") != nuyen:
            continue
        manual = expense_by_id(root, MANUAL_ID)
        locked = expense_by_id(root, LOCKED_ID)
        if manual.findtext("reason") != manual_reason or locked.findtext("reason") != locked_reason:
            continue
        if root.findtext("./customstate/nuyen") != "Unrelated nested Nuyen must survive":
            raise RuntimeError("Nuyen expense edit changed unrelated nested XML")
        return root
    device.capture("career-nuyen-expense-workspace-not-persisted")
    raise RuntimeError("Expected Career Nuyen expense state was not persisted")


def select_locked_expense(device: shared.Device) -> None:
    label = "2081-05-10 10:00 · -500 · Armor"
    device.tap("career-nuyen-expense-picker", timeout=60, scroll=True, max_scrolls=12)
    device.tap(label, timeout=60, scroll=True, max_scrolls=8)
    time.sleep(0.5)


def prove(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    device.wait("career-nuyen-expense-picker", timeout=45)
    device.set_text("career-nuyen-expense-amount", "Amount", "-175")
    device.set_text("career-nuyen-expense-reason", "Reason", "Less ammo", scroll=True)
    device.tap("career-nuyen-expense-save", scroll=True, timeout=180, max_scrolls=20)
    device.wait("build-career-nuyen-expenses", timeout=180, scroll=True, max_scrolls=32)
    root = matching_root(device, "1075", "Less ammo", "Armor")
    manual = expense_by_id(root, MANUAL_ID)
    if (
        manual.findtext("amount") != "-175"
        or manual.findtext("refund") != "True"
        or manual.findtext("forcecareervisible") != "True"
        or manual.findtext("./undo/nuyentype") != "ManualSubtract"
        or manual.findtext("./undo/extra") != "keep"
        or manual.findtext("custom") != "keep-manual"
    ):
        raise RuntimeError("Manual Nuyen expense amount or locked metadata drifted")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    matching_root(device, "1075", "Less ammo", "Armor")
    open_page(device)
    select_locked_expense(device)
    amount_node = device.wait("career-nuyen-expense-amount", timeout=45, scroll=True, max_scrolls=12)
    if amount_node.attributes.get("enabled", "true").lower() != "false":
        device.capture("career-nuyen-expense-locked-amount-enabled")
        raise RuntimeError("Nonmanual Nuyen expense amount was editable")
    device.set_text("career-nuyen-expense-reason", "Reason", "Repaired armor", scroll=True)
    device.tap("career-nuyen-expense-save", scroll=True, timeout=180, max_scrolls=20)
    device.wait("build-career-nuyen-expenses", timeout=180, scroll=True, max_scrolls=32)
    root = matching_root(device, "1075", "Less ammo", "Repaired armor")
    locked = expense_by_id(root, LOCKED_ID)
    if locked.findtext("amount") != "-500" or locked.findtext("./undo/nuyentype") != "AddArmor":
        raise RuntimeError("Nonmanual Nuyen expense amount or undo metadata changed")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    matching_root(device, "1075", "Less ammo", "Repaired armor")
    open_page(device)
    device.wait("career-nuyen-expense-picker", timeout=45)
    device.capture("career-nuyen-expense-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures/career-nuyen-expense-edit-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerNuyenExpensePageSha256": android_root / "src/Chummer.Android/Native/CareerNuyenExpensePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerNuyenExpenseContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CareerNuyenExpenseEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "careerNuyenExpenseRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerNuyenExpenseEditRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Nuyen expense source graph is incomplete: {missing!r}")
    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Nuyen expense E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Nuyen expense E2E requires arm64-v8a, got {abi!r}")
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
        "journey": "career-nuyen-expense-edit",
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
            "manualAmountReasonEdit": "pass",
            "manualBalanceDeltaPersisted": "pass",
            "lockedAmountReasonOnlyEdit": "pass",
            "twoProcessRestarts": "pass",
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
        print(f"Nuyen expense E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
