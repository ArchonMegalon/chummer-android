#!/usr/bin/env python3
"""Prove exact Chummer5 Included in Weapon behavior on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "chkIncludedInWeapon"
CONTROL_PROOF_KEYS = (
    "enabledForSelectedAccessory",
    "disabledForSelectedAccessory",
    "stableParentAndAccessoryIdentity",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "weapon_id": "57111111-5711-5711-5711-571111111111",
        "accessory_id": "57222222-5722-5722-5722-572222222222",
        "untouched_id": "57333333-5733-5733-5733-573333333333",
        "weapon_notes": "Creation weapon notes must remain intact",
        "target_notes": "Creation target accessory notes must remain intact",
        "untouched_notes": "Creation untouched accessory notes",
        "custom_included": "Creation unrelated included text",
    },
    "CharacterCareer": {
        "weapon_id": "58111111-5811-5811-5811-581111111111",
        "accessory_id": "58222222-5822-5822-5822-582222222222",
        "untouched_id": "58333333-5833-5833-5833-583333333333",
        "weapon_notes": "Career weapon notes must remain intact",
        "target_notes": "Career target accessory notes must remain intact",
        "untouched_notes": "Career untouched accessory notes",
        "custom_included": "Career unrelated included text",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_selected_accessory(device: shared.Device, expected: dict[str, str]) -> None:
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
        "build-action-tab-gear-weaponaccessories",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"collection-item-weapon-{expected['accessory_id']}",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.wait(f"collection-editor-weapon-{expected['accessory_id']}", timeout=120)


def open_included_page(device: shared.Device, expected: dict[str, str]) -> None:
    compact_id = expected["accessory_id"].replace("-", "")
    open_selected_accessory(device, expected)
    device.tap(
        f"weapon-accessory-included-open-{compact_id}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    device.wait(f"weapon-accessory-included-page-{compact_id}", timeout=60)


def assert_toggle(device: shared.Device, expected: dict[str, str], value: bool) -> None:
    compact_id = expected["accessory_id"].replace("-", "")
    node = device.wait(f"weapon-accessory-included-toggle-{compact_id}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != value:
        device.capture("weapon-accessory-included-toggle-mismatch")
        raise RuntimeError(f"Included in Weapon switch was {observed!r}; expected {value!r}")


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


def assert_workspace_included(
    device: shared.Device,
    expected: dict[str, str],
    included: bool,
) -> None:
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
        weapon = weapons.get(expected["weapon_id"])
        if weapon is None:
            continue
        accessories = {
            accessory.findtext("guid", default="").lower(): accessory
            for accessory in weapon.findall("./accessories/accessory")
        }
        target = accessories.get(expected["accessory_id"])
        untouched = accessories.get(expected["untouched_id"])
        if target is None or untouched is None:
            continue
        target_flag = target.findtext("included", default="")
        untouched_flag = untouched.findtext("included", default="")
        observed.append({"target": target_flag, "untouched": untouched_flag})
        if (
            target_flag == ("True" if included else "False")
            and untouched_flag == "True"
            and weapon.findtext("notes", default="") == expected["weapon_notes"]
            and target.findtext("notes", default="") == expected["target_notes"]
            and untouched.findtext("notes", default="") == expected["untouched_notes"]
            and character.findtext("./customstate/included", default="") == expected["custom_included"]
        ):
            return
    device.capture("weapon-accessory-included-workspace-not-persisted")
    raise RuntimeError(f"Included in Weapon invariant was not durable; observed {observed!r}")


def set_included(device: shared.Device, expected: dict[str, str], enabled: bool) -> None:
    compact_id = expected["accessory_id"].replace("-", "")
    open_included_page(device, expected)
    assert_toggle(device, expected, not enabled)
    device.tap(f"weapon-accessory-included-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected, enabled)
    device.tap(f"weapon-accessory-included-save-{compact_id}", timeout=240, scroll=True)
    device.wait(f"weapon-accessory-included-open-{compact_id}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    set_included(device, expected, True)
    assert_workspace_included(device, expected, True)
    open_included_page(device, expected)
    assert_toggle(device, expected, True)
    device.capture(f"weapon-accessory-included-{profile.lower()}-enabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_included(device, expected, True)
    open_included_page(device, expected)
    assert_toggle(device, expected, True)
    device.capture(f"weapon-accessory-included-{profile.lower()}-enabled-after-process-restart")

    compact_id = expected["accessory_id"].replace("-", "")
    device.tap(f"weapon-accessory-included-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected, False)
    device.tap(f"weapon-accessory-included-save-{compact_id}", timeout=240, scroll=True)
    assert_workspace_included(device, expected, False)
    open_included_page(device, expected)
    assert_toggle(device, expected, False)
    device.capture(f"weapon-accessory-included-{profile.lower()}-disabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_included(device, expected, False)
    open_included_page(device, expected)
    assert_toggle(device, expected, False)
    device.capture(f"weapon-accessory-included-{profile.lower()}-disabled-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-weapon-accessory-included-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-weapon-accessory-included-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "weaponAccessoryIncludedPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "WeaponAccessoryIncludedPage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "weaponAccessoryIncludedContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WeaponAccessoryIncludedEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Included in Weapon E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Included in Weapon E2E requires API 36, got {api!r}")

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
        "journey": "weapon-accessory-included",
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
            "creationAccessoryEnabled": "pass",
            "creationAccessoryEnabledReopened": "pass",
            "creationAccessoryEnabledProcessRestart": "pass",
            "creationAccessoryDisabled": "pass",
            "creationAccessoryDisabledReopened": "pass",
            "creationAccessoryDisabledProcessRestart": "pass",
            "careerRunnerImported": "pass",
            "careerAccessoryEnabled": "pass",
            "careerAccessoryEnabledReopened": "pass",
            "careerAccessoryEnabledProcessRestart": "pass",
            "careerAccessoryDisabled": "pass",
            "careerAccessoryDisabledReopened": "pass",
            "careerAccessoryDisabledProcessRestart": "pass",
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
        print(f"Included in Weapon E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
