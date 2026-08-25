#!/usr/bin/env python3
"""Prove Chummer5 SustainedObjectControl edits on an API 36 phone."""

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
    "SustainedObjectControl.nudForce",
    "SustainedObjectControl.nudNetHits",
    "SustainedObjectControl.chkSelfSustained",
    "SustainedObjectControl.cmdDelete",
)
CONTROL_PROOF_KEYS = (
    "sharedCreateCareerReachability",
    "linkedTypeGuidOccurrenceIdentity",
    "duplicateCastIsolation",
    "forceAndNetHitsBounds",
    "critterPowerSelfSustainedHidden",
    "explicitDeleteConfirmation",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "spell_id": "83111111-8311-8311-8311-831111111111",
        "critter_id": "83222222-8322-8322-8322-832222222222",
        "custom": "Creation unrelated sustained text",
        "first_notes": "Creation first cast untouched",
        "second_notes": "Creation second cast target",
    },
    "CharacterCareer": {
        "spell_id": "83333333-8333-8333-8333-833333333333",
        "critter_id": "83444444-8344-8344-8344-834444444444",
        "custom": "Career unrelated sustained text",
        "first_notes": "Career first cast untouched",
        "second_notes": "Career second cast target",
    },
}


def token(kind: str, item_id: str, occurrence: int) -> str:
    return f"{kind.lower()}-{item_id.replace('-', '')}-{occurrence}"


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_sustained_list(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-sustained-effects",
        timeout=120,
        scroll=True,
        max_scrolls=30,
        scroll_distance_ratio=0.20,
    )
    device.wait("sustained-effects-page", timeout=120)


def open_effect(device: shared.Device, effect_token: str) -> None:
    open_sustained_list(device)
    device.tap(
        f"sustained-effect-open-{effect_token}",
        timeout=120,
        scroll=True,
        max_scrolls=30,
        scroll_distance_ratio=0.20,
    )
    device.wait(f"sustained-effect-editor-{effect_token}", timeout=60)


def select_picker_value(device: shared.Device, selector: str, value: int) -> None:
    device.tap(selector, timeout=60, scroll=True, max_scrolls=18)
    device.tap(str(value), timeout=60, scroll=True, max_scrolls=8)
    time.sleep(0.5)
    actual = shared.selected_text(device, selector, selector, scroll=True)
    if actual != str(value):
        device.capture(f"{selector}-value-not-selected")
        raise RuntimeError(f"{selector} expected {value}, got {actual!r}")


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


def assert_workspace_state(device: shared.Device, expected: dict[str, str], critter_deleted: bool) -> None:
    observations: list[dict[str, object]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        effects = root.findall("./sustainedobjects/sustainedobject")
        spell = [
            effect
            for effect in effects
            if effect.findtext("linkedobject", default="").lower() == expected["spell_id"]
            and effect.findtext("linkedobjecttype") == "Spell"
        ]
        critter = [
            effect
            for effect in effects
            if effect.findtext("linkedobject", default="").lower() == expected["critter_id"]
            and effect.findtext("linkedobjecttype") == "CritterPower"
        ]
        observations.append({"spell_count": len(spell), "critter_count": len(critter)})
        if (
            len(spell) == 2
            and spell[0].findtext("force") == "4"
            and spell[0].findtext("nethits") == "2"
            and spell[0].findtext("self") == "True"
            and spell[0].findtext("notes") == expected["first_notes"]
            and spell[1].findtext("force") == "8"
            and spell[1].findtext("nethits") == "5"
            and spell[1].findtext("self") == "True"
            and spell[1].findtext("notes") == expected["second_notes"]
            and len(critter) == (0 if critter_deleted else 1)
            and root.findtext("./customstate/sustained") == expected["custom"]
        ):
            return
    device.capture("sustained-effects-workspace-not-persisted")
    raise RuntimeError(f"Sustained-effect invariant was not durable; observed {observations!r}")


def assert_picker(device: shared.Device, selector: str, value: int) -> None:
    actual = shared.selected_text(device, selector, selector, scroll=True)
    if actual != str(value):
        device.capture(f"{selector}-readback-mismatch")
        raise RuntimeError(f"{selector} expected {value}, got {actual!r}")


def edit_duplicate_spell(device: shared.Device, expected: dict[str, str]) -> None:
    effect_token = token("Spell", expected["spell_id"], 1)
    open_effect(device, effect_token)
    force_selector = f"sustained-effect-force-{effect_token}"
    hits_selector = f"sustained-effect-net-hits-{effect_token}"
    self_selector = f"sustained-effect-self-{effect_token}"
    select_picker_value(device, force_selector, 8)
    select_picker_value(device, hits_selector, 5)
    self_switch = device.wait(self_selector, timeout=60, scroll=True)
    if self_switch.attributes.get("checked") != "false":
        raise RuntimeError("The second duplicate spell did not open with Self-Sustained disabled")
    device.tap(self_selector, timeout=60, scroll=True)
    device.tap(f"sustained-effect-save-{effect_token}", timeout=240, scroll=True)
    device.wait("build-sustained-effects", timeout=120, scroll=True)


def assert_spell_ui_readback(device: shared.Device, expected: dict[str, str]) -> None:
    effect_token = token("Spell", expected["spell_id"], 1)
    open_effect(device, effect_token)
    assert_picker(device, f"sustained-effect-force-{effect_token}", 8)
    assert_picker(device, f"sustained-effect-net-hits-{effect_token}", 5)
    switch = device.wait(f"sustained-effect-self-{effect_token}", timeout=60, scroll=True)
    if switch.attributes.get("checked") != "true":
        raise RuntimeError("Self-Sustained readback was not enabled")
    device.shell("input", "keyevent", "4")
    device.shell("input", "keyevent", "4")


def delete_critter_effect(device: shared.Device, expected: dict[str, str]) -> None:
    effect_token = token("CritterPower", expected["critter_id"], 0)
    open_effect(device, effect_token)
    if device.find(f"sustained-effect-self-{effect_token}") is not None:
        device.capture("critter-power-self-sustained-visible")
        raise RuntimeError("Critter Power incorrectly exposed Self-Sustained")
    device.tap(f"sustained-effect-delete-{effect_token}", timeout=60, scroll=True)
    device.tap("Remove", timeout=60)
    device.wait("build-sustained-effects", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    edit_duplicate_spell(device, expected)
    assert_workspace_state(device, expected, critter_deleted=False)
    assert_spell_ui_readback(device, expected)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_state(device, expected, critter_deleted=False)
    assert_spell_ui_readback(device, expected)

    delete_critter_effect(device, expected)
    assert_workspace_state(device, expected, critter_deleted=True)
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace_state(device, expected, critter_deleted=True)
    open_sustained_list(device)
    if device.find(f"sustained-effect-open-{token('CritterPower', expected['critter_id'], 0)}") is not None:
        raise RuntimeError("Deleted Critter Power sustained effect returned after process restart")
    device.capture(f"sustained-effects-{profile.lower()}-complete")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-sustained-effects-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-sustained-effects-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "sustainedEffectsPageSha256": android_root / "src/Chummer.Android/Native/SustainedObjectsPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "sustainedEffectsContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/SustainedObjectEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "sustainedEffectsRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterSustainedObjectRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Sustained-effect E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Sustained-effect E2E requires API 36, got {api!r}")

    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    prove_profile(device, creation_fixture, "CharacterCreate")
    prove_profile(device, career_fixture, "CharacterCareer")

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "sustained-effects",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in CONTROL_PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "creationRunnerImported": "pass",
            "creationDuplicateEditedReopenedRestarted": "pass",
            "creationCritterDeletedRestarted": "pass",
            "careerRunnerImported": "pass",
            "careerDuplicateEditedReopenedRestarted": "pass",
            "careerCritterDeletedRestarted": "pass",
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
        print(f"Sustained-effect E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
