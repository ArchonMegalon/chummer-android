#!/usr/bin/env python3
"""Prove exact Chummer5 weapon Home Node behavior on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "chkWeaponHomeNode"
CONTROL_PROOF_KEYS = (
    "aiEligibilityReadback",
    "enabledAsExclusiveHomeNode",
    "disabledFromHomeNode",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "weapon_id": "63222222-6322-6322-6322-632222222222",
        "other_weapon_id": "63333333-6333-6333-6333-633333333333",
        "owner_id": "63111111-6311-6311-6311-631111111111",
        "target_notes": "Creation target notes must remain intact",
        "other_notes": "Creation untouched notes",
        "owner_notes": "Creation owner notes must remain intact",
    },
    "CharacterCareer": {
        "weapon_id": "64222222-6422-6422-6422-642222222222",
        "other_weapon_id": "64333333-6433-6433-6433-643333333333",
        "owner_id": "64111111-6411-6411-6411-641111111111",
        "target_notes": "Career target notes must remain intact",
        "other_notes": "Career untouched notes",
        "owner_notes": "Career owner notes must remain intact",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_selected_weapon(device: shared.Device, weapon_id: str) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-section-tab-gear",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        "build-action-tab-gear-weapons",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"collection-item-weapon-{weapon_id}",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.wait(f"collection-editor-weapon-{weapon_id}", timeout=120)
    device.wait(
        f"weapon-home-node-open-{weapon_id.replace('-', '')}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )


def open_home_node_page(device: shared.Device, weapon_id: str) -> None:
    compact_id = weapon_id.replace("-", "")
    open_selected_weapon(device, weapon_id)
    device.tap(f"weapon-home-node-open-{compact_id}", timeout=60, scroll=True)
    device.wait(f"weapon-home-node-page-{compact_id}", timeout=60)
    device.wait("Matrix owner", timeout=60, scroll=True)
    device.wait("Device Rating", timeout=60, scroll=True)
    device.wait("Program Limit", timeout=60, scroll=True)
    device.wait("DEP", timeout=60, scroll=True)


def assert_toggle(device: shared.Device, weapon_id: str, expected: bool) -> None:
    node = device.wait(f"weapon-home-node-toggle-{weapon_id.replace('-', '')}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("weapon-home-node-toggle-mismatch")
        raise RuntimeError(f"Weapon Home Node switch was {observed!r}; expected {expected!r}")
    if node.attributes.get("enabled") != "true":
        device.capture("weapon-home-node-toggle-disabled")
        raise RuntimeError("Eligible AI weapon Home Node switch was unexpectedly disabled")


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


def assert_workspace_home_node(device: shared.Device, expected: dict[str, str], enabled: bool) -> None:
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        weapons = {
            weapon.findtext("guid", default="").lower(): weapon
            for weapon in character.findall("./weapons/weapon")
        }
        gears = {
            gear.findtext("guid", default="").lower(): gear
            for gear in character.findall("./gears/gear")
        }
        target = weapons.get(expected["weapon_id"])
        other = weapons.get(expected["other_weapon_id"])
        owner = gears.get(expected["owner_id"])
        if target is None or other is None or owner is None:
            continue
        target_flag = target.findtext("homenode", default="")
        other_flag = other.findtext("homenode", default="")
        owner_flag = owner.findtext("homenode", default="")
        observed.append({"target": target_flag, "other": other_flag, "owner": owner_flag})
        if (
            target_flag == ("True" if enabled else "False")
            and other_flag == "False"
            and owner_flag == "False"
            and target.findtext("notes", default="") == expected["target_notes"]
            and other.findtext("notes", default="") == expected["other_notes"]
            and owner.findtext("notes", default="") == expected["owner_notes"]
            and target.findtext("parentid", default="").lower() == expected["owner_id"]
        ):
            true_flags = [
                node for node in character.iter("homenode")
                if (node.text or "").strip().lower() == "true"
            ]
            if len(true_flags) == (1 if enabled else 0):
                return
    device.capture("weapon-home-node-workspace-not-persisted")
    raise RuntimeError(f"Weapon Home Node invariant was not durable; observed {observed!r}")


def set_home_node(device: shared.Device, weapon_id: str, enabled: bool) -> None:
    compact_id = weapon_id.replace("-", "")
    open_home_node_page(device, weapon_id)
    assert_toggle(device, weapon_id, not enabled)
    device.tap(f"weapon-home-node-toggle-{compact_id}", timeout=60)
    assert_toggle(device, weapon_id, enabled)
    device.tap(f"weapon-home-node-save-{compact_id}", timeout=240, scroll=True)
    device.wait(f"weapon-home-node-open-{compact_id}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    set_home_node(device, expected["weapon_id"], True)
    assert_workspace_home_node(device, expected, True)
    open_home_node_page(device, expected["weapon_id"])
    assert_toggle(device, expected["weapon_id"], True)
    device.capture(f"weapon-home-node-{profile.lower()}-enabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_home_node(device, expected, True)
    open_home_node_page(device, expected["weapon_id"])
    assert_toggle(device, expected["weapon_id"], True)
    device.capture(f"weapon-home-node-{profile.lower()}-enabled-after-process-restart")

    compact_id = expected["weapon_id"].replace("-", "")
    device.tap(f"weapon-home-node-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected["weapon_id"], False)
    device.tap(f"weapon-home-node-save-{compact_id}", timeout=240, scroll=True)
    assert_workspace_home_node(device, expected, False)
    open_home_node_page(device, expected["weapon_id"])
    assert_toggle(device, expected["weapon_id"], False)
    device.capture(f"weapon-home-node-{profile.lower()}-disabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_home_node(device, expected, False)
    open_home_node_page(device, expected["weapon_id"])
    assert_toggle(device, expected["weapon_id"], False)
    device.capture(f"weapon-home-node-{profile.lower()}-disabled-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-weapon-home-node-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-weapon-home-node-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "weaponHomeNodePageSha256": android_root / "src" / "Chummer.Android" / "Native" / "WeaponHomeNodePage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "weaponHomeNodeContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WeaponHomeNodeEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "weaponHomeNodeRulesSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
        "weaponParentResolverSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterWeaponMatrixParentResolver.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Weapon Home Node E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Weapon Home Node E2E requires API 36, got {api!r}")

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
        f"{profile}.{CONTROL}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for profile in ("CharacterCreate", "CharacterCareer")
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "weapon-home-node",
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
            "creationRunnerImported": "pass",
            "creationAiEligibilityReadback": "pass",
            "creationWeaponEnabledExclusive": "pass",
            "creationWeaponEnabledReopened": "pass",
            "creationWeaponEnabledProcessRestart": "pass",
            "creationWeaponDisabled": "pass",
            "creationWeaponDisabledReopened": "pass",
            "creationWeaponDisabledProcessRestart": "pass",
            "careerRunnerImported": "pass",
            "careerAiEligibilityReadback": "pass",
            "careerWeaponEnabledExclusive": "pass",
            "careerWeaponEnabledReopened": "pass",
            "careerWeaponEnabledProcessRestart": "pass",
            "careerWeaponDisabled": "pass",
            "careerWeaponDisabledReopened": "pass",
            "careerWeaponDisabledProcessRestart": "pass",
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
        print(f"weapon Home Node E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
