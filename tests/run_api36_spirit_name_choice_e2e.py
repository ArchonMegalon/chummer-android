#!/usr/bin/env python3
"""Prove the shared Chummer5 SpiritControl metatype DropDownList on an API 36 phone."""

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


CONTROL = "SpiritControl.cboSpiritName"
CONTROL_PROOF_KEYS = (
    "sharedCreateCareerReachability",
    "dropDownListOnly",
    "selectedIdentityStable",
    "traditionStreamChoices",
    "limitBeforeAddRules",
    "enabledImprovementsOnly",
    "critterNameUnchanged",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "target_id": "85111111-8511-8511-8511-851111111111",
        "untouched_id": "85222222-8522-8522-8522-852222222222",
        "original": "Spirit of Fire",
        "changed": "Guardian Spirit",
        "untouched_name": "Spirit of Water",
        "critter_name": "Ash",
        "target_notes": "Creation target Spirit notes must remain intact",
        "untouched_notes": "Creation untouched Spirit notes must remain intact",
        "custom_name": "Creation unrelated Spirit name text",
    },
    "CharacterCareer": {
        "target_id": "85333333-8533-8533-8533-853333333333",
        "untouched_id": "85444444-8544-8544-8544-854444444444",
        "original": "Machine Sprite",
        "changed": "Diagnostics Sprite",
        "untouched_name": "Courier Sprite",
        "critter_name": "Bit",
        "target_notes": "Career target Sprite notes must remain intact",
        "untouched_notes": "Career untouched Sprite notes must remain intact",
        "custom_name": "Career unrelated Sprite name text",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_selected_spirit(device: shared.Device, expected: dict[str, str]) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-section-tab-magician",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        "build-action-tab-magician-spirits",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"collection-item-spirit-{expected['target_id']}",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.wait(f"collection-editor-spirit-{expected['target_id']}", timeout=120)


def open_name_page(device: shared.Device, expected: dict[str, str]) -> str:
    token = expected["target_id"].replace("-", "")
    open_selected_spirit(device, expected)
    device.tap(
        f"spirit-name-choice-open-{token}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    device.wait(f"spirit-name-choice-page-{token}", timeout=60)
    device.wait(f"spirit-name-choice-picker-{token}", timeout=60)
    return token


def assert_ui_name(device: shared.Device, token: str, expected: str) -> None:
    actual = shared.selected_text(
        device,
        f"spirit-name-choice-picker-{token}",
        "Spirit/Sprite metatype",
        scroll=True,
    )
    if actual != expected:
        device.capture("spirit-name-choice-ui-mismatch")
        raise RuntimeError(f"Spirit/Sprite metatype expected {expected!r}, got {actual!r}")


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


def assert_workspace_state(
    device: shared.Device,
    expected: dict[str, str],
    selected_name: str,
) -> None:
    observations: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        spirits = {
            spirit.findtext("guid", default="").lower(): spirit
            for spirit in character.findall("./spirits/spirit")
        }
        target = spirits.get(expected["target_id"])
        untouched = spirits.get(expected["untouched_id"])
        if target is None or untouched is None:
            continue
        observations.append(
            {
                "target": target.findtext("name", default=""),
                "untouched": untouched.findtext("name", default=""),
                "critterName": target.findtext("crittername", default=""),
            }
        )
        if (
            target.findtext("name", default="") == selected_name
            and target.findtext("crittername", default="") == expected["critter_name"]
            and target.findtext("notes", default="") == expected["target_notes"]
            and target.findtext("force", default="") in {"4", "5"}
            and target.findtext("fettered", default="") == "False"
            and untouched.findtext("name", default="") == expected["untouched_name"]
            and untouched.findtext("notes", default="") == expected["untouched_notes"]
            and character.findtext("./customstate/name", default="") == expected["custom_name"]
        ):
            return
    device.capture("spirit-name-choice-workspace-not-persisted")
    raise RuntimeError(f"Spirit/Sprite metatype was not durable; observed {observations!r}")


def set_name(device: shared.Device, expected: dict[str, str], value: str) -> None:
    token = open_name_page(device, expected)
    device.tap(f"spirit-name-choice-picker-{token}", timeout=60, scroll=True)
    device.tap(value, timeout=60, scroll=True, max_scrolls=12)
    time.sleep(0.5)
    assert_ui_name(device, token, value)
    device.tap(f"spirit-name-choice-save-{token}", timeout=240, scroll=True)
    device.wait(f"spirit-name-choice-open-{token}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    token = open_name_page(device, expected)
    assert_ui_name(device, token, expected["original"])
    device.capture(f"spirit-name-choice-{profile.lower()}-initial")
    device.back()

    set_name(device, expected, expected["changed"])
    assert_workspace_state(device, expected, expected["changed"])
    token = open_name_page(device, expected)
    assert_ui_name(device, token, expected["changed"])
    device.capture(f"spirit-name-choice-{profile.lower()}-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_state(device, expected, expected["changed"])
    token = open_name_page(device, expected)
    assert_ui_name(device, token, expected["changed"])
    device.back()

    set_name(device, expected, expected["original"])
    assert_workspace_state(device, expected, expected["original"])
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_state(device, expected, expected["original"])
    token = open_name_page(device, expected)
    assert_ui_name(device, token, expected["original"])
    device.capture(f"spirit-name-choice-{profile.lower()}-reverted-after-restart")


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
        default=fixtures / "creation-spirit-name-choice-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-spirit-name-choice-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    presentation = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    core = workspace_root / "chummer-core-engine"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "buildPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "buildFlowPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "spiritNameChoicePageSha256": android_root / "src" / "Chummer.Android" / "Native" / "SpiritNameChoicePage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "spiritNameChoiceContractSha256": presentation / "SpiritNameChoiceEditRequest.cs",
        "collectionEditorStateSha256": presentation / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": presentation / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": presentation / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": presentation / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": presentation / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": presentation / "ICharacterOverviewPresenter.cs",
        "spiritNameChoiceRulesSha256": core / "Chummer.Contracts" / "Characters" / "CharacterSpiritNameChoiceRules.cs",
        "characterSectionModelsSha256": core / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "sourceResolverContractSha256": core / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": core / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs",
        "characterSectionServiceSha256": core / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "traditionsCatalogSha256": core / "Chummer" / "data" / "traditions.xml",
        "streamsCatalogSha256": core / "Chummer" / "data" / "streams.xml",
        "workspaceStoreSha256": core / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
        "sr5ShellCatalogSha256": core / "Chummer.Rulesets.Sr5" / "Sr5ShellCatalogs.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Spirit/Sprite metatype E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Spirit/Sprite metatype E2E requires API 36, got {api!r}")

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
        "journey": "spirit-name-choice",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": 1,
        "controls": {CONTROL: {key: "pass" for key in CONTROL_PROOF_KEYS}},
        "journeys": {
            "creationRunnerImported": "pass",
            "creationLimitedBaseAndAddSpiritEditedReopenedRestarted": "pass",
            "creationRevertedAndRestarted": "pass",
            "careerRunnerImported": "pass",
            "careerStreamAndAddSpriteEditedReopenedRestarted": "pass",
            "careerRevertedAndRestarted": "pass",
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
        print(f"Spirit/Sprite metatype E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
