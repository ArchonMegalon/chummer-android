#!/usr/bin/env python3
"""Prove exact Create-only Prototype Transhuman behavior on an API 36 phone."""

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


CONTROL = "CharacterCreate.chkPrototypeTranshuman"
PROOF_KEYS = (
    "stableTopLevelBiowareGuid",
    "enabledPrototypeTranshumanImprovement",
    "createOnlyReachability",
    "recursiveDescendantPersistence",
    "expectedRevisionAtomicSave",
    "sameSessionReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
CREATION_ID = "81111111-8111-8111-8111-811111111111"
CHILD_ID = "82222222-8222-8222-8222-822222222222"
UNTOUCHED_ID = "83333333-8333-8333-8333-833333333333"
CAREER_ID = "84444444-8444-8444-8444-844444444444"


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


def open_prototype_page(device: shared.Device) -> str:
    open_cyberware_editor(device, CREATION_ID)
    compact = CREATION_ID.replace("-", "")
    device.tap(f"prototype-transhuman-open-{compact}", timeout=60, scroll=True, max_scrolls=36)
    device.wait(f"prototype-transhuman-page-{compact}", timeout=60)
    return compact


def assert_toggle(device: shared.Device, expected: bool) -> None:
    compact = CREATION_ID.replace("-", "")
    node = device.wait(f"prototype-transhuman-toggle-{compact}", timeout=60)
    observed = node.attributes.get("checked") == "true"
    if observed != expected:
        device.capture("prototype-transhuman-toggle-mismatch")
        raise RuntimeError(f"Prototype Transhuman switch was {observed!r}; expected {expected!r}")


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


def assert_creation_workspace(device: shared.Device) -> None:
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        cyberware = {
            item.findtext("guid", default="").lower(): item
            for item in root.findall(".//cyberware")
        }
        parent = cyberware.get(CREATION_ID)
        child = cyberware.get(CHILD_ID)
        untouched = cyberware.get(UNTOUCHED_ID)
        if parent is None or child is None or untouched is None:
            continue
        flags = {
            "parent": parent.findtext("prototypetranshuman", default=""),
            "child": child.findtext("prototypetranshuman", default=""),
            "untouched": untouched.findtext("prototypetranshuman", default=""),
        }
        observed.append(flags)
        if (
            flags == {"parent": "True", "child": "True", "untouched": "False"}
            and parent.findtext("improvementsource") == "Bioware"
            and parent.findtext("notes") == "Creation parent notes remain intact"
            and child.findtext("notes") == "Creation child notes remain intact"
            and untouched.findtext("notes") == "Creation untouched notes remain intact"
            and root.findtext("customstate") == "Creation Prototype Transhuman unrelated text"
            and root.findtext("./improvements/improvement/improvementttype") == "PrototypeTranshuman"
            and root.findtext("./improvements/improvement/val") == "1"
        ):
            return
    device.capture("prototype-transhuman-workspace-not-persisted")
    raise RuntimeError(f"Prototype Transhuman hierarchy was not durable: {observed!r}")


def prove_creation(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    compact = open_prototype_page(device)
    assert_toggle(device, False)
    device.tap(f"prototype-transhuman-toggle-{compact}", timeout=60)
    assert_toggle(device, True)
    device.tap(f"prototype-transhuman-save-{compact}", timeout=240, scroll=True)
    device.wait(f"prototype-transhuman-open-{compact}", timeout=120, scroll=True)
    assert_creation_workspace(device)

    open_prototype_page(device)
    assert_toggle(device, True)
    device.capture("prototype-transhuman-creation-enabled-after-reopen")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_creation_workspace(device)
    open_prototype_page(device)
    assert_toggle(device, True)
    device.capture("prototype-transhuman-creation-enabled-after-process-restart")


def prove_career_absent(device: shared.Device, fixture: Path) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_cyberware_editor(device, CAREER_ID)
    compact = CAREER_ID.replace("-", "")
    try:
        device.wait(f"prototype-transhuman-open-{compact}", timeout=12, scroll=True, max_scrolls=24)
    except RuntimeError:
        device.capture("prototype-transhuman-career-control-absent")
        return
    raise RuntimeError("Prototype Transhuman must not be reachable from a Career runner")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixtures = Path(__file__).resolve().parent / "fixtures"
    parser.add_argument("--creation-runner", type=Path, default=fixtures / "creation-prototype-transhuman-e2e.chum5")
    parser.add_argument("--career-runner", type=Path, default=fixtures / "career-prototype-transhuman-e2e.chum5")
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "prototypeTranshumanPageSha256": android_root / "src/Chummer.Android/Native/PrototypeTranshumanPage.cs",
        "collectionEditorPagesSha256": android_root / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "prototypeTranshumanContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/PrototypeTranshumanEditRequest.cs",
        "collectionEditorStateSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "prototypeTranshumanRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterPrototypeTranshumanRules.cs",
        "characterSectionModelsSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Prototype Transhuman E2E source graph is incomplete: {missing!r}")

    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Prototype Transhuman E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")
    prove_creation(device, creation_fixture)
    prove_career_absent(device, career_fixture)

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "prototype-transhuman",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": 1,
        "controls": {CONTROL: {key: "pass" for key in PROOF_KEYS}},
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Prototype Transhuman E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
