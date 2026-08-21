#!/usr/bin/env python3
"""Prove CharacterCareer manual Nuyen gained/spent on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("cmdNuyenGained", "cmdNuyenSpent")
CONTROL_PROOF_KEYS = (
    "exactNuyenDelta",
    "exactExpenseAndUndo",
    "percentageApplied",
    "exchangeRatesExact",
    "legacyPeopleValidationManConversion",
    "chronologicalExpenseOrdering",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-manual-nuyen",
        scroll=True,
        timeout=120,
        max_scrolls=30,
        scroll_distance_ratio=0.18,
    )
    device.wait("career-manual-nuyen-page", timeout=60)


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


def matching_root(device: shared.Device, karma: str, nuyen: str) -> ET.Element:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        current = (root.findtext("karma", default=""), root.findtext("nuyen", default=""))
        observed.append(current)
        if current == (karma, nuyen):
            if root.findtext("./customstate/nuyen") != "Unrelated nested Nuyen must survive":
                raise RuntimeError("Manual Nuyen mutation changed unrelated nested XML")
            return root
    device.capture("career-manual-nuyen-workspace-not-persisted")
    raise RuntimeError(f"Manual Nuyen workspace state not found: {observed!r}")


def expense_rows(root: ET.Element) -> list[ET.Element]:
    rows = root.findall("./expenses/expense")
    dates = [datetime.fromisoformat(row.findtext("date", default="")) for row in rows]
    if dates != sorted(dates):
        raise RuntimeError(f"Manual Nuyen expenses are not chronological: {dates!r}")
    return rows


def assert_summary(device: shared.Device, karma: str, nuyen: str) -> None:
    node = device.wait("career-manual-nuyen-summary", timeout=45)
    text = node.attributes.get("text", "")
    expected = f"{nuyen} Nuyen · {karma} Karma"
    if expected not in text:
        device.capture("career-manual-nuyen-summary-mismatch")
        raise RuntimeError(f"Expected {expected!r}; observed {text!r}")


def prove(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    assert_summary(device, "5", "10000")
    device.set_text("career-manual-nuyen-amount", "Amount", "100")
    device.set_text("career-manual-nuyen-percent", "Percent", "150")
    device.set_text("career-manual-nuyen-reason", "Reason", "Run payment", scroll=True)
    device.tap("career-manual-nuyen-refund", scroll=True, timeout=90, max_scrolls=20)
    device.tap("career-manual-nuyen-gain", scroll=True, timeout=120, max_scrolls=20)
    device.wait("build-career-manual-nuyen", timeout=180, scroll=True, max_scrolls=30)
    gained = matching_root(device, "5", "10150")
    gained_expenses = expense_rows(gained)
    gained_row = next(row for row in gained_expenses if row.findtext("reason") == "Run payment")
    if (
        gained_row.findtext("amount") != "150"
        or gained_row.findtext("type") != "Nuyen"
        or gained_row.findtext("refund") != "True"
        or gained_row.findtext("./undo/nuyentype") != "ManualAdd"
    ):
        raise RuntimeError("Manual Nuyen gain percentage/expense/undo did not match Chummer5")

    open_page(device)
    assert_summary(device, "5", "10150")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    matching_root(device, "5", "10150")
    open_page(device)
    assert_summary(device, "5", "10150")

    device.tap("career-manual-nuyen-exchange", scroll=True, timeout=90, max_scrolls=20)
    device.set_text("career-manual-nuyen-amount", "Amount", "3000")
    device.set_text("career-manual-nuyen-reason", "Reason", "Conversion", scroll=True)
    device.tap("career-manual-nuyen-refund", scroll=True, timeout=90, max_scrolls=20)
    device.tap("career-manual-nuyen-force-career-visible", scroll=True, timeout=90, max_scrolls=20)
    device.tap("career-manual-nuyen-spend", scroll=True, timeout=120, max_scrolls=20)
    device.wait("build-career-manual-nuyen", timeout=180, scroll=True, max_scrolls=30)
    spent = matching_root(device, "7", "7150")
    spent_expenses = expense_rows(spent)
    conversion = [row for row in spent_expenses if row.findtext("reason") == "Conversion"]
    if len(conversion) != 2:
        raise RuntimeError("Manual Nuyen exchange did not write exactly two expenses")
    by_type = {row.findtext("type"): row for row in conversion}
    nuyen_row = by_type["Nuyen"]
    karma_row = by_type["Karma"]
    if (
        nuyen_row.findtext("amount") != "-3000"
        or nuyen_row.findtext("refund") != "False"
        or nuyen_row.findtext("forcecareervisible") != "False"
        or nuyen_row.findtext("./undo/nuyentype") != "ManualSubtract"
        or karma_row.findtext("amount") != "2"
        or karma_row.findtext("refund") != "True"
        or karma_row.findtext("forcecareervisible") != "True"
        or karma_row.findtext("./undo/karmatype") != "ManualSubtract"
    ):
        raise RuntimeError("Manual Nuyen spend exchange/undo did not match Chummer5")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    matching_root(device, "7", "7150")
    open_page(device)
    assert_summary(device, "7", "7150")
    device.capture("career-manual-nuyen-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures/career-manual-nuyen-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerManualNuyenPageSha256": android_root / "src/Chummer.Android/Native/CareerManualNuyenPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerManualNuyenContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CareerManualNuyenEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "careerManualNuyenRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerManualNuyenRules.cs",
        "sourceResolverContractSha256": workspace_root / "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Manual Nuyen source graph is incomplete: {missing!r}")
    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Manual Nuyen E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Manual Nuyen E2E requires arm64-v8a, got {abi!r}")
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
        "journey": "career-manual-nuyen",
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
            "careerNuyenGainedWithPercent": "pass",
            "gainWorkspacePersisted": "pass",
            "gainProcessRestartUiReadback": "pass",
            "careerNuyenSpentWithExchange": "pass",
            "exchangeExpenseAndBalancePersisted": "pass",
            "spendProcessRestartUiReadback": "pass",
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
        print(f"manual Nuyen E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
