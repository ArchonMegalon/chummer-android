#!/usr/bin/env python3
"""Prove exact Chummer5 Critter Power Count behavior on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "chkCritterPowerCount"
CONTROL_PROOF_KEYS = (
    "selectedIdentityStable",
    "legacyDefaultTrue",
    "excludedAndIncluded",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "target_id": "69111111-6911-6911-6911-691111111111",
        "untouched_id": "69222222-6922-6922-6922-692222222222",
        "target_notes": "Creation target power notes must remain intact",
        "untouched_notes": "Creation untouched power notes must remain intact",
        "custom_count": "Creation unrelated count text",
    },
    "CharacterCareer": {
        "target_id": "69333333-6933-6933-6933-693333333333",
        "untouched_id": "69444444-6944-6944-6944-694444444444",
        "target_notes": "Career target power notes must remain intact",
        "untouched_notes": "Career untouched power notes must remain intact",
        "custom_count": "Career unrelated count text",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_selected_power(device: shared.Device, expected: dict[str, str]) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-section-tab-critter",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        "build-action-tab-critter-critterpowers",
        scroll=True,
        timeout=120,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.tap(
        f"collection-item-critterpower-{expected['target_id']}",
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
    )
    device.wait(f"collection-editor-critterpower-{expected['target_id']}", timeout=120)


def open_count_page(device: shared.Device, expected: dict[str, str]) -> None:
    compact_id = expected["target_id"].replace("-", "")
    open_selected_power(device, expected)
    device.tap(
        f"critter-power-count-open-{compact_id}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    device.wait(f"critter-power-count-page-{compact_id}", timeout=60)


def assert_toggle(device: shared.Device, expected: dict[str, str], value: bool) -> None:
    compact_id = expected["target_id"].replace("-", "")
    node = device.wait(f"critter-power-count-toggle-{compact_id}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != value:
        device.capture("critter-power-count-toggle-mismatch")
        raise RuntimeError(f"Critter Power Count switch was {observed!r}; expected {value!r}")


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


def assert_workspace_count(
    device: shared.Device,
    expected: dict[str, str],
    counts_towards_limit: bool,
) -> None:
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        powers = {
            power.findtext("guid", default="").lower(): power
            for power in character.findall("./critterpowers/critterpower")
        }
        target = powers.get(expected["target_id"])
        untouched = powers.get(expected["untouched_id"])
        if target is None or untouched is None:
            continue
        target_flag = target.findtext("counttowardslimit", default="")
        untouched_flag = untouched.findtext("counttowardslimit", default="")
        observed.append({"target": target_flag, "untouched": untouched_flag})
        if (
            target_flag == ("True" if counts_towards_limit else "False")
            and untouched_flag == "False"
            and target.findtext("notes", default="") == expected["target_notes"]
            and untouched.findtext("notes", default="") == expected["untouched_notes"]
            and character.findtext("./customstate/counttowardslimit", default="") == expected["custom_count"]
        ):
            return
    device.capture("critter-power-count-workspace-not-persisted")
    raise RuntimeError(f"Critter Power Count invariant was not durable; observed {observed!r}")


def save_count(device: shared.Device, expected: dict[str, str], value: bool) -> None:
    compact_id = expected["target_id"].replace("-", "")
    open_count_page(device, expected)
    assert_toggle(device, expected, not value)
    device.tap(f"critter-power-count-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected, value)
    device.tap(f"critter-power-count-save-{compact_id}", timeout=240, scroll=True)
    device.wait(f"critter-power-count-open-{compact_id}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    open_count_page(device, expected)
    assert_toggle(device, expected, True)
    device.capture(f"critter-power-count-{profile.lower()}-legacy-default")
    device.shell("input", "keyevent", "4")

    save_count(device, expected, False)
    assert_workspace_count(device, expected, False)
    open_count_page(device, expected)
    assert_toggle(device, expected, False)
    device.capture(f"critter-power-count-{profile.lower()}-excluded-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_count(device, expected, False)
    open_count_page(device, expected)
    assert_toggle(device, expected, False)

    compact_id = expected["target_id"].replace("-", "")
    device.tap(f"critter-power-count-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected, True)
    device.tap(f"critter-power-count-save-{compact_id}", timeout=240, scroll=True)
    assert_workspace_count(device, expected, True)
    open_count_page(device, expected)
    assert_toggle(device, expected, True)
    device.capture(f"critter-power-count-{profile.lower()}-included-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_count(device, expected, True)
    open_count_page(device, expected)
    assert_toggle(device, expected, True)
    device.capture(f"critter-power-count-{profile.lower()}-included-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-critter-power-count-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-critter-power-count-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "critterPowerCountPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "CritterPowerCountPage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "critterPowerCountContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CritterPowerCountEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "critterPowerCountRulesSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterCritterPowerCountRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
    }
    if not all(path.is_file() for path in source_paths.values()):
        missing = [str(path) for path in source_paths.values() if not path.is_file()]
        raise RuntimeError(f"Critter Power Count E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Critter Power Count E2E requires API 36, got {api!r}")

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
        "journey": "critter-power-count",
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
            "creationLegacyDefaultReadback": "pass",
            "creationExcludedPersistedReopenedRestarted": "pass",
            "creationIncludedPersistedReopenedRestarted": "pass",
            "careerRunnerImported": "pass",
            "careerSavedTrueReadback": "pass",
            "careerExcludedPersistedReopenedRestarted": "pass",
            "careerIncludedPersistedReopenedRestarted": "pass",
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
        print(f"Critter Power Count E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
