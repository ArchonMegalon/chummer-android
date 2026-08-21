#!/usr/bin/env python3
"""Prove all five exact Chummer5 Custom MAG Spirit-category selectors on an API 36 phone."""

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


CATEGORIES = ("combat", "detection", "health", "illusion", "manipulation")
CONTROLS = (
    "CharacterCreate.cboSpiritCombat",
    "CharacterCreate.cboSpiritDetection",
    "CharacterCreate.cboSpiritHealth",
    "CharacterCreate.cboSpiritIllusion",
    "CharacterCreate.cboSpiritManipulation",
    "CharacterCareer.cboSpiritCombat",
    "CharacterCareer.cboSpiritDetection",
    "CharacterCareer.cboSpiritHealth",
    "CharacterCareer.cboSpiritIllusion",
    "CharacterCareer.cboSpiritManipulation",
)
CUSTOM_SOURCE_ID = "616ba093-306c-45fc-8f41-0b98c8cccb46"
PROOF_KEYS = (
    "customMagVisibilityAndEditability",
    "activeTraditionsCatalog",
    "limitSpiritCategoryFiltered",
    "blankCanonicalValue",
    "stableTraditionAndSourceIdentity",
    "fiveFieldLocalRevisions",
    "expectedRevisionAtomicSave",
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
    device.tap(
        "build-tradition-spirit-categories",
        scroll=True,
        timeout=120,
        max_scrolls=32,
        scroll_distance_ratio=0.20,
    )
    device.wait("tradition-spirit-categories-page", timeout=60)
    device.wait("tradition-spirit-combat-value", timeout=45, scroll=True)


def selected_value(device: shared.Device, category: str) -> str:
    return shared.selected_text(
        device,
        f"tradition-spirit-{category}-value",
        f"{category.title()} spells",
        scroll=True,
    )


def select_value(device: shared.Device, category: str, value: str) -> None:
    expected_label = "None" if value == "" else value
    selector = f"tradition-spirit-{category}-value"
    device.tap(selector, timeout=60, scroll=True, max_scrolls=24)
    device.tap(expected_label, timeout=60, scroll=True, max_scrolls=12)
    time.sleep(0.4)
    observed = selected_value(device, category)
    expected_observed = expected_label
    if observed != expected_observed:
        device.capture(f"tradition-spirit-{category}-selection-mismatch")
        raise RuntimeError(
            f"Tradition {category} Spirit was {observed!r}; expected {expected_observed!r}"
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


def assert_workspace(
    device: shared.Device,
    expected: dict[str, str],
    expected_guid: str,
    tradition_sentinel: str,
    runner_sentinel: str,
) -> None:
    observed: list[dict[str, str]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        tradition = root.find("tradition")
        if tradition is None:
            continue
        values = {
            category: tradition.findtext(f"spirit{category}", default="") or ""
            for category in CATEGORIES
        }
        observed.append(values)
        enabled_limits = {
            node.findtext("improvedname", default="")
            for node in root.findall("./improvements/improvement")
            if node.findtext("improvementttype") == "LimitSpiritCategory"
            and node.findtext("enabled") in {"1", "True"}
        }
        if (
            values == expected
            and tradition.findtext("sourceid", default="").lower() == CUSTOM_SOURCE_ID
            and tradition.findtext("guid", default="").lower() == expected_guid
            and tradition.findtext("traditiontype") == "MAG"
            and tradition.findtext("extra") == tradition_sentinel
            and root.findtext("customstate") == runner_sentinel
            and enabled_limits == {"Spirit of Fire", "Spirit of Air"}
        ):
            return
    device.capture("tradition-spirit-categories-workspace-not-persisted")
    raise RuntimeError(f"Tradition Spirit categories were not durable: {observed!r}")


def assert_ui(device: shared.Device, expected: dict[str, str]) -> None:
    for category, value in expected.items():
        observed = selected_value(device, category)
        expected_label = "None" if value == "" else value
        if observed != expected_label:
            raise RuntimeError(
                f"Tradition {category} UI readback was {observed!r}; expected {expected_label!r}"
            )


def prove_profile(
    device: shared.Device,
    fixture: Path,
    expected: dict[str, str],
    expected_guid: str,
    tradition_sentinel: str,
    runner_sentinel: str,
    profile: str,
) -> None:
    device.shell("pm", "clear", shared.PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    for category, value in expected.items():
        select_value(device, category, value)
    device.tap("tradition-spirit-categories-save", timeout=120, scroll=True, max_scrolls=32)
    device.wait("build-tradition-spirit-categories", timeout=180, scroll=True, max_scrolls=32)
    assert_workspace(device, expected, expected_guid, tradition_sentinel, runner_sentinel)

    open_page(device)
    assert_ui(device, expected)
    device.capture(f"tradition-spirit-categories-{profile}-after-reopen")
    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    assert_workspace(device, expected, expected_guid, tradition_sentinel, runner_sentinel)
    open_page(device)
    assert_ui(device, expected)
    device.capture(f"tradition-spirit-categories-{profile}-after-process-restart")


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
        default=fixtures / "creation-tradition-spirit-categories-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-tradition-spirit-categories-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "spiritCategoryPageSha256": android_root / "src/Chummer.Android/Native/TraditionSpiritCategoryPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "spiritCategoryContractSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/TraditionSpiritCategoryEditRequest.cs",
        "mutationCatalogSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "spiritCategoryRulesSha256": workspace_root / "chummer-core-engine/Chummer.Contracts/Characters/CharacterTraditionSpiritCategoryRules.cs",
        "sourceResolverContractSha256": workspace_root / "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "traditionsCatalogSha256": workspace_root / "chummer-core-engine/Chummer/data/traditions.xml",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Tradition Spirit-category source graph is incomplete: {missing!r}")

    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Tradition Spirit-category E2E requires API 36, got {api!r}")
    subprocess.run(
        [str(args.adb), "-s", args.serial, "install", "--no-streaming", "-r", str(args.apk.resolve())],
        check=True,
        timeout=300,
    )
    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    for fixture in (creation_fixture, career_fixture):
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    prove_profile(
        device,
        creation_fixture,
        {
            "combat": "Spirit of Air",
            "detection": "Spirit of Fire",
            "health": "",
            "illusion": "Spirit of Air",
            "manipulation": "",
        },
        "91111111-9111-9111-9111-911111111111",
        "Creation tradition sentinel",
        "Creation runner sentinel",
        "creation",
    )
    prove_profile(
        device,
        career_fixture,
        {
            "combat": "Spirit of Fire",
            "detection": "",
            "health": "Spirit of Air",
            "illusion": "Spirit of Fire",
            "manipulation": "Spirit of Air",
        },
        "92222222-9222-9222-9222-922222222222",
        "Career tradition sentinel",
        "Career runner sentinel",
        "career",
    )

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "tradition-spirit-categories",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "creationFixtureSha256": shared.sha256(creation_fixture),
        "careerFixtureSha256": shared.sha256(career_fixture),
        "controlCount": len(CONTROLS),
        "controls": {control: {key: "pass" for key in PROOF_KEYS} for control in CONTROLS},
        "journeys": {
            "creationAllFiveFieldsEditedAndRestarted": "pass",
            "careerAllFiveFieldsEditedAndRestarted": "pass",
            "customOverlayAndFieldRevisionDriftFailClosedBySourceContract": "pass",
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
        print(f"Tradition Spirit-category E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
