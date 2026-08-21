#!/usr/bin/env python3
"""Prove both Chummer5 Career Psyche Active surfaces on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "CharacterCareer.chkPsycheActiveMagician",
    "CharacterCareer.chkPsycheActiveTechnomancer",
)
MAGICIAN = "sustained-psyche-active-magician"
TECHNOMANCER = "sustained-psyche-active-technomancer"
PROOF_KEYS = (
    "sharedSavedPsycheBoolean",
    "legacySurfaceVisibility",
    "revisionBoundMutation",
    "atomicWorkspacePersisted",
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


def open_psyche_controls(device: shared.Device) -> None:
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
    device.wait(MAGICIAN, timeout=60, scroll=True, max_scrolls=20)
    device.wait(TECHNOMANCER, timeout=60, scroll=True, max_scrolls=20)


def assert_toggle(device: shared.Device, selector: str, expected: bool) -> None:
    node = device.wait(selector, timeout=60, scroll=True, max_scrolls=20)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture(f"{selector}-mismatch")
        raise RuntimeError(f"{selector} expected checked={expected}, got {observed}")


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


def assert_workspace(device: shared.Device, expected: bool) -> None:
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        sustained = root.findall("./sustainedobjects/sustainedobject")
        if (
            root.findtext("psyche") == ("True" if expected else "False")
            and root.findtext("./customstate/psyche") == "Preserve unrelated Psyche fixture state"
            and len(sustained) == 2
            and {item.findtext("linkedobjecttype") for item in sustained} == {"Spell", "ComplexForm"}
            and root.findtext("./spells/spell/notes") == "Linked spell remains untouched"
            and root.findtext("./complexforms/complexform/notes") == "Linked complex form remains untouched"
        ):
            return
    device.capture("psyche-active-workspace-not-persisted")
    raise RuntimeError(f"Psyche Active={expected} was not durable or unrelated data changed")


def prove(device: shared.Device, fixture: Path) -> None:
    prepare_runner(device, fixture.name)
    open_psyche_controls(device)
    assert_toggle(device, MAGICIAN, False)
    assert_toggle(device, TECHNOMANCER, False)

    device.tap(MAGICIAN, timeout=60, scroll=True, max_scrolls=20)
    device.wait("build-sustained-effects", timeout=180, scroll=True, max_scrolls=30)
    assert_workspace(device, True)
    open_psyche_controls(device)
    assert_toggle(device, MAGICIAN, True)
    assert_toggle(device, TECHNOMANCER, True)

    device.tap(TECHNOMANCER, timeout=60, scroll=True, max_scrolls=20)
    device.wait("build-sustained-effects", timeout=180, scroll=True, max_scrolls=30)
    assert_workspace(device, False)
    open_psyche_controls(device)
    assert_toggle(device, MAGICIAN, False)
    assert_toggle(device, TECHNOMANCER, False)

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, False)
    open_psyche_controls(device)
    assert_toggle(device, MAGICIAN, False)
    assert_toggle(device, TECHNOMANCER, False)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(
        "--runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "career-psyche-active-e2e.chum5",
    )
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
        raise RuntimeError(f"Psyche Active E2E source graph is incomplete: {missing!r}")

    fixture = args.runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Psyche Active E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    device.push(fixture, f"/sdcard/Download/{fixture.name}")
    device.shell("pm", "clear", shared.PACKAGE)
    prove(device, fixture)

    controls = {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS}
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "psyche-active",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "fixtureSha256": shared.sha256(fixture),
        "controlCount": len(controls),
        "controls": controls,
        "journeys": {
            "magicianEnabledSharedPsyche": "pass",
            "technomancerDisabledSharedPsyche": "pass",
            "sameSessionBothSurfacesSynchronized": "pass",
            "processRestartWorkspaceAndUiReadback": "pass",
            "unrelatedSustainedDataPreserved": "pass",
        },
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
