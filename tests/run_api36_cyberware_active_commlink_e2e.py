#!/usr/bin/env python3
"""Prove exact Chummer5 cyberware Active Commlink behavior on an API 36 phone."""

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
    "CharacterCreate.chkCyberwareActiveCommlink",
    "CharacterCareer.chkCyberwareActiveCommlink",
)
PROOF_KEYS = (
    "stableCyberwareGuid",
    "legacyPersonaEligibility",
    "exclusiveCharacterWideActiveCommlink",
    "expectedRevisionAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILES = {
    "creation": {
        "fixture": "creation-cyberware-active-commlink-e2e.chum5",
        "cyberware_id": "91222222-9122-9122-9122-912222222222",
        "other_cyberware_id": "91333333-9133-9133-9133-913333333333",
        "gear_id": "91111111-9111-9111-9111-911111111111",
        "target_notes": "Creation target notes must remain intact",
        "other_notes": "Creation untouched notes",
        "gear_notes": "Creation gear must remain intact",
        "custom_active": "Creation unrelated active text",
    },
    "career": {
        "fixture": "career-cyberware-active-commlink-e2e.chum5",
        "cyberware_id": "92222222-9222-9222-9222-922222222222",
        "other_cyberware_id": "92333333-9233-9233-9233-923333333333",
        "gear_id": "92111111-9211-9211-9211-921111111111",
        "target_notes": "Career target notes must remain intact",
        "other_notes": "Career untouched notes",
        "gear_notes": "Career gear must remain intact",
        "custom_active": "Career unrelated active text",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_cyberware_editor(device: shared.Device, cyberware_id: str) -> None:
    item = f"collection-item-cyberware-{cyberware_id}"
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-section-tab-gear", scroll=True)
    time.sleep(2)
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-action-tab-gear-cyberwares",
        scroll=True,
        timeout=180,
        max_scrolls=48,
        scroll_distance_ratio=0.22,
    )
    device.wait(item, timeout=120, scroll=True, max_scrolls=36, scroll_distance_ratio=0.18)
    device.tap(item, timeout=120, scroll=True, max_scrolls=36, scroll_distance_ratio=0.18)
    device.wait(f"collection-editor-cyberware-{cyberware_id}", timeout=120)


def open_active_commlink_page(device: shared.Device, cyberware_id: str) -> None:
    open_cyberware_editor(device, cyberware_id)
    compact_id = cyberware_id.replace("-", "")
    device.tap(
        f"cyberware-active-commlink-open-{compact_id}",
        timeout=60,
        scroll=True,
        max_scrolls=36,
    )
    device.wait(f"cyberware-active-commlink-page-{compact_id}", timeout=60)


def assert_toggle(device: shared.Device, cyberware_id: str, expected: bool) -> None:
    compact_id = cyberware_id.replace("-", "")
    node = device.wait(f"cyberware-active-commlink-toggle-{compact_id}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("cyberware-active-commlink-toggle-mismatch")
        raise RuntimeError(
            f"Cyberware Active Commlink switch was {observed!r}; expected {expected!r}"
        )


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
        cyberwares = {
            item.findtext("guid", default="").lower(): item
            for item in root.findall(".//cyberware")
        }
        gears = {
            item.findtext("guid", default="").lower(): item
            for item in root.findall(".//gear")
        }
        target = cyberwares.get(expected["cyberware_id"])
        other = cyberwares.get(expected["other_cyberware_id"])
        gear = gears.get(expected["gear_id"])
        if target is None or other is None or gear is None:
            continue
        flags = {
            "target": target.findtext("active", default=""),
            "other": other.findtext("active", default=""),
            "gear": gear.findtext("active", default=""),
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
            and flags["gear"] == "False"
            and len(true_flags) == (1 if enabled else 0)
            and target.findtext("canformpersona", default="") == "Self"
            and target.findtext("notes", default="") == expected["target_notes"]
            and other.findtext("notes", default="") == expected["other_notes"]
            and gear.findtext("notes", default="") == expected["gear_notes"]
            and root.findtext("./customstate/active", default="") == expected["custom_active"]
        ):
            return
    device.capture("cyberware-active-commlink-workspace-not-persisted")
    raise RuntimeError(f"Cyberware Active Commlink invariant was not durable: {observed!r}")


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILES[profile]
    cyberware_id = expected["cyberware_id"]
    compact_id = cyberware_id.replace("-", "")
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    open_active_commlink_page(device, cyberware_id)
    assert_toggle(device, cyberware_id, False)
    device.tap(f"cyberware-active-commlink-toggle-{compact_id}", timeout=60)
    assert_toggle(device, cyberware_id, True)
    device.tap(f"cyberware-active-commlink-save-{compact_id}", timeout=240, scroll=True)
    device.wait(f"cyberware-active-commlink-open-{compact_id}", timeout=120, scroll=True)
    assert_workspace(device, expected, True)
    open_active_commlink_page(device, cyberware_id)
    assert_toggle(device, cyberware_id, True)
    device.capture(f"cyberware-active-commlink-{profile}-enabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, expected, True)
    open_active_commlink_page(device, cyberware_id)
    assert_toggle(device, cyberware_id, True)
    device.capture(f"cyberware-active-commlink-{profile}-enabled-after-process-restart")

    device.tap(f"cyberware-active-commlink-toggle-{compact_id}", timeout=60)
    assert_toggle(device, cyberware_id, False)
    device.tap(f"cyberware-active-commlink-save-{compact_id}", timeout=240, scroll=True)
    assert_workspace(device, expected, False)
    open_active_commlink_page(device, cyberware_id)
    assert_toggle(device, cyberware_id, False)
    device.capture(f"cyberware-active-commlink-{profile}-disabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, expected, False)
    open_active_commlink_page(device, cyberware_id)
    assert_toggle(device, cyberware_id, False)
    device.capture(f"cyberware-active-commlink-{profile}-disabled-after-process-restart")


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
        default=fixtures / PROFILES["creation"]["fixture"],
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / PROFILES["career"]["fixture"],
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "cyberwareActiveCommlinkPageSha256": android_root / "src/Chummer.Android/Native/CyberwareActiveCommlinkPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "cyberwareActiveCommlinkContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CyberwareActiveCommlinkEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "cyberwareActiveCommlinkRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCyberwareActiveCommlinkRules.cs",
        "weaponHomeNodeRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterWeaponHomeNodeRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Cyberware Active Commlink E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Cyberware Active Commlink E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Cyberware Active Commlink E2E requires arm64-v8a, got {abi!r}")

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
        "journey": "cyberware-active-commlink",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
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
        print(f"Cyberware Active Commlink E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
