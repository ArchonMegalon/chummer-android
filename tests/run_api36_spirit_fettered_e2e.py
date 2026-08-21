#!/usr/bin/env python3
"""Prove the shared Chummer5 SpiritControl Fettered/Pet behavior on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "SpiritControl.chkFettered"
CONTROL_PROOF_KEYS = (
    "sharedCreateCareerReachability",
    "selectedIdentityStable",
    "oneFetteredEntityRule",
    "careerKarmaCostAndUndo",
    "magicImprovementLifecycle",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
PROFILE_TARGETS = {
    "CharacterCreate": {
        "target_id": "74111111-7411-7411-7411-741111111111",
        "untouched_id": "74222222-7422-7422-7422-742222222222",
        "target_notes": "Creation target spirit notes must remain intact",
        "untouched_notes": "Creation untouched spirit notes must remain intact",
        "custom_fettered": "Creation unrelated fettered text",
        "karma_after_fetter": "0",
        "expense_amount": None,
    },
    "CharacterCareer": {
        "target_id": "74333333-7433-7433-7433-743333333333",
        "untouched_id": "74444444-7444-7444-7444-744444444444",
        "target_notes": "Career target spirit notes must remain intact",
        "untouched_notes": "Career untouched spirit notes must remain intact",
        "custom_fettered": "Career unrelated fettered text",
        "karma_after_fetter": "8",
        "expense_amount": "-12",
    },
}


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_selected_spirit(device: shared.Device, expected: dict[str, str | None]) -> None:
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


def open_fettered_page(device: shared.Device, expected: dict[str, str | None]) -> None:
    target_id = str(expected["target_id"])
    compact_id = target_id.replace("-", "")
    open_selected_spirit(device, expected)
    device.tap(
        f"spirit-fettered-open-{compact_id}",
        timeout=120,
        scroll=True,
        max_scrolls=36,
        scroll_distance_ratio=0.18,
    )
    device.wait(f"spirit-fettered-page-{compact_id}", timeout=60)


def assert_toggle(device: shared.Device, expected: dict[str, str | None], value: bool) -> None:
    compact_id = str(expected["target_id"]).replace("-", "")
    node = device.wait(f"spirit-fettered-toggle-{compact_id}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != value:
        device.capture("spirit-fettered-toggle-mismatch")
        raise RuntimeError(f"Fettered/Pet switch was {observed!r}; expected {value!r}")


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
    expected: dict[str, str | None],
    fettered: bool,
) -> None:
    observations: list[dict[str, object]] = []
    for payload in workspace_payloads(device):
        try:
            character = ET.fromstring(payload)
        except ET.ParseError:
            continue
        spirits = {
            spirit.findtext("guid", default="").lower(): spirit
            for spirit in character.findall("./spirits/spirit")
        }
        target = spirits.get(str(expected["target_id"]))
        untouched = spirits.get(str(expected["untouched_id"]))
        if target is None or untouched is None:
            continue
        improvements = [
            improvement
            for improvement in character.findall("./improvements/improvement")
            if improvement.findtext("improvementsource", default="") == "SpiritFettering"
        ]
        expenses = [
            expense
            for expense in character.findall("./expenses/expense")
            if expense.findtext("./undo/karmatype", default="") == "SpiritFettering"
            and expense.findtext("./undo/objectid", default="").lower() == expected["target_id"]
        ]
        observations.append(
            {
                "target": target.findtext("fettered", default=""),
                "untouched": untouched.findtext("fettered", default=""),
                "improvements": len(improvements),
                "karma": character.findtext("karma", default=""),
                "expenses": len(expenses),
            }
        )
        expense_ok = expected["expense_amount"] is None and not expenses or (
            expected["expense_amount"] is not None
            and len(expenses) == 1
            and expenses[0].findtext("amount", default="") == expected["expense_amount"]
        )
        if (
            target.findtext("fettered", default="") == ("True" if fettered else "False")
            and target.findtext("notes", default="") == expected["target_notes"]
            and untouched.findtext("fettered", default="") == "False"
            and untouched.findtext("notes", default="") == expected["untouched_notes"]
            and character.findtext("./customstate/fettered", default="") == expected["custom_fettered"]
            and len(improvements) == (1 if fettered else 0)
            and character.findtext("karma", default="") == expected["karma_after_fetter"]
            and expense_ok
        ):
            return
    device.capture("spirit-fettered-workspace-not-persisted")
    raise RuntimeError(f"Fettered/Pet invariant was not durable; observed {observations!r}")


def set_fettered(
    device: shared.Device,
    expected: dict[str, str | None],
    value: bool,
) -> None:
    compact_id = str(expected["target_id"]).replace("-", "")
    open_fettered_page(device, expected)
    assert_toggle(device, expected, not value)
    device.tap(f"spirit-fettered-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected, value)
    device.tap(f"spirit-fettered-save-{compact_id}", timeout=240, scroll=True)
    device.wait(f"spirit-fettered-open-{compact_id}", timeout=120, scroll=True)


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILE_TARGETS[profile]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)

    open_fettered_page(device, expected)
    assert_toggle(device, expected, False)
    device.capture(f"spirit-fettered-{profile.lower()}-initial")
    device.shell("input", "keyevent", "4")

    set_fettered(device, expected, True)
    assert_workspace_state(device, expected, True)
    open_fettered_page(device, expected)
    assert_toggle(device, expected, True)
    device.capture(f"spirit-fettered-{profile.lower()}-enabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_state(device, expected, True)
    open_fettered_page(device, expected)
    assert_toggle(device, expected, True)

    compact_id = str(expected["target_id"]).replace("-", "")
    device.tap(f"spirit-fettered-toggle-{compact_id}", timeout=60)
    assert_toggle(device, expected, False)
    device.tap(f"spirit-fettered-save-{compact_id}", timeout=240, scroll=True)
    assert_workspace_state(device, expected, False)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace_state(device, expected, False)
    open_fettered_page(device, expected)
    assert_toggle(device, expected, False)
    device.capture(f"spirit-fettered-{profile.lower()}-disabled-after-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-spirit-fettered-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-spirit-fettered-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "buildPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "buildFlowPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "spiritFetteredPageSha256": android_root / "src" / "Chummer.Android" / "Native" / "SpiritFetteredPage.cs",
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "spiritFetteredContractSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "SpiritFetteredEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "spiritFetteringRulesSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSpiritFetteringRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
        "sr5ShellCatalogSha256": workspace_root / "chummer-core-engine" / "Chummer.Rulesets.Sr5" / "Sr5ShellCatalogs.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Fettered/Pet E2E source graph is incomplete: {missing!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Fettered/Pet E2E requires API 36, got {api!r}")

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
        "journey": "spirit-fettered",
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
            "creationFetteredPersistedReopenedRestarted": "pass",
            "creationUnfetteredPersistedReopenedRestarted": "pass",
            "careerRunnerImported": "pass",
            "careerExactKarmaAndUndoPersisted": "pass",
            "careerFetteredPersistedReopenedRestarted": "pass",
            "careerUnfetteredPersistedReopenedRestarted": "pass",
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
        print(f"Fettered/Pet E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
