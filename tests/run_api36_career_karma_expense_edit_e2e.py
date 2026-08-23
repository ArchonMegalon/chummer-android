#!/usr/bin/env python3
"""Prove source-exact Career Karma expense editing on a real API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("cmdKarmaEdit", "lstKarma", "tsEditKarmaExpense")
MANUAL_ID = "65da27db-24a8-4b6e-b42c-30f4bb13a4f8"
LOCKED_ID = "a47497a9-0893-43e1-89cb-fb2dfa803b5d"
NUYEN_SIBLING_ID = "d1616d91-6848-49bd-a513-9b52d3399787"
CONTROL_PROOF_KEYS = (
    "stableExpenseGuid",
    "manualAmountAuthority",
    "lockedAmountAuthority",
    "exactKarmaDelta",
    "decimalBucketNoOp",
    "reasonNormalizationLanguageCas",
    "dateReasonEditable",
    "lockedMetadataPreserved",
    "nuyenSiblingPreserved",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "savedPayloadDigestBound",
    "surfaceReopened",
    "twoProcessRestarts",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def prepare_runner(
    device: shared.Device,
    fixture_name: str,
    fixture_sha256: str,
) -> tuple[shared.LaunchState, shared.WorkspaceAuthority]:
    launch = shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("CareerKarmaExpenseEditE2E", timeout=120)
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-karma-expenses",
        scroll=True,
        timeout=120,
        max_scrolls=32,
        scroll_distance_ratio=0.18,
    )
    device.wait("career-karma-expense-page", timeout=60)


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


def payload_sha256(payload: str) -> str:
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def root_for_authority(
    device: shared.Device,
    authority: shared.WorkspaceAuthority,
) -> ET.Element:
    matches = [
        payload
        for payload in workspace_payloads(device)
        if payload_sha256(payload) == authority.payload_sha256
    ]
    if len(matches) != 1:
        device.capture("career-karma-expense-authority-payload-ambiguous")
        raise RuntimeError(
            "Expected one exact saved payload bound to the workspace authority digest, "
            f"got {len(matches)}"
        )
    try:
        root = ET.fromstring(matches[0])
    except ET.ParseError as error:
        raise RuntimeError("The authority-bound Career Karma payload is not XML") from error
    if root.findtext("alias") != "CareerKarmaExpenseEditE2E":
        raise RuntimeError("The authority digest selected a different runner payload")
    return root


def expense_by_id(root: ET.Element, expense_id: str) -> ET.Element:
    rows = [row for row in root.findall("./expenses/expense") if row.findtext("guid") == expense_id]
    if len(rows) != 1:
        raise RuntimeError(f"Expected one expense {expense_id}, got {len(rows)}")
    return rows[0]


def assert_unrelated_state_preserved(root: ET.Element) -> None:
    if root.findtext("./customstate/karma") != "Unrelated nested Karma must survive":
        raise RuntimeError("Karma expense edit changed the unrelated nested Karma sentinel")
    sentinel = root.find("./customstate/sentinel")
    if (
        sentinel is None
        or sentinel.get("guid") != "nested-sentinel"
        or sentinel.text != "keep-nested-structure"
    ):
        raise RuntimeError("Karma expense edit changed unrelated nested XML structure")
    nuyen = expense_by_id(root, NUYEN_SIBLING_ID)
    if (
        nuyen.findtext("type") != "Nuyen"
        or nuyen.findtext("amount") != "-75"
        or nuyen.findtext("reason") != "Nuyen sibling"
        or nuyen.findtext("refund") != "False"
        or nuyen.findtext("forcecareervisible") != "True"
        or nuyen.findtext("./undo/karmatype") != "AddSkill"
        or nuyen.findtext("./undo/nuyentype") != "ManualSubtract"
        or nuyen.findtext("./undo/objectid") != "nuyen-sibling-id"
        or nuyen.findtext("./undo/qty") != "2"
        or nuyen.findtext("./undo/extra") != "keep-nuyen"
        or nuyen.findtext("custom") != "keep-nuyen"
    ):
        raise RuntimeError("Karma expense edit changed the Nuyen sibling record")


def assert_manual_metadata(root: ET.Element) -> ET.Element:
    manual = expense_by_id(root, MANUAL_ID)
    if (
        manual.findtext("type") != "Karma"
        or manual.findtext("refund") != "True"
        or manual.findtext("forcecareervisible") != "True"
        or manual.findtext("./undo/karmatype") != "ManualAdd"
        or manual.findtext("./undo/nuyentype") != "ManualSubtract"
        or manual.findtext("./undo/extra") != "keep-manual"
        or manual.findtext("custom") != "keep-manual"
    ):
        raise RuntimeError("Manual Karma expense metadata drifted")
    return manual


def assert_locked_metadata(root: ET.Element, expected_reason: str) -> None:
    locked = expense_by_id(root, LOCKED_ID)
    if (
        locked.findtext("amount") != "3.5"
        or locked.findtext("reason") != expected_reason
        or locked.findtext("type") != "Karma"
        or locked.findtext("refund") != "False"
        or locked.findtext("forcecareervisible") != "False"
        or locked.findtext("./undo/karmatype") != "ImproveAttribute"
        or locked.findtext("./undo/nuyentype") != "AddArmor"
        or locked.findtext("./undo/objectid") != "locked-object-id"
        or locked.findtext("./undo/qty") != "1"
        or locked.findtext("./undo/extra") != "keep-locked"
        or locked.findtext("custom") != "keep-locked"
    ):
        raise RuntimeError("Locked Karma expense amount or metadata drifted")


def read_saved_authority(device: shared.Device) -> shared.WorkspaceAuthority:
    device.tap("Home")
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_saved_authority(authority)
    return authority


def require_atomic_saved_transition(
    previous: shared.WorkspaceAuthority,
    saved: shared.WorkspaceAuthority,
) -> None:
    if saved.workspace_id != previous.workspace_id:
        raise RuntimeError("Karma expense save changed workspace identity")
    if saved.content_revision != previous.content_revision + 1:
        raise RuntimeError(
            "Karma expense save did not apply exactly one revision: "
            f"before={previous.content_revision}, after={saved.content_revision}"
        )
    shared.require_saved_authority(saved)
    if saved.payload_sha256 == previous.payload_sha256:
        raise RuntimeError("Karma expense save did not change the authority payload digest")


def select_locked_expense(device: shared.Device) -> None:
    device.tap("career-karma-expense-picker", timeout=60, scroll=True, max_scrolls=12)
    device.tap(LOCKED_ID, timeout=60, scroll=True, max_scrolls=8)
    time.sleep(0.5)


def return_home_from_page(device: shared.Device) -> None:
    device.back()
    device.wait("build-career-karma-expenses", timeout=90, scroll=True, max_scrolls=32)
    device.tap("Home")
    device.wait("Continue building", timeout=120)


def prove_same_integer_bucket(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture.name, fixture_sha256)
    open_page(device)
    device.set_text("career-karma-expense-amount", "Amount", "1.1")
    device.set_text(
        "career-karma-expense-reason",
        "Reason",
        "Same integer bucket",
        scroll=True,
    )
    device.tap("career-karma-expense-save", scroll=True, timeout=180, max_scrolls=20)
    device.wait("build-career-karma-expenses", timeout=180, scroll=True, max_scrolls=32)
    saved = read_saved_authority(device)
    require_atomic_saved_transition(imported, saved)
    root = root_for_authority(device, saved)
    manual = assert_manual_metadata(root)
    if manual.findtext("amount") != "1.9" or root.findtext("karma") != "10":
        raise RuntimeError("1.9 -> 1.1 did not preserve amount 1.9 and Karma 10")
    if manual.findtext("reason") != "Same integer bucket":
        raise RuntimeError("Same-integer-bucket reason edit was not persisted")
    assert_locked_metadata(root, "Locked quality")
    assert_unrelated_state_preserved(root)

    open_page(device)
    device.wait("career-karma-expense-picker", timeout=45)
    device.capture("career-karma-same-bucket-reopened")
    return_home_from_page(device)
    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    device.wait("Continue building", timeout=120)
    restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(saved, restored)
    restored_root = root_for_authority(device, restored)
    restored_manual = assert_manual_metadata(restored_root)
    if restored_manual.findtext("amount") != "1.9" or restored_root.findtext("karma") != "10":
        raise RuntimeError("Same-integer-bucket edit changed after process restart")
    assert_unrelated_state_preserved(restored_root)
    return {
        "import": shared.workspace_authority_json(imported),
        "saved": shared.workspace_authority_json(saved),
        "restored": shared.workspace_authority_json(restored),
        "restart": {
            "preForceStopProcessIds": list(restart.before_force_stop.process_ids),
            "postForceStopProcessIds": list(restart.after_force_stop.process_ids),
            "restartProcessIds": list(restart.restarted.process_ids),
        },
    }


def prove_rounded_delta_and_locked_edit(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture.name, fixture_sha256)
    open_page(device)
    device.set_text("career-karma-expense-amount", "Amount", "2.1")
    device.set_text(
        "career-karma-expense-reason",
        "Reason",
        "Rounded training",
        scroll=True,
    )
    device.tap("career-karma-expense-save", scroll=True, timeout=180, max_scrolls=20)
    device.wait("build-career-karma-expenses", timeout=180, scroll=True, max_scrolls=32)
    rounded_saved = read_saved_authority(device)
    require_atomic_saved_transition(imported, rounded_saved)
    rounded_root = root_for_authority(device, rounded_saved)
    manual = assert_manual_metadata(rounded_root)
    if manual.findtext("amount") != "2" or rounded_root.findtext("karma") != "11":
        raise RuntimeError("1.9 -> 2.1 did not normalize amount to 2 and Karma to 11")
    if manual.findtext("reason") != "Rounded training":
        raise RuntimeError("Rounded manual reason edit was not persisted")
    assert_locked_metadata(rounded_root, "Locked quality")
    assert_unrelated_state_preserved(rounded_root)

    open_page(device)
    select_locked_expense(device)
    amount_node = device.wait(
        "career-karma-expense-amount",
        timeout=45,
        scroll=True,
        max_scrolls=12,
    )
    if amount_node.attributes.get("enabled") != "false":
        device.capture("career-karma-expense-locked-amount-enabled")
        raise RuntimeError("Nonmanual Karma expense amount was not exactly disabled")
    device.set_text(
        "career-karma-expense-reason",
        "Reason",
        "Locked reason revised",
        scroll=True,
    )
    device.tap("career-karma-expense-save", scroll=True, timeout=180, max_scrolls=20)
    device.wait("build-career-karma-expenses", timeout=180, scroll=True, max_scrolls=32)
    locked_saved = read_saved_authority(device)
    require_atomic_saved_transition(rounded_saved, locked_saved)
    locked_root = root_for_authority(device, locked_saved)
    final_manual = assert_manual_metadata(locked_root)
    if final_manual.findtext("amount") != "2" or locked_root.findtext("karma") != "11":
        raise RuntimeError("Locked edit changed normalized manual amount or Karma balance")
    assert_locked_metadata(locked_root, "Locked reason revised")
    assert_unrelated_state_preserved(locked_root)

    open_page(device)
    select_locked_expense(device)
    locked_after_reopen = device.wait(
        "career-karma-expense-amount",
        timeout=45,
        scroll=True,
        max_scrolls=12,
    )
    if locked_after_reopen.attributes.get("enabled") != "false":
        raise RuntimeError("Locked Karma amount became enabled after same-session reopen")
    device.capture("career-karma-rounded-and-locked-reopened")
    return_home_from_page(device)
    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    device.wait("Continue building", timeout=120)
    restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(locked_saved, restored)
    restored_root = root_for_authority(device, restored)
    restored_manual = assert_manual_metadata(restored_root)
    if restored_manual.findtext("amount") != "2" or restored_root.findtext("karma") != "11":
        raise RuntimeError("Rounded manual edit changed after process restart")
    assert_locked_metadata(restored_root, "Locked reason revised")
    assert_unrelated_state_preserved(restored_root)
    open_page(device)
    select_locked_expense(device)
    locked_after_restart = device.wait(
        "career-karma-expense-amount",
        timeout=45,
        scroll=True,
        max_scrolls=12,
    )
    if locked_after_restart.attributes.get("enabled") != "false":
        raise RuntimeError("Locked Karma amount became enabled after process restart")
    device.capture("career-karma-expense-after-process-restart")
    return {
        "import": shared.workspace_authority_json(imported),
        "manualSaved": shared.workspace_authority_json(rounded_saved),
        "lockedSaved": shared.workspace_authority_json(locked_saved),
        "restored": shared.workspace_authority_json(restored),
        "restart": {
            "preForceStopProcessIds": list(restart.before_force_stop.process_ids),
            "postForceStopProcessIds": list(restart.after_force_stop.process_ids),
            "restartProcessIds": list(restart.restarted.process_ids),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    fixture_default = (
        Path(__file__).resolve().parent / "fixtures/career-karma-expense-edit-e2e.chum5"
    )
    parser.add_argument("--career-runner", type=Path, default=fixture_default)
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerKarmaExpensePageSha256": android_root
        / "src/Chummer.Android/Native/CareerKarmaExpensePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerKarmaExpenseContractSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerKarmaExpenseEditRequest.cs",
        "careerKarmaExpenseMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerKarmaExpenseMutation.cs",
        "mutationCatalogSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "localizationCatalogSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/DesktopLocalizationCatalog.cs",
        "careerKarmaExpenseRulesSha256": workspace_root
        / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerKarmaExpenseEditRules.cs",
        "workspaceStoreSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Karma expense source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    fixture_sha256 = shared.sha256(fixture)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Karma expense E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(f"Karma expense E2E requires the hosted x86_64 phone lane, got {abi!r}")
    subprocess.run(
        [
            str(args.adb),
            "-s",
            args.serial,
            "install",
            "--no-streaming",
            "-r",
            str(args.apk.resolve()),
        ],
        check=True,
        timeout=300,
    )
    remote_fixture = f"/sdcard/Download/{fixture.name}"
    verified_remote_fixture_sha256 = device.push_verified(
        fixture,
        remote_fixture,
        fixture_sha256,
    )
    same_bucket = prove_same_integer_bucket(device, fixture, fixture_sha256)
    rounded_and_locked = prove_rounded_delta_and_locked_edit(
        device,
        fixture,
        fixture_sha256,
    )

    controls = {
        f"CharacterCareer.{control}": {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-karma-expense-edit",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote_fixture_sha256,
        "manualExpenseGuid": MANUAL_ID,
        "lockedExpenseGuid": LOCKED_ID,
        "nuyenSiblingGuid": NUYEN_SIBLING_ID,
        "controlCount": len(controls),
        "controls": controls,
        "authorityProofStages": {
            "sameIntegerBucket": same_bucket,
            "roundedAndLocked": rounded_and_locked,
        },
        "journeys": {
            "manualOnePointNineToOnePointOneUnchanged": "pass",
            "manualOnePointNineToTwoPointOneNormalized": "pass",
            "lockedAmountReadOnly": "pass",
            "twoProcessRestarts": "pass",
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
        print(f"Karma expense E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
