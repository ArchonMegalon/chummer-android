#!/usr/bin/env python3
"""Prove phone contact/pet linked-runner attach and remove on API 36."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_editing_e2e as shared


CONTROLS = (
    "ContactControl.tsAttachCharacter",
    "ContactControl.tsRemoveCharacter",
    "PetControl.tsAttachCharacter",
    "PetControl.tsRemoveCharacter",
)
CONTROL_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)


def assert_unlinked_after_restart(
    device: shared.Device,
    kind: str,
    original_name: str,
) -> None:
    opener = shared.open_contact_section if kind == "contact" else shared.open_pet_section
    opener(device, "phone", expected_item=original_name)
    device.wait(original_name, timeout=60, scroll=True)
    shared.tap_collection_item(device, original_name)
    shared.reset_collection_editor_to_top(device, "phone")
    name_node = device.wait("collection-field-name", scroll=True)
    actual = name_node.attributes.get("text", "")
    enabled = name_node.attributes.get("enabled")
    if actual != original_name or enabled != "true":
        device.capture(f"phone-{kind}-unlink-restart-failed")
        raise RuntimeError(
            f"Unlinked {kind} identity did not persist after restart: "
            f"expected editable {original_name!r}, got {actual!r}, enabled={enabled!r}"
        )
    device.wait("collection-linked-status-", timeout=60, scroll=True)
    device.back()


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
        default=Path(__file__).resolve().parent / "fixtures" / "creation-contact-pet-e2e.chum5",
    )
    parser.add_argument(
        "--linked-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "linked-runner-e2e.chum5",
    )
    parser.add_argument(
        "--invalid-linked-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures" / "invalid-linked-runner-e2e.chum5",
    )
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    android_root = driver_path.parents[1]
    workspace_root = args.workspace_root.resolve()
    presentation_root = workspace_root / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "collectionEditorPagesSha256": android_root / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "runnerSessionCoordinatorSha256": android_root / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "linkedCharacterFileServiceSha256": android_root / "src" / "Chummer.Android" / "Platform" / "IAndroidLinkedCharacterFileService.cs",
        "linkedDocumentCodecSha256": workspace_root / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "Chummer5LinkedDocumentCodec.cs",
        "workspaceCollectionEditorProjectorSha256": presentation_root / "WorkspaceCollectionEditorProjector.cs",
        "workspaceCollectionEditorStateSha256": presentation_root / "WorkspaceCollectionEditorState.cs",
        "workspaceCollectionMutationRequestSha256": presentation_root / "WorkspaceCollectionMutationRequest.cs",
        "workspaceXmlMutationCatalogSha256": presentation_root / "WorkspaceXmlMutationCatalog.cs",
        "workspaceMutationsSha256": presentation_root / "CharacterOverviewPresenter.WorkspaceMutations.cs",
    }
    fixture_paths = {
        "inputFixtureSha256": args.runner.resolve(),
        "linkedFixtureSha256": args.linked_runner.resolve(),
        "invalidLinkedFixtureSha256": args.invalid_linked_runner.resolve(),
    }
    if not all(path.is_file() for path in (*source_paths.values(), *fixture_paths.values())):
        missing = [
            str(path)
            for path in (*source_paths.values(), *fixture_paths.values())
            if not path.is_file()
        ]
        raise RuntimeError(f"Linked-runner E2E source graph is incomplete: {missing!r}")

    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Linked-runner E2E requires API 36, got {api!r}")
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
    device.shell("pm", "clear", shared.PACKAGE)
    for fixture in fixture_paths.values():
        device.push(fixture, f"/sdcard/Download/{fixture.name}")

    shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, args.runner.name)
    device.wait("Continue building", timeout=120)
    shared.open_build(device, "phone")

    shared.open_contact_section(device, "phone", expected_item="ContactE2E")
    shared.attach_linked_runner(
        device,
        "phone",
        "contact",
        "ContactE2E",
        validate_invalid=True,
    )
    device.back()
    shared.open_pet_section(device, "phone", expected_item="PetE2E")
    shared.attach_linked_runner(device, "phone", "pet", "PetE2E")
    device.back()
    device.capture("phone-linked-runner-attached")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    shared.open_build(device, "phone")
    shared.assert_link_persisted_then_remove(device, "phone", "contact", "ContactE2E")
    device.back()
    shared.assert_link_persisted_then_remove(device, "phone", "pet", "PetE2E")
    device.back()
    device.capture("phone-linked-runner-removed")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Continue building", timeout=120)
    shared.open_build(device, "phone")
    assert_unlinked_after_restart(device, "contact", "ContactE2E")
    device.back()
    assert_unlinked_after_restart(device, "pet", "PetE2E")
    device.capture("phone-linked-runner-after-remove-restart")

    control_proofs = {
        control: {key: "pass" for key in CONTROL_PROOF_KEYS}
        for control in CONTROLS
    }
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "linked-runner",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver_path),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "inputFixture": str(args.runner.resolve()),
        "linkedFixture": str(args.linked_runner.resolve()),
        "invalidLinkedFixture": str(args.invalid_linked_runner.resolve()),
        **{key: shared.sha256(path) for key, path in fixture_paths.items()},
        "controlCount": len(control_proofs),
        "controls": control_proofs,
        "journeys": {
            "creationRunnerImported": "pass",
            "invalidLinkedRunnerRejected": "pass",
            "contactLinkedRunnerAttachPersisted": "pass",
            "petLinkedRunnerAttachPersisted": "pass",
            "processRestartAttachPersistence": "pass",
            "contactLinkedRunnerRemovePersisted": "pass",
            "petLinkedRunnerRemovePersisted": "pass",
            "processRestartRemovePersistence": "pass",
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
        print(f"linked-runner E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
