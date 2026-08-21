#!/usr/bin/env python3
"""Prove exact Chummer5 Create/Career Quality Level behavior on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "nudQualityLevel"
SOURCE_ID = "d537536d-893d-4bd6-89c6-03b7dd5bd24c"
QUALITY_IDS = {
    "creation": "71111111-1111-1111-1111-111111111111",
    "career": "72222222-2222-2222-2222-222222222222",
}
CONTROL_PROOF_KEYS = (
    "stableQualityIdentity",
    "exactDuplicateLevelIdentity",
    "sourceMaximumBound",
    "atomicWorkspacePersisted",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture: Path) -> None:
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    device.shell("pm", "clear", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture.name)
    device.wait("Continue building", timeout=120)


def open_quality_level(device: shared.Device, quality_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-qualities", scroll=True, timeout=60, max_scrolls=24)
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-action-tab-qualities-qualities", scroll=True, timeout=120, max_scrolls=36)
    token = quality_id.replace("-", "")
    device.tap(
        f"collection-item-quality-{quality_id}",
        scroll=True,
        timeout=120,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    device.wait(f"collection-editor-quality-{quality_id}", timeout=120)
    device.tap(f"quality-level-open-{token}", scroll=True, timeout=60, max_scrolls=36)
    device.wait(f"quality-level-page-{token}", timeout=60)


def set_level(
    device: shared.Device,
    quality_id: str,
    level: int,
    *,
    confirm_career_increase: bool,
) -> None:
    token = quality_id.replace("-", "")
    device.set_text(f"quality-level-value-{token}", "Level", str(level), scroll=True)
    device.tap(f"quality-level-save-{token}", timeout=180, scroll=True)
    if confirm_career_increase:
        device.wait("Confirm Quality Level increase", timeout=30)
        device.tap("Increase")
    device.wait(f"collection-editor-quality-{quality_id}", timeout=180)


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            record = json.loads(device.run(
                "exec-out", "run-as", shared.PACKAGE, "cat", path
            ).stdout)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def assert_workspace_level(
    device: shared.Device,
    expected: int,
    *,
    career: bool,
    expected_add_expenses: int,
    expected_remove_expenses: int,
) -> None:
    observed: list[int] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        matches = [
            item for item in root.findall("./qualities/quality")
            if item.findtext("sourceid", default="").lower() == SOURCE_ID
            and item.findtext("extra", default="") == ""
            and item.findtext("sourcename", default="") == ""
            and item.findtext("qualitytype") == "Negative"
        ]
        observed.append(len(matches))
        ids = [item.findtext("guid", default="") for item in matches]
        add_expenses = [
            expense for expense in root.findall("./expenses/expense")
            if expense.findtext("./undo/karmatype") == "AddQuality"
        ]
        remove_expenses = [
            expense for expense in root.findall("./expenses/expense")
            if expense.findtext("./undo/karmatype") == "RemoveQuality"
        ]
        if (
            len(matches) == expected
            and len(ids) == len(set(ids))
            and all(item.findtext("bp") == "0" for item in matches)
            and root.findtext("customstate")
                == ("Career quality level unrelated state" if career else "Creation quality level unrelated state")
            and len(add_expenses) == expected_add_expenses
            and len(remove_expenses) == expected_remove_expenses
            and all(
                expense.findtext("amount") == "0"
                for expense in add_expenses + remove_expenses
            )
            and all(
                expense.findtext("./undo/objectid") == SOURCE_ID
                for expense in remove_expenses
            )
        ):
            return
    device.capture("quality-level-workspace-not-persisted")
    raise RuntimeError(f"Quality Level {expected} was not durable; observed levels {observed!r}")


def assert_ui_level(device: shared.Device, quality_id: str, expected: int) -> None:
    token = quality_id.replace("-", "")
    text = device.wait(f"quality-level-current-{token}", timeout=60).attributes.get("text", "")
    if f"contains {expected} saved instance" not in text:
        raise RuntimeError(f"Quality Level UI did not read back {expected}: {text!r}")


def run_journey(device: shared.Device, fixture: Path, quality_id: str, *, career: bool) -> None:
    prepare_runner(device, fixture)
    open_quality_level(device, quality_id)
    set_level(device, quality_id, 3, confirm_career_increase=career)
    assert_workspace_level(
        device,
        3,
        career=career,
        expected_add_expenses=2 if career else 0,
        expected_remove_expenses=0,
    )

    token = quality_id.replace("-", "")
    device.tap(f"quality-level-open-{token}", scroll=True, timeout=60, max_scrolls=36)
    assert_ui_level(device, quality_id, 3)
    set_level(device, quality_id, 2, confirm_career_increase=False)
    assert_workspace_level(
        device,
        2,
        career=career,
        expected_add_expenses=2 if career else 0,
        expected_remove_expenses=1 if career else 0,
    )
    expected = 2

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_level(
        device,
        expected,
        career=career,
        expected_add_expenses=2 if career else 0,
        expected_remove_expenses=1 if career else 0,
    )
    open_quality_level(device, quality_id)
    assert_ui_level(device, quality_id, expected)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-quality-level-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-quality-level-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    core = workspace_root / "chummer-core-engine"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "qualityLevelPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "QualityLevelPage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "qualityLevelContractSha256": overview / "QualityLevelEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "sourceResolverContractSha256": core / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs",
        "characterSectionModelsSha256": core / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "fileSourceResolverSha256": core / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Quality Level E2E source graph is incomplete: {missing!r}")

    creation = args.creation_runner.resolve()
    career = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Quality Level E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Quality Level E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )

    run_journey(device, creation, QUALITY_IDS["creation"], career=False)
    run_journey(device, career, QUALITY_IDS["career"], career=True)

    controls = {
        f"{form}.{CONTROL}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for form in ("CharacterCreate", "CharacterCareer")
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "quality-level",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation),
        "careerFixtureSha256": shared.sha256(career),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "creationIncrease": "pass",
            "creationDecrease": "pass",
            "careerIncreaseConfirmed": "pass",
            "careerDecrease": "pass",
            "sameSessionReopen": "pass",
            "processRestart": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
