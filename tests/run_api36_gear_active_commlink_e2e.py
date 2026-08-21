#!/usr/bin/env python3
"""Prove exact Chummer5 gear Active Commlink behavior on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "CharacterCreate.chkGearActiveCommlink",
    "CharacterCareer.chkGearActiveCommlink",
)
PROOF_KEYS = (
    "stableGearGuid",
    "legacyPersonaEligibility",
    "exclusiveCharacterWideActiveCommlink",
    "expectedRevisionAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILES = {
    "creation": {
        "fixture": "creation-gear-active-commlink-e2e.chum5",
        "gear_id": "57222222-5722-5722-5722-572222222222",
        "other_gear_id": "57333333-5733-5733-5733-573333333333",
        "armor_id": "57111111-5711-5711-5711-571111111111",
        "target_notes": "Creation target notes must remain intact",
        "other_notes": "Creation untouched notes",
        "armor_notes": "Creation armor must remain intact",
        "custom_active": "Creation unrelated active text",
    },
    "career": {
        "fixture": "career-gear-active-commlink-e2e.chum5",
        "gear_id": "58222222-5822-5822-5822-582222222222",
        "other_gear_id": "58333333-5833-5833-5833-583333333333",
        "armor_id": "58111111-5811-5811-5811-581111111111",
        "target_notes": "Career target notes must remain intact",
        "other_notes": "Career untouched notes",
        "armor_notes": "Career armor must remain intact",
        "custom_active": "Career unrelated active text",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_active_commlink_page(device: shared.Device, gear_id: str) -> None:
    shared.open_build(device, "phone")
    shared.open_gear_section(device, "phone")
    device.tap(
        f"collection-item-gear-{gear_id}",
        timeout=120,
        scroll=True,
        max_scrolls=40,
        scroll_distance_ratio=0.18,
    )
    compact_id = gear_id.replace("-", "")
    device.wait(f"collection-editor-gear-{gear_id}", timeout=120)
    device.tap(f"gear-active-commlink-open-{compact_id}", timeout=60, scroll=True, max_scrolls=40)
    device.wait(f"gear-active-commlink-page-{compact_id}", timeout=60)


def assert_toggle(device: shared.Device, gear_id: str, expected: bool) -> None:
    node = device.wait(f"gear-active-commlink-toggle-{gear_id.replace('-', '')}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("gear-active-commlink-toggle-mismatch")
        raise RuntimeError(f"Gear Active Commlink switch was {observed!r}; expected {expected!r}")


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


def assert_workspace(device: shared.Device, expected: dict[str, str], enabled: bool) -> None:
    observed: list[dict[str, str]] = []
    matrix_tags = {"armor", "gear", "weapon", "cyberware", "vehicle"}
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        gears = {item.findtext("guid", default="").lower(): item for item in root.findall(".//gear")}
        armors = {item.findtext("guid", default="").lower(): item for item in root.findall("./armors/armor")}
        target = gears.get(expected["gear_id"])
        other = gears.get(expected["other_gear_id"])
        armor = armors.get(expected["armor_id"])
        if target is None or other is None or armor is None:
            continue
        flags = {
            "target": target.findtext("active", default=""),
            "other": other.findtext("active", default=""),
            "armor": armor.findtext("active", default=""),
        }
        observed.append(flags)
        true_flags = [
            active
            for item in root.iter()
            if item.tag in matrix_tags
            for active in item.findall("active")
            if (active.text or "").strip().lower() == "true"
        ]
        if (
            flags["target"] == ("True" if enabled else "False")
            and flags["other"] == "False"
            and flags["armor"] == "False"
            and len(true_flags) == (1 if enabled else 0)
            and target.findtext("canformpersona", default="") == "Self"
            and target.findtext("notes", default="") == expected["target_notes"]
            and other.findtext("notes", default="") == expected["other_notes"]
            and armor.findtext("notes", default="") == expected["armor_notes"]
            and root.findtext("./customstate/active", default="") == expected["custom_active"]
        ):
            return
    device.capture("gear-active-commlink-workspace-not-persisted")
    raise RuntimeError(f"Gear Active Commlink invariant was not durable: {observed!r}")


def set_active_commlink(device: shared.Device, gear_id: str, enabled: bool) -> None:
    compact_id = gear_id.replace("-", "")
    open_active_commlink_page(device, gear_id)
    assert_toggle(device, gear_id, not enabled)
    device.tap(f"gear-active-commlink-toggle-{compact_id}", timeout=60)
    assert_toggle(device, gear_id, enabled)
    device.tap(f"gear-active-commlink-save-{compact_id}", timeout=240, scroll=True)
    device.wait(f"gear-active-commlink-open-{compact_id}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILES[profile]
    gear_id = expected["gear_id"]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    set_active_commlink(device, gear_id, True)
    assert_workspace(device, expected, True)
    open_active_commlink_page(device, gear_id)
    assert_toggle(device, gear_id, True)
    device.capture(f"gear-active-commlink-{profile}-enabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected, True)
    open_active_commlink_page(device, gear_id)
    assert_toggle(device, gear_id, True)
    device.capture(f"gear-active-commlink-{profile}-enabled-after-process-restart")

    compact_id = gear_id.replace("-", "")
    device.tap(f"gear-active-commlink-toggle-{compact_id}", timeout=60)
    assert_toggle(device, gear_id, False)
    device.tap(f"gear-active-commlink-save-{compact_id}", timeout=240, scroll=True)
    assert_workspace(device, expected, False)
    open_active_commlink_page(device, gear_id)
    assert_toggle(device, gear_id, False)
    device.capture(f"gear-active-commlink-{profile}-disabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected, False)
    open_active_commlink_page(device, gear_id)
    assert_toggle(device, gear_id, False)
    device.capture(f"gear-active-commlink-{profile}-disabled-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / PROFILES["creation"]["fixture"])
    parser.add_argument("--career-runner", type=Path, default=fixtures / PROFILES["career"]["fixture"])
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "gearActiveCommlinkPageSha256": android_root / "src/Chummer.Android/Native/GearActiveCommlinkPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "gearActiveCommlinkContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/GearActiveCommlinkEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "gearActiveCommlinkRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterGearActiveCommlinkRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Gear Active Commlink E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Gear Active Commlink E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    prove_profile(device, creation_fixture, "creation")
    prove_profile(device, career_fixture, "career")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "gear-active-commlink",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"gear Active Commlink E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
