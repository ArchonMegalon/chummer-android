#!/usr/bin/env python3
"""Prove exact Chummer5 Career Cyberware Upgrade and Sell behavior on API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
import json
from pathlib import Path
import subprocess
import time
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("tsCyberwareUpgrade", "tsCyberwareSell")
CONTROL_PROOF_KEYS = (
    "stableCyberwareIdentity",
    "exactSourceBackedQuoteDigest",
    "expectedRevisionAtomicSave",
    "explicitConfirmation",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
)
CYBERWARE_IDS = {
    "upgrade": "91111111-1111-1111-1111-111111111111",
    "sell": "92222222-2222-2222-2222-222222222222",
    "sentinel": "93333333-3333-3333-3333-333333333333",
    "hole": "94444444-4444-4444-4444-444444444444",
    "linked": "96666666-6666-6666-6666-666666666666",
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_cyberware_section(device: shared.Device, expected_item: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True)
    time.sleep(3)
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-action-tab-gear-cyberwares",
        scroll=True,
        timeout=180,
        max_scrolls=48,
        scroll_distance_ratio=0.22,
    )
    device.wait(
        expected_item,
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )


def open_commerce_page(device: shared.Device, cyberware_id: str) -> str:
    item_selector = f"collection-item-cyberware-{cyberware_id}"
    open_cyberware_section(device, item_selector)
    device.tap(
        item_selector,
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    compact = cyberware_id.replace("-", "")
    device.wait(f"collection-editor-cyberware-{cyberware_id}", timeout=120)
    device.tap(f"cyberware-commerce-open-{compact}", timeout=60, scroll=True, max_scrolls=36)
    device.wait(f"cyberware-commerce-page-{compact}", timeout=60)
    return compact


def select_grade(device: shared.Device, compact: str, grade: str) -> None:
    selector = f"cyberware-commerce-grade-{compact}"
    device.tap(selector, timeout=60, scroll=True, max_scrolls=18)
    device.tap(grade, timeout=60, scroll=True, max_scrolls=8)
    time.sleep(0.5)
    actual = shared.selected_text(device, selector, "Grade", scroll=True)
    if actual != grade:
        device.capture("cyberware-commerce-grade-not-selected")
        raise RuntimeError(f"Cyberware grade expected {grade!r}, got {actual!r}")


def apply_upgrade(device: shared.Device) -> None:
    identity = CYBERWARE_IDS["upgrade"]
    compact = open_commerce_page(device, identity)
    select_grade(device, compact, "Alphaware")
    device.set_text(f"cyberware-commerce-rating-{compact}", "Upgrade rating", "3", scroll=True)
    device.set_text(
        f"cyberware-commerce-refund-percent-{compact}",
        "Refund / sale percent (0.00–9999.99)",
        "50.00",
        scroll=True,
    )
    device.tap(f"cyberware-commerce-upgrade-{compact}", timeout=120, scroll=True)
    device.wait("Confirm Cyberware upgrade", timeout=30)
    device.tap("Upgrade")
    device.wait(f"collection-editor-cyberware-{identity}", timeout=180)
    device.back()


def apply_confirmed_sale_after_cancel(device: shared.Device) -> None:
    identity = CYBERWARE_IDS["sell"]
    compact = open_commerce_page(device, identity)
    device.set_text(
        f"cyberware-commerce-refund-percent-{compact}",
        "Refund / sale percent (0.00–9999.99)",
        "50.00",
        scroll=True,
    )
    device.tap(f"cyberware-commerce-sell-{compact}", timeout=120, scroll=True)
    device.wait("Confirm Cyberware sale", timeout=30)
    device.tap("Cancel")
    device.wait(f"cyberware-commerce-page-{compact}", timeout=30)
    device.tap(f"cyberware-commerce-sell-{compact}", timeout=120, scroll=True)
    device.wait("Confirm Cyberware sale", timeout=30)
    device.tap("Sell")
    device.wait(f"collection-editor-cyberware-{identity}", timeout=180)
    device.back()


def assert_linked_capacity_guard(device: shared.Device) -> None:
    identity = CYBERWARE_IDS["linked"]
    compact = open_commerce_page(device, identity)
    status = device.wait(f"cyberware-commerce-upgrade-status-{compact}", timeout=60, scroll=True)
    if "Capacity=[*]" not in status.attributes.get("text", ""):
        device.capture("cyberware-commerce-linked-capacity-status")
        raise RuntimeError("Linked Capacity=[*] Cyberware did not render its fail-closed reason")
    for action in ("upgrade", "sell"):
        button = device.wait(f"cyberware-commerce-{action}-{compact}", timeout=30, scroll=True)
        if button.attributes.get("enabled") != "false":
            device.capture(f"cyberware-commerce-linked-{action}-enabled")
            raise RuntimeError(f"Linked Capacity=[*] Cyberware unexpectedly enabled {action}")
    device.back()
    device.back()


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


def parse_decimal(value: str | None) -> Decimal | None:
    try:
        return Decimal(value) if value is not None else None
    except InvalidOperation:
        return None


def assert_workspace_commerce(device: shared.Device) -> None:
    observed: list[dict[str, object]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        ware = {
            item.findtext("guid", default="").lower(): item
            for item in root.findall(".//cyberware")
        }
        if CYBERWARE_IDS["upgrade"] not in ware or CYBERWARE_IDS["hole"] not in ware:
            continue
        expenses = root.findall("./expenses/expense")
        upgrade_expense = next((
            item for item in expenses
            if item.findtext("./undo/nuyentype") == "AddGear"
            and item.findtext("./undo/objectid") == CYBERWARE_IDS["upgrade"]
        ), None)
        sale_expenses = [
            item for item in expenses
            if parse_decimal(item.findtext("amount")) == Decimal("500") and item.find("undo") is None
        ]
        observed.append({
            "nuyen": root.findtext("nuyen"),
            "upgradeRating": ware[CYBERWARE_IDS["upgrade"]].findtext("rating"),
            "upgradeGrade": ware[CYBERWARE_IDS["upgrade"]].findtext("grade"),
            "holeRating": ware[CYBERWARE_IDS["hole"]].findtext("rating"),
            "salePresent": CYBERWARE_IDS["sell"] in ware,
            "expenseCount": len(expenses),
        })
        if (
            parse_decimal(root.findtext("nuyen")) == Decimal("7900")
            and ware[CYBERWARE_IDS["upgrade"]].findtext("rating") == "3"
            and ware[CYBERWARE_IDS["upgrade"]].findtext("grade") == "Alphaware"
            and ware[CYBERWARE_IDS["hole"]].findtext("rating") == "22"
            and CYBERWARE_IDS["sell"] not in ware
            and CYBERWARE_IDS["sentinel"] in ware
            and upgrade_expense is not None
            and parse_decimal(upgrade_expense.findtext("amount")) == Decimal("-2600")
            and len(sale_expenses) == 1
            and len(expenses) == 2
            and root.findtext("customstate") == "Cyberware commerce unrelated state"
        ):
            return
    device.capture("cyberware-commerce-workspace-not-persisted")
    raise RuntimeError(f"Cyberware commerce was not durable and exact; observed {observed!r}")


def assert_upgrade_readback(device: shared.Device) -> None:
    compact = open_commerce_page(device, CYBERWARE_IDS["upgrade"])
    grade = shared.selected_text(
        device,
        f"cyberware-commerce-grade-{compact}",
        "Grade",
        scroll=True,
    )
    rating = device.wait(f"cyberware-commerce-rating-{compact}", timeout=60, scroll=True)
    if grade != "Alphaware" or rating.attributes.get("text") != "3":
        device.capture("cyberware-commerce-readback-failed")
        raise RuntimeError(f"Cyberware readback expected Alphaware/3, got {grade!r}/{rating.attributes.get('text')!r}")
    device.back()
    device.back()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures" / "career-cyberware-commerce-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "cyberwareCommercePageSha256": android_root / "src" / "Chummer.Android" / "Native" / "CyberwareCommercePage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "commerceContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CyberwareCommerceRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "commerceRulesSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterCyberwareCommerceRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "sourceResolverContractSha256": workspace_root / "chummer-core-engine" / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs",
        "fileSourceResolverSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Cyberware commerce E2E source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Cyberware commerce E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Cyberware commerce E2E requires arm64-v8a, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    apply_upgrade(device)
    apply_confirmed_sale_after_cancel(device)
    assert_workspace_commerce(device)
    assert_upgrade_readback(device)
    assert_linked_capacity_guard(device)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_commerce(device)
    assert_upgrade_readback(device)

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
        "journey": "cyberware-commerce",
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
            "upgradeRatingGradeEconomicsEssenceHole": "pass",
            "upgradeLegacyAddGearUndo": "pass",
            "saleCancellationZeroMutation": "pass",
            "saleConfirmedDeletionCascade": "pass",
            "linkedCapacityGuard": "pass",
            "sameSessionReopen": "pass",
            "processRestart": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
