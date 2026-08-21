#!/usr/bin/env python3
"""Prove exact Chummer5 Career armor repair/degrade controls on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("cmdArmorIncrease", "cmdArmorDecrease")
CONTROL_PROOF_KEYS = (
    "stableArmorIdentity",
    "exactLegacyDirection",
    "exactBoundaryEnablement",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
ARMOR_ID = "59111111-5911-5911-5911-591111111111"
UNTOUCHED_ARMOR_ID = "59222222-5922-5922-5922-592222222222"


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_damage_page(device: shared.Device) -> None:
    compact_id = ARMOR_ID.replace("-", "")
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True, timeout=120, max_scrolls=24)
    device.tap("build-action-tab-gear-armors", scroll=True, timeout=120, max_scrolls=24)
    device.tap(
        f"collection-item-armor-{ARMOR_ID}",
        scroll=True,
        timeout=120,
        max_scrolls=24,
    )
    device.wait(f"collection-editor-armor-{ARMOR_ID}", timeout=120)
    device.tap(
        f"armor-damage-open-{compact_id}",
        scroll=True,
        timeout=120,
        max_scrolls=36,
    )
    device.wait(f"armor-damage-page-{compact_id}", timeout=60)


def assert_button_state(device: shared.Device, automation_id: str, expected: bool) -> None:
    node = device.wait(automation_id, timeout=60)
    observed = node.attributes.get("enabled") == "true"
    if observed != expected:
        device.capture("armor-damage-button-state-mismatch")
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


def assert_workspace_damage(device: shared.Device, expected_damage: int) -> None:
    observed: list[str] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        armors = {
            armor.findtext("guid", default="").lower(): armor
            for armor in character.findall("./armors/armor")
        }
        target = armors.get(ARMOR_ID)
        untouched = armors.get(UNTOUCHED_ARMOR_ID)
        if target is None or untouched is None:
            continue
        observed.append(target.findtext("damage", default=""))
        if (
            target.findtext("damage", default="") == str(expected_damage)
            and untouched.findtext("damage", default="") == "3"
            and target.findtext("notes", default="") == "Career target armor notes must remain intact"
            and untouched.findtext("notes", default="") == "Career untouched armor notes"
            and character.findtext("./customstate/damage", default="") == "Career unrelated damage text"
        ):
            return
    device.capture("armor-damage-workspace-not-persisted")
    raise RuntimeError(f"Armor damage was not durably {expected_damage}; observed {observed!r}")


def prove_career(device: shared.Device, fixture: Path) -> None:
    compact_id = ARMOR_ID.replace("-", "")
    repair_id = f"armor-damage-repair-{compact_id}"
    degrade_id = f"armor-damage-degrade-{compact_id}"
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    open_damage_page(device)
    assert_button_state(device, repair_id, False)
    assert_button_state(device, degrade_id, True)
    device.tap(degrade_id, timeout=120)
    assert_workspace_damage(device, 1)
    open_damage_page(device)
    assert_button_state(device, repair_id, True)
    assert_button_state(device, degrade_id, False)
    device.capture("armor-damage-career-degraded-bound-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_damage(device, 1)
    open_damage_page(device)
    assert_button_state(device, repair_id, True)
    assert_button_state(device, degrade_id, False)
    device.capture("armor-damage-career-degraded-bound-after-process-restart")

    device.tap(repair_id, timeout=120)
    assert_workspace_damage(device, 0)
    open_damage_page(device)
    assert_button_state(device, repair_id, False)
    assert_button_state(device, degrade_id, True)
    device.capture("armor-damage-career-repaired-bound-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_damage(device, 0)
    open_damage_page(device)
    assert_button_state(device, repair_id, False)
    assert_button_state(device, degrade_id, True)
    device.capture("armor-damage-career-repaired-bound-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = Path(__file__).resolve().parent / "fixtures" / "career-armor-damage-e2e.chum5"
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "armorDamagePageSha256": android_root / "src" / "Chummer.Android" / "Native" / "ArmorDamagePage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "armorDamageContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ArmorDamageAdjustmentRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "armorDamageRulesSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterArmorDamageRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Armor damage E2E source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Armor damage E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    prove_career(device, fixture)

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
        "journey": "armor-damage",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": shared.sha256(fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "careerRunnerImported": "pass",
            "careerDegradeEnabledAtZero": "pass",
            "careerRepairDisabledAtZero": "pass",
            "careerDegradedToMaximum": "pass",
            "careerDegradeDisabledAtMaximum": "pass",
            "careerDegradedReopened": "pass",
            "careerDegradedProcessRestart": "pass",
            "careerRepairedToZero": "pass",
            "careerRepairDisabledAfterRepair": "pass",
            "careerRepairedReopened": "pass",
            "careerRepairedProcessRestart": "pass",
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
        print(f"Armor damage E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
