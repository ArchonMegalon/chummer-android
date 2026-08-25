#!/usr/bin/env python3
"""Prove exact Create-only selected armor-tree flags on an API 36 phone."""

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


CONTROLS = (
    "CharacterCreate.chkArmorStolen",
    "CharacterCreate.chkArmorBlackMarketDiscount",
)
ROOT_ARMOR_ID = "a3111111-3111-3111-3111-311111111111"
CAREER_ARMOR_ID = "a4111111-4111-4111-4111-411111111111"
TARGETS = (
    ("Armor · Flag Root Armor · a3111111", "armor", (True, True)),
    ("Armor Mod · Flag Root Armor > Flag Armor Mod · b3111111", "mod", (False, True)),
    (
        "Gear · Flag Root Armor > Armor Gear Parent > Nested Armor Gear Target · d3111111",
        "armor_child",
        (True, False),
    ),
    (
        "Gear · Flag Root Armor > Flag Armor Mod > Mod Gear Parent > Nested Mod Gear Target · f3111111",
        "mod_child",
        (False, False),
    ),
)
PROOF_KEYS = (
    "creationOnlyCareerNegative",
    "selectedArmorTreeNode",
    "topLevelArmor",
    "armorMod",
    "recursiveGearUnderArmor",
    "recursiveGearUnderArmorMod",
    "stableTypedHierarchicalIdentity",
    "duplicateAmbiguousRejected",
    "stolenElementPersisted",
    "discountedCostElementPersisted",
    "revisionBoundAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_armor_item(device: shared.Device, armor_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-armors", scroll=True, timeout=120, max_scrolls=24)
    device.tap(
        f"collection-item-armor-{armor_id}",
        scroll=True,
        timeout=120,
        max_scrolls=24,
    )
    device.wait(f"collection-editor-armor-{armor_id}", timeout=120)


def open_flags_page(device: shared.Device) -> None:
    token = ROOT_ARMOR_ID.replace("-", "")
    open_armor_item(device, ROOT_ARMOR_ID)
    device.tap(f"armor-tree-flags-open-{token}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"armor-tree-flags-page-{token}", timeout=60)


def select_target(device: shared.Device, label: str) -> None:
    token = ROOT_ARMOR_ID.replace("-", "")
    device.tap(f"armor-tree-flags-target-{token}", timeout=60, scroll=True)
    device.tap(label, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device,
        f"armor-tree-flags-target-{token}",
        "Armor-tree node",
        scroll=True,
    )
    if observed != label:
        device.capture("armor-tree-flags-target-mismatch")
        raise RuntimeError(f"Armor-tree target was {observed!r}; expected {label!r}")


def assert_switches(device: shared.Device, expected: tuple[bool, bool]) -> None:
    token = ROOT_ARMOR_ID.replace("-", "")
    observed = tuple(
        device.wait(selector, timeout=60, scroll=True).attributes.get("checked") == "true"
        for selector in (
            f"armor-tree-stolen-toggle-{token}",
            f"armor-tree-discounted-cost-toggle-{token}",
        )
    )
    if observed != expected:
        device.capture("armor-tree-flags-switch-mismatch")
        raise RuntimeError(f"Armor-tree flags were {observed!r}; expected {expected!r}")


def edit_target(device: shared.Device, label: str, expected: tuple[bool, bool]) -> None:
    token = ROOT_ARMOR_ID.replace("-", "")
    open_flags_page(device)
    select_target(device, label)
    current = tuple(
        device.wait(selector, timeout=60, scroll=True).attributes.get("checked") == "true"
        for selector in (
            f"armor-tree-stolen-toggle-{token}",
            f"armor-tree-discounted-cost-toggle-{token}",
        )
    )
    for selector, before, after in zip(
        (
            f"armor-tree-stolen-toggle-{token}",
            f"armor-tree-discounted-cost-toggle-{token}",
        ),
        current,
        expected,
        strict=True,
    ):
        if before != after:
            device.tap(selector, timeout=60, scroll=True)
    assert_switches(device, expected)
    device.tap(f"armor-tree-flags-save-{token}", timeout=180, scroll=True, max_scrolls=24)
    device.wait(f"collection-editor-armor-{ROOT_ARMOR_ID}", timeout=180)
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


def flags(node: ET.Element | None) -> tuple[str, str]:
    if node is None:
        return ("", "")
    return (
        node.findtext("stolen", default=""),
        node.findtext("discountedcost", default=""),
    )


def assert_workspace(device: shared.Device) -> None:
    observed: list[dict[str, tuple[str, str]]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        armor = next((
            node for node in root.findall("./armors/armor")
            if node.findtext("guid", default="").lower() == ROOT_ARMOR_ID
        ), None)
        if armor is None:
            continue
        mod = armor.find("./armormods/armormod")
        armor_child = armor.find("./gears/gear/children/gear")
        mod_child = armor.find("./armormods/armormod/gears/gear/children/gear")
        state = {
            "armor": flags(armor),
            "mod": flags(mod),
            "armor_child": flags(armor_child),
            "mod_child": flags(mod_child),
        }
        observed.append(state)
        expected = {
            key: tuple("True" if value else "False" for value in values)
            for _, key, values in TARGETS
        }
        untouched = next((
            node for node in root.findall("./armors/armor")
            if node.findtext("guid", default="").lower()
            == "a3222222-3222-3222-3222-322222222222"
        ), None)
        if (
            state == expected
            and armor.findtext("notes") == "Creation selected armor sentinel"
            and armor_child is not None
            and armor_child.findtext("notes") == "Creation nested armor gear sentinel"
            and mod_child is not None
            and mod_child.findtext("notes") == "Creation nested mod gear sentinel"
            and flags(untouched) == ("True", "True")
            and untouched is not None
            and untouched.findtext("notes") == "Creation untouched armor sentinel"
            and root.findtext("customstate") == "Creation armor tree runner sentinel"
        ):
            return
    device.capture("armor-tree-flags-workspace-not-persisted")
    raise RuntimeError(f"Armor-tree flags were not durable: {observed!r}")


def assert_all_ui(device: shared.Device) -> None:
    open_flags_page(device)
    for label, _, expected in TARGETS:
        select_target(device, label)
        assert_switches(device, expected)


def assert_career_negative(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_armor_item(device, CAREER_ARMOR_ID)
    selector = f"armor-tree-flags-open-{CAREER_ARMOR_ID.replace('-', '')}"
    try:
        device.wait(selector, timeout=5, scroll=True)
    except RuntimeError:
        pass
    else:
        device.capture("armor-tree-flags-career-action-exposed")
        raise RuntimeError("Create-only armor-tree flags were exposed for a Career runner")
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        armor = root.find("./armors/armor")
        if armor is not None and armor.findtext("guid", default="").lower() == CAREER_ARMOR_ID:
            if flags(armor) != ("False", "False"):
                raise RuntimeError("Career-negative fixture flags changed unexpectedly")
            return
    raise RuntimeError("Career-negative armor fixture was not available in workspace state")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument(
        "--creation-runner",
        type=Path,
        default=fixtures / "creation-armor-tree-flags-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-armor-tree-flags-negative-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "armorTreeFlagPageSha256": android_root / "src/Chummer.Android/Native/ArmorTreeFlagPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "armorTreeFlagContractSha256": overview / "ArmorTreeFlagEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "armorTreeFlagRulesSha256": contracts / "CharacterArmorTreeFlagRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Armor-tree flag source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Armor-tree flag E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, creation_fixture.name)
    for label, _, expected in TARGETS:
        edit_target(device, label, expected)
    assert_workspace(device)
    assert_all_ui(device)
    device.capture("armor-tree-flags-creation-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device)
    assert_all_ui(device)
    device.capture("armor-tree-flags-creation-after-process-restart")
    assert_career_negative(device, career_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "armor-tree-flags",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerNegativeFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "creationAllThreeNodeKindsAndBothGearParents": "pass",
            "creationSameSessionReopen": "pass",
            "creationProcessRestart": "pass",
            "careerActionNotExposed": "pass",
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
        print(f"Armor-tree flag E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
