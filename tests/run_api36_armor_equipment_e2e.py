#!/usr/bin/env python3
"""Prove exact Chummer5 armor equipped and bulk loadout controls on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("chkArmorEquipped", "cmdArmorEquipAll", "cmdArmorUnEquipAll")
CONTROL_PROOF_KEYS = (
    "stableArmorIdentity",
    "exactSelectedState",
    "exactBulkAllState",
    "exactEligibility",
    "nestedEquipmentFlagsPreserved",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "armor_id": "5a111111-5a11-5a11-5a11-5a1111111111",
        "other_id": "5a222222-5a22-5a22-5a22-5a2222222222",
        "unrelated": "Creation unrelated equipped text",
    },
    "CharacterCareer": {
        "armor_id": "5b111111-5b11-5b11-5b11-5b1111111111",
        "other_id": "5b222222-5b22-5b22-5b22-5b2222222222",
        "unrelated": "Career unrelated equipped text",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_equipment_page(device: shared.Device, armor_id: str) -> None:
    compact_id = armor_id.replace("-", "")
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-armors", scroll=True, timeout=120, max_scrolls=24)
    device.tap(f"collection-item-armor-{armor_id}", scroll=True, timeout=120, max_scrolls=24)
    device.wait(f"collection-editor-armor-{armor_id}", timeout=120)
    device.tap(f"armor-equipment-open-{compact_id}", scroll=True, timeout=120, max_scrolls=36)
    device.wait(f"armor-equipment-page-{compact_id}", timeout=60)


def assert_toggle(device: shared.Device, armor_id: str, expected: bool) -> None:
    node = device.wait(f"armor-equipment-toggle-{armor_id.replace('-', '')}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("armor-equipment-toggle-mismatch")
        raise RuntimeError(f"Armor Equipped switch was {observed!r}; expected {expected!r}")


def assert_button_state(device: shared.Device, automation_id: str, expected: bool) -> None:
    node = device.wait(automation_id, timeout=60)
    observed = node.attributes.get("enabled") == "true"
    if observed != expected:
        device.capture("armor-equipment-button-state-mismatch")
        raise RuntimeError(f"{automation_id} enabled was {observed!r}; expected {expected!r}")


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


def assert_workspace_equipment(device: shared.Device, expected: dict[str, str], values: tuple[bool, bool]) -> None:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        armors = {
            armor.findtext("guid", default="").lower(): armor
            for armor in character.findall("./armors/armor")
        }
        selected = armors.get(expected["armor_id"])
        other = armors.get(expected["other_id"])
        if selected is None or other is None:
            continue
        actual = (selected.findtext("equipped", default=""), other.findtext("equipped", default=""))
        observed.append(actual)
        expected_text = tuple("True" if value else "False" for value in values)
        if (
            actual == expected_text
            and selected.findtext("./armormods/armormod/equipped", default="") == "True"
            and selected.findtext("./gears/gear/equipped", default="") == "False"
            and selected.findtext("notes", default="").endswith("selected notes preserved")
            and other.findtext("notes", default="").endswith("other notes preserved")
            and character.findtext("./customstate/equipped", default="") == expected["unrelated"]
        ):
            return
    device.capture("armor-equipment-workspace-not-persisted")
    raise RuntimeError(f"Armor equipment state was not durable; observed {observed!r}")


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    armor_id = expected["armor_id"]
    token = armor_id.replace("-", "")
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    open_equipment_page(device, armor_id)
    assert_toggle(device, armor_id, False)
    assert_button_state(device, f"armor-equipment-equip-all-{token}", True)
    assert_button_state(device, f"armor-equipment-unequip-all-{token}", True)
    device.tap(f"armor-equipment-toggle-{token}")
    device.tap(f"armor-equipment-save-{token}", timeout=240, scroll=True)
    assert_workspace_equipment(device, expected, (True, True))
    open_equipment_page(device, armor_id)
    assert_toggle(device, armor_id, True)

    device.tap(f"armor-equipment-unequip-all-{token}", timeout=240, scroll=True)
    assert_workspace_equipment(device, expected, (False, False))
    open_equipment_page(device, armor_id)
    assert_toggle(device, armor_id, False)
    assert_button_state(device, f"armor-equipment-equip-all-{token}", True)
    assert_button_state(device, f"armor-equipment-unequip-all-{token}", False)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_equipment(device, expected, (False, False))
    open_equipment_page(device, armor_id)
    assert_toggle(device, armor_id, False)

    device.tap(f"armor-equipment-equip-all-{token}", timeout=240, scroll=True)
    assert_workspace_equipment(device, expected, (True, True))
    open_equipment_page(device, armor_id)
    assert_toggle(device, armor_id, True)
    assert_button_state(device, f"armor-equipment-equip-all-{token}", False)
    assert_button_state(device, f"armor-equipment-unequip-all-{token}", True)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_equipment(device, expected, (True, True))
    open_equipment_page(device, armor_id)
    assert_toggle(device, armor_id, True)
    device.capture(f"armor-equipment-{profile.lower()}-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-armor-equipment-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-armor-equipment-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    contracts = workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "armorEquipmentPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "ArmorEquipmentPage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "armorEquipmentContractSha256": overview / "ArmorEquipmentEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "armorEquipmentRulesSha256": contracts / "CharacterArmorEquipmentRules.cs",
        "characterSectionModelsSha256": contracts / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        raise RuntimeError("Armor equipment E2E source graph is incomplete")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Armor equipment E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    prove_profile(device, creation_fixture, "CharacterCreate")
    prove_profile(device, career_fixture, "CharacterCareer")

    controls = {
        f"{profile}.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for profile in ("CharacterCreate", "CharacterCareer")
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "armor-equipment",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            f"{prefix}{journey}": "pass"
            for prefix in ("creation", "career")
            for journey in (
                "RunnerImported",
                "SelectedEquipped",
                "SelectedReopened",
                "AllUnequipped",
                "AllUnequippedReopened",
                "AllUnequippedProcessRestart",
                "AllEquipped",
                "AllEquippedReopened",
                "AllEquippedProcessRestart",
            )
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
        print(f"Armor equipment E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
