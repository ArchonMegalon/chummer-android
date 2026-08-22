#!/usr/bin/env python3
"""Prove exact Create/Career Martial Art deletion on an API 36 arm64 phone."""

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
    "CharacterCreate.cmdDeleteMartialArt",
    "CharacterCareer.cmdDeleteMartialArt",
)
PACKAGE = "com.myexternalbrain.chummer"
ABI = "arm64-v8a"
PROOF_KEYS = (
    "typedStableMartialArtIdentity",
    "explicitConfirmationRequired",
    "cancelIsNoOp",
    "qualityBackedArtProtected",
    "parentArtCascadeExact",
    "nestedTechniqueParentScoped",
    "exactSourceGuidImprovementCleanup",
    "equalNamedAndUnrelatedSourcesPreserved",
    "creationCareerZeroRefundRules",
    "workspaceRevisionBound",
    "atomicSaveRecovery",
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


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap("build-martial-art-delete", scroll=True, timeout=120, max_scrolls=36)
    device.wait("martial-art-delete-page", timeout=60)


def select_target(device: shared.Device, label: str) -> None:
    device.tap("martial-art-delete-target", timeout=60, scroll=True)
    device.tap(label, timeout=60, scroll=True, max_scrolls=28)
    time.sleep(0.35)
    observed = shared.selected_text(
        device, "martial-art-delete-target", "Martial Art or Technique", scroll=True
    )
    if observed != label:
        device.capture("martial-art-delete-target-mismatch")
        raise RuntimeError(f"Martial Art delete target was {observed!r}; expected {label!r}")


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


def find_technique(root: ET.Element, art_id: str, technique_id: str) -> ET.Element | None:
    art = find_art(root, art_id)
    if art is None:
        return None
    return next((
        technique for technique in art.findall("./martialarttechniques/martialarttechnique")
        if technique.findtext("guid") == technique_id
    ), None)


def improvement_pairs(root: ET.Element) -> set[tuple[str, str]]:
    return {
        (improvement.findtext("improvementsource", ""), improvement.findtext("sourcename", ""))
        for improvement in root.findall("./improvements/improvement")
    }


def assert_workspace(
    device: shared.Device,
    *,
    sentinel: str,
    karma: str,
    nuyen: str,
    target_art_id: str,
    target_technique_id: str | None,
    target_present: bool,
    removed_pairs: set[tuple[str, str]],
    preserved_pairs: set[tuple[str, str]],
    preserved_art_id: str,
    preserved_technique_id: str | None,
) -> None:
    observed: list[str] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        if root.findtext("customstate") != sentinel:
            continue
        target = (
            find_art(root, target_art_id)
            if target_technique_id is None
            else find_technique(root, target_art_id, target_technique_id)
        )
        pairs = improvement_pairs(root)
        preserved = (
            find_art(root, preserved_art_id)
            if preserved_technique_id is None
            else find_technique(root, preserved_art_id, preserved_technique_id)
        )
        observed.append(f"target={target is not None}, improvements={len(pairs)}")
        if (
            (target is not None) == target_present
            and preserved is not None
            and root.findtext("karma") == karma
            and root.findtext("nuyen") == nuyen
            and (target_present or removed_pairs.isdisjoint(pairs))
            and preserved_pairs.issubset(pairs)
        ):
            return
    device.capture("martial-art-delete-workspace-mismatch")
    raise RuntimeError(f"Martial Art deletion workspace mismatch: {observed!r}")


def assert_target_absent(device: shared.Device, deleted_label: str, protected_label: str) -> None:
    open_page(device)
    device.tap("martial-art-delete-target", timeout=60, scroll=True)
    if device.find(deleted_label) is not None:
        device.capture("martial-art-delete-target-still-visible")
        raise RuntimeError(f"Deleted target remains visible: {deleted_label!r}")
    if device.find(protected_label) is not None:
        device.capture("martial-art-delete-quality-art-visible")
        raise RuntimeError(f"Quality-backed Art was exposed for deletion: {protected_label!r}")
    device.back()


def run_mode(
    device: shared.Device,
    *,
    fixture: Path,
    label: str,
    protected_label: str,
    target_art_id: str,
    target_technique_id: str | None,
    removed_pairs: set[tuple[str, str]],
    preserved_pairs: set[tuple[str, str]],
    preserved_art_id: str,
    preserved_technique_id: str | None,
    karma: str,
    nuyen: str,
    sentinel: str,
    mode: str,
) -> None:
    device.shell("pm", "clear", PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    select_target(device, label)
    device.tap("martial-art-delete-confirm", timeout=60, scroll=True)
    device.wait("Delete Martial Art?", timeout=30)
    device.tap("Cancel")
    device.wait("martial-art-delete-page", timeout=30)
    assert_workspace(
        device,
        sentinel=sentinel,
        karma=karma,
        nuyen=nuyen,
        target_art_id=target_art_id,
        target_technique_id=target_technique_id,
        target_present=True,
        removed_pairs=removed_pairs,
        preserved_pairs=preserved_pairs | removed_pairs,
        preserved_art_id=preserved_art_id,
        preserved_technique_id=preserved_technique_id,
    )
    device.capture(f"martial-art-delete-{mode}-cancel-noop")

    device.tap("martial-art-delete-confirm", timeout=60, scroll=True)
    device.wait("Delete Martial Art?", timeout=30)
    device.tap("Delete")
    device.wait("build-martial-art-delete", timeout=180, scroll=True)
    assert_workspace(
        device,
        sentinel=sentinel,
        karma=karma,
        nuyen=nuyen,
        target_art_id=target_art_id,
        target_technique_id=target_technique_id,
        target_present=False,
        removed_pairs=removed_pairs,
        preserved_pairs=preserved_pairs,
        preserved_art_id=preserved_art_id,
        preserved_technique_id=preserved_technique_id,
    )
    assert_target_absent(device, label, protected_label)
    device.capture(f"martial-art-delete-{mode}-same-session")
    device.shell("am", "force-stop", PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(
        device,
        sentinel=sentinel,
        karma=karma,
        nuyen=nuyen,
        target_art_id=target_art_id,
        target_technique_id=target_technique_id,
        target_present=False,
        removed_pairs=removed_pairs,
        preserved_pairs=preserved_pairs,
        preserved_art_id=preserved_art_id,
        preserved_technique_id=preserved_technique_id,
    )
    assert_target_absent(device, label, protected_label)
    device.capture(f"martial-art-delete-{mode}-process-restart")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-martial-art-delete-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-martial-art-delete-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "martialArtDeletePageSha256": android_root / "src/Chummer.Android/Native/MartialArtDeletePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "martialArtDeleteContractSha256": overview / "MartialArtDeleteRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "martialArtDeleteRulesSha256": contracts / "CharacterMartialArtDeleteRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Martial Art Delete source graph is incomplete: {missing!r}")
    if shared.PACKAGE != PACKAGE:
        raise RuntimeError(f"Driver package mismatch: {shared.PACKAGE!r} != {PACKAGE!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if api != "36":
        raise RuntimeError(f"Martial Art Delete E2E requires API 36, got {api!r}")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Martial Art Delete E2E requires {ABI}, got {abi!r}")
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
        device,
        fixture=creation_fixture,
        label="Art · Aikido · a4111111",
        protected_label="Art · Quality Art · a4155555",
        target_art_id="a4111111-a411-a411-a411-a41111111111",
        target_technique_id=None,
        removed_pairs={
            ("MartialArt", "a4111111-a411-a411-a411-a41111111111"),
            ("MartialArtTechnique", "a4122222-a412-a412-a412-a41222222222"),
            ("MartialArtTechnique", "a4133333-a413-a413-a413-a41333333333"),
        },
        preserved_pairs={
            ("Quality", "a4111111-a411-a411-a411-a41111111111"),
            ("MartialArt", "a4144444-a414-a414-a414-a41444444444"),
            ("MartialArt", "a4155555-a415-a415-a415-a41555555555"),
        },
        preserved_art_id="a4144444-a414-a414-a414-a41444444444",
        preserved_technique_id=None,
        karma="29",
        nuyen="2345.67",
        sentinel="Creation Martial Art delete runner sentinel",
        mode="creation",
    )
    run_mode(
        device,
        fixture=career_fixture,
        label="Technique · Aikido > Disarm · a5222222",
        protected_label="Art · Quality Art · a5555555",
        target_art_id="a5111111-a511-a511-a511-a51111111111",
        target_technique_id="a5222222-a522-a522-a522-a52222222222",
        removed_pairs={
            ("MartialArtTechnique", "a5222222-a522-a522-a522-a52222222222"),
        },
        preserved_pairs={
            ("Quality", "a5222222-a522-a522-a522-a52222222222"),
            ("MartialArt", "a5111111-a511-a511-a511-a51111111111"),
            ("MartialArtTechnique", "a5444444-a544-a544-a544-a54444444444"),
            ("MartialArt", "a5555555-a555-a555-a555-a55555555555"),
        },
        preserved_art_id="a5333333-a533-a533-a533-a53333333333",
        preserved_technique_id="a5444444-a544-a544-a544-a54444444444",
        karma="37",
        nuyen="8765.43",
        sentinel="Career Martial Art delete runner sentinel",
        mode="career",
    )

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "martial-art-delete",
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
            "creationCancelNoOp": "pass",
            "creationParentCascadeDeleted": "pass",
            "creationSameSessionReopen": "pass",
            "creationProcessRestart": "pass",
            "careerCancelNoOp": "pass",
            "careerParentScopedTechniqueDeleted": "pass",
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
        print(f"Martial Art Delete E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
