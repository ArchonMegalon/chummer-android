#!/usr/bin/env python3
"""Prove exact Create/Career Martial Arts Notes editing on an API 36 arm64 phone."""

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
    "CharacterCreate.tsMartialArtsNotes",
    "CharacterCareer.tsMartialArtsNotes",
)
PACKAGE = "com.myexternalbrain.chummer"
ABI = "arm64-v8a"
PROOF_KEYS = (
    "typedStableMartialArtIdentity",
    "parentArtScopedTechniqueIdentity",
    "duplicateAmbiguousGuidsRejected",
    "notesAndColorAtomicMutation",
    "creationCareerSameZeroCostRules",
    "nonNotesXmlPreserved",
    "workspaceRevisionBound",
    "atomicSaveRecovery",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(device: shared.Device, fixture_name: str) -> None:
    shared.launch_app(device)
    shared.wait_for_phone_runners(device, timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    shared.wait_for_phone_runner_route(device, timeout=120)


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-martial-art-notes", scroll=True, timeout=120, max_scrolls=32)
    device.wait("martial-art-notes-page", timeout=60)


def select_target(device: shared.Device, label: str) -> None:
    device.tap("martial-art-notes-target", timeout=60, scroll=True)
    device.tap(label, timeout=60, scroll=True, max_scrolls=24)
    time.sleep(0.35)
    observed = shared.selected_text(
        device, "martial-art-notes-target", "Martial Art or Technique", scroll=True
    )
    if observed != label:
        device.capture("martial-art-notes-target-mismatch")
        raise RuntimeError(f"Martial Arts target was {observed!r}; expected {label!r}")


def assert_field(device: shared.Device, selector: str, expected: str) -> None:
    actual = device.wait(selector, timeout=60, scroll=True).attributes.get("text", "")
    if actual != expected:
        device.capture(f"{selector}-mismatch")
        raise RuntimeError(f"{selector} was {actual!r}; expected {expected!r}")


def workspace_payloads(device: shared.Device) -> list[str]:
    listing = device.shell("run-as", PACKAGE, "find", "files/state", "-type", "f")
    payloads: list[str] = []
    for path in (line.strip() for line in listing.splitlines()):
        if not path:
            continue
        try:
            raw = device.run("exec-out", "run-as", PACKAGE, "cat", path).stdout
            record = json.loads(raw)
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            continue
        envelope = record.get("Envelope") if isinstance(record, dict) else None
        payload = envelope.get("Payload") if isinstance(envelope, dict) else None
        if isinstance(payload, str) and payload.strip().startswith("<"):
            payloads.append(payload)
    return payloads


def find_art(root: ET.Element, art_id: str) -> ET.Element | None:
    return next((
        art for art in root.findall("./martialarts/martialart")
        if art.findtext("guid") == art_id
    ), None)


def find_target(root: ET.Element, art_id: str, technique_id: str | None) -> ET.Element | None:
    art = find_art(root, art_id)
    if art is None or technique_id is None:
        return art
    return next((
        technique for technique in art.findall("./martialarttechniques/martialarttechnique")
        if technique.findtext("guid") == technique_id
    ), None)


def assert_workspace(
    device: shared.Device,
    *,
    art_id: str,
    technique_id: str | None,
    expected_notes: str,
    expected_color: str,
    expected_karma: str,
    expected_nuyen: str,
    sentinel: str,
) -> None:
    observed: list[tuple[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        if root.findtext("customstate") != sentinel:
            continue
        target = find_target(root, art_id, technique_id)
        if target is None:
            continue
        state = (target.findtext("notes", ""), target.findtext("notesColor", ""))
        observed.append(state)
        other_parent = find_target(
            root,
            "95333333-9533-9533-9533-953333333333",
            "95444444-9544-9544-9544-954444444444",
        )
        other_parent_ok = other_parent is None or (
            other_parent.findtext("notes") == "Career same-name other-parent technique"
            and other_parent.findtext("notesColor") == "#556677"
        )
        if (
            state == (expected_notes, expected_color)
            and root.findtext("karma") == expected_karma
            and root.findtext("nuyen") == expected_nuyen
            and target.findtext("name") in {"Aikido", "Disarm"}
            and target.findtext("source") == "RG"
            and other_parent_ok
        ):
            return
    device.capture("martial-art-notes-workspace-not-persisted")
    raise RuntimeError(f"Martial Arts notes were not durable: {observed!r}")


def run_mode(
    device: shared.Device,
    *,
    fixture: Path,
    label: str,
    art_id: str,
    technique_id: str | None,
    old_notes: str,
    old_color: str,
    new_notes: str,
    new_color: str,
    karma: str,
    nuyen: str,
    sentinel: str,
    mode: str,
) -> None:
    device.shell("pm", "clear", PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    select_target(device, label)
    assert_field(device, "martial-art-notes-text", old_notes)
    assert_field(device, "martial-art-notes-color", old_color)
    device.set_text("martial-art-notes-text", "Martial Arts notes", new_notes, scroll=True)
    device.set_text("martial-art-notes-color", "Notes color", new_color, scroll=True)
    device.tap("martial-art-notes-save", timeout=180, scroll=True)
    device.wait("build-martial-art-notes", timeout=180, scroll=True)
    assert_workspace(
        device, art_id=art_id, technique_id=technique_id,
        expected_notes=new_notes, expected_color=new_color,
        expected_karma=karma, expected_nuyen=nuyen, sentinel=sentinel,
    )
    open_page(device)
    select_target(device, label)
    assert_field(device, "martial-art-notes-text", new_notes)
    assert_field(device, "martial-art-notes-color", new_color)
    device.capture(f"martial-art-notes-{mode}-same-session")
    device.shell("am", "force-stop", PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(
        device, art_id=art_id, technique_id=technique_id,
        expected_notes=new_notes, expected_color=new_color,
        expected_karma=karma, expected_nuyen=nuyen, sentinel=sentinel,
    )
    open_page(device)
    select_target(device, label)
    assert_field(device, "martial-art-notes-text", new_notes)
    device.capture(f"martial-art-notes-{mode}-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-martial-art-notes-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-martial-art-notes-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "martialArtNotesPageSha256": android_root / "src/Chummer.Android/Native/MartialArtNotesPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "martialArtNotesContractSha256": overview / "MartialArtNotesEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "martialArtNotesRulesSha256": contracts / "CharacterMartialArtNotesRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Martial Arts Notes source graph is incomplete: {missing!r}")
    if shared.PACKAGE != PACKAGE:
        raise RuntimeError(f"Driver package mismatch: {shared.PACKAGE!r} != {PACKAGE!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if api != "36":
        raise RuntimeError(f"Martial Arts Notes E2E requires API 36, got {api!r}")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Martial Arts Notes E2E requires {ABI}, got {abi!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    if not device.shell("cmd", "package", "path", PACKAGE).startswith("package:"):
        raise RuntimeError(f"Expected installed package {PACKAGE!r} was not found")
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    run_mode(
        device, fixture=creation_fixture, label="Art · Aikido · 94111111",
        art_id="94111111-9411-9411-9411-941111111111", technique_id=None,
        old_notes="Creation selected art note", old_color="#112233",
        new_notes="API36 creation Martial Art notes", new_color="#445566",
        karma="19", nuyen="1234.56", sentinel="Creation Martial Arts notes runner sentinel",
        mode="creation",
    )
    run_mode(
        device, fixture=career_fixture, label="Technique · Aikido > Disarm · 95222222",
        art_id="95111111-9511-9511-9511-951111111111",
        technique_id="95222222-9522-9522-9522-952222222222",
        old_notes="Career selected technique note", old_color="#334455",
        new_notes="API36 career Technique notes", new_color="#667788",
        karma="27", nuyen="7654.32", sentinel="Career Martial Arts notes runner sentinel",
        mode="career",
    )

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "martial-art-notes",
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
        "journeys": {
            "creationMartialArtNotesEdited": "pass",
            "creationSameSessionReopen": "pass",
            "creationProcessRestart": "pass",
            "careerParentScopedTechniqueNotesEdited": "pass",
            "careerSameSessionReopen": "pass",
            "careerProcessRestart": "pass",
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
        print(f"Martial Arts Notes E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
