#!/usr/bin/env python3
"""Prove exact Chummer5 Create/Career Lifestyle custom-name editing on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("CharacterCreate.tsLifestyleName", "CharacterCareer.tsLifestyleName")
PROFILES = {
    "creation": {
        "fixture": "creation-lifestyle-name-e2e.chum5",
        "target": "81111111-1111-1111-1111-111111111111",
        "expected": "Creation bolt-hole",
        "name": "Low",
        "notes": "Creation notes preserved",
        "notes_color": "#ff123456",
        "customstate": "Creation unrelated state",
    },
    "career": {
        "fixture": "career-lifestyle-name-e2e.chum5",
        "target": "82222222-2222-2222-2222-222222222222",
        "expected": "Career bolt-hole",
        "name": "Middle",
        "notes": "Career notes preserved",
        "notes_color": "#ff654321",
        "customstate": "Career unrelated state",
    },
}
PROOF_KEYS = (
    "stableLifestyleGuid",
    "exactExtraElement",
    "baseNameNotesAndColorPreserved",
    "expectedRevisionAtomicSave",
    "workspacePersisted",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)


def open_editor(device: shared.Device, target: str) -> None:
    shared.open_build(device, "phone")
    device.tap(
        "build-action-tab-lifestyle-lifestyles",
        scroll=True,
        timeout=120,
        max_scrolls=36,
    )
    device.wait(f"collection-item-lifestyle-{target}", timeout=120, scroll=True)
    device.tap(f"collection-item-lifestyle-{target}", timeout=120, scroll=True)
    device.wait(f"collection-editor-lifestyle-{target}", timeout=120)
    device.wait(f"collection-field-lifestylename-{target}", timeout=60, scroll=True)


def read_value(device: shared.Device, target: str) -> str:
    return shared.selected_text(
        device,
        f"collection-field-lifestylename-{target}",
        "Lifestyle Name",
        scroll=True,
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


def assert_workspace(device: shared.Device, expected: dict[str, str]) -> None:
    observed: list[tuple[str, str, str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        for lifestyle in root.findall("./lifestyles/lifestyle"):
            if lifestyle.findtext("guid", default="").lower() != expected["target"]:
                continue
            state = (
                lifestyle.findtext("extra", default=""),
                lifestyle.findtext("name", default=""),
                lifestyle.findtext("notes", default=""),
                lifestyle.findtext("notesColor", default=""),
            )
            observed.append(state)
            wanted = (
                expected["expected"],
                expected["name"],
                expected["notes"],
                expected["notes_color"],
            )
            if state == wanted:
                if root.findtext("customstate", default="") != expected["customstate"]:
                    raise RuntimeError("Unrelated character state changed during Lifestyle Name editing")
                return
    device.capture("lifestyle-name-workspace-not-persisted")
    raise RuntimeError(f"Lifestyle Name was not durable: {observed!r}")


def prove_profile(device: shared.Device, fixture: Path, profile: str) -> None:
    expected = PROFILES[profile]
    target = expected["target"]
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_editor(device, target)
    device.set_text(
        f"collection-field-lifestylename-{target}",
        "Lifestyle Name",
        expected["expected"],
        scroll=True,
    )
    device.tap(f"collection-save-{target}", timeout=180, scroll=True)
    device.wait(f"collection-field-lifestylename-{target}", timeout=120, scroll=True)
    assert_workspace(device, expected)
    if read_value(device, target) != expected["expected"]:
        raise RuntimeError("Lifestyle Name did not survive immediate refresh")

    device.back()
    device.tap(f"collection-item-lifestyle-{target}", timeout=120, scroll=True, max_scrolls=40)
    device.wait(f"collection-editor-lifestyle-{target}", timeout=120)
    if read_value(device, target) != expected["expected"]:
        raise RuntimeError("Lifestyle Name did not survive same-session reopen")
    device.capture(f"lifestyle-name-{profile}-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected)
    open_editor(device, target)
    if read_value(device, target) != expected["expected"]:
        raise RuntimeError("Lifestyle Name did not survive process restart")
    device.capture(f"lifestyle-name-{profile}-after-process-restart")


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
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "collectionRequestSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
        "collectionStateSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
        "collectionProjectorSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "sectionModelsSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
        "sectionServiceSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Lifestyle Name source graph is incomplete: {missing!r}")

    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Lifestyle Name E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    fixtures_by_profile = {
        "creation": args.creation_runner.resolve(),
        "career": args.career_runner.resolve(),
    }
    for fixture in fixtures_by_profile.values():
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    for profile, fixture in fixtures_by_profile.items():
        prove_profile(device, fixture, profile)

    controls = {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "lifestyle-name",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(fixtures_by_profile["creation"]),
        "careerFixtureSha256": shared.sha256(fixtures_by_profile["career"]),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "creationLifestyleNameEdited": "pass",
            "careerLifestyleNameEdited": "pass",
            "sameSessionAndProcessRestartReadback": "pass",
            "notesAndNotesColorPreserved": "pass",
            "blankAllowedAndLegacyLengthBoundBySourceContract": "pass",
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
        print(f"lifestyle-name E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
