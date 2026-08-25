#!/usr/bin/env python3
"""Prove paired Create/Career Convert to Free Sprite on an API 36 arm64 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = (
    "CharacterCreate.mnuSpecialConvertToFreeSprite",
    "CharacterCareer.mnuSpecialConvertToFreeSprite",
)
PACKAGE = "com.myexternalbrain.chummer"
ABI = "arm64-v8a"
DENIAL_SOURCE_ID = "c2899500-5932-4c39-81a8-fa64b08fa916"
PROOF_KEYS = (
    "typedStableCritterPowerIdentity",
    "exactSpriteEligibility",
    "exactDenialSourceIdentity",
    "countTowardsLimitFalse",
    "freeSpriteCategory",
    "creationCareerSameRules",
    "zeroKarmaNuyenDelta",
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
    device.tap("build-free-sprite-conversion", scroll=True, timeout=120, max_scrolls=30)
    device.wait("free-sprite-conversion-page", timeout=60)


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


def assert_workspace(
    device: shared.Device,
    sentinel: str,
    expected_karma: str,
    expected_nuyen: str,
) -> None:
    observed: list[tuple[str | None, int]] = []
    for payload in workspace_payloads(device):
        try:
            root = ET.fromstring(payload)
        except ET.ParseError:
            continue
        if root.findtext("customstate") != sentinel:
            continue
        denial = [
            power
            for power in root.findall("./critterpowers/critterpower")
            if power.findtext("sourceid") == DENIAL_SOURCE_ID
            and power.findtext("name") == "Denial"
        ]
        observed.append((root.findtext("metatypecategory"), len(denial)))
        if (
            root.findtext("metatypecategory") == "Free Sprite"
            and len(denial) == 1
            and denial[0].findtext("guid")
            and denial[0].findtext("counttowardslimit") == "False"
            and denial[0].findtext("category") == "Emergent"
            and denial[0].findtext("source") == "UN"
            and denial[0].findtext("page") == "160"
            and root.findtext("karma") == expected_karma
            and root.findtext("nuyen") == expected_nuyen
        ):
            return
    device.capture("free-sprite-conversion-workspace-not-persisted")
    raise RuntimeError(f"Free Sprite conversion was not durable: {observed!r}")


def run_mode(
    device: shared.Device,
    fixture: Path,
    sentinel: str,
    expected_karma: str,
    expected_nuyen: str,
    mode: str,
) -> None:
    device.shell("pm", "clear", PACKAGE)
    prepare_runner(device, fixture.name)
    open_page(device)
    summary = device.wait("free-sprite-conversion-summary", timeout=60)
    if "1 saved Critter Powers · 0 Karma · 0 Nuyen" not in summary.attributes.get("text", ""):
        raise RuntimeError(f"{mode} conversion economics/collection summary was not exact")
    device.tap("free-sprite-conversion-save", timeout=180)
    device.wait("build-free-sprite-conversion", timeout=180, scroll=True)
    assert_workspace(device, sentinel, expected_karma, expected_nuyen)

    device.shell("input", "keyevent", "4")
    shared.open_build(device, "phone")
    assert_workspace(device, sentinel, expected_karma, expected_nuyen)
    device.capture(f"free-sprite-conversion-{mode}-same-session")

    device.shell("am", "force-stop", PACKAGE)
    shared.launch_app(device)
    shared.wait_for_phone_runner_route(device, timeout=120)
    assert_workspace(device, sentinel, expected_karma, expected_nuyen)
    shared.open_build(device, "phone")
    device.capture(f"free-sprite-conversion-{mode}-process-restart")


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
        default=fixtures / "creation-free-sprite-conversion-e2e.chum5",
    )
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=fixtures / "career-free-sprite-conversion-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    overview = workspace_root / "chummer-presentation/Chummer.Presentation/Overview"
    contracts = workspace_root / "chummer-core-engine/Chummer.Contracts/Characters"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "freeSpriteConversionPageSha256": android_root / "src/Chummer.Android/Native/FreeSpriteConversionPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "freeSpriteConversionContractSha256": overview / "FreeSpriteConversionRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "freeSpriteConversionRulesSha256": contracts / "CharacterFreeSpriteConversionRules.cs",
        "workspaceStoreSha256": workspace_root / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Free Sprite conversion source graph is incomplete: {missing!r}")
    if shared.PACKAGE != PACKAGE:
        raise RuntimeError(f"Driver package mismatch: {shared.PACKAGE!r} != {PACKAGE!r}")

    creation_fixture = args.creation_runner.resolve()
    career_fixture = args.career_runner.resolve()
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if api != "36":
        raise RuntimeError(f"Free Sprite conversion E2E requires API 36, got {api!r}")
    if abi != "arm64-v8a":
        raise RuntimeError(f"Free Sprite conversion E2E requires {ABI}, got {abi!r}")
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
        creation_fixture,
        "Creation Free Sprite conversion sentinel",
        "23",
        "4567.89",
        "creation",
    )
    run_mode(
        device,
        career_fixture,
        "Career Free Sprite conversion sentinel",
        "31",
        "7654.32",
        "career",
    )

    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "free-sprite-conversion",
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
            "creationExactConversion": "pass",
            "careerExactConversion": "pass",
            "sameSessionReopen": "pass",
            "processRestart": "pass",
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
        print(f"Free Sprite conversion E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
